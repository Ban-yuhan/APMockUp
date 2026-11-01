// 4단계: 입고·주문·도크 흐름과 플레이어 수동 작업 한 바퀴, 재고 예약 카운터 불변식 (원본 flow.js / player.js / main.js)
using System.Collections.Generic;
using System.Linq;
using APC.EditorTools;
using NUnit.Framework;
using UnityEngine;
using static APC.Game;

namespace APC.Tests
{
    public class PipelineTests
    {
        ApcConfig cfg;
        List<string> toasts;

        [SetUp]
        public void SetUp()
        {
            cfg = ScriptableObject.CreateInstance<ApcConfig>();
            Setup(cfg, DefaultLayout.Make(cfg), seed: 2024);
            Sim.Start();
            Sim.input = default;
            toasts = new List<string>();
            Toast += (i, t) => toasts.Add(t);
        }

        [TearDown]
        public void TearDown()
        {
            ClearListeners();
            Object.DestroyImmediate(cfg);
        }

        // 원본 selfTest의 sim(sec)
        static void RunFor(double sec)
        {
            for (double t = 0; t < sec; t += Sim.STEP) Sim.Update(Sim.STEP);
        }

        static void Go(P p) { G.player.x = p.x; G.player.z = p.z; }

        static void AssertInvariants() => CollectionAssert.IsEmpty(Invariants.Check());

        [Test]
        public void Start_FirstTruckPresentWithItems()
        {
            var t0 = G.inTrucks[0];
            Assert.AreEqual(TruckState.Present, t0.state);
            Assert.That(t0.items.Count, Is.InRange(12, 20));
            Assert.AreEqual(TruckState.Away, G.inTrucks[1].state);
        }

        [Test]
        public void Inbound_EmptyTruckLeavesAndReturns()
        {
            var t0 = G.inTrucks[0];
            t0.items.Clear();
            Sim.Update(0.05);
            Assert.AreEqual(TruckState.Leaving, t0.state);
            RunFor(15.0 / 9 + 0.1);
            Assert.AreEqual(TruckState.Away, t0.state);
            Assert.AreEqual(8, t0.timer, 0.2);
            RunFor(8.1);
            Assert.AreEqual(TruckState.Arriving, t0.state);
            Assert.That(t0.items.Count, Is.InRange(12, 20));
            RunFor(15.0 / 9 + 0.1);
            Assert.AreEqual(TruckState.Present, t0.state);
            Assert.AreEqual(0, t0.off);
            // 주차공간 Lv0 = 1대만 순환
            Assert.AreEqual(TruckState.Away, G.inTrucks[1].state);
        }

        [Test]
        public void Inbound_RetriesWhenBinsFull()
        {
            var t0 = G.inTrucks[0];
            t0.items.Clear();
            foreach (var b in G.bins) b.checkedCount = 20;
            RunFor(15.0 / 9 + 0.1 + 8.1);
            Assert.AreEqual(TruckState.Away, t0.state);
            Assert.That(t0.timer, Is.InRange(1.5, 2.0));   // 다시 확인까지 retryDelay 2초
        }

        [Test]
        public void Orders_SpawnRulesAndAssign()
        {
            for (int k = 0; k < 200; k++) Orders.Spawn();
            foreach (var o in G.orders)
            {
                Assert.That(o.units, Is.InRange(1, 5));
                Assert.AreEqual(o.units, o.items.Values.Sum());
                Assert.That(o.items.Count, Is.InRange(1, 3));
                Assert.IsTrue(o.items.Keys.All(i => G.bins[i].unlocked));
                Assert.AreEqual(0, o.dest);   // 해금 도크는 서울뿐
                int reward = 10 + o.items.Sum(kv => cfg.items[kv.Key].price * kv.Value * 2);
                Assert.AreEqual(reward, o.reward);
            }
            Orders.AssignTables();
            Assert.AreSame(G.orders[0], G.tables[0].order);
            Assert.AreEqual(0, G.orders[0].table);
            Assert.IsNull(G.tables[1].order);   // 잠긴 포장대
            Assert.AreEqual(-1, G.orders[1].table);
        }

        [Test]
        public void Orders_TimerFirstDelayThenInterval()
        {
            RunFor(1.9);
            Assert.AreEqual(0, G.orders.Count);
            RunFor(0.2);
            Assert.AreEqual(1, G.orders.Count);
            Assert.AreEqual(25, G.orderTimer, 0.2);
            Assert.AreSame(G.orders[0], G.tables[0].order);
        }

        [Test]
        public void Orders_PackMakesBox()
        {
            var t = G.tables[0];
            var o = new Order { id = 7, dest = 0, units = 4, reward = 99 };
            o.items[0] = 1; o.items[1] = 2; o.items[2] = 1;   // 식품 2, 생필품 2 → 먼저 나온 식품이 대표
            G.orders.Add(o);
            Orders.AssignTables();
            Orders.Pack(t);
            var box = t.boxes.Single();
            CollectionAssert.AreEqual(new[] { "food", "daily" }, box.types);
            Assert.AreEqual("food", box.primary);
            Assert.AreEqual(4, box.cells.Count);
            Assert.AreEqual(99, box.reward);
            Assert.AreEqual(7, box.order);
            Assert.IsNull(t.order);
            Assert.IsEmpty(G.orders);
        }

        [Test]
        public void Docks_TruckArrivesAndDeparts()
        {
            var d = G.docks[0];
            RunFor(0.5);
            Assert.IsNotNull(d.truck);
            Assert.AreEqual(TruckState.Arriving, d.truck.state);
            Assert.IsNull(G.docks[1].truck);   // 잠긴 도크
            RunFor(1.6);
            Assert.AreEqual(TruckState.Present, d.truck.state);
            Assert.IsTrue(Docks.CanOpen(d));
            Assert.IsNull(Docks.Depart(d));   // 실은 박스가 없으면 출발 안 함

            var box = new Box { id = 1, dest = 0, reward = 50, units = 1, cells = { Vector2Int.zero }, types = { "food" } };
            d.truck.placements.Add(new Placement { box = box, cells = new List<Vector2Int> { Vector2Int.zero } });
            d.truck.mode = "manual";
            Loading.dock = d; G.paused = true;
            var pay = Docks.Depart(d);
            Assert.IsNotNull(pay);
            Assert.IsNull(Loading.dock);   // 열려 있던 상차 패널이 닫힌다
            Assert.IsFalse(G.paused);
            Assert.AreEqual(500 + pay.total, G.money);
            Assert.AreEqual(1, G.trucks);
            Assert.AreEqual(TruckState.Leaving, d.truck.state);
            RunFor(1.6);
            Assert.IsNull(d.truck);
            Assert.That(d.truckTimer, Is.InRange(9.8, 10.0));   // 다시 오기까지 truckReturnDelay 10초
        }

        // 입고 → 하차 → 분류 → 검수 → 이송 → 포장 → 출고분류를 플레이어 손으로 한 바퀴
        [Test]
        public void Player_FullLoopByHand()
        {
            var p = G.player;
            var t0 = G.inTrucks[0];
            G.orderTimer = 1e9;   // 주문은 직접 넣는다

            // 하차: 트럭 앞에서 4개
            Go(S.InTruckOut(t0));
            RunFor(3);
            Assert.AreEqual(4, p.hand.Count);
            Assert.IsTrue(p.hand.All(e => e.kind == HandKind.Raw));
            Assert.AreEqual("손이 가득 찼습니다", p.action);
            AssertInvariants();

            // 하차장: 들고 들어가면 내려놓기만
            Go(S.StagingIn());
            RunFor(2);
            Assert.AreEqual(4, G.staging.Count);
            Assert.AreEqual(0, p.hand.Count);
            Go(LAYOUT.playerStart); RunFor(0.2);
            // 빈손으로 들어가면 집기만 (집자마자 내려놓지 않음)
            Go(S.StagingOut());
            RunFor(3);
            Assert.AreEqual(Player.Capacity(), p.hand.Count);
            Assert.AreEqual(0, G.staging.Count);
            Go(LAYOUT.playerStart); RunFor(0.2);

            // 분류: 첫 품목 보관함에 같은 품목만 넣는다
            int first = p.hand[0].item, same = p.hand.Count(e => e.item == first);
            var b = G.bins[first];
            Go(S.BinIn(b));
            RunFor(0.25 + same * 0.25);   // 같은 지점에서 검수가 시작되기 전까지
            Assert.AreEqual(same, b.uncheckedCount);
            Assert.AreEqual(4 - same, p.hand.Count);
            // 나머지는 각자 보관함에
            while (p.hand.Count > 0)
            {
                Go(LAYOUT.playerStart); RunFor(0.2);
                Go(S.BinIn(G.bins[p.hand[0].item])); RunFor(2);
            }
            AssertInvariants();

            // 검수 (합쳐진 지점: 넣기·꺼내기·검수 같은 위치)
            Go(LAYOUT.playerStart); RunFor(0.2);
            Go(S.BinInspect(b));
            RunFor(same * 1.0 + 1);
            Assert.AreEqual(0, b.uncheckedCount);
            Assert.AreEqual(same, b.checkedCount + p.hand.Count(e => e.kind == HandKind.Broken) + b.broken);
            if (p.hand.Count > 0)
            {
                // 파손품은 쓰레기통에
                Go(LAYOUT.playerStart); RunFor(0.2);
                Go(S.TrashIn()); RunFor(2);
                Assert.AreEqual(0, p.hand.Count);
                Assert.That(G.brokenCount, Is.GreaterThan(0));
            }
            Assume.That(b.checkedCount, Is.GreaterThan(0), "시드 2024에서 첫 품목이 모두 파손됨");

            // 주문: 첫 품목 1개 → 포장대 1
            var o = new Order { id = G.seqOrder++, dest = 0, units = 1, reward = 26 };
            o.items[first] = 1;
            G.orders.Add(o);
            Go(LAYOUT.playerStart); RunFor(0.2);
            Assert.AreSame(o, G.tables[0].order);

            // 이송: 필요한 만큼만 집는다
            Go(S.BinOut(b));
            RunFor(1);
            Assert.AreEqual(1, p.hand.Count);
            Assert.AreEqual(HandKind.Pick, p.hand[0].kind);
            Assert.AreEqual(0, S.OutstandingNeed(first));
            AssertInvariants();

            // 전달 → 포장 → 같은 지점에서 박스 집기
            Go(LAYOUT.playerStart); RunFor(0.2);
            Go(S.TableIn(G.tables[0]));
            RunFor(2.5);
            Assert.IsNull(G.tables[0].order);
            Assert.AreEqual(0, G.tables[0].boxes.Count);
            Assert.AreEqual(HandKind.Box, p.hand.Single().kind);
            var box = p.hand[0].box;
            Assert.AreEqual(1, box.units);

            // 출고분류: 목적지 도크에 내려놓기
            Go(LAYOUT.playerStart); RunFor(0.2);
            Go(S.DockIn(G.docks[0]));
            RunFor(1);
            Assert.AreSame(box, G.docks[0].waiting.Single());
            Assert.AreEqual(0, p.hand.Count);
            AssertInvariants();
        }

        [Test]
        public void Player_ReturnsExtraPickAndIdleTexts()
        {
            var p = G.player;
            var b = G.bins[0];
            b.checkedCount = 3;
            // 주문 없음 → 집지 않음
            Go(S.BinOut(b));
            RunFor(1);
            Assert.AreEqual(0, p.hand.Count);
            Assert.AreEqual("할 일 없음", p.action);
            // 손에 남은 이송품 → 필요 없으면 반납
            p.hand.Add(new HandEntry { kind = HandKind.Pick, item = 0 });
            RunFor(0.5);
            Assert.AreEqual(0, p.hand.Count);
            Assert.AreEqual(4, b.checkedCount);
            // 하차장: 빈손 진입, 비어 있음
            Go(LAYOUT.playerStart); RunFor(0.2);
            Go(S.StagingOut()); RunFor(0.5);
            Assert.AreEqual("하차장이 비었습니다", p.action);
        }

        [Test]
        public void Invariants_DetectBrokenCounters()
        {
            AssertInvariants();
            G.bins[0].reservedPick = 1;
            G.bins[1].incomingRaw = 2;
            G.bins[2].checkedCount = -1;
            var errs = Invariants.Check();
            Assert.That(errs, Has.Some.StartsWith("bin0 reservedPick"));
            Assert.That(errs, Has.Some.StartsWith("bin1 incomingRaw"));
            Assert.That(errs, Has.Some.EqualTo("bin2 negative"));
            G.bins[0].reservedPick = 0; G.bins[1].incomingRaw = 0; G.bins[2].checkedCount = 0;
            G.bins[0].checkedCount = 21;
            Assert.That(Invariants.Check(), Has.Some.EqualTo("bin0 over capacity"));
        }

        [Test]
        public void Invariants_HoldOverLongRun()
        {
            // 플레이어가 하차장 ↔ 보관함을 오가며 10분
            var p = G.player;
            for (int lap = 0; lap < 60; lap++)
            {
                Go(S.InTruckOut(G.inTrucks[0])); RunFor(1.5);
                Go(S.StagingIn()); RunFor(1.5);
                Go(LAYOUT.playerStart); RunFor(0.2);
                Go(S.StagingOut()); RunFor(1.5);
                foreach (var e in p.hand.ToList()) { Go(LAYOUT.playerStart); RunFor(0.2); Go(S.BinIn(G.bins[e.item])); RunFor(1); }
                Go(LAYOUT.playerStart); RunFor(0.2);
                Go(S.BinInspect(G.bins[lap % 3])); RunFor(3);
                if (p.hand.Count > 0) { Go(LAYOUT.playerStart); RunFor(0.2); Go(S.TrashIn()); RunFor(1.5); }
                AssertInvariants();
            }
            Assert.That(G.bins.Sum(b => b.checkedCount), Is.GreaterThan(0));
        }
    }
}
