// 상차 미니게임 로직 (원본 js/minigame/loading-logic.js, 차곡차곡 이식)
// 화면과 분리된 순수 판정 함수만 둔다. 트럭 짐칸 = 그리드, 포장 박스 = 물품(블록). 칸 좌표 = (x, y), y는 아래로
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static APC.Game;

namespace APC
{
    public class FillInfo { public int filled, total, bonusCovered, overlap; public double ratio, rate; }
    public class RankInfo { public int count, rank; }
    public class ObjectiveState { public Objective o; public bool done; public string now; }

    public class Payout
    {
        public List<Box> boxes;
        public FillInfo fill;
        public Dictionary<string, RankInfo> ranks;
        public List<ObjectiveState> objectives;
        public int objBonus, total;
        public double rate, fillMult, rankMult;
        public int baseReward;
    }

    public static class LoadLogic
    {
        public static List<Vector2Int> Normalize(IEnumerable<Vector2Int> cells)
        {
            var list = cells.ToList();
            int minX = list.Min(c => c.x), minY = list.Min(c => c.y);
            return list.Select(c => new Vector2Int(c.x - minX, c.y - minY)).ToList();
        }

        // 90도 시계 방향 (화면 좌표, y가 아래)
        public static List<Vector2Int> Rotate(IEnumerable<Vector2Int> cells) => Normalize(cells.Select(c => new Vector2Int(-c.y, c.x)));

        public static ShapeDef RandomShape(int units) => U.Pick(CONFIG.loading.Shapes(U.Clamp(units, 1, 5)));

        public static OutTruck CreateTruck()
        {
            var def = U.Weighted(CONFIG.loading.truckTypes, t => t.weight);
            var all = new List<Vector2Int>();
            for (int y = 0; y < def.h; y++)
                for (int x = 0; x < def.w; x++) all.Add(new Vector2Int(x, y));
            // 잠긴 칸: 트럭 종류별 범위 안에서 개수와 위치 모두 랜덤
            var locked = new HashSet<Vector2Int>(U.Shuffle(all.ToList()).Take(U.RandInt(def.locked[0], def.locked[1])));
            var free = all.Where(k => !locked.Contains(k)).ToList();
            var bonus = new HashSet<Vector2Int>(U.Shuffle(free).Take(G.loadBonus));
            return new OutTruck
            {
                def = def, w = def.w, h = def.h, locked = locked, bonus = bonus,
                objectives = GenObjectives(free.Count),
                state = TruckState.Arriving, off = S.TRUCK_AWAY,
            };
        }

        public static int TotalCells(OutTruck truck) => truck.w * truck.h - truck.locked.Count;

        public static Dictionary<Vector2Int, int> Occupancy(OutTruck truck, Placement ignore = null)
        {
            var occ = new Dictionary<Vector2Int, int>();
            foreach (var p in truck.placements)
            {
                if (p == ignore) continue;
                foreach (var c in p.cells) occ[c] = occ.TryGetValue(c, out int n) ? n + 1 : 1;
            }
            return occ;
        }

        public static int OverlapUsed(Dictionary<Vector2Int, int> occ) => occ.Values.Sum(c => Math.Max(0, c - 1));

        // cells: 짐칸 좌표로 옮긴 칸 목록. 겹친 칸 합 ≤ 압축 적재 레벨이면 허용
        public static bool CanPlace(OutTruck truck, IEnumerable<Vector2Int> cells, Placement ignore = null)
        {
            var list = cells.ToList();
            foreach (var c in list)
            {
                if (c.x < 0 || c.y < 0 || c.x >= truck.w || c.y >= truck.h) return false;
                if (truck.locked.Contains(c)) return false;
            }
            var occ = Occupancy(truck, ignore);
            foreach (var c in list) occ[c] = occ.TryGetValue(c, out int n) ? n + 1 : 1;
            return OverlapUsed(occ) <= G.loadCompress;
        }

        // 적재율: 모든 칸을 다 채우면 fullRate(200%), ratio는 채운 칸 비율(0~1)
        public static FillInfo Fill(OutTruck truck)
        {
            int total = TotalCells(truck);
            var c = CONFIG.loading;
            if (truck.mode == "auto")
            {
                double r = (double)truck.autoCells / total;
                return new FillInfo { filled = truck.autoCells, total = total, ratio = r, bonusCovered = 0, rate = r * c.fullRate };
            }
            var occ = Occupancy(truck);
            int bonusCovered = truck.bonus.Count(k => occ.ContainsKey(k));
            double ratio = (double)occ.Count / total;
            double rate = Math.Min(c.maxRate, ratio * c.fullRate + bonusCovered * c.bonusCellPercent);
            return new FillInfo { filled = occ.Count, total = total, ratio = ratio, bonusCovered = bonusCovered, rate = rate, overlap = OverlapUsed(occ) };
        }

        public static List<Box> LoadedBoxes(OutTruck truck) => truck.mode == "auto" ? truck.autoBoxes : truck.placements.Select(p => p.box).ToList();

        public static int RankIndex(int count) => CONFIG.loading.rankThresholds.Count(t => count >= t);

        // 지게차 묶음은 든 박스 수 (출고 수·랭크·박스 목표에 쓴다)
        public static int BoxCount(List<Box> boxes) => boxes.Sum(b => b.Count);

        // 혼합 박스는 들어간 타입마다 1개씩 센다. 묶음은 든 박스마다 센다
        public static Dictionary<string, RankInfo> Ranks(List<Box> boxes)
        {
            var output = new Dictionary<string, RankInfo>();
            foreach (var t in CONFIG.types) output[t.id] = new RankInfo();
            foreach (var b in boxes) foreach (var pb in b.parts ?? new List<Box> { b }) foreach (var t in pb.types) output[t].count++;
            foreach (var r in output.Values) r.rank = RankIndex(r.count);
            return output;
        }

        public static double FillMult(double rate)
        {
            var c = CONFIG.loading;
            if (rate > c.maxRate) return c.fillMult[0].mult + (rate - c.maxRate) * c.overPerPoint;
            var hit = c.fillMult.Find(f => rate >= f.min);
            return hit != null ? hit.mult : 0.5 + 0.5 * (rate / 100);
        }

        public static double RankMult(Dictionary<string, RankInfo> ranks) => 1 + ranks.Values.Sum(r => r.rank) * CONFIG.loading.rankBonusPerStep;

        // ---------- 특수 목표 ----------
        // 트럭마다 1~5개. 해금된 품목/타입 안에서만 뽑는다
        public static List<Objective> GenObjectives(int cells)
        {
            var c = CONFIG.loading.objectives;
            var items = G.bins.Where(b => b.unlocked).Select(b => b.item).ToList();
            var types = items.Select(i => CONFIG.items[i].type).Distinct().ToList();
            var pool = new List<Objective>();
            foreach (var t in types)
            {
                var ranks = c.rank.ranks.Where(r => r < 3 || cells >= c.rank.bigTruckCells).ToList();
                int r = U.Pick(ranks);
                pool.Add(new Objective { kind = "rank", type = t, rank = r, bonus = c.rank.bonus[r] });
            }
            var f = U.Pick(c.fill);
            pool.Add(new Objective { kind = "fill", rate = f.rate, bonus = f.bonus });
            int n = U.RandInt(c.boxes.min, Math.Max(c.boxes.min, cells / c.boxes.perCells));
            pool.Add(new Objective { kind = "boxes", n = n, bonus = n * c.boxes.bonusPer });
            pool.Add(new Objective { kind = "item", item = U.Pick(items), bonus = c.item.bonus });
            var kinds = c.kinds.Where(k => k.n <= types.Count).ToList();
            if (kinds.Count > 0) { var k = U.Pick(kinds); pool.Add(new Objective { kind = "kinds", n = k.n, bonus = k.bonus }); }
            int count = Math.Min(pool.Count, U.RandInt(c.count[0], c.count[1]));
            return U.Shuffle(pool).Take(count).ToList();
        }

        public static string ObjectiveText(Objective o)
        {
            var rn = CONFIG.loading.rankNames;
            switch (o.kind)
            {
                case "rank": { var t = CONFIG.Type(o.type); return $"{t.emoji} {t.name} 랭크 {rn[o.rank]} 이상"; }
                case "fill": return $"적재율 {o.rate}% 이상";
                case "boxes": return $"박스 {o.n}개 이상 싣기";
                case "item": { var it = CONFIG.items[o.item]; return $"{it.emoji} {it.name} 든 박스 싣기"; }
                case "kinds": return $"서로 다른 타입 {o.n}종 이상";
            }
            return "";
        }

        // 목표마다 달성 여부와 현재 진행 표기. 박스를 빼면 다시 미완료가 된다
        public static List<ObjectiveState> EvalObjectives(OutTruck truck, List<Box> boxes, Dictionary<string, RankInfo> ranks, FillInfo fill)
        {
            var rn = CONFIG.loading.rankNames;
            int typeCount = boxes.SelectMany(b => b.types).Distinct().Count();
            return truck.objectives.Select(o =>
            {
                bool done = false;
                string now = "";
                switch (o.kind)
                {
                    case "rank": { int r = ranks[o.type].rank; done = r >= o.rank; now = rn[r]; break; }
                    case "fill": done = fill.rate >= o.rate; now = U.Round(fill.rate) + "%"; break;
                    case "boxes": { int n = BoxCount(boxes); done = n >= o.n; now = $"{n}/{o.n}"; break; }
                    case "item": { int n = boxes.Count(b => b.items.TryGetValue(o.item, out int q) && q > 0); done = n > 0; now = n + "개"; break; }
                    case "kinds": done = typeCount >= o.n; now = $"{typeCount}/{o.n}"; break;
                }
                return new ObjectiveState { o = o, done = done, now = now };
            }).ToList();
        }

        public static Payout Payout(OutTruck truck)
        {
            var boxes = LoadedBoxes(truck);
            var f = Fill(truck);
            var ranks = Ranks(boxes);
            int baseReward = boxes.Sum(b => b.reward);
            // 특수 목표는 수동 상차(미니게임)에만 적용. 달성 보너스는 적재율에 더하며 200% 상한을 넘을 수 있다
            var objectives = truck.mode == "auto" ? new List<ObjectiveState>() : EvalObjectives(truck, boxes, ranks, f);
            int objBonus = objectives.Where(o => o.done).Sum(o => o.o.bonus);
            double rate = f.rate + objBonus;
            double fm = FillMult(rate), rm = RankMult(ranks);
            return new Payout
            {
                boxes = boxes, fill = f, ranks = ranks, objectives = objectives, objBonus = objBonus, rate = rate,
                baseReward = baseReward, fillMult = fm, rankMult = rm, total = U.Round(baseReward * fm * rm),
            };
        }

        // 알바/자동화 고정 적재율용
        public static bool AutoFits(OutTruck truck, Box box) => truck.autoCells + box.cells.Count <= TotalCells(truck);
    }
}
