// The screens of the phone app: home, new analysis, marking on the video, measurements, technique, report, settings.
// Everything stays on the phone: the video is read where it is, the analyses are kept in the storage of the browser.
function swimAppStart(boot) {
  'use strict';

  var VERSION = '0.1.0';
  var M = swimMetricsFactory(), Mp4 = swimMp4Factory(), Player = swimPlayerFactory(Mp4);
  var Sound = swimSoundFactory(Mp4, swimHornFactory), Pdf = swimPdfFactory(), Report = swimReportFactory(M);

  // ---------------------------------------------------------------- small tools
  function $(id) { return document.getElementById(id); }
  var NS = 'http://www.w3.org/2000/svg';
  var ICONS = {
    back: 'M15 5l-7 7 7 7', next: 'M9 5l7 7-7 7', check: 'M5 12.5l4.5 4.5L19 7', cross: 'M6 6l12 12M18 6L6 18',
    sliders: 'M4 7h9M17 7h3M4 17h3M11 17h9M15 5v4M9 15v4',
    camera: 'M4 8h3.2l1.6-2.5h6.4L16.8 8H20v11H4z|M12 16.3a3 3 0 1 0 0-6 3 3 0 0 0 0 6z',
    gallery: 'M4 5h16v14H4z|M4 15.5l4.5-4.5 4 4 2.5-2.5 5 5|M8.5 9.5h.01',
    play: '*M8 5.5v13l11-6.5z', pause: '*M7 5h3.6v14H7zM13.4 5H17v14h-3.6z',
    trash: 'M5 7h14M10 7V5h4v2M7.5 7l.8 12h7.4l.8-12',
    sound: 'M4 10v4h3l4 3.5v-11L7 10z|M15 9.5a3.5 3.5 0 0 1 0 5|M17.5 7a7 7 0 0 1 0 10',
    mute: 'M4 10v4h3l4 3.5v-11L7 10z|M16 9.5l5 5M21 9.5l-5 5',
    wave: 'M3 12h2l2-5 3 11 3-14 3 11 2-3h3',
    warn: 'M12 4l9 16H3z|M12 10v4.5|M12 17.5h.01',
    save: 'M12 15V4M8 8l4-4 4 4|M5 13v5a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-5',
    copy: 'M9 9h10v11H9z|M5 15V4h10', plus: 'M12 5v14M5 12h14',
    down: 'M12 5v14M6 13l6 6 6-6', up: 'M12 19V5M6 11l6-6 6 6', home: 'M4 11l8-7 8 7|M6 10v10h12V10'
  };

  function icon(name, size) {
    var s = document.createElementNS(NS, 'svg'), d = ICONS[name] || '', fill = d.charAt(0) === '*';
    s.setAttribute('viewBox', '0 0 24 24');
    s.setAttribute('width', size || 22); s.setAttribute('height', size || 22);
    s.setAttribute('fill', fill ? 'currentColor' : 'none');
    s.setAttribute('stroke', fill ? 'none' : 'currentColor');
    s.setAttribute('stroke-width', '2'); s.setAttribute('stroke-linecap', 'round'); s.setAttribute('stroke-linejoin', 'round');
    s.setAttribute('aria-hidden', 'true');
    (fill ? d.slice(1) : d).split('|').forEach(function (p) {
      var e = document.createElementNS(NS, 'path');
      e.setAttribute('d', p);
      s.appendChild(e);
    });
    return s;
  }

  function h(tag, props, kids) {
    var e = document.createElement(tag), k;
    for (k in (props || {})) {
      if (props[k] === null || props[k] === undefined || props[k] === false) continue;
      if (k === 'class') e.className = props[k];
      else if (k === 'text') e.textContent = props[k];
      else if (k.slice(0, 2) === 'on') e.addEventListener(k.slice(2), props[k]);
      else if (k === 'disabled' || k === 'hidden' || k === 'value') e[k] = props[k];
      else e.setAttribute(k, props[k] === true ? '' : props[k]);
    }
    (kids || []).forEach(function (c) {
      if (c === null || c === undefined || c === false) return;
      e.appendChild(typeof c === 'string' ? document.createTextNode(c) : c);
    });
    return e;
  }

  function clear(node) { while (node.firstChild) node.removeChild(node.firstChild); return node; }
  function fill(node, kids) { clear(node); (kids || []).forEach(function (c) { if (c) node.appendChild(typeof c === 'string' ? document.createTextNode(c) : c); }); return node; }
  var tokens = {};
  function css(name) {
    if (!tokens[name]) tokens[name] = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    return tokens[name];
  }
  function fmt3(v) { return v.toFixed(3).replace('.', ',').replace('-', '−'); }
  function seconds(v) { return M.fmt(v, v >= 100 ? 0 : 1) + ' s'; }
  /** How exact a mark is: one frame of the video (s). */
  function frameText(fps) { return M.fmt(1 / fps, fps > 100 ? 3 : 2) + ' s'; }
  function fpsText(fps) {
    var r = Math.round(fps);
    return (Math.abs(fps - r) < 0.02 ? String(r) : fps.toFixed(2).replace('.', ',')) + ' fps';
  }

  // ---------------------------------------------------------------- what is kept on the phone
  var KEY = 'kinovea-swim-mobile', storageFailed = false;
  var DB = { v: 1, analyses: [], tech: null, settings: {} };

  var EXAMPLE_TECH = {
    example: true,
    phases: [
      { id: 'block', name: 'Βατήρας', items: [
        { id: 'b1', name: 'Ισχία ψηλά, βάρος μπροστά στη θέση ετοιμότητας', cue: 'Φέρε τους γοφούς πάνω από το μπροστινό πόδι.' },
        { id: 'b2', name: 'Τα χέρια τραβούν τον βατήρα με το σήμα', cue: 'Τράβα τον βατήρα προς τα πίσω μόλις ακούσεις το σήμα.' },
        { id: 'b3', name: 'Ώθηση και από τα δύο πόδια', cue: 'Σπρώξε πρώτα με το πίσω πόδι, τέλειωσε με το μπροστινό.' }
      ] },
      { id: 'flight', name: 'Πτήση', items: [
        { id: 'f1', name: 'Σώμα τεντωμένο στην πτήση', cue: 'Σφιχτός κορμός, ψηλά οι γοφοί.' },
        { id: 'f2', name: 'Κεφάλι ανάμεσα στα χέρια πριν την είσοδο', cue: 'Το βλέμμα κάτω, τα αυτιά ανάμεσα στα μπράτσα.' }
      ] },
      { id: 'entry', name: 'Είσοδος', items: [
        { id: 'e1', name: 'Όλο το σώμα μπαίνει από το ίδιο σημείο', cue: 'Χέρια, κεφάλι, γοφοί και πόδια από την ίδια «τρύπα» στο νερό.' },
        { id: 'e2', name: 'Χέρια ενωμένα, το ένα πάνω στο άλλο', cue: 'Κλείδωσε τις παλάμες πριν αγγίξεις το νερό.' }
      ] },
      { id: 'under', name: 'Υποβρύχια', items: [
        { id: 'u1', name: 'Streamline μετά την είσοδο', cue: 'Τεντωμένα χέρια και σφιχτό σώμα ως τον πρώτο δελφινισμό.' },
        { id: 'u2', name: 'Έξοδος με ταχύτητα', cue: 'Κράτα το κεφάλι κάτω στην πρώτη χεριά.' }
      ] }
    ]
  };

  function loadDb() {
    try {
      var raw = window.localStorage.getItem(KEY), d = raw ? JSON.parse(raw) : null;
      if (d && d.v === 1 && Array.isArray(d.analyses)) DB = d;
    } catch (e) {
      storageFailed = true;
    }
    DB.settings = DB.settings || {};
    if (typeof DB.settings.hornDist !== 'number') DB.settings.hornDist = 0;
    if (typeof DB.settings.soundFix !== 'number') DB.settings.soundFix = 0;
  }

  function saveDb() {
    try {
      window.localStorage.setItem(KEY, JSON.stringify(DB));
      storageFailed = false;
    } catch (e) {
      storageFailed = true;
    }
  }

  function techList() { return DB.tech || EXAMPLE_TECH; }
  function byId(id) { for (var i = 0; i < DB.analyses.length; i++) if (DB.analyses[i].id === id) return DB.analyses[i]; return null; }
  function kept(a) { return !!a && DB.analyses.indexOf(a) >= 0; }
  function touch(a) { a.updated = new Date().toISOString(); S.report = null; S.reportError = ''; if (kept(a)) saveDb(); }
  function soundFix() { return (DB.settings.soundFix || 0) / 1000; }

  function newAnalysis() {
    return {
      id: 'a' + Date.now().toString(36) + Math.floor(Math.random() * 46656).toString(36), kind: 'start',
      created: new Date().toISOString(), athlete: '', stroke: '', video: null, signal: null, marks: {}, tech: {}, notes: ''
    };
  }

  function title(a) { return (a.athlete || '').trim() || 'Χωρίς όνομα'; }
  function subtitle(a) {
    return 'Εκκίνηση' + (a.stroke ? ' · ' + M.strokeName(a.stroke).toLocaleLowerCase('el') : '') + ' · ' + M.dateText(a.created);
  }

  function techSummary(a) {
    var ok = 0, checked = 0, fix = [], items = [];
    techList().phases.forEach(function (p) {
      p.items.forEach(function (it) {
        var v = a.tech && a.tech[it.id];
        if (!v) return;
        checked++;
        if (v === 'ok') ok++; else { fix.push(it.name); items.push({ name: it.name, cue: it.cue || '', phase: p.name }); }
      });
    });
    return { ok: ok, checked: checked, fix: fix, items: items };
  }

  // ---------------------------------------------------------------- the session
  var S = {
    view: 'home', A: null, file: null, loadedFor: null, loading: false, loadError: '', info: null,
    sound: null, soundBusy: false, sel: 'signal', rate: 0.25, phase: 0, reportToken: 0, report: null, reportBusy: false, reportError: ''
  };
  var VIEWS = ['home', 'setup', 'mark', 'result', 'tech', 'report', 'settings'];
  var RENDER = {};
  var downloads = null, wakeLock = null;

  function show(name) {
    if (S.view === 'mark' && name !== 'mark') Player.pause();
    VIEWS.forEach(function (v) {
      var e = $('v-' + v);
      if (v === 'mark' && name !== 'mark' && (S.loading || S.loadedFor)) {
        // The video stays drawn (unseen) under the other screens: the phone keeps its frames ready for the report.
        e.hidden = false; e.classList.add('under');
      } else {
        e.classList.remove('under'); e.hidden = v !== name;
      }
    });
    S.view = name;
    closeSheet();
    wake(name === 'mark');
    if (RENDER[name]) RENDER[name]();
  }

  function wake(on) {
    try {
      if (on && navigator.wakeLock && !wakeLock) {
        navigator.wakeLock.request('screen').then(function (l) { wakeLock = l; l.addEventListener('release', function () { wakeLock = null; }); }, function () { /* not granted */ });
      } else if (!on && wakeLock) {
        wakeLock.release(); wakeLock = null;
      }
    } catch (e) { /* no wake lock on this phone */ }
  }

  // ---------------------------------------------------------------- messages
  var toastTimer = 0;
  function toast(text, action, run) {
    var t = $('toast');
    fill(t, [h('span', { text: text }), action ? h('button', { type: 'button', text: action, onclick: function () { t.hidden = true; run(); } }) : null]);
    t.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { t.hidden = true; }, action ? 6000 : 2600);
  }

  var flashTimer = 0;
  function flash(text) {
    var f = $('flash');
    f.textContent = text; f.hidden = false;
    clearTimeout(flashTimer);
    flashTimer = setTimeout(function () { f.hidden = true; }, 1400);
  }

  function openSheet(kids) { fill($('sheet'), kids); $('backdrop').hidden = false; }
  function closeSheet() { $('backdrop').hidden = true; clear($('sheet')); }

  function confirmSheet(titleText, body, yes, run, danger) {
    openSheet([
      h('h2', { text: titleText }), body ? h('p', { class: 'muted', text: body }) : null,
      h('button', { class: 'btn ' + (danger ? 'danger' : 'primary'), type: 'button', text: yes, onclick: function () { closeSheet(); run(); } }),
      h('button', { class: 'btn', type: 'button', text: 'Άκυρο', onclick: closeSheet })
    ]);
  }

  // ---------------------------------------------------------------- home
  RENDER.home = function () {
    var list = DB.analyses.slice().sort(function (a, b) { return a.created < b.created ? 1 : -1; });
    var box = clear($('home-list'));
    if (list.length === 0) {
      box.appendChild(h('div', { class: 'empty', text: 'Καμία ανάλυση ακόμα. Η πρώτη σου θα φανεί εδώ, με τον χρόνο στα 15 μ.' }));
    }
    list.slice(0, 40).forEach(function (a) {
      var head = M.headline(a), stroke = a.stroke ? M.strokeName(a.stroke).slice(0, 2).toLocaleUpperCase('el') : 'ΕΚ';
      box.appendChild(h('button', { class: 'item', type: 'button', onclick: function () { S.A = a; show('result'); } }, [
        h('span', { class: 'badge', text: stroke }),
        h('span', { class: 'who' }, [h('b', { text: title(a) }), h('span', { text: subtitle(a) })]),
        h('span', { class: 'val' }, head
          ? [h('b', { text: M.fmt(head.value) + ' ' + head.unit }), h('span', { text: head.label })]
          : [h('span', { text: 'Θέλει σημάδια' })])
      ]));
    });
    var note = $('home-storage');
    note.hidden = !storageFailed;
    if (storageFailed) fill(note, [icon('warn', 20), h('span', { text: 'Εδώ το κινητό δεν κρατάει αποθηκευμένες αναλύσεις: θα χαθούν όταν κλείσεις τη σελίδα. Αποθήκευσε την αναφορά πριν φύγεις.' })]);
  };

  function pickedVideo(input, reopen) {
    var file = input.files && input.files[0];
    input.value = '';
    if (!file) return;
    if (reopen && S.A) {
      var v = S.A.video;
      if (v && v.size && v.size !== file.size) {
        confirmSheet('Άλλο βίντεο;', 'Αυτό το βίντεο δεν έχει το μέγεθος εκείνου της ανάλυσης. Αν είναι άλλο, τα σημάδια δεν θα ταιριάζουν.', 'Άνοιγμα', function () { openVideo(file, S.A, true); });
      } else {
        openVideo(file, S.A, true);
      }
      return;
    }
    S.A = newAnalysis();
    show('setup');
    openVideo(file, S.A, false);
  }

  // ---------------------------------------------------------------- the video and its sound
  async function openVideo(file, a, toMark) {
    S.file = file; S.loading = true; S.loadedFor = null; S.loadError = ''; S.info = null; S.sound = null; S.soundBusy = false;
    S.report = null;
    Zoom.reset();
    if (toMark) show('mark'); else show(S.view);
    try {
      var r = await Player.load($('video'), file);
      if (S.file !== file) return;
      var st = Player.state();
      S.info = r.info; S.loadedFor = a.id; S.loading = false;
      a.video = {
        size: file.size, duration: Math.round(r.duration * 1000) / 1000, fps: Math.round(st.fps * 1000) / 1000, frames: st.count,
        source: st.source, width: r.width, height: r.height
      };
      if (r.width > 0 && r.height > 0) $('stage').style.setProperty('--ar', r.width + ' / ' + r.height);
      touch(a);
      S.soundBusy = true;
      refresh();
      if (a.signal && S.view === 'mark') Player.go(Player.frameAt(a.signal.t));
    } catch (e) {
      if (S.file !== file) return;
      S.loading = false; S.soundBusy = false; S.loadError = (e && e.message) || String(e);
      refresh();
      return;
    }

    // The sound, apart: whatever happens to it, the video is open and can be marked.
    var s = null;
    try {
      s = await Promise.race([
        Sound.analyse(file, S.info, 120),
        new Promise(function (resolve) { setTimeout(function () { resolve({ status: 'none', path: '', start: 0, lead: 0, seconds: 0, beeps: [], choice: { list: [], best: null, clear: false }, note: 'too long' }); }, 75000); })
      ]);
    } catch (e) {
      s = { status: 'none', path: '', start: 0, lead: 0, seconds: 0, beeps: [], choice: { list: [], best: null, clear: false }, note: String((e && e.message) || e) };
    }
    if (S.file !== file) return;
    S.sound = s; S.soundBusy = false;
    heard(a);
    refresh();
  }

  function refresh() { if (RENDER[S.view]) RENDER[S.view](); }

  function fromBeep(b, auto) {
    return {
      t: b.at + soundFix(), src: 'sound', dist: DB.settings.hornDist || 0, low: Math.round(b.low), high: Math.round(b.high),
      single: !!b.single, score: Math.round(b.score * 10) / 10, auto: !!auto
    };
  }

  /** The sound has been read: the loudest sample for the strip, and the signal taken when it is clear and none is marked. */
  function heard(a) {
    var s = S.sound;
    if (!s || s.status !== 'ok') return;
    var peak = 1e-4, i, v = s.loud.values;
    for (i = 0; i < v.length; i++) if (v[i] > peak) peak = v[i];
    s.peak = peak;
    if (a.signal) return;
    if (!s.choice.best || !s.choice.clear) {
      if (S.view === 'mark' && s.choice.list.length > 0) signalSheet();
      return;
    }
    a.signal = fromBeep(s.choice.best, true);
    touch(a);
    if (S.view === 'mark') {
      if (S.sel === 'signal') S.sel = nextUnmarked('signal') || 'signal';
      if (Player.state().frame === 0) Player.go(Player.frameAt(a.signal.t));
      toast('Το σήμα εκκίνησης βρέθηκε στον ήχο');
    }
  }

  function beepText(b) {
    var tones = b.single ? 'ένας τόνος ' + Math.round(b.low) + ' Hz' : 'δύο τόνοι ' + Math.round(b.low) + ' / ' + Math.round(b.high) + ' Hz';
    return tones + ' · ' + (b.score >= 20 ? 'πολύ καθαρό' : b.score >= 10 ? 'καθαρό' : 'αχνό');
  }

  // ---------------------------------------------------------------- new analysis
  function statusLine(kind, text) {
    return h('div', { class: 'status-line ' + kind }, [
      kind === 'busy' ? h('span', { class: 'spin' }) : icon(kind === 'ok' ? 'check' : 'warn', 18), h('span', { text: text })
    ]);
  }

  function videoStatus() {
    var a = S.A, lines = [], st = Player.state(), v = a && a.video;
    if (S.loadError) return [statusLine('bad', S.loadError)];
    if (S.loading || !v || S.loadedFor !== a.id) return [statusLine('busy', 'Ανοίγει το βίντεο…')];
    lines.push(statusLine('ok', 'Βίντεο ' + seconds(v.duration) + ' · ' + v.width + '×' + v.height));
    if (st.source === 'tables') {
      lines.push(statusLine(v.fps >= 49 ? 'ok' : 'warn', fpsText(v.fps) + ' · ' + v.frames + ' καρέ, διαβασμένα από το αρχείο' +
        (v.fps >= 49 ? '. Κάθε σημάδι ±' + frameText(v.fps) + '.' : '. Κάθε σημάδι ±' + frameText(v.fps) + ': στα 60 fps είναι ±0,02 s.')));
    } else if (st.source === 'measured') {
      lines.push(statusLine('warn', 'Περίπου ' + fpsText(v.fps) + ', μετρημένα από την αναπαραγωγή (το αρχείο δεν δίνει τα καρέ του).'));
    } else {
      lines.push(statusLine('warn', 'Τα καρέ ανά δευτερόλεπτο δεν διαβάστηκαν: υπολογίζω 30. Άλλαξέ το στην οθόνη της σήμανσης αν το βίντεο είναι αλλιώς.'));
    }
    var s = S.sound;
    if (S.soundBusy) {
      lines.push(statusLine('busy', 'Ψάχνω την κόρνα στον ήχο…'));
    } else if (!s || s.status !== 'ok') {
      lines.push(statusLine('warn', 'Ο ήχος του βίντεο δεν διαβάστηκε. Το σήμα εκκίνησης το σημαδεύεις εσύ στο καρέ του.'));
    } else if (a.signal && a.signal.src === 'sound') {
      lines.push(statusLine('ok', 'Σήμα εκκίνησης από τον ήχο στα ' + M.fmt(a.signal.t) + ' s του βίντεο (' + beepText(a.signal) + ').'));
    } else if (s.choice.list.length > 0) {
      lines.push(statusLine('warn', 'Ακούστηκαν ' + s.choice.list.length + ' πιθανά σήματα. Διάλεξε το σωστό στην οθόνη της σήμανσης.'));
    } else {
      lines.push(statusLine('warn', 'Δεν ακούστηκε κόρνα. Το σήμα εκκίνησης το σημαδεύεις εσύ: φαίνεται στη λωρίδα του ήχου.'));
    }
    return lines;
  }

  RENDER.setup = function () {
    var a = S.A;
    if (!a) return show('home');
    var names = [], seen = {};
    DB.analyses.slice().sort(function (p, q) { return p.created < q.created ? 1 : -1; }).forEach(function (b) {
      var n = (b.athlete || '').trim(), key = n.toLocaleLowerCase('el');
      if (n && !seen[key]) { seen[key] = 1; names.push(n); }
    });
    fill($('athlete-chips'), names.slice(0, 6).map(function (n) {
      return h('button', { class: 'chip', type: 'button', text: n, onclick: function () { $('athlete').value = n; a.athlete = n; } });
    }));
    fill($('stroke-chips'), M.STROKES.map(function (s) {
      return h('button', {
        class: 'chip' + (a.stroke === s.id ? ' on' : ''), type: 'button', text: s.name, 'aria-pressed': a.stroke === s.id ? 'true' : 'false',
        onclick: function () { a.stroke = a.stroke === s.id ? '' : s.id; RENDER.setup(); }
      });
    }));
    if (document.activeElement !== $('athlete')) $('athlete').value = a.athlete || '';
    fill($('setup-status'), videoStatus().concat(S.loadError ? [
      h('label', { class: 'btn', for: 'pick-video', text: 'Άλλο βίντεο' })
    ] : []));
    $('setup-go').disabled = !(S.loadedFor === a.id && !S.loading);
  };

  function startMarking() {
    var a = S.A;
    a.athlete = $('athlete').value.trim();
    if (!kept(a)) DB.analyses.push(a);
    touch(a);
    Player.prime();
    S.sel = a.signal ? (nextUnmarked('signal') || 'signal') : 'signal';
    show('mark');
    if (a.signal) Player.go(Player.frameAt(a.signal.t));
    else if (S.sound && S.sound.status === 'ok' && S.sound.choice.list.length > 0) signalSheet();
    if (!DB.settings.hinted) {
      DB.settings.hinted = true; saveDb();
      toast('Σύρε τη λωρίδα του ήχου για καρέ-καρέ. Δύο αγγίγματα στην εικόνα: ζουμ.');
    }
  }

  // ---------------------------------------------------------------- zoom of the picture
  var Zoom = (function () {
    var s = 1, tx = 0, ty = 0, points = {}, pinch = null, tap = null, lastTap = null, moved = false;
    function stage() { return $('stage'); }
    function apply() {
      var r = stage().getBoundingClientRect();
      s = Math.max(1, Math.min(6, s));
      tx = Math.min(0, Math.max(r.width * (1 - s), tx));
      ty = Math.min(0, Math.max(r.height * (1 - s), ty));
      $('zoomer').style.transform = s === 1 ? '' : 'translate(' + tx + 'px,' + ty + 'px) scale(' + s + ')';
      $('zoom-out').hidden = s === 1;
      if (s !== 1) $('zoom-out').textContent = M.fmt(s, 1) + '×';
    }
    function reset() { s = 1; tx = 0; ty = 0; apply(); }
    function at(e) { var r = stage().getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; }
    function ids() { return Object.keys(points); }
    function zoomAt(p, to) {
      var cx = (p.x - tx) / s, cy = (p.y - ty) / s;
      s = to; tx = p.x - cx * s; ty = p.y - cy * s; apply();
    }
    function down(e) {
      if (e.target.closest && e.target.closest('button,label,.stage-msg')) return;
      points[e.pointerId] = at(e);
      try { stage().setPointerCapture(e.pointerId); } catch (x) { /* older phones */ }
      var list = ids();
      if (list.length === 1) { tap = { x: points[e.pointerId].x, y: points[e.pointerId].y, t: Date.now() }; moved = false; }
      if (list.length === 2) {
        var a = points[list[0]], b = points[list[1]];
        pinch = { d: Math.hypot(a.x - b.x, a.y - b.y) || 1, s: s, cx: ((a.x + b.x) / 2 - tx) / s, cy: ((a.y + b.y) / 2 - ty) / s };
        tap = null;
      }
    }
    function move(e) {
      var before = points[e.pointerId];
      if (!before) return;
      var p = at(e), list = ids();
      points[e.pointerId] = p;
      if (list.length >= 2 && pinch) {
        var a = points[list[0]], b = points[list[1]], d = Math.hypot(a.x - b.x, a.y - b.y) || 1;
        s = Math.max(1, Math.min(6, pinch.s * d / pinch.d));
        tx = (a.x + b.x) / 2 - pinch.cx * s; ty = (a.y + b.y) / 2 - pinch.cy * s;
        apply();
      } else if (list.length === 1) {
        if (tap && Math.hypot(p.x - tap.x, p.y - tap.y) > 8) moved = true;
        if (s > 1) { tx += p.x - before.x; ty += p.y - before.y; apply(); }
      }
    }
    function up(e) {
      if (!points[e.pointerId]) return;
      var p = points[e.pointerId];
      delete points[e.pointerId];
      if (ids().length < 2) pinch = null;
      if (tap && !moved && Date.now() - tap.t < 320 && ids().length === 0) {
        if (lastTap && Date.now() - lastTap.t < 340 && Math.hypot(p.x - lastTap.x, p.y - lastTap.y) < 36) {
          lastTap = null;
          if (s > 1) reset(); else zoomAt(p, 2.5);
        } else {
          lastTap = { x: p.x, y: p.y, t: Date.now() };
        }
      }
      tap = null;
    }
    return { reset: reset, apply: apply, down: down, move: move, up: up };
  })();

  // ---------------------------------------------------------------- marking
  var PX = 6;   // pixels of the strip for one frame
  var strip = { ctx: null, w: 0, h: 0, dpr: 1 };

  function firstUnmarked() { return nextUnmarked(M.EVENTS[M.EVENTS.length - 1].id); }

  function nextUnmarked(after) {
    var ids = M.EVENTS.map(function (e) { return e.id; }), i = ids.indexOf(after), n;
    for (n = 1; n <= ids.length; n++) {
      var id = ids[(i + n) % ids.length];
      if (M.markTime(S.A, id) === null) return id;
    }
    return null;
  }

  /** The frame that a mark sits on (the signal heard: the frame on screen at its onset). */
  function markFrame(id) {
    var a = S.A, t = id === 'signal' ? (a.signal ? a.signal.t : null) : M.markTime(a, id);
    return t === null ? -1 : Player.frameAt(t);
  }

  function renderEvents() {
    var a = S.A, t0 = M.signalTime(a);
    fill($('events'), M.EVENTS.map(function (e) {
      var t = M.markTime(a, e.id), label, cls = 't';
      if (t === null) { label = '—'; cls += ' none'; }
      else if (e.id === 'signal') { label = a.signal.src === 'sound' ? 'ήχος' : 'καρέ'; if (a.signal.src === 'sound') cls += ' sound'; }
      else label = t0 !== null ? M.fmt(t - t0) : '';
      return h('button', {
        class: 'ev' + (S.sel === e.id ? ' sel' : ''), type: 'button', title: e.hint, 'aria-pressed': S.sel === e.id ? 'true' : 'false',
        onclick: function () { selectEvent(e.id); }
      }, [h('span', { class: 'n', text: e.short }), h('span', { class: cls }, [t !== null ? icon('check', 14) : null, label])]);
    }));
    var marks = clear($('scrub-marks')), d = Player.state().duration;
    if (d > 0) {
      M.EVENTS.forEach(function (e) {
        var t = e.id === 'signal' ? (a.signal ? a.signal.t : null) : M.markTime(a, e.id);
        if (t === null) return;
        var m = h('div', { class: 'scrub-mark' + (e.id === 'signal' ? ' signal' : '') });
        m.style.left = (100 * Math.max(0, Math.min(1, t / d))).toFixed(2) + '%';
        marks.appendChild(m);
      });
    }
  }

  function renderFrame() {
    var a = S.A, st = Player.state();
    if (!a) return;
    var k = st.frame, t = !st.loaded ? 0 : k === st.shown ? Player.current() : Player.time(k), t0 = M.signalTime(a);
    if (!st.loaded) {
      $('clock').textContent = '—';
      $('clock-unit').textContent = '';
    } else if (t0 !== null) {
      var rel = t - t0;
      $('clock').textContent = (rel >= 0 ? '+' : '') + fmt3(rel);
      $('clock-unit').textContent = ' από το σήμα';
    } else {
      $('clock').textContent = fmt3(t);
      $('clock-unit').textContent = ' βίντεο';
    }
    $('frame-no').textContent = st.loaded ? 'καρέ ' + (k + 1) + ' / ' + st.count : '';
    var frac = st.count > 1 ? k / (st.count - 1) : 0;
    $('scrub-fill').style.width = (100 * frac).toFixed(2) + '%';
    $('scrub-knob').style.left = (100 * frac).toFixed(2) + '%';
    $('scrub').setAttribute('aria-valuenow', String(Math.round(100 * frac)));

    var e = M.eventOf(S.sel), mf = markFrame(S.sel), btn = $('mark-btn');
    btn.disabled = !st.loaded;
    btn.classList.toggle('here', mf === k);
    if (mf < 0) btn.textContent = 'Σημάδεψε: ' + e.name;
    else if (mf === k) btn.textContent = '✓ ' + e.short + ' σε αυτό το καρέ';
    else btn.textContent = 'Μετακίνηση εδώ: ' + e.short;
    $('unmark-btn').hidden = mf < 0;
    $('signal-more').hidden = S.sel !== 'signal';
    drawStrip();
  }

  function renderPlayState() {
    var st = Player.state();
    fill($('t-play'), [icon(st.playing ? 'pause' : 'play', 24)]);
    $('t-play').setAttribute('aria-label', st.playing ? 'Παύση' : 'Αναπαραγωγή');
    $('t-rate').textContent = S.rate === 1 ? '1×' : S.rate === 0.5 ? '½×' : '¼×';
    fill($('sound-btn'), [icon($('video').muted ? 'mute' : 'sound')]);
    var slot = clear($('fps-slot'));
    if (!st.loaded) return;
    if (st.source === 'tables') slot.appendChild(h('span', { class: 'fps', text: fpsText(st.fps) }));
    else slot.appendChild(h('button', { class: 'link', type: 'button', text: '≈ ' + fpsText(st.fps) + ' · άλλαξε', onclick: fpsSheet }));
  }

  RENDER.mark = function () {
    var a = S.A;
    if (!a) return show('home');
    if (S.loadedFor && S.loadedFor !== a.id && !S.loading) {
      // The video on screen belongs to another analysis: its frames must not be marked into this one.
      Player.unload();
      S.file = null; S.loadedFor = null; S.info = null; S.sound = null; S.soundBusy = false; S.loadError = '';
    }
    $('mark-title').textContent = title(a);
    $('mark-sub').textContent = 'Εκκίνηση' + (a.stroke ? ' · ' + M.strokeName(a.stroke).toLocaleLowerCase('el') : '');
    var msg = $('stage-msg');
    if (S.loadError) {
      fill(msg, [h('p', { text: S.loadError }), h('label', { class: 'btn small', for: 'reopen-video', text: 'Άλλο βίντεο' })]);
      msg.hidden = false;
    } else if (S.loading) {
      fill(msg, [h('span', { class: 'spin' }), h('p', { text: 'Ανοίγει το βίντεο…' })]);
      msg.hidden = false;
    } else if (S.loadedFor !== a.id) {
      fill(msg, [h('p', { text: 'Για να δεις τα καρέ, διάλεξε ξανά το βίντεο αυτής της ανάλυσης.' }), h('label', { class: 'btn small primary', for: 'reopen-video', text: 'Διάλεξε το βίντεο' })]);
      msg.hidden = false;
    } else {
      msg.hidden = true;
    }
    if (M.markTime(a, S.sel) === null && !M.eventOf(S.sel)) S.sel = 'signal';
    renderEvents();
    renderPlayState();
    renderFrame();
  };

  function selectEvent(id) {
    S.sel = id;
    var f = markFrame(id);
    if (f >= 0 && Player.state().loaded) Player.go(f);
    renderEvents();
    renderFrame();
  }

  async function markHere() {
    var a = S.A, st = Player.state(), id = S.sel, e = M.eventOf(id);
    if (!st.loaded || !a) return;
    await Player.shownFrame(st.frame);
    var k = Player.state().shown, t = Player.current();
    if (markFrame(id) === k) return;
    var before = id === 'signal' ? a.signal : a.marks[id];
    if (id === 'signal') a.signal = { t: t, src: 'frame' }; else a.marks[id] = { t: t };
    touch(a);
    var t0 = M.signalTime(a);
    flash('✓ ' + e.short + (id !== 'signal' && t0 !== null ? ' · ' + M.fmt(t - t0) + ' s' : ''));
    toast('Σημαδεύτηκε: ' + e.short, 'Αναίρεση', function () {
      if (id === 'signal') a.signal = before || null; else if (before) a.marks[id] = before; else delete a.marks[id];
      touch(a); S.sel = id; RENDER.mark();
    });
    var next = nextUnmarked(id);
    if (next) S.sel = next;
    renderEvents();
    renderFrame();
  }

  function unmark() {
    var a = S.A, id = S.sel, e = M.eventOf(id), before = id === 'signal' ? a.signal : a.marks[id];
    if (!before) return;
    if (id === 'signal') a.signal = null; else delete a.marks[id];
    touch(a);
    toast('Σβήστηκε: ' + e.short, 'Αναίρεση', function () {
      if (id === 'signal') a.signal = before; else a.marks[id] = before;
      touch(a); RENDER.mark();
    });
    renderEvents();
    renderFrame();
  }

  function sizeStrip() {
    var c = $('strip'), r = c.getBoundingClientRect(), dpr = Math.min(3, window.devicePixelRatio || 1);
    if (!r.width || !r.height) return false;
    var w = Math.round(r.width * dpr), hh = Math.round(r.height * dpr);
    if (c.width !== w || c.height !== hh) { c.width = w; c.height = hh; }
    strip.w = r.width; strip.h = r.height; strip.dpr = dpr; strip.ctx = c.getContext('2d');
    return true;
  }

  /** The strip under the picture: the sound and the frames around the frame on screen, which stays in the middle. */
  function drawStrip() {
    if (S.view !== 'mark' || !sizeStrip()) return;
    var g = strip.ctx, w = strip.w, hh = strip.h, st = Player.state(), a = S.A;
    g.setTransform(strip.dpr, 0, 0, strip.dpr, 0, 0);
    g.clearRect(0, 0, w, hh);
    if (!st.loaded || !a) return;
    var k = st.frame, now = Player.time(k), cx = Math.round(w / 2), perSecond = PX / st.step;
    function X(t) { return cx + (t - now) * perSecond; }

    g.fillStyle = css('--accent-deep');
    g.fillRect(cx, 0, PX, hh);

    var s = S.sound, loud = s && s.status === 'ok' ? s.loud : null, x, i;
    if (loud) {
      var mid = (hh - 8) / 2, amp = (hh - 14) / 2, fix = soundFix(), from, to, v, y;
      g.fillStyle = css('--dim');
      for (x = 0; x < w; x++) {
        from = Math.floor((now + (x - cx) / perSecond - loud.start - fix) / loud.step);
        to = Math.max(from + 1, Math.ceil((now + (x + 1 - cx) / perSecond - loud.start - fix) / loud.step));
        if (to <= 0 || from >= loud.values.length) continue;
        v = 0;
        for (i = Math.max(0, from); i < Math.min(loud.values.length, to); i++) if (loud.values[i] > v) v = loud.values[i];
        y = Math.max(0.5, Math.pow(Math.min(1, v / s.peak), 0.6) * amp);
        g.fillRect(x, mid - y, 1, 2 * y);
      }
    }

    g.fillStyle = css('--border');
    var first = Math.max(0, k - Math.ceil(cx / PX) - 1), last = Math.min(st.count - 1, k + Math.ceil((w - cx) / PX) + 1), f, tall;
    for (f = first; f <= last; f++) {
      tall = f % 10 === 0;
      g.fillRect(Math.round(X(Player.time(f))), hh - (tall ? 9 : 5), 1, tall ? 9 : 5);
    }

    if (s && s.status === 'ok') {
      g.fillStyle = css('--amber');
      s.choice.list.forEach(function (b) {
        var bx = Math.round(X(b.at + soundFix())), yy;
        if (bx < -2 || bx > w + 2) return;
        for (yy = 0; yy < hh; yy += 6) g.fillRect(bx, yy, 1, 3);
      });
    }

    g.font = '600 10px ' + css('--body');
    g.textBaseline = 'top';
    M.EVENTS.forEach(function (e) {
      var t = e.id === 'signal' ? (a.signal ? a.signal.t : null) : M.markTime(a, e.id);
      if (t === null) return;
      var mx = Math.round(X(t));
      if (mx < -40 || mx > w + 40) return;
      g.fillStyle = css(e.id === 'signal' ? '--amber' : '--accent');
      g.fillRect(mx, 0, 2, hh);
      g.fillText(e.short, mx + 5, 3);
    });

    g.fillStyle = css('--text');
    g.fillRect(cx - 1, 0, 2, hh);
  }

  function fpsSheet() {
    openSheet([
      h('h2', { text: 'Καρέ ανά δευτερόλεπτο' }),
      h('p', { class: 'muted', text: 'Το κινητό δεν διάβασε τα καρέ του αρχείου. Διάλεξε με πόσα τραβήχτηκε το βίντεο: από αυτό βγαίνουν οι χρόνοι.' }),
      h('div', { class: 'chips' }, [24, 25, 30, 50, 60, 100, 120, 240].map(function (f) {
        return h('button', { class: 'chip', type: 'button', text: String(f), onclick: function () { Player.setFps(f); if (S.A && S.A.video) { S.A.video.fps = f; touch(S.A); } closeSheet(); RENDER.mark(); } });
      })),
      h('button', { class: 'btn', type: 'button', text: 'Κλείσιμο', onclick: closeSheet })
    ]);
  }

  function distanceStepper(onChange) {
    var out = h('output', { text: (DB.settings.hornDist || 0) + ' μ.' });
    function set(d) {
      DB.settings.hornDist = Math.max(0, Math.min(60, d));
      out.textContent = DB.settings.hornDist + ' μ.';
      saveDb();
      if (onChange) onChange();
    }
    return h('div', { class: 'stepper' }, [
      h('button', { type: 'button', text: '−', 'aria-label': '1 μέτρο λιγότερο', onclick: function () { set(DB.settings.hornDist - 1); } }), out,
      h('button', { type: 'button', text: '+', 'aria-label': '1 μέτρο περισσότερο', onclick: function () { set(DB.settings.hornDist + 1); } })
    ]);
  }

  function signalSheet() {
    var a = S.A, s = S.sound, kids = [h('h2', { text: 'Σήμα εκκίνησης' })];
    if (S.soundBusy) {
      kids.push(statusLine('busy', 'Ψάχνω την κόρνα στον ήχο…'));
    } else if (s && s.status === 'ok' && s.choice.list.length > 0) {
      kids.push(h('p', { class: 'muted', text: 'Ό,τι ακούστηκε σαν σήμα εκκίνησης στον ήχο του βίντεο:' }));
      s.choice.list.slice().sort(function (p, q) { return p.at - q.at; }).forEach(function (b) {
        var used = a.signal && a.signal.src === 'sound' && Math.abs(a.signal.t - (b.at + soundFix())) < 1e-3;
        kids.push(h('div', { class: 'rowline' }, [
          h('div', { class: 'txt' }, [h('b', { text: M.fmt(b.at + soundFix()) + ' s' }), h('span', { text: beepText(b) })]),
          h('button', { class: 'btn small', type: 'button', text: 'Δες', onclick: function () { closeSheet(); Player.go(Player.frameAt(b.at + soundFix())); } }),
          h('button', {
            class: 'btn small primary', type: 'button', text: used ? '✓' : 'Χρήση', disabled: used,
            onclick: function () {
              a.signal = fromBeep(b, false); touch(a); closeSheet();
              Player.go(Player.frameAt(a.signal.t)); RENDER.mark();
              toast('Σήμα εκκίνησης από τον ήχο');
            }
          })
        ]));
      });
    } else {
      kids.push(h('p', { class: 'muted', text: s && s.status === 'ok'
        ? 'Δεν ακούστηκε κόρνα στο βίντεο. Πήγαινε στο καρέ όπου ξεκινά ο ήχος του σήματος (φαίνεται στη λωρίδα κάτω από την εικόνα) και πάτα «Σημάδεψε».'
        : 'Ο ήχος του βίντεο δεν διαβάστηκε. Πήγαινε στο καρέ του σήματος και πάτα «Σημάδεψε».' }));
    }
    kids.push(h('div', { class: 'section' }, [
      h('h3', { class: 'eyebrow', text: 'Απόσταση από το ηχείο της κόρνας' }),
      distanceStepper(function () {
        if (a.signal && a.signal.src === 'sound') { a.signal.dist = DB.settings.hornDist; touch(a); RENDER.mark(); }
      }),
      h('p', { class: 'muted', text: 'Ο ήχος θέλει 0,03 s για κάθε 10 μέτρα ως το κινητό. Όσο αντιστοιχεί στην απόσταση αφαιρείται από το σήμα που ακούστηκε.' })
    ]));
    kids.push(h('button', { class: 'btn', type: 'button', text: 'Κλείσιμο', onclick: closeSheet }));
    openSheet(kids);
  }

  // ---------------------------------------------------------------- measurements
  function deltaNode(row, now, before) {
    if (now === null || !before) return null;
    var prev = M.measure(before)[row.id];
    if (prev === null) return null;
    var d = M.round2(now - prev), cls = 'delta';
    if (row.less !== null && d !== 0) cls += (d < 0) === row.less ? ' better' : ' worse';
    return h('span', { class: cls }, [d === 0 ? null : icon(d < 0 ? 'down' : 'up', 13), M.fmtDelta(now, prev)]);
  }

  /** The lane from the wall to 15 m: the time at each distance marked and the speed of each 5 m between two of them. */
  function laneSvg(m) {
    var W = 358, H = 96, x0 = 22, x1 = W - 22, y = 52, svg = document.createElementNS(NS, 'svg');
    svg.setAttribute('viewBox', '0 0 ' + W + ' ' + H); svg.setAttribute('class', 'lane'); svg.setAttribute('role', 'img');
    svg.setAttribute('aria-label', 'Οι χρόνοι στα 5, 10 και 15 μέτρα και η ταχύτητα ανάμεσά τους');
    function add(tag, attrs, text) {
      var e = document.createElementNS(NS, tag), k;
      for (k in attrs) e.setAttribute(k, attrs[k]);
      if (text !== undefined) e.textContent = text;
      svg.appendChild(e);
      return e;
    }
    function X(metres) { return x0 + (x1 - x0) * metres / 15; }
    add('line', { x1: x0, y1: y, x2: x1, y2: y, stroke: css('--border'), 'stroke-width': 3, 'stroke-linecap': 'round' });
    [[0, 'σήμα', m.m5 !== null || m.m10 !== null || m.m15 !== null ? 0 : null], [5, '5 μ.', m.m5], [10, '10 μ.', m.m10], [15, '15 μ.', m.m15]].forEach(function (p) {
      var x = X(p[0]), has = p[2] !== null;
      add('circle', { cx: x, cy: y, r: has ? 6 : 4, fill: has ? css('--accent') : css('--bg'), stroke: has ? css('--accent') : css('--border'), 'stroke-width': 2 });
      add('text', { x: x, y: y + 26, 'text-anchor': 'middle', fill: css('--dim'), 'font-size': 12, 'font-family': css('--body') }, p[1]);
      if (has) add('text', { x: x, y: y - 16, 'text-anchor': p[0] === 0 ? 'start' : p[0] === 15 ? 'end' : 'middle', fill: css('--text'), 'font-size': 17, 'font-weight': 700, 'font-family': css('--display') }, M.fmt(p[2]));
    });
    [[5, 10, m.v510], [10, 15, m.v1015]].forEach(function (p) {
      if (p[2] === null) return;
      add('line', { x1: X(p[0]) + 8, y1: y, x2: X(p[1]) - 8, y2: y, stroke: css('--accent'), 'stroke-width': 3, 'stroke-linecap': 'round' });
      add('text', { x: (X(p[0]) + X(p[1])) / 2, y: y + 44, 'text-anchor': 'middle', fill: css('--accent'), 'font-size': 12, 'font-weight': 600, 'font-family': css('--body') }, M.fmt(p[2]) + ' m/s');
    });
    return svg;
  }

  function accuracyText(a) {
    var parts = [], v = a.video, s = a.signal;
    if (v && v.fps) parts.push('Βίντεο ' + fpsText(v.fps) + ': κάθε σημάδι ±' + frameText(v.fps) + '.');
    if (s && s.src === 'sound') {
      parts.push('Σήμα από τον ήχο (' + (s.single ? 'τόνος ' + s.low + ' Hz' : 'τόνοι ' + s.low + ' / ' + s.high + ' Hz') + ')' +
        (s.dist ? ', ' + s.dist + ' μ. από το ηχείο: −' + M.fmt(s.dist / M.SOUND) + ' s.' : '.'));
    } else if (s) {
      parts.push('Σήμα σημαδεμένο στο καρέ.');
    }
    return parts.join(' ');
  }

  RENDER.result = function () {
    var a = S.A;
    if (!a) return show('home');
    $('result-sub').textContent = title(a) + ' · ' + subtitle(a).replace('Εκκίνηση · ', '');
    var m = M.measure(a), head = M.headline(a), body = clear($('result-body')), any = false;

    if (head) {
      var prevHead = M.previous(DB.analyses, a, head.id);
      body.appendChild(h('div', { class: 'hero' }, [
        h('div', { class: 'big' }, [M.fmt(head.value), h('small', { text: head.unit })]),
        h('div', { class: 'what' }, [
          h('span', { text: head.label === 'Πτήση' || head.label === 'Αποδέσμευση' ? head.label : 'στα ' + head.label }),
          prevHead ? h('span', {}, [deltaNode({ id: head.id, less: head.id === 'flight' ? null : true }, head.value, prevHead), ' από την προηγούμενη']) : null
        ])
      ]));
    }
    if (m.m5 !== null || m.m10 !== null || m.m15 !== null || m.v510 !== null || m.v1015 !== null) body.appendChild(laneSvg(m));

    M.warnings(a).forEach(function (w) { body.appendChild(h('div', { class: 'note' }, [icon('warn', 20), h('span', { text: w })])); });
    if (!a.signal) {
      body.appendChild(h('div', { class: 'note' }, [icon('warn', 20), h('span', { text: 'Χωρίς το σήμα εκκίνησης μετριούνται μόνο όσα είναι ανάμεσα σε δύο σημάδια (πτήση, ταχύτητες).' })]));
    }

    var rows = [], anyDelta = false;
    M.ROWS.forEach(function (row) {
      var v = m[row.id];
      if (v === null) return;
      any = true;
      var d = deltaNode(row, v, M.previous(DB.analyses, a, row.id));
      if (d) anyDelta = true;
      rows.push(h('div', { class: 'trow' }, [
        h('span', { class: 'k', text: row.name }),
        h('span', { class: 'v' }, [M.fmt(v), h('small', { text: row.unit })]),
        h('span', { class: 'd' }, [d])
      ]));
    });
    if (any) {
      body.appendChild(h('div', { class: 'section' }, [
        h('div', { class: 'head-row' }, [h('h2', { class: 'eyebrow', text: 'Μετρήσεις' }), anyDelta ? h('span', { class: 'muted', text: 'διαφορά από την προηγούμενη' }) : null]),
        h('div', { class: 'table' }, rows)
      ]));
    } else {
      body.appendChild(h('div', { class: 'empty', text: 'Δεν υπάρχουν ακόμα μετρήσεις. Σημάδεψε το σήμα και τουλάχιστον ένα ακόμα σημείο στο βίντεο.' }));
    }
    var acc = accuracyText(a);
    if (acc) body.appendChild(h('p', { class: 'muted', text: acc }));

    var notes = h('textarea', { id: 'notes', rows: '3', placeholder: 'Τι είδες, τι να δουλέψει ο αθλητής…', value: a.notes || '' });
    notes.addEventListener('input', function () { a.notes = notes.value; touch(a); S.report = null; });
    body.appendChild(h('div', { class: 'field' }, [h('label', { for: 'notes', text: 'Σημειώσεις' }), notes]));
  };

  // ---------------------------------------------------------------- technique
  RENDER.tech = function () {
    var a = S.A;
    if (!a) return show('home');
    var list = techList(), sum = techSummary(a), body = clear($('tech-body'));
    $('tech-sub').textContent = 'Εκκίνηση · ' + title(a);
    if (S.phase >= list.phases.length) S.phase = 0;

    body.appendChild(h('div', { class: 'sum' }, sum.checked === 0
      ? [h('span', { class: 'muted', text: 'Σημείωσε σε κάθε σημείο αν είναι σωστό ή αν θέλει δουλειά.' })]
      : [h('span', { class: 'good' }, [icon('check', 18), sum.ok + ' από ' + sum.checked + ' σημεία σωστά']),
        sum.fix.length ? h('span', { class: 'todo' }, [icon('warn', 18), sum.fix.length === 1 ? '1 σημείο θέλει δουλειά' : sum.fix.length + ' σημεία θέλουν δουλειά']) : null]));

    body.appendChild(h('div', { class: 'seg', role: 'tablist' }, list.phases.map(function (p, i) {
      return h('button', { class: 'chip' + (i === S.phase ? ' on' : ''), type: 'button', role: 'tab', 'aria-selected': i === S.phase ? 'true' : 'false', text: p.name, onclick: function () { S.phase = i; RENDER.tech(); } });
    })));

    var phase = list.phases[S.phase];
    if (phase.items.length === 0) body.appendChild(h('div', { class: 'empty', text: 'Κανένα σημείο σε αυτή τη φάση. Πρόσθεσε στις Ρυθμίσεις.' }));
    phase.items.forEach(function (it) {
      var v = a.tech[it.id];
      function set(to) {
        if (a.tech[it.id] === to) delete a.tech[it.id]; else a.tech[it.id] = to;
        touch(a); S.report = null; RENDER.tech();
      }
      body.appendChild(h('div', { class: 'tp' }, [
        h('div', { class: 'tp-top' }, [
          h('span', { class: 'tp-name', text: it.name }),
          h('button', { class: 'tp-btn ok' + (v === 'ok' ? ' on' : ''), type: 'button', 'aria-label': 'Σωστό: ' + it.name, 'aria-pressed': v === 'ok' ? 'true' : 'false', onclick: function () { set('ok'); } }, [icon('check', 20)]),
          h('button', { class: 'tp-btn fix' + (v === 'fix' ? ' on' : ''), type: 'button', 'aria-label': 'Θέλει δουλειά: ' + it.name, 'aria-pressed': v === 'fix' ? 'true' : 'false', onclick: function () { set('fix'); } }, [icon('cross', 20)])
        ]),
        v === 'fix' && it.cue ? h('div', { class: 'tp-cue' }, [h('b', { text: 'Διόρθωση: ' }), it.cue]) : null
      ]));
    });
    body.appendChild(h('p', { class: 'muted', text: list.example
      ? 'Αυτή είναι μια λίστα-παράδειγμα. Γράψε τα δικά σου σημεία και διορθώσεις στις Ρυθμίσεις.'
      : 'Τα σημεία και οι διορθώσεις είναι από τη λίστα σου στις Ρυθμίσεις.' }));
  };

  // ---------------------------------------------------------------- report
  function fileName(a, ext) {
    var name = ('Εκκίνηση ' + title(a) + ' ' + a.created.slice(0, 10)).replace(/[\\\/:*?"<>|]+/g, ' ').replace(/\s+/g, ' ').trim();
    return name + '.' + ext;
  }

  async function buildReport() {
    var a = S.A, token = ++S.reportToken, thumbs = [];
    S.reportBusy = true; S.report = null; S.reportError = '';
    RENDER.report();
    try {
      if (S.loadedFor === a.id && Player.state().loaded) {
        var want = ['takeoff', 'entry', 'breakout', 'm15', 'm5', 'm10', 'react'].filter(function (id) { return M.markTime(a, id) !== null; }).slice(0, 3)
          .sort(function (p, q) { return M.markTime(a, p) - M.markTime(a, q); });
        var keep = Player.state().frame, i;
        for (i = 0; i < want.length; i++) {
          var c = await Player.capture(Player.frameAt(M.markTime(a, want[i])), 520);
          if (token !== S.reportToken) return;
          thumbs.push({ id: want[i], canvas: c });
        }
        Player.go(keep);
      }
      if (document.fonts && document.fonts.load) {
        await Promise.race([
          Promise.all([document.fonts.load('800 60px "Sofia Sans Semi Condensed"', 'Αθλητής 0,42'), document.fonts.load('600 20px Inter', 'Αθλητής 0,42'), document.fonts.load('400 20px Inter', 'Αθλητής 0,42')]),
          new Promise(function (r) { setTimeout(r, 1800); })
        ]).catch(function () { /* the fonts of the phone will do */ });
      }
      if (token !== S.reportToken) return;
      S.report = { id: a.id, canvas: Report.draw(a, { thumbs: thumbs, list: DB.analyses, tech: techSummary(a), version: VERSION }), thumbs: thumbs.length };
    } catch (e) {
      S.report = null;
      S.reportError = String((e && e.message) || e);
    }
    if (token !== S.reportToken) return;
    S.reportBusy = false;
    if (S.view === 'report') RENDER.report();
  }

  function trendSvg(rows) {
    var W = 358, H = 150, x0 = 30, x1 = W - 30, y0 = 44, y1 = 104, svg = document.createElementNS(NS, 'svg'), i;
    svg.setAttribute('viewBox', '0 0 ' + W + ' ' + H); svg.setAttribute('class', 'trend'); svg.setAttribute('role', 'img');
    svg.setAttribute('aria-label', 'Οι τελευταίες ' + rows.length + ' μετρήσεις: ' + rows.map(function (r) { return M.fmt(r.value); }).join(', '));
    function add(tag, attrs, text) {
      var e = document.createElementNS(NS, tag), k;
      for (k in attrs) e.setAttribute(k, attrs[k]);
      if (text !== undefined) e.textContent = text;
      svg.appendChild(e);
    }
    var lo = Infinity, hi = -Infinity;
    rows.forEach(function (r) { lo = Math.min(lo, r.value); hi = Math.max(hi, r.value); });
    if (hi - lo < 0.05) { hi += 0.03; lo -= 0.03; }
    function X(n) { return rows.length === 1 ? (x0 + x1) / 2 : x0 + (x1 - x0) * n / (rows.length - 1); }
    function Y(v) { return y0 + (y1 - y0) * (v - lo) / (hi - lo); }   // faster (less time) is higher
    add('line', { x1: x0 - 14, y1: y1 + 14, x2: x1 + 14, y2: y1 + 14, stroke: css('--line'), 'stroke-width': 1 });
    var d = '';
    for (i = 0; i < rows.length; i++) d += (i ? 'L' : 'M') + X(i).toFixed(1) + ' ' + Y(rows[i].value).toFixed(1);
    if (rows.length > 1) add('path', { d: d, fill: 'none', stroke: css('--border'), 'stroke-width': 2, 'stroke-linejoin': 'round' });
    rows.forEach(function (r, n) {
      var lastOne = n === rows.length - 1;
      add('circle', { cx: X(n), cy: Y(r.value), r: lastOne ? 6 : 4.5, fill: lastOne ? css('--accent') : css('--dim'), stroke: css('--bg'), 'stroke-width': 2 });
      add('text', { x: X(n), y: Y(r.value) - 12, 'text-anchor': 'middle', fill: css('--text'), 'font-size': lastOne ? 15 : 13, 'font-weight': lastOne ? 700 : 500, 'font-family': css('--body') }, M.fmt(r.value));
      add('text', { x: X(n), y: y1 + 32, 'text-anchor': 'middle', fill: lastOne ? css('--text') : css('--dim'), 'font-size': 11, 'font-family': css('--body') }, lastOne ? 'αυτή' : M.dateText(r.created).slice(0, 5));
    });
    return svg;
  }

  RENDER.report = function () {
    var a = S.A;
    if (!a) return show('home');
    $('report-sub').textContent = 'Εκκίνηση · ' + title(a);
    if ((!S.report || S.report.id !== a.id) && !S.reportBusy && !S.reportError) { buildReport(); return; }
    var body = clear($('report-body')), ready = !!S.report && S.report.id === a.id;
    $('report-pdf').disabled = !ready || !downloads;

    if (S.reportError && !ready) {
      body.appendChild(h('div', { class: 'note' }, [icon('warn', 20), h('span', { text: 'Η αναφορά δεν φτιάχτηκε (' + S.reportError + ').' })]));
      body.appendChild(h('button', { class: 'btn', type: 'button', text: 'Δοκίμασε ξανά', onclick: function () { S.reportError = ''; RENDER.report(); } }));
    } else {
      var preview = h('div', { class: 'preview' });
      if (ready) preview.appendChild(S.report.canvas); else preview.appendChild(h('span', { class: 'spin' }));
      body.appendChild(preview);
    }
    if (ready && S.report.thumbs === 0) {
      body.appendChild(h('p', { class: 'muted', text: S.loadedFor === a.id ? 'Σημάδεψε την αποδέσμευση ή την είσοδο για να μπουν καρέ στην αναφορά.' : 'Για να μπουν καρέ στην αναφορά, άνοιξε ξανά το βίντεο από τον «Έλεγχο σημείων».' }));
    }

    if (!downloads) {
      body.appendChild(h('div', { class: 'note' }, [icon('warn', 20), h('span', { text: 'Σε αυτή την προβολή το κινητό δεν αφήνει αποθήκευση αρχείων. Άνοιξε τη σελίδα μέσα από το Claude ή αντίγραψε το κείμενο της ανάλυσης.' })]));
    }
    body.appendChild(h('div', { class: 'stack' }, [
      h('button', { class: 'btn', type: 'button', disabled: !ready || !downloads, onclick: function () { saveReport('png'); } }, [icon('save'), 'Αποθήκευση ως εικόνα']),
      h('button', { class: 'btn', type: 'button', onclick: copySummary }, [icon('copy'), 'Αντιγραφή κειμένου για μήνυμα'])
    ]));

    var head = M.headline(a), rows = head ? M.history(DB.analyses, a, head.id, 5) : [];
    if (rows.length >= 2) {
      body.appendChild(h('div', { class: 'card' }, [
        h('div', { class: 'head-row' }, [
          h('h2', { class: 'eyebrow', text: head.label + ' · οι ' + rows.length + ' τελευταίες' }),
          h('span', {}, [deltaNode({ id: head.id, less: head.id === 'flight' ? null : true }, head.value, byId(rows[rows.length - 2].id))])
        ]),
        trendSvg(rows),
        h('p', { class: 'muted', text: 'Χρόνος σε δευτερόλεπτα: πιο ψηλά στο γράφημα σημαίνει πιο γρήγορα.' })
      ]));
    }
  };

  function saveError(e) {
    var code = e && e.code;
    if (code === 'declined') toast('Δεν αποθηκεύτηκε');
    else if (code === 'rate_limited') toast('Περίμενε λίγο και δοκίμασε ξανά');
    else toast('Η αποθήκευση δεν είναι διαθέσιμη εδώ');
  }

  async function saveReport(kind) {
    var a = S.A;
    if (!S.report || S.report.id !== a.id || !downloads) return;
    var canvas = S.report.canvas;
    try {
      var blob = await new Promise(function (resolve) { canvas.toBlob(resolve, kind === 'png' ? 'image/png' : 'image/jpeg', 0.92); });
      if (!blob) throw new Error('no picture');
      var data = blob;
      if (kind === 'pdf') {
        var jpeg = new Uint8Array(await new Response(blob).arrayBuffer());
        data = new Blob([Pdf.fromJpeg(jpeg, canvas.width, canvas.height, 'Εκκίνηση ' + title(a))], { type: 'application/pdf' });
      }
      var r = await downloads.save({ filename: fileName(a, kind), data: data });
      toast(r && r.status === 'saved' ? 'Η αναφορά αποθηκεύτηκε' : 'Η αναφορά στάλθηκε');
    } catch (e) {
      saveError(e);
    }
  }

  function copySummary() {
    var text = M.summary(S.A, techSummary(S.A));
    function fallback() {
      var area = h('textarea', { rows: '8', readonly: true, value: text });
      openSheet([h('h2', { text: 'Κείμενο της ανάλυσης' }), h('p', { class: 'muted', text: 'Κράτα πατημένο το κείμενο και διάλεξε «Αντιγραφή».' }),
        h('div', { class: 'field' }, [area]), h('button', { class: 'btn', type: 'button', text: 'Κλείσιμο', onclick: closeSheet })]);
      try { area.focus(); area.select(); } catch (e) { /* the person selects it */ }
    }
    try {
      navigator.clipboard.writeText(text).then(function () { toast('Το κείμενο αντιγράφηκε'); }, fallback);
    } catch (e) {
      fallback();
    }
  }

  // ---------------------------------------------------------------- settings
  function editableTech() {
    if (!DB.tech) { DB.tech = JSON.parse(JSON.stringify(EXAMPLE_TECH)); delete DB.tech.example; }
    return DB.tech;
  }

  RENDER.settings = function () {
    var body = clear($('settings-body')), list = techList();
    $('settings-sub').textContent = 'Έκδοση ' + VERSION;

    var tech = h('div', { class: 'section' }, [
      h('h2', { class: 'eyebrow', text: 'Λίστα τεχνικής για την εκκίνηση' }),
      h('p', { class: 'muted', text: list.example ? 'Λίστα-παράδειγμα. Άλλαξε τα σημεία και τις διορθώσεις όπως τα λες εσύ στους αθλητές σου.' : 'Τα σημεία που ελέγχεις σε κάθε εκκίνηση και η διόρθωση που προτείνεις.' })
    ]);
    list.phases.forEach(function (p, pi) {
      tech.appendChild(h('h3', { text: p.name }));
      p.items.forEach(function (it, ii) {
        var name = h('input', { type: 'text', id: 'tp-' + it.id + '-name', value: it.name, maxlength: '90', 'aria-label': 'Σημείο' });
        var cue = h('input', { type: 'text', class: 'cue', id: 'tp-' + it.id + '-cue', value: it.cue || '', maxlength: '140', placeholder: 'Διόρθωση', 'aria-label': 'Διόρθωση' });
        function keep() {
          var t = editableTech().phases[pi].items[ii];
          t.name = name.value.trim() || t.name; t.cue = cue.value.trim(); saveDb(); S.report = null;
        }
        name.addEventListener('change', keep); cue.addEventListener('change', keep);
        tech.appendChild(h('div', { class: 'edit-item' }, [
          h('div', { class: 'inputs' }, [name, cue]),
          h('button', { class: 'icon-btn danger', type: 'button', 'aria-label': 'Σβήσιμο σημείου', onclick: function () { editableTech().phases[pi].items.splice(ii, 1); saveDb(); RENDER.settings(); } }, [icon('trash')])
        ]));
      });
      tech.appendChild(h('button', { class: 'btn small', type: 'button', onclick: function () {
        var t = editableTech();
        t.phases[pi].items.push({ id: 'p' + Date.now().toString(36), name: 'Νέο σημείο', cue: '' });
        saveDb(); RENDER.settings();
      } }, [icon('plus', 18), 'Σημείο στη φάση «' + p.name + '»']));
    });
    if (!list.example) {
      tech.appendChild(h('button', { class: 'btn small', type: 'button', text: 'Επαναφορά της λίστας-παραδείγματος', onclick: function () {
        confirmSheet('Επαναφορά λίστας;', 'Τα σημεία που έγραψες θα αντικατασταθούν από το παράδειγμα.', 'Επαναφορά', function () { DB.tech = null; saveDb(); RENDER.settings(); }, true);
      } }));
    }
    body.appendChild(tech);

    var fix = h('output', { text: (DB.settings.soundFix || 0) + ' ms' });
    function setFix(v) { DB.settings.soundFix = Math.max(-200, Math.min(200, v)); fix.textContent = DB.settings.soundFix + ' ms'; saveDb(); }
    body.appendChild(h('div', { class: 'section' }, [
      h('h2', { class: 'eyebrow', text: 'Σήμα εκκίνησης από τον ήχο' }),
      h('h3', { text: 'Απόσταση από το ηχείο της κόρνας' }),
      distanceStepper(null),
      h('p', { class: 'muted', text: 'Πόσο μακριά στέκεσαι συνήθως από το ηχείο. Ο ήχος θέλει 0,03 s για κάθε 10 μέτρα· αφαιρείται από το σήμα σε κάθε νέα ανάλυση.' }),
      h('h3', { text: 'Διόρθωση ήχου – εικόνας' }),
      h('div', { class: 'stepper' }, [
        h('button', { type: 'button', text: '−', 'aria-label': '5 ms λιγότερο', onclick: function () { setFix(DB.settings.soundFix - 5); } }), fix,
        h('button', { type: 'button', text: '+', 'aria-label': '5 ms περισσότερο', onclick: function () { setFix(DB.settings.soundFix + 5); } })
      ]),
      h('p', { class: 'muted', text: 'Έλεγχος με παλαμάκι: τράβα ένα βίντεο όπου χτυπάς παλαμάκια και δες αν η κορυφή του ήχου στη λωρίδα πέφτει στο καρέ όπου ενώνονται τα χέρια. Αν ο ήχος έρχεται πιο αργά, βάλε αρνητική διόρθωση.' })
    ]));

    body.appendChild(h('div', { class: 'section' }, [
      h('h2', { class: 'eyebrow', text: 'Αντίγραφο ασφαλείας' }),
      h('p', { class: 'muted', text: 'Οι αναλύσεις κρατιούνται μόνο σε αυτό το κινητό (' + DB.analyses.length + ' ως τώρα). Αν σβηστούν τα δεδομένα της εφαρμογής, χάνονται: κράτα πού και πού ένα αντίγραφο.' }),
      h('button', { class: 'btn', type: 'button', disabled: !downloads || DB.analyses.length === 0, onclick: backup }, [icon('save'), 'Αποθήκευση αντιγράφου']),
      h('label', { class: 'btn', for: 'restore-file', tabindex: '0' }, [icon('up'), 'Επαναφορά από αντίγραφο']),
      h('button', { class: 'btn danger', type: 'button', disabled: DB.analyses.length === 0, text: 'Διαγραφή όλων των αναλύσεων', onclick: function () {
        confirmSheet('Διαγραφή όλων;', 'Θα σβηστούν και οι ' + DB.analyses.length + ' αναλύσεις από αυτό το κινητό. Δεν αναιρείται.', 'Διαγραφή όλων', function () {
          DB.analyses = []; S.A = null; saveDb(); RENDER.settings(); toast('Οι αναλύσεις σβήστηκαν');
        }, true);
      } })
    ]));

    var d = Player.diagnostics(), s = S.sound;
    body.appendChild(h('div', { class: 'section' }, [
      h('h2', { class: 'eyebrow', text: 'Για τον έλεγχο του τελευταίου βίντεο' }),
      h('div', { class: 'diag', text: 'καρέ: ' + d.source + ' · ' + d.fps.toFixed(3) + ' fps · ' + d.frames + '\n' +
        'έλεγχος καρέ: ' + d.watch + ' · μετατόπιση ' + (d.offset * 1000).toFixed(1) + ' ms · διορθώσεις ' + d.corrected + ' · εκκίνηση ' + (d.primed ? 'ναι' : 'όχι') + '\n' +
        'ήχος: ' + (s ? s.status + ' · ' + (s.path || '—') + ' · αρχή ' + (s.start * 1000).toFixed(1) + ' ms · αποκωδικοποιητής ' + s.lead + ' · ' + s.seconds.toFixed(1) + ' s · σήματα ' + s.beeps.length + (s.note ? ' · ' + s.note : '') : 'όχι ακόμα') + '\n' +
        'αποθήκευση: ' + (storageFailed ? 'όχι' : 'ναι') + ' · αρχεία: ' + (downloads ? 'ναι' : 'όχι') })
    ]));
  };

  async function backup() {
    if (!downloads) return;
    var data = JSON.stringify({ format: 'kinovea-swim-mobile', version: 1, saved: new Date().toISOString(), analyses: DB.analyses, tech: DB.tech, settings: DB.settings });
    try {
      await downloads.save({ filename: 'kinovea-swim-kinito-' + new Date().toISOString().slice(0, 10) + '.json', data: data });
      toast('Το αντίγραφο αποθηκεύτηκε');
    } catch (e) {
      saveError(e);
    }
  }

  function restore(input) {
    var file = input.files && input.files[0];
    input.value = '';
    if (!file) return;
    var r = new FileReader();
    r.onerror = function () { toast('Το αρχείο δεν διαβάστηκε'); };
    r.onload = function () {
      var d = null, added = 0;
      try { d = JSON.parse(String(r.result)); } catch (e) { d = null; }
      if (!d || d.format !== 'kinovea-swim-mobile' || !Array.isArray(d.analyses)) { toast('Αυτό δεν είναι αντίγραφο της εφαρμογής'); return; }
      d.analyses.forEach(function (a) {
        if (!a || typeof a.id !== 'string' || typeof a.created !== 'string') return;
        var mine = byId(a.id);
        if (!mine) { DB.analyses.push(a); added++; }
        else if ((a.updated || '') > (mine.updated || '')) { DB.analyses[DB.analyses.indexOf(mine)] = a; added++; }
      });
      if (!DB.tech && d.tech && Array.isArray(d.tech.phases)) DB.tech = d.tech;
      saveDb();
      refresh();
      toast(added === 1 ? 'Επαναφέρθηκε 1 ανάλυση' : 'Επαναφέρθηκαν ' + added + ' αναλύσεις');
    };
    r.readAsText(file);
  }

  // ---------------------------------------------------------------- wiring
  function labelKey(id) {
    $(id).addEventListener('keydown', function (e) {
      if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); $($(id).getAttribute('for')).click(); }
    });
  }

  function wire() {
    fill($('home-settings'), [icon('sliders')]);
    fill($('shoot-label'), [icon('camera'), 'Λήψη βίντεο']);
    fill($('pick-label'), [icon('gallery'), 'Βίντεο από τη συλλογή']);
    ['setup-back', 'mark-back', 'result-back', 'tech-back', 'report-back', 'settings-back'].forEach(function (id) { fill($(id), [icon('back', 24)]); });
    fill($('result-delete'), [icon('trash')]);
    fill($('unmark-btn'), [icon('trash')]);
    fill($('signal-more'), [icon('wave')]);
    labelKey('shoot-label'); labelKey('pick-label');

    $('shoot-video').addEventListener('change', function () { pickedVideo(this, false); });
    $('pick-video').addEventListener('change', function () { pickedVideo(this, false); });
    $('reopen-video').addEventListener('change', function () { pickedVideo(this, true); });
    $('restore-file').addEventListener('change', function () { restore(this); });
    $('home-settings').addEventListener('click', function () { show('settings'); });
    $('settings-back').addEventListener('click', function () { show('home'); });

    $('setup-back').addEventListener('click', function () {
      if (S.A && !kept(S.A)) { Player.unload(); S.file = null; S.loadedFor = null; S.loading = false; S.A = null; }
      show('home');
    });
    $('athlete').addEventListener('input', function () { if (S.A) S.A.athlete = this.value; });
    $('setup-go').addEventListener('click', startMarking);

    $('mark-back').addEventListener('click', function () { show('home'); });
    $('mark-results').addEventListener('click', function () { show('result'); });
    // A tap is one step; a finger kept on the button keeps stepping.
    [['t-b10', -10], ['t-b1', -1], ['t-f1', 1], ['t-f10', 10]].forEach(function (pair) {
      var b = $(pair[0]), d = pair[1], wait = 0, repeat = 0, held = false;
      function stop() { clearTimeout(wait); clearInterval(repeat); wait = 0; repeat = 0; }
      b.addEventListener('pointerdown', function () {
        stop(); held = false;
        wait = setTimeout(function () { held = true; Player.step(d); repeat = setInterval(function () { Player.step(d); }, 110); }, 420);
      });
      ['pointerup', 'pointercancel', 'pointerleave'].forEach(function (n) { b.addEventListener(n, stop); });
      b.addEventListener('click', function () { if (held) { held = false; return; } Player.step(d); });
      b.addEventListener('contextmenu', function (e) { e.preventDefault(); });
    });
    $('t-play').addEventListener('click', function () { if (Player.state().playing) Player.pause(); else Player.play(S.rate); });
    $('t-rate').addEventListener('click', function () {
      S.rate = S.rate === 0.25 ? 1 : S.rate === 1 ? 0.5 : 0.25;
      if (Player.state().playing) { try { $('video').playbackRate = S.rate; } catch (e) { /* keeps its speed */ } }
      renderPlayState();
    });
    $('sound-btn').addEventListener('click', function () { $('video').muted = !$('video').muted; renderPlayState(); });
    $('mark-btn').addEventListener('click', markHere);
    $('unmark-btn').addEventListener('click', unmark);
    $('signal-more').addEventListener('click', signalSheet);
    $('zoom-out').addEventListener('click', Zoom.reset);

    var stage = $('stage');
    stage.addEventListener('pointerdown', Zoom.down);
    stage.addEventListener('pointermove', Zoom.move);
    stage.addEventListener('pointerup', Zoom.up);
    stage.addEventListener('pointercancel', Zoom.up);

    // The strip: a drag moves the frames with the finger, 6 pixels each.
    var jog = null, canvas = $('strip');
    canvas.addEventListener('pointerdown', function (e) {
      if (!Player.state().loaded) return;
      Player.pause();
      jog = { x: e.clientX, frame: Player.state().frame, id: e.pointerId };
      try { canvas.setPointerCapture(e.pointerId); } catch (x) { /* older phones */ }
    });
    canvas.addEventListener('pointermove', function (e) {
      if (!jog || e.pointerId !== jog.id) return;
      Player.go(jog.frame + Math.round((jog.x - e.clientX) / PX));
    });
    function endJog(e) { if (jog && e.pointerId === jog.id) jog = null; }
    canvas.addEventListener('pointerup', endJog);
    canvas.addEventListener('pointercancel', endJog);

    // The bar of the whole video.
    var scrub = $('scrub'), scrubbing = false;
    function scrubTo(e) {
      var r = scrub.getBoundingClientRect(), st = Player.state();
      if (!st.loaded || !r.width) return;
      Player.go(Math.round(Math.max(0, Math.min(1, (e.clientX - r.left) / r.width)) * (st.count - 1)));
    }
    scrub.addEventListener('pointerdown', function (e) {
      scrubbing = true;
      try { scrub.setPointerCapture(e.pointerId); } catch (x) { /* older phones */ }
      scrubTo(e);
    });
    scrub.addEventListener('pointermove', function (e) { if (scrubbing) scrubTo(e); });
    scrub.addEventListener('pointerup', function () { scrubbing = false; });
    scrub.addEventListener('pointercancel', function () { scrubbing = false; });
    scrub.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowLeft') { Player.step(-10); e.preventDefault(); }
      if (e.key === 'ArrowRight') { Player.step(10); e.preventDefault(); }
    });

    document.addEventListener('keydown', function (e) {
      if (S.view !== 'mark' || !$('backdrop').hidden) return;
      var tag = e.target && e.target.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA' || e.target === scrub) return;
      if (e.key === 'ArrowLeft') { Player.step(e.shiftKey ? -10 : -1); e.preventDefault(); }
      else if (e.key === 'ArrowRight') { Player.step(e.shiftKey ? 10 : 1); e.preventDefault(); }
      else if (e.key === ' ' && tag !== 'BUTTON') { if (Player.state().playing) Player.pause(); else Player.play(S.rate); e.preventDefault(); }
      else if (e.key === 'Enter' && tag !== 'BUTTON') { markHere(); e.preventDefault(); }
    });

    $('result-back').addEventListener('click', function () { show('home'); });
    $('result-marks').addEventListener('click', function () {
      S.sel = firstUnmarked() || 'signal';
      show('mark');
      if (S.loadedFor === S.A.id && S.A.signal && Player.state().frame === 0) Player.go(Player.frameAt(S.A.signal.t));
    });
    $('result-tech').addEventListener('click', function () { show('tech'); });
    $('result-delete').addEventListener('click', function () {
      var a = S.A;
      confirmSheet('Διαγραφή ανάλυσης;', title(a) + ' · ' + subtitle(a) + '. Δεν αναιρείται.', 'Διαγραφή', function () {
        var i = DB.analyses.indexOf(a);
        if (i >= 0) DB.analyses.splice(i, 1);
        saveDb();
        if (S.loadedFor === a.id) { Player.unload(); S.file = null; S.loadedFor = null; }
        S.A = null;
        show('home');
        toast('Η ανάλυση σβήστηκε');
      }, true);
    });
    $('tech-back').addEventListener('click', function () { show('result'); });
    $('tech-results').addEventListener('click', function () { show('result'); });
    $('tech-report').addEventListener('click', function () { show('report'); });
    $('report-back').addEventListener('click', function () { show('tech'); });
    $('report-new').addEventListener('click', function () { show('home'); });
    $('report-pdf').addEventListener('click', function () { saveReport('pdf'); });

    $('backdrop').addEventListener('click', function (e) { if (e.target === $('backdrop')) closeSheet(); });
    window.addEventListener('resize', function () { Zoom.apply(); drawStrip(); });

    Player.on('frame', function () { if (S.view === 'mark') renderFrame(); });
    Player.on('state', function () { if (S.view === 'mark') { renderPlayState(); renderFrame(); } });
  }

  // ---------------------------------------------------------------- start
  loadDb();
  wire();
  var hot = window.claude && window.claude.hot;
  if (hot && hot.snapshot) { try { hot.snapshot(function () { return { id: S.A && kept(S.A) ? S.A.id : null }; }); } catch (e) { /* no snapshot here */ } }
  if (window.claude && window.claude.use) {
    window.claude.use('downloads').then(function (d) { downloads = d || null; if (S.view === 'report' || S.view === 'settings') refresh(); }, function () { downloads = null; });
  }
  var again = boot && boot.id ? byId(boot.id) : null;
  if (again) { S.A = again; show('result'); } else show('home');

  return { state: S, db: function () { return DB; }, player: Player, show: show, version: VERSION };
}
