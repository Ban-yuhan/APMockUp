// 씬에 놓인 시설/지점. MapRoot가 이 컴포넌트들을 모아 배치 데이터(LayoutData)를 만든다
// 상호작용·벨트 지점은 이름이 In / Out / Inspect / BeltIn / BeltOut 인 자식 오브젝트 위치를 쓴다 (없으면 시설 위치)
// 도크의 트럭 주차 위치는 TruckSpot 자식. 같은 종류 안에서의 순서 = Hierarchy 순서
using UnityEngine;

namespace APC
{
    public enum PointKind { InTruck, Staging, Bin, Table, Dock, Trash, Rest, Waiting, Purifier, Entrance, PlayerStart, Idle }

    public class LayoutPoint : MonoBehaviour
    {
        public PointKind kind;
        [Tooltip("보관함: 품목 id / 도크: 목적지 id / 대기 지점: 공정 id")]
        public string id;
        [Tooltip("해금 비용 (0 = 시작부터 해금). 보관함·포장대·도크")]
        public int cost;
        [Tooltip("휴게실·대기구역 크기 (칸, x = 가로, y = 세로)")]
        public Vector2 size;
    }
}
