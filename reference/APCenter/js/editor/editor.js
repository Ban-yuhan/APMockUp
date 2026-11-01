// 배치 시뮬레이터: 배치 데이터 편집 동작, 되돌리기, 저장/내보내기
//
// 선택 참조(ref)는 { t: 종류, i: 번호 } 형태라서 되돌리기로 데이터가 바뀌어도 그대로 쓸 수 있다.
//   t: area, wall, inTruck, staging, bin, table, dock, dockTruck, trash, rest, waiting,
//      purifier, entrance, playerStart, idle(st), point(of, role), port(of, port),
//      line(st, id), lpt(st, id, k: 라인의 k번째 점)

const ED_ROLES = { inTruck: ['out'], staging: ['in', 'out'], bin: ['in', 'out', 'inspect'], table: ['in', 'out'], dock: ['in'], trash: ['in'] };
const ED_ROLE_LABEL = { in: '넣기', out: '꺼내기', inspect: '검수' };
const ED_AREA_COLORS = ['#f1e2a4', '#cfe0f0', '#d5ecd2', '#f5d9c0', '#e6d3f0', '#e0e0e0'];
const ED_KEYS = { autosave: 'apc-editor-current', saves: 'apc-editor-saves' };
const ED_LIST = { area: 'areas', wall: 'walls', inTruck: 'inTrucks', bin: 'bins', table: 'tables', dock: 'docks' };
const ATTACH_SNAP = 0.6;   // 벨트 끝점을 포트/라인에 붙이는 거리

const r2 = v => Math.round(v * 100) / 100;

const Ed = {
  L: null,
  sel: null,
  mode: 'select',          // 'select' | 'belt:sort' | 'belt:transfer' | 'belt:outbound'
  draw: null,              // 벨트 그리기 중: { st, from, points }
  snap: 0.5,
  show: { belts: true, ranges: true, labels: true },
  undoStack: [],
  redoStack: [],
  issues: [],

  init() {
    this.L = this.startLayout();
    View.init(document.getElementById('cv'));
    Panels.init();
    this.afterChange();
    View.fit();
  },

  // 시작 배치: 자동 저장본 → layout-data.js → 기본 배치
  startLayout() {
    try {
      const s = localStorage.getItem(ED_KEYS.autosave);
      if (s) { this.startFrom = '마지막 작업 (자동 저장)'; return this.sanitize(JSON.parse(s)); }
    } catch (e) { /* 저장소를 못 쓰면 무시 */ }
    if (window.APC_LAYOUT) { this.startFrom = '게임 배치 (layout-data.js)'; return this.sanitize(LayoutLib.clone(window.APC_LAYOUT)); }
    this.startFrom = '기본 배치';
    return LayoutLib.makeDefault();
  },

  // 빠진 항목 채우기 (예전 데이터나 손으로 고친 파일 대비)
  sanitize(l) {
    l = LayoutLib.upgrade(l);
    if (l._upgraded) { delete l._upgraded; this.startFrom = (this.startFrom || '') + ' · 이전 버전 벨트는 자동 생성 초안으로 바뀜'; }
    const d = LayoutLib.makeDefault();
    for (const k of Object.keys(d)) if (l[k] === undefined) l[k] = LayoutLib.clone(d[k]);
    for (const k of ['areas', 'walls', 'inTrucks', 'bins', 'tables', 'docks']) if (!Array.isArray(l[k])) l[k] = [];
    l.idle = { ...d.idle, ...(l.idle || {}) };
    const fill = (o, keys) => { for (const r of keys) if (!o[r]) o[r] = { x: o.x, z: o.z }; };
    l.inTrucks.forEach(o => { o.rot = o.rot || 0; fill(o, ED_ROLES.inTruck); });
    fill(l.staging, ED_ROLES.staging.concat(LayoutLib.PORTS.staging));
    l.bins.forEach(o => { o.rot = o.rot || 0; fill(o, ED_ROLES.bin.concat(LayoutLib.PORTS.bin)); });
    l.tables.forEach(o => { o.rot = o.rot || 0; fill(o, ED_ROLES.table.concat(LayoutLib.PORTS.table)); });
    l.docks.forEach(o => { fill(o, ED_ROLES.dock.concat(LayoutLib.PORTS.dock)); if (!o.truck) o.truck = { x: o.x + 6.4, z: o.z, rot: 180 }; });
    fill(l.trash, ED_ROLES.trash);
    l.belts = l.belts || {};
    for (const st of LayoutLib.BELT_STAGES) if (!l.belts[st] || !Array.isArray(l.belts[st].lines)) l.belts[st] = { lines: [] };
    l.version = LayoutLib.VERSION;
    return l;
  },

  // ---------- 참조 ----------
  get(ref, L = this.L) {
    if (!ref) return null;
    switch (ref.t) {
      case 'area': case 'wall': case 'inTruck': case 'bin': case 'table': case 'dock': return L[ED_LIST[ref.t]][ref.i];
      case 'dockTruck': return L.docks[ref.i] && L.docks[ref.i].truck;
      case 'idle': return L.idle[ref.st];
      case 'point': { const o = this.get(ref.of, L); return o && o[ref.role]; }
      case 'port': { const o = this.get(ref.of, L); return o && o[ref.port]; }
      case 'line': return L.belts[ref.st].lines.find(n => n.id === ref.id);
      case 'lpt': { const ln = L.belts[ref.st].lines.find(n => n.id === ref.id); return ln && ln.points[ref.k]; }
      default: return L[ref.t];
    }
  },

  same(a, b) { return !!a && !!b && JSON.stringify(a) === JSON.stringify(b); },

  name(ref) {
    const o = this.get(ref);
    switch (ref.t) {
      case 'area': return `구역: ${o.name}`;
      case 'wall': return `벽 ${ref.i + 1}`;
      case 'inTruck': return `하차 주차공간 ${ref.i + 1}`;
      case 'staging': return '하차장';
      case 'bin': { const it = LayoutLib.item(this.L, o.item); return `보관함 ${ref.i + 1} (${it ? it.emoji + ' ' + it.name : o.item})`; }
      case 'table': return `포장대 ${ref.i + 1}`;
      case 'dock': { const de = CONFIG.destinations.find(x => x.id === o.dest); return `도크 ${ref.i + 1} (${de ? de.name : o.dest})`; }
      case 'dockTruck': return `도크 ${ref.i + 1} 트럭 주차 위치`;
      case 'trash': return '쓰레기통';
      case 'rest': return '휴게실';
      case 'waiting': return '알바 대기 구역';
      case 'purifier': return '공기청정기 자리';
      case 'entrance': return '알바 출입구';
      case 'playerStart': return '플레이어 시작 위치';
      case 'idle': return `알바 대기 지점: ${S_STAGE_NAME(ref.st)}`;
      case 'point': return `${this.name(ref.of)} · ${ED_ROLE_LABEL[ref.role]} 지점`;
      case 'port': return `${this.name(ref.of)} · ${LayoutLib.PORT_NAMES[ref.port]}`;
      case 'line': return `${LayoutLib.BELT_NAMES[ref.st]} 라인 ${ref.id}`;
      case 'lpt': {
        const ln = this.get({ t: 'line', st: ref.st, id: ref.id });
        const kind = ref.k === 0 ? '출발점' : ref.k === ln.points.length - 1 ? '도착점' : '꺾이는 점';
        return `${LayoutLib.BELT_NAMES[ref.st]} 라인 ${ref.id} · ${kind}`;
      }
    }
    return ref.t;
  },

  // ---------- 변경 / 되돌리기 ----------
  snapshot() { return JSON.stringify(this.L); },
  pushUndo(snap) {
    this.undoStack.push(snap || this.snapshot());
    if (this.undoStack.length > 150) this.undoStack.shift();
    this.redoStack = [];
  },
  change(fn) {
    this.pushUndo();
    fn(this.L);
    this.afterChange();
  },
  afterChange() {
    this.syncBelts(this.L);
    if (this.sel && !this.get(this.sel)) this.sel = null;
    this.issues = LayoutLib.validate(this.L);
    try { localStorage.setItem(ED_KEYS.autosave, this.snapshot()); } catch (e) { /* 무시 */ }
    Panels.refresh();
    View.render();
  },
  undo() {
    if (!this.undoStack.length) return;
    this.draw = null;
    this.redoStack.push(this.snapshot());
    this.L = JSON.parse(this.undoStack.pop());
    this.afterChange();
  },
  redo() {
    if (!this.redoStack.length) return;
    this.draw = null;
    this.undoStack.push(this.snapshot());
    this.L = JSON.parse(this.redoStack.pop());
    this.afterChange();
  },

  select(ref) {
    this.sel = ref;
    Panels.refresh();
    View.render();
  },

  setMode(mode) {
    this.mode = mode;
    this.draw = null;
    const st = this.beltStage();
    const keep = this.sel && (st ? ['line', 'lpt', 'port'].includes(this.sel.t) && (this.sel.st === st || this.sel.t === 'port') : !['line', 'lpt', 'port'].includes(this.sel.t));
    if (!keep) this.sel = null;
    Panels.refresh();
    View.render();
  },

  snapV(v) { return this.snap > 0 ? r2(Math.round(v / this.snap) * this.snap) : r2(v); },

  // ---------- 이동 (드래그 시작 시점의 배치 base 기준으로 다시 계산) ----------
  mainPos(ref, L) {
    const o = this.get(ref, L);
    if (ref.t === 'area' || ref.t === 'wall') return { x: o.x1, z: o.z1 };
    return { x: o.x, z: o.z };
  },

  // 구역 안에 있는 것들 (구역을 옮길 때 같이 옮긴다)
  contents(L, a) {
    const inside = p => p.x >= a.x1 && p.x <= a.x2 && p.z >= a.z1 && p.z <= a.z2;
    const out = [];
    L.inTrucks.forEach((o, i) => inside(o) && out.push({ t: 'inTruck', i }));
    if (inside(L.staging)) out.push({ t: 'staging' });
    L.bins.forEach((o, i) => inside(o) && out.push({ t: 'bin', i }));
    L.tables.forEach((o, i) => inside(o) && out.push({ t: 'table', i }));
    L.docks.forEach((o, i) => inside(o) && out.push({ t: 'dock', i }));
    for (const t of ['trash', 'rest', 'waiting', 'purifier', 'entrance', 'playerStart']) if (inside(L[t])) out.push({ t });
    for (const st of Object.keys(L.idle)) if (inside(L.idle[st])) out.push({ t: 'idle', st });
    L.walls.forEach((w, i) => inside({ x: w.x1, z: w.z1 }) && inside({ x: w.x2, z: w.z2 }) && out.push({ t: 'wall', i }));
    for (const st of LayoutLib.BELT_STAGES) {
      for (const ln of L.belts[st].lines) ln.points.forEach((p, k) => inside(p) && out.push({ t: 'lpt', st, id: ln.id, k }));
    }
    return out;
  },

  shift(o, dx, dz) { o.x = r2(o.x + dx); o.z = r2(o.z + dz); },

  // ref를 (dx, dz)만큼 옮긴다. 시설은 상호작용 지점과 벨트 포트, 도크는 트럭 주차 위치도 같이 옮긴다
  moveBy(L, ref, dx, dz) {
    const o = this.get(ref, L);
    if (!o) return;
    if (ref.t === 'area' || ref.t === 'wall') {
      o.x1 = r2(o.x1 + dx); o.x2 = r2(o.x2 + dx); o.z1 = r2(o.z1 + dz); o.z2 = r2(o.z2 + dz);
      return;
    }
    this.shift(o, dx, dz);
    for (const k of (ED_ROLES[ref.t] || []).concat(LayoutLib.PORTS[ref.t] || [])) if (o[k]) this.shift(o[k], dx, dz);
    if (ref.t === 'dock') this.shift(o.truck, dx, dz);
  },

  dragMove(base, ref, dx, dz, opts) {
    const L = LayoutLib.clone(base);
    if (ref.t === 'point') {
      const owner = this.get(ref.of, L), p = owner[ref.role], bp = this.get(ref, base);
      let x = bp.x + dx, z = bp.z + dz;
      // 같은 시설의 다른 지점이나 시설 중심 근처면 딱 붙인다 (지점 합치기)
      const cands = ED_ROLES[ref.of.t].filter(r => r !== ref.role).map(r => owner[r]).concat([{ x: owner.x, z: owner.z }]);
      const hit = cands.find(c => Math.hypot(c.x - x, c.z - z) < 0.45);
      if (hit) { x = hit.x; z = hit.z; } else { x = this.snapV(x); z = this.snapV(z); }
      p.x = r2(x); p.z = r2(z);
    } else if (ref.t === 'port') {
      const p = this.get(ref, L), bp = this.get(ref, base);
      p.x = this.snapV(bp.x + dx); p.z = this.snapV(bp.z + dz);
    } else if (ref.t === 'lpt') {
      const ln = this.get({ t: 'line', st: ref.st, id: ref.id }, L), bp = this.get(ref, base);
      const pos = { x: bp.x + dx, z: bp.z + dz };
      const isStart = ref.k === 0, isEnd = ref.k === ln.points.length - 1;
      if (isStart || isEnd) {
        const att = this.findAttach(L, ref.st, pos, isStart ? 'src' : 'dst', ln.id);
        ln[isStart ? 'from' : 'to'] = att ? att.a : null;
        const q = att ? att.p : { x: this.snapV(pos.x), z: this.snapV(pos.z) };
        ln.points[ref.k] = { x: r2(q.x), z: r2(q.z) };
      } else {
        ln.points[ref.k] = { x: this.snapV(pos.x), z: this.snapV(pos.z) };
      }
    } else {
      const m = this.mainPos(ref, base);
      const ex = this.snapV(m.x + dx) - m.x, ez = this.snapV(m.z + dz) - m.z;
      this.moveBy(L, ref, ex, ez);
      if (ref.t === 'area' && !opts.alone) {
        for (const r of this.contents(base, this.get(ref, base))) {
          if (r.t === 'lpt') this.shift(this.get(r, L), ex, ez);
          else this.moveBy(L, r, ex, ez);
        }
      }
    }
    this.L = L;
  },

  // 크기 조절 핸들: 사각형(area, interior, rest, waiting)의 모서리, 벽의 끝점
  dragHandle(base, ref, handle, x, z) {
    const L = LayoutLib.clone(base);
    x = this.snapV(x); z = this.snapV(z);
    const o = ref.t === 'interior' ? L.interior : this.get(ref, L);
    if (ref.t === 'wall') {
      if (handle === 0) { o.x1 = x; o.z1 = z; } else { o.x2 = x; o.z2 = z; }
    } else if (ref.t === 'rest' || ref.t === 'waiting') {
      const r = { x1: o.x - o.w / 2, z1: o.z - o.d / 2, x2: o.x + o.w / 2, z2: o.z + o.d / 2 };
      this.setCorner(r, handle, x, z);
      o.w = r2(Math.max(1, r.x2 - r.x1)); o.d = r2(Math.max(1, r.z2 - r.z1));
      o.x = r2((r.x1 + r.x2) / 2); o.z = r2((r.z1 + r.z2) / 2);
    } else {
      this.setCorner(o, handle, x, z);
    }
    this.L = L;
  },

  // handle: 0 = 왼위, 1 = 오른위, 2 = 오른아래, 3 = 왼아래. 최소 크기 1칸
  setCorner(r, h, x, z) {
    if (h === 0 || h === 3) r.x1 = Math.min(x, r.x2 - 1); else r.x2 = Math.max(x, r.x1 + 1);
    if (h === 0 || h === 1) r.z1 = Math.min(z, r.z2 - 1); else r.z2 = Math.max(z, r.z1 + 1);
  },

  // ---------- 회전 (90도씩, 시설 중심 기준. 지점·포트·도크 트럭도 같이 돈다) ----------
  rotPoint(p, c) {
    const dx = p.x - c.x, dz = p.z - c.z;
    p.x = r2(c.x - dz); p.z = r2(c.z + dx);
  },
  rotate(ref) {
    if (!ref) return;
    const t = ref.t;
    if (!(ED_ROLES[t] || ['dockTruck', 'rest', 'waiting', 'area'].includes(t))) return;
    this.change(L => this.rotateIn(this.get(ref, L), ref));
  },

  // 객체 o(ref 종류)를 제자리에서 90도 돌린다
  rotateIn(o, ref) {
    const t = ref.t;
    if (t === 'rest' || t === 'waiting') { [o.w, o.d] = [o.d, o.w]; return; }
    if (t === 'area') {
      const cx = (o.x1 + o.x2) / 2, cz = (o.z1 + o.z2) / 2, hw = (o.x2 - o.x1) / 2, hd = (o.z2 - o.z1) / 2;
      Object.assign(o, { x1: r2(cx - hd), x2: r2(cx + hd), z1: r2(cz - hw), z2: r2(cz + hw) });
      return;
    }
    o.rot = ((o.rot || 0) + 90) % 360;
    for (const k of (ED_ROLES[t] || []).concat(LayoutLib.PORTS[t] || [])) if (o[k]) this.rotPoint(o[k], o);
    if (t === 'dock') { this.rotPoint(o.truck, o); o.truck.rot = (o.truck.rot + 90) % 360; }
  },

  // ---------- 추가 / 삭제 / 복제 ----------
  freeItems(L = this.L) { const used = new Set(L.bins.map(b => b.item)); return L.items.filter(i => !used.has(i.id)); },

  // ---------- 품목 ----------
  // 새 품목 (보관함 1개 = 품목 1종이라 보관함을 늘리려면 품목도 늘려야 한다)
  newItem(L) {
    let n = L.items.length + 1;
    while (L.items.some(it => it.id === `item${n}`)) n++;
    const it = { id: `item${n}`, name: `새 품목 ${n}`, emoji: '📦', type: Object.keys(CONFIG.types)[1] || 'food', price: 10 };
    L.items.push(it);
    return it;
  },

  addItem() {
    this.change(L => this.newItem(L));
    Panels.toast('🏷️ 품목 추가 (보관함은 아직 없음)');
  },

  // 품목 삭제: 그 품목의 보관함도 같이 지운다
  removeItem(id) {
    const it = LayoutLib.item(this.L, id);
    const bi = this.L.bins.findIndex(b => b.item === id);
    if (!it) return;
    if (bi >= 0 && !confirm(`'${it.name}' 품목을 지우면 보관함 ${bi + 1}도 같이 지워집니다. 계속할까요?`)) return;
    this.change(L => {
      const i = L.bins.findIndex(b => b.item === id);
      if (i >= 0) {
        L.bins.splice(i, 1);
        this.fixAttach(L, 'bin', j => (j === i ? -1 : j > i ? j - 1 : j));
      }
      L.items = L.items.filter(x => x.id !== id);
    });
    if (this.sel && this.sel.t === 'bin') this.select(null);
  },

  // 보관함에 품목 새로 만들어 붙이기
  newItemForBin(ref) {
    this.change(L => {
      const b = this.get(ref, L);
      b.item = this.newItem(L).id;
    });
  },
  freeDests() { const used = new Set(this.L.docks.map(d => d.dest)); return CONFIG.destinations.filter(d => !used.has(d.id)); },

  // 앞면 방향으로 dist만큼, 옆 방향으로 side만큼 떨어진 지점
  front(o, dist, side = 0) {
    const d = LayoutLib.dir(o.rot || 0);
    return { x: r2(o.x + d.x * dist - d.z * side), z: r2(o.z + d.z * dist + d.x * side) };
  },

  // itemId: 보관함을 만들 품목 (없으면 남는 품목, 그것도 없으면 새 품목)
  add(kind, itemId) {
    const c = View.centerWorld(), x = this.snapV(c.x), z = this.snapV(c.z);
    let ref = null;
    this.change(L => {
      switch (kind) {
        case 'area':
          L.areas.push({ name: '새 구역', x1: x - 4, z1: z - 3, x2: x + 4, z2: z + 3, color: ED_AREA_COLORS[L.areas.length % ED_AREA_COLORS.length] });
          ref = { t: 'area', i: L.areas.length - 1 };
          break;
        case 'wall':
          L.walls.push({ x1: x - 3, z1: z, x2: x + 3, z2: z });
          ref = { t: 'wall', i: L.walls.length - 1 };
          break;
        case 'inTruck': {
          const o = { x, z, rot: 0 };
          o.out = this.front(o, 3.8);
          L.inTrucks.push(o);
          ref = { t: 'inTruck', i: L.inTrucks.length - 1 };
          break;
        }
        case 'bin': {
          // 남는 품목이 없으면 새 품목을 만든다
          const it = (itemId && this.freeItems(L).find(x => x.id === itemId)) || this.freeItems(L)[0] || this.newItem(L);
          const o = { x, z, rot: 90, item: it.id, cost: LayoutLib.BASE_COST[it.id] !== undefined ? LayoutLib.BASE_COST[it.id] : 300 };
          o.in = this.front(o, 2.2); o.out = { ...o.in }; o.inspect = { ...o.in };
          o.beltIn = this.front(o, 0, -0.35); o.beltOut = this.front(o, 0, 0.35);
          L.bins.push(o);
          ref = { t: 'bin', i: L.bins.length - 1 };
          break;
        }
        case 'table': {
          const o = { x, z, rot: 180, cost: CONFIG.packTableCosts[L.tables.length] !== undefined ? CONFIG.packTableCosts[L.tables.length] : 600 };
          o.in = this.front(o, 2.2); o.out = { ...o.in };
          o.beltIn = this.front(o, 0.3); o.beltOut = this.front(o, -0.3);
          L.tables.push(o);
          ref = { t: 'table', i: L.tables.length - 1 };
          break;
        }
        case 'dock': {
          const de = this.freeDests()[0];
          if (!de) return;
          L.docks.push({ x, z, dest: de.id, cost: de.cost, in: { x: r2(x - 0.8), z }, beltIn: { x, z }, truck: { x: r2(x + 6.4), z, rot: 180 } });
          ref = { t: 'dock', i: L.docks.length - 1 };
          break;
        }
      }
    });
    if (ref) this.select(ref);
  },

  canDelete(ref) { return !!ref && ['area', 'wall', 'inTruck', 'bin', 'table', 'dock', 'line', 'lpt'].includes(ref.t); },

  // 시설 번호가 바뀌면 벨트가 붙어 있던 번호도 고친다 (map: 옛 번호 → 새 번호, -1이면 떼기)
  fixAttach(L, fac, map) {
    for (const st of LayoutLib.BELT_STAGES) for (const ln of L.belts[st].lines) {
      for (const end of ['from', 'to']) {
        const a = ln[end];
        if (!a || a.fac !== fac) continue;
        const ni = map(a.i);
        if (ni < 0) ln[end] = null; else a.i = ni;
      }
    }
  },

  remove(ref) {
    if (!this.canDelete(ref)) return;
    if (ref.t === 'lpt') { this.removeLinePoint(ref); return; }
    this.change(L => {
      if (ref.t === 'line') {
        const g = L.belts[ref.st];
        g.lines = g.lines.filter(n => n.id !== ref.id);
        // 이 라인에 붙어 있던 끝점은 떨어진 상태로
        for (const ln of g.lines) for (const end of ['from', 'to']) if (ln[end] && ln[end].line === ref.id) ln[end] = null;
        return;
      }
      L[ED_LIST[ref.t]].splice(ref.i, 1);
      if (LayoutLib.PORTS[ref.t]) this.fixAttach(L, ref.t, j => (j === ref.i ? -1 : j > ref.i ? j - 1 : j));
    });
    this.select(null);
  },

  duplicate(ref) {
    if (!ref || !ED_LIST[ref.t]) return;
    if (ref.t === 'dock' && !this.freeDests().length) { Panels.toast('모든 목적지에 도크가 있습니다'); return; }
    let nref = null;
    this.change(L => {
      const list = ED_LIST[ref.t], copy = LayoutLib.clone(L[list][ref.i]);
      L[list].push(copy);
      nref = { t: ref.t, i: L[list].length - 1 };
      this.moveBy(L, nref, 2, 2);
      if (ref.t === 'bin') copy.item = (this.freeItems(L)[0] || this.newItem(L)).id;
      if (ref.t === 'dock') { const de = this.freeDests()[0]; copy.dest = de.id; copy.cost = de.cost; }
      if (ref.t === 'area') copy.name += ' 사본';
    });
    this.select(nref);
  },

  // 상호작용 지점을 모두 첫 번째 지점 위치로 합치기
  mergePoints(ref) {
    const roles = ED_ROLES[ref.t];
    if (!roles) return;
    this.change(L => {
      const o = this.get(ref, L), p = { ...o[roles[0]] };
      for (const r of roles) o[r] = { ...p };
    });
  },

  // 모든 지점을 시설 앞 기본 위치로
  resetPoints(ref) {
    const roles = ED_ROLES[ref.t];
    if (!roles) return;
    const dist = { inTruck: 3.8, staging: 0, bin: 2.2, table: 2.2, dock: 0, trash: 0 }[ref.t];
    this.change(L => {
      const o = this.get(ref, L);
      const p = ref.t === 'dock' ? { x: r2(o.x - LayoutLib.dir(o.truck.rot).x * 0.8), z: r2(o.z - LayoutLib.dir(o.truck.rot).z * 0.8) }
        : dist ? this.front(o, dist) : { x: o.x, z: o.z };
      for (const r of roles) o[r] = { ...p };
    });
  },

  // 순서 바꾸기 (하차 주차공간은 업그레이드 순서). 벨트가 붙은 번호도 같이 바꾼다
  moveOrder(ref, dir) {
    const list = ED_LIST[ref.t], j = ref.i + dir;
    if (!list || j < 0 || j >= this.L[list].length) return;
    this.change(L => {
      [L[list][ref.i], L[list][j]] = [L[list][j], L[list][ref.i]];
      if (LayoutLib.PORTS[ref.t]) this.fixAttach(L, ref.t, k => (k === ref.i ? j : k === j ? ref.i : k));
    });
    this.select({ t: ref.t, i: j });
  },

  // ---------- 벨트 ----------
  beltStage() { return this.mode.startsWith('belt:') ? this.mode.slice(5) : null; },
  nextLineId(st, L = this.L) { return L.belts[st].lines.reduce((m, n) => Math.max(m, n.id), 0) + 1; },

  // 붙은 끝점 위치 맞추기 (포트가 움직였거나 붙은 라인이 움직였을 때)
  syncBelts(L) {
    for (const st of LayoutLib.BELT_STAGES) {
      const g = L.belts[st];
      g.lines = g.lines.filter(ln => ln.points && ln.points.length >= 2);
      for (const ln of g.lines) {
        for (const end of ['from', 'to']) {
          const a = ln[end];
          if (!a) continue;
          if (a.fac && !LayoutLib.port(L, a.fac, a.i, a.port)) ln[end] = null;
          if (a.line !== undefined && !g.lines.some(x => x.id === a.line && x !== ln)) ln[end] = null;
        }
      }
      for (const ln of g.lines) {
        const pts = LayoutLib.linePts(L, st, ln);
        ln.points[0] = { x: r2(pts[0].x), z: r2(pts[0].z) };
        ln.points[ln.points.length - 1] = { x: r2(pts[pts.length - 1].x), z: r2(pts[pts.length - 1].z) };
      }
    }
  },

  // 공정에 맞는 포트 목록 (kind: 'src' = 출발에 쓰는 포트, 'dst' = 도착에 쓰는 포트)
  stagePorts(L, st, kind) {
    const [fac, port] = LayoutLib.STAGE_PORTS[st][kind];
    return LayoutLib.facList(L, fac).map((o, i) => ({ a: { fac, i, port }, p: LayoutLib.pt(o, port) }));
  },

  // 끝점을 붙일 곳 찾기: 맞는 포트 → 다른 라인 위
  findAttach(L, st, pos, kind, selfId) {
    let best = null, bd = ATTACH_SNAP;
    for (const c of this.stagePorts(L, st, kind)) {
      const d = LayoutLib.dist(c.p, pos);
      if (d < bd) { bd = d; best = { a: c.a, p: c.p }; }
    }
    if (best) return best;
    for (const ln of L.belts[st].lines) {
      if (ln.id === selfId) continue;
      const pr = LayoutLib.projectOn(LayoutLib.linePts(L, st, ln), pos);
      if (pr && pr.dist < bd) { bd = pr.dist; best = { a: { line: ln.id }, p: pr.point }; }
    }
    return best;
  },

  // 그리기 시작: from = 시설 포트 / 다른 라인 / null(떨어진 점)
  startDraw(st, p, from) {
    this.draw = { st, from, points: [{ x: r2(p.x), z: r2(p.z) }] };
    this.sel = null;
    Panels.refresh();
    View.render();
  },

  // 그리는 중 클릭: 맞는 도착 포트나 다른 라인이면 붙이고 끝, 아니면 꺾이는 점 추가
  drawClick(x, z, shift) {
    const d = this.draw;
    const att = this.findAttach(this.L, d.st, { x, z }, 'dst', null);
    if (att) { d.points.push({ x: r2(att.p.x), z: r2(att.p.z) }); this.finishDraw(att.a); return; }
    let px = this.snapV(x), pz = this.snapV(z);
    const last = d.points[d.points.length - 1];
    if (shift) { if (Math.abs(px - last.x) > Math.abs(pz - last.z)) pz = last.z; else px = last.x; }
    if (px === last.x && pz === last.z) return;
    d.points.push({ x: px, z: pz });
    View.render();
  },

  // 그리기 끝 (to = 붙일 곳, 없으면 떨어진 끝)
  finishDraw(to) {
    const d = this.draw;
    this.draw = null;
    if (!d || d.points.length < 2) { View.render(); return; }
    let id = null;
    this.change(L => {
      id = this.nextLineId(d.st, L);
      L.belts[d.st].lines.push({ id, points: d.points, from: d.from, to: to || null });
    });
    this.select({ t: 'line', st: d.st, id });
  },

  cancelDraw() { this.draw = null; Panels.refresh(); View.render(); },

  undoDrawPoint() {
    if (!this.draw) return;
    this.draw.points.pop();
    if (!this.draw.points.length) { this.cancelDraw(); return; }
    View.render();
  },

  // 선택한 라인의 구간에 꺾이는 점 넣기
  insertBend(st, id, seg, p) {
    let ref = null;
    this.change(L => {
      const ln = L.belts[st].lines.find(n => n.id === id);
      const k = Math.min(seg + 1, ln.points.length - 1);
      ln.points.splice(k, 0, { x: this.snapV(p.x), z: this.snapV(p.z) });
      ref = { t: 'lpt', st, id, k };
    });
    this.select(ref);
  },

  // 꺾이는 점은 삭제, 출발점/도착점은 떼기
  removeLinePoint(ref) {
    this.sel = { t: 'line', st: ref.st, id: ref.id };
    this.change(L => {
      const ln = L.belts[ref.st].lines.find(n => n.id === ref.id);
      const last = ln.points.length - 1;
      if (ref.k === 0) ln.from = null;
      else if (ref.k === last) ln.to = null;
      else ln.points.splice(ref.k, 1);
    });
  },

  reverseLine(ref) {
    this.change(L => {
      const ln = L.belts[ref.st].lines.find(n => n.id === ref.id);
      ln.points.reverse();
      [ln.from, ln.to] = [ln.to, ln.from];
    });
  },

  detach(ref, end) {
    this.change(L => { L.belts[ref.st].lines.find(n => n.id === ref.id)[end] = null; });
  },

  autoBelt(st) {
    if (this.L.belts[st].lines.length && !confirm(`${LayoutLib.BELT_NAMES[st]} 벨트를 자동 생성 초안으로 바꿀까요? (되돌리기 가능)`)) return;
    this.change(L => { L.belts[st].lines = LayoutLib.autoLines(L, st); });
    Panels.toast(`${LayoutLib.BELT_NAMES[st]} 벨트 자동 생성: 메인 라인 1개 + 지선. 하단 경고로 끊긴 연결을 확인하세요`);
  },

  clearBelt(st) {
    if (!confirm(`${LayoutLib.BELT_NAMES[st]} 벨트를 모두 지울까요?`)) return;
    this.draw = null;
    this.change(L => { L.belts[st].lines = []; });
  },

  // ---------- 저장 / 불러오기 / 내보내기 ----------
  saves() {
    try { return JSON.parse(localStorage.getItem(ED_KEYS.saves) || '{}'); } catch (e) { return {}; }
  },
  saveAs(name) {
    if (!name) return;
    const all = this.saves();
    all[name] = { at: new Date().toLocaleString('ko-KR'), layout: LayoutLib.clone(this.L) };
    all[name].layout.name = name;
    try { localStorage.setItem(ED_KEYS.saves, JSON.stringify(all)); } catch (e) { alert('브라우저 저장소에 저장하지 못했습니다. 파일로 내보내기를 써 주세요.'); return; }
    this.change(L => { L.name = name; });
    Panels.toast(`💾 '${name}' 저장`);
  },
  loadSave(name) {
    const s = this.saves()[name];
    if (s) this.replace(s.layout, `'${name}' 불러옴`);
  },
  deleteSave(name) {
    const all = this.saves();
    delete all[name];
    localStorage.setItem(ED_KEYS.saves, JSON.stringify(all));
  },

  replace(layout, msg) {
    this.pushUndo();
    this.startFrom = '';
    this.L = this.sanitize(LayoutLib.clone(layout));
    this.sel = null;
    this.draw = null;
    this.afterChange();
    View.fit();
    if (msg) Panels.toast(msg + (this.startFrom ? ` (${this.startFrom.replace(/^ · /, '')})` : ''));
  },

  download(filename, text, type) {
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([text], { type }));
    a.download = filename;
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 1000);
  },

  exportData() {
    const errs = this.issues.filter(i => i.level === 'error');
    if (errs.length && !confirm(`오류가 ${errs.length}개 있습니다. 게임은 이 배치 대신 기본 배치를 씁니다. 그래도 내보낼까요?`)) return;
    const text = `// APCenter 배치 데이터 (배치 시뮬레이터에서 내보냄: ${this.L.name}, ${new Date().toLocaleString('ko-KR')})\n`
      + '// 이 파일로 APCenterMU/layout-data.js를 덮어쓰면 게임에 반영됩니다.\n'
      + `window.APC_LAYOUT = ${JSON.stringify(this.L, null, 2)};\n`;
    this.download('layout-data.js', text, 'text/javascript');
    Panels.toast('📦 layout-data.js 내보냄 → APCenterMU 폴더의 같은 이름 파일을 덮어쓰면 게임에 반영됩니다');
  },

  exportJson() {
    this.download(`${this.L.name || '배치'}.json`, JSON.stringify(this.L, null, 2), 'application/json');
  },

  importText(text) {
    try {
      let src = text.trim();
      const m = src.match(/APC_LAYOUT\s*=\s*([\s\S]*?);?\s*$/);
      if (m) src = m[1];
      const l = JSON.parse(src);
      if (!l || typeof l !== 'object') throw new Error('배치 데이터가 아닙니다');
      this.replace(l, `📂 '${l.name || '배치'}' 가져옴`);
    } catch (e) {
      alert('파일을 읽지 못했습니다: ' + e.message);
    }
  },

  // 테스트 플레이: 게임을 새 탭으로 열고 URL에 배치를 실어 보낸다 (파일을 바꾸지 않음)
  testPlay() {
    const errs = this.issues.filter(i => i.level === 'error');
    if (errs.length) { alert('오류를 먼저 고쳐 주세요:\n' + errs.map(e => '· ' + e.msg).join('\n')); return; }
    window.open('index.html#layout=' + encodeURIComponent(JSON.stringify(this.L)), '_blank');
  },

  newDefault() {
    if (!confirm('기본 배치로 새로 시작할까요? (되돌리기로 돌아올 수 있습니다)')) return;
    this.replace(LayoutLib.makeDefault(), '기본 배치로 시작');
  },
  loadGameLayout() {
    if (!window.APC_LAYOUT) { alert('layout-data.js에 저장된 배치가 없습니다 (게임은 기본 배치를 쓰는 중)'); return; }
    this.replace(window.APC_LAYOUT, '게임 배치(layout-data.js) 불러옴');
  },
};

function S_STAGE_NAME(st) {
  const s = CONFIG.stages.find(x => x.id === st);
  return s ? `${s.emoji} ${s.name}` : st;
}
