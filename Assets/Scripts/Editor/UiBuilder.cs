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
        const float LdCS = 64;   // 상차 짐칸 한 칸 처음 크기. 실제 크기는 LoadingView가 트럭마다 GridArea에 맞춘다
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
            // 모바일 세로 기준 720×1280. Expand라 캔버스는 항상 이보다 크거나 같아서 기준 크기에 맞춘 UI는 잘리지 않는다
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(720, 1280);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            if (!Object.FindFirstObjectByType<EventSystem>())
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            // 그리는 순서: 월드 라벨 → HUD → 패널 → 디버그 → 상차 패널 (원본 #labels, #hud, #panel, #debug, #loading-overlay)
            var labels = Rect("Labels", canvasGo.transform);
            Stretch(labels);
            var lv = labels.gameObject.AddComponent<LabelView>();
            SetRefs(lv, ("cam", Camera.main), ("labelPrefab", worldLabel));
            // HUD·패널·디버그는 안전 영역(노치·홈 바 제외) 안에 둔다. 월드 라벨과 상차 패널(어두운 배경)은 화면 전체
            var safe = Rect("Safe", canvasGo.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<SafeArea>();
            var hud = Rect("Hud", safe);
            Stretch(hud);
            var panel = BuildPanel(safe, panelText, panelRow, albaRow);
            var debug = BuildDebug(safe, panel);
            BuildHud(hud, panel, debug, orderCard, toast, snack);
            BuildLoading(canvasGo.transform, cell, piece, mini, card);

            var scene = canvasGo.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // ---------- HUD ----------
        static readonly Color PANEL = new Color(1, 1, 1, .94f);

        static void BuildHud(RectTransform hud, PanelView panel, DebugView debug, Image orderCard, Image toast, RectTransform snack)
        {
            var view = hud.gameObject.AddComponent<HudView>();

            // 다이나믹 조이스틱: 누르기 시작한 위치에만 반투명 원 (조이스틱 UI 대신, 화면을 덜 가리게)
            var mark = Img("JoystickMark", hud, new Color(30 / 255f, 90 / 255f, 168 / 255f, .3f));
            mark.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            mark.raycastTarget = false;
            mark.rectTransform.anchorMin = mark.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            mark.rectTransform.sizeDelta = new Vector2(70, 70);
            mark.gameObject.SetActive(false);
            var root = Object.FindFirstObjectByType<GameRoot>();
            if (root) SetRefs(root, ("joystickMark", mark.rectTransform));

            // 이벤트 배너 (상단 가운데, 주문서 아래)
            var banner = Img("EventBanner", hud, U.Hex(0xff7043));
            banner.raycastTarget = false;
            var br = banner.rectTransform;
            br.anchorMin = br.anchorMax = new Vector2(0.5f, 1);
            br.pivot = new Vector2(0.5f, 1);
            br.anchoredPosition = new Vector2(0, -130);
            var hbn = banner.gameObject.AddComponent<HorizontalLayoutGroup>();
            hbn.padding = new RectOffset(16, 16, 6, 6);
            hbn.childControlWidth = hbn.childControlHeight = true;
            hbn.childForceExpandWidth = hbn.childForceExpandHeight = false;
            Fit(banner.gameObject);
            var eventText = Text("Text", br, 19);
            eventText.color = Color.white;
            banner.gameObject.SetActive(false);
            SetRefs(view, ("debug", debug), ("eventBanner", br), ("eventText", eventText));

            // 좌상단 시계
            var clock = Img("ClockBox", hud, PANEL).rectTransform;
            Corner(clock, new Vector2(0, 1), new Vector2(10, -10));
            clock.sizeDelta = new Vector2(280, 0);
            var v = VLayout(clock.gameObject, new RectOffset(12, 12, 10, 10), 4);
            clock.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var line = Rect("Clock", clock);
            Layout(line.gameObject, 0, 30);
            var day = Text("Day", line, 20);
            Stretch(day.rectTransform);
            var time = Text("Time", line, 20);
            Stretch(time.rectTransform);
            time.alignment = TextAlignmentOptions.MidlineRight;
            var phase = Img("PhaseBar", clock, U.Hex(0xe3e8ee));
            Layout(phase.gameObject, 0, 6);
            var phaseFill = Img("Fill", phase.transform, U.Hex(0xe6a100));
            Stretch(phaseFill.rectTransform);
            var money = Text("Money", clock, 26);
            money.fontStyle = FontStyles.Bold;
            Layout(money.gameObject, 0, 36);
            var speed = Rect("Speed", clock);
            Layout(speed.gameObject, 0, 40);
            var hs = speed.gameObject.AddComponent<HorizontalLayoutGroup>();
            hs.spacing = 4;
            hs.childControlWidth = hs.childControlHeight = true;
            hs.childForceExpandWidth = hs.childForceExpandHeight = true;
            var speeds = new[] { "⏸", "x1", "x2", "x4" }.Select(s => SmallBtn(s, speed, 18, Color.white, INK)).ToArray();
            var stats = Text("Stats", clock, 15);
            stats.color = SOFT;
            Layout(stats.gameObject, 0, 22);

            // 상단 주문서
            var strip = Rect("OrderStrip", hud);
            strip.anchorMin = new Vector2(0, 1); strip.anchorMax = new Vector2(1, 1); strip.pivot = new Vector2(0, 1);
            strip.offsetMin = new Vector2(300, -10 - 110);
            strip.offsetMax = new Vector2(-10, -10);
            var hl = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 6;
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = true;
            strip.gameObject.AddComponent<RectMask2D>();

            // 하단 가운데 메뉴 (패널 여는 버튼). 모바일 터치용 40pt 글자라 한 줄에 다 안 들어가서 2×2
            var menu = Rect("MenuBar", hud);
            Corner(menu, new Vector2(0.5f, 0), new Vector2(0, 10));
            menu.sizeDelta = new Vector2(688, MenuH);
            var gm = menu.gameObject.AddComponent<GridLayoutGroup>();
            gm.cellSize = new Vector2(340, 84);
            gm.spacing = new Vector2(8, 8);
            gm.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gm.constraintCount = 2;
            var facility = MenuBtn("🏗️ 시설", menu);
            var upgrade = MenuBtn("⬆️ 업그레이드", menu);
            var albaBtn = MenuBtn("👷 알바", menu);
            var debugBtn = MenuBtn("🛠️ 디버그", menu);
            debugBtn.image.color = U.Hex(0xfffbea);
            debugBtn.GetComponentInChildren<TMP_Text>().color = U.Hex(0x8a6d00);
            SetRefs(view, ("debugBtn", debugBtn));

            // 우측 간식 바 (간식 칸은 SnackView가 설정 수만큼 만든다)
            var bar = Img("SnackBar", hud, PANEL).rectTransform;
            Corner(bar, Vector2.one, new Vector2(-10, -130));
            bar.sizeDelta = new Vector2(90, 0);
            var vb = VLayout(bar.gameObject, new RectOffset(6, 6, 8, 8), 6);
            vb.childAlignment = TextAnchor.UpperCenter;
            vb.childForceExpandWidth = false;
            bar.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var title = Text("Title", bar, 15);
            title.fontStyle = FontStyles.Bold;
            title.color = SOFT;
            title.alignment = TextAlignmentOptions.Center;
            title.text = "간식";
            var ghost = Text("SnackGhost", hud, 34);
            ghost.rectTransform.anchorMin = ghost.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            ghost.rectTransform.sizeDelta = new Vector2(60, 60);
            ghost.alignment = TextAlignmentOptions.Center;
            SetRefs(bar.gameObject.AddComponent<SnackView>(), ("cam", Camera.main), ("panel", panel), ("snackPrefab", snack), ("ghost", ghost));

            float fy = 10 + MenuH + 10;
            BuildForkliftButton(hud, false, fy);                     // 소환/해제
            BuildForkliftButton(hud, true, fy + FlBtn.y + 10);       // 탑승/하차 (그 위)
            BuildLoadButton(hud, fy + (FlBtn.y + 10) * 2);           // 상차하기 (그 위)

            // 메뉴 바로 위 가운데 토스트 (새 토스트가 아래)
            var toasts = Rect("Toasts", hud);
            Corner(toasts, new Vector2(0.5f, 0), new Vector2(0, fy));
            var vt = VLayout(toasts.gameObject, new RectOffset(), 6);
            vt.childAlignment = TextAnchor.LowerCenter;
            Fit(toasts.gameObject);

            SetRefs(view, ("clockDay", day), ("clockTime", time), ("money", money), ("stats", stats),
                ("phaseFill", phaseFill), ("facilityBtn", facility), ("upgradeBtn", upgrade), ("albaBtn", albaBtn), ("panel", panel),
                ("orderStrip", strip), ("toasts", toasts), ("orderCardPrefab", orderCard), ("toastPrefab", toast));
            var so = new SerializedObject(view);
            var arr = so.FindProperty("speedBtns");
            arr.arraySize = speeds.Length;
            for (int i = 0; i < speeds.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = speeds[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 오른쪽 아래(메뉴·토스트 위) 지게차 버튼: 흰 칸에 지게차 그림, 아래 색 띠에 상태. ride: 탑승/하차, 아니면 소환/해제
        const string ForkliftIconPath = "Assets/Sprites/ForkliftIcon.png";
        const float MenuH = 84 * 2 + 8;
        static readonly Vector2 FlBtn = new Vector2(150, 136);

        // 상차 버튼: 지게차 버튼과 같은 크기, 눈에 띄는 노란색. 상차할 수 있는 도크 위에서만 보인다
        static void BuildLoadButton(RectTransform hud, float y)
        {
            var holder = Rect("LoadButton", hud);
            Corner(holder, new Vector2(1, 0), new Vector2(-10, y));
            holder.sizeDelta = FlBtn;
            var view = holder.gameObject.AddComponent<LoadButtonView>();
            var bg = Img("Btn", holder, U.Hex(0xffca28));
            Stretch(bg.rectTransform);
            bg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            bg.type = Image.Type.Sliced;
            var btn = bg.gameObject.AddComponent<Button>();
            var o = bg.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0, 0, 0, .35f); o.effectDistance = new Vector2(2, -2);
            var label = Text("Label", bg.transform, 30);
            Stretch(label.rectTransform);
            label.margin = new Vector4(6, 6, 6, 6);
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true;
            label.fontSizeMin = 18; label.fontSizeMax = 30;
            label.text = "<b>상차하기</b>\n(목적지)";
            SetRefs(view, ("btn", btn), ("label", label));
        }

        static void BuildForkliftButton(RectTransform hud, bool ride, float y)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(ForkliftIconPath);
            if (imp && imp.textureType != TextureImporterType.Sprite)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.SaveAndReimport();
            }
            // 버튼을 숨겨도 상태 판정은 계속 돌아야 해서 뷰는 바깥 칸에 단다
            var holder = Rect(ride ? "ForkliftRide" : "ForkliftSummon", hud);
            Corner(holder, new Vector2(1, 0), new Vector2(-10, y));
            holder.sizeDelta = FlBtn;
            var view = holder.gameObject.AddComponent<ForkliftButtonView>();
            var bg = Img("Btn", holder, Color.white);
            Stretch(bg.rectTransform);
            bg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            bg.type = Image.Type.Sliced;
            var btn = bg.gameObject.AddComponent<Button>();
            bg.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, .25f);
            var icon = Img("Icon", bg.transform, Color.white);
            icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ForkliftIconPath);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            Stretch(icon.rectTransform);
            icon.rectTransform.offsetMin = new Vector2(4, 40);
            icon.rectTransform.offsetMax = new Vector2(-4, -2);
            var state = Img("State", bg.transform, U.Hex(0x1e5aa8));
            state.raycastTarget = false;
            Band(state.rectTransform, false, 38);
            var label = Text("Label", state.transform, 22);
            Stretch(label.rectTransform);
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            SetRefs(view, ("btn", btn), ("stateBg", state), ("label", label));
            var so = new SerializedObject(view);
            so.FindProperty("ride").boolValue = ride;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- 가운데 패널 ----------
        // 패널 글자 크기 (기준 720×1280 세로). 알바 패널은 PanelView의 같은 이름 값
        const float PTitle = 34, PHead = 28, PBody = 24;

        // 시설·업그레이드 내용은 여기서 Hierarchy에 미리 놓는다 (런타임은 값·버튼·SetActive만). 알바 내용은 런타임에 프리팹으로 만든다
        static PanelView BuildPanel(Transform canvas, TMP_Text text, RectTransform row, RectTransform albaRow)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<ApcConfig>("Assets/Data/ApcConfig.asset");
            var holder = Rect("Panel", canvas);
            Stretch(holder);
            var view = holder.gameObject.AddComponent<PanelView>();
            var root = Img("Box", holder, PANEL).rectTransform;
            Center(root);
            var viewport = Rect("Viewport", root);
            Stretch(viewport);
            viewport.offsetMin = new Vector2(16, 14);
            viewport.offsetMax = new Vector2(-16, -14);
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform Content(string name)
            {
                var c = Rect(name, viewport);
                c.anchorMin = new Vector2(0, 1); c.anchorMax = new Vector2(1, 1); c.pivot = new Vector2(0.5f, 1);
                c.sizeDelta = Vector2.zero;
                VLayout(c.gameObject, new RectOffset(0, 0, 0, 0), 4);
                c.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                c.gameObject.SetActive(false);
                return c;
            }
            var facility = Content("Facility");
            var upgrade = Content("Upgrade");
            var alba = Content("Alba");
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = facility;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            var close = Img("Close", root, Color.clear);
            close.rectTransform.anchorMin = close.rectTransform.anchorMax = close.rectTransform.pivot = Vector2.one;
            close.rectTransform.anchoredPosition = new Vector2(-4, -4);
            close.rectTransform.sizeDelta = new Vector2(56, 56);
            var closeBtn = close.gameObject.AddComponent<Button>();
            var ct = Text("Label", close.transform, 32);
            Stretch(ct.rectTransform);
            ct.alignment = TextAlignmentOptions.Center;
            ct.color = INK;
            ct.text = "✕";

            // 글자·줄은 프리팹 인스턴스라 Hierarchy에서 바로 고칠 수 있다
            void P(Transform parent, string s, float size)
            {
                var t = (TMP_Text)PrefabUtility.InstantiatePrefab(text, parent);
                bool heading = size > PBody;
                t.fontSize = size;
                t.text = heading ? $"<b>{s}</b>" : s;
                t.color = heading ? INK : SOFT;
                // 원본 h3 위 여백
                if (size == PHead) t.margin = new Vector4(0, 16, 0, 0);
            }
            void R(Transform parent, string act, string arg, string label = "")
            {
                var r = (RectTransform)PrefabUtility.InstantiatePrefab(row, parent);
                r.name = arg == null ? act : $"{act} {arg}";
                r.Find("Label").GetComponent<TMP_Text>().text = label;
                var da = r.gameObject.AddComponent<DebugAction>();
                da.act = act; da.arg = arg;
            }

            P(facility, "🏗️ 시설", PTitle);
            P(facility, "보관함 (품목)", PHead);
            for (int i = 0; i < cfg.items.Count; i++)
            {
                var it = cfg.items[i];
                var ty = cfg.Type(it.type);
                R(facility, "bin", i.ToString(), $"{it.emoji} {it.name} {Rich.Small($"{ty.emoji}{ty.name} · 가격 {it.price}")}");
            }
            P(facility, "포장대", PHead);
            var map = Object.FindFirstObjectByType<MapRoot>();
            int tables = map ? map.Points(PointKind.Table).Count : 0;
            for (int i = 0; i < tables; i++) R(facility, "table", i.ToString(), $"🎁 포장대 {i + 1}");
            P(facility, "출고 도크 (목적지)", PHead);
            for (int i = 0; i < cfg.destinations.Count; i++)
            {
                var dest = cfg.destinations[i];
                R(facility, "dock", i.ToString(), $"<color={Rich.Hex(dest.color)}>🏁 {dest.name}</color>");
            }
            P(facility, "케어시설", PHead);
            R(facility, "rest", null);
            R(facility, "purifier", null);

            P(upgrade, "⬆️ 업그레이드", PTitle);
            P(upgrade, "공정", PHead);
            P(upgrade, "Lv1~3: 알바 배치 슬롯 1~3명 · Lv4: 자동화 (하차는 자동화 없음)", PBody);
            foreach (var s in cfg.stages) R(upgrade, "stage", s.id);
            P(upgrade, "물류 시설", PHead);
            foreach (var key in FacilityCfg.Keys) R(upgrade, "fac", key);
            P(upgrade, "보관함 + 하차장 + 운송 중인 물품의 합이 최대 보관량에 닿은 품목(⛔)은 더 이상 배송되지 않습니다.", PBody);
            P(upgrade, "상차 미니게임", PHead);
            R(upgrade, "load", "compress");
            R(upgrade, "load", "bonus");
            P(upgrade, "알바/자동화 상차는 고정 적재율이라 미니게임 업그레이드가 적용되지 않습니다.", PBody);

            SetRefs(view, ("root", root.gameObject), ("scroll", scroll), ("facility", facility), ("upgrade", upgrade), ("alba", alba), ("closeBtn", closeBtn),
                ("textPrefab", text), ("rowPrefab", row), ("albaRowPrefab", albaRow));
            return view;
        }

        // 직원 카드: Info(이름·스탯·시급·MT·숙련도) / Bottom(Assign 드롭다운, Status, Fire)
        static GameObject AlbaRowPrefab()
        {
            var root = Img("AlbaRow", null, U.Hex(0xf5f7fa));
            VLayout(root.gameObject, new RectOffset(8, 8, 8, 8), 4);
            var info = Text("Info", root.transform, PBody);
            info.textWrappingMode = TextWrappingModes.Normal;
            info.alignment = TextAlignmentOptions.TopLeft;
            var bottom = Rect("Bottom", root.transform);
            var h = bottom.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;

            var dd = Dropdown("Assign", bottom, 240);

            var status = Text("Status", bottom, 22);
            status.color = SOFT;
            status.overflowMode = TextOverflowModes.Ellipsis;
            var le = status.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 1; le.flexibleWidth = 1;
            var fire = SmallBtn("해고", bottom, PBody, Color.white, U.Hex(0xd63031));
            fire.name = "Fire";
            var hf = fire.gameObject.AddComponent<HorizontalLayoutGroup>();
            hf.padding = new RectOffset(16, 16, 14, 14);
            hf.childControlWidth = hf.childControlHeight = true;
            hf.childForceExpandWidth = hf.childForceExpandHeight = false;
            return root.gameObject;
        }

        static TMP_DefaultControls.Resources UiRes() => new TMP_DefaultControls.Resources
        {
            standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
            knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
            checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
            dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
            mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
        };

        static GameObject Dropdown(string name, Transform parent, float width)
        {
            var dd = TMP_DefaultControls.CreateDropdown(UiRes());
            dd.name = name;
            dd.transform.SetParent(parent, false);
            Layout(dd, width, 56);
            foreach (var t in dd.GetComponentsInChildren<TMP_Text>(true)) { t.font = font; t.fontSize = PBody; }
            var template = dd.GetComponent<TMP_Dropdown>().template;
            template.sizeDelta = new Vector2(0, 400);
            // 기본 항목 높이(20)는 24pt 글자에 작아서 터치하기 쉽게 키운다
            var item = (RectTransform)template.Find("Viewport/Content/Item");
            item.sizeDelta = new Vector2(item.sizeDelta.x, 56);
            var list = (RectTransform)item.parent;
            list.sizeDelta = new Vector2(list.sizeDelta.x, 56);
            return dd;
        }

        static TMP_InputField Input(string name, Transform parent, float width)
        {
            var go = TMP_DefaultControls.CreateInputField(UiRes());
            go.name = name;
            go.transform.SetParent(parent, false);
            Layout(go, width, 56);
            foreach (var t in go.GetComponentsInChildren<TMP_Text>(true)) { t.font = font; t.fontSize = PBody; }
            var f = go.GetComponent<TMP_InputField>();
            f.contentType = TMP_InputField.ContentType.IntegerNumber;
            f.placeholder.GetComponent<TMP_Text>().text = "";
            return f;
        }

        // ---------- 디버그 패널 (원본 #debug) ----------
        static DebugView BuildDebug(Transform canvas, PanelView panel)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<ApcConfig>("Assets/Data/ApcConfig.asset");
            Color gold = U.Hex(0xc9a227), ink = U.Hex(0x8a6d00), btnBg = Color.white;
            var holder = Rect("Debug", canvas);
            Stretch(holder);
            var view = holder.gameObject.AddComponent<DebugView>();
            var box = Img("Box", holder, U.Hex(0xfffdf3)).rectTransform;
            Center(box);
            var outline = box.gameObject.AddComponent<Outline>();
            outline.effectColor = gold; outline.effectDistance = new Vector2(2, -2);
            var viewport = Rect("Viewport", box);
            Stretch(viewport);
            viewport.offsetMin = new Vector2(12, 10);
            viewport.offsetMax = new Vector2(-12, -10);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            VLayout(content.gameObject, new RectOffset(), 4);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = box.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;

            TMP_Text Label(string s, float size, Color c, Transform parent = null, bool bold = false)
            {
                var t = Text("Text", parent ?? content, size);
                t.textWrappingMode = TextWrappingModes.Normal;
                t.color = c;
                t.text = bold ? $"<b>{s}</b>" : s;
                return t;
            }
            void H3(string s) { var t = Label(s, PHead, SOFT, null, true); t.margin = new Vector4(0, 16, 0, 0); }
            Button Act(Transform parent, string label, string act, string arg = null)
            {
                var b = SmallBtn(label, parent, PBody, btnBg, INK);
                b.name = act;
                var h = b.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.padding = new RectOffset(12, 12, 14, 14);
                h.childControlWidth = h.childControlHeight = true;
                h.childForceExpandWidth = h.childForceExpandHeight = false;
                // 한 줄에 다 안 들어가는 버튼이 많다: 줄 폭을 나눠 갖고 글자는 줄바꿈
                var le = b.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 1; le.flexibleWidth = 1;
                b.GetComponentInChildren<TMP_Text>().textWrappingMode = TextWrappingModes.Normal;
                var o = b.gameObject.AddComponent<Outline>();
                o.effectColor = U.Hex(0xd9c27a);
                var da = b.gameObject.AddComponent<DebugAction>();
                da.act = act; da.arg = arg;
                return b;
            }
            Transform Row(params (string label, string act, string arg)[] btns)
            {
                var r = Rect("Row", content);
                var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.spacing = 8;
                h.childControlWidth = h.childControlHeight = true;
                h.childForceExpandWidth = h.childForceExpandHeight = false;
                foreach (var b in btns) Act(r, b.label, b.act, b.arg);
                return r;
            }

            var close = Img("Close", box, Color.clear);
            close.rectTransform.anchorMin = close.rectTransform.anchorMax = close.rectTransform.pivot = Vector2.one;
            close.rectTransform.anchoredPosition = new Vector2(-4, -4);
            close.rectTransform.sizeDelta = new Vector2(56, 56);
            close.gameObject.AddComponent<Button>();
            close.gameObject.AddComponent<DebugAction>().act = "close";
            var ct = Text("Label", close.transform, 32);
            Stretch(ct.rectTransform);
            ct.alignment = TextAlignmentOptions.Center;
            ct.color = INK;
            ct.text = "✕";

            Label("🛠️ 디버그", PTitle, ink, null, true);
            var state = Label("", PBody, SOFT);
            int step = cfg.debug.moneyStep;
            H3($"💰 돈 ({step:N0}원 단위)");
            var moneyRow = Row();
            var money = Input("MoneyInput", moneyRow, 200);
            money.transform.SetAsFirstSibling();
            Act(moneyRow, "+ 추가", "money-add");
            Act(moneyRow, "- 차감", "money-sub");
            Row(("+1,000", "money", "1000"), ("+10,000", "money", "10000"), ("+100,000", "money", "100000"));
            Row(("0원으로", "money-zero", null));
            H3("🕒 낮/밤");
            Row(("낮/밤 전환 (시급·이벤트 판정 포함)", "phase", null));
            Row(("낮/밤만 전환", "phase-only", null), ("+1시간", "hour", null));
            H3($"🎉 이벤트 ({cfg.evt.name})");
            Row(("강제 발동", "event-on", null), ("강제 종료", "event-off", null));
            H3("📋 주문서");
            Row(("+1", "orders", "1"), ("+5", "orders", "5"), ("대기 주문 비우기", "orders-clear", null));
            H3("🚚 트럭");
            Row(("입고 트럭 즉시 호출", "in-trucks", null), ("출고 트럭 즉시 호출", "out-trucks", null));
            H3("👷 알바");
            Row(("MT 전원 100", "mt", "100"), ("MT 전원 -60", "mt", "-60"), ("지원자 새로고침", "reroll", null));

            H3("🧑‍🔧 알바 스탯 편집");
            var empty = Label("고용한 알바가 없습니다", PBody, SOFT);
            var albaBox = Rect("AlbaBox", content);
            VLayout(albaBox.gameObject, new RectOffset(), 4);
            var select = Dropdown("AlbaSelect", albaBox, 0).GetComponent<TMP_Dropdown>();
            var grid = Rect("Grid", albaBox);
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(205, 84);
            gl.spacing = new Vector2(4, 4);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 3;
            foreach (var (key, label) in new[] { ("str", "💪 힘"), ("agi", "🏃 민첩"), ("int", "🧠 지능"), ("sk-lift", "들기 Lv"), ("sk-carry", "나르기 Lv"),
                ("sk-pack", "포장 Lv"), ("sk-inspect", "검수 Lv"), ("mt", "MT"), ("wage", "시급") })
            {
                var cell = Rect(key, grid);
                VLayout(cell.gameObject, new RectOffset(), 1);
                Label(label, 22, SOFT, cell);
                var f = Input("Input", cell, 0);
                var da = f.gameObject.AddComponent<DebugAction>();
                da.act = "af"; da.arg = key;
            }
            // 알바가 없으면 묶음째 숨긴다
            var unpaidRow = Row(("미지급: 아니오", "unpaid", null));
            unpaidRow.SetParent(albaBox, false);
            var unpaid = unpaidRow.GetComponentInChildren<Button>();
            var effects = Label("", PBody, INK, albaBox);
            Row(("이 스탯·숙련도를 모든 알바에 적용", "alba-copy", null)).SetParent(albaBox, false);
            H3("🏗️ 시설");
            Row(("모든 시설 해금 (무료)", "unlock-all", null));

            SetRefs(view, ("root", box.gameObject), ("state", state), ("albaEffects", effects), ("albaEmpty", empty), ("moneyInput", money),
                ("albaBox", albaBox.gameObject), ("albaSelect", select), ("unpaidBtn", unpaid), ("panel", panel));
            return view;
        }

        // 간식 칸 50×50: 아이콘 + MT 회복량
        static GameObject SnackPrefab()
        {
            var root = Img("Snack", null, U.Hex(0xfff7e6));
            Layout(root.gameObject, 70, 70);
            var t = Text("Text", root.transform, 32);
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
            root.gameObject.AddComponent<LayoutElement>().preferredWidth = 170;
            var top = Img("Top", root.transform, Color.gray);
            top.raycastTarget = false;
            Band(top.rectTransform, true, 4);
            var t = Text("Text", root.transform, 16);
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
            var t = Text("Text", root.transform, 18);
            t.color = Color.white;
            return root.gameObject;
        }

        static GameObject PanelTextPrefab()
        {
            var t = Text("PanelText", null, PBody);
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
            h.padding = new RectOffset(12, 12, 10, 10);
            h.spacing = 8;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            var label = Text("Label", root.transform, 26);
            label.textWrappingMode = TextWrappingModes.Normal;
            var le = label.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 1; le.flexibleWidth = 1;
            var ok = Text("Ok", root.transform, PBody);
            ok.fontStyle = FontStyles.Bold;
            ok.color = U.Hex(0x2e9e57);
            var buy = SmallBtn("", root.transform, PBody, U.Hex(0x1e5aa8), Color.white);
            buy.name = "Buy";
            buy.gameObject.AddComponent<CanvasGroup>();
            var hb = buy.gameObject.AddComponent<HorizontalLayoutGroup>();
            hb.padding = new RectOffset(16, 16, 14, 14);
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
            h.padding = new RectOffset(10, 10, 5, 5);
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            Fit(root.gameObject);
            var t = Text("Text", rt, 17);
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
            var b = SmallBtn(label, parent, 40, PANEL, INK);
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

        // 화면 가운데 패널 (시설·업그레이드·알바·디버그 공통). 기준 720×1280에서 위 시계·주문서와 아래 메뉴를 피하는 크기
        static void Center(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0, -10);
            rt.sizeDelta = new Vector2(660, 860);
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
            // 세로 화면 기준. 레이아웃 그룹 없이 앵커로만 배치해서 씬에서 바로 옮길 수 있다
            var safe = Rect("Safe", overlay.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<SafeArea>();
            var panel = Img("Panel", safe, Color.white);
            Stretch(panel.rectTransform);
            panel.rectTransform.offsetMin = new Vector2(12, 12);
            panel.rectTransform.offsetMax = new Vector2(-12, -12);

            // 위: 제목 · 적재율 · 예상 대금
            var top = Img("Top", panel.transform, BAR).rectTransform;
            Band(top, true, 144);
            var title = Text("Title", top, 21);
            Corner(title.rectTransform, new Vector2(0, 1), new Vector2(18, -10));
            title.rectTransform.sizeDelta = new Vector2(640, 36);
            var rate = Text("Rate", top, 20);
            Corner(rate.rectTransform, new Vector2(0, 1), new Vector2(18, -52));
            rate.rectTransform.sizeDelta = new Vector2(270, 36);
            var bar = Img("Bar", top, new Color(0, 0, 0, .18f));
            Corner(bar.rectTransform, new Vector2(0, 1), new Vector2(294, -65));
            bar.rectTransform.sizeDelta = new Vector2(130, 10);
            var fill = Img("Fill", bar.transform, U.Hex(0x4caf50));
            Stretch(fill.rectTransform);
            fill.rectTransform.anchorMax = new Vector2(0, 1);
            var rateNote = Text("RateNote", top, 17);
            Corner(rateNote.rectTransform, new Vector2(0, 1), new Vector2(436, -52));
            rateNote.rectTransform.sizeDelta = new Vector2(240, 36);
            var pay = Text("Pay", top, 20);
            Corner(pay.rectTransform, new Vector2(0, 1), new Vector2(18, -94));
            pay.rectTransform.sizeDelta = new Vector2(640, 44);

            // 아래 가운데: 출발 · 초기화 · 닫기
            var bottom = Img("Bottom", panel.transform, BAR).rectTransform;
            Band(bottom, false, 96);
            Button BottomBtn(string n, string label, bool primary, float x)
            {
                var b = Btn(n, bottom, label, primary);
                var rt = (RectTransform)b.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(x, 0);
                rt.sizeDelta = new Vector2(150, 64);
                return b;
            }
            var depart = BottomBtn("Depart", "🚚 출발", true, -162);
            var reset = BottomBtn("Reset", "↺ 초기화", false, 0);
            var close = BottomBtn("Close", "✕ 닫기", false, 162);

            // 가운데 본문 (위→아래): 짐칸(GridArea, 가로 꽉 차게) / 출고 대기 박스(가로 목록) / 회전 버튼
            var body = Rect("Body", panel.transform);
            Stretch(body);
            body.offsetMin = new Vector2(18, 96 + 14);
            body.offsetMax = new Vector2(-18, -144 - 14);
            const float RotH = 64, WaitH = 140, Gap = 12;

            var waitArea = Rect("Waiting", body);
            waitArea.anchorMin = new Vector2(0, 0); waitArea.anchorMax = new Vector2(1, 0); waitArea.pivot = new Vector2(0.5f, 0);
            waitArea.anchoredPosition = new Vector2(0, RotH + Gap);
            waitArea.sizeDelta = new Vector2(0, WaitH);
            var listHeader = Text("Header", waitArea, 18);
            listHeader.rectTransform.anchorMin = new Vector2(0, 1);
            listHeader.rectTransform.anchorMax = new Vector2(1, 1);
            listHeader.rectTransform.pivot = new Vector2(0, 1);
            listHeader.rectTransform.sizeDelta = new Vector2(0, 26);
            var viewport = Rect("Viewport", waitArea);
            Stretch(viewport);
            viewport.offsetMax = new Vector2(0, -30);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = Color.clear;   // 스크롤 휠 받기
            var list = Rect("List", viewport);
            list.anchorMin = new Vector2(0, 0); list.anchorMax = new Vector2(0, 1); list.pivot = new Vector2(0, 0.5f);
            list.sizeDelta = Vector2.zero;
            var h = list.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            list.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = waitArea.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = list;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            var empty = Text("EmptyHint", viewport, 16);
            Stretch(empty.rectTransform);
            empty.alignment = TextAlignmentOptions.TopLeft;
            empty.textWrappingMode = TextWrappingModes.Normal;
            empty.color = SOFT;
            empty.text = "출고 대기 박스가 없습니다.\n포장된 박스를 이 도크로 옮겨오세요.";

            var rotL = Btn("RotateLeft", body, "↺ 왼쪽 회전", false);
            var rotR = Btn("RotateRight", body, "오른쪽 회전 ↻", false);
            foreach (var (b, x) in new[] { (rotL, -84f), (rotR, 84f) })
            {
                var rt = (RectTransform)b.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0);
                rt.anchoredPosition = new Vector2(x, 0);
                rt.sizeDelta = new Vector2(160, RotH);
            }

            // 짐칸 크기(칸 크기 포함)는 트럭마다 LoadingView가 GridArea에 맞춘다. 운전석·문은 짐칸 테두리에 붙어서 따라간다
            var gridArea = Rect("GridArea", body);
            Stretch(gridArea);
            gridArea.offsetMin = new Vector2(0, RotH + Gap + WaitH + Gap);
            var frame = Img("GridFrame", gridArea, U.Hex(0x90a4ae));
            Corner(frame.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -34));
            frame.rectTransform.sizeDelta = new Vector2(5 * LdCS + 6, 4 * LdCS + 6);
            var cab = Img("Cab", frame.transform, U.Hex(0xeceff1));
            cab.rectTransform.anchorMin = cab.rectTransform.anchorMax = new Vector2(0.5f, 1);
            cab.rectTransform.pivot = new Vector2(0.5f, 0);
            cab.rectTransform.anchoredPosition = new Vector2(0, 4);
            cab.rectTransform.sizeDelta = new Vector2(140, 30);
            var cabText = Text("Label", cab.transform, 17);
            Stretch(cabText.rectTransform);
            cabText.alignment = TextAlignmentOptions.Center;
            cabText.color = SOFT;
            cabText.text = "🚚 운전석";
            var grid = Img("Grid", frame.transform, U.Hex(0xeceff1)).rectTransform;
            grid.anchorMin = grid.anchorMax = grid.pivot = new Vector2(0, 1);
            grid.anchoredPosition = new Vector2(3, -3);
            grid.sizeDelta = new Vector2(5 * LdCS, 4 * LdCS);
            RectTransform Layer(string n) { var r = Rect(n, grid); Stretch(r); return r; }
            var cells = Layer("Cells");
            var pieces = Layer("Pieces");
            var preview = Layer("Preview");
            RectTransform Below(RectTransform rt, float x, float y)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0);
                rt.pivot = new Vector2(0.5f, 1);
                rt.anchoredPosition = new Vector2(x, y);
                return rt;
            }
            var door = Text("Door", frame.transform, 17);
            Below(door.rectTransform, 0, -4).sizeDelta = new Vector2(140, 26);
            door.alignment = TextAlignmentOptions.Center;
            door.color = SOFT;
            door.text = "⬇ 짐칸 문";

            // 드래그 중인 박스 (마우스를 따라다님)
            // 끌기 블록은 패널 안에 둬야 패널과 같은 비율로 줄어든다 (마지막 자식이라 맨 위에 그려진다)
            var ghost = Rect("Ghost", panel.transform);
            ghost.anchorMin = ghost.anchorMax = new Vector2(0.5f, 0.5f);
            ghost.pivot = new Vector2(0, 1);
            var cg = ghost.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = .75f; cg.blocksRaycasts = false;

            var so = new SerializedObject(view);
            void S(string n, Object o) => so.FindProperty(n).objectReferenceValue = o;
            S("overlay", overlay.gameObject);
            S("title", title); S("rate", rate); S("rateNote", rateNote); S("pay", pay);
            S("listHeader", listHeader); S("emptyHint", empty); S("barFill", fill);
            S("gridFrame", frame.rectTransform); S("grid", grid); S("cellsRoot", cells); S("piecesRoot", pieces); S("previewRoot", preview);
            S("list", list); S("listViewport", viewport); S("ghost", ghost);
            S("departBtn", depart); S("resetBtn", reset); S("closeBtn", close);
            S("rotateLeftBtn", rotL); S("rotateRightBtn", rotR);
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
            var label = Text("Label", root.transform, 23);
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
            var label = Text("Label", root.transform, 20);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(0, 1);
            label.rectTransform.anchoredPosition = new Vector2(3, -2);
            label.rectTransform.sizeDelta = new Vector2(60, 26);
            label.alignment = TextAlignmentOptions.TopLeft;
            var over = Img("Over", root.transform, new Color(0, 0, 0, .55f)).rectTransform;
            over.anchorMin = over.anchorMax = over.pivot = new Vector2(1, 0);
            over.anchoredPosition = new Vector2(-3, 2);
            over.sizeDelta = new Vector2(34, 22);
            var ot = Text("Text", over, 16);
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

        // 대기 박스 카드 (가로 목록): 위 Shape(작은 블록) / 아래 Info(번호·아이콘·금액). 폭·Info 위 여백은 LoadingView가 모양에 맞춘다
        static GameObject CardPrefab()
        {
            var root = Img("LdCard", null, U.Hex(0xfafbfc));
            root.gameObject.AddComponent<LayoutElement>().preferredWidth = 110;
            var shape = Rect("Shape", root.transform);
            shape.anchorMin = shape.anchorMax = shape.pivot = new Vector2(0.5f, 1);
            shape.anchoredPosition = new Vector2(0, -8);
            var info = Text("Info", root.transform, 16);
            Stretch(info.rectTransform);
            info.rectTransform.offsetMin = new Vector2(4, 4);
            info.rectTransform.offsetMax = new Vector2(-4, -40);
            info.alignment = TextAlignmentOptions.Top;
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
            var t = Text("Label", bg.transform, 19);
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

    }
}
