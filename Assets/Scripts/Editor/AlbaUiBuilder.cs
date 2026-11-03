using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace APC
{
    // 알바 패널 UI를 씬에 맞춘다. 이름으로 찾아 없으면 만들고 있으면 설정만 고친다 (다시 실행해도 중복 없음)
    public static class AlbaUiBuilder
    {
        static readonly Color INK = new Color(31 / 255f, 41 / 255f, 51 / 255f);
        static readonly Color SOFT = new Color(91 / 255f, 103 / 255f, 117 / 255f);
        static readonly Color CARD_BG = new Color(0.97f, 0.98f, 0.99f);
        static readonly Color BORDER_COLOR = new Color(0.85f, 0.88f, 0.92f);
        static readonly Color BADGE_BG = new Color(0.9f, 0.93f, 0.97f);
        const float TOUCH = 48;

        [MenuItem("APC/Build Alba UI")]
        public static void BuildAlbaUI()
        {
            var albaGo = GameObject.Find("UI/Safe/Panel/Box/Viewport/Alba");
            if (albaGo == null)
            {
                Debug.LogError("Alba GameObject not found at UI/Safe/Panel/Box/Viewport/Alba");
                return;
            }
            var config = AssetDatabase.FindAssets("t:ApcConfig").Select(g => AssetDatabase.LoadAssetAtPath<ApcConfig>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
            if (config == null)
            {
                Debug.LogError("ApcConfig asset not found");
                return;
            }

            var box = albaGo.transform.parent.parent;
            Undo.RegisterFullObjectHierarchyUndo(box.gameObject, "Build Alba UI");

            var albaRt = (RectTransform)albaGo.transform;
            Vertical(albaGo, new RectOffset(6, 6, 8, 20), 10);
            Comp<ContentSizeFitter>(albaGo).verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var albaView = Comp<AlbaPanelView>(albaGo);
            var scroll = albaGo.GetComponentInParent<ScrollRect>();
            int order = 0;

            var title = Text(albaRt, "Title", "👷 <b>아르바이트 관리</b>", 30, INK);
            title.margin = new Vector4(4, 4, 60, 0); // 오른쪽 위 닫기 버튼 자리
            Order(title, ref order);

            // ---------- 1. 지원자 ----------
            var candHeader = Child(albaRt, "CandidateHeader");
            Horizontal(candHeader.gameObject, 10, false);
            Le(candHeader).preferredHeight = -1;
            Order(candHeader, ref order);

            var candTitle = Text(candHeader, "SectionTitle", "<b>지원자</b>", 24, INK);
            candTitle.alignment = TextAlignmentOptions.MidlineLeft;
            Le(candTitle).flexibleWidth = 1;

            var rerollBox = Child(candHeader, "RerollBox");
            Vertical(rerollBox.gameObject, new RectOffset(0, 0, 0, 0), 4);
            Le(rerollBox).preferredWidth = 250;

            // 예전 빌더는 타이머와 버튼을 헤더 바로 아래에 두었다
            var timerText = Text(rerollBox, "RefreshTimer", "자동 새로고침 00:00", 18, SOFT, candHeader);
            timerText.alignment = TextAlignmentOptions.Center;
            Le(timerText).preferredHeight = 24;

            var rerollBtn = Btn(rerollBox, "RerollBtn", "🔄 새로고침", 20, new Color(0.12f, 0.35f, 0.65f), Color.white, candHeader);
            Le(rerollBtn).preferredHeight = TOUCH;
            Le(rerollBtn).preferredWidth = -1;

            var emptyCand = Text(albaRt, "EmptyCandidate", "지원자가 없습니다", 20, SOFT);
            emptyCand.alignment = TextAlignmentOptions.Center;
            Order(emptyCand, ref order);

            var candCards = Cards<CandidateCardUI>(albaRt, "CandidateCard_", SetupCandidateCard, ref order);

            // ---------- 2. 공정 배치 ----------
            var stageTitle = Text(albaRt, "StageSectionTitle", "📦 <b>공정 배치</b> <size=80%><color=#5b6775>끌어 놓거나 터치해 배치</color></size>", 24, INK);
            stageTitle.margin = new Vector4(4, 10, 4, 0);
            Order(stageTitle, ref order);

            var stageGridRt = Child(albaRt, "StageGrid");
            var grid = Comp<GridLayoutGroup>(stageGridRt.gameObject);
            grid.spacing = new Vector2(8, 8);
            grid.padding = new RectOffset(0, 0, 0, 0);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.cellSize = new Vector2(304, 110); // 실행 중에는 AlbaPanelView가 너비에 맞춰 다시 정한다
            Comp<ContentSizeFitter>(stageGridRt.gameObject).verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Order(stageGridRt, ref order);

            var slots = new List<StageSlotUI>();
            for (int i = 0; i < config.stages.Count; i++)
            {
                var s = config.stages[i];
                var slot = SetupStageSlot(Child(stageGridRt, $"Slot_{s.id}"), s.id, $"{s.emoji} {s.name}");
                slot.transform.SetSiblingIndex(i);
                slots.Add(slot);
            }
            // 대기는 인원이 많을 수 있어 그리드 밖 전체 폭 한 칸
            var waiting = SetupStageSlot(Child(albaRt, "Slot_waiting", stageGridRt), "waiting", "🪑 대기");
            Order(waiting, ref order);
            slots.Add(waiting);

            // ---------- 3. 고용 직원 ----------
            var empHeader = Child(albaRt, "EmployeeHeader");
            Horizontal(empHeader.gameObject, 0, true);
            Le(empHeader).preferredHeight = 40;
            Order(empHeader, ref order);
            var empCount = Text(empHeader, "EmployeeCount", "<b>직원 (0/12)</b>", 24, INK);
            empCount.margin = new Vector4(4, 10, 4, 0);

            var emptyEmp = Text(albaRt, "EmptyEmployee", "고용한 알바가 없습니다", 20, SOFT);
            emptyEmp.alignment = TextAlignmentOptions.Center;
            Order(emptyEmp, ref order);

            var empCards = Cards<EmployeeCardUI>(albaRt, "EmployeeCard_", SetupEmployeeCard, ref order);

            // ---------- 드래그 고스트 (이전 실행이 남긴 중복은 지운다) ----------
            var ghosts = box.Cast<Transform>().Where(t => t.name == "DragGhost").ToList();
            foreach (var g in ghosts.Skip(1)) Undo.DestroyObjectImmediate(g.gameObject);
            var ghostRt = Child(box, "DragGhost");
            ghostRt.sizeDelta = new Vector2(160, TOUCH);
            ghostRt.anchorMin = ghostRt.anchorMax = ghostRt.pivot = new Vector2(0.5f, 0.5f);
            var ghostImg = Comp<Image>(ghostRt.gameObject);
            ghostImg.color = new Color(0.2f, 0.5f, 0.9f, 0.85f);
            ghostImg.raycastTarget = false;
            var ghostText = Text(ghostRt, "Text", "알바", 20, Color.white);
            Stretch(ghostText.rectTransform);
            ghostText.alignment = TextAlignmentOptions.Center;
            ghostRt.gameObject.SetActive(false);

            // ---------- 연결 ----------
            var so = new SerializedObject(albaView);
            so.FindProperty("scrollRect").objectReferenceValue = scroll;
            so.FindProperty("autoRefreshTimerText").objectReferenceValue = timerText;
            so.FindProperty("rerollButton").objectReferenceValue = rerollBtn;
            so.FindProperty("rerollCostText").objectReferenceValue = rerollBtn.GetComponentInChildren<TMP_Text>(true);
            so.FindProperty("emptyCandidatesText").objectReferenceValue = emptyCand;
            SetArray(so.FindProperty("candidateCards"), candCards);
            so.FindProperty("stageGrid").objectReferenceValue = grid;
            SetArray(so.FindProperty("stageSlots"), slots);
            so.FindProperty("employeeCountText").objectReferenceValue = empCount;
            so.FindProperty("emptyEmployeesText").objectReferenceValue = emptyEmp;
            SetArray(so.FindProperty("employeeCards"), empCards);
            so.FindProperty("dragGhost").objectReferenceValue = ghostRt;
            so.FindProperty("dragGhostText").objectReferenceValue = ghostText;
            so.ApplyModifiedPropertiesWithoutUndo();

            var panelView = box.parent.GetComponent<PanelView>();
            if (panelView != null)
            {
                var pso = new SerializedObject(panelView);
                pso.FindProperty("albaView").objectReferenceValue = albaView;
                pso.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(albaGo.scene);
            Debug.Log("[AlbaUiBuilder] 알바 패널 UI 갱신 완료. 씬을 저장하세요.");
        }

        // prefix_0.. 카드: 없으면 _0 하나 만들고, 있는 카드는 모두 설정을 맞춘다
        static List<T> Cards<T>(RectTransform parent, string prefix, System.Func<RectTransform, T> setup, ref int order)
        {
            var existing = parent.Cast<Transform>().Where(t => t.name.StartsWith(prefix)).OrderBy(t => t.GetSiblingIndex()).ToList();
            if (existing.Count == 0) existing.Add(Child(parent, prefix + "0"));
            var list = new List<T>();
            foreach (var t in existing)
            {
                list.Add(setup((RectTransform)t));
                Order(t, ref order);
            }
            return list;
        }

        // ---------- 지원자 카드 ----------
        static CandidateCardUI SetupCandidateCard(RectTransform root)
        {
            Card(root);

            // 한 줄: 이름 | 능력치 3칸 게이지 세로로 | 시급 위, 고용 버튼 아래
            var topRow = Child(root, "TopRow");
            Horizontal(topRow.gameObject, 12, false);
            Le(topRow).preferredHeight = -1;

            var nameText = Text(topRow, "Name", "김민준", 22, INK);
            nameText.alignment = TextAlignmentOptions.MidlineLeft;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            var leName = Le(nameText);
            leName.flexibleWidth = 0;
            leName.preferredWidth = 110;
            leName.minWidth = 80;

            // 예전 가로 능력치 줄은 세로 묶음으로 바꾼다
            var oldStatRow = root.Find("StatRow");
            if (oldStatRow != null) Undo.DestroyObjectImmediate(oldStatRow.gameObject);
            var statCol = Child(topRow, "StatCol");
            Vertical(statCol.gameObject, new RectOffset(0, 0, 0, 0), 4);
            Le(statCol).flexibleWidth = 1;
            var statRow = new[] { "StrGauge", "AgiGauge", "IntGauge" }.Select(n => StatGauge(statCol, n, 22)).ToArray();

            var rightCol = Child(topRow, "RightCol");
            Vertical(rightCol.gameObject, new RectOffset(0, 0, 0, 0), 4);
            Le(rightCol).preferredWidth = 130;
            Le(rightCol).flexibleWidth = 0;

            var wageText = Text(rightCol, "Wage", "시급 3,000", 20, SOFT, topRow);
            wageText.alignment = TextAlignmentOptions.Center;
            wageText.textWrappingMode = TextWrappingModes.NoWrap;
            Le(wageText).preferredHeight = 26;

            var hireBtn = Btn(rightCol, "HireBtn", "고용", 20, new Color(0.12f, 0.65f, 0.35f), Color.white, topRow);
            Le(hireBtn).preferredWidth = -1;
            Le(hireBtn).preferredHeight = TOUCH;

            var comp = Comp<CandidateCardUI>(root.gameObject);
            var so = new SerializedObject(comp);
            so.FindProperty("nameText").objectReferenceValue = nameText;
            so.FindProperty("wageText").objectReferenceValue = wageText;
            so.FindProperty("strGauge").objectReferenceValue = statRow[0];
            so.FindProperty("agiGauge").objectReferenceValue = statRow[1];
            so.FindProperty("intGauge").objectReferenceValue = statRow[2];
            so.FindProperty("hireButton").objectReferenceValue = hireBtn;
            so.FindProperty("hireButtonText").objectReferenceValue = hireBtn.GetComponentInChildren<TMP_Text>(true);
            so.ApplyModifiedPropertiesWithoutUndo();
            return comp;
        }

        // ---------- 직원 카드 ----------
        static EmployeeCardUI SetupEmployeeCard(RectTransform root)
        {
            Card(root);

            var row1 = Child(root, "Row1");
            Horizontal(row1.gameObject, 10, false);
            Le(row1).preferredHeight = TOUCH;

            var nameText = Text(row1, "Name", "김민준", 21, INK);
            nameText.alignment = TextAlignmentOptions.MidlineLeft;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            var leName = Le(nameText);
            leName.flexibleWidth = 1;
            leName.minWidth = 80;

            var costumeBtn = Btn(row1, "CostumeBtn", "👔 코스튬", 18, new Color(0.45f, 0.35f, 0.75f), Color.white);
            Le(costumeBtn).preferredWidth = 120;
            costumeBtn.transform.SetSiblingIndex(1);

            var fireBtn = Btn(row1, "FireBtn", "🚪 해고", 18, new Color(0.9f, 0.3f, 0.25f), Color.white);
            Le(fireBtn).preferredWidth = 110;
            fireBtn.transform.SetSiblingIndex(2);

            var mtBar = Gauge(root, "MtGauge", 26);
            mtBar.transform.SetSiblingIndex(1);
            var stats = StatRow(root, 24);
            stats[0].transform.parent.SetSiblingIndex(2);

            var wageStatus = Text(root, "WageStatus", "시급 3,000", 18, SOFT);
            wageStatus.overflowMode = TextOverflowModes.Ellipsis;
            wageStatus.textWrappingMode = TextWrappingModes.NoWrap;
            Le(wageStatus).preferredHeight = 24;
            wageStatus.transform.SetSiblingIndex(3);

            // 숙련도 줄글은 배지 줄로 바꾼다
            var oldSkill = root.Find("SkillText");
            if (oldSkill != null) Undo.DestroyObjectImmediate(oldSkill.gameObject);
            var skillRow = Child(root, "SkillRow");
            Horizontal(skillRow.gameObject, 6, true);
            Le(skillRow).preferredHeight = 28;
            skillRow.SetSiblingIndex(4);
            var badges = new[] { "Lift", "Carry", "Pack", "Inspect" }.Select(n =>
            {
                var b = Child(skillRow, "Badge_" + n);
                Comp<Image>(b.gameObject).color = BADGE_BG;
                Le(b).flexibleWidth = 1;
                var t = Text(b, "Text", "Lv0", 16, INK);
                Stretch(t.rectTransform);
                t.alignment = TextAlignmentOptions.Center;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                return t;
            }).ToList();

            var comp = Comp<EmployeeCardUI>(root.gameObject);
            var so = new SerializedObject(comp);
            so.FindProperty("nameText").objectReferenceValue = nameText;
            so.FindProperty("wageAndStatusText").objectReferenceValue = wageStatus;
            SetArray(so.FindProperty("skillBadges"), badges);
            so.FindProperty("mtGauge").objectReferenceValue = mtBar;
            so.FindProperty("strGauge").objectReferenceValue = stats[0];
            so.FindProperty("agiGauge").objectReferenceValue = stats[1];
            so.FindProperty("intGauge").objectReferenceValue = stats[2];
            so.FindProperty("fireButton").objectReferenceValue = fireBtn;
            so.FindProperty("costumeButton").objectReferenceValue = costumeBtn;
            so.ApplyModifiedPropertiesWithoutUndo();
            return comp;
        }

        // ---------- 공정 칸 ----------
        static StageSlotUI SetupStageSlot(RectTransform root, string stageId, string displayName)
        {
            Comp<Image>(root.gameObject).color = Color.white;
            var outline = Comp<Outline>(root.gameObject);
            outline.effectColor = BORDER_COLOR;
            outline.effectDistance = new Vector2(2, -2);
            Vertical(root.gameObject, new RectOffset(8, 8, 6, 8), 4);

            var header = Child(root, "Header");
            Horizontal(header.gameObject, 4, true);
            Le(header).preferredHeight = 30;
            var title = Text(header, "Title", displayName, 19, INK);
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            var count = Text(header, "Count", "0/1", 17, SOFT);
            count.alignment = TextAlignmentOptions.MidlineRight;
            count.textWrappingMode = TextWrappingModes.NoWrap;

            // 칩은 칸 너비에 맞춰 줄바꿈
            var chips = Child(root, "ChipsContainer");
            var oldRow = chips.GetComponent<HorizontalLayoutGroup>();
            if (oldRow != null) Undo.DestroyObjectImmediate(oldRow);
            var chipGrid = Comp<GridLayoutGroup>(chips.gameObject);
            chipGrid.cellSize = new Vector2(88, 44);
            chipGrid.spacing = new Vector2(6, 6);
            chipGrid.constraint = GridLayoutGroup.Constraint.Flexible;
            var leChips = Le(chips);
            leChips.preferredHeight = -1;
            leChips.minHeight = 44;
            leChips.flexibleHeight = 1;

            var chipList = chips.Cast<Transform>().Where(t => t.name.StartsWith("Chip_")).Select(t => SetupChip((RectTransform)t)).ToList();
            if (chipList.Count == 0) chipList.Add(SetupChip(Child(chips, "Chip_0")));
            foreach (var c in chipList) c.gameObject.SetActive(false);

            var lockOverlay = Child(root, "LockOverlay");
            Le(lockOverlay).ignoreLayout = true;
            Stretch(lockOverlay);
            Comp<Image>(lockOverlay.gameObject).color = new Color(0.1f, 0.1f, 0.15f, 0.7f);
            var lockText = Text(lockOverlay, "LockText", "🤖 자동화", 20, Color.white);
            Stretch(lockText.rectTransform);
            lockText.alignment = TextAlignmentOptions.Center;
            lockOverlay.gameObject.SetActive(false);

            var comp = Comp<StageSlotUI>(root.gameObject);
            var so = new SerializedObject(comp);
            so.FindProperty("stageId").stringValue = stageId;
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("countText").objectReferenceValue = count;
            so.FindProperty("lockOverlay").objectReferenceValue = lockOverlay.gameObject;
            so.FindProperty("lockText").objectReferenceValue = lockText;
            so.FindProperty("outline").objectReferenceValue = outline;
            so.FindProperty("background").objectReferenceValue = root.GetComponent<Image>();
            so.FindProperty("chipsContainer").objectReferenceValue = chips;
            SetArray(so.FindProperty("chipSlots"), chipList);
            so.ApplyModifiedPropertiesWithoutUndo();
            return comp;
        }

        static AlbaChipUI SetupChip(RectTransform root)
        {
            var img = Comp<Image>(root.gameObject);
            img.color = Color.white;
            var outline = Comp<Outline>(root.gameObject);
            outline.effectColor = new Color(0.2f, 0.6f, 1f, 0.9f);
            outline.effectDistance = new Vector2(2, -2);
            outline.enabled = false;

            var text = Text(root, "Name", "김민준", 17, INK);
            Stretch(text.rectTransform);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;

            var comp = Comp<AlbaChipUI>(root.gameObject);
            var so = new SerializedObject(comp);
            so.FindProperty("nameText").objectReferenceValue = text;
            so.FindProperty("background").objectReferenceValue = img;
            so.FindProperty("outline").objectReferenceValue = outline;
            so.ApplyModifiedPropertiesWithoutUndo();
            return comp;
        }

        // ---------- 조각 ----------
        static void Card(RectTransform root)
        {
            Comp<Image>(root.gameObject).color = CARD_BG;
            var outline = Comp<Outline>(root.gameObject);
            outline.effectColor = BORDER_COLOR;
            outline.effectDistance = new Vector2(1, -1);
            Le(root).preferredHeight = -1;
            Vertical(root.gameObject, new RectOffset(12, 12, 10, 10), 6);
        }

        // 근력·민첩·지능 게이지 한 줄
        static AlbaGaugeBar[] StatRow(RectTransform parent, float height)
        {
            var row = Child(parent, "StatRow");
            Horizontal(row.gameObject, 8, true);
            Le(row).preferredHeight = height;
            return new[] { "StrGauge", "AgiGauge", "IntGauge" }.Select(n =>
            {
                var g = StatGauge(row, n, height);
                Le(g).flexibleWidth = 1;
                return g;
            }).ToArray();
        }

        // 능력치 칸 게이지: [라벨][■■■□□] (칸 수는 실행 중 statMax에 맞춘다)
        static AlbaGaugeBar StatGauge(RectTransform parent, string name, float height)
        {
            var root = Child(parent, name);
            Le(root).preferredHeight = height;
            // 예전 막대 게이지의 배경과 채움은 칸으로 대신한다
            var oldBg = root.GetComponent<Image>();
            if (oldBg != null) Undo.DestroyObjectImmediate(oldBg);
            var oldFill = root.Find("Fill");
            if (oldFill != null) Undo.DestroyObjectImmediate(oldFill.gameObject);
            Horizontal(root.gameObject, 6, false);

            var label = Text(root, "Text", "", 15, SOFT);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            Le(label).preferredWidth = 58; // 이모지 + 두 글자
            Le(label).flexibleWidth = 0;

            // 칸은 고정 폭으로 촘촘하게 왼쪽부터
            var segs = Child(root, "Segments");
            Horizontal(segs.gameObject, 3, false);
            Le(segs).flexibleWidth = 1;
            var cell = Child(segs, "Seg_0");
            Comp<Image>(cell.gameObject).raycastTarget = false;
            Le(cell).preferredWidth = 18;
            Le(cell).flexibleWidth = 0;

            var comp = Comp<AlbaGaugeBar>(root.gameObject);
            var so = new SerializedObject(comp);
            so.FindProperty("fillImage").objectReferenceValue = null;
            so.FindProperty("valueText").objectReferenceValue = label;
            so.FindProperty("segments").objectReferenceValue = segs;
            so.ApplyModifiedPropertiesWithoutUndo();
            return comp;
        }

        static AlbaGaugeBar Gauge(RectTransform parent, string name, float height)
        {
            var root = Child(parent, name);
            Le(root).preferredHeight = height;
            Comp<Image>(root.gameObject).color = new Color(0.88f, 0.9f, 0.93f);

            var fill = Child(root, "Fill");
            var fillImg = Comp<Image>(fill.gameObject);
            fillImg.type = Image.Type.Simple; // 비율은 AlbaGaugeBar가 앵커로 정한다
            fillImg.raycastTarget = false;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0.5f, 1);
            fill.offsetMin = fill.offsetMax = Vector2.zero;

            var text = Text(root, "Text", "", 16, INK);
            Stretch(text.rectTransform);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;

            var comp = Comp<AlbaGaugeBar>(root.gameObject);
            var so = new SerializedObject(comp);
            so.FindProperty("fillImage").objectReferenceValue = fillImg;
            so.FindProperty("valueText").objectReferenceValue = text;
            so.ApplyModifiedPropertiesWithoutUndo();
            return comp;
        }

        // ---------- 찾기/만들기 ----------
        // parent에서 이름으로 찾고, 없으면 legacyParent에서 옮겨오고, 그래도 없으면 만든다
        static RectTransform Child(Transform parent, string name, Transform legacyParent = null)
        {
            var t = parent.Find(name);
            if (t == null && legacyParent != null)
            {
                t = legacyParent.Find(name);
                if (t != null) Undo.SetTransformParent(t, parent, "Build Alba UI");
            }
            if (t == null)
            {
                var go = new GameObject(name, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, "Build Alba UI");
                go.transform.SetParent(parent, false);
                t = go.transform;
            }
            return (RectTransform)t;
        }

        static T Comp<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : Undo.AddComponent<T>(go);
        }
        static LayoutElement Le(Component c) => Comp<LayoutElement>(c.gameObject);

        static void Order(Component c, ref int order) => c.transform.SetSiblingIndex(order++);

        static void Vertical(GameObject go, RectOffset padding, float spacing)
        {
            var v = Comp<VerticalLayoutGroup>(go);
            v.padding = padding;
            v.spacing = spacing;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
        }

        static void Horizontal(GameObject go, float spacing, bool expandWidth)
        {
            var h = Comp<HorizontalLayoutGroup>(go);
            h.spacing = spacing;
            h.padding = new RectOffset(0, 0, 0, 0);
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = expandWidth;
            h.childForceExpandHeight = true;
        }

        static TMP_Text Text(Transform parent, string name, string content, float size, Color color, Transform legacyParent = null)
        {
            var rt = Child(parent, name, legacyParent);
            var t = rt.GetComponent<TMP_Text>();
            if (t == null) t = Undo.AddComponent<TextMeshProUGUI>(rt.gameObject);
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.raycastTarget = false;
            return t;
        }

        static Button Btn(Transform parent, string name, string label, float fontSize, Color bg, Color fg, Transform legacyParent = null)
        {
            var rt = Child(parent, name, legacyParent);
            Comp<Image>(rt.gameObject).color = bg;
            var btn = Comp<Button>(rt.gameObject);
            var text = Text(rt, "Label", label, fontSize, fg);
            Stretch(text.rectTransform);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            Le(rt).minHeight = 44;
            return btn;
        }

        static void SetArray<T>(SerializedProperty p, IList<T> items) where T : Object
        {
            p.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
        }
    }
}
