// 배치 시뮬레이터: 상단 메뉴, 왼쪽 도구/목록, 오른쪽 속성, 하단 검증/동선

const $e = sel => document.querySelector(sel);
const ROT_OPTIONS = [[0, '→ 오른쪽'], [90, '↓ 아래'], [180, '← 왼쪽'], [270, '↑ 위']];

const Panels = {
  fields: [],
  showIssues: false,

  init() {
    $e('#topbar').innerHTML = `
      <div class="brand">🗺️ 배치 시뮬레이터</div>
      <input id="layout-name" title="배치 이름">
      <button data-cmd="undo" title="되돌리기 (Ctrl+Z)">↶</button>
      <button data-cmd="redo" title="다시하기 (Ctrl+Y)">↷</button>
      <span class="sep"></span>
      <button data-cmd="new">새 배치(기본값)</button>
      <button data-cmd="game">게임 배치 불러오기</button>
      <button data-cmd="saves">💾 저장/불러오기</button>
      <button data-cmd="import">📂 파일 가져오기</button>
      <button data-cmd="json">JSON 내보내기</button>
      <span class="sep"></span>
      <button data-cmd="export" class="primary" title="게임 폴더의 layout-data.js를 이 파일로 덮어쓰면 게임에 반영">📦 layout-data.js 내보내기</button>
      <button data-cmd="play" class="go" title="현재 배치로 게임을 새 탭에서 실행 (파일은 바꾸지 않음)">▶ 테스트 플레이</button>
      <button data-cmd="help" title="도움말">?</button>`;
    document.body.addEventListener('click', e => {
      const b = e.target.closest('[data-cmd]');
      if (b) this.cmd(b.dataset.cmd, b.dataset.arg);
    });
    $e('#layout-name').addEventListener('change', e => Ed.change(L => { L.name = e.target.value || '이름 없는 배치'; }));
    $e('#file').addEventListener('change', e => {
      const f = e.target.files[0];
      if (!f) return;
      const rd = new FileReader();
      rd.onload = () => Ed.importText(rd.result);
      rd.readAsText(f, 'utf-8');
      e.target.value = '';
    });
    $e('#inspector').addEventListener('change', e => this.onField(e));
    $e('#modal').addEventListener('change', e => { if (e.target.dataset.fi !== undefined) { this.onField(e, this.mfields); this.openItems(); } });
    $e('#left').addEventListener('change', e => {
      const k = e.target.dataset.show;
      if (k) { Ed.show[k] = e.target.checked; View.render(); }
      if (e.target.id === 'snap') { Ed.snap = +e.target.value; }
    });
    this.toast(`시작: ${Ed.startFrom}`);
  },

  cmd(c, arg) {
    switch (c) {
      case 'undo': Ed.undo(); break;
      case 'redo': Ed.redo(); break;
      case 'new': Ed.newDefault(); break;
      case 'game': Ed.loadGameLayout(); break;
      case 'saves': this.openSaves(); break;
      case 'import': $e('#file').click(); break;
      case 'json': Ed.exportJson(); break;
      case 'export': Ed.exportData(); break;
      case 'play': Ed.testPlay(); break;
      case 'help': this.openHelp(); break;
      case 'mode': Ed.setMode(arg); break;
      case 'add': Ed.add(arg); break;
      case 'clear-belt': Ed.clearBelt(Ed.beltStage()); break;
      case 'auto-belt': Ed.autoBelt(Ed.beltStage()); break;
      case 'items': this.openItems(); break;
      case 'item-add': Ed.addItem(); this.openItems(); break;
      case 'item-del': Ed.removeItem(arg); this.openItems(); break;
      case 'item-bin': $e('#modal').classList.add('hidden'); Ed.add('bin', arg); break;
      case 'select-bin': $e('#modal').classList.add('hidden'); Ed.select({ t: 'bin', i: +arg }); break;
      case 'item-new-bin': Ed.newItemForBin(Ed.sel); break;
      case 'reverse': Ed.reverseLine(Ed.sel); break;
      case 'detach': Ed.detach(Ed.sel, arg); break;
      case 'line-of': Ed.select({ t: 'line', st: Ed.sel.st, id: Ed.sel.id }); break;
      case 'goto-line': Ed.select({ t: 'line', st: Ed.beltStage() || Ed.sel.st, id: +arg }); break;
      case 'select': Ed.select(JSON.parse(arg)); break;
      case 'rotate': Ed.rotate(Ed.sel); break;
      case 'dup': Ed.duplicate(Ed.sel); break;
      case 'del': Ed.remove(Ed.sel); break;
      case 'order': Ed.moveOrder(Ed.sel, +arg); break;
      case 'merge': Ed.mergePoints(Ed.sel.t === 'point' ? Ed.sel.of : Ed.sel, arg); break;
      case 'reset-points': Ed.resetPoints(Ed.sel); break;
      case 'owner': Ed.select(Ed.sel.of); break;
      case 'issues': this.showIssues = !this.showIssues; this.renderBottom(); break;
      case 'fit': View.fit(); break;
      case 'modal-close': $e('#modal').classList.add('hidden'); break;
      case 'save-new': Ed.saveAs($e('#save-name').value.trim()); this.openSaves(); break;
      case 'save-load': Ed.loadSave(arg); $e('#modal').classList.add('hidden'); break;
      case 'save-del': if (confirm(`'${arg}' 저장본을 지울까요?`)) { Ed.deleteSave(arg); this.openSaves(); } break;
    }
  },

  refresh() {
    $e('#layout-name').value = Ed.L.name || '';
    document.querySelector('[data-cmd=undo]').disabled = !Ed.undoStack.length;
    document.querySelector('[data-cmd=redo]').disabled = !Ed.redoStack.length;
    this.renderLeft();
    this.refreshInspector();
    this.renderBottom();
  },

  coords(p) {
    const el = $e('#coords');
    if (el) el.textContent = `x ${p.x.toFixed(1)} · z ${p.z.toFixed(1)} · 확대 ${Math.round(View.scale / 18 * 100)}%`;
  },

  toast(msg) {
    const t = document.createElement('div');
    t.className = 'toast';
    t.textContent = msg;
    $e('#toasts').prepend(t);
    setTimeout(() => t.remove(), 4000);
  },

  // ---------- 왼쪽: 도구, 추가, 표시, 목록 ----------
  renderLeft() {
    const L = Ed.L, m = Ed.mode, st = Ed.beltStage();
    const modeBtn = (id, label, key) => `<button data-cmd="mode" data-arg="${id}" class="${m === id ? 'on' : ''}" ${id.startsWith('belt:') ? `style="--belt:${BELT_COLORS[id.slice(5)]}"` : ''}><kbd>${key}</kbd> ${label}</button>`;
    const item = (ref, label) => {
      const on = Ed.same(Ed.sel, ref) ? 'on' : '';
      return `<div class="li ${on}" data-cmd="select" data-arg='${JSON.stringify(ref)}'>${label}</div>`;
    };
    let h = `<h3>도구</h3><div class="tools">
      ${modeBtn('select', '선택 · 이동', 1)}
      ${modeBtn('belt:sort', '분류 벨트 그리기', 2)}
      ${modeBtn('belt:transfer', '이송 벨트 그리기', 3)}
      ${modeBtn('belt:outbound', '출고분류 벨트 그리기', 4)}</div>`;
    if (st) {
      const sp = LayoutLib.STAGE_PORTS[st], lines = L.belts[st].lines;
      h += `<div class="belt-help">
        <b>${LayoutLib.FAC_NAMES[sp.src[0]]} 출구(출) → ${LayoutLib.FAC_NAMES[sp.dst[0]]} 입구(입)</b>, 화살표 방향으로만 흐릅니다.<br>
        ${Ed.draw ? '<b>✏️ 그리는 중</b> · 클릭: 꺾이는 점 · 입구 포트/다른 라인 클릭: 붙이고 끝 · 더블클릭/Enter/우클릭: 떨어진 끝으로 마치기 · Backspace: 점 취소 · Esc: 취소'
          : '출구 포트 클릭: 거기서 새 라인 · 빈 곳 클릭: 떨어진 점에서 새 라인 · <b>Alt+라인 클릭: 그 위치에서 갈라지는 지선</b> · 라인 클릭: 선택 · 선택한 라인 클릭: 꺾이는 점 추가 · 점 드래그: 이동(끝점은 포트/라인 가까이 놓으면 붙음) · Shift: 가로·세로 정렬 · Del: 삭제'}
        <div class="btnrow"><button data-cmd="auto-belt">⚙️ 자동 생성 (초안)</button><button data-cmd="clear-belt" class="danger">모두 지우기</button></div></div>
        <h3>${LayoutLib.BELT_NAMES[st]} 라인 (${lines.length})</h3><div class="list lines">`
        + (lines.map(ln => {
          const on = Ed.sel && (Ed.sel.t === 'line' || Ed.sel.t === 'lpt') && Ed.sel.st === st && Ed.sel.id === ln.id ? 'on' : '';
          return `<div class="li ${on}" data-cmd="goto-line" data-arg="${ln.id}" title="${LayoutLib.attachName(L, ln.from)} → ${LayoutLib.attachName(L, ln.to)}">
            <b style="color:${BELT_COLORS[st]}">${ln.id}</b> ${this.attachShort(L, ln.from)} → ${this.attachShort(L, ln.to)}</div>`;
        }).join('') || '<p class="hint">라인이 없습니다</p>') + '</div>';
    }
    const freeI = Ed.freeItems().length, freeD = Ed.freeDests().length;
    h += `<h3>추가</h3><div class="adds">
      <button data-cmd="add" data-arg="area">▭ 구역</button>
      <button data-cmd="add" data-arg="wall">▬ 벽</button>
      <button data-cmd="add" data-arg="inTruck">🚚 하차 주차공간</button>
      <button data-cmd="add" data-arg="bin" title="${freeI ? '보관함이 없는 품목으로 만듭니다' : '남는 품목이 없어 새 품목도 같이 만듭니다'}">🗄️ 보관함 ${freeI ? `(남은 품목 ${freeI})` : '+ 새 품목'}</button>
      <button data-cmd="items">🏷️ 품목 관리 (${L.items.length})</button>
      <button data-cmd="add" data-arg="table">🎁 포장대</button>
      <button data-cmd="add" data-arg="dock" ${freeD ? '' : 'disabled'}>🏁 출고 도크 (${L.docks.length}/${CONFIG.destinations.length})</button></div>
      <h3>표시</h3><div class="shows">
      <label><input type="checkbox" data-show="belts" ${Ed.show.belts ? 'checked' : ''}> 컨베이어 벨트</label>
      <label><input type="checkbox" data-show="ranges" ${Ed.show.ranges ? 'checked' : ''}> 상호작용 범위</label>
      <label><input type="checkbox" data-show="labels" ${Ed.show.labels ? 'checked' : ''}> 이름</label>
      <label>격자 맞춤 <select id="snap">${[[0, '끄기'], [0.25, '0.25칸'], [0.5, '0.5칸'], [1, '1칸']].map(([v, l]) => `<option value="${v}" ${Ed.snap === v ? 'selected' : ''}>${l}</option>`).join('')}</select></label>
      <button data-cmd="fit">⤢ 화면에 맞추기 (F)</button></div>`;

    h += '<h3>목록</h3><div class="list objs">';
    h += '<div class="lg">구역 · 벽</div>' + L.areas.map((a, i) => item({ t: 'area', i }, `▭ ${a.name}`)).join('')
      + L.walls.map((w, i) => item({ t: 'wall', i }, `▬ 벽 ${i + 1}`)).join('');
    h += '<div class="lg">하차</div>' + L.inTrucks.map((t, i) => item({ t: 'inTruck', i }, `🚚 하차 주차공간 ${i + 1}`)).join('')
      + item({ t: 'staging' }, '📥 하차장');
    h += '<div class="lg">보관</div>' + L.bins.map((b, i) => {
      const it = LayoutLib.item(L, b.item);
      return item({ t: 'bin', i }, `🗄️ ${i + 1}. ${it ? it.emoji + ' ' + it.name : b.item}${b.cost ? ' 🔒' : ''}`);
    }).join('');
    h += '<div class="lg">포장</div>' + L.tables.map((t, i) => item({ t: 'table', i }, `🎁 포장대 ${i + 1}${t.cost ? ' 🔒' : ''}`)).join('');
    h += '<div class="lg">출고</div>' + L.docks.map((d, i) => {
      const de = CONFIG.destinations.find(x => x.id === d.dest);
      return item({ t: 'dock', i }, `🏁 ${de ? de.name : d.dest} 도크${d.cost ? ' 🔒' : ''}`) + item({ t: 'dockTruck', i }, `　🚛 ${de ? de.name : d.dest} 트럭 주차`);
    }).join('');
    h += '<div class="lg">기타</div>' + item({ t: 'trash' }, '🗑️ 쓰레기통') + item({ t: 'rest' }, '🛋️ 휴게실')
      + item({ t: 'waiting' }, '🪑 알바 대기 구역') + item({ t: 'purifier' }, '🌬️ 공기청정기 자리')
      + item({ t: 'entrance' }, '🚪 알바 출입구') + item({ t: 'playerStart' }, '👔 플레이어 시작 위치');
    h += '<div class="lg">공정별 알바 대기 지점</div>' + Object.keys(L.idle).map(st => item({ t: 'idle', st }, S_STAGE_NAME(st))).join('');
    h += '</div>';
    const left = $e('#left'), old = left.querySelector('.list.objs'), scroll = old ? old.scrollTop : 0;
    left.innerHTML = h;
    left.querySelector('.list.objs').scrollTop = scroll;
  },

  // 라인 목록용 짧은 이름
  attachShort(L, a) {
    if (!a) return '○';
    if (a.line !== undefined) return `라인${a.line}`;
    return `${LayoutLib.FAC_NAMES[a.fac]}${a.fac === 'staging' ? '' : a.i + 1}`;
  },

  // ---------- 오른쪽: 속성 ----------
  // 필드 정의: { label, type, obj(L) → 대상 객체, key, opts }
  field(def) {
    const id = this.fields.push(def) - 1, o = def.obj(Ed.L), v = o ? o[def.key] : '';
    const lab = `<span>${def.label}</span>`;
    switch (def.type) {
      case 'text': return `<label class="f">${lab}<input data-fi="${id}" value="${String(v).replace(/"/g, '&quot;')}"></label>`;
      case 'color': return `<label class="f">${lab}<input data-fi="${id}" type="color" value="${v}"></label>`;
      case 'select': return `<label class="f">${lab}<select data-fi="${id}">${def.opts.map(([ov, ol, dis]) => `<option value="${ov}" ${String(ov) === String(v) ? 'selected' : ''} ${dis ? 'disabled' : ''}>${ol}</option>`).join('')}</select></label>`;
      case 'check': return `<label class="f chk">${lab}<input data-fi="${id}" type="checkbox" ${def.checked(o) ? 'checked' : ''}></label>`;
      default: return `<label class="f">${lab}<input data-fi="${id}" type="number" step="${def.step || 0.5}" value="${v}"></label>`;
    }
  },

  onField(e, list = this.fields) {
    const def = list[e.target.dataset.fi];
    if (!def) return;
    let v = e.target.value;
    if (def.type === 'check') v = e.target.checked;
    else if (def.type === 'select') v = def.num ? +v : v;
    else if (!['text', 'color'].includes(def.type)) { v = parseFloat(v); if (isNaN(v)) return; v = r2(v); }
    Ed.change(L => {
      const o = def.obj(L);
      if (def.set) def.set(o, v, L); else o[def.key] = v;
    });
  },

  // 좌표 x, z 두 칸
  xz(label, obj) {
    return `<div class="pair"><b>${label}</b>${this.field({ label: 'x', obj, key: 'x' })}${this.field({ label: 'z', obj, key: 'z' })}</div>`;
  },

  rotField(obj, label) {
    return this.field({ label: label || '앞면 방향', type: 'select', num: true, obj, key: 'rot', opts: ROT_OPTIONS,
      set: (o, v) => { while ((o.rot || 0) !== v) Ed.rotateIn(o, Ed.sel); } });
  },

  refreshInspector() {
    this.fields = [];
    const sel = Ed.sel, L = Ed.L, el = $e('#inspector');
    let h = '';
    if (!sel) h = this.inspectMap();
    else {
      const btns = [];
      if (ED_ROLES[sel.t] && sel.t !== 'staging' && sel.t !== 'trash' || ['dockTruck', 'area', 'rest', 'waiting'].includes(sel.t)) btns.push('<button data-cmd="rotate" title="R">⟳ 90° 회전</button>');
      if (['area', 'wall', 'inTruck', 'bin', 'table', 'dock'].includes(sel.t)) btns.push('<button data-cmd="dup" title="Ctrl+D">⧉ 복제</button>');
      if (sel.t === 'lpt') {
        const ln = Ed.get({ t: 'line', st: sel.st, id: sel.id });
        if (sel.k > 0 && sel.k < ln.points.length - 1) btns.push('<button data-cmd="del" class="danger" title="Delete">🗑 점 삭제</button>');
      } else if (Ed.canDelete(sel)) btns.push('<button data-cmd="del" class="danger" title="Delete">🗑 삭제</button>');
      h = `<h2>${Ed.name(sel)}</h2><div class="btnrow">${btns.join('')}</div>` + this.inspectRef(sel, L);
    }
    el.innerHTML = h;
  },

  inspectMap() {
    const L = Ed.L, root = l => l, I = l => l.interior, M = l => l.map;
    const m = LayoutLib.measure(L);
    return `<h2>배치 설정</h2>
      ${this.field({ label: '배치 이름', type: 'text', obj: root, key: 'name' })}
      <div class="pair"><b>맵 크기</b>${this.field({ label: '가로', obj: M, key: 'w', step: 1 })}${this.field({ label: '세로', obj: M, key: 'd', step: 1 })}</div>
      <div class="pair"><b>창고 내부</b>${this.field({ label: 'x1', obj: I, key: 'x1' })}${this.field({ label: 'z1', obj: I, key: 'z1' })}</div>
      <div class="pair"><b></b>${this.field({ label: 'x2', obj: I, key: 'x2' })}${this.field({ label: 'z2', obj: I, key: 'z2' })}</div>
      <p class="hint">창고 내부는 밤에도 밝고 플레이어가 다닐 수 있는 범위입니다. 회색 모서리 핸들로도 조절할 수 있습니다.</p>
      <h3>동선 (직선 평균, 칸)</h3>
      <div class="measure">
        <div>🚚 트럭 → 하차장 <b>${m.unload.toFixed(1)}</b></div>
        <div>📥 하차장 → 보관함 <b>${m.sort.toFixed(1)}</b></div>
        <div>🛒 보관함 → 포장대 <b>${m.transfer.toFixed(1)}</b></div>
        <div>📤 포장대 → 도크 <b>${m.outbound.toFixed(1)}</b></div>
        <div class="sum">합계 <b>${m.total.toFixed(1)}</b></div>
      </div>
      <p class="hint">지금 게임은 캐릭터가 시설을 통과해 직선으로 다니므로 이 값이 실제 이동 거리와 거의 같습니다.</p>
      <h3>개수</h3>
      <div class="measure">
        <div>하차 주차공간 <b>${L.inTrucks.length}</b> <small>(업그레이드 단계 수)</small></div>
        <div>품목 <b>${L.items.length}</b> · 보관함 <b>${L.bins.length}</b> · 포장대 <b>${L.tables.length}</b> · 도크 <b>${L.docks.length}</b>/${CONFIG.destinations.length}</div>
        <div>벨트 라인 분류 ${L.belts.sort.lines.length} · 이송 ${L.belts.transfer.lines.length} · 출고 ${L.belts.outbound.lines.length}</div>
      </div>
      <p class="hint">캔버스에서 무언가를 클릭하면 여기서 속성을 바꿀 수 있습니다.</p>`;
  },

  // 품목 정보 필드 (obj = 배치 → 품목 객체)
  itemFields(obj) {
    return this.field({ label: '이름', type: 'text', obj, key: 'name' })
      + this.field({ label: '이모지', type: 'text', obj, key: 'emoji' })
      + this.field({ label: '타입', type: 'select', obj, key: 'type', opts: Object.entries(CONFIG.types).map(([k, t]) => [k, `${t.emoji} ${t.name}`]) })
      + this.field({ label: '가격 (주문 보상 계산)', obj, key: 'price', step: 1 });
  },

  // 품목 관리 창
  openItems() {
    const L = Ed.L;
    this.mfields = [];
    const rows = L.items.map(it => {
      const bi = L.bins.findIndex(b => b.item === it.id);
      const obj = l => LayoutLib.item(l, it.id);
      const f = (key, type, extra) => {
        const id = this.mfields.push({ label: key, type, obj, key, ...(extra || {}) }) - 1;
        if (type === 'select') return `<select data-fi="${id}">${extra.opts.map(([v, lbl]) => `<option value="${v}" ${v === it[key] ? 'selected' : ''}>${lbl}</option>`).join('')}</select>`;
        return `<input data-fi="${id}" ${type === 'num' ? 'type="number" step="1"' : ''} value="${String(it[key]).replace(/"/g, '&quot;')}">`;
      };
      return `<tr><td class="em">${f('emoji', 'text')}</td><td>${f('name', 'text')}</td>
        <td>${f('type', 'select', { opts: Object.entries(CONFIG.types).map(([k, t]) => [k, `${t.emoji} ${t.name}`]) })}</td>
        <td class="num">${f('price', 'num')}</td>
        <td>${bi >= 0 ? `<button data-cmd="select-bin" data-arg="${bi}">보관함 ${bi + 1}</button>` : `<button data-cmd="item-bin" data-arg="${it.id}" class="primary">＋ 보관함 만들기</button>`}</td>
        <td><button data-cmd="item-del" data-arg="${it.id}" class="danger" title="품목 삭제 (보관함도 같이)">✕</button></td></tr>`;
    }).join('');
    $e('#modal').classList.remove('hidden');
    $e('#modal').innerHTML = `<div class="box wide"><button class="x" data-cmd="modal-close">✕</button>
      <h2>🏷️ 품목 관리</h2>
      <p class="hint">보관함 1개 = 품목 1종입니다. 보관함을 늘리려면 품목을 추가하고 '보관함 만들기'를 누르세요.
        타입은 미니게임 랭크 계산에 쓰입니다. 해금 비용은 보관함마다 정합니다.</p>
      <table class="items"><thead><tr><th>이모지</th><th>이름</th><th>타입</th><th>가격</th><th>보관함</th><th></th></tr></thead><tbody>${rows}</tbody></table>
      <div class="btnrow"><button data-cmd="item-add" class="primary">＋ 품목 추가</button></div></div>`;
  },

  // 벨트 포트 편집 블록
  portsBlock(ref) {
    const ports = LayoutLib.PORTS[ref.t];
    if (!ports) return '';
    return '<h3>벨트 포트 <small>(컨베이어가 붙는 곳)</small></h3>'
      + ports.map(p => this.xz(`<i class="role port">${p === 'beltOut' ? '출' : '입'}</i> ${LayoutLib.PORT_NAMES[p]}`, l => Ed.get(ref, l)[p])).join('')
      + '<p class="hint">벨트 그리기 모드(2·3·4)에서 끌어 옮길 수 있습니다.</p>';
  },

  // 상호작용 지점 편집 블록
  pointsBlock(ref) {
    const roles = ED_ROLES[ref.t];
    const o = Ed.get(ref);
    const merged = roles.every(r => o[r].x === o[roles[0]].x && o[r].z === o[roles[0]].z);
    let h = `<h3>상호작용 지점 <small>(${merged ? '모두 한 곳' : '나뉨'})</small></h3>`;
    for (const r of roles) h += this.xz(`<i class="role ${r}">${ROLE_SHORT[r]}</i> ${ED_ROLE_LABEL[r]}`, l => Ed.get(ref, l)[r]);
    if (roles.length > 1) {
      h += `<div class="btnrow"><button data-cmd="merge" data-arg="first">지점 모두 '${ED_ROLE_LABEL[roles[0]]}' 위치로 합치기</button>
        <button data-cmd="reset-points">앞쪽 기본 위치로</button></div>
        <p class="hint">캔버스에서 지점을 끌어 따로 둘 수 있습니다. 같은 시설의 다른 지점 가까이 놓으면 딱 붙어 하나의 범위로 합쳐집니다.</p>`;
    } else {
      h += '<div class="btnrow"><button data-cmd="reset-points">기본 위치로</button></div>';
    }
    return h;
  },

  inspectRef(ref, L) {
    const self = l => Ed.get(ref, l);
    const usedItems = new Set(L.bins.map(b => b.item)), usedDests = new Set(L.docks.map(d => d.dest));
    const cost = this.field({ label: '해금 비용 (0 = 시작부터 해금)', obj: self, key: 'cost', step: 50 });
    const order = (what) => `<div class="btnrow"><button data-cmd="order" data-arg="-1">▲ 앞으로</button><button data-cmd="order" data-arg="1">▼ 뒤로</button><small>${what}</small></div>`;
    switch (ref.t) {
      case 'area':
        return this.field({ label: '이름', type: 'text', obj: self, key: 'name' }) + this.field({ label: '색', type: 'color', obj: self, key: 'color' })
          + `<div class="pair"><b>왼위</b>${this.field({ label: 'x1', obj: self, key: 'x1' })}${this.field({ label: 'z1', obj: self, key: 'z1' })}</div>`
          + `<div class="pair"><b>오른아래</b>${this.field({ label: 'x2', obj: self, key: 'x2' })}${this.field({ label: 'z2', obj: self, key: 'z2' })}</div>`
          + '<p class="hint">구역을 끌면 안에 있는 시설·벨트도 같이 옮겨집니다. 구역만 옮기려면 Alt+드래그. 모서리 핸들로 크기 조절.</p>';
      case 'wall':
        return `<div class="pair"><b>시작</b>${this.field({ label: 'x1', obj: self, key: 'x1' })}${this.field({ label: 'z1', obj: self, key: 'z1' })}</div>`
          + `<div class="pair"><b>끝</b>${this.field({ label: 'x2', obj: self, key: 'x2' })}${this.field({ label: 'z2', obj: self, key: 'z2' })}</div>`;
      case 'inTruck':
        return order(`${ref.i + 1}번째 주차공간 · 1번은 처음부터, 2번부터는 '하차 주차공간' 업그레이드로 열림`)
          + this.xz('트럭 짐칸 중심', self) + this.rotField(self, '뒷문 방향') + this.pointsBlock(ref);
      case 'staging':
        return this.xz('위치', self) + this.pointsBlock(ref) + this.portsBlock(ref);
      case 'bin': {
        const item = l => LayoutLib.item(l, Ed.get(ref, l).item);
        return this.field({ label: '품목', type: 'select', obj: self, key: 'item',
          opts: L.items.map(it => [it.id, `${it.emoji} ${it.name} (${CONFIG.types[it.type] ? CONFIG.types[it.type].name : it.type})`, usedItems.has(it.id) && it.id !== Ed.get(ref).item]) })
          + `<div class="itembox"><b>품목 정보</b> <small>(이 품목을 쓰는 모든 곳에 반영)</small>
            ${this.itemFields(item)}
            <div class="btnrow"><button data-cmd="item-new-bin">＋ 새 품목을 만들어 이 보관함에</button><button data-cmd="items">🏷️ 품목 관리</button></div></div>`
          + order('보관함 번호 순서') + cost + this.xz('위치', self) + this.rotField(self) + this.pointsBlock(ref) + this.portsBlock(ref);
      }
      case 'table':
        return order('포장대 번호 순서') + cost + this.xz('위치', self) + this.rotField(self) + this.pointsBlock(ref) + this.portsBlock(ref);
      case 'dock':
        return this.field({ label: '목적지', type: 'select', obj: self, key: 'dest',
          opts: CONFIG.destinations.map(d => [d.id, d.name, usedDests.has(d.id) && d.id !== Ed.get(ref).dest]) })
          + order('도크 번호 순서') + cost + this.xz('팔레트 위치', self) + this.pointsBlock(ref) + this.portsBlock(ref)
          + '<h3>트럭 주차 위치</h3>' + this.xz('짐칸 중심', l => Ed.get(ref, l).truck)
          + this.field({ label: '뒷문 방향', type: 'select', num: true, obj: l => Ed.get(ref, l).truck, key: 'rot', opts: ROT_OPTIONS })
          + '<p class="hint">트럭은 뒷문 반대 방향에서 들어와 주차합니다. 상차 기계손은 팔레트와 뒷문 사이에 놓입니다.</p>';
      case 'dockTruck':
        return this.xz('짐칸 중심', self) + this.field({ label: '뒷문 방향', type: 'select', num: true, obj: self, key: 'rot', opts: ROT_OPTIONS });
      case 'trash':
        return this.xz('위치', self) + this.pointsBlock(ref);
      case 'rest': case 'waiting':
        return this.xz('중심', self) + `<div class="pair"><b>크기</b>${this.field({ label: '가로', obj: self, key: 'w' })}${this.field({ label: '세로', obj: self, key: 'd' })}</div>`
          + `<p class="hint">${ref.t === 'rest' ? '휴식하는 알바가 이 안에 자리를 잡습니다. 가구는 크기에 맞춰 늘어납니다.' : '배치되지 않은 알바가 이 안에서 기다립니다.'}</p>`;
      case 'point': {
        const owner = Ed.get(ref.of), roles = ED_ROLES[ref.of.t].filter(r => r !== ref.role);
        return `<div class="btnrow"><button data-cmd="owner">시설 선택</button></div>` + this.xz('위치', self)
          + (roles.length ? `<p class="hint">같은 시설의 다른 지점(${roles.map(r => ED_ROLE_LABEL[r]).join(', ')}) 가까이 끌어 놓으면 하나의 범위로 합쳐집니다.</p>` : '')
          + `<p class="hint">시설 중심: x ${owner.x}, z ${owner.z}</p>`;
      }
      case 'line': {
        const ln = Ed.get(ref), pts = LayoutLib.linePts(L, ref.st, ln);
        let len = 0;
        for (let i = 0; i < pts.length - 1; i++) len += LayoutLib.dist(pts[i], pts[i + 1]);
        return `<div class="belt-ends">
            <div><span class="dot start"></span><b>출발점</b> ${LayoutLib.attachName(L, ln.from)}</div>
            <div><span class="dot bend"></span><b>꺾이는 점</b> ${pts.length - 2}개</div>
            <div><span class="dot end"></span><b>도착점</b> ${LayoutLib.attachName(L, ln.to)}</div>
            <div>길이 ${len.toFixed(1)}칸 · 출발점 → 도착점 한 방향</div></div>
          <div class="btnrow"><button data-cmd="reverse">⇄ 방향 뒤집기</button>
            ${ln.from ? '<button data-cmd="detach" data-arg="from">출발점 떼기</button>' : ''}
            ${ln.to ? '<button data-cmd="detach" data-arg="to">도착점 떼기</button>' : ''}</div>
          <p class="hint">점을 끌어 모양을 바꿉니다. 끝점을 맞는 포트나 다른 라인 가까이 놓으면 붙습니다 (다른 라인 = 합류/분기).
            라인을 한 번 더 클릭하면 그 자리에 꺾이는 점이 생깁니다.</p>`;
      }
      case 'lpt': {
        const ln = Ed.get({ t: 'line', st: ref.st, id: ref.id }), last = ln.points.length - 1;
        const end = ref.k === 0 ? 'from' : ref.k === last ? 'to' : null;
        let h = '<div class="btnrow"><button data-cmd="line-of">라인 선택</button></div>';
        if (end && ln[end]) {
          h += `<p class="hint">붙은 곳: <b>${LayoutLib.attachName(L, ln[end])}</b><br>위치는 붙은 곳을 따라갑니다.</p>
            <div class="btnrow"><button data-cmd="detach" data-arg="${end}">떼기</button></div>`;
        } else {
          h += this.xz('위치', self);
          if (end) h += '<p class="hint">떨어진 끝점입니다. 맞는 포트나 다른 라인 가까이 끌어 놓으면 붙습니다.</p>';
          else h += '<p class="hint">Delete: 이 꺾이는 점 삭제</p>';
        }
        return h;
      }
      case 'port': {
        const sts = LayoutLib.BELT_STAGES.filter(st => Object.values(LayoutLib.STAGE_PORTS[st]).some(([f, p]) => f === ref.of.t && p === ref.port));
        return `<div class="btnrow"><button data-cmd="owner">시설 선택</button></div>` + this.xz('위치', self)
          + `<p class="hint">${sts.map(st => LayoutLib.BELT_NAMES[st]).join(', ')} 벨트가 ${ref.port === 'beltOut' ? '출발' : '도착'}하는 곳입니다. 사람이 서는 상호작용 지점과는 별개입니다.</p>`;
      }
      default:
        return this.xz('위치', self);
    }
  },

  // ---------- 하단: 검증, 좌표 ----------
  renderBottom() {
    const errs = Ed.issues.filter(i => i.level === 'error'), warns = Ed.issues.filter(i => i.level === 'warn');
    const st = Ed.beltStage();
    $e('#bottombar').innerHTML = `
      <button data-cmd="issues" class="issues ${errs.length ? 'bad' : warns.length ? 'warn' : 'ok'}">
        ${errs.length ? `❌ 오류 ${errs.length}` : '✅ 오류 없음'} · ⚠️ 경고 ${warns.length} ${this.showIssues ? '▾' : '▴'}</button>
      <span class="mode">${st ? `✏️ ${LayoutLib.BELT_NAMES[st]} 벨트 그리기 중` : '선택 · 이동'}</span>
      <span class="keys">휠: 확대 · 빈 곳 드래그/Space+드래그: 화면 이동 · R: 회전 · Del: 삭제 · Ctrl+D: 복제 · 방향키: 미세 이동</span>
      <span id="coords"></span>`;
    const box = $e('#issues');
    box.classList.toggle('hidden', !this.showIssues);
    box.innerHTML = Ed.issues.length
      ? Ed.issues.map(i => `<div class="${i.level}">${i.level === 'error' ? '❌' : '⚠️'} ${i.msg}</div>`).join('')
      : '<div class="ok">문제 없음</div>';
  },

  // ---------- 모달 ----------
  openSaves() {
    const saves = Ed.saves(), names = Object.keys(saves);
    $e('#modal').classList.remove('hidden');
    $e('#modal').innerHTML = `<div class="box"><button class="x" data-cmd="modal-close">✕</button>
      <h2>💾 브라우저에 저장 / 불러오기</h2>
      <p class="hint">이 컴퓨터의 브라우저에만 저장됩니다. 다른 사람과 나누려면 'JSON 내보내기'를 쓰세요.</p>
      <div class="row"><input id="save-name" value="${(Ed.L.name || '').replace(/"/g, '&quot;')}" placeholder="저장 이름"><button data-cmd="save-new" class="primary">현재 배치 저장</button></div>
      <div class="saves">${names.length ? names.map(n => `<div class="row"><b>${n}</b><small>${saves[n].at}</small>
        <button data-cmd="save-load" data-arg="${n.replace(/"/g, '&quot;')}">불러오기</button>
        <button data-cmd="save-del" data-arg="${n.replace(/"/g, '&quot;')}" class="danger">삭제</button></div>`).join('') : '<p class="hint">저장된 배치가 없습니다.</p>'}</div></div>`;
  },

  openHelp() {
    $e('#modal').classList.remove('hidden');
    $e('#modal').innerHTML = `<div class="box"><button class="x" data-cmd="modal-close">✕</button>
      <h2>도움말</h2>
      <h3>기본 조작</h3>
      <ul>
        <li>클릭: 선택 · 드래그: 이동 (격자 맞춤) · 빈 곳 드래그 / Space+드래그 / 휠 버튼 드래그: 화면 이동 · 휠: 확대</li>
        <li>R: 90° 회전 · Delete: 삭제 · Ctrl+D: 복제 · Ctrl+Z / Ctrl+Y: 되돌리기 / 다시하기 · 방향키: 미세 이동 · F: 화면에 맞추기</li>
        <li>구역을 끌면 안에 있는 시설·벨트가 같이 움직입니다 (Alt+드래그: 구역만)</li>
        <li>노란 모서리 핸들: 구역·휴게실·대기 구역 크기 / 벽 끝점, 회색 핸들: 창고 내부 크기</li>
      </ul>
      <h3>상호작용 지점</h3>
      <ul>
        <li><i class="role in">넣</i> 넣기 · <i class="role out">꺼</i> 꺼내기 · <i class="role inspect">검</i> 검수 — 원은 상호작용 범위 (${CONFIG.interactRadius}칸)</li>
        <li>지점을 끌어 따로 둘 수 있고, 같은 시설의 다른 지점 가까이 놓으면 합쳐집니다</li>
        <li>빨간 원: 다른 범위와 겹침 (게임에서 가까운 쪽만 동작)</li>
      </ul>
      <h3>컨베이어 벨트 (2·3·4 키)</h3>
      <ul>
        <li>공정마다 라인을 그립니다: 분류(하차장→보관함), 이송(보관함→포장대), 출고분류(포장대→도크)</li>
        <li>라인 = <b>● 출발점 → ◆ 꺾이는 점 → ■ 도착점</b>, 화살표 방향으로만 흐릅니다. 색이 찬 끝점은 어딘가에 붙은 상태</li>
        <li>끝점은 시설의 <b>벨트 포트</b>(출 = 출구, 입 = 입구)나 <b>다른 라인 위</b>에 붙습니다.
          다른 라인에 붙이면 길을 공유합니다: 출발점을 붙이면 <b>분기</b>, 도착점을 붙이면 <b>합류</b></li>
        <li>예) 분류: 하차장 출구 → 메인 라인 → (Alt+클릭으로) 보관함마다 지선 / 이송: 보관함 지선들이 메인 라인에 합류 → 포장대로 분기</li>
        <li>⚙️ 자동 생성: 메인 라인 1개 + 지선 초안을 만들어 줍니다. 방향 때문에 못 가는 연결은 하단 경고에 나옵니다</li>
        <li>연결 안 된 시설 쌍은 게임에서 직선으로 이동합니다</li>
      </ul>
      <h3>게임에 반영</h3>
      <ul>
        <li>▶ 테스트 플레이: 파일을 바꾸지 않고 현재 배치로 게임을 새 탭에서 실행</li>
        <li>📦 layout-data.js 내보내기 → 받은 파일로 APCenterMU/layout-data.js를 덮어쓰면 게임 기본 배치가 됩니다</li>
      </ul></div>`;
  },
};
