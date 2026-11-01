// 플레이어(관리자): 이동 + 상호작용 범위 안에서 대기하면 자동 작업
//
// 상호작용 범위는 시설의 지점(in = 넣기, out = 꺼내기, inspect = 검수)마다 하나씩 생긴다.
// 같은 위치에 있는 지점은 하나의 범위로 합쳐지고, 그 범위에서는 합쳐진 역할을 모두 한다.

const ROLE_NAMES = { in: '넣기', out: '꺼내기', inspect: '검수' };
const FACILITY_ROLES = { truck: ['out'], staging: ['in', 'out'], bin: ['in', 'out', 'inspect'], table: ['in', 'out'], dock: ['in'], trash: ['in'] };

const Zones = {
  list() {
    const pts = [];
    const add = (kind, ref, role, p, key) => pts.push({ kind, ref, role, x: p.x, z: p.z, key });
    for (const tr of G.inTrucks) if (tr.state === 'present') add('truck', tr, 'out', S.inTruckOut(tr), 'truck' + tr.idx);
    add('staging', null, 'in', S.stagingIn(), 'staging');
    add('staging', null, 'out', S.stagingOut(), 'staging');
    for (const b of G.bins) {
      if (!b.unlocked) continue;
      add('bin', b, 'in', S.binIn(b), 'bin' + b.idx);
      add('bin', b, 'out', S.binOut(b), 'bin' + b.idx);
      add('bin', b, 'inspect', S.binInspect(b), 'bin' + b.idx);
    }
    for (const t of G.tables) {
      if (!t.unlocked) continue;
      add('table', t, 'in', S.tableIn(t), 'table' + t.idx);
      add('table', t, 'out', S.tableOut(t), 'table' + t.idx);
    }
    for (const d of G.docks) if (d.unlocked) add('dock', d, 'in', S.dockIn(d), 'dock' + d.idx);
    add('trash', null, 'in', S.trashIn(), 'trash');

    // 같은 위치의 지점을 하나의 범위로 합친다
    const zones = [];
    for (const p of pts) {
      let z = zones.find(z => Math.abs(z.x - p.x) < 0.05 && Math.abs(z.z - p.z) < 0.05);
      if (!z) { z = { x: p.x, z: p.z, parts: [], keys: [] }; zones.push(z); }
      let part = z.parts.find(q => q.kind === p.kind && q.ref === p.ref);
      if (!part) { part = { kind: p.kind, ref: p.ref, roles: new Set() }; z.parts.push(part); }
      part.roles.add(p.role);
      z.keys.push(p.key + ':' + p.role);
    }
    for (const z of zones) {
      z.key = z.keys.join('|');
      z.kind = z.parts[0].kind;
      z.ref = z.parts[0].ref;
    }
    return zones;
  },

  nearest(pos) {
    let best = null, bd = CONFIG.interactRadius;
    for (const z of Zones.list()) {
      const d = U.dist(pos, z);
      if (d < bd) { bd = d; best = z; }
    }
    return best;
  },

  partName(part) {
    let name = '';
    switch (part.kind) {
      case 'truck': name = `🚚 입고 트럭 ${part.ref.idx + 1}`; break;
      case 'staging': name = '📥 하차장'; break;
      case 'bin': { const it = CONFIG.items[part.ref.item]; name = `${it.emoji} ${it.name} 보관함`; break; }
      case 'table': name = `🎁 포장대 ${part.ref.idx + 1}`; break;
      case 'dock': name = `🏁 ${CONFIG.destinations[part.ref.idx].name} 도크`; break;
      case 'trash': name = '🗑️ 쓰레기통'; break;
    }
    // 시설의 지점이 나뉘어 있으면 어떤 지점인지 붙인다
    if (part.roles.size < FACILITY_ROLES[part.kind].length) {
      name += ` (${[...part.roles].map(r => ROLE_NAMES[r]).join('·')})`;
    }
    return name;
  },

  name(z) { return z.parts.map(Zones.partName).join(' + '); },
};

const Player = {
  init() {
    G.player = {
      x: LAYOUT.playerStart.x, z: LAYOUT.playerStart.z, facing: 0, moving: false,
      hand: [], target: null, timer: 0, zone: null, zoneKey: null, zoneMode: null,
      progress: 0, busy: null, action: null,
    };
  },

  capacity() { return CONFIG.player.capacity; },

  update(dt, input) {
    const p = G.player;
    let vx = input.x, vz = input.z, step = CONFIG.player.speed * dt;
    if (vx || vz) {
      p.target = null;
      const l = Math.hypot(vx, vz);
      vx /= l; vz /= l;
    } else if (p.target) {
      const dx = p.target.x - p.x, dz = p.target.z - p.z, d = Math.hypot(dx, dz);
      if (d < 0.05) p.target = null;
      else { vx = dx / d; vz = dz / d; step = Math.min(step, d); }
    }
    p.moving = !!(vx || vz);
    if (p.moving) {
      p.x += vx * step;
      p.z += vz * step;
      p.facing = Math.atan2(vx, vz);
    }
    const bd = LAYOUT.bounds;
    p.x = U.clamp(p.x, bd.minX, bd.maxX);
    p.z = U.clamp(p.z, bd.minZ, bd.maxZ);

    const z = Zones.nearest(p);
    const key = z ? z.key : null;
    if (key !== p.zoneKey) {
      Player.leaveZone();
      p.zoneKey = key;
      p.timer = CONFIG.interactDelay;
      p.zoneMode = z ? Player.entryMode(z) : null;
      p.action = null;
    }
    p.zone = z;
    if (!z) return;
    p.timer -= dt;
    if (p.timer > 0) return;
    p.action = null;
    Player.act(z, dt);
    if (!p.action) p.action = Player.idleText(z);
  },

  // 범위에 들어가는 순간 할 일을 정하고, 나갈 때까지 유지한다
  // 하차장 넣기·꺼내기 지점이 합쳐져 있으면: 들고 오면 내려놓기만, 빈손이면 집기만 (집자마자 내려놓는 문제 방지)
  entryMode(z) {
    const part = z.parts.find(q => q.kind === 'staging');
    if (!part) return null;
    if (part.roles.has('in') && part.roles.has('out')) return S.hasKind(G.player, 'raw') ? 'drop' : 'pick';
    return part.roles.has('in') ? 'drop' : 'pick';
  },

  leaveZone() {
    const p = G.player;
    for (const b of G.bins) if (b.inspector === 'player') b.inspector = null;
    for (const t of G.tables) if (t.packer === 'player') { t.packer = null; t.packProgress = 0; }
    p.progress = 0;
    p.busy = null;
  },

  // 합쳐진 시설마다 순서대로 시도하고, 하나라도 일을 하면 멈춘다
  act(z, dt) {
    const p = G.player;
    for (const part of z.parts) {
      Player.actPart(part, dt);
      if (p.action) return;
    }
  },

  actPart(part, dt) {
    const p = G.player, cap = Player.capacity(), h = CONFIG.timing.handle;
    switch (part.kind) {
      case 'truck': {
        const tr = part.ref;
        if (tr.items.length && S.canTake(p, 'raw', cap)) {
          p.hand.push({ kind: 'raw', item: tr.items.pop() });
          p.timer = h;
          p.action = '하차 중';
        }
        break;
      }
      case 'staging': {
        if (p.zoneMode === 'drop') {
          const i = p.hand.findIndex(e => e.kind === 'raw');
          if (i >= 0 && G.staging.length < S.stagingCap()) {
            G.staging.push(p.hand.splice(i, 1)[0].item);
            p.timer = h;
            p.action = '내려놓는 중';
          }
        } else if (S.canTake(p, 'raw', cap)) {
          const i = G.staging.findIndex(it => S.binSpace(G.bins[it]) > 0);
          if (i >= 0) {
            p.hand.push({ kind: 'raw', item: G.staging.splice(i, 1)[0] });
            p.timer = h;
            p.action = '집는 중';
          }
        }
        break;
      }
      case 'bin': Player.actBin(part.ref, dt, part.roles); break;
      case 'table': Player.actTable(part.ref, dt, part.roles); break;
      case 'dock': {
        const d = part.ref;
        const i = p.hand.findIndex(e => e.kind === 'box' && e.box.dest === d.idx);
        if (i >= 0) {
          d.waiting.push(p.hand.splice(i, 1)[0].box);
          p.timer = h;
          p.action = '박스 내려놓는 중';
        }
        break;
      }
      case 'trash': {
        const i = p.hand.findIndex(e => e.kind === 'broken');
        if (i >= 0) {
          p.hand.splice(i, 1);
          G.stats.broken++;
          p.timer = h;
          p.action = '파손품 버리는 중';
        }
        break;
      }
    }
  },

  actBin(b, dt, roles) {
    const p = G.player, cap = Player.capacity(), h = CONFIG.timing.handle, it = b.item;
    // 넣기: 들고 온 미검수 물품 넣기 (분류)
    if (roles.has('in')) {
      const i = p.hand.findIndex(e => e.kind === 'raw' && e.item === it);
      if (i >= 0 && S.binSpace(b) > 0) {
        p.hand.splice(i, 1);
        b.unchecked++;
        p.timer = h;
        p.action = '분류 중';
        return;
      }
    }
    // 꺼내기: 이미 충분히 이송된 물품은 반납, 주문에 필요한 검수완료 물품 집기 (이송)
    if (roles.has('out')) {
      const i = p.hand.findIndex(e => e.kind === 'pick' && e.item === it);
      if (i >= 0 && S.outstandingNeed(it) < 0) {
        p.hand.splice(i, 1);
        b.checked++;
        p.timer = h;
        p.action = '남은 물품 반납';
        return;
      }
      if (S.canTake(p, 'pick', cap) && S.avail(b) > 0 && S.outstandingNeed(it) > 0) {
        b.checked--;
        p.hand.push({ kind: 'pick', item: it });
        p.timer = h;
        p.action = '주문 물품 집는 중';
        return;
      }
    }
    // 검수: 파손품 집기, 검수
    if (roles.has('inspect')) {
      if (b.broken > 0 && S.canTake(p, 'broken', cap)) {
        b.broken--;
        p.hand.push({ kind: 'broken', item: it });
        p.timer = h;
        p.action = '파손품 집는 중';
        return;
      }
      if (b.unchecked > 0 && (b.inspector === null || b.inspector === 'player')) {
        b.inspector = 'player';
        p.busy = 'inspect';
        p.action = '검수 중';
        p.progress += dt;
        if (p.progress >= CONFIG.timing.inspect) {
          p.progress = 0;
          b.unchecked--;
          if (Math.random() < CONFIG.breakChance.inspect) {
            if (S.canTake(p, 'broken', cap)) p.hand.push({ kind: 'broken', item: it });
            else b.broken++;
            UI.toast(`💔 검수 중 ${CONFIG.items[it].name} 불량 발견! 쓰레기통에 버려주세요`);
          } else {
            b.checked++;
          }
        }
        return;
      }
    }
    if (b.inspector === 'player') b.inspector = null;
    p.busy = null;
    p.progress = 0;
  },

  actTable(t, dt, roles) {
    const p = G.player, cap = Player.capacity(), h = CONFIG.timing.handle;
    if (roles.has('in')) {
      // 이송품 전달
      const i = p.hand.findIndex(e => e.kind === 'pick' && S.remainingNeed(t, e.item) > 0);
      if (i >= 0) {
        const e = p.hand.splice(i, 1)[0];
        t.delivered[e.item] = (t.delivered[e.item] || 0) + 1;
        p.timer = h;
        p.action = '물품 전달 중';
        return;
      }
      // 포장
      if (!S.isAuto('pack') && S.tableReady(t) && (t.packer === null || t.packer === 'player')) {
        t.packer = 'player';
        p.busy = 'pack';
        p.action = '포장 중';
        t.packProgress += dt;
        p.progress = t.packProgress;
        if (t.packProgress >= CONFIG.timing.pack) {
          Orders.pack(t);
          p.busy = null;
          p.progress = 0;
        }
        return;
      }
    }
    p.busy = null;
    // 꺼내기: 포장된 박스 집기 (출고분류)
    if (roles.has('out')) {
      const bi = t.boxes.findIndex(b => !b.claimedBy);
      if (bi >= 0 && S.canTake(p, 'box', cap)) {
        p.hand.push({ kind: 'box', box: t.boxes.splice(bi, 1)[0] });
        p.timer = h;
        p.action = '박스 집는 중';
      }
    }
  },

  // 할 일이 없을 때 이유 표기 (첫 번째 시설 기준)
  idleText(z) {
    const p = G.player, cap = Player.capacity(), full = p.hand.length >= cap;
    const part = z.parts[0], roles = part.roles;
    switch (part.kind) {
      case 'truck':
        if (!part.ref.items.length) return '트럭이 비었습니다';
        return full ? '손이 가득 찼습니다' : '다른 물건을 들고 있습니다';
      case 'staging':
        if (p.zoneMode === 'drop') {
          if (S.hasKind(p, 'raw')) return '하차장이 가득 찼습니다';
          return roles.has('out') ? '내려놓기 완료 (다시 들어오면 집기)' : '내려놓을 물건 없음';
        }
        if (full) return '손이 가득 찼습니다';
        if (p.hand.length) return '다른 물건을 들고 있습니다';
        return G.staging.length ? '보관함 자리가 없습니다' : '하차장이 비었습니다';
      case 'bin': {
        const raw = S.hasKind(p, 'raw');
        if (raw && !roles.has('in')) return '여기는 넣기 지점이 아닙니다';
        if (raw && p.hand.some(e => e.kind === 'raw' && e.item === part.ref.item)) return '보관함이 가득 찼습니다';
        if (raw) return '이 보관함 품목이 아닙니다';
        if (full) return '손이 가득 찼습니다';
        return '할 일 없음';
      }
      case 'table':
        if (!roles.has('in')) return part.ref.boxes.length && full ? '손이 가득 찼습니다' : '포장된 박스 없음';
        if (!part.ref.order) return part.ref.boxes.length && full ? '손이 가득 찼습니다' : '주문 대기 중';
        return '이송 물품 대기 중';
      case 'dock':
        if (Player.loadableDock() === part.ref) return '[E] 상차 미니게임';
        return S.hasKind(p, 'box') ? '이 도크 목적지 박스가 아닙니다' : '트럭 대기 중';
      case 'trash':
        return '버릴 파손품 없음';
    }
    return '';
  },

  // 상차 미니게임을 열 수 있는 도크
  loadableDock() {
    const z = G.player.zone;
    const part = z && z.parts.find(q => q.kind === 'dock');
    if (!part) return null;
    return LoadingUI.canOpen(part.ref) ? part.ref : null;
  },
};
