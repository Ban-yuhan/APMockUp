// 3D 위치에 붙는 DOM 라벨 (이모지 표기용)

const Labels = {
  items: [],

  init(root) { this.root = root; },

  add(getPos, cls) {
    const el = U.el('div', 'lbl ' + (cls || ''));
    this.root.appendChild(el);
    const it = { el, getPos, html: null, show: true };
    this.items.push(it);
    return it;
  },

  set(it, html) {
    if (it.html === html) return;
    it.html = html;
    it.el.innerHTML = html;
  },

  remove(it) {
    it.el.remove();
    this.items.splice(this.items.indexOf(it), 1);
  },

  update() {
    for (const it of this.items) {
      if (!it.show || !it.html) { it.el.style.display = 'none'; continue; }
      const p = it.getPos();
      const s = World.project(p.x, p.y, p.z);
      it.el.style.display = '';
      it.el.style.transform = `translate(${s.x.toFixed(1)}px, ${s.y.toFixed(1)}px) translate(-50%, -100%)`;
    }
  },
};
