// 게임 진입점 (원본 Main.init / Main.frame + UI.bindInput 중 월드 입력)
// 상태 생성 → 매 프레임 입력 읽기 → 0.05초 고정 스텝으로 로직 갱신. 뷰는 각자 LateUpdate에서 상태를 읽는다
using System;
using System.Collections.Generic;
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

        [Header("다이나믹 조이스틱")]
        [Tooltip("누르기 시작한 위치에 띄우는 반투명 원 (UI)")]
        [SerializeField] RectTransform joystickMark;
        [Tooltip("이만큼(캔버스 px) 끌어야 이동으로 본다. 그보다 짧으면 클릭")]
        [SerializeField] float dragThreshold = 12;

        InputAction move, zoom, interact, summon;
        bool pressing, joystick;
        Vector2 origin;
        static readonly List<RaycastResult> uiHits = new List<RaycastResult>();

        void Awake()
        {
            Game.Setup(config, map.Build(), seed != 0 ? seed : (int?)null);
            Sim.Start();
            var m = actions.FindActionMap("Player", true);
            move = m.FindAction("Move", true);
            zoom = m.FindAction("Zoom", true);
            interact = m.FindAction("Interact", true);
            summon = m.FindAction("Summon", true);
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
            UpdateJoystick();
            Sim.input = joystick ? JoystickVector() : InputVector();
            var mv = move.ReadValue<Vector2>();
            Sim.drive = joystick ? JoystickDrive() : new P(mv.x, mv.y);
            // 상차 패널이 열려 있으면 월드 입력을 막는다 (원본 UI.bindInput)
            if (Loading.dock == null)
            {
                float wheel = zoom.ReadValue<float>();
                if (wheel != 0) rig.ZoomBy(-wheel);   // 원본 wheel deltaY는 아래로 굴리면 양수, Input System은 위로 굴리면 양수
                if (interact.WasPressedThisFrame())
                {
                    var d = Player.LoadableDock();
                    if (d != null) Loading.Open(d);
                    // 지게차 운전 중 도크 앞이면 파렛트 묶음을 싣는 상차, 아니면 하차
                    else if ((d = Forklifts.LoadableDock()) != null) Loading.Open(d, Forklifts.Ridden());
                    else Forklifts.ToggleRide();
                }
                if (summon.WasPressedThisFrame()) Forklifts.ToggleSummon();
            }
            Sim.Frame(FrameDt);
        }

        // 다이나믹 조이스틱 (원본의 클릭 이동 대체): 월드를 누른 채 끌면 누른 위치 → 지금 위치 방향으로 이동
        // 짧게 누르고 떼면(끌기 전) 이동하지 않아서 클릭은 그대로 UI(EventSystem)가 받는다. UI 위에서 시작한 누르기는 무시
        void UpdateJoystick()
        {
            var ptr = Pointer.current;
            if (ptr == null || Loading.dock != null) { StopJoystick(); return; }
            var pos = ptr.position.ReadValue();
            if (ptr.press.wasPressedThisFrame) { pressing = !OverUI(pos); origin = pos; }
            if (!ptr.press.isPressed) { StopJoystick(); return; }
            if (!pressing || joystick) return;
            float scale = joystickMark ? joystickMark.GetComponentInParent<Canvas>().scaleFactor : 1;
            if ((pos - origin).magnitude < dragThreshold * scale) return;
            joystick = true;
            if (!joystickMark) return;
            joystickMark.gameObject.SetActive(true);
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)joystickMark.parent, origin, null, out var lp);
            joystickMark.anchoredPosition = lp;
        }

        void StopJoystick()
        {
            pressing = joystick = false;
            if (joystickMark && joystickMark.gameObject.activeSelf) joystickMark.gameObject.SetActive(false);
        }

        // 화면에서 끈 방향 (화면 위 = W). 속도는 플레이어가 정규화하므로 방향만 쓴다
        P JoystickVector()
        {
            var d = Pointer.current.position.ReadValue() - origin;
            return CameraCorrect(d.x, -d.y);
        }

        // 지게차용: 끈 방향 그대로 (오른쪽 = 조향 오른쪽, 위 = 전진), 길이 1로
        P JoystickDrive()
        {
            var d = (Pointer.current.position.ReadValue() - origin).normalized;
            return new P(d.x, d.y);
        }

        // 화면 기준 WASD → 월드 방향 (카메라가 살짝 비스듬해서 보정). 원본 UI.inputVector
        P InputVector()
        {
            var v = move.ReadValue<Vector2>();
            return v == Vector2.zero ? default : CameraCorrect(v.x, -v.y);
        }

        P CameraCorrect(double ix, double iz)
        {
            // 원본: 카메라 위치 - 맵 중심 (배치 좌표)
            double yaw = Math.Atan2(rig.offset.x, -rig.offset.z);
            double c = Math.Cos(yaw), s = Math.Sin(yaw);
            return new P(ix * c + iz * s, -ix * s + iz * c);
        }

        // 누른 위치에 클릭을 받는 UI가 있는지 (마우스·터치 공통)
        static bool OverUI(Vector2 pos)
        {
            var es = EventSystem.current;
            if (!es) return false;
            uiHits.Clear();
            es.RaycastAll(new PointerEventData(es) { position = pos }, uiHits);
            return uiHits.Count > 0;
        }
    }
}
