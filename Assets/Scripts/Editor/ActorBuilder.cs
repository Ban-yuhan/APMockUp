// 에디터 전용: 캐릭터 애니메이션 · 플레이어/범위 링 프리팹 · 게임 오브젝트를 씬에 만든다
// 서 있는 자세 = 걷기/들고 걷기 클립에서 양발이 땅에 붙은 프레임 (0프레임, 발 뼈 높이로 확인함)
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace APC.EditorTools
{
    public static class ActorBuilder
    {
        const string CharDir = "Assets/Resources/3D/Charactor/";
        const string AnimDir = "Assets/Animations";
        const string PrefabDir = "Assets/Prefabs";
        const int IDLE_FRAME = 0;

        // Rabbit 모델(알바)은 뼈대가 y = -3.32에 있어서 Model을 올려 발을 바닥에 맞춘다. Basic(사장)은 0
        public const float RABBIT_FOOT_Y = 3.32f;

        [MenuItem("APC/캐릭터·플레이어 만들기")]
        public static void BuildAll()
        {
            Folder(AnimDir); Folder(PrefabDir);
            var rabbit = Controller("Rabbit", "rabbit_walk2", "rabbit_carry");
            var ctrl = Controller("Basic", "basics_walk", "basics_carry");
            var player = CharPrefab("Player", "basics_walk", ctrl, 0, true);
            var alba = CharPrefab("Alba", "rabbit_walk2", rabbit, RABBIT_FOOT_Y, false);
            var ring = RingPrefab();
            AssetDatabase.SaveAssets();
            PlaceInScene(player, alba, ring);
        }

        // ---------- 애니메이션 ----------
        static AnimatorController Controller(string name, string walkFbx, string carryFbx)
        {
            var walk = LoopClip(walkFbx, "Walk");
            var carry = LoopClip(carryFbx, "CarryWalk");
            var idle = Pose(walk, $"{AnimDir}/{name}_Idle.anim");
            var carryIdle = Pose(carry, $"{AnimDir}/{name}_CarryIdle.anim");

            string path = $"{AnimDir}/{name}.controller";
            AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Carrying", AnimatorControllerParameterType.Bool);
            var sm = ctrl.layers[0].stateMachine;
            void State(string n, Motion m, bool moving, bool carrying, bool def = false)
            {
                var s = sm.AddState(n);
                s.motion = m;
                if (def) sm.defaultState = s;
                var t = sm.AddAnyStateTransition(s);
                t.AddCondition(moving ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "Moving");
                t.AddCondition(carrying ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "Carrying");
                t.hasExitTime = false;
                t.duration = 0.1f;
                t.canTransitionToSelf = false;
            }
            State("Idle", idle, false, false, true);
            State("Walk", walk, true, false);
            State("CarryIdle", carryIdle, false, true);
            State("CarryWalk", carry, true, true);
            return ctrl;
        }

        // FBX의 클립 하나를 이름을 바꾸고 반복으로
        static AnimationClip LoopClip(string fbx, string clipName)
        {
            string path = CharDir + fbx + ".fbx";
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            var clips = imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations;
            if (clips[0].name != clipName || !clips[0].loopTime)
            {
                clips[0].name = clipName;
                clips[0].loopTime = true;
                imp.clipAnimations = new[] { clips[0] };
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => c.name == clipName);
        }

        // 한 프레임을 그대로 유지하는 자세 클립
        static AnimationClip Pose(AnimationClip src, string path)
        {
            var clip = new AnimationClip { frameRate = src.frameRate };
            float t = IDLE_FRAME / src.frameRate;
            foreach (var b in AnimationUtility.GetCurveBindings(src))
            {
                float v = AnimationUtility.GetEditorCurve(src, b).Evaluate(t);
                AnimationUtility.SetEditorCurve(clip, b, AnimationCurve.Constant(0, 1 / src.frameRate, v));
            }
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        // ---------- 프리팹 ----------
        // 캐릭터: 루트(CharacterView) / Model(모델 + Animator, footY만큼 올려 발을 바닥에) / HandAnchor(손 물건 슬롯 8칸)
        // 플레이어(사장) = Basic, 알바 = Rabbit
        static GameObject CharPrefab(string name, string fbx, AnimatorController ctrl, float footY, bool isPlayer)
        {
            var root = new GameObject(name);
            var view = root.AddComponent<CharacterView>();
            view.isPlayer = isPlayer;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CharDir + fbx + ".fbx"));
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = new Vector3(0, footY, 0);
            var anim = model.GetComponent<Animator>();
            if (!anim) anim = model.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            // 원본 손 스택 위치 (0, 0.85, 0.42), 로컬 +z = 앞
            var hand = new GameObject("HandAnchor").transform;
            hand.SetParent(root.transform, false);
            hand.localPosition = new Vector3(0, 0.85f, 0.42f);
            for (int i = 0; i < 8; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(c.GetComponent<Collider>());
                c.name = "Slot_" + i;
                c.transform.SetParent(hand, false);
                c.SetActive(false);
            }
            var so = new SerializedObject(view);
            so.FindProperty("animator").objectReferenceValue = anim;
            so.FindProperty("handAnchor").objectReferenceValue = hand;
            so.ApplyModifiedPropertiesWithoutUndo();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        // 컨베이어 위 물품: 흰 큐브 (색은 FlyerView가 바꾼다)
        static GameObject FlyerPrefab()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Flyer";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = MapBuilder.MaterialAsset(1);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/Flyer.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        // 범위 링: 루트(ZoneRing) / Fill(채운 원) / Line(테두리)
        static GameObject RingPrefab()
        {
            var mat = RingMaterial();
            var root = new GameObject("ZoneRing");
            var ring = root.AddComponent<ZoneRing>();
            MeshFilter Part(string n, float y)
            {
                var go = new GameObject(n, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(0, y, 0);
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                return go.GetComponent<MeshFilter>();
            }
            ring.fillMesh = Part("Fill", 0);
            ring.lineMesh = Part("Line", 0.005f);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/ZoneRing.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        // 원본 MeshBasicMaterial(transparent, depthWrite false)
        static Material RingMaterial()
        {
            const string path = "Assets/Materials/ZoneRing.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ---------- 씬 ----------
        static void PlaceInScene(GameObject playerPrefab, GameObject albaPrefab, GameObject ringPrefab)
        {
            var map = Object.FindFirstObjectByType<MapRoot>();
            var cam = Camera.main;
            foreach (var n in new[] { "Game", "Actors", "Zones" }) { var old = GameObject.Find(n); if (old) Object.DestroyImmediate(old); }

            var actors = new GameObject("Actors").transform;
            // 알바는 고용 수만큼 생기므로 프리팹으로 만든다
            Set(actors.gameObject.AddComponent<AlbaView>(), "albaPrefab", albaPrefab.GetComponent<CharacterView>());
            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, actors);
            var start = map.GetComponentsInChildren<LayoutPoint>().First(p => p.kind == PointKind.PlayerStart);
            player.transform.position = start.transform.position;

            var zones = new GameObject("Zones");
            var zv = zones.AddComponent<ZoneView>();
            var marker = ((GameObject)PrefabUtility.InstantiatePrefab(ringPrefab, zones.transform)).GetComponent<ZoneRing>();
            marker.name = "MoveMarker";
            marker.fill = false;
            marker.outer = 0.45f; marker.inner = 0.3f;
            marker.color = U.Hex(0x1e5aa8);
            marker.fillAlpha = 0; marker.lineAlpha = 0.8f;
            marker.gameObject.SetActive(false);
            Set(zv, "ringPrefab", ringPrefab.GetComponent<ZoneRing>());
            Set(zv, "moveMarker", marker);

            var game = new GameObject("Game");
            var root = game.AddComponent<GameRoot>();
            Set(root, "config", AssetDatabase.LoadAssetAtPath<ApcConfig>("Assets/Data/ApcConfig.asset"));
            Set(root, "map", map);
            Set(root, "rig", cam.GetComponent<CameraRig>());
            Set(root, "actions", AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions"));
            // UI를 먼저 만들었으면 조이스틱 원을 연결 (UI 만들기 메뉴도 연결한다)
            var mark = GameObject.Find("UI")?.transform.Find("Hud/JoystickMark");
            if (mark) Set(root, "joystickMark", mark);
            var dn = game.AddComponent<DayNightView>();
            Set(dn, "cam", cam);
            Set(dn, "outsideGround", map.transform.Find("Floor/Outside").GetComponent<Renderer>());
            var fv = game.AddComponent<FacilityView>();
            Set(fv, "map", map);
            Set(fv, "opaque", MapBuilder.MaterialAsset(1));
            Set(fv, "transparent", MapBuilder.MaterialAsset(0.5f));
            Set(game.AddComponent<FlyerView>(), "flyerPrefab", FlyerPrefab().GetComponent<Renderer>());

            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            EditorSceneManager.SaveScene(map.gameObject.scene);
        }

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Folder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder("Assets", path.Substring("Assets/".Length));
        }
    }
}
