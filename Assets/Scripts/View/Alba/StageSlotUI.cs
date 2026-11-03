using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace APC
{
    public enum SlotHighlightState
    {
        Normal,
        Available,
        Blocked
    }

    public class StageSlotUI : MonoBehaviour, IPointerClickHandler
    {
        [Tooltip("ApcConfig.stages의 공정 ID. 비우거나 waiting 이면 대기")]
        [SerializeField] string stageId;
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text countText;
        [SerializeField] GameObject lockOverlay;
        [SerializeField] TMP_Text lockText;
        [SerializeField] Outline outline;
        [SerializeField] Image background;
        [SerializeField] RectTransform chipsContainer;
        [Tooltip("첫 칩은 템플릿. 인원이 많으면 복제해 늘린다")]
        [SerializeField] AlbaChipUI[] chipSlots;

        public string StageId => IsWaitingSlot ? null : stageId;
        public bool IsWaitingSlot => string.IsNullOrEmpty(stageId) || stageId == "waiting";
        public int Count => currentCount;

        AlbaPanelView parentPanel;
        List<AlbaChipUI> chips;
        bool isAutomated;
        int currentCount;
        int maxSlots;

        public void Init(AlbaPanelView panel)
        {
            parentPanel = panel;
            chips = new List<AlbaChipUI>(chipSlots);
        }

        public void Bind(List<Alba> assignedAlbas)
        {
            currentCount = assignedAlbas != null ? assignedAlbas.Count : 0;

            if (IsWaitingSlot)
            {
                isAutomated = false;
                if (titleText != null) titleText.text = "🪑 대기";
                if (countText != null) countText.text = $"{currentCount}명";
            }
            else
            {
                isAutomated = S.IsAuto(stageId);
                maxSlots = S.StageSlots(stageId);
                var s = Game.CONFIG.stages.Find(x => x.id == stageId);
                if (s != null && titleText != null) titleText.text = $"{s.emoji} {s.name}";
                if (countText != null) countText.text = isAutomated ? "🤖" : $"{currentCount}/{maxSlots}";
            }
            if (lockOverlay != null) lockOverlay.SetActive(isAutomated);
            if (lockText != null) lockText.text = "🤖 자동화";

            AlbaPanelView.Grow(chips, currentCount);
            for (int i = 0; i < chips.Count; i++)
                chips[i].Bind(i < currentCount ? assignedAlbas[i] : null, parentPanel);

            SetHighlight(SlotHighlightState.Normal);
        }

        public AlbaChipUI ChipOf(Alba a) => chips.Find(c => c.Alba == a);

        public bool CanAccept(Alba alba)
        {
            if (alba == null) return false;
            if (IsWaitingSlot || alba.stage == stageId) return true;
            return !isAutomated && currentCount < maxSlots;
        }

        public bool Contains(Vector2 screen, Camera cam) =>
            RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, screen, cam);

        // 칸 너비에서 칩이 몇 줄 필요한지 (공정 그리드 셀 높이 계산용)
        public float HeightFor(float width, int count)
        {
            var grid = chipsContainer.GetComponent<GridLayoutGroup>();
            var pad = GetComponent<VerticalLayoutGroup>();
            float inner = width - pad.padding.horizontal - grid.padding.horizontal;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((inner + grid.spacing.x) / (grid.cellSize.x + grid.spacing.x)));
            int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)perRow));
            float header = titleText.transform.parent.GetComponent<LayoutElement>().preferredHeight;
            return pad.padding.vertical + header + pad.spacing + grid.padding.vertical + rows * grid.cellSize.y + (rows - 1) * grid.spacing.y;
        }

        public void SetHighlight(SlotHighlightState state)
        {
            if (outline == null) return;

            switch (state)
            {
                case SlotHighlightState.Normal:
                    outline.effectColor = new Color(0.85f, 0.88f, 0.92f);
                    if (background != null) background.color = Color.white;
                    break;
                case SlotHighlightState.Available:
                    outline.effectColor = new Color(0.18f, 0.8f, 0.44f);
                    if (background != null) background.color = new Color(0.86f, 0.97f, 0.89f);
                    break;
                case SlotHighlightState.Blocked:
                    outline.effectColor = new Color(0.91f, 0.3f, 0.24f);
                    if (background != null) background.color = new Color(1f, 0.9f, 0.9f);
                    break;
            }
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.dragging) return;
            if (parentPanel != null) parentPanel.OnSlotClicked(this);
        }
    }
}
