// 에디터 전용: 코스튬 목록·연기 재질·드레스룸 UI·프리뷰 무대를 만든다
// 이름으로 찾아 없으면 만들고 있으면 설정만 고친다 (다시 실행해도 중복 없음, 목록에 더한 코스튬은 지우지 않음)
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace APC.EditorTools
{
    public static class CostumeBuilder
    {
        const string CatalogPath = "Assets/Resources/CostumeCatalog.asset";
        const string SmokePath = "Assets/Resources/PoofSmoke.mat";
        const string PreviewRtPath = "Assets/Data/CostumePreview.renderTexture";
        const string FontPath = "Assets/Fonts/malgun SDF.asset";
        static readonly Color INK = new Color(31 / 255f, 41 / 255f, 51 / 255f);
        static readonly Color SOFT = new Color(91 / 255f, 103 / 255f, 117 / 255f);
        static readonly Color PANEL = new Color(1, 1, 1, .96f);
        static TMP_FontAsset font;

        [MenuItem("APC/코스튬 만들기")]
        public static void BuildAll()
        {
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            var catalog = Catalog();
            SmokeMaterial();
            var rt = PreviewTexture();
            var (cam, stage) = PreviewStage(rt);
            BuildUi(rt, cam, stage);
            AssetDatabase.SaveAssets();
            Selection.activeObject = catalog;
            Debug.Log($"[CostumeBuilder] 코스튬 {catalog.costumes.Count}종, 드레스룸 UI 갱신 완료. 씬을 저장하세요.");
        }

        // ---------- 데이터 ----------
        static CostumeCatalog Catalog()
        {
            var cat = AssetDatabase.LoadAssetAtPath<CostumeCatalog>(CatalogPath);
            if (!cat)
            {
                cat = ScriptableObject.CreateInstance<CostumeCatalog>();
                cat.costumes.Add(Entry("basic", "베이직", "basics_walk", "basics_carry", 0));
                cat.costumes.Add(Entry("rabbit", "토끼", "rabbit_walk2", "rabbit_carry", ActorBuilder.RABBIT_FOOT_Y));
                cat.costumes.Add(Entry("macaron", "마카롱", "macaron_walk", "macaron_carry", 0));
                AssetDatabase.CreateAsset(cat, CatalogPath);
            }
            // 컨트롤러가 빈 항목은 걷기·들기 FBX로 만든다 (이미 있는 컨트롤러 파일은 그대로 쓴다)
            foreach (var c in cat.costumes.Where(c => !c.controller && c.walkModelPrefab && c.carryModelPrefab))
            {
                string name = char.ToUpper(c.costumeId[0]) + c.costumeId.Substring(1);
                c.controller = AssetDatabase.LoadAssetAtPath<AnimatorController>($"Assets/Animations/{name}.controller")
                    ?? ActorBuilder.Controller(name, c.walkModelPrefab.name, c.carryModelPrefab.name);
            }
            EditorUtility.SetDirty(cat);
            return cat;
        }

        static CostumeData Entry(string id, string name, string walk, string carry, float footY) => new CostumeData
        {
            costumeId = id,
            displayName = name,
            walkModelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ActorBuilder.FbxPath(walk)),
            carryModelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ActorBuilder.FbxPath(carry)),
            footOffsetY = footY,
        };

        // 만화풍 연기: 흰색, 반사 없이
        static void SmokeMaterial()
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(SmokePath)) return;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", new Color(0.97f, 0.97f, 0.99f));
            m.SetFloat("_Smoothness", 0);
            m.SetColor("_EmissionColor", new Color(0.35f, 0.35f, 0.38f));
            m.EnableKeyword("_EMISSION");
            AssetDatabase.CreateAsset(m, SmokePath);
        }

        static RenderTexture PreviewTexture()
        {
            var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(PreviewRtPath);
            if (rt) return rt;
            rt = new RenderTexture(512, 512, 24) { name = "CostumePreview", antiAliasing = 2 };
            AssetDatabase.CreateAsset(rt, PreviewRtPath);
            return rt;
        }

        // ---------- 프리뷰 무대 (맵 아래 멀리) ----------
        static (Camera, Transform) PreviewStage(RenderTexture rt)
        {
            var rootGo = GameObject.Find("CostumePreview");
            if (!rootGo)
            {
                rootGo = new GameObject("CostumePreview");
                Undo.RegisterCreatedObjectUndo(rootGo, "Build Costume");
            }
            var root = rootGo.transform;
            root.position = new Vector3(0, -1000, 0);

            var stage = Node(root, "Stage");
            stage.localPosition = Vector3.zero;
            stage.localRotation = Quaternion.identity;

            // 모델 앞(+z)에서 몸통을 본다
            var camT = Node(root, "PreviewCamera");
            camT.localPosition = new Vector3(0, 0.85f, 3.2f);
            camT.localRotation = Quaternion.LookRotation(new Vector3(0, 0.75f, 0) - camT.localPosition);
            var cam = Comp<Camera>(camT.gameObject);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.93f, 0.95f, 0.98f);
            cam.fieldOfView = 30;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20;
            cam.targetTexture = rt;
            cam.enabled = false;

            // 밤에도 프리뷰가 어둡지 않게 가까이만 비추는 조명
            var lightT = Node(root, "PreviewLight");
            lightT.localPosition = new Vector3(0.8f, 2f, 2.5f);
            var light = Comp<Light>(lightT.gameObject);
            light.type = LightType.Point;
            light.range = 8;
            light.intensity = 3;
            return (cam, stage);
        }

        // ---------- UI ----------
        static void BuildUi(RenderTexture rt, Camera cam, Transform stage)
        {
            var safe = GameObject.Find("UI/Safe");
            if (!safe)
            {
                Debug.LogError("UI/Safe not found. APC/UI 만들기를 먼저 실행하세요.");
                return;
            }

            // 다른 패널보다 위에 그린다 (항상 켜 두는 홀더 + 여닫는 Root)
            var holder = Child(safe.transform, "Costume");
            holder.SetAsLastSibling();
            Stretch(holder);
            var view = Comp<CostumePanelView>(holder.gameObject);

            var root = Child(holder, "Root");
            Stretch(root);
            Comp<Image>(root.gameObject).color = new Color(0, 0, 0, 0.45f); // 뒤 누름 막기

            var box = Child(root, "Box");
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
            box.anchoredPosition = new Vector2(0, -10);
            box.sizeDelta = new Vector2(660, 860);
            Comp<Image>(box.gameObject).color = PANEL;
            var v = Comp<VerticalLayoutGroup>(box.gameObject);
            v.padding = new RectOffset(16, 16, 14, 16);
            v.spacing = 10;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var title = Text(box, "Title", "👕 <b>드레스룸</b>", 30, INK);
            title.margin = new Vector4(4, 4, 60, 0);
            Le(title).preferredHeight = 48;

            var close = Child(box, "Close");
            Le(close).ignoreLayout = true;
            close.anchorMin = close.anchorMax = close.pivot = Vector2.one;
            close.anchoredPosition = new Vector2(-4, -4);
            close.sizeDelta = new Vector2(56, 56);
            Comp<Image>(close.gameObject).color = Color.clear;
            var closeBtn = Comp<Button>(close.gameObject);
            var ct = Text(close, "Label", "✕", 32, INK);
            Stretch(ct.rectTransform);
            ct.alignment = TextAlignmentOptions.Center;

            // 3D 프리뷰
            var previewHolder = Child(box, "PreviewHolder");
            Le(previewHolder).preferredHeight = 420;
            var preview = Child(previewHolder, "Preview");
            preview.anchorMin = preview.anchorMax = preview.pivot = new Vector2(0.5f, 0.5f);
            preview.anchoredPosition = Vector2.zero;
            preview.sizeDelta = new Vector2(420, 420);
            var raw = Comp<RawImage>(preview.gameObject);
            raw.texture = rt;
            raw.raycastTarget = false;

            var current = Text(box, "Current", "현재: 토끼", 24, INK);
            current.alignment = TextAlignmentOptions.Center;
            Le(current).preferredHeight = 34;

            // 코스튬 목록 (가로 스크롤)
            var list = Child(box, "List");
            Le(list).preferredHeight = 250;
            Le(list).flexibleHeight = 1;
            Comp<Image>(list.gameObject).color = new Color(0.94f, 0.95f, 0.97f);
            var scroll = Comp<ScrollRect>(list.gameObject);
            var viewport = Child(list, "Viewport");
            Stretch(viewport);
            Comp<Image>(viewport.gameObject).color = Color.clear; // 칸 사이를 끌어도 스크롤
            Comp<RectMask2D>(viewport.gameObject);
            var content = Child(viewport, "Content");
            content.anchorMin = Vector2.zero;
            content.anchorMax = new Vector2(0, 1);
            content.pivot = new Vector2(0, 0.5f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var h = Comp<HorizontalLayoutGroup>(content.gameObject);
            h.padding = new RectOffset(10, 10, 10, 10);
            h.spacing = 12;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            Comp<ContentSizeFitter>(content.gameObject).horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;

            var items = content.Cast<Transform>().Where(t => t.name.StartsWith("Item_")).Select(t => Item((RectTransform)t)).ToList();
            if (items.Count == 0) items.Add(Item(Child(content, "Item_0")));

            // HUD 좌상단 시계 상자 아래에 플레이어 코스튬 버튼
            Button playerBtn = null;
            var clock = safe.transform.Find("Hud/ClockBox");
            if (clock)
            {
                var pb = Child(clock, "CostumeBtn");
                pb.SetAsLastSibling();
                Le(pb).preferredHeight = 40;
                Comp<Image>(pb.gameObject).color = new Color(0.45f, 0.35f, 0.75f);
                playerBtn = Comp<Button>(pb.gameObject);
                var pt = Text(pb, "Label", "👕 플레이어 코스튬", 18, Color.white);
                Stretch(pt.rectTransform);
                pt.alignment = TextAlignmentOptions.Center;
                pt.fontStyle = FontStyles.Bold;
            }
            else Debug.LogWarning("UI/Safe/Hud/ClockBox not found: 플레이어 코스튬 버튼을 만들지 않았습니다.");

            var so = new SerializedObject(view);
            so.FindProperty("root").objectReferenceValue = root.gameObject;
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("currentText").objectReferenceValue = current;
            so.FindProperty("closeBtn").objectReferenceValue = closeBtn;
            so.FindProperty("playerButton").objectReferenceValue = playerBtn;
            so.FindProperty("previewCam").objectReferenceValue = cam;
            so.FindProperty("previewStage").objectReferenceValue = stage;
            var arr = so.FindProperty("items");
            arr.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            root.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(safe.scene);
        }

        // 목록 한 칸: 아이콘 / 이름 / [적용]
        static CostumeItemUI Item(RectTransform root)
        {
            var bg = Comp<Image>(root.gameObject);
            bg.color = new Color(0.97f, 0.98f, 0.99f);
            Le(root).preferredWidth = 150;
            var v = Comp<VerticalLayoutGroup>(root.gameObject);
            v.padding = new RectOffset(8, 8, 8, 8);
            v.spacing = 6;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var iconBox = Child(root, "IconBox");
            Le(iconBox).preferredHeight = 110;
            var icon = Comp<Image>(Child(iconBox, "Icon").gameObject);
            Stretch(icon.rectTransform);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var ph = Text(iconBox, "Placeholder", "👕", 56, INK);
            Stretch(ph.rectTransform);
            ph.alignment = TextAlignmentOptions.Center;

            var name = Text(root, "Name", "토끼", 20, INK);
            name.alignment = TextAlignmentOptions.Center;
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            Le(name).preferredHeight = 28;

            var btnT = Child(root, "Button");
            Le(btnT).preferredHeight = 48;
            Comp<Image>(btnT.gameObject).color = new Color(0.12f, 0.35f, 0.65f);
            var btn = Comp<Button>(btnT.gameObject);
            var bt = Text(btnT, "Label", "적용", 20, Color.white);
            Stretch(bt.rectTransform);
            bt.alignment = TextAlignmentOptions.Center;

            var comp = Comp<CostumeItemUI>(root.gameObject);
            var so = new SerializedObject(comp);
            so.FindProperty("icon").objectReferenceValue = icon;
            so.FindProperty("placeholder").objectReferenceValue = ph;
            so.FindProperty("nameText").objectReferenceValue = name;
            so.FindProperty("background").objectReferenceValue = bg;
            so.FindProperty("button").objectReferenceValue = btn;
            so.FindProperty("buttonText").objectReferenceValue = bt;
            so.ApplyModifiedPropertiesWithoutUndo();
            return comp;
        }

        // ---------- 찾기/만들기 ----------
        static RectTransform Child(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (!t)
            {
                var go = new GameObject(name, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, "Build Costume");
                go.transform.SetParent(parent, false);
                t = go.transform;
            }
            return (RectTransform)t;
        }

        // 3D 오브젝트용 (RectTransform 없이)
        static Transform Node(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t) return t;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Build Costume");
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static T Comp<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c ? c : Undo.AddComponent<T>(go);
        }

        static LayoutElement Le(Component c) => Comp<LayoutElement>(c.gameObject);

        static TMP_Text Text(Transform parent, string name, string content, float size, Color color)
        {
            var go = Child(parent, name).gameObject;
            var t = go.GetComponent<TMP_Text>();
            if (!t) t = Undo.AddComponent<TextMeshProUGUI>(go);
            if (font) t.font = font;
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.raycastTarget = false;
            return t;
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
