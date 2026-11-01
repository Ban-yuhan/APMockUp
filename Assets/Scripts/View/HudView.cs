// 화면 고정 HUD (원본 ui.js buildHud/refreshHud/renderOrders/toast)
// 좌상단 시계·돈·속도, 상단 주문서, 하단 메뉴·도움말, 우하단 토스트. 오브젝트는 UiBuilder가 씬에 만든다
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static APC.Game;

namespace APC
{
    public class HudView : MonoBehaviour
    {
        [SerializeField] TMP_Text clockDay, clockTime, money, stats, help;
        [SerializeField] Image phaseFill, helpBg;
        [Tooltip("⏸, x1, x2, x4 순서")]
        [SerializeField] Button[] speedBtns;
        [SerializeField] Button facilityBtn, upgradeBtn, albaBtn;
        [SerializeField] PanelView panel;
        [SerializeField] RectTransform orderStrip, toasts;
        [Header("프리팹")]
        [SerializeField] Image orderCardPrefab, toastPrefab;

        static readonly int[] SPEEDS = { 0, 1, 2, 4 };
        const float CARD_W = 132, GAP = 6;

        class ToastItem { public CanvasGroup cg; public float t; }
        readonly List<ToastItem> toastList = new List<ToastItem>();
        readonly List<Image> cards = new List<Image>();
        float hudTimer;

        void Awake()
        {
            for (int i = 0; i < speedBtns.Length; i++)
            {
                int sp = SPEEDS[i];
                speedBtns[i].onClick.AddListener(() =>
                {
                    if (sp == 0) G.userPaused = !G.userPaused;
                    else { G.userPaused = false; G.speed = sp; }
                    Refresh();
                });
            }
            facilityBtn.onClick.AddListener(() => panel.Toggle("facility"));
            upgradeBtn.onClick.AddListener(() => panel.Toggle("upgrade"));
            albaBtn.onClick.AddListener(() => panel.Toggle("alba"));
            // shortcut: 원본 도움말의 "` 디버그"는 9단계(디버그 패널)에서 넣는다
            help.text = "WASD·클릭 이동 · 바닥 원(상호작용 범위) 안에서 대기하면 자동 작업 · <b>E</b> 상차 미니게임 · 휠 확대 · 간식을 알바에게 드래그 · ESC 패널 닫기";
        }

        // GameRoot.Awake(-100)에서 Game.Setup이 Toast를 비우지 않으므로 OnEnable에서 구독해도 된다
        void OnEnable() => Game.Toast += ShowToast;
        void OnDisable() => Game.Toast -= ShowToast;

        void Update()
        {
            if (G == null) return;
            hudTimer -= Time.unscaledDeltaTime;
            if (hudTimer <= 0) { hudTimer = 0.2f; Refresh(); }
            UpdateToasts(Time.unscaledDeltaTime);
        }

        // ---------- 시계·돈 ----------
        public static string ClockText()
        {
            double hours = CONFIG.dayStartHour + (G.phase == "night" ? 12 : 0) + G.phaseTime / CONFIG.phaseSeconds * 12;
            int h = (int)System.Math.Floor(hours) % 24, m = (int)System.Math.Floor(hours % 1 * 60);
            return $"{h:00}:{m:00}";
        }

        void Refresh()
        {
            bool day = G.phase == "day";
            clockDay.text = $"{(day ? "☀️" : "🌙")} <b>{G.day}일차 {(day ? "낮" : "밤")}</b>";
            clockTime.text = $"<b>{ClockText()}</b>";
            phaseFill.rectTransform.anchorMax = new Vector2((float)(G.phaseTime / CONFIG.phaseSeconds), 1);
            phaseFill.color = U.Hex(day ? 0xe6a100 : 0x5c6bc0);
            money.text = Rich.Money(G.money);
            stats.text = $"🚛 출고 {G.trucks}대 · 📦 {G.shipped}개 · 💔 {G.brokenCount}";
            for (int i = 0; i < speedBtns.Length; i++)
            {
                int sp = SPEEDS[i];
                bool on = sp == 0 ? G.userPaused : !G.userPaused && G.speed == sp;
                speedBtns[i].image.color = on ? U.Hex(0x1e5aa8) : Color.white;
                speedBtns[i].GetComponentInChildren<TMP_Text>().color = on ? Color.white : U.Hex(0x1f2933);
            }
            albaBtn.GetComponentInChildren<TMP_Text>().text = $"👷 알바 <size=12><color={Rich.SOFT}>{G.albas.Count}/{CONFIG.alba.max}</color></size>";
            help.color = day ? U.Hex(0x5b6775) : U.Hex(0xcfd8dc);
            helpBg.color = day ? new Color(1, 1, 1, .75f) : new Color(0, 0, 0, .4f);
            RenderOrders();
        }

        // ---------- 주문서 ----------
        void RenderOrders()
        {
            int max = Mathf.Max(1, Mathf.FloorToInt((orderStrip.rect.width + GAP) / (CARD_W + GAP)));
            var list = G.orders;
            int showN = list.Count > max ? max - 1 : list.Count;
            int k = 0;
            for (int i = 0; i < showN; i++)
            {
                var o = list[i];
                var t = o.table >= 0 ? G.tables[o.table] : null;
                var dest = CONFIG.destinations[o.dest];
                string items = string.Join(" ", o.items.Select(kv =>
                {
                    var it = CONFIG.items[kv.Key];
                    int got = t != null && t.delivered.TryGetValue(kv.Key, out int g) ? g : 0;
                    string s = $"{it.emoji}{(t != null ? got + "/" : "x")}{kv.Value}";
                    return t != null && got >= kv.Value ? $"<alpha=#73>{s}<alpha=#FF>" : s;
                }));
                string state = t != null ? (S.TableReady(t) ? "🎁 포장 대기" : $"포장대 {t.idx + 1}") : "대기";
                Card(k++, CARD_W, t != null ? U.Hex(0xfffbe6) : new Color(1, 1, 1, .94f), dest.color,
                    $"#{o.id} → <b>{dest.name}</b>\n<size=13>{items}</size>\n{Rich.Small($"{Rich.Money(o.reward)} · {state}")}", TextAlignmentOptions.TopLeft);
            }
            if (list.Count > showN)
                Card(k++, 90, U.Hex(0xfff3e0), U.Hex(0xff9800), $"<b>📋\n주문서 x{list.Count - showN}</b>", TextAlignmentOptions.Center);
            if (list.Count == 0)
                Card(k++, 100, new Color(1, 1, 1, .94f), Color.clear, Rich.Small("주문서 없음", 12), TextAlignmentOptions.Left);
            for (int i = k; i < cards.Count; i++) cards[i].gameObject.SetActive(false);
        }

        void Card(int i, float width, Color bg, Color top, string text, TextAlignmentOptions align)
        {
            if (i >= cards.Count) cards.Add(Instantiate(orderCardPrefab, orderStrip));
            var c = cards[i];
            c.gameObject.SetActive(true);
            c.color = bg;
            c.GetComponent<LayoutElement>().preferredWidth = width;
            c.transform.Find("Top").GetComponent<Image>().color = top;
            var tx = c.GetComponentInChildren<TMP_Text>();
            tx.text = text;
            tx.alignment = align;
        }

        // ---------- 토스트 ----------
        void ShowToast(string icon, string text)
        {
            var t = Instantiate(toastPrefab, toasts);
            t.transform.SetAsLastSibling();
            t.GetComponentInChildren<TMP_Text>().text = string.IsNullOrEmpty(icon) ? text : $"{icon} {text}";
            toastList.Add(new ToastItem { cg = t.GetComponent<CanvasGroup>() });
            while (toastList.Count > 5) { Destroy(toastList[0].cg.gameObject); toastList.RemoveAt(0); }
        }

        // 3.6초 뒤 0.5초 동안 사라지고 4.2초에 지운다
        void UpdateToasts(float dt)
        {
            for (int i = toastList.Count - 1; i >= 0; i--)
            {
                var it = toastList[i];
                it.t += dt;
                it.cg.alpha = 1 - Mathf.Clamp01((it.t - 3.6f) / 0.5f);
                if (it.t >= 4.2f) { Destroy(it.cg.gameObject); toastList.RemoveAt(i); }
            }
        }
    }
}
