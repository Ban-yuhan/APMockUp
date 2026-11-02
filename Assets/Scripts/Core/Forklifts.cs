// 지게차: 탑승/하차, 주행, 출고 도크 박스 묶기(파렛트), 알바가 차체를 뚫고 지나가지 않게 막기
// 실제 지게차처럼 앞바퀴는 고정, 뒷바퀴로 조향한다. 그래서 차체는 앞차축을 기준으로 돌고 뒤가 크게 휩쓴다
using System;
using System.Collections.Generic;
using System.Linq;
using static APC.Game;

namespace APC
{
    public static class Forklifts
    {
        public const double BOARD_RANGE = 1.0;   // 차체 가장자리에서 이만큼 안이면 탑승 가능
        const double ACTOR_R = 0.3;              // 알바 몸 반지름 (차체를 이만큼 넓혀서 막는다)

        public static Forklift Ridden() => G.forklifts.Find(f => f.shown && f.ridden);
        public static bool Riding => Ridden() != null;

        // 탑승할 수 있는 가장 가까운 지게차 (없으면 null)
        public static Forklift Boardable()
        {
            if (Riding) return null;
            Forklift best = null;
            double bd = BOARD_RANGE;
            foreach (var f in G.forklifts)
            {
                if (!f.shown) continue;
                var (lx, lz) = Local(f, G.player.Pos);
                double d = U.Hypot(Math.Max(0, Math.Abs(lx) - f.width / 2), Math.Max(0, Math.Abs(lz) - f.length / 2));
                if (d <= bd) { bd = d; best = f; }
            }
            return best;
        }

        public static bool CanBoard() => Boardable() != null;

        // E 키: 타고 있으면 하차, 가까우면 탑승. 아무것도 안 했으면 false
        public static bool ToggleRide()
        {
            var r = Ridden();
            if (r != null) { Leave(r); return true; }
            var f = Boardable();
            if (f == null) return false;
            var p = G.player;
            Player.LeaveZone();
            p.zone = null; p.zoneKey = null; p.zoneMode = null; p.action = null; p.target = null;
            f.ridden = true;
            f.speed = 0;
            SyncPlayer(f);
            return true;
        }

        // R 키: 소환 지게차를 플레이어 앞에 플레이어가 보는 방향으로 꺼내고, 나와 있으면 (타고 있어도 내리고) 넣는다
        public static bool ToggleSummon()
        {
            var f = G.forklifts.Find(x => x.summon);
            if (f == null) return false;
            if (f.shown)
            {
                if (f.ridden) Leave(f);
                f.shown = false;
                return true;
            }
            if (Riding) { Notify("", "운전 중에는 지게차를 꺼낼 수 없습니다"); return false; }
            var p = G.player;
            var (fx, fz) = Fwd(p.facing);
            double d = f.length / 2 + ACTOR_R + 0.5;
            f.x = p.x + fx * d; f.z = p.z + fz * d;
            f.heading = p.facing;
            f.speed = 0;
            if (!InBounds(f)) { Notify("", "지게차를 꺼낼 공간이 없습니다"); return false; }
            f.shown = true;
            return true;
        }

        // 지게차는 그 자리에 두고 운전석 쪽(왼쪽) 옆에 내린다
        static void Leave(Forklift f)
        {
            var p = G.player;
            f.ridden = false;
            f.speed = 0;
            var (rx, rz) = Right(f.heading);
            var bd = LAYOUT.bounds;
            double off = f.width / 2 + ACTOR_R + 0.2;
            p.x = U.Clamp(f.x - rx * off, bd.minX, bd.maxX);
            p.z = U.Clamp(f.z - rz * off, bd.minZ, bd.maxZ);
            p.moving = false;
        }

        // input.x = 좌우(조향), input.z = 앞뒤(가속). 화면 기준이 아니라 지게차 기준
        public static void Update(double dt, P input)
        {
            foreach (var f in G.forklifts)
            {
                if (!f.shown) continue;
                f.action = null;
                if (f.ridden)
                {
                    Drive(f, dt, input.x, input.z);
                    SyncPlayer(f);
                    Work(f, dt);
                }
                foreach (var a in G.albas)
                {
                    var q = Push(f, a.Pos);
                    a.x = q.x; a.z = q.z;
                }
            }
        }

        static void Drive(Forklift f, double dt, double h, double v)
        {
            double px = f.x, pz = f.z, ph = f.heading;
            bool pivot = Math.Abs(v) < 0.15 && Math.Abs(h) > 0.1;
            double target = pivot ? 0 : v >= 0 ? v * f.maxSpeed : v * f.reverseSpeed;
            bool speedUp = target * f.speed >= 0 && Math.Abs(target) > Math.Abs(f.speed);
            double dv = (speedUp ? f.accel : f.brake) * dt;
            f.speed = Math.Abs(target - f.speed) <= dv ? target : f.speed + Math.Sign(target - f.speed) * dv;

            if (pivot && f.speed == 0)
            {
                // 제자리 회전: 시계(D)는 오른쪽 앞바퀴, 반시계(A)는 왼쪽 앞바퀴가 축 (뒷바퀴를 끝까지 꺾은 상태)
                double side = h > 0 ? 1 : -1;
                Rotate(f, Wheel(f, side), -side * Math.Abs(h) * f.pivotRate * Math.PI / 180 * dt);
            }
            else if (f.speed != 0)
            {
                // 앞차축 중심은 곧게 나아가고 차체가 그 점을 기준으로 돈다. 회전량은 속도에 비례 (회전 반경 일정), 후진이면 반대로 돈다
                var (fx, fz) = Fwd(f.heading);
                f.x += fx * f.speed * dt;
                f.z += fz * f.speed * dt;
                Rotate(f, Axle(f), -h * f.turnRate * Math.PI / 180 * (f.speed / f.maxSpeed) * dt);
            }

            // 창고 벽에 차체 모서리가 닿으면 그 스텝은 취소하고 멈춘다
            if (!InBounds(f)) { f.x = px; f.z = pz; f.heading = ph; f.speed = 0; }
        }

        // ---------- 출고 도크 묶음 ----------
        // 멈춘 채 포크 끝이 도크 앞이면 대기 박스를 한 개씩(집기 1회 시간마다) 지게차 파렛트로 옮긴다
        // 낱개가 bundleSize개 모이면 묶음 하나로 바뀐다. 묶음이 palletCap개면 더 옮기지 않는다
        // 묶음은 지게차에 탄 채로 연 상차 미니게임에서 싣는다
        static void Work(Forklift f, double dt)
        {
            var d = DockAt(f);
            double h = CONFIG.timing.handle;
            if (d == null || f.speed != 0) { f.workTimer = h; return; }
            var L = CONFIG.loading;
            string count = $"낱개 {f.loose.Count}/{L.bundleSize}";
            int i = d.waiting.FindIndex(b => b.parts == null);
            string idle = f.pallet.Count >= L.palletCap ? $"파렛트가 가득 찼습니다 ({L.palletCap}묶음)"
                // 한 묶음에는 한 도착지만: 다른 도크 낱개가 실려 있으면 그 도크에서 마저 채운다
                : f.loose.Count > 0 && f.loose[0].dest != d.idx ? $"{CONFIG.destinations[f.loose[0].dest].name} 박스를 먼저 묶어야 합니다 ({count})"
                : i < 0 ? $"옮길 대기 박스 없음 ({count})"
                : null;
            if (idle != null) { f.action = idle; f.workTimer = h; return; }
            f.action = $"박스 옮기는 중 ({count})";
            f.workTimer -= dt;
            if (f.workTimer > 0) return;
            f.workTimer = h;
            f.loose.Add(d.waiting[i]);
            d.waiting.RemoveAt(i);
            if (f.loose.Count < L.bundleSize) return;
            f.pallet.Add(Bundle(f.loose));
            f.loose.Clear();
        }

        // 타고 있는 지게차로 상차 미니게임을 열 수 있는 도크: 포크 끝이 도크 앞이고 파렛트에 이 도크 묶음이 있을 때
        // 몸통(뒤끝 ~ 앞차축, 포크 제외)이 출고 발판 위에 있고 이 도크 묶음·낱개가 실려 있을 때. 발판 점이 몸통 사각형(ACTOR_R 여유) 안이면 위에 있다고 본다
        public static Dock LoadableDock()
        {
            var f = Ridden();
            if (f == null) return null;
            foreach (var d in G.docks)
            {
                if (!d.unlocked) continue;
                var (lx, lz) = Local(f, S.DockIn(d));
                bool onBody = Math.Abs(lx) <= f.width / 2 + ACTOR_R && lz >= -f.length / 2 - ACTOR_R && lz <= f.frontAxle + ACTOR_R;
                if (onBody && Docks.CanOpen(d) && (f.pallet.Exists(b => b.dest == d.idx) || f.loose.Exists(b => b.dest == d.idx))) return d;
            }
            return null;
        }

        // 포크 끝에서 상호작용 범위 안의 출고 도크 (가장 가까운 것)
        static Dock DockAt(Forklift f)
        {
            var tip = World(f, 0, f.length / 2);
            Dock best = null;
            double bd = CONFIG.interactRadius;
            foreach (var d in G.docks)
            {
                if (!d.unlocked) continue;
                double dist = U.Dist(tip, S.DockIn(d));
                if (dist <= bd) { bd = dist; best = d; }
            }
            return best;
        }

        // 낱개 박스들 → 묶음 한 조각 (4~5칸 랜덤 모양). 대금은 합 × bundleRewardMult, 타입·품목은 모두 합친다
        public static Box Bundle(List<Box> boxes)
        {
            var L = CONFIG.loading;
            var src = boxes.ToList();
            var shape = LoadLogic.RandomShape(U.RandInt(4, 5));
            var box = new Box
            {
                id = G.seqBox++, dest = src[0].dest, parts = src, shape = shape.name, cells = shape.cells.ToList(),
                reward = U.Round(src.Sum(b => b.reward) * L.bundleRewardMult),
                units = src.Sum(b => b.units),
                types = src.SelectMany(b => b.types).Distinct().ToList(),
                primary = src.GroupBy(b => b.primary).OrderByDescending(g => g.Count()).First().Key,
            };
            foreach (var b in src)
                foreach (var kv in b.items) box.items[kv.Key] = (box.items.TryGetValue(kv.Key, out int n) ? n : 0) + kv.Value;
            return box;
        }

        static void SyncPlayer(Forklift f)
        {
            var p = G.player;
            p.x = f.x; p.z = f.z; p.facing = f.heading;
            p.moving = false;
        }

        // 알바 이동: 다음 위치가 차체 안이면 차체 면을 따라 미끄러지며 돌아간다
        // shortcut: 처음 닿은 지게차 하나만 피한다, 지게차 둘이 붙어 있으면 그 틈으로 끼일 수 있다
        public static P Avoid(P from, double vx, double vz, double step)
        {
            var next = new P(from.x + vx * step, from.z + vz * step);
            foreach (var f in G.forklifts)
                if (f.shown && Inside(f, next)) return AvoidOne(f, from, next, step);
            return next;
        }

        static bool Inside(Forklift f, P p)
        {
            var (lx, lz) = Local(f, p);
            return Math.Abs(lx) < f.width / 2 + ACTOR_R && Math.Abs(lz) < f.length / 2 + ACTOR_R;
        }

        static P AvoidOne(Forklift f, P from, P next, double step)
        {
            double hx = f.width / 2 + ACTOR_R, hz = f.length / 2 + ACTOR_R;
            var (nx, nz) = Local(f, next);
            var (lx, lz) = Local(f, from);
            if (Math.Abs(lx) < hx && Math.Abs(lz) < hz) return Push(f, from);
            // 닿은 면에 붙여 두고 면 방향으로만 움직인다. 정면으로 부딪히면 가까운 끝 쪽으로 돈다
            if (Math.Abs(lx) - hx >= Math.Abs(lz) - hz)
                return World(f, Math.Sign(lx) * hx, lz + Side(nz - lz, lz) * step);
            return World(f, lx + Side(nx - lx, lx) * step, Math.Sign(lz) * hz);
        }

        static double Side(double move, double pos) => Math.Abs(move) > 1e-9 ? Math.Sign(move) : pos >= 0 ? 1 : -1;

        // 지게차가 알바 위로 지나가면 가장 가까운 면 밖으로 밀어낸다
        static P Push(Forklift f, P pos)
        {
            double hx = f.width / 2 + ACTOR_R, hz = f.length / 2 + ACTOR_R;
            var (lx, lz) = Local(f, pos);
            if (Math.Abs(lx) >= hx || Math.Abs(lz) >= hz) return pos;
            var bd = LAYOUT.bounds;
            var q = hx - Math.Abs(lx) < hz - Math.Abs(lz)
                ? World(f, (lx >= 0 ? 1 : -1) * hx, lz)
                : World(f, lx, (lz >= 0 ? 1 : -1) * hz);
            return new P(U.Clamp(q.x, bd.minX, bd.maxX), U.Clamp(q.z, bd.minZ, bd.maxZ));
        }

        // ---------- 좌표 (배치 좌표, facing 규칙: 앞 = (sin, cos), 시계 방향 = heading 감소) ----------
        public static (double, double) Fwd(double h) => (Math.Sin(h), Math.Cos(h));
        public static (double, double) Right(double h) => (-Math.Cos(h), Math.Sin(h));

        // side: +1 오른쪽 앞바퀴, -1 왼쪽 앞바퀴
        public static P Wheel(Forklift f, double side) => World(f, side * f.halfTrack, f.frontAxle);
        static P Axle(Forklift f) => World(f, 0, f.frontAxle);

        // 차체 기준 (오른쪽, 앞)
        static (double, double) Local(Forklift f, P p)
        {
            double dx = p.x - f.x, dz = p.z - f.z;
            var (fx, fz) = Fwd(f.heading);
            var (rx, rz) = Right(f.heading);
            return (dx * rx + dz * rz, dx * fx + dz * fz);
        }

        static P World(Forklift f, double lx, double lz)
        {
            var (fx, fz) = Fwd(f.heading);
            var (rx, rz) = Right(f.heading);
            return new P(f.x + rx * lx + fx * lz, f.z + rz * lx + fz * lz);
        }

        // pivot 점을 기준으로 차체를 d만큼 돌린다 (d < 0 = 시계 방향)
        static void Rotate(Forklift f, P pivot, double d)
        {
            double rx = f.x - pivot.x, rz = f.z - pivot.z, c = Math.Cos(d), s = Math.Sin(d);
            f.x = pivot.x + rx * c + rz * s;
            f.z = pivot.z + rz * c - rx * s;
            f.heading += d;
        }

        static bool InBounds(Forklift f)
        {
            var bd = LAYOUT.bounds;
            foreach (var (sx, sz) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
            {
                var c = World(f, sx * f.width / 2, sz * f.length / 2);
                if (c.x < bd.minX || c.x > bd.maxX || c.z < bd.minZ || c.z > bd.maxZ) return false;
            }
            return true;
        }
    }
}
