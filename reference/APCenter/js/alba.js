// 아르바이트: 고용/배치/AI/MT/휴식/시급

const NAME_FAMILY = ['김', '이', '박', '최', '정', '강', '조', '윤', '장', '임', '한', '오', '서', '신', '권', '황', '안', '송', '류', '홍'];
const NAME_GIVEN = ['민준', '서연', '지호', '유진', '하은', '도윤', '서준', '지우', '예린', '건우', '소율', '태민',
  '지안', '우진', '나연', '시우', '하린', '준혁', '수아', '현우', '다은', '은호', '채원', '지훈'];

// 계획 단계 헬퍼
const Step = {
  move(p) { return { t: 'move', x: p.x, z: p.z }; },
  wait(sec) {
    let t = 0;
    return { t: 'act', fn: (a, dt) => (t += dt) >= sec };
  },
  // interval마다 fn 실행. fn이 'more'를 돌려주면 계속, true면 완료
  repeat(fn) {
    let t = 0;
    return {
      t: 'act', fn: (a, dt) => {
        t += dt;
        const iv = Albas.handleTime(a);
        while (t >= iv) {
          t -= iv;
          const r = fn(a);
          if (r !== 'more') return r;
        }
        return false;
      },
    };
  },
};

const Albas = {
  makeCandidate() {
    const c = CONFIG.alba;
    const st = () => U.randInt(c.statMin, c.statMax);
    const a = { name: U.pick(NAME_FAMILY) + U.pick(NAME_GIVEN), str: st(), agi: st(), int: st() };
    a.wage = Math.round(c.wageBase + c.wagePerStat * (a.str + a.agi + a.int) + U.randInt(0, c.wageJitter));
    return a;
  },

  // 낮/밤 1회(12시간)분 급여
  shiftPay(a) { return a.wage * CONFIG.alba.hoursPerPhase; },

  rerollCandidates() {
    G.candidates = Array.from({ length: CONFIG.alba.candidates }, Albas.makeCandidate);
  },

  hire(i) {
    if (G.albas.length >= CONFIG.alba.max) { UI.toast('👷 더 이상 고용할 수 없습니다'); return; }
    const c = G.candidates.splice(i, 1)[0];
    if (!c) return;
    const a = {
      ...c, id: G.seq.alba++, mt: CONFIG.mt.start,
      skills: { lift: 0, carry: 0, pack: 0, inspect: 0 },
      stage: null, x: LAYOUT.entrance.x, z: LAYOUT.entrance.z, facing: 0, moving: false,
      hand: [], plan: [], pickRes: {}, wantRest: false, resting: false, restT: 0,
      unpaid: false, flee: 0, status: '대기', progress: 0, fx: 0,
    };
    G.albas.push(a);
    World.addAlba(a);
    UI.toast(`👷 ${a.name} 고용 (대기중 슬롯)`);
  },

  fire(a, fled) {
    Albas.reset(a);
    G.albas.splice(G.albas.indexOf(a), 1);
    World.removeAlba(a);
    UI.toast(fled ? `🏃 ${a.name}이(가) 밀린 시급 때문에 도망갔습니다!` : `👋 ${a.name} 해고`);
  },

  assign(a, stageId) {
    if (a.stage === stageId) return true;
    if (stageId && (S.isAuto(stageId) || S.stageCount(stageId) >= S.stageSlots(stageId))) {
      UI.toast('배치 슬롯이 부족합니다');
      return false;
    }
    Albas.reset(a);
    a.stage = stageId;
    return true;
  },

  // 손에 든 물건을 원래 자리로 돌리고 예약/점유 해제
  reset(a) {
    for (const e of a.hand) {
      if (e.kind === 'raw') {
        if (e.reserved) G.bins[e.item].incomingRaw--;
        G.staging.push(e.item);
      } else if (e.kind === 'pick') {
        G.bins[e.item].checked++;
      } else if (e.kind === 'box') {
        e.box.claimedBy = null;
        G.docks[e.box.dest].waiting.push(e.box);
      } else if (e.kind === 'broken') {
        G.stats.broken++;
      }
    }
    a.hand = [];
    Albas.releaseClaims(a);
    a.plan = [];
    a.progress = 0;
    a.resting = false;
  },

  releaseClaims(a) {
    for (const [item, n] of Object.entries(a.pickRes)) G.bins[+item].reservedPick -= n;
    a.pickRes = {};
    for (const b of G.bins) if (b.inspector === a.id) b.inspector = null;
    for (const t of G.tables) {
      if (t.packer === a.id) { t.packer = null; t.packProgress = 0; }
      for (const b of t.boxes) if (b.claimedBy === a.id) b.claimedBy = null;
    }
    for (const d of G.docks) if (d.loader === a.id) Docks.releaseLoader(d);
  },

  // 스탯/숙련도 효과
  capacity(a) { return Math.min(8, 1 + a.str + Math.floor(a.skills.lift / 30)); },
  speed(a) { return (3.0 + 0.5 * a.agi) * (1 + Math.min(0.5, a.skills.carry / 200)) * S.mtMult(a.mt); },
  workMult(a, key) { return (0.6 + 0.12 * a.int) * (1 + Math.min(0.6, a.skills[key] / 150)) * S.mtMult(a.mt); },
  handleTime(a) { return CONFIG.timing.handle / S.mtMult(a.mt); },
  skillLv(xp) { return Math.floor(xp / 10); },
  gain(a, key) {
    const stat = { lift: 'str', carry: 'agi', pack: 'int', inspect: 'int' }[key];
    a.skills[key] += 0.5 + 0.25 * a[stat];
  },
  loadTarget(a) {
    const c = CONFIG.loading.albaTarget;
    const k = 0.6 * (a.int - 1) / 4 + 0.4 * Math.min(1, a.skills.pack / 100);
    return c.min + (c.max - c.min) * k;
  },

  // 작업 1회: MT 감소 + 휴식 확률
  workUnit(a) {
    const pf = G.care.purifier;
    if (pf >= 0 && Math.random() < CONFIG.care.purifier.chance[pf]) {
      a.fx = 1;
    } else {
      const d = CONFIG.mt.perWork * (a.unpaid ? CONFIG.alba.unpaidMtMult : 1);
      a.mt = U.clamp(a.mt - d, CONFIG.mt.min, CONFIG.mt.max);
    }
    if (Math.random() < S.restChance(a.mt)) a.wantRest = true;
  },

  // MT가 음수인 알바가 물건을 옮기면 파손 확률
  carryRoll(a) {
    if (a.mt >= 0 || !a.hand.length || Math.random() >= CONFIG.breakChance.carry) return;
    const e = U.pick(a.hand);
    if (e.kind === 'raw' && e.reserved) G.bins[e.item].incomingRaw--;
    const name = e.kind === 'box' ? `박스 #${e.box.id}` : CONFIG.items[e.item].name;
    e.kind = 'broken';
    e.reserved = false;
    UI.toast(`💔 ${a.name}: 옮기다가 ${name} 파손!`);
  },

  feed(a, snack) {
    a.mt = Math.min(CONFIG.mt.max, a.mt + snack.mt);
    a.fx = 1;
    UI.toast(`${snack.emoji} ${a.name}에게 ${snack.name} (MT +${snack.mt})`);
  },

  onPhaseChange() {
    let paid = 0, paidN = 0;
    const unpaid = [];
    for (const a of G.albas.slice()) {
      const pay = Albas.shiftPay(a);
      if (G.money >= pay) {
        G.money -= pay;
        paid += pay;
        paidN++;
        a.unpaid = false;
        a.flee = 0;
      } else if (!a.unpaid) {
        a.unpaid = true;
        unpaid.push(a.name);
      } else {
        unpaid.push(a.name);
        a.flee += CONFIG.alba.fleeStep;
        if (Math.random() < a.flee) Albas.fire(a, true);
      }
    }
    if (paidN) UI.toast(`💸 시급 ${U.money(paid)} 소비됨 (알바 ${paidN}명 × ${CONFIG.alba.hoursPerPhase}시간)`);
    if (unpaid.length) UI.toast(`😡 시급 미지급: ${unpaid.join(', ')} (작업당 MT 감소 2배, 도주 위험)`);
    Albas.rerollCandidates();
  },

  update(dt) {
    for (const a of G.albas.slice()) Albas.tick(a, dt);
  },

  tick(a, dt) {
    a.fx = Math.max(0, a.fx - dt);
    if (!a.plan.length) {
      a.progress = 0;
      a.plan = Albas.plan(a);
    }
    const step = a.plan[0];
    if (!step) return;
    if (step.t === 'move') {
      const dx = step.x - a.x, dz = step.z - a.z, d = Math.hypot(dx, dz);
      const sp = Albas.speed(a) * dt;
      a.moving = true;
      if (d <= sp) { a.x = step.x; a.z = step.z; a.plan.shift(); }
      else { a.x += dx / d * sp; a.z += dz / d * sp; a.facing = Math.atan2(dx, dz); }
    } else {
      a.moving = false;
      const r = step.fn(a, dt);
      if (r === true) a.plan.shift();
      else if (r === 'abort') { Albas.releaseClaims(a); a.plan = []; }
    }
  },

  plan(a) {
    if (S.hasKind(a, 'broken')) {
      a.status = '파손품 버리러 가는 중';
      return [Step.move(S.trashIn()), Step.repeat(a => {
        const i = a.hand.findIndex(e => e.kind === 'broken');
        if (i < 0) return true;
        a.hand.splice(i, 1);
        G.stats.broken++;
        return 'more';
      })];
    }
    if (a.wantRest && !a.hand.length) return Albas.restPlan(a);
    if (!a.stage) {
      a.status = '대기';
      return [Step.move(S.waitSpot(a)), Step.wait(1)];
    }
    const pl = Planners[a.stage](a);
    if (pl) return pl;
    a.status = '할 일 없음';
    return [Step.move(Albas.idleSpot(a)), Step.wait(0.8)];
  },

  restPlan(a) {
    a.status = '휴식 중';
    const tier = G.care.rest, c = CONFIG.care.rest;
    return [Step.move(S.restSpot(a)), {
      t: 'act', fn: (a, dt) => {
        a.resting = true;
        a.restT += dt;
        a.mt = Math.min(CONFIG.mt.max, a.mt + c.recover[tier] * dt);
        if (a.restT >= c.minRest && a.mt >= c.until[tier]) {
          a.resting = false;
          a.restT = 0;
          a.wantRest = false;
          return true;
        }
        return false;
      },
    }];
  },

  idleSpot(a) {
    const s = LAYOUT.idle[a.stage], o = (a.id % 3) - 1;
    return { x: s.x + o * 0.7, z: s.z + o * 0.5 };
  },
};

function nearestOf(list, pos, posFn) {
  let best = null, bd = Infinity;
  for (const it of list) {
    const d = U.dist(pos, posFn(it));
    if (d < bd) { bd = d; best = it; }
  }
  return best;
}

// 공정별 행동 계획
const Planners = {
  unload(a) {
    if (S.hasKind(a, 'raw')) {
      if (G.staging.length >= S.stagingCap()) {
        a.status = '하차장 가득 참';
        return [Step.wait(1)];
      }
      a.status = '하차장으로 운반';
      return [Step.move(S.stagingIn()), Step.repeat(a => {
        if (!a.hand.length || G.staging.length >= S.stagingCap()) {
          Albas.gain(a, 'carry');
          return true;
        }
        G.staging.push(a.hand.pop().item);
        return 'more';
      })];
    }
    const trucks = G.inTrucks.filter(t => t.state === 'present' && t.items.length);
    if (trucks.length && G.staging.length < S.stagingCap()) {
      const tr = nearestOf(trucks, a, S.inTruckOut);
      a.status = `하차 중 (트럭 ${tr.idx + 1})`;
      return [Step.move(S.inTruckOut(tr)), Step.repeat(a => {
        if (tr.state === 'present' && tr.items.length && a.hand.length < Albas.capacity(a)) {
          a.hand.push({ kind: 'raw', item: tr.items.pop() });
          return 'more';
        }
        if (a.hand.length) { Albas.gain(a, 'lift'); Albas.workUnit(a); Albas.carryRoll(a); }
        return true;
      })];
    }
    return null;
  },

  sort(a) {
    const raw = a.hand.find(e => e.kind === 'raw');
    if (raw) {
      const b = G.bins[raw.item];
      a.status = `${CONFIG.items[b.item].name} 분류`;
      return [Step.move(S.binIn(b)), Step.repeat(a => {
        const i = a.hand.findIndex(e => e.kind === 'raw' && e.item === b.item);
        if (i < 0) { Albas.gain(a, 'carry'); return true; }
        const e = a.hand.splice(i, 1)[0];
        if (e.reserved) { b.incomingRaw--; b.unchecked++; }
        else if (S.binSpace(b) > 0) b.unchecked++;
        else G.staging.push(e.item);
        return 'more';
      })];
    }
    const ok = it => G.bins[it].unlocked && S.binSpace(G.bins[it]) > 0;
    if (!G.staging.some(ok)) return null;
    a.status = '하차장에서 집기';
    return [Step.move(S.stagingOut()), Step.repeat(a => {
      if (a.hand.length < Albas.capacity(a)) {
        const i = G.staging.findIndex(ok);
        if (i >= 0) {
          const item = G.staging.splice(i, 1)[0];
          G.bins[item].incomingRaw++;
          a.hand.push({ kind: 'raw', item, reserved: true });
          return 'more';
        }
      }
      if (a.hand.length) { Albas.gain(a, 'lift'); Albas.workUnit(a); Albas.carryRoll(a); }
      return true;
    })];
  },

  inspect(a) {
    const cap = Albas.capacity(a);
    const bb = G.bins.find(b => b.unlocked && b.broken > 0);
    if (bb && S.canTake(a, 'broken', cap)) {
      a.status = '파손품 수거';
      return [Step.move(S.binInspect(bb)), Step.repeat(a => {
        if (bb.broken > 0 && S.canTake(a, 'broken', cap)) {
          bb.broken--;
          a.hand.push({ kind: 'broken', item: bb.item });
          return 'more';
        }
        return true;
      })];
    }
    const cands = G.bins.filter(b => b.unlocked && b.unchecked > 0 && b.inspector === null);
    if (!cands.length) return null;
    const b = nearestOf(cands, a, S.binInspect);
    b.inspector = a.id;
    a.status = `${CONFIG.items[b.item].name} 검수`;
    let prog = 0;
    return [Step.move(S.binInspect(b)), {
      t: 'act', fn: (a, dt) => {
        if (b.inspector !== a.id) return 'abort';
        if (b.unchecked <= 0 || a.wantRest) { b.inspector = null; return true; }
        prog += dt * Albas.workMult(a, 'inspect');
        a.progress = prog / CONFIG.timing.inspect;
        if (prog < CONFIG.timing.inspect) return false;
        prog = 0;
        b.unchecked--;
        Albas.gain(a, 'inspect');
        Albas.workUnit(a);
        if (Math.random() < CONFIG.breakChance.inspect) {
          a.hand.push({ kind: 'broken', item: b.item });
          b.inspector = null;
          return true;
        }
        b.checked++;
        return false;
      },
    }];
  },

  transfer(a) {
    const picks = a.hand.filter(e => e.kind === 'pick');
    if (picks.length) {
      const t = S.activeTables().find(t => picks.some(e => S.remainingNeed(t, e.item) > 0));
      if (t) {
        a.status = `포장대 ${t.idx + 1}로 이송`;
        return [Step.move(S.tableIn(t)), Step.repeat(a => {
          const i = a.hand.findIndex(e => e.kind === 'pick' && S.remainingNeed(t, e.item) > 0);
          if (i < 0) { Albas.gain(a, 'carry'); return true; }
          const e = a.hand.splice(i, 1)[0];
          t.delivered[e.item] = (t.delivered[e.item] || 0) + 1;
          return 'more';
        })];
      }
      const b = G.bins[picks[0].item];
      a.status = '남은 물품 반납';
      return [Step.move(S.binOut(b)), Step.repeat(a => {
        const i = a.hand.findIndex(e => e.kind === 'pick' && e.item === b.item);
        if (i < 0) return true;
        a.hand.splice(i, 1);
        b.checked++;
        return 'more';
      })];
    }

    const cap = Albas.capacity(a);
    const res = {};
    let n = 0;
    outer:
    for (const t of S.activeTables()) {
      for (const k of Object.keys(t.order.items)) {
        const item = +k, b = G.bins[item];
        const take = Math.min(S.outstandingNeed(item), S.avail(b), cap - n);
        if (take > 0) {
          b.reservedPick += take;
          res[item] = (res[item] || 0) + take;
          n += take;
        }
        if (n >= cap) break outer;
      }
    }
    if (!n) return null;
    a.pickRes = res;
    a.status = '주문 물품 집기';
    const items = Object.keys(res).map(Number)
      .sort((x, y) => S.binOut(G.bins[x]).x - S.binOut(G.bins[y]).x);
    const steps = [];
    items.forEach((item, idx) => {
      const b = G.bins[item];
      steps.push(Step.move(S.binOut(b)));
      steps.push(Step.repeat(a => {
        if (a.pickRes[item] > 0 && b.checked > 0) {
          a.pickRes[item]--;
          b.reservedPick--;
          b.checked--;
          a.hand.push({ kind: 'pick', item });
          return 'more';
        }
        if (a.pickRes[item] > 0) b.reservedPick -= a.pickRes[item];
        delete a.pickRes[item];
        if (idx === items.length - 1 && a.hand.length) { Albas.gain(a, 'lift'); Albas.workUnit(a); Albas.carryRoll(a); }
        return true;
      }));
    });
    return steps;
  },

  pack(a) {
    const t = G.tables.find(t => t.unlocked && S.tableReady(t) && t.packer === null);
    if (!t) return null;
    t.packer = a.id;
    a.status = `포장대 ${t.idx + 1} 포장`;
    return [Step.move(S.tableIn(t)), {
      t: 'act', fn: (a, dt) => {
        if (t.packer !== a.id || !S.tableReady(t)) {
          if (t.packer === a.id) t.packer = null;
          return true;
        }
        t.packProgress += dt * Albas.workMult(a, 'pack');
        a.progress = t.packProgress / CONFIG.timing.pack;
        if (t.packProgress < CONFIG.timing.pack) return false;
        Orders.pack(t);
        Albas.gain(a, 'pack');
        Albas.workUnit(a);
        return true;
      },
    }];
  },

  outbound(a) {
    const boxes = a.hand.filter(e => e.kind === 'box');
    if (boxes.length) {
      const d = G.docks[boxes[0].box.dest];
      a.status = `${CONFIG.destinations[d.idx].name} 도크로 운반`;
      return [Step.move(S.dockIn(d)), Step.repeat(a => {
        const i = a.hand.findIndex(e => e.kind === 'box' && e.box.dest === d.idx);
        if (i < 0) { Albas.gain(a, 'carry'); return true; }
        const e = a.hand.splice(i, 1)[0];
        e.box.claimedBy = null;
        d.waiting.push(e.box);
        return 'more';
      })];
    }
    const cap = Albas.capacity(a);
    const tables = G.tables.filter(t => t.unlocked && t.boxes.some(b => !b.claimedBy));
    if (!tables.length) return null;
    const t = nearestOf(tables, a, S.tableOut);
    t.boxes.filter(b => !b.claimedBy).slice(0, cap).forEach(b => { b.claimedBy = a.id; });
    a.status = '박스 집기';
    return [Step.move(S.tableOut(t)), Step.repeat(a => {
      const i = t.boxes.findIndex(b => b.claimedBy === a.id);
      if (i >= 0 && a.hand.length < cap) {
        a.hand.push({ kind: 'box', box: t.boxes.splice(i, 1)[0] });
        return 'more';
      }
      if (a.hand.length) { Albas.gain(a, 'lift'); Albas.workUnit(a); Albas.carryRoll(a); }
      return true;
    })];
  },

  load(a) {
    const d = G.docks.find(d => d.unlocked && d.truck && d.truck.state === 'present'
      && d.truck.mode !== 'manual' && (d.loader === null || d.loader === a.id)
      && d.waiting.some(b => LoadLogic.autoFits(d.truck, b)));
    if (!d) return null;
    const tr = d.truck;
    d.loader = a.id;
    tr.mode = 'auto';
    tr.autoTarget = Albas.loadTarget(a);
    a.status = `${CONFIG.destinations[d.idx].name} 상차`;
    let tm = 0;
    return [Step.move(S.dockIn(d)), {
      t: 'act', fn: (a, dt) => {
        if (d.truck !== tr || tr.state !== 'present' || d.loader !== a.id) {
          if (d.loader === a.id) Docks.releaseLoader(d);
          return true;
        }
        tm += dt * Albas.workMult(a, 'pack');
        a.progress = tm / CONFIG.timing.loadBox;
        if (tm < CONFIG.timing.loadBox) return false;
        tm = 0;
        const i = d.waiting.findIndex(b => LoadLogic.autoFits(tr, b));
        if (i < 0 || a.wantRest) { Docks.releaseLoader(d); return true; }
        const box = d.waiting.splice(i, 1)[0];
        tr.autoCells += box.cells.length;
        tr.autoBoxes.push(box);
        Albas.gain(a, 'lift');
        Albas.workUnit(a);
        if (LoadLogic.fill(tr).ratio >= tr.autoTarget) {
          Docks.depart(d);
          return true;
        }
        return false;
      },
    }];
  },
};
