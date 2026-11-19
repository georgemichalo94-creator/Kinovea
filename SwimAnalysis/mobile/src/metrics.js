// The moments marked on a start and the measurements made of them. The names are the ones of the desktop program.
function swimMetricsFactory() {
  'use strict';

  /** Speed of sound (m/s): the signal reaches the phone this much later for every metre from the loudspeaker. */
  var SOUND = 343;

  var EVENTS = [
    { id: 'signal', name: 'Σήμα εκκίνησης', short: 'Σήμα', hint: 'Η κόρνα. Από εδώ μετράνε όλοι οι χρόνοι.' },
    { id: 'react', name: 'Αντίδραση (πρώτη κίνηση)', short: 'Αντίδραση', hint: 'Η πρώτη κίνηση του αθλητή μετά το σήμα.' },
    { id: 'takeoff', name: 'Αποδέσμευση από τον βατήρα', short: 'Αποδέσμευση', hint: 'Το μπροστινό πόδι αφήνει τον βατήρα.' },
    { id: 'entry', name: 'Είσοδος στο νερό', short: 'Είσοδος', hint: 'Τα χέρια μπαίνουν στο νερό.' },
    { id: 'm5', name: '5 μ.', short: '5 μ.', hint: 'Το κεφάλι περνά τα 5 μ.' },
    { id: 'm10', name: '10 μ.', short: '10 μ.', hint: 'Το κεφάλι περνά τα 10 μ.' },
    { id: 'm15', name: '15 μ.', short: '15 μ.', hint: 'Το κεφάλι περνά τα 15 μ.' },
    { id: 'breakout', name: 'Έξοδος από το νερό (κεφάλι)', short: 'Έξοδος', hint: 'Το κεφάλι βγαίνει από το νερό.' }
  ];

  var STROKES = [
    { id: 'fs', name: 'Ελεύθερο' }, { id: 'bk', name: 'Ύπτιο' }, { id: 'br', name: 'Πρόσθιο' }, { id: 'fly', name: 'Πεταλούδα' }
  ];

  function eventOf(id) { for (var i = 0; i < EVENTS.length; i++) if (EVENTS[i].id === id) return EVENTS[i]; return null; }
  function strokeName(id) { for (var i = 0; i < STROKES.length; i++) if (STROKES[i].id === id) return STROKES[i].name; return ''; }

  /** Time of the film (s) at which the signal was given: the onset heard, less the way of the sound to the phone. */
  function signalTime(a) {
    var s = a && a.signal;
    if (!s || typeof s.t !== 'number') return null;
    return s.src === 'sound' ? s.t - (s.dist || 0) / SOUND : s.t;
  }

  /** Time of the film of a mark (the signal included), null when it is not marked. */
  function markTime(a, id) {
    if (id === 'signal') return signalTime(a);
    var m = a && a.marks && a.marks[id];
    return m && typeof m.t === 'number' ? m.t : null;
  }

  /** Hundredths, half away from zero (as the desktop program writes its times). */
  function round2(v) {
    if (v === null || v === undefined || isNaN(v)) return null;
    var s = v < 0 ? -1 : 1;
    return s * Math.round(Math.abs(v) * 100 + 1e-7) / 100;
  }

  /**
   * The measurements of an analysis (s and m/s, to the hundredth; null: not marked):
   * from the signal — react, block (the official reaction time), entry, m5, m10, m15, breakout;
   * between marks — blockPhase, flight, underwater; speeds — v510, v1015.
   */
  function measure(a) {
    var t0 = signalTime(a), t = {};
    EVENTS.forEach(function (e) { t[e.id] = markTime(a, e.id); });
    function from(id) { return t0 !== null && t[id] !== null ? round2(t[id] - t0) : null; }
    function between(p, q) { return t[p] !== null && t[q] !== null ? round2(t[q] - t[p]) : null; }
    function speed(p, q, metres) {
      if (t[p] === null || t[q] === null || !(t[q] - t[p] > 0)) return null;
      return round2(metres / (t[q] - t[p]));
    }
    return {
      react: from('react'), block: from('takeoff'), entry: from('entry'),
      m5: from('m5'), m10: from('m10'), m15: from('m15'), breakout: from('breakout'),
      blockPhase: between('react', 'takeoff'), flight: between('takeoff', 'entry'), underwater: between('entry', 'breakout'),
      v510: speed('m5', 'm10', 5), v1015: speed('m10', 'm15', 5)
    };
  }

  /** The rows shown and printed, in order: what is measured, its unit, and whether less is better (for the differences). */
  var ROWS = [
    { id: 'react', name: 'Αντίδραση (πρώτη κίνηση)', unit: 's', less: true },
    { id: 'block', name: 'Αποδέσμευση από τον βατήρα', unit: 's', less: true },
    { id: 'flight', name: 'Πτήση: αποδέσμευση → είσοδος', unit: 's', less: null },
    { id: 'entry', name: 'Είσοδος στο νερό', unit: 's', less: null },
    { id: 'm5', name: '5 μ.', unit: 's', less: true },
    { id: 'm10', name: '10 μ.', unit: 's', less: true },
    { id: 'm15', name: '15 μ.', unit: 's', less: true },
    { id: 'breakout', name: 'Έξοδος από το νερό (κεφάλι)', unit: 's', less: null },
    { id: 'underwater', name: 'Υποβρύχια φάση: είσοδος → έξοδος', unit: 's', less: null },
    { id: 'v510', name: 'Ταχύτητα 5 → 10 μ.', unit: 'm/s', less: false },
    { id: 'v1015', name: 'Ταχύτητα 10 → 15 μ.', unit: 'm/s', less: false }
  ];

  /** What does not add up in the marks, in words for the coach. */
  function warnings(a) {
    var out = [], chain = ['signal', 'react', 'takeoff', 'entry', 'm5', 'm10', 'm15'], i, j, p, q;
    for (i = 0; i < chain.length; i++) {
      p = markTime(a, chain[i]);
      if (p === null) continue;
      for (j = i + 1; j < chain.length; j++) {
        q = markTime(a, chain[j]);
        if (q !== null && q <= p) {
          out.push('«' + eventOf(chain[j]).short + '» πριν από «' + eventOf(chain[i]).short + '»: έλεγξε τα δύο σημάδια.');
          break;
        }
      }
    }
    var entry = markTime(a, 'entry'), out2 = markTime(a, 'breakout');
    if (entry !== null && out2 !== null && out2 <= entry) out.push('«Έξοδος» πριν από «Είσοδος»: έλεγξε τα δύο σημάδια.');
    var m = measure(a);
    if (m.block !== null && m.block > 0 && (m.block < 0.45 || m.block > 1.1)) {
      out.push('Αποδέσμευση στα ' + fmt(m.block) + ' s από το σήμα (συνήθως 0,55–0,95 s). Έλεγξε το καρέ του σήματος εκκίνησης.');
    }
    if (m.react !== null && m.react >= 0 && m.react < 0.1) {
      out.push('Πρώτη κίνηση στα ' + fmt(m.react) + ' s από το σήμα: πολύ νωρίς. Έλεγξε το σήμα εκκίνησης.');
    }
    return out;
  }

  /** A number with a comma, as written in Greek. */
  function fmt(v, digits) {
    if (v === null || v === undefined || isNaN(v)) return '—';
    return v.toFixed(digits === undefined ? 2 : digits).replace('.', ',').replace('-', '−');
  }

  /** A difference with its sign: +0,03 or −0,11 (0,00 when the two are equal to the hundredth); empty without both. */
  function fmtDelta(now, before) {
    if (now === null || before === null || now === undefined || before === undefined) return '';
    var d = round2(now - before);
    if (d === 0) return '0,00';
    return (d > 0 ? '+' : '−') + Math.abs(d).toFixed(2).replace('.', ',');
  }

  /** The analysis of the same athlete just before this one that has this measurement. */
  function previous(list, a, id) {
    var name = (a.athlete || '').trim().toLocaleLowerCase('el'), best = null, i, b, v;
    if (!name) return null;
    for (i = 0; i < list.length; i++) {
      b = list[i];
      if (b.id === a.id || (b.athlete || '').trim().toLocaleLowerCase('el') !== name) continue;
      if (!(b.created < a.created)) continue;
      v = measure(b)[id];
      if (v === null) continue;
      if (!best || b.created > best.created) best = b;
    }
    return best;
  }

  /** The last analyses of the athlete (this one included, the oldest first) that have this measurement. */
  function history(list, a, id, count) {
    var name = (a.athlete || '').trim().toLocaleLowerCase('el');
    var rows = list.filter(function (b) {
      return b.id === a.id || (name && (b.athlete || '').trim().toLocaleLowerCase('el') === name && b.created <= a.created);
    }).map(function (b) { return { id: b.id, created: b.created, value: measure(b)[id] }; })
      .filter(function (r) { return r.value !== null; })
      .sort(function (p, q) { return p.created < q.created ? -1 : p.created > q.created ? 1 : 0; });
    return rows.slice(Math.max(0, rows.length - count));
  }

  /** The one number that sums an analysis up, for the lists: the furthest distance timed. */
  function headline(a) {
    var m = measure(a);
    if (m.m15 !== null) return { label: '15 μ.', value: m.m15, unit: 's', id: 'm15' };
    if (m.m10 !== null) return { label: '10 μ.', value: m.m10, unit: 's', id: 'm10' };
    if (m.m5 !== null) return { label: '5 μ.', value: m.m5, unit: 's', id: 'm5' };
    if (m.block !== null) return { label: 'Αποδέσμευση', value: m.block, unit: 's', id: 'block' };
    if (m.flight !== null) return { label: 'Πτήση', value: m.flight, unit: 's', id: 'flight' };
    return null;
  }

  /** The analysis in a few lines of text, to paste in a message. */
  function summary(a, tech) {
    var m = measure(a), lines = [], parts;
    var who = (a.athlete || 'Αθλητής') + (a.stroke ? ' · ' + strokeName(a.stroke).toLocaleLowerCase('el') : '');
    lines.push('Εκκίνηση — ' + who + ' · ' + dateText(a.created));
    parts = [];
    if (m.react !== null) parts.push('Αντίδραση ' + fmt(m.react) + ' s');
    if (m.block !== null) parts.push('Αποδέσμευση ' + fmt(m.block) + ' s');
    if (m.flight !== null) parts.push('Πτήση ' + fmt(m.flight) + ' s');
    if (m.entry !== null) parts.push('Είσοδος ' + fmt(m.entry) + ' s');
    if (parts.length) lines.push(parts.join(' · '));
    parts = [];
    if (m.m5 !== null) parts.push('5 μ. ' + fmt(m.m5) + ' s');
    if (m.m10 !== null) parts.push('10 μ. ' + fmt(m.m10) + ' s');
    if (m.m15 !== null) parts.push('15 μ. ' + fmt(m.m15) + ' s');
    if (parts.length) lines.push(parts.join(' · '));
    parts = [];
    if (m.breakout !== null) parts.push('Έξοδος ' + fmt(m.breakout) + ' s');
    if (m.underwater !== null) parts.push('Υποβρύχια ' + fmt(m.underwater) + ' s');
    if (m.v510 !== null) parts.push('5→10 μ. ' + fmt(m.v510) + ' m/s');
    if (m.v1015 !== null) parts.push('10→15 μ. ' + fmt(m.v1015) + ' m/s');
    if (parts.length) lines.push(parts.join(' · '));
    if (tech && tech.checked > 0) {
      lines.push('Τεχνική: ' + tech.ok + ' από ' + tech.checked + ' σημεία σωστά.' + (tech.fix.length ? ' Θέλουν δουλειά: ' + tech.fix.join(', ') + '.' : ''));
    }
    if (a.notes && a.notes.trim()) lines.push('Σημειώσεις: ' + a.notes.trim());
    return lines.join('\n');
  }

  function dateText(iso) {
    var d = new Date(iso);
    if (isNaN(d.getTime())) return '';
    function two(n) { return (n < 10 ? '0' : '') + n; }
    return two(d.getDate()) + '/' + two(d.getMonth() + 1) + '/' + d.getFullYear();
  }

  return {
    SOUND: SOUND, EVENTS: EVENTS, STROKES: STROKES, ROWS: ROWS,
    eventOf: eventOf, strokeName: strokeName, signalTime: signalTime, markTime: markTime, round2: round2,
    measure: measure, warnings: warnings, fmt: fmt, fmtDelta: fmtDelta, previous: previous, history: history,
    headline: headline, summary: summary, dateText: dateText
  };
}

if (typeof module !== 'undefined' && module.exports) module.exports = swimMetricsFactory;
