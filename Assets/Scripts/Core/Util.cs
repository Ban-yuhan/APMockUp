// 공용 유틸 (원본 js/util.js)
using System;
using System.Collections.Generic;
using UnityEngine;

namespace APC
{
    // 배치 좌표 (원본 그대로: x = 오른쪽, z = 아래쪽). Unity 좌표 변환은 CoordUtil에서만 한다
    [Serializable]
    public struct P
    {
        public double x, z;
        public P(double x, double z) { this.x = x; this.z = z; }
        public override string ToString() => $"({x:0.##}, {z:0.##})";
    }

    public static class U
    {
        // 난수: 시드 주입 가능 (테스트용). 원본의 Math.random() 자리
        static System.Random rng = new System.Random();
        public static void Seed(int? seed) => rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        public static double Random() => rng.NextDouble();

        public static double Rand(double a, double b) => a + Random() * (b - a);
        public static int RandInt(int a, int b) => (int)Math.Floor(a + Random() * (b - a + 1));
        public static T Pick<T>(IList<T> arr) => arr[(int)Math.Floor(Random() * arr.Count)];
        public static double Clamp(double v, double a, double b) => Math.Max(a, Math.Min(b, v));
        public static int Clamp(int v, int a, int b) => Math.Max(a, Math.Min(b, v));
        public static double Dist(P a, P b) => Hypot(a.x - b.x, a.z - b.z);
        public static double Hypot(double x, double z) => Math.Sqrt(x * x + z * z);

        // JS Math.round와 같게 (.5는 +방향). C# Math.Round는 은행가 반올림이라 쓰지 않는다
        public static int Round(double v) => (int)Math.Floor(v + 0.5);

        // entries 중 weight 비율로 하나 뽑기
        public static T Weighted<T>(IList<T> entries, Func<T, double> weight)
        {
            double total = 0;
            foreach (var e in entries) total += weight(e);
            double r = Random() * total;
            foreach (var e in entries)
            {
                r -= weight(e);
                if (r <= 0) return e;
            }
            return entries[entries.Count - 1];
        }

        public static List<T> Shuffle<T>(List<T> arr)
        {
            for (int i = arr.Count - 1; i > 0; i--)
            {
                int j = (int)Math.Floor(Random() * (i + 1));
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
            return arr;
        }

        // 금액 표기 (💰 아이콘은 뷰에서 붙인다)
        public static string Money(double n) => Round(n).ToString("N0");

        public static Color Hex(int c) => new Color32((byte)(c >> 16 & 255), (byte)(c >> 8 & 255), (byte)(c & 255), 255);
    }
}
