// 아르바이트: 고용/배치/AI/MT/휴식/시급 (원본 js/alba.js)
// 알바는 "계획"(Step 목록)을 하나씩 실행한다. 계획이 비면 공정별 Planner가 새 계획을 만든다
using System;
using System.Collections.Generic;
using System.Linq;
using static APC.Game;

namespace APC
{
    // Wait = 아직 진행 중, Done = 이 단계 끝, Abort = 계획 취소(예약 해제), More = repeat 안에서 한 번 더
    public enum StepResult { Wait, Done, Abort, More }

    public class Step
    {
        public bool move;
        public double x, z;
        public Func<Alba, double, StepResult> fn;

        public static Step Move(P p) => new Step { move = true, x = p.x, z = p.z };

        public static Step Act(Func<Alba, double, StepResult> fn) => new Step { fn = fn };

        public static Step Wait(double sec)
        {
            double t = 0;
            return Act((a, dt) => (t += dt) >= sec ? StepResult.Done : StepResult.Wait);
        }

        // 집기·내려놓기 1회 시간(handleTime)마다 fn 실행. fn이 More면 계속, 그 밖이면 그 결과로 끝
        public static Step Repeat(Func<Alba, StepResult> fn)
        {
            double t = 0;
            return Act((a, dt) =>
            {
                t += dt;
                double iv = Albas.HandleTime(a);
                while (t >= iv)
                {
                    t -= iv;
                    var r = fn(a);
                    if (r != StepResult.More) return r;
                }
                return StepResult.Wait;
            });
        }
    }

    public static class Albas
    {
        static readonly string[] NAME_FAMILY = { "김", "이", "박", "최", "정", "강", "조", "윤", "장", "임", "한", "오", "서", "신", "권", "황", "안", "송", "류", "홍" };
        static readonly string[] NAME_GIVEN = { "민준", "서연", "지호", "유진", "하은", "도윤", "서준", "지우", "예린", "건우", "소율", "태민",
            "지안", "우진", "나연", "시우", "하린", "준혁", "수아", "현우", "다은", "은호", "채원", "지훈" };

        public static Candidate MakeCandidate()
        {
            var c = CONFIG.alba;
            int St() => U.RandInt(c.statMin, c.statMax);
            var a = new Candidate { name = U.Pick(NAME_FAMILY) + U.Pick(NAME_GIVEN), str = St(), agi = St(), intel = St() };
            a.wage = U.Round(c.wageBase + c.wagePerStat * (a.str + a.agi + a.intel) + U.RandInt(0, c.wageJitter));
            return a;
        }

        // 낮/밤 1회(12시간)분 급여
        public static int ShiftPay(int wage) => wage * CONFIG.alba.hoursPerPhase;

        public static void RerollCandidates()
        {
            G.candidates = Enumerable.Range(0, CONFIG.alba.candidates).Select(_ => MakeCandidate()).ToList();
        }

        public static Alba Hire(int i)
        {
            if (G.albas.Count >= CONFIG.alba.max) { Notify("👷", "더 이상 고용할 수 없습니다"); return null; }
            if (i < 0 || i >= G.candidates.Count) return null;
            var c = G.candidates[i];
            G.candidates.RemoveAt(i);
            var a = new Alba
            {
                name = c.name, str = c.str, agi = c.agi, intel = c.intel, wage = c.wage,
                id = G.seqAlba++, mt = CONFIG.mt.start, x = LAYOUT.entrance.x, z = LAYOUT.entrance.z,
            };
            G.albas.Add(a);
            Notify("👷", $"{a.name} 고용 (대기중 슬롯)");
            return a;
        }

        public static void Fire(Alba a, bool fled = false)
        {
            Reset(a);
            G.albas.Remove(a);
            if (fled) Notify("🏃", $"{a.name}이(가) 밀린 시급 때문에 도망갔습니다!");
            else Notify("👋", $"{a.name} 해고");
        }

        // stageId: null = 대기중
        public static bool Assign(Alba a, string stageId)
        {
            if (a.stage == stageId) return true;
            if (stageId != null && (S.IsAuto(stageId) || S.StageCount(stageId) >= S.StageSlots(stageId)))
            {
                Notify("", "배치 슬롯이 부족합니다");
                return false;
            }
            Reset(a);
            a.stage = stageId;
            return true;
        }

        // 손에 든 물건을 원래 자리로 돌리고 예약/점유 해제
        public static void Reset(Alba a)
        {
            foreach (var e in a.hand)
            {
                switch (e.kind)
                {
                    case HandKind.Raw:
                        if (e.reserved) G.bins[e.item].incomingRaw--;
                        G.staging.Add(e.item);
                        break;
                    case HandKind.Pick: G.bins[e.item].checkedCount++; break;
                    case HandKind.Box:
                        e.box.claimedBy = Owner.None;
                        G.docks[e.box.dest].waiting.Add(e.box);
                        break;
                    case HandKind.Broken: G.brokenCount++; break;
                }
            }
            a.hand.Clear();
            ReleaseClaims(a);
            a.plan.Clear();
            a.progress = 0;
            a.resting = false;
        }

        public static void ReleaseClaims(Alba a)
        {
            foreach (var kv in a.pickRes) G.bins[kv.Key].reservedPick -= kv.Value;
            a.pickRes.Clear();
            foreach (var b in G.bins) if (b.inspector == a.id) b.inspector = Owner.None;
            foreach (var t in G.tables)
            {
                if (t.packer == a.id) { t.packer = Owner.None; t.packProgress = 0; }
                foreach (var b in t.boxes) if (b.claimedBy == a.id) b.claimedBy = Owner.None;
            }
            foreach (var d in G.docks) if (d.loader == a.id) Docks.ReleaseLoader(d);
        }

        // ---------- 스탯/숙련도 효과 ----------
        public static int Capacity(Alba a) => Math.Min(8, 1 + a.str + (int)Math.Floor(a.skLift / 30));
        public static double Speed(Alba a) => (3.0 + 0.5 * a.agi) * (1 + Math.Min(0.5, a.skCarry / 200)) * S.MtMult(a.mt);
        public static double WorkMult(Alba a, string key) => (0.6 + 0.12 * a.intel) * (1 + Math.Min(0.6, Skill(a, key) / 150)) * S.MtMult(a.mt);
        public static double HandleTime(Alba a) => CONFIG.timing.handle / S.MtMult(a.mt);
        public static int SkillLv(double xp) => (int)Math.Floor(xp / 10);

        public static double Skill(Alba a, string key) =>
            key == "lift" ? a.skLift : key == "carry" ? a.skCarry : key == "pack" ? a.skPack : a.skInspect;

        public static void Gain(Alba a, string key)
        {
            switch (key)
            {
                case "lift": a.skLift += 0.5 + 0.25 * a.str; break;
                case "carry": a.skCarry += 0.5 + 0.25 * a.agi; break;
                case "pack": a.skPack += 0.5 + 0.25 * a.intel; break;
                case "inspect": a.skInspect += 0.5 + 0.25 * a.intel; break;
            }
        }

        public static double LoadTarget(Alba a)
        {
            var c = CONFIG.loading.albaTarget;
            double k = 0.6 * (a.intel - 1) / 4 + 0.4 * Math.Min(1, a.skPack / 100);
            return c.min + (c.max - c.min) * k;
        }

        // 작업 1회: MT 감소(공기청정기 발동 시 없음) + 휴식 확률
        public static void WorkUnit(Alba a)
        {
            int pf = G.carePurifier;
            if (pf >= 0 && U.Random() < CONFIG.care.purifier.chance[pf]) a.fx = 1;
            else
            {
                double d = CONFIG.mt.perWork * (a.unpaid ? CONFIG.alba.unpaidMtMult : 1);
                a.mt = U.Clamp(a.mt - d, CONFIG.mt.min, CONFIG.mt.max);
            }
            if (U.Random() < S.RestChance(a.mt)) a.wantRest = true;
        }

        // MT가 음수인 알바가 물건을 옮기면 파손 확률
        public static void CarryRoll(Alba a)
        {
            if (a.mt >= 0 || a.hand.Count == 0 || U.Random() >= CONFIG.breakChance.carry) return;
            var e = U.Pick(a.hand);
            if (e.kind == HandKind.Raw && e.reserved) G.bins[e.item].incomingRaw--;
            string name = e.kind == HandKind.Box ? $"박스 #{e.box.id}" : CONFIG.items[e.item].name;
            e.kind = HandKind.Broken;
            e.reserved = false;
            Notify("💔", $"{a.name}: 옮기다가 {name} 파손!");
        }

        public static void Feed(Alba a, SnackDef snack)
        {
            a.mt = Math.Min(CONFIG.mt.max, a.mt + snack.mt);
            a.fx = 1;
            Notify(snack.emoji, $"{a.name}에게 {snack.name} (MT +{snack.mt})");
        }

        public static void OnPhaseChange()
        {
            int paid = 0, paidN = 0;
            var unpaid = new List<string>();
            foreach (var a in G.albas.ToList())
            {
                int pay = ShiftPay(a.wage);
                if (G.money >= pay)
                {
                    G.money -= pay;
                    paid += pay;
                    paidN++;
                    a.unpaid = false;
                    a.flee = 0;
                }
                else if (!a.unpaid)
                {
                    a.unpaid = true;
                    unpaid.Add(a.name);
                }
                else
                {
                    unpaid.Add(a.name);
                    a.flee += CONFIG.alba.fleeStep;
                    if (U.Random() < a.flee) Fire(a, true);
                }
            }
            if (paidN > 0) Notify("💸", $"시급 💰{U.Money(paid)} 소비됨 (알바 {paidN}명 × {CONFIG.alba.hoursPerPhase}시간)");
            if (unpaid.Count > 0) Notify("😡", $"시급 미지급: {string.Join(", ", unpaid)} (작업당 MT 감소 2배, 도주 위험)");
            RerollCandidates();
        }

        // ---------- 매 스텝 ----------
        public static void Update(double dt)
        {
            foreach (var a in G.albas.ToList()) Tick(a, dt);
        }

        static void Tick(Alba a, double dt)
        {
            a.fx = Math.Max(0, a.fx - dt);
            if (a.plan.Count == 0)
            {
                a.progress = 0;
                a.plan = Plan(a);
            }
            if (a.plan.Count == 0) return;
            var step = a.plan[0];
            if (step.move)
            {
                double dx = step.x - a.x, dz = step.z - a.z, d = U.Hypot(dx, dz);
                double sp = Speed(a) * dt;
                a.moving = true;
                if (d <= sp) { a.x = step.x; a.z = step.z; a.plan.RemoveAt(0); }
                else { a.x += dx / d * sp; a.z += dz / d * sp; a.facing = Math.Atan2(dx, dz); }
            }
            else
            {
                a.moving = false;
                var r = step.fn(a, dt);
                if (r == StepResult.Done) a.plan.RemoveAt(0);
                else if (r == StepResult.Abort) { ReleaseClaims(a); a.plan.Clear(); }
            }
        }

        static List<Step> Plan(Alba a)
        {
            if (S.HasKind(a, HandKind.Broken))
            {
                a.status = "파손품 버리러 가는 중";
                return new List<Step> { Step.Move(S.TrashIn()), Step.Repeat(x =>
                {
                    int i = x.hand.FindIndex(e => e.kind == HandKind.Broken);
                    if (i < 0) return StepResult.Done;
                    x.hand.RemoveAt(i);
                    G.brokenCount++;
                    return StepResult.More;
                }) };
            }
            if (a.wantRest && a.hand.Count == 0) return RestPlan(a);
            if (a.stage == null)
            {
                a.status = "대기";
                return new List<Step> { Step.Move(S.WaitSpot(a)), Step.Wait(1) };
            }
            var pl = Planners.For(a.stage, a);
            if (pl != null) return pl;
            a.status = "할 일 없음";
            return new List<Step> { Step.Move(IdleSpot(a)), Step.Wait(0.8) };
        }

        static List<Step> RestPlan(Alba a)
        {
            a.status = "휴식 중";
            int tier = G.careRest;
            var c = CONFIG.care.rest;
            return new List<Step> { Step.Move(S.RestSpot(a)), Step.Act((x, dt) =>
            {
                x.resting = true;
                x.restT += dt;
                x.mt = Math.Min(CONFIG.mt.max, x.mt + c.recover[tier] * dt);
                if (x.restT >= c.minRest && x.mt >= c.until[tier])
                {
                    x.resting = false;
                    x.restT = 0;
                    x.wantRest = false;
                    return StepResult.Done;
                }
                return StepResult.Wait;
            }) };
        }

        static P IdleSpot(Alba a)
        {
            var s = LAYOUT.idle[a.stage];
            int o = a.id % 3 - 1;
            return new P(s.x + o * 0.7, s.z + o * 0.5);
        }

        public static T NearestOf<T>(IEnumerable<T> list, P pos, Func<T, P> posFn)
        {
            T best = default;
            double bd = double.PositiveInfinity;
            foreach (var it in list)
            {
                double d = U.Dist(pos, posFn(it));
                if (d < bd) { bd = d; best = it; }
            }
            return best;
        }
    }

    // 공정별 행동 계획. null = 할 일 없음
    public static class Planners
    {
        public static List<Step> For(string stage, Alba a)
        {
            switch (stage)
            {
                case "unload": return Unload(a);
                case "sort": return Sort(a);
                case "inspect": return Inspect(a);
                case "transfer": return Transfer(a);
                case "pack": return Pack(a);
                case "outbound": return Outbound(a);
                case "load": return Load(a);
            }
            return null;
        }

        static List<Step> Unload(Alba a)
        {
            if (S.HasKind(a, HandKind.Raw))
            {
                if (G.staging.Count >= S.StagingCap())
                {
                    a.status = "하차장 가득 참";
                    return new List<Step> { Step.Wait(1) };
                }
                a.status = "하차장으로 운반";
                return new List<Step> { Step.Move(S.StagingIn()), Step.Repeat(x =>
                {
                    if (x.hand.Count == 0 || G.staging.Count >= S.StagingCap())
                    {
                        Albas.Gain(x, "carry");
                        return StepResult.Done;
                    }
                    var e = x.hand[x.hand.Count - 1];
                    x.hand.RemoveAt(x.hand.Count - 1);
                    G.staging.Add(e.item);
                    return StepResult.More;
                }) };
            }
            var trucks = G.inTrucks.Where(t => t.state == TruckState.Present && t.items.Count > 0).ToList();
            if (trucks.Count == 0 || G.staging.Count >= S.StagingCap()) return null;
            var tr = Albas.NearestOf(trucks, a.Pos, S.InTruckOut);
            a.status = $"하차 중 (트럭 {tr.idx + 1})";
            return new List<Step> { Step.Move(S.InTruckOut(tr)), Step.Repeat(x =>
            {
                if (tr.state == TruckState.Present && tr.items.Count > 0 && x.hand.Count < Albas.Capacity(x))
                {
                    int item = tr.items[tr.items.Count - 1];
                    tr.items.RemoveAt(tr.items.Count - 1);
                    x.hand.Add(new HandEntry { kind = HandKind.Raw, item = item });
                    return StepResult.More;
                }
                if (x.hand.Count > 0) { Albas.Gain(x, "lift"); Albas.WorkUnit(x); Albas.CarryRoll(x); }
                return StepResult.Done;
            }) };
        }

        static List<Step> Sort(Alba a)
        {
            var raw = a.hand.Find(e => e.kind == HandKind.Raw);
            if (raw != null)
            {
                var b = G.bins[raw.item];
                a.status = $"{CONFIG.items[b.item].name} 분류";
                return new List<Step> { Step.Move(S.BinIn(b)), Step.Repeat(x =>
                {
                    int i = x.hand.FindIndex(e => e.kind == HandKind.Raw && e.item == b.item);
                    if (i < 0) { Albas.Gain(x, "carry"); return StepResult.Done; }
                    var e = x.hand[i];
                    x.hand.RemoveAt(i);
                    if (e.reserved) { b.incomingRaw--; b.uncheckedCount++; }
                    else if (S.BinSpace(b) > 0) b.uncheckedCount++;
                    else G.staging.Add(e.item);
                    return StepResult.More;
                }) };
            }
            bool Ok(int it) => G.bins[it].unlocked && S.BinSpace(G.bins[it]) > 0;
            if (!G.staging.Any(Ok)) return null;
            a.status = "하차장에서 집기";
            return new List<Step> { Step.Move(S.StagingOut()), Step.Repeat(x =>
            {
                if (x.hand.Count < Albas.Capacity(x))
                {
                    int i = G.staging.FindIndex(Ok);
                    if (i >= 0)
                    {
                        int item = G.staging[i];
                        G.staging.RemoveAt(i);
                        G.bins[item].incomingRaw++;
                        x.hand.Add(new HandEntry { kind = HandKind.Raw, item = item, reserved = true });
                        return StepResult.More;
                    }
                }
                if (x.hand.Count > 0) { Albas.Gain(x, "lift"); Albas.WorkUnit(x); Albas.CarryRoll(x); }
                return StepResult.Done;
            }) };
        }

        static List<Step> Inspect(Alba a)
        {
            int cap = Albas.Capacity(a);
            var bb = G.bins.Find(b => b.unlocked && b.broken > 0);
            if (bb != null && S.CanTake(a, HandKind.Broken, cap))
            {
                a.status = "파손품 수거";
                return new List<Step> { Step.Move(S.BinInspect(bb)), Step.Repeat(x =>
                {
                    if (bb.broken > 0 && S.CanTake(x, HandKind.Broken, cap))
                    {
                        bb.broken--;
                        x.hand.Add(new HandEntry { kind = HandKind.Broken, item = bb.item });
                        return StepResult.More;
                    }
                    return StepResult.Done;
                }) };
            }
            var cands = G.bins.Where(b => b.unlocked && b.uncheckedCount > 0 && b.inspector == Owner.None).ToList();
            if (cands.Count == 0) return null;
            var bin = Albas.NearestOf(cands, a.Pos, S.BinInspect);
            bin.inspector = a.id;
            a.status = $"{CONFIG.items[bin.item].name} 검수";
            double prog = 0;
            return new List<Step> { Step.Move(S.BinInspect(bin)), Step.Act((x, dt) =>
            {
                if (bin.inspector != x.id) return StepResult.Abort;
                if (bin.uncheckedCount <= 0 || x.wantRest) { bin.inspector = Owner.None; return StepResult.Done; }
                prog += dt * Albas.WorkMult(x, "inspect");
                x.progress = prog / CONFIG.timing.inspect;
                if (prog < CONFIG.timing.inspect) return StepResult.Wait;
                prog = 0;
                bin.uncheckedCount--;
                Albas.Gain(x, "inspect");
                Albas.WorkUnit(x);
                if (U.Random() < CONFIG.breakChance.inspect)
                {
                    x.hand.Add(new HandEntry { kind = HandKind.Broken, item = bin.item });
                    bin.inspector = Owner.None;
                    return StepResult.Done;
                }
                bin.checkedCount++;
                return StepResult.Wait;
            }) };
        }

        static List<Step> Transfer(Alba a)
        {
            var picks = a.hand.Where(e => e.kind == HandKind.Pick).ToList();
            if (picks.Count > 0)
            {
                var t = S.ActiveTables().Find(tb => picks.Any(e => S.RemainingNeed(tb, e.item) > 0));
                if (t != null)
                {
                    a.status = $"포장대 {t.idx + 1}로 이송";
                    return new List<Step> { Step.Move(S.TableIn(t)), Step.Repeat(x =>
                    {
                        int i = x.hand.FindIndex(e => e.kind == HandKind.Pick && S.RemainingNeed(t, e.item) > 0);
                        if (i < 0) { Albas.Gain(x, "carry"); return StepResult.Done; }
                        var e = x.hand[i];
                        x.hand.RemoveAt(i);
                        t.delivered[e.item] = (t.delivered.TryGetValue(e.item, out int n) ? n : 0) + 1;
                        return StepResult.More;
                    }) };
                }
                var b = G.bins[picks[0].item];
                a.status = "남은 물품 반납";
                return new List<Step> { Step.Move(S.BinOut(b)), Step.Repeat(x =>
                {
                    int i = x.hand.FindIndex(e => e.kind == HandKind.Pick && e.item == b.item);
                    if (i < 0) return StepResult.Done;
                    x.hand.RemoveAt(i);
                    b.checkedCount++;
                    return StepResult.More;
                }) };
            }

            int cap = Albas.Capacity(a);
            var res = new SortedDictionary<int, int>();
            int got = 0;
            foreach (var tb in S.ActiveTables())
            {
                foreach (int item in tb.order.items.Keys)
                {
                    var bin = G.bins[item];
                    int take = Math.Min(Math.Min(S.OutstandingNeed(item), S.Avail(bin)), cap - got);
                    if (take > 0)
                    {
                        bin.reservedPick += take;
                        res[item] = (res.TryGetValue(item, out int r) ? r : 0) + take;
                        got += take;
                    }
                    if (got >= cap) break;
                }
                if (got >= cap) break;
            }
            if (got == 0) return null;
            a.pickRes = res;
            a.status = "주문 물품 집기";
            // 보관함 꺼내기 지점 x가 작은 순서로 돈다 (원본 sort는 안정 정렬)
            var items = res.Keys.OrderBy(it => S.BinOut(G.bins[it]).x).ToList();
            var steps = new List<Step>();
            for (int idx = 0; idx < items.Count; idx++)
            {
                int item = items[idx];
                bool last = idx == items.Count - 1;
                var bin = G.bins[item];
                steps.Add(Step.Move(S.BinOut(bin)));
                steps.Add(Step.Repeat(x =>
                {
                    x.pickRes.TryGetValue(item, out int left);
                    if (left > 0 && bin.checkedCount > 0)
                    {
                        x.pickRes[item] = left - 1;
                        bin.reservedPick--;
                        bin.checkedCount--;
                        x.hand.Add(new HandEntry { kind = HandKind.Pick, item = item });
                        return StepResult.More;
                    }
                    if (left > 0) bin.reservedPick -= left;
                    x.pickRes.Remove(item);
                    if (last && x.hand.Count > 0) { Albas.Gain(x, "lift"); Albas.WorkUnit(x); Albas.CarryRoll(x); }
                    return StepResult.Done;
                }));
            }
            return steps;
        }

        static List<Step> Pack(Alba a)
        {
            var t = G.tables.Find(tb => tb.unlocked && S.TableReady(tb) && tb.packer == Owner.None);
            if (t == null) return null;
            t.packer = a.id;
            a.status = $"포장대 {t.idx + 1} 포장";
            return new List<Step> { Step.Move(S.TableIn(t)), Step.Act((x, dt) =>
            {
                if (t.packer != x.id || !S.TableReady(t))
                {
                    if (t.packer == x.id) t.packer = Owner.None;
                    return StepResult.Done;
                }
                t.packProgress += dt * Albas.WorkMult(x, "pack");
                x.progress = t.packProgress / CONFIG.timing.pack;
                if (t.packProgress < CONFIG.timing.pack) return StepResult.Wait;
                Orders.Pack(t);
                Albas.Gain(x, "pack");
                Albas.WorkUnit(x);
                return StepResult.Done;
            }) };
        }

        static List<Step> Outbound(Alba a)
        {
            var boxes = a.hand.Where(e => e.kind == HandKind.Box).ToList();
            if (boxes.Count > 0)
            {
                var d = G.docks[boxes[0].box.dest];
                a.status = $"{CONFIG.destinations[d.idx].name} 도크로 운반";
                return new List<Step> { Step.Move(S.DockIn(d)), Step.Repeat(x =>
                {
                    int i = x.hand.FindIndex(e => e.kind == HandKind.Box && e.box.dest == d.idx);
                    if (i < 0) { Albas.Gain(x, "carry"); return StepResult.Done; }
                    var e = x.hand[i];
                    x.hand.RemoveAt(i);
                    e.box.claimedBy = Owner.None;
                    d.waiting.Add(e.box);
                    return StepResult.More;
                }) };
            }
            int cap = Albas.Capacity(a);
            var tables = G.tables.Where(tb => tb.unlocked && tb.boxes.Any(b => b.claimedBy == Owner.None)).ToList();
            if (tables.Count == 0) return null;
            var t = Albas.NearestOf(tables, a.Pos, S.TableOut);
            foreach (var b in t.boxes.Where(b => b.claimedBy == Owner.None).Take(cap).ToList()) b.claimedBy = a.id;
            a.status = "박스 집기";
            return new List<Step> { Step.Move(S.TableOut(t)), Step.Repeat(x =>
            {
                int i = t.boxes.FindIndex(b => b.claimedBy == x.id);
                if (i >= 0 && x.hand.Count < cap)
                {
                    var box = t.boxes[i];
                    t.boxes.RemoveAt(i);
                    x.hand.Add(new HandEntry { kind = HandKind.Box, box = box });
                    return StepResult.More;
                }
                if (x.hand.Count > 0) { Albas.Gain(x, "lift"); Albas.WorkUnit(x); Albas.CarryRoll(x); }
                return StepResult.Done;
            }) };
        }

        static List<Step> Load(Alba a)
        {
            var d = G.docks.Find(dk => dk.unlocked && dk.truck != null && dk.truck.state == TruckState.Present
                && dk.truck.mode != "manual" && (dk.loader == Owner.None || dk.loader == a.id)
                && dk.waiting.Any(b => LoadLogic.AutoFits(dk.truck, b)));
            if (d == null) return null;
            var tr = d.truck;
            d.loader = a.id;
            tr.mode = "auto";
            tr.autoTarget = Albas.LoadTarget(a);
            a.status = $"{CONFIG.destinations[d.idx].name} 상차";
            double tm = 0;
            return new List<Step> { Step.Move(S.DockIn(d)), Step.Act((x, dt) =>
            {
                if (d.truck != tr || tr.state != TruckState.Present || d.loader != x.id)
                {
                    if (d.loader == x.id) Docks.ReleaseLoader(d);
                    return StepResult.Done;
                }
                tm += dt * Albas.WorkMult(x, "pack");
                x.progress = tm / CONFIG.timing.loadBox;
                if (tm < CONFIG.timing.loadBox) return StepResult.Wait;
                tm = 0;
                int i = d.waiting.FindIndex(b => LoadLogic.AutoFits(tr, b));
                if (i < 0 || x.wantRest) { Docks.ReleaseLoader(d); return StepResult.Done; }
                var box = d.waiting[i];
                d.waiting.RemoveAt(i);
                tr.autoCells += box.cells.Count;
                tr.autoBoxes.Add(box);
                Albas.Gain(x, "lift");
                Albas.WorkUnit(x);
                if (LoadLogic.Fill(tr).ratio >= tr.autoTarget)
                {
                    Docks.Depart(d);
                    return StepResult.Done;
                }
                return StepResult.Wait;
            }) };
        }
    }
}
