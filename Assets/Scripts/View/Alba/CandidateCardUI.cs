using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace APC
{
    public class CandidateCardUI : MonoBehaviour
    {
        [SerializeField] TMP_Text nameText;
        [SerializeField] TMP_Text wageText;
        [SerializeField] AlbaGaugeBar strGauge;
        [SerializeField] AlbaGaugeBar agiGauge;
        [SerializeField] AlbaGaugeBar intGauge;
        [SerializeField] Button hireButton;
        [SerializeField] TMP_Text hireButtonText;

        int candidateIndex = -1;
        AlbaPanelView parentPanel;

        void Awake()
        {
            if (hireButton != null) hireButton.onClick.AddListener(OnHireClicked);
        }

        public void Bind(int index, Candidate candidate, AlbaPanelView panel)
        {
            candidateIndex = index;
            parentPanel = panel;

            if (candidate == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);

            if (nameText != null) nameText.text = $"<b>{candidate.name}</b>";
            if (wageText != null) wageText.text = $"시급 {Rich.Money(candidate.wage)}";

            if (strGauge != null) strGauge.SetStat("💪근력", candidate.str, AlbaGaugeBar.StrColor);
            if (agiGauge != null) agiGauge.SetStat("🏃민첩", candidate.agi, AlbaGaugeBar.AgiColor);
            if (intGauge != null) intGauge.SetStat("🧠지능", candidate.intel, AlbaGaugeBar.IntColor);

            if (hireButton != null) hireButton.interactable = Game.G.albas.Count < Game.CONFIG.alba.max;
            if (hireButtonText != null) hireButtonText.text = "고용";
        }

        void OnHireClicked()
        {
            if (candidateIndex < 0) return;
            if (Albas.Hire(candidateIndex) != null && parentPanel != null) parentPanel.RefreshAll();
        }
    }
}
