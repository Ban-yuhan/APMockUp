using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace APC
{
    // 드레스룸 목록 한 칸: 아이콘 · 이름 · [적용]/[착용 중]
    public class CostumeItemUI : MonoBehaviour
    {
        [SerializeField] Image icon;
        [SerializeField] TMP_Text placeholder;
        [SerializeField] TMP_Text nameText;
        [SerializeField] Image background;
        [SerializeField] Button button;
        [SerializeField] TMP_Text buttonText;

        Action onClick;

        void Awake() => button.onClick.AddListener(() => onClick?.Invoke());

        public void Bind(CostumeData d, bool worn, Action click)
        {
            onClick = click;
            if (d == null) { gameObject.SetActive(false); return; }
            gameObject.SetActive(true);
            icon.sprite = d.icon;
            icon.enabled = d.icon;
            placeholder.gameObject.SetActive(!d.icon);
            nameText.text = d.displayName;
            button.interactable = !worn;
            buttonText.text = worn ? "착용 중" : "적용";
            background.color = worn ? new Color(0.86f, 0.97f, 0.89f) : new Color(0.97f, 0.98f, 0.99f);
        }
    }
}
