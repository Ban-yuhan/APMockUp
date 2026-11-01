// 맵 루트: 씬의 LayoutPoint들 → LayoutData (원본 LAYOUT). 런타임 배치의 기준
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace APC
{
    public class MapRoot : MonoBehaviour
    {
        public string layoutName = "기본 배치";
        [Tooltip("맵 크기 (칸)")] public Vector2 mapSize = new Vector2(58, 32);
        [Tooltip("창고 내부 (배치 좌표 x1, z1, x2, z2). 플레이어 이동 한계 = 안쪽 0.6칸")]
        public Vector4 interior = new Vector4(0, 0, 52, 32);

        public LayoutData Build()
        {
            var l = new LayoutData
            {
                name = layoutName, mapW = mapSize.x, mapD = mapSize.y,
                ix1 = interior.x, iz1 = interior.y, ix2 = interior.z, iz2 = interior.w,
            };
            foreach (var p in GetComponentsInChildren<LayoutPoint>(true))
            {
                var pos = CoordUtil.ToLayout(p.transform.position);
                switch (p.kind)
                {
                    case PointKind.InTruck: l.inTrucks.Add(TruckSlot(p.transform, p)); break;
                    case PointKind.Staging: l.staging = Fill(new Slot(), p); break;
                    case PointKind.Bin: l.bins.Add(Fill(new BinSlot { item = p.id }, p)); break;
                    case PointKind.Table: l.tables.Add(Fill(new Slot(), p)); break;
                    case PointKind.Dock:
                        var spot = p.transform.Find("TruckSpot");
                        l.docks.Add(Fill(new DockSlot { dest = p.id, truck = TruckSlot(spot, null) }, p));
                        break;
                    case PointKind.Trash: l.trash = Fill(new Slot(), p); break;
                    case PointKind.Rest: l.rest = new AreaRect { x = pos.x, z = pos.z, w = p.size.x, d = p.size.y }; break;
                    case PointKind.Waiting: l.waiting = new AreaRect { x = pos.x, z = pos.z, w = p.size.x, d = p.size.y }; break;
                    case PointKind.Purifier: l.purifier = pos; break;
                    case PointKind.Entrance: l.entrance = pos; break;
                    case PointKind.PlayerStart: l.playerStart = pos; break;
                    case PointKind.Idle: l.idle[p.id] = pos; break;
                }
            }
            return l;
        }

        // 종류별 시설 오브젝트 (Build와 같은 순서 = 배치 데이터의 슬롯 번호)
        public List<LayoutPoint> Points(PointKind kind) => GetComponentsInChildren<LayoutPoint>(true).Where(p => p.kind == kind).ToList();

        static T Fill<T>(T s, LayoutPoint p) where T : Slot
        {
            var t = p.transform;
            var pos = CoordUtil.ToLayout(t.position);
            s.x = pos.x; s.z = pos.z; s.cost = p.cost;
            // 원본은 보관함·포장대만 회전한다 (하차장·도크·쓰레기통은 rot 없음 = 0)
            s.rot = p.kind == PointKind.Bin || p.kind == PointKind.Table ? CoordUtil.RotFromFaceYaw(t.eulerAngles.y) : 0;
            s.inPt = Child(t, "In"); s.outPt = Child(t, "Out"); s.inspectPt = Child(t, "Inspect");
            s.beltIn = Child(t, "BeltIn"); s.beltOut = Child(t, "BeltOut");
            return s;
        }

        // 트럭 주차 위치: rot = 뒷문 방향
        static Slot TruckSlot(Transform t, LayoutPoint p)
        {
            var pos = CoordUtil.ToLayout(t.position);
            return new Slot { x = pos.x, z = pos.z, rot = CoordUtil.RotFromTruckYaw(t.eulerAngles.y), outPt = Child(t, "Out"), cost = p ? p.cost : 0 };
        }

        static P? Child(Transform t, string name)
        {
            var c = t.Find(name);
            return c ? CoordUtil.ToLayout(c.position) : (P?)null;
        }
    }
}
