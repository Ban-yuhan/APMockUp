// 자동화(Lv4): 컨베이어 벨트, 기계손, 포장·상차 기계 (원본 js/automation.js)
using System;
using System.Collections.Generic;
using static APC.Game;

namespace APC
{
    public static class Auto
    {
        // 컨베이어 경로: 출발 시설의 벨트 출구 → (벨트 라인, 한 방향) → 도착 시설의 벨트 입구. 연결이 없으면 직선
        static List<P> Route(string stage, string src, string dst, P a, P b) =>
            Belts.CachedRoute(stage, src, dst) ?? new List<P> { a, b };

        public static List<P> SortPath(Bin b) =>
            Route("sort", "staging:0:beltOut", $"bin:{S.BinSlotIdx(b)}:beltIn", LAYOUT.staging.Pos, b.slot.Pos);
        public static List<P> TransferPath(Bin b, Table t) =>
            Route("transfer", $"bin:{S.BinSlotIdx(b)}:beltOut", $"table:{t.idx}:beltIn", b.slot.Pos, t.slot.Pos);
        public static List<P> OutboundPath(Table t, Dock d) =>
            Route("outbound", $"table:{t.idx}:beltOut", $"dock:{S.DockSlotIdx(d)}:beltIn", t.slot.Pos, d.slot.Pos);
        static List<P> LoadPath(Dock d) => new List<P> { d.slot.Pos, S.TruckDoor(d.slot.truck) };
        static List<P> BrokenPath(Bin b) => new List<P> { b.slot.Pos, LAYOUT.trash.Pos };

        static void Fly(HandKind kind, int item, Box box, List<P> path, Action onArrive) =>
            G.flyers.Add(new Flyer { kind = kind, item = item, box = box, path = path, seg = 0, x = path[0].x, z = path[0].z, onArrive = onArrive });

        public static void Update(double dt)
        {
            var c = CONFIG.automation;

            if (S.IsAuto("sort"))
            {
                G.autoSort += dt;
                if (G.autoSort >= c.sortInterval)
                {
                    G.autoSort = 0;
                    int i = G.staging.FindIndex(it => G.bins[it].unlocked && S.BinSpace(G.bins[it]) > 0);
                    if (i >= 0)
                    {
                        var b = G.bins[G.staging[i]];
                        G.staging.RemoveAt(i);
                        b.incomingRaw++;
                        Fly(HandKind.Raw, b.item, null, SortPath(b), () => { b.incomingRaw--; b.uncheckedCount++; });
                    }
                }
            }

            if (S.IsAuto("inspect"))
            {
                foreach (var b in G.bins)
                {
                    if (!b.unlocked || b.uncheckedCount <= 0) { b.armProgress = 0; continue; }
                    b.armProgress += dt;
                    if (b.armProgress < c.inspectTime) continue;
                    b.armProgress = 0;
                    b.uncheckedCount--;
                    if (U.Random() < CONFIG.breakChance.inspect) Fly(HandKind.Broken, b.item, null, BrokenPath(b), () => G.brokenCount++);
                    else b.checkedCount++;
                }
            }

            if (S.IsAuto("transfer"))
            {
                G.autoTransfer += dt;
                if (G.autoTransfer >= c.transferInterval)
                {
                    G.autoTransfer = 0;
                    TransferOne();
                }
            }

            if (S.IsAuto("pack"))
            {
                foreach (var t in G.tables)
                {
                    if (!t.unlocked || !S.TableReady(t)) { t.machineProgress = 0; continue; }
                    t.machineProgress += dt;
                    if (t.machineProgress >= c.packTime) Orders.Pack(t);
                }
            }

            if (S.IsAuto("outbound"))
            {
                G.autoOutbound += dt;
                if (G.autoOutbound >= c.outboundInterval)
                {
                    G.autoOutbound = 0;
                    var t = G.tables.Find(tb => tb.unlocked && tb.boxes.Exists(b => b.claimedBy == Owner.None));
                    if (t != null)
                    {
                        int i = t.boxes.FindIndex(b => b.claimedBy == Owner.None);
                        var box = t.boxes[i];
                        t.boxes.RemoveAt(i);
                        var d = G.docks[box.dest];
                        box.claimedBy = Owner.Auto;
                        Fly(HandKind.Box, 0, box, OutboundPath(t, d), () => { box.claimedBy = Owner.None; d.waiting.Add(box); });
                    }
                }
            }

            if (S.IsAuto("load"))
            {
                foreach (var d in G.docks)
                {
                    var tr = d.truck;
                    if (!d.unlocked || tr == null || tr.state != TruckState.Present || tr.mode == "manual") continue;
                    d.autoTimer += dt;
                    if (d.autoTimer < c.loadInterval) continue;
                    d.autoTimer = 0;
                    int i = d.waiting.FindIndex(b => LoadLogic.AutoFits(tr, b));
                    if (i < 0) continue;
                    var box = d.waiting[i];
                    d.waiting.RemoveAt(i);
                    tr.mode = "auto";
                    tr.autoTarget = c.loadTarget;
                    tr.autoCells += box.cells.Count;
                    tr.autoBoxes.Add(box);
                    Fly(HandKind.Box, 0, box, LoadPath(d), null);
                    if (LoadLogic.Fill(tr).ratio >= tr.autoTarget) Docks.Depart(d);
                }
            }

            UpdateFlyers(dt);
        }

        // 주문서 순서대로, 필요한 검수완료 물품을 포장대로 보낸다
        static void TransferOne()
        {
            foreach (var t in S.ActiveTables())
            {
                foreach (int item in t.order.items.Keys)
                {
                    var b = G.bins[item];
                    if (S.RemainingNeed(t, item) <= 0 || S.OutstandingNeed(item) <= 0 || S.Avail(b) <= 0) continue;
                    b.checkedCount--;
                    b.inFlightPick++;
                    Fly(HandKind.Pick, item, null, TransferPath(b, t), () =>
                    {
                        b.inFlightPick--;
                        var dest = S.RemainingNeed(t, item) > 0 ? t : S.ActiveTables().Find(x => S.RemainingNeed(x, item) > 0);
                        if (dest != null) dest.delivered[item] = (dest.delivered.TryGetValue(item, out int n) ? n : 0) + 1;
                        else b.checkedCount++;
                    });
                    return;
                }
            }
        }

        static void UpdateFlyers(double dt)
        {
            double sp = CONFIG.automation.flySpeed * dt;
            for (int i = G.flyers.Count - 1; i >= 0; i--)
            {
                var f = G.flyers[i];
                double left = sp;
                while (left > 0 && f.seg < f.path.Count - 1)
                {
                    var q = f.path[f.seg + 1];
                    double dx = q.x - f.x, dz = q.z - f.z, d = U.Hypot(dx, dz);
                    if (d <= left) { f.x = q.x; f.z = q.z; f.seg++; left -= d; }
                    else { f.x += dx / d * left; f.z += dz / d * left; left = 0; }
                }
                if (f.seg >= f.path.Count - 1)
                {
                    f.onArrive?.Invoke();
                    G.flyers.RemoveAt(i);
                }
            }
        }
    }
}
