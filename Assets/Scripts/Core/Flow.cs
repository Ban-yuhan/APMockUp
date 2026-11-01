// 주문서, 입고 트럭, 출고 도크(트럭) 흐름 (원본 js/flow.js)
using System;
using System.Collections.Generic;
using System.Linq;
using static APC.Game;

namespace APC
{
    public static class Orders
    {
        public static void Update(double dt)
        {
            G.orderTimer -= dt;
            if (G.orderTimer <= 0)
            {
                Spawn();
                G.orderTimer = CONFIG.orders.interval * (G.eventName != null ? CONFIG.evt.intervalMult : 1);
            }
            AssignTables();
        }

        static readonly (int n, double weight)[] UNITS = { (1, 2), (2, 4), (3, 4), (4, 3), (5, 2) };

        public static void Spawn()
        {
            var c = CONFIG.orders;
            var items = G.bins.Where(b => b.unlocked).Select(b => b.item).ToList();
            var docks = G.docks.Where(d => d.unlocked).ToList();
            int units = U.Weighted(UNITS.Where(u => u.n <= c.maxUnits).ToList(), u => u.weight).n;
            int kinds = Math.Min(Math.Min(U.RandInt(1, c.maxKinds), units), items.Count);
            var chosen = U.Shuffle(items.ToList()).Take(kinds).ToList();
            var qty = new SortedDictionary<int, int>();
            foreach (var i in chosen) qty[i] = 1;
            for (int k = kinds; k < units; k++) qty[U.Pick(chosen)]++;
            int reward = c.baseReward;
            foreach (var kv in qty) reward += CONFIG.items[kv.Key].price * kv.Value * c.priceMult;
            G.orders.Add(new Order { id = G.seqOrder++, dest = U.Pick(docks).idx, items = qty, units = units, reward = reward, table = -1 });
        }

        // 왼쪽(가장 오래된) 주문부터 빈 포장대에 배정
        public static void AssignTables()
        {
            foreach (var t in G.tables)
            {
                if (!t.unlocked || t.order != null) continue;
                var o = G.orders.Find(x => x.table == -1);
                if (o == null) return;
                o.table = t.idx;
                t.order = o;
                t.delivered = new Dictionary<int, int>();
            }
        }

        // 포장 완료 → 박스 생성 (주문 1건 = 박스 1개)
        public static void Pack(Table t)
        {
            var o = t.order;
            var shape = LoadLogic.RandomShape(o.units);
            var types = new List<string>();
            var typeCount = new Dictionary<string, int>();
            foreach (var kv in o.items)
            {
                var ty = CONFIG.items[kv.Key].type;
                if (!typeCount.ContainsKey(ty)) { types.Add(ty); typeCount[ty] = 0; }
                typeCount[ty] += kv.Value;
            }
            string primary = types.Aggregate((a, b) => typeCount[b] > typeCount[a] ? b : a);
            t.boxes.Add(new Box
            {
                id = G.seqBox++, order = o.id, dest = o.dest, reward = o.reward, units = o.units,
                shape = shape.name, cells = shape.cells.ToList(), types = types, primary = primary,
                items = new SortedDictionary<int, int>(o.items), claimedBy = Owner.None,
            });
            G.orders.Remove(o);
            t.order = null;
            t.delivered = new Dictionary<int, int>();
            t.packer = Owner.None;
            t.packProgress = 0;
            t.machineProgress = 0;
        }
    }

    public static class Inbound
    {
        public static void Update(double dt)
        {
            int open = S.ParkingCount();
            foreach (var tr in G.inTrucks)
            {
                if (tr.idx >= open) continue;
                if (tr.state == TruckState.Present && tr.items.Count == 0) tr.state = TruckState.Leaving;
                if (tr.state == TruckState.Leaving)
                {
                    tr.off += 9 * dt;
                    if (tr.off >= S.TRUCK_AWAY) { tr.state = TruckState.Away; tr.timer = CONFIG.inbound.returnDelay; }
                }
                else if (tr.state == TruckState.Away)
                {
                    tr.timer -= dt;
                    if (tr.timer > 0) continue;
                    tr.items = S.RollInboundItems();
                    // 보관함이 다 차서 실을 품목이 없으면 오지 않고 잠시 뒤 다시 확인
                    if (tr.items.Count > 0) { tr.state = TruckState.Arriving; tr.off = S.TRUCK_AWAY; }
                    else tr.timer = CONFIG.inbound.retryDelay;
                }
                else if (tr.state == TruckState.Arriving)
                {
                    tr.off -= 9 * dt;
                    if (tr.off <= 0) { tr.off = 0; tr.state = TruckState.Present; }
                }
            }
        }
    }

    public static class Docks
    {
        public static void Update(double dt)
        {
            foreach (var d in G.docks)
            {
                if (!d.unlocked) continue;
                var tr = d.truck;
                if (tr == null)
                {
                    d.truckTimer -= dt;
                    if (d.truckTimer <= 0) d.truck = LoadLogic.CreateTruck();
                    continue;
                }
                if (tr.state == TruckState.Arriving)
                {
                    tr.off -= 10 * dt;
                    if (tr.off <= 0) { tr.off = 0; tr.state = TruckState.Present; }
                }
                else if (tr.state == TruckState.Leaving)
                {
                    tr.off += 10 * dt;
                    if (tr.off >= S.TRUCK_AWAY) { d.truck = null; d.truckTimer = CONFIG.loading.truckReturnDelay; }
                }
                else if (tr.mode == "auto" && tr.autoCells > 0 && d.loader == Owner.None)
                {
                    // 남은 박스가 하나도 안 들어가면 잠시 뒤 그대로 출발
                    bool stuck = d.waiting.Count > 0 && !d.waiting.Any(b => LoadLogic.AutoFits(tr, b));
                    d.stuckTimer = stuck ? d.stuckTimer + dt : 0;
                    if (d.stuckTimer >= CONFIG.loading.stuckDepartDelay) Depart(d);
                }
            }
        }

        public static void ReleaseLoader(Dock d)
        {
            d.loader = Owner.None;
            var tr = d.truck;
            if (tr != null && tr.mode == "auto" && tr.autoCells == 0 && !S.IsAuto("load")) tr.mode = null;
        }

        public static Payout Depart(Dock d)
        {
            var tr = d.truck;
            if (tr == null || tr.state != TruckState.Present) return null;
            var p = LoadLogic.Payout(tr);
            if (p.boxes.Count == 0) return null;
            G.money += p.total;
            G.earned += p.total;
            G.shipped += p.boxes.Count;
            G.trucks++;
            string obj = p.objectives.Count > 0 ? $" (🎯 {p.objectives.Count(o => o.done)}/{p.objectives.Count} +{p.objBonus}%p)" : "";
            Notify("🚛", $"{CONFIG.destinations[d.idx].name} 트럭 출발! 박스 {p.boxes.Count}개 · 적재율 {U.Round(p.rate)}%{obj} · +💰{U.Money(p.total)}");
            tr.state = TruckState.Leaving;
            d.loader = Owner.None;
            d.stuckTimer = 0;
            if (Loading.dock == d) Loading.Close();
            return p;
        }

        // 상차 미니게임을 열 수 있는지 (원본 LoadingUI.canOpen)
        public static bool CanOpen(Dock d)
        {
            var tr = d?.truck;
            return d != null && d.unlocked && tr != null && tr.state == TruckState.Present && tr.mode != "auto" && d.loader == Owner.None && !S.IsAuto("load");
        }
    }
}
