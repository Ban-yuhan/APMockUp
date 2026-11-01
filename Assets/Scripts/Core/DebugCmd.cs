// 디버그 명령 (원본 js/debug.js의 Debug.run / setAlbaField). 목업 테스트용
using System;
using static APC.Game;

namespace APC
{
    public static class DebugCmd
    {
        // act: 원본 data-dbg 값. arg: data-arg (숫자 문자열)
        public static void Run(string act, string arg = null, Alba alba = null, int moneyInput = 0)
        {
            int n = int.TryParse(arg, out int v) ? v : 0;
            int step = CONFIG.debug.moneyStep;
            switch (act)
            {
                case "money": G.money += n; Notify("🛠️", $"돈 +{Rich(n)}"); break;
                case "money-add":
                case "money-sub":
                {
                    int m = (int)Math.Round((double)moneyInput / step) * step;
                    G.money = Math.Max(0, G.money + (act == "money-add" ? m : -m));
                    Notify("🛠️", $"돈 {(act == "money-add" ? "+" : "-")}{Rich(m)}");
                    break;
                }
                case "money-zero": G.money = 0; break;
                case "phase": G.phaseTime = 0; Sim.SwitchPhase(); break;
                case "phase-only":
                    G.phaseTime = 0;
                    Sim.TogglePhase();
                    Notify("🛠️", $"{(G.phase == "day" ? "낮" : "밤")}으로 전환 (시급·이벤트 판정 없음)");
                    break;
                case "hour": G.phaseTime += CONFIG.phaseSeconds / 12; break;
                case "event-on": if (G.eventName == null) Sim.StartEvent(); break;
                case "event-off": if (G.eventName != null) Sim.EndEvent(); break;
                case "orders": for (int i = 0; i < n; i++) Orders.Spawn(); break;
                case "orders-clear": G.orders.RemoveAll(o => o.table < 0); break;
                case "in-trucks":
                    foreach (var tr in G.inTrucks) if (tr.idx < S.ParkingCount() && tr.state == TruckState.Away) tr.timer = 0;
                    break;
                case "out-trucks":
                    foreach (var d in G.docks) if (d.unlocked && d.truck == null) d.truckTimer = 0;
                    break;
                case "mt": foreach (var a in G.albas) a.mt = n; break;
                case "alba-copy":
                    if (alba == null) break;
                    foreach (var a in G.albas)
                    {
                        if (a == alba) continue;
                        a.str = alba.str; a.agi = alba.agi; a.intel = alba.intel;
                        a.skLift = alba.skLift; a.skCarry = alba.skCarry; a.skPack = alba.skPack; a.skInspect = alba.skInspect;
                    }
                    Notify("🛠️", $"{alba.name}의 스탯·숙련도를 알바 {G.albas.Count - 1}명에게 적용");
                    break;
                case "reroll": Albas.RerollCandidates(); break;
                case "unlock-all":
                    foreach (var b in G.bins) if (b.slot != null) b.unlocked = true;
                    foreach (var t in G.tables) t.unlocked = true;
                    foreach (var d in G.docks) if (d.slot != null && !d.unlocked) { d.unlocked = true; d.truckTimer = 1; }
                    if (G.carePurifier < 0) G.carePurifier = 0;
                    break;
            }
        }

        static string Rich(int n) => "💰" + U.Money(n);

        // 디버그용 직접 수정 (스탯은 지원자 범위를 넘어 0~20까지 허용). 숙련도는 Lv로 받아 경험치 = Lv × 10
        public static void SetAlbaField(Alba a, string key, string value)
        {
            if (key == "unpaid") { a.unpaid = value == "true"; if (!a.unpaid) a.flee = 0; return; }
            if (!double.TryParse(value, out double n)) return;
            int lv = Math.Max(0, U.Round(n)) * 10;
            switch (key)
            {
                case "str": a.str = U.Clamp(U.Round(n), 0, 20); break;
                case "agi": a.agi = U.Clamp(U.Round(n), 0, 20); break;
                case "int": a.intel = U.Clamp(U.Round(n), 0, 20); break;
                case "mt": a.mt = U.Clamp(n, CONFIG.mt.min, CONFIG.mt.max); break;
                case "wage": a.wage = Math.Max(0, U.Round(n)); break;
                case "sk-lift": a.skLift = lv; break;
                case "sk-carry": a.skCarry = lv; break;
                case "sk-pack": a.skPack = lv; break;
                case "sk-inspect": a.skInspect = lv; break;
            }
        }
    }
}
