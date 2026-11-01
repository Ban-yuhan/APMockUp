// 게임 진입점 (원본 Main.init / Main.frame + UI.bindInput 중 월드 입력)
// 상태 생성 → 매 프레임 입력 읽기 → 0.05초 고정 스텝으로 로직 갱신. 뷰는 각자 LateUpdate에서 상태를 읽는다
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace APC
{
    [DefaultExecutionOrder(-100)]
    public class GameRoot : MonoBehaviour
    {
        [SerializeField] ApcConfig config;
        [SerializeField] MapRoot map;
        [SerializeField] CameraRig rig;
        [SerializeField] InputActionAsset actions;
        [Tooltip("난수 시드 (0 = 무작위)")]
        [SerializeField] int seed;

        // 이번 프레임 실제 경과(최대 0.1초)와 뷰 애니메이션용 경과(멈춤이면 0, 배속 반영). 원본 World.sync(running ? dt * speed : 0)
        public static float FrameDt { get; private set; }
        public static float ViewDt { get; private set; }
        public static bool Running => Game.G != null && !Game.G.paused && !Game.G.userPaused;

        InputAction move, click, point, zoom, interact;

        void Awake()
        {
            Game.Setup(config, map.Build(), seed != 0 ? seed : (int?)null);
            Sim.Start();
            var m = actions.FindActionMap("Player", true);
            move = m.FindAction("Move", true);
            click = m.FindAction("ClickMove", true);
            point = m.FindAction("Point", true);
            zoom = m.FindAction("Zoom", true);
            interact = m.FindAction("Interact", true);
            m.Enable();
        }

        // 시작 안내 (원본 Main.init). HUD가 토스트를 구독한 뒤라 Start에서 보낸다
        void Start()
        {
            Game.Notify("📦", "APCenter에 오신 것을 환영합니다! 입고 트럭 앞에 서서 택배를 하차해 보세요");
            Game.Notify("🗺️", $"배치: {Game.LAYOUT.name} (씬)");
        }

        void OnDestroy()
        {
            actions.FindActionMap("Player")?.Disable();
            Game.ClearListeners();
        }

        void Update()
        {
            FrameDt = Mathf.Min(0.1f, Time.deltaTime);
            ViewDt = Running ? FrameDt * Game.G.speed : 0;
            Sim.input = InputVector();
            // 상차 패널이 열려 있으면 월드 입력을 막는다 (원본 UI.bindInput)
            if (Loading.dock == null)
            {
                if (click.WasPressedThisFrame() && !OverUI()) ClickMove();
                float wheel = zoom.ReadValue<float>();
                if (wheel != 0) rig.ZoomBy(-wheel);   // 원본 wheel deltaY는 아래로 굴리면 양수, Input System은 위로 굴리면 양수
                if (interact.WasPressedThisFrame())
                {
                    var d = Player.LoadableDock();
                    if (d != null) Loading.Open(d);
                }
            }
            Sim.Frame(FrameDt);
        }

        // 화면 기준 WASD → 월드 방향 (카메라가 살짝 비스듬해서 보정). 원본 UI.inputVector
        P InputVector()
        {
            var v = move.ReadValue<Vector2>();
            double ix = v.x, iz = -v.y;
            if (ix == 0 && iz == 0) return default;
            // 원본: 카메라 위치 - 맵 중심 (배치 좌표)
            double yaw = Math.Atan2(rig.offset.x, -rig.offset.z);
            double c = Math.Cos(yaw), s = Math.Sin(yaw);
            return new P(ix * c + iz * s, -ix * s + iz * c);
        }

        void ClickMove()
        {
            var ray = rig.GetComponent<Camera>().ScreenPointToRay(point.ReadValue<Vector2>());
            if (!new Plane(Vector3.up, 0).Raycast(ray, out float d)) return;
            var p = CoordUtil.ToLayout(ray.GetPoint(d));
            var bd = Game.LAYOUT.bounds;
            Game.G.player.target = new P(U.Clamp(p.x, bd.minX, bd.maxX), U.Clamp(p.z, bd.minZ, bd.maxZ));
        }

        static bool OverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
