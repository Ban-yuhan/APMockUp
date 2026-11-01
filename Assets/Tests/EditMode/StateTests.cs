// 1단계: 상태 생성과 S 헬퍼 (원본 js/state.js)
using System.Collections.Generic;
using System.Linq;
using APC.EditorTools;
using NUnit.Framework;
using UnityEngine;
using static APC.Game;

namespace APC.Tests
{
    public class StateTests
    {
        ApcConfig cfg;

        [SetUp]
        public void SetUp()
        {
            cfg = ScriptableObject.CreateInstance<ApcConfig>();
            Setup(cfg, DefaultLayout.Make(cfg), seed: 1234);
            G.player = new PlayerState { x = LAYOUT.playerStart.x, z = LAYOUT.playerStart.z };
        }

        [TearDown]
        public void TearDown()
        {
            ClearListeners();
            Object.DestroyImmediate(cfg);
        }

        static Alba NewAlba(int id) => new Alba { id = id, name = "테스트" + id, mt = 50 };

        [Test]
        public void CreateState_MatchesDefaultLayout()
        {
            Assert.AreEqual(500, G.money);
            Assert.AreEqual(8, G.bins.Count);
            CollectionAssert.AreEqual(new[] { true, true, true, false, false, false, false, false }, G.bins.Select(b => b.unlocked));
            Assert.AreEqual(3, G.inTrucks.Count);
            CollectionAssert.AreEqual(new[] { 0.5, 1.5, 2.5 }, G.inTrucks.Select(t => t.timer));
            Assert.IsTrue(G.inTrucks.All(t => t.state == TruckState.Away && t.off == S.TRUCK_AWAY));
            CollectionAssert.AreEqual(new[] { true, false, false }, G.tables.Select(t => t.unlocked));
            CollectionAssert.AreEqual(new[] { true, false, false, false }, G.docks.Select(d => d.unlocked));
            Assert.IsTrue(G.docks.All(d => d.truckTimer == 0.5));
            Assert.AreEqual(2, G.orderTimer);
            Assert.IsTrue(cfg.stages.All(s => G.stages[s.id] == 1));
            Assert.AreEqual(-1, G.carePurifier);
        }

        [Test]
        public void Bounds_InteriorInset()
        {
            Assert.AreEqual(0.6, LAYOUT.bounds.minX, 1e-9);
            Assert.AreEqual(51.4, LAYOUT.bounds.maxX, 1e-9);
            Assert.AreEqual(31.4, LAYOUT.bounds.maxZ, 1e-9);
        }

        [Test]
        public void Facility_ValuesAndParkingDerived()
        {
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, S.FacValues("parking"));
            CollectionAssert.AreEqual(new[] { 0, 400, 900 }, S.FacCosts("parking"));
            Assert.AreEqual(1, S.ParkingCount());
            Assert.AreEqual(30, S.StagingCap());
            Assert.AreEqual(20, S.BinCap());
            G.fac["bin"] = 4;
            Assert.AreEqual(70, S.BinCap());
        }

        [Test]
        public void Parking_ExtendsCostsForMoreSlots()
        {
            for (int i = 0; i < 2; i++) LAYOUT.inTrucks.Add(new Slot { x = 3, z = 20 + i });
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, S.FacValues("parking"));
            // 900*1.6 = 1440 → 1400, 1400*1.6 = 2240 → 2200
            CollectionAssert.AreEqual(new[] { 0, 400, 900, 1400, 2200 }, S.FacCosts("parking"));
        }

        [Test]
        public void Stages_AutoAndSlots()
        {
            Assert.AreEqual(3, S.StageMaxLevel("unload"));
            Assert.AreEqual(4, S.StageMaxLevel("sort"));
            Assert.IsFalse(S.IsAuto("sort"));
            Assert.AreEqual(1, S.StageSlots("sort"));
            G.stages["sort"] = 4;
            Assert.IsTrue(S.IsAuto("sort"));
            Assert.AreEqual(0, S.StageSlots("sort"));
            var a = NewAlba(1); a.stage = "pack"; G.albas.Add(a);
            Assert.AreEqual(1, S.StageCount("pack"));
        }

        [Test]
        public void InboundRoom_CountsEverywhere()
        {
            var b = G.bins[0];
            b.uncheckedCount = 3; b.checkedCount = 2; b.incomingRaw = 1;
            G.staging.AddRange(new[] { 0, 0, 1 });
            G.inTrucks[0].items.AddRange(new[] { 0, 2 });
            G.player.hand.Add(new HandEntry { kind = HandKind.Raw, item = 0 });
            var a = NewAlba(1);
            a.hand.Add(new HandEntry { kind = HandKind.Raw, item = 0, reserved = true });   // 예약분은 incomingRaw로 이미 셈
            a.hand.Add(new HandEntry { kind = HandKind.Raw, item = 0 });
            G.albas.Add(a);
            // 20 - (3+2+1 + 하차장 2 + 트럭 1 + 손 2)
            Assert.AreEqual(9, S.InboundRoom(0));
        }

        [Test]
        public void RollInboundItems_RespectsRoomAndRange()
        {
            for (int k = 0; k < 50; k++)
            {
                var items = S.RollInboundItems();
                Assert.That(items.Count, Is.InRange(12, 20));
                Assert.IsTrue(items.All(i => G.bins[i].unlocked));
            }
            // 공간이 거의 없으면 그만큼만
            G.bins[0].checkedCount = 20; G.bins[1].checkedCount = 20; G.bins[2].checkedCount = 18;
            var few = S.RollInboundItems();
            CollectionAssert.AreEqual(new[] { 2, 2 }, few);
            G.bins[2].checkedCount = 20;
            Assert.AreEqual(0, S.RollInboundItems().Count);
        }

        [Test]
        public void Bin_SpaceAndAvail()
        {
            var b = G.bins[1];
            b.uncheckedCount = 4; b.checkedCount = 6; b.incomingRaw = 2; b.reservedPick = 5;
            Assert.AreEqual(8, S.BinSpace(b));
            Assert.AreEqual(1, S.Avail(b));
        }

        Order MakeOrder(int id, params (int item, int q)[] items)
        {
            var o = new Order { id = id, dest = 0 };
            foreach (var (item, q) in items) o.items[item] = q;
            o.units = items.Sum(x => x.q);
            return o;
        }

        [Test]
        public void Table_NeedReadyActive()
        {
            var t0 = G.tables[0];
            Assert.AreEqual(0, S.RemainingNeed(t0, 0));
            Assert.IsFalse(S.TableReady(t0));
            t0.order = MakeOrder(5, (0, 2), (1, 1));
            t0.delivered[0] = 1;
            Assert.AreEqual(1, S.RemainingNeed(t0, 0));
            Assert.AreEqual(1, S.RemainingNeed(t0, 1));
            Assert.IsFalse(S.TableReady(t0));
            t0.delivered[0] = 2; t0.delivered[1] = 1;
            Assert.IsTrue(S.TableReady(t0));

            var t1 = G.tables[1];
            t1.order = MakeOrder(3, (2, 1));
            Assert.AreEqual(1, S.ActiveTables().Count);   // 잠긴 포장대 제외
            t1.unlocked = true;
            CollectionAssert.AreEqual(new[] { 1, 0 }, S.ActiveTables().Select(t => t.idx));   // 주문 id 순
        }

        [Test]
        public void OutstandingNeed_SubtractsHandsReservedFlight()
        {
            G.tables[0].order = MakeOrder(1, (0, 3));
            G.tables[1].unlocked = true;
            G.tables[1].order = MakeOrder(2, (0, 2));
            G.tables[1].delivered[0] = 1;
            G.player.hand.Add(new HandEntry { kind = HandKind.Pick, item = 0 });
            var a = NewAlba(1);
            a.hand.Add(new HandEntry { kind = HandKind.Pick, item = 0 });
            G.albas.Add(a);
            G.bins[0].reservedPick = 1;
            G.bins[0].inFlightPick = 1;
            Assert.AreEqual(2, S.HandPickCount(0));
            // 필요 3 + 1 - 손 2 - 예약 1 - 이동 1
            Assert.AreEqual(0, S.OutstandingNeed(0));
            G.bins[0].inFlightPick = 3;
            Assert.AreEqual(-2, S.OutstandingNeed(0));
        }

        [Test]
        public void Hand_CanTakeSameKindOnly()
        {
            var p = G.player;
            Assert.IsTrue(S.CanTake(p, HandKind.Raw, 4));
            p.hand.Add(new HandEntry { kind = HandKind.Raw, item = 0 });
            Assert.IsTrue(S.HasKind(p, HandKind.Raw));
            Assert.IsFalse(S.HasKind(p, HandKind.Box));
            Assert.IsTrue(S.CanTake(p, HandKind.Raw, 4));
            Assert.IsFalse(S.CanTake(p, HandKind.Pick, 4));
            Assert.IsFalse(S.CanTake(p, HandKind.Raw, 1));
        }

        [Test]
        public void Mt_MultAndRestChance()
        {
            Assert.AreEqual(1.15, S.MtMult(50));
            Assert.AreEqual(1.0, S.MtMult(49.9));
            Assert.AreEqual(1.0, S.MtMult(0));
            Assert.AreEqual(0.85, S.MtMult(-50));
            Assert.AreEqual(0.7, S.MtMult(-100));
            Assert.AreEqual(0.005, S.RestChance(20));
            Assert.AreEqual(0.03, S.RestChance(0));
            Assert.AreEqual(0.5, S.RestChance(-1));
            Assert.AreEqual(0.6, S.RestChance(-51));
        }

        [Test]
        public void Positions_SlotsAndTruck()
        {
            var b0 = G.bins[0];   // 위쪽 줄 (rot 90): 지점 = z + 2.2
            Assert.AreEqual(16, S.BinIn(b0).x, 1e-9);
            Assert.AreEqual(6.8, S.BinIn(b0).z, 1e-9);
            var b4 = G.bins[4];   // 아래쪽 줄 (rot 270): 지점 = z - 2.2
            Assert.AreEqual(15.2, S.BinInspect(b4).z, 1e-9);
            Assert.AreEqual(4, S.BinSlotIdx(b4));
            Assert.AreEqual(36.8, S.TableIn(G.tables[2]).x, 1e-9);
            Assert.AreEqual(47.2, S.DockIn(G.docks[3]).x, 1e-9);
            Assert.AreEqual(9.5, S.StagingOut().x, 1e-9);
            Assert.AreEqual(25, S.TrashIn().x, 1e-9);
            Assert.AreEqual(6.8, S.InTruckOut(G.inTrucks[1]).x, 1e-9);
            // 시설 위치 대신 쓰는 지점이 없으면 시설 위치
            Assert.AreEqual(39, S.TablePos(G.tables[0]).x, 1e-9);

            var park = LAYOUT.inTrucks[0];   // rot 0 → 뒷문 +x, 들어오기 전엔 -x로 15
            var away = S.TruckPos(park, S.TRUCK_AWAY);
            Assert.AreEqual(-12, away.x, 1e-9);
            Assert.AreEqual(8.5, away.z, 1e-9);
            var dockTruck = G.docks[0].slot.truck;   // rot 180 → 뒷문 -x
            Assert.AreEqual(69.4, S.TruckPos(dockTruck, 15).x, 1e-9);
            Assert.AreEqual(52, S.TruckDoor(dockTruck).x, 1e-9);
        }

        [Test]
        public void RectSpot_RestAndWait()
        {
            // 휴게실 9x7, 9자리 → 3열 3행
            var r = LAYOUT.rest;
            var s0 = S.RectSpot(r, 0, 9);
            Assert.AreEqual(1.5 + 1.5, s0.x, 1e-9);           // 6 - 4.5 + 0.5*3
            Assert.AreEqual(23.5 + 7.0 / 6, s0.z, 1e-9);
            var s4 = S.RectSpot(r, 4, 9);
            Assert.AreEqual(6, s4.x, 1e-9);
            Assert.AreEqual(27, s4.z, 1e-9);
            // 대기구역 5x3.6, 8자리 → round(sqrt(8*5/3.6)) = 3열, 3행
            var w = S.RectSpot(LAYOUT.waiting, 7, 8);
            Assert.AreEqual(16, w.x, 1e-9);
            Assert.AreEqual(27.5 + 1.2, w.z, 1e-9);
            var a = NewAlba(2);
            Assert.AreEqual(S.RectSpot(r, 5, 9).x, S.RestSpot(a).x, 1e-9);   // 2*7 % 9 = 5
            Assert.AreEqual(S.RectSpot(LAYOUT.waiting, 2, 8).z, S.WaitSpot(a).z, 1e-9);
        }

        [Test]
        public void Spend_ToastsWhenShort()
        {
            var toasts = new List<string>();
            Toast += (icon, text) => toasts.Add(icon + text);
            Assert.IsTrue(S.Spend(200));
            Assert.AreEqual(300, G.money);
            Assert.IsFalse(S.Spend(301));
            Assert.AreEqual(300, G.money);
            CollectionAssert.AreEqual(new[] { "💸돈이 부족합니다" }, toasts);
            S.AddMoney(5);
            Assert.AreEqual(305, G.money);
        }

        [Test]
        public void Util_RoundLikeJs()
        {
            Assert.AreEqual(3, U.Round(2.5));
            Assert.AreEqual(-2, U.Round(-2.5));
            Assert.AreEqual(14, U.Round(14.4));
            Assert.AreEqual("1,234", U.Money(1234.4));
            U.Seed(7);
            double a = U.Random();
            U.Seed(7);
            Assert.AreEqual(a, U.Random());
        }

        [Test]
        public void Util_WeightedAndShuffle()
        {
            var entries = new[] { (n: 1, w: 0.0), (n: 2, w: 1.0) };
            for (int i = 0; i < 20; i++) Assert.AreEqual(2, U.Weighted(entries, e => e.w).n);
            var list = Enumerable.Range(0, 10).ToList();
            U.Shuffle(list);
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 10), list);
            for (int i = 0; i < 100; i++) Assert.That(U.RandInt(1, 5), Is.InRange(1, 5));
        }

        [Test]
        public void Config_DefaultsMatchSource()
        {
            Assert.AreEqual(8, cfg.items.Count);
            Assert.AreEqual(4, cfg.destinations.Count);
            Assert.AreEqual(7, cfg.stages.Count);
            Assert.AreEqual(3, cfg.loading.truckTypes.Count);
            Assert.AreEqual(5, cfg.loading.shapes.Count);
            Assert.AreEqual(5, cfg.loading.Shapes(4).Count);
            Assert.AreEqual(new Vector2Int(1, 2), cfg.loading.Shapes(5)[0].cells[4]);
            Assert.AreEqual(double.NegativeInfinity, cfg.mt.effect[3].min);
            Assert.AreEqual("food", cfg.types[0].id);
        }
    }
}
