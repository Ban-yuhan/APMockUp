// 메인 루프, 낮/밤 전환, 자체 점검(?test)

const Main = {
  last: 0,

  init() {
    G = createState();
    Player.init();
    const t0 = G.inTrucks[0];
    Object.assign(t0, { state: 'present', off: 0, items: S.rollInboundItems() });
    Albas.rerollCandidates();
    World.init(document.getElementById('view'));
    UI.init();
    LoadingUI.init();
    Debug.init();
    UI.toast('📦 APCenter에 오신 것을 환영합니다! 입고 트럭 앞에 서서 택배를 하차해 보세요');
    UI.toast(`🗺️ 배치: ${LAYOUT.name} (${LayoutLib.source})`);
    if (LayoutLib.loadErrors.length) UI.toast(`⚠️ 불러온 배치 오류: ${LayoutLib.loadErrors[0].msg}`);
    window.APC = { G: () => G, CONFIG, LAYOUT, S, Shop, Albas, LoadLogic, LoadingUI, Main };
    if (/[?&]test/.test(location.search)) Main.selfTest();
    requestAnimationFrame(t => { Main.last = t; Main.frame(t); });
  },

  frame(now) {
    const dt = Math.min(0.1, Math.max(0, (now - Main.last) / 1000));
    Main.last = now;
    const running = !G.paused && !G.userPaused;
    if (running) {
      let total = dt * G.speed;
      while (total > 1e-6) {
        const s = Math.min(0.05, total);
        Main.update(s);
        total -= s;
      }
    }
    World.sync(running ? dt * G.speed : 0);
    World.updateCamera(dt);
    UI.update(dt);
    Debug.update(dt);
    World.render();
    requestAnimationFrame(Main.frame);
  },

  update(dt) {
    G.time += dt;
    G.phaseTime += dt;
    if (G.phaseTime >= CONFIG.phaseSeconds) {
      G.phaseTime -= CONFIG.phaseSeconds;
      Main.switchPhase();
    }
    Inbound.update(dt);
    Orders.update(dt);
    Docks.update(dt);
    Player.update(dt, UI.inputVector());
    Albas.update(dt);
    Auto.update(dt);
  },

  togglePhase() {
    G.phase = G.phase === 'day' ? 'night' : 'day';
    if (G.phase === 'day') G.day++;
  },

  switchPhase() {
    Main.togglePhase();
    UI.toast(G.phase === 'day' ? `☀️ ${G.day}일차 아침이 밝았습니다` : '🌙 밤이 되었습니다');
    if (G.event) Main.endEvent();
    else if (Math.random() < CONFIG.event.chance) Main.startEvent();
    Albas.onPhaseChange();
  },

  startEvent() {
    G.event = { name: CONFIG.event.name };
    for (let i = 0; i < CONFIG.event.rushOrders; i++) Orders.spawn();
    UI.toast(`🎉 ${CONFIG.event.name} 주문서 ${CONFIG.event.rushOrders}개가 한꺼번에 들어왔습니다!`);
  },

  endEvent() {
    G.event = null;
    UI.toast(`🎉 ${CONFIG.event.name} 종료`);
  },

  // ---------- 자체 점검 ----------
  checkInvariants(log) {
    const errs = [];
    G.bins.forEach((b, i) => {
      let res = 0, inRaw = 0;
      for (const a of G.albas) {
        res += a.pickRes[i] || 0;
        inRaw += a.hand.filter(e => e.kind === 'raw' && e.reserved && e.item === i).length;
      }
      inRaw += G.flyers.filter(f => f.kind === 'raw' && f.ref === i).length;
      const inPick = G.flyers.filter(f => f.kind === 'pick' && f.ref === i).length;
      if (res !== b.reservedPick) errs.push(`bin${i} reservedPick ${b.reservedPick} != ${res}`);
      if (inRaw !== b.incomingRaw) errs.push(`bin${i} incomingRaw ${b.incomingRaw} != ${inRaw}`);
      if (inPick !== b.inFlightPick) errs.push(`bin${i} inFlightPick ${b.inFlightPick} != ${inPick}`);
      if (b.checked < 0 || b.unchecked < 0 || b.broken < 0) errs.push(`bin${i} negative`);
      if (S.binSpace(b) < 0) errs.push(`bin${i} over capacity`);
      if (b.unlocked && S.inboundRoom(i) < 0) errs.push(`bin${i} bin+staging+inbound ${S.binCap() - S.inboundRoom(i)} > cap ${S.binCap()}`);
    });
    G.tables.forEach(t => {
      if (t.order) for (const k of Object.keys(t.order.items)) if (S.remainingNeed(t, +k) < 0) errs.push(`table${t.idx} over-delivered item${k}`);
    });
    log(errs.length ? 'INVARIANT FAIL: ' + errs.join('; ') : 'invariants ok');
    return errs.length === 0;
  },

  selfTest() {
    const out = [];
    const log = (...a) => out.push(a.join(' '));
    const sim = sec => { for (let t = 0; t < sec; t += 0.05) { Main.update(0.05); World.sync(0.05); UI.update(0.05); } };
    const summary = tag => log(tag, JSON.stringify(G.stats), 'money', Math.round(G.money), 'orders', G.orders.length,
      'staging', G.staging.length + '/' + S.stagingCap(), 'trucks', G.inTrucks.map(t => t.state[0] + t.items.length).join(','),
      'bins', G.bins.map(b => b.unchecked + '/' + b.checked).join(' '),
      'tables', G.tables.map(t => (t.order ? t.order.id : '-') + ':' + t.boxes.length).join(' '),
      'docks', G.docks.map(d => d.waiting.length + (d.truck ? '@' + Math.round(LoadLogic.fill(d.truck).rate) : '')).join(','),
      'day', G.day, G.phase);
    let ok = true;
    try {
      // 벨트: 공정별로 필요한 모든 연결이 한 방향 경로로 이어지는지
      for (const st of LayoutLib.BELT_STAGES) {
        const pairs = LayoutLib.beltPairs(LAYOUT, st);
        const found = pairs.filter(pr => LayoutLib.cachedRoute(st, pr.src, pr.dst)).length;
        log(`belt ${st} routes ${found}/${pairs.length}`);
        if (LayoutLib.source.startsWith('기본') && found !== pairs.length) { log(`FAIL: default ${st} belt not fully connected`); ok = false; }
      }

      // 0. 플레이어 수동 작업
      const p = G.player;
      Object.assign(p, S.inTruckOut(G.inTrucks[0]));
      sim(3);
      log('player hand after truck', p.hand.map(e => e.kind + e.item).join(','));
      // 하차장: 들고 들어가면 내려놓기만, 빈손으로 들어가면 집기만 (집자마자 내려놓지 않아야 함)
      Object.assign(p, S.stagingIn());
      sim(2);
      log('staging drop → staging', G.staging.length, 'hand', p.hand.length);
      Object.assign(p, LAYOUT.playerStart);
      sim(0.2);
      Object.assign(p, S.stagingOut());
      sim(3);
      log('staging pick → staging', G.staging.length, 'hand', p.hand.length, 'action', p.action);
      if (p.hand.length !== Player.capacity()) { log('FAIL: staging pick/drop loop'); ok = false; }
      Object.assign(p, LAYOUT.playerStart);
      sim(0.2);
      const first = p.hand[0].item;
      Object.assign(p, S.binIn(G.bins[first]));
      sim(2);
      Object.assign(p, S.binInspect(G.bins[first]));
      sim(5);
      const b0 = G.bins[first];
      log('bin after drop+inspect', b0.unchecked, b0.checked, 'hand', p.hand.length);
      Object.assign(p, LAYOUT.playerStart);
      sim(0.2);

      // 1. 전 시설 해금 + 알바 10명 (Lv3)
      G.money = 100000;
      CONFIG.items.forEach((_, i) => Shop.buyBin(i));
      G.tables.forEach((_, i) => Shop.buyTable(i));
      G.docks.forEach((_, i) => Shop.buyDock(i));
      Shop.upRest(); Shop.upPurifier();
      for (const k of Object.keys(CONFIG.facility)) { Shop.upFacility(k); Shop.upFacility(k); }
      log('facility', JSON.stringify(G.fac), 'parking', S.parkingCount(), 'staging cap', S.stagingCap(), 'bin cap', S.binCap());
      sim(6);
      log('inbound trucks', G.inTrucks.map(t => t.state).join(','));
      for (const s of CONFIG.stages) { Shop.upStage(s.id); Shop.upStage(s.id); }
      const zl = Zones.list(), minGap = CONFIG.interactRadius * 2;
      for (let i = 0; i < zl.length; i++) for (let j = i + 1; j < zl.length; j++) {
        const dd = U.dist(zl[i], zl[j]);
        if (dd < minGap) { log(`FAIL: zone overlap ${zl[i].key}-${zl[j].key} ${dd.toFixed(2)}`); ok = false; }
      }
      log('zones', zl.length, 'checked for overlap');
      const plan = ['unload', 'sort', 'inspect', 'transfer', 'pack', 'outbound', 'load', 'unload', 'transfer', 'outbound'];
      for (const st of plan) {
        if (!G.candidates.length) Albas.rerollCandidates();
        Albas.hire(0);
        const a = G.albas[G.albas.length - 1];
        if (!Albas.assign(a, st)) log('assign fail', st);
      }
      const wageSum = G.albas.reduce((s, a) => s + Albas.shiftPay(a), 0), m0 = G.money;
      Debug.run('phase');
      log('phase switch wages', wageSum, 'money diff', Math.round(m0 - G.money), 'phase', G.phase);
      if (Math.round(m0 - G.money) !== wageSum) { log('FAIL: wage payment'); ok = false; }
      Debug.run('phase-only');
      // MT 분포 측정: 낡은 휴게실, 공기청정기 없음, 간식 없음. 처음 20분은 시작값(50) 영향이라 제외
      const care = { ...G.care };
      G.care.rest = 0;
      G.care.purifier = -1;
      sim(1200);
      const hist = { '<-50': 0, '-50~0': 0, '0~20': 0, '20~50': 0, '50+': 0 };
      let samples = 0, mtSum = 0, rests = 0;
      const perStage = {};
      for (let s = 0; s < 600; s++) {
        sim(1);
        for (const a of G.albas) {
          const m = a.mt;
          hist[m < -50 ? '<-50' : m < 0 ? '-50~0' : m < 20 ? '0~20' : m < 50 ? '20~50' : '50+']++;
          samples++;
          mtSum += m;
          if (a.resting) rests++;
          const ps = perStage[a.stage] = perStage[a.stage] || { sum: 0, n: 0 };
          ps.sum += m; ps.n++;
        }
      }
      log('MT avg by stage', Object.entries(perStage).map(([k, v]) => `${k}:${(v.sum / v.n).toFixed(0)}`).join(' '));
      const pct = Object.entries(hist).map(([k, v]) => `${k}:${Math.round(v / samples * 100)}%`).join(' ');
      log('MT dist', pct, 'avg', (mtSum / samples).toFixed(1), 'resting', Math.round(rests / samples * 100) + '%');
      Object.assign(G.care, care);
      summary('phase1');
      ok = Main.checkInvariants(log) && ok;
      if (G.stats.trucks < 1) { log('FAIL: no truck departed with albas'); ok = false; }

      // 2. 미니게임 (UI 경로 포함)
      const d = G.docks[1];
      const saveLoad = G.stages.load;
      for (const a of G.albas) if (a.stage === 'load') Albas.assign(a, null);
      G.loadUpg.compress = 1;
      G.loadUpg.bonus = 2;
      // 잠긴 칸 랜덤: 종류별 개수 범위 확인
      for (let i = 0; i < 300; i++) {
        const t = LoadLogic.createTruck(), n = t.locked.size;
        if (n < t.def.locked[0] || n > t.def.locked[1]) { log(`FAIL: ${t.def.id} locked ${n}`); ok = false; break; }
      }
      d.truck = LoadLogic.createTruck();
      Object.assign(d.truck, { state: 'present', off: 0 });
      d.truck.locked = new Set();   // 아래 드래그 점검은 고정 좌표를 쓰므로 잠긴 칸을 비운다
      d.loader = null;
      const mk = (id, units) => {
        const sh = LoadLogic.randomShape(units);
        return { id, dest: d.idx, reward: 50, units, shape: sh.name, cells: sh.cells, types: ['food', 'daily'], primary: 'food', items: { 0: units }, claimedBy: null };
      };
      d.waiting.push(mk(9001, 3), mk(9002, 2), mk(9003, 1), mk(9004, 4));
      LoadingUI.open(d);
      log('minigame open', !!LoadingUI.dock, 'paused', G.paused, 'truck', d.truck.def.id, 'bonus', d.truck.bonus.size);
      const grid = document.querySelector('.ld-grid');
      const r = grid.getBoundingClientRect(), CS = LoadingUI.CS;
      const ev = (x, y) => ({ clientX: r.left + x * CS + CS / 2, clientY: r.top + y * CS + CS / 2, preventDefault() {}, button: 0 });
      const box = d.waiting.find(b => b.id === 9002);
      LoadingUI.startDrag(ev(0, 1), box, box.cells.map(c => c.slice()), 0, null);
      // 드래그 중 우클릭 = 회전 (R 키와 같음)
      const cellsBefore = JSON.stringify(LoadingUI.drag.cells);
      window.dispatchEvent(new MouseEvent('mousedown', { button: 2, bubbles: true, cancelable: true }));
      log('right-click rotate', cellsBefore, '->', JSON.stringify(LoadingUI.drag.cells), 'placements', d.truck.placements.length);
      if (JSON.stringify(LoadingUI.drag.cells) === cellsBefore || d.truck.placements.length) { log('FAIL: right-click rotate'); ok = false; }
      LoadingUI.updateDrag(ev(1, 1).clientX, ev(1, 1).clientY);
      log('drag valid', LoadingUI.drag.valid, JSON.stringify(LoadingUI.drag.target));
      LoadingUI.endDrag();
      log('placements', d.truck.placements.length, 'mode', d.truck.mode);
      const b3 = d.waiting.find(b => b.id === 9003);
      log('overlap allowed (compress 1)', LoadLogic.canPlace(d.truck, [[1, 1]]), 'double overlap', LoadLogic.canPlace(d.truck, [[1, 1], [1, 2]]));
      LoadingUI.place(b3, [[1, 1]], null);
      const pay = LoadLogic.payout(d.truck);
      log('payout', JSON.stringify({ rate: Math.round(pay.fill.rate), base: pay.base, fm: pay.fillMult, rm: pay.rankMult, total: pay.total, overlap: pay.fill.overlap }));
      // 특수 목표: 조건을 채우면 체크, 박스를 빼면 해제
      d.truck.objectives = [
        { kind: 'item', item: 0, bonus: 5 },
        { kind: 'boxes', n: 2, bonus: 4 },
        { kind: 'rank', type: 'food', rank: 1, bonus: 5 },
      ];
      const objState = () => LoadLogic.payout(d.truck).objectives.map(o => (o.done ? 1 : 0)).join('');
      log('objectives now', objState(), 'objBonus', LoadLogic.payout(d.truck).objBonus + '%p');
      const removed = d.truck.placements[d.truck.placements.length - 1];
      LoadingUI.remove(removed);
      const afterRemove = objState();
      LoadingUI.place(removed.box, removed.cells, null);
      log('objectives after remove', afterRemove, 'after re-add', objState());
      if (objState() !== '111' || afterRemove === '111') { log('FAIL: objective check/uncheck'); ok = false; }
      // 모든 칸을 다 채우면 적재율 200%
      const full = LoadLogic.createTruck();
      full.mode = 'manual';
      for (let y = 0; y < full.h; y++) for (let x = 0; x < full.w; x++) {
        if (!full.locked.has(LoadLogic.key(x, y))) full.placements.push({ box: mk(9100 + y * 10 + x, 1), cells: [[x, y]] });
      }
      const ff = LoadLogic.fill(full);
      log('full truck rate', Math.round(ff.rate), 'fillMult', LoadLogic.fillMult(ff.rate));
      if (Math.round(ff.rate) !== CONFIG.loading.fullRate) { log('FAIL: full truck rate'); ok = false; }
      // 가득 찬 트럭 + 특수 목표 달성 → 적재율이 200% 상한을 넘고 배율도 x1.5보다 커야 함
      full.objectives = [{ kind: 'boxes', n: 3, bonus: 6 }, { kind: 'item', item: 0, bonus: 5 }];
      const fp = LoadLogic.payout(full);
      log('full + objectives rate', Math.round(fp.rate), 'objBonus', fp.objBonus, 'fillMult', fp.fillMult.toFixed(2));
      if (Math.round(fp.rate) !== CONFIG.loading.maxRate + 11 || Math.abs(fp.fillMult - 1.61) > 1e-9) { log('FAIL: objective bonus over cap'); ok = false; }
      const before = G.money;
      LoadingUI.depart();
      log('depart money +', Math.round(G.money - before), 'dock closed', LoadingUI.dock === null, 'paused', G.paused);
      G.stages.load = saveLoad;

      // 3. 전 공정 자동화
      for (const s of CONFIG.stages) while (G.stages[s.id] < s.costs.length) Shop.upStage(s.id);
      const trucks = G.stats.trucks;
      sim(600);
      summary('phase2');
      ok = Main.checkInvariants(log) && ok;
      if (G.stats.trucks <= trucks) { log('FAIL: no truck departed with automation'); ok = false; }
      log('albas', G.albas.length, 'stages', JSON.stringify(G.stages));

      // 4. 디버그: 알바 스탯 직접 수정
      if (!Debug.open) Debug.toggle();
      const da = G.albas[0];
      Debug.albaId = da.id;
      Debug.renderAlba();
      const hasUi = !!document.querySelector('#dbg-alba-sel') && document.querySelectorAll('#dbg-alba [data-af]').length === 10;
      const cap0 = Albas.capacity(da), sp0 = Albas.speed(da);
      Debug.setAlbaField(da, 'str', 12);
      Debug.setAlbaField(da, 'agi', 30);            // 0~20으로 제한
      Debug.setAlbaField(da, 'sk-lift', 3);
      Debug.setAlbaField(da, 'wage', 77);
      Debug.setAlbaField(da, 'mt', 500);            // MT 최대치로 제한
      Debug.setAlbaField(da, 'unpaid', true);
      log('debug alba edit ui', hasUi, 'capacity', cap0, '->', Albas.capacity(da), 'speed', sp0.toFixed(2), '->', Albas.speed(da).toFixed(2),
        'agi', da.agi, 'lift Lv', Albas.skillLv(da.skills.lift), 'wage', da.wage, 'mt', da.mt, 'unpaid', da.unpaid);
      if (!hasUi || da.str !== 12 || da.agi !== 20 || Albas.skillLv(da.skills.lift) !== 3 || da.wage !== 77 || da.mt !== CONFIG.mt.max || !da.unpaid
        || Albas.capacity(da) !== Math.min(8, 1 + 12 + 1)) { log('FAIL: debug alba edit'); ok = false; }
      Debug.toggle();
    } catch (e) {
      ok = false;
      log('ERROR', e.stack || e);
    }
    log(ok ? 'SELFTEST PASS' : 'SELFTEST FAIL');
    console.log(out.join('\n'));
    if (/[?&]night/.test(location.search)) G.phase = 'night';
    if (/[?&]dbg/.test(location.search)) Debug.toggle();
    if (/[?&]clean/.test(location.search)) {
      if (/[?&]ld/.test(location.search)) Main.demoLoading();
      return;
    }
    const pre = U.el('pre');
    pre.id = 'test-out';
    pre.textContent = out.join('\n');
    document.body.appendChild(pre);
  },

  // 스크린샷 확인용: 미니게임 화면을 박스 몇 개 실은 상태로 연다
  demoLoading() {
    G.stages.load = 3;
    const d = G.docks[0];
    d.truck = LoadLogic.createTruck();
    Object.assign(d.truck, { state: 'present', off: 0 });
    d.loader = null;
    for (const units of [3, 4, 2, 5, 1, 3, 4]) {
      const o = { id: G.seq.order++, dest: 0, items: { 0: Math.ceil(units / 2), 1: Math.floor(units / 2) }, units, reward: 40 + units * 10, table: null };
      if (!o.items[1]) delete o.items[1];
      G.orders.push(o);
      const t = { order: o, boxes: [], delivered: {} };
      Orders.pack(t);
      d.waiting.push(t.boxes[0]);
    }
    LoadingUI.open(d);
    LoadingUI.place(d.waiting[0], d.waiting[0].cells.map(([x, y]) => [x, y]), null);
    LoadingUI.render();
  },
};

Main.init();
