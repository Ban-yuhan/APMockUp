// 캐릭터 표시 (원본 World.syncChar): 위치·방향·걷기/들기 애니메이션
// 프리팹 구조: 루트(이 컴포넌트) / Model(모델 + Animator) / HandAnchor(손에 든 물건)
using UnityEngine;

namespace APC
{
    public class CharacterView : MonoBehaviour
    {
        [Tooltip("켜면 플레이어(G.player)를 따라간다. 알바는 생성할 때 actor를 넣는다")]
        public bool isPlayer;
        public Actor actor;
        [SerializeField] Animator animator;
        public Transform handAnchor;

        static readonly int Moving = Animator.StringToHash("Moving"), Carrying = Animator.StringToHash("Carrying");

        void LateUpdate()
        {
            if (isPlayer) actor = Game.G?.player;
            if (actor == null) return;
            transform.position = CoordUtil.ToWorld(actor.x, actor.z);
            transform.rotation = Quaternion.Euler(0, CoordUtil.FacingToYaw(actor.facing), 0);
            SyncCostume();
            if (handAnchor) SyncHand();
            if (!animator) return;
            if (actor is Alba al) SyncRest(al.resting);
            // 원본 걷기 흔들림은 멈춤이면 정지, 배속이면 빨라진다
            animator.speed = GameRoot.Running ? Game.G.speed : 0;
            animator.SetBool(Moving, actor.moving);
            animator.SetBool(Carrying, actor.hand.Count > 0);
        }

        // 휴식 중인 알바는 누워서 회색 (원본: 몸통 z축 π/2.4 기울이고 높이 0.4, 색 b0bec5)
        Vector3? modelPos;
        bool restShown;

        void SyncRest(bool resting)
        {
            if (resting == restShown) return;
            restShown = resting;
            var m = animator.transform;
            modelPos ??= m.localPosition;
            var p0 = modelPos.Value;
            if (resting)
            {
                var rot = Quaternion.Euler(0, 0, 180f / 2.4f);
                // 모델 원점 기준 엉덩이 높이(발에서 0.7)를 바닥 위 0.4에 둔다
                var hip = new Vector3(0, -p0.y + 0.7f, 0);
                m.localRotation = rot;
                m.localPosition = new Vector3(0, 0.4f, 0) - rot * hip;
            }
            else { m.localRotation = Quaternion.identity; m.localPosition = p0; }
            foreach (var r in m.GetComponentsInChildren<Renderer>())
                if (resting) Palette.Tint(r, U.Hex(0xb0bec5)); else r.SetPropertyBlock(null);
        }

        // 코스튬: 처음엔 바로 입히고, 바뀌면 "펑" 연기 뒤 연기가 가장 클 때 모델을 바꾼다
        string costume;
        float swapAt = -1;

        void SyncCostume()
        {
            if (actor.costumeId == costume) { swapAt = -1; return; }
            if (costume == null) { ApplyCostume(); return; }
            if (swapAt < 0)
            {
                Poof.Play(transform.position + Vector3.up * 0.7f);
                swapAt = Time.unscaledTime + Poof.SwapDelay;
            }
            else if (Time.unscaledTime >= swapAt) ApplyCostume();
        }

        void ApplyCostume()
        {
            costume = actor.costumeId;
            swapAt = -1;
            var d = CostumeCatalog.Find(costume);
            if (d == null || !d.walkModelPrefab) return; // 목록에 없으면 프리팹 모델 그대로
            if (animator) Destroy(animator.gameObject);
            animator = CostumeCatalog.Spawn(d, transform);
            // 새 모델에 휴식 자세를 다시 적용
            modelPos = null;
            restShown = false;
        }

        // 손에 든 물건 (원본 setStack): HandAnchor 아래 슬롯을 아래부터 쌓는다
        void SyncHand()
        {
            var hand = actor.hand;
            for (int i = 0; i < handAnchor.childCount; i++)
            {
                var slot = handAnchor.GetChild(i);
                bool on = i < hand.Count;
                slot.gameObject.SetActive(on);
                if (!on) continue;
                float s = hand[i].kind == HandKind.Box ? 0.42f : 0.3f;
                slot.localScale = new Vector3(s, s * 0.85f, s);
                slot.localPosition = new Vector3(0, i * (s * 0.85f + 0.03f) + 0.15f, 0);
                Palette.Tint(slot.GetComponent<Renderer>(), Palette.EntryColor(hand[i]));
            }
        }
    }
}
