// The checks of the layout of a report, shared by the fuzz tests (2.17.0): no undefined / NaN / null in the cells and the values, no
// overlapping merges, the values of each block inside the grid, every image and chart inside the page, whole on one page of the PDF,
// over empty cells and apart from the others, and in English no Greek letter of the script.
const assert = require('assert');

function makeCheck(G) {
  const GREEK = /[Ͱ-Ͽἀ-῿]/;
  const badText = (s, en) => typeof s === 'string' && (/undefined|NaN|Infinity|\[object/.test(s) || /\bnull\b/.test(s) ||
    (en && GREEK.test(s.replace(/(^|[^Ͱ-Ͽ])Δ(?![Ͱ-Ͽ])/g, '$1'))));

  /** The checks of one layout. */
  function check(L, what, en) {
    // 15 columns (1124 px); a race in yards has one more (the lengths in yards too).
    const g = L.grid, cols = (L.print && L.print.cols) || 15, page = L.page.height;
    let W = 0;
    for (let c = 1; c <= cols; c++) W += G.swimColumnWidth_(c);
    assert.ok(page > 0, what + ': page height');
    // Cells and values.
    Object.values(g.cells).forEach(c => {
      assert.ok(!badText(c.v, en), `${what}: cell ${c.r}:${c.c} «${c.v}»`);
      assert.ok(!(typeof c.v === 'number' && !isFinite(c.v)), `${what}: number of cell ${c.r}:${c.c}`);
      assert.ok(c.c >= 1 && c.c <= cols && c.r >= 1, `${what}: cell ${c.r}:${c.c} outside the grid`);
    });
    const dynamic = {};
    (L.inputs.blocks || []).forEach(b => {
      assert.ok(b.values.length > 0 && b.r >= 1 && b.c >= 1, `${what}: block ${b.r}:${b.c}`);
      const width = b.values[0].length;
      b.values.forEach((row, i) => {
        assert.strictEqual(row.length, width, `${what}: block ${b.r}:${b.c} row ${i} has ${row.length} values, the first ${width}`);
        assert.ok(b.c + row.length - 1 <= cols, `${what}: block ${b.r}:${b.c} outside the ${cols} columns`);
        row.forEach((v, j) => {
          assert.ok(v !== undefined && v !== null && !(typeof v === 'number' && !isFinite(v)) && !badText(v, en), `${what}: value ${b.r + i}:${b.c + j} «${v}»`);
          if (v !== '') dynamic[(b.r + i) + ':' + (b.c + j)] = v;
        });
      });
    });
    // Merges.
    const ms = g.merges;
    const ov = (a, b) => !(a[2] < b[0] || b[2] < a[0] || a[3] < b[1] || b[3] < a[1]);
    for (let i = 0; i < ms.length; i++) for (let j = i + 1; j < ms.length; j++) assert.ok(!ov(ms[i], ms[j]), `${what}: overlapping merges ${ms[i]} / ${ms[j]}`);
    // Geometry: the rows on the pages of the PDF.
    const H = {}, top = {}, pageOf = {};
    let y = 0, pg = 0;
    for (let r = 1; r <= L.lastRow + 40; r++) {
      const h = G.swimRowHeight_(g, r);
      assert.ok(h > 0, `${what}: height of row ${r}`);
      if (y > 0 && y + h > page) { pg++; y = 0; }
      H[r] = h; top[r] = y; pageOf[r] = pg; y += h;
    }
    const x0 = [0];
    for (let c = 1; c <= cols; c++) x0.push(x0[c - 1] + G.swimColumnWidth_(c));
    const span = {};
    ms.forEach(m => { span[m[0] + ':' + m[1]] = m; });
    // The cells with something written (as built, or in each update), as rectangles of their page.
    const texts = [];
    const filled = {};
    Object.values(g.cells).forEach(c => { if (c.v !== '' && c.v !== undefined && c.v !== null) filled[c.r + ':' + c.c] = true; });
    Object.keys(dynamic).forEach(k => { filled[k] = true; });
    Object.keys(filled).forEach(k => {
      const r = +k.split(':')[0], c = +k.split(':')[1], m = span[k];
      const r2 = m ? m[2] : r, c2 = m ? m[3] : c;
      texts.push({ page: pageOf[r], x: x0[c - 1], y: top[r], x2: x0[c2], y2: pageOf[r2] === pageOf[r] ? top[r2] + H[r2] : page, key: k });
    });
    const boxes = [];
    const box = (o, kind) => {
      const oy = o.offsetY === undefined ? (kind === 'image' ? 4 : 0) : o.offsetY;
      const x = kind === 'image' ? o.x : x0[o.col - 1] + (o.offsetX || 0);
      assert.ok(o.width > 0 && o.height > 0, `${what}: size of ${kind} ${JSON.stringify(o).slice(0, 160)}`);
      assert.ok(x >= 0 && x + o.width <= W, `${what}: ${kind} outside the page width (${x} + ${o.width})`);
      assert.ok(top[o.row] + oy + o.height <= page, `${what}: ${kind} at row ${o.row} crosses the end of its page (${top[o.row]} + ${oy} + ${o.height} > ${page})`);
      boxes.push({ page: pageOf[o.row], x, y: top[o.row] + oy, x2: x + o.width, y2: top[o.row] + oy + o.height, kind, id: o.fileId || o.title });
    };
    (L.images || []).forEach(im => { assert.ok(im.fileId, `${what}: image without a file`); box(im, 'image'); });
    (L.charts || []).forEach(ch => box(ch, 'chart'));
    const hit = (a, b) => a.page === b.page && a.x < b.x2 - 0.5 && b.x < a.x2 - 0.5 && a.y < b.y2 - 0.5 && b.y < a.y2 - 0.5;
    for (let i = 0; i < boxes.length; i++) {
      for (let j = i + 1; j < boxes.length; j++) assert.ok(!hit(boxes[i], boxes[j]), `${what}: ${boxes[i].kind} ${boxes[i].id} over ${boxes[j].kind} ${boxes[j].id}`);
      texts.forEach(t => assert.ok(!hit(boxes[i], t), `${what}: ${boxes[i].kind} ${boxes[i].id} over the cell ${t.key} «${dynamic[t.key] !== undefined ? dynamic[t.key] : (g.cells[t.key] || {}).v}»`));
    }
    // Charts read cells of the grid.
    const last = String.fromCharCode(64 + cols), range = new RegExp('^[A-' + last + ']\\d+:[A-' + last + ']\\d+$');
    (L.charts || []).forEach(ch => (ch.ranges || []).forEach(a => assert.ok(range.test(a), `${what}: chart range ${a}`)));
  }

  return { check, badText };
}

module.exports = { makeCheck };
