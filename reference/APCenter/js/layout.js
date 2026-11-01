// 배치 데이터: 게임(index.html)과 배치 시뮬레이터(editor.html)가 함께 쓴다
//
// 좌표: x = 오른쪽, z = 아래쪽(화면 기준), 1 = 1칸
// rot(도): 0 = +x, 90 = +z, 180 = -x, 270 = -z
//   시설의 rot = 앞면(상호작용 쪽) 방향, 트럭의 rot = 뒷문 방향
// 상호작용 지점(사람): in = 넣기, out = 꺼내기, inspect = 검수. 같은 위치에 두면 하나의 범위로 합쳐진다
// 벨트 포트(컨베이어): beltIn = 벨트 입구(물품이 내려옴), beltOut = 벨트 출구(물품이 올라감)
//
// {
//   version: 2, name, map: { w, d }, interior: { x1, z1, x2, z2 },
//   items: [{ id, name, emoji, type, price }],                   품목 (type = CONFIG.types 키). 보관함 1개 = 품목 1종
//   areas: [{ name, x1, z1, x2, z2, color }], walls: [{ x1, z1, x2, z2 }],
//   inTrucks: [{ x, z, rot, out }],                              하차 주차공간 (트럭 짐칸 중심)
//   staging: { x, z, in, out, beltOut },                         하차장 (임시 보관소)
//   bins: [{ x, z, rot, item, cost, in, out, inspect, beltIn, beltOut }],  보관함 (cost 0 = 시작부터 해금)
//   tables: [{ x, z, rot, cost, in, out, beltIn, beltOut }],     포장대 (in = 물품 전달·포장, out = 박스 집기)
//   docks: [{ x, z, dest, cost, in, beltIn, truck: { x, z, rot } }], 출고 도크
//   trash: { x, z, in }, rest: { x, z, w, d }, waiting: { x, z, w, d },
//   purifier, entrance, playerStart: { x, z }, idle: { 공정id: { x, z } },
//   belts: { sort | transfer | outbound: { lines: [Line] } }
// }
// Line = { id, points: [출발점, 꺾이는 점..., 도착점], from: Attach, to: Attach }   (출발점 → 도착점 한 방향)
// Attach = null (떨어짐) | { fac: 'staging'|'bin'|'table'|'dock', i, port: 'beltIn'|'beltOut' } | { line: 같은 공정 라인 id }
//   시설에 붙은 끝점은 그 포트 위치, 다른 라인에 붙은 끝점은 그 라인 위(합류/분기)

const LayoutLib = {
  VERSION: 2,
  BELT_STAGES: ['sort', 'transfer', 'outbound'],
  BELT_NAMES: { sort: '분류', transfer: '이송', outbound: '출고분류' },
  PORTS: { staging: ['beltOut'], bin: ['beltIn', 'beltOut'], table: ['beltIn', 'beltOut'], dock: ['beltIn'] },
  PORT_NAMES: { beltIn: '벨트 입구', beltOut: '벨트 출구' },
  // 공정마다 벨트가 이어야 하는 포트 (출발 → 도착)
  STAGE_PORTS: {
    sort: { src: ['staging', 'beltOut'], dst: ['bin', 'beltIn'] },
    transfer: { src: ['bin', 'beltOut'], dst: ['table', 'beltIn'] },
    outbound: { src: ['table', 'beltOut'], dst: ['dock', 'beltIn'] },
  },
  FAC_NAMES: { staging: '하차장', bin: '보관함', table: '포장대', dock: '도크' },

  // 시설 크기 (rot = 0 기준 [x 방향, z 방향])
  SIZE: {
    inTruck: [4.4, 2.1], staging: [2.6, 2.6], bin: [1.1, 2.4], table: [1.8, 2.2],
    dock: [2.4, 2.4], dockTruck: [4.4, 2.1], trash: [1.5, 1.5], purifier: [1, 1],
  },

  dir(rot) {
    const a = rot * Math.PI / 180;
    return { x: Math.round(Math.cos(a) * 1e6) / 1e6, z: Math.round(Math.sin(a) * 1e6) / 1e6 };
  },
  clone(o) { return JSON.parse(JSON.stringify(o)); },
  pt(slot, role) { return (slot && slot[role]) || { x: slot.x, z: slot.z }; },
  dist(a, b) { return Math.hypot(a.x - b.x, a.z - b.z); },
  r2(v) { return Math.round(v * 100) / 100; },

  // 배치의 품목 목록 (예전 배치에 없으면 기본 품목)
  items(l) { return (l && l.items) || LayoutLib.BASE_ITEMS; },
  item(l, id) { return LayoutLib.items(l).find(x => x.id === id); },

  // ---------- 기본 배치 ----------
  makeDefault() {
    const P = (x, z) => ({ x, z });
    const binCols = [16, 21, 26, 31], rows = [{ z: 4.6, rot: 90 }, { z: 17.4, rot: 270 }];
    const bins = LayoutLib.BASE_ITEMS.slice(0, 8).map((it, i) => {
      const x = binCols[i % 4], row = rows[Math.floor(i / 4)], d = LayoutLib.dir(row.rot);
      const p = P(x, row.z + d.z * 2.2);
      return {
        x, z: row.z, rot: row.rot, item: it.id, cost: LayoutLib.BASE_COST[it.id] || 0, in: { ...p }, out: { ...p }, inspect: { ...p },
        beltIn: P(x - 0.35, row.z), beltOut: P(x + 0.35, row.z),
      };
    });
    const tables = [5, 11, 17].map((z, i) => ({
      x: 39, z, rot: 180, cost: CONFIG.packTableCosts[i] || 0, in: P(36.8, z), out: P(36.8, z),
      beltIn: P(38.7, z), beltOut: P(39.3, z),
    }));
    const docks = CONFIG.destinations.slice(0, 4).map((d, i) => {
      const z = [4, 11, 18, 25][i];
      return { x: 48, z, dest: d.id, cost: d.cost, in: P(47.2, z), beltIn: P(48, z), truck: { x: 54.4, z, rot: 180 } };
    });
    const l = {
      version: LayoutLib.VERSION,
      name: '기본 배치',
      items: LayoutLib.clone(LayoutLib.BASE_ITEMS),
      map: { w: 58, d: 32 },
      interior: { x1: 0, z1: 0, x2: 52, z2: 32 },
      areas: [
        { name: '하차 구역', x1: 0, z1: 1.8, x2: 12, z2: 19.8, color: '#f1e2a4' },
        { name: '보관 구역', x1: 13, z1: 2.4, x2: 34, z2: 19.6, color: '#cfe0f0' },
        { name: '포장 구역', x1: 35, z1: 2.4, x2: 41.6, z2: 19.6, color: '#d5ecd2' },
        { name: '출고 구역', x1: 42.4, z1: 0.4, x2: 52, z2: 28.6, color: '#f5d9c0' },
      ],
      walls: [
        { x1: 0, z1: 0.1, x2: 52, z2: 0.1 },
        { x1: 0.1, z1: 0, x2: 0.1, z2: 2.4 }, { x1: 0.1, z1: 14.6, x2: 0.1, z2: 19 }, { x1: 0.1, z1: 23, x2: 0.1, z2: 32 },
      ],
      inTrucks: [8.5, 4, 13].map(z => ({ x: 3, z, rot: 0, out: P(6.8, z) })),
      staging: { x: 9.5, z: 17.5, in: P(9.5, 17.5), out: P(9.5, 17.5), beltOut: P(11.5, 17.5) },
      bins, tables, docks,
      trash: { x: 25, z: 27, in: P(25, 27) },
      rest: { x: 6, z: 27, w: 9, d: 7 },
      waiting: { x: 16, z: 27.5, w: 5, d: 3.6 },
      purifier: P(34, 26),
      entrance: P(0.8, 21),
      playerStart: P(12, 21),
      idle: {
        unload: P(9.5, 11.2), sort: P(11.5, 13), inspect: P(23.5, 11),
        transfer: P(34, 11), pack: P(35.2, 8), outbound: P(42, 14.5), load: P(45.2, 14.5),
      },
      belts: {},
    };

    // 컨베이어: 메인 라인 + 지선
    const fac = (f, i, port) => ({ fac: f, i, port });
    // 분류: 하차장 → 통로 메인 라인 → 보관함마다 지선 (분기)
    const sortZ = 10.4;
    l.belts.sort = { lines: [{ id: 1, points: [P(11.5, 17.5), P(11.5, sortZ), P(30.65, sortZ)], from: fac('staging', 0, 'beltOut'), to: null }] };
    bins.forEach((b, i) => l.belts.sort.lines.push({ id: i + 2, points: [P(b.beltIn.x, sortZ), { ...b.beltIn }], from: { line: 1 }, to: fac('bin', i, 'beltIn') }));
    // 이송: 보관함 지선 (합류) → 통로 메인 라인 → 포장대 지선 (분기)
    const trZ = 11.6, trX = 37.4;
    l.belts.transfer = { lines: [{ id: 1, points: [P(16.35, trZ), P(trX, trZ)], from: null, to: null }] };
    let id = 2;
    bins.forEach((b, i) => l.belts.transfer.lines.push({ id: id++, points: [{ ...b.beltOut }, P(b.beltOut.x, trZ)], from: fac('bin', i, 'beltOut'), to: { line: 1 } }));
    tables.forEach((t, i) => l.belts.transfer.lines.push({ id: id++, points: [P(trX, trZ), P(trX, t.z), { ...t.beltIn }], from: { line: 1 }, to: fac('table', i, 'beltIn') }));
    // 출고분류: 포장대 지선 (합류) → 위로 모았다가 오른쪽 세로 라인으로 내려가며 → 도크 지선 (분기)
    l.belts.outbound = { lines: [{ id: 1, points: [P(41, 17), P(41, 2), P(43.5, 2), P(43.5, 25)], from: null, to: null }] };
    id = 2;
    tables.forEach((t, i) => l.belts.outbound.lines.push({ id: id++, points: [{ ...t.beltOut }, P(41, t.z)], from: fac('table', i, 'beltOut'), to: { line: 1 } }));
    docks.forEach((d, i) => l.belts.outbound.lines.push({ id: id++, points: [P(43.5, d.z), { ...d.beltIn }], from: { line: 1 }, to: fac('dock', i, 'beltIn') }));
    return l;
  },

  // ---------- 시설 / 포트 ----------
  facList(l, fac) { return fac === 'staging' ? [l.staging] : (l[fac + 's'] || []); },
  port(l, fac, i, port) { const o = LayoutLib.facList(l, fac)[i]; return o ? LayoutLib.pt(o, port) : null; },
  portKey(a) { return `${a.fac}:${a.i}:${a.port}`; },
  attachName(l, a) {
    if (!a) return '떨어짐';
    if (a.line !== undefined) return `라인 ${a.line} 위`;
    const o = LayoutLib.facList(l, a.fac)[a.i];
    let name = LayoutLib.FAC_NAMES[a.fac] + (a.fac === 'staging' ? '' : ` ${a.i + 1}`);
    if (a.fac === 'bin' && o) { const it = LayoutLib.item(l, o.item); if (it) name += ` (${it.name})`; }
    if (a.fac === 'dock' && o) { const de = CONFIG.destinations.find(x => x.id === o.dest); if (de) name += ` (${de.name})`; }
    return `${name} ${LayoutLib.PORT_NAMES[a.port]}`;
  },

  // ---------- 벨트 라인 ----------
  // 폴리라인 위에서 p와 가장 가까운 점
  projectOn(pts, p) {
    let best = null;
    for (let i = 0; i < pts.length - 1; i++) {
      const A = pts[i], B = pts[i + 1], dx = B.x - A.x, dz = B.z - A.z, l2 = dx * dx + dz * dz;
      const t = l2 ? Math.max(0, Math.min(1, ((p.x - A.x) * dx + (p.z - A.z) * dz) / l2)) : 0;
      const q = { x: A.x + dx * t, z: A.z + dz * t }, d = LayoutLib.dist(p, q);
      if (!best || d < best.dist) best = { point: q, seg: i, t, dist: d };
    }
    return best;
  },

  // 시설에 붙은 끝점을 포트 위치로 바꾼 점 목록
  rawPts(l, line) {
    const pts = line.points.map(p => ({ x: p.x, z: p.z }));
    const fp = line.from && line.from.fac ? LayoutLib.port(l, line.from.fac, line.from.i, line.from.port) : null;
    const tp = line.to && line.to.fac ? LayoutLib.port(l, line.to.fac, line.to.i, line.to.port) : null;
    if (fp) pts[0] = { ...fp };
    if (tp) pts[pts.length - 1] = { ...tp };
    return pts;
  },

  // 실제 점 목록: 다른 라인에 붙은 끝점은 그 라인 위로 맞춘다
  linePts(l, st, line) {
    const pts = LayoutLib.rawPts(l, line), lines = l.belts[st].lines;
    for (const [end, k] of [['from', 0], ['to', pts.length - 1]]) {
      const a = line[end];
      if (!a || a.line === undefined) continue;
      const target = lines.find(x => x.id === a.line);
      if (target && target !== line) pts[k] = LayoutLib.projectOn(LayoutLib.rawPts(l, target), pts[k]).point;
    }
    return pts;
  },

  // 라인들 → 방향 있는 경로망 { nodes, edges: [[a, b]] (a → b), ports: Map(포트키 → 노드 id) }
  buildGraph(l, st) {
    const lines = (l.belts[st] && l.belts[st].lines) || [];
    const res = new Map(lines.map(ln => [ln.id, LayoutLib.linePts(l, st, ln)]));
    // 다른 라인이 붙은 위치에서 그 라인을 나눈다
    const splits = new Map(lines.map(ln => [ln.id, []]));
    for (const ln of lines) {
      const pts = res.get(ln.id);
      if (ln.from && ln.from.line !== undefined && splits.has(ln.from.line)) splits.get(ln.from.line).push(pts[0]);
      if (ln.to && ln.to.line !== undefined && splits.has(ln.to.line)) splits.get(ln.to.line).push(pts[pts.length - 1]);
    }
    const nodes = [], byKey = new Map(), edges = [], ports = new Map(), seen = new Set();
    const nodeAt = p => {
      const k = Math.round(p.x * 100) + ',' + Math.round(p.z * 100);
      if (!byKey.has(k)) { const n = { id: nodes.length + 1, x: LayoutLib.r2(p.x), z: LayoutLib.r2(p.z) }; nodes.push(n); byKey.set(k, n.id); }
      return byKey.get(k);
    };
    for (const ln of lines) {
      const pts = res.get(ln.id);
      if (pts.length < 2) continue;
      const extra = splits.get(ln.id).map(p => ({ p, pr: LayoutLib.projectOn(pts, p) }));
      const seq = [];
      for (let i = 0; i < pts.length; i++) {
        seq.push(pts[i]);
        if (i < pts.length - 1) extra.filter(e => e.pr.seg === i).sort((a, b) => a.pr.t - b.pr.t).forEach(e => seq.push(e.pr.point));
      }
      for (let i = 0; i < seq.length - 1; i++) {
        const a = nodeAt(seq[i]), b = nodeAt(seq[i + 1]);
        const k = a + '>' + b;
        if (a !== b && !seen.has(k)) { seen.add(k); edges.push([a, b]); }
      }
      if (ln.from && ln.from.fac) ports.set(LayoutLib.portKey(ln.from), nodeAt(pts[0]));
      if (ln.to && ln.to.fac) ports.set(LayoutLib.portKey(ln.to), nodeAt(pts[pts.length - 1]));
    }
    return { nodes, edges, ports };
  },

  // 방향을 따라가는 최단 경로 (포트키 → 포트키). 없으면 null
  routeKeys(g, srcKey, dstKey) {
    const s = g.ports.get(srcKey), t = g.ports.get(dstKey);
    if (!s || !t) return null;
    const pos = new Map(g.nodes.map(n => [n.id, n]));
    const adj = new Map(g.nodes.map(n => [n.id, []]));
    for (const [a, b] of g.edges) adj.get(a).push([b, LayoutLib.dist(pos.get(a), pos.get(b))]);
    const dist = new Map([[s, 0]]), prev = new Map(), done = new Set();
    for (;;) {
      let u = null, ud = Infinity;
      for (const [id, d] of dist) if (!done.has(id) && d < ud) { ud = d; u = id; }
      if (u === null) return null;
      if (u === t) break;
      done.add(u);
      for (const [v, w] of adj.get(u)) {
        if (ud + w < (dist.has(v) ? dist.get(v) : Infinity)) { dist.set(v, ud + w); prev.set(v, u); }
      }
    }
    const ids = [t];
    while (ids[0] !== s) ids.unshift(prev.get(ids[0]));
    return ids.map(id => ({ x: pos.get(id).x, z: pos.get(id).z }));
  },

  // 공정별로 벨트가 이어줘야 하는 포트 쌍
  beltPairs(l, st) {
    const sp = LayoutLib.STAGE_PORTS[st], out = [];
    const srcs = LayoutLib.facList(l, sp.src[0]), dsts = LayoutLib.facList(l, sp.dst[0]);
    srcs.forEach((_, i) => dsts.forEach((__, j) => {
      const a = { fac: sp.src[0], i, port: sp.src[1] }, b = { fac: sp.dst[0], i: j, port: sp.dst[1] };
      out.push({ src: LayoutLib.portKey(a), dst: LayoutLib.portKey(b), label: `${LayoutLib.attachName(l, a)} → ${LayoutLib.attachName(l, b)}` });
    }));
    return out;
  },

  // 자동 생성 (초안): 포트들을 가로지르는 메인 라인 하나 + 출발 포트 지선(합류) + 도착 포트 지선(분기)
  autoLines(l, st) {
    const sp = LayoutLib.STAGE_PORTS[st], r2 = LayoutLib.r2;
    const list = (fac, port) => LayoutLib.facList(l, fac).map((o, i) => ({ a: { fac, i, port }, p: LayoutLib.pt(o, port) }));
    const srcs = list(sp.src[0], sp.src[1]), dsts = list(sp.dst[0], sp.dst[1]);
    if (!srcs.length || !dsts.length) return [];
    const all = srcs.concat(dsts), mean = a => a.reduce((s, v) => s + v, 0) / a.length;
    const xs = all.map(o => o.p.x), zs = all.map(o => o.p.z);
    const horiz = Math.max(...xs) - Math.min(...xs) >= Math.max(...zs) - Math.min(...zs);
    const along = p => horiz ? p.x : p.z, cross = r2(mean(horiz ? zs : xs));
    const at = v => horiz ? { x: r2(v), z: cross } : { x: cross, z: r2(v) };
    const fwd = mean(dsts.map(o => along(o.p))) >= mean(srcs.map(o => along(o.p)));
    const vals = all.map(o => along(o.p)), lo = Math.min(...vals), hi = Math.max(...vals);
    const lines = [{ id: 1, points: fwd ? [at(lo), at(hi)] : [at(hi), at(lo)], from: null, to: null }];
    let id = 2;
    for (const s of srcs) lines.push({ id: id++, points: [{ ...s.p }, at(along(s.p))], from: s.a, to: { line: 1 } });
    for (const d of dsts) lines.push({ id: id++, points: [at(along(d.p)), { ...d.p }], from: { line: 1 }, to: d.a });
    return lines;
  },

  // 예전 형식(버전 1) → 버전 2: 벨트 포트를 시설 중심에 두고 벨트는 자동 생성 초안으로
  upgrade(l) {
    if (!l || typeof l !== 'object') return l;
    if (!Array.isArray(l.items)) l.items = LayoutLib.clone(LayoutLib.BASE_ITEMS);   // 품목 목록이 없던 배치
    if (l.version === LayoutLib.VERSION) return l;
    if (l.version === 1) {
      const c = o => ({ x: o.x, z: o.z });
      if (l.staging && !l.staging.beltOut) l.staging.beltOut = c(l.staging);
      for (const k of ['bins', 'tables']) (l[k] || []).forEach(o => { o.beltIn = o.beltIn || c(o); o.beltOut = o.beltOut || c(o); });
      (l.docks || []).forEach(o => { o.beltIn = o.beltIn || c(o); });
      l.belts = {};
      for (const st of LayoutLib.BELT_STAGES) l.belts[st] = { lines: LayoutLib.autoLines(l, st) };
      l.version = LayoutLib.VERSION;
      l._upgraded = true;
    }
    return l;
  },

  // ---------- 시설 영역 ----------
  footprint(kind, o) {
    const [w, d] = LayoutLib.SIZE[kind];
    const r = ((o.rot || 0) % 180 + 180) % 180;
    const a = r * Math.PI / 180, c = Math.abs(Math.cos(a)), s = Math.abs(Math.sin(a));
    const hw = (w * c + d * s) / 2, hd = (w * s + d * c) / 2;
    return { x1: o.x - hw, z1: o.z - hd, x2: o.x + hw, z2: o.z + hd };
  },

  // 겹침·범위 검사용 시설 목록
  facilities(l) {
    const out = [];
    l.inTrucks.forEach((t, i) => out.push({ kind: 'inTruck', o: t, label: `입고 트럭 ${i + 1}` }));
    if (l.staging) out.push({ kind: 'staging', o: l.staging, label: '하차장' });
    l.bins.forEach((b, i) => out.push({ kind: 'bin', o: b, label: `보관함 ${i + 1}` }));
    l.tables.forEach((t, i) => out.push({ kind: 'table', o: t, label: `포장대 ${i + 1}` }));
    l.docks.forEach((d, i) => {
      out.push({ kind: 'dock', o: d, label: `도크 ${i + 1}` });
      out.push({ kind: 'dockTruck', o: d.truck, label: `도크 ${i + 1} 트럭` });
    });
    if (l.trash) out.push({ kind: 'trash', o: l.trash, label: '쓰레기통' });
    if (l.purifier) out.push({ kind: 'purifier', o: l.purifier, label: '공기청정기' });
    return out;
  },

  // 상호작용 지점 목록
  points(l) {
    const out = [];
    const add = (o, role, label) => out.push({ p: LayoutLib.pt(o, role), label });
    l.inTrucks.forEach((t, i) => add(t, 'out', `입고 트럭 ${i + 1}`));
    if (l.staging) { add(l.staging, 'in', '하차장 넣기'); add(l.staging, 'out', '하차장 꺼내기'); }
    l.bins.forEach((b, i) => { add(b, 'in', `보관함 ${i + 1} 넣기`); add(b, 'out', `보관함 ${i + 1} 꺼내기`); add(b, 'inspect', `보관함 ${i + 1} 검수`); });
    l.tables.forEach((t, i) => { add(t, 'in', `포장대 ${i + 1} 넣기`); add(t, 'out', `포장대 ${i + 1} 꺼내기`); });
    l.docks.forEach((d, i) => add(d, 'in', `도크 ${i + 1}`));
    if (l.trash) add(l.trash, 'in', '쓰레기통');
    return out;
  },

  // ---------- 검증 ----------
  validate(l) {
    const out = [];
    const err = msg => out.push({ level: 'error', msg });
    const warn = msg => out.push({ level: 'warn', msg });
    if (!l || typeof l !== 'object') { err('배치 데이터가 없습니다'); return out; }
    if (l.version !== LayoutLib.VERSION) err(`지원하지 않는 배치 버전입니다 (${l.version})`);
    for (const k of ['map', 'interior', 'staging', 'trash', 'rest', 'waiting', 'purifier', 'entrance', 'playerStart', 'belts']) {
      if (!l[k]) err(`필수 항목 없음: ${k}`);
    }
    for (const k of ['inTrucks', 'bins', 'tables', 'docks', 'areas', 'walls']) if (!Array.isArray(l[k])) err(`필수 목록 없음: ${k}`);
    if (out.length) return out;
    if (!l.inTrucks.length) err('하차 주차공간이 1개 이상 필요합니다');
    if (!l.bins.length) err('보관함이 1개 이상 필요합니다');
    if (!l.tables.length) err('포장대가 1개 이상 필요합니다');
    if (!l.docks.length) err('출고 도크가 1개 이상 필요합니다');

    // 품목
    const items = LayoutLib.items(l), ids = new Set();
    items.forEach((it, i) => {
      if (!it.id || ids.has(it.id)) err(`품목 ${i + 1}: id가 비었거나 겹칩니다`);
      ids.add(it.id);
      if (!it.name) err(`품목 ${i + 1}: 이름이 없습니다`);
      if (!CONFIG.types[it.type]) err(`품목 ${it.name || i + 1}: 알 수 없는 타입 '${it.type}'`);
      if (!(it.price >= 0)) err(`품목 ${it.name || i + 1}: 가격이 올바르지 않습니다`);
    });
    const itemIds = items.map(i => i.id), destIds = CONFIG.destinations.map(d => d.id);
    const seenItem = new Set(), seenDest = new Set();
    l.bins.forEach((b, i) => {
      if (!itemIds.includes(b.item)) err(`보관함 ${i + 1}: 알 수 없는 품목 '${b.item}'`);
      else if (seenItem.has(b.item)) err(`보관함 ${i + 1}: 품목 '${b.item}'이 다른 보관함과 겹칩니다 (보관함 1개 = 품목 1종)`);
      seenItem.add(b.item);
    });
    const noBin = items.filter(it => !seenItem.has(it.id));
    if (noBin.length) warn(`보관함이 없는 품목 ${noBin.length}개 (${noBin.map(it => it.name).join(', ')}): 게임에서 해금할 수 없고 주문에도 나오지 않습니다`);
    l.docks.forEach((d, i) => {
      if (!destIds.includes(d.dest)) err(`도크 ${i + 1}: 알 수 없는 목적지 '${d.dest}'`);
      else if (seenDest.has(d.dest)) err(`도크 ${i + 1}: 목적지 '${d.dest}'가 다른 도크와 겹칩니다`);
      seenDest.add(d.dest);
    });
    if (l.bins.length && !l.bins.some(b => !b.cost)) warn('시작부터 해금된 보관함이 없습니다 (비용 0인 보관함 필요)');
    if (l.tables.length && !l.tables.some(t => !t.cost)) warn('시작부터 해금된 포장대가 없습니다');
    if (l.docks.length && !l.docks.some(d => !d.cost)) warn('시작부터 해금된 도크가 없습니다');

    // 상호작용 범위 겹침 (같은 위치는 하나로 합쳐지므로 허용)
    const R = CONFIG.interactRadius, pts = LayoutLib.points(l), I = l.interior;
    for (let i = 0; i < pts.length; i++) {
      for (let j = i + 1; j < pts.length; j++) {
        const d = LayoutLib.dist(pts[i].p, pts[j].p);
        if (d > 0.05 && d < R * 2) warn(`상호작용 범위 겹침: ${pts[i].label} ↔ ${pts[j].label} (${d.toFixed(1)}칸)`);
      }
      const p = pts[i].p;
      if (p.x < I.x1 || p.x > I.x2 || p.z < I.z1 || p.z > I.z2) warn(`창고 밖 상호작용 지점: ${pts[i].label}`);
    }

    // 시설끼리 겹침
    const fs = LayoutLib.facilities(l).map(f => ({ ...f, r: LayoutLib.footprint(f.kind, f.o) }));
    for (let i = 0; i < fs.length; i++) for (let j = i + 1; j < fs.length; j++) {
      const a = fs[i].r, b = fs[j].r;
      const ox = Math.min(a.x2, b.x2) - Math.max(a.x1, b.x1), oz = Math.min(a.z2, b.z2) - Math.max(a.z1, b.z1);
      if (ox > 0.05 && oz > 0.05) warn(`시설 겹침: ${fs[i].label} ↔ ${fs[j].label}`);
    }

    // 컨베이어 (자동화했을 때만 쓰임)
    for (const st of LayoutLib.BELT_STAGES) {
      const name = LayoutLib.BELT_NAMES[st], lines = (l.belts[st] && l.belts[st].lines) || [];
      lines.forEach(ln => {
        if (!ln.points || ln.points.length < 2) warn(`${name} 라인 ${ln.id}: 점이 2개보다 적습니다`);
        for (const end of ['from', 'to']) {
          const a = ln[end];
          if (!a) continue;
          if (a.line !== undefined && !lines.some(x => x.id === a.line && x !== ln)) warn(`${name} 라인 ${ln.id}: 붙어 있던 라인 ${a.line}이 없습니다`);
          if (a.fac && !LayoutLib.port(l, a.fac, a.i, a.port)) warn(`${name} 라인 ${ln.id}: 붙어 있던 ${LayoutLib.FAC_NAMES[a.fac]}가 없습니다`);
        }
      });
      const g = LayoutLib.buildGraph(l, st), pairs = LayoutLib.beltPairs(l, st);
      const missing = pairs.filter(p => !LayoutLib.routeKeys(g, p.src, p.dst));
      if (!missing.length) continue;
      const reversed = missing.filter(p => LayoutLib.routeKeys(g, p.dst, p.src));
      warn(`${name} 컨베이어: ${missing.length}/${pairs.length}개 연결 없음 (예: ${missing[0].label})`
        + (reversed.length ? ` · 그중 ${reversed.length}개는 방향이 반대` : '') + ' → 자동화 시 직선으로 이동');
    }
    return out;
  },

  // ---------- 동선 측정 (직선 거리, 칸) ----------
  measure(l) {
    const avg = (as, bs) => {
      let s = 0, n = 0;
      for (const a of as) for (const b of bs) { s += LayoutLib.dist(a, b); n++; }
      return n ? s / n : 0;
    };
    const pts = (list, role) => list.map(o => LayoutLib.pt(o, role));
    const m = {
      unload: avg(pts(l.inTrucks, 'out'), [LayoutLib.pt(l.staging, 'in')]),
      sort: avg([LayoutLib.pt(l.staging, 'out')], pts(l.bins, 'in')),
      transfer: avg(pts(l.bins, 'out'), pts(l.tables, 'in')),
      outbound: avg(pts(l.tables, 'out'), pts(l.docks, 'in')),
    };
    m.total = m.unload + m.sort + m.transfer + m.outbound;
    return m;
  },

  // ---------- 게임에서 불러오기 ----------
  // 우선순위: 시뮬레이터 테스트 플레이(URL #layout=...) → layout-data.js(window.APC_LAYOUT) → 기본 배치
  load() {
    let src = null, from = '기본 배치';
    try {
      const m = location.hash.match(/layout=([^&]*)/);
      if (m) { src = JSON.parse(decodeURIComponent(m[1])); from = '시뮬레이터 테스트 플레이'; }
    } catch (e) { console.warn('테스트 배치를 읽지 못했습니다', e); }
    if (!src && window.APC_LAYOUT) { src = window.APC_LAYOUT; from = 'layout-data.js'; }
    let l = src ? LayoutLib.upgrade(LayoutLib.clone(src)) : LayoutLib.makeDefault();
    const errs = LayoutLib.validate(l).filter(e => e.level === 'error');
    LayoutLib.loadErrors = errs;
    if (errs.length) {
      console.warn('배치 오류로 기본 배치를 씁니다', errs);
      l = LayoutLib.makeDefault();
      from = '기본 배치 (불러온 배치에 오류가 있음)';
    }
    LayoutLib.source = from + (l._upgraded ? ', 이전 버전 → 벨트 자동 생성' : '');
    return LayoutLib.prepare(l);
  },

  // 게임용 파생값 계산
  prepare(l) {
    // 게임은 배치의 품목 목록을 쓴다 (CONFIG.items를 순번으로 참조하는 코드 전체에 적용)
    CONFIG.items = LayoutLib.items(l);
    const I = l.interior;
    l.bounds = { minX: I.x1 + 0.6, maxX: I.x2 - 0.6, minZ: I.z1 + 0.6, maxZ: I.z2 - 0.6 };
    l._graphs = {};
    for (const st of LayoutLib.BELT_STAGES) l._graphs[st] = LayoutLib.buildGraph(l, st);
    // 하차 주차공간 업그레이드 단계 = 주차 슬롯 수
    const f = CONFIG.facility.parking, n = l.inTrucks.length;
    f.values = Array.from({ length: n }, (_, i) => i + 1);
    while (f.costs.length < n) f.costs.push(Math.round(f.costs[f.costs.length - 1] * 1.6 / 100) * 100);
    f.costs = f.costs.slice(0, n);
    l._routes = new Map();
    return l;
  },

  // 게임 중 경로 캐시 (포트키 → 포트키)
  cachedRoute(stage, src, dst) {
    const k = `${stage}:${src}>${dst}`;
    if (!LAYOUT._routes.has(k)) LAYOUT._routes.set(k, LayoutLib.routeKeys(LAYOUT._graphs[stage], src, dst));
    return LAYOUT._routes.get(k);
  },
};

// 기본 품목 (config.js) — 배치에 품목 목록이 없을 때와 기본 배치에 쓴다. 해금 비용은 보관함 슬롯으로 옮긴다
LayoutLib.BASE_ITEMS = CONFIG.items.map(({ id, name, emoji, type, price }) => ({ id, name, emoji, type, price }));
LayoutLib.BASE_COST = Object.fromEntries(CONFIG.items.map(it => [it.id, it.cost || 0]));