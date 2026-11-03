// 화면 오른쪽 상차 버튼 (지게차 탑승/하차 버튼 위). 상차할 수 있는 도크 위에서만 보인다
// 걸어서는 출고 대기 박스, 지게차를 타고는 파렛트 묶음까지 싣는다 (E 키와 같은 판정)
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static APC.Game;

namespace APC
{
    public class LoadButtonView : MonoBehaviour
    {
        [SerializeField] Button btn;
        [SerializeField] TMP_Text label;

        Dock shown;
        bool first = true;

        void Awake() => btn.onClick.AddListener(Click);

        static Dock Target()
        {
            if (G == null || Loading.dock != null) return null;
            return Player.LoadableDock() ?? Forklifts.LoadableDock();
        }

        void Click()
        {
            var d = Player.LoadableDock();
            if (d != null) Loading.Open(d);
            else if ((d = Forklifts.LoadableDock()) != null) Loading.Open(d, Forklifts.Ridden());
        }

        void Update()
        {
            var d = Target();
            if (d == shown && !first) return;
            first = false;
            shown = d;
            btn.gameObject.SetActive(d != null);
            if (d != null) label.text = $"<b>상차하기</b>\n({CONFIG.destinations[d.idx].name})";
        }
    }
}
