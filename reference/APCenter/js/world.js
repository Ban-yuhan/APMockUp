// Three.js 3D 월드: 기본 도형(큐브/원통/캡슐)으로 표현

const COLORS = {
  ground: 0xc9ced6, cardboard: 0xc8a675, box: 0xa47148, broken: 0xd63031,
  locked: 0x9aa0a8, wood: 0xb98b5e, belt: 0x37474f, metal: 0x78909c, skin: 0xf1c27d,
};

const World = {
  mats: {},
  albaMeshes: new Map(),
  flyerMeshes: new Map(),
  truckMeshes: [],
  built: { belts: {}, restTier: null, purifierTier: null },
  clock: 0,

  init(canvas) {
    const r = this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    r.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    r.shadowMap.enabled = true;
    r.shadowMap.type = THREE.PCFSoftShadowMap;

    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0xdfe6ec);
    this.center = new THREE.Vector3(LAYOUT.map.w / 2 - 1, 0, LAYOUT.map.d / 2);
    this.camera = new THREE.OrthographicCamera(-10, 10, 10, -10, 0.1, 400);
    this.camOffset = new THREE.Vector3(8, 48, 36);
    this.camTarget = this.center.clone();
    this.zoom = 1;
    this.camera.position.copy(this.center).add(this.camOffset);
    this.camera.lookAt(this.center);
    this.camera.updateMatrixWorld();
    this.zoneRings = new Map();

    this.hemi = new THREE.HemisphereLight(0xffffff, 0x8a8f98, 0.55);
    this.scene.add(this.hemi);
    const dir = this.dir = new THREE.DirectionalLight(0xffffff, 0.6);
    dir.position.set(this.center.x - 14, 34, this.center.z + 10);
    dir.target.position.copy(this.center);
    dir.castShadow = true;
    dir.shadow.mapSize.set(2048, 2048);
    Object.assign(dir.shadow.camera, { left: -42, right: 42, top: 30, bottom: -30, near: 1, far: 120 });
    this.scene.add(dir, dir.target);

    this.raycaster = new THREE.Raycaster();
    this.groundPlane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);

    this.buildStatic();
    this.player = this.makeChar(0x1e5aa8, true);
    this.resize();
  },

  // ---------- 헬퍼 ----------
  mat(color, opts) {
    const key = color + JSON.stringify(opts || {});
    if (!this.mats[key]) this.mats[key] = new THREE.MeshLambertMaterial({ color, ...(opts || {}) });
    return this.mats[key];
  },
  box(w, h, d, color, x, y, z, parent, opts) {
    const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), this.mat(color, opts));
    m.position.set(x, y, z);
    m.castShadow = true;
    m.receiveShadow = true;
    (parent || this.scene).add(m);
    return m;
  },
  cyl(rt, rb, h, color, x, y, z, parent, opts) {
    const m = new THREE.Mesh(new THREE.CylinderGeometry(rt, rb, h, 20), this.mat(color, opts));
    m.position.set(x, y, z);
    m.castShadow = true;
    m.receiveShadow = true;
    (parent || this.scene).add(m);
    return m;
  },
  sphere(r, color, x, y, z, parent, opts) {
    const m = new THREE.Mesh(new THREE.SphereGeometry(r, 16, 12), this.mat(color, opts));
    m.position.set(x, y, z);
    m.castShadow = true;
    (parent || this.scene).add(m);
    return m;
  },
  floor(x1, z1, x2, z2, color) {
    const m = new THREE.Mesh(new THREE.PlaneGeometry(x2 - x1, z2 - z1), this.mat(color));
    m.rotation.x = -Math.PI / 2;
    m.position.set((x1 + x2) / 2, 0.01, (z1 + z2) / 2);
    m.receiveShadow = true;
    this.scene.add(m);
    return m;
  },
  group(x, z, parent) {
    const g = new THREE.Group();
    g.position.set(x || 0, 0, z || 0);
    (parent || this.scene).add(g);
    return g;
  },
  // rot(도) → 메시 회전: 시설은 로컬 +z가 앞면, 트럭은 로컬 -x가 뒷문
  faceYaw(rot) { const d = LayoutLib.dir(rot); return Math.atan2(d.x, d.z); },
  truckYaw(rot) { const d = LayoutLib.dir(rot); return Math.atan2(d.z, -d.x); },
  hexColor(c) { return typeof c === 'string' ? parseInt(c.replace('#', ''), 16) : c; },
  typeColor(item) { return CONFIG.types[CONFIG.items[item].type].color; },
  entryColor(e) {
    if (e.kind === 'broken') return COLORS.broken;
    if (e.kind === 'box') return COLORS.box;
    return this.typeColor(e.item);
  },

  // ---------- 정적 오브젝트 ----------
  buildStatic() {
    const W = LAYOUT.map.w, D = LAYOUT.map.d;
    // 바깥 땅 (밤에 어두워짐) + 창고 내부 바닥 (항상 밝음)
    this.outsideMat = new THREE.MeshLambertMaterial({ color: 0xb4bcc6 });
    const g = new THREE.Mesh(new THREE.PlaneGeometry(W + 60, D + 40), this.outsideMat);
    g.rotation.x = -Math.PI / 2;
    g.position.set(W / 2, -0.01, D / 2);
    g.receiveShadow = true;
    this.scene.add(g);
    const I = LAYOUT.interior;
    this.floor(I.x1, I.z1, I.x2, I.z2, COLORS.ground).position.y = 0.002;

    // 구역 바닥
    for (const a of LAYOUT.areas) this.floor(a.x1, a.z1, a.x2, a.z2, this.hexColor(a.color));
    const r = LAYOUT.rest, w = LAYOUT.waiting;
    this.restFloor = this.floor(r.x - r.w / 2, r.z - r.d / 2, r.x + r.w / 2, r.z + r.d / 2, 0xe2d5ef);
    this.floor(w.x - w.w / 2, w.z - w.d / 2, w.x + w.w / 2, w.z + w.d / 2, 0xe6e6e6);

    // 벽 (선분)
    for (const wl of LAYOUT.walls) {
      const dx = wl.x2 - wl.x1, dz = wl.z2 - wl.z1, len = Math.hypot(dx, dz);
      const m = this.box(0.3, 1.4, len + 0.3, 0xb0b7c0, (wl.x1 + wl.x2) / 2, 0.7, (wl.z1 + wl.z2) / 2);
      m.rotation.y = Math.atan2(dx, dz);
    }

    // 하차장 팔레트
    const s = LAYOUT.staging;
    this.box(2.6, 0.2, 2.6, COLORS.wood, s.x, 0.1, s.z);
    this.stagingCubes = this.cubeSlots(s.x, s.z, 3, 3, 2, 0.6, 0.5);

    // 입고 트럭 (하차 주차공간마다)
    this.inTruckMeshes = G.inTrucks.map(tr => {
      const m = this.makeTruck(0xf39c12);
      m.rotation.y = this.truckYaw(tr.slot.rot);
      m.visible = false;
      return m;
    });

    // 보관함 (로컬 +z = 앞면)
    this.binObjs = G.bins.map(b => {
      if (!b.slot) return null;
      const grp = this.group(b.slot.x, b.slot.z);
      grp.rotation.y = this.faceYaw(b.slot.rot);
      const body = this.box(2.4, 1.2, 1.1, COLORS.locked, 0, 0.6, 0, grp);
      const sign = this.box(2.4, 0.18, 0.08, COLORS.locked, 0, 1.0, 0.6, grp);
      const cubes = [];
      for (let i = 0; i < 10; i++) {
        const c = this.box(0.36, 0.36, 0.36, COLORS.cardboard, -0.9 + (i % 5) * 0.45, 1.4, -0.22 + Math.floor(i / 5) * 0.44, grp);
        c.visible = false;
        cubes.push(c);
      }
      const brk = this.box(0.4, 0.4, 0.4, COLORS.broken, 0.9, 0.2, 0.8, grp);
      brk.visible = false;
      const arm = this.makeArm(grp, 1.5, 0);
      return { grp, body, sign, cubes, brk, arm, sig: '' };
    });

    // 포장대 (로컬 +z = 앞면: 앞쪽에 전달된 물품, 뒤쪽에 포장된 박스)
    this.tableObjs = G.tables.map(t => {
      const grp = this.group(t.slot.x, t.slot.z);
      grp.rotation.y = this.faceYaw(t.slot.rot);
      const top = this.box(2.2, 0.9, 1.8, COLORS.locked, 0, 0.45, 0, grp);
      const items = [], boxes = [];
      for (let i = 0; i < 6; i++) {
        const c = this.box(0.3, 0.3, 0.3, COLORS.cardboard, -0.8 + Math.floor(i / 2) * 0.4, 1.05, 0.45 - (i % 2) * 0.4, grp);
        c.visible = false;
        items.push(c);
      }
      for (let i = 0; i < 4; i++) {
        const c = this.box(0.5, 0.42, 0.5, COLORS.box, 0.25 + (i % 2) * 0.55, 1.11 + Math.floor(i / 2) * 0.44, -0.4, grp);
        c.visible = false;
        boxes.push(c);
      }
      const machine = this.group(0, 0, grp);
      this.box(0.2, 1.8, 0.2, 0x455a64, -1.2, 0.9, 0, machine);
      this.box(0.2, 1.8, 0.2, 0x455a64, 1.2, 0.9, 0, machine);
      this.box(2.6, 0.5, 0.9, 0xf1c40f, 0, 1.9, 0, machine);
      machine.visible = false;
      return { grp, top, items, boxes, machine, sig: '' };
    });

    // 출고 도크 (팔레트 + 바닥 패드, 기계손은 팔레트와 트럭 뒷문 사이)
    this.dockObjs = G.docks.map(d => {
      if (!d.slot) return null;
      const grp = this.group(d.slot.x, d.slot.z);
      const pad = new THREE.Mesh(new THREE.PlaneGeometry(3.4, 3.4), this.mat(COLORS.locked));
      pad.rotation.x = -Math.PI / 2;
      pad.position.y = 0.02;
      pad.receiveShadow = true;
      grp.add(pad);
      this.box(2.4, 0.2, 2.4, COLORS.wood, 0, 0.1, 0, grp);
      const cubes = this.cubeSlots(0, 0, 3, 3, 2, 0.62, 0.5, grp, COLORS.box);
      const door = S.truckDoor(d.slot.truck), dx = door.x - d.slot.x, dz = door.z - d.slot.z, dl = Math.hypot(dx, dz) || 1;
      const arm = this.makeArm(null, d.slot.x + dx / dl * 1.8 - dz / dl * 1.3, d.slot.z + dz / dl * 1.8 + dx / dl * 1.3);
      return { grp, pad, cubes, arm, sig: '' };
    });

    // 쓰레기통
    const tr = LAYOUT.trash;
    this.cyl(0.75, 0.6, 1.2, 0x2e7d32, tr.x, 0.6, tr.z);
    this.cyl(0.82, 0.82, 0.12, 0x1b5e20, tr.x, 1.26, tr.z);

    // 대기 구역 벤치
    this.box(Math.max(1, w.w * 0.9), 0.4, 0.6, COLORS.wood, w.x, 0.2, w.z + w.d / 2 - 0.5);

    // 클릭 이동 목적지 표시
    this.marker = new THREE.Mesh(new THREE.RingGeometry(0.3, 0.45, 24), this.mat(0x1e5aa8, { transparent: true, opacity: 0.8 }));
    this.marker.rotation.x = -Math.PI / 2;
    this.marker.visible = false;
    this.scene.add(this.marker);

    // 휴게실, 공기청정기는 티어에 따라 다시 만든다
    this.restGroup = this.group();
    this.purifierGroup = this.group(LAYOUT.purifier.x, LAYOUT.purifier.z);
  },

  cubeSlots(cx, cz, nx, nz, ny, step, size, parent, color) {
    const out = [];
    for (let y = 0; y < ny; y++)
      for (let z = 0; z < nz; z++)
        for (let x = 0; x < nx; x++) {
          const c = this.box(size, size, size, color || COLORS.cardboard,
            cx + (x - (nx - 1) / 2) * step, 0.2 + size / 2 + y * (size + 0.02), cz + (z - (nz - 1) / 2) * step, parent);
          c.visible = false;
          out.push(c);
        }
    return out;
  },

  makeArm(parent, x, z) {
    const g = this.group(x, z, parent);
    this.cyl(0.28, 0.34, 0.3, 0x455a64, 0, 0.15, 0, g);
    const pivot = new THREE.Group();
    pivot.position.y = 0.3;
    g.add(pivot);
    this.box(0.16, 1.3, 0.16, 0xf39c12, 0, 0.65, 0, pivot);
    const fore = this.box(0.9, 0.14, 0.14, 0xf39c12, -0.4, 1.3, 0, pivot);
    this.sphere(0.12, 0x263238, -0.85, 1.3, 0, pivot);
    g.visible = false;
    g.userData.pivot = pivot;
    g.userData.fore = fore;
    return g;
  },

  makeTruck(color) {
    const g = this.group();
    this.box(4.4, 2.0, 2.1, 0xeeeeee, 0, 1.4, 0, g);          // 짐칸
    this.box(4.42, 0.3, 2.12, color, 0, 2.3, 0, g);           // 짐칸 띠
    this.box(1.5, 1.7, 2.0, color, 2.95, 1.25, 0, g);         // 운전석
    this.box(0.1, 0.7, 1.7, 0x90caf9, 3.72, 1.55, 0, g);      // 앞유리
    for (const [x, z] of [[-1.4, 1.05], [1.2, 1.05], [2.9, 1.05], [-1.4, -1.05], [1.2, -1.05], [2.9, -1.05]]) {
      const w = this.cyl(0.42, 0.42, 0.3, 0x263238, x, 0.42, z, g);
      w.rotation.x = Math.PI / 2;
    }
    return g;
  },

  makeChar(color, isPlayer) {
    const g = this.group();
    const body = new THREE.Mesh(new THREE.CapsuleGeometry(0.32, 0.5, 4, 10), this.mat(color));
    body.position.y = 0.66;
    body.castShadow = true;
    g.add(body);
    const head = this.sphere(0.24, COLORS.skin, 0, 1.36, 0, g);
    if (isPlayer) {
      this.cyl(0.26, 0.26, 0.14, 0x0d2b52, 0, 1.56, 0, g);
      this.box(0.36, 0.04, 0.2, 0x0d2b52, 0, 1.5, 0.18, g);
    } else {
      this.cyl(0.25, 0.25, 0.1, 0xf1c40f, 0, 1.55, 0, g);
    }
    this.box(0.08, 0.08, 0.06, 0x222222, -0.09, 1.4, 0.21, g);
    this.box(0.08, 0.08, 0.06, 0x222222, 0.09, 1.4, 0.21, g);
    const stack = new THREE.Group();
    stack.position.set(0, 0.85, 0.42);
    g.add(stack);
    g.userData = { body, head, stack, sig: '' };
    return g;
  },

  addAlba(a) {
    const palette = [0xe67e22, 0x16a085, 0xc0392b, 0x8e44ad, 0x2c3e50, 0xd35400, 0x27ae60, 0x7f8c8d];
    const m = this.makeChar(palette[a.id % palette.length], false);
    this.albaMeshes.set(a.id, m);
  },
  removeAlba(a) {
    const m = this.albaMeshes.get(a.id);
    if (m) this.scene.remove(m);
    this.albaMeshes.delete(a.id);
  },

  setStack(mesh, hand) {
    const ud = mesh.userData;
    const sig = hand.map(e => e.kind + (e.item !== undefined ? e.item : '')).join('|');
    if (sig === ud.sig) return;
    ud.sig = sig;
    ud.stack.clear();
    hand.forEach((e, i) => {
      const s = e.kind === 'box' ? 0.42 : 0.3;
      const c = new THREE.Mesh(new THREE.BoxGeometry(s, s * 0.85, s), this.mat(this.entryColor(e)));
      c.position.y = i * (s * 0.85 + 0.03) + 0.15;
      c.castShadow = true;
      ud.stack.add(c);
    });
  },

  // ---------- 매 프레임 동기화 ----------
  sync(dt) {
    this.clock += dt;
    const t = this.clock;

    // 캐릭터
    this.syncChar(this.player, G.player, t);
    const tg = G.player.target;
    this.marker.visible = !!tg;
    if (tg) this.marker.position.set(tg.x, 0.03, tg.z);
    for (const a of G.albas) {
      const m = this.albaMeshes.get(a.id);
      if (!m) continue;
      this.syncChar(m, a, t);
      m.userData.body.material = this.mat(a.resting ? 0xb0bec5 : this.albaColor(a));
      m.userData.body.rotation.z = a.resting ? Math.PI / 2.4 : 0;
      m.userData.body.position.y = a.resting ? 0.4 : m.userData.body.position.y;
    }

    // 입고 트럭
    G.inTrucks.forEach((tr, i) => {
      const m = this.inTruckMeshes[i];
      m.visible = tr.state !== 'away';
      const p = S.truckPos(tr.slot, tr.off);
      m.position.set(p.x, 0, p.z);
    });

    // 하차장
    this.stagingCubes.forEach((c, i) => {
      c.visible = i < G.staging.length;
      if (c.visible) c.material = this.mat(this.typeColor(G.staging[i]));
    });

    // 보관함
    G.bins.forEach((b, i) => {
      const o = this.binObjs[i];
      if (!o) return;
      const sig = [b.unlocked, b.checked, b.unchecked, b.broken].join('|');
      if (sig !== o.sig) {
        o.sig = sig;
        const col = b.unlocked ? this.typeColor(b.item) : COLORS.locked;
        o.body.material = b.unlocked ? this.mat(0xeceff1) : this.mat(COLORS.locked, { transparent: true, opacity: 0.35 });
        o.sign.material = b.unlocked ? this.mat(col) : this.mat(COLORS.locked, { transparent: true, opacity: 0.35 });
        const total = b.checked + b.unchecked, n = Math.min(10, Math.ceil(total / 2)), nc = Math.ceil(b.checked / 2);
        o.cubes.forEach((c, k) => {
          c.visible = k < n;
          c.material = this.mat(k < nc ? col : COLORS.cardboard);
        });
        o.brk.visible = b.broken > 0;
      }
      o.arm.visible = b.unlocked && S.isAuto('inspect');
      if (o.arm.visible) o.arm.userData.pivot.rotation.y = b.armProgress > 0 ? Math.sin(t * 6) * 0.8 : 0;
    });

    // 포장대
    G.tables.forEach((tb, i) => {
      const o = this.tableObjs[i];
      o.top.material = tb.unlocked ? this.mat(COLORS.wood) : this.mat(COLORS.locked, { transparent: true, opacity: 0.35 });
      const del = [];
      if (tb.order) for (const [k, q] of Object.entries(tb.delivered)) for (let n = 0; n < q; n++) del.push(+k);
      o.items.forEach((c, k) => {
        c.visible = k < del.length;
        if (c.visible) c.material = this.mat(this.typeColor(del[k]));
      });
      o.boxes.forEach((c, k) => { c.visible = k < tb.boxes.length; });
      o.machine.visible = tb.unlocked && S.isAuto('pack');
      if (o.machine.visible) o.machine.position.y = tb.machineProgress > 0 ? Math.abs(Math.sin(t * 8)) * 0.15 : 0;
    });

    // 출고 도크와 트럭
    G.docks.forEach((d, i) => {
      const o = this.dockObjs[i];
      if (!o) return;
      const col = CONFIG.destinations[i].color;
      o.pad.material = d.unlocked ? this.mat(col, { transparent: true, opacity: 0.3 }) : this.mat(COLORS.locked, { transparent: true, opacity: 0.4 });
      o.cubes.forEach((c, k) => { c.visible = k < d.waiting.length; });
      o.arm.visible = d.unlocked && S.isAuto('load');
      if (o.arm.visible) o.arm.userData.pivot.rotation.y = Math.PI + Math.sin(t * 3) * 0.6;
      if (d.truck && !this.truckMeshes[i]) {
        this.truckMeshes[i] = this.makeTruck(col);
        this.truckMeshes[i].rotation.y = this.truckYaw(d.slot.truck.rot);
      }
      if (!d.truck && this.truckMeshes[i]) { this.scene.remove(this.truckMeshes[i]); this.truckMeshes[i] = null; }
      if (d.truck) {
        const p = S.truckPos(d.slot.truck, d.truck.off);
        this.truckMeshes[i].position.set(p.x, 0, p.z);
      }
    });

    this.syncZones(t);
    this.syncCare(t);
    this.syncBelts();
    this.syncFlyers();
    this.syncLighting(dt);
  },

  albaColor(a) {
    const palette = [0xe67e22, 0x16a085, 0xc0392b, 0x8e44ad, 0x2c3e50, 0xd35400, 0x27ae60, 0x7f8c8d];
    return palette[a.id % palette.length];
  },

  syncChar(m, a, t) {
    m.position.set(a.x, 0, a.z);
    m.rotation.y = a.facing;
    m.userData.body.position.y = 0.66 + (a.moving ? Math.abs(Math.sin(t * 12)) * 0.08 : 0);
    m.userData.head.position.y = 1.36 + (a.moving ? Math.abs(Math.sin(t * 12)) * 0.08 : 0);
    this.setStack(m, a.hand);
  },

  // 상호작용 범위: 바닥 원, 플레이어가 들어가 있는 범위는 강조
  syncZones(t) {
    const active = G.player.zoneKey, seen = new Set();
    for (const z of Zones.list()) {
      seen.add(z.key);
      let r = this.zoneRings.get(z.key);
      if (!r) {
        r = this.makeRing(this.zoneColor(z));
        this.zoneRings.set(z.key, r);
      }
      r.visible = true;
      r.position.set(z.x, 0.03, z.z);
      const on = z.key === active;
      if (r.userData.on !== on) {
        r.userData.on = on;
        r.userData.fill.material.opacity = on ? 0.35 : 0.16;
        r.userData.line.material.opacity = on ? 1 : 0.7;
      }
      r.userData.line.scale.setScalar(on ? 1 + Math.sin(this.clock * 6) * 0.04 : 1);
    }
    for (const [k, r] of this.zoneRings) if (!seen.has(k)) r.visible = false;
  },

  makeRing(color) {
    const R = CONFIG.interactRadius;
    const g = new THREE.Group();
    const opts = { color, transparent: true, depthWrite: false };
    const fill = new THREE.Mesh(new THREE.CircleGeometry(R, 40), new THREE.MeshBasicMaterial({ ...opts, opacity: 0.16 }));
    const line = new THREE.Mesh(new THREE.RingGeometry(R - 0.12, R, 48), new THREE.MeshBasicMaterial({ ...opts, opacity: 0.7 }));
    fill.rotation.x = line.rotation.x = -Math.PI / 2;
    line.position.y = 0.005;
    g.add(fill, line);
    g.userData = { fill, line, on: null };
    this.scene.add(g);
    return g;
  },

  zoneColor(z) {
    switch (z.kind) {
      case 'truck': return 0xf39c12;
      case 'staging': return 0xd4a017;
      case 'bin': return this.typeColor(z.ref.item);
      case 'table': return 0x2e9e57;
      case 'dock': return CONFIG.destinations[z.ref.idx].color;
      default: return 0x546e7a;
    }
  },

  syncCare(t) {
    const tier = G.care.rest;
    if (this.built.restTier !== tier) {
      this.built.restTier = tier;
      this.restGroup.clear();
      // 가구 배치는 9 x 7 휴게실 기준, 구역 크기에 맞춰 늘이거나 줄인다
      const r = LAYOUT.rest, g = this.restGroup, kx = r.w / 9, kz = r.d / 7;
      const X = dx => r.x + dx * kx, Z = dz => r.z + dz * kz;
      const sofa = [0x8d6e63, 0x5c6bc0, 0x8e24aa, 0xd4af37][tier];
      this.restFloor.material = this.mat([0xd8cfc4, 0xd6dcf2, 0xe6d3f0, 0xf6e8b8][tier]);
      this.box(3.4 * kx, 0.6, 1.0, sofa, X(-1.2), 0.3, Z(-2.6), g);
      this.box(3.4 * kx, 0.9, 0.3, sofa, X(-1.2), 0.7, Z(-3.05), g);
      this.cyl(0.6, 0.6, 0.5, COLORS.wood, X(2), 0.25, Z(-1.8), g);
      if (tier >= 1) {
        this.cyl(0.3, 0.25, 0.5, 0x795548, X(3.4), 0.25, Z(2.8), g);
        this.sphere(0.5, 0x43a047, X(3.4), 0.9, Z(2.8), g);
        this.box(3 * kx, 0.6, 1, sofa, X(-1.6), 0.3, Z(2.6), g);
      }
      if (tier >= 2) {
        this.cyl(0.05, 0.05, 1.8, 0x424242, X(-3.4), 0.9, Z(-2.8), g);
        this.sphere(0.3, 0xfff59d, X(-3.4), 1.9, Z(-2.8), g, { emissive: 0xfff59d });
        const rug = new THREE.Mesh(new THREE.CircleGeometry(1.8 * Math.min(kx, kz), 24), this.mat(0xc62828));
        rug.rotation.x = -Math.PI / 2;
        rug.position.set(r.x, 0.02, r.z);
        g.add(rug);
        this.box(1.6, 1.0, 0.1, 0x212121, X(2.2), 1.1, Z(-3.3), g);
      }
      if (tier >= 3) {
        this.cyl(0.4, 0.5, 0.3, 0xd4af37, r.x, 0.15, r.z, g);
        this.sphere(0.4, 0xffd700, r.x, 0.7, r.z, g, { emissive: 0x806000 });
        this.sphere(0.5, 0x43a047, X(-3.4), 0.6, Z(2.8), g);
      }
    }

    const pt = G.care.purifier;
    if (this.built.purifierTier !== pt) {
      this.built.purifierTier = pt;
      const g = this.purifierGroup;
      g.clear();
      if (pt < 0) {
        this.cyl(0.45, 0.45, 1.0, COLORS.locked, 0, 0.5, 0, g, { transparent: true, opacity: 0.3 });
      } else {
        const h = 1.0 + pt * 0.25;
        this.cyl(0.45, 0.5, h, [0xeceff1, 0xffffff, 0xe3f2fd, 0xfffde7][pt], 0, h / 2, 0, g);
        const ringCol = [0x90a4ae, 0x42a5f5, 0x26c6da, 0xffd700][pt];
        const ring = new THREE.Mesh(new THREE.TorusGeometry(0.48, 0.06, 8, 24), this.mat(ringCol, { emissive: pt >= 2 ? ringCol : 0x000000 }));
        ring.rotation.x = Math.PI / 2;
        ring.position.y = h * 0.7;
        g.add(ring);
        const fan = this.box(0.7, 0.04, 0.12, 0x546e7a, 0, h + 0.05, 0, g);
        g.userData.fan = fan;
      }
    }
    if (this.purifierGroup.userData.fan && pt >= 0) this.purifierGroup.userData.fan.rotation.y = t * (4 + pt * 3);
  },

  syncBelts() {
    for (const id of ['sort', 'transfer', 'outbound']) {
      if (!S.isAuto(id) || this.built.belts[id]) continue;
      this.built.belts[id] = true;
      // 배치의 벨트 라인을 그대로 깐다
      for (const ln of LAYOUT.belts[id].lines) {
        const pts = LayoutLib.linePts(LAYOUT, id, ln);
        for (let i = 0; i < pts.length - 1; i++) this.belt(pts[i], pts[i + 1]);
      }
    }
  },

  belt(a, b) {
    const dx = b.x - a.x, dz = b.z - a.z, len = Math.hypot(dx, dz);
    if (len < 0.01) return;
    const m = this.box(0.42, 0.1, len + 0.42, COLORS.belt, (a.x + b.x) / 2, 0.06, (a.z + b.z) / 2);
    m.rotation.y = Math.atan2(dx, dz);
    m.castShadow = false;
  },

  syncFlyers() {
    const alive = new Set(G.flyers);
    for (const [f, m] of this.flyerMeshes) {
      if (!alive.has(f)) { this.scene.remove(m); this.flyerMeshes.delete(f); }
    }
    for (const f of G.flyers) {
      let m = this.flyerMeshes.get(f);
      if (!m) {
        const col = f.kind === 'box' ? COLORS.box : f.kind === 'broken' ? COLORS.broken : this.typeColor(f.ref);
        const s = f.kind === 'box' ? 0.45 : 0.3;
        m = this.box(s, s, s, col, f.x, 0.3, f.z);
        this.flyerMeshes.set(f, m);
      }
      m.position.set(f.x, f.kind === 'broken' || f.kind === 'box' && f.path.length === 2 ? 1.0 : 0.3, f.z);
    }
  },

  syncLighting(dt) {
    const night = G.phase === 'night' ? 1 : 0;
    this.nightK = this.nightK === undefined ? night : this.nightK + (night - this.nightK) * Math.min(1, dt * 1.5);
    // 밤에는 창고 바깥(배경, 바깥 땅)만 어두워지고 내부 조명은 그대로
    const k = this.nightK;
    this.scene.background.setRGB(0.875 - 0.77 * k, 0.9 - 0.75 * k, 0.925 - 0.68 * k);
    this.outsideMat.color.setRGB(0.706 - 0.6 * k, 0.737 - 0.6 * k, 0.776 - 0.5 * k);
  },

  render() { this.renderer.render(this.scene, this.camera); },

  // ---------- 카메라/좌표 ----------
  // 휠 줌: 1배 = 맵 전체, 확대하면 플레이어를 따라간다
  zoomBy(deltaY) {
    this.zoom = U.clamp(this.zoom * (deltaY > 0 ? 0.88 : 1.14), 1, 2.6);
  },

  updateCamera(dt) {
    const c = this.camera, a = Math.min(1, dt * 8);
    c.zoom += (this.zoom - c.zoom) * a;
    const k = U.clamp((c.zoom - 1) / 0.6, 0, 1);
    const tx = this.center.x + (G.player.x - this.center.x) * k;
    const tz = this.center.z + (G.player.z - this.center.z) * k;
    this.camTarget.x += (tx - this.camTarget.x) * a;
    this.camTarget.z += (tz - this.camTarget.z) * a;
    c.position.copy(this.camTarget).add(this.camOffset);
    c.lookAt(this.camTarget);
    c.updateProjectionMatrix();
    c.updateMatrixWorld();
  },

  resize() {
    const W = window.innerWidth, H = window.innerHeight;
    this.renderer.setSize(W, H, false);
    // 맵 전체가 들어가는 기준 화면은 카메라가 맵 중심을 볼 때로 계산한다
    this.camera.position.copy(this.center).add(this.camOffset);
    this.camera.lookAt(this.center);
    this.camera.updateMatrixWorld();
    const inv = this.camera.matrixWorldInverse;
    let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
    for (const x of [-1, LAYOUT.map.w]) for (const z of [-0.5, LAYOUT.map.d + 0.5]) for (const y of [0, 2.5]) {
      const v = new THREE.Vector3(x, y, z).applyMatrix4(inv);
      minX = Math.min(minX, v.x); maxX = Math.max(maxX, v.x);
      minY = Math.min(minY, v.y); maxY = Math.max(maxY, v.y);
    }
    // HUD 영역을 피해서 맵을 화면에 맞춘다
    const reg = { left: 10, top: 100, right: 86, bottom: 46 };
    const aw = Math.max(200, W - reg.left - reg.right), ah = Math.max(200, H - reg.top - reg.bottom);
    const s = Math.min(aw / (maxX - minX), ah / (maxY - minY));
    const cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
    const c = this.camera;
    c.left = cx - (reg.left + aw / 2) / s;
    c.right = c.left + W / s;
    c.top = cy + (reg.top + ah / 2) / s;
    c.bottom = c.top - H / s;
    c.updateProjectionMatrix();
  },

  groundPoint(clientX, clientY) {
    const ndc = new THREE.Vector2(clientX / window.innerWidth * 2 - 1, -(clientY / window.innerHeight) * 2 + 1);
    this.raycaster.setFromCamera(ndc, this.camera);
    const p = new THREE.Vector3();
    return this.raycaster.ray.intersectPlane(this.groundPlane, p) ? p : null;
  },

  project(x, y, z) {
    const v = new THREE.Vector3(x, y, z).project(this.camera);
    return { x: (v.x + 1) / 2 * window.innerWidth, y: (1 - v.y) / 2 * window.innerHeight };
  },
};
