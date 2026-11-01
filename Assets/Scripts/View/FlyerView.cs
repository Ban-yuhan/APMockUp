// 컨베이어·기계손으로 이동 중인 물품 (원본 World.syncFlyers). 개수가 계속 바뀌어서 프리팹으로 만들고 지운다
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static APC.Game;

namespace APC
{
    public class FlyerView : MonoBehaviour
    {
        [Tooltip("1×1×1 큐브. 색·크기는 여기서 정한다")]
        [SerializeField] Renderer flyerPrefab;

        readonly Dictionary<Flyer, Renderer> views = new Dictionary<Flyer, Renderer>();

        void LateUpdate()
        {
            if (G == null) return;
            foreach (var f in views.Keys.Where(f => !G.flyers.Contains(f)).ToList())
            {
                Destroy(views[f].gameObject);
                views.Remove(f);
            }
            foreach (var f in G.flyers)
            {
                if (!views.TryGetValue(f, out var r))
                {
                    r = Instantiate(flyerPrefab, transform);
                    var col = f.kind == HandKind.Box ? U.Hex(Palette.BOX) : f.kind == HandKind.Broken ? U.Hex(Palette.BROKEN) : Palette.TypeColor(f.item);
                    r.transform.localScale = Vector3.one * (f.kind == HandKind.Box ? 0.45f : 0.3f);
                    Palette.Tint(r, col);
                    views[f] = r;
                }
                // 파손품과 트럭에 싣는 박스(직선 2점 경로)는 높이 들어 옮긴다
                float y = f.kind == HandKind.Broken || f.kind == HandKind.Box && f.path.Count == 2 ? 1.0f : 0.3f;
                r.transform.position = CoordUtil.ToWorld(f.x, f.z, y);
            }
        }
    }
}
