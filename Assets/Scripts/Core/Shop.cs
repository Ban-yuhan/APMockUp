// 구매·업그레이드 (원본 js/ui.js의 Shop). 해금 비용은 배치 데이터의 슬롯에 있다
using static APC.Game;

namespace APC
{
    public static class Shop
    {
        public static void BuyBin(int i)
        {
            var b = G.bins[i];
            var it = CONFIG.items[i];
            if (b.slot == null || b.unlocked || !S.Spend(b.slot.cost)) return;
            b.unlocked = true;
            Notify("📦", $"{it.emoji} {it.name} 보관함 해금! 이제 입고 트럭에 실려 옵니다");
        }

        public static void BuyTable(int i)
        {
            var t = G.tables[i];
            if (t.unlocked || !S.Spend(t.slot.cost)) return;
            t.unlocked = true;
            Notify("🎁", $"포장대 {i + 1} 해금");
        }

        public static void BuyDock(int i)
        {
            var d = G.docks[i];
            var dest = CONFIG.destinations[i];
            if (d.slot == null || d.unlocked || !S.Spend(d.slot.cost)) return;
            d.unlocked = true;
            d.truckTimer = 1;
            Notify("🏁", $"{dest.name} 출고 도크 해금! {dest.name}행 주문이 들어옵니다");
        }

        public static void UpRest()
        {
            int next = G.careRest + 1;
            var c = CONFIG.care;
            if (next >= c.tierNames.Length || !S.Spend(c.rest.costs[next])) return;
            G.careRest = next;
            Notify("🛋️", $"휴게실 → {c.tierNames[next]} 휴게실");
        }

        public static void UpPurifier()
        {
            int next = G.carePurifier + 1;
            var c = CONFIG.care;
            if (next >= c.tierNames.Length || !S.Spend(c.purifier.costs[next])) return;
            G.carePurifier = next;
            Notify("🌬️", $"공기청정기 → {c.tierNames[next]} (발동 {U.Round(c.purifier.chance[next] * 100)}%)");
        }

        public static void UpStage(string id)
        {
            var def = S.StageDef(id);
            int lv = G.stages[id];
            if (lv >= def.costs.Length || !S.Spend(def.costs[lv])) return;
            G.stages[id] = lv + 1;
            if (lv + 1 >= 4)
            {
                foreach (var a in G.albas) if (a.stage == id) { Albas.Reset(a); a.stage = null; }
                Notify("🤖", $"{def.name} 자동화 완료! 배치된 알바는 대기중 슬롯으로 이동");
            }
            else Notify("⬆️", $"{def.name} Lv{lv + 1} (배치 슬롯 {lv + 1}명)");
        }

        public static void UpFacility(string key)
        {
            var f = CONFIG.facility.Get(key);
            var values = S.FacValues(key);
            var costs = S.FacCosts(key);
            int lv = G.fac[key];
            if (lv + 1 >= values.Length || !S.Spend(costs[lv + 1])) return;
            G.fac[key] = lv + 1;
            Notify(f.emoji, $"{f.name} → {values[lv + 1]}{f.unit}");
        }

        // key: compress | bonus
        public static void UpLoad(string key)
        {
            var u = key == "compress" ? CONFIG.loading.upgrades.compress : CONFIG.loading.upgrades.bonus;
            int lv = key == "compress" ? G.loadCompress : G.loadBonus;
            if (lv >= u.max || !S.Spend(u.costs[lv])) return;
            if (key == "compress") G.loadCompress = lv + 1; else G.loadBonus = lv + 1;
            Notify(u.emoji, $"{u.name} Lv{lv + 1}");
        }
    }
}
