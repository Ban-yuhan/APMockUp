// 로직 공용 컨텍스트: 원본 전역 CONFIG / LAYOUT / G 자리. 로직 → 뷰 알림은 이벤트로 보낸다
using System;

namespace APC
{
    public static class Game
    {
        public static ApcConfig CONFIG;
        public static LayoutData LAYOUT;
        public static GameState G;

        // 원본 UI.toast (icon = 아이콘 키, 원본 문구 앞의 이모지)
        public static event Action<string, string> Toast;
        public static void Notify(string icon, string text) => Toast?.Invoke(icon, text);

        // seed: 테스트용 난수 시드 (null = 무작위)
        public static void Setup(ApcConfig config, LayoutData layout, int? seed = null)
        {
            CONFIG = config;
            LAYOUT = layout.Prepare();
            U.Seed(seed);
            G = GameState.Create();
            Loading.dock = null;
        }

        public static void ClearListeners() => Toast = null;
    }
}
