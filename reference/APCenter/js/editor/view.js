// 배치 시뮬레이터: 위에서 내려다본 2D 캔버스 (그리기, 클릭 판정, 마우스/키보드 입력)

const BELT_COLORS = { sort: '#1565c0', transfer: '#2e7d32', outbound: '#e65100' };
const ROLE_COLORS = { in: '#2e9e57', out: '#1e5aa8', inspect: '#8e24aa' };
const ROLE_SHORT = { in: '넣', out: '꺼', inspect: '검' };

const View = {
  scale: 18, ox: -3, oz: -3,
  mouse: { x: 0, z: 0, sx: 0, sy: 0, shift: false },
  hover: null,
  drag: null,
  spaceDown: false,
  queued: false,

  init(cv) {
    this.cv = cv;
    this.ctx = cv.getContext('2d');
    new ResizeObserver(() => this.resize()).observe(cv.parentElement);
    this.resize();
    cv.addEventListener('pointerdown', e => this.onDown(e));
    window.addEventListener('pointermove', e => this.onMove(e));
    window.addEventListener('pointerup', e => this.onUp(e));
    cv.addEventListener('wheel', e => this.onWheel(e), { passive: false });
    cv.addEventListener('contextmenu', e => e.preventDefault());
    cv.addEventListener('dblclick', () => this.onDblClick());
    window.addEventListener('keydown', e => this.onKey(e));
    window.addEventListener('keyup', e => { if (e.code === 'Space') this.spaceDown = false; });
  },

  resize() {
    const r = this.cv.parentElement.getBoundingClientRect();
    this.dpr = window.devicePixelRatio || 1;
    this.W = r.width;
    this.H = r.height;
    this.cv.width = Math.round(r.width * this.dpr);
    this.cv.height = Math.round(r.height * this.dpr);
    this.cv.style.width = r.width + 'px';
    this.cv.style.height = r.height + 'px';
    this.render();
  },

  // 맵 전체가 보이게
  fit() {
    const L = Ed.L, w = L.map.w + 4, d = L.map.d + 4;
    this.scale = Math.max(4, Math.min(this.W / w, this.H / d));
    this.ox = -2 - (this.W / this.scale - w) / 2;
    this.oz = -2 - (this.H / this.scale - d) / 2;
    this.render();
  },

  w2s(x, z) { return [(x - this.ox) * this.scale, (z - this.oz) * this.scale]; },
  s2w(sx, sy) { return { x: sx / this.scale + this.ox, z: sy / this.scale + this.oz }; },
  centerWorld() { return this.s2w(this.W / 2, this.H / 2); },
  px(n) { return n / this.scale; },

  // 선분 AB 위에서 p와 가장 가까운 점
  project(p, A, B) {
    const dx = B.x - A.x, dz = B.z - A.z, l2 = dx * dx + dz * dz;
    const t = l2 ? U.clamp(((p.x - A.x) * dx + (p.z - A.z) * dz) / l2, 0, 1) : 0;
    return { x: A.x + dx * t, z: A.z + dz * t };
  },
  segDist(p, A, B) { const q = this.project(p, A, B); return Math.hypot(p.x - q.x, p.z - q.z); },

  // ---------- 그리기 ----------
  render() {
    if (this.queued) return;
    this.queued = true;
    requestAnimationFrame(() => { this.queued = false; this.draw(); });
  },

  draw() {
    const c = this.ctx, L = Ed.L;
    if (!L) return;
    c.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
    c.fillStyle = '#b4bcc6';
    c.fillRect(0, 0, this.W, this.H);
    this.drawGrid();

    // 맵, 창고 내부
    const M = L.map, I = L.interior;
    this.rect(0, 0, M.w, M.d, null, '#78909c', [6, 4]);
    this.rect(I.x1, I.z1, I.x2, I.z2, '#d6dbe1', '#90a4ae');

    // 구역
    for (const a of L.areas) {
      this.rect(a.x1, a.z1, a.x2, a.z2, a.color, this.shade(a.color));
      this.text(a.name, a.x1 + 0.3, a.z1 + 0.3, { align: 'left', base: 'top', color: '#37474f', size: 12, bold: true });
    }
    this.centerRect(L.rest, '#e2d5ef', '#9575cd', '🛋️ 휴게실');
    this.centerRect(L.waiting, '#eeeeee', '#9e9e9e', '🪑 알바 대기 구역');

    // 벽
    c.lineCap = 'round';
    for (const w of L.walls) this.line(w.x1, w.z1, w.x2, w.z2, '#7b858f', Math.max(3, 0.3 * this.scale));

    this.drawBelts();
    this.drawFacilities();
    this.drawPoints();
    this.drawSelection();
  },

  drawGrid() {
    const c = this.ctx, a = this.s2w(0, 0), b = this.s2w(this.W, this.H);
    const step = this.scale >= 10 ? 1 : 5;
    c.lineWidth = 1;
    for (let x = Math.floor(a.x / step) * step; x <= b.x; x += step) {
      const [sx] = this.w2s(x, 0);
      c.strokeStyle = x % 5 === 0 ? 'rgba(0,0,0,.12)' : 'rgba(0,0,0,.05)';
      c.beginPath(); c.moveTo(sx + 0.5, 0); c.lineTo(sx + 0.5, this.H); c.stroke();
    }
    for (let z = Math.floor(a.z / step) * step; z <= b.z; z += step) {
      const [, sy] = this.w2s(0, z);
      c.strokeStyle = z % 5 === 0 ? 'rgba(0,0,0,.12)' : 'rgba(0,0,0,.05)';
      c.beginPath(); c.moveTo(0, sy + 0.5); c.lineTo(this.W, sy + 0.5); c.stroke();
    }
  },

  shade(hex) {
    const n = parseInt(hex.replace('#', ''), 16);
    const f = v => Math.max(0, Math.round(v * 0.7));
    return `rgb(${f(n >> 16 & 255)},${f(n >> 8 & 255)},${f(n & 255)})`;
  },

  rect(x1, z1, x2, z2, fill, stroke, dash) {
    const c = this.ctx, [a, b] = this.w2s(x1, z1), [e, f] = this.w2s(x2, z2);
    if (fill) { c.fillStyle = fill; c.fillRect(a, b, e - a, f - b); }
    if (stroke) {
      c.setLineDash(dash || []);
      c.strokeStyle = stroke; c.lineWidth = 1.5;
      c.strokeRect(a + 0.5, b + 0.5, e - a, f - b);
      c.setLineDash([]);
    }
  },

  centerRect(o, fill, stroke, label) {
    this.rect(o.x - o.w / 2, o.z - o.d / 2, o.x + o.w / 2, o.z + o.d / 2, fill, stroke);
    this.text(label, o.x, o.z, { size: 12 });
  },

  line(x1, z1, x2, z2, color, width, dash) {
    const c = this.ctx, [a, b] = this.w2s(x1, z1), [e, f] = this.w2s(x2, z2);
    c.strokeStyle = color; c.lineWidth = width;
    c.setLineDash(dash || []);
    c.beginPath(); c.moveTo(a, b); c.lineTo(e, f); c.stroke();
    c.setLineDash([]);
  },

  circle(x, z, r, fill, stroke, width, minPx) {
    const c = this.ctx, [a, b] = this.w2s(x, z);
    c.beginPath(); c.arc(a, b, Math.max(minPx || 2, r * this.scale), 0, Math.PI * 2);
    if (fill) { c.fillStyle = fill; c.fill(); }
    if (stroke) { c.strokeStyle = stroke; c.lineWidth = width || 1.5; c.stroke(); }
  },

  text(s, x, z, o = {}) {
    if (!Ed.show.labels && !o.force) return;
    const c = this.ctx, [a, b] = this.w2s(x, z);
    c.font = `${o.bold ? '700 ' : ''}${o.size || 11}px "Malgun Gothic", sans-serif`;
    c.textAlign = o.align || 'center';
    c.textBaseline = o.base || 'middle';
    if (o.halo !== false) {
      c.lineWidth = 3; c.strokeStyle = 'rgba(255,255,255,.85)';
      c.strokeText(s, a, b);
    }
    c.fillStyle = o.color || '#263238';
    c.fillText(s, a, b);
  },

  // 회전된 사각형 (rot 0에서 w = x 길이, d = z 길이). draw(ctx, w, d)는 로컬 좌표(픽셀)로 그린다
  rotRect(o, w, d, fill, stroke, extra) {
    const c = this.ctx, [a, b] = this.w2s(o.x, o.z), s = this.scale;
    c.save();
    c.translate(a, b);
    c.rotate((o.rot || 0) * Math.PI / 180);
    c.fillStyle = fill; c.fillRect(-w * s / 2, -d * s / 2, w * s, d * s);
    c.strokeStyle = stroke; c.lineWidth = 1.5; c.strokeRect(-w * s / 2, -d * s / 2, w * s, d * s);
    if (extra) extra(c, w * s, d * s, s);
    c.restore();
  },

  // 시설 앞면(로컬 +x)을 굵은 선으로
  frontEdge(c, w, d) {
    c.strokeStyle = '#263238'; c.lineWidth = 3;
    c.beginPath(); c.moveTo(w / 2, -d / 2); c.lineTo(w / 2, d / 2); c.stroke();
  },

  // 트럭: 로컬 +x 쪽이 뒷문, -x 쪽에 운전석
  truckExtra(color) {
    return (c, w, d, s) => {
      c.fillStyle = color;
      c.fillRect(-w / 2 - 1.5 * s, -d * 0.45, 1.5 * s, d * 0.9);
      c.strokeStyle = '#263238'; c.lineWidth = 3;
      c.beginPath(); c.moveTo(w / 2, -d / 2); c.lineTo(w / 2, d / 2); c.stroke();
    };
  },

  costTag(o) { return o.cost ? ` 🔒${o.cost}` : ''; },

  drawFacilities() {
    const L = Ed.L, Z = LayoutLib.SIZE;
    L.inTrucks.forEach((t, i) => {
      this.rotRect(t, Z.inTruck[0], Z.inTruck[1], 'rgba(255,243,224,.9)', '#f39c12', this.truckExtra('#f39c12'));
      this.text(`🚚 하차 ${i + 1}`, t.x, t.z, { size: 11, bold: true });
    });
    const s = L.staging;
    this.rotRect(s, 2.6, 2.6, '#d7b98c', '#8d6e63');
    this.text('📥 하차장', s.x, s.z, { bold: true });

    L.bins.forEach((b, i) => {
      const it = LayoutLib.item(L, b.item);
      const col = it ? U.hex(CONFIG.types[it.type].color) : '#9e9e9e';
      this.rotRect(b, Z.bin[0], Z.bin[1], col + '55', col, this.frontEdge);
      this.text(`${it ? it.emoji + ' ' + it.name : '?'}${this.costTag(b)}`, b.x, b.z, { size: 11 });
    });
    L.tables.forEach((t, i) => {
      this.rotRect(t, Z.table[0], Z.table[1], '#d9b38c', '#8d6e63', this.frontEdge);
      this.text(`🎁 포장대 ${i + 1}${this.costTag(t)}`, t.x, t.z, { size: 11 });
    });
    L.docks.forEach((d, i) => {
      const de = CONFIG.destinations.find(x => x.id === d.dest);
      const col = de ? U.hex(de.color) : '#9e9e9e';
      const door = { x: d.truck.x + LayoutLib.dir(d.truck.rot).x * 2.2, z: d.truck.z + LayoutLib.dir(d.truck.rot).z * 2.2 };
      this.line(d.x, d.z, door.x, door.z, col, 1.5, [4, 4]);
      this.rotRect(d.truck, Z.dockTruck[0], Z.dockTruck[1], col + '33', col, this.truckExtra(col));
      this.text(`🚛 ${de ? de.name : '?'} 트럭`, d.truck.x, d.truck.z, { size: 10, color: col });
      this.rotRect(d, Z.dock[0], Z.dock[1], col + '66', col);
      this.text(`🏁 ${de ? de.name : '?'}${this.costTag(d)}`, d.x, d.z, { size: 11, bold: true });
    });
    this.circle(L.trash.x, L.trash.z, 0.75, '#43a047', '#1b5e20');
    this.text('🗑️', L.trash.x, L.trash.z, { size: 12, force: true });
    this.circle(L.purifier.x, L.purifier.z, 0.5, '#eceff1', '#78909c');
    this.text('🌬️ 공기청정기', L.purifier.x, L.purifier.z + 1, { size: 10 });
    this.marker(L.entrance, '#5d4037', '🚪 출입구');
    this.marker(L.playerStart, '#1e5aa8', '👔 시작');
    for (const [st, p] of Object.entries(L.idle)) {
      const sd = CONFIG.stages.find(x => x.id === st);
      this.diamond(p, '#607d8b');
      this.text(sd ? sd.emoji : st, p.x, p.z - 0.8, { size: 11 });
    }
  },

  marker(p, color, label) {
    this.circle(p.x, p.z, 0.4, color, '#fff', 2);
    this.text(label, p.x, p.z + 0.9, { size: 10 });
  },

  diamond(p, color) {
    const c = this.ctx, [a, b] = this.w2s(p.x, p.z), r = Math.max(4, 0.35 * this.scale);
    c.fillStyle = color;
    c.beginPath(); c.moveTo(a, b - r); c.lineTo(a + r, b); c.lineTo(a, b + r); c.lineTo(a - r, b); c.closePath(); c.fill();
  },

  polyline(pts, color, width, dash) {
    const c = this.ctx;
    c.strokeStyle = color; c.lineWidth = width; c.lineJoin = 'round'; c.lineCap = 'butt';
    c.setLineDash(dash || []);
    c.beginPath();
    pts.forEach((p, i) => { const [a, b] = this.w2s(p.x, p.z); if (i) c.lineTo(a, b); else c.moveTo(a, b); });
    c.stroke();
    c.setLineDash([]);
  },

  // 흐름 방향 화살표 (구간마다 일정 간격)
  arrows(pts, color) {
    const c = this.ctx, gap = Math.max(2.5, this.px(46)), r = Math.max(3.5, 0.17 * this.scale);
    c.fillStyle = color;
    for (let i = 0; i < pts.length - 1; i++) {
      const A = pts[i], B = pts[i + 1], len = LayoutLib.dist(A, B);
      if (len < 0.3) continue;
      const ux = (B.x - A.x) / len, uz = (B.z - A.z) / len, n = Math.max(1, Math.floor(len / gap));
      for (let k = 0; k < n; k++) {
        const t = (k + 0.5) * len / n, [a, b] = this.w2s(A.x + ux * t, A.z + uz * t);
        c.beginPath();
        c.moveTo(a + ux * r, b + uz * r);
        c.lineTo(a - ux * r - uz * r, b - uz * r + ux * r);
        c.lineTo(a - ux * r + uz * r, b - uz * r - ux * r);
        c.closePath(); c.fill();
      }
    }
  },

  // 라인의 점 모양: ● 출발점, ◆ 꺾이는 점, ■ 도착점. 붙어 있으면 색이 차 있다
  linePoint(p, kind, color, attached, on) {
    const c = this.ctx, [a, b] = this.w2s(p.x, p.z), r = Math.max(6, 0.3 * this.scale);
    c.fillStyle = attached ? color : '#fff';
    c.strokeStyle = on ? '#ffb300' : color; c.lineWidth = on ? 3.5 : 2.5;
    c.beginPath();
    if (kind === 'start') c.arc(a, b, r, 0, Math.PI * 2);
    else if (kind === 'end') c.rect(a - r, b - r, r * 2, r * 2);
    else { c.moveTo(a, b - r); c.lineTo(a + r, b); c.lineTo(a, b + r); c.lineTo(a - r, b); c.closePath(); }
    c.fill(); c.stroke();
  },

  drawBelts() {
    const L = Ed.L, act = Ed.beltStage(), sel = Ed.sel;
    for (const st of LayoutLib.BELT_STAGES) {
      if (!Ed.show.belts && st !== act) continue;
      const col = BELT_COLORS[st], dim = act && act !== st;
      this.ctx.globalAlpha = dim ? 0.2 : 0.85;
      for (const ln of L.belts[st].lines) {
        const pts = LayoutLib.linePts(L, st, ln);
        const on = sel && (sel.t === 'line' || sel.t === 'lpt') && sel.st === st && sel.id === ln.id;
        this.polyline(pts, on ? '#ffb300' : col, Math.max(2, 0.42 * this.scale) + (on ? 3 : 0));
        if (!dim) this.arrows(pts, on ? '#5d4037' : '#ffffff');
        // 다른 라인에 붙은 곳 (합류/분기)
        if (act === st) {
          [['from', 0], ['to', pts.length - 1]].forEach(([end, k]) => {
            if (ln[end] && ln[end].line !== undefined) this.circle(pts[k].x, pts[k].z, 0.2, '#fff', col, 2, 4);
          });
        }
      }
      this.ctx.globalAlpha = 1;
    }
    if (!act) return;
    const col = BELT_COLORS[act];

    // 공정에 맞는 벨트 포트 (출 = 벨트 출구, 입 = 벨트 입구)
    for (const kind of ['src', 'dst']) {
      for (const c of Ed.stagePorts(L, act, kind)) {
        const [a, b] = this.w2s(c.p.x, c.p.z), r = Math.max(7, 0.3 * this.scale);
        const on = sel && sel.t === 'port' && Ed.same(sel, this.portRef(c.a));
        this.ctx.fillStyle = kind === 'src' ? col : '#fff';
        this.ctx.strokeStyle = on ? '#ffb300' : col; this.ctx.lineWidth = on ? 3.5 : 2;
        this.ctx.fillRect(a - r, b - r, r * 2, r * 2); this.ctx.strokeRect(a - r, b - r, r * 2, r * 2);
        this.text(kind === 'src' ? '출' : '입', c.p.x, c.p.z, { size: 10, bold: true, color: kind === 'src' ? '#fff' : col, force: true, halo: false });
      }
    }

    // 선택한 라인의 점
    if (sel && (sel.t === 'line' || sel.t === 'lpt') && sel.st === act) {
      const ln = Ed.get({ t: 'line', st: act, id: sel.id });
      if (ln) {
        const pts = LayoutLib.linePts(L, act, ln), last = pts.length - 1;
        pts.forEach((p, k) => {
          const kind = k === 0 ? 'start' : k === last ? 'end' : 'bend';
          const attached = (k === 0 && !!ln.from) || (k === last && !!ln.to);
          this.linePoint(p, kind, col, attached, sel.t === 'lpt' && sel.k === k);
        });
      }
    }

    // 그리는 중: 찍은 점 + 마우스까지 미리보기, 붙을 곳 표시
    const d = Ed.draw;
    if (d && d.st === act) {
      const m = this.beltCursor(), att = Ed.findAttach(L, act, this.mouse, 'dst', null);
      const end = att ? att.p : m;
      this.polyline(d.points.concat([end]), col, Math.max(2, 0.3 * this.scale), [8, 5]);
      d.points.forEach((p, k) => this.linePoint(p, k === 0 ? 'start' : 'bend', col, k === 0 && !!d.from, false));
      if (att) this.circle(att.p.x, att.p.z, 0.55, null, '#ffb300', 3);
    }
  },

  portRef(a) { return { t: 'port', of: a.fac === 'staging' ? { t: 'staging' } : { t: a.fac, i: a.i }, port: a.port }; },

  // 그리는 중 커서 위치 (격자 + Shift 가로/세로 정렬)
  beltCursor() {
    let x = Ed.snapV(this.mouse.x), z = Ed.snapV(this.mouse.z);
    const d = Ed.draw;
    if (this.mouse.shift && d) {
      const last = d.points[d.points.length - 1];
      if (Math.abs(x - last.x) > Math.abs(z - last.z)) z = last.z; else x = last.x;
    }
    return { x, z };
  },

  // 상호작용 지점 목록 (같은 위치끼리 묶음)
  pointGroups() {
    const L = Ed.L, list = [];
    const add = (ref, o) => {
      for (const role of ED_ROLES[ref.t]) {
        const p = o[role];
        if (!p) continue;
        let g = list.find(q => Math.abs(q.x - p.x) < 0.05 && Math.abs(q.z - p.z) < 0.05);
        if (!g) { g = { x: p.x, z: p.z, items: [] }; list.push(g); }
        g.items.push({ ref: { t: 'point', of: ref, role }, owner: o, role });
      }
    };
    L.inTrucks.forEach((o, i) => add({ t: 'inTruck', i }, o));
    add({ t: 'staging' }, L.staging);
    L.bins.forEach((o, i) => add({ t: 'bin', i }, o));
    L.tables.forEach((o, i) => add({ t: 'table', i }, o));
    L.docks.forEach((o, i) => add({ t: 'dock', i }, o));
    add({ t: 'trash' }, L.trash);
    return list;
  },

  drawPoints() {
    const groups = this.pointGroups(), R = CONFIG.interactRadius;
    const selOwner = Ed.sel && (Ed.sel.t === 'point' ? Ed.sel.of : Ed.sel);
    for (const g of groups) {
      const mine = selOwner && g.items.some(it => Ed.same(it.ref.of, selOwner));
      if (!Ed.show.ranges && !mine) continue;
      const overlap = groups.some(h => h !== g && Math.hypot(h.x - g.x, h.z - g.z) < R * 2);
      const col = ROLE_COLORS[g.items[0].role];
      this.circle(g.x, g.z, R, col + '18', overlap ? '#d63031' : col + '88', overlap ? 2.5 : 1.2);
      for (const it of g.items) this.line(it.owner.x, it.owner.z, g.x, g.z, col + '66', 1, [2, 3]);
      const label = [...new Set(g.items.map(it => ROLE_SHORT[it.role]))].join('');
      this.circle(g.x, g.z, 0.34, col, '#fff', 2, label.length > 1 ? 11 : 8);
      this.text(label, g.x, g.z, { size: label.length > 1 ? 9 : 11, color: '#fff', bold: true, force: true, halo: false });
    }
  },

  // 선택 표시와 크기 조절 핸들
  drawSelection() {
    const c = this.ctx, sel = Ed.sel;
    for (const [ref, color] of [[this.hover, 'rgba(255,202,40,.6)'], [sel, '#ffb300']]) {
      if (!ref || ref.t === 'handle') continue;
      const b = this.bounds(ref);
      if (!b) continue;
      const [a, d] = this.w2s(b.x1, b.z1), [e, f] = this.w2s(b.x2, b.z2);
      c.strokeStyle = color; c.lineWidth = ref === sel ? 3 : 2;
      c.strokeRect(a - 3, d - 3, e - a + 6, f - d + 6);
    }
    for (const h of this.handles()) {
      const [a, b] = this.w2s(h.x, h.z);
      c.fillStyle = h.ref.t === 'interior' ? '#78909c' : '#ffb300';
      c.fillRect(a - 5, b - 5, 10, 10);
      c.strokeStyle = '#fff'; c.lineWidth = 1.5; c.strokeRect(a - 5, b - 5, 10, 10);
    }
  },

  // 화면에 그릴 테두리 (선택 표시용)
  bounds(ref) {
    const o = Ed.get(ref);
    if (!o) return null;
    const pad = (p, r) => ({ x1: p.x - r, z1: p.z - r, x2: p.x + r, z2: p.z + r });
    switch (ref.t) {
      case 'area': return o;
      case 'wall': return { x1: Math.min(o.x1, o.x2), z1: Math.min(o.z1, o.z2), x2: Math.max(o.x1, o.x2), z2: Math.max(o.z1, o.z2) };
      case 'rest': case 'waiting': return { x1: o.x - o.w / 2, z1: o.z - o.d / 2, x2: o.x + o.w / 2, z2: o.z + o.d / 2 };
      case 'inTruck': return LayoutLib.footprint('inTruck', o);
      case 'staging': return LayoutLib.footprint('staging', o);
      case 'bin': return LayoutLib.footprint('bin', o);
      case 'table': return LayoutLib.footprint('table', o);
      case 'dock': return LayoutLib.footprint('dock', o);
      case 'dockTruck': return LayoutLib.footprint('dockTruck', o);
      case 'trash': return pad(o, 0.8);
      case 'point': return pad(o, 0.4);
      case 'line': case 'lpt': case 'port': return null;   // 벨트는 자체 강조로 표시
      default: return pad(o, 0.5);
    }
  },

  // 크기 조절 핸들: 선택한 사각형/벽 + 창고 내부 모서리(항상)
  handles() {
    const out = [], sel = Ed.sel;
    const corners = (ref, r) => [[r.x1, r.z1], [r.x2, r.z1], [r.x2, r.z2], [r.x1, r.z2]]
      .forEach(([x, z], i) => out.push({ ref, handle: i, x, z }));
    if (!Ed.beltStage()) corners({ t: 'interior' }, Ed.L.interior);
    if (sel && ['area', 'rest', 'waiting'].includes(sel.t)) corners(sel, this.bounds(sel));
    if (sel && sel.t === 'wall') {
      const w = Ed.get(sel);
      out.push({ ref: sel, handle: 0, x: w.x1, z: w.z1 }, { ref: sel, handle: 1, x: w.x2, z: w.z2 });
    }
    return out;
  },

  // ---------- 클릭 판정 (위에 그려진 것부터) ----------
  inRot(p, o, w, d) {
    const a = -(o.rot || 0) * Math.PI / 180, dx = p.x - o.x, dz = p.z - o.z;
    const lx = dx * Math.cos(a) - dz * Math.sin(a), lz = dx * Math.sin(a) + dz * Math.cos(a);
    return Math.abs(lx) <= w / 2 && Math.abs(lz) <= d / 2;
  },

  hitTest(x, z) {
    const L = Ed.L, p = { x, z }, near = (q, r) => Math.hypot(q.x - x, q.z - z) <= Math.max(r, this.px(8));
    // 핸들
    for (const h of this.handles().reverse()) {
      if (Math.abs(h.x - x) <= this.px(7) && Math.abs(h.z - z) <= this.px(7)) return { t: 'handle', ref: h.ref, handle: h.handle };
    }
    // 벨트 모드: 선택한 라인의 점 → 공정 포트 → 그 공정의 라인
    const st = Ed.beltStage();
    if (st) {
      const sel = Ed.sel;
      if (sel && (sel.t === 'line' || sel.t === 'lpt') && sel.st === st) {
        const ln = Ed.get({ t: 'line', st, id: sel.id });
        if (ln) {
          const pts = LayoutLib.linePts(L, st, ln);
          for (let k = pts.length - 1; k >= 0; k--) if (near(pts[k], 0.35)) return { t: 'lpt', st, id: ln.id, k };
        }
      }
      for (const kind of ['src', 'dst']) {
        for (const c of Ed.stagePorts(L, st, kind)) if (near(c.p, 0.35)) return { ...this.portRef(c.a), kind, a: c.a, p: c.p };
      }
      const lines = L.belts[st].lines;
      for (let i = lines.length - 1; i >= 0; i--) {
        const pr = LayoutLib.projectOn(LayoutLib.linePts(L, st, lines[i]), p);
        if (pr && pr.dist <= Math.max(0.3, this.px(6))) return { t: 'line', st, id: lines[i].id, seg: pr.seg, at: pr.point };
      }
      return null;
    }
    // 상호작용 지점
    const selOwner = Ed.sel && (Ed.sel.t === 'point' ? Ed.sel.of : Ed.sel);
    for (const g of this.pointGroups().reverse()) {
      if (!near(g, 0.4)) continue;
      const mine = g.items.find(it => selOwner && Ed.same(it.ref.of, selOwner));
      if (!Ed.show.ranges && !mine) continue;
      // 같은 위치에 여러 지점이 있으면 선택한 시설의 지점, 같은 시설이면 첫 번째 역할
      return (mine || g.items[0]).ref;
    }
    // 작은 표시
    for (const t of ['entrance', 'playerStart', 'purifier']) if (near(L[t], 0.6)) return { t };
    for (const s of Object.keys(L.idle)) if (near(L.idle[s], 0.5)) return { t: 'idle', st: s };
    if (near(L.trash, 0.8)) return { t: 'trash' };
    // 시설
    const Z = LayoutLib.SIZE;
    for (let i = L.bins.length - 1; i >= 0; i--) if (this.inRot(p, L.bins[i], Z.bin[0], Z.bin[1])) return { t: 'bin', i };
    for (let i = L.tables.length - 1; i >= 0; i--) if (this.inRot(p, L.tables[i], Z.table[0], Z.table[1])) return { t: 'table', i };
    for (let i = L.docks.length - 1; i >= 0; i--) {
      if (this.inRot(p, L.docks[i], Z.dock[0], Z.dock[1])) return { t: 'dock', i };
      if (this.inRot(p, L.docks[i].truck, Z.dockTruck[0] + 1.5, Z.dockTruck[1])) return { t: 'dockTruck', i };
    }
    if (this.inRot(p, L.staging, 2.6, 2.6)) return { t: 'staging' };
    for (let i = L.inTrucks.length - 1; i >= 0; i--) if (this.inRot(p, L.inTrucks[i], Z.inTruck[0] + 1.5, Z.inTruck[1])) return { t: 'inTruck', i };
    for (let i = L.walls.length - 1; i >= 0; i--) {
      const w = L.walls[i];
      if (this.segDist(p, { x: w.x1, z: w.z1 }, { x: w.x2, z: w.z2 }) <= Math.max(0.3, this.px(6))) return { t: 'wall', i };
    }
    for (const t of ['rest', 'waiting']) {
      const o = L[t];
      if (Math.abs(x - o.x) <= o.w / 2 && Math.abs(z - o.z) <= o.d / 2) return { t };
    }
    for (let i = L.areas.length - 1; i >= 0; i--) {
      const a = L.areas[i];
      if (x >= a.x1 && x <= a.x2 && z >= a.z1 && z <= a.z2) return { t: 'area', i };
    }
    return null;
  },

  // ---------- 입력 ----------
  local(e) {
    const r = this.cv.getBoundingClientRect();
    return { sx: e.clientX - r.left, sy: e.clientY - r.top };
  },

  onDown(e) {
    const { sx, sy } = this.local(e), p = this.s2w(sx, sy);
    this.cv.setPointerCapture(e.pointerId);
    const d = this.drag = { kind: null, cx: e.clientX, cy: e.clientY, start: p, moved: false, base: null, ox: this.ox, oz: this.oz };
    if (e.button === 1 || (e.button === 0 && this.spaceDown)) { d.kind = 'pan'; return; }
    if (e.button === 2) {
      // 벨트 그리는 중 우클릭 = 떨어진 끝으로 마치기
      this.drag = null;
      if (Ed.draw) Ed.finishDraw(null);
      return;
    }
    if (e.button !== 0) { this.drag = null; return; }
    const hit = d.hit = this.hitTest(p.x, p.z);
    if (Ed.beltStage()) {
      // 클릭할 때 무엇을 할지 정한다 (끌면 화면 이동, 점·포트는 끌면 이동)
      if (Ed.draw) d.kind = 'drawClick';
      else if (hit && (hit.t === 'lpt' || hit.t === 'port')) {
        Ed.select(hit.t === 'lpt' ? hit : this.portRef(hit.a));
        d.hit = Ed.sel;
        d.port = hit.t === 'port' ? hit : null;
        d.kind = 'move';
      } else if (hit && hit.t === 'line') d.kind = e.altKey ? 'branch' : 'lineClick';
      else d.kind = 'emptyBelt';
      return;
    }
    if (!hit) { Ed.select(null); d.kind = 'pan'; return; }
    if (hit.t === 'handle') { d.kind = 'handle'; return; }
    Ed.select(hit);
    d.kind = 'move';
  },

  onMove(e) {
    const { sx, sy } = this.local(e), p = this.s2w(sx, sy);
    Object.assign(this.mouse, { x: p.x, z: p.z, sx, sy, shift: e.shiftKey });
    Panels.coords(p);
    const d = this.drag;
    if (!d) {
      if (e.target !== this.cv) return;
      const h = this.hitTest(p.x, p.z);
      this.hover = h && h.t !== 'handle' ? h : null;
      this.cv.style.cursor = h ? (h.t === 'handle' ? 'nwse-resize' : Ed.beltStage() ? 'crosshair' : 'move') : (Ed.beltStage() ? 'crosshair' : 'default');
      this.render();
      return;
    }
    if (!d.moved && Math.hypot(e.clientX - d.cx, e.clientY - d.cy) > 3) d.moved = true;
    if (!d.moved) return;
    if (['emptyBelt', 'drawClick', 'lineClick', 'branch'].includes(d.kind)) d.kind = 'pan';
    if (d.kind === 'pan') {
      this.ox = d.ox - (e.clientX - d.cx) / this.scale;
      this.oz = d.oz - (e.clientY - d.cy) / this.scale;
      this.render();
      return;
    }
    if (!d.base) { d.base = LayoutLib.clone(Ed.L); d.snap = Ed.snapshot(); }
    if (d.kind === 'handle') Ed.dragHandle(d.base, d.hit.ref, d.hit.handle, p.x, p.z);
    else Ed.dragMove(d.base, d.hit, p.x - d.start.x, p.z - d.start.z, { alone: e.altKey });
    Panels.refreshInspector();
    this.render();
  },

  onUp(e) {
    const d = this.drag;
    this.drag = null;
    if (!d) return;
    if (d.base) {
      Ed.pushUndo(d.snap);
      Ed.afterChange();
      return;
    }
    const st = Ed.beltStage();
    if (d.moved || !st) return;
    const h = d.hit;
    switch (d.kind) {
      case 'drawClick': Ed.drawClick(d.start.x, d.start.z, e.shiftKey); break;
      case 'emptyBelt': Ed.startDraw(st, { x: Ed.snapV(d.start.x), z: Ed.snapV(d.start.z) }, null); break;
      case 'branch': Ed.startDraw(st, h.at, { line: h.id }); break;
      case 'lineClick': {
        const on = Ed.sel && (Ed.sel.t === 'line' || Ed.sel.t === 'lpt') && Ed.sel.st === st && Ed.sel.id === h.id;
        if (on) Ed.insertBend(st, h.id, h.seg, h.at);
        else Ed.select({ t: 'line', st, id: h.id });
        break;
      }
      case 'move':
        // 출구 포트를 클릭하면 거기서 새 라인 그리기 시작
        if (d.port && d.port.kind === 'src') Ed.startDraw(st, d.port.p, d.port.a);
        break;
    }
  },

  onDblClick() {
    if (Ed.draw) Ed.finishDraw(null);
  },

  onWheel(e) {
    e.preventDefault();
    const { sx, sy } = this.local(e), before = this.s2w(sx, sy);
    this.scale = U.clamp(this.scale * (e.deltaY > 0 ? 1 / 1.15 : 1.15), 3, 120);
    this.ox = before.x - sx / this.scale;
    this.oz = before.z - sy / this.scale;
    Panels.coords(before);
    this.render();
  },

  onKey(e) {
    const tag = e.target.tagName;
    if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA') return;
    const ctrl = e.ctrlKey || e.metaKey, sel = Ed.sel;
    if (e.code === 'Space') { this.spaceDown = true; e.preventDefault(); return; }
    if (ctrl && e.code === 'KeyZ') { e.preventDefault(); if (e.shiftKey) Ed.redo(); else Ed.undo(); return; }
    if (ctrl && e.code === 'KeyY') { e.preventDefault(); Ed.redo(); return; }
    if (ctrl && e.code === 'KeyD') { e.preventDefault(); Ed.duplicate(sel); return; }
    if (ctrl) return;
    if (Ed.draw) {
      if (e.code === 'Enter') { Ed.finishDraw(null); return; }
      if (e.code === 'Backspace') { e.preventDefault(); Ed.undoDrawPoint(); return; }
      if (e.code === 'Escape') { Ed.cancelDraw(); return; }
    }
    switch (e.code) {
      case 'Delete': case 'Backspace': e.preventDefault(); Ed.remove(sel); return;
      case 'KeyR': Ed.rotate(sel); return;
      case 'KeyF': this.fit(); return;
      case 'Digit1': Ed.setMode('select'); return;
      case 'Digit2': Ed.setMode('belt:sort'); return;
      case 'Digit3': Ed.setMode('belt:transfer'); return;
      case 'Digit4': Ed.setMode('belt:outbound'); return;
      case 'Escape':
        if (Ed.sel) Ed.select(null);
        else if (Ed.beltStage()) Ed.setMode('select');
        return;
    }
    const arrows = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
    if (arrows[e.code] && sel && sel.t !== 'line') {
      e.preventDefault();
      const s = Ed.snap || 0.1, [dx, dz] = arrows[e.code];
      Ed.change(L => {
        const o = Ed.get(sel, L);
        if (['point', 'port', 'lpt'].includes(sel.t)) Ed.shift(o, dx * s, dz * s);
        else Ed.moveBy(L, sel, dx * s, dz * s);
      });
    }
  },
};
