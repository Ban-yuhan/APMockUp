// HUD, 패널(시설/업그레이드/알바), 라벨 내용, 입력, 간식 드래그

const $ = sel => document.querySelector(sel);

// 구매/업그레이드
const Shop = {
  // 해금 비용은 배치 데이터의 슬롯에 있다
  buyBin(i) {
    const b = G.bins[i], it = CONFIG.items[i];
    if (!b.slot || b.unlocked || !S.spend(b.slot.cost || 0)) return;
    b.unlocked = true;
    UI.toast(`📦 ${it.emoji} ${it.name} 보관함 해금! 이제 입고 트럭에 실려 옵니다`);
  },
  buyTable(i) {
    const t = G.tables[i];
    if (t.unlocked || !S.spend(t.slot.cost || 0)) return;
    t.unlocked = true;
    UI.toast(`🎁 포장대 ${i + 1} 해금`);
  },
  buyDock(i) {
    const d = G.docks[i], dest = CONFIG.destinations[i];
    if (!d.slot || d.unlocked || !S.spend(d.slot.cost || 0)) return;
    d.unlocked = true;
    d.truckTimer = 1;
    UI.toast(`🏁 ${dest.name} 출고 도크 해금! ${dest.name}행 주문이 들어옵니다`);
  },
  upRest() {
    const next = G.care.rest + 1, c = CONFIG.care;
    if (next >= c.tierNames.length || !S.spend(c.rest.costs[next])) return;
    G.care.rest = next;
    UI.toast(`🛋️ 휴게실 → ${c.tierNames[next]} 휴게실`);
  },
  upPurifier() {
    const next = G.care.purifier + 1, c = CONFIG.care;
    if (next >= c.tierNames.length || !S.spend(c.purifier.costs[next])) return;
    G.care.purifier = next;
    UI.toast(`🌬️ 공기청정기 → ${c.tierNames[next]} (발동 ${Math.round(c.purifier.chance[next] * 100)}%)`);
  },
  upStage(id) {
    const def = S.stageDef(id), lv = G.stages[id];
    if (lv >= def.costs.length || !S.spend(def.costs[lv])) return;
    G.stages[id] = lv + 1;
    if (lv + 1 >= 4) {
      for (const a of G.albas) if (a.stage === id) { Albas.reset(a); a.stage = null; }
      UI.toast(`🤖 ${def.name} 자동화 완료! 배치된 알바는 대기중 슬롯으로 이동`);
    } else {
      UI.toast(`⬆️ ${def.name} Lv${lv + 1} (배치 슬롯 ${lv + 1}명)`);
    }
  },
  upFacility(key) {
    const f = CONFIG.facility[key], lv = G.fac[key];
    if (lv + 1 >= f.values.length || !S.spend(f.costs[lv + 1])) return;
    G.fac[key] = lv + 1;
    UI.toast(`${f.emoji} ${f.name} → ${f.values[lv + 1]}${f.unit}`);
  },
  upLoad(key) {
    const u = CONFIG.loading.upgrades[key], lv = G.loadUpg[key];
    if (lv >= u.max || !S.spend(u.costs[lv])) return;
    G.loadUpg[key] = lv + 1;
    UI.toast(`${u.emoji} ${u.name} Lv${lv + 1}`);
  },
};

const UI = {
  keys: new Set(),
  panelName: null,
  panelSig: '',
  timers: { hud: 0, labels: 0, panel: 0 },
  albaLabels: new Map(),

  init() {
    Labels.init($('#labels'));
    this.buildHud();
    this.buildLabels();
    this.bindInput();
  },

  // ---------- 입력 ----------
  bindInput() {
    window.addEventListener('keydown', e => {
      if (LoadingUI.dock) return;
      if (e.target.tagName === 'INPUT' || e.target.tagName === 'SELECT') return;
      this.keys.add(e.code);
      if (e.code === 'KeyE') {
        const d = Player.loadableDock();
        if (d) LoadingUI.open(d);
      }
      if (e.code === 'Escape') this.closePanel();
    });
    window.addEventListener('keyup', e => this.keys.delete(e.code));
    window.addEventListener('blur', () => this.keys.clear());

    const canvas = $('#view');
    canvas.addEventListener('pointerdown', e => {
      if (e.button !== 0 || LoadingUI.dock) return;
      const p = World.groundPoint(e.clientX, e.clientY);
      if (!p) return;
      const bd = LAYOUT.bounds;
      G.player.target = { x: U.clamp(p.x, bd.minX, bd.maxX), z: U.clamp(p.z, bd.minZ, bd.maxZ) };
    });
    canvas.addEventListener('wheel', e => {
      e.preventDefault();
      World.zoomBy(e.deltaY);
    }, { passive: false });

    $('#labels').addEventListener('click', e => this.onAction(e));
    $('#panel').addEventListener('click', e => this.onAction(e));
    $('#panel').addEventListener('change', e => {
      const id = e.target.dataset.assign;
      if (!id) return;
      const a = G.albas.find(a => a.id === +id);
      if (a) Albas.assign(a, e.target.value || null);
      this.renderPanel(true);
    });
    $('#menu-bar').addEventListener('click', e => this.onAction(e));
    $('#clock-box').addEventListener('click', e => {
      const sp = e.target.dataset.speed;
      if (sp === undefined) return;
      if (sp === '0') G.userPaused = !G.userPaused;
      else { G.userPaused = false; G.speed = +sp; }
      this.refreshHud();
    });
    window.addEventListener('resize', () => World.resize());
  },

  // 화면 기준 WASD → 월드 방향 (카메라가 살짝 비스듬해서 보정)
  inputVector() {
    let ix = 0, iz = 0;
    const k = this.keys;
    if (k.has('KeyW') || k.has('ArrowUp')) iz -= 1;
    if (k.has('KeyS') || k.has('ArrowDown')) iz += 1;
    if (k.has('KeyA') || k.has('ArrowLeft')) ix -= 1;
    if (k.has('KeyD') || k.has('ArrowRight')) ix += 1;
    if (!ix && !iz) return { x: 0, z: 0 };
    const yaw = Math.atan2(World.camera.position.x - World.center.x, World.camera.position.z - World.center.z);
    const c = Math.cos(yaw), s = Math.sin(yaw);
    return { x: ix * c + iz * s, z: -ix * s + iz * c };
  },

  onAction(e) {
    const el = e.target.closest('[data-act]');
    if (!el) return;
    const act = el.dataset.act, arg = el.dataset.arg;
    switch (act) {
      case 'panel': this.openPanel(arg); return;
      case 'close': this.closePanel(); return;
      case 'debug': Debug.toggle(); return;
      case 'buy-bin': Shop.buyBin(+arg); break;
      case 'buy-table': Shop.buyTable(+arg); break;
      case 'buy-dock': Shop.buyDock(+arg); break;
      case 'up-rest': Shop.upRest(); break;
      case 'up-purifier': Shop.upPurifier(); break;
      case 'up-stage': Shop.upStage(arg); break;
      case 'up-load': Shop.upLoad(arg); break;
      case 'up-fac': Shop.upFacility(arg); break;
      case 'hire': Albas.hire(+arg); break;
      case 'reroll':
        if (S.spend(CONFIG.alba.rerollCost)) Albas.rerollCandidates();
        break;
      case 'fire': {
        const a = G.albas.find(a => a.id === +arg);
        if (a) Albas.fire(a);
        break;
      }
      case 'open-load': {
        const d = G.docks[+arg];
        if (Player.loadableDock() === d) LoadingUI.open(d);
        break;
      }
    }
    this.renderPanel(true);
    this.refreshHud();
  },

  // ---------- 토스트 ----------
  toast(msg) {
    const box = $('#toasts');
    if (!box) return;
    const t = U.el('div', 'toast', msg);
    box.prepend(t);
    while (box.children.length > 5) box.lastChild.remove();
    setTimeout(() => t.classList.add('out'), 3600);
    setTimeout(() => t.remove(), 4200);
  },

  // ---------- HUD ----------
  buildHud() {
    $('#clock-box').innerHTML = `
      <div class="clock"><span id="clk-icon">☀️</span> <b id="clk-day"></b> <span id="clk-time"></span></div>
      <div class="phase-bar"><i id="clk-bar"></i></div>
      <div class="money" id="money"></div>
      <div class="speed">
        <button data-speed="0" title="일시정지">⏸</button><button data-speed="1">x1</button><button data-speed="2">x2</button><button data-speed="4">x4</button>
      </div>
      <div class="stats" id="stats"></div>`;
    $('#menu-bar').innerHTML = `
      <button data-act="panel" data-arg="facility">🏗️ 시설</button>
      <button data-act="panel" data-arg="upgrade">⬆️ 업그레이드</button>
      <button data-act="panel" data-arg="alba">👷 알바 <span id="alba-count"></span></button>
      <button class="dbg" data-act="debug">🛠️ 디버그</button>`;
    $('#help').innerHTML = 'WASD·클릭 이동 · 바닥 원(상호작용 범위) 안에서 대기하면 자동 작업 · <b>E</b> 상차 미니게임 · 휠 확대 · 간식을 알바에게 드래그 · <b>`</b> 디버그 · ESC 패널 닫기';

    const bar = $('#snack-bar');
    bar.innerHTML = '<div class="snack-title">간식</div>' + CONFIG.snacks.map((s, i) =>
      `<div class="snack" data-snack="${i}" title="${s.name} (MT +${s.mt})">${s.emoji}<small>+${s.mt}</small></div>`).join('');
    bar.addEventListener('pointerdown', e => {
      const el = e.target.closest('[data-snack]');
      if (el) this.startSnackDrag(e, CONFIG.snacks[+el.dataset.snack]);
    });
  },

  update(dt) {
    this.timers.hud -= dt;
    this.timers.labels -= dt;
    this.timers.panel -= dt;
    if (this.timers.hud <= 0) { this.timers.hud = 0.2; this.refreshHud(); }
    if (this.timers.labels <= 0) { this.timers.labels = 0.1; this.refreshLabels(); }
    if (this.timers.panel <= 0) { this.timers.panel = 0.5; this.renderPanel(false); }
    Labels.update();
  },

  clockText() {
    const hours = CONFIG.dayStartHour + (G.phase === 'night' ? 12 : 0) + G.phaseTime / CONFIG.phaseSeconds * 12;
    const h = Math.floor(hours) % 24, m = Math.floor((hours % 1) * 60);
    return String(h).padStart(2, '0') + ':' + String(m).padStart(2, '0');
  },

  refreshHud() {
    $('#clk-icon').textContent = G.phase === 'day' ? '☀️' : '🌙';
    $('#clk-day').textContent = `${G.day}일차 ${G.phase === 'day' ? '낮' : '밤'}`;
    $('#clk-time').textContent = this.clockText();
    $('#clk-bar').style.width = (G.phaseTime / CONFIG.phaseSeconds * 100).toFixed(1) + '%';
    $('#money').textContent = U.money(G.money);
    $('#stats').innerHTML = `🚛 출고 ${G.stats.trucks}대 · 📦 ${G.stats.shipped}개 · 💔 ${G.stats.broken}`;
    document.querySelectorAll('#clock-box [data-speed]').forEach(b => {
      const sp = +b.dataset.speed;
      b.classList.toggle('on', sp === 0 ? G.userPaused : !G.userPaused && G.speed === sp);
    });
    $('#alba-count').textContent = `${G.albas.length}/${CONFIG.alba.max}`;
    document.body.classList.toggle('night', G.phase === 'night');

    const ev = $('#event-banner');
    ev.style.display = G.event ? '' : 'none';
    if (G.event) ev.innerHTML = `🎉 ${CONFIG.event.name} <small>주문 생성 속도 x${1 / CONFIG.event.intervalMult} · ${G.phase === 'day' ? '밤' : '낮'}이 되면 종료</small>`;

    this.renderOrders();
  },

  renderOrders() {
    const strip = $('#order-strip');
    const cardW = 132;
    const max = Math.max(1, Math.floor((strip.clientWidth + 6) / (cardW + 6)));
    const list = G.orders;
    const showN = list.length > max ? max - 1 : list.length;
    let html = '';
    for (let i = 0; i < showN; i++) {
      const o = list[i], t = o.table !== null ? G.tables[o.table] : null;
      const dest = CONFIG.destinations[o.dest];
      const items = Object.entries(o.items).map(([k, q]) => {
        const it = CONFIG.items[+k];
        const got = t ? (t.delivered[+k] || 0) : 0;
        return `<span class="${t && got >= q ? 'done' : ''}">${it.emoji}${t ? got + '/' : 'x'}${q}</span>`;
      }).join(' ');
      const state = t ? (S.tableReady(t) ? '🎁 포장 대기' : `포장대 ${t.idx + 1}`) : '대기';
      html += `<div class="order ${t ? 'assigned' : ''}" style="--dest:${U.hex(dest.color)}">
        <div class="o-head">#${o.id} → <b>${dest.name}</b></div>
        <div class="o-items">${items}</div>
        <div class="o-foot">${U.money(o.reward)} · ${state}</div></div>`;
    }
    if (list.length > showN) html += `<div class="order more">📋<br>주문서 x${list.length - showN}</div>`;
    if (!list.length) html = '<div class="order empty">주문서 없음</div>';
    strip.innerHTML = html;
  },

  // ---------- 월드 라벨 ----------
  buildLabels() {
    const L = this.lbl = {};
    const at = (x, y, z) => () => ({ x, y, z });
    // 트럭 라벨은 맵 밖으로 나가지 않게 주차 위치 근처까지만 따라간다
    L.inTrucks = G.inTrucks.map(tr => Labels.add(() => {
      const p = S.truckPos(tr.slot, Math.min(tr.off, 3));
      return { x: p.x, y: 3, z: p.z };
    }, 'small'));
    L.staging = Labels.add(at(LAYOUT.staging.x, 1.8, LAYOUT.staging.z));
    L.bins = G.bins.map(b => b.slot ? Labels.add(at(b.slot.x, 2.0, b.slot.z), 'small') : null);
    L.tables = G.tables.map(t => Labels.add(at(t.slot.x, 2.4, t.slot.z), 'small'));
    L.docks = G.docks.map(d => d.slot ? Labels.add(at(d.slot.x, 3.2, d.slot.z), 'small') : null);
    L.trash = Labels.add(at(LAYOUT.trash.x, 1.8, LAYOUT.trash.z), 'small dim');
    L.rest = Labels.add(at(LAYOUT.rest.x, 2.2, LAYOUT.rest.z - LAYOUT.rest.d / 2), 'small');
    L.waiting = Labels.add(at(LAYOUT.waiting.x, 1.4, LAYOUT.waiting.z + LAYOUT.waiting.d / 2), 'small dim');
    L.purifier = Labels.add(at(LAYOUT.purifier.x, 2.4, LAYOUT.purifier.z), 'small');
    L.player = Labels.add(() => ({ x: G.player.x, y: 2.3, z: G.player.z }), 'actor me');
  },

  handIcons(hand) {
    return hand.map(e => {
      if (e.kind === 'box') return '📦';
      if (e.kind === 'broken') return '💔';
      return `<span class="${e.kind}">${CONFIG.items[e.item].emoji}</span>`;
    }).join('');
  },

  lockedLabel(act, arg, text, cost) {
    const can = G.money >= cost;
    return `<div class="locked clickable ${can ? 'can' : ''}" data-act="${act}" data-arg="${arg}" title="클릭해서 해금">🔒 ${text}<br>${U.money(cost)}</div>`;
  },

  refreshLabels() {
    const L = this.lbl;
    G.inTrucks.forEach((tr, i) => {
      let html = '';
      if (i < S.parkingCount()) {
        html = tr.state === 'present' ? `🚚 입고 트럭 ${i + 1} <b>${tr.items.length}</b>개`
          : tr.state === 'away' ? `🅿️ ${i + 1} 다음 입고 ${Math.ceil(tr.timer)}초` : `🚚 ${i + 1} 이동 중`;
      }
      Labels.set(L.inTrucks[i], html);
    });
    Labels.set(L.staging, `📥 하차장 <b>${G.staging.length}</b>/${S.stagingCap()}`);

    G.bins.forEach((b, i) => {
      const item = CONFIG.items[i];
      if (!b.slot) return;
      if (!b.unlocked) { Labels.set(L.bins[i], this.lockedLabel('buy-bin', i, `${item.emoji} ${item.name}`, b.slot.cost || 0)); return; }
      const arm = S.isAuto('inspect') && b.armProgress > 0 ? ' 🦾' : '';
      const full = S.inboundRoom(i) <= 0 ? ' <span class="full" title="보관함+하차장+운송 중이 최대 보관량 → 배송 중지">⛔</span>' : '';
      Labels.set(L.bins[i], `<b>${item.emoji} ${item.name}</b>${arm}${full}<br>✅${b.checked} ❔${b.unchecked}${b.broken ? ' 💔' + b.broken : ''} <small>/${S.binCap()}</small>`);
    });

    G.tables.forEach((t, i) => {
      if (!t.unlocked) { Labels.set(L.tables[i], this.lockedLabel('buy-table', i, `포장대 ${i + 1}`, t.slot.cost || 0)); return; }
      let html = `<b>🎁 포장대 ${i + 1}</b>`;
      if (t.order) {
        html += ` #${t.order.id}<br>` + Object.entries(t.order.items).map(([k, q]) =>
          `${CONFIG.items[+k].emoji}${t.delivered[+k] || 0}/${q}`).join(' ');
        if (S.tableReady(t)) {
          const prog = S.isAuto('pack') ? t.machineProgress / CONFIG.automation.packTime : t.packProgress / CONFIG.timing.pack;
          html += `<br>포장 대기 ${U.bar(prog)}`;
        }
      }
      if (t.boxes.length) html += `<br>📦 완료 x${t.boxes.length}`;
      Labels.set(L.tables[i], html);
    });

    G.docks.forEach((d, i) => {
      const dest = CONFIG.destinations[i];
      if (!d.slot) return;
      if (!d.unlocked) { Labels.set(L.docks[i], this.lockedLabel('buy-dock', i, `${dest.name} 도크`, d.slot.cost || 0)); return; }
      let html = `<b style="color:${U.hex(dest.color)}">🏁 ${dest.name}</b> 📦${d.waiting.length}`;
      const tr = d.truck;
      if (!tr) html += `<br>🚛 도착까지 ${Math.ceil(d.truckTimer)}초`;
      else if (tr.state !== 'present') html += '<br>🚛 이동 중';
      else {
        const pay = LoadLogic.payout(tr);
        html += `<br>🚛 ${tr.def.name}(${tr.w}×${tr.h}) ${Math.round(pay.rate)}%`;
        if (tr.mode === 'auto') html += S.isAuto('load') ? ' 🦾' : ' 👷';
        else if (tr.objectives.length) html += ` 🎯${pay.objectives.filter(o => o.done).length}/${tr.objectives.length}`;
        if (Player.loadableDock() === d) html += `<br><button class="clickable go" data-act="open-load" data-arg="${i}">[E] 상차하기</button>`;
      }
      Labels.set(L.docks[i], html);
    });

    Labels.set(L.trash, '🗑️ 쓰레기통');
    Labels.set(L.rest, `🛋️ ${CONFIG.care.tierNames[G.care.rest]} 휴게실`);
    Labels.set(L.waiting, '🪑 대기 구역');
    Labels.set(L.purifier, G.care.purifier < 0
      ? this.lockedLabel('up-purifier', 0, '🌬️ 공기청정기', CONFIG.care.purifier.costs[0])
      : `🌬️ ${CONFIG.care.tierNames[G.care.purifier]} 공기청정기`);

    const p = G.player;
    let ph = `👔 관리자 ${this.handIcons(p.hand)}`;
    if (p.zone) {
      ph += `<br><span class="zone">${Zones.name(p.zone)}</span> · ${p.action || '…'}`;
      if (p.busy) {
        const max = p.busy === 'inspect' ? CONFIG.timing.inspect : CONFIG.timing.pack;
        ph += U.bar(p.progress / max);
      }
    }
    Labels.set(L.player, ph);

    // 알바 라벨
    for (const [id, lb] of this.albaLabels) {
      if (!G.albas.some(a => a.id === id)) { Labels.remove(lb); this.albaLabels.delete(id); }
    }
    for (const a of G.albas) {
      let lb = this.albaLabels.get(a.id);
      if (!lb) {
        lb = Labels.add(() => ({ x: a.x, y: 2.3, z: a.z }), 'actor');
        this.albaLabels.set(a.id, lb);
      }
      const prog = a.progress > 0 && a.progress < 1 ? U.bar(a.progress) : '';
      Labels.set(lb, `${this.albaFace(a)} ${a.name} ${this.handIcons(a.hand)}<br>${this.mtBar(a.mt)}${prog}`);
    }
  },

  albaFace(a) {
    if (a.resting) return '💤';
    if (a.unpaid) return '😡';
    if (a.fx > 0) return '✨';
    if (a.wantRest) return '😩';
    if (a.mt < 0) return '😞';
    return '🙂';
  },

  mtBar(mt) {
    const r = (mt - CONFIG.mt.min) / (CONFIG.mt.max - CONFIG.mt.min);
    const cls = mt < -50 ? 'mt-bad' : mt < 0 ? 'mt-low' : mt < 50 ? 'mt-mid' : 'mt-good';
    return `<span class="mt ${cls}">MT ${Math.round(mt)}</span>${U.bar(r, cls)}`;
  },

  // ---------- 간식 드래그 ----------
  startSnackDrag(e, snack) {
    e.preventDefault();
    const ghost = $('#drag-ghost');
    ghost.textContent = snack.emoji;
    ghost.style.display = 'block';
    const move = ev => { ghost.style.left = ev.clientX + 'px'; ghost.style.top = ev.clientY + 'px'; };
    move(e);
    const up = ev => {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
      ghost.style.display = 'none';
      const a = this.albaAt(ev.clientX, ev.clientY);
      if (a) Albas.feed(a, snack);
      else UI.toast('간식은 알바 위에 놓아주세요');
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
  },

  albaAt(x, y) {
    const row = document.elementsFromPoint(x, y).map(el => el.closest && el.closest('[data-alba-row]')).find(Boolean);
    if (row) return G.albas.find(a => a.id === +row.dataset.albaRow);
    let best = null, bd = 48;
    for (const a of G.albas) {
      const s = World.project(a.x, 0.9, a.z);
      const d = Math.hypot(s.x - x, s.y - y);
      if (d < bd) { bd = d; best = a; }
    }
    return best;
  },

  // ---------- 패널 ----------
  openPanel(name) {
    if (this.panelName === name) { this.closePanel(); return; }
    this.panelName = name;
    $('#panel').classList.remove('hidden');
    this.renderPanel(true);
  },

  closePanel() {
    this.panelName = null;
    $('#panel').classList.add('hidden');
  },

  renderPanel(force) {
    if (!this.panelName) return;
    const el = $('#panel');
    const sig = this.panelSignature();
    const focused = document.activeElement && document.activeElement.tagName === 'SELECT' && el.contains(document.activeElement);
    if (force || (sig !== this.panelSig && !focused)) {
      this.panelSig = sig;
      const scroll = el.scrollTop;
      el.innerHTML = `<button class="close" data-act="close">✕</button>` + this['panel_' + this.panelName]();
      el.scrollTop = scroll;
    }
    // 자주 바뀌는 값만 갱신
    el.querySelectorAll('[data-mt]').forEach(n => {
      const a = G.albas.find(a => a.id === +n.dataset.mt);
      if (a) n.innerHTML = this.mtBar(a.mt);
    });
    el.querySelectorAll('[data-status]').forEach(n => {
      const a = G.albas.find(a => a.id === +n.dataset.status);
      if (a) n.textContent = `${this.albaFace(a)} ${a.status}`;
    });
  },

  panelSignature() {
    return JSON.stringify([this.panelName, Math.floor(G.money), G.stages, G.care, G.loadUpg, G.fac,
      G.bins.map(b => b.unlocked), G.tables.map(t => t.unlocked), G.docks.map(d => d.unlocked),
      G.albas.map(a => [a.id, a.stage, a.unpaid, Object.values(a.skills).map(Albas.skillLv)]),
      G.candidates.map(c => c.name)]);
  },

  btn(act, arg, label, cost) {
    return `<button class="buy" data-act="${act}" data-arg="${arg}" ${G.money < cost ? 'disabled' : ''}>${label} ${U.money(cost)}</button>`;
  },

  panel_facility() {
    const c = CONFIG.care;
    let h = '<h2>🏗️ 시설</h2><h3>보관함 (품목)</h3><div class="rows">';
    // 배치에 자리가 있는 시설만 표시
    G.bins.forEach((b, i) => {
      if (!b.slot) return;
      const it = CONFIG.items[i], ty = CONFIG.types[it.type];
      h += `<div class="row"><span>${it.emoji} ${it.name} <small>${ty.emoji}${ty.name} · 가격 ${it.price}</small></span>
        ${b.unlocked ? '<span class="ok">해금됨</span>' : this.btn('buy-bin', i, '해금', b.slot.cost || 0)}</div>`;
    });
    h += '</div><h3>포장대</h3><div class="rows">';
    G.tables.forEach((t, i) => {
      h += `<div class="row"><span>🎁 포장대 ${i + 1}</span>
        ${t.unlocked ? '<span class="ok">사용 중</span>' : this.btn('buy-table', i, '해금', t.slot.cost || 0)}</div>`;
    });
    h += '</div><h3>출고 도크 (목적지)</h3><div class="rows">';
    G.docks.forEach((d, i) => {
      if (!d.slot) return;
      const dest = CONFIG.destinations[i];
      h += `<div class="row"><span style="color:${U.hex(dest.color)}">🏁 ${dest.name}</span>
        ${d.unlocked ? '<span class="ok">운영 중</span>' : this.btn('buy-dock', i, '해금', d.slot.cost || 0)}</div>`;
    });
    const rt = G.care.rest, pt = G.care.purifier;
    h += `</div><h3>케어시설</h3><div class="rows">
      <div class="row"><span>🛋️ ${c.tierNames[rt]} 휴게실<br><small>회복 MT ${c.rest.recover[rt]}/초 · MT ${c.rest.until[rt]}까지 휴식</small></span>
        ${rt + 1 < c.tierNames.length ? this.btn('up-rest', 0, c.tierNames[rt + 1] + '으로', c.rest.costs[rt + 1]) : '<span class="ok">최고 티어</span>'}</div>
      <div class="row"><span>🌬️ 공기청정기 ${pt < 0 ? '(미설치)' : c.tierNames[pt]}<br><small>${pt < 0 ? '작업 시 일정 확률로 MT 감소 없음' : `발동 확률 ${Math.round(c.purifier.chance[pt] * 100)}%`}</small></span>
        ${pt + 1 < c.tierNames.length ? this.btn('up-purifier', 0, pt < 0 ? '설치' : c.tierNames[pt + 1] + '으로', c.purifier.costs[pt + 1]) : '<span class="ok">최고 티어</span>'}</div>
    </div>`;
    return h;
  },

  panel_upgrade() {
    let h = '<h2>⬆️ 업그레이드</h2><h3>공정</h3><p class="hint">Lv1~3: 알바 배치 슬롯 1~3명 · Lv4: 자동화 (하차는 자동화 없음)</p><div class="rows">';
    for (const s of CONFIG.stages) {
      const lv = G.stages[s.id], max = s.costs.length;
      const info = S.isAuto(s.id) ? '🤖 자동화' : `슬롯 ${S.stageCount(s.id)}/${lv}명`;
      const next = lv >= max ? '<span class="ok">최대</span>'
        : this.btn('up-stage', s.id, lv + 1 === 4 ? '🤖 자동화' : `Lv${lv + 1}`, s.costs[lv]);
      h += `<div class="row"><span>${s.emoji} ${s.name} <b>Lv${lv}</b><br><small>${info}</small></span>${next}</div>`;
    }
    h += '</div><h3>물류 시설</h3><div class="rows">';
    for (const [key, f] of Object.entries(CONFIG.facility)) {
      const lv = G.fac[key], cur = f.values[lv], next = f.values[lv + 1];
      h += `<div class="row"><span>${f.emoji} ${f.name} <b>${cur}${f.unit}</b><br><small>${f.desc}</small></span>
        ${next === undefined ? '<span class="ok">최대</span>' : this.btn('up-fac', key, `${next}${f.unit}로`, f.costs[lv + 1])}</div>`;
    }
    h += '</div><p class="hint">보관함 + 하차장 + 운송 중인 물품의 합이 최대 보관량에 닿은 품목(⛔)은 더 이상 배송되지 않습니다.</p>';
    h += '<h3>상차 미니게임</h3><div class="rows">';
    for (const [key, u] of Object.entries(CONFIG.loading.upgrades)) {
      const lv = G.loadUpg[key];
      const desc = key === 'compress' ? `트럭마다 겹쳐 놓기 ${lv}칸` : `트럭마다 우선 적재 칸 ${lv}개 (+${CONFIG.loading.bonusCellPercent}%씩)`;
      h += `<div class="row"><span>${u.emoji} ${u.name} <b>Lv${lv}</b>/${u.max}<br><small>${desc}</small></span>
        ${lv >= u.max ? '<span class="ok">최대</span>' : this.btn('up-load', key, `Lv${lv + 1}`, u.costs[lv])}</div>`;
    }
    return h + '</div><p class="hint">알바/자동화 상차는 고정 적재율이라 미니게임 업그레이드가 적용되지 않습니다.</p>';
  },

  panel_alba() {
    let h = `<h2>👷 아르바이트</h2><h3>지원자 <button class="mini" data-act="reroll" ${G.money < CONFIG.alba.rerollCost ? 'disabled' : ''}>🔄 새로 받기 ${U.money(CONFIG.alba.rerollCost)}</button></h3>
      <p class="hint">낮/밤이 바뀔 때 지원자가 새로 옵니다. 낮/밤이 바뀔 때마다 시급 × ${CONFIG.alba.hoursPerPhase}시간을 지급합니다.</p><div class="cands">`;
    G.candidates.forEach((c, i) => {
      h += `<div class="cand"><b>${c.name}</b><div>💪${c.str} 🏃${c.agi} 🧠${c.int}</div><div>시급 ${U.money(c.wage)}<br><small>12시간 ${U.money(Albas.shiftPay(c))}</small></div>
        <button data-act="hire" data-arg="${i}" ${G.albas.length >= CONFIG.alba.max ? 'disabled' : ''}>고용</button></div>`;
    });
    if (!G.candidates.length) h += '<p class="hint">지원자가 없습니다</p>';
    h += '</div><h3>공정 배치 현황</h3><div class="chips">';
    for (const s of CONFIG.stages) {
      h += `<span class="chip">${s.emoji}${s.name} ${S.isAuto(s.id) ? '🤖' : S.stageCount(s.id) + '/' + S.stageSlots(s.id)}</span>`;
    }
    h += `</div><h3>직원 (${G.albas.length}/${CONFIG.alba.max})</h3>`;
    if (!G.albas.length) h += '<p class="hint">고용한 알바가 없습니다. 위 지원자를 고용하세요.</p>';
    for (const a of G.albas) {
      const opts = ['<option value="">🪑 대기중</option>'].concat(CONFIG.stages.map(s => {
        const full = S.isAuto(s.id) || (a.stage !== s.id && S.stageCount(s.id) >= S.stageSlots(s.id));
        const label = S.isAuto(s.id) ? '자동화' : `${S.stageCount(s.id)}/${S.stageSlots(s.id)}`;
        return `<option value="${s.id}" ${a.stage === s.id ? 'selected' : ''} ${full ? 'disabled' : ''}>${s.emoji} ${s.name} (${label})</option>`;
      })).join('');
      const sk = a.skills, lv = Albas.skillLv;
      h += `<div class="alba" data-alba-row="${a.id}">
        <div class="a-top"><b>${a.name}</b> <span class="stat">💪${a.str} 🏃${a.agi} 🧠${a.int}</span> <span>시급 ${U.money(a.wage)} <small>(12시간 ${U.money(Albas.shiftPay(a))})</small>${a.unpaid ? ' <b class="bad">미지급</b>' : ''}</span></div>
        <div data-mt="${a.id}">${this.mtBar(a.mt)}</div>
        <div class="skills">숙련도 들기 ${lv(sk.lift)} · 나르기 ${lv(sk.carry)} · 포장 ${lv(sk.pack)} · 검수 ${lv(sk.inspect)} <small>(최대 ${Albas.capacity(a)}개)</small></div>
        <div class="a-bot"><select data-assign="${a.id}">${opts}</select> <span class="status" data-status="${a.id}"></span>
          <button class="mini danger" data-act="fire" data-arg="${a.id}">해고</button></div>
      </div>`;
    }
    return h + '<p class="hint">간식을 직원 카드나 맵 위 알바에게 드래그하면 MT가 회복됩니다.</p>';
  },
};
