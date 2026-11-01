// 상차 미니게임 화면 (차곡차곡 이식)
// 입력은 "드래그 시작 / 회전 / 드롭 / 제거" 명령으로만 로직(LoadLogic)에 전달한다.

const LoadingUI = {
  dock: null,
  drag: null,
  CS: 56,       // 칸 크기(px)

  init() {
    this.root = document.getElementById('loading-overlay');
    this.root.addEventListener('contextmenu', e => e.preventDefault());
    this.root.addEventListener('click', e => {
      const b = e.target.closest('[data-ld]');
      if (!b) return;
      if (b.dataset.ld === 'depart') this.depart();
      if (b.dataset.ld === 'reset') this.resetAll();
      if (b.dataset.ld === 'close') this.close();
    });
    this.root.addEventListener('pointerdown', e => this.onPointerDown(e));
    window.addEventListener('keydown', e => {
      if (!this.dock) return;
      if (e.code === 'KeyR') { e.preventDefault(); this.rotate(); }
      if (e.code === 'Escape') { if (this.drag) this.cancelDrag(); else this.close(); }
    });
  },

  canOpen(d) {
    const tr = d && d.truck;
    return !!(d && d.unlocked && tr && tr.state === 'present' && tr.mode !== 'auto' && !d.loader && !S.isAuto('load'));
  },

  open(d) {
    if (!this.canOpen(d)) { UI.toast('지금은 상차 패널을 열 수 없습니다'); return; }
    this.dock = d;
    G.paused = true;
    UI.keys.clear();
    this.root.classList.remove('hidden');
    this.render();
  },

  close() {
    if (!this.dock) return;
    this.cancelDrag();
    const tr = this.dock.truck;
    if (tr && tr.mode === 'manual' && !tr.placements.length) tr.mode = null;
    this.dock = null;
    G.paused = false;
    this.root.classList.add('hidden');
    this.root.innerHTML = '';
  },

  get truck() { return this.dock && this.dock.truck; },

  // ---------- 명령 ----------
  place(box, cells, placement) {
    const tr = this.truck;
    if (placement) {
      placement.cells = cells;
    } else {
      const i = this.dock.waiting.indexOf(box);
      if (i < 0) return;
      this.dock.waiting.splice(i, 1);
      tr.placements.push({ box, cells });
    }
    tr.mode = 'manual';
  },

  remove(placement) {
    const tr = this.truck;
    tr.placements.splice(tr.placements.indexOf(placement), 1);
    this.dock.waiting.push(placement.box);
    this.render();
  },

  resetAll() {
    const tr = this.truck;
    for (const p of tr.placements) this.dock.waiting.push(p.box);
    tr.placements = [];
    this.render();
  },

  depart() {
    if (!this.truck.placements.length) return;
    Docks.depart(this.dock);
  },

  rotate() {
    if (!this.drag) return;
    this.drag.cells = LoadLogic.rotate(this.drag.cells);
    this.renderGhost();
    this.updateDrag(this.drag.px, this.drag.py);
  },

  // ---------- 드래그 ----------
  onPointerDown(e) {
    if (!this.dock || this.drag) return;
    const piece = e.target.closest('[data-pl]');
    const card = e.target.closest('[data-box]');
    if (piece && e.button === 2) {
      this.remove(this.truck.placements[+piece.dataset.pl]);
      return;
    }
    if (e.button !== 0) return;
    if (piece) {
      const pl = this.truck.placements[+piece.dataset.pl];
      const cells = LoadLogic.normalize(pl.cells);
      const anchor = pl.cells.findIndex(([x, y]) => x === +piece.dataset.x && y === +piece.dataset.y);
      this.startDrag(e, pl.box, cells, Math.max(0, anchor), pl);
    } else if (card) {
      const box = this.dock.waiting.find(b => b.id === +card.dataset.box);
      if (box) this.startDrag(e, box, box.cells.map(c => c.slice()), 0, null);
    }
  },

  startDrag(e, box, cells, anchor, placement) {
    e.preventDefault();
    this.drag = { box, cells, anchor, placement, px: e.clientX, py: e.clientY, target: null, valid: false };
    this.ghost = U.el('div', 'ld-ghost');
    document.body.appendChild(this.ghost);
    this.renderGhost();
    this._move = ev => this.updateDrag(ev.clientX, ev.clientY);
    this._up = () => this.endDrag();
    // 드래그 중 우클릭 = 회전 (왼쪽 버튼을 누른 채 오른쪽을 누르면 pointerdown이 아니라 mousedown만 온다)
    this._rot = ev => {
      if (ev.button !== 2) return;
      ev.preventDefault();
      ev.stopPropagation();
      this.rotate();
    };
    this._noMenu = ev => ev.preventDefault();
    window.addEventListener('pointermove', this._move);
    window.addEventListener('pointerup', this._up);
    window.addEventListener('mousedown', this._rot, true);
    window.addEventListener('contextmenu', this._noMenu, true);
    this.render();
    this.updateDrag(e.clientX, e.clientY);
  },

  updateDrag(px, py) {
    const d = this.drag;
    if (!d) return;
    d.px = px; d.py = py;
    const CS = this.CS, [ax, ay] = d.cells[d.anchor];
    this.ghost.style.left = (px - ax * CS - CS / 2) + 'px';
    this.ghost.style.top = (py - ay * CS - CS / 2) + 'px';
    const grid = this.root.querySelector('.ld-grid');
    const r = grid.getBoundingClientRect();
    const inside = px >= r.left && px < r.right && py >= r.top && py < r.bottom;
    if (inside) {
      const gx = Math.floor((px - r.left) / CS), gy = Math.floor((py - r.top) / CS);
      d.target = d.cells.map(([x, y]) => [x - ax + gx, y - ay + gy]);
      d.valid = LoadLogic.canPlace(this.truck, d.target, d.placement);
    } else {
      d.target = null;
      d.valid = false;
    }
    this.renderPreview();
  },

  endDrag() {
    const d = this.drag;
    if (!d) return;
    if (d.valid && d.target) this.place(d.box, d.target, d.placement);
    this.cancelDrag();
    this.render();
  },

  cancelDrag() {
    if (this._move) window.removeEventListener('pointermove', this._move);
    if (this._up) window.removeEventListener('pointerup', this._up);
    if (this._rot) window.removeEventListener('mousedown', this._rot, true);
    if (this._noMenu) window.removeEventListener('contextmenu', this._noMenu, true);
    if (this.ghost) this.ghost.remove();
    this.ghost = null;
    this.drag = null;
  },

  // ---------- 렌더 ----------
  boxColor(box) { return U.hex(CONFIG.types[box.primary].color); },

  boxItems(box) {
    return Object.entries(box.items).map(([k, q]) => `${CONFIG.items[+k].emoji}x${q}`).join(' ');
  },

  miniShape(cells, size, color) {
    const w = Math.max(...cells.map(c => c[0])) + 1, h = Math.max(...cells.map(c => c[1])) + 1;
    return `<div class="mini-shape" style="width:${w * size}px;height:${h * size}px">` +
      cells.map(([x, y]) => `<i style="left:${x * size}px;top:${y * size}px;width:${size - 2}px;height:${size - 2}px;background:${color}"></i>`).join('') +
      '</div>';
  },

  renderGhost() {
    const d = this.drag;
    this.ghost.innerHTML = this.miniShape(d.cells, this.CS, this.boxColor(d.box));
  },

  render() {
    const d = this.dock, tr = this.truck;
    if (!d || !tr) return;
    const CS = this.CS, dest = CONFIG.destinations[d.idx];
    const pay = LoadLogic.payout(tr), f = pay.fill;
    const occ = LoadLogic.occupancy(tr);

    // 짐칸 칸
    let cells = '';
    for (let y = 0; y < tr.h; y++) for (let x = 0; x < tr.w; x++) {
      const k = LoadLogic.key(x, y);
      const cls = tr.locked.has(k) ? 'locked' : tr.bonus.has(k) ? 'bonus' : '';
      cells += `<div class="cell ${cls}" style="left:${x * CS}px;top:${y * CS}px;width:${CS}px;height:${CS}px">${cls === 'locked' ? '✖' : cls === 'bonus' ? '⭐' : ''}</div>`;
    }

    // 실린 박스
    let pieces = '';
    tr.placements.forEach((p, i) => {
      const set = new Set(p.cells.map(([x, y]) => LoadLogic.key(x, y)));
      const dragging = this.drag && this.drag.placement === p;
      p.cells.forEach(([x, y], ci) => {
        const edge = [[0, -1, 'top'], [1, 0, 'right'], [0, 1, 'bottom'], [-1, 0, 'left']]
          .filter(([dx, dy]) => !set.has(LoadLogic.key(x + dx, y + dy))).map(e => 'b-' + e[2]).join(' ');
        const n = occ.get(LoadLogic.key(x, y));
        pieces += `<div class="piece ${edge} ${dragging ? 'dragging' : ''}" data-pl="${i}" data-x="${x}" data-y="${y}"
          style="left:${x * CS}px;top:${y * CS}px;width:${CS}px;height:${CS}px;background:${this.boxColor(p.box)}">
          ${ci === 0 ? `<span class="p-label">${p.box.types.map(t => CONFIG.types[t].emoji).join('')}</span>` : ''}
          ${n > 1 ? `<span class="p-over">×${n}</span>` : ''}</div>`;
      });
    });

    // 랭크
    const rn = CONFIG.loading.rankNames, th = CONFIG.loading.rankThresholds;
    const ranks = Object.entries(pay.ranks).map(([t, r]) => {
      const ty = CONFIG.types[t], next = th[r.rank];
      return `<div class="rank-row"><span>${ty.emoji} ${ty.name}</span><span class="rank r${r.rank}">${rn[r.rank]}</span>
        <small>${r.count}개${next ? ` (다음 ${next - r.count})` : ''}</small></div>`;
    }).join('');

    // 특수 목표: 달성하면 체크, 박스를 빼서 조건이 깨지면 해제
    const doneN = pay.objectives.filter(o => o.done).length;
    const objs = pay.objectives.map(o => `
      <div class="obj ${o.done ? 'done' : ''}">
        <span class="chk">${o.done ? '✔' : ''}</span>
        <span class="obj-text">${LoadLogic.objectiveText(o)} <small>(${o.now})</small></span>
        <span class="obj-bonus">+${o.bonus}%p</span>
      </div>`).join('') || '<p class="hint">목표 없음</p>';

    const list = d.waiting.map(b => `
      <div class="box-card" data-box="${b.id}">
        ${this.miniShape(b.cells, 16, this.boxColor(b))}
        <div class="bc-info"><b>#${b.id}</b> ${b.types.map(t => CONFIG.types[t].emoji).join('')} <span>${b.cells.length}칸</span><br>
          <small>${this.boxItems(b)}</small><br><small>${U.money(b.reward)}</small></div>
      </div>`).join('') || '<p class="hint">출고 대기 박스가 없습니다.<br>포장된 박스를 이 도크로 옮겨오세요.</p>';

    const L = CONFIG.loading, rate = Math.round(pay.rate);
    const breakdown = pay.objBonus ? `칸 ${Math.round(f.rate)}% + 목표 ${pay.objBonus}%p` : `${f.filled}/${f.total}칸`;
    this.root.innerHTML = `
      <div class="ld-panel">
        <div class="ld-top">
          <div class="ld-title">🚛 <b style="color:${U.hex(dest.color)}">${dest.name}</b>행 · ${tr.def.name} 트럭 (${tr.w}×${tr.h})</div>
          <div class="ld-rate">적재율 <b class="${rate > L.maxRate ? 'over' : rate >= 100 ? 'good' : ''}">${rate}%</b> / 100% ${U.bar(pay.rate / L.maxRate, rate > L.maxRate ? 'over' : '')}
            <small>(${breakdown} · 가득 = ${L.fullRate}%)</small></div>
          <div class="ld-pay">예상 대금 <b>${U.money(pay.total)}</b>
            <small>= 기본 ${pay.base} × 적재 ${pay.fillMult.toFixed(2)} × 랭크 ${pay.rankMult.toFixed(2)}</small></div>
        </div>
        <div class="ld-body">
          <div class="ld-left">
            <h4>🎯 특수 목표 <small>${doneN}/${pay.objectives.length} 완료 · 적재율 +${pay.objBonus}%p</small></h4>
            <div class="objectives">${objs}</div>
            <p class="hint">달성 보너스는 적재율에 더해지며 ${L.maxRate}% 상한을 넘을 수 있습니다.</p>
            <h4>타입 랭크</h4>${ranks}
            <p class="hint">혼합 박스는 들어간 타입마다 1개씩 셉니다. D 이상 1단계당 +${L.rankBonusPerStep * 100}%</p>
            <h4>적재 배율</h4>
            <p class="hint">100% 미만 x0.5~1.0<br>${L.fillMult.slice().reverse().map(m => `${m.min}%↑ x${m.mult.toFixed(1)}`).join(' · ')}<br>${L.maxRate}% 초과(목표 보너스) 1%p당 +${L.overPerPoint}</p>
            <h4>업그레이드</h4>
            <p class="hint">🗜️ 압축 적재 ${f.overlap || 0}/${G.loadUpg.compress}칸<br>⭐ 우선 적재 칸 ${f.bonusCovered}/${tr.bonus.size} (+${CONFIG.loading.bonusCellPercent}%씩)</p>
          </div>
          <div class="ld-center">
            <div class="truck-cab">🚚 운전석</div>
            <div class="ld-grid" style="width:${tr.w * CS}px;height:${tr.h * CS}px">${cells}${pieces}<div class="ld-preview"></div></div>
            <div class="truck-door">⬇ 짐칸 문</div>
          </div>
          <div class="ld-right"><h4>출고 대기 박스 (${d.waiting.length})</h4><div class="ld-list">${list}</div></div>
        </div>
        <div class="ld-bottom">
          <button class="go" data-ld="depart" ${tr.placements.length ? '' : 'disabled'}>🚚 출발</button>
          <button data-ld="reset">↺ 초기화</button>
          <button data-ld="close">✕ 닫기</button>
          <span class="ld-help">드래그: 배치 · 드래그 중 <b>R</b> 또는 <b>우클릭</b>: 회전 · 좌클릭 드래그: 이동 · 우클릭: 회수 · ⏸ 게임 시간 정지 중</span>
        </div>
      </div>`;
    if (this.drag) this.renderPreview();
  },

  renderPreview() {
    const layer = this.root.querySelector('.ld-preview');
    if (!layer) return;
    const d = this.drag, tr = this.truck, CS = this.CS;
    if (!d || !d.target) { layer.innerHTML = ''; return; }
    layer.innerHTML = d.target.filter(([x, y]) => x >= 0 && y >= 0 && x < tr.w && y < tr.h)
      .map(([x, y]) => `<i class="${d.valid ? 'ok' : 'no'}" style="left:${x * CS}px;top:${y * CS}px;width:${CS}px;height:${CS}px"></i>`).join('');
  },
};
