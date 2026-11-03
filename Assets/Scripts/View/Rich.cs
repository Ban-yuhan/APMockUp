// TMP 리치 텍스트 조각 (원본 U.money, U.bar, <small>)
using UnityEngine;

namespace APC
{
    public static class Rich
    {
        public const string SOFT = "#5b6775", GOOD = "#2e9e57", ACCENT = "#1e5aa8";

        public static string Money(double n) => "💰" + U.Money(n);
        // size를 안 주면 바탕 글자의 85% (패널·라벨 글자 크기가 달라도 같은 비율)
        public static string Small(string s, int size = 0, string color = SOFT) => $"<size={(size > 0 ? size.ToString() : "85%")}><color={color}>{s}</color></size>";
        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // 알바 얼굴 (원본 UI.albaFace)
        public static string Face(Alba a) =>
            a.resting ? "💤" : a.unpaid ? "😡" : a.fx > 0 ? "✨" : a.wantRest ? "😩" : a.mt < 0 ? "😞" : "🙂";

        // MT 글자 + 막대 (원본 UI.mtBar). panel = 패널(밝은 바탕)용 글자색, 아니면 라벨용
        public static string Mt(double mt, bool panel)
        {
            var c = Game.CONFIG.mt;
            double r = (mt - c.min) / (c.max - c.min);
            int k = mt < -50 ? 0 : mt < 0 ? 1 : mt < 50 ? 2 : 3;
            string[] text = panel ? new[] { "#d63031", "#e65100", "#b8860b", GOOD } : new[] { "#ff8a80", "#ffab91", "#ffe082", "#a5d6a7" };
            string[] bar = { "#d63031", "#ff7f50", "#e6a100", GOOD };
            return $"<size=90%><b><color={text[k]}>MT {U.Round(mt)}</color></b></size>{Bar(r, bar[k])}";
        }

        // shortcut: 원본 막대(span.bar)를 글자 10개로 그린다. 진짜 막대가 필요하면 Image로 바꾼다
        public static string Bar(double ratio, string color = "#4caf50")
        {
            int n = Mathf.RoundToInt(Mathf.Clamp01((float)ratio) * 10);
            // 맑은고딕에 █가 없어서 이어지는 굵은 가로선 ━을 쓴다
            return $" <size=70%><color={color}>{new string('━', n)}</color><color=#0000002e>{new string('━', 10 - n)}</color></size>";
        }
    }
}
