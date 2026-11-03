using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace APC
{
    public class AlbaPanelView : MonoBehaviour
    {
        [Header("스크롤 뷰 (칩 드래그 중 가장자리 자동 스크롤)")]
        [SerializeField] ScrollRect scrollRect;

        [Header("1. 지원자 영역")]
        [SerializeField] TMP_Text autoRefreshTimerText;
        [SerializeField] Button rerollButton;
        [SerializeField] TMP_Text rerollCostText;
        [SerializeField] TMP_Text emptyCandidatesText;
        [Tooltip("첫 카드는 템플릿. 지원자가 많으면 복제해 늘린다")]
        [SerializeField] CandidateCardUI[] candidateCards;

        [Header("2. 공정 배치 영역 (공정 + 대기)")]
        [SerializeField] GridLayoutGroup stageGrid;
        [Tooltip("이 너비보다 좁으면 공정 칸을 1열로")]
        [SerializeField] float minStageCellWidth = 260;
        [SerializeField] StageSlotUI[] stageSlots;

        [Header("3. 고용된 직원 영역")]
        [SerializeField] TMP_Text employeeCountText;
        [SerializeField] TMP_Text emptyEmployeesText;
        [Tooltip("첫 카드는 템플릿. 직원이 많으면 복제해 늘린다")]
        [SerializeField] EmployeeCardUI[] employeeCards;

        [Header("드래그 고스트 UI")]
        [SerializeField] RectTransform dragGhost;
        [SerializeField] TMP_Text dragGhostText;

        public ScrollRect ScrollRect => scrollRect;

        List<CandidateCardUI> candidates;
        List<EmployeeCardUI> employees;
        Alba draggingAlba, selectedAlba;
        StageSlotUI hoverSlot;
        Vector2 dragPos;
        Camera dragCam;
        bool refreshPending;
        float dynamicUpdateTimer, gridWidth = -1;
        int lastTimerSeconds = -1;

        void Awake()
        {
            candidates = new List<CandidateCardUI>(candidateCards);
            employees = new List<EmployeeCardUI>(employeeCards);
            if (rerollButton != null) rerollButton.onClick.AddListener(OnRerollClicked);
            foreach (var s in stageSlots) s.Init(this);
            if (dragGhost != null) dragGhost.gameObject.SetActive(false);
        }

        void OnDisable()
        {
            EndDrag();
            ClearSelection();
        }

        // 풀 크기를 n 이상으로: 첫 요소를 복제해 마지막 요소 뒤에 붙인다
        public static void Grow<T>(List<T> pool, int n) where T : Component
        {
            while (pool.Count < n)
            {
                var last = pool[pool.Count - 1];
                var clone = Instantiate(pool[0], last.transform.parent);
                clone.name = $"{pool[0].name.Split('_')[0]}_{pool.Count}";
                clone.transform.SetSiblingIndex(last.transform.GetSiblingIndex() + 1);
                pool.Add(clone);
            }
        }

        void Update()
        {
            if (Game.G == null || Game.CONFIG == null) return;

            UpdateTimerText();

            if (stageGrid != null)
            {
                float w = ((RectTransform)stageGrid.transform).rect.width;
                if (!Mathf.Approximately(w, gridWidth)) LayoutStageGrid();
            }

            if (draggingAlba != null) AutoScroll();

            dynamicUpdateTimer -= Time.unscaledDeltaTime;
            if (dynamicUpdateTimer <= 0)
            {
                dynamicUpdateTimer = 0.5f;
                foreach (var card in employees) if (card.gameObject.activeSelf) card.UpdateDynamicInfo();
            }
        }

        // 다음 낮/밤 전환(지원자 새로 옴)까지 남은 시간. 글자만 바꾼다
        void UpdateTimerText()
        {
            if (autoRefreshTimerText == null) return;
            int sec = (int)Math.Ceiling(Math.Max(0, Game.CONFIG.phaseSeconds - Game.G.phaseTime));
            if (sec == lastTimerSeconds) return;
            lastTimerSeconds = sec;
            autoRefreshTimerText.text = $"자동 새로고침 {sec / 60:D2}:{sec % 60:D2}";
        }

        public void RefreshAll()
        {
            if (Game.G == null || Game.CONFIG == null) return;
            // 드래그 중에 칩을 다시 묶으면 끌던 칩이 꺼진다. 끝난 뒤 갱신
            if (draggingAlba != null) { refreshPending = true; return; }

            var c = Game.CONFIG.alba;
            if (rerollCostText != null) rerollCostText.text = $"🔄 새로고침 {Rich.Money(c.rerollCost)}";
            if (rerollButton != null) rerollButton.interactable = Game.G.money >= c.rerollCost;

            var cands = Game.G.candidates;
            if (emptyCandidatesText != null) emptyCandidatesText.gameObject.SetActive(cands.Count == 0);
            Grow(candidates, cands.Count);
            for (int i = 0; i < candidates.Count; i++)
                candidates[i].Bind(i < cands.Count ? i : -1, i < cands.Count ? cands[i] : null, this);

            foreach (var slot in stageSlots)
                slot.Bind(Game.G.albas.Where(a => a.stage == slot.StageId).ToList());
            LayoutStageGrid();

            var albas = Game.G.albas;
            if (employeeCountText != null) employeeCountText.text = $"<b>직원 ({albas.Count}/{c.max})</b>";
            if (emptyEmployeesText != null) emptyEmployeesText.gameObject.SetActive(albas.Count == 0);
            Grow(employees, albas.Count);
            for (int i = 0; i < employees.Count; i++)
                employees[i].Bind(i < albas.Count ? albas[i] : null, this);

            // 돈이 바뀌어 다시 그려도 탭 선택은 유지
            if (selectedAlba != null && albas.Contains(selectedAlba)) Select(selectedAlba);
            else ClearSelection();
        }

        // 공정 칸: 너비에 맞춰 1~2열, 높이는 가장 많은 칩 줄 수에 맞춘다
        void LayoutStageGrid()
        {
            if (stageGrid == null) return;
            var rt = (RectTransform)stageGrid.transform;
            gridWidth = rt.rect.width;
            float inner = gridWidth - stageGrid.padding.horizontal;
            int cols = Mathf.Clamp(Mathf.FloorToInt((inner + stageGrid.spacing.x) / (minStageCellWidth + stageGrid.spacing.x)), 1, 2);
            float cellW = Mathf.Floor((inner - stageGrid.spacing.x * (cols - 1)) / cols);
            float cellH = 0;
            foreach (var s in stageSlots)
                if (s.transform.parent == rt) cellH = Mathf.Max(cellH, s.HeightFor(cellW, s.Count));
            stageGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            stageGrid.constraintCount = cols;
            stageGrid.cellSize = new Vector2(cellW, cellH);
        }

        void OnRerollClicked()
        {
            if (S.Spend(Game.CONFIG.alba.rerollCost))
            {
                Albas.RerollCandidates();
                RefreshAll();
            }
            else Game.Notify("⚠️", "새로고침 비용이 부족합니다.");
        }

        // ---------- 드래그 앤 드롭: 손가락 아래 칸만 색칠, 놓으면 배치 ----------
        public void OnChipBeginDrag(AlbaChipUI chip, PointerEventData e)
        {
            ClearSelection();
            draggingAlba = chip.Alba;
            dragCam = e.pressEventCamera;
            if (scrollRect != null) scrollRect.StopMovement();
            if (dragGhost != null)
            {
                dragGhost.gameObject.SetActive(true);
                dragGhost.SetAsLastSibling();
                if (dragGhostText != null) dragGhostText.text = draggingAlba.name;
            }
            OnChipDrag(e);
        }

        public void OnChipDrag(PointerEventData e)
        {
            if (draggingAlba == null) return;
            dragPos = e.position;
            if (dragGhost != null) dragGhost.position = dragPos;
            UpdateHover();
        }

        void UpdateHover()
        {
            var slot = SlotAt(dragPos);
            if (slot == hoverSlot) return;
            if (hoverSlot != null) hoverSlot.SetHighlight(SlotHighlightState.Normal);
            hoverSlot = slot;
            if (slot != null) slot.SetHighlight(slot.CanAccept(draggingAlba) ? SlotHighlightState.Available : SlotHighlightState.Blocked);
        }

        public void OnChipEndDrag(PointerEventData e)
        {
            if (draggingAlba == null) return;
            var alba = draggingAlba;
            var slot = SlotAt(e.position);
            EndDrag();
            if (slot != null) TryAssign(alba, slot);
            else if (refreshPending) RefreshAll();
        }

        void EndDrag()
        {
            draggingAlba = null;
            if (hoverSlot != null) hoverSlot.SetHighlight(SlotHighlightState.Normal);
            hoverSlot = null;
            if (dragGhost != null) dragGhost.gameObject.SetActive(false);
        }

        // 보이는 영역(뷰포트) 안에 있는 칸만
        StageSlotUI SlotAt(Vector2 screen)
        {
            if (scrollRect != null && !RectTransformUtility.RectangleContainsScreenPoint(scrollRect.viewport, screen, dragCam)) return null;
            return stageSlots.FirstOrDefault(s => s.gameObject.activeInHierarchy && s.Contains(screen, dragCam));
        }

        // 뷰포트 위/아래 가장자리에 칩을 대고 있으면 그쪽으로 스크롤
        void AutoScroll()
        {
            if (scrollRect == null) return;
            var vp = scrollRect.viewport;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(vp, dragPos, dragCam, out var p)) return;
            const float edge = 90, speed = 700;
            float dir = p.y > vp.rect.yMax - edge ? 1 : p.y < vp.rect.yMin + edge ? -1 : 0;
            float scrollable = scrollRect.content.rect.height - vp.rect.height;
            if (dir == 0 || scrollable <= 0) return;
            scrollRect.verticalNormalizedPosition = Mathf.Clamp01(scrollRect.verticalNormalizedPosition + dir * speed * Time.unscaledDeltaTime / scrollable);
            UpdateHover();
        }

        // ---------- 탭 선택: 칩 터치 후 칸 터치 ----------
        public void OnChipClicked(AlbaChipUI chip)
        {
            if (chip.Alba == null) return;
            if (selectedAlba == chip.Alba) ClearSelection();
            else Select(chip.Alba);
        }

        public void OnSlotClicked(StageSlotUI slot)
        {
            if (selectedAlba == null) return;
            var alba = selectedAlba;
            ClearSelection();
            TryAssign(alba, slot);
        }

        void Select(Alba alba)
        {
            ClearSelection();
            selectedAlba = alba;
            foreach (var s in stageSlots)
            {
                s.ChipOf(alba)?.SetSelected(true);
                s.SetHighlight(s.CanAccept(alba) ? SlotHighlightState.Available : SlotHighlightState.Blocked);
            }
        }

        void ClearSelection()
        {
            if (selectedAlba != null)
                foreach (var s in stageSlots) s.ChipOf(selectedAlba)?.SetSelected(false);
            selectedAlba = null;
            foreach (var s in stageSlots) s.SetHighlight(SlotHighlightState.Normal);
        }

        // 막힌 칸이나 같은 칸이면 상태를 바꾸지 않는다
        void TryAssign(Alba alba, StageSlotUI slot)
        {
            if (Game.G.albas.Contains(alba) && alba.stage != slot.StageId)
            {
                if (slot.CanAccept(alba)) Albas.Assign(alba, slot.StageId);
                else Game.Notify("👷", S.IsAuto(slot.StageId) ? "자동화된 공정에는 배치할 수 없습니다" : "배치 슬롯이 부족합니다");
            }
            refreshPending = false;
            RefreshAll();
        }

        // 간식 드래그용 (SnackView 호환)
        public Alba AlbaAtRow(Vector2 screen)
        {
            foreach (var card in employees)
                if (card.gameObject.activeSelf && card.Alba != null && RectTransformUtility.RectangleContainsScreenPoint(card.RectTransform, screen, null))
                    return card.Alba;
            return null;
        }
    }
}
