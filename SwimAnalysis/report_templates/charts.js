// Inline SVG charts for the report templates. Thin marks, light grid, one y axis, legend for two series or more, direct labels.
'use strict';

const C = {
  ink: '#0B111A', navy: '#14243A', text: '#1F2D3D', dim: '#5B6B7B', faint: '#8A97A6', grid: '#E6EAEF', axis: '#C9D2DC',
  race: '#2E6BD6', pb: '#8A94A3', ref: '#3F4752', gain: '#1C55B8', gainMark: '#2E6BD6', loss: '#A24A07', lossMark: '#E57B22',
  p1: '#123B8A', p2: '#5A8EDC', p3: '#0E8A7E', p4: '#3F4752', first25: '#123B8A', second25: '#5A8EDC', surface: '#FFFFFF'
};

const esc = s => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

function niceTicks(min, max, count) {
  if (!(max > min)) { max = min + 1; }
  const span = max - min;
  const raw = span / Math.max(1, count);
  const mag = Math.pow(10, Math.floor(Math.log10(raw)));
  const step = [1, 2, 2.5, 5, 10].map(m => m * mag).find(s => s >= raw) || 10 * mag;
  const lo = Math.floor(min / step) * step, hi = Math.ceil(max / step) * step;
  const ticks = [];
  for (let v = lo; v <= hi + step * 1e-6; v += step) ticks.push(+v.toFixed(10));
  return { lo, hi, step, ticks };
}

function marker(shape, x, y, color, r) {
  r = r || 4;
  if (shape === 'square') return `<rect x="${(x - r).toFixed(1)}" y="${(y - r).toFixed(1)}" width="${2 * r}" height="${2 * r}" rx="1.5" fill="${color}" stroke="${C.surface}" stroke-width="1.5"/>`;
  if (shape === 'diamond') return `<path d="M${x.toFixed(1)} ${(y - r - 1).toFixed(1)} L${(x + r + 1).toFixed(1)} ${y.toFixed(1)} L${x.toFixed(1)} ${(y + r + 1).toFixed(1)} L${(x - r - 1).toFixed(1)} ${y.toFixed(1)} Z" fill="${color}" stroke="${C.surface}" stroke-width="1.5"/>`;
  if (shape === 'ring') return `<circle cx="${x.toFixed(1)}" cy="${y.toFixed(1)}" r="${r - 0.5}" fill="${C.surface}" stroke="${color}" stroke-width="2"/>`;
  return `<circle cx="${x.toFixed(1)}" cy="${y.toFixed(1)}" r="${r}" fill="${color}" stroke="${C.surface}" stroke-width="1.5"/>`;
}

function dashOf(d) { return d === 'dash' ? '6 4' : d === 'dot' ? '1.5 3.5' : ''; }

function legend(items, x, y) {
  let out = '', cx = x;
  items.forEach(it => {
    const w = 22;
    if (it.kind === 'bar') out += `<rect x="${cx}" y="${y - 6}" width="12" height="9" rx="2" fill="${it.color}"/>`;
    else {
      out += `<line x1="${cx}" y1="${y - 1.5}" x2="${cx + w - 4}" y2="${y - 1.5}" stroke="${it.color}" stroke-width="2" stroke-dasharray="${dashOf(it.dash)}" stroke-linecap="round"/>`;
      if (it.marker && it.marker !== 'none') out += marker(it.marker, cx + (w - 4) / 2, y - 1.5, it.color, 3.5);
    }
    const tx = cx + (it.kind === 'bar' ? 17 : w + 2);
    out += `<text x="${tx}" y="${y + 2}" class="lg">${esc(it.name)}</text>`;
    cx = tx + it.name.length * 5.6 + 16;
  });
  return out;
}

/**
 * Line chart over categories. o: { w, h, cats, series: [{ name, color, dash, values, marker, width, label }], fmt, yTitle, yMin, yMax, ticks,
 * every (x label step), endLabels (true), band: { from, to, color, label }, refLine: { value, label }, legendOff }
 */
function lineChart(o) {
  const w = o.w, h = o.h, fmt = o.fmt || (v => v.toFixed(2));
  const showLegend = o.series.length > 1 && !o.legendOff;
  const m = { l: o.ml || 44, r: o.endLabels === false ? 14 : (o.mr || 58), t: showLegend ? 26 : 12, b: o.xTitle ? 34 : 22 };
  const all = [];
  o.series.forEach(s => s.values.forEach(v => { if (v !== null && v !== undefined && isFinite(v)) all.push(v); }));
  if (o.band) { all.push(o.band.from, o.band.to); }
  if (o.refLine) all.push(o.refLine.value);
  let min = o.yMin !== undefined ? o.yMin : Math.min.apply(null, all), max = o.yMax !== undefined ? o.yMax : Math.max.apply(null, all);
  const pad = (max - min) * 0.08 || 0.1;
  if (o.yMin === undefined) min -= pad;
  if (o.yMax === undefined) max += pad;
  const nt = niceTicks(min, max, o.ticks || 4);
  const lo = o.yMin !== undefined ? o.yMin : nt.lo, hi = o.yMax !== undefined ? o.yMax : nt.hi;
  const iw = w - m.l - m.r, ih = h - m.t - m.b;
  const n = o.cats.length;
  const X = i => m.l + (n === 1 ? iw / 2 : iw * i / (n - 1));
  const Y = v => m.t + ih * (1 - (v - lo) / (hi - lo));
  let g = '';
  nt.ticks.filter(t => t >= lo - 1e-9 && t <= hi + 1e-9).forEach(t => {
    g += `<line x1="${m.l}" y1="${Y(t).toFixed(1)}" x2="${w - m.r + 4}" y2="${Y(t).toFixed(1)}" stroke="${C.grid}" stroke-width="1"/>`;
    g += `<text x="${m.l - 6}" y="${(Y(t) + 3).toFixed(1)}" class="ax" text-anchor="end">${esc((o.tickFmt || fmt)(t))}</text>`;
  });
  if (o.band) {
    const y1 = Y(o.band.to), y2 = Y(o.band.from);
    g += `<rect x="${m.l}" y="${y1.toFixed(1)}" width="${iw}" height="${(y2 - y1).toFixed(1)}" fill="${o.band.color || '#EEF3FC'}" opacity="0.9"/>`;
    if (o.band.label) g += `<text x="${m.l + 6}" y="${(y1 + 10).toFixed(1)}" class="an">${esc(o.band.label)}</text>`;
  }
  if (o.refLine) {
    const y = Y(o.refLine.value);
    g += `<line x1="${m.l}" y1="${y.toFixed(1)}" x2="${w - m.r + 4}" y2="${y.toFixed(1)}" stroke="${o.refLine.color || C.dim}" stroke-width="1.2" stroke-dasharray="4 3"/>`;
    if (o.refLine.label) g += `<text x="${m.l + 5}" y="${(y - 4).toFixed(1)}" class="an">${esc(o.refLine.label)}</text>`;
  }
  const every = o.every || Math.max(1, Math.ceil(n / 16));
  o.cats.forEach((c, i) => {
    if (i % every !== 0 && i !== n - 1) return;
    if (i !== n - 1 && every > 1 && n - 1 - i < every) return;
    g += `<text x="${X(i).toFixed(1)}" y="${h - m.b + 14}" class="ax" text-anchor="middle">${esc(c)}</text>`;
  });
  g += `<line x1="${m.l}" y1="${(m.t + ih).toFixed(1)}" x2="${w - m.r + 4}" y2="${(m.t + ih).toFixed(1)}" stroke="${C.axis}" stroke-width="1"/>`;
  if (o.xTitle) g += `<text x="${(m.l + iw / 2).toFixed(1)}" y="${h - 4}" class="ax" text-anchor="middle">${esc(o.xTitle)}</text>`;
  if (o.yTitle) g += `<text x="${m.l}" y="${m.t - (showLegend ? 16 : 3)}" class="ax">${esc(o.yTitle)}</text>`;
  const ends = [];
  o.series.forEach((s, k) => {
    let d = '', pen = false;
    s.values.forEach((v, i) => {
      if (v === null || v === undefined || !isFinite(v)) { pen = false; return; }
      d += (pen ? 'L' : 'M') + X(i).toFixed(1) + ' ' + Y(v).toFixed(1) + ' ';
      pen = true;
    });
    g += `<path d="${d}" fill="none" stroke="${s.color}" stroke-width="${s.width || 2}" stroke-dasharray="${dashOf(s.dash)}" stroke-linejoin="round" stroke-linecap="round"/>`;
    if (s.marker !== 'none') {
      const r = n > 20 ? 2.6 : (s.r || 4);
      s.values.forEach((v, i) => { if (v !== null && v !== undefined && isFinite(v)) g += marker(s.marker || 'circle', X(i), Y(v), s.color, r); });
    }
    if (s.pointLabels) {
      s.values.forEach((v, i) => {
        if (v === null || v === undefined || !isFinite(v)) return;
        if (s.pointLabels === 'ends' && i !== 0 && i !== n - 1) return;
        const above = s.labelBelow ? false : true;
        g += `<text x="${X(i).toFixed(1)}" y="${(Y(v) + (above ? -8 : 14)).toFixed(1)}" class="pl" text-anchor="middle">${esc(fmt(v))}</text>`;
      });
    }
    let last = -1;
    s.values.forEach((v, i) => { if (v !== null && v !== undefined && isFinite(v)) last = i; });
    if (last >= 0 && o.endLabels !== false) ends.push({ y: Y(s.values[last]), x: X(last), text: (s.label || s.name), val: fmt(s.values[last]), color: s.color });
  });
  // End labels, nudged apart.
  ends.sort((a, b) => a.y - b.y);
  for (let i = 1; i < ends.length; i++) if (ends[i].y - ends[i - 1].y < 11) ends[i].y = ends[i - 1].y + 11;
  ends.forEach(e => {
    g += `<text x="${(e.x + 8).toFixed(1)}" y="${(e.y + 3).toFixed(1)}" class="el"><tspan fill="${C.text}" font-weight="600">${esc(e.val)}</tspan></text>`;
  });
  if (showLegend) {
    const items = o.series.map(s => ({ name: s.name, color: s.color, dash: s.dash, marker: s.marker || 'circle' }));
    const width = items.reduce((a, it) => a + 24 + it.name.length * 5.1 + 16, 0) - 16;
    g += legend(items, Math.max(m.l + 150, w - m.r - width), 10);
  }
  return `<svg viewBox="0 0 ${w} ${h}" width="${w}" height="${h}" class="chart">${g}</svg>`;
}

/** Vertical bars from zero. o: { w, h, cats, values, color, fmt, labels: 'all'|'none', yTitle, every, colors (per bar) } */
function barChart(o) {
  const w = o.w, h = o.h, fmt = o.fmt || (v => v.toFixed(2));
  const m = { l: o.ml || 40, r: 10, t: 16, b: 22 };
  const max = Math.max.apply(null, o.values.filter(v => v !== null));
  const nt = niceTicks(0, max * 1.08, o.ticks || 4);
  const iw = w - m.l - m.r, ih = h - m.t - m.b, n = o.values.length;
  const bw = Math.min(34, iw / n * 0.62), Y = v => m.t + ih * (1 - v / nt.hi);
  let g = '';
  nt.ticks.forEach(t => {
    g += `<line x1="${m.l}" y1="${Y(t).toFixed(1)}" x2="${w - m.r}" y2="${Y(t).toFixed(1)}" stroke="${C.grid}"/>`;
    g += `<text x="${m.l - 6}" y="${(Y(t) + 3).toFixed(1)}" class="ax" text-anchor="end">${esc((o.tickFmt || fmt)(t))}</text>`;
  });
  const every = o.every || 1;
  o.values.forEach((v, i) => {
    const cx = m.l + iw * (i + 0.5) / n;
    if (v !== null) {
      const y = Y(v), x = cx - bw / 2, r = Math.min(4, bw / 2), b = m.t + ih;
      const col = (o.colors && o.colors[i]) || o.color || C.race;
      g += `<path d="M${x.toFixed(1)} ${b} L${x.toFixed(1)} ${(y + r).toFixed(1)} Q${x.toFixed(1)} ${y.toFixed(1)} ${(x + r).toFixed(1)} ${y.toFixed(1)} L${(x + bw - r).toFixed(1)} ${y.toFixed(1)} Q${(x + bw).toFixed(1)} ${y.toFixed(1)} ${(x + bw).toFixed(1)} ${(y + r).toFixed(1)} L${(x + bw).toFixed(1)} ${b} Z" fill="${col}"/>`;
      if (o.labels !== 'none') g += `<text x="${cx.toFixed(1)}" y="${(y - 4).toFixed(1)}" class="pl" text-anchor="middle">${esc(fmt(v))}</text>`;
    }
    if (i % every === 0 || i === n - 1) g += `<text x="${cx.toFixed(1)}" y="${h - 7}" class="ax" text-anchor="middle">${esc(o.cats[i])}</text>`;
  });
  g += `<line x1="${m.l}" y1="${m.t + ih}" x2="${w - m.r}" y2="${m.t + ih}" stroke="${C.axis}"/>`;
  if (o.yTitle) g += `<text x="${m.l}" y="${m.t - 5}" class="ax">${esc(o.yTitle)}</text>`;
  return `<svg viewBox="0 0 ${w} ${h}" width="${w}" height="${h}" class="chart">${g}</svg>`;
}

/**
 * Vertical bars around zero: below zero (faster, blue), above (slower, orange). o: { w, h, cats, values, fmt, every, yTitle, labels }
 */
function vDiverging(o) {
  const w = o.w, h = o.h, fmt = o.fmt || (v => (v > 0 ? '+' : v < 0 ? '−' : '±') + Math.abs(v).toFixed(2));
  const m = { l: o.ml || 40, r: 10, t: 16, b: 22 };
  const vals = o.values.filter(v => v !== null);
  const amp = Math.max(0.1, Math.max.apply(null, vals.map(Math.abs))) * 1.15;
  const nt = niceTicks(-amp, amp, 4);
  const lo = Math.min(nt.lo, -nt.step), hi = Math.max(nt.hi, nt.step);
  const iw = w - m.l - m.r, ih = h - m.t - m.b, n = o.values.length;
  const bw = Math.min(26, iw / n * 0.62), Y = v => m.t + ih * (1 - (v - lo) / (hi - lo));
  let g = '';
  nt.ticks.forEach(t => {
    g += `<line x1="${m.l}" y1="${Y(t).toFixed(1)}" x2="${w - m.r}" y2="${Y(t).toFixed(1)}" stroke="${t === 0 ? C.axis : C.grid}"/>`;
    g += `<text x="${m.l - 6}" y="${(Y(t) + 3).toFixed(1)}" class="ax" text-anchor="end">${esc(t === 0 ? '0' : fmt(t))}</text>`;
  });
  const every = o.every || 1;
  o.values.forEach((v, i) => {
    const cx = m.l + iw * (i + 0.5) / n;
    if (v !== null) {
      const y0 = Y(0), y1 = Y(v), top = Math.min(y0, y1), hh = Math.max(1, Math.abs(y1 - y0));
      const col = v < 0 ? C.gainMark : C.lossMark;
      g += `<rect x="${(cx - bw / 2).toFixed(1)}" y="${top.toFixed(1)}" width="${bw.toFixed(1)}" height="${hh.toFixed(1)}" rx="${Math.min(3, bw / 3)}" fill="${col}"/>`;
      if (o.labels) g += `<text x="${cx.toFixed(1)}" y="${(v < 0 ? y1 + 11 : y1 - 4).toFixed(1)}" class="pl" text-anchor="middle">${esc(fmt(v))}</text>`;
    }
    if ((i % every === 0 && !(i !== n - 1 && every > 1 && n - 1 - i < every)) || i === n - 1) g += `<text x="${cx.toFixed(1)}" y="${h - 7}" class="ax" text-anchor="middle">${esc(o.cats[i])}</text>`;
  });
  if (o.yTitle) g += `<text x="${m.l}" y="${m.t - 5}" class="ax">${esc(o.yTitle)}</text>`;
  return `<svg viewBox="0 0 ${w} ${h}" width="${w}" height="${h}" class="chart">${g}</svg>`;
}

/** Horizontal diverging bars (gains left, losses right). o: { w, items: [{ label, value, note }], fmt, rowH, title } */
function hDiverging(o) {
  const rowH = o.rowH || 22, w = o.w, labelW = o.labelW || 150, noteW = o.noteW || 0;
  const h = o.items.length * rowH + 22;
  const amp = Math.max(o.minAmp || 0.25, Math.max.apply(null, o.items.map(it => Math.abs(it.value || 0)))) * 1.25;
  const x0 = labelW + (w - labelW - noteW) / 2, half = (w - labelW - noteW) / 2 - 8;
  const X = v => x0 + half * v / amp;
  const fmt = o.fmt || (v => (v > 0.004 ? '+' : v < -0.004 ? '−' : '±') + Math.abs(v).toFixed(2));
  let g = `<text x="${(x0 - 8).toFixed(1)}" y="11" class="ax" text-anchor="end">← ${esc(o.leftLabel || 'πιο γρήγορα')}</text>` +
    `<text x="${(x0 + 8).toFixed(1)}" y="11" class="ax">${esc(o.rightLabel || 'πιο αργά')} →</text>`;
  o.items.forEach((it, i) => {
    const y = 20 + i * rowH, cy = y + rowH / 2;
    if (i % 2 === 0) g += `<rect x="0" y="${y}" width="${w}" height="${rowH}" fill="#F7F9FB"/>`;
    g += `<text x="6" y="${cy + 3.5}" class="tl">${esc(it.label)}</text>`;
    const v = it.value;
    if (v !== null && v !== undefined) {
      const a = X(Math.min(0, v)), b = X(Math.max(0, v));
      const col = v < 0 ? C.gainMark : C.lossMark;
      g += `<rect x="${a.toFixed(1)}" y="${cy - 6}" width="${Math.max(1.5, b - a).toFixed(1)}" height="12" rx="3" fill="${col}"/>`;
      const tx = v < 0 ? a - 5 : b + 5;
      g += `<text x="${tx.toFixed(1)}" y="${cy + 3.5}" class="pv" text-anchor="${v < 0 ? 'end' : 'start'}" fill="${v < 0 ? C.gain : C.loss}">${esc(fmt(v))}</text>`;
    }
    if (it.note && noteW) g += `<text x="${w - noteW + 8}" y="${cy + 3.5}" class="nt">${esc(it.note)}</text>`;
  });
  g += `<line x1="${x0.toFixed(1)}" y1="16" x2="${x0.toFixed(1)}" y2="${h - 2}" stroke="${C.axis}"/>`;
  return `<svg viewBox="0 0 ${w} ${h}" width="${w}" height="${h}" class="chart">${g}</svg>`;
}

/** Composition bars: o: { w, rows: [{ label, parts: [{ key, name, value, color }], total }], fmtT } */
function stacked(o) {
  const w = o.w, rowH = 30, labelW = 96, totalW = 64;
  const max = Math.max.apply(null, o.rows.map(r => r.parts.reduce((a, p) => a + p.value, 0)));
  const iw = w - labelW - totalW;
  let g = '';
  o.rows.forEach((r, i) => {
    const y = i * (rowH + 10);
    g += `<text x="0" y="${y + rowH / 2 + 4}" class="tl"${i === 0 ? ' font-weight="700"' : ''}>${esc(r.label)}</text>`;
    let x = labelW;
    r.parts.forEach(p => {
      const pw = iw * p.value / max;
      g += `<rect x="${x.toFixed(1)}" y="${y}" width="${Math.max(0, pw - 2).toFixed(1)}" height="${rowH}" rx="3" fill="${p.color}"/>`;
      const ink = p.ink ? ` fill="${p.ink}"` : '';
      if (pw > 84) g += `<text x="${(x + 7).toFixed(1)}" y="${y + 13}" class="sb"${ink}>${esc(p.name)}</text><text x="${(x + 7).toFixed(1)}" y="${y + 25}" class="sv"${ink}>${esc(o.fmtT(p.value))} · ${(100 * p.value / r.total).toFixed(1)}%</text>`;
      else if (pw > 40) g += `<text x="${(x + pw / 2 - 1).toFixed(1)}" y="${y + 19}" class="sv" text-anchor="middle"${ink}>${esc(o.fmtT(p.value))}</text>`;
      else if (pw > 22) g += `<text x="${(x + pw / 2 - 1).toFixed(1)}" y="${y + 19}" class="sv" text-anchor="middle"${ink}>${(100 * p.value / r.total).toFixed(0)}%</text>`;
      x += pw;
    });
    g += `<text x="${w}" y="${y + rowH / 2 + 4}" class="tv" text-anchor="end">${esc(o.fmtT(r.total))}</text>`;
  });
  const h = o.rows.length * (rowH + 10) - 10;
  return `<svg viewBox="0 0 ${w} ${h}" width="${w}" height="${h}" class="chart">${g}</svg>`;
}

/** The start along the first 15 m: block, flight, under water, swimming to 15. o: { w, s (start), pb, ref, fmt } */
function startStrip(o) {
  const w = o.w, h = 150, l = 18, r = 18, X = d => l + (w - l - r) * d / 15;
  const s = o.s;
  const zones = [
    { from: 0, to: s.entryDist, name: 'Πτήση', color: '#DCE7F7' },
    { from: s.entryDist, to: s.bd, name: 'Υποβρύχια φάση', color: '#C5D8F2' },
    { from: s.bd, to: 15, name: 'Κολύμβηση', color: '#E8EEF5' }
  ];
  let g = '';
  zones.forEach(z => {
    g += `<rect x="${X(z.from).toFixed(1)}" y="54" width="${(X(z.to) - X(z.from) - 2).toFixed(1)}" height="30" rx="4" fill="${z.color}"/>`;
    g += `<text x="${((X(z.from) + X(z.to)) / 2).toFixed(1)}" y="73" class="zn" text-anchor="middle">${esc(z.name)}</text>`;
  });
  // Distance axis.
  for (let d = 0; d <= 15; d += 5) {
    g += `<line x1="${X(d).toFixed(1)}" y1="88" x2="${X(d).toFixed(1)}" y2="93" stroke="${C.axis}"/>`;
    g += `<text x="${X(d).toFixed(1)}" y="104" class="ax" text-anchor="middle">${d} μ.</text>`;
  }
  g += `<line x1="${X(0)}" y1="88" x2="${X(15)}" y2="88" stroke="${C.axis}"/>`;
  const ev = [
    { d: 0, t: s.block, top: 'Βατήρας', sub: o.fmt(s.block) + ' s' },
    { d: s.entryDist, t: s.entryT, top: 'Είσοδος', sub: o.fmt(s.entryT) + ' s · ' + s.entryDist.toFixed(2) + ' μ.' },
    { d: s.bd, t: s.bt, top: 'Έξοδος', sub: o.fmt(s.bt) + ' s · ' + s.bd.toFixed(2) + ' μ.' },
    { d: 15, t: s.t15, top: '15 μ.', sub: o.fmt(s.t15) + ' s' }
  ];
  ev.forEach((e, i) => {
    const x = X(e.d), anchor = i === 0 ? 'start' : i === ev.length - 1 ? 'end' : 'middle';
    g += `<line x1="${x.toFixed(1)}" y1="40" x2="${x.toFixed(1)}" y2="88" stroke="${C.navy}" stroke-width="1.5"/>`;
    g += `<circle cx="${x.toFixed(1)}" cy="40" r="4.5" fill="${C.navy}"/>`;
    g += `<text x="${x.toFixed(1)}" y="16" class="et" text-anchor="${anchor}">${esc(e.top)}</text>`;
    g += `<text x="${x.toFixed(1)}" y="30" class="es" text-anchor="${anchor}">${esc(e.sub)}</text>`;
  });
  // Speeds under the zones.
  const sp = [
    { a: 0, b: s.entryDist, v: s.sigEntryVel, txt: 'σήμα → είσοδος' },
    { a: s.entryDist, b: s.bd, v: s.entryBoVel, txt: 'είσοδος → έξοδος' },
    { a: s.bd, b: 15, v: s.bo15Vel, txt: 'έξοδος → 15 μ.' }
  ];
  sp.forEach(z => {
    const cx = (X(z.a) + X(z.b)) / 2;
    g += `<text x="${cx.toFixed(1)}" y="124" class="sv2" text-anchor="middle">${z.v.toFixed(2)} m/s</text>`;
    g += `<text x="${cx.toFixed(1)}" y="138" class="ax" text-anchor="middle">${esc(z.txt)}</text>`;
  });
  g += `<text x="${((X(s.entryDist) + X(s.bd)) / 2).toFixed(1)}" y="50" class="ax" text-anchor="middle">${s.kicks} δελφινισμοί</text>`;
  return `<svg viewBox="0 0 ${w} ${h}" width="${w}" height="${h}" class="chart">${g}</svg>`;
}

/** One turn from −5 to +15: approach, turn, under water, swimming. o: { w, t (turn), fmt } */
function turnStrip(o) {
  const w = o.w, h = 128, l = 18, r = 18, X = d => l + (w - l - r) * (d + 5) / 20;
  const t = o.t;
  const zones = [
    { from: -5, to: 0, name: 'Προσέγγιση', color: '#E8EEF5' },
    { from: 0, to: t.bd, name: 'Στροφή · υποβρύχια φάση', color: '#C5D8F2' },
    { from: t.bd, to: 15, name: 'Κολύμβηση', color: '#E8EEF5' }
  ];
  let g = '';
  zones.forEach(z => {
    g += `<rect x="${X(z.from).toFixed(1)}" y="50" width="${(X(z.to) - X(z.from) - 2).toFixed(1)}" height="28" rx="4" fill="${z.color}"/>`;
    g += `<text x="${((X(z.from) + X(z.to)) / 2).toFixed(1)}" y="68" class="zn" text-anchor="middle">${esc(z.name)}</text>`;
  });
  for (let d = -5; d <= 15; d += 5) {
    g += `<line x1="${X(d).toFixed(1)}" y1="82" x2="${X(d).toFixed(1)}" y2="87" stroke="${C.axis}"/>`;
    g += `<text x="${X(d).toFixed(1)}" y="98" class="ax" text-anchor="middle">${d === 0 ? 'τοίχος' : (d > 0 ? '+' : '−') + Math.abs(d) + ' μ.'}</text>`;
  }
  g += `<line x1="${X(-5)}" y1="82" x2="${X(15)}" y2="82" stroke="${C.axis}"/>`;
  const ev = [
    { d: -5, top: '−5 μ.', sub: '' },
    { d: 0, top: 'Τοίχος', sub: o.fmt(t.in5) + ' s από −5' },
    { d: t.bd, top: 'Έξοδος', sub: t.bd.toFixed(1) + ' μ. · ' + t.kicks + ' δελφ.' },
    { d: 15, top: '+15 μ.', sub: o.fmt(t.out15) + ' s από τοίχο' }
  ];
  ev.forEach((e, i) => {
    const x = X(e.d), anchor = i === 0 ? 'start' : i === ev.length - 1 ? 'end' : 'middle';
    g += `<line x1="${x.toFixed(1)}" y1="36" x2="${x.toFixed(1)}" y2="82" stroke="${C.navy}" stroke-width="1.5"/>`;
    g += `<circle cx="${x.toFixed(1)}" cy="36" r="4.5" fill="${C.navy}"/>`;
    g += `<text x="${x.toFixed(1)}" y="13" class="et" text-anchor="${anchor}">${esc(e.top)}</text>`;
    if (e.sub) g += `<text x="${x.toFixed(1)}" y="27" class="es" text-anchor="${anchor}">${esc(e.sub)}</text>`;
  });
  g += `<text x="${((X(0) + X(t.bd)) / 2).toFixed(1)}" y="116" class="ax" text-anchor="middle">στροφή ${o.fmt(t.turnTime)} s · υποβρύχια ${o.fmt(t.uw)} s · ${t.vUw.toFixed(2)} m/s</text>`;
  g += `<text x="${((X(t.bd) + X(15)) / 2).toFixed(1)}" y="116" class="ax" text-anchor="middle">${t.vBo15.toFixed(2)} m/s</text>`;
  return `<svg viewBox="0 0 ${w} ${h}" width="${w}" height="${h}" class="chart">${g}</svg>`;
}

module.exports = { C, lineChart, barChart, vDiverging, hDiverging, stacked, startStrip, turnStrip, esc };
