// 왼쪽 패널 (원본 ui.js openPanel/renderPanel/panel_facility/panel_upgrade)
// 내용이 바뀌었을 때만(서명 비교, 0.5초마다) 줄을 다시 만든다. 줄·글자는 프리팹
using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static APC.Game;

namespace APC
{
    public class PanelView : MonoBehaviour
    {
        [SerializeField] GameObject root;
        [SerializeField] RectTransform content;
        [SerializeField] Button closeBtn;
        [Header("프리팹")]
        [SerializeField] TMP_Text textPrefab;
        [Tooltip("Label / Ok / Buy(버튼)")]
        [SerializeField] RectTransform rowPrefab;
        [Tooltip("Info / Bottom(Assign 드롭다운, Status, Fire 버튼)")]
        [SerializeField] RectTransform albaRowPrefab;

        string panelName, sig;
        float timer;
        // 다시 그리지 않고 0.5초마다 바꾸는 알바 카드 글자 (원본 data-mt, data-status)
        readonly System.Collections.Generic.List<(Alba a, RectTransform row, TMP_Text info, TMP_Text status)> albaRows =
            new System.Collections.Generic.List<(Alba, RectTransform, TMP_Text, TMP_Text)>();

        public string PanelName => panelName;

        // 간식 드롭: 화면 좌표 아래의 직원 카드
        public Alba AlbaAtRow(Vector2 screen)
        {
            if (panelName != "alba") return null;
            foreach (var r in albaRows)
                if (r.row && RectTransformUtility.RectangleContainsScreenPoint(r.row, screen, null)) return r.a;
            return null;
        }

        void Awake()
        {
            closeBtn.onClick.AddListener(Close);
            root.SetActive(false);
        }

        public void Toggle(string name)
        {
            if (panelName == name) { Close(); return; }
            panelName = name;
            root.SetActive(true);
            Render(true);
        }

        // 바깥(디버그 패널)에서 상태를 바꾼 뒤 바로 다시 그린다
        public void Refresh() { if (panelName != null) Render(true); }

        public void Close()
        {
            panelName = null;
            root.SetActive(false);
        }

        void Update()
        {
            if (G == null) return;
            var k = Keyboard.current;
            // 상차 패널이 열려 있으면 ESC는 그쪽이 쓴다
            if (k != null && k.escapeKey.wasPressedThisFrame && Loading.dock == null) Close();
            if (panelName == null) return;
            timer -= Time.unscaledDeltaTime;
            if (timer <= 0) { timer = 0.5f; Render(false); }
        }

        string Signature() => string.Join("|", panelName, G.money, string.Join(",", G.stages.Values), G.careRest, G.carePurifier,
            G.loadCompress, G.loadBonus, string.Join(",", G.fac.Values),
            string.Concat(G.bins.Select(b => b.unlocked ? 1 : 0)), string.Concat(G.tables.Select(t => t.unlocked ? 1 : 0)),
            string.Concat(G.docks.Select(d => d.unlocked ? 1 : 0)),
            string.Join(",", G.albas.Select(a => $"{a.id}:{a.stage}:{a.unpaid}:{Albas.SkillLv(a.skLift)}.{Albas.SkillLv(a.skCarry)}.{Albas.SkillLv(a.skPack)}.{Albas.SkillLv(a.skInspect)}")),
            string.Join(",", G.candidates.Select(c => c.name)));

        void Render(bool force)
        {
            string s = Signature();
            // 드롭다운이 펼쳐져 있으면 다시 그리지 않는다 (원본: select에 포커스가 있으면 건너뜀)
            bool open = content.GetComponentsInChildren<TMP_Dropdown>().Any(d => d.IsExpanded);
            if (force || (s != sig && !open))
            {
                sig = s;
                albaRows.Clear();
                for (int i = content.childCount - 1; i >= 0; i--) { var go = content.GetChild(i).gameObject; go.SetActive(false); Destroy(go); }
                if (panelName == "facility") Facility();
                else if (panelName == "upgrade") Upgrade();
                else if (panelName == "alba") AlbaPanel();
            }
            foreach (var r in albaRows)
            {
                r.info.text = AlbaInfo(r.a);
                r.status.text = $"{Face(r.a)} {r.a.status}";
            }
        }

        // ---------- 내용 ----------
        void Facility()
        {
            var c = CONFIG.care;
            Text("🏗️ 시설", 18, true);
            Text("보관함 (품목)", 14, true);
            // 배치에 자리가 있는 시설만 표시
            foreach (var b in G.bins)
            {
                if (b.slot == null) continue;
                var it = CONFIG.items[b.idx];
                var ty = CONFIG.Type(it.type);
                int i = b.idx;
                Buy($"{it.emoji} {it.name} {Rich.Small($"{ty.emoji}{ty.name} · 가격 {it.price}")}", b.unlocked ? "해금됨" : null, "해금", b.slot.cost, () => Shop.BuyBin(i));
            }
            Text("포장대", 14, true);
            foreach (var t in G.tables)
            {
                int i = t.idx;
                Buy($"🎁 포장대 {i + 1}", t.unlocked ? "사용 중" : null, "해금", t.slot.cost, () => Shop.BuyTable(i));
            }
            Text("출고 도크 (목적지)", 14, true);
            foreach (var d in G.docks)
            {
                if (d.slot == null) continue;
                var dest = CONFIG.destinations[d.idx];
                int i = d.idx;
                Buy($"<color={Rich.Hex(dest.color)}>🏁 {dest.name}</color>", d.unlocked ? "운영 중" : null, "해금", d.slot.cost, () => Shop.BuyDock(i));
            }
            int rt = G.careRest, pt = G.carePurifier;
            Text("케어시설", 14, true);
            bool restMax = rt + 1 >= c.tierNames.Length, purMax = pt + 1 >= c.tierNames.Length;
            Buy($"🛋️ {c.tierNames[rt]} 휴게실\n{Rich.Small($"회복 MT {c.rest.recover[rt]}/초 · MT {c.rest.until[rt]}까지 휴식")}",
                restMax ? "최고 티어" : null, restMax ? "" : c.tierNames[rt + 1] + "으로", restMax ? 0 : c.rest.costs[rt + 1], Shop.UpRest);
            Buy($"🌬️ 공기청정기 {(pt < 0 ? "(미설치)" : c.tierNames[pt])}\n{Rich.Small(pt < 0 ? "작업 시 일정 확률로 MT 감소 없음" : $"발동 확률 {U.Round(c.purifier.chance[pt] * 100)}%")}",
                purMax ? "최고 티어" : null, purMax ? "" : pt < 0 ? "설치" : c.tierNames[pt + 1] + "으로", purMax ? 0 : c.purifier.costs[pt + 1], Shop.UpPurifier);
        }

        void Upgrade()
        {
            Text("⬆️ 업그레이드", 18, true);
            Text("공정", 14, true);
            Text("Lv1~3: 알바 배치 슬롯 1~3명 · Lv4: 자동화 (하차는 자동화 없음)", 12, false);
            foreach (var s in CONFIG.stages)
            {
                int lv = G.stages[s.id], max = s.costs.Length;
                string info = S.IsAuto(s.id) ? "🤖 자동화" : $"슬롯 {S.StageCount(s.id)}/{lv}명";
                string id = s.id;
                Buy($"{s.emoji} {s.name} <b>Lv{lv}</b>\n{Rich.Small(info)}", lv >= max ? "최대" : null,
                    lv + 1 == 4 ? "🤖 자동화" : $"Lv{lv + 1}", lv >= max ? 0 : s.costs[lv], () => Shop.UpStage(id));
            }
            Text("물류 시설", 14, true);
            foreach (var key in FacilityCfg.Keys)
            {
                var f = CONFIG.facility.Get(key);
                var values = S.FacValues(key);
                var costs = S.FacCosts(key);
                int lv = G.fac[key];
                bool last = lv + 1 >= values.Length;
                Buy($"{f.emoji} {f.name} <b>{values[lv]}{f.unit}</b>\n{Rich.Small(f.desc)}", last ? "최대" : null,
                    last ? "" : $"{values[lv + 1]}{f.unit}로", last ? 0 : costs[lv + 1], () => Shop.UpFacility(key));
            }
            Text("보관함 + 하차장 + 운송 중인 물품의 합이 최대 보관량에 닿은 품목(⛔)은 더 이상 배송되지 않습니다.", 12, false);
            Text("상차 미니게임", 14, true);
            var L = CONFIG.loading;
            foreach (var key in new[] { "compress", "bonus" })
            {
                var u = key == "compress" ? L.upgrades.compress : L.upgrades.bonus;
                int lv = key == "compress" ? G.loadCompress : G.loadBonus;
                string desc = key == "compress" ? $"트럭마다 겹쳐 놓기 {lv}칸" : $"트럭마다 우선 적재 칸 {lv}개 (+{L.bonusCellPercent}%씩)";
                Buy($"{u.emoji} {u.name} <b>Lv{lv}</b>/{u.max}\n{Rich.Small(desc)}", lv >= u.max ? "최대" : null,
                    $"Lv{lv + 1}", lv >= u.max ? 0 : u.costs[lv], () => Shop.UpLoad(key));
            }
            Text("알바/자동화 상차는 고정 적재율이라 미니게임 업그레이드가 적용되지 않습니다.", 12, false);
        }

        // ---------- 조각 ----------
        void Text(string s, float size, bool heading)
        {
            var t = Instantiate(textPrefab, content);
            t.fontSize = size;
            t.text = heading ? $"<b>{s}</b>" : s;
            t.color = heading ? U.Hex(0x1f2933) : U.Hex(0x5b6775);
            // 원본 h3 위 여백 16px
            if (heading && size < 18) t.margin = new Vector4(0, 10, 0, 0);
        }

        // ok가 있으면 초록 글자, 없으면 구매 버튼 (돈이 모자라면 흐리게, 누를 수 없음)
        void Buy(string label, string ok, string btn, int cost, Action act) =>
            Row(label, ok, $"{btn} {Rich.Money(cost)}", G.money >= cost, act);

        void Row(string label, string ok, string btn, bool enabled, Action act)
        {
            var row = Instantiate(rowPrefab, content);
            row.Find("Label").GetComponent<TMP_Text>().text = label;
            var okText = row.Find("Ok").GetComponent<TMP_Text>();
            okText.gameObject.SetActive(ok != null);
            okText.text = ok;
            var buy = row.Find("Buy").GetComponent<Button>();
            buy.gameObject.SetActive(ok == null);
            if (ok != null) return;
            buy.GetComponentInChildren<TMP_Text>().text = btn;
            Enable(buy, enabled);
            buy.onClick.AddListener(() => { act(); Render(true); });
        }

        static void Enable(Button b, bool on)
        {
            b.interactable = on;
            b.GetComponent<CanvasGroup>().alpha = on ? 1 : 0.4f;
        }

        // ---------- 알바 패널 (원본 panel_alba) ----------
        void AlbaPanel()
        {
            var c = CONFIG.alba;
            Text("👷 아르바이트", 18, true);
            Row("<b>지원자</b>", null, $"🔄 새로 받기 {Rich.Money(c.rerollCost)}", G.money >= c.rerollCost,
                () => { if (S.Spend(c.rerollCost)) Albas.RerollCandidates(); });
            Text($"낮/밤이 바뀔 때 지원자가 새로 옵니다. 낮/밤이 바뀔 때마다 시급 × {c.hoursPerPhase}시간을 지급합니다.", 12, false);
            for (int i = 0; i < G.candidates.Count; i++)
            {
                var cd = G.candidates[i];
                int idx = i;
                Row($"<b>{cd.name}</b>  💪{cd.str} 🏃{cd.agi} 🧠{cd.intel}\n시급 {Rich.Money(cd.wage)} {Rich.Small("12시간 " + Rich.Money(Albas.ShiftPay(cd.wage)))}",
                    null, "고용", G.albas.Count < c.max, () => Albas.Hire(idx));
            }
            if (G.candidates.Count == 0) Text("지원자가 없습니다", 12, false);

            Text("공정 배치 현황", 14, true);
            Text(string.Join("   ", CONFIG.stages.Select(s => $"{s.emoji}{s.name} {(S.IsAuto(s.id) ? "🤖" : $"{S.StageCount(s.id)}/{S.StageSlots(s.id)}")}")), 12, false);

            Text($"직원 ({G.albas.Count}/{c.max})", 14, true);
            if (G.albas.Count == 0) Text("고용한 알바가 없습니다. 위 지원자를 고용하세요.", 12, false);
            foreach (var a in G.albas)
            {
                var row = Instantiate(albaRowPrefab, content);
                var dd = row.Find("Bottom/Assign").GetComponent<TMP_Dropdown>();
                dd.ClearOptions();
                var opts = new System.Collections.Generic.List<string> { "🪑 대기중" };
                // shortcut: TMP 드롭다운은 항목 비활성화가 없어 가득 찬 공정도 고를 수 있다. 고르면 Assign이 "배치 슬롯이 부족합니다"로 막는다
                opts.AddRange(CONFIG.stages.Select(s => $"{s.emoji} {s.name} ({(S.IsAuto(s.id) ? "자동화" : $"{S.StageCount(s.id)}/{S.StageSlots(s.id)}")})"));
                dd.AddOptions(opts);
                dd.SetValueWithoutNotify(a.stage == null ? 0 : CONFIG.stages.FindIndex(s => s.id == a.stage) + 1);
                var alba = a;
                dd.onValueChanged.AddListener(v => { Albas.Assign(alba, v == 0 ? null : CONFIG.stages[v - 1].id); Render(true); });
                row.Find("Bottom/Fire").GetComponent<Button>().onClick.AddListener(() => { Albas.Fire(alba); Render(true); });
                albaRows.Add((a, row, row.Find("Info").GetComponent<TMP_Text>(), row.Find("Bottom/Status").GetComponent<TMP_Text>()));
            }
            Text("간식을 직원 카드나 맵 위 알바에게 드래그하면 MT가 회복됩니다.", 12, false);
        }

        static string AlbaInfo(Alba a) =>
            $"<b><size=14>{a.name}</size></b>  💪{a.str} 🏃{a.agi} 🧠{a.intel}  시급 {Rich.Money(a.wage)} {Rich.Small($"(12시간 {Rich.Money(Albas.ShiftPay(a.wage))})")}" +
            (a.unpaid ? " <b><color=#d63031>미지급</color></b>" : "") +
            $"\n{Rich.Mt(a.mt, true)}\n<color={Rich.SOFT}>숙련도 들기 {Albas.SkillLv(a.skLift)} · 나르기 {Albas.SkillLv(a.skCarry)} · 포장 {Albas.SkillLv(a.skPack)} · 검수 {Albas.SkillLv(a.skInspect)}</color> {Rich.Small($"(최대 {Albas.Capacity(a)}개)")}";

        static string Face(Alba a) => Rich.Face(a);
    }
}
