// 에디터 전용 배치 도구: 기본 배치(DefaultLayout) 좌표대로 맵 오브젝트를 씬에 한 번 만든다 (원본 World.buildStatic)
// 만든 뒤에는 씬 오브젝트가 기준이다. 사용자가 Hierarchy에서 직접 옮기고 모델을 바꾼다
// 원본 그룹의 로컬 좌표 변환: 회전하는 시설(보관함·포장대)은 로컬 x 반전, 그 외(월드·도크·트럭)는 z 반전.
//   전체 결과가 원본 월드를 z 반전한 것과 같아진다 (CoordUtil 규칙)
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace APC.EditorTools
{
    public static class MapBuilder
    {
        const string MatDir = "Assets/Materials/APC";
        const string TruckPrefab = "Assets/Prefabs/Truck.prefab";
        const string TruckModel = "Assets/Resources/3D/AI_Truck/AI_Truck.fbx";

        // 원본 COLORS
        const int GROUND = Palette.GROUND, CARDBOARD = Palette.CARDBOARD, BOX = Palette.BOX, BROKEN = Palette.BROKEN,
            LOCKED = Palette.LOCKED, WOOD = Palette.WOOD, METAL_DARK = 0x455a64, ARM = 0xf39c12, BELT = 0x37474f;

        static bool persist;
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        [MenuItem("APC/기본 배치로 맵 만들기")]
        static void BuildMenu()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<ApcConfig>("Assets/Data/ApcConfig.asset");
            var root = Build(DefaultLayout.Make(cfg), cfg, true);
            Undo.RegisterCreatedObjectUndo(root, "APC 맵 만들기");
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
        }

        // assets = false: 테스트용. 재질 에셋·트럭 프리팹을 만들지 않는다
        public static GameObject Build(LayoutData l, ApcConfig cfg, bool assets)
        {
            persist = assets;
            mats.Clear();
            var root = new GameObject("Map");
            var map = root.AddComponent<MapRoot>();
            map.layoutName = l.name;
            map.mapSize = new Vector2((float)l.mapW, (float)l.mapD);
            map.interior = new Vector4((float)l.ix1, (float)l.iz1, (float)l.ix2, (float)l.iz2);
            var R = root.transform;

            // 바닥: 바깥 땅 (밤에 어두워짐) + 창고 내부 + 구역
            var floor = Group(R, "Floor");
            Floor(floor, "Outside", -30, -20, l.mapW + 30, l.mapD + 20, 0xb4bcc6, -0.01f);
            Floor(floor, "Interior", l.ix1, l.iz1, l.ix2, l.iz2, GROUND, 0.002f);
            foreach (var a in l.areas) Floor(floor, a.name, a.x1, a.z1, a.x2, a.z2, ColorUtility.ToHtmlStringRGB(a.color), 0.01f);
            var w = l.waiting;
            Floor(floor, "WaitingFloor", w.x - w.w / 2, w.z - w.d / 2, w.x + w.w / 2, w.z + w.d / 2, 0xe6e6e6, 0.01f);

            var walls = Group(R, "Walls");
            for (int i = 0; i < l.walls.Count; i++)
            {
                var wl = l.walls[i];
                double dx = wl.x2 - wl.x1, dz = wl.z2 - wl.z1, len = System.Math.Sqrt(dx * dx + dz * dz);
                var m = Prim(PrimitiveType.Cube, walls, "Wall_" + (i + 1), CoordUtil.ToWorld((wl.x1 + wl.x2) / 2, (wl.z1 + wl.z2) / 2, 0.7f),
                    new Vector3(0.3f, 1.4f, (float)len + 0.3f), 0xb0b7c0, world: true);
                m.transform.rotation = Quaternion.Euler(0, (float)(System.Math.Atan2(dx, -dz) * 180 / System.Math.PI), 0);
            }

            // 입고 트럭 주차공간 (트럭 짐칸 중심, 회전 = 뒷문 방향)
            var inTrucks = Group(R, "InTrucks");
            for (int i = 0; i < l.inTrucks.Count; i++)
            {
                var s = l.inTrucks[i];
                var t = Point(inTrucks, "InTruck_" + (i + 1), PointKind.InTruck, s.Pos, CoordUtil.TruckYaw(s.rot));
                Marker(t, "Out", s.outPt);
                Truck(t, "Truck");
            }

            // 하차장 팔레트
            {
                var s = l.staging;
                var t = Point(R, "Staging", PointKind.Staging, s.Pos, 0);
                Markers(t, s);
                Prim(PrimitiveType.Cube, t, "Pallet", new Vector3(0, 0.1f, 0), new Vector3(2.6f, 0.2f, 2.6f), WOOD);
                CubeSlots(Group(t, "Cubes"), 3, 3, 2, 0.6f, 0.5f, CARDBOARD);
            }

            // 보관함 (로컬 +z = 앞면)
            var bins = Group(R, "Bins");
            for (int i = 0; i < l.bins.Count; i++)
            {
                var s = l.bins[i];
                var it = cfg.items.Find(x => x.id == s.item);
                var t = Point(bins, $"Bin_{i + 1}_{s.item}", PointKind.Bin, s.Pos, CoordUtil.FaceYaw(s.rot), s.item, s.cost);
                Markers(t, s);
                bool open = s.cost == 0;
                var col = cfg.Type(it.type).color;
                Prim(PrimitiveType.Cube, t, "Body", Fx(0, 0.6, 0), new Vector3(2.4f, 1.2f, 1.1f), open ? Hex(0xeceff1) : Locked(0.35f));
                Prim(PrimitiveType.Cube, t, "Sign", Fx(0, 1.0, 0.6), new Vector3(2.4f, 0.18f, 0.08f), open ? Mat(col, 1) : Locked(0.35f));
                var cubes = Group(t, "Cubes");
                for (int k = 0; k < 10; k++)
                    Prim(PrimitiveType.Cube, cubes, "Cube_" + k, Fx(-0.9 + k % 5 * 0.45, 1.4, -0.22 + k / 5 * 0.44), Vector3.one * 0.36f, CARDBOARD).SetActive(false);
                Prim(PrimitiveType.Cube, t, "Broken", Fx(0.9, 0.2, 0.8), Vector3.one * 0.4f, BROKEN).SetActive(false);
                Arm(t, Fx(1.5, 0, 0), mirrorX: true);
            }

            // 포장대 (로컬 +z = 앞면: 앞쪽에 전달된 물품, 뒤쪽에 포장된 박스)
            var tables = Group(R, "Tables");
            for (int i = 0; i < l.tables.Count; i++)
            {
                var s = l.tables[i];
                var t = Point(tables, "Table_" + (i + 1), PointKind.Table, s.Pos, CoordUtil.FaceYaw(s.rot), null, s.cost);
                Markers(t, s);
                Prim(PrimitiveType.Cube, t, "Top", Fx(0, 0.45, 0), new Vector3(2.2f, 0.9f, 1.8f), s.cost == 0 ? Hex(WOOD) : Locked(0.35f));
                var items = Group(t, "Items");
                for (int k = 0; k < 6; k++)
                    Prim(PrimitiveType.Cube, items, "Item_" + k, Fx(-0.8 + k / 2 * 0.4, 1.05, 0.45 - k % 2 * 0.4), Vector3.one * 0.3f, CARDBOARD).SetActive(false);
                var boxes = Group(t, "Boxes");
                for (int k = 0; k < 4; k++)
                    Prim(PrimitiveType.Cube, boxes, "Box_" + k, Fx(0.25 + k % 2 * 0.55, 1.11 + k / 2 * 0.44, -0.4), new Vector3(0.5f, 0.42f, 0.5f), BOX).SetActive(false);
                var machine = Group(t, "Machine");
                Prim(PrimitiveType.Cube, machine, "PostL", Fx(-1.2, 0.9, 0), new Vector3(0.2f, 1.8f, 0.2f), METAL_DARK);
                Prim(PrimitiveType.Cube, machine, "PostR", Fx(1.2, 0.9, 0), new Vector3(0.2f, 1.8f, 0.2f), METAL_DARK);
                Prim(PrimitiveType.Cube, machine, "Head", Fx(0, 1.9, 0), new Vector3(2.6f, 0.5f, 0.9f), 0xf1c40f);
                machine.gameObject.SetActive(false);
            }

            // 출고 도크 (팔레트 + 바닥 패드, 기계손은 팔레트와 트럭 뒷문 사이)
            var docks = Group(R, "Docks");
            for (int i = 0; i < l.docks.Count; i++)
            {
                var s = l.docks[i];
                var de = cfg.destinations.Find(x => x.id == s.dest);
                var t = Point(docks, $"Dock_{i + 1}_{s.dest}", PointKind.Dock, s.Pos, 0, s.dest, s.cost);
                Markers(t, s);
                var pad = Prim(PrimitiveType.Quad, t, "Pad", new Vector3(0, 0.02f, 0), new Vector3(3.4f, 3.4f, 1), s.cost == 0 ? Alpha(de.color, 0.3f) : Locked(0.4f));
                pad.transform.localRotation = Quaternion.Euler(90, 0, 0);
                Prim(PrimitiveType.Cube, t, "Pallet", new Vector3(0, 0.1f, 0), new Vector3(2.4f, 0.2f, 2.4f), WOOD);
                CubeSlots(Group(t, "Cubes"), 3, 3, 2, 0.62f, 0.5f, BOX);
                var door = S.TruckDoor(s.truck);
                double dx = door.x - s.x, dz = door.z - s.z, dl = System.Math.Sqrt(dx * dx + dz * dz);
                if (dl == 0) dl = 1;
                var arm = Arm(t, Vector3.zero, mirrorX: false);
                arm.position = CoordUtil.ToWorld(s.x + dx / dl * 1.8 - dz / dl * 1.3, s.z + dz / dl * 1.8 + dx / dl * 1.3);
                var spot = Group(t, "TruckSpot");
                spot.position = CoordUtil.ToWorld(s.truck.Pos);
                spot.rotation = Quaternion.Euler(0, CoordUtil.TruckYaw(s.truck.rot), 0);
                Truck(spot, "Truck");
            }

            // 쓰레기통
            {
                var t = Point(R, "Trash", PointKind.Trash, l.trash.Pos, 0);
                Markers(t, l.trash);
                Cyl(t, "Can", 0.675, 1.2, new Vector3(0, 0.6f, 0), Hex(0x2e7d32));
                Cyl(t, "Lid", 0.82, 0.12, new Vector3(0, 1.26f, 0), Hex(0x1b5e20));
            }

            // 휴게실: 티어별 가구 (G.care.rest에 맞는 것만 켠다)
            {
                var r = l.rest;
                var t = Point(R, "Rest", PointKind.Rest, new P(r.x, r.z), 0);
                t.GetComponent<LayoutPoint>().size = new Vector2((float)r.w, (float)r.d);
                for (int tier = 0; tier < cfg.care.rest.costs.Length; tier++) RestTier(Group(t, "Tier_" + tier), r, tier).gameObject.SetActive(tier == 0);
            }

            // 대기 구역 벤치
            {
                var t = Point(R, "Waiting", PointKind.Waiting, new P(w.x, w.z), 0);
                t.GetComponent<LayoutPoint>().size = new Vector2((float)w.w, (float)w.d);
                var bench = Prim(PrimitiveType.Cube, t, "Bench", Vector3.zero, new Vector3((float)System.Math.Max(1, w.w * 0.9), 0.4f, 0.6f), WOOD);
                bench.transform.position = CoordUtil.ToWorld(w.x, w.z + w.d / 2 - 0.5, 0.2f);
            }

            // 공기청정기: 미설치(반투명) + 티어별
            {
                var t = Point(R, "Purifier", PointKind.Purifier, l.purifier, 0);
                Cyl(t, "Locked", 0.45, 1.0, new Vector3(0, 0.5f, 0), Locked(0.3f));
                int[] body = { 0xeceff1, 0xffffff, 0xe3f2fd, 0xfffde7 }, ring = { 0x90a4ae, 0x42a5f5, 0x26c6da, 0xffd700 };
                for (int pt = 0; pt < body.Length; pt++)
                {
                    var g = Group(t, "Tier_" + pt);
                    float h = 1.0f + pt * 0.25f;
                    Cyl(g, "Body", 0.475, h, new Vector3(0, h / 2, 0), Hex(body[pt]));
                    Cyl(g, "Ring", 0.54, 0.12, new Vector3(0, h * 0.7f, 0), Mat(ring[pt], 1, pt >= 2 ? ring[pt] : (int?)null));
                    Prim(PrimitiveType.Cube, g, "Fan", new Vector3(0, h + 0.05f, 0), new Vector3(0.7f, 0.04f, 0.12f), 0x546e7a);
                    g.gameObject.SetActive(false);
                }
            }

            Point(R, "Entrance", PointKind.Entrance, l.entrance, 0);
            Point(R, "PlayerStart", PointKind.PlayerStart, l.playerStart, 0);
            var idle = Group(R, "Idle");
            foreach (var kv in l.idle) Point(idle, "Idle_" + kv.Key, PointKind.Idle, kv.Value, 0, kv.Key);

            BeltPaths(R, l);
            BeltMeshes(map, l);

            if (persist) AssetDatabase.SaveAssets();
            return root;
        }

        // ---------- 컨베이어 벨트 ----------
        // 이미 있는 맵에 기본 배치의 벨트 라인을 넣는다 (시설 위치는 씬 기준)
        [MenuItem("APC/기본 배치로 벨트 만들기")]
        static void BeltMenu()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<ApcConfig>("Assets/Data/ApcConfig.asset");
            var map = Object.FindFirstObjectByType<MapRoot>();
            persist = true;
            BeltPaths(map.transform, DefaultLayout.Make(cfg));
            BeltMeshes(map, map.Build());
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            EditorSceneManager.SaveScene(map.gameObject.scene);
        }

        // 벨트 점(P0, P1, ...)을 옮긴 뒤 모양만 다시 만든다
        [MenuItem("APC/벨트 모양 갱신")]
        static void BeltMeshMenu()
        {
            var map = Object.FindFirstObjectByType<MapRoot>();
            persist = true;
            BeltMeshes(map, map.Build());
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        }

        // Map/Belts/공정/Line_id (BeltPath + 점 P0..Pn)
        static void BeltPaths(Transform mapRoot, LayoutData l)
        {
            var old = mapRoot.Find("Belts");
            if (old) Object.DestroyImmediate(old.gameObject);
            var belts = Group(mapRoot, "Belts");
            foreach (var st in LayoutData.BELT_STAGES)
            {
                var g = Group(belts, st);
                if (!l.belts.TryGetValue(st, out var lines)) continue;
                foreach (var ln in lines)
                {
                    var t = Group(g, "Line_" + ln.id);
                    var bp = t.gameObject.AddComponent<BeltPath>();
                    bp.stage = st; bp.id = ln.id;
                    if (ln.from != null) { bp.fromFac = ln.from.fac; bp.fromIndex = ln.from.i; bp.fromPort = ln.from.port; bp.fromLine = ln.from.line; }
                    if (ln.to != null) { bp.toFac = ln.to.fac; bp.toIndex = ln.to.i; bp.toPort = ln.to.port; bp.toLine = ln.to.line; }
                    for (int i = 0; i < ln.points.Count; i++) Group(t, "P" + i).position = CoordUtil.ToWorld(ln.points[i]);
                }
            }
        }

        // 원본 World.belt: 구간마다 0.42 x 0.1 x (길이+0.42) 상자, 높이 0.06
        static void BeltMeshes(MapRoot map, LayoutData l)
        {
            foreach (var bp in map.GetComponentsInChildren<BeltPath>(true))
            {
                var old = bp.transform.Find("Mesh");
                if (old) Object.DestroyImmediate(old.gameObject);
                var mesh = Group(bp.transform, "Mesh");
                var line = l.belts[bp.stage].Find(x => x.id == bp.id);
                var pts = APC.Belts.LinePts(l, bp.stage, line);
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    P a = pts[i], b = pts[i + 1];
                    double dx = b.x - a.x, dz = b.z - a.z, len = System.Math.Sqrt(dx * dx + dz * dz);
                    if (len < 0.01) continue;
                    var m = Prim(PrimitiveType.Cube, mesh, "Seg_" + i, CoordUtil.ToWorld((a.x + b.x) / 2, (a.z + b.z) / 2, 0.06f),
                        new Vector3(0.42f, 0.1f, (float)len + 0.42f), BELT, world: true);
                    m.transform.rotation = Quaternion.Euler(0, CoordUtil.FacingYaw(dx, dz), 0);
                    m.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        }

        // ---------- 휴게실 가구 (원본 syncCare: 9 x 7 기준, 구역 크기에 맞춰 늘이거나 줄인다) ----------
        static Transform RestTier(Transform g, AreaRect r, int tier)
        {
            double kx = r.w / 9, kz = r.d / 7;
            Vector3 At(double dx, double y, double dz) => CoordUtil.ToWorld(r.x + dx * kx, r.z + dz * kz, (float)y);
            int sofa = new[] { 0x8d6e63, 0x5c6bc0, 0x8e24aa, 0xd4af37 }[tier];
            Floor(g, "Floor", r.x - r.w / 2, r.z - r.d / 2, r.x + r.w / 2, r.z + r.d / 2, new[] { 0xd8cfc4, 0xd6dcf2, 0xe6d3f0, 0xf6e8b8 }[tier], 0.01f);
            WorldPrim(PrimitiveType.Cube, g, "Sofa", At(-1.2, 0.3, -2.6), new Vector3((float)(3.4 * kx), 0.6f, 1.0f), Hex(sofa));
            WorldPrim(PrimitiveType.Cube, g, "SofaBack", At(-1.2, 0.7, -3.05), new Vector3((float)(3.4 * kx), 0.9f, 0.3f), Hex(sofa));
            WorldCyl(g, "Table", 0.6, 0.5, At(2, 0.25, -1.8), Hex(WOOD));
            if (tier >= 1)
            {
                WorldCyl(g, "Pot", 0.275, 0.5, At(3.4, 0.25, 2.8), Hex(0x795548));
                WorldPrim(PrimitiveType.Sphere, g, "Plant", At(3.4, 0.9, 2.8), Vector3.one * 1.0f, Hex(0x43a047));
                WorldPrim(PrimitiveType.Cube, g, "Sofa2", At(-1.6, 0.3, 2.6), new Vector3((float)(3 * kx), 0.6f, 1), Hex(sofa));
            }
            if (tier >= 2)
            {
                WorldCyl(g, "LampPole", 0.05, 1.8, At(-3.4, 0.9, -2.8), Hex(0x424242));
                WorldPrim(PrimitiveType.Sphere, g, "Lamp", At(-3.4, 1.9, -2.8), Vector3.one * 0.6f, Mat(0xfff59d, 1, 0xfff59d));
                WorldCyl(g, "Rug", 1.8 * System.Math.Min(kx, kz), 0.01, CoordUtil.ToWorld(r.x, r.z, 0.02f), Hex(0xc62828));
                WorldPrim(PrimitiveType.Cube, g, "TV", At(2.2, 1.1, -3.3), new Vector3(1.6f, 1.0f, 0.1f), Hex(0x212121));
            }
            if (tier >= 3)
            {
                WorldCyl(g, "Pedestal", 0.45, 0.3, CoordUtil.ToWorld(r.x, r.z, 0.15f), Hex(0xd4af37));
                WorldPrim(PrimitiveType.Sphere, g, "Trophy", CoordUtil.ToWorld(r.x, r.z, 0.7f), Vector3.one * 0.8f, Mat(0xffd700, 1, 0x806000));
                WorldPrim(PrimitiveType.Sphere, g, "Plant2", At(-3.4, 0.6, 2.8), Vector3.one * 1.0f, Hex(0x43a047));
            }
            return g;
        }

        // ---------- 헬퍼 ----------
        // 회전하는 시설 그룹의 원본 로컬 좌표 → Unity 로컬 (x 반전)
        static Vector3 Fx(double x, double y, double z) => new Vector3((float)-x, (float)y, (float)z);

        static Transform Group(Transform parent, string name)
        {
            var g = new GameObject(name).transform;
            g.SetParent(parent, false);
            return g;
        }

        static Transform Point(Transform parent, string name, PointKind kind, P pos, float yaw, string id = null, int cost = 0)
        {
            var t = Group(parent, name);
            t.position = CoordUtil.ToWorld(pos);
            t.rotation = Quaternion.Euler(0, yaw, 0);
            var lp = t.gameObject.AddComponent<LayoutPoint>();
            lp.kind = kind; lp.id = id; lp.cost = cost;
            return t;
        }

        static void Marker(Transform parent, string name, P? p)
        {
            if (p == null) return;
            Group(parent, name).position = CoordUtil.ToWorld(p.Value);
        }

        static void Markers(Transform t, Slot s)
        {
            Marker(t, "In", s.inPt); Marker(t, "Out", s.outPt); Marker(t, "Inspect", s.inspectPt);
            Marker(t, "BeltIn", s.beltIn); Marker(t, "BeltOut", s.beltOut);
        }

        static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 local, Vector3 scale, int color, bool world = false) =>
            Prim(type, parent, name, local, scale, Hex(color), world);

        static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 local, Vector3 scale, Color color, bool world = false) =>
            Prim(type, parent, name, local, scale, Mat(color, color.a), world);

        static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 local, Vector3 scale, Material mat, bool world = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            if (world) go.transform.position = local; else go.transform.localPosition = local;
            go.transform.localScale = scale;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = type == PrimitiveType.Quad ? ShadowCastingMode.Off : ShadowCastingMode.On;
            return go;
        }

        static GameObject WorldPrim(PrimitiveType type, Transform parent, string name, Vector3 world, Vector3 scale, Material mat) =>
            Prim(type, parent, name, world, scale, mat, true);

        // 원통: 원본 위·아래 반지름이 다르면 평균 반지름
        static GameObject Cyl(Transform parent, string name, double r, double h, Vector3 local, Material mat) =>
            Prim(PrimitiveType.Cylinder, parent, name, local, new Vector3((float)(r * 2), (float)(h / 2), (float)(r * 2)), mat);

        static GameObject WorldCyl(Transform parent, string name, double r, double h, Vector3 world, Material mat) =>
            Prim(PrimitiveType.Cylinder, parent, name, world, new Vector3((float)(r * 2), (float)(h / 2), (float)(r * 2)), mat, true);

        static GameObject Floor(Transform parent, string name, double x1, double z1, double x2, double z2, int color, float y) =>
            Floor(parent, name, x1, z1, x2, z2, Hex(color), y);

        static GameObject Floor(Transform parent, string name, double x1, double z1, double x2, double z2, string hex, float y)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return Floor(parent, name, x1, z1, x2, z2, Mat(c, 1), y);
        }

        static GameObject Floor(Transform parent, string name, double x1, double z1, double x2, double z2, Material mat, float y)
        {
            var go = Prim(PrimitiveType.Quad, parent, name, CoordUtil.ToWorld((x1 + x2) / 2, (z1 + z2) / 2, y), new Vector3((float)(x2 - x1), (float)(z2 - z1), 1), mat, true);
            go.transform.rotation = Quaternion.Euler(90, 0, 0);
            return go;
        }

        // 원본 cubeSlots: 바닥 0.2 위에 nx x nz x ny 칸. 순서 = 층 → z → x
        static void CubeSlots(Transform parent, int nx, int nz, int ny, float step, float size, int color)
        {
            int i = 0;
            for (int y = 0; y < ny; y++)
                for (int z = 0; z < nz; z++)
                    for (int x = 0; x < nx; x++)
                        Prim(PrimitiveType.Cube, parent, "Cube_" + i++,
                            new Vector3((x - (nx - 1) / 2f) * step, 0.2f + size / 2 + y * (size + 0.02f), -(z - (nz - 1) / 2f) * step),
                            Vector3.one * size, color).SetActive(false);
        }

        // 원본 makeArm. Pivot이 y축으로 돈다
        static Transform Arm(Transform parent, Vector3 local, bool mirrorX)
        {
            float sx = mirrorX ? -1 : 1;
            var g = Group(parent, "Arm");
            g.localPosition = local;
            Prim(PrimitiveType.Cylinder, g, "Base", new Vector3(0, 0.15f, 0), new Vector3(0.62f, 0.15f, 0.62f), METAL_DARK);
            var pivot = Group(g, "Pivot");
            pivot.localPosition = new Vector3(0, 0.3f, 0);
            Prim(PrimitiveType.Cube, pivot, "Post", new Vector3(0, 0.65f, 0), new Vector3(0.16f, 1.3f, 0.16f), ARM);
            Prim(PrimitiveType.Cube, pivot, "Fore", new Vector3(-0.4f * sx, 1.3f, 0), new Vector3(0.9f, 0.14f, 0.14f), ARM);
            Prim(PrimitiveType.Sphere, pivot, "Tip", new Vector3(-0.85f * sx, 1.3f, 0), Vector3.one * 0.24f, 0x263238);
            g.gameObject.SetActive(false);
            return g;
        }

        static void Truck(Transform parent, string name)
        {
            if (!persist) { Group(parent, name); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(TruckAsset(), parent);
            go.name = name;
        }

        // 트럭 프리팹: 루트(로직·뷰) + Model 자식(AI_Truck). 로컬 -x = 뒷문, 짐칸 중심 = 원점
        static GameObject TruckAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(TruckPrefab);
            if (existing) return existing;
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
            var root = new GameObject("Truck");
            var model = Group(root.transform, "Model");
            // FBX: 길이 1.9 (x, +x = 운전석), 높이 1.06. 2.3배 = 길이 4.4, 높이 2.4 (원본 트럭과 비슷)
            model.localScale = Vector3.one * 2.3f;
            var fbx = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(TruckModel), model);
            fbx.name = "AI_Truck";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, TruckPrefab);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ---------- 재질 (URP Lit, 반사 없음 = 원본 Lambert) ----------
        // 뷰에서 색을 바꿔 쓰는 기본 재질 (흰색 불투명 / 흰색 반투명)
        public static Material MaterialAsset(float alpha)
        {
            persist = true;
            var m = Mat(Color.white, alpha);
            AssetDatabase.SaveAssets();
            return m;
        }

        static Material Hex(int c) => Mat(U.Hex(c), 1);
        static Material Locked(float alpha) => Mat(U.Hex(LOCKED), alpha);
        static Material Alpha(Color c, float alpha) => Mat(c, alpha);
        static Material Mat(int c, float alpha, int? emissive) => Mat(U.Hex(c), alpha, emissive.HasValue ? U.Hex(emissive.Value) : (Color?)null);

        static Material Mat(Color c, float alpha, Color? emissive = null)
        {
            string key = ColorUtility.ToHtmlStringRGB(c) + (alpha < 1 ? "_a" + Mathf.RoundToInt(alpha * 100) : "") + (emissive.HasValue ? "_e" + ColorUtility.ToHtmlStringRGB(emissive.Value) : "");
            // 테스트(persist = false)가 만든 임시 재질은 에셋으로 저장할 때 다시 쓰지 않는다
            if (mats.TryGetValue(key, out var cached) && cached && (!persist || AssetDatabase.Contains(cached))) return cached;
            string path = $"{MatDir}/C_{key}.mat";
            var m = persist ? AssetDatabase.LoadAssetAtPath<Material>(path) : null;
            if (!m)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                c.a = alpha;
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", 0);
                m.SetFloat("_EnvironmentReflections", 0); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                m.SetFloat("_SpecularHighlights", 0); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                if (alpha < 1)
                {
                    m.SetFloat("_Surface", 1);
                    m.SetFloat("_Blend", 0);
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_ZWrite", 0);
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    m.renderQueue = (int)RenderQueue.Transparent;
                }
                if (emissive.HasValue)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", emissive.Value);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
                if (persist)
                {
                    if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
                    if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/Materials", "APC");
                    AssetDatabase.CreateAsset(m, path);
                }
            }
            mats[key] = m;
            return m;
        }
    }
}
