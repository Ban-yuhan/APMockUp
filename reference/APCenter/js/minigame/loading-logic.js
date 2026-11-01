// 상차 미니게임 로직 (차곡차곡 이식)
// 화면(loading-ui.js)과 분리된 순수 판정 함수만 둔다.
// 트럭 짐칸 = 그리드, 포장 박스 = 물품(블록)

const LoadLogic = {
  key(x, y) { return x + ',' + y; },

  normalize(cells) {
    const minX = Math.min(...cells.map(c => c[0]));
    const minY = Math.min(...cells.map(c => c[1]));
    return cells.map(([x, y]) => [x - minX, y - minY]);
  },

  // 90도 시계 방향 (화면 좌표, y가 아래)
  rotate(cells) {
    return LoadLogic.normalize(cells.map(([x, y]) => [-y, x]));
  },

  randomShape(units) {
    const list = CONFIG.loading.shapes[U.clamp(units, 1, 5)];
    const s = U.pick(list);
    return { name: s.name, cells: s.cells.map(c => c.slice()) };
  },

  createTruck() {
    const def = U.weighted(CONFIG.loading.truckTypes);
    const all = [];
    for (let y = 0; y < def.h; y++)
      for (let x = 0; x < def.w; x++) all.push(LoadLogic.key(x, y));
    // 잠긴 칸: 트럭 종류별 범위 안에서 개수와 위치 모두 랜덤
    const locked = new Set(U.shuffle(all.slice()).slice(0, U.randInt(def.locked[0], def.locked[1])));
    const free = all.filter(k => !locked.has(k));
    const bonus = new Set(U.shuffle(free).slice(0, G.loadUpg.bonus));
    return {
      def, w: def.w, h: def.h, locked, bonus,
      objectives: LoadLogic.genObjectives(free.length),
      placements: [],          // 수동 상차: { box, cells: [[x,y]...] }
      mode: null,              // null | 'manual' | 'auto'
      autoCells: 0, autoBoxes: [], autoTarget: 0,
      state: 'arriving', off: S.TRUCK_AWAY,
    };
  },

  totalCells(truck) { return truck.w * truck.h - truck.locked.size; },

  occupancy(truck, ignore) {
    const occ = new Map();
    for (const p of truck.placements) {
      if (p === ignore) continue;
      for (const [x, y] of p.cells) {
        const k = LoadLogic.key(x, y);
        occ.set(k, (occ.get(k) || 0) + 1);
      }
    }
    return occ;
  },

  overlapUsed(occ) {
    let n = 0;
    for (const c of occ.values()) n += Math.max(0, c - 1);
    return n;
  },

  // cells: 짐칸 좌표로 옮긴 칸 목록
  canPlace(truck, cells, ignore) {
    for (const [x, y] of cells) {
      if (x < 0 || y < 0 || x >= truck.w || y >= truck.h) return false;
      if (truck.locked.has(LoadLogic.key(x, y))) return false;
    }
    const occ = LoadLogic.occupancy(truck, ignore);
    for (const [x, y] of cells) {
      const k = LoadLogic.key(x, y);
      occ.set(k, (occ.get(k) || 0) + 1);
    }
    return LoadLogic.overlapUsed(occ) <= G.loadUpg.compress;
  },

  // 적재율: 모든 칸을 다 채우면 fullRate(200%), ratio는 채운 칸 비율(0~1)
  fill(truck) {
    const total = LoadLogic.totalCells(truck), c = CONFIG.loading;
    if (truck.mode === 'auto') {
      const ratio = truck.autoCells / total;
      return { filled: truck.autoCells, total, ratio, bonusCovered: 0, rate: ratio * c.fullRate };
    }
    const occ = LoadLogic.occupancy(truck);
    let bonusCovered = 0;
    for (const k of truck.bonus) if (occ.has(k)) bonusCovered++;
    const ratio = occ.size / total;
    const rate = Math.min(c.maxRate, ratio * c.fullRate + bonusCovered * c.bonusCellPercent);
    return { filled: occ.size, total, ratio, bonusCovered, rate, overlap: LoadLogic.overlapUsed(occ) };
  },

  loadedBoxes(truck) {
    return truck.mode === 'auto' ? truck.autoBoxes : truck.placements.map(p => p.box);
  },

  rankIndex(count) {
    return CONFIG.loading.rankThresholds.filter(t => count >= t).length;
  },

  // 혼합 박스는 들어간 타입마다 1개씩 센다
  ranks(boxes) {
    const out = {};
    for (const t of Object.keys(CONFIG.types)) out[t] = { count: 0, rank: 0 };
    for (const b of boxes) for (const t of b.types) out[t].count++;
    for (const t of Object.keys(out)) out[t].rank = LoadLogic.rankIndex(out[t].count);
    return out;
  },

  fillMult(rate) {
    const c = CONFIG.loading;
    if (rate > c.maxRate) return c.fillMult[0].mult + (rate - c.maxRate) * c.overPerPoint;
    const hit = c.fillMult.find(f => rate >= f.min);
    return hit ? hit.mult : 0.5 + 0.5 * (rate / 100);
  },

  rankMult(ranks) {
    let steps = 0;
    for (const r of Object.values(ranks)) steps += r.rank;
    return 1 + steps * CONFIG.loading.rankBonusPerStep;
  },

  // ---------- 특수 목표 ----------
  // 트럭마다 1~5개. 해금된 품목/타입 안에서만 뽑는다
  genObjectives(cells) {
    const c = CONFIG.loading.objectives;
    const items = G.bins.filter(b => b.unlocked).map(b => b.item);
    const types = [...new Set(items.map(i => CONFIG.items[i].type))];
    const pool = [];
    for (const t of types) {
      const ranks = c.rank.ranks.filter(r => r < 3 || cells >= c.rank.bigTruckCells);
      const r = U.pick(ranks);
      pool.push({ kind: 'rank', type: t, rank: r, bonus: c.rank.bonus[r] });
    }
    const f = U.pick(c.fill);
    pool.push({ kind: 'fill', rate: f.rate, bonus: f.bonus });
    const n = U.randInt(c.boxes.min, Math.max(c.boxes.min, Math.floor(cells / c.boxes.perCells)));
    pool.push({ kind: 'boxes', n, bonus: n * c.boxes.bonusPer });
    pool.push({ kind: 'item', item: U.pick(items), bonus: c.item.bonus });
    const kinds = c.kinds.filter(k => k.n <= types.length);
    if (kinds.length) { const k = U.pick(kinds); pool.push({ kind: 'kinds', n: k.n, bonus: k.bonus }); }
    const count = Math.min(pool.length, U.randInt(c.count[0], c.count[1]));
    return U.shuffle(pool).slice(0, count);
  },

  objectiveText(o) {
    const rn = CONFIG.loading.rankNames;
    switch (o.kind) {
      case 'rank': { const t = CONFIG.types[o.type]; return `${t.emoji} ${t.name} 랭크 ${rn[o.rank]} 이상`; }
      case 'fill': return `적재율 ${o.rate}% 이상`;
      case 'boxes': return `박스 ${o.n}개 이상 싣기`;
      case 'item': { const it = CONFIG.items[o.item]; return `${it.emoji} ${it.name} 든 박스 싣기`; }
      case 'kinds': return `서로 다른 타입 ${o.n}종 이상`;
    }
    return '';
  },

  // 목표마다 { done, now(현재 진행 표기) }를 붙여 돌려준다. 박스를 빼면 다시 미완료가 된다
  evalObjectives(truck, boxes, ranks, fill) {
    const rn = CONFIG.loading.rankNames;
    const typeCount = new Set(boxes.flatMap(b => b.types)).size;
    return (truck.objectives || []).map(o => {
      let done = false, now = '';
      if (o.kind === 'rank') { const r = ranks[o.type].rank; done = r >= o.rank; now = rn[r]; }
      if (o.kind === 'fill') { done = fill.rate >= o.rate; now = Math.round(fill.rate) + '%'; }
      if (o.kind === 'boxes') { done = boxes.length >= o.n; now = `${boxes.length}/${o.n}`; }
      if (o.kind === 'item') { const n = boxes.filter(b => b.items[o.item]).length; done = n > 0; now = n + '개'; }
      if (o.kind === 'kinds') { done = typeCount >= o.n; now = `${typeCount}/${o.n}`; }
      return { ...o, done, now };
    });
  },

  payout(truck) {
    const boxes = LoadLogic.loadedBoxes(truck);
    const f = LoadLogic.fill(truck);
    const ranks = LoadLogic.ranks(boxes);
    const base = boxes.reduce((s, b) => s + b.reward, 0);
    // 특수 목표는 수동 상차(미니게임)에만 적용. 달성 보너스는 적재율에 더하며 200% 상한을 넘을 수 있다
    const objectives = truck.mode === 'auto' ? [] : LoadLogic.evalObjectives(truck, boxes, ranks, f);
    const objBonus = objectives.filter(o => o.done).reduce((s, o) => s + o.bonus, 0);
    const rate = f.rate + objBonus;
    const fm = LoadLogic.fillMult(rate), rm = LoadLogic.rankMult(ranks);
    return { boxes, fill: f, ranks, objectives, objBonus, rate, base, fillMult: fm, rankMult: rm, total: Math.round(base * fm * rm) };
  },

  // 알바/자동화 고정 적재율용
  autoFits(truck, box) {
    return truck.autoCells + box.cells.length <= LoadLogic.totalCells(truck);
  },
};
