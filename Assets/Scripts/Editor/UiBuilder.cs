// 에디터 전용: UI 캔버스와 상차 미니게임 패널을 씬에 만든다 (원본 index.html #loading-overlay + style.css .ld-*)
// 개수가 바뀌는 칸·박스 조각·대기 카드·작은 블록만 프리팹(Prefabs/UI)으로 둔다
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace APC.EditorTools
{
    public static class UiBuilder
    {
        const string PrefabDir = "Assets/Prefabs/UI";
        const string FontPath = "Assets/Fonts/malgun SDF.asset";
        static TMP_FontAsset font;

        static readonly Color INK = U.Hex(0x1f2933), SOFT = U.Hex(0x5b6775), LINE = U.Hex(0xd5dbe3), BAR = U.Hex(0xf3f6fa);

        [MenuItem("APC/UI 만들기")]
        public static void BuildAll()
        {
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (!AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
            var cell = SavePrefab(CellPrefab(), "LdCell").GetComponent<Image>();
            var piece = SavePrefab(PiecePrefab(), "LdPiece").GetComponent<Image>();
            var mini = SavePrefab(MiniPrefab(), "LdMini").GetComponent<Image>();
            var card = SavePrefab(CardPrefab(), "LdCard").GetComponent<RectTransform>();
            var orderCard = SavePrefab(OrderCardPrefab(), "OrderCard").GetComponent<Image>();
            var toast = SavePrefab(ToastPrefab(), "Toast").GetComponent<Image>();
            var panelText = SavePrefab(PanelTextPrefab(), "PanelText").GetComponent<TMP_Text>();
            var panelRow = SavePrefab(PanelRowPrefab(), "PanelRow").GetComponent<RectTransform>();
            var worldLabel = SavePrefab(WorldLabelPrefab(), "WorldLabel").GetComponent<Image>();
            var albaRow = SavePrefab(AlbaRowPrefab(), "AlbaRow").GetComponent<RectTransform>();
            var snack = SavePrefab(SnackPrefab(), "Snack").GetComponent<RectTransform>();
            AssetDatabase.SaveAssets();

            var old = GameObject.Find("UI");
            if (old) Object.DestroyImmediate(old);
            var canvasGo = new GameObject("UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            // 원본 패널은 1180×720, 최대 96vw × 94vh. 화면이 작으면 패널이 화면 안에 들어오게 줄인다
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1230, 766);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            if (!Object.FindFirstObjectByType<EventSystem>())
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            // 그리는 순서: 월드 라벨 → HUD → 패널 → 상차 패널 (원본 #labels, #hud, #panel, #loading-overlay)
            var labels = Rect("Labels", canvasGo.transform);
            Stretch(labels);
            var lv = labels.gameObject.AddComponent<LabelView>();
            SetRefs(lv, ("cam", Camera.main), ("labelPrefab", worldLabel));
            var hud = Rect("Hud", canvasGo.transform);
            Stretch(hud);
            var panel = BuildPanel(canvasGo.transform, panelText, panelRow, albaRow);
            BuildHud(hud, panel, orderCard, toast, snack);
            BuildLoading(canvasGo.transform, cell, piece, mini, card);

            var scene = canvasGo.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // ---------- HUD ----------
        static readonly Color PANEL = new Color(1, 1, 1, .94f);

        static void BuildHud(RectTransform hud, PanelView panel, Image orderCard, Image toast, RectTransform snack)
        {
            var view = hud.gameObject.AddComponent<HudView>();

            // 좌상단 시계
            var clock = Img("ClockBox", hud, PANEL).rectTransform;
            Corner(clock, new Vector2(0, 1), new Vector2(10, -10));
            clock.sizeDelta = new Vector2(200, 0);
            var v = VLayout(clock.gameObject, new RectOffset(12, 12, 10, 10), 4);
            clock.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var line = Rect("Clock", clock);
            Layout(line.gameObject, 0, 22);
            var day = Text("Day", line, 15);
            Stretch(day.rectTransform);
            var time = Text("Time", line, 15);
            Stretch(time.rectTransform);
            time.alignment = TextAlignmentOptions.MidlineRight;
            var phase = Img("PhaseBar", clock, U.Hex(0xe3e8ee));
            Layout(phase.gameObject, 0, 4);
            var phaseFill = Img("Fill", phase.transform, U.Hex(0xe6a100));
            Stretch(phaseFill.rectTransform);
            var money = Text("Money", clock, 20);
            money.fontStyle = FontStyles.Bold;
            Layout(money.gameObject, 0, 28);
            var speed = Rect("Speed", clock);
            Layout(speed.gameObject, 0, 22);
            var hs = speed.gameObject.AddComponent<HorizontalLayoutGroup>();
            hs.spacing = 4;
            hs.childControlWidth = hs.childControlHeight = true;
            hs.childForceExpandWidth = hs.childForceExpandHeight = true;
            var speeds = new[] { "⏸", "x1", "x2", "x4" }.Select(s => SmallBtn(s, speed, 12, Color.white, INK)).ToArray();
            var stats = Text("Stats", clock, 11);
            stats.color = SOFT;
            Layout(stats.gameObject, 0, 16);

            // 상단 주문서
            var strip = Rect("OrderStrip", hud);
            strip.anchorMin = new Vector2(0, 1); strip.anchorMax = new Vector2(1, 1); strip.pivot = new Vector2(0, 1);
            strip.offsetMin = new Vector2(220, -10 - 82);
            strip.offsetMax = new Vector2(-90, -10);
            var hl = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 6;
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = true;
            strip.gameObject.AddComponent<RectMask2D>();

            // 하단 메뉴
            var menu = Rect("MenuBar", hud);
            Corner(menu, Vector2.zero, new Vector2(10, 10));
            var hm = menu.gameObject.AddComponent<HorizontalLayoutGroup>();
            hm.spacing = 6;
            hm.childControlWidth = hm.childControlHeight = true;
            hm.childForceExpandWidth = hm.childForceExpandHeight = false;
            Fit(menu.gameObject);
            var facility = MenuBtn("🏗️ 시설", menu);
            var upgrade = MenuBtn("⬆️ 업그레이드", menu);
            var albaBtn = MenuBtn("👷 알바", menu);

            // 우측 간식 바 (간식 칸은 SnackView가 설정 수만큼 만든다)
            var bar = Img("SnackBar", hud, PANEL).rectTransform;
            Corner(bar, Vector2.one, new Vector2(-10, -100));
            bar.sizeDelta = new Vector2(66, 0);
            var vb = VLayout(bar.gameObject, new RectOffset(6, 6, 8, 8), 6);
            vb.childAlignment = TextAnchor.UpperCenter;
            vb.childForceExpandWidth = false;
            bar.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var title = Text("Title", bar, 11);
            title.fontStyle = FontStyles.Bold;
            title.color = SOFT;
            title.alignment = TextAlignmentOptions.Center;
            title.text = "간식";
            var ghost = Text("SnackGhost", hud, 34);
            ghost.rectTransform.anchorMin = ghost.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            ghost.rectTransform.sizeDelta = new Vector2(60, 60);
            ghost.alignment = TextAlignmentOptions.Center;
            SetRefs(bar.gameObject.AddComponent<SnackView>(), ("cam", Camera.main), ("panel", panel), ("snackPrefab", snack), ("ghost", ghost));

            // 하단 도움말 (원본: 왼쪽 끝이 화면 가운데에서 자기 폭의 30%만큼 왼쪽)
            var helpBg = Img("Help", hud, new Color(1, 1, 1, .75f));
            helpBg.raycastTarget = false;
            var hr = helpBg.rectTransform;
            hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0);
            hr.pivot = new Vector2(0.3f, 0);
            hr.anchoredPosition = new Vector2(0, 14);
            var hh = helpBg.gameObject.AddComponent<HorizontalLayoutGroup>();
            hh.padding = new RectOffset(10, 10, 4, 4);
            hh.childControlWidth = hh.childControlHeight = true;
            hh.childForceExpandWidth = hh.childForceExpandHeight = false;
            Fit(helpBg.gameObject);
            var help = Text("Text", hr, 12);
            help.color = SOFT;

            // 우하단 토스트 (새 토스트가 아래)
            var toasts = Rect("Toasts", hud);
            Corner(toasts, new Vector2(1, 0), new Vector2(-90, 44));
            var vt = VLayout(toasts.gameObject, new RectOffset(), 6);
            vt.childAlignment = TextAnchor.LowerRight;
            Fit(toasts.gameObject);

            SetRefs(view, ("clockDay", day), ("clockTime", time), ("money", money), ("stats", stats), ("help", help),
                ("phaseFill", phaseFill), ("helpBg", helpBg), ("facilityBtn", facility), ("upgradeBtn", upgrade), ("albaBtn", albaBtn), ("panel", panel),
                ("orderStrip", strip), ("toasts", toasts), ("orderCardPrefab", orderCard), ("toastPrefab", toast));
            var so = new SerializedObject(view);
            var arr = so.FindProperty("speedBtns");
            arr.arraySize = speeds.Length;
            for (int i = 0; i < speeds.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = speeds[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- 왼쪽 패널 ----------
        static PanelView BuildPanel(Transform canvas, TMP_Text text, RectTransform row, RectTransform albaRow)
        {
            var holder = Rect("Panel", canvas);
            Stretch(holder);
            var view = holder.gameObject.AddComponent<PanelView>();
            var root = Img("Box", holder, PANEL).rectTransform;
            root.anchorMin = new Vector2(0, 0); root.anchorMax = new Vector2(0, 1); root.pivot = new Vector2(0, 1);
            root.offsetMin = new Vector2(10, 56);
            root.offsetMax = new Vector2(10 + 400, -100);
            var viewport = Rect("Viewport", root);
            Stretch(viewport);
            viewport.offsetMin = new Vector2(16, 14);
            viewport.offsetMax = new Vector2(-16, -14);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            VLayout(content.gameObject, new RectOffset(0, 0, 0, 0), 4);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            var close = Img("Close", root, Color.clear);
            close.rectTransform.anchorMin = close.rectTransform.anchorMax = close.rectTransform.pivot = Vector2.one;
            close.rectTransform.anchoredPosition = new Vector2(-6, -6);
            close.rectTransform.sizeDelta = new Vector2(28, 28);
            var closeBtn = close.gameObject.AddComponent<Button>();
            var ct = Text("Label", close.transform, 18);
            Stretch(ct.rectTransform);
            ct.alignment = TextAlignmentOptions.Center;
            ct.color = SOFT;
            ct.text = "✕";
            SetRefs(view, ("root", root.gameObject), ("content", content), ("closeBtn", closeBtn), ("textPrefab", text), ("rowPrefab", row), ("albaRowPrefab", albaRow));
            return view;
        }

        // 직원 카드: Info(이름·스탯·시급·MT·숙련도) / Bottom(Assign 드롭다운, Status, Fire)
        static GameObject AlbaRowPrefab()
        {
            var root = Img("AlbaRow", null, U.Hex(0xf5f7fa));
            VLayout(root.gameObject, new RectOffset(8, 8, 8, 8), 4);
            var info = Text("Info", root.transform, 12);
            info.textWrappingMode = TextWrappingModes.Normal;
            info.alignment = TextAlignmentOptions.TopLeft;
            var bottom = Rect("Bottom", root.transform);
            var h = bottom.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;

            var res = new TMP_DefaultControls.Resources
            {
                standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
                inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
                knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
            };
            var dd = TMP_DefaultControls.CreateDropdown(res);
            dd.name = "Assign";
            dd.transform.SetParent(bottom, false);
            Layout(dd, 170, 24);
            foreach (var t in dd.GetComponentsInChildren<TMP_Text>(true)) { t.font = font; t.fontSize = 12; }
            var dt = dd.GetComponent<TMP_Dropdown>();
            dt.template.sizeDelta = new Vector2(0, 230);

            var status = Text("Status", bottom, 12);
            status.color = SOFT;
            status.overflowMode = TextOverflowModes.Ellipsis;
            var le = status.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 1; le.flexibleWidth = 1;
            var fire = SmallBtn("해고", bottom, 12, Color.white, U.Hex(0xd63031));
            fire.name = "Fire";
            var hf = fire.gameObject.AddComponent<HorizontalLayoutGroup>();
            hf.padding = new RectOffset(8, 8, 2, 2);
            hf.childControlWidth = hf.childControlHeight = true;
            hf.childForceExpandWidth = hf.childForceExpandHeight = false;
            return root.gameObject;
        }

        // 간식 칸 50×50: 아이콘 + MT 회복량
        static GameObject SnackPrefab()
        {
            var root = Img("Snack", null, U.Hex(0xfff7e6));
            Layout(root.gameObject, 50, 50);
            var t = Text("Text", root.transform, 24);
            Stretch(t.rectTransform);
            t.alignment = TextAlignmentOptions.Center;
            t.lineSpacing = -20;
            return root.gameObject;
        }

        // ---------- HUD·패널 프리팹 ----------
        // 주문서 카드: 바탕 / Top(목적지 색 띠 4px) / Text
        static GameObject OrderCardPrefab()
        {
            var root = Img("OrderCard", null, PANEL);
            root.raycastTarget = false;
            root.gameObject.AddComponent<LayoutElement>().preferredWidth = 132;
            var top = Img("Top", root.transform, Color.gray);
            top.raycastTarget = false;
            Band(top.rectTransform, true, 4);
            var t = Text("Text", root.transform, 12);
            Stretch(t.rectTransform);
            t.rectTransform.offsetMin = new Vector2(7, 4);
            t.rectTransform.offsetMax = new Vector2(-7, -8);
            t.alignment = TextAlignmentOptions.TopLeft;
            t.lineSpacing = 4;
            t.overflowMode = TextOverflowModes.Truncate;
            return root.gameObject;
        }

        static GameObject ToastPrefab()
        {
            var root = Img("Toast", null, new Color(22 / 255f, 30 / 255f, 42 / 255f, .9f));
            root.raycastTarget = false;
            var cg = root.gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false; cg.interactable = false;
            var h = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(12, 12, 7, 7);
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            var t = Text("Text", root.transform, 13);
            t.color = Color.white;
            return root.gameObject;
        }

        static GameObject PanelTextPrefab()
        {
            var t = Text("PanelText", null, 12);
            t.textWrappingMode = TextWrappingModes.Normal;
            t.alignment = TextAlignmentOptions.TopLeft;
            return t.gameObject;
        }

        // 패널 줄: Label(왼쪽, 남는 폭) / Ok(초록 글자) / Buy(구매 버튼)
        static GameObject PanelRowPrefab()
        {
            var root = Img("PanelRow", null, U.Hex(0xf5f7fa));
            root.raycastTarget = false;
            var h = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(8, 8, 6, 6);
            h.spacing = 8;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            var label = Text("Label", root.transform, 13);
            label.textWrappingMode = TextWrappingModes.Normal;
            var le = label.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 1; le.flexibleWidth = 1;
            var ok = Text("Ok", root.transform, 12);
            ok.fontStyle = FontStyles.Bold;
            ok.color = U.Hex(0x2e9e57);
            var buy = SmallBtn("", root.transform, 12, U.Hex(0x1e5aa8), Color.white);
            buy.name = "Buy";
            buy.gameObject.AddComponent<CanvasGroup>();
            var hb = buy.gameObject.AddComponent<HorizontalLayoutGroup>();
            hb.padding = new RectOffset(10, 10, 4, 4);
            hb.childControlWidth = hb.childControlHeight = true;
            hb.childForceExpandWidth = hb.childForceExpandHeight = false;
            return root.gameObject;
        }

        // 월드 라벨: 바탕(누를 수 있으면 Button) / Text. 크기는 글자에 맞추고 가운데 아래가 기준점
        static GameObject WorldLabelPrefab()
        {
            var root = Img("WorldLabel", null, Color.black);
            root.raycastTarget = false;
            var rt = root.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0);
            var b = root.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.enabled = false;
            var h = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(7, 7, 3, 3);
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            Fit(root.gameObject);
            var t = Text("Text", rt, 12);
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            return root.gameObject;
        }

        static Button SmallBtn(string label, Transform parent, float size, Color bg, Color fg)
        {
            var img = Img("Btn", parent, bg);
            var b = img.gameObject.AddComponent<Button>();
            var t = Text("Label", img.transform, size);
            Stretch(t.rectTransform);
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold;
            t.color = fg;
            t.text = label;
            return b;
        }

        static Button MenuBtn(string label, Transform parent)
        {
            var b = SmallBtn(label, parent, 14, PANEL, INK);
            b.name = label;
            var h = b.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(14, 14, 8, 8);
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            return b;
        }

        static VerticalLayoutGroup VLayout(GameObject go, RectOffset pad, float spacing)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = pad;
            v.spacing = spacing;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            return v;
        }

        static void Fit(GameObject go)
        {
            var f = go.AddComponent<ContentSizeFitter>();
            f.horizontalFit = f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        // 화면 모서리 기준 배치 (corner = 기준 모서리이자 기준점)
        static void Corner(RectTransform rt, Vector2 corner, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = corner;
            rt.anchoredPosition = pos;
        }

        static void SetRefs(Object target, params (string field, Object value)[] refs)
        {
            var so = new SerializedObject(target);
            foreach (var (f, v) in refs) so.FindProperty(f).objectReferenceValue = v;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- 상차 패널 ----------
        static void BuildLoading(Transform canvas, Image cell, Image piece, Image mini, RectTransform card)
        {
            var root = Rect("Loading", canvas);
            Stretch(root);
            var view = root.gameObject.AddComponent<LoadingView>();

            var overlay = Img("Overlay", root, new Color(15 / 255f, 22 / 255f, 32 / 255f, .55f));
            Stretch(overlay.rectTransform);
            var panel = Img("Panel", overlay.transform, Color.white);
            panel.rectTransform.sizeDelta = new Vector2(1180, 720);

            // 위: 제목 · 적재율 · 예상 대금
            var top = Img("Top", panel.transform, BAR).rectTransform;
            Band(top, true, 56);
            var h = top.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(18, 18, 0, 0);
            h.spacing = 18;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true; h.childControlHeight = false;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            var title = Text("Title", top, 16);
            var rate = Text("Rate", top, 15);
            var bar = Img("Bar", top, new Color(0, 0, 0, .18f));
            Layout(bar.gameObject, 120, 9);
            var fill = Img("Fill", bar.transform, U.Hex(0x4caf50));
            Stretch(fill.rectTransform);
            fill.rectTransform.anchorMax = new Vector2(0, 1);
            var rateNote = Text("RateNote", top, 12);
            Layout(Rect("Spacer", top).gameObject, 0, 0).flexibleWidth = 1;
            var pay = Text("Pay", top, 15);

            // 아래: 버튼 · 도움말
            var bottom = Img("Bottom", panel.transform, BAR).rectTransform;
            Band(bottom, false, 56);
            var hb = bottom.gameObject.AddComponent<HorizontalLayoutGroup>();
            hb.padding = new RectOffset(18, 18, 0, 0);
            hb.spacing = 8;
            hb.childAlignment = TextAnchor.MiddleLeft;
            hb.childControlWidth = true; hb.childControlHeight = false;
            hb.childForceExpandWidth = false; hb.childForceExpandHeight = false;
            var depart = Btn("Depart", bottom, "🚚 출발", true);
            var reset = Btn("Reset", bottom, "↺ 초기화", false);
            var close = Btn("Close", bottom, "✕ 닫기", false);
            Layout(Rect("Spacer", bottom).gameObject, 0, 0).flexibleWidth = 1;
            var help = Text("Help", bottom, 12);
            help.color = SOFT;
            help.text = "드래그: 배치 · 드래그 중 <b>R</b> 또는 <b>우클릭</b>: 회전 · 좌클릭 드래그: 이동 · 우클릭: 회수 · ⏸ 게임 시간 정지 중";

            // 가운데 본문
            var body = Rect("Body", panel.transform);
            Stretch(body);
            body.offsetMin = new Vector2(18, 56 + 14);
            body.offsetMax = new Vector2(-18, -56 - 14);

            var leftCol = Rect("Left", body);
            Column(leftCol, 0, 290, true);
            var left = Text("Info", leftCol, 13);
            Stretch(left.rectTransform);
            left.alignment = TextAlignmentOptions.TopLeft;
            left.textWrappingMode = TextWrappingModes.Normal;
            left.lineSpacing = 8;

            var rightCol = Rect("Right", body);
            Column(rightCol, 0, 270, false);
            var listHeader = Text("Header", rightCol, 13);
            listHeader.rectTransform.anchorMin = new Vector2(0, 1);
            listHeader.rectTransform.anchorMax = new Vector2(1, 1);
            listHeader.rectTransform.pivot = new Vector2(0, 1);
            listHeader.rectTransform.sizeDelta = new Vector2(0, 24);
            var viewport = Rect("Viewport", rightCol);
            Stretch(viewport);
            viewport.offsetMax = new Vector2(0, -28);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = Color.clear;   // 스크롤 휠 받기
            var list = Rect("List", viewport);
            list.anchorMin = new Vector2(0, 1); list.anchorMax = new Vector2(1, 1); list.pivot = new Vector2(0.5f, 1);
            list.sizeDelta = Vector2.zero;
            var v = list.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6;
            v.padding = new RectOffset(0, 4, 0, 0);
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = rightCol.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = list;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            var empty = Text("EmptyHint", viewport, 11);
            Stretch(empty.rectTransform);
            empty.alignment = TextAlignmentOptions.TopLeft;
            empty.textWrappingMode = TextWrappingModes.Normal;
            empty.color = SOFT;
            empty.text = "출고 대기 박스가 없습니다.\n포장된 박스를 이 도크로 옮겨오세요.";

            var center = Rect("Center", body);
            Stretch(center);
            center.offsetMin = new Vector2(290 + 14, 0);
            center.offsetMax = new Vector2(-(270 + 14), 0);
            var vc = center.gameObject.AddComponent<VerticalLayoutGroup>();
            vc.spacing = 4;
            vc.childAlignment = TextAnchor.MiddleCenter;
            vc.childControlWidth = false; vc.childControlHeight = false;
            vc.childForceExpandWidth = false; vc.childForceExpandHeight = false;
            var cab = Img("Cab", center, U.Hex(0xeceff1));
            cab.rectTransform.sizeDelta = new Vector2(110, 22);
            var cabText = Text("Label", cab.transform, 12);
            Stretch(cabText.rectTransform);
            cabText.alignment = TextAlignmentOptions.Center;
            cabText.color = SOFT;
            cabText.text = "🚚 운전석";
            var frame = Img("GridFrame", center, U.Hex(0x90a4ae));
            frame.rectTransform.sizeDelta = new Vector2(5 * 56 + 6, 4 * 56 + 6);
            var grid = Img("Grid", frame.transform, U.Hex(0xeceff1)).rectTransform;
            grid.anchorMin = grid.anchorMax = grid.pivot = new Vector2(0, 1);
            grid.anchoredPosition = new Vector2(3, -3);
            grid.sizeDelta = new Vector2(5 * 56, 4 * 56);
            RectTransform Layer(string n) { var r = Rect(n, grid); Stretch(r); return r; }
            var cells = Layer("Cells");
            var pieces = Layer("Pieces");
            var preview = Layer("Preview");
            var door = Text("Door", center, 12);
            door.rectTransform.sizeDelta = new Vector2(110, 20);
            door.alignment = TextAlignmentOptions.Center;
            door.color = SOFT;
            door.text = "⬇ 짐칸 문";

            // 드래그 중인 박스 (마우스를 따라다님)
            var ghost = Rect("Ghost", overlay.transform);
            ghost.anchorMin = ghost.anchorMax = new Vector2(0.5f, 0.5f);
            ghost.pivot = new Vector2(0, 1);
            var cg = ghost.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = .75f; cg.blocksRaycasts = false;

            var so = new SerializedObject(view);
            void S(string n, Object o) => so.FindProperty(n).objectReferenceValue = o;
            S("overlay", overlay.gameObject);
            S("title", title); S("rate", rate); S("rateNote", rateNote); S("pay", pay); S("left", left);
            S("listHeader", listHeader); S("emptyHint", empty); S("barFill", fill);
            S("gridFrame", frame.rectTransform); S("grid", grid); S("cellsRoot", cells); S("piecesRoot", pieces); S("previewRoot", preview);
            S("list", list); S("listViewport", viewport); S("ghost", ghost);
            S("departBtn", depart); S("resetBtn", reset); S("closeBtn", close);
            S("cellPrefab", cell); S("piecePrefab", piece); S("miniPrefab", mini); S("cardPrefab", card);
            so.ApplyModifiedPropertiesWithoutUndo();
            overlay.gameObject.SetActive(false);
        }

        // ---------- 프리팹 ----------
        // 칸: 테두리(루트 색) / Fill / Label(✖ ⭐)
        static GameObject CellPrefab()
        {
            var root = Img("LdCell", null, U.Hex(0xcfd8dc));
            var fill = Img("Fill", root.transform, U.Hex(0xfafafa));
            Stretch(fill.rectTransform);
            fill.rectTransform.offsetMin = Vector2.one;
            fill.rectTransform.offsetMax = -Vector2.one;
            var label = Text("Label", root.transform, 18);
            Stretch(label.rectTransform);
            label.alignment = TextAlignmentOptions.Center;
            return root.gameObject;
        }

        // 박스 조각: 바탕(박스 색) / 변 테두리 4개 / Label(타입 아이콘) / Over(겹침 ×n)
        static GameObject PiecePrefab()
        {
            var root = Img("LdPiece", null, Color.white);
            root.raycastTarget = false;
            void Edge(string n, Vector2 min, Vector2 max, Vector2 size)
            {
                var e = Img(n, root.transform, new Color(0, 0, 0, .45f)).rectTransform;
                e.anchorMin = min; e.anchorMax = max; e.pivot = (min + max) / 2;
                e.sizeDelta = size;
                e.anchoredPosition = Vector2.zero;
            }
            Edge("Top", new Vector2(0, 1), Vector2.one, new Vector2(0, 3));
            Edge("Right", new Vector2(1, 0), Vector2.one, new Vector2(3, 0));
            Edge("Bottom", Vector2.zero, new Vector2(1, 0), new Vector2(0, 3));
            Edge("Left", Vector2.zero, new Vector2(0, 1), new Vector2(3, 0));
            var label = Text("Label", root.transform, 15);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(0, 1);
            label.rectTransform.anchoredPosition = new Vector2(3, -2);
            label.rectTransform.sizeDelta = new Vector2(50, 20);
            label.alignment = TextAlignmentOptions.TopLeft;
            var over = Img("Over", root.transform, new Color(0, 0, 0, .55f)).rectTransform;
            over.anchorMin = over.anchorMax = over.pivot = new Vector2(1, 0);
            over.anchoredPosition = new Vector2(-3, 2);
            over.sizeDelta = new Vector2(24, 15);
            var ot = Text("Text", over, 11);
            Stretch(ot.rectTransform);
            ot.alignment = TextAlignmentOptions.Center;
            ot.fontStyle = FontStyles.Bold;
            ot.color = Color.white;
            return root.gameObject;
        }

        static GameObject MiniPrefab()
        {
            var i = Img("LdMini", null, Color.white);
            i.raycastTarget = false;
            return i.gameObject;
        }

        // 대기 박스 카드: Shape(작은 블록) / Info(번호·아이콘·품목·금액)
        static GameObject CardPrefab()
        {
            var root = Img("LdCard", null, U.Hex(0xfafbfc));
            root.gameObject.AddComponent<LayoutElement>().preferredHeight = 62;
            var shape = Rect("Shape", root.transform);
            shape.anchorMin = shape.anchorMax = new Vector2(0, 0.5f);
            shape.pivot = new Vector2(0, 0.5f);
            shape.anchoredPosition = new Vector2(8, 0);
            var info = Text("Info", root.transform, 13);
            Stretch(info.rectTransform);
            info.rectTransform.offsetMin = new Vector2(40, 6);
            info.rectTransform.offsetMax = new Vector2(-8, -6);
            info.alignment = TextAlignmentOptions.Left;
            info.textWrappingMode = TextWrappingModes.Normal;
            return root.gameObject;
        }

        static GameObject SavePrefab(GameObject go, string name)
        {
            var p = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(go);
            return p;
        }

        // ---------- 도우미 ----------
        static RectTransform Rect(string name, Transform parent)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            if (parent) rt.SetParent(parent, false);
            return rt;
        }

        static Image Img(string name, Transform parent, Color c)
        {
            var i = Rect(name, parent).gameObject.AddComponent<Image>();
            i.color = c;
            return i;
        }

        static TextMeshProUGUI Text(string name, Transform parent, float size)
        {
            var t = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.color = INK;
            t.richText = true;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.rectTransform.sizeDelta = new Vector2(100, 40);
            return t;
        }

        static Button Btn(string name, Transform parent, string label, bool primary)
        {
            var bg = Img(name, parent, primary ? U.Hex(0x2e9e57) : Color.white);
            Layout(bg.gameObject, 0, 36);
            bg.rectTransform.sizeDelta = new Vector2(100, 36);
            var b = bg.gameObject.AddComponent<Button>();
            var t = Text("Label", bg.transform, 14);
            Stretch(t.rectTransform);
            t.margin = new Vector4(16, 0, 16, 0);
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold;
            t.color = primary ? Color.white : INK;
            t.text = label;
            // 버튼 폭은 글자에 맞춘다
            var le = bg.GetComponent<LayoutElement>();
            le.preferredWidth = t.GetPreferredValues(label).x + 32;
            return b;
        }

        static LayoutElement Layout(GameObject go, float w, float h)
        {
            var le = go.AddComponent<LayoutElement>();
            if (w > 0) le.preferredWidth = w;
            if (h > 0) le.preferredHeight = h;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(w, h);
            return le;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // 패널 위/아래 띠
        static void Band(RectTransform rt, bool top, float height)
        {
            rt.anchorMin = new Vector2(0, top ? 1 : 0);
            rt.anchorMax = new Vector2(1, top ? 1 : 0);
            rt.pivot = new Vector2(0.5f, top ? 1 : 0);
            rt.sizeDelta = new Vector2(0, height);
            rt.anchoredPosition = Vector2.zero;
        }

        // 본문 왼쪽/오른쪽 세로 칸
        static void Column(RectTransform rt, float x, float width, bool leftSide)
        {
            rt.anchorMin = new Vector2(leftSide ? 0 : 1, 0);
            rt.anchorMax = new Vector2(leftSide ? 0 : 1, 1);
            rt.pivot = new Vector2(leftSide ? 0 : 1, 0.5f);
            rt.sizeDelta = new Vector2(width, 0);
            rt.anchoredPosition = new Vector2(x, 0);
        }
    }
}
