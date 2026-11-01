// 9단계: 명절 이벤트 (원본 main.js startEvent/endEvent), 디버그 명령 (원본 debug.js)
using System.Collections.Generic;
using System.Linq;
using APC.EditorTools;
using NUnit.Framework;
using UnityEngine;
using static APC.Game;

namespace APC.Tests
{
    public class EventDebugTests
    {
        ApcConfig cfg;
        List<string> toasts;

        [SetUp]
        public void SetUp()
        {
            cfg = ScriptableObject.CreateInstance<ApcConfig>();
            Setup(cfg, DefaultLayout.Make(cfg), seed: 9);
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

        [Test]
        public void Event_RushOrdersAndFasterInterval_EndsOnNextPhase()
        {
            int before = G.orders.Count;
            Sim.StartEvent();
            Assert.AreEqual(cfg.evt.name, G.eventName);
            Assert.AreEqual(before + cfg.evt.rushOrders, G.orders.Count);
            Assert.AreEqual($"🎉 {cfg.evt.name} 주문서 10개가 한꺼번에 들어왔습니다!", toasts.Last());
            G.orderTimer = 0.01;
            Orders.Update(0.05);
            Assert.AreEqual(cfg.orders.interval * 0.5, G.orderTimer, 1e-9);   // 다음 주문 간격 절반

            Sim.SwitchPhase();
            Assert.IsNull(G.eventName);
            Assert.IsTrue(toasts.Contains($"🎉 {cfg.evt.name} 종료"));
        }

        [Test]
        public void Event_ChanceOnPhaseChange()
        {
            int started = 0;
            for (int i = 0; i < 200; i++)
            {
                G.eventName = null;
                Sim.SwitchPhase();
                if (G.eventName != null) started++;
            }
            Assert.That(started / 200.0, Is.InRange(0.25, 0.45));   // chance 0.35
        }

        [Test]
        public void Debug_MoneyAndPhase()
        {
            DebugCmd.Run("money", "1000");
            Assert.AreEqual(1500, G.money);
            DebugCmd.Run("money-add", moneyInput: 1499);   // 1000 단위로 반올림
            Assert.AreEqual(2500, G.money);
            DebugCmd.Run("money-sub", moneyInput: 99999);
            Assert.AreEqual(0, G.money);

            var a = Albas.Hire(0);
            a.wage = 5;
            G.money = 1000;
            DebugCmd.Run("phase-only");   // 시급 판정 없음
            Assert.AreEqual("night", G.phase);
            Assert.AreEqual(1000, G.money);
            DebugCmd.Run("phase");        // 시급 지급 포함
            Assert.AreEqual("day", G.phase);
            Assert.AreEqual(2, G.day);
            Assert.AreEqual(1000 - 60, G.money);
        }

        [Test]
        public void Debug_UnlockOrdersTrucks()
        {
            DebugCmd.Run("unlock-all");
            Assert.IsTrue(G.bins.Where(b => b.slot != null).All(b => b.unlocked));
            Assert.IsTrue(G.tables.All(t => t.unlocked));
            Assert.IsTrue(G.docks.Where(d => d.slot != null).All(d => d.unlocked));
            Assert.AreEqual(0, G.carePurifier);

            DebugCmd.Run("orders", "5");
            Assert.AreEqual(5, G.orders.Count);
            Orders.AssignTables();
            DebugCmd.Run("orders-clear");
            Assert.IsTrue(G.orders.All(o => o.table >= 0));   // 포장대에 배정된 주문은 남는다
            Assert.IsNotEmpty(G.orders);

            DebugCmd.Run("out-trucks");
            Assert.IsTrue(G.docks.Where(d => d.unlocked && d.truck == null).All(d => d.truckTimer == 0));
        }

        [Test]
        public void Debug_AlbaFields()
        {
            var a = Albas.Hire(0);
            var b = Albas.Hire(0);
            DebugCmd.SetAlbaField(a, "str", "99");
            Assert.AreEqual(20, a.str);
            DebugCmd.SetAlbaField(a, "sk-pack", "3");
            Assert.AreEqual(30, a.skPack);
            DebugCmd.SetAlbaField(a, "mt", "-500");
            Assert.AreEqual(cfg.mt.min, a.mt);
            a.flee = 0.3;
            DebugCmd.SetAlbaField(a, "unpaid", "false");
            Assert.AreEqual(0, a.flee);
            DebugCmd.SetAlbaField(a, "wage", "abc");   // 숫자가 아니면 무시
            DebugCmd.Run("alba-copy", alba: a);
            Assert.AreEqual(20, b.str);
            Assert.AreEqual(30, b.skPack);
            DebugCmd.Run("mt", "-60");
            Assert.IsTrue(G.albas.All(x => x.mt == -60));
        }
    }
}
