// 원본 LayoutLib.makeDefault()의 좌표. 에디터 전용:
//  - 씬 배치 도구가 이 좌표대로 시설 오브젝트를 한 번 놓는다 (런타임에는 씬 컴포넌트가 기준)
//  - EditMode 테스트용 배치
using System.Collections.Generic;
using System.Linq;

namespace APC.EditorTools
{
    public static class DefaultLayout
    {
        static P Pt(double x, double z) => new P(x, z);
        static Attach Fac(string f, int i, string port) => new Attach { fac = f, i = i, port = port };
        static Attach Line(int id) => new Attach { line = id };

        public static LayoutData Make(ApcConfig cfg)
        {
            double[] binCols = { 16, 21, 26, 31 };
            (double z, double rot)[] rows = { (4.6, 90), (17.4, 270) };
            var bins = new List<BinSlot>();
            for (int i = 0; i < 8 && i < cfg.items.Count; i++)
            {
                var it = cfg.items[i];
                double x = binCols[i % 4];
                var row = rows[i / 4];
                var d = LayoutData.Dir(row.rot);
                var p = Pt(x, row.z + d.z * 2.2);
                bins.Add(new BinSlot
                {
                    x = x, z = row.z, rot = row.rot, item = it.id, cost = it.cost, inPt = p, outPt = p, inspectPt = p,
                    beltIn = Pt(x - 0.35, row.z), beltOut = Pt(x + 0.35, row.z),
                });
            }
            var tables = new[] { 5.0, 11, 17 }.Select((z, i) => new Slot
            {
                x = 39, z = z, rot = 180, cost = i < cfg.packTableCosts.Length ? cfg.packTableCosts[i] : 0,
                inPt = Pt(36.8, z), outPt = Pt(36.8, z), beltIn = Pt(38.7, z), beltOut = Pt(39.3, z),
            }).ToList();
            double[] dockZ = { 4, 11, 18, 25 };
            var docks = cfg.destinations.Take(4).Select((de, i) => new DockSlot
            {
                x = 48, z = dockZ[i], dest = de.id, cost = de.cost, inPt = Pt(47.2, dockZ[i]), beltIn = Pt(48, dockZ[i]),
                truck = new Slot { x = 54.4, z = dockZ[i], rot = 180 },
            }).ToList();

            var l = new LayoutData
            {
                name = "기본 배치",
                mapW = 58, mapD = 32,
                ix1 = 0, iz1 = 0, ix2 = 52, iz2 = 32,
                areas =
                {
                    new Area { name = "하차 구역", x1 = 0, z1 = 1.8, x2 = 12, z2 = 19.8, color = U.Hex(0xf1e2a4) },
                    new Area { name = "보관 구역", x1 = 13, z1 = 2.4, x2 = 34, z2 = 19.6, color = U.Hex(0xcfe0f0) },
                    new Area { name = "포장 구역", x1 = 35, z1 = 2.4, x2 = 41.6, z2 = 19.6, color = U.Hex(0xd5ecd2) },
                    new Area { name = "출고 구역", x1 = 42.4, z1 = 0.4, x2 = 52, z2 = 28.6, color = U.Hex(0xf5d9c0) },
                },
                walls =
                {
                    new Wall { x1 = 0, z1 = 0.1, x2 = 52, z2 = 0.1 },
                    new Wall { x1 = 0.1, z1 = 0, x2 = 0.1, z2 = 2.4 },
                    new Wall { x1 = 0.1, z1 = 14.6, x2 = 0.1, z2 = 19 },
                    new Wall { x1 = 0.1, z1 = 23, x2 = 0.1, z2 = 32 },
                },
                inTrucks = new[] { 8.5, 4, 13 }.Select(z => new Slot { x = 3, z = z, rot = 0, outPt = Pt(6.8, z) }).ToList(),
                staging = new Slot { x = 9.5, z = 17.5, inPt = Pt(9.5, 17.5), outPt = Pt(9.5, 17.5), beltOut = Pt(11.5, 17.5) },
                bins = bins, tables = tables, docks = docks,
                trash = new Slot { x = 25, z = 27, inPt = Pt(25, 27) },
                rest = new AreaRect { x = 6, z = 27, w = 9, d = 7 },
                waiting = new AreaRect { x = 16, z = 27.5, w = 5, d = 3.6 },
                purifier = Pt(34, 26),
                entrance = Pt(0.8, 21),
                playerStart = Pt(12, 21),
                idle =
                {
                    ["unload"] = Pt(9.5, 11.2), ["sort"] = Pt(11.5, 13), ["inspect"] = Pt(23.5, 11),
                    ["transfer"] = Pt(34, 11), ["pack"] = Pt(35.2, 8), ["outbound"] = Pt(42, 14.5), ["load"] = Pt(45.2, 14.5),
                },
            };

            // 컨베이어: 메인 라인 + 지선
            // 분류: 하차장 → 통로 메인 라인 → 보관함마다 지선 (분기)
            const double sortZ = 10.4;
            var sort = new List<BeltLine> { new BeltLine { id = 1, points = { Pt(11.5, 17.5), Pt(11.5, sortZ), Pt(30.65, sortZ) }, from = Fac("staging", 0, "beltOut") } };
            for (int i = 0; i < bins.Count; i++)
                sort.Add(new BeltLine { id = i + 2, points = { Pt(bins[i].beltIn.Value.x, sortZ), bins[i].beltIn.Value }, from = Line(1), to = Fac("bin", i, "beltIn") });
            // 이송: 보관함 지선 (합류) → 통로 메인 라인 → 포장대 지선 (분기)
            const double trZ = 11.6, trX = 37.4;
            var transfer = new List<BeltLine> { new BeltLine { id = 1, points = { Pt(16.35, trZ), Pt(trX, trZ) } } };
            int id = 2;
            for (int i = 0; i < bins.Count; i++)
                transfer.Add(new BeltLine { id = id++, points = { bins[i].beltOut.Value, Pt(bins[i].beltOut.Value.x, trZ) }, from = Fac("bin", i, "beltOut"), to = Line(1) });
            for (int i = 0; i < tables.Count; i++)
                transfer.Add(new BeltLine { id = id++, points = { Pt(trX, trZ), Pt(trX, tables[i].z), tables[i].beltIn.Value }, from = Line(1), to = Fac("table", i, "beltIn") });
            // 출고분류: 포장대 지선 (합류) → 위로 모았다가 오른쪽 세로 라인으로 내려가며 → 도크 지선 (분기)
            var outbound = new List<BeltLine> { new BeltLine { id = 1, points = { Pt(41, 17), Pt(41, 2), Pt(43.5, 2), Pt(43.5, 25) } } };
            id = 2;
            for (int i = 0; i < tables.Count; i++)
                outbound.Add(new BeltLine { id = id++, points = { tables[i].beltOut.Value, Pt(41, tables[i].z) }, from = Fac("table", i, "beltOut"), to = Line(1) });
            for (int i = 0; i < docks.Count; i++)
                outbound.Add(new BeltLine { id = id++, points = { Pt(43.5, docks[i].z), docks[i].beltIn.Value }, from = Line(1), to = Fac("dock", i, "beltIn") });
            l.belts["sort"] = sort;
            l.belts["transfer"] = transfer;
            l.belts["outbound"] = outbound;
            return l;
        }
    }
}
