// 시설·플레이어 위 라벨 (원본 ui.js buildLabels/refreshLabels + labels.js)
// 라벨은 3D 위치의 화면 좌표 위(가운데 아래 기준)에 붙는다. 잠긴 시설 라벨은 눌러서 해금, 상차 가능한 도크는 눌러서 상차 패널
// 시설 개수는 배치마다 달라서 라벨은 프리팹으로 만든다
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static APC.Game;

namespace APC
{
    [DefaultExecutionOrder(100)]   // 카메라가 움직인 뒤 위치를 맞춘다
    public class LabelView : MonoBehaviour
    {
        [SerializeField] Camera cam;
        [SerializeField] Image labelPrefab;

        enum Style { Normal, Small, SmallDim, Actor, Me }

        class Label
        {
            public Func<Vector3> pos;
            public Image bg;
            public TMP_Text text;
            public Button btn;
            public Action act;
            public string html;
        }

        readonly List<Label> labels = new List<Label>();
        readonly Dictionary<int, Label> albaLabels = new Dictionary<int, Label>();
        Label[] inTrucks, bins, tables, docks;
        Label staging, trash, rest, waiting, purifier, player;
        float timer;

        void Start()
        {
            var L = LAYOUT;
            Func<Vector3> At(double x, double y, double z) => () => CoordUtil.ToWorld(x, z, (float)y);
            // 트럭 라벨은 맵 밖으로 나가지 않게 주차 위치 근처까지만 따라간다
            inTrucks = G.inTrucks.Select(tr => Add(() => CoordUtil.ToWorld(S.TruckPos(tr.slot, Math.Min(tr.off, 3)), 3), Style.Small)).ToArray();
            staging = Add(At(L.staging.x, 1.8, L.staging.z), Style.Normal);
            bins = G.bins.Select(b => b.slot != null ? Add(At(b.slot.x, 2.0, b.slot.z), Style.Small) : null).ToArray();
            tables = G.tables.Select(t => Add(At(t.slot.x, 2.4, t.slot.z), Style.Small)).ToArray();
            docks = G.docks.Select(d => d.slot != null ? Add(At(d.slot.x, 3.2, d.slot.z), Style.Small) : null).ToArray();
            trash = Add(At(L.trash.x, 1.8, L.trash.z), Style.SmallDim);
            rest = Add(At(L.rest.x, 2.2, L.rest.z - L.rest.d / 2), Style.Small);
            waiting = Add(At(L.waiting.x, 1.4, L.waiting.z + L.waiting.d / 2), Style.SmallDim);
            purifier = Add(At(L.purifier.x, 2.4, L.purifier.z), Style.Small);
            player = Add(() => CoordUtil.ToWorld(G.player.x, G.player.z, 2.3f), Style.Me);
        }

        Label Add(Func<Vector3> pos, Style style)
        {
            var bg = Instantiate(labelPrefab, transform);
            var lb = new Label { pos = pos, bg = bg, text = bg.GetComponentInChildren<TMP_Text>(), btn = bg.GetComponent<Button>() };
            bool actor = style == Style.Me || style == Style.Actor;
            bg.color = style == Style.Me ? U.Hex(0xe3eefc) : style == Style.Actor ? new Color(1, 1, 1, .92f)
                : new Color(22 / 255f, 30 / 255f, 42 / 255f, style == Style.SmallDim ? .55f : .82f);
            lb.text.color = actor ? U.Hex(0x1f2933) : Color.white;
            lb.text.fontSize = style == Style.Normal ? 12 : 11;
            if (style == Style.Me) lb.text.fontStyle = FontStyles.Bold;
            lb.btn.onClick.AddListener(() => lb.act?.Invoke());
            labels.Add(lb);
            return lb;
        }

        void Set(Label lb, string html, Action act = null)
        {
            lb.act = act;
            // 누를 수 있는 라벨만 클릭을 받는다 (나머지는 월드 클릭 이동을 막지 않게)
            lb.bg.raycastTarget = act != null;
            lb.btn.enabled = act != null;
            if (lb.html == html) return;
            lb.html = html;
            lb.text.text = html;
        }

        void LateUpdate()
        {
            if (G == null) return;
            timer -= Time.unscaledDeltaTime;
            if (timer <= 0) { timer = 0.1f; Refresh(); }
            var root = (RectTransform)transform;
            foreach (var lb in labels)
            {
                bool show = !string.IsNullOrEmpty(lb.html);
                var sp = cam.WorldToScreenPoint(lb.pos());
                show &= sp.z > 0;
                if (lb.bg.gameObject.activeSelf != show) lb.bg.gameObject.SetActive(show);
                if (!show) continue;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, null, out var lp);
                lb.bg.rectTransform.anchoredPosition = lp;
            }
        }

        static string HandIcons(List<HandEntry> hand) => string.Concat(hand.Select(e =>
            e.kind == HandKind.Box ? "📦" : e.kind == HandKind.Broken ? "💔" : CONFIG.items[e.item].emoji));

        static string Locked(string text, int cost)
        {
            bool can = G.money >= cost;
            return $"<color={(can ? "#ffffff" : "#e0e0e0")}>🔒 {text}</color>\n<color={(can ? "#8be0a4" : "#b0bec5")}>{Rich.Money(cost)}</color>";
        }

        void Refresh()
        {
            var C = CONFIG;
            for (int i = 0; i < G.inTrucks.Count; i++)
            {
                var tr = G.inTrucks[i];
                string html = "";
                if (i < S.ParkingCount())
                    html = tr.state == TruckState.Present ? $"🚚 입고 트럭 {i + 1} <b>{tr.items.Count}</b>개"
                        : tr.state == TruckState.Away ? $"🅿️ {i + 1} 다음 입고 {Math.Ceiling(tr.timer)}초" : $"🚚 {i + 1} 이동 중";
                Set(inTrucks[i], html);
            }
            Set(staging, $"📥 하차장 <b>{G.staging.Count}</b>/{S.StagingCap()}");

            foreach (var b in G.bins)
            {
                if (b.slot == null) continue;
                var it = C.items[b.idx];
                int i = b.idx;
                if (!b.unlocked) { Set(bins[i], Locked($"{it.emoji} {it.name}", b.slot.cost), () => Shop.BuyBin(i)); continue; }
                string arm = S.IsAuto("inspect") && b.armProgress > 0 ? " 🦾" : "";
                string full = S.InboundRoom(i) <= 0 ? " ⛔" : "";
                Set(bins[i], $"<b>{it.emoji} {it.name}</b>{arm}{full}\n✅{b.checkedCount} ❔{b.uncheckedCount}{(b.broken > 0 ? " 💔" + b.broken : "")} {Rich.Small($"/{S.BinCap()}", 11, "#b0bec5")}");
            }

            foreach (var t in G.tables)
            {
                int i = t.idx;
                if (!t.unlocked) { Set(tables[i], Locked($"포장대 {i + 1}", t.slot.cost), () => Shop.BuyTable(i)); continue; }
                string html = $"<b>🎁 포장대 {i + 1}</b>";
                if (t.order != null)
                {
                    html += $" #{t.order.id}\n" + string.Join(" ", t.order.items.Select(kv =>
                        $"{C.items[kv.Key].emoji}{(t.delivered.TryGetValue(kv.Key, out int g) ? g : 0)}/{kv.Value}"));
                    if (S.TableReady(t))
                    {
                        double prog = S.IsAuto("pack") ? t.machineProgress / C.automation.packTime : t.packProgress / C.timing.pack;
                        html += "\n포장 대기" + Rich.Bar(prog);
                    }
                }
                if (t.boxes.Count > 0) html += $"\n📦 완료 x{t.boxes.Count}";
                Set(tables[i], html);
            }

            var loadable = Player.LoadableDock();
            foreach (var d in G.docks)
            {
                if (d.slot == null) continue;
                var dest = C.destinations[d.idx];
                int i = d.idx;
                if (!d.unlocked) { Set(docks[i], Locked($"{dest.name} 도크", d.slot.cost), () => Shop.BuyDock(i)); continue; }
                string html = $"<b><color={Rich.Hex(dest.color)}>🏁 {dest.name}</color></b> 📦{d.waiting.Count}";
                var tr = d.truck;
                Action act = null;
                if (tr == null) html += $"\n🚛 도착까지 {Math.Ceiling(d.truckTimer)}초";
                else if (tr.state != TruckState.Present) html += "\n🚛 이동 중";
                else
                {
                    var pay = LoadLogic.Payout(tr);
                    html += $"\n🚛 {tr.def.name}({tr.w}×{tr.h}) {U.Round(pay.rate)}%";
                    if (tr.mode == "auto") html += S.IsAuto("load") ? " 🦾" : " 👷";
                    else if (tr.objectives.Count > 0) html += $" 🎯{pay.objectives.Count(o => o.done)}/{tr.objectives.Count}";
                    if (loadable == d)
                    {
                        html += "\n<color=#ffca28><b>[E] 상차하기</b></color>";
                        act = () => Loading.Open(d);
                    }
                }
                Set(docks[i], html, act);
            }

            Set(trash, "🗑️ 쓰레기통");
            Set(rest, $"🛋️ {C.care.tierNames[G.careRest]} 휴게실");
            Set(waiting, "🪑 대기 구역");
            if (G.carePurifier < 0) Set(purifier, Locked("🌬️ 공기청정기", C.care.purifier.costs[0]), Shop.UpPurifier);
            else Set(purifier, $"🌬️ {C.care.tierNames[G.carePurifier]} 공기청정기");

            var p = G.player;
            string ph = $"👔 관리자 {HandIcons(p.hand)}";
            if (p.zone != null)
            {
                ph += $"\n<color={Rich.ACCENT}>{Zones.Name(p.zone)}</color> · {p.action ?? "…"}";
                if (p.busy != null)
                {
                    double max = p.busy == "inspect" ? C.timing.inspect : C.timing.pack;
                    ph += Rich.Bar(p.progress / max);
                }
            }
            // 지게차 운전 중: 파렛트가 가리지 않게 E 안내만 반투명으로 (묶음·낱개는 파렛트에 보인다)
            bool riding = Forklifts.Riding;
            if (riding) ph = $"<b>{(Forklifts.LoadableDock() != null ? "[E] 상차하기" : "[E] 하차")}</b>";
            else if (Forklifts.CanBoard()) ph += "\n<color=#ffca28><b>[E] 지게차 탑승</b></color>";
            player.bg.color = Palette.WithAlpha(U.Hex(0xe3eefc), riding ? 0.35f : 1);
            Set(player, ph);

            // 알바 라벨: 고용·해고에 맞춰 만들고 지운다
            foreach (var id in albaLabels.Keys.Where(id => !G.albas.Any(a => a.id == id)).ToList())
            {
                Destroy(albaLabels[id].bg.gameObject);
                labels.Remove(albaLabels[id]);
                albaLabels.Remove(id);
            }
            foreach (var a in G.albas)
            {
                if (!albaLabels.TryGetValue(a.id, out var lb))
                {
                    albaLabels[a.id] = lb = Add(() => CoordUtil.ToWorld(a.x, a.z, 2.3f), Style.Actor);
                    player.bg.transform.SetAsLastSibling();   // 원본 .me는 z-index 2로 맨 위
                }
                string prog = a.progress > 0 && a.progress < 1 ? Rich.Bar(a.progress) : "";
                Set(lb, $"{Rich.Face(a)} {a.name} {HandIcons(a.hand)}\n{Rich.Mt(a.mt, false)}{prog}");
            }
        }
    }
}
