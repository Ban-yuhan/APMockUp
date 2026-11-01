// 공용 유틸

const U = {
  rand(a, b) { return a + Math.random() * (b - a); },
  randInt(a, b) { return Math.floor(a + Math.random() * (b - a + 1)); },
  pick(arr) { return arr[Math.floor(Math.random() * arr.length)]; },
  clamp(v, a, b) { return Math.max(a, Math.min(b, v)); },
  dist(a, b) { return Math.hypot(a.x - b.x, a.z - b.z); },

  // entries 중 weight 필드 비율로 하나 뽑기
  weighted(entries) {
    const total = entries.reduce((s, e) => s + e.weight, 0);
    let r = Math.random() * total;
    for (const e of entries) {
      r -= e.weight;
      if (r <= 0) return e;
    }
    return entries[entries.length - 1];
  },

  shuffle(arr) {
    for (let i = arr.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [arr[i], arr[j]] = [arr[j], arr[i]];
    }
    return arr;
  },

  money(n) { return '💰' + Math.round(n).toLocaleString('ko-KR'); },

  el(tag, cls, html) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html !== undefined) e.innerHTML = html;
    return e;
  },

  bar(ratio, cls) {
    const p = Math.round(U.clamp(ratio, 0, 1) * 100);
    return `<span class="bar ${cls || ''}"><i style="width:${p}%"></i></span>`;
  },

  hex(c) { return '#' + c.toString(16).padStart(6, '0'); },
};
