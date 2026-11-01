// 시설·트럭 표시 (원본 World.sync 중 입고 트럭·하차장·보관함·포장대·도크 부분)
// 오브젝트는 씬에 이미 있다 (MapBuilder). 여기서는 켜고 끄고, 옮기고, 색만 바꾼다
using System.Linq;
using UnityEngine;

namespace APC
{
    public class FacilityView : MonoBehaviour
    {
        [SerializeField] MapRoot map;
        [Tooltip("색은 Palette.Tint로 바꾼다. 잠긴 시설은 반투명 재질")]
        [SerializeField] Material opaque, transparent;

        class BinObj { public Renderer body, sign; public Renderer[] cubes; public GameObject broken, arm; public Transform pivot; public string sig; }
        class TableObj { public Renderer top; public Renderer[] items; public GameObject[] boxes; public GameObject machine; }
        class DockObj { public Renderer pad; public GameObject[] cubes; public GameObject arm; public Transform pivot, truck; }

        Transform belts;
        float clock;   // 원본 World.sync의 t: 게임이 돌 때만(배속 반영) 흐른다

        Transform[] inTrucks;
        Renderer[] stagingCubes;
        BinObj[] bins;
        TableObj[] tables;
        DockObj[] docks;

        void Start()
        {
            inTrucks = map.Points(PointKind.InTruck).Select(p => p.transform.Find("Truck")).ToArray();
            stagingCubes = Kids<Renderer>(map.Points(PointKind.Staging)[0].transform, "Cubes");
            bins = map.Points(PointKind.Bin).Select(p => new BinObj
            {
                body = R(p, "Body"), sign = R(p, "Sign"), cubes = Kids<Renderer>(p.transform, "Cubes"),
                broken = p.transform.Find("Broken").gameObject, arm = p.transform.Find("Arm").gameObject, pivot = p.transform.Find("Arm/Pivot"),
            }).ToArray();
            tables = map.Points(PointKind.Table).Select(p => new TableObj
            {
                top = R(p, "Top"), items = Kids<Renderer>(p.transform, "Items"),
                boxes = Kids<Transform>(p.transform, "Boxes").Select(t => t.gameObject).ToArray(),
                machine = p.transform.Find("Machine").gameObject,
            }).ToArray();
            docks = map.Points(PointKind.Dock).Select(p => new DockObj
            {
                pad = R(p, "Pad"), cubes = Kids<Transform>(p.transform, "Cubes").Select(t => t.gameObject).ToArray(),
                arm = p.transform.Find("Arm").gameObject, pivot = p.transform.Find("Arm/Pivot"), truck = p.transform.Find("TruckSpot/Truck"),
            }).ToArray();
            belts = map.transform.Find("Belts");
        }

        void LateUpdate()
        {
            var G = Game.G;
            if (G == null) return;
            clock += GameRoot.ViewDt;

            // 벨트는 그 공정이 자동화되면 깔린다 (원본 syncBelts)
            if (belts)
                foreach (var st in LayoutData.BELT_STAGES)
                {
                    var g = belts.Find(st);
                    if (g && g.gameObject.activeSelf != S.IsAuto(st)) g.gameObject.SetActive(S.IsAuto(st));
                }

            // 입고 트럭
            foreach (var tr in G.inTrucks)
            {
                var m = inTrucks[tr.idx];
                m.gameObject.SetActive(tr.state != TruckState.Away);
                m.position = CoordUtil.ToWorld(S.TruckPos(tr.slot, tr.off));
            }

            // 하차장
            for (int i = 0; i < stagingCubes.Length; i++)
            {
                bool on = i < G.staging.Count;
                stagingCubes[i].gameObject.SetActive(on);
                if (on) Palette.Tint(stagingCubes[i], Palette.TypeColor(G.staging[i]));
            }

            // 보관함
            foreach (var b in G.bins)
            {
                if (b.slot == null) continue;
                var o = bins[S.BinSlotIdx(b)];
                string sig = $"{b.unlocked}|{b.checkedCount}|{b.uncheckedCount}|{b.broken}";
                if (sig != o.sig)
                {
                    o.sig = sig;
                    var col = b.unlocked ? Palette.TypeColor(b.item) : U.Hex(Palette.LOCKED);
                    Paint(o.body, b.unlocked, b.unlocked ? U.Hex(0xeceff1) : Palette.WithAlpha(U.Hex(Palette.LOCKED), 0.35f));
                    Paint(o.sign, b.unlocked, b.unlocked ? col : Palette.WithAlpha(U.Hex(Palette.LOCKED), 0.35f));
                    int total = b.checkedCount + b.uncheckedCount;
                    int n = Mathf.Min(10, Mathf.CeilToInt(total / 2f)), nc = Mathf.CeilToInt(b.checkedCount / 2f);
                    for (int k = 0; k < o.cubes.Length; k++)
                    {
                        o.cubes[k].gameObject.SetActive(k < n);
                        Palette.Tint(o.cubes[k], k < nc ? col : U.Hex(Palette.CARDBOARD));
                    }
                    o.broken.SetActive(b.broken > 0);
                }
                // 기계손: 검수 중이면 좌우로 흔든다 (원본 rotation.y = sin(t×6)×0.8, z 반전이라 각도 부호 반대)
                o.arm.SetActive(b.unlocked && S.IsAuto("inspect"));
                if (o.arm.activeSelf) o.pivot.localRotation = Quaternion.Euler(0, b.armProgress > 0 ? -Mathf.Sin(clock * 6) * 0.8f * Mathf.Rad2Deg : 0, 0);
            }

            // 포장대
            foreach (var t in G.tables)
            {
                var o = tables[t.idx];
                Paint(o.top, t.unlocked, t.unlocked ? U.Hex(Palette.WOOD) : Palette.WithAlpha(U.Hex(Palette.LOCKED), 0.35f));
                int k = 0;
                if (t.order != null)
                    foreach (var item in t.delivered.Keys.OrderBy(x => x))
                        for (int q = 0; q < t.delivered[item] && k < o.items.Length; q++, k++)
                        {
                            o.items[k].gameObject.SetActive(true);
                            Palette.Tint(o.items[k], Palette.TypeColor(item));
                        }
                for (; k < o.items.Length; k++) o.items[k].gameObject.SetActive(false);
                for (int i = 0; i < o.boxes.Length; i++) o.boxes[i].SetActive(i < t.boxes.Count);
                // 포장 기계: 포장 중이면 위아래로 (원본 |sin(t×8)|×0.15)
                o.machine.SetActive(t.unlocked && S.IsAuto("pack"));
                if (o.machine.activeSelf)
                    o.machine.transform.localPosition = new Vector3(0, t.machineProgress > 0 ? Mathf.Abs(Mathf.Sin(clock * 8)) * 0.15f : 0, 0);
            }

            // 출고 도크와 트럭
            foreach (var d in G.docks)
            {
                if (d.slot == null) continue;
                var o = docks[S.DockSlotIdx(d)];
                var col = Game.CONFIG.destinations[d.idx].color;
                Paint(o.pad, false, d.unlocked ? Palette.WithAlpha(col, 0.3f) : Palette.WithAlpha(U.Hex(Palette.LOCKED), 0.4f));
                for (int i = 0; i < o.cubes.Length; i++) o.cubes[i].SetActive(i < d.waiting.Count);
                o.arm.SetActive(d.unlocked && S.IsAuto("load"));
                if (o.arm.activeSelf) o.pivot.localRotation = Quaternion.Euler(0, -(180 + Mathf.Sin(clock * 3) * 0.6f * Mathf.Rad2Deg), 0);
                o.truck.gameObject.SetActive(d.truck != null);
                if (d.truck != null) o.truck.position = CoordUtil.ToWorld(S.TruckPos(d.slot.truck, d.truck.off));
            }
        }

        void Paint(Renderer r, bool solid, Color c)
        {
            var m = solid ? opaque : transparent;
            if (r.sharedMaterial != m) r.sharedMaterial = m;
            Palette.Tint(r, c);
        }

        static Renderer R(LayoutPoint p, string name) => p.transform.Find(name).GetComponent<Renderer>();

        static T[] Kids<T>(Transform t, string group) where T : Component =>
            t.Find(group).Cast<Transform>().Select(c => c.GetComponent<T>()).ToArray();
    }
}
