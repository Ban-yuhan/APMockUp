// 플레이어(관리자): 이동 + 상호작용 범위 안에서 대기하면 자동 작업 (원본 js/player.js의 Player)
using System;
using static APC.Game;

namespace APC
{
    public static class Player
    {
        public static void Init()
        {
            G.player = new PlayerState { x = LAYOUT.playerStart.x, z = LAYOUT.playerStart.z };
        }

        public static int Capacity() => CONFIG.player.capacity;

        // input: 화면 기준 WASD를 월드 방향으로 바꾼 값 (원본 UI.inputVector)
        public static void Update(double dt, P input)
        {
            var p = G.player;
            // 지게차 운전 중: 위치는 Forklifts가 맞추고, 시설 작업은 하지 않는다
            if (Forklifts.Riding) return;
            double vx = input.x, vz = input.z, step = CONFIG.player.speed * dt;
            if (vx != 0 || vz != 0)
            {
                p.target = null;
                double l = U.Hypot(vx, vz);
                vx /= l; vz /= l;
            }
            else if (p.target.HasValue)
            {
                var t = p.target.Value;
                double dx = t.x - p.x, dz = t.z - p.z, d = U.Hypot(dx, dz);
                if (d < 0.05) p.target = null;
                else { vx = dx / d; vz = dz / d; step = Math.Min(step, d); }
            }
            p.moving = vx != 0 || vz != 0;
            if (p.moving)
            {
                p.x += vx * step;
                p.z += vz * step;
                p.facing = Math.Atan2(vx, vz);
            }
            var bd = LAYOUT.bounds;
            p.x = U.Clamp(p.x, bd.minX, bd.maxX);
            p.z = U.Clamp(p.z, bd.minZ, bd.maxZ);

            var z = Zones.Nearest(p.Pos);
            var key = z?.key;
            if (key != p.zoneKey)
            {
                LeaveZone();
                p.zoneKey = key;
                p.timer = CONFIG.interactDelay;
                p.zoneMode = z != null ? EntryMode(z) : null;
                p.action = null;
            }
            p.zone = z;
            if (z == null) return;
            p.timer -= dt;
            if (p.timer > 0) return;
            p.action = null;
            Act(z, dt);
            if (p.action == null) p.action = IdleText(z);
        }

        // 범위에 들어가는 순간 할 일을 정하고, 나갈 때까지 유지한다
        // 하차장 넣기·꺼내기 지점이 합쳐져 있으면: 들고 오면 내려놓기만, 빈손이면 집기만 (집자마자 내려놓는 문제 방지)
        public static string EntryMode(Zone z)
        {
            var part = z.parts.Find(q => q.kind == "staging");
            if (part == null) return null;
            if (part.roles.Contains("in") && part.roles.Contains("out")) return S.HasKind(G.player, HandKind.Raw) ? "drop" : "pick";
            return part.roles.Contains("in") ? "drop" : "pick";
        }

        public static void LeaveZone()
        {
            var p = G.player;
            foreach (var b in G.bins) if (b.inspector == Owner.Player) b.inspector = Owner.None;
            foreach (var t in G.tables) if (t.packer == Owner.Player) { t.packer = Owner.None; t.packProgress = 0; }
            p.progress = 0;
            p.busy = null;
        }

        // 합쳐진 시설마다 순서대로 시도하고, 하나라도 일을 하면 멈춘다
        static void Act(Zone z, double dt)
        {
            foreach (var part in z.parts)
            {
                ActPart(part, dt);
                if (G.player.action != null) return;
            }
        }

        static void ActPart(ZonePart part, double dt)
        {
            var p = G.player;
            int cap = Capacity();
            double h = CONFIG.timing.handle;
            switch (part.kind)
            {
                case "truck":
                {
                    var tr = (InTruck)part.refObj;
                    if (tr.items.Count > 0 && S.CanTake(p, HandKind.Raw, cap))
                    {
                        p.hand.Add(new HandEntry { kind = HandKind.Raw, item = Pop(tr.items) });
                        p.timer = h;
                        p.action = "하차 중";
                    }
                    break;
                }
                case "staging":
                    if (p.zoneMode == "drop")
                    {
                        int i = p.hand.FindIndex(e => e.kind == HandKind.Raw);
                        if (i >= 0 && G.staging.Count < S.StagingCap())
                        {
                            G.staging.Add(p.hand[i].item);
                            p.hand.RemoveAt(i);
                            p.timer = h;
                            p.action = "내려놓는 중";
                        }
                    }
                    else if (S.CanTake(p, HandKind.Raw, cap))
                    {
                        int i = G.staging.FindIndex(it => S.BinSpace(G.bins[it]) > 0);
                        if (i >= 0)
                        {
                            p.hand.Add(new HandEntry { kind = HandKind.Raw, item = G.staging[i] });
                            G.staging.RemoveAt(i);
                            p.timer = h;
                            p.action = "집는 중";
                        }
                    }
                    break;
                case "bin": ActBin((Bin)part.refObj, dt, part.roles); break;
                case "table": ActTable((Table)part.refObj, dt, part.roles); break;
                case "dock":
                {
                    var d = (Dock)part.refObj;
                    int i = p.hand.FindIndex(e => e.kind == HandKind.Box && e.box.dest == d.idx);
                    if (i >= 0)
                    {
                        d.waiting.Add(p.hand[i].box);
                        p.hand.RemoveAt(i);
                        p.timer = h;
                        p.action = "박스 내려놓는 중";
                    }
                    break;
                }
                case "trash":
                {
                    int i = p.hand.FindIndex(e => e.kind == HandKind.Broken);
                    if (i >= 0)
                    {
                        p.hand.RemoveAt(i);
                        G.brokenCount++;
                        p.timer = h;
                        p.action = "파손품 버리는 중";
                    }
                    break;
                }
            }
        }

        static void ActBin(Bin b, double dt, System.Collections.Generic.HashSet<string> roles)
        {
            var p = G.player;
            int cap = Capacity(), it = b.item;
            double h = CONFIG.timing.handle;
            // 넣기: 들고 온 미검수 물품 넣기 (분류)
            if (roles.Contains("in"))
            {
                int i = p.hand.FindIndex(e => e.kind == HandKind.Raw && e.item == it);
                if (i >= 0 && S.BinSpace(b) > 0)
                {
                    p.hand.RemoveAt(i);
                    b.uncheckedCount++;
                    p.timer = h;
                    p.action = "분류 중";
                    return;
                }
            }
            // 꺼내기: 이미 충분히 이송된 물품은 반납, 주문에 필요한 검수완료 물품 집기 (이송)
            if (roles.Contains("out"))
            {
                int i = p.hand.FindIndex(e => e.kind == HandKind.Pick && e.item == it);
                if (i >= 0 && S.OutstandingNeed(it) < 0)
                {
                    p.hand.RemoveAt(i);
                    b.checkedCount++;
                    p.timer = h;
                    p.action = "남은 물품 반납";
                    return;
                }
                if (S.CanTake(p, HandKind.Pick, cap) && S.Avail(b) > 0 && S.OutstandingNeed(it) > 0)
                {
                    b.checkedCount--;
                    p.hand.Add(new HandEntry { kind = HandKind.Pick, item = it });
                    p.timer = h;
                    p.action = "주문 물품 집는 중";
                    return;
                }
            }
            // 검수: 파손품 집기, 검수
            if (roles.Contains("inspect"))
            {
                if (b.broken > 0 && S.CanTake(p, HandKind.Broken, cap))
                {
                    b.broken--;
                    p.hand.Add(new HandEntry { kind = HandKind.Broken, item = it });
                    p.timer = h;
                    p.action = "파손품 집는 중";
                    return;
                }
                if (b.uncheckedCount > 0 && (b.inspector == Owner.None || b.inspector == Owner.Player))
                {
                    b.inspector = Owner.Player;
                    p.busy = "inspect";
                    p.action = "검수 중";
                    p.progress += dt;
                    if (p.progress >= CONFIG.timing.inspect)
                    {
                        p.progress = 0;
                        b.uncheckedCount--;
                        if (U.Random() < CONFIG.breakChance.inspect)
                        {
                            if (S.CanTake(p, HandKind.Broken, cap)) p.hand.Add(new HandEntry { kind = HandKind.Broken, item = it });
                            else b.broken++;
                            Notify("💔", $"검수 중 {CONFIG.items[it].name} 불량 발견! 쓰레기통에 버려주세요");
                        }
                        else b.checkedCount++;
                    }
                    return;
                }
            }
            if (b.inspector == Owner.Player) b.inspector = Owner.None;
            p.busy = null;
            p.progress = 0;
        }

        static void ActTable(Table t, double dt, System.Collections.Generic.HashSet<string> roles)
        {
            var p = G.player;
            int cap = Capacity();
            double h = CONFIG.timing.handle;
            if (roles.Contains("in"))
            {
                // 이송품 전달
                int i = p.hand.FindIndex(e => e.kind == HandKind.Pick && S.RemainingNeed(t, e.item) > 0);
                if (i >= 0)
                {
                    var e = p.hand[i];
                    p.hand.RemoveAt(i);
                    t.delivered[e.item] = (t.delivered.TryGetValue(e.item, out int n) ? n : 0) + 1;
                    p.timer = h;
                    p.action = "물품 전달 중";
                    return;
                }
                // 포장
                if (!S.IsAuto("pack") && S.TableReady(t) && (t.packer == Owner.None || t.packer == Owner.Player))
                {
                    t.packer = Owner.Player;
                    p.busy = "pack";
                    p.action = "포장 중";
                    t.packProgress += dt;
                    p.progress = t.packProgress;
                    if (t.packProgress >= CONFIG.timing.pack)
                    {
                        Orders.Pack(t);
                        p.busy = null;
                        p.progress = 0;
                    }
                    return;
                }
            }
            p.busy = null;
            // 꺼내기: 포장된 박스 집기 (출고분류)
            if (roles.Contains("out"))
            {
                int bi = t.boxes.FindIndex(b => b.claimedBy == Owner.None);
                if (bi >= 0 && S.CanTake(p, HandKind.Box, cap))
                {
                    p.hand.Add(new HandEntry { kind = HandKind.Box, box = t.boxes[bi] });
                    t.boxes.RemoveAt(bi);
                    p.timer = h;
                    p.action = "박스 집는 중";
                }
            }
        }

        // 할 일이 없을 때 이유 표기 (첫 번째 시설 기준)
        public static string IdleText(Zone z)
        {
            var p = G.player;
            bool full = p.hand.Count >= Capacity();
            var part = z.parts[0];
            var roles = part.roles;
            switch (part.kind)
            {
                case "truck":
                    if (((InTruck)part.refObj).items.Count == 0) return "트럭이 비었습니다";
                    return full ? "손이 가득 찼습니다" : "다른 물건을 들고 있습니다";
                case "staging":
                    if (p.zoneMode == "drop")
                    {
                        if (S.HasKind(p, HandKind.Raw)) return "하차장이 가득 찼습니다";
                        return roles.Contains("out") ? "내려놓기 완료 (다시 들어오면 집기)" : "내려놓을 물건 없음";
                    }
                    if (full) return "손이 가득 찼습니다";
                    if (p.hand.Count > 0) return "다른 물건을 들고 있습니다";
                    return G.staging.Count > 0 ? "보관함 자리가 없습니다" : "하차장이 비었습니다";
                case "bin":
                {
                    bool raw = S.HasKind(p, HandKind.Raw);
                    if (raw && !roles.Contains("in")) return "여기는 넣기 지점이 아닙니다";
                    if (raw && p.hand.Exists(e => e.kind == HandKind.Raw && e.item == ((Bin)part.refObj).item)) return "보관함이 가득 찼습니다";
                    if (raw) return "이 보관함 품목이 아닙니다";
                    if (full) return "손이 가득 찼습니다";
                    return "할 일 없음";
                }
                case "table":
                {
                    var t = (Table)part.refObj;
                    if (!roles.Contains("in")) return t.boxes.Count > 0 && full ? "손이 가득 찼습니다" : "포장된 박스 없음";
                    if (t.order == null) return t.boxes.Count > 0 && full ? "손이 가득 찼습니다" : "주문 대기 중";
                    return "이송 물품 대기 중";
                }
                case "dock":
                    if (LoadableDock() == part.refObj) return "[E] 상차 미니게임";
                    return S.HasKind(p, HandKind.Box) ? "이 도크 목적지 박스가 아닙니다" : "트럭 대기 중";
                case "trash":
                    return "버릴 파손품 없음";
            }
            return "";
        }

        // 상차 미니게임을 열 수 있는 도크
        public static Dock LoadableDock()
        {
            var z = G.player.zone;
            var part = z?.parts.Find(q => q.kind == "dock");
            if (part == null) return null;
            var d = (Dock)part.refObj;
            return Docks.CanOpen(d) ? d : null;
        }

        static int Pop(System.Collections.Generic.List<int> list)
        {
            int v = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            return v;
        }
    }
}
