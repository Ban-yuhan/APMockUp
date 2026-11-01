// 자동화(Lv4): 컨베이어 벨트, 기계손, 포장 기계

const Auto = {
  // 컨베이어 경로: 출발 시설의 벨트 출구 → (벨트 라인, 한 방향) → 도착 시설의 벨트 입구. 연결이 없으면 직선
  paths: {
    route(stage, src, dst, a, b) { return LayoutLib.cachedRoute(stage, src, dst) || [a, b]; },
    sort(b) {
      return Auto.paths.route('sort', 'staging:0:beltOut', `bin:${S.binSlotIdx(b)}:beltIn`, S.pos(LAYOUT.staging), S.binPos(b));
    },
    transfer(b, t) {
      return Auto.paths.route('transfer', `bin:${S.binSlotIdx(b)}:beltOut`, `table:${t.idx}:beltIn`, S.binPos(b), S.tablePos(t));
    },
    outbound(t, d) {
      return Auto.paths.route('outbound', `table:${t.idx}:beltOut`, `dock:${S.dockSlotIdx(d)}:beltIn`, S.tablePos(t), S.dockPos(d));
    },
    load(d) { return [S.dockPos(d), S.truckDoor(d.slot.truck)]; },
    broken(b) { return [S.binPos(b), S.pos(LAYOUT.trash)]; },
  },

  fly(kind, ref, path, onArrive) {
    G.flyers.push({ kind, ref, path, seg: 0, x: path[0].x, z: path[0].z, onArrive });
  },

  update(dt) {
    const c = CONFIG.automation;

    if (S.isAuto('sort')) {
      G.auto.sort += dt;
      if (G.auto.sort >= c.sortInterval) {
        G.auto.sort = 0;
        const i = G.staging.findIndex(it => G.bins[it].unlocked && S.binSpace(G.bins[it]) > 0);
        if (i >= 0) {
          const b = G.bins[G.staging.splice(i, 1)[0]];
          b.incomingRaw++;
          Auto.fly('raw', b.item, Auto.paths.sort(b), () => { b.incomingRaw--; b.unchecked++; });
        }
      }
    }

    if (S.isAuto('inspect')) {
      for (const b of G.bins) {
        if (!b.unlocked || b.unchecked <= 0) { b.armProgress = 0; continue; }
        b.armProgress += dt;
        if (b.armProgress < c.inspectTime) continue;
        b.armProgress = 0;
        b.unchecked--;
        if (Math.random() < CONFIG.breakChance.inspect) {
          Auto.fly('broken', b.item, Auto.paths.broken(b), () => { G.stats.broken++; });
        } else {
          b.checked++;
        }
      }
    }

    if (S.isAuto('transfer')) {
      G.auto.transfer += dt;
      if (G.auto.transfer >= c.transferInterval) {
        G.auto.transfer = 0;
        Auto.transferOne();
      }
    }

    if (S.isAuto('pack')) {
      for (const t of G.tables) {
        if (!t.unlocked || !S.tableReady(t)) { t.machineProgress = 0; continue; }
        t.machineProgress += dt;
        if (t.machineProgress >= c.packTime) Orders.pack(t);
      }
    }

    if (S.isAuto('outbound')) {
      G.auto.outbound += dt;
      if (G.auto.outbound >= c.outboundInterval) {
        G.auto.outbound = 0;
        const t = G.tables.find(t => t.unlocked && t.boxes.some(b => !b.claimedBy));
        if (t) {
          const box = t.boxes.splice(t.boxes.findIndex(b => !b.claimedBy), 1)[0];
          const d = G.docks[box.dest];
          box.claimedBy = 'auto';
          Auto.fly('box', box, Auto.paths.outbound(t, d), () => { box.claimedBy = null; d.waiting.push(box); });
        }
      }
    }

    if (S.isAuto('load')) {
      for (const d of G.docks) {
        const tr = d.truck;
        if (!d.unlocked || !tr || tr.state !== 'present' || tr.mode === 'manual') continue;
        d.autoTimer += dt;
        if (d.autoTimer < c.loadInterval) continue;
        d.autoTimer = 0;
        const i = d.waiting.findIndex(b => LoadLogic.autoFits(tr, b));
        if (i < 0) continue;
        const box = d.waiting.splice(i, 1)[0];
        tr.mode = 'auto';
        tr.autoTarget = c.loadTarget;
        tr.autoCells += box.cells.length;
        tr.autoBoxes.push(box);
        Auto.fly('box', box, Auto.paths.load(d), null);
        if (LoadLogic.fill(tr).ratio >= tr.autoTarget) Docks.depart(d);
      }
    }

    Auto.updateFlyers(dt);
  },

  // 주문서 순서대로, 필요한 검수완료 물품을 포장대로 보낸다
  transferOne() {
    for (const t of S.activeTables()) {
      for (const k of Object.keys(t.order.items)) {
        const item = +k, b = G.bins[item];
        if (S.remainingNeed(t, item) <= 0 || S.outstandingNeed(item) <= 0 || S.avail(b) <= 0) continue;
        b.checked--;
        b.inFlightPick++;
        Auto.fly('pick', item, Auto.paths.transfer(b, t), () => {
          b.inFlightPick--;
          const dest = S.remainingNeed(t, item) > 0 ? t : S.activeTables().find(x => S.remainingNeed(x, item) > 0);
          if (dest) dest.delivered[item] = (dest.delivered[item] || 0) + 1;
          else b.checked++;
        });
        return;
      }
    }
  },

  updateFlyers(dt) {
    const sp = CONFIG.automation.flySpeed * dt;
    for (let i = G.flyers.length - 1; i >= 0; i--) {
      const f = G.flyers[i];
      let left = sp;
      while (left > 0 && f.seg < f.path.length - 1) {
        const q = f.path[f.seg + 1], dx = q.x - f.x, dz = q.z - f.z, d = Math.hypot(dx, dz);
        if (d <= left) { f.x = q.x; f.z = q.z; f.seg++; left -= d; }
        else { f.x += dx / d * left; f.z += dz / d * left; left = 0; }
      }
      if (f.seg >= f.path.length - 1) {
        if (f.onArrive) f.onArrive();
        G.flyers.splice(i, 1);
      }
    }
  },
};
