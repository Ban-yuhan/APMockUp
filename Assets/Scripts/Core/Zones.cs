// 상호작용 범위 (원본 js/player.js의 Zones)
// 범위는 시설의 지점(in = 넣기, out = 꺼내기, inspect = 검수)마다 하나씩 생긴다.
// 같은 위치에 있는 지점은 하나의 범위로 합쳐지고, 그 범위에서는 합쳐진 역할을 모두 한다.
using System;
using System.Collections.Generic;
using static APC.Game;

namespace APC
{
    public class ZonePart
    {
        public string kind;    // truck | staging | bin | table | dock | trash
        public object refObj;  // InTruck / Bin / Table / Dock (하차장·쓰레기통은 null)
        public HashSet<string> roles = new HashSet<string>();
    }

    public class Zone
    {
        public double x, z;
        public List<ZonePart> parts = new List<ZonePart>();
        public List<string> keys = new List<string>();
        public string key, kind;
        public object refObj;
        public P Pos => new P(x, z);
    }

    public static class Zones
    {
        public static readonly Dictionary<string, string[]> FACILITY_ROLES = new Dictionary<string, string[]>
        {
            ["truck"] = new[] { "out" }, ["staging"] = new[] { "in", "out" }, ["bin"] = new[] { "in", "out", "inspect" },
            ["table"] = new[] { "in", "out" }, ["dock"] = new[] { "in" }, ["trash"] = new[] { "in" },
        };

        public static List<Zone> List()
        {
            var pts = new List<(string kind, object r, string role, P p, string key)>();
            foreach (var tr in G.inTrucks) if (tr.state == TruckState.Present) pts.Add(("truck", tr, "out", S.InTruckOut(tr), "truck" + tr.idx));
            pts.Add(("staging", null, "in", S.StagingIn(), "staging"));
            pts.Add(("staging", null, "out", S.StagingOut(), "staging"));
            foreach (var b in G.bins)
            {
                if (!b.unlocked) continue;
                pts.Add(("bin", b, "in", S.BinIn(b), "bin" + b.idx));
                pts.Add(("bin", b, "out", S.BinOut(b), "bin" + b.idx));
                pts.Add(("bin", b, "inspect", S.BinInspect(b), "bin" + b.idx));
            }
            foreach (var t in G.tables)
            {
                if (!t.unlocked) continue;
                pts.Add(("table", t, "in", S.TableIn(t), "table" + t.idx));
                pts.Add(("table", t, "out", S.TableOut(t), "table" + t.idx));
            }
            foreach (var d in G.docks) if (d.unlocked) pts.Add(("dock", d, "in", S.DockIn(d), "dock" + d.idx));
            pts.Add(("trash", null, "in", S.TrashIn(), "trash"));

            // 같은 위치의 지점을 하나의 범위로 합친다
            var zones = new List<Zone>();
            foreach (var p in pts)
            {
                var z = zones.Find(q => Math.Abs(q.x - p.p.x) < 0.05 && Math.Abs(q.z - p.p.z) < 0.05);
                if (z == null) { z = new Zone { x = p.p.x, z = p.p.z }; zones.Add(z); }
                var part = z.parts.Find(q => q.kind == p.kind && q.refObj == p.r);
                if (part == null) { part = new ZonePart { kind = p.kind, refObj = p.r }; z.parts.Add(part); }
                part.roles.Add(p.role);
                z.keys.Add(p.key + ":" + p.role);
            }
            foreach (var z in zones)
            {
                z.key = string.Join("|", z.keys);
                z.kind = z.parts[0].kind;
                z.refObj = z.parts[0].refObj;
            }
            return zones;
        }

        static readonly Dictionary<string, string> ROLE_NAMES = new Dictionary<string, string> { ["in"] = "넣기", ["out"] = "꺼내기", ["inspect"] = "검수" };

        public static string PartName(ZonePart part)
        {
            string name = "";
            switch (part.kind)
            {
                case "truck": name = $"🚚 입고 트럭 {((InTruck)part.refObj).idx + 1}"; break;
                case "staging": name = "📥 하차장"; break;
                case "bin": { var it = CONFIG.items[((Bin)part.refObj).item]; name = $"{it.emoji} {it.name} 보관함"; break; }
                case "table": name = $"🎁 포장대 {((Table)part.refObj).idx + 1}"; break;
                case "dock": name = $"🏁 {CONFIG.destinations[((Dock)part.refObj).idx].name} 도크"; break;
                case "trash": name = "🗑️ 쓰레기통"; break;
            }
            // 시설의 지점이 나뉘어 있으면 어떤 지점인지 붙인다 (HashSet은 넣은 순서대로 돈다)
            if (part.roles.Count < FACILITY_ROLES[part.kind].Length)
                name += $" ({string.Join("·", System.Linq.Enumerable.Select(part.roles, r => ROLE_NAMES[r]))})";
            return name;
        }

        public static string Name(Zone z) => string.Join(" + ", z.parts.ConvertAll(PartName));

        public static Zone Nearest(P pos)
        {
            Zone best = null;
            double bd = CONFIG.interactRadius;
            foreach (var z in List())
            {
                double d = U.Dist(pos, z.Pos);
                if (d < bd) { bd = d; best = z; }
            }
            return best;
        }
    }
}
