// 디버그 패널 (원본 js/debug.js). 메뉴의 🛠️ 디버그 버튼 또는 ` 키
// 버튼·입력칸은 UiBuilder가 씬에 만들고, 각자 DebugAction(act, arg)을 단다 (원본 data-dbg / data-arg / data-af)
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static APC.Game;

namespace APC
{
    public class DebugView : MonoBehaviour
    {
        [SerializeField] GameObject root;
        [SerializeField] TMP_Text state, albaEffects, albaEmpty;
        [SerializeField] TMP_InputField moneyInput;
        [Tooltip("알바 스탯 편집 묶음 (알바가 없으면 숨김)")]
        [SerializeField] GameObject albaBox;
        [SerializeField] TMP_Dropdown albaSelect;
        [SerializeField] Button unpaidBtn;
        [SerializeField] PanelView panel;

        int albaId = -1;
        string albaSig;
        float timer;
        TMP_InputField[] fields;

        Alba Selected => G.albas.Find(a => a.id == albaId);

        void Awake()
        {
            foreach (var da in root.GetComponentsInChildren<DebugAction>(true))
            {
                var d = da;
                var input = d.GetComponent<TMP_InputField>();
                if (input && d.act == "af")
                    input.onEndEdit.AddListener(v => { var a = Selected; if (a == null) return; DebugCmd.SetAlbaField(a, d.arg, v); RenderAlba(); panel.Refresh(); });
                else if (d.GetComponent<Button>() is Button b)
                    b.onClick.AddListener(() => Run(d.act, d.arg));
            }
            fields = root.GetComponentsInChildren<DebugAction>(true).Where(d => d.act == "af").Select(d => d.GetComponent<TMP_InputField>()).Where(f => f).ToArray();
            albaSelect.onValueChanged.AddListener(i => { if (i < G.albas.Count) albaId = G.albas[i].id; RenderAlba(); });
            moneyInput.text = CONFIG.debug.moneyStep.ToString();
            root.SetActive(false);
        }

        public void Toggle()
        {
            root.SetActive(!root.activeSelf);
            if (root.activeSelf) { timer = 0; RenderAlba(); }
        }

        void Run(string act, string arg)
        {
            if (act == "close") { Toggle(); return; }
            if (act == "unpaid") { var a = Selected; if (a != null) DebugCmd.SetAlbaField(a, "unpaid", (!a.unpaid).ToString().ToLower()); }
            else DebugCmd.Run(act, arg, Selected, int.TryParse(moneyInput.text, out int m) ? m : 0);
            RenderAlba();
            timer = 0;
            panel.Refresh();
        }

        void Update()
        {
            if (G == null) return;
            var k = Keyboard.current;
            if (k != null && k.backquoteKey.wasPressedThisFrame && Loading.dock == null) Toggle();
            if (!root.activeSelf) return;
            timer -= Time.unscaledDeltaTime;
            if (timer > 0) return;
            timer = 0.3f;
            state.text = $"{Rich.Money(G.money)} · {G.day}일차 {(G.phase == "day" ? "낮" : "밤")} {HudView.ClockText()}\n" +
                         $"이벤트: {(G.eventName != null ? "🎉 " + CONFIG.evt.name : "없음")} · 주문서 {G.orders.Count}개";
            // 고용/해고하면 목록을 다시 그리고, 아니면 바뀌는 값(MT, 능력치)만 갱신
            if (Sig() != albaSig) { RenderAlba(); return; }
            var a = Selected;
            if (a == null) return;
            var mt = fields.FirstOrDefault(f => f.GetComponent<DebugAction>().arg == "mt");
            if (mt && !mt.isFocused) mt.SetTextWithoutNotify(U.Round(a.mt).ToString());
            albaEffects.text = Effects(a);
        }

        string Sig() => string.Join("|", G.albas.Select(a => a.id + a.name));

        // ---------- 알바 스탯 편집 ----------
        void RenderAlba()
        {
            albaSig = Sig();
            albaBox.SetActive(G.albas.Count > 0);
            albaEmpty.gameObject.SetActive(G.albas.Count == 0);
            if (G.albas.Count == 0) return;
            if (Selected == null) albaId = G.albas[0].id;
            var a = Selected;
            string Stage(string id) { var s = CONFIG.stages.Find(x => x.id == id); return s != null ? s.emoji + s.name : "대기"; }
            albaSelect.ClearOptions();
            albaSelect.AddOptions(G.albas.Select(x => $"{x.name} ({Stage(x.stage)})").ToList());
            albaSelect.SetValueWithoutNotify(G.albas.IndexOf(a));
            foreach (var f in fields)
            {
                if (f.isFocused) continue;
                string key = f.GetComponent<DebugAction>().arg;
                double v = key == "str" ? a.str : key == "agi" ? a.agi : key == "int" ? a.intel : key == "mt" ? U.Round(a.mt) : key == "wage" ? a.wage
                    : Albas.SkillLv(key == "sk-lift" ? a.skLift : key == "sk-carry" ? a.skCarry : key == "sk-pack" ? a.skPack : a.skInspect);
                f.SetTextWithoutNotify(v.ToString());
            }
            unpaidBtn.GetComponentInChildren<TMP_Text>().text = $"미지급: {(a.unpaid ? "✔ 예" : "아니오")}";
            albaEffects.text = Effects(a);
        }

        static string Effects(Alba a) =>
            $"최대 픽업 <b>{Albas.Capacity(a)}</b>개 {Rich.Small("(상한 8)")} · 이동 <b>{Albas.Speed(a):F1}</b>칸/초\n" +
            $"작업속도 포장 <b>x{Albas.WorkMult(a, "pack"):F2}</b> · 검수 <b>x{Albas.WorkMult(a, "inspect"):F2}</b>\n" +
            $"상차 목표 적재율 <b>{U.Round(Albas.LoadTarget(a) * CONFIG.loading.fullRate)}%</b> · MT 배율 x{S.MtMult(a.mt)}";
    }
}
