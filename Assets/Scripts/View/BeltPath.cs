// 컨베이어 벨트 라인 1개 (원본 LAYOUT.belts[공정].lines의 한 줄). 점 = 이름이 P0, P1, ... 인 자식 위치 (순서 = 물품이 가는 방향)
// 끝점이 시설 포트에 붙어 있으면 그 포트 위치로, 다른 라인에 붙어 있으면 그 라인 위로 맞춰진다 (Belts.LinePts)
// 벨트 모양(Mesh 자식)은 에디터 메뉴로 만들고, 그 공정이 자동화되면 켜진다
using System.Linq;
using UnityEngine;

namespace APC
{
    public class BeltPath : MonoBehaviour
    {
        [Tooltip("sort | transfer | outbound")]
        public string stage;
        [Tooltip("같은 공정 안의 라인 번호")]
        public int id;

        [Header("시작점: 시설 포트(fac = staging|bin|table|dock, port = beltIn|beltOut) 또는 다른 라인(line ≥ 0)")]
        public string fromFac;
        public int fromIndex;
        public string fromPort;
        public int fromLine = -1;

        [Header("끝점")]
        public string toFac;
        public int toIndex;
        public string toPort;
        public int toLine = -1;

        public BeltLine ToLine() => new BeltLine
        {
            id = id,
            points = transform.Cast<Transform>().Where(c => c.name.StartsWith("P")).OrderBy(c => int.Parse(c.name.Substring(1)))
                .Select(c => CoordUtil.ToLayout(c.position)).ToList(),
            from = Attach(fromFac, fromIndex, fromPort, fromLine),
            to = Attach(toFac, toIndex, toPort, toLine),
        };

        static Attach Attach(string fac, int i, string port, int line) =>
            line >= 0 ? new Attach { line = line } : string.IsNullOrEmpty(fac) ? null : new Attach { fac = fac, i = i, port = port };
    }
}
