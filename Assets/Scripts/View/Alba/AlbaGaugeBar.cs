using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace APC
{
    public class AlbaGaugeBar : MonoBehaviour
    {
        [SerializeField] Slider slider;
        [SerializeField] Image fillImage;
        [SerializeField] TMP_Text valueText;
        [Tooltip("칸 게이지: 첫 자식 Image를 statMax개까지 복제해 값만큼 칠한다")]
        [SerializeField] RectTransform segments;

        static readonly Color EmptyCell = new Color(0.86f, 0.88f, 0.91f);

        // ratio: 0~1
        public void SetRatio(float ratio, string text, Color color)
        {
            ratio = Mathf.Clamp01(ratio);
            if (slider != null)
            {
                slider.minValue = 0;
                slider.maxValue = 1;
                slider.value = ratio;
            }
            else if (fillImage != null)
            {
                // 스프라이트 없는 Image는 Filled 타입이 무시되므로 앵커로 채운다
                var rt = fillImage.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = new Vector2(ratio, 1);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }

            if (fillImage != null) fillImage.color = color;
            if (valueText != null) valueText.text = text;
        }

        public void SetValue(float current, float max, string text, Color color) =>
            SetRatio(max > 0 ? current / max : 0, text, color);

        public static readonly Color StrColor = new Color(0.92f, 0.45f, 0.28f), AgiColor = new Color(0.2f, 0.75f, 0.45f), IntColor = new Color(0.25f, 0.55f, 0.9f);

        // 능력치: 칸 게이지면 statMax칸 중 v칸, 아니면 statMax 대비 비율
        public void SetStat(string label, int v, Color color)
        {
            int max = Game.CONFIG.alba.statMax;
            if (segments == null || segments.childCount == 0)
            {
                SetValue(v, max, $"{label} {v}", color);
                return;
            }
            if (valueText != null) valueText.text = label;
            var first = segments.GetChild(0);
            while (segments.childCount < max) Instantiate(first, segments).name = $"Seg_{segments.childCount - 1}";
            for (int i = 0; i < segments.childCount; i++)
            {
                var cell = segments.GetChild(i);
                cell.gameObject.SetActive(i < max);
                cell.GetComponent<Image>().color = i < v ? color : EmptyCell;
            }
        }

        // MT: 설정 mt.min~max 범위, Rich.Mt와 같은 구간 색
        public void SetMt(double mt)
        {
            ColorUtility.TryParseHtmlString(Rich.MtBar[Rich.MtLevel(mt)], out var c);
            SetRatio((float)Rich.MtRatio(mt), $"MT {U.Round(mt)}", c);
        }
    }
}
