// 알바 캐릭터 생성·삭제 (원본 World.addAlba / removeAlba). 고용 수만큼 프리팹을 만든다
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static APC.Game;

namespace APC
{
    public class AlbaView : MonoBehaviour
    {
        [SerializeField] CharacterView albaPrefab;

        readonly Dictionary<int, CharacterView> views = new Dictionary<int, CharacterView>();

        void LateUpdate()
        {
            if (G == null) return;
            foreach (var id in views.Keys.Where(id => !G.albas.Any(a => a.id == id)).ToList())
            {
                Destroy(views[id].gameObject);
                views.Remove(id);
            }
            foreach (var a in G.albas)
            {
                if (views.ContainsKey(a.id)) continue;
                var v = Instantiate(albaPrefab, CoordUtil.ToWorld(a.x, a.z), Quaternion.identity, transform);
                v.name = $"Alba_{a.id}_{a.name}";
                v.actor = a;
                views[a.id] = v;
            }
        }
    }
}
