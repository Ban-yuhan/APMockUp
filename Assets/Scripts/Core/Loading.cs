// 상차 미니게임 상태 명령 (원본 loading-ui.js의 open/close/place/remove/resetAll/depart)
// 화면(LoadingView)은 드래그만 맡고, 상태를 바꾸는 일은 모두 여기로 보낸다
using System.Collections.Generic;
using static APC.Game;

namespace APC
{
    public static class Loading
    {
        // 열려 있는 도크 (null = 닫힘)
        public static Dock dock;
        public static OutTruck Truck => dock?.truck;
        // 지게차에 탄 채로 열었으면 그 지게차 (파렛트 위 이 도크 묶음·낱개도 실을 수 있다)
        public static Forklift forklift;

        // 실을 수 있는 것: 출고 대기 박스 + 파렛트 위 이 도크 묶음·낱개
        public static List<Box> Waiting()
        {
            var list = new List<Box>(dock.waiting);
            if (forklift != null)
            {
                list.AddRange(forklift.pallet.FindAll(b => b.dest == dock.idx));
                list.AddRange(forklift.loose.FindAll(b => b.dest == dock.idx));
            }
            return list;
        }

        public static bool Open(Dock d, Forklift f = null)
        {
            if (!Docks.CanOpen(d)) { Notify("", "지금은 상차 패널을 열 수 없습니다"); return false; }
            dock = d;
            forklift = f;
            G.paused = true;
            return true;
        }

        public static void Close()
        {
            if (dock == null) return;
            var tr = dock.truck;
            if (tr != null && tr.mode == "manual" && tr.placements.Count == 0) tr.mode = null;
            dock = null;
            forklift = null;
            G.paused = false;
        }

        // 뺀 박스는 원래 자리로: 묶음은 열어 둔 지게차 파렛트로, 낱개는 출고 대기 (지게차 낱개였어도 도크로 내려놓는다)
        static void Return(Box b)
        {
            if (b.parts != null && forklift != null) forklift.pallet.Add(b);
            else dock.waiting.Add(b);
        }

        // placement가 있으면 실린 박스를 옮기고, 없으면 대기 박스를 새로 싣는다
        public static void Place(Box box, List<UnityEngine.Vector2Int> cells, Placement placement)
        {
            var tr = Truck;
            if (!LoadLogic.InTruck(tr, cells)) return;
            if (placement != null) placement.cells = cells;
            else
            {
                if (!dock.waiting.Remove(box) && (forklift == null || !forklift.pallet.Remove(box) && !forklift.loose.Remove(box))) return;
                tr.placements.Add(new Placement { box = box, cells = cells });
            }
            tr.mode = "manual";
        }

        public static void Remove(Placement p)
        {
            Truck.placements.Remove(p);
            Return(p.box);
        }

        public static void ResetAll()
        {
            var tr = Truck;
            foreach (var p in tr.placements) Return(p.box);
            tr.placements.Clear();
        }

        public static void Depart()
        {
            if (Truck.placements.Count == 0 || LoadLogic.Invalid(Truck).Count > 0) return;
            Docks.Depart(dock);
        }
    }
}
