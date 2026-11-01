// 7단계: 알바 고용/배치/계획 AI/MT/시급 (원본 alba.js), 알바만으로 공정이 돌 때 불변식
using System.Collections.Generic;
using System.Linq;
using APC.EditorTools;
using NUnit.Framework;
using UnityEngine;
using static APC.Game;

namespace APC.Tests
{
    public class AlbaTests
    {
        ApcConfig cfg;
        List<string> toasts;

        static readonly string[] STAGES = { "unload", "sort", "inspect", "transfer", "pack", "outbound", "load" };

        [SetUp]
        public void SetUp()
        {
            cfg = ScriptableObject.CreateInstance<ApcConfig>();
            Setup(cfg, DefaultLayout.Make(cfg), seed: 11);
            Sim.Start();
            Sim.input = default;
            toasts = new List<string>();
            Toast += (i, t) => toasts.Add(i + " " + t);
        }

        [TearDown]
        public void TearDown()
        {
            ClearListeners();
            Object.DestroyImmediate(cfg);
        }

        static Alba HireOne()
        {
            if (G.candidates.Count == 0) Albas.RerollCandidates();
            return Albas.Hire(0);
        }

        [Test]
        public void Candidates_WageFormula()
        {
            Assert.AreEqual(3, G.candidates.Count);
            foreach (var c in G.candidates)
            {
                Assert.That(c.str, Is.InRange(1, 5));
                double baseW = 2 + 0.5 * (c.str + c.agi + c.intel);
                Assert.That(c.wage, Is.InRange(U.Round(baseW), U.Round(baseW + 2)));
            }
        }

        [Test]
        public void Hire_Assign_Fire()
        {
            var a = HireOne();
            Assert.AreEqual(1, a.id);
            Assert.AreEqual(2, G.candidates.Count);
            Assert.AreEqual(cfg.mt.start, a.mt);
            Assert.IsTrue(Albas.Assign(a, "sort"));
            var b = HireOne();
            Assert.IsFalse(Albas.Assign(b, "sort"));   // Lv1 = 슬롯 1명
            Assert.AreEqual(" 배치 슬롯이 부족합니다", toasts.Last());
            G.stages["sort"] = 4;
            Assert.IsFalse(Albas.Assign(b, "sort"));   // 자동화 공정은 배치 불가

            a.hand.Add(new HandEntry { kind = HandKind.Raw, item = 0, reserved = true });
            G.bins[0].incomingRaw++;
            int staging = G.staging.Count;
            Albas.Fire(a);
            Assert.AreEqual(staging + 1, G.staging.Count);   // 든 물건은 하차장으로
            Assert.AreEqual(0, G.bins[0].incomingRaw);
            Assert.AreEqual("👋 " + a.name + " 해고", toasts.Last());
            Assert.IsFalse(G.albas.Contains(a));
        }

        [Test]
        public void StatEffects()
        {
            var a = new Alba { str = 3, agi = 2, intel = 4, mt = 50, skLift = 65, skCarry = 400, skPack = 50 };
            Assert.AreEqual(6, Albas.Capacity(a));           // 1 + 3 + floor(65/30)
            Assert.AreEqual(4.0 * 1.5 * S.MtMult(50), Albas.Speed(a), 1e-9);
            Assert.AreEqual((0.6 + 0.48) * (1 + 50.0 / 150) * S.MtMult(50), Albas.WorkMult(a, "pack"), 1e-9);
            Assert.AreEqual(0.6 + 0.3 * (0.6 * 3 / 4 + 0.4 * 0.5), Albas.LoadTarget(a), 1e-9);
            Albas.Gain(a, "carry");
            Assert.AreEqual(401, a.skCarry, 1e-9);           // 0.5 + 0.25 x agi
        }

        [Test]
        public void PhaseChange_PayUnpaidFlee()
        {
            var a = HireOne();
            int pay = Albas.ShiftPay(a.wage);
            G.money = pay;
            Albas.OnPhaseChange();
            Assert.AreEqual(0, G.money);
            Assert.IsFalse(a.unpaid);
            StringAssert.StartsWith("💸 시급", toasts[toasts.Count - 1]);
            Assert.AreEqual(3, G.candidates.Count);   // 지원자 새로

            Albas.OnPhaseChange();
            Assert.IsTrue(a.unpaid);
            StringAssert.StartsWith("😡 시급 미지급", toasts.Last());
            for (int i = 0; i < 20 && G.albas.Contains(a); i++) Albas.OnPhaseChange();
            Assert.IsFalse(G.albas.Contains(a));     // 결국 도망
            Assert.IsTrue(toasts.Any(t => t.Contains("도망갔습니다")));
        }

        [Test]
        public void Feed_RaisesMt()
        {
            var a = HireOne();
            a.mt = 95;
            Albas.Feed(a, cfg.snacks[1]);
            Assert.AreEqual(100, a.mt);
            Assert.AreEqual(1, a.fx);
        }

        [Test]
        public void Rest_WhenWanted()
        {
            var a = HireOne();
            a.wantRest = true;
            a.mt = 0;
            for (int i = 0; i < 400 && a.wantRest; i++) Sim.Update(Sim.STEP);
            Assert.IsFalse(a.wantRest);
            Assert.That(a.mt, Is.GreaterThanOrEqualTo(cfg.care.rest.until[0]));
            Assert.AreEqual("휴식 중", a.status);
        }

        // 공정마다 알바 1명씩: 플레이어 없이 입고부터 출고까지 돌고, 불변식이 매초 유지된다
        [Test]
        public void AllStages_RunByAlbas_InvariantsHold()
        {
            G.money = 100000;
            foreach (var st in STAGES)
            {
                var a = HireOne();
                a.mt = 100;
                Assert.IsTrue(Albas.Assign(a, st));
            }
            G.player.x = LAYOUT.rest.x; G.player.z = LAYOUT.rest.z;   // 플레이어는 범위 밖
            var seen = new HashSet<string>();
            for (int sec = 0; sec < 300; sec++)
            {
                for (int k = 0; k < 20; k++) Sim.Update(Sim.STEP);
                foreach (var a in G.albas) seen.Add(a.stage + ":" + a.status);
                var errs = Invariants.Check();
                Assert.IsEmpty(errs, $"t={sec}s " + string.Join("; ", errs));
            }
            Assert.Greater(G.trucks, 0, "알바 상차로 출고 트럭이 떠나야 한다");
            Assert.Greater(G.shipped, 0);
            foreach (var expect in new[] { "unload:하차 중", "sort:하차장에서 집기", "inspect:", "transfer:주문 물품 집기", "pack:포장대", "outbound:박스 집기", "load:" })
                Assert.IsTrue(seen.Any(s => s.StartsWith(expect)), "안 보인 상태: " + expect);
        }

        [Test]
        public void UpStage_Automation_ResetsAlba()
        {
            G.money = 100000;
            var a = HireOne();
            Albas.Assign(a, "pack");
            a.hand.Add(new HandEntry { kind = HandKind.Pick, item = 0 });
            int chk = G.bins[0].checkedCount;
            Shop.UpStage("pack"); Shop.UpStage("pack"); Shop.UpStage("pack");
            Assert.IsNull(a.stage);
            Assert.IsEmpty(a.hand);
            Assert.AreEqual(chk + 1, G.bins[0].checkedCount);
        }
    }
}
