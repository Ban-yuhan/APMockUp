// 디버그 패널 (목업 테스트용): 메뉴의 🛠️ 디버그 버튼 또는 ` 키

const SKILL_NAMES = { lift: '들기', carry: '나르기', pack: '포장', inspect: '검수' };

const Debug = {
  open: false,
  timer: 0,
  albaId: null,      // 스탯 편집 중인 알바
  albaSig: '',

  init() {
    this.el = document.getElementById('debug');
    this.el.addEventListener('click', e => {
      const b = e.target.closest('[data-dbg]');
      if (b) this.run(b.dataset.dbg, b.dataset.arg);
    });
    this.el.addEventListener('change', e => {
      const t = e.target;
      if (t.id === 'dbg-alba-sel') { this.albaId = +t.value; this.renderAlba(); return; }
      const key = t.dataset.af, a = this.alba();
      if (!key || !a) return;
      this.setAlbaField(a, key, t.type === 'checkbox' ? t.checked : t.value);
      this.renderAlba();
      UI.renderPanel(true);
    });
    window.addEventListener('keydown', e => {
      if (e.code === 'Backquote' && !LoadingUI.dock) this.toggle();
    });
  },

  toggle() {
    this.open = !this.open;
    this.el.classList.toggle('hidden', !this.open);
    if (this.open) this.render();
  },

  update(dt) {
    if (!this.open) return;
    this.timer -= dt;
    if (this.timer > 0) return;
    this.timer = 0.3;
    const st = this.el.querySelector('#dbg-state');
    if (st) st.innerHTML = this.stateText();
    // 알바를 고용/해고하면 목록을 다시 그리고, 아니면 바뀌는 값(MT, 능력치)만 갱신
    const sig = G.albas.map(a => a.id + a.name).join('|');
    if (sig !== this.albaSig) { this.renderAlba(); return; }
    const a = this.alba();
    if (!a) return;
    const mt = this.el.querySelector('[data-af=mt]');
    if (mt && document.activeElement !== mt) mt.value = Math.round(a.mt);
    const eff = this.el.querySelector('#dbg-alba-eff');
    if (eff) eff.innerHTML = this.albaEffects(a);
  },

  // ---------- 알바 스탯 편집 ----------
  alba() { return G.albas.find(a => a.id === this.albaId) || null; },

  // 디버그용 직접 수정 (스탯은 지원자 범위를 넘어 0~20까지 허용)
  setAlbaField(a, key, v) {
    if (key === 'unpaid') { a.unpaid = !!v; if (!v) a.flee = 0; return; }
    const n = Number(v);
    if (isNaN(n)) return;
    if (key === 'str' || key === 'agi' || key === 'int') a[key] = U.clamp(Math.round(n), 0, 20);
    else if (key === 'mt') a.mt = U.clamp(n, CONFIG.mt.min, CONFIG.mt.max);
    else if (key === 'wage') a.wage = Math.max(0, Math.round(n));
    else if (key.startsWith('sk-')) a.skills[key.slice(3)] = Math.max(0, Math.round(n)) * 10;   // 숙련도 Lv → 경험치
  },

  albaEffects(a) {
    return `최대 픽업 <b>${Albas.capacity(a)}</b>개 <small>(상한 8)</small> · 이동 <b>${Albas.speed(a).toFixed(1)}</b>칸/초<br>`
      + `작업속도 포장 <b>x${Albas.workMult(a, 'pack').toFixed(2)}</b> · 검수 <b>x${Albas.workMult(a, 'inspect').toFixed(2)}</b><br>`
      + `상차 목표 적재율 <b>${Math.round(Albas.loadTarget(a) * CONFIG.loading.fullRate)}%</b> · MT 배율 x${S.mtMult(a.mt)}`;
  },

  renderAlba() {
    const box = this.el.querySelector('#dbg-alba');
    if (!box) return;
    this.albaSig = G.albas.map(a => a.id + a.name).join('|');
    if (!G.albas.length) { box.innerHTML = '<div class="state">고용한 알바가 없습니다</div>'; return; }
    if (!this.alba()) this.albaId = G.albas[0].id;
    const a = this.alba();
    const stage = id => { const s = id && CONFIG.stages.find(x => x.id === id); return s ? s.emoji + s.name : '대기'; };
    const num = (key, label, val, min, max) =>
      `<label><span>${label}</span><input type="number" data-af="${key}" value="${val}" ${min !== undefined ? `min="${min}"` : ''} ${max !== undefined ? `max="${max}"` : ''} step="1"></label>`;
    box.innerHTML = `
      <select id="dbg-alba-sel">${G.albas.map(x => `<option value="${x.id}" ${x.id === a.id ? 'selected' : ''}>${x.name} (${stage(x.stage)})</option>`).join('')}</select>
      <div class="af-grid">
        ${num('str', '💪 힘', a.str, 0, 20)}${num('agi', '🏃 민첩', a.agi, 0, 20)}${num('int', '🧠 지능', a.int, 0, 20)}
        ${Object.entries(SKILL_NAMES).map(([k, n]) => num('sk-' + k, `${n} Lv`, Albas.skillLv(a.skills[k]), 0)).join('')}
        ${num('mt', 'MT', Math.round(a.mt), CONFIG.mt.min, CONFIG.mt.max)}${num('wage', '시급', a.wage, 0)}
        <label class="chk"><span>미지급</span><input type="checkbox" data-af="unpaid" ${a.unpaid ? 'checked' : ''}></label>
      </div>
      <div class="state" id="dbg-alba-eff">${this.albaEffects(a)}</div>
      <div class="btns" style="margin-top:4px"><button data-dbg="alba-copy">이 스탯·숙련도를 모든 알바에 적용</button></div>`;
  },

  stateText() {
    return `${U.money(G.money)} · ${G.day}일차 ${G.phase === 'day' ? '낮' : '밤'} ${UI.clockText()}<br>`
      + `이벤트: ${G.event ? '🎉 ' + CONFIG.event.name : '없음'} · 주문서 ${G.orders.length}개`;
  },

  render() {
    const step = CONFIG.debug.moneyStep;
    const b = (act, label, arg) => `<button data-dbg="${act}" ${arg !== undefined ? `data-arg="${arg}"` : ''}>${label}</button>`;
    this.el.innerHTML = `
      <button class="close" data-dbg="close">✕</button>
      <h2>🛠️ 디버그</h2>
      <div class="state" id="dbg-state">${this.stateText()}</div>
      <h3>💰 돈 (${step.toLocaleString('ko-KR')}원 단위)</h3>
      <div class="money-row">
        <input id="dbg-money" type="number" step="${step}" min="0" value="${step}">
        ${b('money-add', '+ 추가')}${b('money-sub', '- 차감')}
      </div>
      <div class="btns" style="margin-top:4px">
        ${b('money', '+1,000', 1000)}${b('money', '+10,000', 10000)}${b('money', '+100,000', 100000)}${b('money-zero', '0원으로')}
      </div>
      <h3>🕒 낮/밤</h3>
      <div class="btns">
        ${b('phase', '낮/밤 전환 (시급·이벤트 판정 포함)')}${b('phase-only', '낮/밤만 전환')}${b('hour', '+1시간')}
      </div>
      <h3>🎉 이벤트 (${CONFIG.event.name})</h3>
      <div class="btns">${b('event-on', '강제 발동')}${b('event-off', '강제 종료')}</div>
      <h3>📋 주문서</h3>
      <div class="btns">${b('orders', '+1', 1)}${b('orders', '+5', 5)}${b('orders-clear', '대기 주문 비우기')}</div>
      <h3>🚚 트럭</h3>
      <div class="btns">${b('in-trucks', '입고 트럭 즉시 호출')}${b('out-trucks', '출고 트럭 즉시 호출')}</div>
      <h3>👷 알바</h3>
      <div class="btns">${b('mt', 'MT 전원 100', 100)}${b('mt', 'MT 전원 -60', -60)}${b('reroll', '지원자 새로고침')}</div>
      <h3>🧑‍🔧 알바 스탯 편집</h3>
      <div id="dbg-alba"></div>
      <h3>🏗️ 시설</h3>
      <div class="btns">${b('unlock-all', '모든 시설 해금 (무료)')}</div>`;
    this.renderAlba();
  },

  run(act, arg) {
    const step = CONFIG.debug.moneyStep;
    switch (act) {
      case 'close': this.toggle(); return;
      case 'money': G.money += +arg; UI.toast(`🛠️ 돈 +${U.money(+arg)}`); break;
      case 'money-add':
      case 'money-sub': {
        const v = Math.round((+this.el.querySelector('#dbg-money').value || 0) / step) * step;
        G.money = Math.max(0, G.money + (act === 'money-add' ? v : -v));
        UI.toast(`🛠️ 돈 ${act === 'money-add' ? '+' : '-'}${U.money(v)}`);
        break;
      }
      case 'money-zero': G.money = 0; break;
      case 'phase': G.phaseTime = 0; Main.switchPhase(); break;
      case 'phase-only': G.phaseTime = 0; Main.togglePhase(); UI.toast(`🛠️ ${G.phase === 'day' ? '낮' : '밤'}으로 전환 (시급·이벤트 판정 없음)`); break;
      case 'hour': G.phaseTime += CONFIG.phaseSeconds / 12; break;
      case 'event-on': if (!G.event) Main.startEvent(); break;
      case 'event-off': if (G.event) Main.endEvent(); break;
      case 'orders': for (let i = 0; i < +arg; i++) Orders.spawn(); break;
      case 'orders-clear': G.orders = G.orders.filter(o => o.table !== null); break;
      case 'in-trucks':
        for (const tr of G.inTrucks) if (tr.idx < S.parkingCount() && tr.state === 'away') tr.timer = 0;
        break;
      case 'out-trucks':
        for (const d of G.docks) if (d.unlocked && !d.truck) d.truckTimer = 0;
        break;
      case 'mt': for (const a of G.albas) a.mt = +arg; this.renderAlba(); break;
      case 'alba-copy': {
        const src = this.alba();
        if (!src) break;
        for (const a of G.albas) {
          if (a === src) continue;
          a.str = src.str; a.agi = src.agi; a.int = src.int;
          a.skills = { ...src.skills };
        }
        UI.toast(`🛠️ ${src.name}의 스탯·숙련도를 알바 ${G.albas.length - 1}명에게 적용`);
        break;
      }
      case 'reroll': Albas.rerollCandidates(); break;
      case 'unlock-all':
        G.bins.forEach(b => { if (b.slot) b.unlocked = true; });
        G.tables.forEach(t => { t.unlocked = true; });
        G.docks.forEach(d => { if (d.slot && !d.unlocked) { d.unlocked = true; d.truckTimer = 1; } });
        if (G.care.purifier < 0) G.care.purifier = 0;
        break;
    }
    this.update(1);
    UI.refreshHud();
    UI.renderPanel(true);
  },
};
