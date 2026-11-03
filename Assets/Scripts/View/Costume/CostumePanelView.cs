// 드레스룸 팝업 (플레이어·알바 공통): 위 3D 프리뷰 + 아래 코스튬 목록
// 프리뷰는 맵에서 멀리 떨어진 PreviewStage를 전용 카메라가 RenderTexture로 찍는다
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static APC.Game;

namespace APC
{
    public class CostumePanelView : MonoBehaviour
    {
        public static CostumePanelView Instance { get; private set; }

        [SerializeField] GameObject root;
        [SerializeField] TMP_Text titleText, currentText;
        [SerializeField] Button closeBtn;
        [Tooltip("HUD 좌상단 플레이어 코스튬 버튼")]
        [SerializeField] Button playerButton;
        [SerializeField] Camera previewCam;
        [SerializeField] Transform previewStage;
        [Tooltip("첫 칸은 템플릿. 코스튬이 많으면 복제해 늘린다")]
        [SerializeField] CostumeItemUI[] items;

        List<CostumeItemUI> pool;
        Actor target;
        GameObject model;

        void Awake()
        {
            Instance = this;
            pool = new List<CostumeItemUI>(items);
            closeBtn.onClick.AddListener(Close);
            if (playerButton) playerButton.onClick.AddListener(() => { if (G != null) Open(G.player, "플레이어"); });
            Close();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Open(Actor targetActor, string targetName)
        {
            if (targetActor == null) return;
            target = targetActor;
            titleText.text = $"👕 <b>드레스룸</b> <size=80%><color=#5b6775>{targetName}</color></size>";
            root.SetActive(true);
            previewCam.enabled = true;
            ShowModel(target.costumeId);
            Refresh();
        }

        public void Close()
        {
            target = null;
            root.SetActive(false);
            previewCam.enabled = false;
            if (model) Destroy(model);
        }

        void Update()
        {
            // 보던 알바가 해고되거나 도망가면 닫는다
            if (target is Alba a && (G == null || !G.albas.Contains(a))) Close();
        }

        void Select(CostumeData d)
        {
            if (target == null || target.costumeId == d.costumeId) return;
            // 맵 위 캐릭터는 CharacterView가 바뀐 ID를 보고 같은 연기로 바꾼다
            target.costumeId = d.costumeId;
            Poof.Play(previewStage.position + Vector3.up * 0.7f, 1, true);
            DOVirtual.DelayedCall(Poof.SwapDelay, () => { if (target != null) ShowModel(target.costumeId); }, true).SetLink(gameObject);
            Refresh();
        }

        void ShowModel(string id)
        {
            if (model) Destroy(model);
            var d = CostumeCatalog.Find(id);
            if (d == null || !d.walkModelPrefab) return;
            var anim = CostumeCatalog.Spawn(d, previewStage);
            anim.updateMode = AnimatorUpdateMode.UnscaledTime; // 게임이 멈춰도 프리뷰는 대기 모션
            model = anim.gameObject;
        }

        void Refresh()
        {
            var list = CostumeCatalog.Instance ? CostumeCatalog.Instance.costumes : new List<CostumeData>();
            var cur = CostumeCatalog.Find(target.costumeId);
            currentText.text = $"현재: <b>{(cur != null ? cur.displayName : target.costumeId)}</b>";
            AlbaPanelView.Grow(pool, list.Count);
            for (int i = 0; i < pool.Count; i++)
            {
                var d = i < list.Count ? list[i] : null;
                pool[i].Bind(d, d != null && d.costumeId == target.costumeId, () => Select(d));
            }
        }
    }
}
