/**
 * Kinovea Swim · the race report, 2nd version (Apps Script 2.22.0; 2.23.0: «Πού κρύβεται ο χρόνος» in the place of the target time;
 * 2.24.0: how far behind the reference each phase is, in %, and the whole time it loses, in the place of the gains of half the way;
 * 2.24.1: the last metres against the PB and the reference only at the same distance as the race's; 2.25.0: what the comparison
 * means in the place of the recommendations, and the conclusion of the whole race; 2.26.0: the slides of the race, the
 * pages at the end of the video (16:9) and the posts for the social media of the federation (4:5); 2.26.1: the start in the chart of
 * the post of the speed).
 *
 * The pages of the templates approved by the analysts on 24.11.2026 (SwimAnalysis/report_templates, 2nd version), made from the real
 * analysis of a race: the race, the personal best (a stored analysis of the athlete) and the reference (elite times). One self-contained
 * HTML page per report (inline SVG charts, A4 pages): Kinovea prints it to PDF with Edge on the analysis computer and sends the PDF back
 * ('pdfStore'), so that the PDF is exactly the template. Everything the pages show comes from swimAnalyze_ and the values Kinovea sends,
 * the same numbers as the sheet of the report and the graphics of the video. A value that is missing (not marked, no PB, a reference
 * with times only) is shown as «–» and compared with nothing.
 *
 * All in one namespace (SwimV2): the files of an Apps Script project share their names.
 */
var SwimV2 = (function () {
  'use strict';

  const STYLE_NAME = { fs: 'Ελεύθερο', bk: 'Ύπτιο', br: 'Πρόσθιο', fly: 'Πεταλούδα', im: 'Μικτή' };
  const IM_ORDER = ['fly', 'bk', 'br', 'fs'];
  const MEDLEY_RELAY = ['bk', 'br', 'fly', 'fs'];
  const PPC = s => (s === 'fs' || s === 'bk') ? 2 : 1;
  const YD = 0.9144;

  // ---------------------------------------------------------------------------------------------------------------------------
  // Charts (inline SVG)
  // ---------------------------------------------------------------------------------------------------------------------------
  // Inline SVG charts for the report templates. Thin marks, light grid, one y axis, legend for two series or more, direct labels.

  const C = {
    ink: '#0B111A', navy: '#14243A', text: '#1F2D3D', dim: '#5B6B7B', faint: '#8A97A6', grid: '#E6EAEF', axis: '#C9D2DC',
    race: '#2E6BD6', pb: '#8A94A3', ref: '#3F4752', gain: '#1C55B8', gainMark: '#2E6BD6', loss: '#A24A07', lossMark: '#E57B22',
    p1: '#123B8A', p2: '#5A8EDC', p3: '#0E8A7E', p4: '#3F4752', first25: '#123B8A', second25: '#5A8EDC', surface: '#FFFFFF'
  };

  /** A value to draw (not null, not NaN). */
  const isNum = v => typeof v === 'number' && isFinite(v);
  /** A value of a chart with n decimals, «–» when there is none. */
  const fx = (v, n) => isNum(v) ? v.toFixed(n) : '–';
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
    o = Object.assign({}, o, { series: o.series.filter(s0 => s0.values.some(isNum)) });
    if (!o.series.length) return '';
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
    (o.vlines || []).forEach(vl => {
      const x = (X(vl.at - 1) + X(vl.at)) / 2;
      g += `<line x1="${x.toFixed(1)}" y1="${m.t}" x2="${x.toFixed(1)}" y2="${(m.t + ih).toFixed(1)}" stroke="${C.navy}" stroke-width="1" stroke-dasharray="3 3" opacity="0.55"/>`;
      if (vl.label) g += `<text x="${(x + 4).toFixed(1)}" y="${m.t + 11}" class="an">${esc(vl.label)}</text>`;
    });
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
    if (!o.values.some(isNum)) return '';
    const max = Math.max.apply(null, o.values.filter(isNum));
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
      if (isNum(v)) {
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
    const w = o.w, h = o.h, fmt = o.fmt || (v => { const r = Math.round(v * 100) / 100; return (r > 0 ? '+' : r < 0 ? '−' : '±') + Math.abs(r).toFixed(2); });
    const m = { l: o.ml || 40, r: 10, t: 16, b: 22 };
    const vals = o.values.filter(isNum);
    if (!vals.length) return '';
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
      if (isNum(v)) {
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
    if (!o.items.length) return '';
    const amp = Math.max(o.minAmp || 0.25, Math.max.apply(null, o.items.map(it => isNum(it.value) ? Math.abs(it.value) : 0))) * 1.25;
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
      if (isNum(v)) {
        const a = X(Math.min(0, v)), b = X(Math.max(0, v));
        const col = v < 0 ? C.gainMark : C.lossMark;
        g += `<rect x="${a.toFixed(1)}" y="${cy - 6}" width="${Math.max(1.5, b - a).toFixed(1)}" height="12" rx="3" fill="${col}"/>`;
        const tx = v < 0 ? a - 5 : b + 5;
        g += `<text x="${tx.toFixed(1)}" y="${cy + 3.5}" class="pv" text-anchor="${v < 0 ? 'end' : 'start'}" fill="${v < 0 ? C.gain : C.loss}">${esc(fmt(v))}</text>`;
      } else {
        g += `<text x="${(x0 + 8).toFixed(1)}" y="${cy + 3.5}" class="nt">–</text>`;
      }
      if (it.note && noteW) g += `<text x="${w - noteW + 8}" y="${cy + 3.5}" class="nt">${esc(it.note)}</text>`;
    });
    g += `<line x1="${x0.toFixed(1)}" y1="16" x2="${x0.toFixed(1)}" y2="${h - 2}" stroke="${C.axis}"/>`;
    return `<svg viewBox="0 0 ${w} ${h}" width="${w}" height="${h}" class="chart">${g}</svg>`;
  }

  /** Composition bars: o: { w, rows: [{ label, parts: [{ key, name, value, color }], total }], fmtT } */
  function stacked(o) {
    o = Object.assign({}, o, { rows: o.rows.map(r => Object.assign({}, r, { parts: r.parts.filter(p => isNum(p.value) && p.value > 0) }))
      .filter(r => r.parts.length && isNum(r.total) && r.total > 0) });
    if (!o.rows.length) return '';
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
      { d: s.entryDist, t: s.entryT, top: s.entryHead ? 'Είσοδος κεφαλιού' : 'Είσοδος', sub: o.fmt(s.entryT) + ' s · ' + fx(s.entryDist, 2) + ' μ.' },
      { d: s.bd, t: s.bt, top: 'Έξοδος', sub: o.fmt(s.bt) + ' s · ' + fx(s.bd, 2) + ' μ.' },
      { d: 15, t: s.t15, top: '15 μ.', sub: o.fmt(s.t15) + ' s' }
    ];
    const close = 15 - s.bd < 2.6;
    ev.forEach((e, i) => {
      const x = X(e.d);
      let anchor = i === 0 ? 'start' : i === ev.length - 1 ? 'end' : 'middle', tx = x;
      if (close && i === 2) { anchor = 'end'; tx = x - 8; }
      g += `<line x1="${x.toFixed(1)}" y1="40" x2="${x.toFixed(1)}" y2="88" stroke="${C.navy}" stroke-width="1.5"/>`;
      g += `<circle cx="${x.toFixed(1)}" cy="40" r="4.5" fill="${C.navy}"/>`;
      g += `<text x="${tx.toFixed(1)}" y="16" class="et" text-anchor="${anchor}">${esc(e.top)}</text>`;
      g += `<text x="${tx.toFixed(1)}" y="30" class="es" text-anchor="${anchor}">${esc(e.sub)}</text>`;
    });
    // Speeds under the zones.
    const sp = [
      { a: 0, b: s.entryDist, v: s.sigEntryVel, txt: 'σήμα → είσοδος' },
      { a: s.entryDist, b: s.bd, v: s.entryBoVel, txt: 'είσοδος → έξοδος' },
      { a: s.bd, b: 15, v: s.bo15Vel, txt: s.bo15Slow ? 'έξοδος → 15 μ. ⚠' : 'έξοδος → 15 μ.' }
    ];
    sp.forEach(z => {
      const cx = (X(z.a) + X(z.b)) / 2;
      g += `<text x="${cx.toFixed(1)}" y="124" class="sv2" text-anchor="middle">${fx(z.v, 2)} m/s</text>`;
      g += `<text x="${cx.toFixed(1)}" y="138" class="ax" text-anchor="middle">${esc(z.txt)}</text>`;
    });
    if (isNum(s.kicks)) g += `<text x="${((X(s.entryDist) + X(s.bd)) / 2).toFixed(1)}" y="50" class="ax" text-anchor="middle">${s.kicks} δελφινισμοί</text>`;
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
      { d: t.bd, top: 'Έξοδος', sub: fx(t.bd, 1) + ' μ.' + (isNum(t.kicks) ? ' · ' + t.kicks + ' δελφ.' : typeof t.kicks === 'string' && t.kicks !== '–' ? ' · ' + t.kicks + ' δελφ.' : '') },
      { d: 15, top: '+15 μ.', sub: o.fmt(t.out15) + ' s από τοίχο' }
    ];
    ev.forEach((e, i) => {
      const x = X(e.d), anchor = i === 0 ? 'start' : i === ev.length - 1 ? 'end' : 'middle';
      g += `<line x1="${x.toFixed(1)}" y1="36" x2="${x.toFixed(1)}" y2="82" stroke="${C.navy}" stroke-width="1.5"/>`;
      g += `<circle cx="${x.toFixed(1)}" cy="36" r="4.5" fill="${C.navy}"/>`;
      g += `<text x="${x.toFixed(1)}" y="13" class="et" text-anchor="${anchor}">${esc(e.top)}</text>`;
      if (e.sub) g += `<text x="${x.toFixed(1)}" y="27" class="es" text-anchor="${anchor}">${esc(e.sub)}</text>`;
    });
    g += `<text x="${((X(0) + X(t.bd)) / 2).toFixed(1)}" y="116" class="ax" text-anchor="middle">στροφή ${o.fmt(t.turnTime)} s · υποβρύχια ${o.fmt(t.uw)} s${isNum(t.vUw) ? ' · ' + t.vUw.toFixed(2) + ' m/s' : ''}</text>`;
    if (isNum(t.vBo15)) g += `<text x="${((X(t.bd) + X(15)) / 2).toFixed(1)}" y="116" class="ax" text-anchor="middle">${t.vBo15.toFixed(2)} m/s${t.bo15Slow ? ' ⚠' : ''}</text>`;
    return `<svg viewBox="0 0 ${w} ${h}" width="${w}" height="${h}" class="chart">${g}</svg>`;
  }


  // ---- (2nd version) scorecard bullets, the race in 50s, ΔΑΚ components

  /** One bullet: the athlete (dot), the PB (grey tick), the reference (dark tick), on a scale of the three. */
  function bullet(o) {
    if (!isNum(o.a)) return '';
    const w = o.w, h = 22, vals = [o.a, o.pb, o.ref].filter(isNum);
    let lo = Math.min(...vals), hi = Math.max(...vals);
    const pad = Math.max((hi - lo) * 0.35, Math.abs(hi) * 0.01, 0.02);
    lo -= pad; hi += pad;
    const X = v => 8 + (w - 16) * (o.better === 'lower' ? (hi - v) / (hi - lo) : (v - lo) / (hi - lo));
    let g = `<rect x="4" y="${h / 2 - 4}" width="${w - 8}" height="8" rx="4" fill="#E8EEF5"/>`;
    if (isNum(o.ref)) g += `<rect x="${(X(o.ref) - 1.5).toFixed(1)}" y="${h / 2 - 9}" width="3" height="18" rx="1.5" fill="${C.ref}"/>`;
    if (isNum(o.pb)) g += `<rect x="${(X(o.pb) - 1.5).toFixed(1)}" y="${h / 2 - 7}" width="3" height="14" rx="1.5" fill="${C.pb}"/>`;
    g += `<circle cx="${X(o.a).toFixed(1)}" cy="${h / 2}" r="7" fill="${C.race}" stroke="#fff" stroke-width="2"/>`;
    return `<svg viewBox="0 0 ${w} ${h}" width="${w}" height="${h}" class="chart">${g}</svg>`;
  }

  /** The race in boxes of 50: time, ΔΑΚ, coloured by the ΔΑΚ against the mean of the race (blue better, orange worse). */
  function heatStrip(o) {
    const n = o.items.length, w = o.w;
    const perRow = n <= 10 ? n : n <= 16 ? 8 : n <= 30 ? 10 : 11;
    const rows = Math.ceil(n / perRow), gap = 6, bw = (w - gap * (perRow - 1)) / perRow, bh = n > 16 ? 42 : n > 10 ? 46 : 62;
    const known = o.items.filter(it => isNum(it.dak));
    const mean = known.length ? known.reduce((s0, it) => s0 + it.dak, 0) / known.length : 0;
    const amp = Math.max(0.25, Math.max(0, ...known.map(it => Math.abs(it.dak - mean))));
    let g = '';
    o.items.forEach((it, i) => {
      const r = Math.floor(i / perRow), c = i % perRow, x = c * (bw + gap), y = r * (bh + gap);
      const d = (it.dak - mean) / amp;
      const fill = o.neutral || !isNum(d) ? '#EEF2F7' : d < 0 ? mix('#FFFFFF', '#9DBDF0', Math.min(1, -d)) : mix('#FFFFFF', '#F6C08F', Math.min(1, d));
      g += `<rect x="${x.toFixed(1)}" y="${y}" width="${bw.toFixed(1)}" height="${bh}" rx="7" fill="${fill}" stroke="${it.mark ? C.navy : '#DCE3EB'}" stroke-width="${it.mark ? 1.6 : 1}"/>`;
      const sm = n > 10;
      const xs = n > 16;
      g += `<text x="${(x + bw / 2).toFixed(1)}" y="${y + (xs ? 11 : sm ? 12 : 15)}" class="hs${sm ? ' s' : ''}" text-anchor="middle">${esc(it.label)}</text>`;
      g += `<text x="${(x + bw / 2).toFixed(1)}" y="${y + (xs ? 25.5 : sm ? 27.5 : 36)}" class="hv${sm ? ' s' : ''}" text-anchor="middle">${esc(it.time)}</text>`;
      g += `<text x="${(x + bw / 2).toFixed(1)}" y="${y + (xs ? 37.5 : sm ? 40.5 : 53)}" class="hd${sm ? ' s' : ''}" text-anchor="middle">ΔΑΚ ${esc(it.dakText)}</text>`;
    });
    const h = rows * (bh + gap) - gap;
    return `<svg viewBox="0 0 ${w} ${h}" width="${w}" height="${h}" class="chart">${g}</svg>`;
  }

  function mix(a, b, t) {
    const p = h => [1, 3, 5].map(i => parseInt(h.slice(i, i + 2), 16));
    const A = p(a), B = p(b);
    return '#' + A.map((v, i) => Math.round(v + (B[i] - v) * t).toString(16).padStart(2, '0')).join('');
  }

  /** Vertical stacked bars from zero: o: { w, h, cats, parts: [{ name, color, values }], fmt, totalFmt, every, yTitle } */
  function vStacked(o) {
    const w = o.w, h = o.h, m = { l: 46, r: 12, t: 30, b: 26 };
    const n = o.cats.length;
    const totals = o.cats.map((_, i) => o.parts.every(p => isNum(p.values[i])) ? o.parts.reduce((s0, p) => s0 + p.values[i], 0) : NaN);
    if (!totals.some(isNum)) return '';
    const nt = niceTicks(0, Math.max(...totals.filter(isNum)) * 1.12, 4);
    const iw = w - m.l - m.r, ih = h - m.t - m.b, bw = Math.min(46, iw / n * 0.64), Y = v => m.t + ih * (1 - v / nt.hi);
    let g = '';
    nt.ticks.forEach(t => {
      g += `<line x1="${m.l}" y1="${Y(t).toFixed(1)}" x2="${w - m.r}" y2="${Y(t).toFixed(1)}" stroke="${C.grid}"/>`;
      g += `<text x="${m.l - 6}" y="${(Y(t) + 3.5).toFixed(1)}" class="ax" text-anchor="end">${esc(String(+t.toFixed(1)))}</text>`;
    });
    const every = o.every || 1;
    o.cats.forEach((c, i) => {
      const cx = m.l + iw * (i + 0.5) / n;
      let base = 0;
      if (isNum(totals[i])) o.parts.forEach((p, k) => {
        const v = p.values[i], y0 = Y(base), y1 = Y(base + v);
        g += `<rect x="${(cx - bw / 2).toFixed(1)}" y="${(y1 + (k ? 1 : 0)).toFixed(1)}" width="${bw.toFixed(1)}" height="${Math.max(1, y0 - y1 - (k ? 1 : 0)).toFixed(1)}" rx="${k === o.parts.length - 1 ? 4 : 0}" fill="${p.color}"/>`;
        base += v;
      });
      if (n <= 16 && isNum(totals[i])) g += `<text x="${cx.toFixed(1)}" y="${(Y(totals[i]) - 5).toFixed(1)}" class="pl" text-anchor="middle">${esc((o.totalFmt || (v => v.toFixed(2)))(totals[i]))}</text>`;
      if ((i % every === 0 && !(i !== n - 1 && every > 1 && n - 1 - i < every)) || i === n - 1) g += `<text x="${cx.toFixed(1)}" y="${h - 8}" class="ax" text-anchor="middle">${esc(c)}</text>`;
    });
    g += `<line x1="${m.l}" y1="${m.t + ih}" x2="${w - m.r}" y2="${m.t + ih}" stroke="${C.axis}"/>`;
    if (o.yTitle) g += `<text x="${m.l}" y="12" class="ax">${esc(o.yTitle)}</text>`;
    g += legend(o.parts.map(p => ({ name: p.name, color: p.color, kind: 'bar' })), Math.max(m.l + 160, w - m.r - o.parts.reduce((s, p) => s + 17 + p.name.length * 5.6 + 16, 0)), 14);
    return `<svg viewBox="0 0 ${w} ${h}" width="${w}" height="${h}" class="chart">${g}</svg>`;
  }

  const ch = { C, lineChart, barChart, vDiverging, hDiverging, stacked, startStrip, turnStrip, bullet, heatStrip, vStacked, mix, esc };

  // ---------------------------------------------------------------------------------------------------------------------------
  // Numbers, recommendations and messages
  // ---------------------------------------------------------------------------------------------------------------------------
  // What the numbers of a race show against the reference (or the PB), phase by phase, and what the comparison means (2.25.0: a
  // description for the coach, never what the athlete should do; until 2.24.1 an action and a target).

  const T = x => {
    if (x === null || x === undefined || !isFinite(x)) return '–';
    if (x < 60) return (Math.round(x * 100) / 100).toFixed(2);
    let m = Math.floor(x / 60), s = Math.round((x - m * 60) * 100) / 100;
    if (s >= 60) { m++; s = 0; }
    return m + ':' + (s < 10 ? '0' : '') + s.toFixed(2);
  };
  const F = (x, n) => x === null || x === undefined || !isFinite(x) ? '–' : x.toFixed(n === undefined ? 2 : n);
  const S = (x, n) => {
    if (x === null || x === undefined || !isFinite(x)) return '–';
    const k = Math.pow(10, n === undefined ? 2 : n), a = Math.round(Math.abs(x) * k) / k;
    return (a === 0 ? '±' : x < 0 ? '−' : '+') + a.toFixed(n === undefined ? 2 : n);
  };
  const vals = (l, f) => l.map(t => typeof f === 'function' ? f(t) : t[f]);
  /** The mean of the values there are (NaN when there is none), as the averages of the sheet. */
  const avg = (l, f) => { const v = vals(l, f).filter(ok); return v.length ? v.reduce((s0, y) => s0 + y, 0) / v.length : NaN; };
  /** The sum of all the values: NaN when one of them is missing (a part is never counted as the whole); 0 for none. */
  const sum = (l, f) => { const v = vals(l, f); return v.every(ok) ? v.reduce((s0, y) => s0 + y, 0) : NaN; };
  const U = ev => ev.unit === 'yd' ? ' yd' : ' μ.';
  const IN_STYLE = { fly: 'στην πεταλούδα', bk: 'στο ύπτιο', br: 'στο πρόσθιο', fs: 'στο ελεύθερο' };
  const kicksText = n => n <= 1 ? 'ένας δελφινισμός' : n + ' δελφινισμοί';
  const end = t => /[.!]$/.test(t.replace(/<[^>]+>/g, '')) ? t : t + '.';
  /** (2.23.0) A title in a sentence: lower case, the ΔΑΚ (an acronym) as it is. */
  const low = t => t.toLowerCase().replace(/δακ/g, 'ΔΑΚ');

  /**
   * What the race is compared with in the recommendations: the reference when it has the value of the phase, else the personal best;
   * null when neither has it (no recommendation from a comparison then). { m: the model, name: its name in the text, ref }.
   */
  function against(x, has) {
    if (has(x.ref)) return { m: x.ref, name: 'αναφορά', ref: true };
    if (has(x.pb)) return { m: x.pb, name: x.ev.relay ? 'ρεκόρ ομάδας' : 'ΑΡ', ref: false };
    return null;
  }

  /** «value (name value)», the part in brackets only when there is something to compare with. */
  const vs = (v, name, w, n, unit) => `${F(v, n)}${unit || ''}` + (ok(w) ? ` (${name} ${F(w, n)}${unit || ''})` : '');

  /** (2.25.0) The comparison in a sentence: «την αναφορά» / «το ΑΡ» (accusative), «της αναφοράς» / «του ΑΡ» (genitive). */
  const theC = c => c.ref ? 'την αναφορά' : 'το ' + c.name;
  const ofC = c => c.ref ? 'της αναφοράς' : 'του ' + c.name;
  const nearC = c => c.ref ? 'στην αναφορά' : 'στο ' + c.name;
  const TheC = c => c.ref ? 'Η αναφορά' : 'Το ' + c.name;

  /** (2.25.0) How much a speed is above (+) or below (−) another, in % with a decimal comma. */
  const VP = (v, w) => ok(v) && ok(w) && w > 0 ? P(100 * (v - w) / w) : '–';

  /**
   * (2.25.0) Where the speed of the swimming differs from the comparison: speed = rate × cycle length, so the difference of the speed is
   * the one of the rate and the one of the cycle length (their logarithms add up). A sentence of what the numbers show, the part that
   * weighs most ('main': 'sl', 'sr' or 'both') and a short phrase for the conclusion; null without the rate and the cycle length.
   */
  function swimParts(mine, theirs, name) {
    if (!ok(mine.sl) || !ok(theirs.sl) || !ok(mine.sr) || !ok(theirs.sr) || !(theirs.sl > 0) || !(theirs.sr > 0)) return null;
    const lsl = Math.log(mine.sl / theirs.sl), lsr = Math.log(mine.sr / theirs.sr), lv = lsl + lsr;
    const sl = VP(mine.sl, theirs.sl), sr = VP(mine.sr, theirs.sr);
    const slTxt = `μήκος κύκλου ${F(mine.sl)} μ. (${name} ${F(theirs.sl)}, ${sl})`, srTxt = `συχνότητα ${F(mine.sr, 1)} (${name} ${F(theirs.sr, 1)}, ${sr})`;
    let text, main, why;
    if (lv < 0 && lsl < 0 && lsr < 0) {
      main = Math.abs(lsl) > 2 * Math.abs(lsr) ? 'sl' : Math.abs(lsr) > 2 * Math.abs(lsl) ? 'sr' : 'both';
      text = `Και τα δύο είναι χαμηλότερα: ${slTxt}, ${srTxt}` + (main === 'sl' ? ', με το μεγαλύτερο μέρος στο μήκος κύκλου.' : main === 'sr' ? ', με το μεγαλύτερο μέρος στη συχνότητα.' : '.');
      why = main === 'sl' ? 'μικρότερο μήκος κύκλου' : main === 'sr' ? 'χαμηλότερη συχνότητα' : 'μικρότερο μήκος κύκλου και χαμηλότερη συχνότητα';
    } else if (lv < 0 && lsl < 0) {
      main = 'sl';
      text = `Το ${slTxt} είναι μικρότερο, και η ${srTxt} δεν το καλύπτει.`;
      why = 'μικρότερο μήκος κύκλου';
    } else if (lv < 0) {
      main = 'sr';
      text = `Η ${srTxt} είναι χαμηλότερη, και το ${slTxt} δεν την καλύπτει.`;
      why = 'χαμηλότερη συχνότητα';
    } else {
      main = lsl >= lsr ? 'sl' : 'sr';
      text = `Η ταχύτητα δεν είναι χαμηλότερη: ${slTxt}, ${srTxt}.`;
      why = 'ίδια ή μεγαλύτερη ταχύτητα';
    }
    return { text, main, why };
  }

  /**
   * The phases of a race against the reference (or the PB) from the numbers: what the numbers show (facts), what the comparison means
   * (meaning: a description, never what the athlete should do; 2.25.0, the analyst's request: until 2.24.1 an action and a target), a
   * short phrase for the conclusion (why), the time it loses and how far behind it is (lost, pct), where on the course (zones).
   */
  function recommendations(x) {
    const ev = x.ev, a = x.race, b = x.pb, D = ev.distance, u = U(ev);
    const relay = ev.relay, im = ev.style === 'im';
    const recs = [];
    const real = r => r.turns.filter(t => t.kind !== 'exchange');
    const tA = real(a);
    const who = relay ? 'ρεκόρ ομάδας' : 'ΑΡ';

    // 1. Turns: against the reference (or the PB) on the turns both have.
    const tc = against(x, r => real(r).some(t => ok(t.inOut)));
    if (tA.length && tc) {
      const tC = real(tc.m);
      const both = tA.map((t, i) => ({ t, c: tC[i] })).filter(p => p.c && ok(p.t.inOut) && ok(p.c.inOut));
      const gap = both.reduce((s0, p) => s0 + p.t.inOut - p.c.inOut, 0);
      if (both.length && gap > 0.08) {
        const tAb = both.map(p => p.t), tCb = both.map(p => p.c);
        const head0 = `−5/+15 κατά μ.ο. <b>${F(avg(tAb, 'inOut'))} s</b> (${tc.name} ${F(avg(tCb, 'inOut'))}` + (tc.ref && ok(avg(real(b), 'inOut')) ? `, ${who} ${F(avg(real(b), 'inOut'))})` : ')');
        const pct = 100 * gap / tAb.reduce((s0, t) => s0 + t.inOut, 0);
        const many = both.length > 1 ? `${F(gap / both.length)} s σε κάθε στροφή κατά μ.ο.` : '';
        if (im) {
          const worst = both.map(p => ({ t: p.t, c: p.c, d: p.t.inOut - p.c.inOut })).sort((p, q) => q.d - p.d)[0];
          const name = t => t.styleIn === t.styleOut ? `στροφή ${IN_STYLE[t.styleIn]}` : `${STYLE_NAME[t.styleIn].toLowerCase()} → ${STYLE_NAME[t.styleOut].toLowerCase()}`;
          const changes = tA.filter(t => t.kind === 'change').length;
          const facts = [head0, `η μεγαλύτερη διαφορά στα ${worst.t.wall}${u} (${name(worst.t)}): <b>${S(worst.d)} s</b> από ${theC(tc)}`];
          if (ok(worst.t.turnTime) || ok(worst.t.bd)) facts.push(`εκεί: στροφή ${vs(worst.t.turnTime, 'σύγκρ.', worst.c.turnTime)} s · έξοδος στα ${vs(worst.t.bd, 'σύγκρ.', worst.c.bd, 1)}${u}`);
          const spin = ok(worst.t.turnTime) && ok(worst.c.turnTime) ? worst.t.turnTime - worst.c.turnTime : NaN;
          const meaning = (many ? many + ' ' : '') + `Η μεγαλύτερη διαφορά είναι στα ${worst.t.wall}${u} (${name(worst.t)}): ` +
            (spin > 0.08 ? `εκεί η στροφή από την επαφή ως την ώθηση κρατά ${F(spin)} s περισσότερο.` : 'εκεί ο χρόνος χάνεται κυρίως μετά την ώθηση, στην υποβρύχια φάση και ως τα +15.');
          recs.push({ key: 'turn', title: changes === tA.length ? 'Αλλαγές στυλ' : changes ? 'Στροφές και αλλαγές στυλ' : `Στροφές (${tA.length})`, facts, meaning,
            why: spin > 0.08 ? `πιο αργή περιστροφή στα ${worst.t.wall}${u}` : `μετά την ώθηση, κυρίως στα ${worst.t.wall}${u}`, lost: gap, pct });
        } else {
          const outGap = avg(tAb, 'out15') - avg(tCb, 'out15'), inGap = avg(tAb, 'in5') - avg(tCb, 'in5');
          const bdA = avg(tA, 'bd'), bdC = avg(tCb, 'bd'), kA = avg(tA, 'kicks'), kC = avg(tCb, 'kicks');
          const trend = tA.length >= 3 ? avg(tA.slice(-2), 'inOut') - avg(tA.slice(0, 2), 'inOut') : NaN;
          const facts = [head0];
          if (ok(bdA)) facts.push(`έξοδος στα <b>${F(bdA, 1)}${u}</b>${ok(kA) ? ` με ${F(kA, 1)} δελφινισμούς` : ''}` + (ok(bdC) ? ` (${tc.name} ${F(bdC, 1)}${u}${ok(kC) ? ', ' + F(kC, 1) : ''})` : ''));
          if (ok(inGap) && ok(outGap)) facts.push(`−5 → τοίχος ${vs(avg(tAb, 'in5'), tc.name, avg(tCb, 'in5'))} s · τοίχος → +15 ${vs(avg(tAb, 'out15'), tc.name, avg(tCb, 'out15'))} s`);
          if (trend > 0.2) facts.push(`οι στροφές του τέλους είναι <b>${F(trend)} s</b> πιο αργές από τις πρώτες`);
          let meaning = many;
          const after0 = !(inGap > outGap);
          if (ok(inGap) && ok(outGap)) meaning += (after0
            ? ` Η διαφορά είναι κυρίως μετά τον τοίχο (τοίχος → +15 ${S(outGap)} s, −5 → τοίχος ${S(inGap)} s κατά μ.ο.): η ώθηση, η υποβρύχια φάση και η κολύμβηση ως τα +15.`
            : ` Η διαφορά είναι κυρίως πριν από τον τοίχο (−5 → τοίχος ${S(inGap)} s, τοίχος → +15 ${S(outGap)} s κατά μ.ο.): η προσέγγιση και η επαφή.`);
          const shorter = ok(bdA) && ok(bdC) ? bdC - bdA : NaN;
          if (Math.abs(shorter) >= 0.3) meaning += ` Η έξοδος γίνεται ${F(Math.abs(shorter), 1)}${u} ${shorter > 0 ? 'νωρίτερα' : 'αργότερα'} από ${theC(tc)}` +
            (ok(kA) && ok(kC) && Math.abs(kA - kC) >= 0.5 ? `, με ${F(kA, 1)} δελφινισμούς (${F(kC, 1)}).` : '.');
          if (trend > 0.2) meaning += ` Οι στροφές του τέλους είναι ${F(trend)} s πιο αργές από τις πρώτες.`;
          if (!meaning) meaning = `Η σύγκριση έχει μόνο τον χρόνο −5/+15: ${P(-pct)} ταχύτητα.`;
          meaning = meaning.trim();
          const why = after0 ? 'μετά τον τοίχο' + (shorter >= 0.3 ? ', με πιο κοντινή έξοδο' : '') : 'στην προσέγγιση του τοίχου';
          recs.push({ key: 'turn', title: tA.length > 1 ? `Στροφές (${tA.length})` : 'Στροφή', facts, meaning, why, lost: gap, pct });
        }
        recs[recs.length - 1].zones = both.map(p => ({ from: p.t.wall - 5, to: p.t.wall + 15, w: Math.max(0, p.t.inOut - p.c.inOut) }));
      }
    }

    // 2. Start: 15 m against the reference (or the PB).
    const s0 = a.start;
    const sc = against(x, r => ok(r.start.t15));
    const sGap = sc ? s0.t15 - sc.m.start.t15 : NaN;
    if (sGap > 0.05) {
      const c0 = sc.m.start;
      const blockLoss = s0.block - c0.block, entryLoss = c0.entryDist - s0.entryDist, bdLoss = c0.bd - s0.bd;
      const facts = [`15${u} σε <b>${F(s0.t15)} s</b> (${sc.name} ${F(c0.t15)}` + (sc.ref && ok(b.start.t15) ? `, ${who} ${F(b.start.t15)})` : ')')];
      if (ok(s0.block) || ok(s0.entryDist)) facts.push(`${s0.style === 'bk' ? 'ώθηση από τον τοίχο' : 'βατήρας'} ${vs(s0.block, sc.name, c0.block)} s` + (ok(s0.entryDist) ? ` · είσοδος στα ${vs(s0.entryDist, sc.name, c0.entryDist)}${u}` : ''));
      if (ok(s0.bd)) facts.push(`έξοδος στα <b>${F(s0.bd, 1)}${u}</b>${ok(s0.kicks) ? `, ${s0.kicks} δελφινισμοί` : ''}` + (ok(c0.bd) ? ` (${sc.name} ${F(c0.bd, 1)}${u}${ok(c0.kicks) ? ', ' + c0.kicks : ''})` : ''));
      // What the comparison shows: which part of the start holds the difference (the same order as the facts).
      const on = s0.style === 'bk' ? 'στην ώθηση από τον τοίχο' : 'στον βατήρα';
      let meaning, why;
      const known = [[ok(blockLoss), s0.style === 'bk' ? 'η ώθηση' : 'ο βατήρας'], [ok(entryLoss), 'η είσοδος'], [ok(bdLoss), 'η έξοδος']].filter(q => q[0]).map(q => q[1]);
      if (bdLoss >= 0.8) {
        meaning = `Η μεγαλύτερη διαφορά είναι στην υποβρύχια φάση: βγαίνει από το νερό ${F(bdLoss, 1)}${u} νωρίτερα (${F(s0.bd, 1)} έναντι ${F(c0.bd, 1)}${u})` +
          (ok(s0.kicks) && ok(c0.kicks) && s0.kicks !== c0.kicks ? `, με ${s0.kicks} δελφινισμούς (${c0.kicks}).` : '.');
        why = `έξοδος ${F(bdLoss, 1)}${u} νωρίτερα`;
      } else if (blockLoss >= 0.03) {
        meaning = `Η διαφορά ξεκινά ${on}: ${F(s0.block)} s έναντι ${F(c0.block)} s.`;
        why = s0.style === 'bk' ? 'πιο αργή ώθηση από τον τοίχο' : 'περισσότερος χρόνος στον βατήρα';
      } else if (entryLoss >= 0.15) {
        meaning = `Μπαίνει στο νερό ${F(entryLoss)}${u} πιο κοντά (${F(s0.entryDist)} έναντι ${F(c0.entryDist)}${u}).`;
        why = 'πιο κοντινή είσοδος στο νερό';
      } else if (known.length) {
        const list = known.length > 1 ? known.slice(0, -1).join(', ') + ' και ' + known[known.length - 1] : known[0];
        meaning = end(`${list.charAt(0).toUpperCase() + list.slice(1)} είναι κοντά ${nearC(sc)}: η διαφορά μαζεύεται στη διαδρομή ως τα 15${u}` +
          (ok(s0.bo15Vel) && ok(c0.bo15Vel) ? ` (έξοδος → 15: ${F(s0.bo15Vel)} έναντι ${F(c0.bo15Vel)} m/s)` : ''));
        why = 'στη διαδρομή ως τα 15' + u;
      } else {
        meaning = end(`${TheC(sc)} έχει μόνο τον χρόνο στα 15${u}: δεν φαίνεται σε ποιο κομμάτι της εκκίνησης είναι η διαφορά (${P(-100 * sGap / s0.t15)} ταχύτητα ως τα 15${u})`);
        why = 'ως τα 15' + u;
      }
      recs.push({ key: 'start', title: 'Εκκίνηση', facts, meaning, why, lost: sGap, pct: 100 * sGap / s0.t15 });
    }

    // 3. Swimming and ΔΑΚ: the time of the clean swimming against the reference (or the PB).
    const wc = against(x, r => ok(r.composition.swim));
    const swGap = wc ? a.composition.swim - wc.m.composition.swim : NaN;
    if (swGap > 0.1) {
      const c0 = wc.m;
      const facts = [];
      if (ok(a.avg.dak)) facts.push(`ΔΑΚ κατά μ.ο. <b>${F(a.avg.dak)}</b>` + [[who, b.avg.dak], ['αναφορά', x.ref.avg.dak]].filter(p => ok(p[1])).map(p => `${p[0]} ${F(p[1])}`).join(', ').replace(/^(.+)$/, ' ($1)'));
      facts.push(`ταχύτητα κολύμβησης <b>${F(ok(a.avg.vClean) ? a.avg.vClean : NaN)} m/s</b>` + (ok(c0.avg.vClean) ? ` (${wc.name} ${F(c0.avg.vClean)})` : ''));
      if (ok(a.avg.sl)) facts.push(`μήκος κύκλου ${vs(a.avg.sl, wc.name, c0.avg.sl)} μ. · συχνότητα ${vs(a.avg.sr, wc.name, c0.avg.sr, 1)} · tempo ${F(a.avg.tempo)} s/κύκλο`);
      facts.push(`κολύμβηση (χωρίς εκκίνηση, στροφές, τερματισμό) <b>${S(swGap)} s</b> από ${wc.ref ? 'την αναφορά' : 'το ' + who}`);
      const parts = swimParts(a.avg, c0.avg, wc.name);
      let meaning = parts ? parts.text : (ok(a.avg.vClean) && ok(c0.avg.vClean) ? `Η ταχύτητα της κολύμβησης είναι ${VP(a.avg.vClean, c0.avg.vClean)} από ${ofC(wc)}.` :
        `Η καθαρή κολύμβηση είναι ${F(swGap)} s πιο αργή.`);
      if (ok(a.avg.dak) && ok(c0.avg.dak)) meaning += ` Ο ΔΑΚ είναι ${F(a.avg.dak)} (${wc.name} ${F(c0.avg.dak)}): ${a.avg.dak > c0.avg.dak + 0.03 ? 'περισσότερες χεριές και δευτερόλεπτα για την ίδια απόσταση' : 'ίδια περίπου αποδοτικότητα'}.`;
      recs.push({ key: 'swim', title: 'Κολύμβηση και ΔΑΚ', facts, meaning, why: parts ? parts.why : 'πιο αργή κολύμβηση', lost: swGap, pct: 100 * swGap / a.composition.swim });
    }

    // 4. ΔΑΚ through the race (the second half against the first): from the race alone, beside the reference's when it has it.
    const bl = a.blocks;
    const blD = bl.filter(bk => ok(bk.dak));
    if (blD.length >= 4 && blD.length === bl.length && !im) {
      const h = Math.floor(bl.length / 2), first = bl.slice(0, h), second = bl.slice(h);
      const rise = avg(second, 'dak') - avg(first, 'dak');
      if (rise >= 0.3) {
        const rS = avg(second, 'dakStrokes') - avg(first, 'dakStrokes'), rT = avg(second, 'dakTime') - avg(first, 'dakTime');
        const fT = avg(first, 'dakTime');
        const save = second.reduce((acc, bk) => acc + (ok(bk.dakTime) && ok(fT) && bk.dakTime > 0 ? Math.max(0, bk.dakTime - fT) * (bk.winTime / bk.dakTime) : 0), 0);
        const top = bl.reduce((p, q) => q.dak > p.dak ? q : p);
        const facts = [`ΔΑΚ 1ου μισού <b>${F(avg(first, 'dak'))}</b> → 2ου μισού <b>${F(avg(second, 'dak'))}</b> (${S(rise)})`];
        if (ok(rS) && ok(rT)) facts.push(`από τις χεριές ${S(rS)} και από τον χρόνο ${S(rT)} ανά 10 μ.`);
        facts.push(`ο υψηλότερος ΔΑΚ στο 50άρι ${top.from}–${top.to}${u} (${F(top.dak)})`);
        let meaning = 'Η ίδια απόσταση κοστίζει περισσότερο στο 2ο μισό' + (!(ok(rS) && ok(rT)) ? '.' : rS > rT
          ? `, κυρίως σε χεριές (${S(rS)} ανά 10 μ.): ο κύκλος μικραίνει.` : `, κυρίως σε χρόνο (${S(rT)} s ανά 10 μ.): η ταχύτητα πέφτει περισσότερο από όσο αλλάζουν οι χεριές.`);
        const rb = x.ref.blocks;
        if (rb && rb.length === bl.length && rb.every(bk => ok(bk.dak))) {
          const riseC = avg(rb.slice(h), 'dak') - avg(rb.slice(0, h), 'dak');
          meaning += ` Στην αναφορά ο ΔΑΚ αλλάζει ${S(riseC)}.`;
        }
        recs.push({ key: 'dak', title: 'ΔΑΚ στο 2ο μισό', facts, meaning, why: `ΔΑΚ ${S(rise)} στο 2ο μισό` + (ok(rS) && ok(rT) ? (rS > rT ? ' (χεριές)' : ' (χρόνος)') : ''),
          lost: save > 0.05 ? save : null, pct: null });
      }
    }

    // 5. Pacing (200 and longer, not the medley): the race against itself, and the reference (or the PB) when it has the 50s.
    let splits = null, pace = null;
    const allTimes = r => r.blocks.length === bl.length && r.blocks.every(bk => ok(bk.time));
    if (bl.length >= 4 && !im && allTimes(a)) {
      const pc = allTimes(x.ref) ? { m: x.ref, name: 'αναφορά', ref: true } : allTimes(b) ? { m: b, name: who, ref: false } : null;
      const mean = r => avg(r.blocks.slice(1), 'time');
      const half = r => { const n = Math.floor(r.blocks.length / 2); return sum(r.blocks.slice(n), 'time') - sum(r.blocks.slice(0, n), 'time'); };
      const facts = [`1ο 50άρι <b>${T(bl[0].time)}</b>, μ.ο. των υπολοίπων ${T(mean(a))}` + (pc ? ` (${pc.name} ${T(pc.m.blocks[0].time)} / ${T(mean(pc.m))})` : ''),
        `2ο μισό ${S(half(a))} s από το 1ο` + (pc ? ` (${pc.name} ${S(half(pc.m))})` : '')];
      let meaning = null, why = null;
      const dev0 = bl[0].time - mean(a);
      if (pc) {
        const dev0c = pc.m.blocks[0].time - mean(pc.m), fade = half(a) - half(pc.m);
        const lastGap = avg(bl.slice(-2), 'time') - avg(pc.m.blocks.slice(-2), 'time'), midGap = avg(bl.slice(1, -2), 'time') - avg(pc.m.blocks.slice(1, -2), 'time');
        if (dev0 - dev0c < -0.4) {
          meaning = `Το 1ο 50άρι είναι ${F(-dev0)} s πιο γρήγορο από τον μέσο όρο των υπολοίπων, ενώ ${pc.ref ? 'στην αναφορά' : 'στο ' + pc.name} ${F(-dev0c)} s: ο αθλητής ξεκινά αναλογικά πιο γρήγορα.` +
            (fade > 0.3 ? ` Το 2ο μισό είναι ${S(half(a))} s από το 1ο (${pc.name} ${S(half(pc.m))}).` : '');
          why = 'αναλογικά πιο γρήγορο 1ο 50άρι';
        } else if (fade > 0.6) {
          meaning = `Το 2ο μισό είναι ${S(half(a))} s από το 1ο, ${pc.ref ? 'στην αναφορά' : 'στο ' + pc.name} ${S(half(pc.m))}: στο 2ο μισό ο αθλητής χάνει ${F(fade)} s περισσότερα.`;
          why = `${F(fade)} s περισσότερα στο 2ο μισό`;
        } else if (bl.length >= 6 && lastGap > midGap + 0.2) {
          meaning = `Τα δύο τελευταία 50άρια είναι ${F(lastGap)} s πιο αργά από ${theC(pc)} κατά μ.ο., τα μεσαία ${F(midGap)} s: η διαφορά μεγαλώνει στο τέλος.`;
          why = 'η διαφορά μεγαλώνει στο τελευταίο 100άρι';
        }
      } else if (half(a) / a.final > 0.05) {
        meaning = `Το 2ο μισό είναι ${S(half(a))} s πιο αργό από το 1ο (${P(100 * half(a) / a.final).replace(/^\+/, '')} του χρόνου).`;
        why = 'πιο αργό 2ο μισό';
      }
      if (meaning) recs.push({ key: 'pace', title: 'Ρυθμός αγώνα', facts, meaning, why, lost: null, pct: null });
      pace = pc;
    }

    // 6. Finish: the last 5 against the reference (or the PB).
    const fc = against(x, r => ok(r.finish.last5));
    const fGap = fc ? a.finish.last5 - fc.m.finish.last5 : NaN;
    if (fGap > 0.05) {
      const c0 = fc.m.finish;
      const gapLoss = a.finish.lastGap - c0.lastGap;
      const facts = [`τελευταία 5${u} σε <b>${F(a.finish.last5)} s</b> (${fc.name} ${F(c0.last5)}` + (fc.ref && ok(b.finish.last5) ? `, ${who} ${F(b.finish.last5)})` : ')')];
      if (ok(a.finish.lastGap)) facts.push(`τελευταία χεριά → επαφή ${vs(a.finish.lastGap, fc.name, c0.lastGap)} s`);
      const meaning = ok(a.finish.lastGap) && ok(c0.lastGap) ? (gapLoss > 0.04
        ? `Από την τελευταία χεριά ως την επαφή περνούν ${F(a.finish.lastGap)} s (${fc.name} ${F(c0.lastGap)}): η διαφορά είναι κυρίως εκεί, στο κομμάτι χωρίς χεριά ως τον τοίχο.`
        : `Από την τελευταία χεριά ως την επαφή ${F(a.finish.lastGap)} s, όσο περίπου ${ofC(fc)} (${F(c0.lastGap)}): η διαφορά είναι στην ταχύτητα των τελευταίων μέτρων.`)
        : end(`${TheC(fc)} δεν έχει τον χρόνο της τελευταίας χεριάς: φαίνεται μόνο η ταχύτητα των τελευταίων 5${u}, ${P(-100 * fGap / a.finish.last5)}`);
      recs.push({ key: 'finish', title: 'Τερματισμός', facts, meaning, why: gapLoss > 0.04 ? 'τελευταία χεριά → επαφή' : ok(gapLoss) ? 'πιο αργά τελευταία μέτρα' : 'τελευταία 5' + u,
        lost: fGap, pct: 100 * fGap / a.finish.last5 });
    }

    // 7. Stroke rate in the second half (100 and longer, not the medley): from the race alone, beside the reference's when it has it.
    const free = a.rows.filter(r => ok(r.sr));
    if (!im && free.length >= 4) {
      const n = free.length, early = free.slice(0, Math.max(1, Math.floor(n / 3))), late = free.slice(Math.floor(2 * n / 3), D >= 400 ? n - 2 : n);
      const drop = late.length ? avg(early, 'sr') - avg(late, 'sr') : 0;
      if (drop >= 1.8) {
        const slE = avg(early, 'sl'), slL = avg(late, 'sl'), vE = avg(early, 'vel'), vL = avg(late, 'vel');
        let meaning = `Η συχνότητα πέφτει ${VP(avg(late, 'sr'), avg(early, 'sr'))} από την αρχή ως το τέλος.`;
        if (ok(slE) && ok(slL)) meaning += ` Το μήκος κύκλου ${slL >= slE - 0.02 ? 'μένει ίδιο ή μεγαλώνει' : 'μικραίνει κι αυτό'} (${F(slE)} → ${F(slL)} μ.)` +
          (ok(vE) && ok(vL) ? ` και η ταχύτητα πηγαίνει από ${F(vE)} σε ${F(vL)} m/s` : '') + (slL >= slE - 0.02 ? ': η πτώση της ταχύτητας έρχεται από τη συχνότητα.' : '.');
        const cr = x.ref.rows.filter(r => ok(r.sr));
        if (cr.length === n) {
          const cE = avg(cr.slice(0, Math.max(1, Math.floor(n / 3))), 'sr'), cL = avg(cr.slice(Math.floor(2 * n / 3), D >= 400 ? n - 2 : n), 'sr');
          if (ok(cE) && ok(cL)) meaning += ` Στην αναφορά: ${F(cE, 1)} → ${F(cL, 1)}.`;
        }
        recs.push({ key: 'rate', title: 'Συχνότητα στο τέλος', facts: [
          `συχνότητα στην αρχή <b>${F(avg(early, 'sr'), 1)}</b> → στο τέλος <b>${F(avg(late, 'sr'), 1)}</b> κύκλοι/λεπτό (${S(-drop, 1)})`,
          `tempo ${F(60 / avg(early, 'sr'))} → ${F(60 / avg(late, 'sr'))} s/κύκλο`],
          meaning, why: `συχνότητα ${S(-drop, 1)} στο τέλος`, lost: null, pct: null });
      }
    }

    // 8. Relays: the takeovers (the zone of 0.10–0.20 s needs nothing to compare with).
    if (relay && a.legs) {
      a.legs.slice(1).forEach((l, j) => {
        if (!(l.ex > 0.24)) return;
        const facts = [`επαφή → πόδια φεύγουν <b>${F(l.ex)} s</b>` + [[who, b.legs && b.legs[j + 1] ? b.legs[j + 1].ex : NaN], ['αναφορά', x.ref.legs && x.ref.legs[j + 1] ? x.ref.legs[j + 1].ex : NaN]]
          .filter(p => ok(p[1])).map(p => `${p[0]} ${F(p[1])}`).join(', ').replace(/^(.+)$/, ' ($1)')];
        if (ok(l.out15)) facts.push(`15${u} από την επαφή σε ${F(l.out15)} s`);
        recs.push({ key: 'ex' + (j + 2), title: `Αλλαγή ${j + 1} → ${j + 2}`, facts, zones: [{ from: l.from - 5, to: l.from + 15, w: 1 }],
          meaning: `Από την επαφή του ${j + 1}ου ως το ξεκίνημα του ${j + 2}ου περνούν ${F(l.ex)} s. Στις γρήγορες αλλαγές είναι 0.10–0.20 s: εδώ περίπου ${F(l.ex - 0.18)} s παραπάνω.`,
          why: `αλλαγή ${F(l.ex)} s`, lost: l.ex - 0.18, pct: null });
      });
    }

    // (2.24.0) Where on the course each phase loses its time against the reference (or the PB), the map of the race: the start (to 15),
    // the clean swimming of every length (15 after a wall to 5 before the next), each turn (−5/+15), the finish (the last 5), the
    // takeovers. The ΔΑΚ of the second half is the race against itself, its time is in the swimming: not on the map.
    const lengths = [];
    for (let s0 = 0; s0 < D; s0 += ev.pool) lengths.push({ from: s0, to: Math.min(D, s0 + ev.pool) });
    const swimIn = (lo, hi) => lengths.map(l => ({ from: Math.max(l.from + 15, lo), to: Math.min(l.to >= D ? D - 5 : l.to - 5, hi) }))
      .filter(z => z.to > z.from).map(z => ({ from: z.from, to: z.to, w: z.to - z.from }));
    recs.forEach(r => {
      if (r.zones) return;
      if (r.key === 'start') r.zones = [{ from: 0, to: 15, w: 1 }];
      else if (r.key === 'swim') r.zones = swimIn(0, D);
      else if (r.key === 'finish') r.zones = [{ from: D - 5, to: D, w: 1 }];
    });
    recs.forEach(r => {
      if (r.lost !== null && !ok(r.lost)) r.lost = null;
      if (r.pct !== null && !ok(r.pct)) r.pct = null;
    });
    // (2.24.0) The order: the phases against the reference (or the PB) by how far behind they are (%), then the others by the time
    // they lose, then the rest (until 2.23.0: by the time won if the phase came half the way to the reference).
    recs.sort((p, q) => (q.pct !== null) - (p.pct !== null) || (p.pct !== null ? q.pct - p.pct : 0) ||
      (q.lost !== null) - (p.lost !== null) || (p.lost !== null ? q.lost - p.lost : 0));
    // (2.23.0) The splits per 50: today's time with the pacing of the reference (or the PB): the same time, a better distribution.
    if (bl.length >= 4 && !im && pace && ok(pace.m.final) && ok(a.final)) splits = pace.m.blocks.map(bk => bk.time * a.final / pace.m.final);

    // (2.24.0) The level of each phase: how far its speed is behind the reference (or the PB) on the same distance, in % of the speed
    // of the race (100 × lost / time; less than 0: faster), and the time it loses there. Every phase that both have, as they are.
    const levels = [];
    const lvl = (key, title, c, mine, theirs) => {
      if (c && ok(mine) && ok(theirs) && mine > 0) levels.push({ key, title, pct: 100 * (mine - theirs) / mine, lost: mine - theirs, name: c.name, ref: !!c.ref });
    };
    if (sc) lvl('start', 'Εκκίνηση', sc, s0.t15, sc.m.start.t15);
    if (wc) lvl('swim', 'Κολύμβηση', wc, a.composition.swim, wc.m.composition.swim);
    if (tc && tA.length) {
      const pairs = tA.map((t, i) => ({ t, c: real(tc.m)[i] })).filter(q => q.c && ok(q.t.inOut) && ok(q.c.inOut));
      if (pairs.length) lvl('turn', 'Στροφές', tc, pairs.reduce((s1, q) => s1 + q.t.inOut, 0), pairs.reduce((s1, q) => s1 + q.c.inOut, 0));
    }
    if (fc) lvl('finish', 'Τερματισμός', fc, a.finish.last5, fc.m.finish.last5);

    const zones = [];
    recs.filter(r => r.lost !== null && r.lost > 0 && r.key !== 'dak').forEach(r => {
      const ws = (r.zones || []).reduce((acc, z) => acc + (z.w > 0 ? z.w : 0), 0);
      if (ws > 0) r.zones.forEach(z => { if (z.w > 0 && Math.min(D, z.to) > Math.max(0, z.from)) zones.push({ key: r.key, title: r.title, from: Math.max(0, z.from), to: Math.min(D, z.to), lost: r.lost * z.w / ws }); });
    });
    return { recs, splits, splitsFrom: pace ? pace.name : '', levels, map: { D, pool: ev.pool, u, zones } };
  }

  /** (2.24.0) A % with its sign and a decimal comma: «−7,4%», «+2,1%». */
  const P = v => (v > 0.05 ? '+' : v < -0.05 ? '−' : '') + Math.abs(v).toFixed(1).replace('.', ',') + '%';

  /** (2.24.0) Where the time of a phase is lost (the third message). */
  const IN_PHASE = { start: 'στην εκκίνηση', swim: 'στην κολύμβηση', turn: 'στις στροφές', finish: 'στον τερματισμό' };

  /** The three messages of the first page. */
  function messages(x, R) {
    const ev = x.ev, a = x.race, b = x.pb, c = x.ref;
    const pbWho = ev.relay ? 'τον καλύτερο χρόνο της ομάδας' : 'το ατομικό ρεκόρ';
    const out = [];
    const d = a.final - b.final, dr = a.final - c.final;
    const refPart = ok(dr) ? `${S(dr)} s από την αναφορά (${T(c.final)})` : '';
    if (ok(d)) out.push(d < -0.005 ? `<b>${ev.relay ? 'Νέος καλύτερος χρόνος ομάδας' : 'Νέο ατομικό ρεκόρ'}: ${T(a.final)}</b>, ${S(d)} s από ${pbWho}${refPart ? '· ' + refPart : ''}.`
      : `<b>${T(a.final)}</b>: ${S(d)} s από ${pbWho} (${T(b.final)})${refPart ? '· ' + refPart : ''}.`);
    else if (refPart) out.push(`<b>${T(a.final)}</b>: ${refPart}.`);
    else out.push(`<b>${T(a.final)}</b>, μέση ταχύτητα ${F(ev.distance * a.k / a.final)} m/s.`);
    const bl = a.blocks;
    if (ok(a.avg.dak)) {
      const dPbDak = a.avg.dak - b.avg.dak, dRefDak = a.avg.dak - c.avg.dak;
      const parts = [];
      if (ok(dPbDak)) parts.push(`${dPbDak < -0.03 ? 'καλύτερος από' : dPbDak > 0.03 ? 'χειρότερος από' : 'ίδιος με'} ${ev.relay ? 'τον καλύτερο αγώνα της ομάδας' : 'το ΑΡ'} (${F(b.avg.dak)})`);
      if (ok(dRefDak)) parts.push(dRefDak < -0.03 ? `καλύτερος και από την αναφορά (${F(c.avg.dak)})` : dRefDak > 0.03 ? `${S(dRefDak)} από την αναφορά (${F(c.avg.dak)})` : `ίδιος με την αναφορά (${F(c.avg.dak)})`);
      let dakMsg = `<b>ΔΑΚ ${F(a.avg.dak)}</b>` + (parts.length ? ': ' + parts.join(', ') + '.' : '.');
      if (bl.length >= 4 && ev.style !== 'im' && bl.every(bk => ok(bk.dak))) {
        const h = Math.floor(bl.length / 2);
        const rise = avg(bl.slice(h), 'dak') - avg(bl.slice(0, h), 'dak');
        dakMsg += rise >= 0.3 ? ` Ανεβαίνει ${S(rise)} στο 2ο μισό.` : ' Σταθερός σε όλο τον αγώνα.';
      }
      out.push(dakMsg);
    } else {
      out.push('Ο ΔΑΚ θέλει τις χεριές του αγώνα (στα 15 → τοίχος κάθε μήκους): δεν μετρήθηκαν όλες.');
    }
    // (2.24.0) The weakest phase against the reference (or the PB), and where the most time is lost when it is another phase: one line
    // (the first page has no room for more), the order of all the phases on page 2.
    const behind = (R.levels || []).filter(l => l.pct > 0.05);
    const weak = behind.slice().sort((p0, q0) => q0.pct - p0.pct)[0], most = behind.slice().sort((p0, q0) => q0.lost - p0.lost)[0];
    if (weak) out.push(`<b>Η πιο αδύνατη φάση: ${low(weak.title)}, ${P(-weak.pct)}</b> ${weak.ref ? 'από την αναφορά' : 'από το ' + weak.name}` +
      (most === weak ? `· χάνει ${F(weak.lost)}&nbsp;s.` : `· ${IN_PHASE[most.key]} χάνει τα περισσότερα (${F(most.lost)}&nbsp;s).`));
    else if (R.recs.length) out.push(`<b>${R.recs[0].title}</b>: ${R.recs[0].why}.`);
    else if (ok(c.final) || ok(b.final)) out.push('Σε όλες τις φάσεις ο αγώνας είναι κοντά στις συγκρίσεις του.');
    else out.push('Χωρίς ΑΡ και αναφορά τα ευρήματα βγαίνουν μόνο από τον ίδιο τον αγώνα (ΔΑΚ, ρυθμός, συχνότητα).');
    return out;
  }

  // ---------------------------------------------------------------------------------------------------------------------------
  // Pages
  // ---------------------------------------------------------------------------------------------------------------------------
  // Report templates, 2nd version: every event (50 m, 25 m, 25 yd; all styles, medley, relays), bigger type, ΔΑΚ as the main
  // index, recommendations from the numbers. Fictional data only (sim2.js).
  const W = 726;

  function chip(x, better, n, suffix) {
    if (x === null || x === undefined || !isFinite(x)) return '';
    const k = Math.pow(10, n === undefined ? 2 : n);
    const zero = Math.round(Math.abs(x) * k) === 0;
    const good = better === 'lower' ? x < 0 : better === 'higher' ? x > 0 : null;
    const cls = zero || good === null ? 'chip n' : good ? 'chip g' : 'chip l';
    return `<span class="${cls}">${S(x, n)}${suffix || ''}</span>`;
  }

  // ---- names
  /**
   * The names of the report: the event, the athlete (or the team) and the details of the race (x.details, from the race), what the
   * personal best and the reference are, and which of them there is (I.has: a missing one is compared with nothing).
   */
  function info(x) {
    const ev = x.ev, yd = ev.unit === 'yd', u = yd ? ' yd' : ' μ.', uu = yd ? ' yd' : 'μ.';
    const d = x.details || {};
    const legLen = ev.distance / 4;
    const style = ev.style === 'im' ? (ev.relay ? 'Μικτή' : 'Μικτή Ατομική') : STYLE_NAME[ev.style];
    const title = ev.relay ? `4×${legLen}${uu} ${style}` : `${ev.distance}${uu} ${style}`;
    const has = { pb: ok(x.pb.final), ref: ok(x.ref.final) };
    return { ev, yd, u, uu, title, relay: ev.relay, im: ev.style === 'im', D: ev.distance, P: ev.pool,
      who: d.who || (ev.relay ? 'Ομάδα' : 'Αθλητής'), kind: d.kind || (ev.relay ? 'Ομαδικό' : ''),
      pbName: ev.relay ? 'Ρεκόρ ομάδας' : 'Ατομικό ρεκόρ', pbShort: ev.relay ? 'Ρεκ. ομάδας' : 'ΑΡ', refName: 'Αναφορά', pool: ev.poolName,
      pbLabel: d.pbLabel || '', refLabel: d.refLabel || '', competition: d.competition || '', meta: d.meta || [], round: d.round || '',
      swimmers: d.swimmers || ['', '', '', ''], has };
  }

  // ---- frame
  function band(x, I) {
    const a = x.race, b = x.pb, c = x.ref;
    const subs = [];
    if (I.has.pb) subs.push(`<span>${esc(I.pbShort)} ${T(b.final)} ${chip(a.final - b.final, 'lower')}</span>`);
    if (I.has.ref) subs.push(`<span>Αναφ. ${T(c.final)} ${chip(a.final - c.final, 'lower')}</span>`);
    if (!subs.length && ok(a.final)) subs.push(`<span>μέση ταχύτητα ${F(I.D * a.k / a.final)} m/s</span>`);
    return `<div class="band">
      <div class="band-top"><span class="kicker">ΑΝΑΛΥΣΗ ΑΓΩΝΑ · RACE ANALYSIS</span>${I.round ? `<span class="proto">${esc(upper(I.round))}</span>` : ''}</div>
      <div class="band-main">
        <div class="who">
          <div class="event">${esc(I.title)}${I.kind ? ` <span class="cat">${esc(I.kind)}</span>` : ''}</div>
          <div class="name">${esc(I.who)}</div>
          <div class="meta">${I.meta.map(esc).join(' · ')}</div>
        </div>
        <div class="hero">
          <div class="hero-lab">Τελικός χρόνος</div>
          <div class="hero-time">${T(a.final)}</div>
          <div class="hero-sub">${subs.join('')}</div>
        </div>
      </div>
    </div>`;
  }
  function head(I, title) {
    return `<div class="ph"><div class="ph-l"><span class="ph-ev">${esc(I.title)}</span><span class="ph-sep">·</span>${esc(I.who)}<span class="ph-sep">·</span>${esc(I.pool)}</div><div class="ph-r">${esc(title)}</div></div>`;
  }
  let footText = 'Kinovea Swim · Ανάλυση αγώνα';
  const foot = (n, N) => `<div class="pf"><span>${esc(footText)}</span><span>Σελίδα ${n} από ${N}</span></div>`;
  const section = (t, s) => `<div class="sec"><div class="sec-t">${esc(t)}</div>${s ? `<div class="sec-s">${s}</div>` : ''}</div>`;
  // (2.27.1) The check of the breakout (Kinovea 2.41.1, the analyst's rule 1 as a check): the speed from the breakout to the 15 (+15)
  // below the swimming after it, as measured, with a ⚠ and this line.
  const slowNote = what => `<div class="warn">⚠ ${esc(what)}: πιο αργά από την κολύμβηση μετά (χάνει ταχύτητα στην έξοδο ή η απόσταση εξόδου μετρήθηκε πιο μακριά).</div>`;
  const page = (I, title, body, n, N, first) => `<div class="page${first ? ' p1' : ''}">${first ? '' : head(I, title)}${body}${foot(n, N)}</div>`;
  /** Capitals without the accents (as Greek is written in capitals). */
  const upper = s => String(s || '').toUpperCase().replace(/[ΆΈΉΊΌΎΏ]/g, ch0 => ({ 'Ά': 'Α', 'Έ': 'Ε', 'Ή': 'Η', 'Ί': 'Ι', 'Ό': 'Ο', 'Ύ': 'Υ', 'Ώ': 'Ω' })[ch0]);

  function tile(label, value, unit, dPb, dRef, better, n, extra, I) {
    const deltas = dPb === null && dRef === null ? '' : `<div class="t-d"><span>${esc(I ? I.pbShort : 'ΑΡ')} ${chip(dPb, better, n) || '–'}</span><span>Αναφ. ${chip(dRef, better, n) || '–'}</span></div>`;
    return `<div class="tile"><div class="t-lab">${esc(label)}</div><div class="t-val">${value}<span class="t-u">${esc(unit || '')}</span></div>${deltas}${extra ? `<div class="t-x">${extra}</div>` : ''}</div>`;
  }
  const legend3 = I => `<div class="lgrow c">${[['Αυτός ο αγώνας', C.race, ''], [I.pbName, C.pb, 'dash'], [I.refName, C.ref, 'dot']].map(([t, col, d]) => `<span class="lgi"><b class="ln ${d}" style="border-color:${col}"></b>${esc(t)}</span>`).join('')}</div>`;
  const three = (x, I, f, from) => [{ name: 'Αυτός ο αγώνας', color: C.race, values: from(x.race).map(f) },
    { name: I.pbName, color: C.pb, dash: 'dash', marker: 'square', values: from(x.pb).map(f) },
    { name: I.refName, color: C.ref, dash: 'dot', marker: 'diamond', values: from(x.ref).map(f) }];
  const realTurns = r => r.turns.filter(t => t.kind !== 'exchange');
  /** The time of a race at a distance (0 at the start), NaN when it has none there. */
  const tOf = (r, d) => d === 0 ? 0 : (r.byD[d] ? r.byD[d].t : NaN);
  const legLines = (x, cats) => x.ev.relay ? [1, 2, 3].map(k => ({ at: Math.round(cats.length * k / 4), label: 'Σκέλος ' + (k + 1) })) : [];

  // ---- page 1: overview
  function scorecard(x, I) {
    const a = x.race, b = x.pb, c = x.ref, tA = realTurns(a);
    const rows = [
      ['ΔΑΚ (μ.ο. 50άρι)', a.avg.dak, b.avg.dak, c.avg.dak, 'lower', v => F(v), '', true],
      ['Εκκίνηση 15' + I.u, a.start.t15, b.start.t15, c.start.t15, 'lower', v => F(v), ' s'],
      tA.length ? ['Στροφή −5/+15 (μ.ο.)', avg(tA, 'inOut'), avg(realTurns(b), 'inOut'), avg(realTurns(c), 'inOut'), 'lower', v => F(v), ' s'] : null,
      I.relay && a.legs ? ['Αλλαγές (μ.ο.)', avg(a.legs.slice(1), 'ex'), b.legs ? avg(b.legs.slice(1), 'ex') : NaN, c.legs ? avg(c.legs.slice(1), 'ex') : NaN, 'lower', v => F(v), ' s'] : null,
      ['Ταχύτητα κολύμβησης', a.avg.vClean, b.avg.vClean, c.avg.vClean, 'higher', v => F(v), ' m/s'],
      ['Μήκος κύκλου', a.avg.sl, b.avg.sl, c.avg.sl, 'higher', v => F(v), ' μ.'],
      ['Συχνότητα · tempo', a.avg.sr, b.avg.sr, c.avg.sr, 'none', v => F(v, 1), ' κύκλ./λ.', false, ok(a.avg.tempo) ? `tempo ${F(a.avg.tempo)} s` : ''],
      ['Τερματισμός 5' + I.u, a.finish.last5, b.finish.last5, c.finish.last5, 'lower', v => F(v), ' s']
    ].filter(r => r && ok(r[1]));
    if (!rows.length) return '<div class="nt0">Δεν υπάρχουν ακόμη δείκτες: λείπουν οι χρόνοι των σημείων ελέγχου.</div>';
    return `<table class="sc"><thead><tr><th>Δείκτης</th><th>Αγώνας</th><th class="bh">← χειρότερα · καλύτερα →</th><th>${esc(I.pbShort)}</th><th>Αναφ.</th></tr></thead><tbody>
      ${rows.map(([lab, va, vb, vc, better, f, unit, main, sub]) => `<tr${main ? ' class="main"' : ''}><td class="l">${esc(lab)}${sub ? `<div class="sub">${esc(sub)}</div>` : ''}</td><td class="v"><b>${f(va)}</b><span class="u">${esc(unit)}</span></td>
        <td class="b">${ch.bullet({ w: 270, a: va, pb: vb, ref: vc, better: better === 'none' ? 'higher' : better })}</td><td>${chip(va - vb, better, f(1) === '1.0' ? 1 : 2) || '–'}</td><td>${chip(va - vc, better, f(1) === '1.0' ? 1 : 2) || '–'}</td></tr>`).join('')}
    </tbody></table>`;
  }

  function raceStrip(x, I) {
    const a = x.race;
    if (a.blocks.length <= 2 && !I.relay) {
      const items = a.rows.map(r => ({ label: `${r.prevD}–${r.d}`, time: F(r.seg), dak: NaN, dakText: F(r.vel) + ' m/s', mark: false }));
      return ch.heatStrip({ w: W - 26, items, neutral: true }).replace(/ΔΑΚ /g, '');
    }
    const items = a.blocks.map(bk => ({ label: `${bk.from}–${bk.to}`, time: T(bk.time), dak: bk.dak, dakText: F(bk.dak) }));
    return ch.heatStrip({ w: W - 26, items, neutral: x.ev.style === 'im' });
  }

  /** (2.23.0) The colour of the heat of the map: 0 (little time there) teal → 1 (the most) orange-red. */
  function heatColor(v, cool) {
    // (2.26.0) cool: the blues of the slides (the analyst: blue, not pink), from the dark blue (little time there) to the light.
    const st = cool ? [[0, [24, 66, 128]], [0.4, [46, 107, 214]], [0.7, [0, 170, 240]], [1, [150, 228, 255]]]
      : [[0, [31, 111, 120]], [0.4, [43, 179, 160]], [0.7, [245, 163, 92]], [1, [255, 90, 54]]];
    const x0 = Math.max(0, Math.min(1, v));
    let k = 1;
    while (k < st.length - 1 && x0 > st[k][0]) k++;
    const f = (x0 - st[k - 1][0]) / (st[k][0] - st[k - 1][0]);
    return 'rgb(' + st[k - 1][1].map((c, i) => Math.round(c + (st[k][1][i] - c) * f)).join(',') + ')';
  }

  /**
   * (2.23.0) «Πού κρύβεται ο χρόνος»: the race on its course (start → finish, every wall), each part lit by the time the phases lose
   * there against the reference (or the PB, 2.24.0: the whole of it), in seconds per metre (the most: the brightest, with a glow).
   * 'big': the map of page 2 (dark, the distances and the seconds of the hottest spots); otherwise a strip. Nothing without a zone.
   * cool (2.26.0): in the blues of the slides.
   */
  function raceMap(m, big, id, cool) {
    if (!m || !m.zones || !m.zones.length || !(m.D > 0)) return '';
    const W = big ? 640 : 220, x0 = big ? 14 : 1, lw = W - 2 * x0, H = big ? 64 : 14, ly = big ? 22 : 2, lh = big ? 18 : 10;
    const X = v => x0 + lw * v / m.D;
    const n = Math.ceil(m.D), bins = new Array(n).fill(0);
    m.zones.forEach(z => {
      const a0 = Math.max(0, Math.floor(z.from)), b0 = Math.min(n, Math.ceil(z.to));
      for (let i = a0; i < b0; i++) bins[i] += z.lost / (b0 - a0);
    });
    const max = bins.reduce((p0, q0) => Math.max(p0, q0), 0);
    if (!(max > 0)) return '';
    const segs = [];
    for (let i = 0; i < n;) {
      let j = i + 1;
      while (j < n && Math.abs(bins[j] - bins[i]) < 1e-12) j++;
      if (bins[i] > 0) segs.push({ from: i, to: Math.min(j, m.D), v: bins[i] / max });
      i = j;
    }
    const rect = (s, dy, o) => `<rect x="${X(s.from).toFixed(1)}" y="${(ly - dy).toFixed(1)}" width="${Math.max(0.8, X(s.to) - X(s.from)).toFixed(1)}" height="${(lh + 2 * dy).toFixed(1)}" fill="${heatColor(s.v, cool)}" ${o}/>`;
    let g = `<defs><filter id="${id}g" x="-50%" y="-200%" width="200%" height="500%"><feGaussianBlur stdDeviation="${big ? 3.5 : 1.6}"/></filter>` +
      `<clipPath id="${id}c"><rect x="${x0}" y="${ly}" width="${lw}" height="${lh}" rx="${lh / 2}"/></clipPath></defs>`;
    g += `<rect x="${x0}" y="${ly}" width="${lw}" height="${lh}" rx="${lh / 2}" fill="${big ? '#1B2B42' : '#E3E9F0'}"/>`;
    g += segs.filter(s => s.v > 0.5).map(s => rect(s, big ? 4 : 1.5, `opacity="${(0.7 * s.v).toFixed(2)}" filter="url(#${id}g)"`)).join('');
    g += `<g clip-path="url(#${id}c)">` + segs.map(s => rect(s, 0, `opacity="${(0.4 + 0.6 * s.v).toFixed(2)}"`)).join('') + '</g>';
    const walls = [];
    for (let w = m.pool; w < m.D - 0.5; w += m.pool) walls.push(w);
    g += walls.map(w => `<line x1="${X(w).toFixed(1)}" x2="${X(w).toFixed(1)}" y1="${ly - (big ? 4 : 0)}" y2="${ly + lh + (big ? 4 : 0)}" stroke="${big ? 'rgba(255,255,255,0.5)' : '#FFFFFF'}" stroke-width="${big ? 1.1 : 1.3}"/>`).join('');
    if (big) {
      // The distances under the walls (every k-th, at least 38 apart), the finish at the end.
      const k = Math.max(1, Math.ceil(38 / (lw * m.pool / m.D)));
      const lab = (x, t, anchor) => `<text x="${x.toFixed(1)}" y="${ly + lh + 15}" text-anchor="${anchor}" font-size="9.5" fill="#8CA0B6" font-family="Inter, sans-serif">${t}</text>`;
      g += lab(x0, '0', 'start') + walls.filter((w, i) => (i + 1) % k === 0 && X(m.D) - X(w) > 22 + 5.6 * String(m.D + m.u).length).map(w => lab(X(w), String(w), 'middle')).join('') + lab(X(m.D), m.D + m.u, 'end');
      // The seconds of the hottest spots: a turn, the start, the finish, a takeover one by one; the swimming once.
      const spots = [];
      const spread = {};
      m.zones.forEach(z => {
        if (z.key === 'swim') {
          const s = spread[z.key] || (spread[z.key] = { lost: 0, at: z, len: 0 });
          s.lost += z.lost;
          if (z.to - z.from > s.len) { s.len = z.to - z.from; s.at = z; }
        } else spots.push({ x: X((z.from + z.to) / 2), lost: z.lost, v: (z.lost / Math.max(1, z.to - z.from)) / max });
      });
      Object.keys(spread).forEach(key => { const s = spread[key]; spots.push({ x: X((s.at.from + s.at.to) / 2), lost: s.lost, v: (s.at.lost / Math.max(1, s.at.to - s.at.from)) / max }); });
      const placed = [];
      spots.filter(s => s.lost >= 0.02).sort((p0, q0) => q0.lost - p0.lost).forEach(s => {
        if (placed.length >= 5 || placed.some(q0 => Math.abs(q0.x - s.x) < 54)) return;
        placed.push(s);
      });
      g += placed.map(s => {
        const x = Math.max(x0 + 24, Math.min(X(m.D) - 24, s.x));
        return `<line x1="${s.x.toFixed(1)}" x2="${s.x.toFixed(1)}" y1="${ly - 6}" y2="${ly - 1}" stroke="${heatColor(s.v, cool)}" stroke-width="1.5"/>` +
          `<text x="${x.toFixed(1)}" y="${ly - 9}" text-anchor="middle" font-size="11" font-weight="800" fill="${s.v > 0.6 ? (cool ? '#FFFFFF' : '#FFB08A') : (cool ? '#A9D8F5' : '#E6F4F1')}" font-family="Inter, sans-serif">${S(-s.lost)} s</text>`;
      }).join('');
    }
    return `<svg class="rmap${big ? ' big' : ''}" viewBox="0 0 ${W} ${H}" xmlns="http://www.w3.org/2000/svg">${g}</svg>`;
  }

  /** The three cards under the band: the ΔΑΚ, the race against the PB (or the reference), the weakest phase (2.24.0). */
  function heroCards(x, I, R) {
    const a = x.race, b = x.pb, c = x.ref;
    const dak = a.avg.dak;
    const cmp = [[I.pbShort, b.avg.dak], ['Αναφ.', c.avg.dak]].filter(p => ok(p[1])).map(p => `${esc(p[0])} ${F(p[1])} ${chip(dak - p[1], 'lower')}`).join(' · ');
    const dakCard = ok(dak)
      ? `<div class="hc dak"><div class="hc-lab">ΔΑΚ · ο βασικός δείκτης</div><div class="hc-val">${F(dak)}</div>
        <div class="hc-sub">${cmp || 'χωρίς ΑΡ και αναφορά για σύγκριση'}</div>
        <div class="hc-x">χεριές + δευτερόλεπτα ανά 10 μ. · όσο μικρότερος τόσο καλύτερα</div></div>`
      : `<div class="hc dak"><div class="hc-lab">ΔΑΚ · ο βασικός δείκτης</div><div class="hc-val">–</div>
        <div class="hc-sub">θέλει τις χεριές από τα 15 ως τον τοίχο κάθε μήκους</div><div class="hc-x">χεριές + δευτερόλεπτα ανά 10 μ.</div></div>`;
    let second;
    if (I.has.pb) {
      second = `<div class="hc"><div class="hc-lab">${a.final < b.final ? (I.relay ? 'Νέο ρεκόρ ομάδας' : 'Νέο ατομικό ρεκόρ') : 'Από το ' + I.pbShort}</div><div class="hc-val">${S(a.final - b.final)}<span class="hc-u"> s</span></div>
        <div class="hc-sub">${T(b.final)} → ${T(a.final)}</div><div class="hc-x">${I.has.ref ? `από την αναφορά ${S(a.final - c.final)} s (${T(c.final)})` : esc(I.pbLabel)}</div></div>`;
    } else if (I.has.ref) {
      second = `<div class="hc"><div class="hc-lab">Από την αναφορά</div><div class="hc-val">${S(a.final - c.final)}<span class="hc-u"> s</span></div>
        <div class="hc-sub">${T(c.final)} → ${T(a.final)}</div><div class="hc-x">${esc(I.refLabel)}</div></div>`;
    } else {
      second = `<div class="hc"><div class="hc-lab">Μέση ταχύτητα</div><div class="hc-val">${F(I.D * a.k / a.final)}<span class="hc-u"> m/s</span></div>
        <div class="hc-sub">${T(a.final)} στα ${I.D}${I.u}</div><div class="hc-x">χωρίς ΑΡ και αναφορά στην ανάλυση</div></div>`;
    }
    let third;
    const lv = R.levels || [];
    const weak = lv.filter(l => l.pct > 0.05).sort((p0, q0) => q0.pct - p0.pct)[0];
    if (weak) {
      const len = weak.title.length + P(-weak.pct).length;
      third = `<div class="hc aim"><div class="hc-lab">Η πιο αδύνατη φάση</div><div class="hc-val hc-word${len > 18 ? ' xl' : len > 14 ? ' l' : ''}">${esc(weak.title)} <span class="hc-pct">${P(-weak.pct)}</span></div>
        <div class="hc-sub">${lv.filter(l => l !== weak).map((l, i, all) => `<span class="nw">${esc(low(l.title))} <b>${P(-l.pct)}</b>${i < all.length - 1 ? ' ·' : ''}</span>`).join(' ')}</div>
        <div class="hc-x">ταχύτητα ${lv.every(l => l.ref) ? 'απέναντι στην αναφορά' : 'απέναντι στη σύγκριση'} · σελίδα 2</div></div>`;
    } else {
      third = `<div class="hc aim"><div class="hc-lab">Κολύμβηση</div><div class="hc-val">${F(a.avg.vClean)}<span class="hc-u"> m/s</span></div>
        <div class="hc-sub">συχνότητα ${F(a.avg.sr, 1)} · μήκος κύκλου ${F(a.avg.sl)} μ.</div><div class="hc-x">καθαρή κολύμβηση (χωρίς εκκίνηση, στροφές, τερματισμό)</div></div>`;
    }
    return `<div class="heros">${dakCard}${second}${third}</div>`;
  }

  /** Who the PB and the reference are (under the indicators). */
  function compareNote(I) {
    const parts = [];
    if (I.has.pb) parts.push(`<b>${esc(I.pbShort)}</b>: ${esc(I.pbLabel || I.pbName)}`);
    if (I.has.ref) parts.push(`<b>Αναφορά</b>: ${esc(I.refLabel || 'ο αγώνας σύγκρισης')}`);
    if (!parts.length) parts.push(I.relay ? 'Χωρίς ρεκόρ ομάδας και αναφορά στην ανάλυση: οι δείκτες χωρίς σύγκριση.' : 'Χωρίς ΑΡ και αναφορά στην ανάλυση: οι δείκτες χωρίς σύγκριση.');
    return `<div class="cmpn">${parts.join(' · ')}</div>`;
  }

  function pageOverview(x, I, R, n, N) {
    const a = x.race;
    const msgs = messages(x, R);
    const strip = section(a.blocks.length <= 2 && !I.relay ? 'Ο αγώνας ανά τμήμα' : 'Ο αγώνας ανά 50άρι', a.blocks.length <= 2 && !I.relay ? 'Ο χρόνος και η ταχύτητα κάθε τμήματος.'
      : (x.ev.style === 'im' ? 'Ο χρόνος και ο ΔΑΚ κάθε 50άρι (ο ΔΑΚ συγκρίνεται μόνο μέσα στο ίδιο στυλ).' : 'Ο χρόνος και ο ΔΑΚ κάθε 50άρι· μπλε: ΔΑΚ καλύτερος από τον μέσο όρο του αγώνα, πορτοκαλί: χειρότερος.')) + `<div class="box">${raceStrip(x, I)}</div>`;
    const key = `<span class="kk"><span class="k a"></span>αγώνας${I.has.pb ? ` <span class="k p"></span>${esc(I.pbShort)}` : ''}${I.has.ref ? ' <span class="k r"></span>αναφορά' : ''}</span>`;
    const against0 = [I.has.pb ? (I.relay ? 'το ρεκόρ της ομάδας' : 'το ατομικό ρεκόρ (ΑΡ)') : '', I.has.ref ? 'την αναφορά' : ''].filter(t => t).join(' και ');
    const body = band(x, I) + heroCards(x, I, R) + section('Οι δείκτες του αγώνα', key + (against0 ? ' Κάθε δείκτης δίπλα στο ' + against0 + '· μπλε: καλύτερα, πορτοκαλί: χειρότερα.' : ' Οι δείκτες του αγώνα.')) +
      `<div class="box">${scorecard(x, I)}${compareNote(I)}</div>` + strip + section('Τα 3 βασικά μηνύματα', '') +
      `<div class="msgs">${msgs.map((m, i) => `<div class="msg"><span class="num">${i + 1}</span><div>${m}</div></div>`).join('')}</div>`;
    return page(I, '', body, n, N, true);
  }

  // ---- page 2: the phases against the reference (2.25.0: what the comparison means; until 2.24.1 recommendations)
  function pageRecs(x, I, R, n, N) {
    const a = x.race;
    const list = R.recs.slice(0, 5);
    const lv = R.levels || [];
    const against1 = !lv.length ? '' : lv.every(l => l.ref) ? 'την αναφορά' : lv.every(l => !l.ref) ? 'το ' + lv[0].name : 'τη σύγκριση';
    // The bars: the speed of each phase as a % of the one it is compared with (85% to 105%, the line: the same speed).
    const lo = 85, hi = 105, at = v => Math.max(3, Math.min(100, 100 * (v - lo) / (hi - lo)));
    const map = raceMap(R.map, true, 'rm2');
    const top = lv.length ? `<div class="aim-card map"><div class="ac-head"><div class="ac-lab">Υστέρηση ανά φάση</div>
      <div class="ac-txt">Η ταχύτητα κάθε φάσης απέναντι στ${against1.slice(1)}· η γραμμή: ίδια ταχύτητα.</div></div>
      <div class="ac-bars">${lv.map(l => `<div class="acb"><span class="acb-t">${esc(l.title)}</span><span class="acb-bar"><i class="${l.pct <= 0.05 ? 'up' : ''}" style="width:${at(100 - l.pct).toFixed(1)}%"></i><b style="left:${at(100).toFixed(1)}%"></b></span><span class="acb-v">${P(-l.pct)}${l.pct > 0.05 ? `<small> · ${F(l.lost)} s</small>` : ''}</span></div>`).join('')}</div>
      ${map ? `<div class="ac-lab ac-sub">Πού χάνεται ο χρόνος</div>${map}` : ''}</div>` : '';
    const badge = r => r.pct !== null ? `<span class="rc-g l">${P(-r.pct)} · χάνει ${F(r.lost)} s</span>`
      : r.lost !== null ? `<span class="rc-g l">χάνει ${F(r.lost)} s${r.key === 'dak' ? ' στο 2ο μισό' : ''}</span>`
        : `<span class="rc-g n">${r.key === 'rate' ? 'συχνότητα' : 'ρυθμός'}</span>`;
    const cards = list.map((r, i) => `<div class="rc">
      <div class="rc-h"><span class="rc-n">${i + 1}</span><span class="rc-t">${esc(r.title)}</span>${badge(r)}</div>
      <div class="rc-b"><div class="rc-col"><div class="rc-l">Τι δείχνουν τα νούμερα</div><ul>${r.facts.map(f => `<li>${f}</li>`).join('')}</ul></div>
        <div class="rc-col"><div class="rc-l">Τι σημαίνει</div><div class="rc-a">${r.meaning}</div></div></div>
    </div>`).join('');
    const how = lv.length ? `Με σειρά υστέρησης: πόσο % πιο αργή είναι κάθε φάση από ${against1} στην ίδια απόσταση, πόσος χρόνος χάνεται εκεί και τι σημαίνει.`
      : 'Από τα νούμερα του ίδιου του αγώνα.';
    const body = section('Τι δείχνει η σύγκριση ανά φάση', how) + top + cards;
    return page(I, 'Σύγκριση ανά φάση', body, n, N);
  }

  // ---- the conclusion (2.25.0, the analyst's request)
  /**
   * What the data showed over the whole race, for the coach to read and decide what to change with the athlete: the result against the
   * reference (else the PB), where the difference is (the start, the clean swimming, the turns, the takeovers, the finish: they make up
   * the whole race, so their differences make up the whole gap), how the race went (the halves or the legs, the swimming speed from the
   * stroke rate and the cycle length, the ΔΑΚ and the rate through the race) and the findings in the order of the time they hold. A
   * description, never what the athlete should do. Without a PB and a reference, the race against itself.
   */
  function conclusion(x, I, R) {
    const a = x.race, u = I.u, uu = I.uu;
    const cm = ok(x.ref.final) ? { m: x.ref, name: 'αναφορά', ref: true } : ok(x.pb.final) ? { m: x.pb, name: I.pbShort, ref: false } : null;
    const recOf = key => R.recs.find(r => r.key === key) || null;
    let html = '';

    // The result.
    const total = cm ? a.final - cm.m.final : NaN;
    const lines = [];
    if (I.has.ref) lines.push(`από την αναφορά <b>${S(a.final - x.ref.final)} s</b> (${T(x.ref.final)})`);
    if (I.has.pb) lines.push(`από ${I.relay ? 'το ρεκόρ της ομάδας' : 'το ΑΡ'} <b>${S(a.final - x.pb.final)} s</b> (${T(x.pb.final)})`);
    html += `<div class="cc"><div class="cc-big">${cm ? S(total) + '<small> s</small>' : T(a.final)}</div><div class="cc-txt">` +
      (cm ? `<b>${T(a.final)}</b> · ${lines.join(' · ')}.` : `<b>${T(a.final)}</b>, μέση ταχύτητα ${F(I.D * a.k / a.final)} m/s. Χωρίς ΑΡ και αναφορά: ό,τι δείχνει ο ίδιος ο αγώνας.`) +
      `</div></div>`;

    // Where the difference is: the parts of the race, each against the comparison.
    const parts = [['start', 'Εκκίνηση', `0–15${uu}`], ['swim', 'Κολύμβηση', 'χωρίς εκκίνηση, στροφές, τερματισμό'], ['turn', I.relay ? 'Στροφές' : 'Στροφές',
      '−5 ως +15 κάθε τοίχου'], ['exchange', 'Αλλαγές', '−5 ως +15 κάθε αλλαγής'], ['finish', 'Τερματισμός', `τελευταία 5${uu}`]]
      .map(([key, title, sub]) => ({ key, title, sub, mine: a.composition[key], theirs: cm ? cm.m.composition[key] : NaN }))
      .filter(p0 => ok(p0.mine) && p0.mine > 0);
    parts.forEach(p0 => { p0.gap = p0.mine - p0.theirs; p0.pct = ok(p0.gap) ? 100 * p0.gap / p0.mine : NaN; });
    const compared = parts.filter(p0 => ok(p0.gap));
    if (cm && compared.length) {
      const sumGap = compared.reduce((s0, p0) => s0 + p0.gap, 0);
      const behind = compared.filter(p0 => p0.gap > 0.005), behindSum = behind.reduce((s0, p0) => s0 + p0.gap, 0);
      const color = { start: '#123B8A', swim: '#E07A2E', turn: '#0E8A7E', exchange: '#5A8EDC', finish: '#7A4FB5' };
      const share = behindSum > 0 ? `<div class="share">${behind.map(p0 => `<i style="width:${(100 * p0.gap / behindSum).toFixed(1)}%;background:${color[p0.key]}">${100 * p0.gap / behindSum >= 18 ? esc(p0.title) + ' ' + Math.round(100 * p0.gap / behindSum) + '%' : 100 * p0.gap / behindSum >= 6 ? Math.round(100 * p0.gap / behindSum) + '%' : ''}</i>`).join('')}</div>` +
        `<div class="share-n">Το μερίδιο κάθε φάσης στον χρόνο που χάνεται${compared.length === parts.length && Math.abs(sumGap - total) < 0.02 ? '' : ' (όπου υπάρχει σύγκριση)'}.</div>` : '';
      html += section('Πού βρίσκεται η διαφορά', `Κάθε φάση απέναντι ${cm.ref ? 'στην αναφορά' : 'στο ' + cm.name}: όλες μαζί είναι όλος ο αγώνας, άρα και όλη η διαφορά.`) +
        `<div class="box"><table class="tbl"><thead><tr><th>Φάση</th><th>Αγώνας</th><th>${esc(cm.ref ? 'Αναφ.' : cm.name)}</th><th>Διαφορά</th><th>Ταχύτητα</th></tr></thead><tbody>
        ${parts.map(p0 => `<tr><td class="l">${p0.gap > 0.005 ? `<span class="dot" style="background:${color[p0.key]}"></span>` : ''}<b>${esc(p0.title)}</b> <span class="sub-i">${esc(p0.sub)}</span></td><td>${T(p0.mine)}</td><td>${T(p0.theirs)}</td><td>${chip(p0.gap, 'lower') || '–'}</td><td>${ok(p0.pct) ? chip(-p0.pct, 'higher', 1, '%').replace('.', ',') : '–'}</td></tr>`).join('')}
        <tr class="tot"><td class="l"><b>Αγώνας</b></td><td><b>${T(a.final)}</b></td><td>${T(cm.m.final)}</td><td>${chip(total, 'lower') || '–'}</td><td></td></tr>
        </tbody></table>${share}</div>`;
    }

    // How the race went.
    const told = [];
    const bl = a.blocks;
    if (I.relay && a.legs && a.legs.every(l => ok(l.split))) {
      const cl = cm && cm.m.legs && cm.m.legs.every(l => ok(l.split)) ? cm.m.legs : null;
      told.push(`<b>Τα σκέλη:</b> ${a.legs.map((l, i) => `${i + 1}ο ${T(l.split)}` + (cl ? ` (${S(l.split - cl[i].split)})` : '')).join(' · ')}` +
        (cl ? `. Τη μεγαλύτερη διαφορά από ${theC(cm)} την έχει το ${a.legs.reduce((p0, l, i) => l.split - cl[i].split > p0.d ? { i, d: l.split - cl[i].split } : p0, { i: 0, d: -Infinity }).i + 1}ο σκέλος.` : '.'));
    } else if (bl.length >= 2 && bl.every(bk => ok(bk.time))) {
      const h = Math.floor(bl.length / 2), h1 = sum(bl.slice(0, h), 'time'), h2 = sum(bl.slice(h), 'time');
      let t0 = `<b>Τα δύο μισά:</b> ${T(h1)} και ${T(h2)} (${S(h2 - h1)} s στο 2ο).`;
      const cb = cm ? cm.m.blocks : null;
      if (cb && cb.length === bl.length && cb.every(bk => ok(bk.time))) {
        const c1 = sum(cb.slice(0, h), 'time'), c2 = sum(cb.slice(h), 'time'), d1 = h1 - c1, d2 = h2 - c2;
        t0 += ` Από ${theC(cm)}: ${S(d1)} s στο 1ο μισό, ${S(d2)} s στο 2ο` + (d2 > d1 + 0.2 ? ': η διαφορά μεγαλώνει στο 2ο μισό.' : d1 > d2 + 0.2 ? ': η διαφορά είναι κυρίως στο 1ο μισό.' : ': η διαφορά μοιράζεται στα δύο μισά.');
      }
      told.push(t0);
    }
    if (cm && ok(a.avg.vClean) && ok(cm.m.avg.vClean)) {
      const sp = swimParts(a.avg, cm.m.avg, cm.ref ? 'αναφ.' : cm.name);
      told.push(`<b>Η κολύμβηση:</b> ${F(a.avg.vClean)} m/s (${cm.ref ? 'αναφ.' : cm.name} ${F(cm.m.avg.vClean)}, ${VP(a.avg.vClean, cm.m.avg.vClean)}).` + (sp ? ' ' + sp.text : '') +
        (ok(a.avg.dak) && ok(cm.m.avg.dak) ? ` ΔΑΚ ${F(a.avg.dak)} (${F(cm.m.avg.dak)}).` : ''));
    } else if (ok(a.avg.vClean)) {
      told.push(`<b>Η κολύμβηση:</b> ${F(a.avg.vClean)} m/s, συχνότητα ${F(a.avg.sr, 1)}, μήκος κύκλου ${F(a.avg.sl)} μ.` + (ok(a.avg.dak) ? `, ΔΑΚ ${F(a.avg.dak)}.` : '.'));
    }
    ['pace', 'dak', 'rate'].forEach(key => { const r = recOf(key); if (r) told.push(`<b>${esc(r.title)}:</b> ${r.meaning}`); });
    if (told.length) html += section('Πώς πήγε ο αγώνας', '') + `<div class="cn">${told.map(t0 => `<p>${t0}</p>`).join('')}</div>`;

    // The findings in the order of the time they hold, then what is as the comparison or better.
    const items = [];
    compared.filter(p0 => p0.gap > 0.05).sort((p0, q0) => q0.gap - p0.gap).forEach(p0 => {
      const r = recOf(p0.key === 'exchange' ? 'ex2' : p0.key);
      items.push(`<b>${esc(p0.title)}: ${S(p0.gap)} s</b> (${P(-p0.pct)} ταχύτητα)` + (r && r.why ? ` — ${end(esc(r.why))}` : '.'));
    });
    R.recs.filter(r => r.pct === null && ['pace', 'dak', 'rate'].includes(r.key)).forEach(r => items.push(`<b>${esc(r.title)}</b> — ${end(esc(r.why))}`));
    R.recs.filter(r => /^ex\d/.test(r.key)).forEach(r => items.push(`<b>${esc(r.title)}</b> — ${end(esc(r.why))}`));
    const fine = compared.filter(p0 => p0.gap <= 0.05);
    if (fine.length && cm) items.push(`<b>${fine.length === compared.length ? 'Όλες οι φάσεις είναι ίδιες ή καλύτερες από' : 'Ίδια ή καλύτερα από'} ${theC(cm)}:</b> ` +
      fine.map(p0 => `${low(p0.title)} ${S(p0.gap)} s`).join(' · ') + '.');
    const top = compared.filter(p0 => p0.gap > 0.05).sort((p0, q0) => q0.gap - p0.gap)[0];
    const closing = top && total > 0.05 ? `Από τη διαφορά των ${F(total)} s, τα ${F(top.gap)} s (${Math.round(100 * top.gap / total)}%) είναι ${IN_PHASE[top.key] || 'στις αλλαγές'}.` : '';
    if (items.length) html += section('Συνοπτικά', 'Με τη σειρά του χρόνου που χάνεται: τι δείχνουν τα νούμερα, για να αποφασίσει ο προπονητής τι θα αλλάξει.') +
      `<div class="msgs">${items.slice(0, 7).map((m0, i) => `<div class="msg"><span class="num">${i + 1}</span><div>${m0}</div></div>`).join('')}</div>` +
      (closing ? `<div class="cmpn concl">${closing}</div>` : '');
    return html;
  }

  function pageConclusion(x, I, R, n, N) {
    return page(I, 'Συμπέρασμα', section('Τι έδειξε ο αγώνας', 'Όλη η κούρσα με μια ματιά: από τα νούμερα των προηγούμενων σελίδων.') + conclusion(x, I, R), n, N);
  }

  // ---- page 3: ΔΑΚ
  function pageDak(x, I, n, N) {
    const a = x.race, b = x.pb, c = x.ref, bl = a.blocks;
    let body = '';
    // The example of how it is made: the first 50 with its parts.
    const ex = bl.find(bk => ok(bk.dakStrokes) && ok(bk.dakTime) && bk.dakTime > 0) || bl.find(bk => ok(bk.dak)) || bl[0];
    const div = ex.winTime / ex.dakTime;
    const exName = ex === bl[0] ? 'Στο 1ο 50άρι' : `Στο 50άρι ${ex.from}–${ex.to}${I.u}`;
    const exLine = ok(div) ? `<li>${exName}: (<b>${F(ex.winStrokes, 1)}</b> χεριές + <b>${F(ex.winTime)}</b> s) ÷ ${F(div, div % 1 ? 2 : 1)} = <b>${F(ex.dak)}</b>.</li>` : '';
    const how = `<div class="ins dakx"><div class="ins-t">Πώς βγαίνει ο ΔΑΚ</div><ul>
      <li>ΔΑΚ = (χεριές + δευτερόλεπτα από τα 15${I.u} ως τον τοίχο) ÷ μέτρα του παραθύρου ÷ 10${I.P === 25 ? ', σε κάθε μήκος του 50άρι' : ''}.</li>
      ${exLine}
      <li>Όσο μικρότερος, τόσο λιγότερες χεριές και λιγότερος χρόνος για την ίδια απόσταση: πιο αποδοτικό κολύμπι.</li></ul></div>`;
    const withWho = [I.has.pb ? 'στο ' + I.pbShort : '', I.has.ref ? 'στην αναφορά' : ''].filter(t => t).join(' και ');
    if (bl.length === 1) {
      body += section('ΔΑΚ', 'Ο βασικός δείκτης του αγώνα' + (withWho ? ', δίπλα ' + withWho : '') + '.');
      body += `<div class="tiles">${tile('ΔΑΚ', F(a.avg.dak), '', a.avg.dak - b.avg.dak, a.avg.dak - c.avg.dak, 'lower', 2, null, I)}
        ${tile('Χεριές στο παράθυρο', F(ex.winStrokes, 1), '', ex.winStrokes - b.blocks[0].winStrokes, ex.winStrokes - c.blocks[0].winStrokes, 'lower', 1, null, I)}
        ${tile('Χρόνος στο παράθυρο', F(ex.winTime), ' s', ex.winTime - b.blocks[0].winTime, ex.winTime - c.blocks[0].winTime, 'lower', 2, null, I)}
        ${tile('Ταχύτητα κολύμβησης', F(a.avg.vClean), ' m/s', a.avg.vClean - b.avg.vClean, a.avg.vClean - c.avg.vClean, 'higher', 2, null, I)}</div>`;
      const who3 = [[a, 'Αυτός ο αγώνας'], [b, I.pbName], [c, I.refName]].filter((p, i) => i === 0 || ok(p[0].blocks[0] && p[0].blocks[0].dakStrokes));
      const parts3 = ch.vStacked({ w: W - 26, h: 260, cats: who3.map(p => p[1]), totalFmt: v => F(v),
        parts: [{ name: 'Χεριές ανά 10 μ.', color: C.p1, values: who3.map(p => p[0].blocks[0].dakStrokes) }, { name: 'Δευτερόλεπτα ανά 10 μ.', color: '#F2A65A', values: who3.map(p => p[0].blocks[0].dakTime) }] });
      if (parts3) {
        body += section('Από τι αποτελείται', 'Το μέρος των χεριών και το μέρος του χρόνου' + (who3.length > 1 ? ', δίπλα ' + withWho : '') + '.');
        body += `<div class="box">${parts3}</div>`;
      }
      body += how;
    } else {
      const h = Math.floor(bl.length / 2);
      const known = bl.filter(bk => ok(bk.dak));
      const best = known.reduce((p, q) => q.dak < p.dak ? q : p), worst = known.reduce((p, q) => q.dak > p.dak ? q : p);
      const rise = bl.every(bk => ok(bk.dak)) ? avg(bl.slice(h), 'dak') - avg(bl.slice(0, h), 'dak') : NaN;
      const mixedStyles = x.ev.style === 'im';
      const byStyle = mixedStyles ? ['fly', 'bk', 'br', 'fs'].map(st => ({ st, a: bl.filter(z => z.style === st), b: b.blocks.filter(z => z.style === st), c: c.blocks.filter(z => z.style === st) }))
        .filter(g => g.a.length).sort((p, q) => bl.indexOf(p.a[0]) - bl.indexOf(q.a[0])) : null;
      body += section('ΔΑΚ: ο βασικός δείκτης', 'Ο ΔΑΚ κάθε 50άρι' + (withWho ? ' δίπλα ' + withWho : '') + (mixedStyles ? '· σε κάθε στυλ συγκρίνεται με το ίδιο στυλ.' : '.'));
      if (mixedStyles) body += `<div class="tiles">${byStyle.map(g => tile('ΔΑΚ · ' + STYLE_NAME[g.st], F(avg(g.a, 'dak')), '', avg(g.a, 'dak') - avg(g.b, 'dak'), avg(g.a, 'dak') - avg(g.c, 'dak'), 'lower', 2, null, I)).join('')}</div>`;
      else body += `<div class="tiles">${tile('ΔΑΚ μ.ο.', F(a.avg.dak), '', a.avg.dak - b.avg.dak, a.avg.dak - c.avg.dak, 'lower', 2, null, I)}
        ${tile('Καλύτερο 50άρι', F(best.dak), '', null, null, 'none', 2, `${best.from}–${best.to}${I.u}`, I)}
        ${tile('Χειρότερο 50άρι', F(worst.dak), '', null, null, 'none', 2, `${worst.from}–${worst.to}${I.u}`, I)}
        ${tile('2ο μισό − 1ο', S(rise), '', null, null, 'none', 2, !ok(rise) ? 'λείπει ΔΑΚ κάποιου 50άρι' : rise >= 0.3 ? 'ανεβαίνει στο 2ο μισό' : 'σταθερός', I)}</div>`;
      const cats = bl.map(bk => String(bk.to));
      const mid = bl.length > 4 && bl.length <= 8;
      body += `<div class="box">${ch.lineChart({ w: W - 26, h: bl.length > 16 ? 215 : mid ? 196 : 230, cats, fmt: v => v.toFixed(2), yTitle: 'ΔΑΚ', xTitle: '50άρι (' + I.uu.trim() + ')', every: bl.length > 16 ? 2 : 1,
        series: three(x, I, bk => bk.dak, r => r.blocks), vlines: legLines(x, cats) })}</div>`;
      const partsBl = ch.vStacked({ w: W - 26, h: bl.length > 16 ? 200 : mid ? 178 : 215, cats, every: bl.length > 16 ? 2 : 1, totalFmt: v => F(v),
        parts: [{ name: 'Χεριές ανά 10 μ.', color: C.p1, values: bl.map(bk => bk.dakStrokes) }, { name: 'Δευτερόλεπτα ανά 10 μ.', color: '#F2A65A', values: bl.map(bk => bk.dakTime) }] });
      if (partsBl) body += section('Από τι αποτελείται ο ΔΑΚ', 'Σε κάθε 50άρι: χεριές ανά 10 μ. και δευτερόλεπτα ανά 10 μ.· έτσι φαίνεται αν ανεβαίνει από τις χεριές ή από τον χρόνο.') + `<div class="box">${partsBl}</div>`;
      const first = bl.slice(0, h), second = bl.slice(h);
      const rS = avg(second, 'dakStrokes') - avg(first, 'dakStrokes'), rT = avg(second, 'dakTime') - avg(first, 'dakTime');
      const styleGaps = mixedStyles ? byStyle.map(g => ({ g, d: avg(g.a, 'dak') - avg(g.c, 'dak') })).filter(p => ok(p.d)) : [];
      const worstStyle = styleGaps.length ? styleGaps.sort((p, q) => q.d - p.d)[0].g : null;
      const lines = [];
      if (mixedStyles) {
        if (worstStyle) lines.push(`<li>Η μεγαλύτερη διαφορά από την αναφορά στο <b>${esc(STYLE_NAME[worstStyle.st].toLowerCase())}</b>: ${F(avg(worstStyle.a, 'dak'))} έναντι ${F(avg(worstStyle.c, 'dak'))} (${S(avg(worstStyle.a, 'dak') - avg(worstStyle.c, 'dak'))}).</li>`);
        lines.push(`<li>${byStyle.map(g => `${esc(STYLE_NAME[g.st])} ${F(avg(g.a, 'dak'))}`).join(' · ')}: κάθε στυλ έχει τη δική του κλίμακα ΔΑΚ (π.χ. το πρόσθιο έχει λιγότερες χεριές).</li>`);
      } else {
        if (ok(rise)) lines.push(`<li>1ο μισό <b>${F(avg(first, 'dak'))}</b> → 2ο μισό <b>${F(avg(second, 'dak'))}</b> (${S(rise)})` + (ok(rS) && ok(rT) ? `: από τις χεριές ${S(rS)}, από τον χρόνο ${S(rT)} ανά 10 μ.` : '') + '</li>');
        if (known.length >= 2) lines.push(`<li>Το καλύτερο 50άρι στα ${best.from}–${best.to}${I.u} (${F(best.dak)}), το χειρότερο στα ${worst.from}–${worst.to}${I.u} (${F(worst.dak)}).</li>`);
        if (known.length < bl.length) lines.push(`<li>Χωρίς ΔΑΚ τα 50άρια όπου λείπουν χεριές ή χρόνοι (${bl.filter(bk => !ok(bk.dak)).map(bk => bk.from + '–' + bk.to).join(', ')}).</li>`);
      }
      if (ok(div)) lines.push(`<li>ΔΑΚ = (χεριές + δευτερόλεπτα από τα 15${I.u} ως τον τοίχο) ÷ μέτρα/10· ${exName.toLowerCase().replace('στο ', 'στο ')} (${F(ex.winStrokes, 1)} + ${F(ex.winTime)}) ÷ ${F(div, div % 1 ? 2 : 1)} = ${F(ex.dak)}.</li>`);
      body += `<div class="ins"><div class="ins-t">Τι λέει ο ΔΑΚ</div><ul>${lines.join('')}</ul></div>`;
      if (bl.length <= 8) body += `<div class="box">${dakTable(x, I)}</div>`;
    }
    return page(I, 'ΔΑΚ', body, n, N);
  }

  function dakTable(x, I) {
    const a = x.race, b = x.pb, c = x.ref, bl = a.blocks;
    const step = bl.length > 16 ? 2 : 1;
    const rows = [];
    for (let i = 0; i < bl.length; i += step) {
      const g = bl.slice(i, i + step), gb = b.blocks.slice(i, i + step), gc = c.blocks.slice(i, i + step);
      rows.push({ from: g[0].from, to: g[g.length - 1].to, dak: avg(g, 'dak'), dPb: avg(g, 'dak') - avg(gb, 'dak'), dRef: avg(g, 'dak') - avg(gc, 'dak'),
        strokes: sum(g, 'winStrokes'), time: sum(g, 'winTime'), v: avg(g, 'vClean'), sr: avg(g, 'sr'), sl: avg(g, 'sl'), style: g[0].style });
    }
    return `<table class="tbl${rows.length > 10 ? ' sm' : ''}"><thead><tr><th>${step > 1 ? '100άρι' : '50άρι'}</th>${I.im || (I.relay && x.ev.style === 'im') ? '<th>Στυλ</th>' : ''}<th>ΔΑΚ</th><th>Δ ${esc(I.pbShort)}</th><th>Δ Αναφ.</th><th>Χεριές<br>παραθύρου</th><th>Χρόνος<br>παραθύρου</th><th>Ταχύτητα<br>m/s</th><th>Συχνότητα</th><th>Μήκος<br>κύκλου</th></tr></thead><tbody>
      ${rows.map(r => `<tr><td class="l">${r.from}–${r.to}</td>${I.im || (I.relay && x.ev.style === 'im') ? `<td class="l">${esc(STYLE_NAME[r.style])}</td>` : ''}<td class="dk"><b>${F(r.dak)}</b></td><td>${chip(r.dPb, 'lower')}</td><td>${chip(r.dRef, 'lower')}</td>
        <td>${F(r.strokes, 1)}</td><td>${F(r.time)}</td><td>${F(r.v)}</td><td>${F(r.sr, 1)}</td><td>${F(r.sl)}</td></tr>`).join('')}
      <tr class="tot"><td class="l">Μ.ο.</td>${I.im || (I.relay && x.ev.style === 'im') ? '<td></td>' : ''}<td class="dk"><b>${F(a.avg.dak)}</b></td><td>${chip(a.avg.dak - b.avg.dak, 'lower')}</td><td>${chip(a.avg.dak - c.avg.dak, 'lower')}</td><td></td><td></td><td>${F(a.avg.vClean)}</td><td>${F(a.avg.sr, 1)}</td><td>${F(a.avg.sl)}</td></tr>
    </tbody></table>`;
  }

  // ---- composition and phases
  function groups(x) {
    const a = x.race, ev = x.ev, D = ev.distance, P = ev.pool;
    const unit = ev.relay ? D / 4 : D <= 200 ? 50 * (P === 25 && D >= 200 ? 2 : 1) : D <= 500 ? 100 : D <= 1000 ? 200 : 500;
    const out = [];
    const add = (label, pick) => {
      // A phase of a race is the sum of its parts: none missing (NaN otherwise, never a part as the whole).
      const t = r => { const l = r.phases.filter(pick); return l.length && l.every(p => ok(p.t)) ? l.reduce((s0, p) => s0 + p.t, 0) : NaN; };
      out.push({ label, a: t(x.race), pb: t(x.pb), ref: t(x.ref) });
    };
    const u = ev.unit === 'yd' ? ' yd' : ' μ.';
    add('Εκκίνηση 0–15' + u, p => p.kind === 'start');
    for (let s0 = 0; s0 < D; s0 += unit) {
      const e = s0 + unit;
      const lab = ev.relay ? `Σκέλος ${s0 / unit + 1}` : `${s0}–${e}${u}`;
      const swims = a.phases.filter(p => p.kind === 'swim' && p.from >= s0 && p.to <= e + 1e-9);
      const swimLab = ev.relay ? `Κολύμβηση · ${lab}` : swims.length === 1 ? `Κολύμβηση ${swims[0].from}–${swims[0].to}${u}` : `Κολύμβηση στα ${s0}–${e}${u}`;
      add(swimLab, p => p.kind === 'swim' && p.from >= s0 && p.to <= e + 1e-9);
      const walls = a.turns.filter(t => t.kind !== 'exchange' && t.wall > s0 && t.wall < e).length;
      if (walls) add(`${walls > 1 ? 'Στροφές' : 'Στροφή'} · ${lab}`, p => p.kind === 'turn' && p.wall > s0 && p.wall < e);
      if (ev.relay && e < D) add(`Αλλαγή ${s0 / unit + 1} → ${s0 / unit + 2}`, p => p.kind === 'exchange' && p.wall === e);
      if (!ev.relay && unit <= 50 && e < D && P === 50) add(`Στροφή ${e}${u}`, p => p.kind === 'turn' && p.wall === e);
    }
    add('Τερματισμός 5' + u, p => p.kind === 'finish');
    return out.filter(g => g.a > 0);
  }

  function compositionChart(x, I) {
    const parts = cmp => [
      { name: 'Εκκίνηση', value: cmp.start, color: C.p1 }, { name: 'Κολύμβηση', value: cmp.swim, color: C.p2 },
      { name: 'Στροφές', value: cmp.turn, color: C.p3 }, { name: 'Αλλαγές', value: cmp.exchange, color: '#8E5BB0' }, { name: 'Τερματισμός', value: cmp.finish, color: C.p4 }
    ].filter(p => p.value > 0);
    // A race whose phases are not all there is not drawn (its bar would not add up to its time).
    const whole = r => ['start', 'swim', 'turn', 'exchange', 'finish'].every(k => ok(r.composition[k]));
    const rows = [[x.race, 'Αυτός ο αγώνας'], [x.pb, I.pbName], [x.ref, I.refName]].filter(p => whole(p[0]) && ok(p[0].final))
      .map(p => ({ label: p[1], parts: parts(p[0].composition), total: p[0].final }));
    const chart = ch.stacked({ w: W - 26, fmtT: v => T(v) + ' s', rows });
    if (!chart) return '';
    const lg = parts(x.race.composition).map(p => `<span class="lgi"><i style="background:${p.color}"></i>${esc(p.name)}</span>`).join('');
    return chart + `<div class="lgrow">${lg}</div>`;
  }

  /** What the phases are compared with in the bars: the PB, else the reference (null: nothing). */
  function phaseBase(I) {
    return I.has.pb ? { key: 'pb', name: I.pbShort, other: I.has.ref ? 'ref' : null, otherName: 'Αναφ.' }
      : I.has.ref ? { key: 'ref', name: 'την αναφορά', other: null } : null;
  }

  function phaseBars(x, I, gs, rowH) {
    const pb = phaseBase(I);
    if (!pb) return '';
    const items = gs.map(g => ({ label: g.label, value: g.a - g[pb.key], note: pb.other ? `${pb.otherName} ${S(g.a - g[pb.other])}` : '' }));
    const text = pb.key === 'pb' ? 'Διαφορά κάθε φάσης από το ' + I.pbShort + ' (μπάρες)' + (pb.other ? ' και από την αναφορά (δεξιά).' : '.') : 'Διαφορά κάθε φάσης από την αναφορά.';
    return section('Πού κερδήθηκε και πού χάθηκε χρόνος', text) +
      `<div class="box tight">${ch.hDiverging({ w: W - 26, items, labelW: 190, noteW: pb.other ? 100 : 0, rowH })}</div>`;
  }

  function phaseTable(x, I) {
    const gs = groups(x);
    const best = I.has.pb ? sum(gs, g => Math.min(g.a, g.pb)) : NaN;
    return `<table class="tbl${gs.length > 10 ? ' sm' : ''}"><thead><tr><th>Φάση</th><th>Αγώνας</th><th>${esc(I.pbShort)}</th><th>Αναφορά</th><th>Δ ${esc(I.pbShort)}</th><th>Δ Αναφ.</th><th>Καλύτερη</th></tr></thead><tbody>
      ${gs.map(g => `<tr><td class="l">${esc(g.label)}</td><td><b>${T(g.a)}</b></td><td>${T(g.pb)}</td><td>${T(g.ref)}</td><td>${chip(g.a - g.pb, 'lower') || '–'}</td><td>${chip(g.a - g.ref, 'lower') || '–'}</td><td>${T(ok(g.pb) ? Math.min(g.a, g.pb) : NaN)}</td></tr>`).join('')}
      <tr class="tot"><td class="l">Σύνολο</td><td><b>${T(x.race.final)}</b></td><td>${T(x.pb.final)}</td><td>${T(x.ref.final)}</td><td>${chip(x.race.final - x.pb.final, 'lower') || '–'}</td><td>${chip(x.race.final - x.ref.final, 'lower') || '–'}</td><td><b>${T(best)}</b></td></tr>
    </tbody></table>`;
  }

  function pageComposition(x, I, n, N) {
    const gs = groups(x);
    const best = I.has.pb ? sum(gs, g => Math.min(g.a, g.pb)) : NaN;
    let body = '';
    const comp = compositionChart(x, I);
    if (comp) body += section('Σύνθεση του χρόνου', 'Πού πήγε ο χρόνος: εκκίνηση (0–15), καθαρή κολύμβηση, στροφές (−5/+15)' + (I.relay ? ', αλλαγές' : '') + ' και τερματισμός (τελευταία 5).') + `<div class="box">${comp}</div>`;
    body += phaseBars(x, I, gs, gs.length > 10 ? 20.5 : 25);
    body += section('Σύγκριση ανά φάση', '');
    body += `<div class="box">${phaseTable(x, I)}</div>`;
    if (ok(best) && ok(x.race.final)) body += `<div class="ins"><div class="ins-t">Ο χρόνος με τις καλύτερες φάσεις</div><ul><li>${best < x.race.final - 0.005
      ? `Με την καλύτερη κάθε φάσης από αυτόν τον αγώνα και το ${I.pbShort}: <b>${T(best)}</b> (${S(best - x.race.final)} s), ο χρόνος που ${I.relay ? 'η ομάδα έχει' : 'ο αθλητής έχει'} ήδη δείξει ότι μπορεί.`
      : `Σε κάθε φάση αυτός ο αγώνας ήταν καλύτερος από το ${I.pbShort}` + (I.has.ref ? '· το επόμενο βήμα είναι οι φάσεις με τη μεγαλύτερη διαφορά από την αναφορά.' : '.')}</li></ul></div>`;
    return page(I, 'Σύνθεση και φάσεις', body, n, N);
  }

  // ---- speed (50 / 100)
  function pageSpeed(x, I, n, N) {
    const a = x.race, b = x.pb, c = x.ref, D = I.D;
    const cats = a.rows.map(r => `${r.prevD}–${r.d}`);
    let body = section('Καμπύλη ταχύτητας', 'Μέση ταχύτητα κάθε τμήματος· τα τμήματα εκκίνησης, στροφής και τερματισμού περιέχουν πτήση, υποβρύχια φάση ή γλίστρημα.');
    body += `<div class="box">${ch.lineChart({ w: W - 26, h: 240, cats, fmt: v => v.toFixed(2), yTitle: 'm/s', xTitle: 'τμήμα (' + I.uu.trim() + ')', every: cats.length > 12 ? 2 : 1,
      series: three(x, I, r => r.vel, r => r.rows) })}</div>`;
    const free = a.rows.filter(r => r.isFree && ok(r.vel));
    const vmax = free.length ? free.reduce((p, q) => q.vel > p.vel ? q : p) : null, vlast = free.length ? free[free.length - 1] : null;
    const drop = vmax && vlast && vmax !== vlast ? tile('Πτώση ταχύτητας', ((vmax.vel - vlast.vel) / vmax.vel * 100).toFixed(1), '%', null, null, 'none', 1,
      D === 100 ? `${vmax.prevD}–${vmax.d} → ${vlast.prevD}–${vlast.d}${I.u}` : 'από το πιο γρήγορο τμήμα στο τελευταίο', I) : '';
    if (D === 100 && a.blocks.length >= 2) {
      const h1 = a.blocks[0].time, h2 = a.blocks[1].time;
      const bb = r => r.blocks.length >= 2 ? [r.blocks[0].time, r.blocks[1].time] : [NaN, NaN];
      const [b1, b2] = bb(b), [c1, c2] = bb(c);
      body += `<div class="tiles">${tile('1ο 50άρι', T(h1), ' s', h1 - b1, h1 - c1, 'lower', 2, null, I)}
        ${tile('2ο 50άρι', T(h2), ' s', h2 - b2, h2 - c2, 'lower', 2, null, I)}
        ${tile('Διαφορά 2ου − 1ου', S(h2 - h1), ' s', (h2 - h1) - (b2 - b1), (h2 - h1) - (c2 - c1), 'lower', 2, null, I)}
        ${drop}</div>`;
    } else if (vmax) {
      const at = (r, d) => r.byD[d] ? r.byD[d].vel : NaN;
      body += `<div class="tiles">${tile('Μέγιστη ταχύτητα', F(vmax.vel), ' m/s', vmax.vel - at(b, vmax.d), vmax.vel - at(c, vmax.d), 'higher', 2, `${vmax.prevD}–${vmax.d}${I.u}`, I)}
        ${vlast !== vmax ? tile('Τελευταίο τμήμα', F(vlast.vel), ' m/s', vlast.vel - at(b, vlast.d), vlast.vel - at(c, vlast.d), 'higher', 2, `${vlast.prevD}–${vlast.d}${I.u}`, I) : ''}
        ${drop}
        ${tile('Τελευταία 5' + I.u, F(a.finish.vLast5), ' m/s', a.finish.vLast5 - b.finish.vLast5, a.finish.vLast5 - c.finish.vLast5, 'higher', 2, null, I)}</div>`;
    }
    const comp = compositionChart(x, I);
    if (comp) body += section('Σύνθεση του χρόνου', 'Εκκίνηση (0–15), καθαρή κολύμβηση, στροφές (−5/+15) και τερματισμός (τελευταία 5).') + `<div class="box">${comp}</div>`;
    body += phaseBars(x, I, groups(x), 25);
    return page(I, D === 100 ? 'Ρυθμός, ταχύτητα, σύνθεση' : 'Ταχύτητα και σύνθεση', body, n, N);
  }

  // ---- pace (200 and longer, relays)
  function pagePace(x, I, R, n, N) {
    const a = x.race, b = x.pb, c = x.ref, D = I.D, bl = a.blocks;
    const cats = bl.map(bk => String(bk.to));
    const mixed = I.im;
    const withWho = [I.has.pb ? 'στο ' + I.pbShort : '', I.has.ref ? 'στην αναφορά' : ''].filter(t => t).join(' και ');
    let body = section('Ρυθμός ανά 50άρι', mixed ? 'Ο χρόνος κάθε 50άρι' + (withWho ? ', δίπλα ' + withWho : '') + '.' : `Ο χρόνος κάθε 50άρι· η διακεκομμένη γραμμή είναι ο μέσος όρος από το 2ο 50άρι.`);
    const mean = avg(bl.slice(1), 'time');
    body += `<div class="box">${ch.lineChart({ w: W - 26, h: 240, cats, fmt: v => T(v), yTitle: 's / 50', xTitle: '50άρι (' + I.uu.trim() + ')', every: bl.length > 16 ? 2 : 1,
      refLine: mixed || I.relay || !ok(mean) ? null : { value: mean, label: 'μ.ο.' }, series: three(x, I, bk => bk.time, r => r.blocks), vlines: legLines(x, cats) })}</div>`;
    // The medley and the relays against the PB (or the reference): their 50s are not alike; the others against their own mean.
    const vsKey = I.has.pb ? 'pb' : I.has.ref ? 'ref' : null;
    const vsRace = vsKey ? x[vsKey] : null;
    const dev = mixed || I.relay ? (vsRace ? bl.map((bk, i) => bk.time - vsRace.blocks[i].time) : null) : bl.map((bk, i) => i === 0 ? NaN : bk.time - mean);
    const devChart = dev ? ch.vDiverging({ w: W - 26, h: 150, cats, values: dev, labels: bl.length <= 10, every: bl.length > 16 ? 2 : 1, yTitle: 's' }) : '';
    if (devChart) {
      body += section(mixed || I.relay ? 'Διαφορά από ' + (vsKey === 'pb' ? 'το ' + I.pbShort : 'την αναφορά') + ' ανά 50άρι' : 'Απόκλιση από τον μέσο όρο', 'Κάτω από το μηδέν πιο γρήγορα (μπλε), πάνω πιο αργά (πορτοκαλί).');
      body += `<div class="box">${devChart}</div>`;
    }
    if (!mixed && !I.relay) {
      const nb = bl.length, hh = Math.floor(nb / 2), h1 = sum(bl.slice(0, hh), 'time'), h2 = sum(bl.slice(hh), 'time');
      const timed = bl.slice(1).filter(bk => ok(bk.time));
      const sd = Math.sqrt(avg(timed, bk => (bk.time - mean) * (bk.time - mean)));
      const fast = timed.length ? timed.reduce((p, q) => q.time < p.time ? q : p) : null, slow = timed.length ? timed.reduce((p, q) => q.time > p.time ? q : p) : null;
      const rel = (h2 - h1) / (h1 + h2);
      const type = !ok(rel) ? '–' : rel < -0.002 ? 'Αρνητικός' : rel < 0.012 ? (bl[nb - 1].time < mean - 0.3 ? 'Σταθερός, δυνατό τέλος' : 'Σταθερός') : 'Θετικός';
      const last2 = sum(bl.slice(-2), 'time');
      body += `<div class="tiles">${tile('Ρυθμός αγώνα', `<span class="t-word">${type}</span>`, '', null, null, 'none', 2, ok(h2 - h1) ? `2ο μισό ${S(h2 - h1)} s` : 'λείπει χρόνος 50άρι', I)}
        ${tile('Διακύμανση 50άρι', ok(sd / mean) ? (100 * sd / mean).toFixed(1) : '–', '%', null, null, 'none', 1, `τυπική απόκλιση ${F(sd)} s`, I)}
        ${fast ? tile('Πιο γρήγορο / αργό', `${T(fast.time)}<span class="t-u"> / </span>${T(slow.time)}`, '', null, null, 'none', 2, `στα ${fast.to} και ${slow.to}${I.u}`, I) : ''}
        ${tile('Τελευταίο 100άρι', T(last2), ' s', last2 - sum(b.blocks.slice(-2), 'time'), last2 - sum(c.blocks.slice(-2), 'time'), 'lower', 2, null, I)}</div>`;
    }
    if (R.splits && !I.relay) {
      const k = bl.length > 16 ? 2 : 1;
      const rows = [];
      for (let i = 0; i < bl.length; i += k) rows.push({ from: bl[i].from, to: bl[Math.min(bl.length, i + k) - 1].to, a: sum(bl.slice(i, i + k), 'time'), t: R.splits.slice(i, i + k).reduce((p, q) => p + q, 0) });
      const per = Math.ceil(rows.length / 2);
      const dmax = rows.reduce((m0, r) => Math.max(m0, Math.abs(r.t - r.a)), 0);
      const dbar = dd => dmax > 0 ? `<span class="db"><i class="${dd < 0 ? 'f' : 's'}" style="width:${Math.round(15 * Math.min(1, Math.abs(dd) / dmax))}px"></i></span>` : '';
      const table = list => `<table class="tbl${rows.length > 16 ? ' sm' : ''}"><thead><tr><th>${k > 1 ? '100άρι' : '50άρι'}</th><th>Αγώνας</th><th>Κατανομή</th><th>Διαφορά</th></tr></thead><tbody>
        ${list.map(r => `<tr><td class="l">${r.from}–${r.to}</td><td>${T(r.a)}</td><td class="aimv"><b>${T(r.t)}</b></td><td class="dv">${dbar(r.t - r.a)}${chip(r.t - r.a, 'lower')}</td></tr>`).join('')}</tbody></table>`;
      // (2.25.0) What the distribution of the comparison would make of today's time, described (until 2.24.1 «καλύτερη κατανομή»).
      const byRef = R.splitsFrom === 'αναφορά';
      body += section('Ο ίδιος χρόνος με την κατανομή ' + (byRef ? 'της αναφοράς' : 'του ' + I.pbShort), `Ο σημερινός χρόνος (${T(a.final)}) μοιρασμένος στα 50άρια όπως τον μοιράζει ` +
        (byRef ? 'η αναφορά' : 'το ' + I.pbShort) + ': μπλε όπου ' + (byRef ? 'η αναφορά πηγαίνει' : 'το ' + I.pbShort + ' πηγαίνει') + ' αναλογικά πιο γρήγορα, πορτοκαλί όπου πιο συγκρατημένα.');
      body += `<div class="two"><div class="box">${table(rows.slice(0, per))}</div><div class="box">${table(rows.slice(per))}</div></div>`;
    } else {
      const per100 = [];
      const step = I.relay || I.im ? I.D / 4 : 100;
      for (let s = 0; s < D; s += step) {
        const e = s + step, t = r => tOf(r, e) - tOf(r, s);
        per100.push({ s, e, a: t(a), pb: t(b), ref: t(c), lab: I.relay ? `Σκέλος ${s / step + 1}` : I.im ? STYLE_NAME[IM_ORDER[s / step]] : '' });
      }
      body += section(I.relay ? 'Χρόνοι ανά σκέλος' : I.im ? 'Χρόνοι ανά στυλ' : 'Χρόνοι ανά 100', '');
      const head0 = I.relay ? 'Σκέλος' : I.im ? 'Στυλ' : '100άρι';
      if (per100.length <= 8) {
        body += `<div class="box"><table class="tbl"><thead><tr><th>${head0}</th><th>Χρόνος</th><th>Σωρευτικός</th><th>${esc(I.pbShort)}</th><th>Δ ${esc(I.pbShort)}</th><th>Αναφ.</th><th>Δ Αναφ.</th></tr></thead><tbody>
          ${per100.map(p => `<tr><td class="l">${p.s}–${p.e}${p.lab ? ' · ' + esc(p.lab) : ''}</td><td><b>${T(p.a)}</b></td><td>${T(tOf(a, p.e))}</td><td>${T(p.pb)}</td><td>${chip(p.a - p.pb, 'lower') || '–'}</td><td>${T(p.ref)}</td><td>${chip(p.a - p.ref, 'lower') || '–'}</td></tr>`).join('')}
          </tbody></table></div>`;
      } else {
        // The long races: two tables side by side, the differences only.
        const half = Math.ceil(per100.length / 2);
        const table = list => `<table class="tbl sm"><thead><tr><th>${head0}</th><th>Χρόνος</th><th>Σωρευτικός</th><th>Δ ${esc(I.pbShort)}</th><th>Δ Αναφ.</th></tr></thead><tbody>
          ${list.map(p => `<tr><td class="l">${p.s}–${p.e}</td><td><b>${T(p.a)}</b></td><td>${T(tOf(a, p.e))}</td><td>${chip(p.a - p.pb, 'lower') || '–'}</td><td>${chip(p.a - p.ref, 'lower') || '–'}</td></tr>`).join('')}</tbody></table>`;
        body += `<div class="two"><div class="box">${table(per100.slice(0, half))}</div><div class="box">${table(per100.slice(half))}</div></div>`;
      }
    }
    return page(I, 'Ρυθμός αγώνα', body, n, N);
  }

  // ---- strokes
  function pageStrokes(x, I, n, N) {
    const a = x.race, b = x.pb, c = x.ref, P = I.P, lengths = I.D / P;
    const pattern = x.race.pattern || [];
    let body = '';
    const charts = (cats, get, every, vl, h) => ['vel', 'sr', 'sl'].map((f, i) => `<div class="box">${ch.lineChart({ w: W - 26, h: h || 175, cats, every,
      fmt: v => f === 'sr' ? v.toFixed(1) : v.toFixed(2), yTitle: ['Ταχύτητα (m/s)', 'Συχνότητα (κύκλοι/λεπτό)', 'Μήκος κύκλου (μ.)'][i], series: get(f), vlines: vl })}</div>`).join('');
    if (P === 50 && pattern.length === 4) {
      // 15 · 25 · 45 · 50: the 1st and the 2nd 25 of every 50.
      const cats = a.blocks.map(bk => String(bk.to));
      const pair = f => [{ name: '1ο 25άρι (15–25)', color: C.first25, values: a.blocks.map(bk => bk.first25 ? bk.first25[f] : null) },
        { name: '2ο 25άρι (25–45)', color: C.second25, marker: 'square', values: a.blocks.map(bk => bk.second25 ? bk.second25[f] : null) }];
      body += section('Χεριές ανά 25άρι', 'Σε κάθε 50άρι: 1ο 25άρι = 15→25, 2ο 25άρι = 25→45 (καθαρή κολύμβηση). Ταχύτητα = συχνότητα × μήκος κύκλου.');
      body += charts(cats, pair, 1, legLines(x, cats), 165);
      body += section('Ανά 50άρι: 1ο και 2ο 25άρι', '');
      body += `<div class="box"><table class="tbl sm"><thead><tr><th rowspan="2">50άρι</th><th rowspan="2">Χρόνος</th><th colspan="4" class="g1">1ο 25άρι · 15→25</th><th colspan="4" class="g2">2ο 25άρι · 25→45</th><th rowspan="2">ΔΑΚ</th></tr>
        <tr><th class="g1">m/s</th><th class="g1">κύκλ./λ.</th><th class="g1">tempo</th><th class="g1">μήκος</th><th class="g2">m/s</th><th class="g2">κύκλ./λ.</th><th class="g2">tempo</th><th class="g2">μήκος</th></tr></thead><tbody>
        ${a.blocks.map(bk => { const f = bk.first25 || {}, s = bk.second25 || {}; return `<tr><td class="l">${bk.from}–${bk.to}${I.im || I.relay ? ' · ' + esc(STYLE_NAME[bk.style]) : ''}</td><td><b>${T(bk.time)}</b></td>
          <td>${F(f.vel)}</td><td>${F(f.sr, 1)}</td><td>${F(f.tempo)}</td><td>${F(f.sl)}</td><td>${F(s.vel)}</td><td>${F(s.sr, 1)}</td><td>${F(s.tempo)}</td><td>${F(s.sl)}</td><td class="dk"><b>${F(bk.dak)}</b></td></tr>`; }).join('')}
        </tbody></table></div>`;
    } else if ((P === 50 && pattern.length === 5) || (P === 25 && lengths <= 16)) {
      const free = r => r.rows.filter(q => q.isFree);
      const cats = free(a).map(r => `${r.prevD}–${r.d}`);
      body += section('Ταχύτητα, συχνότητα και μήκος κύκλου', 'Στην καθαρή κολύμβηση (15 μετά τον τοίχο ως 5 πριν τον επόμενο)' + (P === 25 ? ', σε κάθε μήκος' : '') + ': ταχύτητα = συχνότητα × μήκος κύκλου.');
      body += charts(cats, f => three(x, I, r => r[f], free), cats.length > 12 ? 2 : 1, legLines(x, cats), cats.length > 12 ? 146 : cats.length > 8 ? 160 : 170);
      body += section('Οι χεριές ανά τμήμα', '');
      body += `<div class="box"><table class="tbl${cats.length > 10 ? ' sm' : ''}"><thead><tr><th>Τμήμα</th>${I.im || I.relay ? '<th>Στυλ</th>' : ''}<th>Χρόνος</th><th>Ταχύτητα<br>m/s</th><th>Χεριές</th><th>Συχνότητα</th><th>Tempo<br>s/κύκλο</th><th>Μήκος<br>κύκλου</th><th>SI<br>m²/s</th><th>Δ ταχ.<br>${esc(I.pbShort)}</th></tr></thead><tbody>
        ${free(a).map(r => `<tr><td class="l">${r.prevD}–${r.d}</td>${I.im || I.relay ? `<td class="l">${esc(STYLE_NAME[r.style])}</td>` : ''}<td>${F(r.seg)}</td><td><b>${F(r.vel)}</b></td><td>${F(r.strokes, 1)}</td><td>${F(r.sr, 1)}</td><td>${F(r.tempo)}</td><td>${F(r.sl)}</td><td>${F(r.si)}</td><td>${chip(r.vel - (b.byD[r.d] ? b.byD[r.d].vel : NaN), 'higher') || '–'}</td></tr>`).join('')}
        </tbody></table></div>`;
    } else {
      const cats = a.blocks.map(bk => String(bk.to));
      body += section('Χεριές ανά 50άρι', 'Στην καθαρή κολύμβηση κάθε 50άρι. Ταχύτητα = συχνότητα × μήκος κύκλου.');
      body += charts(cats, f => three(x, I, bk => bk[f === 'vel' ? 'vClean' : f], r => r.blocks), cats.length > 16 ? 2 : 1, legLines(x, cats), 185);
      const per = I.relay ? I.D / 4 : I.D <= 800 ? 200 : 500;
      const parts = [];
      for (let s = 0; s < I.D; s += per) {
        const bl = a.blocks.filter(bk => bk.from >= s && bk.to <= s + per);
        parts.push({ s, e: s + per, v: avg(bl, 'vClean'), sr: avg(bl, 'sr'), tempo: avg(bl, 'tempo'), sl: avg(bl, 'sl'), si: avg(bl, 'si'), dak: avg(bl, 'dak') });
      }
      body += section(I.relay ? 'Μέσοι όροι ανά σκέλος' : `Μέσοι όροι ανά ${per}${I.u}`, '');
      body += `<div class="box"><table class="tbl"><thead><tr><th>Μέρος</th><th>Ταχύτητα m/s</th><th>Συχνότητα</th><th>Tempo s/κύκλο</th><th>Μήκος κύκλου</th><th>SI</th><th>ΔΑΚ</th></tr></thead><tbody>
        ${parts.map(p => `<tr><td class="l">${p.s}–${p.e}${I.relay ? ' · Σκέλος ' + (p.s / per + 1) : ''}</td><td><b>${F(p.v)}</b></td><td>${F(p.sr, 1)}</td><td>${F(p.tempo)}</td><td>${F(p.sl)}</td><td>${F(p.si)}</td><td class="dk"><b>${F(p.dak)}</b></td></tr>`).join('')}
        </tbody></table></div>`;
    }
    return page(I, 'Χεριές: συχνότητα, tempo, μήκος κύκλου', body, n, N);
  }

  // ---- start and finish (and the single turn of the 100 / the 50 of the 25 m pool)
  function startTable(x, I) {
    const a = x.race.start, b = x.pb.start, c = x.ref.start;
    const bk = a.style === 'bk';
    const rows = [
      ['Αντίδραση (πρώτη κίνηση)', 'reaction', 's', 'lower', 2],
      [bk ? 'Ώθηση από τον τοίχο' : 'Χρόνος στον βατήρα', 'block', 's', 'lower', 2],
      ['Πτήση ως την είσοδο', 'flight', 's', 'none', 2],
      [a.entryHead ? 'Απόσταση εισόδου (κεφάλι)' : 'Απόσταση εισόδου (χέρια)', 'entryDist', I.uu.trim(), 'higher', 2],
      [a.entryHead ? 'Ταχύτητα εισόδου (κεφάλι)' : 'Ταχύτητα εισόδου (χέρια)', 'entryVel', 'm/s', 'higher', 2],
      [a.style === 'br' ? 'Δελφινισμός στο υποβρύχιο' : 'Υποβρύχιοι δελφινισμοί', 'kicks', '', 'none', 0],
      ['Χρόνος ως την έξοδο', 'bt', 's', 'none', 2],
      ['Απόσταση εξόδου (κεφάλι)', 'bd', I.uu.trim(), 'higher', 2],
      ['Ταχύτητα είσοδος → έξοδος', 'entryBoVel', 'm/s', 'higher', 2],
      [a.bo15Slow ? 'Ταχύτητα έξοδος → 15 ⚠' : 'Ταχύτητα έξοδος → 15', 'bo15Vel', 'm/s', 'higher', 2],
      ['Χρόνος 15' + I.u, 't15', 's', 'lower', 2]
    ];
    return `<table class="tbl sm"><thead><tr><th>Εκκίνηση</th><th>Αγώνας</th><th>${esc(I.pbShort)}</th><th>Αναφορά</th><th>Δ ${esc(I.pbShort)}</th><th>Δ Αναφ.</th></tr></thead><tbody>
      ${rows.map(([lab, k, u, better, d]) => `<tr${k === 't15' ? ' class="tot"' : ''}><td class="l">${esc(lab)}</td><td><b>${F(a[k], d)}</b> <span class="dim">${esc(u)}</span></td><td>${F(b[k], d)}</td><td>${F(c[k], d)}</td><td>${chip(a[k] - b[k], better, d)}</td><td>${chip(a[k] - c[k], better, d)}</td></tr>`).join('')}
    </tbody></table>`;
  }

  function finishTiles(x, I) {
    const a = x.race.finish, b = x.pb.finish, c = x.ref.finish;
    return `<div class="tiles">${tile('Τελευταία 5' + I.u, T(a.last5), ' s', a.last5 - b.last5, a.last5 - c.last5, 'lower', 2, null, I)}
      ${tile('Τελ. χεριά → επαφή', F(a.lastGap), ' s', a.lastGap - b.lastGap, a.lastGap - c.lastGap, 'lower', 2, null, I)}
      ${tile('Ταχύτητα τελ. 5' + I.u, F(a.vLast5), ' m/s', a.vLast5 - b.vLast5, a.vLast5 - c.vLast5, 'higher', 2, null, I)}
      ${ok(a.lastX) ? tile(`Τελευταία ${a.lastX}` + I.u, T(a.lastXt), ' s', a.lastXt - b.lastXt, a.lastXt - c.lastXt, 'lower', 2, null, I) : ''}</div>`;
  }

  function pageStart(x, I, n, N) {
    const a = x.race;
    const tA = realTurns(a);
    let body = section(I.relay ? 'Εκκίνηση (1ο σκέλος)' : 'Εκκίνηση', 'Από το σήμα ως τα 15: χρόνοι από το σήμα, αποστάσεις από τον τοίχο της εκκίνησης.');
    // The strip of the start along the 15: with the entry and the breakout measured on the video (in their order).
    const st = a.start;
    if (ok(st.entryDist) && ok(st.bd) && st.entryDist > 0 && st.bd > st.entryDist && st.bd < 15) body += `<div class="box">${ch.startStrip({ w: W - 26, s: st, fmt: v => F(v) }).replace(/ μ\.</g, I.yd ? ' yd<' : ' μ.<')}</div>`;
    body += `<div class="box">${startTable(x, I)}${st.bo15Slow ? slowNote('Έξοδος → 15' + I.u) : ''}</div>`;
    const single = tA.length === 1 && !I.relay;
    if (single) {
      const t = tA[0], tb = realTurns(x.pb)[0], tc = realTurns(x.ref)[0];
      body += section(`Στροφή στα ${t.wall}${I.u}`, 'Από 5 πριν ως 15 μετά τον τοίχο.');
      if (ok(t.bd) && t.bd > 0 && t.bd < 15) body += `<div class="box">${ch.turnStrip({ w: W - 26, t: Object.assign({}, t, { bd: t.bd }), fmt: v => F(v) }).replace(/ μ\.</g, I.yd ? ' yd<' : ' μ.<')}${t.bo15Slow ? slowNote('Έξοδος → +15' + I.u) : ''}</div>`;
      const kicksNote = ok(t.kicks) ? `${t.kicks} δελφ.` + [[I.pbShort, tb.kicks], ['αναφ.', tc.kicks]].filter(p => ok(p[1])).map(p => `${p[0]} ${p[1]}`).join(', ').replace(/^(.+)$/, ' ($1)') : null;
      body += `<div class="tiles">${tile('−5/+15', F(t.inOut), ' s', t.inOut - tb.inOut, t.inOut - tc.inOut, 'lower', 2, null, I)}
        ${tile('Στροφή', F(t.turnTime), ' s', t.turnTime - tb.turnTime, t.turnTime - tc.turnTime, 'lower', 2, t.kind === 'flip' ? 'τελευταία χεριά → πόδια φεύγουν' : 'επαφή → πόδια φεύγουν', I)}
        ${tile('Απόσταση εξόδου', F(t.bd, 1), I.u, t.bd - tb.bd, t.bd - tc.bd, 'higher', 1, kicksNote, I)}
        ${tile('Τοίχος → +15', F(t.out15), ' s', t.out15 - tb.out15, t.out15 - tc.out15, 'lower', 2, null, I)}</div>`;
    } else {
      const sp = st => [{ name: 'Βατήρας', value: st.block, color: '#123B8A' }, { name: 'Πτήση', value: st.flight, color: '#2E6BD6' },
        { name: 'Υποβρύχια', value: st.bt - st.entryT, color: '#5A8EDC' }, { name: 'Ως τα 15', value: st.t15 - st.bt, color: '#A9C4EE', ink: '#123B8A' }];
      // Only a start whose four phases are all measured (its bar adds up to its 15).
      const whole = st0 => sp(st0).every(p => ok(p.value) && p.value > 0) && ok(st0.t15);
      const startRows = [[a.start, 'Αυτός ο αγώνας'], [x.pb.start, I.pbName], [x.ref.start, I.refName]].filter(p => whole(p[0]))
        .map(p => ({ label: p[1], parts: sp(p[0]), total: p[0].t15 }));
      const phases0 = whole(a.start) ? ch.stacked({ w: W - 26, fmtT: v => F(v) + ' s', rows: startRows }) : '';
      if (phases0) {
        body += section('Η εκκίνηση σε φάσεις', 'Ο χρόνος ως τα 15 στις φάσεις της εκκίνησης' + (startRows.length > 1 ? ', δίπλα ' + [I.has.pb && whole(x.pb.start) ? 'στο ' + I.pbShort : '', I.has.ref && whole(x.ref.start) ? 'στην αναφορά' : ''].filter(t0 => t0).join(' και ') : '') + '.');
        body += `<div class="box">${phases0}</div>`;
      }
    }
    body += section(I.relay ? 'Τερματισμός (4ο σκέλος)' : 'Τερματισμός', 'Τα τελευταία μέτρα ως την επαφή.');
    body += finishTiles(x, I);
    return page(I, single ? 'Εκκίνηση, στροφή, τερματισμός' : 'Εκκίνηση και τερματισμός', body, n, N);
  }

  // ---- turns
  function pageTurns(x, I, n, N) {
    const a = x.race, b = x.pb, c = x.ref;
    const tA = realTurns(a), tB = realTurns(b), tC = realTurns(c);
    const nT = tA.length;
    let body = section(`Στροφές (${nT})`, 'Από 5 πριν ως 15 μετά τον τοίχο, με τα γεγονότα στη σειρά που γίνονται. Στροφή: τελευταία χεριά → πόδια φεύγουν (τούμπα) ή επαφή → πόδια φεύγουν (ανοιχτή).');
    const kindCol = I.im || (I.relay && x.ev.style === 'im');
    const kindName = t => t.kind === 'change' ? `${STYLE_NAME[t.styleIn]} → ${STYLE_NAME[t.styleOut]}` : t.kind === 'flip' ? 'Τούμπα' : 'Ανοιχτή';
    if (nT <= 16) {
      body += `<div class="box"><table class="tbl${nT > 8 ? ' sm' : ''}"><thead><tr><th>Τοίχος</th>${kindCol ? '<th>Είδος</th>' : ''}<th>−5 →<br>τοίχος</th><th>Στροφή</th><th>Υποβρύχια</th><th>Δελφ.</th><th>Έξοδος</th><th>Τοίχος<br>→ +15</th><th>−5/+15</th><th>Δ ${esc(I.pbShort)}</th><th>Δ Αναφ.</th></tr></thead><tbody>
        ${tA.map((t, i) => `<tr><td class="l"><b>${t.wall}</b></td>${kindCol ? `<td class="l sm">${esc(kindName(t))}</td>` : ''}<td>${F(t.in5)}</td><td>${F(t.turnTime)}</td><td>${F(t.uw)}</td><td>${F(t.kicks, 0)}</td><td>${F(t.bd, 1)}${t.bo15Slow ? ' ⚠' : ''}</td><td>${F(t.out15)}</td><td><b>${F(t.inOut)}</b></td><td>${chip(t.inOut - (tB[i] ? tB[i].inOut : NaN), 'lower') || '–'}</td><td>${chip(t.inOut - (tC[i] ? tC[i].inOut : NaN), 'lower') || '–'}</td></tr>`).join('')}
        <tr class="tot"><td class="l">Μ.ο.</td>${kindCol ? '<td></td>' : ''}<td>${F(avg(tA, 'in5'))}</td><td>${F(avg(tA, 'turnTime'))}</td><td>${F(avg(tA, 'uw'))}</td><td>${F(avg(tA, 'kicks'), 1)}</td><td>${F(avg(tA, 'bd'), 1)}</td><td>${F(avg(tA, 'out15'))}</td><td><b>${F(avg(tA, 'inOut'))}</b></td><td>${chip(avg(tA, 'inOut') - avg(tB, 'inOut'), 'lower') || '–'}</td><td>${chip(avg(tA, 'inOut') - avg(tC, 'inOut'), 'lower') || '–'}</td></tr>
        </tbody></table></div>`;
    } else {
      const step = I.relay ? I.D / 4 : I.D <= 800 ? 100 : 200;
      const rows = [];
      for (let s = 0; s < I.D; s += step) {
        const pick = r => realTurns(r).filter(t => t.wall > s && t.wall <= s + step);
        const la = pick(a);
        if (!la.length) continue;
        rows.push({ s, e: s + step, n: la.length, inOut: avg(la, 'inOut'), in5: avg(la, 'in5'), out15: avg(la, 'out15'), bd: avg(la, 'bd'), kicks: avg(la, 'kicks'), turn: avg(la, 'turnTime'),
          dPb: avg(la, 'inOut') - avg(pick(b), 'inOut'), dRef: avg(la, 'inOut') - avg(pick(c), 'inOut') });
      }
      body += `<div class="box"><table class="tbl"><thead><tr><th>Μέρος</th><th>Στροφές</th><th>−5 → τοίχος</th><th>Στροφή</th><th>Δελφ.</th><th>Έξοδος</th><th>Τοίχος → +15</th><th>−5/+15</th><th>Δ ${esc(I.pbShort)}</th><th>Δ Αναφ.</th></tr></thead><tbody>
        ${rows.map(r => `<tr><td class="l">${r.s}–${r.e}</td><td>${r.n}</td><td>${F(r.in5)}</td><td>${F(r.turn)}</td><td>${F(r.kicks, 1)}</td><td>${F(r.bd, 1)}</td><td>${F(r.out15)}</td><td><b>${F(r.inOut)}</b></td><td>${chip(r.dPb, 'lower')}</td><td>${chip(r.dRef, 'lower')}</td></tr>`).join('')}
        <tr class="tot"><td class="l">Όλες</td><td>${nT}</td><td>${F(avg(tA, 'in5'))}</td><td>${F(avg(tA, 'turnTime'))}</td><td>${F(avg(tA, 'kicks'), 1)}</td><td>${F(avg(tA, 'bd'), 1)}</td><td>${F(avg(tA, 'out15'))}</td><td><b>${F(avg(tA, 'inOut'))}</b></td><td>${chip(avg(tA, 'inOut') - avg(tB, 'inOut'), 'lower')}</td><td>${chip(avg(tA, 'inOut') - avg(tC, 'inOut'), 'lower')}</td></tr>
        </tbody></table></div>`;
    }
    // (2.27.1) The turns whose breakout → +15 was slower than the swimming after it (⚠, Kinovea 2.41.1).
    const slowWalls = tA.filter(t => t.bo15Slow).map(t => t.wall);
    if (slowWalls.length) body += slowNote((slowWalls.length === 1 ? 'Έξοδος → +15 στη στροφή ' : 'Έξοδος → +15 στις στροφές ') + slowWalls.join(', '));
    const cats = tA.map(t => String(t.wall));
    const every = nT > 32 ? 4 : nT > 16 ? 2 : 1;
    const ser = f => three(x, I, t => t[f], r => realTurns(r));
    const h = nT > 8 ? 175 : 190;
    const inOutChart = ch.lineChart({ w: W - 26, h, cats, every, fmt: v => v.toFixed(2), yTitle: '−5/+15 (s)', series: ser('inOut'), xTitle: 'τοίχος (' + I.uu.trim() + ')' });
    const bdChart = ch.lineChart({ w: W - 26, h, cats, every, fmt: v => v.toFixed(1), yTitle: 'Απόσταση εξόδου (' + I.uu.trim() + ')', series: ser('bd'), xTitle: 'τοίχος (' + I.uu.trim() + ')' });
    if (inOutChart || bdChart) {
      body += section('Οι στροφές μέσα στον αγώνα', 'Πώς αλλάζουν από στροφή σε στροφή: ο χρόνος −5/+15' + (bdChart ? ' και η απόσταση εξόδου.' : '.'));
      if (inOutChart) body += `<div class="box">${inOutChart}</div>`;
      if (bdChart) body += `<div class="box">${bdChart}</div>`;
    }
    const oneKind = tA.every(t => t.kind === tA[0].kind && t.styleOut === tA[0].styleOut);
    if (nT <= 7 && oneKind && ok(avg(tA, 'bd')) && avg(tA, 'bd') < 15) {
      const m = {};
      ['in5', 'out15', 'bd', 'turnTime', 'uw', 'vUw', 'vBo15', 'inOut'].forEach(k => { m[k] = avg(tA, k); });
      m.kicks = F(avg(tA, 'kicks'), 1);
      const others = [[I.pbShort, avg(tB, 'inOut')], ['αναφορά', avg(tC, 'inOut')]].filter(p0 => ok(p0[1])).map(p0 => `${p0[0]} ${F(p0[1])}`).join(', ');
      body += section('Η μέση στροφή του αγώνα', `Οι μέσοι όροι των ${nT} στροφών: −5/+15 σε ${F(m.inOut)} s${others ? ' (' + others + ')' : ''}.`);
      body += `<div class="box">${ch.turnStrip({ w: W - 26, t: m, fmt: v => F(v) }).replace(/ μ\.</g, I.yd ? ' yd<' : ' μ.<')}</div>`;
    }
    return page(I, 'Στροφές', body, n, N);
  }

  // ---- IM: by style
  function pageStyles(x, I, n, N) {
    const a = x.race, b = x.pb, c = x.ref, D = I.D, q = D / 4;
    const legs = IM_ORDER.map((st, i) => {
      const s = i * q, e = s + q, t = r => tOf(r, e) - tOf(r, s);
      const fr = r => r.rows.filter(z => z.isFree && z.prevD >= s && z.d <= e && ok(z.sr) && ok(z.seg) && z.seg > 0);
      const w = (r, f) => { const l = fr(r); let cyc = 0, ct = 0, cd = 0; l.forEach(z => { cyc += z.sr * z.seg / 60; ct += z.seg; cd += (z.d - z.prevD) * r.k; });
        if (!(ct > 0)) return NaN; return f === 'v' ? cd / ct : f === 'sr' ? cyc * 60 / ct : cd / cyc; };
      const bl = r => r.blocks.filter(bk => bk.from >= s && bk.to <= e);
      return { st, s, e, a: t(a), pb: t(b), ref: t(c), v: w(a, 'v'), sr: w(a, 'sr'), sl: w(a, 'sl'), vPb: w(b, 'v'), vRef: w(c, 'v'), dak: avg(bl(a), 'dak'), dakRef: avg(bl(c), 'dak') };
    });
    const withWho = [I.has.pb ? 'το ' + I.pbShort : '', I.has.ref ? 'την αναφορά' : ''].filter(t0 => t0).join(' και ');
    let body = section('Ανά στυλ', `Ο χρόνος κάθε ${q}${I.u} της μικτής` + (withWho ? ` και η διαφορά του από ${withWho}.` : '.'));
    body += `<div class="tiles">${legs.map(l => tile(STYLE_NAME[l.st], T(l.a), ' s', l.a - l.pb, l.a - l.ref, 'lower', 2, `ΔΑΚ ${F(l.dak)}` + (ok(l.dakRef) ? ` (αναφ. ${F(l.dakRef)})` : ''), I)).join('')}</div>`;
    const share = r => IM_ORDER.map((st, i) => ({ name: STYLE_NAME[st], value: tOf(r, (i + 1) * q) - tOf(r, i * q), color: ['#123B8A', '#2E6BD6', '#5A8EDC', '#A9C4EE'][i], ink: i === 3 ? '#123B8A' : undefined }));
    const whole = r => share(r).every(p0 => ok(p0.value) && p0.value > 0) && ok(r.final);
    const shareChart = ch.stacked({ w: W - 26, fmtT: v => T(v) + ' s', rows: [[a, 'Αυτός ο αγώνας'], [b, I.pbName], [c, I.refName]].filter(p0 => whole(p0[0])).map(p0 => ({ label: p0[1], parts: share(p0[0]), total: p0[0].final })) });
    if (shareChart && whole(a)) body += section('Πώς μοιράζεται ο χρόνος στα στυλ', '') + `<div class="box">${shareChart}</div>`;
    const vsKey = I.has.ref ? 'ref' : I.has.pb ? 'pb' : null;
    if (vsKey) {
      body += section('Διαφορά από ' + (vsKey === 'ref' ? 'την αναφορά' : 'το ' + I.pbShort) + ' ανά στυλ', '');
      body += `<div class="box tight">${ch.hDiverging({ w: W - 26, items: legs.map(l => ({ label: `${STYLE_NAME[l.st]} (${l.s}–${l.e})`, value: l.a - l[vsKey], note: vsKey === 'ref' && I.has.pb ? `${I.pbShort} ${S(l.a - l.pb)}` : '' })), labelW: 190, noteW: vsKey === 'ref' && I.has.pb ? 100 : 0, rowH: 27 })}</div>`;
    }
    body += section('Κολύμβηση ανά στυλ', 'Στην καθαρή κολύμβηση κάθε στυλ.');
    body += `<div class="box"><table class="tbl"><thead><tr><th>Στυλ</th><th>Χρόνος</th><th>Ταχύτητα m/s</th><th>Δ ταχ. ${esc(I.pbShort)}</th><th>Δ ταχ. Αναφ.</th><th>Συχνότητα</th><th>Tempo</th><th>Μήκος κύκλου</th><th>ΔΑΚ</th></tr></thead><tbody>
      ${legs.map(l => `<tr><td class="l"><b>${esc(STYLE_NAME[l.st])}</b></td><td>${T(l.a)}</td><td><b>${F(l.v)}</b></td><td>${chip(l.v - l.vPb, 'higher') || '–'}</td><td>${chip(l.v - l.vRef, 'higher') || '–'}</td><td>${F(l.sr, 1)}</td><td>${F(60 / l.sr)}</td><td>${F(l.sl)}</td><td class="dk"><b>${F(l.dak)}</b></td></tr>`).join('')}
    </tbody></table></div>`;
    return page(I, 'Μικτή: ανά στυλ', body, n, N);
  }

  // ---- relays: legs and takeovers
  function pageLegs(x, I, n, N, withExchanges) {
    const a = x.race, b = x.pb, c = x.ref;
    const legOf = (r, i) => r.legs && r.legs[i] ? r.legs[i] : null;
    const split = (r, i) => legOf(r, i) ? legOf(r, i).split : NaN;
    const cols = [I.has.pb ? 'pb' : '', I.has.ref ? 'ref' : ''].filter(k => k);
    const name = k => k === 'pb' ? I.pbShort : 'Αναφ.';
    let body = section('Τα σκέλη', 'Ο χρόνος κάθε σκέλους (από την επαφή του προηγούμενου), ο ΔΑΚ και η κολύμβησή του' +
      (cols.length ? ', δίπλα ' + cols.map(k => k === 'pb' ? 'στο ρεκόρ της ομάδας' : 'στην αναφορά').join(' και ') : '') + '.');
    body += `<div class="box"><table class="tbl sm"><thead><tr><th>Σκέλος</th><th>Στυλ</th><th>Χρόνος</th><th>Σωρευτικός</th>${cols.map(k => `<th>${esc(name(k))}</th><th>Δ ${esc(name(k))}</th>`).join('')}<th>ΔΑΚ</th><th>Συχνότητα</th><th>Μήκος<br>κύκλου</th></tr></thead><tbody>
      ${a.legs.map((l, i) => `<tr><td class="l"><b>${i + 1}</b> · ${l.from}–${l.to}${I.swimmers[i] ? ' · ' + esc(I.swimmers[i]) : ''}</td><td class="l">${esc(STYLE_NAME[l.style])}</td><td><b>${T(l.split)}</b></td><td>${T(l.cum)}</td>${cols.map(k => `<td>${T(split(x[k], i))}</td><td>${chip(l.split - split(x[k], i), 'lower') || '–'}</td>`).join('')}<td class="dk"><b>${F(l.dak)}</b></td><td>${F(l.sr, 1)}</td><td>${F(l.sl)}</td></tr>`).join('')}
      <tr class="tot"><td class="l">Ομάδα</td><td></td><td><b>${T(a.final)}</b></td><td></td>${cols.map(k => `<td>${T(x[k].final)}</td><td>${chip(a.final - x[k].final, 'lower') || '–'}</td>`).join('')}<td class="dk"><b>${F(a.avg.dak)}</b></td><td>${F(a.avg.sr, 1)}</td><td>${F(a.avg.sl)}</td></tr>
    </tbody></table></div>`;
    if (cols.length) {
      const k0 = cols[0];
      const chart = ch.vDiverging({ w: W - 26, h: 140, cats: a.legs.map((l, i) => `Σκέλος ${i + 1}`), values: a.legs.map((l, i) => l.split - split(x[k0], i)), labels: true, yTitle: 's' });
      if (chart) {
        body += section('Κάθε σκέλος σε σχέση με ' + (k0 === 'pb' ? 'το ρεκόρ της ομάδας' : 'την αναφορά'), 'Κάτω από το μηδέν πιο γρήγορα (μπλε), πάνω πιο αργά (πορτοκαλί).');
        body += `<div class="box">${chart}</div>`;
      }
    }
    // (2.28.0) The exchanges have their own page when they are measured (pageExchanges).
    if (!withExchanges) return page(I, 'Τα σκέλη', body, n, N);
    body += section('Οι αλλαγές', 'Επαφή του κολυμβητή που φτάνει → τα πόδια του επόμενου φεύγουν από τον βατήρα (επιτρέπεται ως −0.03 s). Ζώνη-στόχος 0.10–0.20 s.');
    const exOf = (r, i) => legOf(r, i) ? legOf(r, i).ex : NaN;
    body += `<div class="box"><table class="tbl"><thead><tr><th>Αλλαγή</th><th>Επαφή → πόδια<br>φεύγουν</th><th>${esc(I.pbShort)}</th><th>Αναφορά</th><th>15 από<br>την επαφή</th><th>Έξοδος</th><th>Δελφ.</th><th>Πάνω από 0.20 s</th></tr></thead><tbody>
      ${a.legs.slice(1).map((l, j) => { const t = a.turns.find(z => z.kind === 'exchange' && z.wall === l.from) || {};
        const zone = !ok(l.ex) ? '–' : l.ex > 0.2 ? `<span class="chip l">${F(l.ex - 0.2)} s</span>` : '<span class="chip g">στη ζώνη</span>';
        return `<tr><td class="l">${j + 1} → ${j + 2} (${l.from}${I.u})</td><td><b>${F(l.ex)}</b> s</td><td>${F(exOf(b, j + 1))}</td><td>${F(exOf(c, j + 1))}</td><td>${F(l.out15)}</td><td>${F(t.bd, 1)}</td><td>${F(t.kicks, 0)}</td><td>${zone}</td></tr>`; }).join('')}
      <tr class="tot"><td class="l">Σύνολο πάνω από 0.20</td><td colspan="7" class="l"><b>${F(a.legs.slice(1).reduce((s0, l) => s0 + (ok(l.ex) ? Math.max(0, l.ex - 0.2) : 0), 0))} s</b>${ok(a.start.block) ? ` · 1ο σκέλος: αντίδραση ${F(a.start.block)} s` : ''}</td></tr>
    </tbody></table></div>`;
    const exChart = ch.barChart({ w: W - 26, h: 132, cats: a.legs.slice(1).map((l, j) => `${j + 1} → ${j + 2}`), values: a.legs.slice(1).map(l => l.ex), fmt: v => F(v) + ' s', yTitle: 's',
      colors: a.legs.slice(1).map(l => l.ex > 0.2 ? C.lossMark : C.gainMark) });
    if (exChart) body += `<div class="box">${exChart}</div>`;
    return page(I, 'Σκέλη και αλλαγές', body, n, N);
  }

  // ---- (2.28.0) relays: the exchanges with their snapshots, the starts against the individual bests
  /** The colours of the four swimmers of a relay (their dots, as on the HUD of Kinovea). */
  const LEG_COLORS = ['#E85454', '#F2C230', '#4D94FF', '#3DDC96'];
  /** The zones of an exchange (as Kinovea's SwimRelay): 0 not valid (before −0.03), 1 risk, 2 the target 0.10–0.20, 3 slow, 4 very slow. */
  const exZone = t => t < -0.03 - 1e-9 ? 0 : t < 0.10 - 1e-9 ? 1 : t <= 0.20 + 1e-9 ? 2 : t <= 0.35 + 1e-9 ? 3 : 4;
  const EX_ZONE_COLORS = ['#E5484D', '#2E6BD6', '#1FA97A', '#E0A020', '#E57B22'];
  /** What the time of an exchange says (as on the HUD): in the target, how much above or below it, not valid. */
  function exVerdict(t) {
    const z = exZone(t);
    if (z === 0) return 'άκυρη: πριν από το −0.03';
    if (z === 1) return `${F(0.10 - t)} s κάτω από τη ζώνη · ρίσκο`;
    if (z === 2) return 'στη ζώνη 0.10–0.20';
    return `${F(t - 0.20)} s πάνω από τη ζώνη${z === 4 ? ' · αργή' : ''}`;
  }
  const legDot = i => `<i class="rl-dot" style="background:${LEG_COLORS[i % 4]}"></i>`;
  const family = name => String(name || '').trim().split(/\s+/)[0] || '';
  /** A snapshot of an exchange: its picture, put in by Kinovea when it prints the pages ({{SNAP:id}}), or its place when there is none. */
  const snap = (id, color, caption) => `<div class="xc-ph">${id ? `<div class="im"><img src="{{SNAP:${esc(id)}}}" alt=""></div>` : '<div class="im none">χωρίς στιγμιότυπο</div>'}
      <div class="xc-cap"><i style="background:${color}"></i>${caption}</div></div>`;

  function pageExchanges(x, I, n, N, withLegs) {
    const a = x.race, b = x.pb, rl = x.relay;
    const name = i => I.swimmers[i] || (rl && rl.legs[i].name) || '';
    const officialEx = i => rl ? rl.legs[i].officialEx : NaN;
    const exOf = i => ok(a.legs[i].ex) ? a.legs[i].ex : (rl && i > 0 ? rl.exchanges[i - 1].ex : NaN);
    let body = section('Οι κολυμβητές', 'Η σειρά της ομάδας και ο χρόνος κάθε σκέλους (από την επαφή του προηγούμενου).');
    const over = a.legs.slice(1).reduce((s0, l, j) => s0 + (ok(exOf(j + 1)) ? Math.max(0, exOf(j + 1) - 0.2) : 0), 0);
    // (Without the page of the legs: their ΔΑΚ and their swimming here.)
    const more = withLegs ? '<th>ΔΑΚ</th><th>Συχνότητα</th><th>Μήκος<br>κύκλου</th>' : '';
    const moreOf = l => withLegs ? `<td class="dk"><b>${F(l.dak)}</b></td><td>${F(l.sr, 1)}</td><td>${F(l.sl)}</td>` : '';
    body += `<div class="box"><table class="tbl sm"><thead><tr><th>Σκέλος</th><th style="text-align:left">Κολυμβητής</th>${I.relay && x.ev.style === 'im' ? '<th style="text-align:left">Στυλ</th>' : ''}<th>Χρόνος</th><th>Σωρευτικός</th>${more}<th>Αλλαγή<br>(βίντεο)</th><th>Αλλαγή<br>(επίσημη)</th></tr></thead><tbody>
      ${a.legs.map((l, i) => `<tr><td class="l"><b>${i + 1}</b> · ${l.from}–${l.to}</td><td class="l">${legDot(i)}${esc(name(i)) || '–'}</td>${I.relay && x.ev.style === 'im' ? `<td class="l">${esc(STYLE_NAME[l.style])}</td>` : ''}<td><b>${T(l.split)}</b></td><td>${T(l.cum)}</td>${moreOf(l)}
        <td>${i === 0 ? (ok(a.start.block) ? 'αντ. ' + F(a.start.block) : '–') : `<b>${F(exOf(i))}</b>`}</td><td>${F(officialEx(i))}</td></tr>`).join('')}
      <tr class="tot"><td class="l">Ομάδα</td><td></td>${I.relay && x.ev.style === 'im' ? '<td></td>' : ''}<td><b>${T(a.final)}</b></td><td></td>${withLegs ? `<td class="dk"><b>${F(a.avg.dak)}</b></td><td>${F(a.avg.sr, 1)}</td><td>${F(a.avg.sl)}</td>` : ''}<td colspan="2" class="l">πάνω από 0.20: <b>${F(over)} s</b></td></tr>
    </tbody></table></div>`;
    body += section('Οι αλλαγές', 'Επαφή του κολυμβητή που φτάνει → τα πόδια του επόμενου φεύγουν από τον βατήρα (επιτρέπεται ως −0.03 s). Ζώνη-στόχος 0.10–0.20 s. ' +
      'Τα στιγμιότυπα είναι τα καρέ του βίντεο στις δύο στιγμές.');
    a.legs.slice(1).forEach((l, j) => {
      const i = j + 1, t = exOf(i), e = rl ? rl.exchanges[j] : { touchSnap: '', offSnap: '' };
      const turn = a.turns.find(z => z.kind === 'exchange' && z.wall === l.from) || {};
      const pbEx = b.legs && b.legs[i] ? b.legs[i].ex : NaN;
      const extra = [ok(officialEx(i)) ? 'επίσημη ' + F(officialEx(i)) : '', ok(pbEx) ? `${esc(I.pbShort).toLowerCase()} ${F(pbEx)}` : ''].filter(s0 => s0).join(' · ');
      const out = [ok(l.out15) ? `15${I.u} από την επαφή ${F(l.out15)} s` : '', ok(turn.bd) ? `έξοδος ${F(turn.bd, 1)}${I.u}` : '', ok(turn.kicks) ? `${F(turn.kicks, 0)} δελφ.` : ''].filter(s0 => s0).join(' · ');
      const names = name(i - 1) || name(i) ? `${legDot(i - 1)}${esc(family(name(i - 1)) || 'Σκέλος ' + i)} → ${legDot(i)}${esc(family(name(i)) || 'Σκέλος ' + (i + 1))}` : '';
      body += `<div class="xc"><div><div class="xc-t">Αλλαγή ${i} → ${i + 1} <small>· ${l.from}${I.u}</small></div>
        ${names ? `<div class="xc-n">${names}</div>` : ''}
        <div class="xc-v">${ok(t) ? F(t) : '–'}<small> s</small></div>
        ${ok(t) ? `<span class="zone z${exZone(t)}">${exVerdict(t)}</span>` : ''}
        ${extra ? `<div class="xc-x">${extra}</div>` : ''}${out ? `<div class="xc-x">${out}</div>` : ''}</div>
        ${snap(e.touchSnap, '#00C8FF', `Επαφή · ${T(a.legs[i - 1].cum)}`)}
        ${snap(e.offSnap, '#E57B22', `Πόδια φεύγουν · ${ok(t) ? S(t) + ' s' : '–'}`)}</div>`;
    });
    const exs = a.legs.slice(1).map((l, j) => exOf(j + 1));
    const chart = ch.barChart({ w: W - 26, h: 120, cats: a.legs.slice(1).map((l, j) => `${j + 1} → ${j + 2}`), values: exs.map(v => ok(v) ? Math.max(0, v) : NaN),
      fmt: v => F(v) + ' s', yTitle: 's', colors: exs.map(v => ok(v) ? EX_ZONE_COLORS[exZone(v)] : C.pb) });
    if (chart) body += `<div class="box">${chart}</div>`;
    return page(I, 'Οι αλλαγές της σκυτάλης', body, n, N);
  }

  /**
   * The start of each swimmer in the relay and in his individual best (x.relay.legs[i].pb): to the 15 from the signal in the first leg
   * (both normal starts), from the touch before him in the others against from the signal in his best; split into the exchange (or
   * the reaction) and the dive (feet off the block → 15); the breakout, the kicks, the speeds feet → breakout and breakout → 15, the
   * time of the leg against his best.
   */
  function relayStartOf(x, i) {
    const a = x.race, k = a.k || 1;
    if (i === 0) {
      const s = a.start, uw = s.bt - s.block;
      return { react: s.block, to15: s.t15, dive: s.t15 - s.block, bd: s.bd, kicks: s.kicks, leg: a.legs[0].split,
        uwV: uw > 0 && ok(s.bd) ? s.bd * k / uw : NaN, boV: ok(s.bo15Vel) ? s.bo15Vel : (ok(s.bd) && s.t15 > s.bt ? (15 - s.bd) * k / (s.t15 - s.bt) : NaN) };
    }
    const l = a.legs[i], t = a.turns.find(z => z.kind === 'exchange' && z.wall === l.from) || {};
    const react = l.ex, to15 = l.out15, bt = react + t.uw;
    return { react, to15, dive: to15 - react, bd: t.bd, kicks: t.kicks, leg: l.split,
      uwV: t.uw > 0 && ok(t.bd) ? t.bd * k / t.uw : NaN, boV: ok(t.vBo15) ? t.vBo15 : (ok(t.bd) && to15 > bt ? (15 - t.bd) * k / (to15 - bt) : NaN) };
  }

  function pbStartOf(model) {
    if (!model) return null;
    const s = model.start, k = model.k || 1, uw = s.bt - s.block;
    return { react: s.block, to15: s.t15, dive: s.t15 - s.block, bd: s.bd, kicks: s.kicks, leg: model.final,
      uwV: uw > 0 && ok(s.bd) ? s.bd * k / uw : NaN, boV: ok(s.bo15Vel) ? s.bo15Vel : (ok(s.bd) && s.t15 > s.bt ? (15 - s.bd) * k / (s.t15 - s.bt) : NaN) };
  }

  function pageRelayStart(x, I, n, N) {
    const a = x.race, rl = x.relay;
    const legLen = I.D / 4;
    const starts = a.legs.map((l, i) => ({ r: relayStartOf(x, i), p: pbStartOf(rl.legs[i].pb), leg: rl.legs[i] }));
    const most = Math.max.apply(null, [].concat.apply([], starts.map(s => [s.r.to15, s.p ? s.p.to15 : NaN])).filter(ok).concat([1]));
    const PX = 300 / most;
    const event = st => `${legLen} ${(STYLE_NAME[st] || '').toLowerCase()}`;
    let body = section('Η εκκίνηση στη σκυτάλη και στο ατομικό ρεκόρ', `Κάθε κολυμβητής με το ατομικό του ρεκόρ στο ίδιο αγώνισμα (${event(a.legs[0].style)}${I.relay && x.ev.style === 'im' ? ', το στυλ του σκέλους του' : ''}), ` +
      `από τις αναλύσεις του και όχι μεταγενέστερο. Στα σκέλη 2–4 η εκκίνηση μετρά από την επαφή του προηγούμενου, στο ατομικό από το σήμα. Η βουτιά: από τα πόδια που φεύγουν από τον βατήρα ως τα 15${I.u}`);
    body += `<div class="rs-key"><i style="background:#00C8FF"></i>αντίδραση (σήμα → πόδια) ή αλλαγή (επαφή → πόδια) <i style="background:${C.navy};margin-left:12px"></i>βουτιά: πόδια φεύγουν → 15${I.u} <span style="margin-left:12px">δεξιά: ως τα 15${I.u} (s)</span></div>`;
    const bar = (s, exchange) => {
      if (!s || !ok(s.to15)) return '<div class="rs-bar"><span class="t">–</span></div>';
      const rw = Math.max(0, (ok(s.react) ? s.react : 0) * PX), dw = Math.max(0, (ok(s.dive) ? s.dive : s.to15) * PX);
      const label = exchange ? `<span class="rs-lab" style="left:0">αλλαγή ${F(s.react)}</span>` : '';
      return `<div class="rs-bar">${label}<span class="r" style="width:${rw.toFixed(1)}px">${rw >= 28 ? F(s.react) : ''}</span><span class="d" style="width:${dw.toFixed(1)}px">${dw >= 34 ? F(s.dive) : ''}</span><span class="t">${F(s.to15)}</span></div>`;
    };
    const diff = (r, p, nn) => ok(r) && ok(p) ? S(r - p, nn) : '–';
    let gainAll = 0, gainEx = 0, gainDive = 0, counted = 0;
    starts.forEach((s, i) => {
      const r = s.r, p = s.p, leg = s.leg;
      const who = leg.name || I.swimmers[i] || `Σκέλος ${i + 1}`;
      const pbLine = p ? `ατομικό ρεκόρ: ${event(a.legs[i].style)} <b>${T(p.leg)}</b>${leg.pbCompetition ? ' · ' + esc(leg.pbCompetition) : ''}` : '';
      let verdict;
      if (!p || !ok(p.to15) || !ok(r.to15)) {
        verdict = leg.athleteId ? `Χωρίς ατομικό ρεκόρ στο ${event(a.legs[i].style)} στις αναλύσεις του: μόνο η σκυτάλη.` : 'Ο κολυμβητής δεν είναι από το KOE App: μόνο η σκυτάλη.';
      } else if (i === 0) {
        const d = r.to15 - p.to15;
        const word = Math.abs(d) < 0.005 ? '<span class="chip n">ίδια</span>' : d > 0 ? `<span class="chip l">${F(d)} s πιο αργή</span>` : `<span class="chip g">${F(-d)} s πιο γρήγορη</span>`;
        verdict = `${word} Κανονική εκκίνηση (σήμα): ως τα 15${I.u} ${Math.abs(d) < 0.005 ? 'όσο' : d > 0 ? 'πιο αργή από' : 'πιο γρήγορη από'} το ατομικό του· αντίδραση ${diff(r.react, p.react)}, βουτιά ${diff(r.dive, p.dive)} s.`;
      } else {
        const g = p.to15 - r.to15, gEx = p.react - r.react, gDive = p.dive - r.dive;
        if (ok(g) && ok(gEx) && ok(gDive)) { gainAll += g; gainEx += gEx; gainDive += gDive; counted++; }
        const word = g >= 0 ? `<span class="chip g">κερδίζει ${F(g)} s</span>` : `<span class="chip l">χάνει ${F(-g)} s</span>`;
        const dive = gDive < -0.005 ? `, αλλά <b class="loss">στη βουτιά χάνει ${F(-gDive)} s</b> από το ατομικό του.` : `, ${F(gDive)} από τη βουτιά.`;
        verdict = `${word} ως τα 15${I.u}: ${F(gEx)} από την αλλαγή αντί για αντίδραση${dive}`;
      }
      const rows = [['Έξοδος' + I.u.replace(' ', ' (') + ')', r.bd, p ? p.bd : NaN, 1], ['Δελφινισμοί', r.kicks, p ? p.kicks : NaN, 0],
        ['Πόδια → έξοδος (m/s)', r.uwV, p ? p.uwV : NaN, 2], [`Έξοδος → 15${I.u} (m/s)`, r.boV, p ? p.boV : NaN, 2], ['Σκέλος / ατομικό', r.leg, p ? p.leg : NaN, -1]];
      body += `<div class="rs"><div class="rs-h"><div class="rs-n">${i + 1} · ${esc(who)}</div><div class="rs-pb">${pbLine}</div></div>
        <div class="rs-b"><div><div class="rs-row"><span class="lab">Σκυτάλη</span>${bar(r, i > 0)}</div><div class="rs-row"><span class="lab">Ατομικό</span>${bar(p, false)}</div>
          <div class="rs-txt">${verdict}</div></div>
        <table class="rs-tbl"><thead><tr><th></th><th>Σκυτάλη</th><th>Ατομικό</th><th>Διαφ.</th></tr></thead><tbody>
          ${rows.map(q => `<tr><td class="l">${q[0]}</td><td><b>${q[3] < 0 ? T(q[1]) : F(q[1], q[3])}</b></td><td>${q[3] < 0 ? T(q[2]) : F(q[2], q[3])}</td><td>${diff(q[1], q[2], q[3] < 0 ? 2 : q[3])}</td></tr>`).join('')}
        </tbody></table></div></div>`;
    });
    if (counted) {
      const part = (g0, what) => g0 >= 0 ? `${F(g0)} από ${what}` : `χάνουν ${F(-g0)} ${what.replace(/^τις /, 'στις ')}`;
      body += `<div class="rs-sum"><b>Σκέλη ${counted === 3 ? '2–4' : 'με ατομικό ρεκόρ'}:</b> ως τα 15${I.u} ${gainAll >= 0 ? 'κερδίζουν' : 'χάνουν'} μαζί <b>${F(Math.abs(gainAll))} s</b> ` +
        `${gainAll >= 0 ? 'από' : 'σε σχέση με'} τα ατομικά τους· ${part(gainEx, 'τις αλλαγές')}${gainDive >= 0 === gainEx >= 0 ? ' και ' : ', ενώ '}${part(gainDive, 'τις βουτιές')}.</div>`;
    }
    return page(I, 'Η εκκίνηση στη σκυτάλη', body, n, N);
  }

  // ---- appendix
  function appendixTable(x, I) {
    const a = x.race, b = x.pb, c = x.ref;
    const rows = a.rows;
    if (rows.length <= 20) {
      return `<div class="box"><table class="tbl sm"><thead><tr><th>Πέρασμα</th><th>Χρόνος</th><th>Τμήμα</th><th>m/s</th><th>Χεριές</th><th>Συχνότητα</th><th>Μήκος κύκλου</th><th>Δ ${esc(I.pbShort)}</th><th>Δ Αναφ.</th></tr></thead><tbody>
        ${rows.map(r => `<tr class="${r.isFree ? '' : 'phase'}"><td class="l"><b>${r.d}</b></td><td><b>${T(r.t)}</b></td><td>${F(r.seg)}</td><td>${F(r.vel)}</td><td>${r.isFree ? F(r.strokes, 1) : ''}</td><td>${r.sr ? F(r.sr, 1) : ''}</td><td>${r.sl ? F(r.sl) : ''}</td><td>${chip(r.t - tOf(b, r.d), 'lower') || '–'}</td><td>${chip(r.t - tOf(c, r.d), 'lower') || '–'}</td></tr>`).join('')}
        </tbody></table></div>`;
    }
    if (rows.length <= 66) {
      const cols = rows.length > 40 ? 3 : 2, per = Math.ceil(rows.length / cols);
      let html = '<div class="cols">';
      for (let k = 0; k < cols; k++) html += `<div class="box"><table class="tbl xs"><thead><tr><th>Πέρασμα</th><th>Χρόνος</th><th>Τμήμα</th>${cols === 2 ? '<th>m/s</th>' : ''}<th>Δ ${esc(I.pbShort)}</th><th>Δ Αναφ.</th></tr></thead><tbody>
        ${rows.slice(k * per, (k + 1) * per).map(r => `<tr class="${r.isFree ? '' : 'phase'}"><td class="l"><b>${r.d}</b></td><td><b>${T(r.t)}</b></td><td>${F(r.seg)}</td>${cols === 2 ? `<td>${F(r.vel)}</td>` : ''}<td>${chip(r.t - tOf(b, r.d), 'lower') || '–'}</td><td>${chip(r.t - tOf(c, r.d), 'lower') || '–'}</td></tr>`).join('')}</tbody></table></div>`;
      return html + '</div>';
    }
    // Long races in 25 pools: one row per length.
    const P = I.P, n = I.D / P;
    const len = [];
    for (let L = 0; L < n; L++) {
      const e = (L + 1) * P, s0 = L * P, t = r => tOf(r, e) - tOf(r, s0);
      const clean = a.rows.find(r => r.isFree && r.prevD === s0 + 15);
      len.push({ L: L + 1, e, cum: tOf(a, e), split: t(a), v: clean ? clean.vel : NaN, sr: clean ? clean.sr : NaN, dPb: tOf(a, e) - tOf(b, e), dRef: tOf(a, e) - tOf(c, e) });
    }
    const cols = 2, per = Math.ceil(len.length / cols);
    let html = '<div class="cols">';
    for (let k = 0; k < cols; k++) html += `<div class="box"><table class="tbl xs"><thead><tr><th>Μήκος</th><th>Σωρευτικός</th><th>Μήκος (s)</th><th>m/s 15–20</th><th>Συχν.</th><th>Δ ${esc(I.pbShort)}</th><th>Δ Αναφ.</th></tr></thead><tbody>
      ${len.slice(k * per, (k + 1) * per).map(r => `<tr><td class="l"><b>${r.L}</b> · ${r.e}</td><td><b>${T(r.cum)}</b></td><td>${F(r.split)}</td><td>${F(r.v)}</td><td>${F(r.sr, 1)}</td><td>${chip(r.dPb, 'lower') || '–'}</td><td>${chip(r.dRef, 'lower') || '–'}</td></tr>`).join('')}</tbody></table></div>`;
    return html + '</div>';
  }

  function cumChart(x, I, h) {
    const a = x.race, b = x.pb, c = x.ref;
    const pts = a.blocks.length <= 2 ? a.rows.map(r => r.d) : a.blocks.map(bk => bk.to);
    if (!I.has.pb && !I.has.ref) return '';
    return section('Διαφορά χρόνου σε κάθε πέρασμα', 'Πόσο μπροστά (κάτω από το μηδέν) ή πίσω (πάνω) ήταν ' + (I.relay ? 'η ομάδα' : 'ο αθλητής') + ' σε κάθε σημείο του αγώνα.') +
      `<div class="box">${ch.lineChart({ w: W - 26, h, cats: pts.map(String), fmt: v => S(v), tickFmt: v => (v > 0 ? '+' : v < 0 ? '−' : '') + Math.abs(v).toFixed(1),
        refLine: { value: 0, label: '' }, yTitle: 's', every: pts.length > 24 ? 2 : 1, xTitle: 'απόσταση (' + I.uu.trim() + ')',
        series: [{ name: 'από το ' + I.pbShort, color: C.race, values: pts.map(d => tOf(a, d) - tOf(b, d)) }, { name: 'από την αναφορά', color: C.ref, dash: 'dot', marker: 'diamond', values: pts.map(d => tOf(a, d) - tOf(c, d)) }] })}</div>`;
  }

  function glossary(I) {
    const list = [
      ['ΔΑΚ', `ο βασικός δείκτης: (χεριές + δευτερόλεπτα από τα 15${I.u} ως τον τοίχο) ανά 10 μ., σε κάθε 50άρι· όσο μικρότερος, τόσο πιο αποδοτικό το κολύμπι.`],
      ['Καθαρή κολύμβηση', `τα τμήματα χωρίς εκκίνηση, στροφές και τερματισμό: από 15 μετά τον τοίχο ως 5 πριν τον επόμενο.`],
      ['Συχνότητα', 'κύκλοι χεριών ανά λεπτό (κύκλος = δύο χεριές στο ελεύθερο και στο ύπτιο, μία στην πεταλούδα και στο πρόσθιο).'],
      ['Tempo', 'δευτερόλεπτα ανά κύκλο χεριών (60 ÷ συχνότητα), όπως το δίνει ένα tempo trainer.'],
      ['Μήκος κύκλου', 'μέτρα ανά κύκλο χεριών· ταχύτητα = συχνότητα × μήκος κύκλου.'],
      ['SI', 'δείκτης κολύμβησης = ταχύτητα × μήκος κύκλου· μεγαλύτερος = πιο αποδοτική κολύμβηση.'],
      ['Στροφή −5/+15', 'ο χρόνος από 5 πριν ως 15 μετά τον τοίχο.'],
      I.relay ? ['Αλλαγή', 'επαφή του κολυμβητή που φτάνει → πόδια του επόμενου φεύγουν (ως −0.03 s επιτρέπεται).'] : ['Ρυθμός αγώνα', 'πώς μοιράζεται ο χρόνος στα 50άρια: σταθερός, θετικός (πιο αργό 2ο μισό), αρνητικός (πιο γρήγορο 2ο μισό).'],
      [I.pbShort, I.relay ? 'ο καλύτερος χρόνος της ομάδας, από αγώνα με ανάλυση.' : 'ατομικό ρεκόρ: ο καλύτερος χρόνος του αθλητή, από αγώνα με ανάλυση.'],
      ['Αναφορά', 'ο αγώνας σύγκρισης που διαλέγει ο αναλυτής (π.χ. ένας τελικός).']
    ];
    return section('Επεξήγηση όρων', '') + `<div class="gloss">${list.map(([k, v]) => `<div><b>${esc(k)}</b>: ${esc(v)}</div>`).join('')}</div>`;
  }

  function pagesAppendix(x, I, startN, N) {
    const a = x.race;
    const big = a.rows.length > 40 || (I.D <= 100 && !I.relay && a.rows.length > 10);
    const t1 = section(a.rows.length > 66 ? 'Ανά μήκος' : 'Όλα τα περάσματα', a.rows.length > 66 ? 'Ο σωρευτικός χρόνος σε κάθε τοίχο, ο χρόνος του μήκους και η ταχύτητα της καθαρής κολύμβησης (15–20).'
      : 'Ο χρόνος σε κάθε πέρασμα. Με απαλό πορτοκαλί τα τμήματα εκκίνησης, στροφής και τερματισμού.') + appendixTable(x, I);
    const small = I.D <= 100 && !I.relay;
    const phase = small ? section('Σύγκριση ανά φάση', '') + `<div class="box">${phaseTable(x, I)}</div>` : '';
    if (!big) return [page(I, 'Περάσματα, σύγκριση, όροι', t1 + phase + cumChart(x, I, 190) + glossary(I), startN, N)];
    if (small) return [page(I, 'Περάσματα και φάσεις', t1 + phase, startN, N), page(I, 'Σύγκριση και όροι', cumChart(x, I, 250) + glossary(I), startN + 1, N)];
    return [page(I, a.rows.length > 66 ? 'Ανά μήκος' : 'Περάσματα', t1, startN, N), page(I, 'Σύγκριση και όροι', phase + cumChart(x, I, 230) + glossary(I), startN + 1, N)];
  }

  // ---- the document
  /** The pages of the report, those with something to show (a page of the strokes needs strokes, the ΔΑΚ a ΔΑΚ, ...). */
  function plan(x, I, R0) {
    const a = x.race;
    const tA = realTurns(a);
    const P = [];
    // (2.25.0) The pages of the short analysis by their kind (SHORT_PAGES).
    const add = (kind, f) => { f.kind = kind; P.push(f); };
    add('overview', (R, n, N) => pageOverview(x, I, R, n, N));
    if (R0.recs.length) add('phases', (R, n, N) => pageRecs(x, I, R, n, N));
    // (2.25.0) The conclusion of the whole race right after the phases (in the short analysis too).
    add('conclusion', (R, n, N) => pageConclusion(x, I, R, n, N));
    if (a.blocks.some(bk => ok(bk.dak))) add('dak', (R, n, N) => pageDak(x, I, n, N));
    // (2.28.0) A relay: its exchanges on their own page (with the snapshots of their two moments), and each swimmer's start against his
    // individual best when there is one.
    const exPage = I.relay && !!x.relay && a.legs && a.legs.slice(1).some(l => ok(l.ex));
    // Without the team's best and a reference the legs have nothing to be compared with: their values go to the page of the exchanges.
    const legsPage = I.relay && a.legs && a.legs.some(l => ok(l.split)) && (!exPage || I.has.pb || I.has.ref);
    if (legsPage) add('legs', (R, n, N) => pageLegs(x, I, n, N, !exPage));
    if (exPage) add('exchanges', (R, n, N) => pageExchanges(x, I, n, N, !legsPage));
    if (x.relay && x.relay.legs.some(l => l.pb && ok(l.pb.start.t15))) add('relayStart', (R, n, N) => pageRelayStart(x, I, n, N));
    if (I.D <= 100 && !I.relay) add('speed', (R, n, N) => pageSpeed(x, I, n, N));
    else add('composition', (R, n, N) => pageComposition(x, I, n, N));
    if (I.im && !I.relay && a.rows.some(r => ok(r.t))) add('styles', (R, n, N) => pageStyles(x, I, n, N));
    if ((I.D >= 200 || I.relay) && a.blocks.length >= 2 && a.blocks.filter(bk => ok(bk.time)).length >= 2) add('pace', (R, n, N) => pagePace(x, I, R, n, N));
    if (a.rows.some(r => ok(r.sr))) add('strokes', (R, n, N) => pageStrokes(x, I, n, N));
    add('start', (R, n, N) => pageStart(x, I, n, N));
    if ((tA.length >= 2 || (I.relay && tA.length >= 1)) && tA.some(t => ok(t.inOut))) add('turns', (R, n, N) => pageTurns(x, I, n, N));
    return P;
  }

  /** (2.25.0) The pages of the short analysis: the race at a glance, the phases against the reference, the conclusion, the ΔΑΚ. */
  const SHORT_PAGES = ['overview', 'phases', 'conclusion', 'dak'];

  /**
   * The whole report (x: { ev, race, pb, ref, details }, see fromScript): { html, pages, title }. opts.short: only the pages of the
   * short analysis (SHORT_PAGES; until 2.24.1 the first three: the race at a glance, the recommendations, the ΔΑΚ), without the appendix.
   */
  function document(x, opts) {
    opts = opts || {};
    const I = info(x);
    footText = 'Kinovea Swim · Ανάλυση αγώνα' + (I.competition ? ' · ' + I.competition : '');
    const R = recommendations(x);
    let P = plan(x, I, R);
    if (opts.short) P = P.filter(f => SHORT_PAGES.indexOf(f.kind) >= 0);
    const extra = opts.short ? 0 : (x.race.rows.length > 40 || (I.D <= 100 && !I.relay && x.race.rows.length > 10) ? 2 : 1);
    const N = P.length + extra;
    const pages = P.map((f, i) => f(R, i + 1, N));
    if (extra) pagesAppendix(x, I, P.length + 1, N).forEach(p => pages.push(p));
    const title = [I.title, I.who, I.competition].filter(s => s).join(' · ');
    return { html: `<!doctype html><html lang="el"><head><meta charset="utf-8"><title>${esc(title)}</title><style>${CSS}</style></head><body>${pages.join('\n')}</body></html>`,
      pages: N, title, I, x, R };
  }

  // ---------------------------------------------------------------------------------------------------------------------------
  // The model of the pages from the analysis of the script (swimAnalyze_), in the shape of report_templates/sim2.js analyse
  // ---------------------------------------------------------------------------------------------------------------------------
  /** A number of the analysis; NaN when there is none, so that a missing value is never a 0 in a sum or a difference. */
  function num(v) {
    if (v === null || v === undefined || v === '' || typeof v === 'boolean') return NaN;
    const n = Number(v);
    return isFinite(n) ? n : NaN;
  }
  function ok(v) { return typeof v === 'number' && isFinite(v); }

  /**
   * The model of one race: its analysis by the script (an = swimAnalyze_(race)) and the values Kinovea sends with it (race.manual: the
   * distances measured on the video, the speeds of the phases of the start and the turns), with the same priorities as the sheet of the
   * report (a value typed or sent by Kinovea before the script's own). An estimate (≈) is not a measurement: not used.
   */
  function analyse(race, an) {
    const D = race.distance, P = race.pool, k = race.k || 1, relay = !!race.relay;
    const legLen = relay ? D / 4 : D;
    const m = race.manual || {};
    const mn = key => num(m[key]);
    const pick = (key, auto) => ok(mn(key)) ? mn(key) : num(auto);
    const styleAt = d => swimStyleAt_(race, d);
    const legOf = d => relay ? Math.max(0, Math.min(3, Math.floor((d - 1e-6) / legLen))) : 0;

    const rows = (an.rows || []).map(r => {
      const sr = num(r.sr), vel = num(r.vel), sl = num(r.sl);
      return { d: r.d, prevD: r.prevD, t: num(r.t), seg: num(r.segTime), vel, isFree: !!r.isFree, style: r.style, ppc: r.ppc || PPC(r.style),
        strokes: num(r.strokes), sr, sl, dps: num(r.dps), tempo: sr > 0 ? 60 / sr : NaN, si: vel * sl, leg: legOf(r.d) };
    });
    const byD = {};
    rows.forEach(r => { byD[r.d] = r; });
    const tAt = d => d === 0 ? 0 : (byD[d] ? byD[d].t : NaN);
    const inRange = (r, a, b) => r.prevD >= a - 1e-9 && r.d <= b + 1e-9;

    // The 50s (swimBlocks50_), with the parts of the ΔΑΚ: the strokes and the seconds of its windows (15 after each wall of the 50 to
    // the next wall) per 10 m. The ΔΑΚ itself is the script's.
    const blocks = (an.blocks || []).map(b0 => {
      const b = b0.from, e = b0.to;
      let wS = 0, wT = 0, width = 0, whole = true;
      for (let w = b; w < e - 1e-9; w += P) {
        const a15 = w + 15, aw = Math.min(w + P, e);
        const win = rows.filter(r => inRange(r, a15, aw));
        const tt = tAt(aw) - tAt(a15);
        if (!win.length || !ok(tt) || aw <= a15 || win.some(r => !ok(r.strokes))) { whole = false; break; }
        wS += win.reduce((s, r) => s + r.strokes, 0);
        wT += tt;
        width += aw - a15;
      }
      const dak = num(b0.dak), div = width * k / 10;
      const parts = whole && ok(dak) && div > 0;
      let cd = 0, ct = 0;
      rows.forEach(r => { if (inRange(r, b, e) && ok(r.sr) && ok(r.seg) && r.seg > 0) { ct += r.seg; cd += (r.d - r.prevD) * k; } });
      const sr = num(b0.sr), sl = num(b0.sl), vClean = ct > 0 ? cd / ct : NaN;
      return { from: b, to: e, time: num(b0.time), cum: num(b0.cum), strokes: num(b0.strokes), swolf: num(b0.swolf), dak,
        dakStrokes: parts ? wS / div : NaN, dakTime: parts ? wT / div : NaN, winStrokes: parts ? wS : NaN, winTime: parts ? wT : NaN,
        sr, sl, dps: num(b0.dps), vel: num(b0.vel), vClean, tempo: sr > 0 ? 60 / sr : NaN, si: vClean * sl, style: b0.style || styleAt(e), leg: legOf(e),
        first25: P === 50 ? rows.find(r => r.isFree && r.prevD === b + 15 && r.d === b + 25) || null : null,
        second25: P === 50 ? rows.find(r => r.isFree && r.prevD === b + 25 && r.d === b + 45) || null : null };
    });

    // The start, as the row of the start of the sheet (swimInputPlan_).
    const s0 = an.start || {};
    const startDist = m.underwaterEstimated ? NaN : mn('underwaterDist');
    const startB = pick('underwaterTime', s0.underwaterTime);
    const bd = ok(mn('breakoutDist')) ? mn('breakoutDist') : startDist;
    // (2.27.0) The entry of the head (Kinovea 2.41.0: manual.entryHead, entryDistHead, entryVelHead); an analysis of before: the hands'.
    const entryHead = m.entryHead === true;
    const entryDist = entryHead ? mn('entryDistHead') : mn('entryDistHands');
    const start = {
      entryHead,
      reaction: mn('reaction'),
      block: pick('blockTime', s0.blockTime),
      flight: pick('flightTime', s0.flightTime),
      entryT: pick('entryTime', s0.entryTime),
      entryDist,
      entryVel: entryHead ? mn('entryVelHead') : mn('entryVelHands'),
      sigEntryVel: mn('sigEntryVel'),
      kicks: pick('kicks', s0.kicks),
      bt: startB,
      bd,
      uwDist: bd - entryDist,
      entryBoVel: mn('entryBreakoutVel'),
      sigBoVel: ok(mn('signalBreakoutVel')) ? mn('signalBreakoutVel') : (ok(startDist) && startB > 0 ? startDist * k / startB : NaN),
      t15: num(s0.t15),
      bo15Vel: mn('breakout15Vel'),
      // (2.27.1) The check of the breakout (Kinovea 2.41.1): slower to the 15 than the swimming after it (⚠).
      bo15Slow: m.breakout15Slow === true,
      style: styleAt(Math.min(D, 1))
    };

    // The turns (exchanges in a relay), with the values of the sheet (a value typed in Kinovea before the script's).
    const turns = (an.turns || []).map(t0 => {
      const w = t0.wall, mt = (m.turns && m.turns[String(w)]) || {};
      const kind = t0.exchange ? 'exchange' : t0.styleIn !== t0.styleOut ? 'change' : t0.flip ? 'flip' : 'open';
      const turnTime = ok(num(mt.turnTime)) ? num(mt.turnTime) : num(t0.turnTime);
      return { wall: w, kind, styleIn: t0.styleIn, styleOut: t0.styleOut, in5: num(t0.in5), out15: num(t0.out15), inOut: num(t0.inOut), turnTime,
        uw: ok(num(mt.underwaterTime)) ? num(mt.underwaterTime) : num(t0.underwaterTime),
        kicks: ok(num(mt.kicks)) ? num(mt.kicks) : num(t0.kicks),
        bd: mt.underwaterEstimated ? NaN : num(mt.underwaterDist),
        vUw: num(mt.uwSpeed), vBo15: num(mt.bo15Speed), bo15Slow: mt.bo15Slow === true, lastGap: num(t0.lastStrokeGap), leg: legOf(w + 1),
        ex: kind === 'exchange' ? turnTime : NaN };
    });
    const turnAt = {};
    turns.forEach(t => { turnAt[t.wall] = t; });

    // The finish.
    const f0 = an.finish || {};
    const last5 = num(f0.last5);
    const lastX = [15, 25, 35].find(x0 => D - x0 > 0 && byD[D - x0] && ok(byD[D - x0].t)) || NaN;
    const finish = { last5, lastGap: num(f0.lastStrokeGap), lastX, lastXt: ok(lastX) ? tAt(D) - tAt(D - lastX) : NaN,
      vLast5: last5 > 0 ? 5 * k / last5 : NaN, style: styleAt(D) };

    // The phases (as swimV2Phases_): the start (0–15), the clean swimming, the turns or the exchanges (−5/+15), the finish (last 5).
    const span = (a, b) => tAt(b) - tAt(a);
    const phases = [{ kind: 'start', from: 0, to: 15, t: span(0, 15) }];
    let prev = 15;
    for (let w = P; w < D - 1e-9; w += P) {
      const tn = turnAt[w];
      phases.push({ kind: 'swim', from: prev, to: w - 5, t: span(prev, w - 5), style: styleAt(w) });
      const to = Math.min(w + 15, D - 5);
      phases.push({ kind: tn && tn.kind === 'exchange' ? 'exchange' : 'turn', from: w - 5, to, wall: w, t: span(w - 5, to) });
      prev = to;
    }
    phases.push({ kind: 'swim', from: prev, to: D - 5, t: span(prev, D - 5), style: styleAt(D) });
    phases.push({ kind: 'finish', from: D - 5, to: D, t: span(D - 5, D) });
    const phaseSum = kd => { const l = phases.filter(x0 => x0.kind === kd); return l.length ? (l.every(x0 => ok(x0.t)) ? l.reduce((a, x0) => a + x0.t, 0) : NaN) : 0; };
    const composition = { start: phaseSum('start'), swim: phaseSum('swim'), turn: phaseSum('turn'), exchange: phaseSum('exchange'), finish: phaseSum('finish') };

    // The averages of the clean swimming (as the summary of swimAnalyze_), the ΔΑΚ and the turns.
    let cyc = 0, ct = 0, cd = 0;
    rows.forEach(r => { if (ok(r.sr) && ok(r.seg) && r.seg > 0) { cyc += r.sr * r.seg / 60; ct += r.seg; cd += (r.d - r.prevD) * k; } });
    const avgOf = (l, f) => { const v = l.map(f).filter(ok); return v.length ? v.reduce((a, b) => a + b, 0) / v.length : NaN; };
    const sum0 = an.summary || {};
    const average = { sr: ct > 0 ? cyc * 60 / ct : num(sum0.avgSr), sl: cyc > 0 ? cd / cyc : num(sum0.avgSl), vClean: ct > 0 ? cd / ct : NaN };
    average.tempo = average.sr > 0 ? 60 / average.sr : NaN;
    average.si = average.vClean * average.sl;
    average.dak = ok(num(sum0.avgDak)) ? num(sum0.avgDak) : avgOf(blocks, b => b.dak);
    average.dakStrokes = avgOf(blocks, b => b.dakStrokes);
    average.dakTime = avgOf(blocks, b => b.dakTime);
    const realT = turns.filter(t => t.kind !== 'exchange');
    average.turnInOut = avgOf(realT, t => t.inOut);

    // The legs of a relay.
    let legs = null;
    if (relay) {
      legs = [0, 1, 2, 3].map(i => {
        const from = i * legLen, to = from + legLen;
        const bl = blocks.filter(b => b.from >= from - 1e-9 && b.to <= to + 1e-9);
        let c2 = 0, t2 = 0, d2 = 0;
        rows.forEach(r => { if (inRange(r, from, to) && ok(r.sr) && ok(r.seg) && r.seg > 0) { c2 += r.sr * r.seg / 60; t2 += r.seg; d2 += (r.d - r.prevD) * k; } });
        const ex = i > 0 ? turnAt[from] : null;
        return { leg: i, style: styleAt(to), from, to, split: tAt(to) - tAt(from), cum: tAt(to), dak: avgOf(bl, b => b.dak),
          sr: t2 > 0 ? c2 * 60 / t2 : NaN, sl: c2 > 0 ? d2 / c2 : NaN, vClean: t2 > 0 ? d2 / t2 : NaN, ex: ex ? ex.ex : NaN, out15: ex ? ex.out15 : NaN,
          reaction: i === 0 ? start.block : NaN, t15: i === 0 ? start.t15 : (ex ? ex.out15 : NaN) };
      });
    }
    const cps = rows.map(r => r.d);
    return { rows, byD, blocks, start, finish, phases, composition, avg: average, final: num(f0.tFinish !== undefined ? f0.tFinish : sum0.finalTime),
      turns, cps, legs, k, legLen, pattern: cps.filter(d => d <= P + 1e-9) };
  }

  /** A race to compare with that is not there (no PB, no reference): the shape of 'like', every number missing. */
  function blank(like) {
    const nan = o => {
      if (o === null || o === undefined) return o;
      if (Array.isArray(o)) return o.map(nan);
      if (typeof o === 'number') return NaN;
      if (typeof o !== 'object') return o;
      const out = {};
      Object.keys(o).forEach(key => {
        // The places (distances, walls, kinds) stay: the rows of the race and of the blank one line up.
        const keep = ['d', 'prevD', 'from', 'to', 'wall', 'leg', 'kind', 'style', 'styleIn', 'styleOut', 'isFree', 'ppc', 'lastX'];
        out[key] = keep.indexOf(key) >= 0 ? o[key] : nan(o[key]);
      });
      return out;
    };
    const b = nan({ rows: like.rows, blocks: like.blocks, start: like.start, finish: like.finish, phases: like.phases, composition: like.composition,
      avg: like.avg, turns: like.turns, legs: like.legs });
    b.byD = {};
    b.rows.forEach(r => { b.byD[r.d] = r; });
    b.final = NaN; b.cps = like.cps; b.k = like.k; b.legLen = like.legLen; b.pattern = like.pattern;
    return b;
  }

  /**
   * A race to compare with, on the rows of the race: its values at the same distances (a reference with other checkpoints has NaN where
   * it has no time), so that every comparison is at the same place.
   */
  function aligned(model, like, D) {
    const out = blank(like);
    out.final = model.final;
    out.rows.forEach((r, i) => { const o = model.byD[r.d]; if (o && o.prevD === r.prevD) out.rows[i] = o; else if (o) out.rows[i] = Object.assign({}, r, { t: o.t }); });
    out.byD = {};
    out.rows.forEach(r => { out.byD[r.d] = r; });
    out.blocks = like.blocks.map(b => model.blocks.find(o => o.from === b.from && o.to === b.to) || blank({ blocks: [b] }).blocks[0]);
    out.turns = like.turns.map(t => model.turns.find(o => o.wall === t.wall) || Object.assign({}, t, { in5: NaN, out15: NaN, inOut: NaN, turnTime: NaN, uw: NaN, kicks: NaN, bd: NaN, vUw: NaN, vBo15: NaN, lastGap: NaN, ex: NaN }));
    ['start', 'finish', 'composition', 'avg', 'phases', 'legs'].forEach(key => { out[key] = model[key]; });
    if (like.phases && model.phases && like.phases.length !== model.phases.length) out.phases = blank({ phases: like.phases }).phases;
    // (2.24.1) The last metres at the distance of the race's own (its last 15, else 25 or 35), from the times of the PB or the reference
    // there, NaN without them: each race chose its own, and a final with no time 15 before the finish (15, 25, 45, 50, 65, 75, 95, 100)
    // gave its last 25 against the race's last 15 (a −4.41 s «better» in blue).
    const lastX = like.finish.lastX;
    const at = d => model.byD[d] && ok(model.byD[d].t) ? model.byD[d].t : NaN;
    out.finish = Object.assign({}, model.finish, { lastX, lastXt: ok(lastX) ? at(D) - at(D - lastX) : NaN });
    return out;
  }

  /**
   * The model of the pages from the script: the race and its analysis, the personal best and the reference of the report
   * (swimComparisons_: { pb: { race, analysis, label }, elite: { race, analysis, label } }), the names of the race.
   */
  function fromScript(race, analysis, comparisons) {
    const cmp = comparisons || {};
    const yd = race.poolUnit === 'yd';
    const ev = { distance: race.distance, pool: race.pool, unit: yd ? 'yd' : 'm', style: race.style, relay: !!race.relay,
      poolName: race.pool === 50 ? 'Πισίνα 50μ.' : yd ? 'Πισίνα 25 γιάρδες' : 'Πισίνα 25μ.' };
    const a = analyse(race, analysis);
    // (2.27.0) The entry of the head (Kinovea 2.41.0) is compared only with an entry of the head, the hands' only with the hands': in the
    // other analysis the values that depend on it are left out (–).
    const sameEntry = o => {
      if (o && o.start && o.start.entryHead !== a.start.entryHead)
        ['entryT', 'flight', 'entryDist', 'entryVel', 'sigEntryVel', 'entryBoVel', 'uwDist'].forEach(k => { o.start[k] = NaN; });
      return o;
    };
    const other = c => c && c.race && c.analysis ? sameEntry(aligned(analyse(c.race, c.analysis), a, race.distance)) : blank(a);
    const gender = String(race.gender || race.category || '').trim();
    const meta = [race.competition, race.location, race.date, race.pool === 50 ? 'Πισίνα 50μ.' : yd ? 'Πισίνα 25yd' : 'Πισίνα 25μ.', race.round, race.lane ? 'Διαδρομή ' + race.lane : '']
      .map(s => String(s || '').trim()).filter(s => s);
    const relay = relayOf(race, a, cmp);
    return { ev, race: a, pb: other(cmp.pb), ref: other(cmp.elite), relay,
      details: { who: race.athlete || '', kind: gender || (race.relay ? 'Ομαδικό' : ''), competition: String(race.competition || '').trim(), club: race.relay ? '' : String(race.club || '').trim(),
        round: String(race.round || '').trim(), meta, pbLabel: cmp.pb ? String(cmp.pb.label || '') : '', refLabel: cmp.elite ? String(cmp.elite.label || '') : '',
        swimmers: relay && relay.legs.some(l => l.name) ? relay.legs.map(l => l.name) : null } };
  }

  /**
   * (2.28.0) A relay with its swimmers (Kinovea 2.42.0, race.relayLegs): each swimmer (his name, his athlete of the KOE App, his
   * official leg and exchange) with his individual personal best of the event of his leg (swimRelayPbs_: same stroke, the distance of
   * the leg, the same pool, not later than the race), and the exchanges with the ids of the snapshots of their two moments. Null for
   * another race.
   */
  function relayOf(race, a, cmp) {
    const legsIn = Array.isArray(race.relayLegs) ? race.relayLegs : [], exIn = Array.isArray(race.relayExchanges) ? race.relayExchanges : [];
    if (!race.relay || !a.legs || (!legsIn.length && !exIn.length)) return null;
    const pbs = Array.isArray(cmp.relayPbs) ? cmp.relayPbs : [];
    const legs = [1, 2, 3, 4].map(n => {
      const l = legsIn.find(o => o.leg === n) || null;
      const p = pbs[n - 1];
      let pb = null;
      try { pb = p && p.race && p.analysis ? analyse(p.race, p.analysis) : null; } catch (e) { pb = null; }
      return { n, name: l ? String(l.athlete || '') : '', athleteId: l ? String(l.athleteId || '') : '', style: a.legs[n - 1].style,
        official: l ? num(l.official) : NaN, officialEx: l ? num(l.officialExchange) : NaN, pb,
        pbCompetition: p && p.race ? String(p.race.competition || '').trim() : '', pbDate: p && p.race ? String(p.race.date || '').trim() : '' };
    });
    const exchanges = [1, 2, 3].map(n => {
      const e = exIn.find(o => o.n === n) || {};
      return { n, touchSnap: e.touchSnap || '', offSnap: e.offSnap || '', ex: num(e.ex), official: num(e.official) };
    });
    return { legs, exchanges };
  }

  // ---------------------------------------------------------------------------------------------------------------------------
  // (2.26.0) Slides: the pages as a presentation, and the posts for the social media
  // ---------------------------------------------------------------------------------------------------------------------------
  // The pages of the report as slides (16:9, 1920 × 1080) at the end of the video of the race, 3 s each, in the order of the pages;
  // and the posts for the social media of the federation (4:5, 1080 × 1350): the race's own numbers only (no PB, no reference, no
  // conclusion). The same model and the same numbers as the pages. Dark, as the band of the first page; the logo of the federation and
  // a frame of the video are put in by Kinovea when it renders them ({{LOGO}}, {{FRAME}}: the data of the two images).

  /** The colours of the slides, on the dark of the band: the race, the PB, the reference; better (blue) and worse (orange). */
  const K = {
    race: '#47A3FF', pb: '#9AA7B8', ref: '#E9EEF5', grid: 'rgba(255,255,255,0.10)', axis: 'rgba(255,255,255,0.30)', text: '#FFFFFF',
    soft: '#C9D6E3', dim: '#8CA0B6', gain: '#4C8DFF', loss: '#FF9A4D', accent: '#5CC8FF', teal: '#5FE3C9', cyan: '#00C8FF', bg: '#0E1D33',
    phase: { start: '#4F86E8', swim: '#F08A3E', turn: '#24B3A1', exchange: '#8DB7F2', finish: '#A07CDB' },
    phaseName: { start: 'Εκκίνηση', swim: 'Κολύμβηση', turn: 'Στροφές', exchange: 'Αλλαγές', finish: 'Τερματισμός' }
  };
  const dashK = d => d === 'dash' ? '16 10' : d === 'dot' ? '2 9' : '';

  /**
   * A line chart on the dark (the slides): one y axis, the categories on x, up to three series (the race solid, the PB dashed, the
   * reference dotted), the value of each at its end, a reference line (the mean). fs: the size of the type.
   */
  function slLine(o) {
    const fs = o.fs || 24, padL = fs * 3.6, padR = o.end === false ? fs * 1.2 : fs * 4.4, padT = fs * 1.2, padB = fs * 2.3;
    const iw = o.w - padL - padR, ih = o.h - padT - padB;
    const vals = [];
    o.series.forEach(s => s.values.forEach(v => { if (ok(v)) vals.push(v); }));
    if (o.ref && ok(o.ref.value)) vals.push(o.ref.value);
    if (!vals.length || !o.cats.length) return '';
    const mn = Math.min.apply(null, vals), mx = Math.max.apply(null, vals);
    const pad = (mx - mn) * 0.15 || Math.abs(mx) * 0.04 || 1;
    const tk = niceTicks(mn - pad, mx + pad, o.ticks || 4);
    const n = o.cats.length;
    const X = i => padL + (n === 1 ? iw / 2 : iw * i / (n - 1));
    const Y = v => padT + ih * (1 - (v - tk.lo) / (tk.hi - tk.lo));
    const fmt = o.fmt || (v => F(v));
    let g = '';
    tk.ticks.forEach(t => {
      g += `<line x1="${padL}" x2="${(padL + iw).toFixed(1)}" y1="${Y(t).toFixed(1)}" y2="${Y(t).toFixed(1)}" stroke="${K.grid}" stroke-width="1.5"/>` +
        `<text x="${(padL - fs * 0.5).toFixed(1)}" y="${(Y(t) + fs * 0.33).toFixed(1)}" text-anchor="end" font-size="${(fs * 0.8).toFixed(1)}" fill="${K.dim}">${o.tick ? o.tick(t) : fmt(t)}</text>`;
    });
    const every = o.every || 1;
    o.cats.forEach((c, i) => {
      if (i !== n - 1 && (i % every !== 0 || n - 1 - i < Math.max(1, every * 0.75))) return;
      g += `<text x="${X(i).toFixed(1)}" y="${(padT + ih + fs * 1.45).toFixed(1)}" text-anchor="middle" font-size="${(fs * 0.8).toFixed(1)}" fill="${K.dim}">${esc(c)}</text>`;
    });
    if (o.xTitle) g += `<text x="${(padL + iw).toFixed(1)}" y="${(padT + ih + fs * 2.25).toFixed(1)}" text-anchor="end" font-size="${(fs * 0.7).toFixed(1)}" fill="${K.dim}">${esc(o.xTitle)}</text>`;
    if (o.ref && ok(o.ref.value)) {
      const y = Y(o.ref.value).toFixed(1);
      g += `<line x1="${padL}" x2="${(padL + iw).toFixed(1)}" y1="${y}" y2="${y}" stroke="${K.soft}" stroke-width="2" stroke-dasharray="10 8" opacity="0.7"/>` +
        (o.end === false ? `<text x="${(padL + iw).toFixed(1)}" y="${(Number(y) - fs * 0.45).toFixed(1)}" text-anchor="end" font-size="${(fs * 0.75).toFixed(1)}" fill="${K.soft}">${esc(o.ref.label)} ${fmt(o.ref.value)}</text>`
          : `<text x="${(padL + 6).toFixed(1)}" y="${(Number(y) - fs * 0.45).toFixed(1)}" font-size="${(fs * 0.75).toFixed(1)}" fill="${K.soft}">${esc(o.ref.label)} ${fmt(o.ref.value)}</text>`);
    }
    const ends = [];
    o.series.forEach(s => {
      const w = s.width || 5;
      const pts = s.values.map((v, i) => ok(v) ? [X(i), Y(v)] : null);
      let d = '', pen = false;
      pts.forEach(p => { if (!p) { pen = false; return; } d += (pen ? 'L' : 'M') + p[0].toFixed(1) + ' ' + p[1].toFixed(1); pen = true; });
      if (!d) return;
      g += `<path d="${d}" fill="none" stroke="${s.color}" stroke-width="${w}" stroke-dasharray="${dashK(s.dash)}" stroke-linecap="round" stroke-linejoin="round"/>`;
      if (s.dots !== false && (!o.dotsMax || n <= o.dotsMax)) pts.forEach(p => { if (p) g += `<circle cx="${p[0].toFixed(1)}" cy="${p[1].toFixed(1)}" r="${(w * 1.35).toFixed(1)}" fill="${s.color}" stroke="${K.bg}" stroke-width="3"/>`; });
      let li = -1;
      s.values.forEach((v, i) => { if (ok(v)) li = i; });
      if (li >= 0 && o.end !== false) ends.push({ y: Y(s.values[li]), x: X(li), text: fmt(s.values[li]), color: s.main ? K.text : s.color, bold: !!s.main });
    });
    // The values at the end, apart from each other.
    ends.sort((p, q) => p.y - q.y);
    for (let i = 1; i < ends.length; i++) if (ends[i].y - ends[i - 1].y < fs * 1.05) ends[i].y = ends[i - 1].y + fs * 1.05;
    ends.forEach(e => { g += `<text x="${(e.x + fs * 0.7).toFixed(1)}" y="${(e.y + fs * 0.35).toFixed(1)}" font-size="${(fs * (e.bold ? 0.95 : 0.85)).toFixed(1)}" font-weight="${e.bold ? 800 : 600}" fill="${e.color}">${e.text}</text>`; });
    return `<svg class="slc" viewBox="0 0 ${o.w} ${o.h}" width="${o.w}" height="${o.h}" xmlns="http://www.w3.org/2000/svg">${g}</svg>`;
  }

  /** The legend of the slides: the race, the PB, the reference, those that are there. */
  function slLegend(I, which) {
    const items = [['Αυτός ο αγώνας', K.race, ''], I.has.pb && which !== 'race' ? [I.pbName, K.pb, 'dash'] : null, I.has.ref && which !== 'race' ? [I.refName, K.ref, 'dot'] : null].filter(q => q);
    return `<div class="sl-lg">${items.map(([t, col, d]) => `<span><svg width="54" height="14"><line x1="4" x2="50" y1="7" y2="7" stroke="${col}" stroke-width="5" stroke-dasharray="${dashK(d)}" stroke-linecap="round"/></svg>${esc(t)}</span>`).join('')}</div>`;
  }

  /** A chip of the slides (as the chips of the band: blue better, orange worse). */
  const sChip = (x, better, n, suffix) => chip(x, better, n, suffix);

  /** «ΑΡ 1:48.78 +0.42 · Αναφ. 1:45.71 +3.49»: the comparisons that are there, of a value of the race. */
  function slVs(I, va, vb, vc, better, fmt, n) {
    const out = [];
    if (I.has.pb && ok(vb)) out.push(`<span>${esc(I.pbShort)} ${fmt(vb)} ${sChip(va - vb, better, n)}</span>`);
    if (I.has.ref && ok(vc)) out.push(`<span>Αναφ. ${fmt(vc)} ${sChip(va - vc, better, n)}</span>`);
    return out.length ? `<div class="sl-vs">${out.join('')}</div>` : '';
  }

  /** The frame of a slide: the strip of colours, the event and the athlete, the logo, the title, the body, the counter. */
  function slFrame(kind, I, title, sub, body, n, N, cls) {
    return `<section class="sl ${kind}${cls ? ' ' + cls : ''}">
      <div class="sl-strip"></div>
      <div class="sl-top"><div class="sl-ev"><b>${esc(I.title)}</b>${I.kind ? ` <span class="sl-cat">${esc(I.kind)}</span>` : ''}<i>·</i>${esc(I.who)}</div><img class="sl-logo" src="{{LOGO}}" alt=""></div>
      ${title ? `<div class="sl-hd"><h1><span class="tmark"></span>${esc(title)}</h1>${sub ? `<p>${sub}</p>` : ''}</div>` : ''}
      <div class="sl-bd">${body}</div>
      <div class="sl-ft"><span>${esc(footText)}</span><span class="sl-n">${n} / ${N}</span></div>
    </section>`;
  }

  /** At most 'max' groups of the 50s (the 50s themselves when they are few): from, to, time, ΔΑΚ (mean), strokes. */
  function slGroups(bl, max) {
    const k = Math.max(1, Math.ceil(bl.length / max));
    const out = [];
    for (let i = 0; i < bl.length; i += k) {
      const part = bl.slice(i, i + k);
      out.push({ from: part[0].from, to: part[part.length - 1].to, time: sum(part, 'time'), dak: avg(part, 'dak'), strokes: sum(part, 'strokes'),
        sr: avg(part, 'sr'), sl: avg(part, 'sl'), vClean: avg(part, 'vClean'), cum: part[part.length - 1].cum, n: part.length });
    }
    return out;
  }

  // ---- 16:9: the pages
  function vOverview(x, I, R, n, N) {
    const a = x.race, b = x.pb, c = x.ref;
    const subs = [];
    if (I.has.pb) subs.push(`<span>${esc(I.pbShort)} ${T(b.final)} ${sChip(a.final - b.final, 'lower')}</span>`);
    if (I.has.ref) subs.push(`<span>Αναφ. ${T(c.final)} ${sChip(a.final - c.final, 'lower')}</span>`);
    if (!subs.length && ok(a.final)) subs.push(`<span>μέση ταχύτητα ${F(I.D * a.k / a.final)} m/s</span>`);
    const head = `<div class="ov-top"><span class="kick">ΑΝΑΛΥΣΗ ΑΓΩΝΑ · RACE ANALYSIS</span>${I.round ? `<span class="pill">${esc(upper(I.round))}</span>` : ''}</div>
      <div class="ov-main"><div class="ov-who"><div class="ov-ev">${esc(I.title)}${I.kind ? ` <span>${esc(I.kind)}</span>` : ''}</div><div class="ov-name">${esc(I.who)}</div>
        <div class="ov-meta">${I.meta.map(esc).join(' · ')}</div></div>
        <div class="ov-time"><div class="lab">Τελικός χρόνος</div><div class="big">${T(a.final)}</div><div class="ov-subs">${subs.join('')}</div></div></div>`;
    // The three cards of the first page.
    const dak = a.avg.dak;
    const dakCmp = [[I.pbShort, b.avg.dak], ['Αναφ.', c.avg.dak]].filter(p => ok(p[1])).map(p => `<span>${esc(p[0])} ${F(p[1])} ${sChip(dak - p[1], 'lower')}</span>`).join('');
    const card1 = `<div class="hc2 dak"><div class="lab">ΔΑΚ · ο βασικός δείκτης</div><div class="val">${F(dak)}</div><div class="sub">${dakCmp || 'χεριές + δευτερόλεπτα ανά 10 μ.'}</div></div>`;
    let card2;
    if (I.has.pb) card2 = `<div class="hc2"><div class="lab">${a.final < b.final ? (I.relay ? 'Νέο ρεκόρ ομάδας' : 'Νέο ατομικό ρεκόρ') : 'Από το ' + esc(I.pbShort)}</div><div class="val">${S(a.final - b.final)}<small> s</small></div><div class="sub">${T(b.final)} → ${T(a.final)}</div></div>`;
    else if (I.has.ref) card2 = `<div class="hc2"><div class="lab">Από την αναφορά</div><div class="val">${S(a.final - c.final)}<small> s</small></div><div class="sub">${T(c.final)} → ${T(a.final)}</div></div>`;
    else card2 = `<div class="hc2"><div class="lab">Μέση ταχύτητα</div><div class="val">${F(I.D * a.k / a.final)}<small> m/s</small></div><div class="sub">${T(a.final)} στα ${I.D}${I.u}</div></div>`;
    const lv = R.levels || [];
    const weak = lv.filter(l => l.pct > 0.05).sort((p0, q0) => q0.pct - p0.pct)[0];
    const card3 = weak
      ? `<div class="hc2 aim"><div class="lab">Η πιο αδύνατη φάση</div><div class="val word">${esc(weak.title)} <b>${P(-weak.pct)}</b></div><div class="sub">${lv.filter(l => l !== weak).map(l => `<span>${esc(low(l.title))} ${P(-l.pct)}</span>`).join('')}</div></div>`
      : `<div class="hc2 aim"><div class="lab">Κολύμβηση</div><div class="val">${F(a.avg.vClean)}<small> m/s</small></div><div class="sub"><span>συχνότητα ${F(a.avg.sr, 1)}</span><span>μήκος κύκλου ${F(a.avg.sl)} μ.</span></div></div>`;
    // The race by its 50s (or its parts), with the ΔΑΚ: blue better than the race's own mean, orange worse.
    let strip, stripLab;
    if (a.blocks.length <= 2 && !I.relay) {
      stripLab = 'Ο αγώνας ανά τμήμα · ο χρόνος και η ταχύτητα';
      strip = a.rows.map(r => `<div class="st"><div class="st-l">${r.prevD}–${r.d}</div><div class="st-v">${F(r.seg)}</div><div class="st-d">${F(r.vel)} m/s</div></div>`).join('');
    } else {
      const gs = slGroups(a.blocks, 8), mean = avg(gs, 'dak'), im = x.ev.style === 'im';
      stripLab = `Ο αγώνας ανά ${gs[0] && gs[0].n > 1 ? 'τμήμα' : '50άρι'} · χρόνος και ΔΑΚ${im ? '' : ' · μπλε καλύτερος, πορτοκαλί χειρότερος από τον μ.ο.'}`;
      strip = gs.map(gp => `<div class="st${!im && ok(gp.dak) && ok(mean) ? (gp.dak < mean - 0.05 ? ' g' : gp.dak > mean + 0.05 ? ' l' : '') : ''}"><div class="st-l">${gp.from}–${gp.to}</div><div class="st-v">${T(gp.time)}</div><div class="st-d">ΔΑΚ ${F(gp.dak)}</div></div>`).join('');
    }
    const body = head + `<div class="hc2s">${card1}${card2}${card3}</div><div class="sl-sub2">${stripLab}</div><div class="strip">${strip}</div>`;
    return slFrame('v', I, '', '', body, n, N, 'ov');
  }

  function vPhases(x, I, R, n, N) {
    const lv = R.levels || [];
    const against1 = lv.every(l => l.ref) ? 'την αναφορά' : lv.every(l => !l.ref) ? 'το ' + lv[0].name : 'τη σύγκριση';
    const lo = 85, hi = 105, at = v => Math.max(3, Math.min(100, 100 * (v - lo) / (hi - lo)));
    const bars = `<div class="pb2">${lv.map(l => `<div class="pb2-r"><span class="t">${esc(l.title)}</span><span class="bar"><i class="${l.pct <= 0.05 ? 'up' : ''}" style="width:${at(100 - l.pct).toFixed(1)}%"></i><b style="left:${at(100).toFixed(1)}%"></b></span><span class="v">${P(-l.pct)}${l.pct > 0.05 ? `<small>χάνει ${F(l.lost)} s</small>` : ''}</span></div>`).join('')}</div>`;
    const map = raceMap(R.map, true, 'slm', true);
    const body = bars + (map ? `<div class="sl-sub2">Πού χάνεται ο χρόνος</div><div class="mapw">${map}</div>` : '');
    return slFrame('v', I, 'Σύγκριση ανά φάση', `Η ταχύτητα κάθε φάσης απέναντι στ${against1.slice(1)} στην ίδια απόσταση · η λευκή γραμμή: ίδια ταχύτητα`, body, n, N, 'ph');
  }

  function vConclusion(x, I, R, n, N) {
    const a = x.race;
    const cm = ok(x.ref.final) ? { m: x.ref, name: 'αναφορά', ref: true } : ok(x.pb.final) ? { m: x.pb, name: I.pbShort, ref: false } : null;
    if (!cm) {
      // Without a PB and a reference: the race against itself (the two halves, the swimming).
      const bl = a.blocks, h = Math.floor(bl.length / 2);
      const h1 = sum(bl.slice(0, h), 'time'), h2 = sum(bl.slice(h), 'time');
      const tiles = [['Τα δύο μισά', ok(h1) && ok(h2) ? `${T(h1)} · ${T(h2)}` : '–', ok(h2 - h1) ? `2ο μισό ${S(h2 - h1)} s` : ''],
        ['Κολύμβηση', `${F(a.avg.vClean)}<small> m/s</small>`, `συχνότητα ${F(a.avg.sr, 1)} · μήκος κύκλου ${F(a.avg.sl)} μ.`],
        ['ΔΑΚ', F(a.avg.dak), 'μ.ο. 50άρι']];
      return slFrame('v', I, 'Συμπέρασμα', 'Χωρίς ΑΡ και αναφορά: ό,τι δείχνει ο ίδιος ο αγώνας', `<div class="tl3">${tiles.map(([l, v, s]) => `<div class="tl"><div class="lab">${l}</div><div class="val">${v}</div><div class="sub">${s}</div></div>`).join('')}</div>`, n, N, 'cc2');
    }
    const parts = ['start', 'swim', 'turn', 'exchange', 'finish'].map(key => ({ key, title: K.phaseName[key], mine: a.composition[key], theirs: cm.m.composition[key] }))
      .filter(p0 => ok(p0.mine) && p0.mine > 0);
    parts.forEach(p0 => { p0.gap = p0.mine - p0.theirs; p0.pct = ok(p0.gap) ? 100 * p0.gap / p0.mine : NaN; });
    const compared = parts.filter(p0 => ok(p0.gap));
    const total = a.final - cm.m.final;
    const behind = compared.filter(p0 => p0.gap > 0.005), behindSum = behind.reduce((s0, p0) => s0 + p0.gap, 0);
    const share = behindSum > 0 ? `<div class="share2">${behind.map(p0 => { const pc = 100 * p0.gap / behindSum; return `<i style="width:${pc.toFixed(1)}%;background:${K.phase[p0.key]}">${pc >= 16 ? esc(p0.title) + ' ' + Math.round(pc) + '%' : pc >= 6 ? Math.round(pc) + '%' : ''}</i>`; }).join('')}</div>` : '';
    const lines = [];
    if (I.has.ref) lines.push(`από την αναφορά <b>${S(a.final - x.ref.final)} s</b> (${T(x.ref.final)})`);
    if (I.has.pb) lines.push(`από ${I.relay ? 'το ρεκόρ της ομάδας' : 'το ΑΡ'} <b>${S(a.final - x.pb.final)} s</b> (${T(x.pb.final)})`);
    const top = `<div class="cc2-top"><div class="big">${S(total)}<small> s</small></div><div class="txt"><b>${T(a.final)}</b><br>${lines.join('<br>')}</div></div>`;
    const tiles = `<div class="parts">${parts.map(p0 => `<div class="part"><div class="lab"><i style="background:${K.phase[p0.key]}"></i>${esc(p0.title)}</div><div class="val ${ok(p0.gap) && p0.gap > 0.005 ? 'l' : 'g'}">${ok(p0.gap) ? S(p0.gap) + '<small> s</small>' : '–'}</div><div class="sub">${T(p0.mine)} · ${cm.ref ? 'αναφ.' : esc(cm.name)} ${T(p0.theirs)}</div></div>`).join('')}</div>`;
    const topPart = compared.filter(p0 => p0.gap > 0.05).sort((p0, q0) => q0.gap - p0.gap)[0];
    const IN = { start: 'στην εκκίνηση', swim: 'στην κολύμβηση', turn: 'στις στροφές', exchange: 'στις αλλαγές', finish: 'στον τερματισμό' };
    const closing = topPart && total > 0.05 ? `Από τη διαφορά των ${F(total)} s, τα ${F(topPart.gap)} s (${Math.round(100 * topPart.gap / total)}%) είναι ${IN[topPart.key]}.` : '';
    const body = top + (share ? `<div class="sl-sub2">Το μερίδιο κάθε φάσης στον χρόνο που χάνεται</div>${share}` : '') + tiles + (closing ? `<div class="closing">${closing}</div>` : '');
    return slFrame('v', I, 'Συμπέρασμα', `Πού βρίσκεται η διαφορά από ${cm.ref ? 'την αναφορά' : 'το ' + esc(cm.name)}: εκκίνηση, κολύμβηση, στροφές, τερματισμός μαζί είναι όλος ο αγώνας`, body, n, N, 'cc2');
  }

  /** The ΔΑΚ of the 50s as bars (its two parts: the strokes and the seconds per 10 m), the means of the PB and the reference as lines. */
  function slDakBars(x, I, w, h, fs, withCmp) {
    const a = x.race, gs = slGroups(a.blocks, 8);
    const parts = a.blocks.length === gs.length;
    const vals = gs.map(gp => gp.dak).filter(ok);
    const lines = withCmp ? [[I.pbShort, x.pb.avg.dak, K.pb, 'dash'], ['Αναφ.', x.ref.avg.dak, K.ref, 'dot']].filter(q => ok(q[1])) : [];
    if (!vals.length) return '';
    const top = Math.max.apply(null, vals.concat(lines.map(q => q[1])));
    const tk = niceTicks(0, top * 1.12, 4);
    const padL = fs * 2.4, padR = lines.length ? fs * 5.2 : fs, padT = fs * 1.6, padB = fs * 2.2, iw = w - padL - padR, ih = h - padT - padB;
    const Y = v => padT + ih * (1 - v / tk.hi);
    const bw = Math.min(fs * 5, iw / gs.length * 0.58), X = i => padL + iw * (i + 0.5) / gs.length;
    let g = '';
    tk.ticks.forEach(t => { g += `<line x1="${padL}" x2="${padL + iw}" y1="${Y(t).toFixed(1)}" y2="${Y(t).toFixed(1)}" stroke="${K.grid}" stroke-width="1.5"/><text x="${padL - fs * 0.5}" y="${(Y(t) + fs * 0.33).toFixed(1)}" text-anchor="end" font-size="${fs * 0.8}" fill="${K.dim}">${t}</text>`; });
    const mean = avg(gs, 'dak');
    gs.forEach((gp, i) => {
      if (!ok(gp.dak)) return;
      const x0 = X(i) - bw / 2;
      const bk = parts ? a.blocks[i] : null;
      if (bk && ok(bk.dakStrokes) && ok(bk.dakTime)) {
        const yS = Y(bk.dakStrokes), yT = Y(bk.dakStrokes + bk.dakTime);
        g += `<rect x="${x0.toFixed(1)}" y="${yS.toFixed(1)}" width="${bw.toFixed(1)}" height="${(Y(0) - yS).toFixed(1)}" fill="#2F6FD8"/>`;
        g += `<rect x="${x0.toFixed(1)}" y="${yT.toFixed(1)}" width="${bw.toFixed(1)}" height="${(yS - yT - 3).toFixed(1)}" rx="6" fill="#F08A3E"/>`;
        g += `<text x="${X(i).toFixed(1)}" y="${((yS + Y(0)) / 2 + fs * 0.3).toFixed(1)}" text-anchor="middle" font-size="${fs * 0.75}" fill="#FFFFFF" font-weight="700">${F(bk.dakStrokes)}</text>`;
        g += `<text x="${X(i).toFixed(1)}" y="${((yT + yS) / 2 + fs * 0.3).toFixed(1)}" text-anchor="middle" font-size="${fs * 0.75}" fill="#0B111A" font-weight="700">${F(bk.dakTime)}</text>`;
      } else {
        g += `<rect x="${x0.toFixed(1)}" y="${Y(gp.dak).toFixed(1)}" width="${bw.toFixed(1)}" height="${(Y(0) - Y(gp.dak)).toFixed(1)}" rx="6" fill="#F08A3E"/>`;
      }
      const col = x.ev.style === 'im' || !ok(mean) ? K.text : gp.dak < mean - 0.05 ? K.accent : gp.dak > mean + 0.05 ? K.loss : K.text;
      g += `<text x="${X(i).toFixed(1)}" y="${(Y(gp.dak) - fs * 0.45).toFixed(1)}" text-anchor="middle" font-size="${fs * 1.15}" font-weight="800" fill="${col}">${F(gp.dak)}</text>`;
      g += `<text x="${X(i).toFixed(1)}" y="${(Y(0) + fs * 1.4).toFixed(1)}" text-anchor="middle" font-size="${fs * 0.8}" fill="${K.dim}">${gp.from}–${gp.to}</text>`;
    });
    const labs = lines.map(([name, v, col]) => ({ y: Y(v), name, v, col })).sort((p0, q0) => p0.y - q0.y);
    for (let i = 1; i < labs.length; i++) if (labs[i].y - labs[i - 1].y < fs * 0.95) labs[i].y = labs[i - 1].y + fs * 0.95;
    lines.forEach(([name, v, col, d]) => {
      g += `<line x1="${padL}" x2="${padL + iw}" y1="${Y(v).toFixed(1)}" y2="${Y(v).toFixed(1)}" stroke="${col}" stroke-width="3" stroke-dasharray="${dashK(d)}" stroke-linecap="round"/>`;
    });
    labs.forEach(l => { g += `<text x="${padL + iw + fs * 0.4}" y="${(l.y + fs * 0.33).toFixed(1)}" font-size="${fs * 0.75}" fill="${l.col}">${esc(l.name)} ${F(l.v)}</text>`; });
    g += `<line x1="${padL}" x2="${padL + iw}" y1="${Y(0)}" y2="${Y(0)}" stroke="${K.axis}" stroke-width="2"/>`;
    return `<svg class="slc" viewBox="0 0 ${w} ${h}" width="${w}" height="${h}" xmlns="http://www.w3.org/2000/svg">${g}</svg>`;
  }

  function vDak(x, I, R, n, N) {
    const a = x.race, b = x.pb, c = x.ref, gs = slGroups(a.blocks, 8).filter(gp => ok(gp.dak));
    const best = gs.length ? gs.reduce((p0, q0) => q0.dak < p0.dak ? q0 : p0) : null, worst = gs.length ? gs.reduce((p0, q0) => q0.dak > p0.dak ? q0 : p0) : null;
    const left = `<div class="dk-l"><div class="lab">μ.ο. 50άρι</div><div class="dk-big">${F(a.avg.dak)}</div>${slVs(I, a.avg.dak, b.avg.dak, c.avg.dak, 'lower', v => F(v))}
      ${best && worst && best !== worst ? `<div class="dk-bw"><div><span>καλύτερο</span><b>${F(best.dak)}</b><em>${best.from}–${best.to}${I.u}</em></div><div><span>χειρότερο</span><b>${F(worst.dak)}</b><em>${worst.from}–${worst.to}${I.u}</em></div></div>` : ''}
      <div class="dk-key"><span><i style="background:#2F6FD8"></i>χεριές ανά 10 μ.</span><span><i style="background:#F08A3E"></i>δευτερόλεπτα ανά 10 μ.</span></div></div>`;
    const body = `<div class="dk">${left}<div class="dk-r">${slDakBars(x, I, 1160, 640, 30, true)}</div></div>`;
    return slFrame('v', I, 'ΔΑΚ · ο βασικός δείκτης', 'Χεριές + δευτερόλεπτα ανά 10 μ. κάθε 50άρι, από τα 15 μ. ως τον τοίχο · όσο μικρότερος, τόσο καλύτερα', body, n, N, 'dkp');
  }

  function vComposition(x, I, R, n, N) {
    const keys = ['start', 'swim', 'turn', 'exchange', 'finish'];
    const rows = [['Αυτός ο αγώνας', x.race], I.has.pb ? [I.pbName, x.pb] : null, I.has.ref ? [I.refName, x.ref] : null].filter(q => q && keys.some(k0 => ok(q[1].composition[k0]) && q[1].composition[k0] > 0));
    const span = Math.max.apply(null, rows.map(([, m]) => keys.reduce((s0, k0) => s0 + (ok(m.composition[k0]) ? m.composition[k0] : 0), 0)));
    const bars = rows.map(([name, m], i) => {
      const segs = keys.filter(k0 => ok(m.composition[k0]) && m.composition[k0] > 0).map(k0 => {
        const t = m.composition[k0], wpc = 100 * t / span;
        return `<i style="width:${wpc.toFixed(2)}%;background:${K.phase[k0]}">${wpc >= 14 ? `<span>${esc(K.phaseName[k0])}</span><b>${T(t)}</b>` : wpc >= 7 ? `<b>${T(t)}</b>` : ''}</i>`;
      }).join('');
      return `<div class="cp-r${i === 0 ? ' main' : ''}"><div class="cp-n">${esc(name)}</div><div class="cp-b">${segs}</div><div class="cp-t">${T(m.final)}</div></div>`;
    }).join('');
    const legend = `<div class="cp-lg">${keys.filter(k0 => ok(x.race.composition[k0]) && x.race.composition[k0] > 0).map(k0 => `<span><i style="background:${K.phase[k0]}"></i>${esc(K.phaseName[k0])}${k0 === 'start' || k0 === 'finish' ? ' ' + T(x.race.composition[k0]) : ''}</span>`).join('')}</div>`;
    // Where time was won and lost, phase by phase, as on the page: against the PB (the reference beside it), else the reference.
    const pb = phaseBase(I);
    let div = '';
    const gs = pb ? groups(x) : [];
    if (pb && gs.length <= 16) {
      const items = gs.map(g0 => ({ label: g0.label, v: g0.a - g0[pb.key], o: pb.other ? g0.a - g0[pb.other] : NaN }));
      const w = 1728, h = 300, fs = 24, top = fs * 1.5, bottom = fs * 3.1, ih = h - top - bottom;
      const m0 = Math.max.apply(null, items.map(it => ok(it.v) ? Math.abs(it.v) : 0).concat([0.05]));
      const zero = top + ih / 2, Yh = v => ih / 2 * Math.min(1, Math.abs(v) / m0);
      const cw = w / items.length, bw = Math.min(fs * 3.2, cw * 0.5);
      let g = `<line x1="0" x2="${w}" y1="${zero}" y2="${zero}" stroke="${K.axis}" stroke-width="2"/>`;
      items.forEach((it, i) => {
        const cx = cw * (i + 0.5);
        if (ok(it.v)) {
          const hh = Math.max(3, Yh(it.v)), up = it.v > 0;
          g += `<rect x="${(cx - bw / 2).toFixed(1)}" y="${(up ? zero - hh : zero).toFixed(1)}" width="${bw.toFixed(1)}" height="${hh.toFixed(1)}" rx="5" fill="${up ? '#F08A3E' : '#4C8DFF'}"/>`;
          g += `<text x="${cx.toFixed(1)}" y="${(up ? zero - hh - fs * 0.45 : zero + hh + fs * 1.05).toFixed(1)}" text-anchor="middle" font-size="${fs}" font-weight="800" fill="${up ? K.loss : K.accent}">${S(it.v)}</text>`;
        }
        const sp = it.label.indexOf(' ');
        const l1 = sp > 0 ? it.label.slice(0, sp) : it.label, l2 = sp > 0 ? it.label.slice(sp + 1) : '';
        g += `<text x="${cx.toFixed(1)}" y="${(h - bottom + fs * 1.5).toFixed(1)}" text-anchor="middle" font-size="${fs * 0.85}" font-weight="700" fill="${K.soft}">${esc(l1)}</text>`;
        if (l2) g += `<text x="${cx.toFixed(1)}" y="${(h - bottom + fs * 2.6).toFixed(1)}" text-anchor="middle" font-size="${fs * 0.8}" fill="${K.dim}">${esc(l2)}</text>`;
      });
      div = `<div class="sl-sub2">Πού κερδήθηκε και πού χάθηκε χρόνος · από ${pb.key === 'pb' ? 'το ' + esc(I.pbShort) : 'την αναφορά'} (μπλε κέρδος, πορτοκαλί απώλεια)</div>` +
        `<svg class="slc" viewBox="0 0 ${w} ${h}" width="${w}" height="${h}" xmlns="http://www.w3.org/2000/svg">${g}</svg>`;
    }
    return slFrame('v', I, 'Σύνθεση του χρόνου', `Εκκίνηση 0–15${I.u}, καθαρή κολύμβηση, στροφές −5/+15${I.u}, τερματισμός (τελευταία 5${I.u})`, bars + legend + div, n, N, 'cp');
  }

  function vSpeed(x, I, R, n, N) {
    const a = x.race, cats = a.rows.map(r => String(r.d));
    const s3 = [{ values: a.rows.map(r => r.vel), color: K.race, main: true }];
    if (I.has.pb) s3.push({ values: x.pb.rows.map(r => r.vel), color: K.pb, dash: 'dash', width: 4 });
    if (I.has.ref) s3.push({ values: x.ref.rows.map(r => r.vel), color: K.ref, dash: 'dot', width: 4 });
    const chart = slLine({ w: 1728, h: 600, fs: 28, cats, series: s3, xTitle: 'απόσταση (' + I.uu.trim() + ')', every: cats.length > 14 ? 2 : 1 });
    return slFrame('v', I, 'Ταχύτητα ανά τμήμα', 'Η μέση ταχύτητα κάθε τμήματος (m/s), από την εκκίνηση ως τον τερματισμό', chart + slLegend(I), n, N, 'sp');
  }

  function vPace(x, I, R, n, N) {
    const a = x.race, b = x.pb, c = x.ref, bl = a.blocks, cats = bl.map(bk => String(bk.to));
    const mean = avg(bl.slice(1), 'time');
    const s3 = [{ values: bl.map(bk => bk.time), color: K.race, main: true }];
    if (I.has.pb) s3.push({ values: b.blocks.map(bk => bk.time), color: K.pb, dash: 'dash', width: 4 });
    if (I.has.ref) s3.push({ values: c.blocks.map(bk => bk.time), color: K.ref, dash: 'dot', width: 4 });
    const chart = slLine({ w: 1120, h: 640, fs: 28, cats, series: s3, fmt: v => T(v), tick: v => T(v), xTitle: '50άρι (' + I.uu.trim() + ')', every: Math.max(1, Math.ceil(bl.length / 10)), dotsMax: 24,
      ref: I.im || I.relay || !ok(mean) ? null : { value: mean, label: 'μ.ο.' } });
    let tiles = '';
    if (!I.im && !I.relay) {
      const nb = bl.length, hh = Math.floor(nb / 2), h1 = sum(bl.slice(0, hh), 'time'), h2 = sum(bl.slice(hh), 'time');
      const timed = bl.slice(1).filter(bk => ok(bk.time));
      const sd = Math.sqrt(avg(timed, bk => (bk.time - mean) * (bk.time - mean)));
      const fast = timed.length ? timed.reduce((p0, q0) => q0.time < p0.time ? q0 : p0) : null, slow = timed.length ? timed.reduce((p0, q0) => q0.time > p0.time ? q0 : p0) : null;
      const rel = (h2 - h1) / (h1 + h2);
      const type = !ok(rel) ? '–' : rel < -0.002 ? 'Αρνητικός' : rel < 0.012 ? (bl[nb - 1].time < mean - 0.3 ? 'Σταθερός, δυνατό τέλος' : 'Σταθερός') : 'Θετικός';
      const last2 = sum(bl.slice(-2), 'time');
      tiles = [['Ρυθμός αγώνα', type, ok(h2 - h1) ? `2ο μισό ${S(h2 - h1)} s` : ''], ['Διακύμανση 50άρι', ok(sd / mean) ? (100 * sd / mean).toFixed(1) + '<small> %</small>' : '–', `τυπική απόκλιση ${F(sd)} s`],
        fast ? ['Πιο γρήγορο / αργό', `${T(fast.time)}<small> / </small>${T(slow.time)}`, `στα ${fast.to} και ${slow.to}${I.u}`] : null,
        ['Τελευταίο 100άρι', T(last2) + '<small> s</small>', slVs(I, last2, sum(b.blocks.slice(-2), 'time'), sum(c.blocks.slice(-2), 'time'), 'lower', v => T(v))]]
        .filter(q => q).map(([l, v, s]) => `<div class="tl"><div class="lab">${l}</div><div class="val">${v}</div><div class="sub">${s}</div></div>`).join('');
    }
    const body = `<div class="pc"><div class="pc-l">${chart}${slLegend(I)}</div><div class="pc-r">${tiles}</div></div>`;
    return slFrame('v', I, 'Ρυθμός αγώνα', 'Ο χρόνος κάθε 50άρι' + (I.im || I.relay ? '' : ' · η διακεκομμένη: ο μέσος όρος από το 2ο 50άρι'), body, n, N, 'pcp');
  }

  /** The clean swimming of the segments (the rows with a rate): the speed, the rate and the cycle length of the race, the PB, the reference. */
  function slSwimRows(m) { return m.rows.filter(r => ok(r.sr)); }

  function vStrokes(x, I, R, n, N) {
    const a = x.race, b = x.pb, c = x.ref;
    const rowsA = slSwimRows(a);
    const cats = rowsA.map(r => String(r.d));
    const pick = (m, f) => { const by = {}; slSwimRows(m).forEach(r => { by[r.d] = f(r); }); return rowsA.map(r => ok(by[r.d]) ? by[r.d] : NaN); };
    const panel = (title, unit, f, va, vb, vc, better, nd) => {
      const s3 = [{ values: rowsA.map(f), color: K.race, main: true, width: 4 }];
      if (I.has.pb) s3.push({ values: pick(b, f), color: K.pb, dash: 'dash', width: 3, dots: false });
      if (I.has.ref) s3.push({ values: pick(c, f), color: K.ref, dash: 'dot', width: 3, dots: false });
      return `<div class="sk"><div class="lab">${esc(title)}</div><div class="val">${F(va, nd)}<small> ${unit}</small></div>${slVs(I, va, vb, vc, better, v => F(v, nd), nd)}
        ${slLine({ w: 540, h: 420, fs: 22, cats, series: s3, fmt: v => F(v, nd), end: false, every: Math.max(1, Math.ceil(cats.length / 6)) })}</div>`;
    };
    const body = `<div class="sks">${panel('Ταχύτητα κολύμβησης', 'm/s', r => r.vel, a.avg.vClean, b.avg.vClean, c.avg.vClean, 'higher', 2)}
      ${panel('Συχνότητα', 'κύκλ./λ.', r => r.sr, a.avg.sr, b.avg.sr, c.avg.sr, 'none', 1)}
      ${panel('Μήκος κύκλου', 'μ.', r => r.sl, a.avg.sl, b.avg.sl, c.avg.sl, 'higher', 2)}</div>` + slLegend(I);
    return slFrame('v', I, 'Χεριές', 'Η καθαρή κολύμβηση: 15 μ. μετά τον τοίχο ως 5 μ. πριν τον επόμενο · ταχύτητα = συχνότητα × μήκος κύκλου', body, n, N, 'skp');
  }

  function slStartTrack(s, I, w, fs) {
    const h = fs * 7.4, x0 = fs * 1.2, x1 = w - fs * 1.6, Xd = d => x0 + (x1 - x0) * d / 15, yT = fs * 3.3, th = fs * 1.5;
    const ev = [{ d: 0, t: s.block, name: 'Βατήρας' }, ok(s.entryDist) ? { d: s.entryDist, t: s.entryT, name: 'Είσοδος' } : null,
      ok(s.bd) ? { d: s.bd, t: s.bt, name: 'Έξοδος' } : null, { d: 15, t: s.t15, name: '15' + I.u }].filter(q => q);
    let g = `<rect x="${x0}" y="${yT}" width="${x1 - x0}" height="${th}" rx="${th / 2}" fill="rgba(255,255,255,0.08)"/>`;
    const names = { 'Βατήρας': 'Πτήση', 'Είσοδος': 'Υποβρύχια φάση', 'Έξοδος': 'Κολύμβηση' };
    const cols = { 'Πτήση': '#5A8EDC', 'Υποβρύχια φάση': '#2BB3A0', 'Κολύμβηση': '#F08A3E' };
    const vel = { 'Πτήση': s.sigEntryVel, 'Υποβρύχια φάση': s.entryBoVel, 'Κολύμβηση': s.bo15Vel };
    for (let i = 0; i < ev.length - 1; i++) {
      const A = ev[i], B = ev[i + 1], nm = names[A.name] || '', xa = Xd(A.d), xb = Xd(B.d);
      g += `<rect x="${(xa + 3).toFixed(1)}" y="${yT}" width="${Math.max(2, xb - xa - 6).toFixed(1)}" height="${th}" rx="${th / 2}" fill="${cols[nm] || '#5A8EDC'}"/>`;
      const label = nm === 'Υποβρύχια φάση' && ok(s.kicks) ? `${nm} · ${s.kicks} δελφ.` : nm;
      if (xb - xa > fs * 4) g += `<text x="${((xa + xb) / 2).toFixed(1)}" y="${(yT + th * 0.5 + fs * 0.33).toFixed(1)}" text-anchor="middle" font-size="${fs * 0.8}" font-weight="800" fill="#0B111A">${esc(xb - xa > fs * 9 ? label : nm.split(' ')[0])}</text>`;
      if (ok(vel[nm])) g += `<text x="${((xa + xb) / 2).toFixed(1)}" y="${(yT + th + fs * 1.75).toFixed(1)}" text-anchor="middle" font-size="${fs * 0.95}" font-weight="800" fill="#FFFFFF">${F(vel[nm])} <tspan font-size="${fs * 0.7}" font-weight="600" fill="${K.dim}">m/s</tspan></text>`;
    }
    ev.forEach((e, i) => {
      const xx = Xd(e.d), anchor = i === 0 ? 'start' : i === ev.length - 1 ? 'end' : 'middle';
      g += `<line x1="${xx.toFixed(1)}" x2="${xx.toFixed(1)}" y1="${yT - fs * 0.5}" y2="${yT + th + fs * 0.5}" stroke="#FFFFFF" stroke-width="3"/>`;
      g += `<text x="${xx.toFixed(1)}" y="${(yT - fs * 1.75).toFixed(1)}" text-anchor="${anchor}" font-size="${fs * 0.8}" fill="${K.dim}" font-weight="700">${esc(upper(e.name))}</text>`;
      g += `<text x="${xx.toFixed(1)}" y="${(yT - fs * 0.6).toFixed(1)}" text-anchor="${anchor}" font-size="${fs * 1.05}" fill="#FFFFFF" font-weight="800">${F(e.t)} s${e.d > 0 && e.d < 15 ? ` <tspan fill="${K.cyan}">· ${F(e.d)}${I.u}</tspan>` : ''}</text>`;
    });
    return `<svg class="slc" viewBox="0 0 ${w} ${h}" width="${w}" height="${h}" xmlns="http://www.w3.org/2000/svg">${g}</svg>`;
  }

  function vStart(x, I, R, n, N) {
    const a = x.race, b = x.pb, c = x.ref, s = a.start, f = a.finish;
    const t = (lab, v, unit, vb, vc, better, nd) => `<div class="tl"><div class="lab">${lab}</div><div class="val">${F(v, nd)}<small> ${unit}</small></div>${slVs(I, v, vb, vc, better, q => F(q, nd), nd)}</div>`;
    const startTiles = [ok(s.block) ? t('Βατήρας', s.block, 's', b.start.block, c.start.block, 'lower') : '',
      ok(s.entryDist) ? t('Είσοδος', s.entryDist, 'μ.', b.start.entryDist, c.start.entryDist, 'higher') : '',
      ok(s.kicks) ? `<div class="tl"><div class="lab">Δελφινισμοί</div><div class="val">${F(s.kicks, 0)}</div>${slVs(I, s.kicks, b.start.kicks, c.start.kicks, 'none', q => F(q, 0), 0)}</div>` : '',
      ok(s.bd) ? t('Έξοδος', s.bd, 'μ.', b.start.bd, c.start.bd, 'higher') : '',
      t('15' + I.u, s.t15, 's', b.start.t15, c.start.t15, 'lower')].join('');
    const finTiles = [t('Τελευταία 5' + I.u, f.last5, 's', b.finish.last5, c.finish.last5, 'lower'),
      ok(f.lastGap) ? t('Τελ. χεριά → επαφή', f.lastGap, 's', b.finish.lastGap, c.finish.lastGap, 'lower') : '',
      ok(f.vLast5) ? t('Ταχύτητα τελ. 5' + I.u, f.vLast5, 'm/s', b.finish.vLast5, c.finish.vLast5, 'higher') : '',
      ok(f.lastXt) ? t(`Τελευταία ${f.lastX}${I.u}`, f.lastXt, 's', b.finish.lastXt, c.finish.lastXt, 'lower') : ''].join('');
    const body = `<div class="trk">${slStartTrack(s, I, 1728, 28)}</div><div class="tl5">${startTiles}</div><div class="sl-sub2">Τερματισμός</div><div class="tl4">${finTiles}</div>`;
    return slFrame('v', I, 'Εκκίνηση και τερματισμός', `Από το σήμα ως τα 15${I.u}: οι χρόνοι από το σήμα, οι αποστάσεις από τον τοίχο`, body, n, N, 'stp');
  }

  function vTurns(x, I, R, n, N) {
    const a = x.race, tA = realTurns(a), tB = realTurns(x.pb), tC = realTurns(x.ref);
    const mean = avg(tA, 'inOut');
    const foot = `<div class="tr-m"><span>Μέσος όρος <b>${F(mean)} s</b></span>${slVs(I, mean, avg(tB, 'inOut'), avg(tC, 'inOut'), 'lower', v => F(v))}</div>`;
    const title = `Στροφές (${tA.length})`, sub = `Από 5${I.u} πριν ως 15${I.u} μετά τον τοίχο: η άφιξη, η στροφή και η έξοδος`;
    if (tA.length > 3) {
      // Many turns: each one in the race (the −5/+15), as the chart of the page, and the numbers of all of them.
      const cats = tA.map(t => String(t.wall));
      const s3 = [{ values: tA.map(t => t.inOut), color: K.race, main: true }];
      if (I.has.pb) s3.push({ values: tA.map((t, i) => tB[i] ? tB[i].inOut : NaN), color: K.pb, dash: 'dash', width: 4, dots: false });
      if (I.has.ref) s3.push({ values: tA.map((t, i) => tC[i] ? tC[i].inOut : NaN), color: K.ref, dash: 'dot', width: 4, dots: false });
      const chart = slLine({ w: 1180, h: 560, fs: 26, cats, series: s3, xTitle: 'τοίχος (' + I.uu.trim() + ')', every: Math.max(1, Math.ceil(cats.length / 10)), ticks: 4, dotsMax: 24 });
      const timed = tA.filter(t => ok(t.inOut));
      const best = timed.length ? timed.reduce((p0, q0) => q0.inOut < p0.inOut ? q0 : p0) : null, worst = timed.length ? timed.reduce((p0, q0) => q0.inOut > p0.inOut ? q0 : p0) : null;
      const tiles = [['Μέσος όρος', `${F(mean)}<small> s</small>`, slVs(I, mean, avg(tB, 'inOut'), avg(tC, 'inOut'), 'lower', v => F(v))],
        best ? ['Η καλύτερη', `${F(best.inOut)}<small> s</small>`, `στα ${best.wall}${I.u}`] : null, worst ? ['Η πιο αργή', `${F(worst.inOut)}<small> s</small>`, `στα ${worst.wall}${I.u}`] : null,
        ['Έξοδος · δελφινισμοί', `${F(avg(tA, 'bd'))}<small>${I.u}</small> · ${F(avg(tA, 'kicks'), 1)}`, 'μέσοι όροι']].filter(q => q)
        .map(([l, v, s0]) => `<div class="tl"><div class="lab">${l}</div><div class="val">${v}</div><div class="sub">${s0}</div></div>`).join('');
      return slFrame('v', I, title, sub, `<div class="pc"><div class="pc-l">${chart}${slLegend(I)}</div><div class="pc-r">${tiles}</div></div>`, n, N, 'pcp');
    }
    const max = Math.max.apply(null, tA.map(t => ok(t.in5) && ok(t.out15) ? t.in5 + t.out15 : ok(t.inOut) ? t.inOut : 0).concat([1]));
    const row = (t, i) => {
      const cmp = slVs(I, t.inOut, tB[i] ? tB[i].inOut : NaN, tC[i] ? tC[i].inOut : NaN, 'lower', v => F(v));
      const w5 = ok(t.in5) ? 100 * t.in5 / max : 0, w15 = ok(t.out15) ? 100 * t.out15 / max : 0;
      return `<div class="tr"><div class="tr-w">${t.wall}<small>${I.u}</small></div>
        <div class="tr-b"><i class="in" style="width:${w5.toFixed(1)}%">${F(t.in5)}</i><i class="out" style="width:${w15.toFixed(1)}%">${F(t.out15)}</i></div>
        <div class="tr-t"><div>${F(t.inOut)}<small> s</small></div>${cmp}</div>
        <div class="tr-x"><span>${ok(t.bd) ? F(t.bd) + '<small>' + I.u + '</small>' : '–'}</span><span>${ok(t.kicks) ? F(t.kicks, 0) : '–'}</span><span>${ok(t.uw) ? F(t.uw) + '<small> s</small>' : '–'}</span></div></div>`;
    };
    const head = `<div class="tr h"><div>τοίχος</div><div class="tr-hb"><span><i class="in"></i>−5 → τοίχος</span><span><i class="out"></i>τοίχος → +15</span></div><div>−5 / +15</div><div class="tr-x"><span>έξοδος</span><span>δελφ.</span><span>υποβρύχια</span></div></div>`;
    return slFrame('v', I, title, sub, `<div class="trs${tA.length > 3 ? ' c4' : ''}">${head}${tA.map(row).join('')}</div>` + foot, n, N, 'trp');
  }

  function vSplits(x, I, R, n, N) {
    const a = x.race, D = I.D, step = D <= 400 ? 50 : D <= 800 ? 100 : 200;
    const rows = [];
    for (let s0 = 0; s0 < D; s0 += step) {
      const e = Math.min(D, s0 + step), split = tOf(a, e) - tOf(a, s0);
      const halves = step === 50 ? [tOf(a, s0 + 25) - tOf(a, s0), tOf(a, e) - tOf(a, s0 + 25)] : [];
      const bls = a.blocks.filter(bk => bk.from >= s0 - 1e-9 && bk.to <= e + 1e-9);
      rows.push({ s0, e, cum: tOf(a, e), split, halves, v: split > 0 ? (e - s0) * a.k / split : NaN, strokes: sum(bls, 'strokes'), dak: avg(bls, 'dak'),
        pb: tOf(x.pb, e) - tOf(x.pb, s0), ref: tOf(x.ref, e) - tOf(x.ref, s0) });
    }
    const size = rows.length <= 4 ? ' xl' : rows.length <= 6 ? '' : ' sm';
    const tbl = `<table class="spl${size}"><thead><tr><th>${step}άρι</th><th>Χρόνος</th><th>Ενδιάμεσος</th>${step === 50 ? '<th>25 + 25</th>' : ''}<th>m/s</th><th>Χεριές</th><th>ΔΑΚ</th>${I.has.pb ? `<th>Δ ${esc(I.pbShort)}</th>` : ''}${I.has.ref ? '<th>Δ Αναφ.</th>' : ''}</tr></thead><tbody>
      ${rows.map(r => `<tr><td class="l">${r.s0}–${r.e}</td><td class="c">${T(r.cum)}</td><td class="b">${T(r.split)}</td>${step === 50 ? `<td>${r.halves.every(ok) ? `${F(r.halves[0])} + ${F(r.halves[1])}` : '–'}</td>` : ''}<td>${F(r.v)}</td><td>${ok(r.strokes) ? F(r.strokes, 0) : '–'}</td><td>${F(r.dak)}</td>${I.has.pb ? `<td>${sChip(r.split - r.pb, 'lower') || '–'}</td>` : ''}${I.has.ref ? `<td>${sChip(r.split - r.ref, 'lower') || '–'}</td>` : ''}</tr>`).join('')}
      </tbody></table>`;
    return slFrame('v', I, 'Περάσματα', `Ο χρόνος, ο ενδιάμεσος και η ταχύτητα ανά ${step}${I.u}`, tbl, n, N, 'spp');
  }

  function vLegs(x, I, R, n, N) {
    const a = x.race, legs = a.legs || [];
    const cl = I.has.ref && x.ref.legs ? x.ref.legs : I.has.pb && x.pb.legs ? x.pb.legs : null;
    const cname = I.has.ref ? 'Αναφ.' : esc(I.pbShort);
    const tiles = legs.map((l, i) => `<div class="tl"><div class="lab">Σκέλος ${i + 1} · ${esc(STYLE_NAME[l.style] || '')}</div>${(I.swimmers || [])[i] ? `<div class="who">${esc(I.swimmers[i])}</div>` : ''}
      <div class="val">${T(l.split)}</div>${cl && ok(cl[i].split) ? `<div class="sub">${cname} ${T(cl[i].split)} ${sChip(l.split - cl[i].split, 'lower')}</div>` : ''}
      <div class="more"><span><em>${i > 0 ? 'αλλαγή' : 'βατήρας'}</em>${i > 0 ? (ok(l.ex) ? F(l.ex) + ' s' : '–') : (ok(l.reaction) ? F(l.reaction) + ' s' : '–')}</span><span><em>ΔΑΚ</em>${F(l.dak)}</span><span><em>κολύμβηση</em>${F(l.vClean)} m/s</span></div></div>`).join('');
    return slFrame('v', I, 'Τα σκέλη', 'Ο χρόνος κάθε σκέλους, η αλλαγή, ο ΔΑΚ και η ταχύτητα της κολύμβησης', `<div class="tl4 fill">${tiles}</div>`, n, N, 'lgp');
  }

  function vStyles(x, I, R, n, N) {
    const a = x.race, step = I.D / 4;
    const tiles = [0, 1, 2, 3].map(i => {
      const s0 = i * step, e = s0 + step, t = m => tOf(m, e) - tOf(m, s0);
      const bl = a.blocks.filter(bk => bk.from >= s0 - 1e-9 && bk.to <= e + 1e-9);
      return `<div class="tl"><div class="lab">${esc(STYLE_NAME[IM_ORDER[i]])} · ${s0}–${e}${I.u}</div><div class="val">${T(t(a))}</div>${slVs(I, t(a), t(x.pb), t(x.ref), 'lower', v => T(v))}
        <div class="more"><span><em>ΔΑΚ</em>${F(avg(bl, 'dak'))}</span><span><em>συχνότητα</em>${F(avg(bl, 'sr'), 1)}</span><span><em>μήκος κύκλου</em>${F(avg(bl, 'sl'))} μ.</span></div></div>`;
    }).join('');
    return slFrame('v', I, 'Ανά στυλ', 'Ο χρόνος, ο ΔΑΚ, η συχνότητα και το μήκος κύκλου κάθε στυλ της μικτής', `<div class="tl4 fill">${tiles}</div>`, n, N, 'lgp');
  }

  // ---- 4:5: the posts (the race's own numbers only)
  function sCover(x, I, n, N) {
    const a = x.race, d = x.details || {};
    const where = [d.competition, (d.meta || []).find(m0 => /^\d{1,2}\.\d{1,2}\.\d{4}$/.test(m0)) || ''].filter(q => q).map(esc).join(' · ');
    return `<section class="sl s cover">
      <div class="sl-strip"></div>
      <div class="cv-top"><img class="cv-logo" src="{{LOGO}}" alt=""><div class="cv-kick">ΑΝΑΛΥΣΗ ΑΓΩΝΑ<br><span>RACE ANALYSIS</span></div></div>
      <div class="cv-frame"><div class="cv-img"></div>${I.round ? `<span class="pill">${esc(upper(I.round))}${(d.meta || []).find(m0 => /^Διαδρομή/.test(m0)) ? ' · ' + esc(upper((d.meta || []).find(m0 => /^Διαδρομή/.test(m0)))) : ''}</span>` : ''}</div>
      <div class="cv-ev">${esc(I.title)}${I.kind ? ` <span>${esc(I.kind)}</span>` : ''}</div>
      <div class="cv-name">${esc(I.who)}</div>
      ${d.club ? `<div class="cv-club">${esc(d.club)}</div>` : ''}
      <div class="cv-time"><div class="lab">Τελικός χρόνος</div><div class="big">${T(a.final)}</div></div>
      <div class="cv-where">${where}</div>
      <div class="sl-ft"><span>${esc(footText)}</span><span class="sl-n">${n} / ${N}</span></div>
    </section>`;
  }

  function sSplits(x, I, n, N) {
    // The shortest step with at most 4 (to 100) or 6 parts, all of them timed: 25, 50, 100, … (the 25s of a 50 beside it).
    const a = x.race, D = I.D, want = D <= 100 ? 4 : 6;
    const timedAt = st => { for (let d = st; d <= D + 1e-9; d += st) if (!ok(tOf(a, d))) return false; return true; };
    const step = [25, 50, 75, 100, 150, 200, 250, 275, 300, 400, 500].find(st => D % st === 0 && D / st <= want && timedAt(st)) || D;
    const rows = [];
    for (let s0 = 0; s0 < D; s0 += step) {
      const e = Math.min(D, s0 + step), split = tOf(a, e) - tOf(a, s0);
      const halves = step === 50 && ok(tOf(a, s0 + 25)) ? [tOf(a, s0 + 25) - tOf(a, s0), tOf(a, e) - tOf(a, s0 + 25)] : [];
      rows.push({ s0, e, split, cum: tOf(a, e), halves, v: split > 0 ? (e - s0) * a.k / split : NaN });
    }
    const vs = rows.map(r => r.v).filter(ok), vmax = Math.max.apply(null, vs), vmin = Math.min.apply(null, vs);
    const list = rows;
    const big = list.length <= 4 ? ' big' : list.length <= 5 ? '' : ' sm';
    const body = `<div class="ss${big}">${list.map(r => `<div class="ss-r"><div class="ss-d">${r.e}<small>${I.u}</small></div>
      <div class="ss-m"><div class="ss-v">${T(r.split)}${r.halves.length && r.halves.every(ok) ? `<em>${F(r.halves[0])} + ${F(r.halves[1])}</em>` : ''}</div>
        <div class="ss-bar"><span class="tr0"><i style="width:${ok(r.v) ? (35 + 65 * (r.v - vmin) / Math.max(1e-9, vmax - vmin)).toFixed(1) : 0}%"></i></span><span class="vv">${F(r.v)} <small>m/s</small></span></div></div>
      <div class="ss-c"><small>σύνολο</small>${T(r.cum)}</div></div>`).join('')}</div>`;
    return slFrame('s', I, 'Περάσματα', `Ο ενδιάμεσος κάθε ${step}${I.u}${rows.some(r => r.halves.length) ? ' (τα δύο 25άρια)' : ''}, η ταχύτητά του και ο χρόνος ως εκεί`, body, n, N, 'ssp');
  }

  function sSpeed(x, I, n, N) {
    const a = x.race;
    // (2.26.1) Every segment from the signal to the finish, the start (0–15) first, as the PDF and the slide of the video have them (the
    // analyst: «δεν είχε την ταχύτητα της εκκίνησης»); the start also in its own tile.
    const rows = a.rows;
    const cats = rows.map(r => String(r.d));
    const mean = I.D * a.k / a.final;
    const chart = slLine({ w: 936, h: 640, fs: 26, cats, series: [{ values: rows.map(r => r.vel), color: K.race, main: true, width: 5 }], ref: { value: mean, label: 'μέση' },
      xTitle: 'απόσταση (' + I.uu.trim() + ')', every: Math.max(1, Math.ceil(cats.length / 7)), end: false });
    const s15 = a.rows.find(r => r.prevD === 0);
    const tiles = [['Μέση ταχύτητα', `${F(mean)}<small> m/s</small>`, `${I.D}${I.u} σε ${T(a.final)}`], ['Κολύμβηση', `${F(a.avg.vClean)}<small> m/s</small>`, 'χωρίς εκκίνηση, στροφές, τερματισμό'],
      s15 && ok(s15.vel) ? ['Εκκίνηση', `${F(s15.vel)}<small> m/s</small>`, `0–${s15.d}${I.u} σε ${F(s15.seg)} s`] : null].filter(q => q)
      .map(([l, val, s0]) => `<div class="tl"><div class="lab">${l}</div><div class="val">${val}</div><div class="sub">${s0}</div></div>`).join('');
    return slFrame('s', I, 'Ταχύτητα', `Η ταχύτητα κάθε τμήματος (m/s) από το σήμα ως τον τερματισμό· οι κορυφές: η εκκίνηση και η ώθηση από κάθε τοίχο`,
      `<div class="chw">${chart}</div><div class="tl3">${tiles}</div>`, n, N, 'spd');
  }

  function sStart(x, I, n, N) {
    const s = x.race.start;
    const t = (lab, v, unit, nd, sub) => ok(v) ? `<div class="tl"><div class="lab">${lab}</div><div class="val">${F(v, nd)}<small> ${unit}</small></div>${sub ? `<div class="sub">${sub}</div>` : ''}</div>` : '';
    const tiles = [t('Βατήρας', s.block, 's', 2, 'από το σήμα ως τα πόδια'), t('Είσοδος', s.entryDist, 'μ.', 2, ok(s.entryT) ? `στα ${F(s.entryT)} s` : ''), t('Δελφινισμοί', s.kicks, '', 0, 'υποβρύχια'),
      t('Έξοδος', s.bd, 'μ.', 2, ok(s.bt) ? `στα ${F(s.bt)} s` : ''), t('15' + I.u, s.t15, 's', 2, 'από το σήμα'), t('Έξοδος → 15' + I.u, s.bo15Vel, 'm/s', 2, 'ταχύτητα')].join('');
    return slFrame('s', I, 'Εκκίνηση', `Από το σήμα ως τα 15${I.u}`, `<div class="trk">${slStartTrack(s, I, 936, 24)}</div><div class="tl6">${tiles}</div>`, n, N, 'sst');
  }

  function sTurns(x, I, n, N) {
    const tA = realTurns(x.race);
    const mean = avg(tA, 'inOut');
    if (tA.length > 4) {
      const cats = tA.map(t => String(t.wall));
      const chart = slLine({ w: 936, h: 620, fs: 26, cats, series: [{ values: tA.map(t => t.inOut), color: K.race, main: true, width: 5 }], ref: { value: mean, label: 'μ.ο.' },
        xTitle: 'τοίχος (' + I.uu.trim() + ')', every: Math.max(1, Math.ceil(cats.length / 7)), end: false, dotsMax: 24 });
      const timed = tA.filter(t => ok(t.inOut));
      const best = timed.length ? timed.reduce((p0, q0) => q0.inOut < p0.inOut ? q0 : p0) : null;
      const tiles = [['Μέσος όρος', `${F(mean)}<small> s</small>`, `${tA.length} στροφές`], best ? ['Η καλύτερη', `${F(best.inOut)}<small> s</small>`, `στα ${best.wall}${I.u}`] : null,
        ['Έξοδος', `${F(avg(tA, 'bd'))}<small>${I.u}</small>`, `${F(avg(tA, 'kicks'), 1)} δελφινισμοί`]].filter(q => q)
        .map(([l, v, s0]) => `<div class="tl"><div class="lab">${l}</div><div class="val">${v}</div><div class="sub">${s0}</div></div>`).join('');
      return slFrame('s', I, 'Στροφές', `Ο χρόνος από 5${I.u} πριν ως 15${I.u} μετά από κάθε τοίχο`, `<div class="chw">${chart}</div><div class="tl3">${tiles}</div>`, n, N, 'spd');
    }
    const body = `<div class="stn${tA.length <= 3 ? ' big' : ''}">${tA.map((t, i) => `<div class="stn-r"><div class="stn-h"><span>Στροφή ${i + 1}</span><em>${t.wall}${I.u}</em></div>
      <div class="stn-v">${F(t.inOut)}<small> s</small><em>−5 → +15${I.u}</em></div>
      <div class="stn-x"><span><b>${ok(t.bd) ? F(t.bd) : '–'}</b><small>${I.u}</small> έξοδος</span><span><b>${ok(t.kicks) ? F(t.kicks, 0) : '–'}</b> δελφινισμοί</span><span><b>${ok(t.uw) ? F(t.uw) : '–'}</b><small> s</small> υποβρύχια</span></div></div>`).join('')}</div>
      <div class="stn-m">Μέσος όρος <b>${F(mean)} s</b></div>`;
    return slFrame('s', I, 'Στροφές', `Από 5${I.u} πριν ως 15${I.u} μετά τον τοίχο`, body, n, N, 'stt');
  }

  function sStrokes(x, I, n, N) {
    const a = x.race, gs = slGroups(a.blocks, 8);
    const cols = [['Χεριές', gp => ok(gp.strokes) ? F(gp.strokes, 0) : '–'], ['Συχνότητα', gp => F(gp.sr, 1)], ['Μήκος κύκλου', gp => F(gp.sl)]];
    const tbl = `<div class="sstw"><table class="sst2"><thead><tr><th>${gs.length && gs[0].n > 1 ? 'τμήμα' : '50άρι'}</th>${cols.map(([h0]) => `<th>${h0}</th>`).join('')}</tr></thead><tbody>
      ${gs.map(gp => `<tr><td class="l">${gp.from}–${gp.to}</td>${cols.map(([, f]) => `<td>${f(gp)}</td>`).join('')}</tr>`).join('')}</tbody></table></div>`;
    const tiles = [['Συχνότητα', `${F(a.avg.sr, 1)}<small> κύκλ./λ.</small>`], ['Μήκος κύκλου', `${F(a.avg.sl)}<small> μ.</small>`], ['Ταχύτητα', `${F(a.avg.vClean)}<small> m/s</small>`]]
      .map(([l, v]) => `<div class="tl"><div class="lab">${l}</div><div class="val">${v}</div><div class="sub">μ.ο. καθαρής κολύμβησης</div></div>`).join('');
    return slFrame('s', I, 'Χεριές', 'Συχνότητα (κύκλοι το λεπτό) και μήκος κύκλου (μέτρα ανά κύκλο χεριών): ταχύτητα = συχνότητα × μήκος κύκλου', `<div class="tl3">${tiles}</div>` + tbl, n, N, 'ssk');
  }

  function sDak(x, I, n, N) {
    const a = x.race;
    const body = `<div class="sdk"><div><div class="lab">ΔΑΚ · μ.ο. 50άρι</div><div class="dk-big">${F(a.avg.dak)}</div></div><div class="dk-key"><span><i style="background:#2F6FD8"></i>χεριές ανά 10 μ.</span><span><i style="background:#F08A3E"></i>δευτερόλεπτα ανά 10 μ.</span></div></div>
      <div class="chw">${slDakBars(x, I, 936, 640, 28, false)}</div>`;
    return slFrame('s', I, 'ΔΑΚ', 'Ο δείκτης της ΚΟΕ: χεριές + δευτερόλεπτα ανά 10 μ. κάθε 50άρι · όσο μικρότερος, τόσο πιο αποδοτική η κολύμβηση', body, n, N, 'sdkp');
  }

  /**
   * The slides of a report (x as for document): { html, video: n, social: n } — the sections of the 16:9 slides (class "v") and of the
   * 4:5 posts (class "s"), each its own size, one under the other; Kinovea renders each section as a picture. opts.short: the slides of
   * the pages of the short analysis.
   */
  function slides(x, opts) {
    opts = opts || {};
    const I = info(x);
    footText = 'Kinovea Swim · Ανάλυση αγώνα' + (I.competition ? ' · ' + I.competition : '');
    const R = recommendations(x);
    let kinds = plan(x, I, R).map(f => f.kind);
    if (opts.short) kinds = kinds.filter(k0 => SHORT_PAGES.indexOf(k0) >= 0);
    else kinds.push('splits');
    const V = { overview: vOverview, phases: vPhases, conclusion: vConclusion, dak: vDak, legs: vLegs, speed: vSpeed, composition: vComposition,
      styles: vStyles, pace: vPace, strokes: vStrokes, start: vStart, turns: vTurns, splits: vSplits };
    kinds = kinds.filter(k0 => V[k0] && (k0 !== 'phases' || (R.levels || []).length));
    const video = kinds.map((k0, i) => V[k0](x, I, R, i + 1, kinds.length));
    // The posts: the race's own numbers.
    const a = x.race;
    const S0 = [sCover, sSplits, a.rows.some(r => ok(r.vel)) ? sSpeed : null, sStart, realTurns(a).length ? sTurns : null,
      a.rows.some(r => ok(r.sr)) ? sStrokes : null, a.blocks.some(bk => ok(bk.dak)) && !I.im ? sDak : null].filter(f => f);
    const social = S0.map((f, i) => f(x, I, i + 1, S0.length));
    const title = [I.title, I.who, I.competition].filter(s0 => s0).join(' · ');
    return { html: `<!doctype html><html lang="el"><head><meta charset="utf-8"><title>${esc(title)}</title><style>${SLIDE_CSS}</style></head><body>${video.join('\n')}\n${social.join('\n')}</body></html>`,
      video: video.length, social: social.length, kinds };
  }

  const SLIDE_CSS = `
  * { box-sizing: border-box; }
  html, body { margin: 0; padding: 0; background: #05090F; }
  body { font-family: 'Inter', 'DejaVu Sans', sans-serif; font-variant-numeric: tabular-nums; font-feature-settings: 'tnum' 1, 'cv11' 1; -webkit-print-color-adjust: exact; }
  img[src^="{{"] { visibility: hidden; }
  .sl { position: relative; overflow: hidden; color: #fff; display: flex; flex-direction: column;
    background: radial-gradient(1300px 820px at 92% -12%, rgba(16, 82, 148, 0.85), rgba(16, 82, 148, 0) 72%), radial-gradient(900px 600px at -10% 110%, rgba(0, 200, 255, 0.10), rgba(0, 200, 255, 0) 70%), linear-gradient(165deg, #0B1626 0%, #0D2342 52%, #0A1322 100%); }
  .sl::after { content: ''; position: absolute; inset: 0; pointer-events: none; background: repeating-linear-gradient(90deg, rgba(255,255,255,0.018) 0 2px, transparent 2px 160px); }
  .sl > * { position: relative; z-index: 1; }
  .sl.v { width: 1920px; height: 1080px; padding: 58px 96px 44px; }
  .sl.s { width: 1080px; height: 1350px; padding: 60px 72px 46px; }
  .sl-strip { position: absolute !important; left: 0; right: 0; top: 0; height: 10px; background: linear-gradient(90deg, #00C8FF, #2E6BD6 60%, #E65100); }
  .sl-top { display: flex; justify-content: space-between; align-items: center; min-height: 72px; }
  .sl-ev { font-size: 27px; color: #C9D6E3; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; padding-right: 30px; }
  .sl-ev b { color: #fff; font-weight: 800; } .sl-ev .sl-cat { color: #8CA0B6; } .sl-ev i { font-style: normal; color: #5E7088; margin: 0 14px; }
  .sl-logo { height: 76px; width: auto; flex: 0 0 auto; }
  .sl.s .sl-logo { height: 64px; } .sl.s .sl-ev { font-size: 25px; }
  .sl-hd { margin: 22px 0 26px; }
  .sl-hd h1 { margin: 0; font-family: 'Inter Display', 'Inter', sans-serif; font-weight: 800; font-size: 66px; letter-spacing: -0.015em; line-height: 1.05; display: flex; align-items: center; gap: 22px; }
  .sl.s .sl-hd h1 { font-size: 64px; }
  .tmark { position: relative; display: inline-block; width: 46px; height: 12px; background: #00C8FF; border-radius: 3px; flex: 0 0 auto; }
  .tmark::after { content: ''; position: absolute; right: -4px; top: -16px; width: 12px; height: 44px; background: #00C8FF; border-radius: 3px; }
  .sl-hd p { margin: 12px 0 0; font-size: 27px; color: #A9BACB; line-height: 1.35; max-width: 1500px; }
  .sl.s .sl-hd p { font-size: 26px; }
  .sl-bd { flex: 1 1 auto; min-height: 0; display: flex; flex-direction: column; }
  .sl-ft { display: flex; justify-content: space-between; font-size: 21px; color: #6F8299; border-top: 1px solid rgba(255,255,255,0.10); padding-top: 14px; margin-top: 18px; }
  .sl-n { font-weight: 800; color: #C9D6E3; letter-spacing: 0.06em; }
  .sl-sub2 { font-size: 24px; letter-spacing: 0.1em; text-transform: uppercase; color: #8CA0B6; font-weight: 800; margin: 26px 0 12px; }
  .sl .chip { display: inline-block; font-size: 22px; font-weight: 800; padding: 3px 12px; border-radius: 16px; line-height: 1.3; white-space: nowrap; vertical-align: 2px; }
  .sl .chip.g { background: #1C55B8; color: #fff; } .sl .chip.l { background: #E57B22; color: #0B111A; } .sl .chip.n { background: rgba(255,255,255,0.14); color: #C9D6E3; }
  .sl-vs { display: flex; flex-wrap: wrap; gap: 6px 22px; font-size: 23px; color: #C9D6E3; margin-top: 10px; } .sl-vs span { white-space: nowrap; }
  .sl-lg { display: flex; gap: 40px; justify-content: center; font-size: 23px; color: #C9D6E3; margin-top: 8px; } .sl-lg span { display: inline-flex; align-items: center; gap: 10px; }
  .slc { display: block; overflow: visible; } .slc text { font-family: 'Inter', 'DejaVu Sans', sans-serif; }
  .pill { font-size: 22px; letter-spacing: 0.1em; color: #0B111A; background: #00C8FF; padding: 6px 16px; border-radius: 6px; font-weight: 800; }
  /* tiles */
  .tl { background: rgba(255,255,255,0.06); border: 1px solid rgba(255,255,255,0.10); border-radius: 20px; padding: 22px 26px 20px; min-width: 0; }
  .tl .lab { font-size: 22px; letter-spacing: 0.08em; text-transform: uppercase; color: #8CA0B6; font-weight: 800; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .tl .val { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 62px; font-weight: 800; line-height: 1.08; margin-top: 6px; white-space: nowrap; }
  .tl .val small { font-family: 'Inter', sans-serif; font-size: 26px; font-weight: 600; color: #A9BACB; }
  .tl .sub { font-size: 22px; color: #A9BACB; margin-top: 6px; }
  .tl3 { display: grid; grid-template-columns: repeat(3, 1fr); gap: 22px; } .tl4 { display: grid; grid-template-columns: repeat(4, 1fr); gap: 22px; }
  .tl5 { display: grid; grid-template-columns: repeat(5, 1fr); gap: 22px; } .tl6 { display: grid; grid-template-columns: repeat(2, 1fr); gap: 20px; }
  .tl4.fill { flex: 1 1 auto; } .tl4.fill .tl { display: flex; flex-direction: column; justify-content: center; padding: 30px 30px; }
  .tl4.fill .tl .val { font-size: 84px; margin: 14px 0 6px; } .tl4.fill .who { font-size: 28px; font-weight: 700; margin-top: 10px; }
  .tl4.fill .more { display: grid; gap: 14px; margin-top: 34px; padding-top: 24px; border-top: 1px solid rgba(255,255,255,0.12); }
  .tl4.fill .more span { display: flex; justify-content: space-between; font-size: 30px; font-weight: 800; } .tl4.fill .more em { font-style: normal; font-size: 22px; color: #8CA0B6; font-weight: 700; text-transform: uppercase; letter-spacing: 0.06em; }
  /* overview */
  .ov .sl-top { position: absolute; right: 96px; top: 50px; }
  .ov .sl-top .sl-ev { display: none; }
  .ov-top { display: flex; align-items: center; gap: 22px; min-height: 72px; }
  .kick { font-size: 24px; letter-spacing: 0.18em; color: #8CA0B6; font-weight: 700; }
  .ov-main { display: flex; justify-content: space-between; align-items: flex-end; gap: 40px; margin: 34px 0 34px; }
  .ov-who { min-width: 0; flex: 1 1 auto; }
  .ov-ev { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 80px; font-weight: 800; letter-spacing: -0.015em; line-height: 1.02; white-space: nowrap; } .ov-ev span { font-size: 40px; color: #8CA0B6; font-weight: 600; }
  .ov-name { font-size: 50px; font-weight: 700; margin-top: 18px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .ov-meta { font-size: 25px; color: #A9BACB; margin-top: 12px; line-height: 1.35; max-height: 68px; overflow: hidden; }
  .ov-time { text-align: right; flex: 0 0 auto; } .ov-time .lab { font-size: 24px; letter-spacing: 0.14em; text-transform: uppercase; color: #8CA0B6; font-weight: 800; }
  .ov-time .big { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 196px; font-weight: 800; letter-spacing: -0.03em; line-height: 0.95; margin: 6px 0 14px; }
  .ov-subs { display: flex; gap: 26px; justify-content: flex-end; font-size: 26px; color: #C9D6E3; } .ov-subs span { white-space: nowrap; }
  .hc2s { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 24px; }
  .hc2 { border-radius: 22px; padding: 22px 28px 20px; background: rgba(255,255,255,0.06); border: 1px solid rgba(255,255,255,0.12); border-top: 6px solid #4C8DFF; min-width: 0; }
  .hc2.dak { border-top-color: #00C8FF; background: linear-gradient(160deg, rgba(0,200,255,0.18), rgba(0,200,255,0.04)); } .hc2.aim { border-top-color: #5FE3C9; background: linear-gradient(160deg, rgba(95,227,201,0.14), rgba(95,227,201,0.03)); }
  .hc2 .lab { font-size: 22px; letter-spacing: 0.1em; text-transform: uppercase; color: #A9BACB; font-weight: 800; }
  .hc2 .val { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 76px; font-weight: 800; line-height: 1.05; margin: 6px 0 6px; white-space: nowrap; }
  .hc2.dak .val { color: #5CC8FF; } .hc2 .val small { font-size: 32px; color: #A9BACB; font-weight: 700; }
  .hc2 .val.word { font-size: 52px; margin: 16px 0 14px; } .hc2 .val.word b { color: #5CC8FF; font-size: 0.82em; }
  .hc2 .sub { font-size: 23px; color: #C9D6E3; display: flex; flex-wrap: wrap; gap: 4px 20px; } .hc2 .sub span { white-space: nowrap; }
  .strip { display: flex; gap: 14px; margin-top: 26px; }
  .st { flex: 1 1 0; min-width: 0; border-radius: 16px; padding: 14px 18px 12px; background: rgba(255,255,255,0.05); border: 1px solid rgba(255,255,255,0.10); border-bottom: 5px solid rgba(255,255,255,0.25); }
  .st.g { border-bottom-color: #4C8DFF; } .st.l { border-bottom-color: #FF9A4D; }
  .st-l { font-size: 21px; color: #8CA0B6; font-weight: 700; } .st-v { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 44px; font-weight: 800; margin: 2px 0; } .st-d { font-size: 21px; color: #C9D6E3; }
  .st.g .st-d { color: #5CC8FF; } .st.l .st-d { color: #FF9A4D; }
  /* phases */
  .pb2 { display: grid; gap: 26px; margin-top: 6px; }
  .pb2-r { display: grid; grid-template-columns: 330px 1fr 330px; gap: 34px; align-items: center; }
  .pb2-r .t { font-size: 40px; font-weight: 800; }
  .pb2-r .bar { position: relative; height: 34px; background: rgba(255,255,255,0.08); border-radius: 17px; }
  .pb2-r .bar i { display: block; height: 100%; border-radius: 17px; background: linear-gradient(90deg, #E07A2E, #F5A35C); } .pb2-r .bar i.up { background: linear-gradient(90deg, #2BB3A0, #5FE3C9); }
  .pb2-r .bar b { position: absolute; top: -10px; bottom: -10px; width: 4px; background: #fff; border-radius: 2px; }
  .pb2-r .v { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 50px; font-weight: 800; text-align: right; white-space: nowrap; } .pb2-r .v small { display: block; font-family: 'Inter', sans-serif; font-size: 23px; color: #A9BACB; font-weight: 700; }
  .mapw .rmap { width: 100%; height: auto; }
  /* conclusion */
  .cc2-top { display: flex; align-items: center; gap: 46px; }
  .cc2-top .big { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 150px; font-weight: 800; color: #5CC8FF; line-height: 1; white-space: nowrap; } .cc2-top .big small { font-size: 50px; color: #DCE6F0; }
  .cc2-top .txt { font-size: 30px; color: #C9D6E3; line-height: 1.5; } .cc2-top .txt b { color: #fff; }
  .share2 { display: flex; height: 70px; border-radius: 14px; overflow: hidden; }
  .share2 i { display: flex; align-items: center; justify-content: center; font-style: normal; font-size: 26px; font-weight: 800; color: #fff; white-space: nowrap; overflow: hidden; border-right: 4px solid #0D2342; }
  .share2 i:last-child { border-right: 0; }
  .parts { display: grid; grid-template-columns: repeat(auto-fit, minmax(0, 1fr)); gap: 20px; margin-top: 28px; }
  .part { background: rgba(255,255,255,0.06); border: 1px solid rgba(255,255,255,0.10); border-radius: 18px; padding: 18px 22px; min-width: 0; }
  .part .lab { font-size: 24px; font-weight: 800; display: flex; align-items: center; gap: 12px; } .part .lab i { width: 18px; height: 18px; border-radius: 5px; }
  .part .val { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 56px; font-weight: 800; margin: 4px 0; } .part .val.l { color: #FFFFFF; } .part .val.g { color: #5CC8FF; } .part .val small { font-size: 26px; color: #A9BACB; }
  .part .sub { font-size: 21px; color: #A9BACB; }
  .closing { margin-top: 26px; font-size: 34px; font-weight: 800; color: #fff; border-left: 8px solid #00C8FF; padding: 6px 0 6px 24px; }
  /* ΔΑΚ */
  .dk { display: flex; gap: 60px; align-items: stretch; }
  .dk-l { flex: 0 0 470px; display: flex; flex-direction: column; } .dk-l .lab, .sdk .lab { font-size: 24px; letter-spacing: 0.12em; text-transform: uppercase; color: #8CA0B6; font-weight: 800; }
  .dk-big { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 170px; font-weight: 800; color: #5CC8FF; line-height: 1; margin: 8px 0 6px; }
  .dk-bw { display: grid; gap: 14px; margin-top: 34px; } .dk-bw div { display: flex; align-items: baseline; gap: 16px; font-size: 24px; color: #A9BACB; } .dk-bw b { font-size: 40px; color: #fff; } .dk-bw em { font-style: normal; }
  .dk-bw span { width: 150px; text-transform: uppercase; letter-spacing: 0.06em; font-weight: 800; font-size: 21px; }
  .dk-key { display: flex; gap: 30px; font-size: 22px; color: #C9D6E3; margin-top: auto; flex-wrap: wrap; } .dk-key span { display: inline-flex; align-items: center; gap: 10px; } .dk-key i { width: 22px; height: 22px; border-radius: 6px; }
  .sl.s .dk-key { justify-content: center; margin-top: 18px; }
  /* composition */
  .cp-r { display: grid; grid-template-columns: 330px 1fr 190px; gap: 30px; align-items: center; margin-bottom: 20px; }
  .cp-n { font-size: 30px; font-weight: 700; color: #C9D6E3; } .cp-r.main .cp-n { color: #fff; font-weight: 800; }
  .cp-b { display: flex; height: 100px; border-radius: 16px; overflow: hidden; } .cp-r:not(.main) .cp-b { height: 62px; }
  .cp-b i { display: flex; flex-direction: column; justify-content: center; padding: 0 14px; font-style: normal; color: #fff; white-space: nowrap; overflow: hidden; border-right: 4px solid #0D2342; }
  .cp-b i:last-child { border-right: 0; } .cp-b i span { font-size: 22px; font-weight: 700; } .cp-b i b { font-size: 32px; font-weight: 800; }
  .cp-r:not(.main) .cp-b i span { display: none; } .cp-r:not(.main) .cp-b i b { font-size: 26px; }
  .cp-t { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 50px; font-weight: 800; text-align: right; }
  .cp-lg { display: flex; gap: 34px; font-size: 24px; color: #C9D6E3; margin: 4px 0 6px 360px; } .cp-lg span { display: inline-flex; align-items: center; gap: 10px; } .cp-lg i { width: 22px; height: 22px; border-radius: 6px; }
  .cp-d { display: flex; gap: 36px; align-items: center; margin-top: 46px; font-size: 28px; color: #C9D6E3; flex-wrap: wrap; } .cp-d div { display: flex; gap: 12px; align-items: center; } .cp-d em { font-style: normal; color: #8CA0B6; font-size: 23px; }
  /* pace */
  .pc { display: flex; gap: 50px; } .pc-l { flex: 0 0 1120px; } .pc-r { flex: 1 1 auto; display: grid; gap: 18px; align-content: start; }
  .pc-r .tl { padding: 14px 22px 14px; } .pc-r .tl .val { font-size: 46px; } .pc-r .tl .sl-vs { font-size: 21px; margin-top: 4px; } .pc-r .tl .sub { font-size: 21px; }
  /* strokes */
  .sks { display: grid; grid-template-columns: repeat(3, 1fr); gap: 30px; }
  .sk { background: rgba(255,255,255,0.05); border: 1px solid rgba(255,255,255,0.10); border-radius: 20px; padding: 20px 22px 6px; }
  .sk .lab { font-size: 22px; letter-spacing: 0.1em; text-transform: uppercase; color: #8CA0B6; font-weight: 800; }
  .sk .val { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 66px; font-weight: 800; line-height: 1.1; } .sk .val small { font-family: 'Inter', sans-serif; font-size: 26px; color: #A9BACB; font-weight: 600; }
  .sk .sl-vs { font-size: 21px; margin: 4px 0 6px; min-height: 34px; } .sk .slc { width: 100%; height: auto; }
  /* start */
  .trk { margin: 0 0 16px; }
  .stp .tl .val { font-size: 50px; } .stp .tl5 .tl, .stp .tl4 .tl { padding: 14px 22px 12px; } .stp .sl-sub2 { margin: 18px 0 10px; } .stp .sl-hd { margin: 16px 0 14px; }
  .stp .tl .sl-vs { font-size: 20px; gap: 2px 14px; }
  /* turns */
  .trs { display: grid; align-content: start; }
  .tr { display: grid; grid-template-columns: 170px 1fr 450px 430px; gap: 34px; align-items: center; padding: 22px 0; border-bottom: 1px solid rgba(255,255,255,0.10); }
  .tr.h { padding: 0 0 12px; font-size: 21px; color: #8CA0B6; text-transform: uppercase; letter-spacing: 0.08em; font-weight: 800; }
  .tr-hb { display: flex; gap: 34px; } .tr-hb span { display: inline-flex; align-items: center; gap: 10px; } .tr-hb i { width: 24px; height: 16px; border-radius: 4px; }
  .tr-hb i.in { background: #5A8EDC; } .tr-hb i.out { background: #24B3A1; }
  .tr-w { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 54px; font-weight: 800; } .tr-w small { font-size: 24px; color: #A9BACB; margin-left: 4px; }
  .tr-b { display: flex; height: 64px; border-radius: 12px; overflow: hidden; background: rgba(255,255,255,0.05); }
  .tr-b i { display: flex; align-items: center; justify-content: center; font-style: normal; font-weight: 800; font-size: 28px; }
  .tr-b i.in { background: #5A8EDC; color: #0B111A; } .tr-b i.out { background: #24B3A1; color: #0B111A; border-left: 4px solid #0D2342; }
  .tr-t > div { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 62px; font-weight: 800; line-height: 1; white-space: nowrap; } .tr-t small { font-size: 26px; color: #A9BACB; }
  .tr-t .sl-vs { font-size: 21px; margin-top: 8px; gap: 2px 18px; }
  .tr-x { display: grid; grid-template-columns: repeat(3, 1fr); gap: 20px; font-size: 40px; font-weight: 800; text-align: right; } .tr-x small { font-size: 22px; color: #A9BACB; font-weight: 600; }
  .tr.h .tr-x { font-size: 21px; font-weight: 800; }
  .trs.c4 .tr { padding: 12px 0; } .trs.c4 .tr-t > div { font-size: 52px; } .trs.c4 .tr-b { height: 54px; } .trs.c4 .tr-t .sl-vs { margin-top: 4px; }
  .tr-m { margin-top: 24px; font-size: 30px; display: flex; align-items: center; gap: 34px; color: #C9D6E3; } .tr-m b { color: #fff; } .tr-m .sl-vs { margin: 0; } .tr-m em { font-style: normal; font-size: 22px; color: #8CA0B6; }
  /* splits */
  .spl { width: 100%; border-collapse: collapse; }
  .spl th { font-size: 22px; letter-spacing: 0.08em; text-transform: uppercase; color: #8CA0B6; font-weight: 800; text-align: right; padding: 0 18px 14px; border-bottom: 2px solid rgba(255,255,255,0.2); }
  .spl th:first-child { text-align: left; }
  .spl td { font-size: 34px; text-align: right; padding: 16px 18px; border-bottom: 1px solid rgba(255,255,255,0.08); color: #C9D6E3; white-space: nowrap; }
  .spl td.l { text-align: left; color: #8CA0B6; font-weight: 700; } .spl td.c { color: #fff; font-weight: 700; } .spl td.b { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 46px; font-weight: 800; color: #fff; }
  .spl.sm td { font-size: 26px; padding: 9px 14px; } .spl.sm td.b { font-size: 32px; }
  .spl.xl td { font-size: 40px; padding: 28px 18px; } .spl.xl td.b { font-size: 62px; } .spl.xl th { font-size: 23px; }
  /* the posts */
  .cover { padding: 56px 64px 46px; }
  .cv-top { display: flex; justify-content: space-between; align-items: center; } .cv-logo { height: 118px; width: auto; }
  .cv-kick { text-align: right; font-size: 24px; letter-spacing: 0.2em; font-weight: 800; color: #fff; line-height: 1.4; } .cv-kick span { color: #00C8FF; }
  .cv-frame { position: relative; height: 500px; margin: 36px 0 30px; border-radius: 30px; overflow: hidden; border: 1px solid rgba(255,255,255,0.14); }
  .cv-img { position: absolute; inset: 0; background: url({{FRAME}}) center / cover no-repeat, repeating-linear-gradient(0deg, #0F3B6E 0 92px, #0C335F 92px 100px); }
  .cv-frame::after { content: ''; position: absolute; inset: 0; background: linear-gradient(180deg, rgba(10,19,34,0) 55%, rgba(10,19,34,0.85) 100%); }
  .cv-frame .pill { position: absolute; left: 26px; bottom: 24px; z-index: 2; }
  .cv-ev { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 60px; font-weight: 800; letter-spacing: -0.01em; } .cv-ev span { font-size: 36px; color: #8CA0B6; font-weight: 600; }
  .cv-name { font-size: 66px; font-weight: 800; margin-top: 8px; line-height: 1.05; }
  .cv-club { font-size: 32px; color: #A9BACB; margin-top: 6px; }
  .cv-time { margin-top: auto; } .cv-time .lab { font-size: 24px; letter-spacing: 0.16em; text-transform: uppercase; color: #8CA0B6; font-weight: 800; }
  .cv-time .big { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 176px; font-weight: 800; letter-spacing: -0.03em; line-height: 0.95; }
  .cv-where { font-size: 27px; color: #C9D6E3; margin-top: 10px; }
  .ss { flex: 1 1 auto; display: grid; grid-auto-rows: 1fr; gap: 18px; }
  .ss-r { display: grid; grid-template-columns: 170px 1fr 210px; gap: 26px; align-items: center; background: rgba(255,255,255,0.05); border: 1px solid rgba(255,255,255,0.10); border-radius: 22px; padding: 14px 28px; }
  .ss-d { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 58px; font-weight: 800; } .ss-d small { font-size: 24px; color: #A9BACB; margin-left: 3px; }
  .ss-v { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 66px; font-weight: 800; line-height: 1; display: flex; align-items: baseline; gap: 20px; white-space: nowrap; }
  .ss-v em { font-family: 'Inter', sans-serif; font-style: normal; font-size: 24px; color: #A9BACB; font-weight: 600; }
  .ss-bar { display: flex; align-items: center; gap: 18px; margin-top: 16px; }
  .ss-bar .tr0 { flex: 1 1 auto; height: 14px; background: rgba(255,255,255,0.08); border-radius: 7px; overflow: hidden; } .ss-bar .tr0 i { display: block; height: 100%; border-radius: 7px; background: linear-gradient(90deg, #2E6BD6, #00C8FF); }
  .ss-bar .vv { font-size: 26px; font-weight: 800; white-space: nowrap; } .ss-bar .vv small { font-size: 20px; color: #A9BACB; font-weight: 600; }
  .ss-c { text-align: right; font-family: 'Inter Display', 'Inter', sans-serif; font-size: 40px; font-weight: 800; white-space: nowrap; } .ss-c small { display: block; font-family: 'Inter', sans-serif; font-size: 18px; color: #8CA0B6; letter-spacing: 0.1em; text-transform: uppercase; font-weight: 800; margin-bottom: 4px; }
  .ss.big .ss-d { font-size: 70px; } .ss.big .ss-v { font-size: 92px; } .ss.big .ss-c { font-size: 44px; }
  .ss.sm { gap: 12px; } .ss.sm .ss-r { padding: 8px 26px; } .ss.sm .ss-d { font-size: 44px; } .ss.sm .ss-v { font-size: 50px; } .ss.sm .ss-bar { margin-top: 6px; } .ss.sm .ss-c { font-size: 32px; }
  .chw { flex: 1 1 auto; display: flex; align-items: center; justify-content: center; min-height: 0; }
  .spd .tl3, .ssk .tl3 { gap: 16px; } .sl.s .tl { padding: 18px 20px 16px; } .sl.s .tl .val { font-size: 50px; } .sl.s .tl .lab { font-size: 20px; } .sl.s .tl .sub { font-size: 20px; }
  .sst .trk { margin: 6px 0 26px; } .tl6 { flex: 1 1 auto; grid-auto-rows: 1fr; } .tl6 .tl { display: flex; flex-direction: column; justify-content: center; } .sl.s .tl6 .tl .val { font-size: 74px; }
  .stn { flex: 1 1 auto; display: grid; grid-auto-rows: 1fr; gap: 20px; }
  .stn-r { display: grid; grid-template-columns: 250px 1fr; grid-template-rows: auto auto; align-content: center; gap: 12px 26px; background: rgba(255,255,255,0.05); border: 1px solid rgba(255,255,255,0.10); border-radius: 22px; padding: 20px 30px; }
  .stn-h { grid-row: 1 / 3; display: flex; flex-direction: column; justify-content: center; } .stn-h span { font-size: 34px; font-weight: 800; } .stn-h em { font-style: normal; font-size: 28px; color: #8CA0B6; margin-top: 4px; }
  .stn-v { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 72px; font-weight: 800; line-height: 1; display: flex; align-items: baseline; gap: 18px; } .stn-v small { font-size: 30px; color: #A9BACB; } .stn-v em { font-family: 'Inter', sans-serif; font-style: normal; font-size: 22px; color: #8CA0B6; font-weight: 700; }
  .stn.big .stn-v { font-size: 96px; }
  .stn-x { display: flex; gap: 30px; font-size: 22px; color: #A9BACB; } .stn-x b { font-size: 36px; color: #fff; } .stn-x small { font-size: 22px; color: #A9BACB; }
  .stn-m { font-size: 30px; color: #C9D6E3; margin-top: 18px; } .stn-m b { color: #fff; }
  .sstw { flex: 1 1 auto; display: flex; margin-top: 26px; } .sst2 { width: 100%; height: 100%; border-collapse: collapse; }
  .sst2 th { font-size: 21px; letter-spacing: 0.08em; text-transform: uppercase; color: #8CA0B6; font-weight: 800; text-align: right; padding: 0 14px 12px; border-bottom: 2px solid rgba(255,255,255,0.2); height: 40px; } .sst2 th:first-child { text-align: left; }
  .sst2 td { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 52px; font-weight: 800; text-align: right; padding: 8px 14px; border-bottom: 1px solid rgba(255,255,255,0.08); } .sst2 td.l { font-family: 'Inter', sans-serif; text-align: left; font-size: 30px; color: #8CA0B6; font-weight: 700; }
  .sdk { display: flex; justify-content: space-between; align-items: flex-end; margin-bottom: 6px; } .sdk .dk-big { font-size: 140px; } .sdk .dk-key { flex-direction: column; gap: 10px; margin: 0 0 20px; }
  `;

  const CSS = `
  @page { size: A4; margin: 0; }
  * { box-sizing: border-box; }
  html, body { margin: 0; padding: 0; background: #fff; }
  body { font-family: 'Inter', 'DejaVu Sans', sans-serif; color: ${C.text}; font-size: 11.2px; line-height: 1.38; -webkit-print-color-adjust: exact; print-color-adjust: exact;
    font-variant-numeric: tabular-nums; font-feature-settings: 'tnum' 1, 'cv11' 1; }
  .page { width: 794px; height: 1123px; position: relative; overflow: hidden; page-break-after: always; break-after: page; padding: 0 34px; background: #fff; }
  .band { margin: 0 -34px; background: ${C.ink}; color: #fff; padding: 18px 34px 20px; position: relative; }
  .band::after { content: ''; position: absolute; left: 0; right: 0; bottom: 0; height: 4px; background: linear-gradient(90deg, #00C8FF, #2E6BD6 60%, #E65100); }
  .band-top { display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px; }
  .kicker { font-size: 10px; letter-spacing: 0.14em; color: #8CA0B6; font-weight: 600; }
  .proto { font-size: 9.5px; letter-spacing: 0.08em; color: #0B111A; background: #00C8FF; padding: 3px 9px; border-radius: 3px; font-weight: 800; }
  .band-main { display: flex; justify-content: space-between; align-items: flex-end; gap: 16px; }
  .event { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 33px; font-weight: 800; letter-spacing: -0.015em; line-height: 1.05; white-space: nowrap; }
  .event .cat { font-size: 16px; font-weight: 500; color: #8CA0B6; margin-left: 6px; }
  .name { font-size: 18px; font-weight: 700; margin-top: 8px; }
  .meta { font-size: 10.6px; color: #9DB0C4; margin-top: 4px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .band-main .who { min-width: 0; flex: 1 1 auto; overflow: hidden; }
  .name { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .hero { text-align: right; min-width: 250px; }
  .hero-lab { font-size: 10px; letter-spacing: 0.12em; color: #8CA0B6; text-transform: uppercase; font-weight: 700; }
  .hero-time { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 60px; font-weight: 800; letter-spacing: -0.02em; line-height: 1.0; margin: 3px 0 6px; }
  .hero-sub { display: flex; gap: 12px; justify-content: flex-end; font-size: 10.6px; color: #C9D6E3; } .hero-sub span { white-space: nowrap; }
  .band .chip.g { background: #1C55B8; color: #fff; } .band .chip.l { background: #E57B22; color: #0B111A; }
  .heros { display: grid; grid-template-columns: 1.15fr 1fr 1fr; gap: 10px; margin: 14px 0 2px; }
  .hc { border-radius: 12px; padding: 9px 14px 8px; background: #F4F7FB; border: 1px solid #E3E9F0; }
  .hc.dak { background: linear-gradient(135deg, #FFF3E0, #FFE1C2); border-color: #F7C99A; }
  .hc.aim { background: linear-gradient(135deg, #E9F6F4, #D5EEEA); border-color: #A9DCD3; }
  .hc-lab { font-size: 10px; letter-spacing: 0.08em; text-transform: uppercase; font-weight: 800; color: #5B6B7B; }
  .hc.dak .hc-lab { color: #B34700; } .hc.aim .hc-lab { color: #0E6B62; }
  .hc-val { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 38px; font-weight: 800; color: ${C.ink}; line-height: 1.05; margin: 3px 0 3px; }
  .hc.dak .hc-val { color: #B34700; font-size: 44px; }
  .page.p1 .sec { margin: 10px 0 6px; }
  .hc-u { font-size: 18px; font-weight: 600; color: #5B6B7B; }
  .hc-sub { font-size: 11px; color: ${C.text}; } .hc-x { font-size: 10px; color: #5B6B7B; margin-top: 3px; }
  .ph { display: flex; justify-content: space-between; align-items: center; padding: 16px 0 9px; border-bottom: 2.5px solid ${C.ink}; margin-bottom: 2px; }
  .ph-l { font-size: 11px; color: ${C.dim}; } .ph-ev { font-weight: 800; color: ${C.ink}; } .ph-sep { margin: 0 6px; color: #B8C3CF; }
  .ph-r { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 15px; font-weight: 800; color: ${C.ink}; }
  .pf { position: absolute; left: 34px; right: 34px; bottom: 13px; display: flex; justify-content: space-between; font-size: 9px; color: #8A97A6; border-top: 1px solid #E3E8EE; padding-top: 6px; }
  .sec { margin: 13px 0 7px; }
  .sec-t { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 17px; font-weight: 800; color: ${C.navy}; display: flex; align-items: center; gap: 9px; }
  .sec-t::before { content: ''; width: 5px; height: 17px; background: #00C8FF; border-radius: 3px; }
  .sec-s { font-size: 10.6px; color: ${C.dim}; margin-top: 2px; max-width: 720px; }
  .box { background: #fff; border: 1px solid #E3E8EE; border-radius: 10px; padding: 10px 12px; margin-bottom: 8px; }
  .box.tight { padding: 6px 12px; }
  .tiles { display: grid; grid-template-columns: repeat(4, 1fr); gap: 8px; margin-bottom: 6px; }
  .tile { background: #F5F7FA; border: 1px solid #E6EBF1; border-radius: 10px; padding: 9px 11px 8px; min-width: 0; }
  .t-lab { font-size: 10px; letter-spacing: 0.05em; text-transform: uppercase; color: ${C.dim}; font-weight: 700; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .t-val { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 27px; font-weight: 800; color: ${C.ink}; margin: 3px 0 4px; white-space: nowrap; }
  .t-u { font-family: 'Inter', sans-serif; font-size: 11px; font-weight: 500; color: ${C.dim}; margin-left: 2px; }
  .t-word { font-size: 17px; font-weight: 800; white-space: normal; display: inline-block; line-height: 1.12; }
  .t-d { display: flex; justify-content: space-between; font-size: 10px; color: ${C.dim}; gap: 4px; }
  .t-x { font-size: 10px; color: ${C.dim}; margin-top: 3px; }
  .chip { display: inline-block; font-size: 10px; font-weight: 800; padding: 1px 6px; border-radius: 9px; line-height: 1.35; white-space: nowrap; }
  .chip.g { background: #E1EAFB; color: #1C55B8; } .chip.l { background: #FCE6D3; color: #A24A07; } .chip.n { background: #EDF0F4; color: #5B6B7B; }
  .sc { width: 100%; border-collapse: collapse; }
  .sc th { font-size: 9.8px; font-weight: 700; color: #2B3F55; text-align: right; padding: 3px 6px 6px; border-bottom: 1.5px solid #D3DCE6; }
  .sc th:first-child { text-align: left; } .sc th.bh { text-align: center; font-weight: 600; color: ${C.dim}; }
  .kk { margin-right: 6px; font-weight: 600; color: ${C.text}; }
  .k { display: inline-block; width: 9px; height: 9px; border-radius: 50%; margin: 0 3px 0 6px; vertical-align: -1px; }
  .k.a { background: ${C.race}; } .k.p { background: ${C.pb}; border-radius: 2px; width: 4px; } .k.r { background: ${C.ref}; border-radius: 2px; width: 4px; }
  .sc .k { display: inline-block; width: 9px; height: 9px; border-radius: 50%; margin: 0 3px 0 8px; vertical-align: -1px; }
  .sc .k.a { background: ${C.race}; } .sc .k.p { background: ${C.pb}; border-radius: 2px; width: 4px; } .sc .k.r { background: ${C.ref}; border-radius: 2px; width: 4px; }
  .sc td { padding: 2.5px 6px; border-bottom: 1px solid #EEF2F6; text-align: right; font-size: 11.5px; }
  .sc td.l { text-align: left; font-weight: 600; font-size: 11.8px; } .sc td.b { width: 286px; text-align: center; }
  .sc td.v b { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 19px; font-weight: 800; color: ${C.ink}; } .sc td .u { font-size: 10px; color: ${C.dim}; margin-left: 2px; }
  .sc tr.main td { background: #FFF6EC; } .sc tr.main td.v b { color: #B34700; font-size: 22px; } .sc .sub { font-size: 9.8px; font-weight: 500; color: ${C.dim}; }
  .msgs { display: grid; gap: 7px; }
  .msg { display: flex; gap: 11px; align-items: flex-start; background: #F4F7FB; border-radius: 10px; padding: 6px 12px; font-size: 12.2px; line-height: 1.38; }
  .msg .num { flex: 0 0 26px; height: 26px; border-radius: 50%; background: ${C.ink}; color: #fff; font-weight: 800; font-size: 13px; display: flex; align-items: center; justify-content: center; }
  .aim-card { display: flex; gap: 18px; align-items: center; background: linear-gradient(135deg, #0B111A, #14243A); color: #fff; border-radius: 12px; padding: 13px 18px; margin-bottom: 10px; }
  .ac-lab { font-size: 10px; letter-spacing: 0.1em; text-transform: uppercase; color: #8CA0B6; font-weight: 800; }
  .ac-val { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 44px; font-weight: 800; color: #5FE3C9; line-height: 1.05; }
  .ac-txt { font-size: 12.2px; color: #DCE6F0; line-height: 1.45; } .ac-txt b { color: #fff; }
  .hc-val.hc-word { font-size: 25px; margin: 5px 0 3px; color: #0E6B62; } .hc-val.hc-word.l { font-size: 19px; } .hc-val.hc-word.xl { font-size: 15.5px; }
  .rmap { display: block; width: 100%; height: auto; } .hc .rmap { margin: 5px 0 0; }
  .aim-card.map { display: block; padding: 11px 16px 10px; }
  .ac-head { display: flex; gap: 16px; align-items: baseline; justify-content: space-between; margin-bottom: 2px; } .ac-head .ac-txt { font-size: 10.5px; color: #A9BACB; text-align: right; max-width: 64%; line-height: 1.35; }
  .hc-pct { font-size: 0.84em; color: ${C.loss}; white-space: nowrap; } .hc.aim .hc-sub b { color: ${C.loss}; font-weight: 800; }
  .ac-bars { display: grid; gap: 5px; margin: 6px 0 4px; }
  .acb { display: grid; grid-template-columns: 112px 1fr 128px; gap: 10px; align-items: center; } .acb-t { font-weight: 800; font-size: 12px; color: #fff; }
  .acb-bar { position: relative; height: 11px; background: rgba(255,255,255,0.08); border-radius: 6px; } .acb-bar i { display: block; height: 100%; background: linear-gradient(90deg, #E07A2E, #F5A35C); border-radius: 6px; }
  .acb-bar i.up { background: linear-gradient(90deg, #2BB3A0, #5FE3C9); } .acb-bar b { position: absolute; top: -3px; bottom: -3px; width: 2px; background: #fff; }
  .acb-v { font-size: 12.5px; color: #fff; font-weight: 800; text-align: right; white-space: nowrap; } .acb-v small { font-size: 10.5px; color: #A9BACB; font-weight: 700; }
  .ac-sub { margin: 6px 0 0; } .nw { white-space: nowrap; }
  .rl-dot { display: inline-block; width: 9px; height: 9px; border-radius: 50%; margin: 0 4px 0 1px; vertical-align: 0; }
  .xc { display: grid; grid-template-columns: 204px 1fr 1fr; gap: 10px; align-items: start; border: 1px solid #E3E8EE; border-radius: 10px; padding: 8px 10px; margin-bottom: 7px; }
  .xc-t { font-family: 'Inter Display', 'Inter', sans-serif; font-weight: 800; font-size: 14px; color: ${C.ink}; } .xc-t small { font-weight: 600; font-size: 10.5px; color: ${C.dim}; }
  .xc-n { font-size: 10.4px; font-weight: 600; color: ${C.text}; margin: 2px 0 1px; }
  .xc-v { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 36px; font-weight: 800; color: ${C.ink}; line-height: 1.05; } .xc-v small { font-size: 15px; color: ${C.dim}; font-weight: 600; }
  .xc-x { font-size: 9.8px; color: ${C.dim}; margin-top: 3px; line-height: 1.35; }
  .xc-ph .im { width: 100%; aspect-ratio: 16 / 9; border-radius: 7px; background: #DDE4EC; border: 1px solid #D3DCE6; overflow: hidden; }
  .xc-ph .im img { width: 100%; height: 100%; object-fit: cover; display: block; }
  .xc-ph .im.none { display: flex; align-items: center; justify-content: center; font-size: 10px; color: ${C.faint}; }
  .xc-cap { font-size: 10px; font-weight: 700; color: ${C.text}; margin-top: 3px; } .xc-cap i { display: inline-block; width: 8px; height: 8px; border-radius: 2px; margin-right: 4px; vertical-align: 0; }
  .zone { display: inline-block; font-size: 10px; font-weight: 800; padding: 1px 7px; border-radius: 9px; margin-top: 3px; }
  .zone.z0 { background: #FDE0E2; color: #B3261E; } .zone.z1 { background: #E1EAFB; color: #1C55B8; } .zone.z2 { background: #DDF5EA; color: #0E7A4F; }
  .zone.z3 { background: #FFF1DA; color: #8A5300; } .zone.z4 { background: #FCE6D3; color: #A24A07; }
  .rs-key { font-size: 10px; color: ${C.dim}; margin: 0 0 6px; } .rs-key i { display: inline-block; width: 9px; height: 9px; border-radius: 2px; margin: 0 4px 0 0; vertical-align: -1px; }
  .rs { border: 1px solid #E3E8EE; border-radius: 10px; padding: 7px 11px 7px; margin-bottom: 7px; }
  .rs-h { display: flex; justify-content: space-between; align-items: baseline; gap: 10px; }
  .rs-n { font-family: 'Inter Display', 'Inter', sans-serif; font-weight: 800; font-size: 13.5px; color: ${C.ink}; white-space: nowrap; }
  .rs-pb { font-size: 10px; color: ${C.dim}; text-align: right; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; min-width: 0; } .rs-pb b { font-size: 14px; color: ${C.ink}; }
  .rs-b { display: grid; grid-template-columns: 1fr 282px; gap: 12px; align-items: start; margin-top: 3px; }
  .rs-row { display: grid; grid-template-columns: 52px 1fr; align-items: center; gap: 6px; margin: 9px 0 4px; } .rs-row .lab { font-size: 10.4px; font-weight: 700; color: ${C.dim}; }
  .rs-bar { position: relative; height: 18px; display: flex; align-items: center; }
  .rs-bar .r { height: 18px; min-width: 3px; background: #00C8FF; border-radius: 3px 0 0 3px; display: flex; align-items: center; justify-content: center; font-size: 10px; font-weight: 800; color: #0B111A; }
  .rs-bar .d { height: 18px; background: ${C.navy}; border-radius: 0 3px 3px 0; display: flex; align-items: center; justify-content: center; font-size: 10.5px; font-weight: 800; color: #fff; }
  .rs-bar .t { margin-left: 6px; font-size: 12px; font-weight: 800; color: ${C.ink}; white-space: nowrap; }
  .rs-lab { position: absolute; top: -12px; font-size: 9px; font-weight: 700; color: ${C.dim}; white-space: nowrap; }
  .rs-txt { font-size: 10.4px; color: ${C.text}; margin-top: 4px; line-height: 1.4; } .rs-txt .loss { color: ${C.loss}; }
  .rs-tbl { width: 100%; border-collapse: collapse; } .rs-tbl th { font-size: 9px; color: ${C.dim}; font-weight: 700; text-align: right; padding: 1px 5px 3px; border-bottom: 1px solid #D3DCE6; background: #EEF2F7; }
  .rs-tbl th:first-child { text-align: left; } .rs-tbl td { font-size: 10.6px; text-align: right; padding: 2px 5px; border-bottom: 1px solid #EEF2F6; } .rs-tbl td.l { text-align: left; font-weight: 600; }
  .rs-sum { border: 1px solid #E3E8EE; border-radius: 10px; padding: 7px 12px; font-size: 11px; }
  .db { display: inline-block; position: relative; width: 32px; height: 8px; vertical-align: middle; margin-right: 4px; background: linear-gradient(#C9D2DC, #C9D2DC) center / 1px 100% no-repeat; }
  .db i { position: absolute; top: 1px; height: 6px; border-radius: 3px; } .db i.f { right: 50%; background: ${C.gainMark}; } .db i.s { left: 50%; background: ${C.lossMark}; }
  .rc { border: 1px solid #E3E8EE; border-left: 5px solid #2E6BD6; border-radius: 10px; padding: 7px 13px 8px; margin-bottom: 7px; background: #fff; }
  .rc:nth-of-type(1) { border-left-color: #E65100; }
  .rc-h { display: flex; align-items: center; gap: 10px; margin-bottom: 4px; }
  .rc-n { width: 26px; height: 26px; border-radius: 50%; background: ${C.navy}; color: #fff; font-weight: 800; display: flex; align-items: center; justify-content: center; font-size: 13px; }
  .rc-t { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 16px; font-weight: 800; color: ${C.ink}; flex: 1; }
  .rc-g { font-size: 13px; font-weight: 800; color: #0E6B62; background: #DDF3EF; padding: 3px 10px; border-radius: 12px; white-space: nowrap; } .rc-g.n { color: #5B6B7B; background: #EDF0F4; }
  .rc-g.l { color: ${C.loss}; background: #FBEADF; }
  .rc-b { display: grid; grid-template-columns: 1.05fr 1fr; gap: 16px; }
  .rc-l { font-size: 9.8px; letter-spacing: 0.07em; text-transform: uppercase; color: ${C.dim}; font-weight: 800; margin: 2px 0 3px; }
  .rc ul { margin: 0; padding-left: 15px; } .rc li { font-size: 11px; margin: 1.5px 0; line-height: 1.34; }
  .rc-a { font-size: 11.2px; line-height: 1.38; margin-bottom: 2px; }
  .tbl { width: 100%; border-collapse: collapse; font-size: 11px; }
  .tbl th { background: #E8EEF5; color: #2B3F55; font-weight: 700; font-size: 9.8px; padding: 4px 5px; text-align: right; line-height: 1.2; }
  .tbl th:first-child { text-align: left; border-top-left-radius: 6px; } .tbl th:last-child { border-top-right-radius: 6px; }
  .tbl td { padding: 3.6px 5px; text-align: right; border-bottom: 1px solid #EEF2F6; white-space: nowrap; }
  .tbl td.l { text-align: left; } .tbl td.dk { background: #FFF6EC; color: #B34700; } .tbl td.aimv { background: #EAF7F4; color: #0E6B62; }
  .tbl tr.phase td { background: #FDF4EA; }
  .tbl tr.tot td { background: #F1F5F9; font-weight: 600; border-top: 1px solid #D3DCE6; } .tbl tr.tot td b { font-weight: 800; }
  .tbl.sm { font-size: 10.4px; } .tbl.sm td { padding: 2.8px 4px; } .tbl.sm th { padding: 3px 4px; font-size: 9.4px; }
  .tbl.xs { font-size: 9.6px; } .tbl.xs td { padding: 1.9px 3.5px; } .tbl.xs th { padding: 3px 3.5px; font-size: 8.8px; }
  .tbl .chip { font-size: 9.2px; padding: 0 4px; }
  .tbl th.g1 { background: #DDE6F6; color: #123B8A; text-align: center; } .tbl th.g2 { background: #E7EFFB; color: #2B5BA8; text-align: center; }
  .tbl td.sm { font-size: 9.6px; }
  .dim { color: #8A97A6; }
  .warn { color: #B34700; font-size: 9.6px; line-height: 1.3; margin: 4px 2px 0; }
  .lgrow { display: flex; gap: 16px; margin: 6px 0 0 96px; font-size: 10.5px; color: ${C.text}; }
  .lgrow.c { margin: -2px 0 6px 0; justify-content: center; }
  .lgi { display: inline-flex; align-items: center; gap: 5px; } .lgi i { width: 12px; height: 10px; border-radius: 2px; display: inline-block; }
  .lgi b.ln { width: 22px; height: 0; border-top: 2.5px solid; display: inline-block; } .lgi b.ln.dash { border-top-style: dashed; } .lgi b.ln.dot { border-top-style: dotted; }
  .two { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
  .cols { display: grid; grid-template-columns: repeat(auto-fit, minmax(0, 1fr)); gap: 8px; } .cols .box { padding: 6px 6px; }
  .ins { background: #F1F5FD; border-left: 4px solid #1C55B8; border-radius: 8px; padding: 8px 13px; margin: 2px 0 8px; }
  .ins.dakx { background: #FFF6EC; border-left-color: #E65100; }
  .ins-t { font-family: 'Inter Display', 'Inter', sans-serif; font-weight: 800; font-size: 12.5px; color: ${C.ink}; margin-bottom: 3px; }
  .ins ul { margin: 0; padding-left: 15px; } .ins li { margin: 2px 0; font-size: 11px; }
  .gloss { display: grid; grid-template-columns: 1fr 1fr; gap: 5px 18px; font-size: 10.4px; color: ${C.text}; line-height: 1.42; }
  svg.chart { display: block; overflow: visible; }
  svg text { font-family: 'Inter', 'DejaVu Sans', sans-serif; }
  .ax { font-size: 10px; fill: ${C.dim}; } .lg { font-size: 10.3px; fill: ${C.text}; } .pl { font-size: 9.6px; fill: ${C.text}; font-weight: 700; }
  .el { font-size: 10.4px; } .an { font-size: 9.6px; fill: ${C.dim}; } .tl { font-size: 11px; fill: ${C.text}; } .pv { font-size: 10.4px; font-weight: 800; }
  .nt { font-size: 10px; fill: ${C.dim}; } .sb { font-size: 10.2px; fill: #fff; font-weight: 800; } .sv { font-size: 9.8px; fill: #fff; }
  .tv { font-size: 12.5px; font-weight: 800; fill: ${C.ink}; } .zn { font-size: 11px; fill: ${C.navy}; font-weight: 700; } .et { font-size: 11.5px; font-weight: 800; fill: ${C.ink}; }
  .es { font-size: 10px; fill: ${C.dim}; } .sv2 { font-size: 11px; font-weight: 800; fill: ${C.text}; }
  .bx { font-size: 8px; fill: #9AA6B4; }
  .hs { font-size: 9.6px; fill: ${C.dim}; font-weight: 600; } .hv { font-size: 15px; font-weight: 800; fill: ${C.ink}; } .hd { font-size: 10px; fill: #8A3A00; font-weight: 700; }
  .hs.s { font-size: 8.6px; } .hv.s { font-size: 12.5px; } .hd.s { font-size: 8.8px; }
  .cmpn { font-size: 9.8px; color: ${C.dim}; margin: 5px 4px 0; line-height: 1.35; }
  /* (2.25.0) The conclusion. */
  .cc { display: flex; gap: 20px; align-items: center; background: linear-gradient(135deg, #0B111A, #14243A); color: #fff; border-radius: 12px; padding: 14px 20px; margin: 2px 0 10px; }
  .cc-big { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 40px; font-weight: 800; color: #FFB08A; line-height: 1; white-space: nowrap; }
  .cc-big small { font-size: 16px; color: #DCE6F0; font-weight: 700; }
  .cc-txt { font-size: 12.6px; color: #DCE6F0; line-height: 1.5; } .cc-txt b { color: #fff; }
  .share { display: flex; height: 22px; border-radius: 6px; overflow: hidden; margin: 9px 0 3px; background: #EEF2F6; }
  .share i { display: flex; align-items: center; justify-content: center; font-style: normal; font-size: 10.4px; font-weight: 800; color: #fff; white-space: nowrap; overflow: hidden; border-right: 2px solid #fff; }
  .share i:last-child { border-right: 0; }
  .share-n { font-size: 9.8px; color: ${C.dim}; }
  .sub-i { font-size: 9.6px; color: ${C.faint}; font-weight: 500; margin-left: 4px; }
  .dot { display: inline-block; width: 9px; height: 9px; border-radius: 2px; margin-right: 6px; vertical-align: -0.5px; }
  .cn p { font-size: 12px; line-height: 1.5; margin: 0 0 7px; color: ${C.text}; } .cn p b { color: ${C.ink}; }
  .cmpn.concl { font-size: 12.2px; color: ${C.ink}; font-weight: 700; margin: 9px 2px 0; }
  .nt0 { font-size: 10.6px; color: ${C.dim}; padding: 8px 4px; }
  `;

  return { document, slides, fromScript, analyse, recommendations, blank, aligned, num, ok };
})();
