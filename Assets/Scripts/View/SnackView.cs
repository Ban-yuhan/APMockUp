// 우측 간식 바와 간식 드래그 (원본 ui.js buildHud의 snack-bar, startSnackDrag, albaAt)
// 간식을 알바(맵 위 또는 알바 패널의 직원 카드)에 놓으면 MT 회복. 간식 종류 수는 설정을 따르므로 칸은 프리팹으로 만든다
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using static APC.Game;

namespace APC
{
    public class SnackView : MonoBehaviour
    {
        [SerializeField] Camera cam;
        [SerializeField] PanelView panel;
        [SerializeField] RectTransform snackPrefab;
        [Tooltip("드래그 중 마우스를 따라다니는 간식 아이콘")]
        [SerializeField] TMP_Text ghost;

        readonly List<(RectTransform rt, SnackDef snack)> items = new List<(RectTransform, SnackDef)>();
        SnackDef dragging;

        void Start()
        {
            foreach (var s in CONFIG.snacks)
            {
                var rt = Instantiate(snackPrefab, transform);
                rt.GetComponentInChildren<TMP_Text>().text = $"{s.emoji}\n<size=45%><b><color={Rich.GOOD}>+{s.mt}</color></b></size>";
                items.Add((rt, s));
            }
            ghost.gameObject.SetActive(false);
        }

        void Update()
        {
            var m = Mouse.current;
            if (G == null || m == null) return;
            var pos = m.position.ReadValue();
            if (dragging == null)
            {
                if (!m.leftButton.wasPressedThisFrame || Loading.dock != null) return;
                foreach (var (rt, snack) in items)
                    if (RectTransformUtility.RectangleContainsScreenPoint(rt, pos, null)) { dragging = snack; break; }
                if (dragging == null) return;
                ghost.text = dragging.emoji;
                ghost.gameObject.SetActive(true);
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)ghost.transform.parent, pos, null, out var lp);
            ghost.rectTransform.anchoredPosition = lp;
            if (!m.leftButton.wasReleasedThisFrame) return;
            ghost.gameObject.SetActive(false);
            var a = AlbaAt(pos);
            if (a != null) Albas.Feed(a, dragging);
            else Notify("", "간식은 알바 위에 놓아주세요");
            dragging = null;
        }

        // 직원 카드 위면 그 알바, 아니면 화면에서 48px 안의 가장 가까운 알바 (몸통 높이 0.9)
        Alba AlbaAt(Vector2 screen)
        {
            var row = panel.AlbaAtRow(screen);
            if (row != null) return row;
            float scale = GetComponentInParent<Canvas>().scaleFactor;
            Alba best = null;
            float bd = 48 * scale;
            foreach (var a in G.albas)
            {
                var s = cam.WorldToScreenPoint(CoordUtil.ToWorld(a.x, a.z, 0.9f));
                float d = Vector2.Distance(s, screen);
                if (s.z > 0 && d < bd) { bd = d; best = a; }
            }
            return best;
        }
    }
}
