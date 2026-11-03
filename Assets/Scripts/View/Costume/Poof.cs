// 코스튬을 바꿀 때 "펑" 만화풍 연기: 흰 구름 구슬이 부풀며 퍼졌다 사라진다 + 짧은 팝 소리
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

namespace APC
{
    public static class Poof
    {
        // 연기가 가장 크게 가리는 시점. 이때 모델을 바꾼다
        public const float SwapDelay = 0.08f;

        static Material mat;
        static AudioSource audio;
        static AudioClip pop;

        public static void Play(Vector3 pos, float scale = 1, bool sound = false)
        {
            if (!mat) mat = Resources.Load<Material>("PoofSmoke");
            var root = new GameObject("Poof").transform;
            root.position = pos;
            for (int i = 0; i < 10; i++)
            {
                var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(puff.GetComponent<Collider>());
                var r = puff.GetComponent<MeshRenderer>();
                if (mat) r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                var t = puff.transform;
                t.SetParent(root, false);
                var dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.7f;
                t.localPosition = dir * 0.1f * scale;
                t.localScale = Vector3.zero;
                float size = Random.Range(0.35f, 0.6f) * scale;
                DOTween.Sequence().SetUpdate(true).SetLink(puff)
                    .Append(t.DOScale(size, 0.12f).SetEase(Ease.OutBack))
                    .Join(t.DOLocalMove(dir * 0.6f * scale, 0.45f).SetEase(Ease.OutQuad))
                    .Insert(0.22f, t.DOScale(0, 0.25f).SetEase(Ease.InQuad));
            }
            Object.Destroy(root.gameObject, 0.6f);
            if (sound) PlayPop();
        }

        static void PlayPop()
        {
            if (!audio)
            {
                var go = new GameObject("PoofAudio");
                Object.DontDestroyOnLoad(go);
                audio = go.AddComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.spatialBlend = 0;
            }
            if (!pop) pop = MakePop();
            audio.PlayOneShot(pop, 0.8f);
        }

        // 음 높이가 빠르게 내려가는 짧은 "뽁" + 바람 소리
        static AudioClip MakePop()
        {
            const int rate = 44100;
            int n = (int)(rate * 0.2f);
            var data = new float[n];
            var rnd = new System.Random(7);
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / rate;
                phase += 2 * System.Math.PI * (900 * System.Math.Exp(-t * 18) + 180) / rate;
                double tone = System.Math.Sin(phase) * System.Math.Exp(-t * 22);
                double noise = (rnd.NextDouble() * 2 - 1) * System.Math.Exp(-t * 45);
                data[i] = (float)(tone * 0.5 + noise * 0.25);
            }
            var clip = AudioClip.Create("Pop", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
