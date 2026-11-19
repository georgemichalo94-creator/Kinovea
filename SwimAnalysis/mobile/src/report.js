// The report of a start as one picture of the proportions of A4 (1240 × 1754): what is saved as PDF or as an image.
// Light paper, for reading on any screen and for printing.
function swimReportFactory(M) {
  'use strict';

  var W = 1240, H = 1754, L = 84, R = W - 84;
  var DISPLAY = '"Sofia Sans Semi Condensed", "Arial Narrow", "Roboto Condensed", sans-serif';
  var BODY = 'Inter, "Segoe UI", Roboto, system-ui, sans-serif';
  var PAPER = '#FFFFFF', INK = '#06202E', SOFT = '#4C6A77', RULE = '#D5E3EA', TINT = '#EEF6F9', BAND = '#06283A';
  var ACCENT = '#0A8FB3', BRIGHT = '#38E1FF', PALE = '#A9DCE8', GOOD = '#0B7F55', WARN = '#A65F00';

  /**
   * a: the analysis; o: { thumbs: [{ id, canvas }], list: all the analyses (for the differences), tech: the summary of
   * the technique { ok, checked, items: [{ name, cue }] }, version }. Returns the canvas.
   */
  function draw(a, o) {
    var c = document.createElement('canvas');
    c.width = W; c.height = H;
    var g = c.getContext('2d'), m = M.measure(a), y, i;

    function font(weight, size, display) { g.font = weight + ' ' + size + 'px ' + (display ? DISPLAY : BODY); }
    function put(s, x, yy, color, align) { g.fillStyle = color; g.textAlign = align || 'left'; g.fillText(s, x, yy); }
    function width(s) { return g.measureText(s).width; }
    function spacing(px) { if ('letterSpacing' in g) g.letterSpacing = px + 'px'; }
    function fit(s, max) {
      if (width(s) <= max) return s;
      while (s.length > 1 && width(s + '…') > max) s = s.slice(0, -1);
      return s + '…';
    }
    function lines(s, max) {
      var out = [];
      String(s).split('\n').forEach(function (part) {
        var words = part.split(/\s+/), line = '', test, n;
        for (n = 0; n < words.length; n++) {
          test = line ? line + ' ' + words[n] : words[n];
          if (line && width(test) > max) { out.push(line); line = words[n]; } else line = test;
        }
        if (line) out.push(line);
      });
      return out;
    }
    function wrap(s, x, yy, max, lineHeight, color, most) {
      var all = lines(s, max), n;
      if (most && all.length > most) { all = all.slice(0, most); all[most - 1] = fit(all[most - 1] + ' …', max); }
      for (n = 0; n < all.length; n++) put(all[n], x, yy + n * lineHeight, color);
      return yy + all.length * lineHeight;
    }
    function label(s, x, yy, color, align) { font(700, 20, false); spacing(2.5); put(s, x, yy, color, align); spacing(0); }
    function difference(row, value) {
      var before = M.previous(o.list || [], a, row.id), prev = before ? M.measure(before)[row.id] : null;
      if (value === null || prev === null) return null;
      var d = M.round2(value - prev);
      return { text: M.fmtDelta(value, prev), prev: prev, color: row.less === null || d === 0 ? SOFT : (d < 0) === row.less ? GOOD : WARN };
    }

    g.textBaseline = 'alphabetic';
    g.fillStyle = PAPER; g.fillRect(0, 0, W, H);

    // The band at the top: who, when.
    g.fillStyle = BAND; g.fillRect(0, 0, W, 214);
    label('ΑΝΑΛΥΣΗ ΕΚΚΙΝΗΣΗΣ', L, 76, BRIGHT);
    font(800, 72, true);
    put(fit((a.athlete || '').trim() || 'Χωρίς όνομα', R - L - 320), L, 156, PAPER);
    font(600, 28, false); put(M.dateText(a.created), R, 104, PAPER, 'right');
    font(400, 25, false); if (a.stroke) put(M.strokeName(a.stroke), R, 142, PALE, 'right');
    label('KINOVEA SWIM', R, 184, BRIGHT, 'right');
    y = 214;

    // The number that sums it up.
    var head = M.headline(a);
    if (head) {
      y += 150;
      var big = M.fmt(head.value), x;
      font(800, 136, true); put(big, L, y, INK); x = L + width(big) + 14;
      font(700, 46, true); put(head.unit, x, y, SOFT); x += width(head.unit) + 34;
      font(600, 34, false);
      put(head.id === 'flight' || head.id === 'block' ? head.label : 'στα ' + head.label, x, y - 52, INK);
      var hd = difference({ id: head.id, less: head.id === 'flight' ? null : true }, head.value);
      if (hd) { font(600, 26, false); put(hd.text + ' s από την προηγούμενη (' + M.fmt(hd.prev) + ')', x, y - 10, hd.color); }
      y += 30;
    }

    // The frames of the marks.
    var thumbs = (o.thumbs || []).slice(0, 3);
    if (thumbs.length) {
      y += 34;
      var gap = 22, tw = (R - L - 2 * gap) / 3, ratio = thumbs[0].canvas.height / Math.max(1, thumbs[0].canvas.width);
      var th = Math.round(Math.min(250, tw * ratio)), t0 = M.signalTime(a);
      thumbs.forEach(function (t, n) {
        var bx = L + n * (tw + gap), s = Math.min(tw / t.canvas.width, th / t.canvas.height);
        var dw = t.canvas.width * s, dh = t.canvas.height * s, time = M.markTime(a, t.id);
        g.fillStyle = INK; g.fillRect(bx, y, tw, th);
        g.drawImage(t.canvas, bx + (tw - dw) / 2, y + (th - dh) / 2, dw, dh);
        font(600, 23, false); put(fit(M.eventOf(t.id).short, tw - 110), bx, y + th + 34, INK);
        if (t0 !== null && time !== null) { font(700, 26, true); put(M.fmt(time - t0) + ' s', bx + tw, y + th + 34, ACCENT, 'right'); }
      });
      y += th + 56;
    }

    // The measurements.
    var rows = M.ROWS.filter(function (r) { return m[r.id] !== null; }), anyDifference = false;
    if (rows.length) {
      y += 46;
      label('ΜΕΤΡΗΣΕΙΣ', L, y, SOFT);
      var top = y;
      y += 18;
      rows.forEach(function (r, n) {
        var d = difference(r, m[r.id]), value = M.fmt(m[r.id]);
        if (n % 2 === 0) { g.fillStyle = TINT; g.fillRect(L, y, R - L, 50); }
        font(500, 24, false); put(r.name, L + 18, y + 33, INK);
        font(700, 30, true); put(value, R - 236, y + 35, INK, 'right');
        font(500, 21, false); put(r.unit, R - 226, y + 33, SOFT);
        if (d) { anyDifference = true; font(600, 22, false); put(d.text, R - 18, y + 33, d.color, 'right'); }
        y += 50;
      });
      if (anyDifference) { font(400, 19, false); put('διαφορά από την προηγούμενη', R, top, SOFT, 'right'); }
      g.fillStyle = RULE; g.fillRect(L, y, R - L, 2);
      y += 2;
    }

    // What does not add up, said as on the screen.
    var warnings = M.warnings(a);
    if (warnings.length && y < H - 260) {
      y += 40;
      font(500, 22, false);
      warnings.slice(0, 2).forEach(function (wtext) { y = wrap('! ' + wtext, L, y, R - L, 30, WARN, 2) + 4; });
      y -= 12;
    }

    // The technique.
    var tech = o.tech;
    if (tech && tech.checked > 0 && y < H - 240) {
      y += 60;
      label('ΤΕΧΝΙΚΗ', L, y, SOFT);
      y += 44;
      font(600, 27, false);
      put(tech.ok + ' από ' + tech.checked + ' σημεία σωστά' + (tech.items.length ? ' · ' + (tech.items.length === 1 ? '1 θέλει δουλειά' : tech.items.length + ' θέλουν δουλειά') : ''), L, y, INK);
      y += 14;
      for (i = 0; i < tech.items.length; i++) {
        if (y > H - 230) { font(400, 21, false); put('… και ' + (tech.items.length - i) + ' ακόμα', L + 26, y + 34, SOFT); y += 34; break; }
        y += 38;
        g.fillStyle = WARN; g.fillRect(L, y - 17, 12, 12);
        font(600, 23, false); put(fit(tech.items[i].name, R - L - 30), L + 26, y, INK);
        if (tech.items[i].cue) { font(400, 21, false); y = wrap('Διόρθωση: ' + tech.items[i].cue, L + 26, y + 30, R - L - 30, 28, SOFT, 2) - 28; }
      }
    }

    // The notes of the coach.
    if (a.notes && a.notes.trim() && y < H - 210) {
      y += 62;
      label('ΣΗΜΕΙΩΣΕΙΣ', L, y, SOFT);
      font(400, 23, false);
      y = wrap(a.notes.trim(), L, y + 42, R - L, 32, INK, Math.max(1, Math.floor((H - 130 - y - 42) / 32)));
    }

    // The foot: how exact, from what.
    g.fillStyle = RULE; g.fillRect(L, H - 96, R - L, 2);
    font(400, 18, false);
    var v = a.video, s = a.signal, foot = [];
    if (v && v.fps) foot.push('Βίντεο ' + (Math.abs(v.fps - Math.round(v.fps)) < 0.02 ? Math.round(v.fps) : v.fps.toFixed(2).replace('.', ',')) + ' fps: κάθε σημάδι ±' + M.fmt(1 / v.fps, v.fps > 100 ? 3 : 2) + ' s');
    if (s && s.src === 'sound') foot.push('Σήμα από τον ήχο' + (s.dist ? ', ' + s.dist + ' μ. από το ηχείο' : ''));
    else if (s) foot.push('Σήμα σημαδεμένο στο καρέ');
    put(fit(foot.join(' · '), R - L - 300), L, H - 58, SOFT);
    put('Kinovea Swim Κινητό ' + (o.version || ''), R, H - 58, SOFT, 'right');
    return c;
  }

  return { draw: draw, width: W, height: H };
}

if (typeof module !== 'undefined' && module.exports) module.exports = swimReportFactory;
