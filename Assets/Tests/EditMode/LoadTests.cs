// 5단계: 상차 정산 (원본 loading-logic.js). 기대값은 원본을 Node로 직접 실행해 뽑았다
using System.Collections.Generic;
using System.Linq;
using APC.EditorTools;
using NUnit.Framework;
using UnityEngine;
using static APC.Game;

namespace APC.Tests
{
    public class LoadTests
    {
        ApcConfig cfg;

        [SetUp]
        public void SetUp()
        {
            cfg = ScriptableObject.CreateInstance<ApcConfig>();
            Setup(cfg, DefaultLayout.Make(cfg), seed: 7);
        }

        [TearDown]
        public void TearDown()
        {
            ClearListeners();
            Object.DestroyImmediate(cfg);
        }

        static List<Vector2Int> C(params int[] xy) =>
            Enumerable.Range(0, xy.Length / 2).Select(i => new Vector2Int(xy[i * 2], xy[i * 2 + 1])).ToList();

        static OutTruck Truck(int w, int h, int[] locked, int[] bonus, string mode = "manual", params Objective[] objs) => new OutTruck
        {
            w = w, h = h, locked = new HashSet<Vector2Int>(C(locked)), bonus = new HashSet<Vector2Int>(C(bonus)),
            mode = mode, objectives = objs.ToList(), state = TruckState.Present,
        };

        static Box Box(int reward, string[] types, int[] cells, params int[] items)
        {
            var b = new Box { reward = reward, types = types.ToList(), primary = types[0], cells = C(cells) };
            for (int i = 0; i < items.Length; i += 2) b.items[items[i]] = items[i + 1];
            return b;
        }

        static void Load(OutTruck t, Box b) => t.placements.Add(new Placement { box = b, cells = b.cells });

        [Test]
        public void FillMult_MatchesOriginal()
        {
            var cases = new (double rate, double mult)[]
            {
                (0, 0.5), (50, 0.75), (99.9, 0.9995), (100, 1), (120, 1), (149.99, 1),
                (150, 1.2), (199, 1.2), (200, 1.5), (215, 1.65), (237.5, 1.875),
            };
            foreach (var (rate, mult) in cases) Assert.AreEqual(mult, LoadLogic.FillMult(rate), 1e-9, $"rate {rate}");
        }

        [Test]
        public void RankMult_MatchesOriginal()
        {
            double RM(IEnumerable<Box> bs) => LoadLogic.RankMult(LoadLogic.Ranks(bs.ToList()));
            Assert.AreEqual(1, RM(new Box[0]), 1e-9);
            Assert.AreEqual(1.02, RM(Enumerable.Range(0, 2).Select(_ => Box(1, new[] { "food" }, new[] { 0, 0 }))), 1e-9);
            Assert.AreEqual(1.08, RM(Enumerable.Range(0, 5).Select(_ => Box(1, new[] { "food", "daily" }, new[] { 0, 0 }))), 1e-9);
            Assert.AreEqual(1.1, RM(Enumerable.Range(0, 11).Select(_ => Box(1, new[] { "book" }, new[] { 0, 0 }))), 1e-9);
        }

        [Test]
        public void CanPlace_WithCompression_MatchesOriginal()
        {
            var t = Truck(5, 4, new[] { 0, 0, 4, 3 }, new int[0]);
            Load(t, Box(10, new[] { "food" }, new[] { 1, 0, 2, 0, 1, 1 }));
            Load(t, Box(10, new[] { "food" }, new[] { 3, 0 }));
            // 빈 칸, 잠김, 밖, 1칸 겹침, 3칸 겹침, 2칸 겹침, 자기 자신 무시
            var tries = new[] { C(0, 1, 0, 2), C(0, 0), C(5, 0), C(1, 1, 1, 2), C(1, 0, 2, 0, 3, 0), C(2, 0, 3, 0), C(1, 0, 2, 0, 1, 1) };
            var expect = new Dictionary<int, bool[]>
            {
                [0] = new[] { true, false, false, false, false, false, true },
                [1] = new[] { true, false, false, true, false, false, true },
                [2] = new[] { true, false, false, true, false, true, true },
            };
            foreach (var kv in expect)
            {
                G.loadCompress = kv.Key;
                for (int i = 0; i < tries.Length; i++)
                    Assert.AreEqual(kv.Value[i], LoadLogic.CanPlace(t, tries[i], i == 6 ? t.placements[0] : null), $"compress {kv.Key} case {i}");
            }
        }

        static void Check(Payout p, double rate, int baseR, double fm, double rm, int total, int filled, int cells, int bonusCovered, int objBonus)
        {
            Assert.AreEqual(rate, p.rate, 1e-9, "rate");
            Assert.AreEqual(baseR, p.baseReward, "base");
            Assert.AreEqual(fm, p.fillMult, 1e-9, "fillMult");
            Assert.AreEqual(rm, p.rankMult, 1e-9, "rankMult");
            Assert.AreEqual(total, p.total, "total");
            Assert.AreEqual(filled, p.fill.filled, "filled");
            Assert.AreEqual(cells, p.fill.total, "cells");
            Assert.AreEqual(bonusCovered, p.fill.bonusCovered, "bonusCovered");
            Assert.AreEqual(objBonus, p.objBonus, "objBonus");
        }

        [Test]
        public void Payout_NoObjectives_BonusCell()
        {
            var t = Truck(5, 4, new[] { 0, 0 }, new[] { 1, 0, 4, 3 });
            Load(t, Box(120, new[] { "food" }, new[] { 1, 0, 2, 0, 1, 1 }, 0, 2));
            Load(t, Box(80, new[] { "daily" }, new[] { 3, 0, 3, 1 }, 5, 1));
            Check(LoadLogic.Payout(t), 57.63157894736842, 200, 0.7881578947368422, 1, 158, 5, 19, 1, 0);
        }

        [Test]
        public void Payout_MixedObjectives()
        {
            var t = Truck(5, 4, new[] { 4, 0, 4, 1 }, new int[0], "manual",
                new Objective { kind = "fill", rate = 50, bonus = 10 },
                new Objective { kind = "boxes", n = 4, bonus = 15 },
                new Objective { kind = "item", item = 0, bonus = 10 },
                new Objective { kind = "kinds", n = 2, bonus = 10 },
                new Objective { kind = "rank", type = "food", rank = 2, bonus = 5 },
                new Objective { kind = "item", item = 7, bonus = 10 });
            Load(t, Box(100, new[] { "food" }, new[] { 0, 0, 1, 0, 2, 0, 3, 0 }, 0, 1, 1, 2));
            Load(t, Box(90, new[] { "food", "daily" }, new[] { 0, 1, 1, 1, 2, 1, 3, 1 }, 1, 1, 5, 1));
            Load(t, Box(70, new[] { "cloth" }, new[] { 0, 2, 1, 2, 0, 3, 1, 3 }));
            var p = LoadLogic.Payout(t);
            Check(p, 163.33333333333331, 260, 1.2, 1.02, 318, 12, 18, 0, 30);
            CollectionAssert.AreEqual(new[] { true, false, true, true, false, false }, p.objectives.Select(o => o.done));
            CollectionAssert.AreEqual(new[] { "133%", "3/4", "1개", "3/2", "D", "0개" }, p.objectives.Select(o => o.now));
        }

        [Test]
        public void Payout_ObjectiveBonusAboveMaxRate()
        {
            var t = Truck(2, 2, new int[0], new int[0], "manual", new Objective { kind = "fill", rate = 100, bonus = 20 });
            Load(t, Box(55, new[] { "book" }, new[] { 0, 0, 1, 0, 0, 1, 1, 1 }));
            Check(LoadLogic.Payout(t), 220, 55, 1.7, 1, 94, 4, 4, 0, 20);
        }

        [Test]
        public void Payout_CompressedOverlap()
        {
            G.loadCompress = 2;
            var t = Truck(3, 3, new[] { 2, 2 }, new[] { 0, 0 });
            Load(t, Box(40, new[] { "elec" }, new[] { 0, 0, 1, 0, 2, 0 }));
            Load(t, Box(40, new[] { "elec" }, new[] { 0, 0, 1, 0, 1, 1 }));
            var p = LoadLogic.Payout(t);
            Check(p, 105, 80, 1, 1.02, 82, 4, 8, 1, 0);
            Assert.AreEqual(2, p.fill.overlap);
        }

        [Test]
        public void Payout_AutoIgnoresObjectives()
        {
            var t = Truck(5, 5, new[] { 0, 0, 1, 1 }, new int[0], "auto", new Objective { kind = "fill", rate = 10, bonus = 99 });
            t.autoCells = 14;
            t.autoBoxes = new List<Box> { Box(60, new[] { "food" }, new int[0]), Box(60, new[] { "food" }, new int[0]), Box(30, new[] { "daily" }, new int[0]) };
            var p = LoadLogic.Payout(t);
            Check(p, 121.73913043478262, 150, 1, 1.02, 153, 14, 23, 0, 0);
            Assert.IsEmpty(p.objectives);
        }

        [Test]
        public void Rotate_ClockwiseNormalized()
        {
            // L자: (0,0)(0,1)(1,1) → 시계 90도
            CollectionAssert.AreEquivalent(C(1, 0, 0, 0, 0, 1), LoadLogic.Rotate(C(0, 0, 0, 1, 1, 1)));
        }

        [Test]
        public void Loading_PlaceMoveRemoveResetClose()
        {
            RunTruck();
            var d = G.docks[0];
            var b1 = Box(50, new[] { "food" }, new[] { 0, 0, 1, 0 });
            var b2 = Box(30, new[] { "daily" }, new[] { 0, 0 });
            d.waiting.AddRange(new[] { b1, b2 });
            Assert.IsTrue(Loading.Open(d));
            Assert.IsTrue(G.paused);

            Loading.Place(b1, C(0, 0, 1, 0), null);
            Assert.AreEqual("manual", d.truck.mode);
            Assert.AreEqual(1, d.waiting.Count);
            var pl = d.truck.placements[0];
            Loading.Place(b1, C(0, 1, 1, 1), pl);   // 옮기기
            Assert.AreEqual(1, d.truck.placements.Count);
            CollectionAssert.AreEqual(C(0, 1, 1, 1), pl.cells);

            Loading.Remove(pl);
            Assert.AreEqual(2, d.waiting.Count);
            Loading.Place(b1, C(0, 0, 1, 0), null);
            Loading.Place(b2, C(2, 0), null);
            Loading.ResetAll();
            Assert.IsEmpty(d.truck.placements);
            Assert.AreEqual(2, d.waiting.Count);

            Loading.Close();
            Assert.IsNull(Loading.dock);
            Assert.IsFalse(G.paused);
            Assert.IsNull(d.truck.mode);   // 아무것도 안 싣고 닫으면 수동 표시 해제
        }

        [Test]
        public void Loading_DepartClosesPanel()
        {
            RunTruck();
            var d = G.docks[0];
            var b = Box(50, new[] { "food" }, new[] { 0, 0 });
            d.waiting.Add(b);
            Loading.Open(d);
            Loading.Depart();   // 실은 박스가 없으면 아무 일 없음
            Assert.AreSame(d, Loading.dock);
            var cell = Enumerable.Range(0, d.truck.w * d.truck.h).Select(i => new Vector2Int(i % d.truck.w, i / d.truck.w)).First(c => !d.truck.locked.Contains(c));
            Loading.Place(b, new List<Vector2Int> { cell }, null);
            int money = G.money;
            Loading.Depart();
            Assert.IsNull(Loading.dock);
            Assert.IsFalse(G.paused);
            Assert.Greater(G.money, money);
            Assert.AreEqual(TruckState.Leaving, d.truck.state);
        }

        [Test]
        public void Loading_CannotDepartWithInvalidPlacement()
        {
            RunTruck();
            var d = G.docks[0];
            var b = Box(50, new[] { "food" }, new[] { 0, 0 });
            d.waiting.Add(b);
            var b2 = Box(30, new[] { "daily" }, new[] { 0, 0 });
            d.waiting.Add(b2);
            G.loadCompress = 0;
            Loading.Open(d);
            Loading.Place(b, new List<Vector2Int> { new Vector2Int(-1, 0) }, null);   // 짐칸 밖에는 놓이지 않는다
            Assert.IsEmpty(d.truck.placements);
            Assert.AreEqual(2, d.waiting.Count);
            Loading.Place(b, C(0, 0), null);
            Loading.Place(b2, C(0, 0), null);   // 겹치기는 한도를 넘어도 일단 놓인다
            Assert.AreEqual(2, LoadLogic.Invalid(d.truck).Count);
            Loading.Depart();
            Assert.AreSame(d, Loading.dock);
            Assert.AreEqual(TruckState.Present, d.truck.state);
        }

        [Test]
        public void Loading_CannotOpenWithoutTruck()
        {
            var toasts = new List<string>();
            Toast += (i, t) => toasts.Add(t);
            Assert.IsFalse(Loading.Open(G.docks[0]));
            Assert.IsNull(Loading.dock);
            CollectionAssert.Contains(toasts, "지금은 상차 패널을 열 수 없습니다");
        }

        [Test]
        public void ObjectiveText_MatchesOriginal()
        {
            Assert.AreEqual("🍱 식품 랭크 C 이상", LoadLogic.ObjectiveText(new Objective { kind = "rank", type = "food", rank = 2 }));
            Assert.AreEqual("적재율 120% 이상", LoadLogic.ObjectiveText(new Objective { kind = "fill", rate = 120 }));
            Assert.AreEqual("박스 3개 이상 싣기", LoadLogic.ObjectiveText(new Objective { kind = "boxes", n = 3 }));
            Assert.AreEqual("서로 다른 타입 2종 이상", LoadLogic.ObjectiveText(new Objective { kind = "kinds", n = 2 }));
            StringAssert.EndsWith("든 박스 싣기", LoadLogic.ObjectiveText(new Objective { kind = "item", item = 0 }));
        }

        // 첫 도크 트럭이 도착할 때까지
        static void RunTruck()
        {
            Sim.Start();
            for (int i = 0; i < 60 && G.docks[0].truck?.state != TruckState.Present; i++) Sim.Update(Sim.STEP);
            Assert.AreEqual(TruckState.Present, G.docks[0].truck.state);
            G.docks[0].truck.locked.Clear();   // 랜덤 잠긴 칸에 걸리면 Place가 거부한다
        }
    }
}
