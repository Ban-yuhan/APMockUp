// 화면 오른쪽 지게차 버튼 (두 개, ride로 구분)
// 소환 버튼: 미소환 → 소환, 소환됨 → 해제 (소환 지게차가 없으면 숨김)
// 탑승 버튼: 탑승 중 → 하차, 탑승 거리 안 → 탑승 (둘 다 아니면 숨김)
// 기능은 E·R 키와 같은 Forklifts 명령을 쓴다
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static APC.Game;

namespace APC
{
    public class ForkliftButtonView : MonoBehaviour
    {
        [SerializeField] Button btn;
        [SerializeField] Image stateBg;
        [SerializeField] TMP_Text label;
        [Tooltip("켜면 탑승/하차 버튼, 끄면 소환/해제 버튼")]
        [SerializeField] bool ride;

        enum Mode { None, Leave, Board, Dismiss, Summon }
        Mode mode = (Mode)(-1);

        void Awake() => btn.onClick.AddListener(Click);

        Mode Current()
        {
            if (G == null) return Mode.None;
            if (ride) return Forklifts.Riding ? Mode.Leave : Forklifts.CanBoard() ? Mode.Board : Mode.None;
            var s = G.forklifts.Find(f => f.summon);
            if (s == null) return Mode.None;
            return s.shown ? Mode.Dismiss : Mode.Summon;
        }

        void Click()
        {
            // 누르는 순간 상태로 다시 판단한다 (표시는 한 프레임 늦을 수 있다)
            var m = Current();
            if (m == Mode.Leave || m == Mode.Board) Forklifts.ToggleRide();
            else if (m == Mode.Dismiss || m == Mode.Summon) Forklifts.ToggleSummon();
        }

        void Update()
        {
            var m = Current();
            if (m == mode) return;
            mode = m;
            btn.gameObject.SetActive(m != Mode.None);
            switch (m)
            {
                case Mode.Leave: Show("하차", 0xef6c00); break;
                case Mode.Board: Show("탑승", 0x2e9e57); break;
                case Mode.Dismiss: Show("해제", 0x607d8b); break;
                case Mode.Summon: Show("소환", 0x1e5aa8); break;
            }
        }

        void Show(string text, int color)
        {
            label.text = $"<b>{text}</b>";
            stateBg.color = U.Hex(color);
        }
    }
}
