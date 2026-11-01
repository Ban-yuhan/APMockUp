// 게임 상태와 공용 판정 함수

let G = null;

function createState() {
  const g = {
    time: 0,
    phase: 'day',
    phaseTime: 0,
    day: 1,
    speed: 1,
    userPaused: false,
    paused: false,           // 미니게임 패널이 열려 있으면 true
    money: CONFIG.startMoney,
    stats: { earned: 0, shipped: 0, broken: 0, trucks: 0 },

    // 품목 순서대로 보관함 1개씩. slot = 배치의 보관함 (배치에 없으면 null → 해금 불가)
    bins: CONFIG.items.map((it, i) => {
      const slot = LAYOUT.bins.find(b => b.item === it.id) || null;
      return {
        idx: i, item: i, slot, unlocked: !!slot && !slot.cost,
        unchecked: 0, checked: 0, broken: 0,
        incomingRaw: 0,        // 분류 중인(손/컨베이어) 물품 → 보관함 공간 예약
        reservedPick: 0,       // 이송 알바가 집으러 가는 중인 검수완료 재고
        inFlightPick: 0,       // 이송 컨베이어 위의 물품
        inspector: null,       // 검수 중인 대상 ('player' 또는 알바 id)
        armProgress: 0,
      };
    }),

    staging: [],             // 하차장에 쌓인 품목 idx 배열
    // 하차 주차공간마다 입고 트럭 1대. off = 주차 위치에서 떨어진 거리 (0 = 주차됨)
    inTrucks: LAYOUT.inTrucks.map((slot, i) => ({ idx: i, slot, state: 'away', items: [], timer: 0.5 + i, off: S.TRUCK_AWAY })),
    fac: { parking: 0, staging: 0, bin: 0 },   // 물류 시설 업그레이드 레벨

    tables: LAYOUT.tables.map((slot, i) => ({
      idx: i, slot, unlocked: !slot.cost, order: null, delivered: {},
      packer: null, packProgress: 0, machineProgress: 0, boxes: [],
    })),

    // 목적지 순서대로 도크 1개씩. slot = 배치의 도크 (배치에 없으면 null → 해금 불가)
    docks: CONFIG.destinations.map((dest, i) => {
      const slot = LAYOUT.docks.find(d => d.dest === dest.id) || null;
      return {
        idx: i, slot, unlocked: !!slot && !slot.cost, waiting: [], truck: null,
        truckTimer: 0.5, loader: null, autoTimer: 0, stuckTimer: 0,
      };
    }),

    orders: [],
    orderTimer: CONFIG.orders.firstDelay,
    seq: { order: 1, box: 1, alba: 1 },

    albas: [],
    candidates: [],

    stages: {},
    care: { rest: 0, purifier: -1 },
    loadUpg: { compress: 0, bonus: 0 },

    auto: { sort: 0, transfer: 0, outbound: 0 },
    flyers: [],
    event: null,
    player: null,
  };
  for (const s of CONFIG.stages) g.stages[s.id] = 1;
  return g;
}

const S = {
  TRUCK_AWAY: 15,          // 트럭이 들어오기 전/나간 뒤 주차 위치에서 떨어진 거리
  item(i) { return CONFIG.items[i]; },
  stageDef(id) { return CONFIG.stages.find(s => s.id === id); },
  stageMaxLevel(id) { return S.stageDef(id).costs.length; },
  isAuto(id) { return G.stages[id] >= 4; },
  stageSlots(id) { return S.isAuto(id) ? 0 : G.stages[id]; },
  stageCount(id) { return G.albas.filter(a => a.stage === id).length; },

  // 물류 시설 수치
  facValue(key) { const f = CONFIG.facility[key]; return f.values[G.fac[key]]; },
  parkingCount() { return S.facValue('parking'); },
  stagingCap() { return S.facValue('staging'); },
  binCap() { return S.facValue('bin'); },

  // 입고 가능 수량 = 보관함 최대 보관량 - (보관함 + 하차장 + 트럭/손에 있는 미검수 물품)
  // 0 이하면 그 품목은 더 이상 배송되지 않는다
  inboundRoom(item) {
    const b = G.bins[item];
    let n = b.unchecked + b.checked + b.incomingRaw;
    for (const it of G.staging) if (it === item) n++;
    for (const tr of G.inTrucks) for (const it of tr.items) if (it === item) n++;
    for (const a of S.actors()) for (const e of a.hand) if (e.kind === 'raw' && !e.reserved && e.item === item) n++;
    return S.binCap() - n;
  },

  // 섞여서 오되, 입고 가능 수량이 남은 품목만, 많이 남은 품목일수록 많이 온다
  rollInboundItems() {
    const room = {};
    for (const b of G.bins) if (b.unlocked) room[b.item] = S.inboundRoom(b.item);
    const n = U.randInt(CONFIG.inbound.minItems, CONFIG.inbound.maxItems);
    const out = [];
    for (let i = 0; i < n; i++) {
      const pool = Object.entries(room).filter(([, r]) => r > 0).map(([it, r]) => ({ item: +it, weight: r }));
      if (!pool.length) break;
      const it = U.weighted(pool).item;
      room[it]--;
      out.push(it);
    }
    return out;
  },


  // 보관함
  binSpace(b) { return S.binCap() - b.unchecked - b.checked - b.incomingRaw; },
  avail(b) { return b.checked - b.reservedPick; },

  // 포장대
  remainingNeed(t, item) {
    if (!t.order) return 0;
    return (t.order.items[item] || 0) - (t.delivered[item] || 0);
  },
  tableReady(t) {
    if (!t.order) return false;
    return Object.keys(t.order.items).every(k => S.remainingNeed(t, +k) <= 0);
  },
  activeTables() {
    return G.tables.filter(t => t.unlocked && t.order).sort((a, b) => a.order.id - b.order.id);
  },

  // 손에 든 물건
  actors() { return [G.player, ...G.albas]; },
  handPickCount(item) {
    let n = 0;
    for (const a of S.actors()) for (const e of a.hand) if (e.kind === 'pick' && e.item === item) n++;
    return n;
  },
  // 아직 아무도 가져가지 않은 필요 수량 (음수면 과잉)
  outstandingNeed(item) {
    let need = 0;
    for (const t of G.tables) need += Math.max(0, S.remainingNeed(t, item));
    const b = G.bins[item];
    return need - S.handPickCount(item) - b.reservedPick - b.inFlightPick;
  },
  hasKind(actor, kind) { return actor.hand.some(e => e.kind === kind); },
  canTake(actor, kind, cap) {
    return actor.hand.length < cap && actor.hand.every(e => e.kind === kind);
  },

  // MT
  mtMult(mt) { return CONFIG.mt.effect.find(e => mt >= e.min).mult; },
  restChance(mt) { return CONFIG.mt.restChance.find(e => mt >= e.min).p; },

  // 위치 (배치 데이터의 슬롯 기준)
  pos(o) { return { x: o.x, z: o.z }; },
  binPos(b) { return S.pos(b.slot); },
  binSlotIdx(b) { return LAYOUT.bins.indexOf(b.slot); },
  dockSlotIdx(d) { return LAYOUT.docks.indexOf(d.slot); },
  binIn(b) { return LayoutLib.pt(b.slot, 'in'); },
  binOut(b) { return LayoutLib.pt(b.slot, 'out'); },
  binInspect(b) { return LayoutLib.pt(b.slot, 'inspect'); },
  tablePos(t) { return S.pos(t.slot); },
  tableIn(t) { return LayoutLib.pt(t.slot, 'in'); },
  tableOut(t) { return LayoutLib.pt(t.slot, 'out'); },
  dockPos(d) { return S.pos(d.slot); },
  dockIn(d) { return LayoutLib.pt(d.slot, 'in'); },
  stagingIn() { return LayoutLib.pt(LAYOUT.staging, 'in'); },
  stagingOut() { return LayoutLib.pt(LAYOUT.staging, 'out'); },
  inTruckOut(tr) { return LayoutLib.pt(tr.slot, 'out'); },
  trashIn() { return LayoutLib.pt(LAYOUT.trash, 'in'); },
  // 트럭 위치: 주차 위치에서 뒷문 반대 방향으로 off만큼 떨어진 곳
  truckPos(park, off) {
    const d = LayoutLib.dir(park.rot);
    return { x: park.x - d.x * off, z: park.z - d.z * off };
  },
  // 트럭 뒷문 앞 (상차 기계손이 박스를 넣는 곳)
  truckDoor(park) {
    const d = LayoutLib.dir(park.rot);
    return { x: park.x + d.x * 2.4, z: park.z + d.z * 2.4 };
  },
  // 사각형 구역 안에 겹치지 않게 자리 나누기
  rectSpot(r, i, n) {
    const cols = Math.max(1, Math.round(Math.sqrt(n * r.w / r.d)));
    const rows = Math.ceil(n / cols);
    const k = i % n;
    return {
      x: r.x - r.w / 2 + (k % cols + 0.5) * r.w / cols,
      z: r.z - r.d / 2 + (Math.floor(k / cols) + 0.5) * r.d / rows,
    };
  },
  restSpot(a) { return S.rectSpot(LAYOUT.rest, (a.id * 7) % 9, 9); },
  waitSpot(a) { return S.rectSpot(LAYOUT.waiting, a.id % 8, 8); },

  addMoney(n) { G.money += n; },
  spend(n) {
    if (G.money < n) { UI.toast('💸 돈이 부족합니다'); return false; }
    G.money -= n;
    return true;
  },
};
