/**
 * Swim Analytics – ο κεντρικός πίνακας σύγκρισης κουρσών (web app του Google Apps Script).
 *
 * Διαβάζει τις αναλύσεις από το φύλλο «Αγώνες» (το ευρετήριο που γράφει το SwimRaceAnalysis.gs για κάθε αγώνα που στέλνει το
 * Kinovea) και τις αναλύει με τις ίδιες συναρτήσεις με τις αναφορές (swimNormalizeRace_, swimAnalyze_), με τις τιμές που έγραψε
 * ο αναλυτής να υπερισχύουν όπως στο φύλλο: οι αριθμοί του πίνακα είναι ίδιοι με τα φύλλα και τα PDF των αναφορών.
 *
 * ΜΟΝΟ ΔΙΑΒΑΖΕΙ: δεν γράφει σε φύλλα, ρυθμίσεις (Script Properties) ή αρχεία· μόνο στην προσωρινή μνήμη του script (CacheService).
 * Δεν δίνει ποτέ αριθμό μητρώου, έτος γέννησης, βίντεο ή άλλα στοιχεία που δεν χρειάζονται στη σύγκριση.
 *
 * Ποιος τον βλέπει: ο πίνακας ανοίγει μόνο όταν το script εκτελείται με τον λογαριασμό αυτού που τον ανοίγει (deployment
 * «Execute as: User accessing the web app», «Who has access: Anyone with Google account») και μόνο για όσους έχουν πρόσβαση στο
 * Google Sheet των αναλύσεων (το ελέγχει το ίδιο το Google όταν ανοίγει το αρχείο). Σε deployment «Execute as: Me» (π.χ. η
 * διεύθυνση του Kinovea) ανοίγει μόνο για τον ιδιοκτήτη· όποιος άλλος παίρνει απλό μήνυμα κειμένου, χωρίς σελίδα και χωρίς
 * δεδομένα, ώστε κανείς να μη διαβάζει με τα δικαιώματα του ιδιοκτήτη.
 *
 * Χρειάζεται στο ίδιο project το SwimRaceAnalysis.gs (2.15.0 ή νεότερο) και το αρχείο HTML «SwimDashboard». Εγκατάσταση:
 *  α) (προτείνεται) νέο Apps Script μέσα στο Google Sheet των αναλύσεων «Αναλύσεις Αγώνων Κολύμβησης (Kinovea)»
 *     (Επεκτάσεις → Apps Script): SwimRaceAnalysis.gs, SwimDashboard.gs, SwimDashboardWeb.gs και το HTML SwimDashboard.
 *     Δεν αλλάζει τίποτα στο KOE App ούτε στη σύνδεση με το Kinovea.
 *  β) στο project του KOE App: SwimDashboard.gs και το HTML SwimDashboard, και στην αρχή του υπάρχοντος doGet(e):
 *       var swimPage = swimDashTryGet(e); if (swimPage) return swimPage;
 *     Η διεύθυνση τότε τελειώνει σε …/exec?page=dashboard.
 *  Και στους δύο: Deploy → New deployment → Web app, Execute as: User accessing the web app, Who has access: Anyone with Google
 *  account. Ένας προπονητής βλέπει τον πίνακα όταν του μοιραστείς (Προβολή) το Google Sheet των αναλύσεων.
 */

var SWIM_DASH_VERSION = '1.0.0';
/** Πόσες αναλύσεις δίνει η swimDashRaces σε μία κλήση (η σελίδα συγκρίνει ως 5). */
var SWIM_DASH_MAX_RACES = 12;
/** Για πόσο (s) μένουν στην προσωρινή μνήμη η λίστα και οι αναλύσεις (το μέγιστο του Apps Script: 6 ώρες). */
var SWIM_DASH_CACHE_TIME = 21600;

var SWIM_DASH_MSG = {
  signIn: 'Ο πίνακας ανοίγει μόνο από τη διεύθυνση που εκτελείται με τον λογαριασμό Google αυτού που τον ανοίγει ' +
    '(Deploy → Web app → Execute as: User accessing the web app). Άνοιξε αυτή τη διεύθυνση, συνδεδεμένος στον λογαριασμό σου.',
  noAccess: 'Δεν βρέθηκε το Google Sheet των αναλύσεων ή δεν έχεις πρόσβαση σε αυτό. Ζήτα από τον διαχειριστή να σου μοιραστεί ' +
    'το αρχείο «Αναλύσεις Αγώνων Κολύμβησης (Kinovea)» και άνοιξε ξανά τη σελίδα.'
};

// ---------------------------------------------------------------------------------------------
// Η σελίδα
// ---------------------------------------------------------------------------------------------

/** Για project που έχει ήδη δικό του doGet (π.χ. KOE App): η σελίδα όταν η διεύθυνση έχει ?page=dashboard, αλλιώς null. */
function swimDashTryGet(e) {
  var page = e && e.parameter ? String(e.parameter.page || '') : '';
  return page === 'dashboard' ? swimDashboardGet(e) : null;
}

/**
 * Η σελίδα του πίνακα. Όταν δεν επιτρέπεται, απλό κείμενο (ContentService) αντί για σελίδα HtmlService: από μια σελίδα HtmlService
 * καλούνται οι συναρτήσεις του project (google.script.run), οπότε δεν δίνεται σε όποιον δεν περνά τον έλεγχο.
 */
function swimDashboardGet(e) {
  try {
    swimDashOpen_();
  } catch (err) {
    return ContentService.createTextOutput('Swim Analytics\n\n' + swimDashError_(err)).setMimeType(ContentService.MimeType.TEXT);
  }
  return HtmlService.createHtmlOutputFromFile('SwimDashboard')
    .setTitle('Swim Analytics · Κ.Ο.Ε.')
    .addMetaTag('viewport', 'width=device-width, initial-scale=1');
}

// ---------------------------------------------------------------------------------------------
// Οι κλήσεις της σελίδας (google.script.run). Καθεμία ελέγχει ξανά τον χρήστη και την πρόσβαση στο αρχείο.
// ---------------------------------------------------------------------------------------------

/** Η λίστα των αγώνων (χωρίς μετρήσεις) και η υπογραφή του ευρετηρίου, για να βλέπει η σελίδα πότε αλλάζει. */
function swimDashList() {
  return swimDashGuard_(function (ctx) {
    var list = swimDashListFor_(ctx, null);
    return { ok: true, sig: list.sig, races: list.races, version: SWIM_DASH_VERSION, analysis: SWIM_VERSION, user: ctx.email };
  });
}

/** Έλεγχος για νέους ή ενημερωμένους αγώνες: με ίδια υπογραφή { changed: false }, αλλιώς η νέα λίστα. */
function swimDashPoll(sig) {
  return swimDashGuard_(function (ctx) {
    var head = swimDashHead_(ctx);
    if (String(sig || '') === head.sig) return { ok: true, changed: false, sig: head.sig };
    var list = swimDashListFor_(ctx, head);
    return { ok: true, changed: true, sig: list.sig, races: list.races };
  });
}

/** Οι μετρήσεις των αγώνων που ζητήθηκαν (ids), ως SWIM_DASH_MAX_RACES. missing: όσοι δεν βρέθηκαν (π.χ. σβήστηκαν). */
function swimDashRaces(ids) {
  return swimDashGuard_(function (ctx) {
    var want = [];
    (Array.isArray(ids) ? ids : [ids]).forEach(function (id) {
      var s = String(id === null || id === undefined ? '' : id).trim();
      if (s && want.indexOf(s) < 0 && want.length < SWIM_DASH_MAX_RACES) want.push(s);
    });
    var head = swimDashHead_(ctx);
    var at = {};
    head.vals.forEach(function (row, i) {
      var id = String(row[SWIM_IDX.id]).trim();
      if (id && !at[id]) at[id] = { i: i, stamp: swimDashHash_(swimDashRowText_(row)) };
    });
    var cache = swimDashCache_();
    var out = [], missing = [];
    want.forEach(function (id) {
      var a = at[id];
      if (!a) { missing.push(id); return; }
      var key = ['swimDash', 'race', SWIM_DASH_VERSION, SWIM_VERSION, ctx.ssId, id.length > 60 ? swimDashHash_(id) : id, a.stamp].join('|');
      var full = cache ? swimCacheGet_(cache, key) : null;
      if (!full || typeof full !== 'object') {
        var race = swimDashParse_(ctx.index.getRange(a.i + 2, SWIM_IDX.json + 1).getValue());
        if (!race || race.kind === 'training') { missing.push(id); return; }
        full = swimDashFull_(race, head.vals[a.i]);
        if (cache) swimCachePut_(cache, key, full, SWIM_DASH_CACHE_TIME);
      }
      out.push(full);
    });
    return { ok: true, races: out, missing: missing };
  });
}

// ---------------------------------------------------------------------------------------------
// Έλεγχος πρόσβασης και ανάγνωση (χωρίς καμία εγγραφή)
// ---------------------------------------------------------------------------------------------

function swimDashGuard_(fn) {
  var ctx;
  try {
    ctx = swimDashOpen_();
  } catch (err) {
    return { ok: false, denied: true, error: swimDashError_(err) };
  }
  try {
    return fn(ctx);
  } catch (err2) {
    return { ok: false, error: swimDashError_(err2) };
  }
}

function swimDashError_(err) {
  return String(err && err.message ? err.message : err);
}

/**
 * Ο χρήστης και το αρχείο των αναλύσεων. Ο χρήστης: το email αυτού που άνοιξε τη σελίδα, μόνο όταν το script εκτελείται ως αυτός
 * (ή είναι ο ιδιοκτήτης). Το αρχείο ανοίγει με τα δικαιώματά του: χωρίς πρόσβαση, σφάλμα.
 */
function swimDashOpen_() {
  var active = '', effective = '';
  try { active = String(Session.getActiveUser().getEmail() || '').trim().toLowerCase(); } catch (e) { active = ''; }
  try { effective = String(Session.getEffectiveUser().getEmail() || '').trim().toLowerCase(); } catch (e2) { effective = ''; }
  if (!active || active !== effective) throw new Error(SWIM_DASH_MSG.signIn);
  var ss = swimDashSpreadsheet_();
  return { email: active, ss: ss, ssId: String(ss.getId()), index: ss.getSheetByName(SWIM_INDEX_SHEET) };
}

/**
 * Το Google Sheet με το φύλλο «Αγώνες», χωρίς να φτιάχνεται τίποτα (το swimReportsSpreadsheet_ φτιάχνει νέο αρχείο όταν δεν βρίσκει
 * το παλιό· εδώ ένας χρήστης χωρίς πρόσβαση παίρνει μόνο μήνυμα). Με τη σειρά: η ρύθμιση SWIM_DASH_SPREADSHEET_ID, το αρχείο του
 * project όταν έχει το φύλλο «Αγώνες» (τρόπος α), οι ρυθμίσεις του KOE App (τρόπος β: SWIM_REPORTS_MODE / SWIM_REPORTS_SPREADSHEET_ID).
 */
function swimDashSpreadsheet_() {
  var props = swimProps_();
  var open = function (id) {
    try { return SpreadsheetApp.openById(id); } catch (e) { return null; }
  };
  var fixed = props.getProperty('SWIM_DASH_SPREADSHEET_ID');
  if (fixed) {
    var f = open(fixed);
    if (f) return f;
    throw new Error(SWIM_DASH_MSG.noAccess);
  }
  var active = null;
  try { active = SpreadsheetApp.getActiveSpreadsheet(); } catch (e) { active = null; }
  if (active && active.getSheetByName(SWIM_INDEX_SHEET)) return active;
  if (props.getProperty('SWIM_REPORTS_MODE') === 'same') {
    var dataId = props.getProperty('SWIM_DATA_SPREADSHEET_ID');
    var d = dataId ? open(dataId) : active;
    if (d) return d;
    throw new Error(SWIM_DASH_MSG.noAccess);
  }
  var reportsId = props.getProperty('SWIM_REPORTS_SPREADSHEET_ID');
  if (reportsId) {
    var r = open(reportsId);
    if (r) return r;
  }
  throw new Error(SWIM_DASH_MSG.noAccess);
}

function swimDashCache_() {
  try { return CacheService.getScriptCache(); } catch (e) { return null; }
}

/** Η τιμή ενός κελιού ως κείμενο για την υπογραφή (μια ημερομηνία που διάβασε το Google ως ημερομηνία: η στιγμή της). */
function swimDashCell_(v) {
  if (v instanceof Date) return String(v.getTime());
  return String(v === null || v === undefined ? '' : v);
}

function swimDashRowText_(row) {
  return row.map(swimDashCell_).join('\u0001');
}

/** Hash 32 bit (FNV-1a) σε 8 δεκαεξαδικά. */
function swimDashHash_(text) {
  var h = 0x811c9dc5;
  var s = String(text);
  for (var i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = (h + ((h << 1) + (h << 4) + (h << 7) + (h << 8) + (h << 24))) >>> 0;
  }
  return ('0000000' + h.toString(16)).slice(-8);
}

/**
 * Οι στήλες του ευρετηρίου χωρίς τα JSON (Race ID … Ενημέρωση) και η υπογραφή τους: αλλάζει με κάθε νέα ανάλυση, κάθε ενημέρωση
 * (η στήλη Ενημέρωση) και κάθε γραμμή που σβήνεται.
 */
function swimDashHead_(ctx) {
  var index = ctx.index;
  var last = index ? index.getLastRow() : 0;
  var vals = last >= 2 ? index.getRange(2, 1, last - 1, 11).getValues() : [];
  var text = vals.map(swimDashRowText_).join('\u0002');
  return { last: last, vals: vals, sig: [SWIM_DASH_VERSION, ctx.ssId, last, swimDashHash_(text)].join(':') };
}

function swimDashParse_(json) {
  try {
    var raw = JSON.parse(String(json));
    return raw && typeof raw === 'object' ? swimNormalizeRace_(raw) : null;
  } catch (e) {
    return null;
  }
}

/** Η λίστα (από την προσωρινή μνήμη όταν το ευρετήριο δεν άλλαξε). */
function swimDashListFor_(ctx, head) {
  head = head || swimDashHead_(ctx);
  var cache = swimDashCache_();
  var key = 'swimDash|list|' + head.sig;
  var races = cache ? swimCacheGet_(cache, key) : null;
  if (!Array.isArray(races)) {
    races = [];
    if (head.last >= 2) {
      var json = ctx.index.getRange(2, SWIM_IDX.json + 1, head.last - 1, 1).getValues();
      head.vals.forEach(function (row, i) {
        if (!String(row[SWIM_IDX.id]).trim()) return;
        var race = swimDashParse_(json[i] ? json[i][0] : '');
        // Οι τεχνικές αναλύσεις (προπόνηση) δεν είναι κούρσες: δεν έχουν χρόνους για σύγκριση.
        if (!race || race.kind === 'training') return;
        races.push(swimDashLight_(race, row));
      });
    }
    if (cache) swimCachePut_(cache, key, races, SWIM_DASH_CACHE_TIME);
  }
  return { sig: head.sig, races: races };
}

/** Ημερομηνία αγώνα σε μορφή 2026-10-17 (από «17.10.2026», «17/10/2026» ή «2026-10-17»), αλλιώς κενό. */
function swimDashDate_(s) {
  var t = String(s || '').trim();
  var m = t.match(/^(\d{4})-(\d{1,2})-(\d{1,2})/);
  if (m) return m[1] + '-' + ('0' + m[2]).slice(-2) + '-' + ('0' + m[3]).slice(-2);
  m = t.match(/^(\d{1,2})[.\/-](\d{1,2})[.\/-](\d{4})$/);
  if (m) return m[3] + '-' + ('0' + m[2]).slice(-2) + '-' + ('0' + m[1]).slice(-2);
  return '';
}

/** Η στήλη «Ενημέρωση» (κείμενο 30/10/2026 16:45 ή ημερομηνία του Google) ως 2026-10-30 16:45, για ταξινόμηση. */
function swimDashUpdated_(v) {
  if (v instanceof Date) {
    try { return Utilities.formatDate(v, Session.getScriptTimeZone(), 'yyyy-MM-dd HH:mm'); } catch (e) { return ''; }
  }
  var m = String(v || '').trim().match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})(?:\s+(\d{1,2}):(\d{2}))?/);
  if (!m) return '';
  return m[3] + '-' + ('0' + m[2]).slice(-2) + '-' + ('0' + m[1]).slice(-2) + ' ' + ('0' + (m[4] || '0')).slice(-2) + ':' + (m[5] || '00');
}

function swimDashRound_(v) {
  var n = swimNum_(v);
  return n === null ? null : Math.round(n * 1000) / 1000;
}

/** Η τιμή που έγραψε ο αναλυτής (υπερισχύει, όπως στο φύλλο), αλλιώς η αυτόματη. */
function swimDashManual_(obj, key, auto) {
  var v = obj ? swimNum_(obj[key]) : null;
  return v !== null ? v : auto;
}

/** Τα στοιχεία ενός αγώνα για τη λίστα: χωρίς αριθμό μητρώου, έτος γέννησης, βίντεο και μετρήσεις. */
function swimDashLight_(race, row) {
  return {
    id: String(row[SWIM_IDX.id]).trim(),
    athlete: race.athlete,
    gender: swimGenderCode_(race.gender),
    club: race.club,
    category: race.category,
    style: race.style,
    distance: race.distance,
    distanceLabel: race.distanceLabel,
    pool: race.pool,
    poolUnit: race.poolUnit || 'm',
    relay: !!race.relay,
    relayLeg: race.relayLeg || '',
    competition: race.competition,
    location: race.location,
    round: race.round,
    date: swimDashDate_(race.date),
    dateText: race.date,
    lane: race.lane,
    dq: !!race.dq,
    finalTime: swimDashRound_(row[9]),
    updated: swimDashUpdated_(row[10])
  };
}

/** Ο αγώνας με τις μετρήσεις του για τα γραφήματα και τους πίνακες της σελίδας (ίδιες τιμές με το φύλλο του αγώνα). */
function swimDashFull_(race, row) {
  var a = swimAnalyze_(race);
  var n = swimDashRound_;
  var m = race.manual || {};
  var o = swimDashLight_(race, row);
  var estimated = SWIM_STRICT_VALUES && !!m.underwaterEstimated;
  o.manual = {
    reaction: n(m.reaction),
    entryDistHands: n(m.entryDistHands),
    underwaterDist: n(m.underwaterDist),
    underwaterEstimated: estimated,
    breakoutDist: n(m.breakoutDist),
    turns: {}
  };
  a.turns.forEach(function (t) {
    var mt = m.turns ? m.turns[String(t.wall)] : null;
    if (mt && swimNum_(mt.underwaterDist) !== null)
      o.manual.turns[String(t.wall)] = { underwaterDist: n(mt.underwaterDist), underwaterEstimated: !!mt.underwaterEstimated };
  });
  o.rows = a.rows.map(function (x) {
    return { d: x.d, prevD: x.prevD, t: n(x.t), segTime: n(x.segTime), vel: n(x.vel), strokes: n(x.strokes), sr: n(x.sr), dps: n(x.dps),
      isWall: !!x.isWall };
  });
  // Η εκκίνηση όπως στο φύλλο: όποια τιμή έγραψε ο αναλυτής υπερισχύει της αυτόματης (η «έξοδος» = χρόνος ως την έξοδο από το νερό).
  var s = a.start;
  o.start = {
    blockTime: n(swimDashManual_(m, 'blockTime', s.blockTime)),
    entryTime: n(swimDashManual_(m, 'entryTime', s.entryTime)),
    breakout: n(swimDashManual_(m, 'underwaterTime', s.underwaterTime)),
    kicks: n(swimDashManual_(m, 'kicks', s.kicks)),
    flightTime: n(swimDashManual_(m, 'flightTime', s.flightTime)),
    t15: n(s.t15)
  };
  o.turns = a.turns.map(function (t) {
    var mt = m.turns ? m.turns[String(t.wall)] : null;
    return { wall: t.wall, tWall: n(t.tWall), pushoff: n(t.pushoff), breakout: n(t.breakout),
      turnTime: n(swimDashManual_(mt, 'turnTime', t.turnTime)), underwaterTime: n(swimDashManual_(mt, 'underwaterTime', t.underwaterTime)),
      kicks: n(swimDashManual_(mt, 'kicks', t.kicks)), inOut: n(t.inOut), out15: n(t.out15), in5: n(t.in5),
      lastStrokeGap: n(t.lastStrokeGap), exchange: !!t.exchange, flip: !!t.flip };
  });
  var f = a.finish;
  o.finish = { tFinish: n(f.tFinish), lastStrokeGap: n(f.lastStrokeGap), last5: n(f.last5), last15: n(f.last15) };
  o.blocks = a.blocks.map(function (b) {
    return { from: b.from, to: b.to, time: n(b.time), strokes: n(b.strokes), swolf: n(b.swolf), dak: n(b.dak), sr: n(b.sr), dps: n(b.dps),
      vel: n(b.vel), style: b.style };
  });
  var u = a.summary;
  o.summary = { finalTime: n(u.finalTime), avgVel: n(u.avgVel), totalStrokes: n(u.totalStrokes), avgSr: n(u.avgSr), avgDps: n(u.avgDps),
    avgDak: n(u.avgDak), startTime: n(u.startTime), turnsTime: n(u.turnsTime), finishTime: n(u.finishTime) };
  o.warnings = a.warnings.length;
  return o;
}
