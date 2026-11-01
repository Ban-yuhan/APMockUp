// 컨베이어 벨트 경로망 (원본 js/layout.js의 projectOn / rawPts / linePts / buildGraph / routeKeys / cachedRoute)
// 벨트 라인 → 방향 있는 경로망 → 포트(시설의 벨트 입구·출구)끼리 최단 경로. 자동화 공정의 물품이 이 경로를 따라 이동한다
using System;
using System.Collections.Generic;
using System.Linq;

namespace APC
{
    public static class Belts
    {
        public class Graph
        {
            public List<P> nodes = new List<P>();                          // 노드 id = 인덱스 + 1
            public List<(int a, int b)> edges = new List<(int, int)>();    // a → b
            public Dictionary<string, int> ports = new Dictionary<string, int>();
        }

        static double R2(double v) => U.Round(v * 100) / 100.0;

        static IList<Slot> FacList(LayoutData l, string fac)
        {
            switch (fac)
            {
                case "staging": return new List<Slot> { l.staging };
                case "bin": return l.bins.Cast<Slot>().ToList();
                case "table": return l.tables;
                case "dock": return l.docks.Cast<Slot>().ToList();
            }
            return new List<Slot>();
        }

        static P? Port(LayoutData l, Attach a)
        {
            var list = FacList(l, a.fac);
            return a.i < list.Count && list[a.i] != null ? list[a.i].Pt(a.port) : (P?)null;
        }

        public static string PortKey(Attach a) => $"{a.fac}:{a.i}:{a.port}";

        // 폴리라인 위에서 p와 가장 가까운 점
        public static (P point, int seg, double t, double dist) ProjectOn(List<P> pts, P p)
        {
            (P point, int seg, double t, double dist) best = default;
            bool has = false;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                P A = pts[i], B = pts[i + 1];
                double dx = B.x - A.x, dz = B.z - A.z, l2 = dx * dx + dz * dz;
                double t = l2 > 0 ? Math.Max(0, Math.Min(1, ((p.x - A.x) * dx + (p.z - A.z) * dz) / l2)) : 0;
                var q = new P(A.x + dx * t, A.z + dz * t);
                double d = U.Dist(p, q);
                if (!has || d < best.dist) { best = (q, i, t, d); has = true; }
            }
            return best;
        }

        // 시설에 붙은 끝점을 포트 위치로 바꾼 점 목록
        static List<P> RawPts(LayoutData l, BeltLine line)
        {
            var pts = line.points.ToList();
            var fp = line.from != null && line.from.fac != null ? Port(l, line.from) : null;
            var tp = line.to != null && line.to.fac != null ? Port(l, line.to) : null;
            if (fp != null) pts[0] = fp.Value;
            if (tp != null) pts[pts.Count - 1] = tp.Value;
            return pts;
        }

        // 실제 점 목록: 다른 라인에 붙은 끝점은 그 라인 위로 맞춘다
        public static List<P> LinePts(LayoutData l, string st, BeltLine line)
        {
            var pts = RawPts(l, line);
            var lines = l.belts[st];
            foreach (var (a, k) in new[] { (line.from, 0), (line.to, pts.Count - 1) })
            {
                if (a == null || !a.IsLine) continue;
                var target = lines.Find(x => x.id == a.line);
                if (target != null && target != line) pts[k] = ProjectOn(RawPts(l, target), pts[k]).point;
            }
            return pts;
        }

        public static Graph BuildGraph(LayoutData l, string st)
        {
            var g = new Graph();
            if (!l.belts.TryGetValue(st, out var lines)) return g;
            var res = lines.ToDictionary(ln => ln.id, ln => LinePts(l, st, ln));
            // 다른 라인이 붙은 위치에서 그 라인을 나눈다
            var splits = lines.ToDictionary(ln => ln.id, ln => new List<P>());
            foreach (var ln in lines)
            {
                var pts = res[ln.id];
                if (ln.from != null && ln.from.IsLine && splits.ContainsKey(ln.from.line)) splits[ln.from.line].Add(pts[0]);
                if (ln.to != null && ln.to.IsLine && splits.ContainsKey(ln.to.line)) splits[ln.to.line].Add(pts[pts.Count - 1]);
            }
            var byKey = new Dictionary<string, int>();
            var seen = new HashSet<string>();
            int NodeAt(P p)
            {
                string k = U.Round(p.x * 100) + "," + U.Round(p.z * 100);
                if (!byKey.TryGetValue(k, out int id))
                {
                    g.nodes.Add(new P(R2(p.x), R2(p.z)));
                    byKey[k] = id = g.nodes.Count;
                }
                return id;
            }
            foreach (var ln in lines)
            {
                var pts = res[ln.id];
                if (pts.Count < 2) continue;
                var extra = splits[ln.id].Select(p => (p, pr: ProjectOn(pts, p))).ToList();
                var seq = new List<P>();
                for (int i = 0; i < pts.Count; i++)
                {
                    seq.Add(pts[i]);
                    if (i < pts.Count - 1) seq.AddRange(extra.Where(e => e.pr.seg == i).OrderBy(e => e.pr.t).Select(e => e.pr.point));
                }
                for (int i = 0; i < seq.Count - 1; i++)
                {
                    int a = NodeAt(seq[i]), b = NodeAt(seq[i + 1]);
                    if (a != b && seen.Add(a + ">" + b)) g.edges.Add((a, b));
                }
                if (ln.from != null && ln.from.fac != null) g.ports[PortKey(ln.from)] = NodeAt(pts[0]);
                if (ln.to != null && ln.to.fac != null) g.ports[PortKey(ln.to)] = NodeAt(pts[pts.Count - 1]);
            }
            return g;
        }

        // 방향을 따라가는 최단 경로 (포트키 → 포트키). 없으면 null
        public static List<P> RouteKeys(Graph g, string srcKey, string dstKey)
        {
            if (!g.ports.TryGetValue(srcKey, out int s) || !g.ports.TryGetValue(dstKey, out int t)) return null;
            var adj = Enumerable.Range(1, g.nodes.Count).ToDictionary(id => id, _ => new List<(int v, double w)>());
            foreach (var (a, b) in g.edges) adj[a].Add((b, U.Dist(g.nodes[a - 1], g.nodes[b - 1])));
            // 원본 Map 순서(넣은 순서)대로 최소를 고른다. Dictionary는 지우지 않으면 넣은 순서를 지킨다
            var dist = new Dictionary<int, double> { [s] = 0 };
            var prev = new Dictionary<int, int>();
            var done = new HashSet<int>();
            for (;;)
            {
                int u = -1;
                double ud = double.PositiveInfinity;
                foreach (var kv in dist) if (!done.Contains(kv.Key) && kv.Value < ud) { ud = kv.Value; u = kv.Key; }
                if (u < 0) return null;
                if (u == t) break;
                done.Add(u);
                foreach (var (v, w) in adj[u])
                    if (ud + w < (dist.TryGetValue(v, out double dv) ? dv : double.PositiveInfinity)) { dist[v] = ud + w; prev[v] = u; }
            }
            var ids = new List<int> { t };
            while (ids[0] != s) ids.Insert(0, prev[ids[0]]);
            return ids.Select(id => g.nodes[id - 1]).ToList();
        }

        // 게임 중 경로 캐시 (포트키 → 포트키). 연결이 없으면 null
        public static List<P> CachedRoute(string stage, string src, string dst)
        {
            var l = Game.LAYOUT;
            string k = $"{stage}:{src}>{dst}";
            if (!l.routes.TryGetValue(k, out var r))
            {
                if (!l.graphs.TryGetValue(stage, out var g)) l.graphs[stage] = g = BuildGraph(l, stage);
                l.routes[k] = r = RouteKeys(g, src, dst);
            }
            return r;
        }
    }
}
