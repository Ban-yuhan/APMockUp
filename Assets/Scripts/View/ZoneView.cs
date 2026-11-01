// 상호작용 범위 표시 (원본 World.syncZones) + 클릭 이동 목적지 표시
// 범위 개수는 해금·트럭 도착에 따라 바뀌므로 링 프리팹을 Instantiate 한다
using System.Collections.Generic;
using UnityEngine;

namespace APC
{
    public class ZoneView : MonoBehaviour
    {
        [SerializeField] ZoneRing ringPrefab;
        [SerializeField] ZoneRing moveMarker;

        readonly Dictionary<string, ZoneRing> rings = new Dictionary<string, ZoneRing>();
        readonly HashSet<string> seen = new HashSet<string>();
        float clock;

        void LateUpdate()
        {
            var G = Game.G;
            if (G?.player == null) return;
            clock += GameRoot.ViewDt;
            float R = (float)Game.CONFIG.interactRadius;
            seen.Clear();
            foreach (var z in Zones.List())
            {
                seen.Add(z.key);
                if (!rings.TryGetValue(z.key, out var r))
                {
                    r = Instantiate(ringPrefab, transform);
                    r.name = "Zone " + z.key;
                    r.Build(R, R - 0.12f);
                    r.SetColor(ZoneColor(z));
                    rings[z.key] = r;
                }
                r.gameObject.SetActive(true);
                r.transform.position = CoordUtil.ToWorld(z.Pos, 0.03f);
                bool on = z.key == G.player.zoneKey;
                r.SetAlpha(on ? 0.35f : 0.16f, on ? 1 : 0.7f);
                r.lineMesh.transform.localScale = Vector3.one * (on ? 1 + Mathf.Sin(clock * 6) * 0.04f : 1);
            }
            foreach (var kv in rings) if (!seen.Contains(kv.Key)) kv.Value.gameObject.SetActive(false);

            var tg = G.player.target;
            moveMarker.gameObject.SetActive(tg.HasValue);
            if (tg.HasValue) moveMarker.transform.position = CoordUtil.ToWorld(tg.Value, 0.03f);
        }

        static Color ZoneColor(Zone z)
        {
            var C = Game.CONFIG;
            switch (z.kind)
            {
                case "truck": return U.Hex(0xf39c12);
                case "staging": return U.Hex(0xd4a017);
                case "bin": return C.Type(C.items[((Bin)z.refObj).item].type).color;
                case "table": return U.Hex(0x2e9e57);
                case "dock": return C.destinations[((Dock)z.refObj).idx].color;
                default: return U.Hex(0x546e7a);
            }
        }
    }
}
