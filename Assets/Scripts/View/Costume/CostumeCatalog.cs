// 코스튬 목록. Assets/Resources/CostumeCatalog.asset 의 리스트에 항목을 더하면 드레스룸에 바로 나온다
// 걷기/들기 모델은 뼈대가 같아서, 걷기 모델 하나에 두 모션을 담은 Animator Controller로 바꿔 튼다
// (controller는 비워 두면 메뉴 APC/코스튬 만들기 가 걷기·들기 FBX로 만들어 채운다)
using System.Collections.Generic;
using UnityEngine;

namespace APC
{
    [System.Serializable]
    public class CostumeData
    {
        public string costumeId;            // "basic", "rabbit", "macaron"
        public string displayName;          // 목록·프리뷰에 보일 이름
        public Sprite icon;                 // 비우면 👕

        [Header("3D Model")]
        public GameObject walkModelPrefab;  // 화면에 놓는 모델 (걷기 FBX)
        public GameObject carryModelPrefab; // 들기 모션을 가져올 FBX
        public RuntimeAnimatorController controller; // Idle/Walk/CarryIdle/CarryWalk

        [Header("Offset")]
        public float footOffsetY;           // 발 높이 보정 (Rabbit 3.32)
    }

    [CreateAssetMenu(menuName = "APC/Costume Catalog", fileName = "CostumeCatalog")]
    public class CostumeCatalog : ScriptableObject
    {
        public List<CostumeData> costumes = new List<CostumeData>();

        static CostumeCatalog instance;
        public static CostumeCatalog Instance => instance ? instance : instance = Resources.Load<CostumeCatalog>("CostumeCatalog");

        public static CostumeData Find(string id) => Instance ? Instance.costumes.Find(c => c.costumeId == id) : null;

        // 걷기 모델을 parent 아래에 놓고 발 높이·애니메이터를 맞춘다
        public static Animator Spawn(CostumeData d, Transform parent)
        {
            var m = Instantiate(d.walkModelPrefab, parent);
            m.name = "Model";
            m.transform.localPosition = new Vector3(0, d.footOffsetY, 0);
            m.transform.localRotation = Quaternion.identity;
            var anim = m.GetComponent<Animator>();
            if (!anim) anim = m.AddComponent<Animator>();
            anim.runtimeAnimatorController = d.controller;
            anim.applyRootMotion = false;
            return anim;
        }
    }
}
