// 배치 시뮬레이터 자체 점검 (editor.html?test)
// 결과는 콘솔과 <pre id="test-out">에, 변형 배치(layout-data.js 형식)는 <pre id="variant-out">에 쓴다.

const EdTest = {
  run() {
    const out = [], log = (...a) => out.push(a.join(' '));
    let ok = true;
    const fail = m => { log('FAIL: ' + m); ok = false; };
    const routes = (L, st) => {
      const g = LayoutLib.buildGraph(L, st), pairs = LayoutLib.beltPairs(L, st);
      return `${pairs.filter(p => LayoutLib.routeKeys(g, p.src, p.dst)).length}/${pairs.length}`;
    };
    const hasRoute = (L, st, src, dst) => !!LayoutLib.routeKeys(LayoutLib.buildGraph(L, st), src, dst);
    try {
      Ed.replace(LayoutLib.makeDefault());
      const errs0 = Ed.issues.filter(i => i.level === 'error');
      log('default issues', Ed.issues.length, 'errors', errs0.length, Ed.issues.map(i => i.msg).join(' | '));
      if (Ed.issues.length) fail('default layout has issues');
      for (const st of LayoutLib.BELT_STAGES) log(`default ${st} routes`, routes(Ed.L, st), 'lines', Ed.L.belts[st].lines.length);

      // 도크는 목적지가 다 쓰였으면 추가 안 됨
      const nd = Ed.L.docks.length;
      Ed.add('dock');
      if (Ed.L.docks.length !== nd) fail('dock added beyond dest count');
      // 보관함: 남는 품목이 없으면 새 품목을 만들어 붙인다
      const nb = Ed.L.bins.length, ni = Ed.L.items.length;
      Ed.add('bin');
      const nbin = Ed.L.bins[Ed.L.bins.length - 1];
      log('add bin → bins', nb, '->', Ed.L.bins.length, 'items', ni, '->', Ed.L.items.length, 'item', nbin.item, 'errors', Ed.issues.filter(i => i.level === 'error').length);
      if (Ed.L.bins.length !== nb + 1 || Ed.L.items.length !== ni + 1 || !LayoutLib.item(Ed.L, nbin.item)) fail('add bin with new item');
      // 품목만 추가 → 경고(보관함 없음) → 그 품목으로 보관함 만들기
      Ed.addItem();
      const lone = Ed.L.items[Ed.L.items.length - 1];
      const warned = Ed.issues.some(i => i.msg.includes('보관함이 없는 품목'));
      Ed.add('bin', lone.id);
      log('item without bin warned', warned, '→ bin for it', Ed.L.bins[Ed.L.bins.length - 1].item === lone.id);
      if (!warned || Ed.L.bins[Ed.L.bins.length - 1].item !== lone.id) fail('add bin for item');
      // 품목 이름 바꾸기 → 보관함 이름에 반영
      Ed.change(L => { LayoutLib.item(L, lone.id).name = '컵라면'; });
      if (!Ed.name({ t: 'bin', i: Ed.L.bins.length - 1 }).includes('컵라면')) fail('item rename');
      // 품목 삭제 → 그 보관함도 삭제 (confirm 없이 바로 실행되도록 첫 번째 새 품목은 보관함째)
      const before0 = Ed.L.bins.length;
      window.confirm = () => true;
      Ed.removeItem(nbin.item);
      log('remove item → bins', before0, '->', Ed.L.bins.length, 'items', Ed.L.items.length);
      if (Ed.L.bins.length !== before0 - 1 || LayoutLib.item(Ed.L, nbin.item)) fail('remove item');
      // 보관함 복제 → 새 품목
      const ni2 = Ed.L.items.length;
      Ed.duplicate({ t: 'bin', i: 0 });
      if (Ed.L.items.length !== ni2 + 1 || Ed.issues.some(i => i.level === 'error')) fail('duplicate bin with new item');
      Ed.replace(LayoutLib.makeDefault());
      Ed.add('table');
      const ti = Ed.L.tables.length - 1, t = Ed.L.tables[ti];
      // 회전: 지점·포트도 같이 돈다
      const before = { ...t.in }, pBefore = { ...t.beltIn };
      Ed.rotate({ t: 'table', i: ti });
      const t2 = Ed.L.tables[ti];
      log('rotate table rot', t2.rot, 'in', JSON.stringify(before), '->', JSON.stringify(t2.in), 'beltIn', JSON.stringify(pBefore), '->', JSON.stringify(t2.beltIn));
      if (t2.rot !== 270 || LayoutLib.dist(t2.in, t2) !== LayoutLib.dist(before, t) || LayoutLib.dist(t2.beltIn, t2) !== LayoutLib.dist(pBefore, t)) fail('rotate');
      Ed.remove({ t: 'table', i: ti });

      // 구역 드래그: 안의 시설·벨트 점이 같이 움직인다 (Alt면 구역만)
      const sx = Ed.L.staging.x, base = LayoutLib.clone(Ed.L);
      Ed.dragMove(base, { t: 'area', i: 0 }, 1.1, 0, {});
      log('area drag staging x', sx, '->', Ed.L.staging.x, 'beltOut x', base.staging.beltOut.x, '->', Ed.L.staging.beltOut.x);
      if (Ed.L.staging.x !== sx + 1 || Ed.L.staging.beltOut.x !== base.staging.beltOut.x + 1) fail('area contents move');
      Ed.dragMove(base, { t: 'area', i: 0 }, 1, 0, { alone: true });
      if (Ed.L.staging.x !== sx) fail('area alone move');
      Ed.L = base;

      // 상호작용 지점 분리와 다시 붙이기
      const b0 = Ed.L.bins[0], pref = { t: 'point', of: { t: 'bin', i: 0 }, role: 'out' };
      Ed.dragMove(LayoutLib.clone(Ed.L), pref, 0, -4.4, {});
      if (Ed.L.bins[0].out.z !== Ed.snapV(b0.out.z - 4.4) || Ed.L.bins[0].in.z !== b0.in.z) fail('point split');
      Ed.dragMove(LayoutLib.clone(Ed.L), pref, 0.2, 4.3, {});
      if (Ed.L.bins[0].out.z !== Ed.L.bins[0].in.z || Ed.L.bins[0].out.x !== Ed.L.bins[0].in.x) fail('point snap merge');

      // ---- 벨트 라인 ----
      Ed.change(L => { L.belts.sort.lines = []; });
      Ed.setMode('belt:sort');
      const S0 = { fac: 'staging', i: 0, port: 'beltOut' }, so = Ed.L.staging.beltOut;
      // 메인 라인: 하차장 출구 → 위로 → 오른쪽 (Shift 정렬), 떨어진 끝
      Ed.startDraw('sort', so, S0);
      Ed.drawClick(so.x + 0.2, 10.4, true);
      Ed.drawClick(31, 10.7, true);
      Ed.finishDraw(null);
      const trunk = Ed.L.belts.sort.lines[0];
      log('trunk', JSON.stringify(trunk.points), 'from', JSON.stringify(trunk.from), 'to', JSON.stringify(trunk.to));
      if (trunk.points.length !== 3 || trunk.points[1].x !== so.x || trunk.points[2].z !== trunk.points[1].z) fail('trunk draw');
      // 지선: 메인 라인 위에서 분기 → 보관함 1 입구에 붙이고 끝
      const bi0 = Ed.L.bins[0].beltIn;
      Ed.startDraw('sort', { x: 16, z: trunk.points[1].z }, { line: trunk.id });
      Ed.drawClick(bi0.x + 0.1, bi0.z, false);
      const br = Ed.L.belts.sort.lines[1];
      log('branch to', JSON.stringify(br.to), 'routes', routes(Ed.L, 'sort'));
      if (!br.to || br.to.fac !== 'bin' || br.to.i !== 0) fail('branch attach to port');
      if (!hasRoute(Ed.L, 'sort', 'staging:0:beltOut', 'bin:0:beltIn')) fail('route via branch');
      if (hasRoute(Ed.L, 'sort', 'staging:0:beltOut', 'bin:1:beltIn')) fail('unexpected route');
      // 방향 뒤집기 → 길이 끊김
      Ed.reverseLine({ t: 'line', st: 'sort', id: trunk.id });
      log('after reverse route', hasRoute(Ed.L, 'sort', 'staging:0:beltOut', 'bin:0:beltIn'));
      if (hasRoute(Ed.L, 'sort', 'staging:0:beltOut', 'bin:0:beltIn')) fail('one-way after reverse');
      Ed.undo();
      if (!hasRoute(Ed.L, 'sort', 'staging:0:beltOut', 'bin:0:beltIn')) fail('undo reverse');

      // 떨어진 라인을 그린 뒤 끝점을 끌어 붙이기
      Ed.startDraw('sort', { x: 20, z: 12 }, null);
      Ed.drawClick(26, 12, false);
      Ed.finishDraw(null);
      const fl = Ed.L.belts.sort.lines[Ed.L.belts.sort.lines.length - 1], fid = fl.id;
      Ed.dragMove(LayoutLib.clone(Ed.L), { t: 'lpt', st: 'sort', id: fid, k: 0 }, 21 - 20, trunk.points[1].z - 12, {});
      const bi2 = Ed.L.bins[2].beltIn;
      Ed.dragMove(LayoutLib.clone(Ed.L), { t: 'lpt', st: 'sort', id: fid, k: 1 }, bi2.x - 26 + 0.2, bi2.z - 12, {});
      Ed.afterChange();
      const fl2 = Ed.get({ t: 'line', st: 'sort', id: fid });
      log('dragged ends from', JSON.stringify(fl2.from), 'to', JSON.stringify(fl2.to));
      if (!fl2.from || fl2.from.line !== trunk.id || !fl2.to || fl2.to.i !== 2) fail('drag attach');
      if (!hasRoute(Ed.L, 'sort', 'staging:0:beltOut', 'bin:2:beltIn')) fail('route after drag attach');
      // 꺾이는 점 추가 / 삭제
      Ed.insertBend('sort', fid, 0, { x: 22, z: 8 });
      const nb1 = Ed.get({ t: 'line', st: 'sort', id: fid }).points.length;
      Ed.remove({ t: 'lpt', st: 'sort', id: fid, k: 1 });
      const nb2 = Ed.get({ t: 'line', st: 'sort', id: fid }).points.length;
      log('bend add/remove', nb1, nb2);
      if (nb1 !== 3 || nb2 !== 2) fail('bend add/remove');
      // 메인 라인 삭제 → 붙어 있던 지선은 떨어짐
      Ed.remove({ t: 'line', st: 'sort', id: trunk.id });
      const detached = Ed.L.belts.sort.lines.every(ln => !ln.from || ln.from.line === undefined);
      log('delete trunk → branches detached', detached);
      if (!detached) fail('detach on delete');
      Ed.setMode('select');

      // 보관함 삭제 → 벨트가 붙은 번호 정리
      Ed.replace(LayoutLib.makeDefault());
      const lnTo1 = Ed.L.belts.sort.lines.find(ln => ln.to && ln.to.fac === 'bin' && ln.to.i === 1).id;
      const lnTo0 = Ed.L.belts.sort.lines.find(ln => ln.to && ln.to.fac === 'bin' && ln.to.i === 0).id;
      Ed.remove({ t: 'bin', i: 0 });
      const a1 = Ed.get({ t: 'line', st: 'sort', id: lnTo1 }).to, a0 = Ed.get({ t: 'line', st: 'sort', id: lnTo0 }).to;
      log('remove bin0 → line to bin1 now', JSON.stringify(a1), 'line to bin0 now', JSON.stringify(a0));
      if (!a1 || a1.i !== 0 || a0 !== null) fail('reindex on remove');

      // 자동 생성 (초안)
      Ed.replace(LayoutLib.makeDefault());
      Ed.change(L => { for (const st of LayoutLib.BELT_STAGES) L.belts[st].lines = LayoutLib.autoLines(L, st); });
      log('auto routes', LayoutLib.BELT_STAGES.map(st => `${st} ${routes(Ed.L, st)}`).join(' · '));
      if (routes(Ed.L, 'sort') !== '8/8' || routes(Ed.L, 'transfer') !== '24/24') fail('auto sort/transfer should connect all');

      // 예전 형식(버전 1) 불러오기 → 버전 2로
      const v1 = LayoutLib.makeDefault();
      v1.version = 1;
      v1.belts = { sort: { nodes: [], edges: [] }, transfer: { nodes: [], edges: [] }, outbound: { nodes: [], edges: [] } };
      delete v1.staging.beltOut;
      v1.bins.forEach(b => { delete b.beltIn; delete b.beltOut; });
      Ed.replace(v1);
      log('v1 upgrade version', Ed.L.version, 'sort lines', Ed.L.belts.sort.lines.length, 'errors', Ed.issues.filter(i => i.level === 'error').length);
      if (Ed.L.version !== 2 || !Ed.L.belts.sort.lines.length) fail('v1 upgrade');

      // 내보내기 → 가져오기 왕복
      Ed.replace(LayoutLib.makeDefault());
      const text = `window.APC_LAYOUT = ${JSON.stringify(Ed.L, null, 2)};\n`;
      const snap = Ed.snapshot();
      Ed.importText(text);
      log('export/import round trip', Ed.snapshot() === snap);
      if (Ed.snapshot() !== snap) fail('round trip');

      // 변형 배치 (게임 반영 확인용)
      const V = LayoutLib.makeDefault();
      V.name = '변형 배치 (점검용)';
      V.staging.in = { x: 7.5, z: 17.5 };                 // 하차장 넣기/꺼내기 분리
      V.staging.out = { x: 11.5, z: 17.5 };
      V.staging.beltOut = { x: 11.5, z: 16 };              // 벨트 출구도 옮김 (붙은 라인이 따라감)
      V.bins.forEach(b => {                                 // 보관함 꺼내기 지점은 뒤쪽
        const d = LayoutLib.dir(b.rot);
        b.out = { x: b.x, z: r2(b.z - d.z * 2.2) };
      });
      // 새 품목 2개 + 보관함 2개 (보관 구역 아래쪽), 분류·이송 벨트는 자동 생성
      V.items.push({ id: 'cupnoodle', name: '컵라면', emoji: '🍥', type: 'food', price: 9 }, { id: 'manga', name: '만화책', emoji: '📗', type: 'book', price: 11 });
      [['cupnoodle', 16], ['manga', 21]].forEach(([item, x]) => V.bins.push({
        x, z: 24.5, rot: 90, item, cost: 0, in: { x, z: 26.7 }, out: { x, z: 26.7 }, inspect: { x, z: 26.7 },
        beltIn: { x: x - 0.35, z: 24.5 }, beltOut: { x: x + 0.35, z: 24.5 },
      }));
      V.belts.sort.lines = LayoutLib.autoLines(V, 'sort');
      V.belts.transfer.lines = LayoutLib.autoLines(V, 'transfer');
      Ed.replace(V);
      const vErr = Ed.issues.filter(i => i.level === 'error'), vWarn = Ed.issues.filter(i => i.level === 'warn');
      log('variant errors', vErr.length, 'warns', vWarn.length, vWarn.map(i => i.msg).join(' | '),
        'items', Ed.L.items.length, 'bins', Ed.L.bins.length, 'sort routes', routes(Ed.L, 'sort'), 'transfer routes', routes(Ed.L, 'transfer'));
      if (vErr.length) fail('variant has errors');
      const pre = document.createElement('pre');
      pre.id = 'variant-out';
      pre.textContent = `window.APC_LAYOUT = ${JSON.stringify(Ed.L)};`;
      document.body.appendChild(pre);
    } catch (e) {
      ok = false;
      log('ERROR', e.stack || e);
    }
    log(ok ? 'EDITOR TEST PASS' : 'EDITOR TEST FAIL');
    const pre = document.createElement('pre');
    pre.id = 'test-out';
    pre.textContent = out.join('\n');
    document.body.appendChild(pre);
    console.log(out.join('\n'));
  },
};
