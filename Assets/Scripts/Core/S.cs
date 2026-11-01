// 공용 판정 함수 (원본 js/state.js의 S)
using System;
using System.Collections.Generic;
using System.Linq;
using static APC.Game;

namespace APC
{
    public static class S
    {
        public const double TRUCK_AWAY = 15;   // 트럭이 들어오기 전/나간 뒤 주차 위치에서 떨어진 거리

        public static ItemDef Item(int i) => CONFIG.items[i];
        public static StageDef StageDef(string id) => CONFIG.stages.Find(s => s.id == id);
        public static int StageMaxLevel(string id) => StageDef(id).costs.Length;
        public static bool IsAuto(string id) => G.stages[id] >= 4;
        public static int StageSlots(string id) => IsAuto(id) ? 0 : G.stages[id];
        public static int StageCount(string id) => G.albas.Count(a => a.stage == id);

        // 물류 시설 수치. 하차 주차공간 단계 수 = 배치의 주차 슬롯 수 (원본 LayoutLib.prepare)
        public static int[] FacValues(string key)
        {
            if (key != "parking") return CONFIG.facility.Get(key).values;
            return Enumerable.Range(1, LAYOUT.inTrucks.Count).ToArray();
        }

        public static int[] FacCosts(string key)
        {
            var costs = CONFIG.facility.Get(key).costs.ToList();
            if (key != "parking") return costs.ToArray();
            int n = LAYOUT.inTrucks.Count;
            while (costs.Count < n) costs.Add(U.Round(costs[costs.Count - 1] * 1.6 / 100) * 100);
            return costs.Take(n).ToArray();
        }

        public static int FacValue(string key) => FacValues(key)[G.fac[key]];
        public static int ParkingCount() => FacValue("parking");
        public static int StagingCap() => FacValue("staging");
        public static int BinCap() => FacValue("bin");

        // 입고 가능 수량 = 보관함 최대 보관량 - (보관함 + 하차장 + 트럭/손에 있는 미검수 물품). 0 이하면 더 배송되지 않는다
        public static int InboundRoom(int item)
        {
            var b = G.bins[item];
            int n = b.uncheckedCount + b.checkedCount + b.incomingRaw;
            foreach (var it in G.staging) if (it == item) n++;
            foreach (var tr in G.inTrucks) foreach (var it in tr.items) if (it == item) n++;
            foreach (var a in Actors()) foreach (var e in a.hand) if (e.kind == HandKind.Raw && !e.reserved && e.item == item) n++;
            return BinCap() - n;
        }

        // 섞여서 오되, 입고 가능 수량이 남은 품목만, 많이 남은 품목일수록 많이 온다
        public static List<int> RollInboundItems()
        {
            var room = new SortedDictionary<int, int>();
            foreach (var b in G.bins) if (b.unlocked) room[b.item] = InboundRoom(b.item);
            int n = U.RandInt(CONFIG.inbound.minItems, CONFIG.inbound.maxItems);
            var output = new List<int>();
            for (int i = 0; i < n; i++)
            {
                var pool = room.Where(kv => kv.Value > 0).ToList();
                if (pool.Count == 0) break;
                int it = U.Weighted(pool, kv => kv.Value).Key;
                room[it]--;
                output.Add(it);
            }
            return output;
        }

        // 보관함
        public static int BinSpace(Bin b) => BinCap() - b.uncheckedCount - b.checkedCount - b.incomingRaw;
        public static int Avail(Bin b) => b.checkedCount - b.reservedPick;

        // 포장대
        public static int RemainingNeed(Table t, int item)
        {
            if (t.order == null) return 0;
            t.order.items.TryGetValue(item, out int need);
            t.delivered.TryGetValue(item, out int got);
            return need - got;
        }

        public static bool TableReady(Table t) => t.order != null && t.order.items.Keys.All(k => RemainingNeed(t, k) <= 0);

        public static List<Table> ActiveTables() => G.tables.Where(t => t.unlocked && t.order != null).OrderBy(t => t.order.id).ToList();

        // 손에 든 물건
        public static IEnumerable<Actor> Actors()
        {
            yield return G.player;
            foreach (var a in G.albas) yield return a;
        }

        public static int HandPickCount(int item)
        {
            int n = 0;
            foreach (var a in Actors()) foreach (var e in a.hand) if (e.kind == HandKind.Pick && e.item == item) n++;
            return n;
        }

        // 아직 아무도 가져가지 않은 필요 수량 (음수면 과잉)
        public static int OutstandingNeed(int item)
        {
            int need = 0;
            foreach (var t in G.tables) need += Math.Max(0, RemainingNeed(t, item));
            var b = G.bins[item];
            return need - HandPickCount(item) - b.reservedPick - b.inFlightPick;
        }

        public static bool HasKind(Actor actor, HandKind kind) => actor.hand.Any(e => e.kind == kind);
        public static bool CanTake(Actor actor, HandKind kind, int cap) => actor.hand.Count < cap && actor.hand.All(e => e.kind == kind);

        // MT
        public static double MtMult(double mt) => CONFIG.mt.effect.Find(e => mt >= e.min).mult;
        public static double RestChance(double mt) => CONFIG.mt.restChance.Find(e => mt >= e.min).p;

        // 위치 (배치 데이터의 슬롯 기준)
        public static P BinPos(Bin b) => b.slot.Pos;
        public static int BinSlotIdx(Bin b) => LAYOUT.bins.IndexOf(b.slot);
        public static int DockSlotIdx(Dock d) => LAYOUT.docks.IndexOf(d.slot);
        public static P BinIn(Bin b) => b.slot.Pt("in");
        public static P BinOut(Bin b) => b.slot.Pt("out");
        public static P BinInspect(Bin b) => b.slot.Pt("inspect");
        public static P TablePos(Table t) => t.slot.Pos;
        public static P TableIn(Table t) => t.slot.Pt("in");
        public static P TableOut(Table t) => t.slot.Pt("out");
        public static P DockPos(Dock d) => d.slot.Pos;
        public static P DockIn(Dock d) => d.slot.Pt("in");
        public static P StagingIn() => LAYOUT.staging.Pt("in");
        public static P StagingOut() => LAYOUT.staging.Pt("out");
        public static P InTruckOut(InTruck tr) => tr.slot.Pt("out");
        public static P TrashIn() => LAYOUT.trash.Pt("in");

        // 트럭 위치: 주차 위치에서 뒷문 반대 방향으로 off만큼 떨어진 곳
        public static P TruckPos(Slot park, double off)
        {
            var d = LayoutData.Dir(park.rot);
            return new P(park.x - d.x * off, park.z - d.z * off);
        }

        // 트럭 뒷문 앞 (상차 기계손이 박스를 넣는 곳)
        public static P TruckDoor(Slot park)
        {
            var d = LayoutData.Dir(park.rot);
            return new P(park.x + d.x * 2.4, park.z + d.z * 2.4);
        }

        // 사각형 구역 안에 겹치지 않게 자리 나누기
        public static P RectSpot(AreaRect r, int i, int n)
        {
            int cols = Math.Max(1, U.Round(Math.Sqrt(n * r.w / r.d)));
            int rows = (int)Math.Ceiling((double)n / cols);
            int k = i % n;
            return new P(r.x - r.w / 2 + (k % cols + 0.5) * r.w / cols, r.z - r.d / 2 + (k / cols + 0.5) * r.d / rows);
        }

        public static P RestSpot(Alba a) => RectSpot(LAYOUT.rest, a.id * 7 % 9, 9);
        public static P WaitSpot(Alba a) => RectSpot(LAYOUT.waiting, a.id % 8, 8);

        public static void AddMoney(int n) => G.money += n;

        public static bool Spend(int n)
        {
            if (G.money < n) { Notify("💸", "돈이 부족합니다"); return false; }
            G.money -= n;
            return true;
        }
    }
}
