/**
 * Swim Race Analysis – backend σε Google Sheets για το πάνελ "Ανάλυση αγώνα κολύμβησης" του Kinovea.
 *
 * Μπαίνει ως ξεχωριστό αρχείο (π.χ. SwimRaceAnalysis.gs) σε υπάρχον project Apps Script, όπως το KOE App:
 * δεν ορίζει onOpen / doGet / doPost, ώστε να μη συγκρούεται με τον κώδικα που υπάρχει ήδη.
 * Οι συναρτήσεις εισόδου είναι swimOnOpen, swimDoGet, swimDoPost (και swimTryHandlePost για υπάρχον doPost).
 *
 * Τι κάνει:
 *   1. Δίνει στο Kinovea τη λίστα αθλητών από το φύλλο "Στοιχεία Αθλητών/τριων" (unique_id, ονοματεπώνυμο, σωματείο…).
 *   2. Δέχεται από το Kinovea τα γεγονότα που μαρκάρεις στο βίντεο (περάσματα, χεριές, δελφινισμοί, έξοδος, αποκόλληση, είσοδος)
 *      και υπολογίζει χεριές ανά τμήμα, φάσεις εκκίνησης/στροφών/τερματισμού, SR από τους χρόνους χεριών.
 *   3. Φτιάχνει ένα φύλλο ανά αγώνα με τύπους όπως το Excel ανάλυσης, σε ξεχωριστό spreadsheet αναφορών
 *      ("Αναλύσεις Αγώνων Κολύμβησης (Kinovea)"), ώστε το κύριο αρχείο να μένει ελαφρύ.
 *   4. Γράφει μία γραμμή ανά ανάλυση στο φύλλο "Αναλύσεις Αγώνων" του κύριου αρχείου, με το unique_id του αθλητή.
 *   5. Εξάγει το report σε PDF στον φάκελο "Swim Race Reports" του Drive.
 *   6. Κρατά τις elite αναφορές (φύλλο "Elite αναφορές") για το live Δ στα περάσματα στο Kinovea.
 */

var SWIM_VERSION = '1.2.0';
var SWIM_APP_ID = 'kinovea-swim';
var SWIM_INDEX_SHEET = 'Αγώνες';
var SWIM_SUMMARY_SHEET = 'Αναλύσεις Αγώνων';
var SWIM_ATHLETES_SHEET = 'Στοιχεία Αθλητών/τριων';
var SWIM_REPORTS_TITLE = 'Αναλύσεις Αγώνων Κολύμβησης (Kinovea)';
var SWIM_REPORT_FOLDER = 'Swim Race Reports';
var SWIM_TIMING_FIRST_ROW = 17;

var SWIM_COLORS = {
  title: '#0B3C5D',
  section: '#1D6FA3',
  sectionText: '#FFFFFF',
  header: '#D9E7F2',
  label: '#EEF4F9',
  formula: '#F2F2F2',
  auto: '#E3F1FB',
  input: '#FFFFFF',
  kpi: '#FFF6E0',
  border: '#9BB7CC'
};

var SWIM_IM_ORDER = ['fly', 'bk', 'br', 'fs'];
var SWIM_MEDLEY_RELAY_ORDER = ['bk', 'br', 'fly', 'fs'];
var SWIM_STYLE_LABELS = { fs: 'fs', bk: 'bk', br: 'br', fly: 'fly', im: 'medley' };

// ---------------------------------------------------------------------------------------------
// Web app – σημεία εισόδου (τα doGet/doPost/onOpen βρίσκονται στο SwimWebApp.gs ή στον δικό σου κώδικα)
// ---------------------------------------------------------------------------------------------

function swimDoGet(e) {
  return swimJson_({ ok: true, app: SWIM_APP_ID, version: SWIM_VERSION });
}

function swimDoPost(e) {
  var req = swimParseRequest_(e);
  if (!req) return swimJson_({ ok: false, error: 'Μη έγκυρο αίτημα (όχι JSON).' });
  return swimHandle_(req);
}

/**
 * Για project που έχει ήδη δικό του doPost: βάλε στην αρχή του
 *   var swimResponse = swimTryHandlePost(e); if (swimResponse) return swimResponse;
 * Επιστρέφει null όταν το αίτημα δεν είναι από το Kinovea, οπότε ο υπόλοιπος κώδικας τρέχει κανονικά.
 */
function swimTryHandlePost(e) {
  var req = swimParseRequest_(e);
  if (!req || req.app !== SWIM_APP_ID) return null;
  return swimHandle_(req);
}

function swimParseRequest_(e) {
  try {
    var text = e && e.postData ? e.postData.contents : '';
    if (!text) return null;
    var req = JSON.parse(text);
    return req && typeof req === 'object' ? req : null;
  } catch (err) {
    return null;
  }
}

function swimHandle_(req) {
  var token = swimGetToken_();
  if (!token || String(req.token || '') !== token)
    return swimJson_({ ok: false, error: 'Λάθος token. Αντέγραψε ξανά το token από το μενού Swim Analysis → Στοιχεία σύνδεσης.' });

  var action = String(req.action || 'update');

  // Η λίστα αθλητών μόνο διαβάζει: δεν χρειάζεται κλείδωμα.
  if (action === 'athletes') {
    try {
      var list = swimAthletes_();
      return swimJson_({ ok: true, version: SWIM_VERSION, sheet: list.sheet, athletes: list.athletes });
    } catch (err) {
      return swimJson_({ ok: false, error: String(err && err.message ? err.message : err) });
    }
  }

  var lock = LockService.getScriptLock();
  try {
    lock.waitLock(25000);
  } catch (err) {
    return swimJson_({ ok: false, error: 'Το φύλλο είναι απασχολημένο, θα ξαναπροσπαθήσει.', retry: true });
  }

  try {
    if (action === 'ping') {
      var data = swimDataSpreadsheet_();
      var reports = swimReportsSpreadsheet_();
      return swimJson_({ ok: true, version: SWIM_VERSION, spreadsheet: data.getName(), reports: reports.getName(), url: reports.getUrl() });
    }

    if (action === 'references') {
      var refs = swimReferences_();
      return swimJson_({ ok: true, version: SWIM_VERSION, sheet: SWIM_REFERENCES_SHEET, references: refs.references, skipped: refs.skipped });
    }

    if (action === 'saveReference') {
      return swimJson_(swimSaveReference_(req.reference));
    }

    if (action === 'update' || action === 'pdf') {
      var result = swimUpsertRace_(req.race);
      if (action === 'pdf') {
        var ss = swimReportsSpreadsheet_();
        result.pdf = swimExportPdf_(ss, swimSheetById_(ss, result.sheetId));
        swimSetSummaryPdf_(result.raceId, result.pdf.url);
      }
      return swimJson_(result);
    }

    return swimJson_({ ok: false, error: 'Άγνωστη ενέργεια: ' + action });
  } catch (err) {
    return swimJson_({ ok: false, error: String(err && err.message ? err.message : err), stack: String(err && err.stack ? err.stack : '') });
  } finally {
    lock.releaseLock();
  }
}

function swimJson_(obj) {
  return ContentService.createTextOutput(JSON.stringify(obj)).setMimeType(ContentService.MimeType.JSON);
}

function swimProps_() {
  return PropertiesService.getScriptProperties();
}

function swimGetToken_() {
  return swimProps_().getProperty('SWIM_TOKEN');
}

/** Το κύριο αρχείο (π.χ. KOE App): λίστα αθλητών και πίνακας "Αναλύσεις Αγώνων". */
function swimDataSpreadsheet_() {
  var id = swimProps_().getProperty('SWIM_DATA_SPREADSHEET_ID');
  if (id) return SpreadsheetApp.openById(id);
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  if (ss) return ss;
  throw new Error('Δεν βρέθηκε το Google Sheet. Τρέξε πρώτα τη συνάρτηση swimSetup από το Apps Script.');
}

/**
 * Το spreadsheet των αναφορών (ένα φύλλο ανά αγώνα). Δημιουργείται αυτόματα την πρώτη φορά.
 * Με το Script Property SWIM_REPORTS_MODE = same, οι αναφορές μπαίνουν στο κύριο αρχείο.
 */
function swimReportsSpreadsheet_() {
  var props = swimProps_();
  if (props.getProperty('SWIM_REPORTS_MODE') === 'same') return swimDataSpreadsheet_();

  var id = props.getProperty('SWIM_REPORTS_SPREADSHEET_ID');
  if (id) {
    try {
      return SpreadsheetApp.openById(id);
    } catch (e) {
      // Διαγράφηκε ή δεν είναι προσβάσιμο: φτιάχνουμε νέο.
    }
  }

  var ss = SpreadsheetApp.create(SWIM_REPORTS_TITLE);
  try { DriveApp.getFileById(ss.getId()).moveTo(swimReportFolder_()); } catch (e) { /* μένει στο My Drive */ }
  var first = ss.getSheets()[0];
  var index = swimIndexSheet_(ss);
  if (first.getSheetId() !== index.getSheetId()) ss.deleteSheet(first);
  props.setProperty('SWIM_REPORTS_SPREADSHEET_ID', ss.getId());
  return ss;
}

// ---------------------------------------------------------------------------------------------
// Αθλητές (από το φύλλο "Στοιχεία Αθλητών/τριων")
// ---------------------------------------------------------------------------------------------

var SWIM_ATHLETE_COLUMNS = {
  id: ['UNIQUE_ID', 'ΑΡΙΘΜΟΣ ΜΗΤΡΩΟΥ', 'ΜΗΤΡΩΟ', 'Α.Μ.', 'ΑΜ'],
  name: ['ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΟΝΟΜΑ'],
  gender: ['ΦΥΛΟ'],
  birthYear: ['ΕΤΟΣ ΓΕΝΝΗΣΗΣ', 'ΕΤΟΣ'],
  category: ['ΚΑΤΗΓΟΡΙΑ'],
  club: ['ΣΩΜΑΤΕΙΟ', 'ΟΜΙΛΟΣ']
};

function swimNormText_(s) {
  return String(s === null || s === undefined ? '' : s).normalize('NFD').replace(/[̀-ͯ]/g, '')
    .toUpperCase().replace(/\s+/g, ' ').trim();
}

function swimHeaderMap_(headerRow) {
  var norm = headerRow.map(swimNormText_);
  var map = {};
  Object.keys(SWIM_ATHLETE_COLUMNS).forEach(function (key) {
    var names = SWIM_ATHLETE_COLUMNS[key];
    for (var i = 0; i < names.length && map[key] === undefined; i++) {
      var idx = norm.indexOf(names[i]);
      if (idx >= 0) map[key] = idx;
    }
  });
  return map;
}

/** Επιστρέφει { sheet, athletes: [{id, name, gender, birthYear, category, club}] } χωρίς διπλότυπα μητρώα. */
function swimAthletes_() {
  var ss = swimDataSpreadsheet_();
  var preferred = swimProps_().getProperty('SWIM_ATHLETES_SHEET') || SWIM_ATHLETES_SHEET;
  var sheet = ss.getSheetByName(preferred);
  var candidates = sheet ? [sheet] : ss.getSheets();

  for (var s = 0; s < candidates.length; s++) {
    var sh = candidates[s];
    var lastCol = sh.getLastColumn(), lastRow = sh.getLastRow();
    if (lastCol < 2 || lastRow < 2) continue;
    var map = swimHeaderMap_(sh.getRange(1, 1, 1, lastCol).getValues()[0]);
    if (map.id === undefined || map.name === undefined) continue;

    var rows = sh.getRange(2, 1, lastRow - 1, lastCol).getValues();
    var seen = {}, athletes = [];
    var cell = function (row, key) {
      return map[key] === undefined ? '' : String(row[map[key]] === null ? '' : row[map[key]]).trim();
    };
    rows.forEach(function (row) {
      var id = cell(row, 'id');
      var name = cell(row, 'name');
      if (!id || !name || seen[id]) return;
      seen[id] = true;
      athletes.push({ id: id, name: name, gender: cell(row, 'gender'), birthYear: cell(row, 'birthYear'), category: cell(row, 'category'), club: cell(row, 'club') });
    });
    athletes.sort(function (a, b) { return a.name.localeCompare(b.name, 'el'); });
    return { sheet: sh.getName(), athletes: athletes };
  }

  return { sheet: '', athletes: [] };
}

// ---------------------------------------------------------------------------------------------
// Upsert: race JSON -> sheet
// ---------------------------------------------------------------------------------------------

function swimUpsertRace_(rawRace) {
  if (!rawRace) throw new Error('Λείπουν τα δεδομένα του αγώνα.');
  var race = swimNormalizeRace_(rawRace);
  if (!race.id) throw new Error('Λείπει το id του αγώνα.');
  if (!race.checkpoints.length) throw new Error('Δεν υπάρχουν σημεία ελέγχου (αποστάσεις).');

  var analysis = swimAnalyze_(race);
  var layout = swimBuildLayout_(race, analysis);

  var ss = swimReportsSpreadsheet_();
  var index = swimIndexSheet_(ss);
  var entry = swimFindIndexEntry_(index, race.id);

  var desiredName = swimSheetBaseName_(race);
  var sheet = entry && entry.sheetId ? swimSheetById_(ss, entry.sheetId) : null;
  var rebuilt = false;

  if (!sheet) {
    sheet = ss.insertSheet(swimUniqueSheetName_(ss, desiredName, null));
    rebuilt = true;
  } else if (swimNameRoot_(sheet.getName()) !== desiredName && entry.prevBaseName !== null &&
             swimNameRoot_(sheet.getName()) === entry.prevBaseName) {
    // Το όνομα το είχαμε δώσει εμείς και άλλαξαν τα στοιχεία (π.χ. επιλέχθηκε αθλητής): μετονομασία.
    // Αν ο χρήστης μετονόμασε το φύλλο με το χέρι, το αφήνουμε όπως είναι.
    var newName = swimUniqueSheetName_(ss, desiredName, sheet);
    if (newName !== sheet.getName()) sheet.setName(newName);
  }

  if (rebuilt || !entry || entry.layoutSig !== layout.signature) {
    swimRenderLayout_(sheet, layout);
    rebuilt = true;
  }

  swimWriteInputs_(sheet, race, analysis, layout);
  SpreadsheetApp.flush();

  var kpi = sheet.getRange(2, layout.kpi.finalTime.c, 6, 1).getValues().map(function (r) { return r[0]; });
  var sheetUrl = ss.getUrl() + '#gid=' + sheet.getSheetId();
  swimSaveIndexEntry_(index, entry, race, sheet, layout.signature, kpi[0]);
  swimWriteSummary_(race, analysis, kpi, sheetUrl);

  return {
    ok: true,
    version: SWIM_VERSION,
    raceId: race.id,
    sheetId: sheet.getSheetId(),
    sheetName: sheet.getName(),
    sheetUrl: sheetUrl,
    rebuilt: rebuilt,
    rows: analysis.rows.map(function (row) {
      return {
        d: row.d, t: row.t, segTime: row.segTime, vel: row.vel, avgVel: row.avgVel,
        strokes: row.strokes, sr: row.sr, dps: row.dps, srExact: row.srExact, style: row.style
      };
    }),
    start: analysis.start,
    turns: analysis.turns,
    finish: analysis.finish,
    summary: analysis.summary,
    warnings: analysis.warnings
  };
}

// ---------------------------------------------------------------------------------------------
// Πίνακας "Αναλύσεις Αγώνων" στο κύριο αρχείο (μία γραμμή ανά ανάλυση, με το unique_id)
// ---------------------------------------------------------------------------------------------

var SWIM_SUMMARY_HEADERS = ['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΣΩΜΑΤΕΙΟ', 'ΚΑΤΗΓΟΡΙΑ', 'Ημερομηνία', 'Διοργάνωση', 'Τόπος', 'Φάση',
  'Αγώνισμα', 'Απόσταση', 'Πισίνα', 'Τελικός χρόνος', 'Μέση ταχύτητα', 'Συνολικές χεριές', 'Μέση συχνότητα', 'Μέσο DPS',
  'Μέσος ΔΑΚ', 'Χρόνος 15μ.', 'Χρόνοι ανά 50μ.', 'Report', 'PDF', 'Race ID', 'Ενημέρωση'];
var SWIM_SUM = { pdf: 20, raceId: 21 };

function swimSummarySheet_(ss) {
  var sh = ss.getSheetByName(SWIM_SUMMARY_SHEET);
  if (!sh) {
    sh = ss.insertSheet(SWIM_SUMMARY_SHEET);
    sh.getRange(1, 1, 1, SWIM_SUMMARY_HEADERS.length).setValues([SWIM_SUMMARY_HEADERS])
      .setFontWeight('bold').setBackground(SWIM_COLORS.header);
    sh.setFrozenRows(1);
  }
  return sh;
}

function swimSummaryRow_(sh, raceId) {
  var last = sh.getLastRow();
  if (last < 2) return null;
  var ids = sh.getRange(2, SWIM_SUM.raceId + 1, last - 1, 1).getValues();
  for (var i = 0; i < ids.length; i++) if (String(ids[i][0]) === raceId) return i + 2;
  return null;
}

function swimSplits50_(analysis) {
  var at = {};
  analysis.rows.forEach(function (r) { if (r.t !== null) at[r.d] = r.t; });
  var out = [], prev = 0;
  for (var d = 50; at[d] !== undefined; d += 50) {
    out.push((at[d] - prev).toFixed(2));
    prev = at[d];
  }
  return out.join(' / ');
}

function swimWriteSummary_(race, analysis, kpi, sheetUrl) {
  var sh = swimSummarySheet_(swimDataSpreadsheet_());
  var row = swimSummaryRow_(sh, race.id);
  var pdf = row ? sh.getRange(row, SWIM_SUM.pdf + 1).getValue() : '';
  var num = function (v) { return typeof v === 'number' && isFinite(v) ? Math.round(v * 100) / 100 : ''; };
  var t15 = null;
  analysis.rows.forEach(function (r) { if (r.d === 15) t15 = r.t; });
  var idValue = /^\d+$/.test(race.athleteId) ? Number(race.athleteId) : race.athleteId;
  var values = [idValue, race.athlete, race.club, race.category, race.date, race.competition, race.location, race.round,
    (SWIM_STYLE_LABELS[race.style] || race.style) + (race.relay ? ' relay' : ''), race.distanceLabel, race.pool,
    num(kpi[0]), num(kpi[1]), num(kpi[2]), num(kpi[3]), num(kpi[4]), num(kpi[5]),
    t15 === null ? '' : Math.round(t15 * 100) / 100, swimSplits50_(analysis), sheetUrl, pdf, race.id,
    Utilities.formatDate(new Date(), Session.getScriptTimeZone(), 'dd/MM/yyyy HH:mm')];
  var r = row || sh.getLastRow() + 1;
  sh.getRange(r, 1, 1, values.length).setValues([values]);
}

function swimSetSummaryPdf_(raceId, url) {
  var sh = swimSummarySheet_(swimDataSpreadsheet_());
  var row = swimSummaryRow_(sh, raceId);
  if (row) sh.getRange(row, SWIM_SUM.pdf + 1).setValue(url);
}

// ---------------------------------------------------------------------------------------------
// Analysis (pure functions – no Apps Script services)
// ---------------------------------------------------------------------------------------------

function swimNum_(v) {
  if (v === null || v === undefined || v === '') return null;
  var n = Number(v);
  return isFinite(n) ? n : null;
}

function swimSortedTimes_(arr) {
  var out = [];
  (arr || []).forEach(function (v) {
    var n = swimNum_(v);
    if (n !== null) out.push(n);
  });
  out.sort(function (a, b) { return a - b; });
  return out;
}

function swimNormalizeRace_(raw) {
  var r = raw || {};
  var style = String(r.style || 'fs').toLowerCase();
  if (style === 'medley' || style === 'im') style = 'im';
  if (['fs', 'bk', 'br', 'fly', 'im'].indexOf(style) < 0) style = 'fs';

  var race = {
    id: String(r.id || ''),
    athlete: String(r.athlete || ''),
    athleteId: String(r.athleteId === undefined || r.athleteId === null ? '' : r.athleteId).trim(),
    gender: String(r.gender || ''),
    club: String(r.club || ''),
    category: String(r.category || ''),
    birthYear: String(r.birthYear || ''),
    style: style,
    relay: !!r.relay,
    distance: swimNum_(r.distance) || 0,
    distanceLabel: r.distanceLabel ? String(r.distanceLabel) : '',
    pool: swimNum_(r.pool) || 50,
    competition: String(r.competition || ''),
    location: String(r.location || ''),
    round: String(r.round || ''),
    date: String(r.date || ''),
    video: String(r.video || ''),
    lane: r.lane === undefined || r.lane === null ? '' : String(r.lane),
    checkpoints: [],
    strokes: swimSortedTimes_(r.strokes),
    kicks: swimSortedTimes_(r.kicks),
    breakouts: swimSortedTimes_(r.breakouts),
    feetOff: swimSortedTimes_(r.feetOff),
    entries: swimSortedTimes_(r.entries),
    manual: r.manual || {},
    strokeRounding: r.strokeRounding === undefined || r.strokeRounding === null ? 0.5 : (swimNum_(r.strokeRounding) || 0)
  };

  var seen = {};
  (r.checkpoints || []).forEach(function (cp) {
    var d = swimNum_(cp && typeof cp === 'object' ? cp.d : cp);
    if (d === null || d <= 0 || seen[d]) return;
    seen[d] = true;
    race.checkpoints.push({ d: d, t: swimNum_(cp && typeof cp === 'object' ? cp.t : null) });
  });
  race.checkpoints.sort(function (a, b) { return a.d - b.d; });

  if (!race.distance && race.checkpoints.length) race.distance = race.checkpoints[race.checkpoints.length - 1].d;
  if (!race.distanceLabel) race.distanceLabel = String(race.distance);
  return race;
}

function swimStyleAt_(race, dEnd) {
  if (race.style !== 'im') return race.style;
  var order = race.relay ? SWIM_MEDLEY_RELAY_ORDER : SWIM_IM_ORDER;
  var leg = race.distance / 4;
  if (!(leg > 0)) return 'fs';
  var idx = Math.floor((dEnd - 1e-6) / leg);
  if (idx < 0) idx = 0;
  if (idx > 3) idx = 3;
  return order[idx];
}

/** Πατήματα ανά κύκλο: ελεύθερο/ύπτιο μετράμε κάθε χέρι (2 ανά κύκλο), πεταλούδα/πρόσθιο 1. */
function swimPressesPerCycle_(style) {
  return (style === 'fs' || style === 'bk') ? 2 : 1;
}

function swimRound_(x, step) {
  if (x === null || x === undefined) return null;
  if (!(step > 0)) return Math.round(x * 100) / 100;
  return Math.round(x / step + 1e-9) * step;
}

function swimFixed_(x, digits) {
  if (x === null || x === undefined || !isFinite(x)) return null;
  var f = Math.pow(10, digits);
  return Math.round(x * f) / f;
}

/** Πλήθος στοιχείων <= t σε ταξινομημένο πίνακα. */
function swimUpperBound_(arr, t) {
  var lo = 0, hi = arr.length;
  while (lo < hi) {
    var mid = (lo + hi) >> 1;
    if (arr[mid] <= t) lo = mid + 1; else hi = mid;
  }
  return lo;
}

function swimHasBarrier_(barriers, a, b) {
  for (var i = 0; i < barriers.length; i++) {
    if (barriers[i] > a && barriers[i] < b) return true;
  }
  return false;
}

/**
 * Σωρευτικός αριθμός χεριών τη στιγμή t, με γραμμική παρεμβολή ανάμεσα σε δύο πατήματα
 * (έτσι προκύπτουν οι "μισές" χεριές στα σημεία ελέγχου). Δεν γίνεται παρεμβολή πάνω από
 * στροφή (τοίχο), γιατί εκεί ο κολυμβητής είναι στη στροφή / κάτω από το νερό.
 */
function swimCumulativeStrokes_(strokes, barriers, t) {
  var k = swimUpperBound_(strokes, t);
  if (k === 0 || k === strokes.length) return k;
  var a = strokes[k - 1], b = strokes[k];
  if (b <= a || swimHasBarrier_(barriers, a, b)) return k;
  return k + (t - a) / (b - a);
}

function swimFirst_(arr, pred) {
  for (var i = 0; i < arr.length; i++) if (pred(arr[i])) return arr[i];
  return null;
}

function swimLast_(arr, pred) {
  for (var i = arr.length - 1; i >= 0; i--) if (pred(arr[i])) return arr[i];
  return null;
}

function swimAnalyze_(race) {
  var pool = race.pool;
  var dist = race.distance;
  var warnings = [];

  var rows = race.checkpoints.map(function (cp) { return { d: cp.d, t: cp.t }; });
  var timeAt = {};
  rows.forEach(function (row) { if (row.t !== null) timeAt[row.d] = row.t; });

  var turnWalls = [];
  for (var w = pool; w < dist - 1e-9; w += pool) turnWalls.push(w);
  var barriers = [];
  turnWalls.forEach(function (wall) { if (timeAt[wall] !== undefined) barriers.push(timeAt[wall]); });
  barriers.sort(function (a, b) { return a - b; });

  var hasStrokes = race.strokes.length > 0;
  var step = race.strokeRounding;

  // ---- Σημεία ελέγχου / τμήματα
  var lastT = null;
  rows.forEach(function (row, i) {
    row.prevD = i === 0 ? 0 : rows[i - 1].d;
    var tPrev = i === 0 ? 0 : rows[i - 1].t;
    row.style = swimStyleAt_(race, row.d);
    row.ppc = swimPressesPerCycle_(row.style);
    var lastWall = Math.floor((row.prevD + 1e-9) / pool) * pool;
    var nextWall = lastWall + pool;
    row.isSwim = row.prevD >= lastWall + 15 - 1e-9;                      // χωρίς εκκίνηση/στροφή
    row.isFree = row.isSwim && row.d <= nextWall - 5 + 1e-9;              // καθαρή κολύμβηση (ειδικοί τομείς)
    row.isWall = Math.abs(row.d / pool - Math.round(row.d / pool)) < 1e-9;

    row.segTime = (row.t !== null && tPrev !== null) ? row.t - tPrev : null;
    row.vel = row.segTime !== null && row.segTime > 0 ? (row.d - row.prevD) / row.segTime : null;
    row.avgVel = row.t !== null && row.t > 0 ? row.d / row.t : null;

    if (row.t !== null) {
      if (lastT !== null && row.t <= lastT) warnings.push('Ο χρόνος στα ' + row.d + 'μ. δεν είναι μεγαλύτερος από τον προηγούμενο.');
      lastT = row.t;
    }

    row.strokes = null;
    row.srExact = null;
    if (hasStrokes && row.t !== null && tPrev !== null) {
      var nA = swimRound_(swimCumulativeStrokes_(race.strokes, barriers, tPrev), step);
      var nB = swimRound_(swimCumulativeStrokes_(race.strokes, barriers, row.t), step);
      row.strokes = Math.max(0, swimRound_(nB - nA, step > 0 ? step : 0));

      var sum = 0, cnt = 0;
      for (var j = 0; j + 1 < race.strokes.length; j++) {
        var a = race.strokes[j], b = race.strokes[j + 1];
        if (a < tPrev - 1e-9) continue;
        if (b > row.t + 1e-9) break;
        if (swimHasBarrier_(barriers, a, b)) continue;
        sum += b - a;
        cnt++;
      }
      if (cnt > 0 && sum > 0) row.srExact = 60 / ((sum / cnt) * row.ppc);
    }

    row.sr = (row.isSwim && row.strokes && row.segTime > 0) ? (row.strokes / row.ppc) * 60 / row.segTime : null;
    row.dps = (row.isSwim && row.strokes) ? (row.d - row.prevD) / row.strokes : null;
  });

  // ---- Εκκίνηση
  var firstWallT = barriers.length ? barriers[0] : Infinity;
  var start = {
    blockTime: swimFirst_(race.feetOff, function (t) { return t > 0 && t < firstWallT; }),
    entryTime: swimFirst_(race.entries, function (t) { return t > 0 && t < firstWallT; }),
    breakout: swimFirst_(race.breakouts, function (t) { return t > 0 && t < firstWallT; }),
    kicks: null
  };
  start.underwaterTime = start.breakout;
  if (race.kicks.length) {
    var kFrom = start.entryTime !== null ? start.entryTime : 0;
    var kTo = start.breakout !== null ? start.breakout : firstWallT;
    start.kicks = race.kicks.filter(function (t) { return t > kFrom && t <= kTo; }).length;
  }

  // ---- Στροφές
  var knownTimes = rows.filter(function (r) { return r.t !== null && r.isWall; }).map(function (r) { return r.t; });
  var turns = turnWalls.map(function (wall) {
    var tw = timeAt[wall] !== undefined ? timeAt[wall] : null;
    var res = { wall: wall, tWall: tw, pushoff: null, breakout: null, turnTime: null, underwaterTime: null, kicks: null, lastStrokeGap: null };
    if (tw === null) return res;
    var nextT = swimFirst_(knownTimes, function (t) { return t > tw + 1e-9; });
    if (nextT === null) nextT = Infinity;
    var prevT = swimLast_(knownTimes, function (t) { return t < tw - 1e-9; });
    if (prevT === null) prevT = 0;

    res.pushoff = swimFirst_(race.feetOff, function (t) { return t > tw && t < nextT; });
    res.breakout = swimFirst_(race.breakouts, function (t) { return t > tw && t < nextT; });
    if (res.pushoff !== null) res.turnTime = res.pushoff - tw;
    if (res.breakout !== null) res.underwaterTime = res.breakout - (res.pushoff !== null ? res.pushoff : tw);
    if (race.kicks.length) {
      var to = res.breakout !== null ? res.breakout : nextT;
      res.kicks = race.kicks.filter(function (t) { return t > tw && t <= to; }).length;
    }
    var ls = swimLast_(race.strokes, function (t) { return t < tw && t > prevT; });
    if (ls !== null) res.lastStrokeGap = tw - ls;
    return res;
  });

  // ---- Τερματισμός
  var finish = { tFinish: timeAt[dist] !== undefined ? timeAt[dist] : null, lastStrokeGap: null };
  if (finish.tFinish !== null) {
    var prevWallT = swimLast_(knownTimes, function (t) { return t < finish.tFinish - 1e-9; });
    var lastStroke = swimLast_(race.strokes, function (t) { return t < finish.tFinish && (prevWallT === null || t > prevWallT); });
    if (lastStroke !== null) finish.lastStrokeGap = finish.tFinish - lastStroke;
  }

  // ---- Σύνοψη
  var totalStrokes = null;
  if (hasStrokes) {
    totalStrokes = 0;
    rows.forEach(function (r) { if (r.strokes) totalStrokes += r.strokes; });
  }
  var srs = rows.filter(function (r) { return r.sr !== null; }).map(function (r) { return r.sr; });
  var summary = {
    finalTime: finish.tFinish,
    avgVel: finish.tFinish ? dist / finish.tFinish : null,
    totalStrokes: totalStrokes,
    avgSr: srs.length ? srs.reduce(function (a, b) { return a + b; }, 0) / srs.length : null,
    marked: rows.filter(function (r) { return r.t !== null; }).length,
    total: rows.length
  };

  return { rows: rows, start: start, turns: turns, finish: finish, summary: summary, warnings: warnings, turnWalls: turnWalls };
}

// ---------------------------------------------------------------------------------------------
// Layout (pure) – περιγραφή του φύλλου: τιμές, τύποι, μορφοποίηση, γραφήματα
// ---------------------------------------------------------------------------------------------

function swimColLetter_(c) {
  var s = '';
  while (c > 0) {
    var m = (c - 1) % 26;
    s = String.fromCharCode(65 + m) + s;
    c = Math.floor((c - 1) / 26);
  }
  return s;
}

function swimA1_(r, c) { return swimColLetter_(c) + r; }

function SwimGrid_() {
  this.cells = {};
  this.maxRow = 0;
  this.maxCol = 0;
  this.merges = [];
  this.boxes = [];
  this.rowHeights = {};
}

SwimGrid_.prototype.set = function (r, c, v, fmt) {
  var key = r + ':' + c;
  var cell = this.cells[key] || { r: r, c: c };
  if (v !== undefined) cell.v = v;
  if (fmt) {
    for (var k in fmt) if (Object.prototype.hasOwnProperty.call(fmt, k)) cell[k] = fmt[k];
  }
  this.cells[key] = cell;
  if (r > this.maxRow) this.maxRow = r;
  if (c > this.maxCol) this.maxCol = c;
  return cell;
};

SwimGrid_.prototype.get = function (r, c) {
  return this.cells[r + ':' + c];
};

SwimGrid_.prototype.merge = function (r, c, r2, c2) {
  this.merges.push([r, c, r2, c2]);
};

SwimGrid_.prototype.box = function (r, c, r2, c2) {
  this.boxes.push([r, c, r2, c2]);
};

SwimGrid_.prototype.section = function (r, text, lastCol) {
  this.set(r, 1, text, { bg: SWIM_COLORS.section, color: SWIM_COLORS.sectionText, bold: true, size: 11 });
  for (var c = 2; c <= lastCol; c++) this.set(r, c, '', { bg: SWIM_COLORS.section });
  this.merge(r, 1, r, lastCol);
};

SwimGrid_.prototype.headers = function (r, labels) {
  var self = this;
  labels.forEach(function (label, i) {
    self.set(r, i + 1, label, { bg: SWIM_COLORS.header, bold: true, wrap: true, align: 'center', valign: 'middle', size: 9 });
  });
  this.rowHeights[r] = 48;
};

var SWIM_FMT = {
  time: '0.00',
  vel: '0.00',
  strokes: '0.0',
  sr: '0.0',
  dps: '0.00',
  pct: '0.0%',
  int: '0'
};

function swimF_(fmt, kind) {
  var base = {};
  if (fmt) base.fmt = fmt;
  if (kind === 'formula') base.bg = SWIM_COLORS.formula;
  else if (kind === 'auto') base.bg = SWIM_COLORS.auto;
  else if (kind === 'input') base.bg = SWIM_COLORS.input;
  else if (kind === 'label') { base.bg = SWIM_COLORS.label; base.bold = true; }
  base.align = 'center';
  return base;
}

function swimLayoutSignature_(race) {
  return JSON.stringify({
    v: 3,
    d: race.checkpoints.map(function (c) { return c.d; }),
    pool: race.pool,
    dist: race.distance,
    style: race.style,
    relay: race.relay
  });
}

function swimBuildLayout_(race, analysis) {
  var g = new SwimGrid_();
  var LAST = 13; // A..M
  var rows = analysis.rows;
  var n = rows.length;
  var dist = race.distance;
  var pool = race.pool;

  var layout = {
    signature: swimLayoutSignature_(race),
    grid: g,
    charts: [],
    inputs: {},
    kpi: {}
  };

  // ---- Τίτλος & στοιχεία αγώνα
  g.set(1, 1, 'Swimming Race Analysis', { bold: true, size: 16, color: SWIM_COLORS.title });
  g.merge(1, 1, 1, 6);
  g.set(1, 9, 'Ανάλυση με Kinovea', { italic: true, color: '#666666', align: 'right' });
  g.merge(1, 9, 1, LAST);
  g.rowHeights[1] = 30;

  var headerLabels = ['Date:', 'Athlete:', 'Event:', 'Distance:', 'Competition:', 'Location:', 'Round:', 'Pool:', 'Club / ID:'];
  var headerKeys = ['date', 'athlete', 'event', 'distance', 'competition', 'location', 'round', 'pool', 'club'];
  layout.inputs.header = [];
  headerLabels.forEach(function (label, i) {
    var r = 2 + i;
    g.set(r, 1, label, { bold: true, bg: SWIM_COLORS.label });
    g.set(r, 2, '', { bg: SWIM_COLORS.input, align: 'left', fmt: '@' });
    g.merge(r, 2, r, 5);
    layout.inputs.header.push({ r: r, c: 2, key: headerKeys[i] });
  });
  g.box(2, 1, 10, 5);

  // ---- Πίνακας χρόνων (θέσεις γραμμών που χρειάζονται οι τύποι KPI)
  var T0 = SWIM_TIMING_FIRST_ROW;
  var rowOf = {};
  rows.forEach(function (row, i) { rowOf[row.d] = T0 + i; });
  var lastRow = T0 + n - 1;
  var rN = rowOf[dist] !== undefined ? rowOf[dist] : lastRow;

  // ---- KPI
  var kpiCol = 7, kpiVal = 10;
  var kpis = [
    ['Τελικός χρόνος (s)', '=IF(B' + rN + '="","",B' + rN + ')', SWIM_FMT.time],
    ['Μέση ταχύτητα (m/s)', '=IF(J2="","",' + dist + '/J2)', SWIM_FMT.vel],
    ['Συνολικές χεριές', '=IF(COUNT(H' + T0 + ':H' + lastRow + ')=0,"",SUM(H' + T0 + ':H' + lastRow + '))', SWIM_FMT.strokes],
    ['Μέση συχνότητα (κύκλοι/λεπτό)', '', SWIM_FMT.sr],
    ['Μέσο μήκος χεριάς (m/χεριά)', '=IFERROR(AVERAGE(J' + T0 + ':J' + lastRow + '),"")', SWIM_FMT.dps],
    ['Μέσος ΔΑΚ', '', SWIM_FMT.vel],
    ['Χρόνος στροφών (% του συνόλου)', '', SWIM_FMT.pct],
    ['Τελευταία ενημέρωση', '', null]
  ];
  kpis.forEach(function (k, i) {
    var r = 2 + i;
    g.set(r, kpiCol, k[0], { bold: true, bg: SWIM_COLORS.kpi });
    g.set(r, kpiCol + 1, '', { bg: SWIM_COLORS.kpi });
    g.set(r, kpiCol + 2, '', { bg: SWIM_COLORS.kpi });
    g.merge(r, kpiCol, r, kpiCol + 2);
    g.set(r, kpiVal, k[1], { bold: true, fmt: k[2], bg: SWIM_COLORS.kpi, align: 'center', size: i === 0 ? 12 : 10 });
    g.set(r, kpiVal + 1, '', { bg: SWIM_COLORS.kpi });
    g.set(r, kpiVal + 2, '', { bg: SWIM_COLORS.kpi });
    g.set(r, kpiVal + 3, '', { bg: SWIM_COLORS.kpi });
    if (i < kpis.length - 1) g.merge(r, kpiVal + 1, r, kpiVal + 3);
  });
  g.set(2, kpiVal + 1, '=IF(J2="","",INT(J2/60)&":"&TEXT(MOD(J2,60),"00.00"))', { bold: true, size: 12, bg: SWIM_COLORS.kpi, align: 'center' });
  g.box(2, kpiCol, 9, LAST);
  layout.kpi.finalTime = { r: 2, c: kpiVal };
  layout.inputs.updated = { r: 9, c: kpiVal };
  g.set(9, kpiVal, '', { bg: SWIM_COLORS.kpi, fmt: '@', align: 'center' });
  g.merge(9, kpiVal, 9, LAST);

  // ---- Εκκίνηση
  g.section(11, 'Εκκίνηση', LAST);
  g.headers(12, [
    'Χρόνος Αντίδρασης (sec)', 'Χρόνος Πάνω στον βατήρα (sec)', 'Χρόνος Πτήσης (sec)',
    'Απόσταση εισόδου (Χέρια) (m)', 'Απόσταση εισόδου (Ισχία) (m)', 'Ταχύτητα εισόδου (Χέρια) (m/s)',
    'Ταχύτητα εισόδου (Ισχία) (m/s)', 'Ταχύτητα εισόδου (Πόδια) (m/s)', 'Χρόνος Κάτω από το νερό (sec)',
    'Υποβρύχιοι Δελφινισμοί', 'Απόσταση κάτω από το νερό (m)', 'Απόσταση Εξόδου από το νερό (κεφάλι) (m)',
    'Ταχύτητα εξόδου από το νερό (m/s)'
  ]);
  var startDefs = [
    ['reaction', 'manual', SWIM_FMT.time],
    ['blockTime', 'auto', SWIM_FMT.time],
    ['entryTime', 'auto', SWIM_FMT.time],
    ['entryDistHands', 'manual', SWIM_FMT.vel],
    ['entryDistHips', 'manual', SWIM_FMT.vel],
    ['entryVelHands', 'manual', SWIM_FMT.vel],
    ['entryVelHips', 'manual', SWIM_FMT.vel],
    ['entryVelFeet', 'manual', SWIM_FMT.vel],
    ['underwaterTime', 'auto', SWIM_FMT.time],
    ['kicks', 'auto', SWIM_FMT.int],
    ['underwaterDist', 'manual', SWIM_FMT.vel],
    ['breakoutDist', 'manual', SWIM_FMT.vel]
  ];
  layout.inputs.start = [];
  startDefs.forEach(function (def, i) {
    g.set(13, i + 1, '', swimF_(def[2], def[1] === 'auto' ? 'auto' : 'input'));
    layout.inputs.start.push({ r: 13, c: i + 1, key: def[0], kind: def[1] });
  });
  g.set(13, 13, '=IF(OR(K13="",I13="",I13=0),"",K13/I13)', swimF_(SWIM_FMT.vel, 'formula'));
  g.box(12, 1, 13, LAST);

  // ---- Χρόνοι & χεριές
  g.section(15, 'Χρόνοι & Χεριές (Timing Data)', LAST + 1);
  g.headers(16, [
    'Distance (m)', 'Time (s)', 'Vel (m/s)', 'Dif Time 25', 'Dif 50', 'Turn time -5m/+15m',
    'Swolf / Total Strokes by 50', 'Χεριές τμήματος', 'StrRate (Cycles/min)', 'Distance Per Stroke (m/stroke)',
    'Χρόνος τμήματος (s)', 'Μέση ταχύτητα από εκκίνηση (m/s)', 'SR από χρόνους χεριών (Cycles/min)', 'Στυλ'
  ]);

  var all25 = true;
  for (var d25 = 25; d25 <= dist + 1e-9; d25 += 25) if (rowOf[d25] === undefined) { all25 = false; break; }

  var prev25 = null, prev50 = null, prev50Row = null;
  rows.forEach(function (row, i) {
    var r = T0 + i;
    var p = r - 1;
    var B = 'B' + r, Bp = 'B' + p;
    g.set(r, 1, row.d, swimF_(null, 'label'));
    g.set(r, 2, '', swimF_(SWIM_FMT.time, 'input'));

    if (i === 0) g.set(r, 3, '=IF(' + B + '="","",A' + r + '/' + B + ')', swimF_(SWIM_FMT.vel, 'formula'));
    else g.set(r, 3, '=IF(OR(' + B + '="",' + Bp + '=""),"",(A' + r + '-A' + p + ')/(' + B + '-' + Bp + '))', swimF_(SWIM_FMT.vel, 'formula'));

    var on25 = Math.abs(row.d / 25 - Math.round(row.d / 25)) < 1e-9;
    if (all25 && on25) {
      if (prev25 === null) g.set(r, 4, '=IF(' + B + '="","",' + B + ')', swimF_(SWIM_FMT.time, 'formula'));
      else g.set(r, 4, '=IF(OR(' + B + '="",B' + prev25 + '=""),"",' + B + '-B' + prev25 + ')', swimF_(SWIM_FMT.time, 'formula'));
      prev25 = r;
    } else {
      g.set(r, 4, '', swimF_(null, 'formula'));
    }

    var on50 = Math.abs(row.d / 50 - Math.round(row.d / 50)) < 1e-9;
    if (on50) {
      if (prev50 === null) g.set(r, 5, '=IF(' + B + '="","",' + B + ')', swimF_(SWIM_FMT.time, 'formula'));
      else g.set(r, 5, '=IF(OR(' + B + '="",B' + prev50 + '=""),"",' + B + '-B' + prev50 + ')', swimF_(SWIM_FMT.time, 'formula'));
    } else {
      g.set(r, 5, '', swimF_(null, 'formula'));
    }

    // Στροφή -5/+15
    var turnFormula = '';
    analysis.turnWalls.forEach(function (wall) {
      if (Math.abs(row.d - (wall + 15)) < 1e-9 && rowOf[wall - 5] !== undefined) {
        var q = rowOf[wall - 5];
        turnFormula = '=IF(OR(' + B + '="",B' + q + '=""),"",' + B + '-B' + q + ')';
      }
    });
    g.set(r, 6, turnFormula, swimF_(SWIM_FMT.time, 'formula'));

    // Swolf / σύνολο χεριών ανά 50
    if (on50) {
      var a = prev50 === null ? T0 : prev50 + 1;
      g.set(r, 7, '=IF(COUNT(H' + a + ':H' + r + ')=0,"",SUM(H' + a + ':H' + r + '))', swimF_(SWIM_FMT.strokes, 'formula'));
      if (p >= a && p >= T0) {
        g.set(p, 7, '=IF(OR(E' + r + '="",G' + r + '=""),"",E' + r + '+G' + r + ')', swimF_(SWIM_FMT.time, 'formula'));
      }
      prev50 = r;
    } else if (!g.get(r, 7)) {
      g.set(r, 7, '', swimF_(null, 'formula'));
    }

    g.set(r, 8, '', swimF_(SWIM_FMT.strokes, 'input'));

    if (row.isSwim && i > 0) {
      g.set(r, 9, '=IF(OR(H' + r + '="",H' + r + '=0,K' + r + '=""),"",IF(OR(N' + r + '="fs",N' + r + '="bk"),H' + r + '/2,H' + r + ')*60/K' + r + ')', swimF_(SWIM_FMT.sr, 'formula'));
      g.set(r, 10, '=IF(OR(H' + r + '="",H' + r + '=0),"",(A' + r + '-A' + p + ')/H' + r + ')', swimF_(SWIM_FMT.dps, 'formula'));
    } else {
      g.set(r, 9, '', swimF_(null, 'formula'));
      g.set(r, 10, '', swimF_(null, 'formula'));
    }

    if (i === 0) g.set(r, 11, '=IF(' + B + '="","",' + B + ')', swimF_(SWIM_FMT.time, 'formula'));
    else g.set(r, 11, '=IF(OR(' + B + '="",' + Bp + '=""),"",' + B + '-' + Bp + ')', swimF_(SWIM_FMT.time, 'formula'));
    g.set(r, 12, '=IF(' + B + '="","",A' + r + '/' + B + ')', swimF_(SWIM_FMT.vel, 'formula'));
    g.set(r, 13, '', swimF_(SWIM_FMT.sr, 'auto'));
    g.set(r, 14, row.style, { color: '#777777', align: 'center', size: 9 });
  });
  g.box(16, 1, lastRow, LAST + 1);
  layout.inputs.times = { col: 2, row: T0, n: n };
  layout.inputs.strokes = { col: 8, row: T0, n: n };
  layout.inputs.srExact = { col: 13, row: T0, n: n };

  var cur = lastRow + 2;

  // ---- Ανά 50μ.
  var fifties = [];
  for (var k50 = 1; k50 * 50 <= dist + 1e-9; k50++) {
    if (rowOf[k50 * 50] !== undefined) fifties.push(k50);
  }
  var p50First = null, p50Last = null;
  if (fifties.length) {
    g.section(cur, 'Ανά 50μ.', LAST);
    g.headers(cur + 1, ['50μ.', 'Χρόνος 50μ. (s)', 'Χρόνος (σωρευτικός)', 'Χεριές', 'StrRate 50μ. (Cycles/min)', 'DPS (m/stroke)',
      'Swolf', 'ΔΑΚ', 'Μέση ταχύτητα (m/s)', 'Διαφορά από προηγ. 50 (s)', 'Χρόνος 100μ. (s)', 'Μ.ό. StrRate τμημάτων', 'Στυλ']);
    var r50 = cur + 2;
    p50First = r50;
    fifties.forEach(function (k, idx) {
      var dEnd = k * 50, dStart = dEnd - 50;
      var re = rowOf[dEnd];
      var rs = dStart === 0 ? null : rowOf[dStart];
      var x = r50 + idx;
      var Be = 'B' + re;
      g.set(x, 1, dStart + '-' + dEnd, swimF_('@', 'label'));
      if (rs === null) g.set(x, 2, '=IF(' + Be + '="","",' + Be + ')', swimF_(SWIM_FMT.time, 'formula'));
      else if (rs !== undefined) g.set(x, 2, '=IF(OR(' + Be + '="",B' + rs + '=""),"",' + Be + '-B' + rs + ')', swimF_(SWIM_FMT.time, 'formula'));
      else g.set(x, 2, '', swimF_(SWIM_FMT.time, 'formula'));
      g.set(x, 3, '=IF(' + Be + '="","",' + Be + ')', swimF_(SWIM_FMT.time, 'formula'));
      var a = rs === null ? T0 : (rs !== undefined ? rs + 1 : null);
      if (a !== null) g.set(x, 4, '=IF(COUNT(H' + a + ':H' + re + ')=0,"",SUM(H' + a + ':H' + re + '))', swimF_(SWIM_FMT.strokes, 'formula'));
      else g.set(x, 4, '', swimF_(null, 'formula'));
      g.set(x, 5, '=IF(OR(D' + x + '="",D' + x + '=0,B' + x + '=""),"",IF(OR(M' + x + '="fs",M' + x + '="bk"),D' + x + '/2,D' + x + ')*60/B' + x + ')', swimF_(SWIM_FMT.sr, 'formula'));
      g.set(x, 6, '=IF(OR(D' + x + '="",D' + x + '=0),"",50/D' + x + ')', swimF_(SWIM_FMT.dps, 'formula'));
      g.set(x, 7, '=IF(OR(B' + x + '="",D' + x + '=""),"",B' + x + '+D' + x + ')', swimF_(SWIM_FMT.time, 'formula'));
      var r15 = rowOf[dStart + 15];
      if (r15 !== undefined && r15 < re) {
        g.set(x, 8, '=IF(OR(' + Be + '="",B' + r15 + '="",COUNT(H' + (r15 + 1) + ':H' + re + ')=0),"",((' + Be + '-B' + r15 + ')+SUM(H' + (r15 + 1) + ':H' + re + '))/3.5)', swimF_(SWIM_FMT.vel, 'formula'));
      } else {
        g.set(x, 8, '', swimF_(null, 'formula'));
      }
      g.set(x, 9, '=IF(OR(B' + x + '="",B' + x + '=0),"",50/B' + x + ')', swimF_(SWIM_FMT.vel, 'formula'));
      if (idx > 0) g.set(x, 10, '=IF(OR(B' + x + '="",B' + (x - 1) + '=""),"",B' + x + '-B' + (x - 1) + ')', swimF_(SWIM_FMT.time, 'formula'));
      else g.set(x, 10, '', swimF_(null, 'formula'));
      if (k % 2 === 0) {
        var rPrev100 = dEnd - 100 === 0 ? null : rowOf[dEnd - 100];
        if (rPrev100 === null) g.set(x, 11, '=IF(' + Be + '="","",' + Be + ')', swimF_(SWIM_FMT.time, 'formula'));
        else if (rPrev100 !== undefined) g.set(x, 11, '=IF(OR(' + Be + '="",B' + rPrev100 + '=""),"",' + Be + '-B' + rPrev100 + ')', swimF_(SWIM_FMT.time, 'formula'));
        else g.set(x, 11, '', swimF_(null, 'formula'));
      } else {
        g.set(x, 11, '', swimF_(null, 'formula'));
      }
      // Μέσος όρος SR μόνο στα τμήματα καθαρής κολύμβησης (π.χ. 15-25, 25-35, 35-45), όπως στο Excel.
      var fFirst = null, fLast = null;
      rows.forEach(function (rw, ri) {
        if (rw.isFree && ri > 0 && rw.prevD >= dStart - 1e-9 && rw.d <= dEnd + 1e-9) {
          if (fFirst === null) fFirst = T0 + ri;
          fLast = T0 + ri;
        }
      });
      if (fFirst !== null) g.set(x, 12, '=IFERROR(AVERAGE(I' + fFirst + ':I' + fLast + '),"")', swimF_(SWIM_FMT.sr, 'formula'));
      else g.set(x, 12, '', swimF_(null, 'formula'));
      g.set(x, 13, swimStyleAt_(race, dEnd), { color: '#777777', align: 'center', size: 9 });
      p50Last = x;
    });
    g.box(cur + 1, 1, p50Last, 13);
    g.set(7, kpiVal, '=IFERROR(AVERAGE(H' + p50First + ':H' + p50Last + '),"")', { bold: true, fmt: SWIM_FMT.vel, bg: SWIM_COLORS.kpi, align: 'center' });
    g.set(5, kpiVal, '=IFERROR(AVERAGE(L' + p50First + ':L' + p50Last + '),"")', { bold: true, fmt: SWIM_FMT.sr, bg: SWIM_COLORS.kpi, align: 'center' });
    cur = p50Last + 2;
  }
  layout.per50 = { first: p50First, last: p50Last };

  // ---- Στροφές
  layout.inputs.turns = [];
  var turnFirst = null, turnLast = null;
  if (analysis.turnWalls.length) {
    g.section(cur, 'Στροφές', LAST);
    g.headers(cur + 1, ['Τοίχος (m)', 'Χρόνος προσέγγισης 5μ. (s)', 'Χρόνος -5/+15μ. (s)', 'Χρόνος στροφής (s)',
      'Μέτρα κάτω από το νερό (m)', 'Χρόνος κάτω από το νερό (s)', 'Δελφινισμοί', 'Μέση ταχύτητα -5/+15μ. (m/s)',
      'Ταχύτητα εξόδου (m/s)', 'Ποσοστό στον τελικό χρόνο', 'Τελευταία χεριά → τοίχος (s)']);
    var rt = cur + 2;
    turnFirst = rt;
    analysis.turnWalls.forEach(function (wall, idx) {
      var x = rt + idx;
      var rw = rowOf[wall], rm5 = rowOf[wall - 5], rp15 = rowOf[wall + 15];
      g.set(x, 1, wall, swimF_(null, 'label'));
      if (rw !== undefined && rm5 !== undefined) g.set(x, 2, '=IF(OR(B' + rw + '="",B' + rm5 + '=""),"",B' + rw + '-B' + rm5 + ')', swimF_(SWIM_FMT.time, 'formula'));
      else g.set(x, 2, '', swimF_(null, 'formula'));
      if (rp15 !== undefined && rm5 !== undefined) g.set(x, 3, '=IF(OR(B' + rp15 + '="",B' + rm5 + '=""),"",B' + rp15 + '-B' + rm5 + ')', swimF_(SWIM_FMT.time, 'formula'));
      else g.set(x, 3, '', swimF_(null, 'formula'));
      g.set(x, 4, '', swimF_(SWIM_FMT.time, 'auto'));
      g.set(x, 5, '', swimF_(SWIM_FMT.vel, 'input'));
      g.set(x, 6, '', swimF_(SWIM_FMT.time, 'auto'));
      g.set(x, 7, '', swimF_(SWIM_FMT.int, 'auto'));
      g.set(x, 8, '=IF(OR(C' + x + '="",C' + x + '=0),"",20/C' + x + ')', swimF_(SWIM_FMT.vel, 'formula'));
      g.set(x, 9, '=IF(OR(E' + x + '="",F' + x + '="",F' + x + '=0),"",E' + x + '/F' + x + ')', swimF_(SWIM_FMT.vel, 'formula'));
      g.set(x, 10, '=IF(OR(C' + x + '="",$J$2=""),"",C' + x + '/$J$2)', swimF_(SWIM_FMT.pct, 'formula'));
      g.set(x, 11, '', swimF_(SWIM_FMT.time, 'auto'));
      layout.inputs.turns.push({ r: x, wall: wall });
      turnLast = x;
    });
    g.box(cur + 1, 1, turnLast, 11);
    cur = turnLast + 2;
  }
  layout.turnRows = { first: turnFirst, last: turnLast };

  // ---- Τερματισμός
  g.section(cur, 'Τερματισμός', LAST);
  g.headers(cur + 1, ['Χρόνος τελευταίων 15μ. (s)', 'Χρόνος τελευταίων 5μ. (s)', 'Χεριές τελευταίων 5μ.',
    'Προσέγγιση τοίχου σε sec (τελευταία χεριά)', 'Προσέγγιση τοίχου σε μήκος μ. (τελευταία χεριά)', 'Μέση ταχύτητα τελευταίων 5μ. (m/s)']);
  var fr = cur + 2;
  var rL15 = rowOf[dist - 15], rL5 = rowOf[dist - 5];
  if (rowOf[dist] !== undefined && rL15 !== undefined) g.set(fr, 1, '=IF(OR(B' + rN + '="",B' + rL15 + '=""),"",B' + rN + '-B' + rL15 + ')', swimF_(SWIM_FMT.time, 'formula'));
  else g.set(fr, 1, '', swimF_(null, 'formula'));
  if (rowOf[dist] !== undefined && rL5 !== undefined) {
    g.set(fr, 2, '=IF(OR(B' + rN + '="",B' + rL5 + '=""),"",B' + rN + '-B' + rL5 + ')', swimF_(SWIM_FMT.time, 'formula'));
    if (rL5 === rN - 1) g.set(fr, 3, '=IF(H' + rN + '="","",H' + rN + ')', swimF_(SWIM_FMT.strokes, 'formula'));
    else g.set(fr, 3, '', swimF_(null, 'formula'));
  } else {
    g.set(fr, 2, '', swimF_(null, 'formula'));
    g.set(fr, 3, '', swimF_(null, 'formula'));
  }
  g.set(fr, 4, '', swimF_(SWIM_FMT.time, 'auto'));
  g.set(fr, 5, '', swimF_(SWIM_FMT.vel, 'input'));
  g.set(fr, 6, '=IF(OR(B' + fr + '="",B' + fr + '=0),"",5/B' + fr + ')', swimF_(SWIM_FMT.vel, 'formula'));
  g.box(cur + 1, 1, fr, 6);
  layout.inputs.finish = { r: fr };
  cur = fr + 2;

  // ---- Ειδικοί τομείς (καθαρή κολύμβηση)
  var freeRows = [];
  rows.forEach(function (row, i) { if (row.isFree && i > 0) freeRows.push(T0 + i); });
  if (freeRows.length) {
    g.section(cur, 'Ειδικοί τομείς (καθαρή κολύμβηση)', LAST);
    g.headers(cur + 1, ['Τμήμα', 'Χρόνος (s)', 'Ταχύτητα (m/s)', 'Μεταβολή ταχύτητας (m/s)', 'Ποσοστό του συνολικού χρόνου', 'Χεριές', 'StrRate (Cycles/min)', 'DPS (m/stroke)']);
    freeRows.forEach(function (tr, idx) {
      var x = cur + 2 + idx;
      var row = rows[tr - T0];
      g.set(x, 1, row.prevD + '-' + row.d, swimF_('@', 'label'));
      g.set(x, 2, '=IF(K' + tr + '="","",K' + tr + ')', swimF_(SWIM_FMT.time, 'formula'));
      g.set(x, 3, '=IF(C' + tr + '="","",C' + tr + ')', swimF_(SWIM_FMT.vel, 'formula'));
      g.set(x, 4, '=IF(OR(C' + tr + '="",C' + (tr - 1) + '=""),"",C' + tr + '-C' + (tr - 1) + ')', swimF_('0.00;-0.00', 'formula'));
      g.set(x, 5, '=IF(OR(K' + tr + '="",$J$2=""),"",K' + tr + '/$J$2)', swimF_(SWIM_FMT.pct, 'formula'));
      g.set(x, 6, '=IF(H' + tr + '="","",H' + tr + ')', swimF_(SWIM_FMT.strokes, 'formula'));
      g.set(x, 7, '=IF(I' + tr + '="","",I' + tr + ')', swimF_(SWIM_FMT.sr, 'formula'));
      g.set(x, 8, '=IF(J' + tr + '="","",J' + tr + ')', swimF_(SWIM_FMT.dps, 'formula'));
    });
    var freeLast = cur + 1 + freeRows.length;
    g.box(cur + 1, 1, freeLast, 8);
    layout.free = { first: cur + 2, last: freeLast };
    cur = freeLast + 2;
  }

  // ---- Σύνθεση αγώνα
  g.section(cur, 'Σύνθεση αγώνα (ποσοστό του συνολικού χρόνου)', LAST);
  g.headers(cur + 1, ['Φάση', 'Χρόνος (s)', 'Ποσοστό']);
  g.set(cur + 1, 5, 'Εκκίνηση: 0-15μ. · Στροφές: -5/+15μ. · Τερματισμός: τελευταία 5μ.', { italic: true, color: '#555555', size: 9 });
  g.merge(cur + 1, 5, cur + 1, LAST);
  var c1 = cur + 2;
  var r15first = rowOf[15];
  g.set(c1, 1, 'Εκκίνηση', swimF_(null, 'label'));
  g.set(c1, 2, r15first !== undefined ? '=IF(B' + r15first + '="","",B' + r15first + ')' : '', swimF_(SWIM_FMT.time, 'formula'));
  g.set(c1 + 1, 1, 'Στροφές', swimF_(null, 'label'));
  g.set(c1 + 1, 2, turnFirst !== null ? '=IF(COUNT(C' + turnFirst + ':C' + turnLast + ')=0,"",SUM(C' + turnFirst + ':C' + turnLast + '))' : '', swimF_(SWIM_FMT.time, 'formula'));
  g.set(c1 + 2, 1, 'Τερματισμός', swimF_(null, 'label'));
  g.set(c1 + 2, 2, '=IF(B' + fr + '="","",B' + fr + ')', swimF_(SWIM_FMT.time, 'formula'));
  g.set(c1 + 3, 1, 'Κολύμβηση', swimF_(null, 'label'));
  g.set(c1 + 3, 2, '=IF($J$2="","",$J$2-SUM(B' + c1 + ':B' + (c1 + 2) + '))', swimF_(SWIM_FMT.time, 'formula'));
  for (var ci = 0; ci < 4; ci++) {
    g.set(c1 + ci, 3, '=IF(OR(B' + (c1 + ci) + '="",$J$2=""),"",B' + (c1 + ci) + '/$J$2)', swimF_(SWIM_FMT.pct, 'formula'));
  }
  g.box(cur + 1, 1, c1 + 3, 3);
  if (turnFirst !== null) g.set(8, kpiVal, '=IF(C' + (c1 + 1) + '="","",C' + (c1 + 1) + ')', { bold: true, fmt: SWIM_FMT.pct, bg: SWIM_COLORS.kpi, align: 'center' });
  layout.composition = { first: c1, last: c1 + 3 };

  // Σημειώσεις δίπλα στη σύνθεση
  var notes = [
    'Ο Δείκτης Απόδοσης Κολύμβησης (ΔΑΚ) = (Χεριές + Χρόνος 15-50μ.) / 3,5, για κάθε 50μ.',
    'StrRate: ελεύθερο/ύπτιο χεριές/2, πεταλούδα/πρόσθιο χεριές×1 (κύκλοι/λεπτό).',
    'SR από χρόνους χεριών: από τα διαστήματα ανάμεσα στα πατήματα χεριάς μέσα στο τμήμα.',
    'Λευκά κελιά: πληκτρολόγηση · Γαλάζια: αυτόματα από το Kinovea · Γκρι: τύποι.'
  ];
  notes.forEach(function (t, i) {
    g.set(c1 + i, 5, t, { italic: true, color: '#555555', size: 9 });
    g.merge(c1 + i, 5, c1 + i, LAST);
  });
  cur = c1 + 5;

  // ---- Γραφήματα
  g.section(cur, 'Γραφήματα', LAST);
  var chartRow = cur + 1;
  layout.charts.push({
    type: 'line', title: 'Ταχύτητα ανά τμήμα (m/s)',
    ranges: ['A' + T0 + ':A' + lastRow, 'C' + T0 + ':C' + lastRow],
    row: chartRow, col: 1, width: 470, height: 260, vAxis: 'm/s'
  });
  layout.charts.push({
    type: 'line', title: 'Συχνότητα (κύκλοι/λεπτό) & μήκος χεριάς (m)',
    ranges: ['A' + T0 + ':A' + lastRow, 'I' + T0 + ':I' + lastRow, 'J' + T0 + ':J' + lastRow],
    row: chartRow, col: 8, width: 470, height: 260, secondAxisSeries: 1
  });
  if (p50First !== null) {
    layout.charts.push({
      type: 'column', title: 'Χρόνος ανά 50μ. (s)',
      ranges: ['A' + p50First + ':A' + p50Last, 'B' + p50First + ':B' + p50Last],
      row: chartRow + 14, col: 1, width: 470, height: 260
    });
  }
  layout.charts.push({
    type: 'pie', title: 'Σύνθεση αγώνα',
    ranges: ['A' + c1 + ':A' + (c1 + 3), 'B' + c1 + ':B' + (c1 + 3)],
    row: chartRow + 14, col: 8, width: 470, height: 260
  });
  layout.lastRow = chartRow + 28;
  layout.lastCol = LAST + 1;
  return layout;
}

// ---------------------------------------------------------------------------------------------
// Τιμές εισόδου (pure): ποια κελιά γράφουμε σε κάθε ενημέρωση
// ---------------------------------------------------------------------------------------------

function swimValueOrBlank_(v, digits) {
  if (v === null || v === undefined || !isFinite(v)) return '';
  return swimFixed_(v, digits);
}

function swimHeaderValues_(race) {
  var eventLabel = SWIM_STYLE_LABELS[race.style] || race.style;
  if (race.relay) eventLabel += ' (relay)';
  return {
    date: race.date,
    athlete: race.athlete + (race.lane ? '  (Διάδρομος ' + race.lane + ')' : ''),
    event: eventLabel,
    distance: race.distanceLabel,
    competition: race.competition,
    location: race.location,
    round: race.round,
    pool: race.pool + 'μ.',
    club: [race.club, race.athleteId ? 'Μητρώο ' + race.athleteId : '', race.category, race.birthYear]
      .filter(function (x) { return x; }).join(' · ')
  };
}

/**
 * Επιστρέφει { columns: [{col,row,values,always}], patches: [{r,c,v}] } – τα patches γράφονται μόνο
 * όταν η τιμή δεν είναι null (ώστε να μη σβήνονται όσα πληκτρολογήθηκαν απευθείας στο φύλλο).
 */
function swimInputPlan_(race, analysis, layout) {
  var plan = { columns: [], patches: [] };
  var rows = analysis.rows;

  plan.columns.push({ col: layout.inputs.times.col, row: layout.inputs.times.row, values: rows.map(function (r) { return swimValueOrBlank_(r.t, 3); }) });
  if (race.strokes.length) {
    plan.columns.push({ col: layout.inputs.strokes.col, row: layout.inputs.strokes.row, values: rows.map(function (r) { return swimValueOrBlank_(r.strokes, 2); }) });
    plan.columns.push({ col: layout.inputs.srExact.col, row: layout.inputs.srExact.row, values: rows.map(function (r) { return swimValueOrBlank_(r.srExact, 1); }) });
  }

  var hv = swimHeaderValues_(race);
  layout.inputs.header.forEach(function (h) {
    var v = hv[h.key];
    if (v !== null && v !== undefined && String(v) !== '') plan.patches.push({ r: h.r, c: h.c, v: v });
  });

  var m = race.manual || {};
  var startAuto = {
    blockTime: analysis.start.blockTime,
    entryTime: analysis.start.entryTime,
    underwaterTime: analysis.start.underwaterTime,
    kicks: analysis.start.kicks
  };
  layout.inputs.start.forEach(function (s) {
    var v = s.kind === 'auto' ? startAuto[s.key] : swimNum_(m[s.key]);
    if (s.kind === 'auto' && swimNum_(m[s.key]) !== null) v = swimNum_(m[s.key]); // χειροκίνητη τιμή υπερισχύει
    if (v !== null && v !== undefined) plan.patches.push({ r: s.r, c: s.c, v: swimFixed_(v, 3) });
  });

  var manualTurns = m.turns || {};
  layout.inputs.turns.forEach(function (t) {
    var a = null;
    analysis.turns.forEach(function (x) { if (x.wall === t.wall) a = x; });
    var mt = manualTurns[String(t.wall)] || {};
    var turnTime = swimNum_(mt.turnTime) !== null ? swimNum_(mt.turnTime) : (a ? a.turnTime : null);
    var uwDist = swimNum_(mt.underwaterDist);
    var uwTime = swimNum_(mt.underwaterTime) !== null ? swimNum_(mt.underwaterTime) : (a ? a.underwaterTime : null);
    var kicks = swimNum_(mt.kicks) !== null ? swimNum_(mt.kicks) : (a ? a.kicks : null);
    var gap = a ? a.lastStrokeGap : null;
    if (turnTime !== null) plan.patches.push({ r: t.r, c: 4, v: swimFixed_(turnTime, 3) });
    if (uwDist !== null) plan.patches.push({ r: t.r, c: 5, v: uwDist });
    if (uwTime !== null) plan.patches.push({ r: t.r, c: 6, v: swimFixed_(uwTime, 3) });
    if (kicks !== null) plan.patches.push({ r: t.r, c: 7, v: kicks });
    if (gap !== null) plan.patches.push({ r: t.r, c: 11, v: swimFixed_(gap, 3) });
  });

  var fr = layout.inputs.finish.r;
  var gapF = swimNum_(m.finishLastStrokeTime) !== null ? swimNum_(m.finishLastStrokeTime) : analysis.finish.lastStrokeGap;
  if (gapF !== null) plan.patches.push({ r: fr, c: 4, v: swimFixed_(gapF, 3) });
  if (swimNum_(m.finishLastStrokeDist) !== null) plan.patches.push({ r: fr, c: 5, v: swimNum_(m.finishLastStrokeDist) });

  return plan;
}

// ---------------------------------------------------------------------------------------------
// Rendering (Apps Script)
// ---------------------------------------------------------------------------------------------

function swimRenderLayout_(sheet, layout) {
  var g = layout.grid;
  sheet.getCharts().forEach(function (ch) { sheet.removeChart(ch); });
  sheet.clear();
  sheet.getRange(1, 1, sheet.getMaxRows(), sheet.getMaxColumns()).breakApart();

  var nr = Math.max(g.maxRow, layout.lastRow);
  var nc = Math.max(g.maxCol, layout.lastCol);
  if (sheet.getMaxRows() < nr) sheet.insertRowsAfter(sheet.getMaxRows(), nr - sheet.getMaxRows());
  if (sheet.getMaxColumns() < nc) sheet.insertColumnsAfter(sheet.getMaxColumns(), nc - sheet.getMaxColumns());

  var values = [], byFormat = {}, bgs = [], weights = [], styles = [], colors = [], sizes = [], aligns = [], wraps = [];
  for (var r = 1; r <= g.maxRow; r++) {
    var vr = [], br = [], wr = [], sr = [], cr = [], zr = [], ar = [], wrp = [];
    for (var c = 1; c <= nc; c++) {
      var cell = g.get(r, c) || {};
      vr.push(cell.v === undefined || cell.v === null ? '' : cell.v);
      if (cell.fmt) (byFormat[cell.fmt] = byFormat[cell.fmt] || []).push(swimA1_(r, c));
      br.push(cell.bg || '#ffffff');
      wr.push(cell.bold ? 'bold' : 'normal');
      sr.push(cell.italic ? 'italic' : 'normal');
      cr.push(cell.color || '#000000');
      zr.push(cell.size || 10);
      ar.push(cell.align || 'left');
      wrp.push(!!cell.wrap);
    }
    values.push(vr); bgs.push(br); weights.push(wr); styles.push(sr); colors.push(cr); sizes.push(zr); aligns.push(ar); wraps.push(wrp);
  }

  // Μορφές αριθμών πριν από τις τιμές (π.χ. "15-25" ως κείμενο, όχι ημερομηνία).
  Object.keys(byFormat).forEach(function (fmt) {
    sheet.getRangeList(byFormat[fmt]).setNumberFormat(fmt);
  });
  var range = sheet.getRange(1, 1, g.maxRow, nc);
  range.setValues(values);
  range.setBackgrounds(bgs);
  range.setFontWeights(weights);
  range.setFontStyles(styles);
  range.setFontColors(colors);
  range.setFontSizes(sizes);
  range.setHorizontalAlignments(aligns);
  range.setWraps(wraps);
  range.setVerticalAlignment('middle');
  range.setFontFamily('Arial');

  g.merges.forEach(function (m) {
    sheet.getRange(m[0], m[1], m[2] - m[0] + 1, m[3] - m[1] + 1).merge();
  });
  g.boxes.forEach(function (b) {
    sheet.getRange(b[0], b[1], b[2] - b[0] + 1, b[3] - b[1] + 1)
      .setBorder(true, true, true, true, true, true, SWIM_COLORS.border, SpreadsheetApp.BorderStyle.SOLID);
  });
  for (var hr in g.rowHeights) {
    if (Object.prototype.hasOwnProperty.call(g.rowHeights, hr)) sheet.setRowHeight(Number(hr), g.rowHeights[hr]);
  }

  sheet.setColumnWidth(1, 92);
  for (var cc = 2; cc <= 13; cc++) sheet.setColumnWidth(cc, 84);
  sheet.setColumnWidth(14, 46);
  sheet.setHiddenGridlines(true);
  sheet.setFrozenRows(0);

  layout.charts.forEach(function (spec) { swimInsertChart_(sheet, spec); });
}

function swimInsertChart_(sheet, spec) {
  var builder = sheet.newChart();
  if (spec.type === 'line') builder = builder.asLineChart();
  else if (spec.type === 'column') builder = builder.asColumnChart();
  else if (spec.type === 'pie') builder = builder.asPieChart();
  spec.ranges.forEach(function (a1) { builder.addRange(sheet.getRange(a1)); });
  builder
    .setOption('title', spec.title)
    .setOption('width', spec.width)
    .setOption('height', spec.height)
    .setOption('legend', { position: spec.type === 'pie' ? 'right' : 'bottom' })
    .setOption('useFirstColumnAsDomain', true)
    .setPosition(spec.row, spec.col, 0, 0);
  if (spec.type === 'line') {
    builder.setOption('pointSize', 5).setOption('curveType', 'none').setOption('interpolateNulls', true);
  }
  if (spec.secondAxisSeries !== undefined) {
    var series = {};
    series[spec.secondAxisSeries] = { targetAxisIndex: 1 };
    builder.setOption('series', series);
    builder.setOption('vAxes', { 0: { title: 'κύκλοι/λεπτό' }, 1: { title: 'm/χεριά' } });
  }
  sheet.insertChart(builder.build());
}

function swimWriteInputs_(sheet, race, analysis, layout) {
  var plan = swimInputPlan_(race, analysis, layout);
  plan.columns.forEach(function (col) {
    if (!col.values.length) return;
    sheet.getRange(col.row, col.col, col.values.length, 1).setValues(col.values.map(function (v) { return [v]; }));
  });

  // Ομαδοποίηση των patches ανά γραμμή σε συνεχόμενα τμήματα: getValues -> αλλαγές -> setValues
  var byRow = {};
  plan.patches.forEach(function (p) { (byRow[p.r] = byRow[p.r] || []).push(p); });
  Object.keys(byRow).forEach(function (rk) {
    var list = byRow[rk].sort(function (a, b) { return a.c - b.c; });
    var r = Number(rk);
    var c0 = list[0].c, c1 = list[list.length - 1].c;
    var rng = sheet.getRange(r, c0, 1, c1 - c0 + 1);
    var formulas = rng.getFormulas()[0];
    var vals = rng.getValues()[0];
    var out = vals.map(function (v, i) { return formulas[i] ? formulas[i] : v; });
    list.forEach(function (p) { out[p.c - c0] = p.v; });
    rng.setValues([out]);
  });

  sheet.getRange(layout.inputs.updated.r, layout.inputs.updated.c)
    .setValue(Utilities.formatDate(new Date(), Session.getScriptTimeZone(), 'dd/MM/yyyy HH:mm:ss'));
}

// ---------------------------------------------------------------------------------------------
// Elite αναφορές: χρόνοι σύγκρισης για το Δ στα περάσματα (στο spreadsheet των αναφορών)
//
// Μορφή: μία γραμμή ανά σημείο ελέγχου. Το όνομα (και τα στοιχεία του αγωνίσματος) γράφονται μόνο
// στην πρώτη γραμμή κάθε αναφοράς· οι επόμενες γραμμές με κενό όνομα ανήκουν στην ίδια αναφορά.
// Έτσι επικολλάς κατευθείαν τις στήλες Distance / Time από το Excel ανάλυσης.
// ---------------------------------------------------------------------------------------------

var SWIM_REFERENCES_SHEET = 'Elite αναφορές';
var SWIM_REF_HEADERS = ['Αναφορά', 'Φύλο', 'Στυλ', 'Απόσταση', 'Πισίνα', 'Πηγή / σημειώσεις', 'Σημείο (μ.)', 'Χρόνος (s)', 'Race ID'];
var SWIM_REF = { name: 0, gender: 1, style: 2, distance: 3, pool: 4, source: 5, d: 6, t: 7, raceId: 8 };
var SWIM_REF_NOTES = [
  'Όνομα της αναφοράς (π.χ. "WR 2024 – Pan Zhanle" ή "EJC 2025 Final – 1ος"). Γράφεται μόνο στην πρώτη γραμμή: ' +
    'οι επόμενες γραμμές με κενό όνομα είναι τα υπόλοιπα περάσματα της ίδιας αναφοράς.',
  'Ανδρών / Γυναικών (ή κενό). Αν ο αθλητής έχει φύλο, το Kinovea δείχνει μόνο τις αναφορές του ίδιου φύλου.',
  'fs, bk, br, fly, im (ή ελεύθερο, ύπτιο, πρόσθιο, πεταλούδα, μικτή).',
  'Απόσταση του αγωνίσματος: 50, 100, 200, 400, 800, 1500 ή 4x100, 4x200 για σκυταλοδρομίες.',
  '50 ή 25 (μέτρα πισίνας). Κενό = 50.',
  'Ελεύθερο κείμενο (διοργάνωση, ημερομηνία, πηγή των χρόνων).',
  'Απόσταση του περάσματος σε μέτρα (15, 25, 35, 45, 50, 100…). Δεν χρειάζονται όλα τα σημεία: το Δ βγαίνει όπου υπάρχει χρόνος.',
  'Χρόνος από το σήμα εκκίνησης: 26.12 ή 1:02.35.',
  'Συμπληρώνεται αυτόματα όταν η αναφορά προέρχεται από ανάλυση του Kinovea.'
];

function swimReferencesSheet_(ss) {
  var sh = ss.getSheetByName(SWIM_REFERENCES_SHEET);
  if (sh) return sh;
  sh = ss.insertSheet(SWIM_REFERENCES_SHEET);
  var header = sh.getRange(1, 1, 1, SWIM_REF_HEADERS.length);
  header.setValues([SWIM_REF_HEADERS]).setFontWeight('bold').setBackground(SWIM_COLORS.header);
  for (var i = 0; i < SWIM_REF_HEADERS.length; i++) sh.getRange(1, i + 1).setNote(SWIM_REF_NOTES[i]);
  sh.setFrozenRows(1);
  // Κείμενο στη στήλη του χρόνου, ώστε το 1:02.35 να μένει όπως γράφεται.
  sh.getRange(2, SWIM_REF.t + 1, Math.max(1, sh.getMaxRows() - 1), 1).setNumberFormat('@');
  sh.getRange(2, SWIM_REF.distance + 1, Math.max(1, sh.getMaxRows() - 1), 1).setNumberFormat('@');
  [260, 90, 60, 80, 60, 260, 90, 90, 120].forEach(function (w, i) { sh.setColumnWidth(i + 1, w); });
  return sh;
}

function swimStyleCode_(v) {
  var s = swimNormText_(v);
  if (!s) return null;
  var map = [
    ['IM', 'im'], ['MEDLEY', 'im'], ['ΜΙΚΤ', 'im'], ['ΑΤΟΜΙΚΗ ΜΙΚΤΗ', 'im'],
    ['FS', 'fs'], ['FREE', 'fs'], ['ΕΛΕΥΘ', 'fs'], ['ΕΛ', 'fs'],
    ['BK', 'bk'], ['BACK', 'bk'], ['ΥΠΤ', 'bk'], ['ΥΠ', 'bk'],
    ['BR', 'br'], ['BREAST', 'br'], ['ΠΡΟΣΘ', 'br'], ['ΠΡ', 'br'],
    ['FLY', 'fly'], ['BUTTER', 'fly'], ['ΠΕΤΑΛ', 'fly'], ['ΠΕΤ', 'fly']
  ];
  for (var i = 0; i < map.length; i++) {
    if (s === map[i][0] || s.indexOf(map[i][0]) === 0) return map[i][1];
  }
  return null;
}

/** "100" → {100, false}, "4x100" → {400, true}. */
function swimDistanceValue_(v) {
  var s = String(v === null || v === undefined ? '' : v).trim().toLowerCase().replace(/\s+/g, '');
  var m = s.match(/^(\d+)[x×χ*](\d+)/);
  if (m) return { distance: Number(m[1]) * Number(m[2]), relay: true };
  m = s.match(/^(\d+)/);
  return { distance: m ? Number(m[1]) : 0, relay: false };
}

function swimPoolValue_(v) {
  var s = swimNormText_(v);
  if (!s) return 50;
  if (s.indexOf('SC') === 0 || s.indexOf('25') === 0) return 25;
  return 50;
}

/** Χρόνος σε δευτερόλεπτα από τιμή κελιού: αριθμός, "26,12", "1:02.35", ή ημερομηνία/ώρα (μέσω του κειμένου του κελιού). */
function swimRefTime_(value, display) {
  if (typeof value === 'number') return value > 0 ? value : null;
  var s = String(value instanceof Date ? display : (value === null || value === undefined ? '' : value)).trim().replace(/,/g, '.');
  if (!s) return null;
  var parts = s.split(':');
  var total = 0;
  for (var i = 0; i < parts.length; i++) {
    var n = Number(parts[i]);
    if (!isFinite(n) || parts[i] === '') return null;
    total = total * 60 + n;
  }
  return total > 0 ? total : null;
}

/** Όλες οι αναφορές του φύλλου: { references: [...], skipped: ["γραμμή 7: ..."] }. */
function swimReferences_() {
  var sh = swimReferencesSheet_(swimReportsSpreadsheet_());
  var last = sh.getLastRow();
  var out = { references: [], skipped: [] };
  if (last < 2) return out;

  var width = SWIM_REF_HEADERS.length;
  var range = sh.getRange(2, 1, last - 1, width);
  var values = range.getValues();
  var display = range.getDisplayValues();
  var current = null, ids = {};

  for (var i = 0; i < values.length; i++) {
    var row = values[i], rowNumber = i + 2;
    var name = String(row[SWIM_REF.name] === null ? '' : row[SWIM_REF.name]).trim();
    if (name) {
      var style = swimStyleCode_(row[SWIM_REF.style]);
      var dist = swimDistanceValue_(row[SWIM_REF.distance]);
      current = null;
      if (!style) { out.skipped.push('γραμμή ' + rowNumber + ': άγνωστο στυλ "' + row[SWIM_REF.style] + '"'); continue; }
      if (!dist.distance) { out.skipped.push('γραμμή ' + rowNumber + ': λείπει η απόσταση'); continue; }
      current = {
        id: '', name: name, gender: String(row[SWIM_REF.gender] || '').trim(), style: style,
        distance: dist.distance, relay: dist.relay, pool: swimPoolValue_(row[SWIM_REF.pool]),
        source: String(row[SWIM_REF.source] || '').trim(), raceId: String(row[SWIM_REF.raceId] || '').trim(), times: []
      };
      var id = current.raceId || ['sheet', name, style, (dist.relay ? '4x' + dist.distance / 4 : dist.distance), current.pool, current.gender].join('|');
      if (ids[id]) id += '#' + rowNumber;
      ids[id] = true;
      current.id = id;
      out.references.push(current);
    }
    if (!current) continue;

    var dRaw = row[SWIM_REF.d], tRaw = row[SWIM_REF.t];
    if ((dRaw === '' || dRaw === null) && (tRaw === '' || tRaw === null)) continue;
    var d = swimNum_(typeof dRaw === 'string' ? dRaw.replace(',', '.') : dRaw);
    var t = swimRefTime_(tRaw, display[i][SWIM_REF.t]);
    if (d === null || d <= 0) continue; // π.χ. η γραμμή 0 (χρόνος αντίδρασης) του Excel
    if (t === null) { out.skipped.push('γραμμή ' + rowNumber + ': μη έγκυρος χρόνος "' + display[i][SWIM_REF.t] + '"'); continue; }
    current.times.push({ d: d, t: Math.round(t * 1000) / 1000 });
  }

  out.references = out.references.filter(function (r) {
    var seen = {};
    r.times = r.times.filter(function (p) { if (seen[p.d]) return false; seen[p.d] = true; return true; })
      .sort(function (a, b) { return a.d - b.d; });
    return r.times.length > 0;
  });
  return out;
}

function swimDeleteReferenceBlock_(sh, raceId) {
  var last = sh.getLastRow();
  if (!raceId || last < 2) return 0;
  var values = sh.getRange(2, 1, last - 1, SWIM_REF_HEADERS.length).getValues();
  for (var i = 0; i < values.length; i++) {
    if (String(values[i][SWIM_REF.raceId]).trim() !== raceId) continue;
    var count = 1;
    while (i + count < values.length && !String(values[i + count][SWIM_REF.name]).trim()) count++;
    sh.deleteRows(i + 2, count);
    return count;
  }
  return 0;
}

/** Προσθέτει (ή αντικαθιστά, για το ίδιο Race ID) μία αναφορά. */
function swimSaveReference_(raw) {
  var ref = raw || {};
  var name = String(ref.name || '').trim() || 'Αναφορά';
  var style = swimStyleCode_(ref.style);
  var distance = swimNum_(ref.distance) || 0;
  if (!style || !distance) throw new Error('Η αναφορά χρειάζεται στυλ και απόσταση.');
  var relay = !!ref.relay;
  var seen = {};
  var times = (ref.times || []).map(function (p) { return { d: swimNum_(p && p.d), t: swimNum_(p && p.t) }; })
    .filter(function (p) { if (p.d === null || p.d <= 0 || p.t === null || p.t <= 0 || seen[p.d]) return false; seen[p.d] = true; return true; })
    .sort(function (a, b) { return a.d - b.d; });
  if (!times.length) throw new Error('Η αναφορά δεν έχει χρόνους.');

  var raceId = String(ref.raceId || '').trim();
  var sh = swimReferencesSheet_(swimReportsSpreadsheet_());
  swimDeleteReferenceBlock_(sh, raceId);

  var distanceLabel = relay ? '4x' + distance / 4 : String(distance);
  var rows = times.map(function (p, i) {
    var t = String(Math.round(p.t * 100) / 100);
    return i === 0
      ? [name, String(ref.gender || ''), style, distanceLabel, swimNum_(ref.pool) || 50, String(ref.source || ''), p.d, t, raceId]
      : ['', '', '', '', '', '', p.d, t, ''];
  });
  var start = sh.getLastRow() + 1;
  var needed = start + rows.length - 1 - sh.getMaxRows();
  if (needed > 0) sh.insertRowsAfter(sh.getMaxRows(), needed);
  sh.getRange(start, SWIM_REF.distance + 1, rows.length, 1).setNumberFormat('@');
  sh.getRange(start, SWIM_REF.t + 1, rows.length, 1).setNumberFormat('@');
  sh.getRange(start, 1, rows.length, SWIM_REF_HEADERS.length).setValues(rows);
  sh.getRange(start, 1, 1, SWIM_REF_HEADERS.length).setFontWeight('bold');
  return { ok: true, version: SWIM_VERSION, name: name, count: rows.length, row: start, sheet: SWIM_REFERENCES_SHEET };
}

/** Αναφορά από μια αποθηκευμένη ανάλυση (για το μενού). */
function swimReferenceFromRace_(race, name) {
  var label = name || [race.athlete, [race.competition, race.round].filter(function (x) { return x; }).join(' ')]
    .filter(function (x) { return x; }).join(' – ') || 'Αναφορά';
  return {
    raceId: race.id, name: label, gender: race.gender, style: race.style, distance: race.distance, relay: race.relay, pool: race.pool,
    source: [race.competition, race.round, race.date].filter(function (x) { return x; }).join(' · '),
    times: race.checkpoints.filter(function (cp) { return cp.t !== null && cp.t > 0; }).map(function (cp) { return { d: cp.d, t: cp.t }; })
  };
}

function swimReferenceSelected() {
  var ui = SpreadsheetApp.getUi();
  var id = swimSelectedRaceId_();
  var stored = id ? swimStoredRace_(id) : null;
  if (!stored) { ui.alert('Επίλεξε μια γραμμή στο φύλλο "' + SWIM_SUMMARY_SHEET + '" (ή άνοιξε το φύλλο της ανάλυσης) και ξαναδοκίμασε.'); return; }
  var race = swimNormalizeRace_(stored.race);
  var ref = swimReferenceFromRace_(race);
  if (!ref.times.length) { ui.alert('Η ανάλυση δεν έχει χρόνους περασμάτων.'); return; }
  var res = swimSaveReference_(ref);
  ui.alert('Η ανάλυση μπήκε στο φύλλο "' + SWIM_REFERENCES_SHEET + '" ως "' + res.name + '" (' + res.count + ' περάσματα).\n' +
    'Στο Kinovea: καρτέλα Κολύμβηση → ↻ για να εμφανιστεί στη σύγκριση.');
}

function swimOpenReferences() {
  var ss = swimReportsSpreadsheet_();
  var sh = swimReferencesSheet_(ss);
  var url = ss.getUrl() + '#gid=' + sh.getSheetId();
  var html = '<div style="font-family:Arial;font-size:13px;line-height:1.5">' +
    '<p><a href="' + url + '" target="_blank">Άνοιγμα του φύλλου "' + SWIM_REFERENCES_SHEET + '"</a></p>' +
    '<p>Μία γραμμή ανά πέρασμα. Όνομα, φύλο, στυλ, απόσταση και πισίνα μόνο στην πρώτη γραμμή κάθε αναφοράς.' +
    ' Μπορείς να επικολλήσεις τις στήλες <b>Distance</b> και <b>Time</b> από το Excel ανάλυσης στις στήλες "Σημείο" και "Χρόνος".</p>' +
    '<p style="color:#666">Το Kinovea δείχνει το Δ σε κάθε πέρασμα όπου η αναφορά έχει χρόνο (π.χ. μόνο στα 50άρια, αν έχεις επίσημα περάσματα).</p></div>';
  SpreadsheetApp.getUi().showModalDialog(HtmlService.createHtmlOutput(html).setWidth(480).setHeight(220), 'Elite αναφορές');
}

// ---------------------------------------------------------------------------------------------
// Ευρετήριο αγώνων
// ---------------------------------------------------------------------------------------------

var SWIM_INDEX_HEADERS = ['Race ID', 'Φύλλο', 'Αθλητής', 'Μητρώο', 'Αγώνισμα', 'Απόσταση', 'Φάση', 'Διοργάνωση', 'Ημερομηνία', 'Τελικός χρόνος', 'Ενημέρωση', 'Βίντεο', 'SheetId', 'Layout', 'JSON'];
var SWIM_IDX = { id: 0, link: 1, sheetId: 12, layout: 13, json: 14 };

function swimIndexSheet_(ss) {
  var sh = ss.getSheetByName(SWIM_INDEX_SHEET);
  if (!sh) {
    sh = ss.insertSheet(SWIM_INDEX_SHEET, 0);
    sh.getRange(1, 1, 1, SWIM_INDEX_HEADERS.length).setValues([SWIM_INDEX_HEADERS])
      .setFontWeight('bold').setBackground(SWIM_COLORS.header);
    sh.setFrozenRows(1);
    sh.setColumnWidth(1, 120);
    sh.setColumnWidth(2, 220);
    sh.hideColumns(SWIM_IDX.sheetId + 1, 3);
  }
  return sh;
}

function swimFindIndexEntry_(index, id) {
  var last = index.getLastRow();
  if (last < 2) return null;
  var data = index.getRange(2, 1, last - 1, SWIM_INDEX_HEADERS.length).getValues();
  for (var i = 0; i < data.length; i++) {
    if (String(data[i][SWIM_IDX.id]) === id) {
      var prevBase = null;
      try { prevBase = swimSheetBaseName_(swimNormalizeRace_(JSON.parse(String(data[i][SWIM_IDX.json])))); } catch (e) { prevBase = null; }
      return { row: i + 2, sheetId: String(data[i][SWIM_IDX.sheetId]), layoutSig: String(data[i][SWIM_IDX.layout]), prevBaseName: prevBase };
    }
  }
  return null;
}

function swimSheetById_(ss, id) {
  var sheets = ss.getSheets();
  for (var i = 0; i < sheets.length; i++) if (String(sheets[i].getSheetId()) === String(id)) return sheets[i];
  return null;
}

function swimSaveIndexEntry_(index, entry, race, sheet, signature, finalTime) {
  var sheetName = sheet.getName();
  var link = '=HYPERLINK("#gid=' + sheet.getSheetId() + '","' + sheetName.replace(/"/g, '""') + '")';
  var raw = JSON.stringify(race);
  if (raw.length > 49000) raw = raw.substring(0, 49000);
  var rowValues = [race.id, link, race.athlete, race.athleteId, SWIM_STYLE_LABELS[race.style] + (race.relay ? ' relay' : ''), race.distanceLabel,
    race.round, race.competition, race.date, finalTime === '' ? '' : finalTime,
    Utilities.formatDate(new Date(), Session.getScriptTimeZone(), 'dd/MM/yyyy HH:mm'), race.video, String(sheet.getSheetId()), signature, raw];
  var r = entry ? entry.row : index.getLastRow() + 1;
  index.getRange(r, 1, 1, rowValues.length).setValues([rowValues]);
}

function swimSheetBaseName_(race) {
  var styleLabel = SWIM_STYLE_LABELS[race.style] || race.style;
  var parts = [race.athlete || 'Αγώνας', race.distanceLabel + styleLabel, race.round];
  var name = parts.filter(function (p) { return p; }).join(' ').replace(/[\[\]\*\?\/\\:]/g, ' ').replace(/\s+/g, ' ').trim();
  if (name.length > 80) name = name.substring(0, 80).trim();
  if (name === SWIM_INDEX_SHEET) name += ' 1';
  return name;
}

function swimNameRoot_(name) {
  return String(name).replace(/ \(\d+\)$/, '');
}

function swimUniqueSheetName_(ss, base, self) {
  var name = base, i = 2;
  while (true) {
    var existing = ss.getSheetByName(name);
    if (!existing || (self && existing.getSheetId() === self.getSheetId())) return name;
    name = base + ' (' + i + ')';
    i++;
  }
}

// ---------------------------------------------------------------------------------------------
// PDF
// ---------------------------------------------------------------------------------------------

function swimReportFolder_() {
  var it = DriveApp.getFoldersByName(SWIM_REPORT_FOLDER);
  return it.hasNext() ? it.next() : DriveApp.createFolder(SWIM_REPORT_FOLDER);
}

function swimExportPdf_(ss, sheet) {
  if (!sheet) throw new Error('Δεν βρέθηκε το φύλλο για PDF.');
  SpreadsheetApp.flush();
  var url = 'https://docs.google.com/spreadsheets/d/' + ss.getId() + '/export?format=pdf&gid=' + sheet.getSheetId() +
    '&size=A4&portrait=true&fitw=true&gridlines=false&printtitle=false&sheetnames=false&pagenum=CENTER&fzr=false' +
    '&top_margin=0.40&bottom_margin=0.40&left_margin=0.35&right_margin=0.35&horizontal_alignment=CENTER';
  var resp = UrlFetchApp.fetch(url, { headers: { Authorization: 'Bearer ' + ScriptApp.getOAuthToken() }, muteHttpExceptions: true });
  if (resp.getResponseCode() !== 200) throw new Error('Αποτυχία εξαγωγής PDF (HTTP ' + resp.getResponseCode() + ').');
  var fileName = sheet.getName() + '.pdf';
  var folder = swimReportFolder_();
  var old = folder.getFilesByName(fileName);
  while (old.hasNext()) old.next().setTrashed(true);
  var file = folder.createFile(resp.getBlob().setName(fileName));
  return { url: file.getUrl(), name: fileName, id: file.getId() };
}

// ---------------------------------------------------------------------------------------------
// Μενού & ρύθμιση
// ---------------------------------------------------------------------------------------------

/**
 * Προσθέτει το μενού "Swim Analysis". Καλείται από το onOpen (SwimWebApp.gs ή το δικό σου onOpen)
 * ή από trigger που εγκαθιστά το swimSetup όταν το onOpen του project δεν το καλεί.
 */
function swimOnOpen(e) {
  SpreadsheetApp.getUi().createMenu('Swim Analysis')
    .addItem('1. Αρχική ρύθμιση', 'swimSetup')
    .addItem('2. Στοιχεία σύνδεσης για το Kinovea', 'swimShowConnectionInfo')
    .addSeparator()
    .addItem('Άνοιγμα των αναφορών (reports)', 'swimOpenReports')
    .addItem('PDF της επιλεγμένης ανάλυσης', 'swimPdfSelected')
    .addItem('Επαναδημιουργία της επιλεγμένης ανάλυσης', 'swimRebuildSelected')
    .addSeparator()
    .addItem('Elite αναφορές (άνοιγμα)', 'swimOpenReferences')
    .addItem('Η επιλεγμένη ανάλυση ως elite αναφορά', 'swimReferenceSelected')
    .addSeparator()
    .addItem('Δοκιμαστικός αγώνας (demo)', 'swimCreateDemo')
    .addSeparator()
    .addItem('Νέο token (ακυρώνει το παλιό)', 'swimResetToken')
    .addToUi();
}

/**
 * Τρέχει μία φορά από το Apps Script (επιλογή swimSetup → Εκτέλεση).
 * Δημιουργεί token, το spreadsheet των αναφορών, τον πίνακα "Αναλύσεις Αγώνων" και το μενού.
 */
function swimSetup() {
  var props = swimProps_();
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  if (ss) props.setProperty('SWIM_DATA_SPREADSHEET_ID', ss.getId());
  if (!props.getProperty('SWIM_TOKEN')) props.setProperty('SWIM_TOKEN', Utilities.getUuid().replace(/-/g, ''));

  var data = swimDataSpreadsheet_();
  var reports = swimReportsSpreadsheet_();
  swimIndexSheet_(reports);
  swimReferencesSheet_(reports);
  swimSummarySheet_(data);
  swimReportFolder_();
  swimEnsureMenuTrigger_(data);

  var athletes = swimAthletes_();
  Logger.log('Token: ' + swimGetToken_());
  Logger.log('Αναφορές: ' + reports.getUrl());
  Logger.log('Αθλητές: ' + athletes.athletes.length + ' από το φύλλο "' + athletes.sheet + '"');
  var st = swimProjectStatus_();
  Logger.log(st.doPostOk ? 'doPost: OK' : (st.doPost ? 'doPost: υπάρχει – πρόσθεσε στην αρχή του: var swimResponse = swimTryHandlePost(e); if (swimResponse) return swimResponse;'
    : 'doPost: λείπει – πρόσθεσε: function doPost(e) { return swimDoPost(e); }'));
  try { swimShowConnectionInfo(); } catch (e) { /* εκτέλεση χωρίς ανοιχτό φύλλο: τα στοιχεία είναι στο αρχείο καταγραφής */ }
}

/** Αν το onOpen του project δεν καλεί το swimOnOpen, εγκαθιστά trigger ώστε να εμφανίζεται το μενού. */
function swimEnsureMenuTrigger_(ss) {
  var simpleCallsUs = false;
  try {
    simpleCallsUs = (typeof onOpen === 'function') && String(onOpen).indexOf('swimOnOpen') >= 0;
  } catch (e) {
    simpleCallsUs = false;
  }
  var triggers = ScriptApp.getProjectTriggers().filter(function (t) { return t.getHandlerFunction() === 'swimOnOpen'; });
  if (simpleCallsUs) {
    triggers.forEach(function (t) { ScriptApp.deleteTrigger(t); });
  } else if (!triggers.length && ss) {
    ScriptApp.newTrigger('swimOnOpen').forSpreadsheet(ss).onOpen().create();
  }
}

/** Ελέγχει αν το project έχει doPost που περνά τα αιτήματα του Kinovea στο swimDoPost / swimTryHandlePost. */
function swimProjectStatus_() {
  var st = { doPost: false, doPostOk: false };
  try {
    st.doPost = typeof doPost === 'function';
    var src = st.doPost ? String(doPost) : '';
    st.doPostOk = src.indexOf('swimDoPost') >= 0 || src.indexOf('swimTryHandlePost') >= 0;
  } catch (e) { st.doPost = false; }
  return st;
}

function swimProjectStatusHtml_() {
  var st = swimProjectStatus_();
  if (st.doPostOk) return '<p style="color:#2e8b57">✔ Το doPost του project περνά τα αιτήματα του Kinovea.</p>';
  if (!st.doPost)
    return '<p style="color:#c06e00">⚠ Το project δεν έχει doPost. Πρόσθεσε σε ένα αρχείο:<br>' +
      '<code>function doPost(e) { return swimDoPost(e); }</code></p>';
  return '<p style="color:#c06e00">⚠ Το project έχει ήδη δικό του doPost. Βάλε στην πρώτη γραμμή του:<br>' +
    '<code>var swimResponse = swimTryHandlePost(e); if (swimResponse) return swimResponse;</code></p>';
}

function swimResetToken() {
  swimProps_().setProperty('SWIM_TOKEN', Utilities.getUuid().replace(/-/g, ''));
  swimShowConnectionInfo();
}

function swimShowConnectionInfo() {
  var token = swimGetToken_() || '(τρέξε πρώτα: Swim Analysis → 1. Αρχική ρύθμιση)';
  var url = '';
  try { url = ScriptApp.getService().getUrl() || ''; } catch (e) { url = ''; }
  var reportsUrl = '';
  try { reportsUrl = swimReportsSpreadsheet_().getUrl(); } catch (e) { reportsUrl = ''; }
  var athletes = 0, athleteSheet = '';
  try { var a = swimAthletes_(); athletes = a.athletes.length; athleteSheet = a.sheet; } catch (e) { athletes = 0; }
  var html =
    '<div style="font-family:Arial;font-size:13px;line-height:1.5">' +
    '<p><b>Token</b> (επικόλλησέ το στην καρτέλα Σύνδεση του πάνελ στο Kinovea):</p>' +
    '<input style="width:100%;font-size:13px" readonly value="' + token + '" onclick="this.select()">' +
    '<p style="margin-top:14px"><b>Διεύθυνση Web App</b>: αντέγραψε τη διεύθυνση που τελειώνει σε <code>/exec</code> ' +
    'από το Apps Script → Ανάπτυξη → Διαχείριση αναπτύξεων.' +
    (url ? '<br>Πιθανή διεύθυνση:<br><input style="width:100%" readonly value="' + url + '" onclick="this.select()">' : '') + '</p>' +
    '<p>Αθλητές για το Kinovea: <b>' + athletes + '</b>' + (athleteSheet ? ' (φύλλο "' + athleteSheet + '")' : ' – δεν βρέθηκε φύλλο με στήλες unique_id και ΟΝΟΜΑΤΕΠΩΝΥΜΟ') + '</p>' +
    (reportsUrl ? '<p>Αναφορές: <a href="' + reportsUrl + '" target="_blank">' + SWIM_REPORTS_TITLE + '</a></p>' : '') +
    swimProjectStatusHtml_() +
    '<p style="color:#666">Έκδοση backend ' + SWIM_VERSION + '</p></div>';
  SpreadsheetApp.getUi().showModalDialog(HtmlService.createHtmlOutput(html).setWidth(560).setHeight(460), 'Σύνδεση με Kinovea');
}

function swimOpenReports() {
  var url = swimReportsSpreadsheet_().getUrl();
  var html = '<div style="font-family:Arial;font-size:13px"><p><a href="' + url + '" target="_blank">Άνοιγμα: ' + SWIM_REPORTS_TITLE + '</a></p>' +
    '<p style="color:#666">Ένα φύλλο ανά ανάλυση. Η λίστα όλων των αναλύσεων είναι και στο φύλλο "' + SWIM_SUMMARY_SHEET + '" εδώ.</p></div>';
  SpreadsheetApp.getUi().showModalDialog(HtmlService.createHtmlOutput(html).setWidth(420).setHeight(140), 'Αναφορές');
}

/** Race ID της ανάλυσης που είναι επιλεγμένη: γραμμή στο "Αναλύσεις Αγώνων" ή ενεργό φύλλο αγώνα. */
function swimSelectedRaceId_() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var sheet = ss.getActiveSheet();
  if (sheet.getName() === SWIM_SUMMARY_SHEET) {
    var row = sheet.getActiveRange().getRow();
    if (row < 2) return null;
    var id = String(sheet.getRange(row, SWIM_SUM.raceId + 1).getValue() || '');
    return id || null;
  }
  var race = swimRaceFromSheet_(sheet);
  return race ? race.id : null;
}

function swimStoredRace_(raceId) {
  var index = swimIndexSheet_(swimReportsSpreadsheet_());
  var last = index.getLastRow();
  if (last < 2) return null;
  var data = index.getRange(2, 1, last - 1, SWIM_INDEX_HEADERS.length).getValues();
  for (var i = 0; i < data.length; i++) {
    if (String(data[i][SWIM_IDX.id]) === raceId) {
      try { return { row: i + 2, race: JSON.parse(String(data[i][SWIM_IDX.json])), sheetId: String(data[i][SWIM_IDX.sheetId]) }; } catch (e) { return null; }
    }
  }
  return null;
}

function swimRaceFromSheet_(sheet) {
  var ss = sheet.getParent();
  var index = ss.getSheetByName(SWIM_INDEX_SHEET);
  if (!index) return null;
  var last = index.getLastRow();
  if (last < 2) return null;
  var data = index.getRange(2, 1, last - 1, SWIM_INDEX_HEADERS.length).getValues();
  for (var i = 0; i < data.length; i++) {
    if (String(data[i][SWIM_IDX.sheetId]) === String(sheet.getSheetId())) {
      try { return JSON.parse(String(data[i][SWIM_IDX.json])); } catch (e) { return null; }
    }
  }
  return null;
}

function swimRebuildSelected() {
  var ui = SpreadsheetApp.getUi();
  var id = swimSelectedRaceId_();
  var stored = id ? swimStoredRace_(id) : null;
  if (!stored) { ui.alert('Επίλεξε μια γραμμή στο φύλλο "' + SWIM_SUMMARY_SHEET + '" και ξαναδοκίμασε.'); return; }
  var index = swimIndexSheet_(swimReportsSpreadsheet_());
  index.getRange(stored.row, SWIM_IDX.layout + 1).setValue('');
  swimUpsertRace_(stored.race);
  ui.alert('Η ανάλυση δημιουργήθηκε ξανά.');
}

function swimPdfSelected() {
  var ui = SpreadsheetApp.getUi();
  var id = swimSelectedRaceId_();
  var stored = id ? swimStoredRace_(id) : null;
  if (!stored) { ui.alert('Επίλεξε μια γραμμή στο φύλλο "' + SWIM_SUMMARY_SHEET + '" και ξαναδοκίμασε.'); return; }
  var reports = swimReportsSpreadsheet_();
  var info = swimExportPdf_(reports, swimSheetById_(reports, stored.sheetId));
  swimSetSummaryPdf_(id, info.url);
  var html = '<div style="font-family:Arial;font-size:13px"><p>Το PDF αποθηκεύτηκε στον φάκελο <b>' + SWIM_REPORT_FOLDER + '</b>.</p>' +
    '<p><a href="' + info.url + '" target="_blank">Άνοιγμα ' + info.name + '</a></p></div>';
  ui.showModalDialog(HtmlService.createHtmlOutput(html).setWidth(420).setHeight(140), 'Report PDF');
}

/** Δοκιμαστικός αγώνας 100μ. ελεύθερο, για να δεις το report χωρίς Kinovea. */
function swimDemoRace_() {
  var cps = [15, 25, 35, 45, 50, 65, 75, 85, 95, 100];
  var times = [6.10, 10.95, 16.02, 21.12, 23.80, 30.15, 35.55, 41.02, 46.55, 49.42];
  var strokes = [];
  var t;
  for (t = 4.95; t < 23.6; t += 0.615) strokes.push(Math.round(t * 100) / 100);
  for (t = 28.70; t < 49.2; t += 0.64) strokes.push(Math.round(t * 100) / 100);
  return {
    id: 'demo-100fs', athlete: 'Αθλητής Δοκιμής', athleteId: '', club: 'Δοκιμαστικό σωματείο', category: '', style: 'fs', relay: false,
    distance: 100, distanceLabel: '100', pool: 50,
    competition: 'Δοκιμή συστήματος', location: 'Αθήνα', round: 'Final', date: Utilities.formatDate(new Date(), Session.getScriptTimeZone(), 'd.M.yyyy'),
    checkpoints: cps.map(function (d, i) { return { d: d, t: times[i] }; }),
    strokes: strokes,
    kicks: [1.5, 1.9, 2.3, 2.7, 3.1, 3.5, 3.9, 25.0, 25.4, 25.8, 26.2, 26.6, 27.0],
    breakouts: [4.62, 28.35],
    feetOff: [0.68, 24.40],
    entries: [0.98],
    manual: { reaction: 0.62, entryDistHands: 3.4, entryDistHips: 3.1, underwaterDist: 11.8, breakoutDist: 12.1,
      turns: { '50': { underwaterDist: 9.1 } }, finishLastStrokeDist: 0.35 }
  };
}

function swimCreateDemo() {
  var res = swimUpsertRace_(swimDemoRace_());
  var html = '<div style="font-family:Arial;font-size:13px"><p>Δημιουργήθηκε ο δοκιμαστικός αγώνας.</p>' +
    '<p><a href="' + res.sheetUrl + '" target="_blank">Άνοιγμα του report</a></p>' +
    '<p style="color:#666">Μπήκε και μία γραμμή στο φύλλο "' + SWIM_SUMMARY_SHEET + '" (μπορείς να τη διαγράψεις).</p></div>';
  SpreadsheetApp.getUi().showModalDialog(HtmlService.createHtmlOutput(html).setWidth(420).setHeight(170), 'Demo');
}
