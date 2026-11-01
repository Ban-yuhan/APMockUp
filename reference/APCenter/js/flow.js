// 주문서, 입고 트럭, 출고 도크(트럭) 흐름

const Orders = {
  update(dt) {
    G.orderTimer -= dt;
    if (G.orderTimer <= 0) {
      Orders.spawn();
      G.orderTimer = CONFIG.orders.interval * (G.event ? CONFIG.event.intervalMult : 1);
    }
    Orders.assignTables();
  },

  spawn() {
    const c = CONFIG.orders;
    const items = G.bins.filter(b => b.unlocked).map(b => b.item);
    const docks = G.docks.filter(d => d.unlocked);
    const units = U.weighted([
      { n: 1, weight: 2 }, { n: 2, weight: 4 }, { n: 3, weight: 4 }, { n: 4, weight: 3 }, { n: 5, weight: 2 },
    ].filter(u => u.n <= c.maxUnits)).n;
    const kinds = Math.min(U.randInt(1, c.maxKinds), units, items.length);
    const chosen = U.shuffle(items.slice()).slice(0, kinds);
    const qty = {};
    for (const i of chosen) qty[i] = 1;
    for (let k = kinds; k < units; k++) qty[U.pick(chosen)]++;
    let reward = c.baseReward;
    for (const [i, q] of Object.entries(qty)) reward += CONFIG.items[+i].price * q * c.priceMult;
    G.orders.push({ id: G.seq.order++, dest: U.pick(docks).idx, items: qty, units, reward, table: null });
  },

  // 왼쪽(가장 오래된) 주문부터 빈 포장대에 배정
  assignTables() {
    for (const t of G.tables) {
      if (!t.unlocked || t.order) continue;
      const o = G.orders.find(o => o.table === null);
      if (!o) return;
      o.table = t.idx;
      t.order = o;
      t.delivered = {};
    }
  },

  // 포장 완료 → 박스 생성 (주문 1건 = 박스 1개)
  pack(t) {
    const o = t.order;
    const shape = LoadLogic.randomShape(o.units);
    const typeCount = {};
    for (const [i, q] of Object.entries(o.items)) {
      const ty = CONFIG.items[+i].type;
      typeCount[ty] = (typeCount[ty] || 0) + q;
    }
    const types = Object.keys(typeCount);
    const primary = types.reduce((a, b) => (typeCount[b] > typeCount[a] ? b : a));
    t.boxes.push({
      id: G.seq.box++, order: o.id, dest: o.dest, reward: o.reward, units: o.units,
      shape: shape.name, cells: shape.cells, types, primary, items: { ...o.items }, claimedBy: null,
    });
    G.orders.splice(G.orders.indexOf(o), 1);
    t.order = null;
    t.delivered = {};
    t.packer = null;
    t.packProgress = 0;
    t.machineProgress = 0;
  },
};

const Inbound = {
  update(dt) {
    const open = S.parkingCount();
    for (const tr of G.inTrucks) {
      if (tr.idx >= open) continue;
      if (tr.state === 'present' && tr.items.length === 0) tr.state = 'leaving';
      if (tr.state === 'leaving') {
        tr.off += 9 * dt;
        if (tr.off >= S.TRUCK_AWAY) { tr.state = 'away'; tr.timer = CONFIG.inbound.returnDelay; }
      } else if (tr.state === 'away') {
        tr.timer -= dt;
        if (tr.timer > 0) continue;
        tr.items = S.rollInboundItems();
        // 보관함이 다 차서 실을 품목이 없으면 오지 않고 잠시 뒤 다시 확인
        if (tr.items.length) { tr.state = 'arriving'; tr.off = S.TRUCK_AWAY; }
        else tr.timer = CONFIG.inbound.retryDelay;
      } else if (tr.state === 'arriving') {
        tr.off -= 9 * dt;
        if (tr.off <= 0) { tr.off = 0; tr.state = 'present'; }
      }
    }
  },
};

const Docks = {
  update(dt) {
    for (const d of G.docks) {
      if (!d.unlocked) continue;
      const tr = d.truck;
      if (!tr) {
        d.truckTimer -= dt;
        if (d.truckTimer <= 0) d.truck = LoadLogic.createTruck();
        continue;
      }
      if (tr.state === 'arriving') {
        tr.off -= 10 * dt;
        if (tr.off <= 0) { tr.off = 0; tr.state = 'present'; }
      } else if (tr.state === 'leaving') {
        tr.off += 10 * dt;
        if (tr.off >= S.TRUCK_AWAY) { d.truck = null; d.truckTimer = CONFIG.loading.truckReturnDelay; }
      } else if (tr.mode === 'auto' && tr.autoCells > 0 && !d.loader) {
        // 남은 박스가 하나도 안 들어가면 잠시 뒤 그대로 출발
        const stuck = d.waiting.length > 0 && !d.waiting.some(b => LoadLogic.autoFits(tr, b));
        d.stuckTimer = stuck ? d.stuckTimer + dt : 0;
        if (d.stuckTimer >= CONFIG.loading.stuckDepartDelay) Docks.depart(d);
      }
    }
  },

  releaseLoader(d) {
    d.loader = null;
    const tr = d.truck;
    if (tr && tr.mode === 'auto' && tr.autoCells === 0 && !S.isAuto('load')) tr.mode = null;
  },

  depart(d) {
    const tr = d.truck;
    if (!tr || tr.state !== 'present') return null;
    const p = LoadLogic.payout(tr);
    if (!p.boxes.length) return null;
    G.money += p.total;
    G.stats.earned += p.total;
    G.stats.shipped += p.boxes.length;
    G.stats.trucks++;
    const obj = p.objectives.length ? ` (🎯 ${p.objectives.filter(o => o.done).length}/${p.objectives.length} +${p.objBonus}%p)` : '';
    UI.toast(`🚛 ${CONFIG.destinations[d.idx].name} 트럭 출발! 박스 ${p.boxes.length}개 · 적재율 ${Math.round(p.rate)}%${obj} · +${U.money(p.total)}`);
    tr.state = 'leaving';
    d.loader = null;
    d.stuckTimer = 0;
    if (LoadingUI.dock === d) LoadingUI.close();
    return p;
  },
};
