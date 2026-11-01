// 상차 미니게임 화면 (원본 js/minigame/loading-ui.js)
// 화면 오브젝트는 씬에 있다 (UiBuilder). 칸·박스·카드처럼 개수가 바뀌는 것만 프리팹으로 만든다
// 입력은 "드래그 시작 / 회전 / 드롭 / 회수" 명령으로만 Loading에 전달한다
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static APC.Game;

namespace APC
{
    public class LoadingView : MonoBehaviour
    {
        const float CS = 56;   // 칸 크기(px)

        [SerializeField] GameObject overlay;
        [SerializeField] TMP_Text title, rate, rateNote, pay, left, listHeader, emptyHint;
        [SerializeField] Image barFill;
        [Tooltip("GridFrame(테두리) / Grid(칸 원점, 왼쪽 위) / Cells·Pieces·Preview")]
        [SerializeField] RectTransform gridFrame, grid, cellsRoot, piecesRoot, previewRoot;
        [SerializeField] RectTransform list, listViewport, ghost;
        [SerializeField] Button departBtn, resetBtn, closeBtn;
        [Header("프리팹")]
        [SerializeField] Image cellPrefab, piecePrefab, miniPrefab;
        [SerializeField] RectTransform cardPrefab;

        class Drag { public Box box; public List<Vector2Int> cells; public int anchor; public Placement placement; public List<Vector2Int> target; public bool valid; public string key; }

        Drag drag;
        Dock shown;
        readonly List<(RectTransform rt, Box box)> cards = new List<(RectTransform, Box)>();

        static readonly string[] RANK_COLORS = { "#9e9e9e", "#8d6e63", "#43a047", "#1e88e5", "#8e24aa", "#f9a825" };
        const string SOFT = "#5b6775";

        void Awake()
        {
            departBtn.onClick.AddListener(Loading.Depart);
            resetBtn.onClick.AddListener(() => { Loading.ResetAll(); Render(); });
            closeBtn.onClick.AddListener(() => { CancelDrag(); Loading.Close(); });
            overlay.SetActive(false);
            ghost.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            // 열기·닫기는 로직(Loading.dock)을 따라간다 (E 키, 출발, 닫기 버튼)
            if (Loading.dock != shown)
            {
                CancelDrag();
                shown = Loading.dock;
                overlay.SetActive(shown != null);
                if (shown != null) Render();
            }
            if (shown != null) HandleInput();
        }

        // ---------- 입력 ----------
        void HandleInput()
        {
            var m = Mouse.current;
            var k = Keyboard.current;
            if (m == null || k == null) return;
            var pos = m.position.ReadValue();
            if (drag != null)
            {
                // 드래그 중 R 또는 우클릭 = 회전
                if (k.rKey.wasPressedThisFrame || m.rightButton.wasPressedThisFrame) Rotate();
                if (k.escapeKey.wasPressedThisFrame) { CancelDrag(); Render(); return; }
                UpdateDrag(pos);
                if (m.leftButton.wasReleasedThisFrame) EndDrag();
                return;
            }
            if (k.escapeKey.wasPressedThisFrame) { Loading.Close(); return; }
            bool l = m.leftButton.wasPressedThisFrame, r = m.rightButton.wasPressedThisFrame;
            if (!l && !r) return;

            var tr = shown.truck;
            var cell = GridCell(pos);
            // 겹친 칸은 나중에 실은 박스가 위에 그려진다
            var pl = cell == null ? null : tr.placements.LastOrDefault(p => p.cells.Contains(cell.Value));
            if (pl != null && r) { Loading.Remove(pl); Render(); return; }
            if (!l) return;
            if (pl != null)
            {
                int anchor = Mathf.Max(0, pl.cells.IndexOf(cell.Value));
                StartDrag(pl.box, LoadLogic.Normalize(pl.cells), anchor, pl, pos);
            }
            else if (RectTransformUtility.RectangleContainsScreenPoint(listViewport, pos, null))
            {
                foreach (var (rt, box) in cards)
                    if (RectTransformUtility.RectangleContainsScreenPoint(rt, pos, null)) { StartDrag(box, box.cells.ToList(), 0, null, pos); break; }
            }
        }

        Vector2Int? GridCell(Vector2 screen)
        {
            var tr = shown.truck;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(grid, screen, null, out var lp);
            if (lp.x < 0 || lp.x >= tr.w * CS || -lp.y < 0 || -lp.y >= tr.h * CS) return null;
            return new Vector2Int(Mathf.FloorToInt(lp.x / CS), Mathf.FloorToInt(-lp.y / CS));
        }

        void StartDrag(Box box, List<Vector2Int> cells, int anchor, Placement placement, Vector2 pos)
        {
            drag = new Drag { box = box, cells = cells, anchor = anchor, placement = placement };
            BuildGhost();
            ghost.gameObject.SetActive(true);
            Render();
            UpdateDrag(pos);
        }

        void Rotate()
        {
            drag.cells = LoadLogic.Rotate(drag.cells);
            drag.key = null;
            BuildGhost();
        }

        void UpdateDrag(Vector2 pos)
        {
            var a = drag.cells[drag.anchor];
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)ghost.parent, pos, null, out var lp);
            ghost.anchoredPosition = lp + new Vector2(-(a.x * CS + CS / 2), a.y * CS + CS / 2);
            var g = GridCell(pos);
            if (g != null)
            {
                drag.target = drag.cells.Select(c => c - a + g.Value).ToList();
                drag.valid = LoadLogic.CanPlace(shown.truck, drag.target, drag.placement);
            }
            else { drag.target = null; drag.valid = false; }
            string key = drag.target == null ? "" : drag.valid + string.Join(";", drag.target);
            if (key != drag.key) { drag.key = key; RenderPreview(); }
        }

        void EndDrag()
        {
            if (drag.valid && drag.target != null) Loading.Place(drag.box, drag.target, drag.placement);
            CancelDrag();
            Render();
        }

        void CancelDrag()
        {
            drag = null;
            ghost.gameObject.SetActive(false);
            Clear(previewRoot);
        }

        // ---------- 렌더 ----------
        static Color BoxColor(Box b) => CONFIG.Type(b.primary).color;
        static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
        static string Small(string s, int size = 11) => $"<size={size}><color={SOFT}>{s}</color></size>";
        static string Emojis(Box b) => string.Concat(b.types.Select(t => CONFIG.Type(t).emoji));

        void Render()
        {
            var d = shown;
            var tr = d?.truck;
            if (tr == null) return;
            var L = CONFIG.loading;
            var p = LoadLogic.Payout(tr);
            var f = p.fill;
            var occ = LoadLogic.Occupancy(tr);
            var dest = CONFIG.destinations[d.idx];

            // 위쪽: 제목·적재율·예상 대금
            title.text = $"🚛 <b><color={Hex(dest.color)}>{dest.name}</color></b>행 · {tr.def.name} 트럭 ({tr.w}×{tr.h})";
            int r = U.Round(p.rate);
            string rc = r > L.maxRate ? "#8e24aa" : r >= 100 ? "#2e9e57" : "#1f2933";
            rate.text = $"적재율 <size=20><b><color={rc}>{r}%</color></b></size> / 100%";
            barFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01((float)(p.rate / L.maxRate)), 1);
            barFill.color = U.Hex(r > L.maxRate ? 0x8e24aa : 0x4caf50);
            string breakdown = p.objBonus > 0 ? $"칸 {U.Round(f.rate)}% + 목표 {p.objBonus}%p" : $"{f.filled}/{f.total}칸";
            rateNote.text = Small($"({breakdown} · 가득 = {L.fullRate}%)", 12);
            pay.text = $"예상 대금 <size=18><b><color=#b8860b>💰{U.Money(p.total)}</color></b></size> " +
                       Small($"= 기본 {p.baseReward} × 적재 {p.fillMult:F2} × 랭크 {p.rankMult:F2}", 12);

            // 왼쪽: 특수 목표·랭크·배율·업그레이드
            var sb = new System.Text.StringBuilder();
            int doneN = p.objectives.Count(o => o.done);
            sb.AppendLine($"<b>🎯 특수 목표</b> {Small($"{doneN}/{p.objectives.Count} 완료 · 적재율 +{p.objBonus}%p")}");
            foreach (var o in p.objectives)
                sb.AppendLine($"{(o.done ? "✅" : "⬜")} {LoadLogic.ObjectiveText(o.o)} {Small($"({o.now})")} <b><color={(o.done ? "#2e9e57" : "#b8860b")}>+{o.o.bonus}%p</color></b>");
            if (p.objectives.Count == 0) sb.AppendLine(Small("목표 없음"));
            sb.AppendLine(Small($"달성 보너스는 적재율에 더해지며 {L.maxRate}% 상한을 넘을 수 있습니다."));
            sb.AppendLine().AppendLine("<b>타입 랭크</b>");
            foreach (var ty in CONFIG.types)
            {
                var ri = p.ranks[ty.id];
                string next = ri.rank < L.rankThresholds.Length ? $" (다음 {L.rankThresholds[ri.rank] - ri.count})" : "";
                sb.AppendLine($"{ty.emoji} {ty.name}<pos=36%><b><color={RANK_COLORS[ri.rank]}>{L.rankNames[ri.rank]}</color></b>  {Small($"{ri.count}개{next}")}");
            }
            sb.AppendLine(Small($"혼합 박스는 들어간 타입마다 1개씩 셉니다. D 이상 1단계당 +{L.rankBonusPerStep * 100}%"));
            sb.AppendLine().AppendLine("<b>적재 배율</b>");
            string mults = string.Join(" · ", L.fillMult.AsEnumerable().Reverse().Select(m => $"{m.min}%↑ x{m.mult:F1}"));
            sb.AppendLine(Small($"100% 미만 x0.5~1.0\n{mults}\n{L.maxRate}% 초과(목표 보너스) 1%p당 +{L.overPerPoint}"));
            sb.AppendLine().AppendLine("<b>업그레이드</b>");
            sb.Append(Small($"🗜️ 압축 적재 {f.overlap}/{G.loadCompress}칸\n⭐ 우선 적재 칸 {f.bonusCovered}/{tr.bonus.Count} (+{L.bonusCellPercent}%씩)"));
            left.text = sb.ToString();

            // 가운데: 짐칸
            gridFrame.sizeDelta = new Vector2(tr.w * CS + 6, tr.h * CS + 6);
            grid.sizeDelta = new Vector2(tr.w * CS, tr.h * CS);
            Clear(cellsRoot);
            for (int y = 0; y < tr.h; y++)
                for (int x = 0; x < tr.w; x++)
                {
                    var c = new Vector2Int(x, y);
                    var cell = Instantiate(cellPrefab, cellsRoot);
                    Place(cell.rectTransform, x, y, CS);
                    bool locked = tr.locked.Contains(c), bonus = !locked && tr.bonus.Contains(c);
                    cell.color = U.Hex(locked ? 0x455a64 : bonus ? 0xf9a825 : 0xcfd8dc);   // 테두리
                    var fill = (RectTransform)cell.transform.Find("Fill");
                    fill.GetComponent<Image>().color = U.Hex(locked ? 0x546e7a : bonus ? 0xfff8e1 : 0xfafafa);
                    float inset = bonus ? 2 : 1;
                    fill.offsetMin = new Vector2(inset, inset);
                    fill.offsetMax = new Vector2(-inset, -inset);
                    cell.GetComponentInChildren<TMP_Text>().text = locked ? "✖" : bonus ? "⭐" : "";
                }

            // 실린 박스: 칸마다 한 조각, 다른 칸과 맞닿지 않은 변에만 테두리
            Clear(piecesRoot);
            foreach (var pl in tr.placements)
            {
                var set = new HashSet<Vector2Int>(pl.cells);
                bool dragging = drag != null && drag.placement == pl;
                for (int i = 0; i < pl.cells.Count; i++)
                {
                    var c = pl.cells[i];
                    var piece = Instantiate(piecePrefab, piecesRoot);
                    Place(piece.rectTransform, c.x, c.y, CS);
                    piece.color = WithA(BoxColor(pl.box), dragging ? 0.25f : 0.92f);
                    piece.transform.Find("Top").gameObject.SetActive(!set.Contains(c + Vector2Int.down));
                    piece.transform.Find("Right").gameObject.SetActive(!set.Contains(c + Vector2Int.right));
                    piece.transform.Find("Bottom").gameObject.SetActive(!set.Contains(c + Vector2Int.up));
                    piece.transform.Find("Left").gameObject.SetActive(!set.Contains(c + Vector2Int.left));
                    piece.transform.Find("Label").GetComponent<TMP_Text>().text = i == 0 ? Emojis(pl.box) : "";
                    int n = occ[c];
                    var over = piece.transform.Find("Over").gameObject;
                    over.SetActive(n > 1);
                    if (n > 1) over.GetComponentInChildren<TMP_Text>().text = "×" + n;
                }
            }

            // 오른쪽: 출고 대기 박스
            listHeader.text = $"<b>출고 대기 박스 ({d.waiting.Count})</b>";
            emptyHint.gameObject.SetActive(d.waiting.Count == 0);
            Clear(list);
            cards.Clear();
            foreach (var b in d.waiting)
            {
                var card = Instantiate(cardPrefab, list);
                var shape = (RectTransform)card.Find("Shape");
                var size = MiniShape(shape, b.cells, 16, BoxColor(b));
                var info = (RectTransform)card.Find("Info");
                info.offsetMin = new Vector2(8 + size.x + 8, info.offsetMin.y);
                string items = string.Join(" ", b.items.Select(kv => $"{CONFIG.items[kv.Key].emoji}x{kv.Value}"));
                info.GetComponent<TMP_Text>().text = $"<b>#{b.id}</b> {Emojis(b)} {b.cells.Count}칸\n{Small(items)}\n{Small("💰" + U.Money(b.reward))}";
                card.GetComponent<LayoutElement>().preferredHeight = Mathf.Max(size.y, 50) + 12;
                cards.Add((card, b));
            }

            departBtn.interactable = tr.placements.Count > 0;
            if (drag != null) { drag.key = null; RenderPreview(); }
        }

        void RenderPreview()
        {
            Clear(previewRoot);
            var tr = shown.truck;
            if (drag?.target == null) return;
            foreach (var c in drag.target)
            {
                if (c.x < 0 || c.y < 0 || c.x >= tr.w || c.y >= tr.h) continue;
                var i = Instantiate(miniPrefab, previewRoot);
                Place(i.rectTransform, c.x, c.y, CS);
                i.color = drag.valid ? new Color32(46, 158, 87, 115) : new Color32(214, 48, 49, 115);
            }
        }

        void BuildGhost()
        {
            Clear(ghost);
            MiniShape(ghost, drag.cells, CS, BoxColor(drag.box));
        }

        // 작은 블록 모양 (원본 miniShape). 크기를 돌려준다
        Vector2 MiniShape(RectTransform root, List<Vector2Int> cells, float size, Color color)
        {
            foreach (var c in cells)
            {
                var i = Instantiate(miniPrefab, root);
                Place(i.rectTransform, c.x, c.y, size, size - 2);
                i.color = color;
            }
            var w = cells.Max(c => c.x) + 1;
            var h = cells.Max(c => c.y) + 1;
            return root.sizeDelta = new Vector2(w * size, h * size);
        }

        // 왼쪽 위 기준 칸 배치
        static void Place(RectTransform rt, int x, int y, float step, float size = -1)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x * step, -y * step);
            rt.sizeDelta = Vector2.one * (size < 0 ? step : size);
        }

        static Color WithA(Color c, float a) { c.a = a; return c; }

        static void Clear(Transform t)
        {
            // 끄고 지운다: Destroy는 프레임 끝에 처리되어 그 전까지 레이아웃에 남는다
            for (int i = t.childCount - 1; i >= 0; i--) { var go = t.GetChild(i).gameObject; go.SetActive(false); Destroy(go); }
        }
    }
}
