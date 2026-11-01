// 배치 데이터 (원본 js/layout.js의 LAYOUT 형식). 게임에서는 씬에 놓인 시설 컴포넌트에서 만든다.
// 좌표: x = 오른쪽, z = 아래쪽(화면 기준), 1 = 1칸. rot(도): 0 = +x, 90 = +z, 180 = -x, 270 = -z
//   시설의 rot = 앞면(상호작용 쪽) 방향, 트럭의 rot = 뒷문 방향
// 상호작용 지점: in = 넣기, out = 꺼내기, inspect = 검수 (없으면 시설 위치)
using System;
using System.Collections.Generic;

namespace APC
{
    public class Slot
    {
        public double x, z, rot;
        public int cost;
        public P? inPt, outPt, inspectPt, beltIn, beltOut;
        public P Pos => new P(x, z);

        // 원본 LayoutLib.pt(slot, role)
        public P Pt(string role)
        {
            P? p = role switch
            {
                "in" => inPt, "out" => outPt, "inspect" => inspectPt, "beltIn" => beltIn, "beltOut" => beltOut, _ => null,
            };
            return p ?? Pos;
        }
    }

    public class BinSlot : Slot { public string item; }
    public class DockSlot : Slot { public string dest; public Slot truck; }
    public class AreaRect { public double x, z, w, d; }
    public class Area { public string name; public double x1, z1, x2, z2; public UnityEngine.Color color; }
    public class Wall { public double x1, z1, x2, z2; }
    public class Bounds2 { public double minX, maxX, minZ, maxZ; }

    // 벨트 끝점: 시설 포트 또는 같은 공정의 다른 라인 (null = 떨어짐)
    public class Attach
    {
        public string fac;   // staging | bin | table | dock (line이면 null)
        public int i;
        public string port;  // beltIn | beltOut
        public int line = -1;
        public bool IsLine => line >= 0;
    }

    public class BeltLine { public int id; public List<P> points = new List<P>(); public Attach from, to; }

    public class LayoutData
    {
        public string name;
        public double mapW, mapD;
        public double ix1, iz1, ix2, iz2;   // interior
        public List<Area> areas = new List<Area>();
        public List<Wall> walls = new List<Wall>();
        public List<Slot> inTrucks = new List<Slot>();
        public Slot staging;
        public List<BinSlot> bins = new List<BinSlot>();
        public List<Slot> tables = new List<Slot>();
        public List<DockSlot> docks = new List<DockSlot>();
        public Slot trash;
        public AreaRect rest, waiting;
        public P purifier, entrance, playerStart;
        public Dictionary<string, P> idle = new Dictionary<string, P>();
        public Dictionary<string, List<BeltLine>> belts = new Dictionary<string, List<BeltLine>>();

        // 파생값 (원본 LayoutLib.prepare)
        public Bounds2 bounds;
        public Dictionary<string, Belts.Graph> graphs = new Dictionary<string, Belts.Graph>();   // 공정별 벨트 경로망 (처음 쓸 때 만든다)
        public Dictionary<string, List<P>> routes = new Dictionary<string, List<P>>();         // 경로 캐시

        public static readonly string[] BELT_STAGES = { "sort", "transfer", "outbound" };

        // 원본 prepare 중 bounds만 여기서 계산한다.
        // CONFIG.items 덮어쓰기와 parking 값 재계산은 에셋을 바꾸지 않도록 S.FacValues/FacCosts에서 한다
        public LayoutData Prepare()
        {
            bounds = new Bounds2 { minX = ix1 + 0.6, maxX = ix2 - 0.6, minZ = iz1 + 0.6, maxZ = iz2 - 0.6 };
            graphs.Clear();
            routes.Clear();
            return this;
        }

        public static P Dir(double rot)
        {
            double a = rot * Math.PI / 180;
            return new P(Math.Round(Math.Cos(a) * 1e6) / 1e6, Math.Round(Math.Sin(a) * 1e6) / 1e6);
        }
    }
}
