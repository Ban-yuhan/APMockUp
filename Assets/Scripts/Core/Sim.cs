// 고정 스텝 갱신과 낮/밤 전환 (원본 js/main.js의 Main.update / togglePhase / switchPhase)
using static APC.Game;

namespace APC
{
    public static class Sim
    {
        public const double STEP = 0.05;

        // 이번 프레임의 이동 입력 (원본은 매 스텝 UI.inputVector()를 읽는다. 프레임 안에서는 같은 값)
        public static P input;

        // 원본 Main.init 중 상태 준비: 플레이어 생성, 첫 입고 트럭은 바로 도착
        public static void Start()
        {
            Player.Init();
            var t0 = G.inTrucks[0];
            t0.state = TruckState.Present;
            t0.off = 0;
            t0.items = S.RollInboundItems();
            Albas.RerollCandidates();
        }

        // 원본 Main.frame의 고정 스텝 분할. dt = 실제 경과(최대 0.1초)
        public static void Frame(double dt)
        {
            if (G.paused || G.userPaused) return;
            double total = dt * G.speed;
            while (total > 1e-6)
            {
                double s = System.Math.Min(STEP, total);
                Update(s);
                total -= s;
            }
        }

        public static void Update(double dt)
        {
            G.time += dt;
            G.phaseTime += dt;
            if (G.phaseTime >= CONFIG.phaseSeconds)
            {
                G.phaseTime -= CONFIG.phaseSeconds;
                SwitchPhase();
            }
            Inbound.Update(dt);
            Orders.Update(dt);
            Docks.Update(dt);
            Player.Update(dt, input);
            Albas.Update(dt);
            Auto.Update(dt);
        }

        public static void TogglePhase()
        {
            G.phase = G.phase == "day" ? "night" : "day";
            if (G.phase == "day") G.day++;
        }

        public static void SwitchPhase()
        {
            TogglePhase();
            if (G.phase == "day") Notify("☀️", $"{G.day}일차 아침이 밝았습니다");
            else Notify("🌙", "밤이 되었습니다");
            if (G.eventName != null) EndEvent();
            else if (U.Random() < CONFIG.evt.chance) StartEvent();
            Albas.OnPhaseChange();
        }

        // 명절 물류 폭주: 주문서가 한꺼번에 들어오고, 다음 낮/밤 전환까지 주문 간격이 줄어든다
        public static void StartEvent()
        {
            G.eventName = CONFIG.evt.name;
            for (int i = 0; i < CONFIG.evt.rushOrders; i++) Orders.Spawn();
            Notify("🎉", $"{CONFIG.evt.name} 주문서 {CONFIG.evt.rushOrders}개가 한꺼번에 들어왔습니다!");
        }

        public static void EndEvent()
        {
            G.eventName = null;
            Notify("🎉", $"{CONFIG.evt.name} 종료");
        }
    }
}
