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
        // 짐칸 위 운전석·아래 문 글자 자리 (px). 칸 크기는 GridFrame의 부모(GridArea)에 꽉 차게 트럭마다 다시 잰다
        const float CabH = 34, DoorH = 30;
        float CS = 64;

        [SerializeField] GameObject overlay;
        [SerializeField] TMP_Text title, rate, rateNote, pay, listHeader, emptyHint;
        [SerializeField] Image barFill;
        [Tooltip("GridFrame(테두리) / Grid(칸 원점, 왼쪽 위) / Cells·Pieces·Preview")]
        [SerializeField] RectTransform gridFrame, grid, cellsRoot, piecesRoot, previewRoot;
        [SerializeField] RectTransform list, listViewport, ghost;
        [SerializeField] Button departBtn, resetBtn, closeBtn;
        [Tooltip("마지막으로 놓은 박스를 돌린다")]
        [SerializeField] Button rotateLeftBtn, rotateRightBtn;
        [Header("프리팹")]
        [SerializeField] Image cellPrefab, piecePrefab, miniPrefab;
        [SerializeField] RectTransform cardPrefab;

        class Drag { public Box box; public List<Vector2Int> cells; public int anchor; public Placement placement; public List<Vector2Int> target; public bool valid; public string key; }

        Drag drag;
        Dock shown;
        Placement selected;   // 회전 버튼 대상
        readonly List<(RectTransform rt, Box box)> cards = new List<(RectTransform, Box)>();
        // 지게차 묶음 칸: 칸 좌표로 색상을 밀어서 무지개가 대각선으로 흐른다
        readonly List<(Image img, Vector2Int c, float a)> rainbow = new List<(Image, Vector2Int, float)>();

        const string SOFT = "#5b6775";
        static readonly Color EDGE = new Color(0, 0, 0, .45f), BAD = new Color(.84f, .19f, .19f);

        void Awake()
        {
            departBtn.onClick.AddListener(Loading.Depart);
            resetBtn.onClick.AddListener(() => { Loading.ResetAll(); Render(); });
            closeBtn.onClick.AddListener(() => { CancelDrag(); Loading.Close(); });
            rotateLeftBtn.onClick.AddListener(() => RotateSelected(false));
            rotateRightBtn.onClick.AddListener(() => RotateSelected(true));
            overlay.SetActive(false);
            ghost.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            // 열기·닫기는 로직(Loading.dock)을 따라간다 (E 키, 출발, 닫기 버튼)
            if (Loading.dock != shown)
            {
                CancelDrag();
                selected = null;
                shown = Loading.dock;
                overlay.SetActive(shown != null);
                if (shown != null) Render();
            }
            if (shown != null) HandleInput();
            if (shown != null) Rainbow();
        }

        void Rainbow()
        {
            rainbow.RemoveAll(r => !r.img);
            float t = Time.unscaledTime * 0.6f;
            foreach (var (img, c, a) in rainbow)
                img.color = WithA(Color.HSVToRGB(Mathf.Repeat(t - (c.x + c.y) * 0.12f, 1), 0.55f, 1), a);
        }

        // ---------- 입력 ----------
        void HandleInput()
        {
            // 터치·마우스 공통은 Pointer. 모바일엔 마우스·키보드가 없다
            var ptr = Pointer.current;
            if (ptr == null) return;
            var m = Mouse.current;
            var k = Keyboard.current;
            bool esc = k != null && k.escapeKey.wasPressedThisFrame;
            bool r = m != null && m.rightButton.wasPressedThisFrame;
            var pos = ptr.position.ReadValue();
            if (drag != null)
            {
                // 드래그 중 R 또는 우클릭 = 회전
                if (r || k != null && k.rKey.wasPressedThisFrame) Rotate();
                if (esc) { CancelDrag(); Render(); return; }
                UpdateDrag(pos);
                if (ptr.press.wasReleasedThisFrame) EndDrag();
                return;
            }
            if (esc) { Loading.Close(); return; }
            bool l = ptr.press.wasPressedThisFrame;
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

        // 짐칸 안에 다 들어가면 겹쳐도 일단 놓는다 (한도를 넘긴 겹침은 빨간 테두리, 출발 불가)
        // 짐칸에 걸치기만 하면 놓지 않는다(원래 자리 그대로). 실은 박스를 짐칸 밖에 놓으면 뺀다
        void EndDrag()
        {
            var tr = shown.truck;
            if (drag.target != null && LoadLogic.InTruck(tr, drag.target))
            {
                Loading.Place(drag.box, drag.target, drag.placement);
                selected = tr.placements.LastOrDefault(p => p.box == drag.box);
            }
            else if (drag.target == null && drag.placement != null) Loading.Remove(drag.placement);
            CancelDrag();
            Render();
        }

        // 왼쪽 위 칸을 기준으로 제자리에서 돌린다. 터치는 끄는 중에 버튼을 누를 수 없어서 놓은 뒤에 돌린다
        // 오른쪽·아래 벽을 넘으면 안쪽으로 밀고, 그래도 잠긴 칸에 걸리면 돌리지 않는다
        void RotateSelected(bool cw)
        {
            var tr = shown.truck;
            if (selected == null || !tr.placements.Contains(selected)) return;
            var min = new Vector2Int(selected.cells.Min(c => c.x), selected.cells.Min(c => c.y));
            var cells = cw ? LoadLogic.Rotate(selected.cells) : LoadLogic.RotateCcw(selected.cells);
            var size = new Vector2Int(cells.Max(c => c.x) + 1, cells.Max(c => c.y) + 1);
            min = new Vector2Int(Mathf.Min(min.x, tr.w - size.x), Mathf.Min(min.y, tr.h - size.y));
            var moved = cells.Select(c => c + min).ToList();
            if (!LoadLogic.InTruck(tr, moved)) return;
            Loading.Place(selected.box, moved, selected);
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
        static string Small(string s, int size = 16) => $"<size={size}><color={SOFT}>{s}</color></size>";
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
            rate.text = $"적재율 <size=25><b><color={rc}>{r}%</color></b></size> / 100%";
            barFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01((float)(p.rate / L.maxRate)), 1);
            barFill.color = U.Hex(r > L.maxRate ? 0x8e24aa : 0x4caf50);
            string breakdown = p.objBonus > 0 ? $"칸 {U.Round(f.rate)}% + 목표 {p.objBonus}%p" : $"{f.filled}/{f.total}칸";
            rateNote.text = Small($"({breakdown} · 가득 = {L.fullRate}%)", 17);
            pay.text = $"예상 대금 <size=23><b><color=#b8860b>💰{U.Money(p.total)}</color></b></size> " +
                       Small($"= 기본 {p.baseReward} × 적재 {p.fillMult:F2} × 랭크 {p.rankMult:F2}", 17);

            // 위: 짐칸 (GridArea 가로에 꽉 차게, 세로가 모자라면 세로에 맞춤)
            var area = ((RectTransform)gridFrame.parent).rect;
            CS = Mathf.Floor(Mathf.Min((area.width - 6) / tr.w, (area.height - CabH - DoorH - 6) / tr.h));
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

            // 실린 박스: 칸마다 한 조각, 다른 칸과 맞닿지 않은 변에만 테두리. 놓을 수 없는 자리면 빨간 테두리
            Clear(piecesRoot);
            var invalid = LoadLogic.Invalid(tr);
            foreach (var pl in tr.placements)
            {
                var set = new HashSet<Vector2Int>(pl.cells);
                bool dragging = drag != null && drag.placement == pl;
                var edge = invalid.Contains(pl) ? BAD : EDGE;
                for (int i = 0; i < pl.cells.Count; i++)
                {
                    var c = pl.cells[i];
                    var piece = Instantiate(piecePrefab, piecesRoot);
                    Place(piece.rectTransform, c.x, c.y, CS);
                    piece.color = WithA(BoxColor(pl.box), dragging ? 0.25f : 0.92f);
                    if (pl.box.parts != null) rainbow.Add((piece, c, piece.color.a));
                    foreach (var (side, dir) in new[] { ("Top", Vector2Int.down), ("Right", Vector2Int.right), ("Bottom", Vector2Int.up), ("Left", Vector2Int.left) })
                    {
                        var e = piece.transform.Find(side);
                        e.gameObject.SetActive(!set.Contains(c + dir));
                        e.GetComponent<Image>().color = edge;
                    }
                    piece.transform.Find("Label").GetComponent<TMP_Text>().text = i != 0 ? "" : pl.box.parts != null ? $"🌈×{pl.box.Count}" : Emojis(pl.box);
                    int n = occ[c];
                    var over = piece.transform.Find("Over").gameObject;
                    over.SetActive(n > 1);
                    if (n > 1) over.GetComponentInChildren<TMP_Text>().text = "×" + n;
                }
            }

            // 아래: 출고 대기 박스 (가로 목록)
            var waiting = Loading.Waiting();
            // 파렛트 묶음도 한 개로 센다 (따로 표기하면 머리글이 길어져 칸 밖으로 나감)
            listHeader.text = $"<b>출고 대기 박스 ({waiting.Count})</b>";
            emptyHint.gameObject.SetActive(waiting.Count == 0);
            Clear(list);
            cards.Clear();
            // 묶음을 왼쪽으로. 카드: 위 블록 모양, 아래 번호·금액
            foreach (var b in waiting.OrderBy(b => b.parts == null))
            {
                var card = Instantiate(cardPrefab, list);
                var shape = (RectTransform)card.Find("Shape");
                var size = MiniShape(shape, b);
                var info = (RectTransform)card.Find("Info");
                info.offsetMax = new Vector2(info.offsetMax.x, -(8 + size.y + 4));
                string head = b.parts != null ? $"<b>🌈 묶음 #{b.id}</b>\n박스 {b.Count}개" : $"<b>#{b.id}</b> {Emojis(b)}";
                string bonus = b.parts != null ? $" x{L.bundleRewardMult:F1}" : "";
                info.GetComponent<TMP_Text>().text = $"{head}\n{Small("💰" + U.Money(b.reward) + bonus, 14)}";
                card.GetComponent<LayoutElement>().preferredWidth = Mathf.Max(size.x + 16, 110);
                cards.Add((card, b));
            }

            departBtn.interactable = tr.placements.Count > 0 && invalid.Count == 0;
            rotateLeftBtn.interactable = rotateRightBtn.interactable = selected != null && tr.placements.Contains(selected);
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
            MiniShape(ghost, drag.box, drag.cells, CS);
        }

        // 작은 블록 모양 (원본 miniShape). 크기를 돌려준다. 묶음이면 무지개
        Vector2 MiniShape(RectTransform root, Box box, List<Vector2Int> cells = null, float size = 20)
        {
            cells ??= box.cells;
            foreach (var c in cells)
            {
                var i = Instantiate(miniPrefab, root);
                Place(i.rectTransform, c.x, c.y, size, size - 2);
                i.color = BoxColor(box);
                if (box.parts != null) rainbow.Add((i, c, 1));
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
