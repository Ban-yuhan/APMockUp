// 6단계: 구매·업그레이드 (원본 ui.js Shop)
using System.Collections.Generic;
using System.Linq;
using APC.EditorTools;
using NUnit.Framework;
using UnityEngine;
using static APC.Game;

namespace APC.Tests
{
    public class ShopTests
    {
        ApcConfig cfg;
        List<string> toasts;

        [SetUp]
        public void SetUp()
        {
            cfg = ScriptableObject.CreateInstance<ApcConfig>();
            Setup(cfg, DefaultLayout.Make(cfg), seed: 3);
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
        public void BuyBin_SpendsAndUnlocks()
        {
            var b = G.bins.First(x => x.slot != null && !x.unlocked);
            G.money = b.slot.cost - 1;
            Shop.BuyBin(b.idx);
            Assert.IsFalse(b.unlocked);
            Assert.AreEqual("💸 돈이 부족합니다", toasts.Last());

            G.money = b.slot.cost + 5;
            Shop.BuyBin(b.idx);
            Assert.IsTrue(b.unlocked);
            Assert.AreEqual(5, G.money);
            StringAssert.EndsWith("보관함 해금! 이제 입고 트럭에 실려 옵니다", toasts.Last());
            Shop.BuyBin(b.idx);   // 이미 해금
            Assert.AreEqual(5, G.money);
        }

        [Test]
        public void BuyTableAndDock()
        {
            G.money = 100000;
            var t = G.tables.First(x => !x.unlocked);
            Shop.BuyTable(t.idx);
            Assert.IsTrue(t.unlocked);
            Assert.AreEqual($"🎁 포장대 {t.idx + 1} 해금", toasts.Last());

            var d = G.docks.First(x => x.slot != null && !x.unlocked);
            Shop.BuyDock(d.idx);
            Assert.IsTrue(d.unlocked);
            Assert.AreEqual(1, d.truckTimer);
            Assert.AreEqual(100000 - t.slot.cost - d.slot.cost, G.money);
        }

        [Test]
        public void Care_RestAndPurifier()
        {
            G.money = 100000;
            Shop.UpPurifier();
            Assert.AreEqual(0, G.carePurifier);
            Assert.AreEqual("🌬️ 공기청정기 → 낡은 (발동 10%)", toasts.Last());
            Assert.AreEqual(100000 - 300, G.money);
            for (int i = 0; i < 5; i++) Shop.UpRest();
            Assert.AreEqual(3, G.careRest);   // 최고 티어에서 멈춤
            Assert.AreEqual("🛋️ 휴게실 → 최고급 휴게실", toasts.Last());
        }

        [Test]
        public void UpStage_ToAutomation()
        {
            G.money = 100000;
            G.albas.Add(new Alba { id = 1, stage = "sort" });
            Shop.UpStage("sort");
            Assert.AreEqual("⬆️ 분류 Lv2 (배치 슬롯 2명)", toasts.Last());
            Shop.UpStage("sort");
            Shop.UpStage("sort");
            Assert.IsTrue(S.IsAuto("sort"));
            Assert.IsNull(G.albas[0].stage);
            Assert.AreEqual("🤖 분류 자동화 완료! 배치된 알바는 대기중 슬롯으로 이동", toasts.Last());
            int money = G.money;
            Shop.UpStage("sort");   // 최대
            Assert.AreEqual(money, G.money);
        }

        [Test]
        public void UpFacilityAndLoad()
        {
            G.money = 100000;
            Shop.UpFacility("staging");
            Assert.AreEqual(45, S.StagingCap());
            Assert.AreEqual("📥 하차장 크기 → 45칸", toasts.Last());
            Assert.AreEqual(100000 - 200, G.money);
            Shop.UpFacility("parking");
            Assert.AreEqual(2, S.ParkingCount());

            Shop.UpLoad("compress");
            Shop.UpLoad("bonus");
            Shop.UpLoad("bonus");
            Assert.AreEqual(1, G.loadCompress);
            Assert.AreEqual(2, G.loadBonus);
            Assert.AreEqual("⭐ 우선 적재 칸 Lv2", toasts.Last());
        }
    }
}
