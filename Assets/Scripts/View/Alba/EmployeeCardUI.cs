using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace APC
{
    public class EmployeeCardUI : MonoBehaviour
    {
        [SerializeField] TMP_Text nameText;
        [SerializeField] TMP_Text wageAndStatusText;
        [Tooltip("숙련도 배지 4개: 들기, 나르기, 포장, 검수")]
        [SerializeField] TMP_Text[] skillBadges;
        [SerializeField] AlbaGaugeBar mtGauge;
        [SerializeField] AlbaGaugeBar strGauge;
        [SerializeField] AlbaGaugeBar agiGauge;
        [SerializeField] AlbaGaugeBar intGauge;
        [SerializeField] Button fireButton;
        [SerializeField] Button costumeButton;

        public Alba Alba { get; private set; }
        public RectTransform RectTransform => (RectTransform)transform;

        AlbaPanelView parentPanel;

        void Awake()
        {
            if (fireButton != null) fireButton.onClick.AddListener(OnFireClicked);
            if (costumeButton != null) costumeButton.onClick.AddListener(() => { if (Alba != null) CostumePanelView.Instance?.Open(Alba, Alba.name); });
        }

        public void Bind(Alba alba, AlbaPanelView panel)
        {
            Alba = alba;
            parentPanel = panel;

            if (alba == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            UpdateDynamicInfo();
        }

        public void UpdateDynamicInfo()
        {
            if (Alba == null) return;

            string stageName = "대기";
            if (Alba.stage != null)
            {
                var s = Game.CONFIG.stages.Find(x => x.id == Alba.stage);
                if (s != null) stageName = s.name;
            }

            if (nameText != null)
            {
                string unpaidTag = Alba.unpaid ? " <color=#d63031><b>미지급</b></color>" : "";
                nameText.text = $"<b>{Alba.name}</b> {Rich.Small(stageName)}{unpaidTag}";
            }

            if (wageAndStatusText != null)
                wageAndStatusText.text = $"시급 {Rich.Money(Alba.wage)} · 최대 {Albas.Capacity(Alba)}개 · {Rich.Face(Alba)} {Alba.status}";

            if (skillBadges != null && skillBadges.Length >= 4)
            {
                skillBadges[0].text = $"들기 <b>Lv{Albas.SkillLv(Alba.skLift)}</b>";
                skillBadges[1].text = $"나르기 <b>Lv{Albas.SkillLv(Alba.skCarry)}</b>";
                skillBadges[2].text = $"포장 <b>Lv{Albas.SkillLv(Alba.skPack)}</b>";
                skillBadges[3].text = $"검수 <b>Lv{Albas.SkillLv(Alba.skInspect)}</b>";
            }

            if (mtGauge != null) mtGauge.SetMt(Alba.mt);
            if (strGauge != null) strGauge.SetStat("💪근력", Alba.str, AlbaGaugeBar.StrColor);
            if (agiGauge != null) agiGauge.SetStat("🏃민첩", Alba.agi, AlbaGaugeBar.AgiColor);
            if (intGauge != null) intGauge.SetStat("🧠지능", Alba.intel, AlbaGaugeBar.IntColor);
        }

        void OnFireClicked()
        {
            if (Alba == null) return;
            Albas.Fire(Alba);
            if (parentPanel != null) parentPanel.RefreshAll();
        }
    }
}
