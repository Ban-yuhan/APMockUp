// 재고 예약 카운터 정합성 (원본 js/main.js의 Main.checkInvariants). 빈 목록 = 통과
using System.Collections.Generic;
using System.Linq;
using static APC.Game;

namespace APC
{
    public static class Invariants
    {
        public static List<string> Check()
        {
            var errs = new List<string>();
            foreach (var b in G.bins)
            {
                int i = b.idx, res = 0, inRaw = 0;
                foreach (var a in G.albas)
                {
                    res += a.pickRes.TryGetValue(i, out int n) ? n : 0;
                    inRaw += a.hand.Count(e => e.kind == HandKind.Raw && e.reserved && e.item == i);
                }
                inRaw += G.flyers.Count(f => f.kind == HandKind.Raw && f.item == i);
                int inPick = G.flyers.Count(f => f.kind == HandKind.Pick && f.item == i);
                if (res != b.reservedPick) errs.Add($"bin{i} reservedPick {b.reservedPick} != {res}");
                if (inRaw != b.incomingRaw) errs.Add($"bin{i} incomingRaw {b.incomingRaw} != {inRaw}");
                if (inPick != b.inFlightPick) errs.Add($"bin{i} inFlightPick {b.inFlightPick} != {inPick}");
                if (b.checkedCount < 0 || b.uncheckedCount < 0 || b.broken < 0) errs.Add($"bin{i} negative");
                if (S.BinSpace(b) < 0) errs.Add($"bin{i} over capacity");
                if (b.unlocked && S.InboundRoom(i) < 0) errs.Add($"bin{i} bin+staging+inbound {S.BinCap() - S.InboundRoom(i)} > cap {S.BinCap()}");
            }
            foreach (var t in G.tables)
                if (t.order != null)
                    foreach (var k in t.order.items.Keys)
                        if (S.RemainingNeed(t, k) < 0) errs.Add($"table{t.idx} over-delivered item{k}");
            return errs;
        }
    }
}
