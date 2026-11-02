// 지게차 표시: 씬에 놓인 위치·방향으로 G.forklifts에 상태를 넣고, 매 프레임 상태를 따라간다. 탑승 중이면 플레이어를 좌석에 둔다
// 모델 루트에 바로 붙여도 된다 (처음 회전을 기억해 두고 yaw만 더한다)
// 소환 지게차(summon)는 처음엔 숨겨 두고, R로 꺼내고 넣을 때 펑 이펙트와 함께 커지고 작아진다
using DG.Tweening;
using UnityEngine;
using static APC.Game;

namespace APC
{
    [DefaultExecutionOrder(100)]   // CharacterView가 플레이어를 놓은 뒤 좌석으로 옮긴다
    public class ForkliftView : MonoBehaviour
    {
        [Tooltip("씬 배치 상태에서 포크(앞)가 향하는 월드 yaw. 선택하면 파란 화살표로 보인다")]
        [SerializeField] float forwardYaw;
        [Tooltip("탑승한 플레이어를 둘 자리 (자식 오브젝트). 비우면 차체 중앙 바닥")]
        [SerializeField] Transform seat;
        [Tooltip("비우면 씬에서 isPlayer인 CharacterView를 찾는다")]
        [SerializeField] CharacterView player;

        [Header("소환")]
        [Tooltip("켜면 처음엔 숨겨 두고 R로 플레이어 앞에 꺼내고 넣는다")]
        [SerializeField] bool summon;
        [Tooltip("꺼내고 넣을 때 터뜨릴 이펙트 (비우면 기본 연기)")]
        [SerializeField] ParticleSystem poof;

        [Header("파렛트")]
        [Tooltip("포크 위 파렛트 바닥 가운데 (자식 오브젝트). 비우면 차체 앞쪽 끝")]
        [SerializeField] Transform palletAnchor;
        [Tooltip("묶음(박스 2×2×2) 한 변 길이")]
        [SerializeField] float bundleEdge = 0.5f;

        [Header("주행")]
        [SerializeField] float maxSpeed = 5, reverseSpeed = 3, accel = 4, brake = 10;
        [Tooltip("최고 속도일 때 회전 속도 (도/초). 속도에 비례해서 줄어든다")]
        [SerializeField] float turnRate = 90;
        [Tooltip("좌우만 누를 때 앞바퀴 축 제자리 회전 속도 (도/초)")]
        [SerializeField] float pivotRate = 60;

        [Header("차체 (길이·폭은 렌더러 크기로 잰다)")]
        [Tooltip("차체 중심 → 앞차축 거리 / 길이")]
        [SerializeField, Range(0, 0.5f)] float frontAxleRatio = 0.3f;
        [Tooltip("바퀴 간격의 절반 / 폭의 절반")]
        [SerializeField, Range(0, 1)] float trackRatio = 0.85f;

        Quaternion rot0;
        Vector3 offset, scale0;   // offset: 차체 중심 → transform 위치 (포크가 +z를 보는 기준)
        float y0;
        Forklift start, state;
        GameState owner;
        Renderer[] renderers;
        bool visible = true;
        Transform palletRoot;
        readonly PalletSlot[] slots = new PalletSlot[8];
        int palletShown = -1;

        void Awake()
        {
            rot0 = transform.rotation;
            scale0 = transform.localScale;
            y0 = transform.position.y;
            // 포크가 +z를 보게 돌려 놓고 렌더러 크기를 잰다
            transform.rotation = Quaternion.Euler(0, -forwardYaw, 0) * rot0;
            renderers = GetComponentsInChildren<Renderer>();
            var b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(transform.position, Vector3.one);
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            transform.rotation = rot0;
            offset = transform.position - b.center;
            offset.y = 0;
            var c = transform.position - Quaternion.Euler(0, forwardYaw, 0) * offset;
            var lp = CoordUtil.ToLayout(c);
            start = new Forklift
            {
                x = lp.x, z = lp.z, heading = CoordUtil.YawToFacing(forwardYaw),
                length = b.size.z, width = b.size.x,
                frontAxle = b.size.z * frontAxleRatio, halfTrack = b.size.x / 2 * trackRatio,
                maxSpeed = maxSpeed, reverseSpeed = reverseSpeed, accel = accel, brake = brake, turnRate = turnRate, pivotRate = pivotRate,
                summon = summon, shown = !summon,
            };
            if (!player)
                foreach (var v in FindObjectsByType<CharacterView>(FindObjectsSortMode.None))
                    if (v.isPlayer) player = v;
            BuildPallet();
        }

        // 파렛트 판 + 묶음 자리 8개(2×2×2). 묶음 하나 = 작은 박스 8개(2×2×2)
        // 묶음 자리 하나: 낱개 박스 8개(아래층부터 채움) + 묶였을 때만 켜는 끈·버클·도착지 스티커
        class PalletSlot { public GameObject root, strap; public GameObject[] boxes = new GameObject[8]; public Renderer sticker; }

        void BuildPallet()
        {
            palletRoot = new GameObject(name + "_Pallet").transform;
            float e = bundleEdge, g = e * 0.06f, s = e + g;
            Palette.Tint(Cube(palletRoot, new Vector3(0, 0.06f, 0), new Vector3(2 * s, 0.12f, 2 * s)), U.Hex(0x8d6e63));
            Color kraft = U.Hex(0xc49a6c), strapC = U.Hex(0xe8742a), buckleC = U.Hex(0x37474f);
            for (int i = 0; i < slots.Length; i++)
            {
                var sl = slots[i] = new PalletSlot { root = new GameObject("Bundle" + i) };
                var b = sl.root.transform;
                b.SetParent(palletRoot, false);
                b.localPosition = new Vector3((i % 2 - 0.5f) * s, 0.12f + (i / 4) * s + e / 2, (i / 2 % 2 - 0.5f) * s);
                // 골판지 상자: 상자마다 밝기를 조금씩 달리한다
                for (int k = 0; k < 8; k++)
                {
                    var r = Cube(b, new Vector3(k % 2 - 0.5f, k / 4 - 0.5f, k / 2 % 2 - 0.5f) * (e / 2), Vector3.one * (e / 2 * 0.95f));
                    Palette.Tint(r, kraft * (0.92f + 0.04f * ((k * 5 + i) % 4)));
                    sl.boxes[k] = r.gameObject;
                }
                // 주황 래싱 끈: 앞뒤·좌우로 두 줄씩 세로로 감고, 아래쪽에 가로로 한 바퀴. 앞면 세로 끈에 버클
                sl.strap = new GameObject("Strap");
                var st = sl.strap.transform;
                st.SetParent(b, false);
                float w = e * 0.07f, l = e * 1.0f, o = e * 0.22f;
                foreach (float x in new[] { -o, o })
                {
                    Palette.Tint(Cube(st, new Vector3(x, 0, 0), new Vector3(w, l, l)), strapC);
                    Palette.Tint(Cube(st, new Vector3(0, 0, x), new Vector3(l, l, w)), strapC);
                    Palette.Tint(Cube(st, new Vector3(x, e * 0.05f, l / 2), new Vector3(w * 1.6f, w * 2.4f, e * 0.04f)), buckleC);
                }
                Palette.Tint(Cube(st, new Vector3(0, -e * 0.12f, 0), new Vector3(l + 0.002f, w, l + 0.002f)), strapC);
                sl.sticker = Cube(st, new Vector3(0, e * 0.3f, l / 2 + 0.002f), new Vector3(e * 0.26f, e * 0.12f, e * 0.01f));
            }
            palletRoot.gameObject.SetActive(false);
        }

        static Renderer Cube(Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            return go.GetComponent<Renderer>();
        }

        // 파렛트는 포크를 따라간다. 묶음 자리는 끈까지, 그다음 자리에는 낱개 박스만 놓는다. 수가 바뀔 때만 다시 그린다
        void SyncPallet(Forklift f, Quaternion q)
        {
            int bundled = f.pallet.Count, loose = f.loose.Count;
            bool on = f.shown && bundled + loose > 0;
            palletRoot.gameObject.SetActive(on);
            if (!on) return;
            var tip = CoordUtil.ToWorld(f.x, f.z, y0) + q * new Vector3(0, 0.15f, (float)f.length / 2 - bundleEdge);
            palletRoot.SetPositionAndRotation(palletAnchor ? palletAnchor.position : tip, q);
            int key = bundled * 100 + loose;
            if (palletShown == key) return;
            palletShown = key;
            for (int i = 0; i < slots.Length; i++)
            {
                var sl = slots[i];
                bool full = i < bundled, part = i == bundled && loose > 0;
                sl.root.SetActive(full || part);
                sl.strap.SetActive(full);
                for (int k = 0; k < 8; k++) sl.boxes[k].SetActive(full || k < loose);
                if (full) Palette.Tint(sl.sticker, CONFIG.destinations[f.pallet[i].dest].color);
            }
        }

        void LateUpdate()
        {
            if (G == null) return;
            // 새 게임(GameState 새로 생성)이면 씬 배치 상태로 다시 만든다
            if (owner != G)
            {
                owner = G;
                state = start.Clone();
                G.forklifts.Add(state);
                palletShown = -1;
                transform.DOKill();
                transform.localScale = scale0;
                SetRenderers(visible = state.shown);
            }
            var f = state;
            if (f.shown != visible) Pop(f.shown);
            float yaw = CoordUtil.FacingToYaw(f.heading);
            var q = Quaternion.Euler(0, yaw, 0);
            if (f.shown)
            {
                transform.rotation = Quaternion.Euler(0, yaw - forwardYaw, 0) * rot0;
                transform.position = CoordUtil.ToWorld(f.x, f.z, y0) + q * offset;
            }
            SyncPallet(f, q);
            if (!f.shown) return;
            if (f.ridden && player)
                player.transform.SetPositionAndRotation(seat ? seat.position : CoordUtil.ToWorld(f.x, f.z), q);
        }

        // 펑: 연기를 터뜨리고 차체를 0 ↔ 원래 크기로 튕기듯 바꾼다
        void Pop(bool show)
        {
            visible = show;
            if (!poof) poof = MakePoof();
            else if (!poof.gameObject.scene.IsValid()) poof = Instantiate(poof);   // 프리팹 에셋을 넣었으면 씬에 하나 만든다
            poof.transform.position = CoordUtil.ToWorld(state.x, state.z, 0.6f);
            poof.Play();
            transform.DOKill();
            if (show)
            {
                SetRenderers(true);
                transform.localScale = Vector3.zero;
                transform.DOScale(scale0, 0.35f).SetEase(Ease.OutBack);
            }
            else transform.DOScale(Vector3.zero, 0.25f).SetEase(Ease.InBack).OnComplete(() => SetRenderers(false));
        }

        void SetRenderers(bool on)
        {
            foreach (var r in renderers) r.enabled = on;
        }

        void OnDestroy()
        {
            transform.DOKill();
            if (palletRoot) Destroy(palletRoot.gameObject);
        }

        // 기본 연기: 흰 원 40개를 한 번에 사방으로 뿌리고 작아지며 사라진다
        static ParticleSystem MakePoof()
        {
            var ps = new GameObject("ForkliftPoof").AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.duration = 0.6f;
            m.loop = false;
            m.playOnAwake = false;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            m.startColor = new Color(0.95f, 0.95f, 0.95f, 0.9f);
            m.gravityModifier = -0.3f;   // 연기라 살짝 떠오른다
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(0, 40) });
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 1f;
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            col.color = g;
            // Sprites/Default는 빌드에 항상 들어가고 파티클 색(정점 색)을 그대로 쓴다
            ps.GetComponent<ParticleSystemRenderer>().material = new Material(Shader.Find("Sprites/Default")) { mainTexture = SoftDot() };
            return ps;
        }

        // 가장자리로 갈수록 투명해지는 흰 원
        static Texture2D SoftDot()
        {
            const int n = 32;
            var t = new Texture2D(n, n);
            var c = new Vector2(n / 2f - 0.5f, n / 2f - 0.5f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float a = Mathf.Clamp01(1 - Vector2.Distance(new Vector2(x, y), c) / (n / 2f));
                    t.SetPixel(x, y, new Color(1, 1, 1, a * a));
                }
            t.Apply();
            return t;
        }

        void OnDrawGizmosSelected()
        {
            if (Application.isPlaying) return;
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(transform.position + Vector3.up, Quaternion.Euler(0, forwardYaw, 0) * Vector3.forward * 3);
        }
    }
}
