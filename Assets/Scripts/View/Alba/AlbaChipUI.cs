using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace APC
{
    public class AlbaChipUI : MonoBehaviour, IPointerDownHandler, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        // 이 시간 이상 누른 뒤 움직이거나 가로로 끌면 칩 드래그, 짧게 세로로 끌면 스크롤
        const float HoldToDrag = 0.25f;

        [SerializeField] TMP_Text nameText;
        [SerializeField] Image background;
        [SerializeField] Outline outline;

        public Alba Alba { get; private set; }

        AlbaPanelView parentPanel;
        float downTime;

        public void Bind(Alba alba, AlbaPanelView panel)
        {
            Alba = alba;
            parentPanel = panel;

            if (alba == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            SetSelected(false);
            if (nameText != null) nameText.text = alba.name;
        }

        public void SetSelected(bool selected)
        {
            if (outline != null)
            {
                outline.enabled = selected;
                outline.effectColor = new Color(0.2f, 0.6f, 1f, 0.9f);
            }
            if (background != null) background.color = selected ? new Color(0.85f, 0.93f, 1f) : new Color(0.9f, 0.93f, 0.97f);
        }

        public void OnPointerDown(PointerEventData e) => downTime = Time.unscaledTime;

        public void OnInitializePotentialDrag(PointerEventData e)
        {
            var scroll = parentPanel != null ? parentPanel.ScrollRect : null;
            if (scroll != null) scroll.OnInitializePotentialDrag(e);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.dragging) return;
            if (parentPanel != null) parentPanel.OnChipClicked(this);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (Alba == null || parentPanel == null) return;
            var scroll = parentPanel.ScrollRect;
            bool held = Time.unscaledTime - downTime >= HoldToDrag;
            bool vertical = Mathf.Abs(e.delta.y) > Mathf.Abs(e.delta.x);
            if (scroll != null && vertical && !held)
            {
                // 이후 드래그 이벤트는 스크롤이 받는다
                e.pointerDrag = scroll.gameObject;
                scroll.OnBeginDrag(e);
                return;
            }
            parentPanel.OnChipBeginDrag(this, e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (parentPanel != null) parentPanel.OnChipDrag(e);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (parentPanel != null) parentPanel.OnChipEndDrag(e);
        }
    }
}
