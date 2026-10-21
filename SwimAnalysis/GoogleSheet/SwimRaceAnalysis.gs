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
 *   7. Φτιάχνει το report, το PDF και τα γραφήματα στα αγγλικά όταν το ζητά το Kinovea (lang 'en', 2.7.7)· αλλιώς στα ελληνικά.
 */

var SWIM_VERSION = '2.9.5';
var SWIM_APP_ID = 'kinovea-swim';
var SWIM_INDEX_SHEET = 'Αγώνες';
var SWIM_SUMMARY_SHEET = 'Αναλύσεις Αγώνων';
var SWIM_ATHLETES_SHEET = 'Στοιχεία Αθλητών/τριων';

/**
 * Οι διεθνείς αθλητές (2.7.6): ένας μοναδικός κωδικός ο καθένας, WA και ο κωδικός του στη World Aquatics, αλλιώς X-<ΧΩΡΑ>-<αύξων>,
 * σε δικό τους φύλλο (το μητρώο της ΚΟΕ μένει όπως είναι). Οι άλλες γραφές των ονομάτων των αθλητών του μητρώου (π.χ. με λατινικά σε
 * αποτελέσματα του εξωτερικού) σε δικό τους φύλλο.
 */
var SWIM_INTL_SHEET = 'Διεθνείς αθλητές';
var SWIM_INTL_HEADERS = ['Κωδικός', 'Ονοματεπώνυμο', 'Χώρα', 'Φύλο', 'Έτος γέννησης', 'Σωματείο', 'Κωδικός World Aquatics', 'Άλλες γραφές', 'Καταχώρηση'];
var SWIM_ALIAS_SHEET = 'Άλλες γραφές ονομάτων';
var SWIM_ALIAS_HEADERS = ['Αριθμός μητρώου', 'Όνομα όπως γράφεται', 'Καταχώρηση'];
var SWIM_REPORTS_TITLE = 'Αναλύσεις Αγώνων Κολύμβησης (Kinovea)';
var SWIM_REPORT_FOLDER = 'Swim Race Reports';

/**
 * (2.9.5) Ο ΔΑΚ κάθε 50άρι, όχι μόνο ο μέσος όρος του: στη στήλη L των χρόνων (στη θέση της «Μέση ταχύτητα από εκκίνηση»)
 * στη γραμμή όπου τελειώνει κάθε 50άρι, κάτω από το πλακίδιο του ΔΑΚ και στην πρώτη σελίδα του PDF («Με μια ματιά»).
 */
var SWIM_DAK50 = true;

/**
 * (2.9.5) Τα γραφήματα του PDF: τα γραφήματα του Google (όσα δεν έχουν εικόνα του Kinovea για τις τιμές τους) χωρίς τις τιμές
 * πάνω στις σειρές (με αυτές το Google, στο PDF και στην εικόνα του γραφήματος, σχεδίαζε μόνο τον τίτλο), με τα δεδομένα τους
 * και από κρυμμένες γραμμές· ένα γράφημα χωρίς καμία τιμή δεν μπαίνει· όσα έχουν εικόνα του Kinovea για παλιότερες τιμές τα
 * ζητά ξανά από το Kinovea μία φορά πριν από το PDF.
 */
var SWIM_PDF_CHARTS = true;

/** Μέτρα ανά γιάρδα (πισίνες 25yd). */
var SWIM_YARD = 0.9144;

/** Γραμματοσειρά και χρώματα των report (μοντέρνο, φωτεινό στυλ που τυπώνεται καλά). */
var SWIM_FONT = 'Roboto';
var SWIM_COLORS = {
  title: '#0B111A',       // σκούρα μπάντα τίτλου, τίτλοι (όπως οι κάρτες του βίντεο)
  titleText: '#FFFFFF',
  subText: '#8CA0B6',
  accent: '#00C8FF',      // γραμμή κάτω από τον τίτλο (το κυανό του βίντεο)
  accentDark: '#123B5C',  // τίτλοι στιγμιοτύπων
  section: '#14243A',     // επικεφαλίδες ενοτήτων
  sectionText: '#FFFFFF',
  header: '#E8EEF5',      // κεφαλίδες πινάκων
  headerText: '#2B3F55',
  label: '#F1F5F9',
  formula: '#F7F9FB',     // τύποι
  auto: '#E6F4FB',        // αυτόματα από το Kinovea
  input: '#FFFFFF',       // πληκτρολόγηση
  kpi: '#F3F6FA',
  kpiDak: '#FFF3E0',      // ΔΑΚ
  dakText: '#E65100',
  phase: '#FCE9D6',       // τμήματα εκκίνησης / στροφής / τερματισμού
  border: '#D3DCE6',
  text: '#1F2D3D',
  dim: '#5B6B7B',
  athlete: '#1565C0',     // σειρές γραφημάτων
  pb: '#90A4AE',
  elite: '#F9A825',
  eliteText: '#B26A00',
  sr: '#00897B',
  dps: '#8E24AA',
  dak: '#EF6C00',
  swolf: '#5C6BC0'
};

/**
 * Τα χρώματα των σειρών στα γραφήματα, όπως στα γραφικά του βίντεο: ο αθλητής πορτοκαλί-κίτρινος, η αναφορά (elite) κυανή,
 * η συχνότητα πράσινη (πιο σκούρα από το βίντεο, για λευκό φόντο).
 */
var SWIM_CHART_COLORS = {};
SWIM_CHART_COLORS[SWIM_COLORS.athlete] = '#F2A900';
SWIM_CHART_COLORS[SWIM_COLORS.pb] = '#8CA0B6';
SWIM_CHART_COLORS[SWIM_COLORS.elite] = '#00A8DB';
SWIM_CHART_COLORS[SWIM_COLORS.sr] = '#1FAF7A';
SWIM_CHART_COLORS[SWIM_COLORS.dps] = '#8E5CF6';
SWIM_CHART_COLORS[SWIM_COLORS.dak] = '#F2711C';
SWIM_CHART_COLORS[SWIM_COLORS.swolf] = '#5C6BC0';

function swimChartColor_(c) {
  return SWIM_CHART_COLORS[c] || c;
}

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
  // Η γλώσσα του report και των μηνυμάτων προς το Kinovea (2.7.7): μόνο για αυτό το αίτημα· μετά ξανά ελληνικά (μενού).
  SWIM_LANG_ = swimLangOf_(req);
  try {
    return swimHandleRequest_(req);
  } finally {
    SWIM_LANG_ = 'el';
  }
}

function swimHandleRequest_(req) {
  var token = swimGetToken_();
  if (!token || String(req.token || '') !== token)
    return swimJson_({ ok: false, error: swimT_('Λάθος token. Αντέγραψε ξανά το token από το μενού Swim Analysis → Στοιχεία σύνδεσης.') });

  var action = String(req.action || 'update');
  var fail = function (err) { return swimJson_({ ok: false, error: String(err && err.message ? err.message : err) }); };

  // Ενέργειες που μόνο διαβάζουν ή γράφουν μόνο στο Drive: χωρίς κλείδωμα, ώστε να μην περιμένουν τις ενημερώσεις.
  var unlocked = {
    athletes: function () {
      var list = swimAthletes_();
      return { ok: true, version: SWIM_VERSION, sheet: list.sheet, athletes: list.athletes };
    },
    analyses: function () {
      var q = req.query || {};
      var out = { ok: true, version: SWIM_VERSION, analyses: swimAnalysesFor_(q) };
      // 2.8.0: και οι αναλύσεις άλλων αθλητών του αγωνίσματος, για σύγκριση στο βίντεο (με τους χρόνους τους).
      if (q.others) out.others = swimEventAnalyses_(q);
      return out;
    },
    benchmarks: function () { return { ok: true, version: SWIM_VERSION, levels: SWIM_BENCH_LEVELS, benchmarks: swimBenchmarks_() }; },
    eliteFrames: function () { return { ok: true, version: SWIM_VERSION, sheet: SWIM_ELITE_SHEET, frames: swimEliteFrames_() }; },
    getImage: function () { return swimGetImage_(req); },
    image: function () { return swimSaveSnapshot_(req); },
    images: function () { return swimSaveImages_(req); }
  };
  if (Object.prototype.hasOwnProperty.call(unlocked, action)) {
    try { return swimJson_(unlocked[action]()); } catch (err) { return fail(err); }
  }

  var lock = LockService.getScriptLock();
  try {
    lock.waitLock(25000);
  } catch (err) {
    return swimJson_({ ok: false, error: swimT_('Το φύλλο είναι απασχολημένο, θα ξαναπροσπαθήσει.'), retry: true });
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

    if (action === 'saveEliteFrame') {
      return swimJson_(swimSaveEliteFrame_(req));
    }

    if (action === 'addAthlete') {
      return swimJson_(swimAddAthlete_(req));
    }

    if (action === 'addAlias') {
      return swimJson_(swimAddAlias_(req));
    }

    if (action === 'update' || action === 'pdf') {
      // drawCharts: το Kinovea σχεδιάζει τα γραφήματα του PDF με τα γραφικά του βίντεο. Όσα δεν έχουν ακόμη εικόνα για τις
      // τρέχουσες τιμές τους επιστρέφονται (τιμές και ρυθμίσεις) χωρίς PDF· το Kinovea τα ανεβάζει και ζητά ξανά το PDF.
      var drawCharts = !!req.drawCharts;
      var result = swimUpsertRace_(req.race);
      var chartData = result.charts;
      delete result.charts;
      if (drawCharts && result.chartsMissing.length) {
        result.charts = chartData.filter(function (c) { return result.chartsMissing.indexOf(c.key) >= 0; });
        if (action === 'pdf') {
          result.needCharts = true;
          return swimJson_(result);
        }
      } else if (action === 'pdf' && (result.chartsStale || []).length && swimChartsAgain_(result.raceId)) {
        // 2.9.5: το PDF μετά το ανέβασμα των γραφημάτων (χωρίς drawCharts) βρήκε εικόνες για τιμές που άλλαξαν στο μεταξύ: το
        // Kinovea τις ξανασχεδιάζει (μία φορά) αντί να μπουν στο PDF γραφήματα του Google.
        result.charts = chartData.filter(function (c) { return result.chartsStale.indexOf(c.key) >= 0; });
        result.needCharts = true;
        return swimJson_(result);
      }
      if (action === 'pdf') {
        var ss = swimReportsSpreadsheet_();
        result.pdf = swimExportPdf_(ss, swimSheetById_(ss, result.sheetId), result.print, result.rebuilt, result.quick);
        swimSetSummaryPdf_(result.raceId, result.pdf.url);
      }
      return swimJson_(result);
    }

    // Ελληνικά και στα δύο: το Kinovea αναγνωρίζει το «Άγνωστη ενέργεια» (παλιότερο script).
    return swimJson_({ ok: false, error: 'Άγνωστη ενέργεια: ' + action });
  } catch (err) {
    return swimJson_({ ok: false, error: String(err && err.message ? err.message : err), stack: String(err && err.stack ? err.stack : '') });
  } finally {
    // Οι αλλαγές γράφονται πριν αφεθεί το κλείδωμα: το επόμενο αίτημα (π.χ. ένας ακόμη διεθνής αθλητής) τις βλέπει (2.7.6).
    try { SpreadsheetApp.flush(); } catch (e) { /* τίποτα να γραφτεί */ }
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
  throw new Error(swimT_('Δεν βρέθηκε το Google Sheet. Τρέξε πρώτα τη συνάρτηση swimSetup από το Apps Script.'));
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
// Γλώσσα του report (2.7.7): ελληνικά ή αγγλικά
//
// Το Kinovea στέλνει σε κάθε αίτημα lang: 'el' | 'en' (τα παλιότερα Kinovea χωρίς lang: ελληνικά). Στα αγγλικά βγαίνουν τα
// κείμενα του report, του PDF και των γραφημάτων και τα μηνύματα προς το Kinovea. Μένουν ελληνικά τα φύλλα δεδομένων και οι
// επικεφαλίδες τους, οι τιμές που γράφονται σε αυτά, τα ονόματα των φύλλων και των αρχείων, τα μενού και οι διάλογοι του
// φύλλου. Τα αγγλικά στο SWIM_EN_ (στο τέλος του αρχείου), με κλειδί το ελληνικό κείμενο.
// ---------------------------------------------------------------------------------------------

/** Η γλώσσα του αιτήματος που εκτελείται: 'en' ή 'el' (ελληνικά εκτός αιτήματος, π.χ. στα μενού). */
var SWIM_LANG_ = 'el';

/** 'en' όταν το αίτημα ζητά αγγλικά (lang, αλλιώς το lang του αγώνα), αλλιώς 'el'. */
function swimLangOf_(req) {
  var lang = req && req.lang !== undefined && req.lang !== null && req.lang !== '' ? req.lang
    : (req && req.race && typeof req.race === 'object' ? req.race.lang : '');
  return lang === 'en' ? 'en' : 'el';
}

/** fn() στη γλώσσα lang ('en', αλλιώς ελληνικά) και μετά στην προηγούμενη (το μενού: η γλώσσα του αγώνα όπως κρατήθηκε). */
function swimWithLang_(lang, fn) {
  var prev = SWIM_LANG_;
  SWIM_LANG_ = lang === 'en' ? 'en' : 'el';
  try {
    return fn();
  } finally {
    SWIM_LANG_ = prev;
  }
}

function swimHasEn_(el) {
  return Object.prototype.hasOwnProperty.call(SWIM_EN_, el);
}

/** Ένα κείμενο στη γλώσσα του report: τα αγγλικά του (SWIM_EN_), αλλιώς το ίδιο (και όταν δεν έχει αγγλικά). */
function swimT_(el) {
  return SWIM_LANG_ === 'en' && typeof el === 'string' && swimHasEn_(el) ? SWIM_EN_[el] : el;
}

/** Κείμενο με κενά {0}, {1}…: το πρότυπο στη γλώσσα του report με τις τιμές (στα ελληνικά όσο και πριν). */
function swimTF_(el) {
  var args = arguments;
  return swimT_(el).replace(/\{(\d+)\}/g, function (all, n) {
    var i = Number(n) + 1;
    return i < args.length ? String(args[i]) : all;
  });
}

/**
 * Μια τιμή που έρχεται από το Kinovea στα ελληνικά (στάδιο ή γεγονός στιγμιότυπου, γωνία, φάση, τιμή του τελικού, φάση του
 * αγώνα, φύλο) στη γλώσσα του report, όπως το SwimLang.Label του Kinovea: τα αγγλικά της, αλλιώς των λέξεων γύρω από τον
 * αριθμό της ("Στροφή 50μ." → "Turn 50m", "Εκκίνηση 2" → "Start 2"), αλλιώς η ίδια (π.χ. όνομα που έγραψε ο αναλυτής).
 * Με « · » (η ετικέτα του τελικού "OG2024 · 100μ. bk Ανδρών") κάθε κομμάτι χωριστά.
 */
function swimL_(v) {
  if (SWIM_LANG_ !== 'en' || typeof v !== 'string' || !v) return v;
  if (swimHasEn_(v)) return SWIM_EN_[v];
  if (v.indexOf(' · ') >= 0) return v.split(' · ').map(swimL_).join(' · ');
  var m = /^(\D*?)(\d+(?:[.,]\d+)?(?:–\d+)?)(μ\.|yd|m)?(.*)$/.exec(v);
  if (!m) return v;
  var pre = m[1] ? swimWords_(m[1]) : '', post = m[4] ? swimWords_(m[4]) : '';
  if (pre === null || post === null) return v;
  return pre + m[2] + (m[3] === 'μ.' ? 'm' : (m[3] || '')) + post;
}

/**
 * Τα αγγλικά λέξεων γύρω από έναν αριθμό, με τα κενά τους ("Στροφή " → "Turn ", " bk Ανδρών" → " bk Men"): κάθε λέξη με
 * ελληνικά γράμματα πρέπει να είναι γνωστή, αλλιώς null.
 */
function swimWords_(el) {
  if (swimHasEn_(el)) return SWIM_EN_[el];
  var t = el.trim();
  if (!t) return el;
  var en = swimHasEn_(t) ? SWIM_EN_[t] : null;
  if (en === null) {
    var words = t.split(' ').map(function (w) { return !/[Ͱ-Ͽ]/.test(w) ? w : (swimHasEn_(w) ? SWIM_EN_[w] : null); });
    if (words.indexOf(null) >= 0) return null;
    en = words.join(' ');
  }
  var lead = el.length - el.replace(/^\s+/, '').length, trail = el.length - el.replace(/\s+$/, '').length;
  return el.substring(0, lead) + en + el.substring(el.length - trail);
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

/**
 * Επιστρέφει { sheet, athletes: [{id, name, gender, birthYear, category, club, aliases}] } χωρίς διπλότυπα μητρώα, και μετά τους
 * διεθνείς αθλητές ({…, nation, waId, intl: true}).
 */
function swimAthletes_() {
  var list = swimRegistryAthletes_();
  var ss = swimDataSpreadsheet_();
  var aliases = swimAliasRows_(ss.getSheetByName(SWIM_ALIAS_SHEET));
  list.athletes.forEach(function (a) { a.aliases = aliases[a.id] || []; });
  var seen = {};
  list.athletes.forEach(function (a) { seen[a.id] = true; });
  swimIntlRows_(ss.getSheetByName(SWIM_INTL_SHEET)).forEach(function (r) {
    if (seen[r.id]) return;
    seen[r.id] = true;
    list.athletes.push({ id: r.id, name: r.name, gender: r.gender, birthYear: r.birthYear, category: '', club: r.club, nation: r.nation,
      waId: r.waId, aliases: r.aliases, intl: true });
  });
  return list;
}

/** Το μητρώο της ΚΟΕ: { sheet, athletes: [{id, name, gender, birthYear, category, club}] } χωρίς διπλότυπα μητρώα. */
function swimRegistryAthletes_() {
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

/** Το όνομα για σύγκριση: κεφαλαία χωρίς τόνους, μόνο γράμματα, οι λέξεις με αλφαβητική σειρά (επώνυμο και όνομα με όποια σειρά). */
function swimNameKey_(name) {
  return swimNormText_(name).replace(/[^A-ZΑ-Ω ]/g, ' ').split(' ').filter(function (w) { return w; }).sort().join(' ');
}

/** Οι διεθνείς αθλητές του φύλλου τους: [{id, name, nation, gender, birthYear, club, waId, aliases}]. */
function swimIntlRows_(sh) {
  if (!sh || sh.getLastRow() < 2) return [];
  var values = sh.getRange(2, 1, sh.getLastRow() - 1, SWIM_INTL_HEADERS.length).getValues();
  var text = function (v) { return String(v === null || v === undefined ? '' : v).trim(); };
  return values.filter(function (row) { return text(row[0]) && text(row[1]); }).map(function (row) {
    return { id: text(row[0]), name: text(row[1]), nation: text(row[2]).toUpperCase(), gender: text(row[3]), birthYear: text(row[4]),
      club: text(row[5]), waId: text(row[6]), aliases: text(row[7]).split(';').map(function (x) { return x.trim(); }).filter(function (x) { return x; }) };
  });
}

/** Οι άλλες γραφές των ονομάτων των αθλητών του μητρώου, ανά αριθμό μητρώου. */
function swimAliasRows_(sh) {
  var map = {};
  if (!sh || sh.getLastRow() < 2) return map;
  sh.getRange(2, 1, sh.getLastRow() - 1, 2).getValues().forEach(function (row) {
    var id = String(row[0] === null ? '' : row[0]).trim(), alias = String(row[1] === null ? '' : row[1]).trim();
    if (!id || !alias) return;
    (map[id] = map[id] || []).push(alias);
  });
  return map;
}

/** Ένα φύλλο του κύριου αρχείου με τις επικεφαλίδες του (δημιουργείται την πρώτη φορά). */
function swimSheetWithHeaders_(name, headers) {
  var ss = swimDataSpreadsheet_();
  var sh = ss.getSheetByName(name);
  if (sh) return sh;
  sh = ss.insertSheet(name);
  sh.getRange(1, 1, 1, headers.length).setValues([headers]).setFontWeight('bold');
  sh.setFrozenRows(1);
  return sh;
}

/**
 * Ένας διεθνής αθλητής (από τα επίσημα αποτελέσματα στο Kinovea, 2.7.6) στο φύλλο «Διεθνείς αθλητές», με μοναδικό κωδικό: WA και ο
 * κωδικός του στη World Aquatics, αλλιώς X-<ΧΩΡΑ>-<αύξων>. Αν υπάρχει ήδη (ίδιος κωδικός World Aquatics, ή ίδιο όνομα, χώρα και έτος
 * γέννησης) επιστρέφει τον κωδικό του. Ίδιο όνομα και χώρα χωρίς έτος γέννησης στον έναν από τους δύο: duplicates, για να διαλέξει ο
 * αναλυτής (force: νέος αθλητής).
 */
function swimAddAthlete_(req) {
  var a = req.athlete || {};
  var name = String(a.name || '').replace(/\s+/g, ' ').trim();
  var nation = String(a.nation || '').trim().toUpperCase();
  if (!name || !/^[A-Z]{3}$/.test(nation))
    return { ok: false, error: swimT_('Χρειάζονται το ονοματεπώνυμο και η χώρα (κωδικός τριών γραμμάτων, π.χ. ITA).') };
  if (nation === 'GRE')
    return { ok: false, error: swimT_('Οι αθλητές της Ελλάδας είναι στο μητρώο της ΚΟΕ, με τον αριθμό μητρώου τους.') };

  var wa = String(a.waId || '').replace(/\D/g, '');
  var born = String(a.birthYear || '').replace(/\D/g, '');
  if (born.length !== 4) born = '';
  var aliases = (a.aliases || []).map(function (x) { return String(x).replace(/\s+/g, ' ').trim(); })
    .filter(function (x) { return x && x !== name; });
  var sh = swimSheetWithHeaders_(SWIM_INTL_SHEET, SWIM_INTL_HEADERS);
  var rows = swimIntlRows_(sh);
  var key = swimNameKey_(name);
  var i;
  if (wa) {
    for (i = 0; i < rows.length; i++)
      if (rows[i].waId === wa || rows[i].id === 'WA' + wa) return { ok: true, id: rows[i].id, existed: true };
  }

  var maybe = [];
  for (i = 0; i < rows.length; i++) {
    var r = rows[i];
    if (r.nation !== nation || (wa && r.waId && r.waId !== wa)) continue;
    var same = swimNameKey_(r.name) === key || r.aliases.some(function (x) { return swimNameKey_(x) === key; });
    if (!same) continue;
    if (born && r.birthYear === born) return { ok: true, id: r.id, existed: true };
    if (!born || !r.birthYear) maybe.push({ id: r.id, name: r.name, nation: r.nation, birthYear: r.birthYear });
  }

  if (maybe.length && !req.force)
    return { ok: false, duplicates: maybe, error: swimT_('Μοιάζει με αθλητή που υπάρχει ήδη στους διεθνείς.') };

  var id = wa ? 'WA' + wa : swimNextIntlId_(rows, nation);
  sh.appendRow([id, name, nation, String(a.gender || '').trim(), born, String(a.club || '').trim(), wa, aliases.join('; '), new Date()]);
  return { ok: true, id: id, created: true };
}

/**
 * Ο επόμενος κωδικός διεθνούς αθλητή χωρίς κωδικό World Aquatics: X-ITA-0001, X-ITA-0002… Ο τελευταίος που δόθηκε κρατιέται στις
 * ιδιότητες του script: ένας κωδικός δεν ξαναδίνεται ακόμη κι αν σβηστεί η γραμμή του (αγώνες μπορεί να τον έχουν ήδη).
 */
function swimNextIntlId_(rows, nation) {
  var max = 0, re = new RegExp('^X-' + nation + '-(\\d+)$');
  rows.forEach(function (r) { var m = re.exec(r.id); if (m) max = Math.max(max, Number(m[1])); });
  var props = PropertiesService.getScriptProperties();
  var key = 'SWIM_INTL_LAST_' + nation;
  var last = Number(props.getProperty(key) || 0);
  var next = Math.max(max, isNaN(last) ? 0 : last) + 1;
  props.setProperty(key, String(next));
  return 'X-' + nation + '-' + ('000' + next).slice(-4);
}

/**
 * Μια άλλη γραφή του ονόματος ενός αθλητή (π.χ. με λατινικά σε αποτελέσματα του εξωτερικού, 2.7.6): των διεθνών στη γραμμή τους, του
 * μητρώου στο φύλλο «Άλλες γραφές ονομάτων». Μία φορά η καθεμία.
 */
function swimAddAlias_(req) {
  var id = String(req.id === undefined || req.id === null ? '' : req.id).trim();
  var alias = String(req.alias || '').replace(/\s+/g, ' ').trim();
  if (!id || !alias) return { ok: false, error: swimT_('Χρειάζονται ο κωδικός του αθλητή και το όνομα.') };
  var key = swimNameKey_(alias);

  var ss = swimDataSpreadsheet_();
  var intl = ss.getSheetByName(SWIM_INTL_SHEET);
  var rows = swimIntlRows_(intl);
  for (var i = 0; i < rows.length; i++) {
    if (rows[i].id !== id) continue;
    if (swimNameKey_(rows[i].name) === key || rows[i].aliases.some(function (x) { return swimNameKey_(x) === key; }))
      return { ok: true, existed: true };
    var list = rows[i].aliases.concat([alias]);
    var values = intl.getRange(2, 1, intl.getLastRow() - 1, 1).getValues();
    for (var r = 0; r < values.length; r++) {
      if (String(values[r][0]).trim() === id) {
        intl.getRange(r + 2, 8).setValue(list.join('; '));
        return { ok: true, created: true };
      }
    }
  }

  var known = swimAliasRows_(ss.getSheetByName(SWIM_ALIAS_SHEET))[id] || [];
  if (known.some(function (x) { return swimNameKey_(x) === key; })) return { ok: true, existed: true };
  swimSheetWithHeaders_(SWIM_ALIAS_SHEET, SWIM_ALIAS_HEADERS).appendRow([id, alias, new Date()]);
  return { ok: true, created: true };
}

// ---------------------------------------------------------------------------------------------
// Upsert: race JSON -> sheet
// ---------------------------------------------------------------------------------------------

function swimUpsertRace_(rawRace) {
  if (!rawRace) throw new Error(swimT_('Λείπουν τα δεδομένα του αγώνα.'));
  var race = swimNormalizeRace_(rawRace);
  if (!race.id) throw new Error(swimT_('Λείπει το id του αγώνα.'));
  if (race.kind !== 'training' && !race.checkpoints.length) throw new Error(swimT_('Δεν υπάρχουν σημεία ελέγχου (αποστάσεις).'));
  // Η γλώσσα του report κρατιέται με τον αγώνα στο ευρετήριο (για τα report και τα PDF από το μενού)· τα ελληνικά χωρίς κλειδί.
  if (SWIM_LANG_ === 'en') race.lang = 'en';

  var analysis = swimAnalyze_(race);
  var extras = race.kind === 'training' ? {} : swimComparisons_(race);
  var ss = swimReportsSpreadsheet_();
  // Ύψος της σελίδας του PDF (μετριέται μία φορά από το ίδιο το Google): οι αλλαγές σελίδας του layout.
  extras.pageHeight = function (width) { return swimPdfPageHeight_(ss, width); };
  var layout = swimBuildLayout_(race, analysis, extras);

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

  var stored = swimSplitSignature_(entry ? entry.layoutSig : '');
  if (rebuilt || !entry || stored.grid !== layout.signature) {
    swimRenderLayout_(sheet, layout);
    rebuilt = true;
  }

  swimWriteInputs_(sheet, race, analysis, layout);
  SpreadsheetApp.flush();

  // Τα γραφήματα μετά τις τιμές: οι εικόνες του Kinovea όπου είναι για τις τρέχουσες τιμές, αλλιώς γραφήματα του Google.
  var charts = swimPlaceChartItems_(sheet, layout, race, rebuilt, rebuilt ? '' : stored.charts);

  var kpi = swimReadKpis_(sheet, layout, analysis);
  // Η πρώτη σελίδα του PDF (2.8.0): μόνο για τους αγώνες, με τη σελίδα της αναφοράς.
  var quick = null;
  if (race.kind !== 'training' && layout.page && layout.page.height > 0) {
    try { quick = swimBuildQuickLook_(race, analysis, extras, kpi, { height: layout.page.height, cols: layout.print.cols }); } catch (e) { quick = null; }
  }
  var sheetUrl = ss.getUrl() + '#gid=' + sheet.getSheetId();
  swimSaveIndexEntry_(index, entry, race, sheet, layout.signature + '\n' + charts.placement, kpi.finalTime);
  if (race.kind !== 'training') swimWriteSummary_(race, analysis, kpi, sheetUrl);

  var result = {
    ok: true,
    version: SWIM_VERSION,
    raceId: race.id,
    sheetId: sheet.getSheetId(),
    sheetName: sheet.getName(),
    sheetUrl: sheetUrl,
    rebuilt: rebuilt,
    print: layout.print,
    rows: analysis.rows.map(function (row) {
      return {
        d: row.d, t: row.t, segTime: row.segTime, vel: row.vel, avgVel: row.avgVel,
        strokes: row.strokes, sr: row.sr, dps: row.dps, srExact: row.srExact, style: row.style
      };
    }),
    blocks: analysis.blocks.map(function (b) { return { from: b.from, to: b.to, time: b.time, dak: b.dak, swolf: b.swolf }; }),
    start: analysis.start,
    turns: analysis.turns,
    finish: analysis.finish,
    summary: analysis.summary,
    compare: {
      pb: extras.pb ? extras.pb.label : '',
      elite: extras.elite ? extras.elite.label : '',
      finalists: (extras.finalists || []).length,
      benchmarks: !!extras.benchmarks
    },
    warnings: analysis.warnings,
    charts: charts.data,
    chartsMissing: charts.missing
  };
  Object.defineProperty(result, 'quick', { value: quick, enumerable: false });
  // 2.9.5: τα γραφήματα με εικόνα του Kinovea για άλλες τιμές (το PDF τα ζητά ξανά μία φορά, swimChartsAgain_).
  Object.defineProperty(result, 'chartsStale', { value: charts.stale || [], enumerable: false });
  return result;
}

/** Η υπογραφή του ευρετηρίου: του φύλλου (layout) και, στη δεύτερη γραμμή, της θέσης των γραφημάτων. */
function swimSplitSignature_(s) {
  var text = String(s === null || s === undefined ? '' : s), i = text.indexOf('\n');
  return i < 0 ? { grid: text, charts: '' } : { grid: text.substring(0, i), charts: text.substring(i + 1) };
}

/** Οι τιμές των KPI από το φύλλο (μετά τους τύπους, ώστε να μετρούν και όσα γράφτηκαν με το χέρι). */
function swimReadKpis_(sheet, layout, analysis) {
  var read = function (cell) {
    if (!cell) return '';
    try { return sheet.getRange(cell.r, cell.c).getValue(); } catch (e) { return ''; }
  };
  var num = function (v, fallback) { return typeof v === 'number' && isFinite(v) ? v : (fallback === undefined || fallback === null ? '' : fallback); };
  var k = layout.kpi || {};
  var s = analysis.summary || {};
  var out = {
    finalTime: num(read(k.finalTime), s.finalTime),
    avgVel: num(read(k.avgVel), s.avgVel),
    totalStrokes: num(read(k.totalStrokes), s.totalStrokes),
    sr: num(read(k.sr), s.avgSr),
    dps: num(read(k.dps), s.avgDps),
    dak: num(read(k.dak), s.avgDak)
  };
  // 2.9.5: και ο ΔΑΚ κάθε 50άρι (για την πρώτη σελίδα του PDF).
  if (SWIM_DAK50) out.dak50 = swimReadDak50_(sheet, layout, analysis);
  return out;
}

/**
 * (2.9.5) Ο ΔΑΚ κάθε 50άρι: [{ k (το πολλοστό 50άρι), v }], από τη στήλη H του πίνακα «Ανά 50» με μία ανάγνωση (μετρά και ό,τι
 * γράφτηκε με το χέρι), αλλιώς της ανάλυσης. Οι γραμμές του πίνακα είναι τα 50άρια της ανάλυσης, με την ίδια σειρά.
 */
function swimReadDak50_(sheet, layout, analysis) {
  var blocks = (analysis && analysis.blocks) || [];
  var p = layout && layout.per50, vals = [];
  if (p && p.first > 0 && p.last - p.first + 1 === blocks.length) {
    try { vals = sheet.getRange(p.first, 8, blocks.length, 1).getValues(); } catch (e) { vals = []; }
  }
  return blocks.map(function (b, i) {
    var v = vals[i] ? vals[i][0] : null;
    return { k: Math.round(b.to / 50), v: typeof v === 'number' && isFinite(v) && v > 0 ? v : swimQuickNum_(b.dak) };
  });
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
  var values = swimSummaryValues_(race, analysis, kpi, sheetUrl, pdf);
  var r = row || sh.getLastRow() + 1;
  sh.getRange(r, 1, 1, values.length).setValues([values]);
}

/** Η γραμμή ενός αγώνα στο "Αναλύσεις Αγώνων" (με τις στήλες του SWIM_SUMMARY_HEADERS). */
function swimSummaryValues_(race, analysis, kpi, sheetUrl, pdf) {
  var num = function (v) { return typeof v === 'number' && isFinite(v) ? Math.round(v * 100) / 100 : ''; };
  var t15 = null;
  analysis.rows.forEach(function (r) { if (r.d === 15) t15 = r.t; });
  var idValue = /^\d+$/.test(race.athleteId) ? Number(race.athleteId) : race.athleteId;
  return [idValue, race.athlete, race.club, race.category, race.date, race.competition, race.location, race.round,
    swimIndexEventLabel_(race) + (race.dq ? ' (DQ)' : ''), race.distanceLabel, swimPoolLabel_(race),
    num(kpi.finalTime), num(kpi.avgVel), num(kpi.totalStrokes), num(kpi.sr), num(kpi.dps), num(kpi.dak),
    t15 === null ? '' : Math.round(t15 * 100) / 100, swimSplits50_(analysis), sheetUrl || '', pdf || '', race.id,
    Utilities.formatDate(new Date(), Session.getScriptTimeZone(), 'dd/MM/yyyy HH:mm')];
}

/** KPI από την ανάλυση (χωρίς φύλλο report), για αγώνες που καταχωρίζονται χωρίς report (βιβλιοθήκη). */
function swimKpisFromAnalysis_(analysis) {
  var s = analysis.summary || {};
  return { finalTime: s.finalTime, avgVel: s.avgVel, totalStrokes: s.totalStrokes, sr: s.avgSr, dps: s.avgDps, dak: s.avgDak };
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

  // Πισίνα: 50 / 25 ή κείμενο ("25y", "25μ.", "SCY", "SCM", "LCM").
  var poolNum = swimNum_(r.pool);
  if (poolNum === null) {
    var poolText = String(r.pool === undefined || r.pool === null ? '' : r.pool);
    var pm = poolText.match(/\d+/);
    poolNum = pm ? Number(pm[0]) : (swimNormText_(poolText).indexOf('SC') === 0 ? 25 : 50);
  }
  var unit = swimPoolUnit_(r.poolUnit, r.pool);

  var race = {
    id: String(r.id || ''),
    kind: String(r.kind || '') === 'training' ? 'training' : 'race',
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
    pool: poolNum || 50,
    poolUnit: unit,
    k: unit === 'yd' ? SWIM_YARD : 1,
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
    strokeRounding: r.strokeRounding === undefined || r.strokeRounding === null ? 0.5 : (swimNum_(r.strokeRounding) || 0),
    reference: r.reference && typeof r.reference === 'object' ? r.reference : null,
    // Ο τελικός του αγωνίσματος από το Kinovea (π.χ. οι 8 του ολυμπιακού τελικού).
    finals: swimFinalsPayload_(r.finals),
    compare: r.compare && typeof r.compare === 'object' ? r.compare : {},
    tech: swimNormalizeTech_(r.tech),
    // Αναλύσεις της βιβλιοθήκης (παλιά Excel): χεριές ανά τμήμα αντί για χρόνους χεριών, σκέλος σκυταλοδρομίας, ακύρωση.
    segmentStrokes: swimSegmentStrokes_(r.segmentStrokes),
    strokesPer50: !!r.strokesPer50,
    relayLeg: r.relayLeg ? String(r.relayLeg) : '',
    dq: !!r.dq,
    source: r.source ? String(r.source) : '',
    // Το καρέ του τερματισμού με τα γραφικά του Kinovea (εικόνα στο Drive).
    finalFrame: swimFinalFrame_(r.finalFrame),
    // Τα γραφικά του βίντεο για το report: η κάρτα σύνοψης και ο αγώνας απέναντι στον τελικό (εικόνες στο Drive).
    graphics: swimGraphics_(r.graphics),
    // Τα γραφήματα του report σχεδιασμένα από το Kinovea (εικόνες στο Drive), με το hash των τιμών από τις οποίες σχεδιάστηκαν.
    chartImages: swimChartImages_(r.chartImages)
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

/**
 * Οι φιναλίστ του τελικού του αγωνίσματος, όπως τους στέλνει το Kinovea από το αρχείο τελικών του υπολογιστή του αναλυτή
 * (π.χ. οι 8 του ολυμπιακού τελικού): { label, source, swimmers: [{ rank, name, times: [{ d, t }] }] }, αλλιώς null.
 */
function swimFinalsPayload_(raw) {
  if (!raw || typeof raw !== 'object' || !Array.isArray(raw.swimmers)) return null;
  var swimmers = raw.swimmers.slice(0, 8).map(function (s) {
    if (!s || typeof s !== 'object') return null;
    var times = (Array.isArray(s.times) ? s.times : []).map(function (p) {
      return { d: swimNum_(p && p.d), t: swimNum_(p && p.t) };
    }).filter(function (p) { return p.d !== null && p.d > 0 && p.t !== null && p.t > 0; });
    return times.length ? { rank: swimNum_(s.rank), name: String(s.name || '').substring(0, 60), times: times } : null;
  }).filter(function (s) { return s; });
  if (swimmers.length < 2) return null;
  var text = function (v, n) { return String(v === null || v === undefined ? '' : v).substring(0, n || 40); };
  var metrics = (Array.isArray(raw.metrics) ? raw.metrics : []).slice(0, 200).map(function (m) {
    if (!m || typeof m !== 'object' || !m.label) return null;
    return { group: text(m.group, 40), label: text(m.label, 60), athlete: text(m.athlete), rank: text(m.rank, 10), winner: text(m.winner),
      mean: text(m.mean), best: text(m.best), diff: text(m.diff, 20) };
  }).filter(function (m) { return m; });
  return { label: String(raw.label || 'Τελικός').substring(0, 80), source: String(raw.source || '').substring(0, 120), swimmers: swimmers, metrics: metrics };
}

/**
 * Ο αγώνας ανάμεσα στους φιναλίστ του τελικού του αγωνίσματος, τιμή προς τιμή όπως στο Kinovea (εκκίνηση, 15μ., περάσματα,
 * στροφές, τελευταία 5μ., χεριές, συχνότητα, μήκος κύκλου): ο αθλητής, η θέση του, ο νικητής, ο μέσος όρος, ο καλύτερος και η
 * διαφορά από τον νικητή. Οι τιμές γράφονται στο layout.inputs.blocks (αλλάζουν χωρίς ανακατασκευή του φύλλου).
 */
function swimLayoutFinalsMetrics_(L) {
  var g = L.g, race = L.race, cur = L.cur;
  var fm = race.finals && race.finals.metrics ? race.finals.metrics : [];
  if (!fm.length) return;
  var put = function (r, c, v) { L.layout.inputs.blocks.push({ r: r, c: c, values: [[v]] }); };
  // Σε όλο το πλάτος (A-O), με ενωμένα κελιά: η ετικέτα της τιμής και οι τιμές με τις μονάδες τους χωρούν χωρίς να κόβονται.
  var cols = [[1, 1, 'Φάση'], [2, 4, 'Τιμή'], [5, 6, 'Αθλητής'], [7, 7, 'Θέση'], [8, 9, 'Νικητής'], [10, 11, 'Μέσος όρος'],
    [12, 13, 'Καλύτερος'], [14, 15, 'Από τον νικητή']];
  var span = function (r, a, b, v, fmt) {
    g.set(r, a, v, fmt);
    for (var c = a + 1; c <= b; c++) g.set(r, c, '', fmt);
    if (b > a) g.merge(r, a, r, b);
  };
  cur = swimKeep_(L, cur, 'finalsMetrics');
  g.section(cur, swimT_('Ο αγώνας ανάμεσα στους φιναλίστ: ') + swimL_(race.finals.label) + ' (' + race.finals.swimmers.length + ')', L.LAST);
  cur++;
  var head = { bg: SWIM_COLORS.header, color: SWIM_COLORS.headerText, bold: true, wrap: true, align: 'center', valign: 'middle', size: 8 };
  cols.forEach(function (col) { span(cur, col[0], col[1], swimT_(col[2]), head); });
  g.rowHeights[cur] = 30;
  var first = cur + 1;
  fm.forEach(function (m, i) {
    var r = first + i;
    // Οι τιμές του Kinovea στα ελληνικά ("Στροφή 50μ.", "52.5 κύκλοι/λ.") στη γλώσσα του report.
    var vals = [m.group, m.label, m.athlete, m.rank, m.winner, m.mean, m.best, m.diff].map(swimL_);
    cols.forEach(function (col, j) {
      var fmt = swimF_('@', j < 2 ? 'label' : 'auto');
      if (j === 2 || j === 3) fmt.bold = true;
      span(r, col[0], col[1], '', fmt);
      put(r, col[0], vals[j] || '');
    });
  });
  var last = first + fm.length - 1;
  g.box(cur, 1, last, L.LAST);
  swimKeepEnd_(L, 'finalsMetrics', last, first + 2);
  L.cur = last + 2;
}

/**
 * Τα γραφικά του βίντεο από το Kinovea: { summary: fileId, versus: fileId } (μόνο όσα έχουν εικόνα στο Drive). Δέχεται τη λίστα
 * του Kinovea [{ id, fileId }] και τη μορφή του αγώνα όπως κρατιέται στο ευρετήριο (για τα PDF από το μενού).
 */
function swimGraphics_(raw) {
  var out = {};
  if (raw && typeof raw === 'object' && !Array.isArray(raw)) {
    raw = Object.keys(raw).map(function (k) { return { id: k, fileId: raw[k] }; });
  }
  (Array.isArray(raw) ? raw : []).forEach(function (x) {
    if (!x || typeof x !== 'object') return;
    var id = String(x.id || ''), fileId = swimDriveId_(x.fileId);
    if ((id === 'summary' || id === 'versus') && fileId) out[id] = fileId;
  });
  return out;
}

/**
 * Τα γραφήματα που σχεδίασε το Kinovea: { "chart-…": { fileId, hash } } (μόνο με έγκυρο id του Drive). Το hash είναι των τιμών
 * και των ρυθμίσεων του γραφήματος (swimChartData_) τη στιγμή που σχεδιάστηκε: αν άλλαξαν, μπαίνει γράφημα του Google.
 */
function swimChartImages_(raw) {
  var out = {};
  // Και η μορφή του ευρετηρίου { "chart-…": { fileId, hash } } (ο αγώνας όπως κρατιέται, για τα PDF από το μενού).
  if (raw && typeof raw === 'object' && !Array.isArray(raw)) {
    raw = Object.keys(raw).map(function (k) { var v = raw[k] || {}; return { id: k, fileId: v.fileId, hash: v.hash }; });
  }
  (Array.isArray(raw) ? raw : []).slice(0, 60).forEach(function (x) {
    if (!x || typeof x !== 'object') return;
    var id = String(x.id || ''), fileId = swimDriveId_(x.fileId), hash = String(x.hash || '');
    if (/^chart-[0-9a-f]{8}$/.test(id) && fileId && /^[0-9a-f]{8}$/.test(hash)) out[id] = { fileId: fileId, hash: hash };
  });
  return out;
}

/** { fileId, t }: το τελικό καρέ του Kinovea (t: χρόνος του καρέ από το σήμα), αλλιώς null. */
function swimFinalFrame_(raw) {
  if (!raw || typeof raw !== 'object') return null;
  var id = swimDriveId_(raw.fileId);
  return id ? { fileId: id, t: swimNum_(raw.t) } : null;
}

/** { "15": 3, "25": 7.5, … }: χεριές του τμήματος που τελειώνει σε κάθε σημείο (αριθμοί ≥ 0), αλλιώς null. */
function swimSegmentStrokes_(raw) {
  if (!raw || typeof raw !== 'object') return null;
  var out = {}, any = false;
  Object.keys(raw).forEach(function (k) {
    var d = swimNum_(k), n = swimNum_(raw[k]);
    if (d === null || d <= 0 || n === null || n < 0) return;
    out[String(d)] = n;
    any = true;
  });
  return any ? out : null;
}

/** Μονάδα της πισίνας: 'yd' για γιάρδες (π.χ. "25y", "SCY", "yd"), αλλιώς 'm'. */
function swimPoolUnit_(unit, pool) {
  var u = swimNormText_(unit) + ' ' + swimNormText_(pool);
  return /(^|[^A-ZΑ-Ω])(Y|YD|YDS|YARD|YARDS|SCY|ΓΙΑΡΔ|ΓΙΑΡΝΤ)/.test(u) ? 'yd' : 'm';
}

/** "μ." ή "yd" για τις ετικέτες αποστάσεων ("m" στο αγγλικό report). */
function swimUnitLabel_(race) {
  return race && race.poolUnit === 'yd' ? 'yd' : swimT_('μ.');
}

/** "50μ.", "25μ.", "25yd" (η τιμή της στήλης Πισίνα)· shown: στη γλώσσα του report ("50m"). */
function swimPoolLabel_(race, shown) {
  return race.pool + (race.poolUnit === 'yd' ? 'yd' : (shown ? swimT_('μ.') : 'μ.'));
}

/** Τεχνική ανάλυση (εκκινήσεις, στροφές): σημάδια σε δευτερόλεπτα, γωνίες, στιγμιότυπα. */
function swimNormalizeTech_(raw) {
  var t = raw && typeof raw === 'object' ? raw : {};
  var tech = {
    phases: [],
    angles: [],
    snapshots: [],
    values: t.values && typeof t.values === 'object' ? t.values : {}
  };
  (t.phases || []).forEach(function (p) {
    if (!p || typeof p !== 'object') return;
    tech.phases.push({
      id: String(p.id || ''), kind: p.kind === 'turn' ? 'turn' : 'start', wall: swimNum_(p.wall) || 0,
      attempt: swimNum_(p.attempt) || 0, origin: swimNum_(p.origin), label: String(p.label || ''),
      values: p.values && typeof p.values === 'object' ? p.values : {}
    });
  });
  (t.angles || []).forEach(function (a) {
    if (!a || typeof a !== 'object') return;
    var deg = swimNum_(a.deg);
    if (deg === null) return;
    tech.angles.push({ id: String(a.id || ''), name: String(a.name || 'Γωνία'), t: swimNum_(a.t), deg: deg, phase: String(a.phase || '') });
  });
  (t.snapshots || []).forEach(function (s) {
    if (!s || typeof s !== 'object') return;
    tech.snapshots.push({
      id: String(s.id || ''), t: swimNum_(s.t), phase: String(s.phase || ''), stage: String(s.stage || ''), event: String(s.event || ''),
      comment: String(s.comment || ''), fileId: String(s.fileId || ''),
      eliteFileId: String(s.eliteFileId || ''), eliteLabel: String(s.eliteLabel || ''), eliteComment: String(s.eliteComment || '')
    });
  });
  return tech;
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
  var k = race.k || 1;
  var yards = race.poolUnit === 'yd';
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

    // Ταχύτητες σε m/s (στις γιάρδες και σε yd/s).
    row.segTime = (row.t !== null && tPrev !== null) ? row.t - tPrev : null;
    row.vel = row.segTime !== null && row.segTime > 0 ? (row.d - row.prevD) * k / row.segTime : null;
    row.velYd = yards && row.segTime !== null && row.segTime > 0 ? (row.d - row.prevD) / row.segTime : null;
    row.avgVel = row.t !== null && row.t > 0 ? row.d * k / row.t : null;

    if (row.t !== null) {
      if (lastT !== null && row.t <= lastT) warnings.push(swimTF_('Ο χρόνος στα {0} δεν είναι μεγαλύτερος από τον προηγούμενο.', row.d + swimUnitLabel_(race)));
      lastT = row.t;
    }

    row.strokes = null;
    row.srExact = null;
    if (hasStrokes && row.t !== null && tPrev !== null) {
      var nA = swimRound_(swimCumulativeStrokes_(race.strokes, barriers, tPrev), step);
      var nB = swimRound_(swimCumulativeStrokes_(race.strokes, barriers, row.t), step);
      row.strokes = Math.max(0, swimRound_(nB - nA, step > 0 ? step : 0));
      // Ύπτιο μετά την εκκίνηση: η πρώτη χεριά είναι αυτή που φέρνει τον αθλητή στην επιφάνεια (η ανάδυση)· τα φύλλα των
      // αναλυτών μετρούν από την επόμενη (όπως και το Kinovea).
      if (i === 0 && row.style === 'bk' && race.strokes[0] <= row.t) row.strokes = Math.max(0, row.strokes - 1);

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
    // Χεριές ανά τμήμα όπως γράφτηκαν: αναφορές χωρίς πατήματα (π.χ. από το Excel) ή αριθμοί που επιβεβαίωσε / διόρθωσε
    // ο αναλυτής στο Kinovea σε κάθε πέρασμα. Έχουν προτεραιότητα από το μέτρημα των πατημάτων.
    row.strokesTyped = false;
    if (race.segmentStrokes && race.segmentStrokes[row.d] !== undefined && race.segmentStrokes[row.d] !== null && row.t !== null) {
      row.strokes = race.segmentStrokes[row.d];
      row.strokesTyped = true;
    }

    // Συχνότητα και μήκος χεριάς μόνο στην καθαρή κολύμβηση (15μ. μετά τον τοίχο έως 5μ. πριν τον επόμενο),
    // όπως στα φύλλα αγώνων του Excel: στα τελευταία 5μ. υπάρχει γλίστρημα και η αρχή της στροφής / του τερματισμού,
    // και στα 0-15μ. / +15μ. η υποβρύχια φάση, οπότε εκεί οι τιμές δεν είναι συχνότητα / μήκος χεριάς κολύμβησης.
    row.sr = (row.isFree && row.strokes && row.segTime > 0) ? (row.strokes / row.ppc) * 60 / row.segTime : null;
    row.dps = (row.isFree && row.strokes) ? (row.d - row.prevD) * k / row.strokes : null;
    row.sl = (row.isFree && row.strokes) ? (row.d - row.prevD) * k / (row.strokes / row.ppc) : null;
    row.dpsYd = yards && row.dps !== null ? row.dps / k : null;
    row.slYd = yards && row.sl !== null ? row.sl / k : null;
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
  start.flightTime = start.blockTime !== null && start.entryTime !== null && start.entryTime > start.blockTime ? start.entryTime - start.blockTime : null;
  start.t15 = timeAt[15] !== undefined ? timeAt[15] : null;
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
    // Στις σκυταλοδρομίες, στο τέλος κάθε διαδρομής η "στροφή" είναι η αλλαγή: ο επόμενος μπορεί να φύγει λίγο πριν την επαφή.
    res.exchange = race.relay && Math.abs((wall / (dist / 4)) - Math.round(wall / (dist / 4))) < 1e-9;
    // Ανάποδη στροφή: ελεύθερο ή ύπτιο, ίδιο στυλ πριν και μετά τον τοίχο. Οι υπόλοιπες (πεταλούδα, πρόσθιο, αλλαγή στυλ
    // στη μικτή) είναι ανοιχτές, με επαφή των χεριών στον τοίχο.
    res.styleIn = swimStyleAt_(race, wall);
    res.styleOut = swimStyleAt_(race, wall + pool);
    res.flip = !res.exchange && (res.styleIn === 'fs' || res.styleIn === 'bk') && res.styleOut === res.styleIn;
    // Χρόνοι -5/+15 (20 μονάδες) και τοίχος → +15 (όπως οι πίνακες επιπέδων "Τοίχος έως τα 15m κεφάλι").
    res.inOut = timeAt[wall - 5] !== undefined && timeAt[wall + 15] !== undefined ? timeAt[wall + 15] - timeAt[wall - 5] : null;
    res.out15 = tw !== null && timeAt[wall + 15] !== undefined ? timeAt[wall + 15] - tw : null;
    res.in5 = tw !== null && timeAt[wall - 5] !== undefined ? tw - timeAt[wall - 5] : null;
    if (tw === null) return res;
    var nextT = swimFirst_(knownTimes, function (t) { return t > tw + 1e-9; });
    if (nextT === null) nextT = Infinity;
    var prevT = swimLast_(knownTimes, function (t) { return t < tw - 1e-9; });
    if (prevT === null) prevT = 0;

    var fromT = res.exchange ? tw - 0.5 : tw;
    res.pushoff = swimFirst_(race.feetOff, function (t) { return t > fromT && t < nextT; });
    res.breakout = swimFirst_(race.breakouts, function (t) { return t > tw && t < nextT; });
    var ls = swimLast_(race.strokes, function (t) { return t < tw && t > prevT; });
    if (ls !== null) res.lastStrokeGap = tw - ls;
    // Χρόνος στροφής: ανάποδη από την τελευταία είσοδο χεριού έως την αποκόλληση, ανοιχτή από την επαφή στον τοίχο
    // έως την αποκόλληση, αλλαγή σκυταλοδρομίας από την επαφή έως την αποδέσμευση του επόμενου.
    if (res.pushoff !== null) {
      if (!res.flip) res.turnTime = res.pushoff - tw;
      else if (ls !== null) res.turnTime = res.pushoff - ls;
    }
    if (res.breakout !== null) res.underwaterTime = res.breakout - (res.pushoff !== null ? res.pushoff : tw);
    if (race.kicks.length) {
      var to = res.breakout !== null ? res.breakout : nextT;
      res.kicks = race.kicks.filter(function (t) { return t > tw && t <= to; }).length;
    }
    return res;
  });

  // ---- Τερματισμός
  var finish = { tFinish: timeAt[dist] !== undefined ? timeAt[dist] : null, lastStrokeGap: null };
  if (finish.tFinish !== null) {
    var prevWallT = swimLast_(knownTimes, function (t) { return t < finish.tFinish - 1e-9; });
    var lastStroke = swimLast_(race.strokes, function (t) { return t < finish.tFinish && (prevWallT === null || t > prevWallT); });
    if (lastStroke !== null) finish.lastStrokeGap = finish.tFinish - lastStroke;
    finish.last5 = timeAt[dist - 5] !== undefined ? finish.tFinish - timeAt[dist - 5] : null;
    finish.last15 = timeAt[dist - 15] !== undefined ? finish.tFinish - timeAt[dist - 15] : null;
  }

  // ---- Ανά 50 (με τον ΔΑΚ)
  var blocks = swimBlocks50_(race, rows);

  // ---- Σύνοψη
  var totalStrokes = null;
  if (hasStrokes || race.segmentStrokes) {
    totalStrokes = 0;
    rows.forEach(function (r) { if (r.strokes) totalStrokes += r.strokes; });
  }
  // Μέση συχνότητα της καθαρής κολύμβησης, σταθμισμένη με τον χρόνο (κύκλοι / χρόνος).
  var cyc = 0, cycT = 0, cs = 0, cd = 0;
  rows.forEach(function (r) { if (r.sr !== null) { cyc += r.strokes / r.ppc; cycT += r.segTime; cs += r.strokes; cd += (r.d - r.prevD) * k; } });
  var daks = blocks.filter(function (b) { return b.dak !== null; }).map(function (b) { return b.dak; });
  var summary = {
    finalTime: finish.tFinish,
    avgVel: finish.tFinish ? dist * k / finish.tFinish : null,
    totalStrokes: totalStrokes,
    avgSr: cycT > 0 ? cyc * 60 / cycT : null,
    avgDps: cs > 0 ? cd / cs : null,
    avgSl: cyc > 0 ? cd / cyc : null,
    avgDak: daks.length ? daks.reduce(function (a, b) { return a + b; }, 0) / daks.length : null,
    marked: rows.filter(function (r) { return r.t !== null; }).length,
    total: rows.length
  };
  if (summary.avgSr === null && race.strokesPer50) {
    var wc = 0, wt = 0, ws = 0, wd = 0;
    blocks.forEach(function (b) {
      if (!b.whole) return;
      wc += b.strokes / swimPressesPerCycle_(b.style); wt += b.time; ws += b.strokes; wd += 50 * k;
    });
    if (wt > 0) { summary.avgSr = wc * 60 / wt; summary.avgDps = wd / ws; summary.avgSl = wd / wc; summary.whole50 = true; }
  }
  summary.startTime = start.t15;
  summary.turnsTime = null;
  var inOuts = turns.filter(function (t) { return t.inOut !== null; });
  if (inOuts.length) summary.turnsTime = inOuts.reduce(function (s, t) { return s + t.inOut; }, 0);
  summary.finishTime = finish.last5 === undefined ? null : finish.last5;

  return { rows: rows, start: start, turns: turns, finish: finish, blocks: blocks, summary: summary, warnings: warnings, turnWalls: turnWalls };
}

/**
 * Τα 50άρια του αγώνα (σε μονάδες της πισίνας) με χρόνο, χεριές, Swolf, ΔΑΚ και τα σταθμισμένα της καθαρής κολύμβησης.
 * ΔΑΚ (Excel): (Χεριές + Χρόνος από τα 15 έως τον τοίχο) / 3,5, «για την αναγωγή ανά 10μ.». Γενικά, για κάθε μήκος του 50άρι
 * το παράθυρο είναι από τα 15 έως τον τοίχο και ο διαιρέτης είναι η απόστασή του σε μέτρα / 10: πισίνα 50μ. 3,5,
 * πισίνα 25μ. 1,0 ανά μήκος (2,0 για το 50άρι), 25yd 0,9144 ανά μήκος. Στις γιάρδες βγαίνει και «ανά 10yd».
 */
function swimBlocks50_(race, rows) {
  var pool = race.pool, k = race.k || 1, dist = race.distance;
  var byD = {};
  rows.forEach(function (r) { byD[r.d] = r; });
  var inRange = function (r, a, b) { return r.prevD >= a - 1e-9 && r.d <= b + 1e-9; };
  var blocks = [];
  for (var b = 0; b + 50 <= dist + 1e-9; b += 50) {
    var e = b + 50;
    var re = byD[e];
    if (!re) continue;
    var rs = b === 0 ? null : byD[b];
    var blk = { from: b, to: e, cum: re.t, time: null, strokes: null, swolf: null, dak: null, dakYd: null, dakWindow: null,
      sr: null, dps: null, sl: null, vel: null, velYd: null, style: swimStyleAt_(race, e) };
    if (re.t !== null && (b === 0 || (rs && rs.t !== null))) blk.time = re.t - (b === 0 ? 0 : rs.t);
    var inBlock = rows.filter(function (r) { return inRange(r, b, e); });
    if (inBlock.some(function (r) { return r.strokes !== null; }))
      blk.strokes = inBlock.reduce(function (s, r) { return s + (r.strokes || 0); }, 0);
    if (blk.time !== null && blk.strokes !== null) blk.swolf = blk.time + blk.strokes;
    if (blk.time !== null && blk.time > 0) {
      blk.vel = 50 * k / blk.time;
      if (race.poolUnit === 'yd') blk.velYd = 50 / blk.time;
    }

    var sumS = 0, sumT = 0, sumW = 0, ok = true, any = false;
    for (var w = b; w < e - 1e-9; w += pool) {
      var a15 = byD[w + 15], aw = byD[Math.min(w + pool, e)];
      if (!a15 || !aw || a15.t === null || aw.t === null || aw.d <= a15.d) { ok = false; break; }
      var win = rows.filter(function (r) { return inRange(r, a15.d, aw.d); });
      win.forEach(function (r) { if (r.strokes !== null) { any = true; sumS += r.strokes; } });
      sumT += aw.t - a15.t;
      sumW += aw.d - a15.d;
    }
    if (ok && any && sumW > 0) {
      blk.dak = (sumS + sumT) / (sumW * k / 10);
      blk.dakWindow = sumW * k / 10;
      if (race.poolUnit === 'yd') blk.dakYd = (sumS + sumT) / (sumW / 10);
    }

    var cyc = 0, ct = 0, cs = 0, cd = 0;
    rows.forEach(function (r) {
      if (r.sr === null || !inRange(r, b, e)) return;
      cyc += r.strokes / r.ppc; ct += r.segTime; cs += r.strokes; cd += (r.d - r.prevD) * k;
    });
    blk.sr = ct > 0 ? cyc * 60 / ct : null;
    blk.dps = cs > 0 ? cd / cs : null;
    blk.sl = cyc > 0 ? cd / cyc : null;
    // Χεριές ανά 50 (παλιό Excel 400-1500μ.): συχνότητα και μήκος χεριάς σε όλο το 50, όπως τα υπολόγιζε το Excel.
    if (race.strokesPer50 && blk.sr === null && blk.strokes && blk.time > 0) {
      var ppc50 = swimPressesPerCycle_(blk.style);
      blk.sr = (blk.strokes / ppc50) * 60 / blk.time;
      blk.dps = 50 * k / blk.strokes;
      blk.sl = 50 * k / (blk.strokes / ppc50);
      blk.whole = true;
    }
    blocks.push(blk);
  }
  return blocks;
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
  // Ύψη γραμμών του σχεδίου (px στις γραμματοσειρές του layout· στο φύλλο × SWIM_FONT_SCALE και όσο χρειάζεται
  // το κείμενο) και ακριβή ύψη (κενές γραμμές των σελίδων, σχόλια): βλ. swimRowHeight_.
  this.rowHeights = {};
  this.exactHeights = {};
  this.byRow = {};
  this.mergeAt = {};
  this.covered = {};
  this.need = {};
}

SwimGrid_.prototype.set = function (r, c, v, fmt) {
  var key = r + ':' + c;
  var cell = this.cells[key] || { r: r, c: c };
  if (v !== undefined) cell.v = v;
  if (fmt) {
    for (var k in fmt) if (Object.prototype.hasOwnProperty.call(fmt, k)) cell[k] = fmt[k];
  }
  this.cells[key] = cell;
  (this.byRow[r] = this.byRow[r] || {})[c] = cell;
  delete this.need[r];
  if (r > this.maxRow) this.maxRow = r;
  if (c > this.maxCol) this.maxCol = c;
  return cell;
};

SwimGrid_.prototype.get = function (r, c) {
  return this.cells[r + ':' + c];
};

SwimGrid_.prototype.merge = function (r, c, r2, c2) {
  this.merges.push([r, c, r2, c2]);
  this.mergeAt[r + ':' + c] = [r, c, r2, c2];
  for (var rr = r; rr <= r2; rr++) {
    for (var cc = c; cc <= c2; cc++) if (rr !== r || cc !== c) this.covered[rr + ':' + cc] = true;
    delete this.need[rr];
  }
};

SwimGrid_.prototype.box = function (r, c, r2, c2) {
  this.boxes.push([r, c, r2, c2]);
};

SwimGrid_.prototype.section = function (r, text, lastCol) {
  this.set(r, 1, text, { bg: SWIM_COLORS.section, color: SWIM_COLORS.sectionText, bold: true, size: 11 });
  for (var c = 2; c <= lastCol; c++) this.set(r, c, '', { bg: SWIM_COLORS.section });
  this.merge(r, 1, r, lastCol);
  this.rowHeights[r] = 26;
};

SwimGrid_.prototype.headers = function (r, labels, c0) {
  var self = this, first = c0 || 1;
  labels.forEach(function (label, i) {
    self.set(r, first + i, label, { bg: SWIM_COLORS.header, color: SWIM_COLORS.headerText, bold: true, wrap: true, align: 'center', valign: 'middle', size: 8 });
  });
  this.rowHeights[r] = 44;
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
  else if (kind === 'label') { base.bg = SWIM_COLORS.label; base.bold = true; base.color = SWIM_COLORS.title; base.wrap = true; }
  base.align = 'center';
  return base;
}

/** Σύντομο hash (FNV-1a, 32 bit) για τις υπογραφές του layout. */
function swimHash_(s) {
  var h = 0x811c9dc5;
  var str = String(s);
  for (var i = 0; i < str.length; i++) {
    h ^= str.charCodeAt(i);
    h = (h + ((h << 1) + (h << 4) + (h << 7) + (h << 8) + (h << 24))) >>> 0;
  }
  return ('0000000' + h.toString(16)).slice(-8);
}

/**
 * Υπογραφή της δομής του φύλλου: όταν αλλάζει, το φύλλο ξαναφτιάχνεται. Περιέχει τα σημεία ελέγχου, την πισίνα,
 * τις συγκρίσεις (οι τιμές τους γράφονται στατικά) και τη δομή της τεχνικής ανάλυσης (οι τιμές της όχι: γράφονται
 * σε κάθε ενημέρωση, όπως οι χρόνοι).
 */
function swimLayoutSignature_(race, extras, pageHeight) {
  var x = extras || {};
  var sig = function (o) { return o ? String(o.id || '') + ':' + swimHash_(JSON.stringify(o.race)) : ''; };
  var s = {
    // 10 (2.9.5): ο ΔΑΚ κάθε 50άρι στη στήλη L των χρόνων· τα report ξαναφτιάχνονται μία φορά.
    v: SWIM_DAK50 ? 10 : 9,
    kind: race.kind,
    d: race.checkpoints.map(function (c) { return c.d; }),
    pool: race.pool,
    unit: race.poolUnit,
    dist: race.distance,
    style: race.style,
    relay: race.relay,
    pb: sig(x.pb),
    elite: sig(x.elite),
    finalists: (x.finalists || []).length ? swimHash_(JSON.stringify([x.finalsLabel || ''].concat(x.finalists.map(function (f) {
      return [f.label, f.race.checkpoints, f.race.segmentStrokes || null];
    })))) : '',
    finalsMetrics: race.finals && race.finals.metrics && race.finals.metrics.length ? race.finals.metrics.length + ':' + race.finals.label : '',
    bench: x.benchmarks ? swimHash_(JSON.stringify(x.benchmarks)) : '',
    tech: swimTechSignature_(race),
    final: race.finalFrame ? race.finalFrame.fileId : '',
    graphics: race.graphics ? [race.graphics.summary || '', race.graphics.versus || ''].join(',') : '',
    page: pageHeight || 0
  };
  // Η γλώσσα (2.7.7): ένα report στην άλλη γλώσσα ξαναφτιάχνεται· τα ελληνικά χωρίς κλειδί, όπως πριν.
  if (SWIM_LANG_ === 'en') s.lang = 'en';
  return JSON.stringify(s);
}

// ---------------------------------------------------------------------------------------------
// Σελίδες του PDF: πίνακες, γραφήματα και εικόνες δεν κόβονται ανάμεσα σε δύο σελίδες
//
// Το PDF του Google Sheets αλλάζει σελίδα ανάμεσα σε γραμμές (μια γραμμή δεν κόβεται ποτέ), ενώ τα γραφήματα και
// οι εικόνες πάνω από τα κελιά κόβονται όπου πέσει η αλλαγή σελίδας. Το layout φτιάχνεται δύο φορές: η πρώτη μετρά
// το ύψος κάθε ενότητας (swimKeep_ … swimKeepEnd_), η δεύτερη βάζει πριν από όποια ενότητα δεν χωρά στο υπόλοιπο της
// σελίδας λεπτές κενές γραμμές ως το τέλος της, ώστε η ενότητα να αρχίζει στην επόμενη. Όλες οι γραμμές του φύλλου
// έχουν σταθερό ύψος (swimRenderLayout_), οπότε οι σελίδες πέφτουν εκεί που υπολογίστηκαν.
// ---------------------------------------------------------------------------------------------

/**
 * Ευανάγνωστο PDF: οι γραμματοσειρές του layout × 1.2 στο φύλλο και στενότερες στήλες (A 110, N 52, οι άλλες 74,
 * σύνολο A-O 1124 px), ώστε στη σελίδα A4 τα νούμερα να βγαίνουν ~8 pt (πριν ~5 pt).
 */
var SWIM_FONT_SCALE = 1.2;
/** Ύψος γραμμής χωρίς άλλη ρύθμιση (px): κείμενο 12 pt. */
var SWIM_ROW_H = 25;
/** Ύψος γραμμής κειμένου ως προς το μέγεθος της γραμματοσειράς (Roboto, με λίγο περιθώριο). */
var SWIM_LINE = 1.17;

/** Μέγεθος γραμματοσειράς στο φύλλο (pt) για μέγεθος του layout (10 αν δεν ορίζεται). */
function swimFontSize_(size) {
  return Math.round((size || 10) * SWIM_FONT_SCALE);
}

/** Πλάτη χαρακτήρων της Roboto (em): a-z, A-Z, α-ω, Α-Ω (U+03A2 δεν υπάρχει). */
var SWIM_EM = {
  latinLower: [.53, .55, .51, .55, .52, .34, .55, .54, .24, .24, .5, .24, .86, .54, .56, .55, .55, .33, .5, .32, .54, .47, .74, .49, .46, .49],
  latinUpper: [.65, .62, .65, .66, .57, .55, .68, .71, .27, .55, .63, .54, .87, .71, .69, .63, .69, .62, .59, .6, .65, .64, .89, .63, .6, .6],
  greekLower: [.56, .56, .48, .55, .5, .46, .55, .55, .27, .52, .5, .57, .48, .46, .56, .6, .56, .48, .57, .5, .54, .7, .5, .72, .8],
  greekUpper: [.65, .62, .54, .69, .57, .6, .71, .69, .27, .63, .65, .87, .71, .57, .69, .71, .63, .6, .58, .6, .6, .76, .63, .76, .66],
  // τονισμένα → το βασικό γράμμα (+ λίγο για τον τόνο στα κεφαλαία)
  accented: { 0x3AC: 0x3B1, 0x3AD: 0x3B5, 0x3AE: 0x3B7, 0x3AF: 0x3B9, 0x3CC: 0x3BF, 0x3CD: 0x3C5, 0x3CE: 0x3C9, 0x3CA: 0x3B9, 0x3CB: 0x3C5,
    0x390: 0x3B9, 0x3B0: 0x3C5, 0x386: 0x391, 0x388: 0x395, 0x389: 0x397, 0x38A: 0x399, 0x38C: 0x39F, 0x38E: 0x3A5, 0x38F: 0x3A9 },
  other: { 32: .25, 46: .26, 44: .22, 58: .24, 59: .22, 40: .35, 41: .35, 91: .27, 93: .27, 45: .29, 47: .41, 37: .74, 43: .57,
    61: .55, 39: .17, 34: .32, 33: .26, 63: .47, 38: .63, 42: .43, 95: .45, 60: .51, 62: .52, 183: .26, 0x387: .26, 176: .38,
    177: .54, 0x2013: .55, 0x2014: .9, 0x2192: .9, 0x2190: .9 }
};

/** Πλάτος χαρακτήρα της Roboto σε em (κατά προσέγγιση). */
function swimCharEm_(code) {
  var extra = 0;
  if (SWIM_EM.accented[code]) { extra = code < 0x390 ? 0.08 : 0; code = SWIM_EM.accented[code]; }
  if (code >= 48 && code <= 57) return 0.56;
  if (code >= 97 && code <= 122) return SWIM_EM.latinLower[code - 97];
  if (code >= 65 && code <= 90) return SWIM_EM.latinUpper[code - 65];
  if (code >= 0x3B1 && code <= 0x3C9) return SWIM_EM.greekLower[code - 0x3B1];
  if (code >= 0x391 && code <= 0x3A9) return (SWIM_EM.greekUpper[code - 0x391 - (code > 0x3A1 ? 1 : 0)] || 0.65) + extra;
  var o = SWIM_EM.other[code];
  return o !== undefined ? o : 0.62;
}

/**
 * Γραμμές που πιάνει ένα κείμενο σε πλάτος width px (αναδίπλωση στις λέξεις, όπως στο Sheets). Ρυθμισμένο πάνω σε
 * PDF του Google Sheets: κεφαλίδες 8 pt bold σε στήλες 84 px έπιασαν 3 και 4 γραμμές.
 */
function swimTextLines_(text, width, px, bold) {
  var k = px * 1.02 * (bold ? 1.04 : 1);
  var measure = function (s) {
    var w = 0;
    for (var i = 0; i < s.length; i++) w += swimCharEm_(s.charCodeAt(i));
    return w * k;
  };
  var space = measure(' '), lines = 0;
  String(text).split('\n').forEach(function (para) {
    var count = 1, line = 0;
    para.split(' ').forEach(function (word) {
      if (!word) { line += space; return; }
      var ww = measure(word);
      if (line > 0 && line + space + ww <= width) { line += space + ww; return; }
      if (line > 0) { count++; line = 0; }
      while (ww > width) { count++; ww -= width; }
      line = ww;
    });
    lines += count;
  });
  return Math.max(1, lines);
}

/** Ύψος (px) για κείμενο σε πλάτος width px, με μέγεθος γραμματοσειράς του layout size. */
function swimTextHeight_(text, width, size, bold) {
  var px = swimFontSize_(size) * 4 / 3;
  return Math.ceil(swimTextLines_(text, width - 5, px, bold) * px * SWIM_LINE + 6);
}

/** Πόσο ύψος θέλει το περιεχόμενο μιας γραμμής: κείμενο (με αναδίπλωση στο πλάτος του κελιού), τύποι και αριθμοί. */
function swimRowNeed_(g, r) {
  var row = g.byRow[r], need = 0;
  if (!row) return 0;
  Object.keys(row).forEach(function (k) {
    var cell = row[k], c = Number(k);
    if (g.covered[r + ':' + c]) return;
    var m = g.mergeAt[r + ':' + c];
    if (m && m[2] > m[0]) return;
    var v = cell.v;
    var formula = typeof v === 'string' && v.charAt(0) === '=';
    var text = formula || v === null || v === undefined || v === '' ? '' : String(v);
    // Κενό κελί χωρίς δική του γραμματοσειρά (τιμές που γράφονται αργότερα): αρκεί το κανονικό ύψος.
    if (!text && !formula && !cell.size && !cell.bold) return;
    var width = 0;
    for (var cc = c; cc <= (m ? m[3] : c); cc++) width += swimColumnWidth_(cc);
    var px = swimFontSize_(cell.size) * 4 / 3;
    var lines = !text ? 1 : (cell.wrap ? swimTextLines_(text, width - 5, px, cell.bold) : text.split('\n').length);
    need = Math.max(need, Math.ceil(lines * px * SWIM_LINE + 6));
  });
  return need;
}

/**
 * Το ύψος μιας γραμμής στο φύλλο (px): ακριβές όπου ορίζεται (κενές γραμμές σελίδας, σχόλια), αλλιώς το ύψος του
 * σχεδίου × SWIM_FONT_SCALE ή όσο χρειάζεται το περιεχόμενο. Το ίδιο ύψος μπαίνει στο φύλλο (σταθερό) και
 * υπολογίζει τις σελίδες του PDF.
 */
function swimRowHeight_(g, r) {
  if (g.exactHeights[r] > 0) return g.exactHeights[r];
  var base = g.rowHeights[r] > 0 ? Math.max(2, Math.round(g.rowHeights[r] * SWIM_FONT_SCALE)) : SWIM_ROW_H;
  if (g.need[r] === undefined) g.need[r] = swimRowNeed_(g, r);
  return Math.max(base, g.need[r]);
}

/** Πλάτος στήλης του report (px): A 110, N 52 (στυλ), οι άλλες 74. */
function swimColumnWidth_(c) {
  return c === 1 ? 110 : (c === 14 ? 52 : 74);
}

/** Συνολικό πλάτος των στηλών 1..nc (px): το πλάτος που χωράει στη σελίδα του PDF. */
function swimLayoutWidth_(nc) {
  var w = 0;
  for (var c = 1; c <= nc; c++) w += swimColumnWidth_(c);
  return w;
}

/** Στήλη και απόσταση μέσα σε αυτήν (px) για μια θέση x από την αριστερή άκρη του φύλλου. */
function swimColumnAt_(x) {
  var c = 1, left = 0;
  while (left + swimColumnWidth_(c) <= x) { left += swimColumnWidth_(c); c++; }
  return { col: c, offset: Math.max(0, Math.round(x - left)) };
}

function swimNewPaging_(height, plan) {
  return { H: height, plan: plan, count: {}, open: {}, blocks: {}, fillers: 0 };
}

/** Θέση (px από την κορυφή της σελίδας του PDF) όπου αρχίζει η γραμμή row. */
function swimPageTop_(g, row, H) {
  var y = 0;
  for (var r = 1; r < row; r++) {
    var h = swimRowHeight_(g, r);
    y = y > 0 && y + h > H ? h : y + h;
  }
  return y;
}

/**
 * Αρχή ενότητας που δεν κόβεται: γραφήματα, εικόνες και στιγμιότυπα (whole) ολόκληρα σε μία σελίδα· πίνακες ολόκληροι
 * αν πιάνουν ως 40% της σελίδας, αλλιώς τουλάχιστον ο τίτλος με τις πρώτες γραμμές τους (οι υπόλοιπες συνεχίζουν
 * στην επόμενη σελίδα). Επιστρέφει τη γραμμή όπου αρχίζει τελικά η ενότητα.
 */
var SWIM_KEEP_TABLE = 0.4;

function swimKeep_(L, row, name, whole) {
  var P = L.paging;
  if (!P) return row;
  var n = P.count[name] = (P.count[name] || 0) + 1;
  var id = name + '#' + n;
  P.open[name] = id;
  var b = P.plan ? P.plan[id] : null;
  if (b && P.H > 0) {
    var y = swimPageTop_(L.g, row, P.H);
    var need = b.h <= P.H * (whole ? 1 : SWIM_KEEP_TABLE) ? b.h : b.head;
    var rest = P.H - y;
    // Αν δεν χωρά ούτε η πρώτη γραμμή, η σελίδα αλλάζει μόνη της.
    if (y > 0 && need > rest && rest >= b.h1) row = swimFillPage_(L, row, rest);
  }
  P.blocks[id] = { first: row, last: row, headLast: row };
  return row;
}

/** Τέλος της ενότητας name: τελευταία γραμμή της και ως πού μένει ενωμένη αν δεν χωρά σε μία σελίδα. */
function swimKeepEnd_(L, name, last, headLast) {
  var P = L.paging;
  var b = P ? P.blocks[P.open[name]] : null;
  if (!b) return;
  b.last = Math.max(b.first, last);
  b.headLast = Math.min(b.last, headLast !== undefined ? headLast : b.first + 3);
}

/**
 * Κενές γραμμές (έως 20 px η καθεμία) που γεμίζουν το υπόλοιπο της σελίδας εκτός από 2 px: η επόμενη γραμμή
 * (τουλάχιστον 21 px) δεν χωρά και αρχίζει στην επόμενη σελίδα.
 */
function swimFillPage_(L, row, rest) {
  var total = Math.floor(rest) - 2;
  if (total < 1) return row;
  var n = Math.ceil(total / 20);
  var base = Math.floor(total / n), extra = total - base * n;
  for (var i = 0; i < n; i++) L.g.exactHeights[row + i] = base + (i < extra ? 1 : 0);
  L.paging.fillers += n;
  return row + n;
}

/** Ύψος κάθε ενότητας του πρώτου περάσματος: { id: { h, head, h1 } }. */
function swimPagePlan_(layout) {
  var g = layout.grid, blocks = layout.paging.blocks, plan = {};
  Object.keys(blocks).forEach(function (id) {
    var b = blocks[id], h = 0, head = 0;
    for (var r = b.first; r <= b.last; r++) {
      var rh = swimRowHeight_(g, r);
      h += rh;
      if (r <= b.headLast) head += rh;
    }
    plan[id] = { h: h, head: head, h1: swimRowHeight_(g, b.first) };
  });
  return plan;
}

/** Οι γραμμές και οι στήλες που τυπώνονται: ως το κάτω άκρο του τελευταίου πίνακα, γραφήματος ή εικόνας. */
function swimPrintArea_(layout) {
  var g = layout.grid, last = g.maxRow;
  var bottom = function (row, height) {
    var y = 0, r = row;
    while (y < height) { y += swimRowHeight_(g, r); r++; }
    return r - 1;
  };
  layout.charts.forEach(function (c) { last = Math.max(last, bottom(c.row, (c.offsetY || 0) + c.height)); });
  (layout.images || []).forEach(function (im) { last = Math.max(last, bottom(im.row, (im.offsetY === undefined ? 4 : im.offsetY) + im.height)); });
  layout.print = { rows: last, cols: Math.max(g.maxCol, layout.lastCol) };
  if (layout.lastRow < last) layout.lastRow = last;
}

function swimCleanFormulas_(rowsSel, T0, K) {
  var cyc = [], tim = [], str = [], dst = [];
  var mult = K || '';
  rowsSel.forEach(function (x) {
    var r = T0 + x.i;
    var ok = 'AND(ISNUMBER(H' + r + '),H' + r + '>0,ISNUMBER(K' + r + '))';
    cyc.push('IF(' + ok + ',H' + r + '/' + x.row.ppc + ',0)');
    tim.push('IF(' + ok + ',K' + r + ',0)');
    str.push('IF(' + ok + ',H' + r + ',0)');
    dst.push('IF(' + ok + ',' + (x.row.d - x.row.prevD) + mult + ',0)');
  });
  if (!rowsSel.length) return { sr: '', dps: '', sl: '' };
  var C = '(' + cyc.join('+') + ')', T = '(' + tim.join('+') + ')', S = '(' + str.join('+') + ')', D = '(' + dst.join('+') + ')';
  return {
    sr: '=IFERROR(IF(' + T + '=0,"",' + C + '*60/' + T + '),"")',
    dps: '=IFERROR(IF(' + S + '=0,"",' + D + '/' + S + '),"")',
    sl: '=IFERROR(IF(' + C + '=0,"",' + D + '/' + C + '),"")'
  };
}

/**
 * Συχνότητα / DPS / SL σε όλο το 50 (γραμμή x του πίνακα ανά 50: B χρόνος, D χεριές), για αναλύσεις με χεριές ανά 50
 * (παλιό Excel 400-1500μ.), όπως τα υπολόγιζε το Excel.
 */
function swimWhole50Formulas_(x, style, K) {
  var ppc = swimPressesPerCycle_(style);
  var cyc = ppc === 1 ? 'D' + x : 'D' + x + '/' + ppc;
  var ok = 'OR(D' + x + '="",D' + x + '=0,B' + x + '="",B' + x + '=0)';
  return {
    sr: '=IFERROR(IF(' + ok + ',"",' + cyc + '*60/B' + x + '),"")',
    dps: '=IFERROR(IF(' + ok + ',"",50' + (K || '') + '/D' + x + '),"")',
    sl: '=IFERROR(IF(' + ok + ',"",50' + (K || '') + '/(' + cyc + ')),"")'
  };
}

/** Συχνότητα και DPS όλου του αγώνα από τα 50άρια (χεριές ανά 50), με τα πατήματα ανά κύκλο του στυλ κάθε 50. */
function swimWhole50Totals_(L) {
  var race = L.race, K = L.K || '';
  var cyc = [], tim = [], str = [], dst = [];
  Object.keys(L.per50Rows || {}).forEach(function (dEnd) {
    var x = L.per50Rows[dEnd];
    var ppc = swimPressesPerCycle_(swimStyleAt_(race, Number(dEnd)));
    var ok = 'AND(ISNUMBER(D' + x + '),D' + x + '>0,ISNUMBER(B' + x + '))';
    cyc.push('IF(' + ok + ',D' + x + '/' + ppc + ',0)');
    tim.push('IF(' + ok + ',B' + x + ',0)');
    str.push('IF(' + ok + ',D' + x + ',0)');
    dst.push('IF(' + ok + ',50' + K + ',0)');
  });
  if (!cyc.length) return { sr: '', dps: '' };
  var C = '(' + cyc.join('+') + ')', T = '(' + tim.join('+') + ')', S = '(' + str.join('+') + ')', D = '(' + dst.join('+') + ')';
  return { sr: '=IFERROR(IF(' + T + '=0,"",' + C + '*60/' + T + '),"")', dps: '=IFERROR(IF(' + S + '=0,"",' + D + '/' + S + '),"")' };
}

/**
 * ΔΑΚ ενός 50άρι ως τύπος του φύλλου: (χεριές + χρόνος από τα 15 έως τον τοίχο) / (απόσταση σε μέτρα / 10),
 * για κάθε μήκος του 50άρι (στα 50μ. /3,5 όπως στο Excel, στα 25μ. /1,0 ανά μήκος). perYard: "ανά 10yd".
 */
function swimDakFormula_(race, rowOf, b, perYard) {
  var pool = race.pool, e = b + 50;
  var times = [], sums = [], counts = [], blanks = [], width = 0;
  for (var w = b; w < e - 1e-9; w += pool) {
    var r15 = rowOf[w + 15], rw = rowOf[Math.min(w + pool, e)];
    if (r15 === undefined || rw === undefined || rw <= r15) return '';
    times.push('(B' + rw + '-B' + r15 + ')');
    sums.push('SUM(H' + (r15 + 1) + ':H' + rw + ')');
    counts.push('COUNT(H' + (r15 + 1) + ':H' + rw + ')');
    blanks.push('B' + rw + '=""', 'B' + r15 + '=""');
    width += Math.min(w + pool, e) - (w + 15);
  }
  if (!times.length || width <= 0) return '';
  var div = perYard ? width / 10 : width * race.k / 10;
  var divText = String(Math.round(div * 1e6) / 1e6);
  if (times.length === 1)
    return '=IF(OR(' + blanks[1] + ',' + blanks[0] + ',' + counts[0] + '=0),"",(' + times[0] + '+' + sums[0] + ')/' + divText + ')';
  return '=IF(OR(' + blanks.join(',') + ',' + counts.join('+') + '=0),"",(' + times.join('+') + '+' + sums.join('+') + ')/' + divText + ')';
}

/**
 * Το layout του report σε δύο περάσματα: το πρώτο μετρά τις ενότητες, το δεύτερο τις βάζει στις σελίδες του PDF
 * (extras.pageHeight(πλάτος) δίνει το ύψος της σελίδας σε px του φύλλου· χωρίς αυτό, ο υπολογισμός από το A4).
 */
function swimBuildLayout_(race, analysis, extras) {
  extras = extras || {};
  var build = function (paging) {
    return race.kind === 'training' ? swimBuildTechLayout_(race, analysis, extras, paging) : swimBuildRaceLayout_(race, analysis, extras, paging);
  };
  var layout = build(swimNewPaging_(0, null));
  var width = swimLayoutWidth_(Math.max(layout.grid.maxCol, layout.lastCol));
  var height = 0;
  try {
    height = typeof extras.pageHeight === 'function' ? extras.pageHeight(width) : swimPdfEstimatedHeight_(width);
  } catch (e) {
    height = swimPdfEstimatedHeight_(width);
  }
  if (height > 0) layout = build(swimNewPaging_(height, swimPagePlan_(layout)));
  layout.page = { width: width, height: height > 0 ? height : 0, fillers: layout.paging.fillers };
  layout.signature = swimLayoutSignature_(race, extras, layout.page.height);
  swimPrintArea_(layout);
  return layout;
}

function swimBuildRaceLayout_(race, analysis, extras, paging) {
  var yards = race.poolUnit === 'yd';
  var L = {
    g: new SwimGrid_(), race: race, analysis: analysis, extras: extras, rows: analysis.rows, n: analysis.rows.length,
    U: swimUnitLabel_(race), K: race.k === 1 ? '' : '*' + race.k, yards: yards, LAST: 15, WIDE: yards ? 17 : 15, paging: paging
  };
  var layout = { signature: '', grid: L.g, charts: [], images: [], inputs: { blocks: [] }, kpi: {}, paging: paging };
  L.layout = layout;

  // Θέσεις που χρειάζονται οι τύποι από την αρχή (KPI): εκκίνηση στη γραμμή 12, χρόνοι από τη 17 (με την κάρτα σύνοψης
  // του βίντεο, μετά από αυτήν).
  L.startRow = 12 + swimSummaryGraphicRows_(race);
  L.T0 = L.startRow + 6;
  L.rowOf = {};
  L.rows.forEach(function (row, i) { L.rowOf[row.d] = L.T0 + i; });
  L.lastRow = L.T0 + L.n - 1;
  L.rN = L.rowOf[race.distance] !== undefined ? L.rowOf[race.distance] : L.lastRow;
  L.FT = '$B$' + L.rN;
  L.cleanSel = [];
  L.rows.forEach(function (row, i) { if (row.isFree && i > 0) L.cleanSel.push({ i: i, row: row }); });

  swimLayoutHeader_(L);
  swimLayoutTiles_(L, false);
  swimLayoutSummaryGraphic_(L);
  swimLayoutStart_(L);
  swimLayoutTiming_(L);
  swimLayoutPer50_(L);
  swimLayoutTurns_(L);
  swimLayoutFinish_(L);
  swimLayoutFree_(L);
  swimLayoutComposition_(L);
  swimLayoutTiles_(L, true);
  if (extras.pb || extras.elite) swimLayoutCompare_(L);
  if (extras.benchmarks) swimLayoutBenchmarks_(L);
  swimLayoutExtras_(L);
  swimLayoutVersusGraphic_(L);
  swimLayoutFinalsMetrics_(L);
  swimLayoutFinalFrame_(L);
  swimLayoutCharts_(L);
  swimLayoutTech_(L, L.cur);

  layout.T0 = L.T0;
  layout.lastRow = Math.max(L.cur + 2, L.g.maxRow + 1);
  layout.lastCol = L.WIDE;
  return layout;
}

/** Τίτλος, αθλητής, αγώνισμα (γραμμές 1-6). */
function swimLayoutHeader_(L) {
  var g = L.g, W = L.WIDE;
  g.set(1, 1, 'SWIMMING RACE ANALYSIS', { bold: true, size: 18, color: SWIM_COLORS.titleText, bg: SWIM_COLORS.title });
  for (var c = 2; c <= W; c++) g.set(1, c, '', { bg: SWIM_COLORS.title });
  g.merge(1, 1, 1, W);
  g.rowHeights[1] = 40;
  g.set(2, 1, swimT_('Κ.Ο.Ε. · Ανάλυση αγώνα με Kinovea'), { size: 9, color: SWIM_COLORS.subText, bg: SWIM_COLORS.title });
  for (c = 2; c <= W; c++) g.set(2, c, '', { bg: SWIM_COLORS.title });
  g.merge(2, 1, 2, 10);
  g.set(2, 11, '', { size: 9, color: SWIM_COLORS.subText, bg: SWIM_COLORS.title, align: 'right', fmt: '@' });
  g.merge(2, 11, 2, W);
  g.rowHeights[2] = 22;
  for (c = 1; c <= W; c++) g.set(3, c, '', { bg: SWIM_COLORS.accent });
  g.rowHeights[3] = 4;
  g.rowHeights[4] = 8;

  g.set(5, 1, '', { bold: true, size: 18, color: SWIM_COLORS.title, fmt: '@' });
  g.merge(5, 1, 5, 8);
  g.set(5, 9, '', { bold: true, size: 13, color: SWIM_COLORS.title, align: 'right', fmt: '@' });
  g.merge(5, 9, 5, W);
  g.rowHeights[5] = 30;
  g.set(6, 1, '', { size: 10, color: SWIM_COLORS.dim, fmt: '@' });
  g.merge(6, 1, 6, 8);
  g.set(6, 9, '', { size: 10, color: SWIM_COLORS.dim, align: 'right', fmt: '@' });
  g.merge(6, 9, 6, W);
  g.rowHeights[6] = 20;
  g.rowHeights[7] = 10;
  // Πλακίδια (γραμμές 8-10): γράφονται στο τέλος, αλλά τα ύψη τους χρειάζονται από την αρχή για τις σελίδες.
  g.rowHeights[8] = 18;
  g.rowHeights[9] = 34;
  g.rowHeights[10] = 28;
  g.rowHeights[11] = 10;
  L.layout.inputs.header = [
    { r: 5, c: 1, key: 'athlete' }, { r: 5, c: 9, key: 'eventLine' },
    { r: 6, c: 1, key: 'clubLine' }, { r: 6, c: 9, key: 'compLine' }
  ];
  L.layout.inputs.updated = { r: 2, c: 11 };
}

/** KPI σε πλακίδια (γραμμές 8-10): ο ΔΑΚ πρώτος. Οι τύποι χρειάζονται τις θέσεις των πινάκων, γι' αυτό μπαίνουν στο τέλος. */
function swimLayoutTiles_(L, values) {
  var g = L.g, race = L.race;
  // Πρώτα οι ετικέτες (τα ύψη τους χρειάζονται για τις σελίδες), στο τέλος οι τύποι των τιμών.
  var cleanAll = !values ? { sr: '', dps: '' } : (race.strokesPer50 ? swimWhole50Totals_(L) : swimCleanFormulas_(L.cleanSel, L.T0, L.K));
  var r15 = L.rowOf[15];
  var dak = values && L.p50First !== null ? '=IFERROR(AVERAGE(H' + L.p50First + ':H' + L.p50Last + '),"")' : '';
  // 2.9.5: κάτω από τον μέσο όρο ο ΔΑΚ κάθε 50άρι (2 έως 4 50άρια· με περισσότερα, στους πίνακες).
  var dak50 = swimDak50Tile_(L);
  var tiles = [
    { cap: swimT_(dak50 ? 'ΔΑΚ (μ.ο.)' : 'ΔΑΚ'), val: dak, fmt: SWIM_FMT.vel, sub: dak50 ? dak50.sub : swimT_('Δείκτης Απόδοσης Κολύμβησης (μ.ο. 50άρι)'),
      key: 'dak', accent: true },
    { cap: swimT_('ΤΕΛΙΚΟΣ ΧΡΟΝΟΣ'), val: '=IF(' + L.FT + '="","",IF(' + L.FT + '<60,TEXT(' + L.FT + ',"0.00"),INT(' + L.FT + '/60)&":"&TEXT(MOD(' + L.FT + ',60),"00.00")))', fmt: '@', sub: swimT_('λεπτά:δευτερόλεπτα'), key: 'finalText' },
    { cap: swimT_('ΜΕΣΗ ΤΑΧΥΤΗΤΑ'), val: '=IF(' + L.FT + '="","",' + race.distance + L.K + '/' + L.FT + ')', fmt: SWIM_FMT.vel,
      sub: L.yards ? '=IF(' + L.FT + '="","m/s",TEXT(' + race.distance + '/' + L.FT + ',"0.00")&" yd/s")' : 'm/s', key: 'avgVel' },
    { cap: swimT_('ΣΥΧΝΟΤΗΤΑ'), val: cleanAll.sr, fmt: SWIM_FMT.sr, sub: swimT_(race.strokesPer50 ? 'κύκλοι/λεπτό · ολόκληρα 50άρια' : 'κύκλοι/λεπτό · καθαρή κολύμβηση'), key: 'sr' },
    { cap: swimT_('ΜΗΚΟΣ ΧΕΡΙΑΣ'), val: cleanAll.dps, fmt: SWIM_FMT.dps, sub: swimT_(L.yards ? 'm/χεριά (yd: ÷0.9144)' : (race.strokesPer50 ? 'm/χεριά · ολόκληρα 50άρια' : 'm/χεριά · καθαρή κολύμβηση')), key: 'dps' },
    { cap: swimT_('ΕΚΚΙΝΗΣΗ 0-15') + L.U.replace('.', '').toUpperCase(), val: r15 !== undefined ? '=IF(B' + r15 + '="","",B' + r15 + ')' : '', fmt: SWIM_FMT.time, sub: swimT_('δευτερόλεπτα από το σήμα'), key: 'start15' },
    { cap: swimT_('ΣΤΡΟΦΕΣ'), val: values && L.turnFirst !== null ? '=IF(C' + (L.c1 + 1) + '="","",C' + (L.c1 + 1) + ')' : '', fmt: SWIM_FMT.pct, sub: swimT_('% του χρόνου (-5/+15)'), key: 'turnsPct' }
  ];
  tiles.forEach(function (t, i) {
    var c = 1 + 2 * i;
    L.layout.kpi[t.key] = { r: 9, c: c };
    if (values) { g.set(9, c, t.val); return; }
    var bg = t.accent ? SWIM_COLORS.kpiDak : SWIM_COLORS.kpi;
    g.set(8, c, t.cap, { bold: true, size: 8, color: SWIM_COLORS.dim, bg: bg, align: 'center', wrap: true });
    g.set(8, c + 1, '', { bg: bg });
    g.merge(8, c, 8, c + 1);
    g.set(9, c, '', { bold: true, size: 20, color: t.accent ? SWIM_COLORS.dakText : SWIM_COLORS.title, bg: bg, align: 'center', fmt: t.fmt });
    g.set(9, c + 1, '', { bg: bg });
    g.merge(9, c, 9, c + 1);
    g.set(10, c, t.sub, { size: 8, color: SWIM_COLORS.dim, bg: bg, align: 'center', wrap: true });
    g.set(10, c + 1, '', { bg: bg });
    g.merge(10, c, 10, c + 1);
  });
  g.rowHeights[8] = 18;
  g.rowHeights[9] = 34;
  // Ο τύπος των 50άρι μετρά για μία γραμμή: το ύψος από το κείμενο που θα δείξει (ίδιο και στα δύο περάσματα).
  g.rowHeights[10] = dak50 ? Math.max(28, Math.ceil(dak50.height / SWIM_FONT_SCALE)) : 28;
  if (!values) return;
  L.layout.kpi.finalTime = { r: L.rN, c: 2 };
  L.layout.kpi.totalStrokes = L.p50Total ? { r: L.p50Total, c: 4 } : null;
}

/** (2.9.5) Τακτικό αριθμητικό ενός 50άρι: «1ο», «2ο» … (στα αγγλικά «1st», «2nd», «3rd», «4th» …). */
function swimOrdinal_(n) {
  if (SWIM_LANG_ !== 'en') return n + 'ο';
  var t = n % 100, u = n % 10;
  return n + (t >= 11 && t <= 13 ? 'th' : (u === 1 ? 'st' : (u === 2 ? 'nd' : (u === 3 ? 'rd' : 'th'))));
}

/** (2.9.5) Ο ΔΑΚ κάθε 50άρι σε μία γραμμή: [{ k (το πολλοστό 50άρι), v }] → «1ο 1.23 · 2ο 1.31» (χωρίς τιμή: «–»). */
function swimDak50Text_(list) {
  return list.map(function (x) {
    return swimOrdinal_(x.k) + ' ' + (x.v === null || x.v === undefined || !isFinite(x.v) ? '–' : swimQuickFixed_(x.v, 2));
  }).join(' · ');
}

/**
 * (2.9.5) Το κείμενο κάτω από τον μέσο όρο στο πλακίδιο του ΔΑΚ: ο ΔΑΚ κάθε 50άρι, από τη στήλη L των χρόνων (στις γραμμές
 * των 50άρι), όταν ο αγώνας έχει 2 έως 4 50άρια. { sub: τύπος του φύλλου, height: px για το κείμενο που θα δείξει } ή null.
 */
function swimDak50Tile_(L) {
  if (!SWIM_DAK50) return null;
  var list = [];
  for (var k = 1; k * 50 <= L.race.distance + 1e-9; k++) if (L.rowOf[k * 50] !== undefined) list.push({ k: k, r: L.rowOf[k * 50] });
  if (list.length < 2 || list.length > 4) return null;
  // Χωρίς καμία τιμή (π.χ. χωρίς χεριές) το κείμενο όπως πριν.
  var label = swimT_('Δείκτης Απόδοσης Κολύμβησης (μ.ο. 50άρι)');
  var parts = list.map(function (x) {
    var o = swimOrdinal_(x.k);
    return 'IF(ISNUMBER(L' + x.r + '),"' + o + ' "&TEXT(L' + x.r + ',"0.00"),"' + o + ' –")';
  });
  // Ως δύο 50άρια σε κάθε γραμμή (τρία ή τέσσερα: δύο γραμμές, χωρίς να κόβεται κάπου αλλού).
  var lines = function (items, sep, br) { return items.length > 2 ? items.slice(0, 2).join(sep) + br + items.slice(2).join(sep) : items.join(sep); };
  var refs = list.map(function (x) { return 'L' + x.r; }).join(',');
  var width = swimColumnWidth_(1) + swimColumnWidth_(2);
  // Το πιο φαρδύ κείμενο: ΔΑΚ με δύο ψηφία (π.χ. 12.40 σε πισίνα 50μ.).
  var sample = lines(list.map(function (x) { return swimDak50Text_([{ k: x.k, v: 88.88 }]); }), ' · ', '\n');
  return {
    sub: '=IF(COUNT(' + refs + ')=0,"' + label.replace(/"/g, '""') + '",' + lines(parts, '&" · "&', '&CHAR(10)&') + ')',
    height: Math.max(swimTextHeight_(sample, width, 8, false), swimTextHeight_(label, width, 8, false))
  };
}

function swimLayoutStart_(L) {
  var g = L.g, U = L.U, r0 = L.startRow;
  g.section(r0, swimT_('Εκκίνηση (χρόνοι από το σήμα εκκίνησης)'), L.LAST);
  g.headers(r0 + 1, [
    swimT_('Χρόνος Αντίδρασης – πρώτη κίνηση (sec)'), swimT_('Χρόνος Πάνω στον βατήρα (sec)'), swimT_('Χρόνος έως την είσοδο (sec)'),
    swimTF_('Απόσταση εισόδου (Χέρια) ({0})', U), swimTF_('Απόσταση εισόδου (Ισχία) ({0})', U), swimT_('Ταχύτητα εισόδου (Χέρια) (m/s)'),
    swimT_('Ταχύτητα εισόδου (Ισχία) (m/s)'), swimT_('Ταχύτητα εισόδου (Πόδια) (m/s)'), swimT_('Χρόνος έως την έξοδο από το νερό (sec)'),
    swimT_('Υποβρύχιοι Δελφινισμοί'), swimTF_('Απόσταση κάτω από το νερό ({0})', U), swimTF_('Απόσταση Εξόδου από το νερό (κεφάλι) ({0})', U),
    swimT_('Μέση ταχύτητα από το σήμα έως την έξοδο (m/s)'), swimT_('Πτήση: βατήρας → είσοδος (sec)'), swimTF_('Χρόνος 15{0} (sec)', U)
  ]);
  var rv = r0 + 2;
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
  L.layout.inputs.start = [];
  startDefs.forEach(function (def, i) {
    g.set(rv, i + 1, '', swimF_(def[2], def[1] === 'auto' ? 'auto' : 'input'));
    L.layout.inputs.start.push({ r: rv, c: i + 1, key: def[0], kind: def[1] });
  });
  g.set(rv, 13, '=IF(OR(K' + rv + '="",I' + rv + '="",I' + rv + '=0),"",K' + rv + L.K + '/I' + rv + ')', swimF_(SWIM_FMT.vel, 'formula'));
  g.set(rv, 14, '=IF(OR(B' + rv + '="",C' + rv + '=""),"",C' + rv + '-B' + rv + ')', swimF_(SWIM_FMT.time, 'formula'));
  var r15 = L.rowOf[15];
  g.set(rv, 15, r15 !== undefined ? '=IF(B' + r15 + '="","",B' + r15 + ')' : '', swimF_(SWIM_FMT.time, 'formula'));
  g.box(r0 + 1, 1, rv, 15);
  L.startValues = rv;
}

function swimLayoutTiming_(L) {
  var g = L.g, race = L.race, analysis = L.analysis, rows = L.rows, T0 = L.T0, U = L.U, K = L.K;
  var sec = T0 - 2;
  g.section(sec, swimT_('Χρόνοι & Χεριές (Timing Data)'), L.WIDE);
  var labels = [
    'Distance (' + (L.yards ? 'yd' : 'm') + ')', 'Time (s)', 'Vel (m/s)', 'Dif Time 25', 'Dif 50', 'Turn time -5/+15',
    'Swolf / Total Strokes by 50', swimT_('Χεριές τμήματος'), 'StrRate (Cycles/min)', 'Distance Per Stroke (m/stroke)',
    // 2.9.5: στη στήλη L ο ΔΑΚ κάθε 50άρι (στη γραμμή όπου τελειώνει, από τον πίνακα «Ανά 50») αντί για τη μέση ταχύτητα από την εκκίνηση.
    swimT_('Χρόνος τμήματος (s)'), SWIM_DAK50 ? swimT_('ΔΑΚ 50άρι') : swimT_('Μέση ταχύτητα από εκκίνηση (m/s)'), swimT_('SR από χρόνους χεριών (Cycles/min)'), swimT_('Στυλ'),
    swimT_('Μήκος κύκλου SL (m/κύκλο)')
  ];
  if (L.yards) labels.push('Vel (yd/s)', swimT_('DPS (yd/χεριά)'));
  g.headers(T0 - 1, labels);

  var rowOf = L.rowOf, dist = race.distance;
  var all25 = true;
  for (var d25 = 25; d25 <= dist + 1e-9; d25 += 25) if (rowOf[d25] === undefined) { all25 = false; break; }

  var prev25 = null, prev50 = null;
  rows.forEach(function (row, i) {
    var r = T0 + i;
    var p = r - 1;
    var B = 'B' + r, Bp = 'B' + p;
    g.set(r, 1, row.d, swimF_(null, 'label'));
    g.set(r, 2, '', swimF_(SWIM_FMT.time, 'input'));

    // Ταχύτητα τμήματος. Στα τμήματα εκκίνησης / στροφής / τελευταίων 5μ. (πορτοκαλί) είναι συμβατική:
    // το σημείο στον τοίχο είναι η επαφή (χέρια ή πόδια), όχι το κεφάλι, και περιέχει γλίστρημα, περιστροφή ή υποβρύχια φάση.
    var velFmt = swimF_(SWIM_FMT.vel, 'formula');
    if (!row.isFree) velFmt.bg = SWIM_COLORS.phase;
    if (i === 0) g.set(r, 3, '=IF(' + B + '="","",A' + r + K + '/' + B + ')', velFmt);
    else g.set(r, 3, '=IF(OR(' + B + '="",' + Bp + '=""),"",(A' + r + '-A' + p + ')' + K + '/(' + B + '-' + Bp + '))', velFmt);

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

    if (row.isFree && i > 0) {
      g.set(r, 9, '=IF(OR(H' + r + '="",H' + r + '=0,K' + r + '=""),"",IF(OR(N' + r + '="fs",N' + r + '="bk"),H' + r + '/2,H' + r + ')*60/K' + r + ')', swimF_(SWIM_FMT.sr, 'formula'));
      g.set(r, 10, '=IF(OR(H' + r + '="",H' + r + '=0),"",(A' + r + '-A' + p + ')' + K + '/H' + r + ')', swimF_(SWIM_FMT.dps, 'formula'));
      g.set(r, 15, '=IF(OR(H' + r + '="",H' + r + '=0),"",(A' + r + '-A' + p + ')' + K + '/IF(OR(N' + r + '="fs",N' + r + '="bk"),H' + r + '/2,H' + r + '))', swimF_(SWIM_FMT.dps, 'formula'));
      if (L.yards) g.set(r, 17, '=IF(OR(H' + r + '="",H' + r + '=0),"",(A' + r + '-A' + p + ')/H' + r + ')', swimF_(SWIM_FMT.dps, 'formula'));
    } else {
      g.set(r, 9, '', swimF_(null, 'formula'));
      g.set(r, 10, '', swimF_(null, 'formula'));
      g.set(r, 15, '', swimF_(null, 'formula'));
      if (L.yards) g.set(r, 17, '', swimF_(null, 'formula'));
    }

    if (i === 0) g.set(r, 11, '=IF(' + B + '="","",' + B + ')', swimF_(SWIM_FMT.time, 'formula'));
    else g.set(r, 11, '=IF(OR(' + B + '="",' + Bp + '=""),"",' + B + '-' + Bp + ')', swimF_(SWIM_FMT.time, 'formula'));
    // Ο ΔΑΚ των 50άρι μπαίνει στις γραμμές τους από τον πίνακα «Ανά 50» (swimLayoutPer50_).
    if (SWIM_DAK50) g.set(r, 12, '', swimF_(SWIM_FMT.vel, 'formula'));
    else g.set(r, 12, '=IF(' + B + '="","",A' + r + K + '/' + B + ')', swimF_(SWIM_FMT.vel, 'formula'));
    g.set(r, 13, '', swimF_(SWIM_FMT.sr, 'auto'));
    g.set(r, 14, row.style, { color: '#777777', align: 'center', size: 9 });
    if (L.yards) {
      var vy = swimF_(SWIM_FMT.vel, 'formula');
      if (!row.isFree) vy.bg = SWIM_COLORS.phase;
      if (i === 0) g.set(r, 16, '=IF(' + B + '="","",A' + r + '/' + B + ')', vy);
      else g.set(r, 16, '=IF(OR(' + B + '="",' + Bp + '=""),"",(A' + r + '-A' + p + ')/(' + B + '-' + Bp + '))', vy);
    }
  });
  g.box(T0 - 1, 1, L.lastRow, L.WIDE);
  L.layout.inputs.times = { col: 2, row: T0, n: L.n };
  L.layout.inputs.strokes = { col: 8, row: T0, n: L.n };
  L.layout.inputs.srExact = { col: 13, row: T0, n: L.n };
  L.cur = L.lastRow + 2;
}

function swimLayoutPer50_(L) {
  var g = L.g, race = L.race, rowOf = L.rowOf, T0 = L.T0, U = L.U, K = L.K, cur = L.cur;
  var dist = race.distance;
  var fifties = [];
  for (var k50 = 1; k50 * 50 <= dist + 1e-9; k50++) {
    if (rowOf[k50 * 50] !== undefined) fifties.push(k50);
  }
  L.p50First = null; L.p50Last = null; L.p50Total = null;
  L.per50Rows = {};
  L.layout.per50 = null;
  if (!fifties.length) return;
  cur = swimKeep_(L, cur, 'per50');
  var lab50 = '50' + U;
  g.section(cur, swimTF_('Ανά 50{0} · ΔΑΚ (Δείκτης Απόδοσης Κολύμβησης)', U), L.LAST);
  var headers = [lab50, swimTF_('Χρόνος {0} (s)', lab50), swimT_('Χρόνος (σωρευτικός)'), swimTF_('Χεριές {0}', lab50),
    swimT_('StrRate καθαρής κολύμβησης (Cycles/min)'), swimT_('DPS καθαρής κολύμβησης (m/stroke)'), 'Swolf', swimT_('ΔΑΚ'),
    swimT_('Μέση ταχύτητα (m/s)'), swimT_('Διαφορά από προηγ. 50 (s)'), swimTF_('Χρόνος 100{0} (s)', U), swimT_('SL καθαρής κολύμβησης (m/κύκλο)'),
    swimT_('Στυλ')];
  if (L.yards) headers.push(swimT_('ΔΑΚ ανά 10yd'), swimT_('Μέση ταχύτητα (yd/s)'));
  g.headers(cur + 1, headers);
  var r50 = cur + 2;
  L.p50First = r50;
  fifties.forEach(function (k, idx) {
    var dEnd = k * 50, dStart = dEnd - 50;
    var re = rowOf[dEnd];
    var rs = dStart === 0 ? null : rowOf[dStart];
    var x = r50 + idx;
    var Be = 'B' + re;
    L.per50Rows[dEnd] = x;
    g.set(x, 1, dStart + '-' + dEnd, swimF_('@', 'label'));
    if (rs === null) g.set(x, 2, '=IF(' + Be + '="","",' + Be + ')', swimF_(SWIM_FMT.time, 'formula'));
    else if (rs !== undefined) g.set(x, 2, '=IF(OR(' + Be + '="",B' + rs + '=""),"",' + Be + '-B' + rs + ')', swimF_(SWIM_FMT.time, 'formula'));
    else g.set(x, 2, '', swimF_(SWIM_FMT.time, 'formula'));
    g.set(x, 3, '=IF(' + Be + '="","",' + Be + ')', swimF_(SWIM_FMT.time, 'formula'));
    var a = rs === null ? T0 : (rs !== undefined ? rs + 1 : null);
    if (a !== null) g.set(x, 4, '=IF(COUNT(H' + a + ':H' + re + ')=0,"",SUM(H' + a + ':H' + re + '))', swimF_(SWIM_FMT.strokes, 'formula'));
    else g.set(x, 4, '', swimF_(null, 'formula'));
    // Συχνότητα / μήκος χεριάς του 50: μόνο από την καθαρή κολύμβηση (όχι από όλο το 50, που περιέχει
    // την υποβρύχια φάση της εκκίνησης ή της στροφής και θα έδινε μικρότερη συχνότητα και μεγαλύτερο DPS).
    var sel50 = L.cleanSel.filter(function (x) { return x.row.prevD >= dStart - 1e-9 && x.row.d <= dEnd + 1e-9; });
    var clean50 = race.strokesPer50 ? swimWhole50Formulas_(x, swimStyleAt_(race, dEnd), K) : swimCleanFormulas_(sel50, T0, K);
    g.set(x, 5, clean50.sr, swimF_(SWIM_FMT.sr, 'formula'));
    g.set(x, 6, clean50.dps, swimF_(SWIM_FMT.dps, 'formula'));
    g.set(x, 7, '=IF(OR(B' + x + '="",D' + x + '=""),"",B' + x + '+D' + x + ')', swimF_(SWIM_FMT.time, 'formula'));
    var dakFmt = swimF_(SWIM_FMT.vel, 'formula');
    dakFmt.bg = SWIM_COLORS.kpiDak; dakFmt.bold = true; dakFmt.color = SWIM_COLORS.dakText;
    g.set(x, 8, swimDakFormula_(race, rowOf, dStart, false), dakFmt);
    // 2.9.5: ο ΔΑΚ του 50άρι και στη στήλη L των χρόνων, στη γραμμή όπου τελειώνει (η τιμή του H αυτού του πίνακα).
    if (SWIM_DAK50) g.set(re, 12, '=IF(H' + x + '="","",H' + x + ')', dakFmt);
    g.set(x, 9, '=IF(OR(B' + x + '="",B' + x + '=0),"",50' + K + '/B' + x + ')', swimF_(SWIM_FMT.vel, 'formula'));
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
    g.set(x, 12, clean50.sl, swimF_(SWIM_FMT.dps, 'formula'));
    g.set(x, 13, swimStyleAt_(race, dEnd), { color: '#777777', align: 'center', size: 9 });
    if (L.yards) {
      g.set(x, 14, swimDakFormula_(race, rowOf, dStart, true), dakFmt);
      g.set(x, 15, '=IF(OR(B' + x + '="",B' + x + '=0),"",50/B' + x + ')', swimF_(SWIM_FMT.vel, 'formula'));
    }
    L.p50Last = x;
  });
  // Σύνολο / μέσοι όροι
  var t = L.p50Last + 1, f = L.p50First, l = L.p50Last;
  var lab = swimF_(null, 'label');
  g.set(t, 1, swimT_('Σύνολο'), lab);
  g.set(t, 2, '=IF(COUNT(B' + f + ':B' + l + ')=0,"",SUM(B' + f + ':B' + l + '))', swimF_(SWIM_FMT.time, 'formula'));
  g.set(t, 4, '=IF(COUNT(D' + f + ':D' + l + ')=0,"",SUM(D' + f + ':D' + l + '))', swimF_(SWIM_FMT.strokes, 'formula'));
  g.set(t, 7, '=IF(COUNT(G' + f + ':G' + l + ')=0,"",AVERAGE(G' + f + ':G' + l + '))', swimF_(SWIM_FMT.time, 'formula'));
  var dakAvg = swimF_(SWIM_FMT.vel, 'formula');
  dakAvg.bg = SWIM_COLORS.kpiDak; dakAvg.bold = true; dakAvg.color = SWIM_COLORS.dakText;
  g.set(t, 8, '=IFERROR(AVERAGE(H' + f + ':H' + l + '),"")', dakAvg);
  for (var c = 3; c <= 13; c++) if (!g.get(t, c)) g.set(t, c, '', swimF_(null, 'formula'));
  L.p50Total = t;
  L.layout.per50 = { first: L.p50First, last: L.p50Last, total: t };
  g.box(cur + 1, 1, t, L.yards ? 15 : 13);
  swimKeepEnd_(L, 'per50', t, r50 + 2);
  L.cur = t + 2;
}

function swimLayoutTurns_(L) {
  var g = L.g, race = L.race, analysis = L.analysis, rowOf = L.rowOf, U = L.U, K = L.K, cur = L.cur;
  L.layout.inputs.turns = [];
  L.turnFirst = null; L.turnLast = null;
  L.turnRows = {};
  if (!analysis.turnWalls.length) { L.layout.turnRows = { first: null, last: null }; return; }
  cur = swimKeep_(L, cur, 'turns');
  g.section(cur, swimT_('Στροφές'), L.LAST);
  g.headers(cur + 1, [swimTF_('Τοίχος ({0})', U), swimTF_('Χρόνος προσέγγισης 5{0} (s)', U), swimTF_('Χρόνος -5/+15{0} (s)', U), swimT_('Χρόνος στροφής (s)'),
    swimTF_('Μέτρα κάτω από το νερό ({0})', U), swimT_('Χρόνος κάτω από το νερό (s)'), swimT_('Δελφινισμοί'), swimTF_('Μέση ταχύτητα -5/+15{0} (m/s)', U),
    swimT_('Μέση ταχύτητα από στροφή έως έξοδο (m/s)'), swimT_('Ποσοστό στον τελικό χρόνο'), swimT_('Τελευταία χεριά → τοίχος (s)'),
    swimTF_('Τοίχος → κεφάλι στα +15{0} (s)', U), swimT_('Είδος στροφής')]);
  var rt = cur + 2;
  L.turnFirst = rt;
  analysis.turnWalls.forEach(function (wall, idx) {
    var x = rt + idx;
    var turn = analysis.turns[idx];
    var rw = rowOf[wall], rm5 = rowOf[wall - 5], rp15 = rowOf[wall + 15];
    L.turnRows[wall] = x;
    g.set(x, 1, wall, swimF_(null, 'label'));
    if (rw !== undefined && rm5 !== undefined) g.set(x, 2, '=IF(OR(B' + rw + '="",B' + rm5 + '=""),"",B' + rw + '-B' + rm5 + ')', swimF_(SWIM_FMT.time, 'formula'));
    else g.set(x, 2, '', swimF_(null, 'formula'));
    if (rp15 !== undefined && rm5 !== undefined) g.set(x, 3, '=IF(OR(B' + rp15 + '="",B' + rm5 + '=""),"",B' + rp15 + '-B' + rm5 + ')', swimF_(SWIM_FMT.time, 'formula'));
    else g.set(x, 3, '', swimF_(null, 'formula'));
    g.set(x, 4, '', swimF_(SWIM_FMT.time, 'auto'));
    g.set(x, 5, '', swimF_(SWIM_FMT.vel, 'input'));
    g.set(x, 6, '', swimF_(SWIM_FMT.time, 'auto'));
    g.set(x, 7, '', swimF_(SWIM_FMT.int, 'auto'));
    g.set(x, 8, '=IF(OR(C' + x + '="",C' + x + '=0),"",20' + K + '/C' + x + ')', swimF_(SWIM_FMT.vel, 'formula'));
    g.set(x, 9, '=IF(OR(E' + x + '="",F' + x + '="",F' + x + '=0),"",E' + x + K + '/F' + x + ')', swimF_(SWIM_FMT.vel, 'formula'));
    g.set(x, 10, '=IF(OR(C' + x + '="",' + L.FT + '=""),"",C' + x + '/' + L.FT + ')', swimF_(SWIM_FMT.pct, 'formula'));
    g.set(x, 11, '', swimF_(SWIM_FMT.time, 'auto'));
    if (rp15 !== undefined && rw !== undefined) g.set(x, 12, '=IF(OR(B' + rp15 + '="",B' + rw + '=""),"",B' + rp15 + '-B' + rw + ')', swimF_(SWIM_FMT.time, 'formula'));
    else g.set(x, 12, '', swimF_(null, 'formula'));
    g.set(x, 13, swimTurnKindLabel_(turn), { color: '#555555', align: 'center', size: 9, bg: SWIM_COLORS.label });
    L.layout.inputs.turns.push({ r: x, wall: wall });
    L.turnLast = x;
  });
  g.box(cur + 1, 1, L.turnLast, 13);
  L.layout.turnRows = { first: L.turnFirst, last: L.turnLast };
  swimKeepEnd_(L, 'turns', L.turnLast, rt + 2);
  L.cur = L.turnLast + 2;
}

var SWIM_STYLE_NAMES = { fs: 'Ελεύθερο', bk: 'Ύπτιο', br: 'Πρόσθιο', fly: 'Πεταλούδα', im: 'Μικτή' };

/** Το όνομα του στυλ στη γλώσσα του report ("Ελεύθερο", "Freestyle"). */
function swimStyleName_(style) {
  return swimT_(SWIM_STYLE_NAMES[style]);
}

/** "ανάποδη", "ανοιχτή", "αλλαγή" ή "Πεταλούδα → Ύπτιο" στη μικτή. */
function swimTurnKindLabel_(turn) {
  if (!turn) return '';
  if (turn.exchange) return swimT_('αλλαγή σκυταλοδρομίας');
  if (turn.styleIn && turn.styleOut && turn.styleIn !== turn.styleOut)
    return swimStyleName_(turn.styleIn) + ' → ' + swimStyleName_(turn.styleOut);
  return swimT_(turn.flip ? 'ανάποδη' : 'ανοιχτή');
}

function swimLayoutFinish_(L) {
  var g = L.g, race = L.race, rowOf = L.rowOf, U = L.U, K = L.K, cur = L.cur, dist = race.distance, rN = L.rN;
  cur = swimKeep_(L, cur, 'finish');
  g.section(cur, swimT_('Τερματισμός'), L.LAST);
  g.headers(cur + 1, [swimTF_('Χρόνος τελευταίων 15{0} (s)', U), swimTF_('Χρόνος τελευταίων 5{0} (s)', U), swimTF_('Χεριές τελευταίων 5{0}', U),
    swimT_('Προσέγγιση τοίχου σε sec (τελευταία χεριά)'), swimTF_('Προσέγγιση τοίχου σε μήκος {0} (τελευταία χεριά)', U),
    swimTF_('Μέση ταχύτητα τελευταίων 5{0} (m/s)', U)]);
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
  g.set(fr, 6, '=IF(OR(B' + fr + '="",B' + fr + '=0),"",5' + K + '/B' + fr + ')', swimF_(SWIM_FMT.vel, 'formula'));
  g.box(cur + 1, 1, fr, 6);
  L.layout.inputs.finish = { r: fr };
  L.finishRow = fr;
  swimKeepEnd_(L, 'finish', fr);
  L.cur = fr + 2;
}

function swimLayoutFree_(L) {
  var g = L.g, rows = L.rows, T0 = L.T0, cur = L.cur;
  var freeRows = [];
  rows.forEach(function (row, i) { if (row.isFree && i > 0) freeRows.push(T0 + i); });
  L.free = null;
  if (!freeRows.length) return;
  cur = swimKeep_(L, cur, 'free');
  g.section(cur, swimT_('Ειδικοί τομείς (καθαρή κολύμβηση)'), L.LAST);
  g.headers(cur + 1, [swimT_('Τμήμα'), swimT_('Χρόνος (s)'), swimT_('Ταχύτητα (m/s)'), swimT_('Μεταβολή ταχύτητας (m/s)'),
    swimT_('Ποσοστό του συνολικού χρόνου'), swimT_('Χεριές'), 'StrRate (Cycles/min)', 'DPS (m/stroke)']);
  freeRows.forEach(function (tr, idx) {
    var x = cur + 2 + idx;
    var row = rows[tr - T0];
    g.set(x, 1, row.prevD + '-' + row.d, swimF_('@', 'label'));
    g.set(x, 2, '=IF(K' + tr + '="","",K' + tr + ')', swimF_(SWIM_FMT.time, 'formula'));
    g.set(x, 3, '=IF(C' + tr + '="","",C' + tr + ')', swimF_(SWIM_FMT.vel, 'formula'));
    g.set(x, 4, '=IF(OR(C' + tr + '="",C' + (tr - 1) + '=""),"",C' + tr + '-C' + (tr - 1) + ')', swimF_('0.00;-0.00', 'formula'));
    g.set(x, 5, '=IF(OR(K' + tr + '="",' + L.FT + '=""),"",K' + tr + '/' + L.FT + ')', swimF_(SWIM_FMT.pct, 'formula'));
    g.set(x, 6, '=IF(H' + tr + '="","",H' + tr + ')', swimF_(SWIM_FMT.strokes, 'formula'));
    g.set(x, 7, '=IF(I' + tr + '="","",I' + tr + ')', swimF_(SWIM_FMT.sr, 'formula'));
    g.set(x, 8, '=IF(J' + tr + '="","",J' + tr + ')', swimF_(SWIM_FMT.dps, 'formula'));
  });
  var freeLast = cur + 1 + freeRows.length;
  g.box(cur + 1, 1, freeLast, 8);
  L.free = { first: cur + 2, last: freeLast };
  L.layout.free = L.free;
  swimKeepEnd_(L, 'free', freeLast, cur + 4);
  L.cur = freeLast + 2;
}

function swimLayoutComposition_(L) {
  var g = L.g, race = L.race, rowOf = L.rowOf, U = L.U, cur = L.cur;
  cur = swimKeep_(L, cur, 'composition');
  g.section(cur, swimT_('Σύνθεση αγώνα (ποσοστό του συνολικού χρόνου)'), L.LAST);
  g.headers(cur + 1, [swimT_('Φάση'), swimT_('Χρόνος (s)'), swimT_('Ποσοστό')]);
  g.set(cur + 1, 5, swimTF_('Εκκίνηση: 0-15{0} · Στροφές: -5/+15{0} · Τερματισμός: τελευταία 5{0}', U), { italic: true, color: '#555555', size: 9 });
  g.merge(cur + 1, 5, cur + 1, L.LAST);
  var c1 = cur + 2;
  var r15first = rowOf[15];
  g.set(c1, 1, swimT_('Εκκίνηση'), swimF_(null, 'label'));
  g.set(c1, 2, r15first !== undefined ? '=IF(B' + r15first + '="","",B' + r15first + ')' : '', swimF_(SWIM_FMT.time, 'formula'));
  g.set(c1 + 1, 1, swimT_('Στροφές'), swimF_(null, 'label'));
  g.set(c1 + 1, 2, L.turnFirst !== null ? '=IF(COUNT(C' + L.turnFirst + ':C' + L.turnLast + ')=0,"",SUM(C' + L.turnFirst + ':C' + L.turnLast + '))' : '', swimF_(SWIM_FMT.time, 'formula'));
  g.set(c1 + 2, 1, swimT_('Τερματισμός'), swimF_(null, 'label'));
  g.set(c1 + 2, 2, '=IF(B' + L.finishRow + '="","",B' + L.finishRow + ')', swimF_(SWIM_FMT.time, 'formula'));
  g.set(c1 + 3, 1, swimT_('Κολύμβηση'), swimF_(null, 'label'));
  g.set(c1 + 3, 2, '=IF(' + L.FT + '="","",' + L.FT + '-SUM(B' + c1 + ':B' + (c1 + 2) + '))', swimF_(SWIM_FMT.time, 'formula'));
  for (var ci = 0; ci < 4; ci++) {
    g.set(c1 + ci, 3, '=IF(OR(B' + (c1 + ci) + '="",' + L.FT + '=""),"",B' + (c1 + ci) + '/' + L.FT + ')', swimF_(SWIM_FMT.pct, 'formula'));
  }
  g.box(cur + 1, 1, c1 + 3, 3);
  L.c1 = c1;
  L.layout.composition = { first: c1, last: c1 + 3 };

  // Σημειώσεις δίπλα στη σύνθεση
  var notes = [
    swimTF_('ΔΑΚ = (Χεριές + Χρόνος από τα 15{0} έως τον τοίχο) / (μέτρα / 10), για κάθε μήκος: σε πισίνα 50μ. /3,5 όπως στο Excel, σε πισίνα 25 /1,0 ανά μήκος.', U) +
      (L.yards ? swimT_(' Σε γιάρδες και «ανά 10yd».') : ''),
    swimTF_('StrRate, DPS, SL μόνο στην καθαρή κολύμβηση (15{0} μετά τον τοίχο έως 5{0} πριν τον επόμενο). DPS: μέτρα ανά χεριά (ελ./υπτ. ανά χέρι) · SL: μέτρα ανά κύκλο.', U),
    swimTF_('Πορτοκαλί ταχύτητα: τμήμα εκκίνησης / στροφής / τελευταίων 5{0} – συμβατική (στον τοίχο μετράει η επαφή, όχι το κεφάλι). Σύγκρινε εκεί τους χρόνους.', U),
    swimT_('Χρόνος στροφής: ανάποδη (ελεύθερο/ύπτιο) από την τελευταία είσοδο χεριού έως την αποκόλληση · ανοιχτή (πεταλούδα, πρόσθιο, αλλαγή στυλ) από την επαφή στον τοίχο · σκυταλοδρομία: χρόνος αλλαγής.'),
    swimT_('SR από χρόνους χεριών: από τα διαστήματα των πατημάτων · Λευκά: πληκτρολόγηση · Γαλάζια: αυτόματα από το Kinovea · Γκρι: τύποι.') +
      (L.yards ? swimT_(' Ταχύτητες σε m/s (1yd = 0,9144μ.), με τις τιμές σε yd/s στις τελευταίες στήλες.') : '')
  ];
  notes.forEach(function (t, i) {
    g.set(c1 + i, 5, t, { italic: true, color: '#555555', size: 9, wrap: true });
    g.merge(c1 + i, 5, c1 + i, L.LAST);
    g.rowHeights[c1 + i] = 30;
  });
  swimKeepEnd_(L, 'composition', c1 + notes.length - 1);
  L.cur = c1 + notes.length + 1;
}

/** Τιμή ή κενό για στατικά κελιά σύγκρισης. */
function swimV_(v, digits) {
  return v === null || v === undefined || !isFinite(v) ? '' : swimFixed_(v, digits === undefined ? 3 : digits);
}

/** Γραμμή της ανάλυσης σύγκρισης στο ίδιο σημείο ελέγχου. */
function swimRowAt_(analysis, d) {
  if (!analysis) return null;
  for (var i = 0; i < analysis.rows.length; i++) if (Math.abs(analysis.rows[i].d - d) < 1e-9) return analysis.rows[i];
  return null;
}

function swimBlockAt_(analysis, to) {
  if (!analysis) return null;
  for (var i = 0; i < analysis.blocks.length; i++) if (analysis.blocks[i].to === to) return analysis.blocks[i];
  return null;
}

function swimTurnAt_(analysis, wall) {
  if (!analysis) return null;
  for (var i = 0; i < analysis.turns.length; i++) if (analysis.turns[i].wall === wall) return analysis.turns[i];
  return null;
}

/** Μεταβολή της ταχύτητας από το προηγούμενο τμήμα (ίδια ανάλυση). */
function swimVelChange_(analysis, d) {
  if (!analysis) return null;
  for (var i = 1; i < analysis.rows.length; i++) {
    if (Math.abs(analysis.rows[i].d - d) < 1e-9) {
      var a = analysis.rows[i - 1].vel, b = analysis.rows[i].vel;
      return a !== null && b !== null ? b - a : null;
    }
  }
  return null;
}

/**
 * Συγκριτική αναφορά (όπως στο Excel): PB του αθλητή, αγώνας, elite. Οι τιμές του αγώνα είναι τύποι προς τους
 * πίνακες του φύλλου, του PB και της αναφοράς στατικές (ξαναγράφονται όταν αλλάζει η επιλογή).
 */
function swimLayoutCompare_(L) {
  var g = L.g, race = L.race, rows = L.rows, T0 = L.T0, U = L.U, x = L.extras;
  var pbA = x.pb ? x.pb.analysis : null, elA = x.elite ? x.elite.analysis : null;
  var pbRace = x.pb ? x.pb.race : null;
  var cur = swimKeep_(L, L.cur, 'compare');
  var cmp = { first: null };
  L.cmp = cmp;

  g.section(cur, swimT_('Συγκριτική αναφορά: PB – αγώνας – elite'), L.LAST);
  // Η ετικέτα του PB με τη φάση του αγώνα στη γλώσσα του report (η x.pb.label μένει όπως τη δίνει η swimRaceLabel_).
  var pbLabel = x.pb ? (x.pb.race ? 'PB: ' + swimRaceLabel_(x.pb.race, x.pb.analysis, true) : x.pb.label) : swimT_('PB: (δεν επιλέχθηκε)');
  var legend = [pbLabel, x.elite ? 'Elite: ' + x.elite.label : swimT_('Elite: (δεν επιλέχθηκε)')].join('   ·   ');
  g.set(cur + 1, 1, legend, { italic: true, color: SWIM_COLORS.dim, size: 9, wrap: true });
  g.merge(cur + 1, 1, cur + 1, L.LAST);
  cur += 2;

  var hdr = function (labels) { g.headers(cur, labels.map(swimT_)); cur++; };
  var cell = function (r, c, v, fmt, kind) { g.set(r, c, v, swimF_(fmt, kind || 'formula')); };
  var colors = function (r, cols, bg) { cols.forEach(function (c) { var o = g.get(r, c); if (o) o.bg = bg; }); };

  // 1) Περάσματα και ταχύτητα ανά τμήμα
  g.set(cur, 1, swimT_('Περάσματα και ταχύτητα ανά τμήμα'), { bold: true, color: SWIM_COLORS.title, size: 10 });
  g.merge(cur, 1, cur, L.LAST);
  cur++;
  hdr([swimTF_('Απόσταση ({0})', U), 'Χρόνος PB', 'Χρόνος', 'Χρόνος elite', 'Δ από PB (s)', 'Δ από elite (s)',
    'Ταχ. PB (m/s)', 'Ταχ. (m/s)', 'Ταχ. elite (m/s)', 'Χρόνος τμήματος PB', 'Χρόνος τμήματος', 'Χρόνος τμήματος elite',
    'Μεταβολή ταχ. PB', 'Μεταβολή ταχ.', 'Μεταβολή ταχ. elite']);
  cmp.passFirst = cur;
  rows.forEach(function (row, i) {
    var r = cur + i, tr = T0 + i;
    var pr = swimRowAt_(pbA, row.d), er = swimRowAt_(elA, row.d);
    g.set(r, 1, row.d, swimF_(null, 'label'));
    cell(r, 2, pr ? swimV_(pr.t) : '', SWIM_FMT.time, 'auto');
    cell(r, 3, '=IF(B' + tr + '="","",B' + tr + ')', SWIM_FMT.time);
    cell(r, 4, er ? swimV_(er.t) : '', SWIM_FMT.time, 'auto');
    cell(r, 5, '=IF(OR(C' + r + '="",B' + r + '=""),"",C' + r + '-B' + r + ')', '+0.00;-0.00;0.00');
    cell(r, 6, '=IF(OR(C' + r + '="",D' + r + '=""),"",C' + r + '-D' + r + ')', '+0.00;-0.00;0.00');
    cell(r, 7, pr ? swimV_(pr.vel) : '', SWIM_FMT.vel, 'auto');
    cell(r, 8, '=IF(C' + tr + '="","",C' + tr + ')', SWIM_FMT.vel);
    cell(r, 9, er ? swimV_(er.vel) : '', SWIM_FMT.vel, 'auto');
    cell(r, 10, pr ? swimV_(pr.segTime) : '', SWIM_FMT.time, 'auto');
    cell(r, 11, '=IF(K' + tr + '="","",K' + tr + ')', SWIM_FMT.time);
    cell(r, 12, er ? swimV_(er.segTime) : '', SWIM_FMT.time, 'auto');
    cell(r, 13, swimV_(swimVelChange_(pbA, row.d)), '+0.00;-0.00;0.00', 'auto');
    cell(r, 14, i > 0 ? '=IF(OR(C' + tr + '="",C' + (tr - 1) + '=""),"",C' + tr + '-C' + (tr - 1) + ')' : '', '+0.00;-0.00;0.00');
    cell(r, 15, swimV_(swimVelChange_(elA, row.d)), '+0.00;-0.00;0.00', 'auto');
    if (!row.isFree) colors(r, [7, 8, 9], SWIM_COLORS.phase);
  });
  cmp.passLast = cur + rows.length - 1;
  g.box(cmp.passFirst - 1, 1, cmp.passLast, 15);
  swimKeepEnd_(L, 'compare', cmp.passLast, cmp.passFirst + 2);
  cur = cmp.passLast + 2;

  // 2) Συχνότητα, μήκος χεριάς, κύκλου και χεριές (καθαρή κολύμβηση)
  cur = swimKeep_(L, cur, 'compareTable');
  g.set(cur, 1, swimT_('Συχνότητα και μήκος χεριάς (καθαρή κολύμβηση)'), { bold: true, color: SWIM_COLORS.title, size: 10 });
  g.merge(cur, 1, cur, L.LAST);
  cur++;
  hdr([swimTF_('Απόσταση ({0})', U), 'SR PB', 'SR', 'SR elite', 'DPS PB', 'DPS', 'DPS elite', 'SL PB', 'SL', 'SL elite', 'Χεριές PB', 'Χεριές', 'Χεριές elite']);
  cmp.strokeFirst = cur;
  rows.forEach(function (row, i) {
    var r = cur + i, tr = T0 + i;
    var pr = swimRowAt_(pbA, row.d), er = swimRowAt_(elA, row.d);
    g.set(r, 1, row.d, swimF_(null, 'label'));
    cell(r, 2, pr ? swimV_(pr.sr) : '', SWIM_FMT.sr, 'auto');
    cell(r, 3, '=IF(I' + tr + '="","",I' + tr + ')', SWIM_FMT.sr);
    cell(r, 4, er ? swimV_(er.sr) : '', SWIM_FMT.sr, 'auto');
    cell(r, 5, pr ? swimV_(pr.dps) : '', SWIM_FMT.dps, 'auto');
    cell(r, 6, '=IF(J' + tr + '="","",J' + tr + ')', SWIM_FMT.dps);
    cell(r, 7, er ? swimV_(er.dps) : '', SWIM_FMT.dps, 'auto');
    cell(r, 8, pr ? swimV_(pr.sl) : '', SWIM_FMT.dps, 'auto');
    cell(r, 9, '=IF(O' + tr + '="","",O' + tr + ')', SWIM_FMT.dps);
    cell(r, 10, er ? swimV_(er.sl) : '', SWIM_FMT.dps, 'auto');
    cell(r, 11, pr ? swimV_(pr.strokes, 2) : '', SWIM_FMT.strokes, 'auto');
    cell(r, 12, '=IF(H' + tr + '="","",H' + tr + ')', SWIM_FMT.strokes);
    cell(r, 13, er ? swimV_(er.strokes, 2) : '', SWIM_FMT.strokes, 'auto');
  });
  cmp.strokeLast = cur + rows.length - 1;
  g.box(cmp.strokeFirst - 1, 1, cmp.strokeLast, 13);
  swimKeepEnd_(L, 'compareTable', cmp.strokeLast, cmp.strokeFirst + 2);
  cur = cmp.strokeLast + 2;

  // 3) Ανά 50
  var blocks = L.analysis.blocks;
  if (blocks.length && L.p50First !== null) {
    cur = swimKeep_(L, cur, 'compareTable');
    g.set(cur, 1, swimTF_('Ανά 50{0}: χρόνος, ΔΑΚ, Swolf, συχνότητα', U), { bold: true, color: SWIM_COLORS.title, size: 10 });
    g.merge(cur, 1, cur, L.LAST);
    cur++;
    hdr(['50' + U, 'Χρόνος PB', 'Χρόνος', 'Χρόνος elite', 'ΔΑΚ PB', 'ΔΑΚ', 'ΔΑΚ elite', 'Swolf PB', 'Swolf', 'Swolf elite', 'SR PB', 'SR', 'SR elite']);
    cmp.p50First = cur;
    blocks.forEach(function (b, i) {
      var r = cur + i, pr = L.per50Rows[b.to];
      var pb = swimBlockAt_(pbA, b.to), el = swimBlockAt_(elA, b.to);
      g.set(r, 1, b.from + '-' + b.to, swimF_('@', 'label'));
      cell(r, 2, pb ? swimV_(pb.time) : '', SWIM_FMT.time, 'auto');
      cell(r, 3, pr ? '=IF(B' + pr + '="","",B' + pr + ')' : '', SWIM_FMT.time);
      cell(r, 4, el ? swimV_(el.time) : '', SWIM_FMT.time, 'auto');
      cell(r, 5, pb ? swimV_(pb.dak) : '', SWIM_FMT.vel, 'auto');
      cell(r, 6, pr ? '=IF(H' + pr + '="","",H' + pr + ')' : '', SWIM_FMT.vel);
      cell(r, 7, el ? swimV_(el.dak) : '', SWIM_FMT.vel, 'auto');
      cell(r, 8, pb ? swimV_(pb.swolf) : '', SWIM_FMT.time, 'auto');
      cell(r, 9, pr ? '=IF(G' + pr + '="","",G' + pr + ')' : '', SWIM_FMT.time);
      cell(r, 10, el ? swimV_(el.swolf) : '', SWIM_FMT.time, 'auto');
      cell(r, 11, pb ? swimV_(pb.sr) : '', SWIM_FMT.sr, 'auto');
      cell(r, 12, pr ? '=IF(E' + pr + '="","",E' + pr + ')' : '', SWIM_FMT.sr);
      cell(r, 13, el ? swimV_(el.sr) : '', SWIM_FMT.sr, 'auto');
      [5, 6, 7].forEach(function (c) { var o = g.get(r, c); o.bold = true; o.color = SWIM_COLORS.dakText; });
    });
    cmp.p50Last = cur + blocks.length - 1;
    g.box(cmp.p50First - 1, 1, cmp.p50Last, 13);
    swimKeepEnd_(L, 'compareTable', cmp.p50Last, cmp.p50First + 2);
    cur = cmp.p50Last + 2;
  }

  // 4) Στροφές
  var turns = L.analysis.turns;
  if (turns.length && L.turnFirst !== null) {
    cur = swimKeep_(L, cur, 'compareTable');
    g.set(cur, 1, swimT_('Στροφές'), { bold: true, color: SWIM_COLORS.title, size: 10 });
    g.merge(cur, 1, cur, L.LAST);
    cur++;
    hdr([swimTF_('Τοίχος ({0})', U), '-5/+15 PB', '-5/+15', '-5/+15 elite', 'Τοίχος→+15 PB', 'Τοίχος→+15', 'Τοίχος→+15 elite',
      'Χρόνος στροφής PB', 'Χρόνος στροφής', 'Υποβρύχια PB', 'Υποβρύχια', 'Δελφινισμοί PB', 'Δελφινισμοί']);
    cmp.turnFirst = cur;
    turns.forEach(function (t, i) {
      var r = cur + i, tr = L.turnRows[t.wall];
      var pt = swimTurnAt_(pbA, t.wall), et = swimTurnAt_(elA, t.wall);
      g.set(r, 1, t.wall, swimF_(null, 'label'));
      cell(r, 2, pt ? swimV_(pt.inOut) : '', SWIM_FMT.time, 'auto');
      cell(r, 3, '=IF(C' + tr + '="","",C' + tr + ')', SWIM_FMT.time);
      cell(r, 4, et ? swimV_(et.inOut) : '', SWIM_FMT.time, 'auto');
      cell(r, 5, pt ? swimV_(pt.out15) : '', SWIM_FMT.time, 'auto');
      cell(r, 6, '=IF(L' + tr + '="","",L' + tr + ')', SWIM_FMT.time);
      cell(r, 7, et ? swimV_(et.out15) : '', SWIM_FMT.time, 'auto');
      cell(r, 8, pt ? swimV_(swimManualTurn_(pbRace, t.wall, 'turnTime', pt.turnTime)) : '', SWIM_FMT.time, 'auto');
      cell(r, 9, '=IF(D' + tr + '="","",D' + tr + ')', SWIM_FMT.time);
      cell(r, 10, pt ? swimV_(pt.underwaterTime) : '', SWIM_FMT.time, 'auto');
      cell(r, 11, '=IF(F' + tr + '="","",F' + tr + ')', SWIM_FMT.time);
      cell(r, 12, pt && pt.kicks !== null ? pt.kicks : '', SWIM_FMT.int, 'auto');
      cell(r, 13, '=IF(G' + tr + '="","",G' + tr + ')', SWIM_FMT.int);
    });
    cmp.turnLast = cur + turns.length - 1;
    g.box(cmp.turnFirst - 1, 1, cmp.turnLast, 13);
    swimKeepEnd_(L, 'compareTable', cmp.turnLast, cmp.turnFirst + 2);
    cur = cmp.turnLast + 2;
  }

  // 5) Εκκίνηση, φάσεις και τερματισμός: μία γραμμή για καθένα
  cur = swimKeep_(L, cur, 'compareTable');
  g.set(cur, 1, swimT_('Εκκίνηση, φάσεις και τελικός χρόνος'), { bold: true, color: SWIM_COLORS.title, size: 10 });
  g.merge(cur, 1, cur, L.LAST);
  cur++;
  hdr(['', 'Αντίδραση (s)', 'Βατήρας (s)', 'Είσοδος (s)', 'Πτήση (s)', 'Έξοδος (s)', 'Δελφινισμοί', swimTF_('Εκκίνηση 0-15{0} (s)', U),
    'Στροφές -5/+15 (s)', swimTF_('Τελευταία 5{0} (s)', U), 'Κολύμβηση (s)', 'Τελικός χρόνος (s)']);
  cmp.phaseFirst = cur;
  var sv = L.startValues;
  var phaseRow = function (r, label, a, rr, isRace) {
    g.set(r, 1, label, swimF_(null, 'label'));
    if (isRace) {
      cell(r, 2, '=IF(A' + sv + '="","",A' + sv + ')', SWIM_FMT.time);
      cell(r, 3, '=IF(B' + sv + '="","",B' + sv + ')', SWIM_FMT.time);
      cell(r, 4, '=IF(C' + sv + '="","",C' + sv + ')', SWIM_FMT.time);
      cell(r, 5, '=IF(N' + sv + '="","",N' + sv + ')', SWIM_FMT.time);
      cell(r, 6, '=IF(I' + sv + '="","",I' + sv + ')', SWIM_FMT.time);
      cell(r, 7, '=IF(J' + sv + '="","",J' + sv + ')', SWIM_FMT.int);
      cell(r, 8, '=IF(B' + L.c1 + '="","",B' + L.c1 + ')', SWIM_FMT.time);
      cell(r, 9, '=IF(B' + (L.c1 + 1) + '="","",B' + (L.c1 + 1) + ')', SWIM_FMT.time);
      cell(r, 10, '=IF(B' + (L.c1 + 2) + '="","",B' + (L.c1 + 2) + ')', SWIM_FMT.time);
      cell(r, 11, '=IF(B' + (L.c1 + 3) + '="","",B' + (L.c1 + 3) + ')', SWIM_FMT.time);
      cell(r, 12, '=IF(' + L.FT + '="","",' + L.FT + ')', SWIM_FMT.time);
      return;
    }
    var m = rr && rr.manual ? rr.manual : {};
    var st = a ? a.start : {};
    var pick = function (manual, auto) { var v = swimNum_(manual); return v !== null ? v : auto; };
    cell(r, 2, swimV_(swimNum_(m.reaction)), SWIM_FMT.time, 'auto');
    cell(r, 3, swimV_(pick(m.blockTime, st.blockTime)), SWIM_FMT.time, 'auto');
    cell(r, 4, swimV_(pick(m.entryTime, st.entryTime)), SWIM_FMT.time, 'auto');
    cell(r, 5, swimV_(st.flightTime), SWIM_FMT.time, 'auto');
    cell(r, 6, swimV_(pick(m.underwaterTime, st.underwaterTime)), SWIM_FMT.time, 'auto');
    cell(r, 7, st.kicks !== null && st.kicks !== undefined ? st.kicks : '', SWIM_FMT.int, 'auto');
    var s = a ? a.summary : {};
    cell(r, 8, swimV_(s.startTime), SWIM_FMT.time, 'auto');
    cell(r, 9, swimV_(s.turnsTime), SWIM_FMT.time, 'auto');
    cell(r, 10, swimV_(s.finishTime), SWIM_FMT.time, 'auto');
    var swim = s.finalTime && s.startTime !== null && s.finishTime !== null ? s.finalTime - s.startTime - (s.turnsTime || 0) - s.finishTime : null;
    cell(r, 11, swimV_(swim), SWIM_FMT.time, 'auto');
    cell(r, 12, swimV_(s.finalTime), SWIM_FMT.time, 'auto');
  };
  phaseRow(cur, 'PB', pbA, pbRace, false);
  phaseRow(cur + 1, swimT_('Αγώνας'), null, null, true);
  phaseRow(cur + 2, 'Elite', elA, x.elite ? x.elite.race : null, false);
  cmp.phaseLast = cur + 2;
  g.box(cmp.phaseFirst - 1, 1, cmp.phaseLast, 12);
  swimKeepEnd_(L, 'compareTable', cmp.phaseLast);
  L.cur = cmp.phaseLast + 2;
  L.layout.compare = cmp;
}

/** Χειροκίνητος χρόνος στροφής (αν γράφτηκε στο πάνελ) ή ο αυτόματος. */
function swimManualTurn_(race, wall, key, auto) {
  var m = race && race.manual && race.manual.turns ? race.manual.turns[String(wall)] : null;
  var v = m ? swimNum_(m[key]) : null;
  return v !== null ? v : auto;
}

/** Πίνακες επιπέδων 2015–2024: εκκίνηση 0-15, στροφή τοίχος → +15 και στροφές μικτής. */
function swimLayoutBenchmarks_(L) {
  var g = L.g, b = L.extras.benchmarks, U = L.U, cur = L.cur;
  var levels = b.levels.map(swimT_);
  var bench = { rows: [] };
  L.bench = bench;
  cur = swimKeep_(L, cur, 'bench');
  g.section(cur, swimT_('Επίπεδα (Δεδομένα 2015–2024)'), L.LAST);
  cur++;
  g.headers(cur, [swimT_('Δείκτης')].concat(levels).concat([swimT_('Αθλητής'), swimT_('Επίπεδο αθλητή')]));
  var hdrRow = cur;
  cur++;
  var levelFormula = function (r) {
    // Το καλύτερο επίπεδο που φτάνει ο αθλητής (μικρότερος χρόνος = καλύτερο).
    var expr = '"' + swimTF_('κάτω από {0}', levels[levels.length - 1]) + '"';
    for (var i = levels.length - 1; i >= 0; i--) {
      var c = swimColLetter_(2 + i);
      expr = 'IF(AND(' + c + r + '<>"",H' + r + '<=' + c + r + '),"' + levels[i] + '",' + expr + ')';
    }
    return '=IF(H' + r + '="","",' + expr + ')';
  };
  var addRow = function (label, values, athleteFormula) {
    var r = cur;
    g.set(r, 1, label, swimF_(null, 'label'));
    for (var i = 0; i < levels.length; i++) g.set(r, 2 + i, values[i] === null || values[i] === undefined ? '' : values[i], swimF_(SWIM_FMT.time, 'auto'));
    g.set(r, 8, athleteFormula, { fmt: SWIM_FMT.time, bg: SWIM_COLORS.kpiDak, bold: true, color: SWIM_COLORS.dakText, align: 'center' });
    g.set(r, 9, levelFormula(r), { bg: SWIM_COLORS.label, bold: true, align: 'center', color: SWIM_COLORS.title });
    g.merge(r, 9, r, 10);
    bench.rows.push({ r: r, label: label });
    cur++;
    return r;
  };
  if (b.start) {
    var r15 = L.rowOf[15];
    bench.start = addRow(swimTF_('Εκκίνηση 0-15{0}', U), b.start.values, r15 !== undefined ? '=IF(B' + r15 + '="","",B' + r15 + ')' : '');
  }
  if (b.turn && L.turnFirst !== null) {
    bench.turn = addRow(swimTF_('Στροφή → +15{0} (μ.ό.)', U), b.turn.values,
      '=IFERROR(AVERAGE(L' + L.turnFirst + ':L' + L.turnLast + '),"")');
    bench.turnBest = addRow(swimTF_('Στροφή → +15{0} (καλύτερη)', U), b.turn.values,
      '=IF(COUNT(L' + L.turnFirst + ':L' + L.turnLast + ')=0,"",MIN(L' + L.turnFirst + ':L' + L.turnLast + '))');
  }
  bench.im = [];
  if (b.im && b.im.length && L.turnFirst !== null) {
    L.analysis.turns.forEach(function (t) {
      var match = b.im.filter(function (x) { return x.from === t.styleIn && x.to === t.styleOut; })[0];
      if (!match) return;
      var tr = L.turnRows[t.wall];
      var r = addRow(swimTF_('Στροφή {0}: {1} → {2}', t.wall + U, swimStyleName_(t.styleIn), swimStyleName_(t.styleOut)), match.values,
        '=IF(L' + tr + '="","",L' + tr + ')');
      bench.im.push(r);
    });
  }
  g.box(hdrRow, 1, cur - 1, 10);

  // Βοηθητικός πίνακας για τα γραφήματα: επίπεδα και αθλητής σε ξεχωριστές σειρές (ο αθλητής με άλλο χρώμα).
  if (bench.start || bench.turn) {
    var h0 = cur + 1;
    g.set(h0, 1, swimT_('Για τα γραφήματα'), { italic: true, color: SWIM_COLORS.dim, size: 9 });
    g.headers(h0 + 1, [swimT_('Επίπεδο'), swimT_('Εκκίνηση: επίπεδα'), swimT_('Εκκίνηση: αθλητής'), swimT_('Στροφή: επίπεδα'), swimT_('Στροφή: αθλητής')]);
    var names = levels.concat([swimT_('Αθλητής')]);
    names.forEach(function (name, i) {
      var r = h0 + 2 + i, isAthlete = i === levels.length;
      var col = swimColLetter_(2 + i);
      g.set(r, 1, name, swimF_('@', 'label'));
      var pair = function (srcRow, c) {
        if (!srcRow) { g.set(r, c, '', swimF_(null, 'formula')); g.set(r, c + 1, '', swimF_(null, 'formula')); return; }
        g.set(r, c, isAthlete ? '' : '=IF(' + col + srcRow + '="","",' + col + srcRow + ')', swimF_(SWIM_FMT.time, 'formula'));
        g.set(r, c + 1, isAthlete ? '=IF(H' + srcRow + '="","",H' + srcRow + ')' : '', swimF_(SWIM_FMT.time, 'formula'));
      };
      pair(bench.start, 2);
      pair(bench.turn, 4);
    });
    bench.helper = { first: h0 + 2, last: h0 + 2 + levels.length };
    g.box(h0 + 1, 1, bench.helper.last, 5);
    cur = bench.helper.last + 1;
  }
  swimKeepEnd_(L, 'bench', bench.helper ? bench.helper.last : cur - 1, hdrRow + 2);
  L.cur = cur + 1;
  L.layout.bench = bench;
}

/** Μικτή: συμμετοχή κάθε στυλ στον χρόνο · σκυταλοδρομίες: διαδρομές · τελικός: 50άρια του αθλητή και των φιναλίστ. */
function swimLayoutExtras_(L) {
  var g = L.g, race = L.race, rowOf = L.rowOf, U = L.U, cur = L.cur;
  L.extra = {};
  var legs = 4, leg = race.distance / legs;
  var legsOk = (race.style === 'im' || race.relay) && leg > 0 && Math.abs(leg - Math.round(leg)) < 1e-9;
  if (legsOk) {
    var title = swimT_(race.relay ? 'Διαδρομές της σκυταλοδρομίας' : 'Συμμετοχή των στυλ στον συνολικό χρόνο');
    cur = swimKeep_(L, cur, 'legs');
    g.section(cur, title, L.LAST);
    cur++;
    g.headers(cur, [swimT_(race.relay ? 'Διαδρομή' : 'Στυλ'), swimT_('Χρόνος (s)'), swimT_('Ποσοστό'), swimT_('Μέση ταχύτητα (m/s)')]);
    var first = cur + 1;
    for (var i = 0; i < legs; i++) {
      var r = first + i, dEnd = leg * (i + 1), dStart = leg * i;
      var re = rowOf[dEnd], rs = dStart === 0 ? null : rowOf[dStart];
      var name = race.style === 'im' ? swimStyleName_(swimStyleAt_(race, dEnd)) : swimTF_('{0}η διαδρομή', i + 1);
      if (race.relay && race.style === 'im') name = swimTF_('{0}η: {1}', i + 1, name);
      g.set(r, 1, name, swimF_('@', 'label'));
      if (re !== undefined && rs !== undefined)
        g.set(r, 2, rs === null ? '=IF(B' + re + '="","",B' + re + ')' : '=IF(OR(B' + re + '="",B' + rs + '=""),"",B' + re + '-B' + rs + ')', swimF_(SWIM_FMT.time, 'formula'));
      else g.set(r, 2, '', swimF_(SWIM_FMT.time, 'formula'));
      g.set(r, 3, '=IF(OR(B' + r + '="",' + L.FT + '=""),"",B' + r + '/' + L.FT + ')', swimF_(SWIM_FMT.pct, 'formula'));
      g.set(r, 4, '=IF(OR(B' + r + '="",B' + r + '=0),"",' + leg + L.K + '/B' + r + ')', swimF_(SWIM_FMT.vel, 'formula'));
    }
    g.box(cur, 1, first + legs - 1, 4);
    L.extra.legs = { first: first, last: first + legs - 1 };
    swimKeepEnd_(L, 'legs', first + legs - 1);
    cur = first + legs + 1;
  }

  var fin = L.extras.finalists || [];
  if (fin.length >= 2 && L.analysis.blocks.length && L.p50First !== null) {
    cur = swimKeep_(L, cur, 'finalists');
    g.section(cur, L.extras.finalsLabel
      ? swimTF_('Κατανομή ανά 50{0}: αθλητής – {1} ({2} φιναλίστ)', U, swimL_(L.extras.finalsLabel), fin.length)
      : swimTF_('Κατανομή ανά 50{0}: αθλητής – τελικός ({1} αναφορές της ίδιας πηγής)', U, fin.length), L.LAST);
    cur++;
    var cols = ['50' + U, swimT_('Αθλητής'), swimT_('Μέσος όρος τελικού'), swimT_('Καλύτερος'), swimT_('Δ από μ.ο. (s)')];
    var shown = fin.slice(0, 8);
    shown.forEach(function (f) { cols.push(f.label); });
    g.headers(cur, cols);
    var f0 = cur + 1;
    L.analysis.blocks.forEach(function (b, i) {
      var r = f0 + i, pr = L.per50Rows[b.to];
      g.set(r, 1, b.from + '-' + b.to, swimF_('@', 'label'));
      g.set(r, 2, pr ? '=IF(B' + pr + '="","",B' + pr + ')' : '', swimF_(SWIM_FMT.time, 'formula'));
      var vals = fin.map(function (f) { var x = swimBlockAt_(f.analysis, b.to); return x ? x.time : null; }).filter(function (v) { return v !== null; });
      g.set(r, 3, vals.length ? swimV_(vals.reduce(function (a, c) { return a + c; }, 0) / vals.length) : '', swimF_(SWIM_FMT.time, 'auto'));
      g.set(r, 4, vals.length ? swimV_(Math.min.apply(null, vals)) : '', swimF_(SWIM_FMT.time, 'auto'));
      g.set(r, 5, '=IF(OR(B' + r + '="",C' + r + '=""),"",B' + r + '-C' + r + ')', swimF_('+0.00;-0.00;0.00', 'formula'));
      shown.forEach(function (f, j) {
        var x = swimBlockAt_(f.analysis, b.to);
        g.set(r, 6 + j, x ? swimV_(x.time) : '', swimF_(SWIM_FMT.time, 'auto'));
      });
    });
    var fl = f0 + L.analysis.blocks.length - 1;
    g.box(cur, 1, fl, 5 + shown.length);
    L.extra.finalists = { first: f0, last: fl };
    swimKeepEnd_(L, 'finalists', fl, f0 + 2);
    cur = fl + 2;
  }
  L.cur = cur;
}

/** Η κάρτα σύνοψης του βίντεο σε όλο το πλάτος (16:9): ύψος εικόνας και γραμμές της ενότητας (τίτλος, εικόνα, κενό). */
var SWIM_GRAPHIC = { width: 1116, height: 628 };

function swimSummaryGraphicRows_(race) {
  if (!race.graphics || !race.graphics.summary || race.training) return 0;
  return 1 + Math.ceil((SWIM_GRAPHIC.height + 8) / SWIM_ROW_H) + 1;
}

/**
 * Η κάρτα σύνοψης του αγώνα με τα γραφικά του βίντεο (Kinovea), στην πρώτη σελίδα κάτω από τα πλακίδια: ο χρόνος, η θέση
 * στον τελικό ή το Δ, αντίδραση, 15μ., έξοδος, χρόνοι, χεριές, συχνότητα, απόσταση και ταχύτητα ανά τμήμα και κύκλο.
 */
function swimLayoutSummaryGraphic_(L) {
  var rows = swimSummaryGraphicRows_(L.race);
  if (!rows) return;
  var g = L.g, top = 12;
  g.section(top, swimT_('Ο αγώνας με τα γραφικά του βίντεο (Kinovea)'), L.LAST);
  var full = swimLayoutWidth_(L.LAST);
  L.layout.images.push({ fileId: L.race.graphics.summary, row: top + 1, x: Math.round((full - SWIM_GRAPHIC.width) / 2), offsetY: 4,
    width: SWIM_GRAPHIC.width, height: SWIM_GRAPHIC.height, fit: true });
}

/**
 * Ο αγώνας απέναντι στον τελικό του αγωνίσματος (ή στην αναφορά) μετά τον τερματισμό, με τα γραφικά του βίντεο: ο πίνακας
 * του τελικού, ο πίνακας του αγώνα, το Δ σε κάθε πέρασμα και η ταχύτητα ανά τμήμα. Ολόκληρος σε μία σελίδα.
 */
function swimLayoutVersusGraphic_(L) {
  var gr = L.race.graphics;
  if (!gr || !gr.versus || L.race.training) return;
  var g = L.g;
  var cur = swimKeep_(L, L.cur, 'versus', true);
  g.section(cur, L.race.finals && L.race.finals.label ? swimT_('Ο αγώνας ανάμεσα στους φιναλίστ στο βίντεο: ') + swimL_(L.race.finals.label)
    : swimT_('Ο αγώνας απέναντι στην αναφορά στο βίντεο'), L.LAST);
  var full = swimLayoutWidth_(L.LAST);
  var top = cur + 1, rows = Math.ceil((SWIM_GRAPHIC.height + 8) / SWIM_ROW_H);
  L.layout.images.push({ fileId: gr.versus, row: top, x: Math.round((full - SWIM_GRAPHIC.width) / 2), offsetY: 4,
    width: SWIM_GRAPHIC.width, height: SWIM_GRAPHIC.height, fit: true });
  swimKeepEnd_(L, 'versus', top + rows - 1);
  L.cur = top + rows + 1;
}

/**
 * Το καρέ του τερματισμού από το Kinovea, με τα γραφικά του βίντεο (χρόνος, πέρασμα, γράφημα ταχύτητας): μεγάλη
 * εικόνα στο κέντρο, πριν από τα γραφήματα, ολόκληρη σε μία σελίδα.
 */
function swimLayoutFinalFrame_(L) {
  var ff = L.race.finalFrame;
  if (!ff || !ff.fileId) return;
  var g = L.g, W = L.LAST;
  var cur = swimKeep_(L, L.cur, 'finalFrame', true);
  var at = ff.t !== null && ff.t !== undefined && isFinite(ff.t) ? swimT_(' · καρέ στο ') + swimFormatTime_(ff.t) + ' s' : '';
  g.section(cur, swimT_('Τερματισμός στο βίντεο (Kinovea)') + at, W);
  var full = swimLayoutWidth_(L.LAST);
  var width = Math.min(900, full - 40), height = Math.round(width * 9 / 16);
  var top = cur + 1, rows = Math.ceil((height + 12) / SWIM_ROW_H);
  L.layout.images.push({ fileId: ff.fileId, row: top, x: Math.round((full - width) / 2), offsetY: 6, width: width, height: height, fit: true });
  swimKeepEnd_(L, 'finalFrame', top + rows - 1);
  L.cur = top + rows + 1;
}

/**
 * Δύο γραφήματα ή εικόνες δίπλα-δίπλα μέσα στις στήλες A-O (1124 px): πλάτος καθενός, θέση του δεξιού (px) και
 * ύψος εικόνας 16:9. Τα γραφήματα έχουν ύψος 300 px.
 */
var SWIM_PAIR = { width: 546, right: 578, imageHeight: 307 };
var SWIM_CHART_H = 300;
var SWIM_CHART_ROWS = Math.ceil((SWIM_CHART_H + 10) / SWIM_ROW_H);
var SWIM_IMAGE_ROWS = Math.ceil((SWIM_PAIR.imageHeight + 8) / SWIM_ROW_H);

/** Η ενότητα «Γραφήματα»: δύο ανά σειρά, κάθε σειρά ολόκληρη σε μία σελίδα. Επιστρέφει την επόμενη γραμμή. */
function swimPlaceCharts_(L, specs, cur, W) {
  var g = L.g, seen = {};
  // Σταθερό κλειδί κάθε γραφήματος (είδος και τίτλος): με αυτό βρίσκεται η εικόνα του από το Kinovea.
  specs.forEach(function (spec) {
    var base = spec.type + '|' + spec.title;
    seen[base] = (seen[base] || 0) + 1;
    spec.key = 'chart-' + swimHash_(base + '|' + seen[base]);
  });
  for (var i = 0; i < specs.length; i += 2) {
    cur = swimKeep_(L, cur, 'charts', true);
    if (i === 0) { g.section(cur, swimT_('Γραφήματα'), W); cur++; }
    for (var j = 0; j < 2 && i + j < specs.length; j++) {
      var spec = specs[i + j], pos = swimColumnAt_(j === 0 ? 0 : SWIM_PAIR.right);
      spec.row = cur;
      spec.col = pos.col;
      spec.offsetX = pos.offset;
      spec.width = SWIM_PAIR.width;
      spec.height = SWIM_CHART_H;
      L.layout.charts.push(spec);
    }
    swimKeepEnd_(L, 'charts', cur + SWIM_CHART_ROWS - 1);
    cur += SWIM_CHART_ROWS;
  }
  return cur + 1;
}

/** Όλα τα γραφήματα (όπως στο Excel), δύο ανά σειρά. */
function swimLayoutCharts_(L) {
  var g = L.g, race = L.race, T0 = L.T0, last = L.lastRow, U = L.U, cmp = L.cmp, cur = L.cur;
  var specs = [];
  var add = function (spec) { specs.push(spec); };
  // Οι ονομασίες των σειρών στη γλώσσα του report.
  var tl = function (labels) { return labels.map(swimT_); };
  var C = SWIM_COLORS;
  var trio = [C.pb, C.athlete, C.elite];
  var hasCmp = !!cmp;

  // Ταχύτητα / συχνότητα / μήκος χεριάς ανά τμήμα (με PB και elite όταν υπάρχουν)
  if (hasCmp) {
    add({ type: 'line', title: swimT_('Ταχύτητα ανά τμήμα (m/s)'), ranges: ['A' + cmp.passFirst + ':A' + cmp.passLast, 'G' + cmp.passFirst + ':I' + cmp.passLast],
      colors: trio, vTitle: 'm/s', hTitle: U, labels: tl(['PB', 'Αγώνας', 'Elite']) });
    add({ type: 'line', title: swimT_('Συχνότητα χεριάς (κύκλοι/λεπτό)'), ranges: ['A' + cmp.strokeFirst + ':A' + cmp.strokeLast, 'B' + cmp.strokeFirst + ':D' + cmp.strokeLast],
      colors: trio, vTitle: swimT_('κύκλοι/λεπτό'), hTitle: U, labels: tl(['PB', 'Αγώνας', 'Elite']) });
    add({ type: 'line', title: swimT_('Μήκος χεριάς DPS (m/χεριά)'), ranges: ['A' + cmp.strokeFirst + ':A' + cmp.strokeLast, 'E' + cmp.strokeFirst + ':G' + cmp.strokeLast],
      colors: trio, vTitle: swimT_('m/χεριά'), hTitle: U, labels: tl(['PB', 'Αγώνας', 'Elite']) });
    add({ type: 'line', title: swimT_('Διαφορά χρόνου από PB και elite (s)'), ranges: ['A' + cmp.passFirst + ':A' + cmp.passLast, 'E' + cmp.passFirst + ':F' + cmp.passLast],
      colors: [C.pb, C.elite], vTitle: swimT_('s (+ πιο αργά)'), hTitle: U, labels: tl(['Δ από PB', 'Δ από elite']) });
  } else {
    add({ type: 'line', title: swimT_('Ταχύτητα ανά τμήμα (m/s)'), ranges: ['A' + T0 + ':A' + last, 'C' + T0 + ':C' + last], colors: [C.athlete], vTitle: 'm/s', hTitle: U, labels: tl(['Αγώνας']) });
    add({ type: 'line', title: swimT_('Συχνότητα χεριάς (κύκλοι/λεπτό)'), ranges: ['A' + T0 + ':A' + last, 'I' + T0 + ':I' + last], colors: [C.sr], vTitle: swimT_('κύκλοι/λεπτό'), hTitle: U, labels: tl(['Αγώνας']) });
    add({ type: 'line', title: swimT_('Μήκος χεριάς DPS (m/χεριά)'), ranges: ['A' + T0 + ':A' + last, 'J' + T0 + ':J' + last], colors: [C.dps], vTitle: swimT_('m/χεριά'), hTitle: U, labels: tl(['Αγώνας']) });
  }
  add({ type: 'combo', title: swimT_('Συχνότητα – μήκος χεριάς – ταχύτητα'), ranges: ['A' + T0 + ':A' + last, 'I' + T0 + ':I' + last, 'J' + T0 + ':J' + last, 'C' + T0 + ':C' + last],
    colors: [C.sr, C.dps, C.athlete], seriesTypes: ['bars', 'line', 'line'], secondAxis: [1, 2], vTitle: swimT_('κύκλοι/λεπτό'), v2Title: swimT_('m/χεριά · m/s'), hTitle: U,
    labels: tl(['Συχνότητα', 'Μήκος χεριάς', 'Ταχύτητα']) });
  if (L.free) {
    add({ type: 'combo', title: swimT_('Καθαρή κολύμβηση: χρόνος και μεταβολή ταχύτητας'),
      ranges: ['A' + L.free.first + ':A' + L.free.last, 'B' + L.free.first + ':B' + L.free.last, 'D' + L.free.first + ':D' + L.free.last],
      colors: [C.athlete, C.dak], seriesTypes: ['bars', 'line'], secondAxis: [1], vTitle: 's', v2Title: 'm/s', labels: tl(['Χρόνος', 'Μεταβολή ταχύτητας']) });
  }
  if (L.p50First !== null) {
    var f = L.p50First, l = L.p50Last;
    add({ type: 'combo', title: swimTF_('Ανά 50{0}: χρόνος, χεριές και συχνότητα', U), ranges: ['A' + f + ':A' + l, 'B' + f + ':B' + l, 'D' + f + ':D' + l, 'E' + f + ':E' + l],
      colors: [C.athlete, C.pb, C.sr], seriesTypes: ['bars', 'bars', 'line'], secondAxis: [2], vTitle: swimT_('s · χεριές'), v2Title: swimT_('κύκλοι/λεπτό'),
      labels: tl(['Χρόνος', 'Χεριές', 'Συχνότητα']) });
    add({ type: 'combo', title: swimTF_('Swolf & ΔΑΚ ανά 50{0}', U), ranges: ['A' + f + ':A' + l, 'G' + f + ':G' + l, 'H' + f + ':H' + l],
      colors: [C.swolf, C.dak], seriesTypes: ['bars', 'bars'], secondAxis: [1], vTitle: 'Swolf', v2Title: swimT_('ΔΑΚ'), labels: tl(['Swolf', 'ΔΑΚ']) });
    if (hasCmp && cmp.p50First) {
      add({ type: 'combo', title: swimTF_('ΔΑΚ ανά 50{0}: PB – αγώνας – elite', U), ranges: ['A' + cmp.p50First + ':A' + cmp.p50Last, 'E' + cmp.p50First + ':G' + cmp.p50Last],
        colors: trio, seriesTypes: ['bars', 'bars', 'bars'], vTitle: swimT_('ΔΑΚ'), labels: tl(['PB', 'Αγώνας', 'Elite']) });
    }
    add({ type: 'combo', title: swimTF_('ΔΑΚ & ταχύτητα ανά 50{0}', U), ranges: ['A' + f + ':A' + l, 'H' + f + ':H' + l, 'I' + f + ':I' + l],
      colors: [C.dak, C.athlete], seriesTypes: ['bars', 'line'], secondAxis: [1], vTitle: swimT_('ΔΑΚ'), v2Title: 'm/s', labels: tl(['ΔΑΚ', 'Ταχύτητα']) });
  }
  if (L.turnFirst !== null) {
    if (hasCmp && cmp.turnFirst) {
      add({ type: 'column', title: swimTF_('Στροφές -5/+15{0}: PB – αγώνας – elite (s)', U), ranges: ['A' + cmp.turnFirst + ':A' + cmp.turnLast, 'B' + cmp.turnFirst + ':D' + cmp.turnLast],
        colors: trio, vTitle: 's', labels: tl(['PB', 'Αγώνας', 'Elite']) });
      add({ type: 'column', title: swimTF_('Στροφές τοίχος → +15{0}: PB – αγώνας – elite (s)', U), ranges: ['A' + cmp.turnFirst + ':A' + cmp.turnLast, 'E' + cmp.turnFirst + ':G' + cmp.turnLast],
        colors: trio, vTitle: 's', labels: tl(['PB', 'Αγώνας', 'Elite']) });
    }
    add({ type: 'combo', title: swimTF_('Στροφές: χρόνος -5/+15{0} και μέση ταχύτητα', U), ranges: ['A' + L.turnFirst + ':A' + L.turnLast, 'C' + L.turnFirst + ':C' + L.turnLast, 'H' + L.turnFirst + ':H' + L.turnLast],
      colors: [C.athlete, C.dak], seriesTypes: ['bars', 'line'], secondAxis: [1], vTitle: 's', v2Title: 'm/s', labels: tl(['Χρόνος -5/+15', 'Μέση ταχύτητα']) });
  }
  add({ type: 'pie', title: swimT_('Σύνθεση αγώνα'), ranges: ['A' + L.c1 + ':A' + (L.c1 + 3), 'B' + L.c1 + ':B' + (L.c1 + 3)],
    colors: [C.athlete, C.dak, C.elite, C.sr] });
  if (hasCmp) {
    add({ type: 'column', title: swimT_('Φάσεις: PB – αγώνας – elite (s)'), stacked: true,
      ranges: ['A' + cmp.phaseFirst + ':A' + cmp.phaseLast, 'H' + cmp.phaseFirst + ':K' + cmp.phaseLast],
      colors: [C.athlete, C.dak, C.elite, C.sr], vTitle: 's', labels: tl(['Εκκίνηση', 'Στροφές', 'Τερματισμός', 'Κολύμβηση']) });
  }
  if (L.bench) {
    var bh = L.bench;
    if (bh.helper && bh.start) {
      add({ type: 'column', title: swimTF_('Εκκίνηση 0-15{0}: επίπεδα και αθλητής (s)', U), stacked: true,
        ranges: ['A' + bh.helper.first + ':A' + bh.helper.last, 'B' + bh.helper.first + ':C' + bh.helper.last],
        colors: [C.elite, C.dak], vTitle: 's', labels: tl(['Επίπεδα 2015–2024', 'Αθλητής']) });
    }
    if (bh.helper && bh.turn) {
      add({ type: 'column', title: swimTF_('Στροφή τοίχος → +15{0}: επίπεδα και αθλητής (s)', U), stacked: true,
        ranges: ['A' + bh.helper.first + ':A' + bh.helper.last, 'D' + bh.helper.first + ':E' + bh.helper.last],
        colors: [C.elite, C.dak], vTitle: 's', labels: tl(['Επίπεδα 2015–2024', 'Αθλητής']) });
    }
    if (bh.im && bh.im.length >= 3) {
      add({ type: 'radar', title: swimT_('Στροφές μικτής: αθλητής και επίπεδα'), ranges: ['A' + bh.im[0] + ':A' + bh.im[bh.im.length - 1],
        'C' + bh.im[0] + ':C' + bh.im[bh.im.length - 1], 'E' + bh.im[0] + ':E' + bh.im[bh.im.length - 1], 'H' + bh.im[0] + ':H' + bh.im[bh.im.length - 1]],
        colors: [C.elite, C.pb, C.dak], labels: tl(['Ολυμπ./Παγκ. 1ος', 'Top 8', 'Αθλητής']) });
    }
  }
  if (L.extra.legs) {
    add({ type: 'pie', title: swimT_(race.relay ? 'Διαδρομές: ποσοστό του χρόνου' : 'Συμμετοχή των στυλ στον χρόνο'),
      ranges: ['A' + L.extra.legs.first + ':A' + L.extra.legs.last, 'B' + L.extra.legs.first + ':B' + L.extra.legs.last],
      colors: [C.athlete, C.dak, C.elite, C.sr] });
  }
  if (L.extra.finalists) {
    var fx = L.extra.finalists;
    add({ type: 'line', title: swimTF_('Ανά 50{0}: αθλητής – μ.ο. τελικού – καλύτερος (s)', U),
      ranges: ['A' + fx.first + ':A' + fx.last, 'B' + fx.first + ':D' + fx.last], colors: [C.athlete, C.pb, C.elite], vTitle: 's',
      labels: tl(['Αθλητής', 'Μέσος όρος τελικού', 'Καλύτερος']) });
  }
  L.cur = swimPlaceCharts_(L, specs, cur, L.LAST);
}

// ---------------------------------------------------------------------------------------------
// Τεχνική ανάλυση εκκινήσεων και στροφών (από το Kinovea: τιμές ανά φάση, γωνίες, στιγμιότυπα)
// ---------------------------------------------------------------------------------------------

/**
 * Οι τιμές που στέλνει το Kinovea για κάθε φάση: κλειδί, ετικέτα, μονάδα (s χρόνος, d απόσταση, v ταχύτητα,
 * n πλήθος, r συχνότητα), είδος φάσης και ποια τιμή είναι η καλύτερη (min / max, κενό όταν δεν έχει νόημα).
 */
var SWIM_TECH_METRICS = [
  { key: 'reaction', label: 'Αντίδραση – πρώτη κίνηση', unit: 's', kind: 'start', best: 'min' },
  { key: 'handsOff', label: 'Χέρια φεύγουν από τον βατήρα', unit: 's', kind: 'start', best: 'min' },
  { key: 'backFoot', label: 'Πίσω πόδι φεύγει', unit: 's', kind: 'start', best: 'min' },
  { key: 'blockTime', label: 'Αποδέσμευση (μπροστινό πόδι)', unit: 's', kind: 'start', best: 'min' },
  { key: 'blockPhase', label: 'Φάση βατήρα: πρώτη κίνηση → αποδέσμευση', unit: 's', kind: 'start', best: 'min' },
  { key: 'flight', label: 'Πτήση: αποδέσμευση → είσοδος', unit: 's', kind: 'start' },
  { key: 'entryTime', label: 'Είσοδος χεριών', unit: 's', kind: 'start', best: 'min' },
  { key: 'entryDist', label: 'Απόσταση εισόδου χεριών', unit: 'd', kind: 'start', best: 'max' },
  { key: 'in5', label: 'Προσέγγιση: -5 → τοίχος', unit: 's', kind: 'turn', best: 'min' },
  { key: 'lastStrokeGap', label: 'Τελευταία χεριά → τοίχος', unit: 's', kind: 'turn' },
  { key: 'lastStrokeDist', label: 'Απόσταση τελευταίας χεριάς από τον τοίχο', unit: 'd', kind: 'turn' },
  { key: 'rotation', label: 'Περιστροφή: αρχή → επαφή', unit: 's', kind: 'turn', best: 'min' },
  { key: 'contact', label: 'Επαφή στον τοίχο: επαφή → αποκόλληση', unit: 's', kind: 'turn', best: 'min' },
  { key: 'turnTime', label: 'Χρόνος στροφής', unit: 's', kind: 'turn', best: 'min' },
  { key: 't5', label: 'Χρόνος στα 5', unit: 's', kind: 'start', best: 'min' },
  { key: 't10', label: 'Χρόνος στα 10', unit: 's', kind: 'start', best: 'min' },
  { key: 't15', label: 'Χρόνος στα 15', unit: 's', kind: 'start', best: 'min' },
  { key: 'out5', label: 'Τοίχος → +5', unit: 's', kind: 'turn', best: 'min' },
  { key: 'out10', label: 'Τοίχος → +10', unit: 's', kind: 'turn', best: 'min' },
  { key: 'out15', label: 'Τοίχος → +15', unit: 's', kind: 'turn', best: 'min' },
  { key: 'inOut5', label: '-5 → +5', unit: 's', kind: 'turn', best: 'min' },
  { key: 'inOut10', label: '-5 → +10', unit: 's', kind: 'turn', best: 'min' },
  { key: 'inOut15', label: '-5 → +15', unit: 's', kind: 'turn', best: 'min' },
  { key: 'v0_5', label: 'Ταχύτητα 0 → 5', unit: 'v', kind: 'both', best: 'max' },
  { key: 'v5_10', label: 'Ταχύτητα 5 → 10', unit: 'v', kind: 'both', best: 'max' },
  { key: 'v10_15', label: 'Ταχύτητα 10 → 15', unit: 'v', kind: 'both', best: 'max' },
  { key: 'underwater', label: 'Υποβρύχια φάση (έως την έξοδο)', unit: 's', kind: 'both' },
  { key: 'kicks', label: 'Υποβρύχιοι δελφινισμοί', unit: 'n', kind: 'both' },
  { key: 'kickRate', label: 'Συχνότητα δελφινισμών', unit: 'r', kind: 'both' },
  { key: 'kickDist', label: 'Απόσταση ανά δελφινισμό', unit: 'd', kind: 'both' },
  { key: 'breakoutTime', label: 'Έξοδος από το νερό', unit: 's', kind: 'both' },
  { key: 'breakoutDist', label: 'Απόσταση εξόδου (κεφάλι)', unit: 'd', kind: 'both' },
  { key: 'uwVel', label: 'Μέση ταχύτητα υποβρύχιας φάσης', unit: 'v', kind: 'both', best: 'max' },
  { key: 'firstStroke', label: 'Πρώτη χεριά μετά την έξοδο', unit: 's', kind: 'both' }
];

/** Ετικέτα μιας τιμής με τη μονάδα: "Χρόνος στα 5μ. (s)", "Ταχύτητα 0 → 5μ. (m/s)". */
function swimTechLabel_(m, U) {
  var label = swimT_(m.label);
  if (/(στα|\+|-) ?(5|10|15)$/.test(m.label) || /→ \+?(5|10|15)$/.test(m.label)) label += U;
  var unit = { s: 's', d: U, v: 'm/s', n: '', r: swimT_('δελφ./s') }[m.unit];
  return unit ? label + ' (' + unit + ')' : label;
}

function swimTechFormat_(m) {
  return { s: SWIM_FMT.time, d: SWIM_FMT.vel, v: SWIM_FMT.vel, n: SWIM_FMT.int, r: SWIM_FMT.vel }[m.unit] || SWIM_FMT.vel;
}

/** Τίτλος μιας φάσης: "Εκκίνηση", "Στροφή 50μ.", "Εκκίνηση 2" (προπόνηση), στη γλώσσα του report ("Turn 50m"). */
function swimPhaseTitle_(p, race) {
  var U = race && race.poolUnit === 'yd' ? 'yd' : 'μ.';
  if (race.kind === 'training') return swimL_((p.kind === 'turn' ? 'Στροφή ' : 'Εκκίνηση ') + (p.attempt || 1));
  return swimL_(p.kind === 'turn' ? 'Στροφή ' + p.wall + U : 'Εκκίνηση');
}

/** Οι φάσεις της τεχνικής ανάλυσης με τις γωνίες τους. */
function swimTechPhases_(race) {
  var tech = race.tech || { phases: [], angles: [], snapshots: [] };
  return tech.phases.map(function (p) {
    return { id: p.id, kind: p.kind, wall: p.wall, attempt: p.attempt, values: p.values || {},
      angles: tech.angles.filter(function (a) { return a.phase === p.id; }) };
  });
}

/** Οι τιμές που έχουν δεδομένα σε τουλάχιστον μία φάση του είδους (με τη σειρά του SWIM_TECH_METRICS). */
function swimTechMetricsFor_(phases, kind) {
  return SWIM_TECH_METRICS.filter(function (m) {
    return (m.kind === kind || m.kind === 'both') && phases.some(function (p) { return swimNum_(p.values[m.key]) !== null; });
  });
}

function swimTechAngleNames_(phases) {
  var names = [];
  phases.forEach(function (p) { (p.angles || []).forEach(function (a) { if (names.indexOf(a.name) < 0) names.push(a.name); }); });
  return names;
}

/** Γωνίες που δεν ανήκουν σε φάση (π.χ. μετρήθηκαν πριν οριστεί η προσπάθεια). */
function swimTechLooseAngles_(race) {
  var tech = race.tech || { phases: [], angles: [] };
  var ids = {};
  tech.phases.forEach(function (p) { ids[p.id] = true; });
  return tech.angles.filter(function (a) { return !ids[a.phase]; });
}

/**
 * Η δομή της τεχνικής ανάλυσης για την υπογραφή του layout: φάσεις, ποιες τιμές και γωνίες εμφανίζονται, στιγμιότυπα.
 * Οι ίδιες οι τιμές και τα σχόλια γράφονται σε κάθε ενημέρωση χωρίς να ξαναφτιάχνεται το φύλλο.
 */
function swimTechSignature_(race) {
  var tech = race.tech || { phases: [], angles: [], snapshots: [], values: {} };
  if (!tech.phases.length && !tech.snapshots.length && !tech.angles.length && !(tech.values && tech.values.notes)) return '';
  var phases = swimTechPhases_(race);
  var parts = ['start', 'turn'].map(function (kind) {
    var list = phases.filter(function (p) { return p.kind === kind; });
    return [kind, list.map(function (p) { return p.id + '@' + p.wall + '#' + p.attempt; }).join(','),
      swimTechMetricsFor_(list, kind).map(function (m) { return m.key; }).join(','), swimTechAngleNames_(list).join(',')].join(':');
  });
  parts.push(swimTechLooseAngles_(race).map(function (a) { return a.id + ':' + a.name; }).join(','));
  parts.push(tech.snapshots.map(function (s) {
    return [s.id, s.phase, s.fileId, s.eliteFileId, swimSnapshotHeight_(swimSnapshotText_(s))].join(':');
  }).join(','));
  parts.push(tech.values && tech.values.notes ? 'notes' + swimSnapshotHeight_(tech.values.notes) : '');
  return swimHash_(parts.join('|'));
}

/**
 * Πίνακας τιμών: γραμμές οι τιμές (ετικέτα στις στήλες A-C), στήλες οι φάσεις από τη D (+ καλύτερη και μέσος όρος όταν
 * είναι πολλές). Πάνω από 10 φάσεις (π.χ. οι 29 στροφές των 1500μ.) χωρίζονται σε διαδοχικούς πίνακες.
 * Οι τιμές μπαίνουν στο layout.inputs.blocks (ενημερώνονται χωρίς ανακατασκευή του φύλλου).
 * Επιστρέφει { tables: [{first, last, header, cols, c0, phases}], lines, next }.
 */
function swimTechTable_(L, phases, kind, row, title, writeSection) {
  var g = L.g, race = L.race, U = L.U, W = L.LAST, V0 = SWIM_TECH_V0;
  var metrics = swimTechMetricsFor_(phases, kind);
  var angleNames = swimTechAngleNames_(phases);
  if (!metrics.length && !angleNames.length) return null;
  var many = phases.length > 1;
  var per = W - (V0 - 1) - (many ? 2 : 0);
  var lines = metrics.map(function (m) {
    return { key: m.key, label: swimTechLabel_(m, U), fmt: swimTechFormat_(m), best: m.best || '',
      get: function (p) { return swimNum_(p.values[m.key]); } };
  });
  angleNames.forEach(function (name) {
    // Το όνομα της γωνίας από το Kinovea (ελληνικά) στη γλώσσα του report, όπως στο Kinovea.
    lines.push({ key: 'angle:' + name, label: (/^γωνία/i.test(name) ? swimL_(name) : swimT_('Γωνία: ') + swimL_(name)) + ' (°)', fmt: '0.0', best: '',
      get: function (p) { var a = (p.angles || []).filter(function (x) { return x.name === name; })[0]; return a ? a.deg : null; } });
  });

  var chunks = [];
  for (var i = 0; i < phases.length; i += per) chunks.push(phases.slice(i, i + per));
  var r = row, tables = [], ranges = lines.map(function () { return []; });
  chunks.forEach(function (chunk, ci) {
    // Κάθε πίνακας ολόκληρος σε μία σελίδα, ο πρώτος μαζί με τον τίτλο της ενότητας (writeSection).
    r = swimKeep_(L, r, 'techTable');
    if (ci === 0 && writeSection) r = writeSection(r);
    g.set(r, 1, chunks.length > 1 ? title + ' (' + (ci + 1) + '/' + chunks.length + ')' : title, { bold: true, color: SWIM_COLORS.title, size: 10 });
    g.merge(r, 1, r, W);
    var hdr = [swimT_('Τιμή'), '', ''];
    chunk.forEach(function (p) { hdr.push(swimPhaseTitle_(p, race)); });
    if (many) hdr.push(swimT_('Καλύτερη'), swimT_('Μέσος όρος'));
    g.headers(r + 1, hdr);
    g.merge(r + 1, 1, r + 1, V0 - 1);
    var first = r + 2, values = [];
    var lastValueCol = swimColLetter_(V0 + chunk.length - 1);
    lines.forEach(function (line, li) {
      var rr = first + li, vr = [];
      g.set(rr, 1, line.label, { bg: SWIM_COLORS.label, bold: true, color: SWIM_COLORS.title, size: 9, align: 'left', fmt: '@', wrap: true });
      for (var c = 2; c < V0; c++) g.set(rr, c, '', { bg: SWIM_COLORS.label });
      g.merge(rr, 1, rr, V0 - 1);
      chunk.forEach(function (p, j) {
        g.set(rr, V0 + j, '', swimF_(line.fmt, 'auto'));
        var v = line.get(p);
        vr.push(v === null || v === undefined ? '' : swimV_(v));
      });
      values.push(vr);
      ranges[li].push(swimColLetter_(V0) + rr + ':' + lastValueCol + rr);
    });
    L.layout.inputs.blocks.push({ r: first, c: V0, values: values });
    tables.push({ first: first, last: first + lines.length - 1, header: r + 1, cols: chunk.length, c0: V0, width: hdr.length, phases: chunk });
    swimKeepEnd_(L, 'techTable', first + lines.length - 1);
    r = first + lines.length + 1;
  });
  if (many) {
    // Καλύτερη και μέσος όρος από όλες τις φάσεις (από όλους τους πίνακες).
    tables.forEach(function (tbl) {
      lines.forEach(function (line, li) {
        var rr = tbl.first + li, all = ranges[li].join(',');
        var bestFn = line.best === 'min' ? 'MIN' : (line.best === 'max' ? 'MAX' : '');
        g.set(rr, V0 + tbl.cols, bestFn ? '=IF(COUNT(' + all + ')=0,"",' + bestFn + '(' + all + '))' : '', swimF_(line.fmt, 'formula'));
        g.set(rr, V0 + tbl.cols + 1, '=IF(COUNT(' + all + ')=0,"",AVERAGE(' + all + '))', swimF_(line.fmt, 'formula'));
      });
    });
  }
  tables.forEach(function (tbl) { g.box(tbl.header, 1, tbl.last, tbl.width); });
  return { tables: tables, lines: lines, next: r };
}

/** Πρώτη στήλη τιμών στους πίνακες της τεχνικής ανάλυσης (οι A-C είναι η ετικέτα). */
var SWIM_TECH_V0 = 4;

/** Γωνίες χωρίς φάση: όνομα, χρόνος στο βίντεο, μοίρες. */
function swimTechLooseTable_(L, row, writeSection) {
  var g = L.g, loose = swimTechLooseAngles_(L.race), V0 = SWIM_TECH_V0;
  if (!loose.length) return row;
  row = swimKeep_(L, row, 'looseAngles');
  if (writeSection) row = writeSection(row);
  g.set(row, 1, swimT_('Γωνίες'), { bold: true, color: SWIM_COLORS.title, size: 10 });
  g.merge(row, 1, row, L.LAST);
  g.headers(row + 1, [swimT_('Γωνία'), '', '', swimT_('Χρόνος (s)'), swimT_('Μοίρες (°)')]);
  g.merge(row + 1, 1, row + 1, V0 - 1);
  var values = [];
  loose.forEach(function (a, i) {
    var r = row + 2 + i;
    g.set(r, 1, swimL_(a.name), { bg: SWIM_COLORS.label, bold: true, color: SWIM_COLORS.title, size: 9, align: 'left', fmt: '@', wrap: true });
    for (var c = 2; c < V0; c++) g.set(r, c, '', { bg: SWIM_COLORS.label });
    g.merge(r, 1, r, V0 - 1);
    g.set(r, V0, '', swimF_(SWIM_FMT.time, 'auto'));
    g.set(r, V0 + 1, '', swimF_('0.0', 'auto'));
    values.push([swimV_(a.t, 2), swimV_(a.deg, 1)]);
  });
  L.layout.inputs.blocks.push({ r: row + 2, c: V0, values: values });
  g.box(row + 1, 1, row + 1 + loose.length, V0 + 1);
  swimKeepEnd_(L, 'looseAngles', row + 1 + loose.length);
  return row + loose.length + 3;
}

/** Το κείμενο κάτω από ένα στιγμιότυπο: τα σχόλια και, αν υπάρχει, το σχόλιο του elite καρέ. */
function swimSnapshotText_(s) {
  var text = s.comment || '';
  if (s.eliteComment) text += (text ? '\n' : '') + 'Elite: ' + s.eliteComment;
  return text;
}

/** Ύψος της γραμμής των σχολίων (περίπου 140 χαρακτήρες ανά γραμμή κειμένου). */
function swimSnapshotHeight_(text) {
  return Math.max(40, Math.min(1200, swimTextHeight_(String(text || ' '), swimLayoutWidth_(15), 10, false)));
}

function swimTechSnapshotList_(race, phases) {
  var tech = race.tech || { snapshots: [] };
  var byId = {};
  phases.forEach(function (p) { byId[p.id] = p; });
  return tech.snapshots.map(function (s) {
    var o = {};
    for (var k in s) if (Object.prototype.hasOwnProperty.call(s, k)) o[k] = s[k];
    o.phaseTitle = byId[s.phase] ? swimPhaseTitle_(byId[s.phase], race) : '';
    return o;
  });
}

/**
 * Στιγμιότυπα: εικόνα του αθλητή (και το elite καρέ δίπλα), φάση, χρόνος και σχόλια. Οι εικόνες μπαίνουν πάνω από
 * τα κελιά (layout.images) και τα κείμενα στο layout.inputs.blocks (αλλάζουν χωρίς ανακατασκευή).
 */
function swimTechSnapshots_(L, snapshots, row, writeTitle) {
  var g = L.g, W = L.LAST;
  var put = function (r, c, v) { L.layout.inputs.blocks.push({ r: r, c: c, values: [[v]] }); };
  var r = row;
  snapshots.forEach(function (s, i) {
    // Κάθε στιγμιότυπο (τίτλος, εικόνα, σχόλια) σε μία σελίδα· το πρώτο μαζί με τον τίτλο της ενότητας.
    r = swimKeep_(L, r, 'snapshot', true);
    if (i === 0 && writeTitle) r = writeTitle(r);
    // Το γεγονός του αυτόματου στιγμιότυπου (π.χ. «Τελευταία χεριά») πριν από το στάδιο, όταν διαφέρουν
    // (τιμές του Kinovea στα ελληνικά, στη γλώσσα του report).
    var title = [s.phaseTitle || '', swimL_(s.event || ''), s.stage && s.stage !== s.event ? swimL_(s.stage) : '',
      s.t !== null && s.t !== undefined ? 't = ' + swimFormatTime_(s.t) + ' s' : '']
      .filter(function (x) { return x; }).join(' · ');
    g.set(r, 1, '', { bold: true, color: SWIM_COLORS.titleText, bg: SWIM_COLORS.accentDark, size: 10, fmt: '@' });
    for (var c = 2; c <= W; c++) g.set(r, c, '', { bg: SWIM_COLORS.accentDark });
    g.merge(r, 1, r, W);
    g.rowHeights[r] = 24;
    put(r, 1, title || swimT_('Στιγμιότυπο'));

    // Η εικόνα του αθλητή αριστερά και το elite καρέ δεξιά (16:9), με τις ετικέτες τους από κάτω.
    var imgRow = r + 1, n = SWIM_IMAGE_ROWS, rightCol = swimColumnAt_(SWIM_PAIR.right).col;
    if (s.fileId) L.layout.images.push({ fileId: s.fileId, row: imgRow, x: 0, width: SWIM_PAIR.width, height: SWIM_PAIR.imageHeight });
    else g.set(imgRow, 1, swimT_('(η εικόνα δεν έχει ανέβει ακόμη)'), { italic: true, color: SWIM_COLORS.dim });
    if (s.eliteFileId) {
      L.layout.images.push({ fileId: s.eliteFileId, row: imgRow, x: SWIM_PAIR.right, width: SWIM_PAIR.width, height: SWIM_PAIR.imageHeight });
      g.set(imgRow + n, 1, '', { bold: true, color: SWIM_COLORS.athlete, size: 9, fmt: '@' });
      g.merge(imgRow + n, 1, imgRow + n, rightCol - 1);
      put(imgRow + n, 1, L.race.athlete || swimT_('Αθλητής'));
      g.set(imgRow + n, rightCol, '', { bold: true, color: SWIM_COLORS.eliteText, size: 9, fmt: '@' });
      g.merge(imgRow + n, rightCol, imgRow + n, W);
      put(imgRow + n, rightCol, 'Elite: ' + (s.eliteLabel || ''));
    }

    var cr = imgRow + n + 1;
    var text = swimSnapshotText_(s);
    g.set(cr, 1, '', { wrap: true, size: 10, color: SWIM_COLORS.title, bg: SWIM_COLORS.kpi, valign: 'top', fmt: '@' });
    for (c = 2; c <= W; c++) g.set(cr, c, '', { bg: SWIM_COLORS.kpi });
    g.merge(cr, 1, cr, W);
    g.exactHeights[cr] = swimSnapshotHeight_(text);
    put(cr, 1, text || ' ');
    swimKeepEnd_(L, 'snapshot', cr);
    r = cr + 2;
  });
  return r;
}

/**
 * Οι σημειώσεις του αναλυτή για την τεχνική: ένα κελί σε όλο το πλάτος, με ύψος ανάλογο του κειμένου.
 * Το κείμενο γράφεται σε κάθε ενημέρωση (layout.inputs.blocks) χωρίς ανακατασκευή του φύλλου.
 */
function swimTechNotes_(L, cur, W) {
  var race = L.race, g = L.g;
  var notes = race.tech && race.tech.values && race.tech.values.notes ? String(race.tech.values.notes) : '';
  if (!notes) return cur;
  g.set(cur, 1, '', { wrap: true, color: SWIM_COLORS.title, bg: SWIM_COLORS.kpi, valign: 'top', fmt: '@' });
  for (var c = 2; c <= W; c++) g.set(cur, c, '', { bg: SWIM_COLORS.kpi });
  g.merge(cur, 1, cur, W);
  g.exactHeights[cur] = swimSnapshotHeight_(notes);
  L.layout.inputs.blocks.push({ r: cur, c: 1, values: [[notes]] });
  return cur + 2;
}

/** Ενότητα «Τεχνική ανάλυση» στο τέλος του report του αγώνα (μόνο αν υπάρχουν δεδομένα). */
function swimLayoutTech_(L, cur) {
  var race = L.race, g = L.g;
  var phases = swimTechPhases_(race);
  var snaps = swimTechSnapshotList_(race, phases);
  var loose = swimTechLooseAngles_(race);
  var hasNotes = !!(race.tech && race.tech.values && race.tech.values.notes);
  if (!phases.length && !snaps.length && !loose.length && !hasNotes) { L.cur = cur; return; }
  // Ο τίτλος της ενότητας και οι σημειώσεις μένουν στην ίδια σελίδα με ό,τι ακολουθεί (πρώτος πίνακας ή στιγμιότυπο).
  var pending = true;
  var section = function (r) {
    pending = false;
    g.section(r, swimT_('Τεχνική ανάλυση εκκίνησης και στροφών'), L.LAST);
    return swimTechNotes_(L, r + 2, L.LAST);
  };
  var first = function () { return pending ? section : null; };
  var starts = phases.filter(function (p) { return p.kind === 'start'; });
  var turns = phases.filter(function (p) { return p.kind === 'turn'; });
  var t1 = starts.length ? swimTechTable_(L, starts, 'start', cur, swimT_('Εκκίνηση (χρόνοι από το σήμα)'), first()) : null;
  if (t1) cur = t1.next;
  var t2 = turns.length ? swimTechTable_(L, turns, 'turn', cur, swimT_('Στροφές (χρόνοι από την επαφή στον τοίχο)'), first()) : null;
  if (t2) cur = t2.next;
  cur = swimTechLooseTable_(L, cur, first());
  if (snaps.length) {
    var head = first();
    cur = swimTechSnapshots_(L, snaps, cur, function (r) {
      if (head) r = head(r);
      g.set(r, 1, swimT_('Στιγμιότυπα και σχόλια'), { bold: true, color: SWIM_COLORS.title, size: 11 });
      g.merge(r, 1, r, L.LAST);
      return r + 1;
    });
  }
  if (pending) {
    cur = swimKeep_(L, cur, 'techNotes');
    cur = section(cur);
    swimKeepEnd_(L, 'techNotes', cur - 2);
  }
  L.cur = cur;
}

/**
 * Report τεχνικής ανάλυσης για βίντεο προπόνησης: προσπάθειες εκκίνησης ή στροφής, τιμές ανά προσπάθεια,
 * καλύτερη / μέσος όρος, γραφήματα, γωνίες και στιγμιότυπα με σχόλια.
 */
function swimBuildTechLayout_(race, analysis, extras, paging) {
  var L = { g: new SwimGrid_(), race: race, analysis: analysis, extras: extras || {}, U: swimUnitLabel_(race), LAST: 15, WIDE: 15, paging: paging };
  var layout = { signature: '', grid: L.g, charts: [], images: [], inputs: { blocks: [] }, kpi: {}, technical: true, paging: paging };
  L.layout = layout;
  var g = L.g, W = 15;
  swimLayoutHeader_(L);
  g.set(1, 1, swimT_('TECHNICAL ANALYSIS · ΕΚΚΙΝΗΣΕΙΣ & ΣΤΡΟΦΕΣ'));
  g.set(2, 1, swimT_('Κ.Ο.Ε. · Τεχνική ανάλυση με Kinovea'));

  var phases = swimTechPhases_(race);
  var starts = phases.filter(function (p) { return p.kind === 'start'; });
  var turns = phases.filter(function (p) { return p.kind === 'turn'; });
  var pick = function (list, key, fn) {
    var v = list.map(function (p) { return swimNum_(p.values[key]); }).filter(function (x) { return x !== null; });
    if (!v.length) return '';
    if (fn === 'min') return swimV_(Math.min.apply(null, v));
    if (fn === 'max') return swimV_(Math.max.apply(null, v));
    return swimV_(v.reduce(function (a, b) { return a + b; }, 0) / v.length);
  };
  var U = L.U.replace('.', '').toUpperCase();
  var tiles = [
    { cap: swimT_('ΠΡΟΣΠΑΘΕΙΕΣ'), val: phases.length, fmt: SWIM_FMT.int, sub: swimTF_('{0} εκκινήσεις · {1} στροφές', starts.length, turns.length), accent: true },
    { cap: swimT_('ΚΑΛΥΤΕΡΟ 0-15') + U, val: pick(starts, 't15', 'min'), fmt: SWIM_FMT.time, sub: swimT_('εκκίνηση (s)') },
    { cap: swimT_('ΚΑΛΥΤΕΡΟ ΤΟΙΧΟΣ→15') + U, val: pick(turns, 'out15', 'min'), fmt: SWIM_FMT.time, sub: swimT_('στροφή (s)') },
    { cap: swimT_('ΧΡΟΝΟΣ ΣΤΡΟΦΗΣ'), val: pick(turns, 'turnTime', 'min'), fmt: SWIM_FMT.time, sub: swimT_('καλύτερος (s)') },
    { cap: swimT_('ΔΕΛΦΙΝΙΣΜΟΙ'), val: pick(phases, 'kicks', 'avg'), fmt: '0.0', sub: swimT_('μέσος όρος') },
    { cap: swimT_('ΣΥΧΝ. ΔΕΛΦΙΝΙΣΜΩΝ'), val: pick(phases, 'kickRate', 'avg'), fmt: SWIM_FMT.vel, sub: swimT_('δελφ./s (μ.ο.)') },
    { cap: swimT_('ΕΞΟΔΟΣ'), val: pick(phases, 'breakoutDist', 'avg'), fmt: SWIM_FMT.vel, sub: swimTF_('{0} από τον τοίχο (μ.ο.)', L.U) }
  ];
  var tileValues = [];
  tiles.forEach(function (t, i) {
    var c = 1 + 2 * i, bg = t.accent ? SWIM_COLORS.kpiDak : SWIM_COLORS.kpi;
    g.set(8, c, t.cap, { bold: true, size: 8, color: SWIM_COLORS.dim, bg: bg, align: 'center', wrap: true }); g.set(8, c + 1, '', { bg: bg }); g.merge(8, c, 8, c + 1);
    g.set(9, c, '', { bold: true, size: 20, color: t.accent ? SWIM_COLORS.dakText : SWIM_COLORS.title, bg: bg, align: 'center', fmt: t.fmt });
    g.set(9, c + 1, '', { bg: bg }); g.merge(9, c, 9, c + 1);
    g.set(10, c, t.sub, { size: 8, color: SWIM_COLORS.dim, bg: bg, align: 'center', wrap: true }); g.set(10, c + 1, '', { bg: bg }); g.merge(10, c, 10, c + 1);
    tileValues.push(t.val === null || t.val === undefined ? '' : t.val, '');
  });
  layout.inputs.blocks.push({ r: 9, c: 1, values: [tileValues.slice(0, 14)] });
  g.rowHeights[8] = 18; g.rowHeights[9] = 34; g.rowHeights[10] = 28;

  var cur = swimTechNotes_(L, 12, W);
  var t1 = null, t2 = null;
  var sectionWriter = function (title) { return function (r) { g.section(r, title, W); return r + 1; }; };
  if (starts.length) {
    t1 = swimTechTable_(L, starts, 'start', cur, swimT_('Τιμές ανά προσπάθεια (χρόνοι από το σήμα)'), sectionWriter(swimT_('Εκκινήσεις')));
    if (t1) cur = t1.next;
  }
  if (turns.length) {
    t2 = swimTechTable_(L, turns, 'turn', cur, swimT_('Τιμές ανά προσπάθεια (χρόνοι από την επαφή στον τοίχο)'), sectionWriter(swimT_('Στροφές')));
    if (t2) cur = t2.next;
  }
  cur = swimTechLooseTable_(L, cur);

  // Γραφήματα: μικροί βοηθητικοί πίνακες (γραμμές οι προσπάθειες, τύποι προς τους πίνακες τιμών), τρεις ανά σειρά·
  // κάθε σειρά σε μία σελίδα, η πρώτη μαζί με τον τίτλο.
  var slots = [1, 6, 11], slot = 0, stripTop = cur + 1, stripBottom = cur;
  var helpersStarted = false;
  var helper = function (t, keys, names, title) {
    if (!t) return null;
    var cols = [];
    keys.forEach(function (k, i) {
      var li = t.lines.map(function (x) { return x.key; }).indexOf(k);
      if (li >= 0) cols.push({ li: li, name: names[i], fmt: t.lines[li].fmt });
    });
    if (!cols.length) return null;
    if (!helpersStarted) {
      cur = swimKeep_(L, cur, 'chartData');
      g.set(cur, 1, swimT_('Δεδομένα των γραφημάτων (από τους πίνακες πιο πάνω)'), { italic: true, color: SWIM_COLORS.dim, size: 9 });
      g.merge(cur, 1, cur, W);
      stripTop = cur + 1;
      stripBottom = cur;
      helpersStarted = true;
    } else if (slot === slots.length) {
      swimKeepEnd_(L, 'chartData', stripBottom);
      slot = 0;
      stripTop = swimKeep_(L, stripBottom + 2, 'chartData');
    }
    var c0 = slots[slot++], top = stripTop;
    g.set(top, c0, title, { bold: true, color: SWIM_COLORS.title, size: 9 });
    g.merge(top, c0, top, c0 + 3);
    g.headers(top + 1, [swimT_('Προσπάθεια')].concat(cols.map(function (x) { return x.name; })), c0);
    var rr = top + 2;
    t.tables.forEach(function (tbl) {
      for (var j = 0; j < tbl.cols; j++) {
        var src = swimColLetter_(tbl.c0 + j);
        g.set(rr, c0, swimPhaseTitle_(tbl.phases[j], race), swimF_('@', 'label'));
        cols.forEach(function (x, i) {
          var a = src + (tbl.first + x.li);
          g.set(rr, c0 + 1 + i, '=IF(' + a + '="","",' + a + ')', swimF_(x.fmt, 'formula'));
        });
        rr++;
      }
    });
    var h = { first: top + 2, last: rr - 1, ncols: cols.length, c0: c0 };
    g.box(top + 1, c0, h.last, c0 + cols.length);
    stripBottom = Math.max(stripBottom, h.last);
    return h;
  };
  var specs = [];
  var h1 = helper(t1, ['t5', 't10', 't15'], ['5' + L.U, '10' + L.U, '15' + L.U], swimT_('Εκκινήσεις: χρόνοι από το σήμα'));
  if (h1) specs.push({ h: h1, title: swimTF_('Εκκινήσεις: χρόνοι στα 5 / 10 / 15{0} (s)', L.U) });
  var h2 = helper(t2, ['out5', 'out10', 'out15'], ['+5' + L.U, '+10' + L.U, '+15' + L.U], swimT_('Στροφές: χρόνοι από την επαφή'));
  if (h2) specs.push({ h: h2, title: swimTF_('Στροφές: τοίχος → +5 / +10 / +15{0} (s)', L.U) });
  var h3 = helper(t1, ['kicks', 'breakoutDist'], [swimT_('Δελφινισμοί'), swimTF_('Έξοδος ({0})', L.U)], swimT_('Εκκινήσεις: υποβρύχια φάση'));
  if (h3) specs.push({ h: h3, title: swimT_('Εκκινήσεις: δελφινισμοί και απόσταση εξόδου'), combo: true });
  var h4 = helper(t2, ['kicks', 'breakoutDist'], [swimT_('Δελφινισμοί'), swimTF_('Έξοδος ({0})', L.U)], swimT_('Στροφές: υποβρύχια φάση'));
  if (h4) specs.push({ h: h4, title: swimT_('Στροφές: δελφινισμοί και απόσταση εξόδου'), combo: true });
  var h5 = helper(t2, ['turnTime', 'contact', 'rotation'], [swimT_('Χρόνος στροφής'), swimT_('Επαφή'), swimT_('Περιστροφή')], swimT_('Στροφές: φάσεις της στροφής'));
  if (h5) specs.push({ h: h5, title: swimT_('Στροφές: χρόνος στροφής, επαφή και περιστροφή (s)') });
  if (helpersStarted) {
    swimKeepEnd_(L, 'chartData', stripBottom);
    cur = stripBottom + 2;
  }
  if (specs.length) {
    cur = swimPlaceCharts_(L, specs.map(function (sp) {
      var h = sp.h, lab = swimColLetter_(h.c0), v0 = swimColLetter_(h.c0 + 1), v1 = swimColLetter_(h.c0 + h.ncols);
      var spec = { type: sp.combo && h.ncols > 1 ? 'combo' : 'column', title: sp.title,
        ranges: [lab + h.first + ':' + lab + h.last, v0 + h.first + ':' + v1 + h.last],
        colors: [SWIM_COLORS.athlete, SWIM_COLORS.sr, SWIM_COLORS.dak], vTitle: sp.combo ? swimT_('δελφινισμοί') : 's' };
      if (spec.type === 'combo') { spec.seriesTypes = ['bars', 'line']; spec.secondAxis = [1]; spec.v2Title = L.U; }
      return spec;
    }), cur, W);
  }

  var snaps = swimTechSnapshotList_(race, phases);
  if (snaps.length) cur = swimTechSnapshots_(L, snaps, cur, sectionWriter(swimT_('Στιγμιότυπα και σχόλια')));
  layout.lastRow = Math.max(cur + 2, g.maxRow + 1);
  layout.lastCol = W;
  layout.T0 = null;
  layout.kpi.finalTime = null;
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
  var styleName = swimStyleName_(race.style) || race.style;
  var unit = swimUnitLabel_(race);
  var dist = race.relay ? '4x' + (race.distance / 4) : String(race.distance);
  // Η φάση (π.χ. «Τελικός») και η κατηγορία («Γυναικών») έρχονται στα ελληνικά: στη γλώσσα του report (swimL_).
  var eventLine = race.kind === 'training'
    ? [swimT_('Τεχνική ανάλυση (προπόνηση)'), styleName, swimT_('Πισίνα ') + swimPoolLabel_(race, true)].join(' · ')
    : [dist + unit + ' ' + styleName + (race.relay ? swimT_(' (σκυταλοδρομία)') : '') + (race.relayLeg ? swimT_(' (σκέλος σκυταλοδρομίας)') : ''),
      swimT_('Πισίνα ') + swimPoolLabel_(race, true), swimL_(race.round), race.dq ? swimT_('Ακύρωση (DQ)') : '']
      .filter(function (x) { return x; }).join(' · ');
  return {
    athlete: (race.athlete || '') + (race.lane ? swimTF_('  (Διάδρομος {0})', race.lane) : ''),
    eventLine: eventLine,
    clubLine: [race.club, race.athleteId ? swimT_('Μητρώο ') + race.athleteId : '', swimL_(race.category), race.birthYear]
      .filter(function (x) { return x; }).join(' · '),
    compLine: [race.competition, race.location, race.date].filter(function (x) { return x; }).join(' · ')
  };
}

function swimInputPlan_(race, analysis, layout) {
  // blocks: τιμές της τεχνικής ανάλυσης και κείμενα, γράφονται πάντα (και κενές) σε ορθογώνια τμήματα.
  var plan = { columns: [], patches: [], blocks: (layout.inputs.blocks || []).slice() };
  var rows = analysis.rows;

  var hv = swimHeaderValues_(race);
  (layout.inputs.header || []).forEach(function (h) {
    var v = hv[h.key];
    if (v !== null && v !== undefined && String(v) !== '') plan.patches.push({ r: h.r, c: h.c, v: v });
  });
  if (layout.technical) return plan;

  plan.columns.push({ col: layout.inputs.times.col, row: layout.inputs.times.row, values: rows.map(function (r) { return swimValueOrBlank_(r.t, 3); }) });
  if (race.strokes.length) {
    plan.columns.push({ col: layout.inputs.strokes.col, row: layout.inputs.strokes.row, values: rows.map(function (r) { return swimValueOrBlank_(r.strokes, 2); }) });
    plan.columns.push({ col: layout.inputs.srExact.col, row: layout.inputs.srExact.row, values: rows.map(function (r) { return swimValueOrBlank_(r.srExact, 1); }) });
  } else if (race.segmentStrokes) {
    // Αναλύσεις της βιβλιοθήκης: οι χεριές ανά τμήμα όπως γράφτηκαν στο Excel.
    plan.columns.push({ col: layout.inputs.strokes.col, row: layout.inputs.strokes.row, values: rows.map(function (r) { return swimValueOrBlank_(r.strokes, 2); }) });
  }

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
  try { sheet.getImages().forEach(function (im) { im.remove(); }); } catch (e) { /* παλιότερο Apps Script */ }
  sheet.clear();
  sheet.getRange(1, 1, sheet.getMaxRows(), sheet.getMaxColumns()).breakApart();

  var nr = Math.max(g.maxRow, layout.lastRow);
  var nc = Math.max(g.maxCol, layout.lastCol);
  if (sheet.getMaxRows() < nr) sheet.insertRowsAfter(sheet.getMaxRows(), nr - sheet.getMaxRows());
  if (sheet.getMaxColumns() < nc) sheet.insertColumnsAfter(sheet.getMaxColumns(), nc - sheet.getMaxColumns());

  var values = [], byFormat = {}, bgs = [], weights = [], styles = [], colors = [], sizes = [], aligns = [], wraps = [], valigns = [];
  for (var r = 1; r <= g.maxRow; r++) {
    var vr = [], br = [], wr = [], sr = [], cr = [], zr = [], ar = [], wrp = [], vl = [];
    for (var c = 1; c <= nc; c++) {
      var cell = g.get(r, c) || {};
      vr.push(cell.v === undefined || cell.v === null ? '' : cell.v);
      if (cell.fmt) (byFormat[cell.fmt] = byFormat[cell.fmt] || []).push(swimA1_(r, c));
      br.push(cell.bg || '#ffffff');
      wr.push(cell.bold ? 'bold' : 'normal');
      sr.push(cell.italic ? 'italic' : 'normal');
      cr.push(cell.color || SWIM_COLORS.text);
      zr.push(swimFontSize_(cell.size));
      ar.push(cell.align || 'left');
      wrp.push(!!cell.wrap);
      vl.push(cell.valign || 'middle');
    }
    values.push(vr); bgs.push(br); weights.push(wr); styles.push(sr); colors.push(cr); sizes.push(zr); aligns.push(ar); wraps.push(wrp); valigns.push(vl);
  }

  // Μορφές αριθμών πριν από τις τιμές (π.χ. "15-25" ως κείμενο, όχι ημερομηνία).
  Object.keys(byFormat).forEach(function (fmt) {
    sheet.getRangeList(byFormat[fmt]).setNumberFormat(fmt);
  });
  var range = sheet.getRange(1, 1, g.maxRow, nc);
  range.setFontFamily(SWIM_FONT);
  range.setValues(values);
  range.setBackgrounds(bgs);
  range.setFontWeights(weights);
  range.setFontStyles(styles);
  range.setFontColors(colors);
  range.setFontSizes(sizes);
  range.setHorizontalAlignments(aligns);
  range.setWraps(wraps);
  range.setVerticalAlignments(valigns);

  g.merges.forEach(function (m) {
    sheet.getRange(m[0], m[1], m[2] - m[0] + 1, m[3] - m[1] + 1).merge();
  });
  g.boxes.forEach(function (b) {
    sheet.getRange(b[0], b[1], b[2] - b[0] + 1, b[3] - b[1] + 1)
      .setBorder(true, true, true, true, true, true, SWIM_COLORS.border, SpreadsheetApp.BorderStyle.SOLID);
  });
  // Σταθερό ύψος σε κάθε γραμμή, ακριβώς όπως στο layout: οι σελίδες του PDF πέφτουν εκεί που υπολογίστηκαν.
  var from = 1, height = swimRowHeight_(g, 1);
  for (var hr = 2; hr <= nr + 1; hr++) {
    var h = hr <= nr ? swimRowHeight_(g, hr) : -1;
    if (h !== height) {
      swimForceRowHeights_(sheet, from, hr - from, height);
      from = hr;
      height = h;
    }
  }

  for (var cc = 1; cc <= Math.max(15, nc); cc++) sheet.setColumnWidth(cc, swimColumnWidth_(cc));
  sheet.setHiddenGridlines(true);
  sheet.setFrozenRows(0);

  // Τα γραφήματα μπαίνουν μετά τις τιμές (swimPlaceChartItems_): εικόνες του Kinovea ή γραφήματα του Google.
  (layout.images || []).forEach(function (img) {
    var pos = img.x !== undefined ? swimColumnAt_(img.x) : { col: img.col, offset: 4 };
    try {
      var blob = DriveApp.getFileById(img.fileId).getBlob();
      var im = sheet.insertImage(blob, pos.col, img.row, pos.offset, img.offsetY === undefined ? 4 : img.offsetY);
      var w = img.width, h = img.height;
      if (img.fit) {
        // Ολόκληρη η εικόνα μέσα στο πλαίσιο, με τις αναλογίες της, στο κέντρο του.
        var w0 = swimImageSize_(im, 'Width'), h0 = swimImageSize_(im, 'Height');
        if (w0 > 0 && h0 > 0) {
          var s = Math.min(img.width / w0, img.height / h0);
          w = Math.max(1, Math.round(w0 * s));
          h = Math.max(1, Math.round(h0 * s));
          var centered = swimColumnAt_((img.x || 0) + Math.round((img.width - w) / 2));
          im.setAnchorCell(sheet.getRange(img.row, centered.col)).setAnchorCellXOffset(centered.offset);
        }
      }
      im.setWidth(w).setHeight(h);
    } catch (e) {
      sheet.getRange(img.row, pos.col).setValue(swimT_('(δεν βρέθηκε η εικόνα στο Drive)'));
    }
  });
}

// ---------------------------------------------------------------------------------------------
// Γραφήματα: εικόνες του Kinovea (με τα γραφικά του βίντεο) ή, όσο δεν υπάρχουν, γραφήματα του Google
//
// Το PDF του Google δεν σχεδιάζει τα περισσότερα γραφήματα του φύλλου (μόνο τον τίτλο τους). Γι' αυτό τα σχεδιάζει το
// Kinovea: το Apps Script του δίνει τις τιμές κάθε γραφήματος όπως είναι στο φύλλο (και με όσα διορθώθηκαν με το χέρι)
// μαζί με ένα hash τους· το Kinovea ανεβάζει τις εικόνες και τις στέλνει με το hash. Μια εικόνα μπαίνει στη θέση του
// γραφήματος μόνο όσο το hash είναι των τρεχουσών τιμών· αλλιώς μπαίνει το γράφημα του Google (σωστές τιμές πάντα).
// ---------------------------------------------------------------------------------------------

/** Αλλάζει όταν αλλάζει το τι στέλνεται για κάθε γράφημα: όλες οι εικόνες ξανασχεδιάζονται. */
var SWIM_CHART_DATA_VERSION = 1;

/** "G20:I25" → { r1: 20, c1: 7, r2: 25, c2: 9 }. */
function swimA1Box_(a1) {
  var m = /^([A-Z]+)(\d+)(?::([A-Z]+)(\d+))?$/.exec(String(a1 || ''));
  if (!m) return null;
  var col = function (s) { var n = 0; for (var i = 0; i < s.length; i++) n = n * 26 + s.charCodeAt(i) - 64; return n; };
  var r1 = Number(m[2]), c1 = col(m[1]);
  return { r1: r1, c1: c1, r2: m[3] ? Number(m[4]) : r1, c2: m[3] ? col(m[3]) : c1 };
}

/**
 * Οι τιμές κάθε γραφήματος από το φύλλο (μία ανάγνωση για όλα): { key, type, title, vTitle, v2Title, hTitle, stacked, width, colors,
 * height, categories: [κείμενο], series: [{ name, color, type ('line'|'bars'), axis (0|1), values: [αριθμός|null],
 * texts: [κείμενο όπως στο φύλλο] }], labelSeries: [δείκτες σειρών με τιμές πάνω στο γράφημα], hash }.
 */
function swimChartData_(sheet, specs) {
  if (!specs || !specs.length) return [];
  var boxes = specs.map(function (spec) { return (spec.ranges || []).map(swimA1Box_); });
  var maxR = 1, maxC = 1;
  boxes.forEach(function (list) {
    list.forEach(function (b) { if (b) { maxR = Math.max(maxR, b.r2); maxC = Math.max(maxC, b.c2); } });
  });
  var block = sheet.getRange(1, 1, maxR, maxC);
  var values = block.getValues(), texts = block.getDisplayValues();
  var at = function (grid, r, c) { var row = grid[r - 1]; return row ? row[c - 1] : ''; };

  return specs.map(function (spec, i) {
    var list = boxes[i], dom = list[0];
    var out = {
      key: spec.key, type: spec.type, title: spec.title || '', vTitle: spec.vTitle || '', v2Title: spec.v2Title || '', hTitle: spec.hTitle || '',
      stacked: !!spec.stacked, width: spec.width || SWIM_PAIR.width, height: spec.height || SWIM_CHART_H,
      // Τα χρώματα όλα (στις πίτες ένα ανά κομμάτι), όπως του βίντεο.
      colors: (spec.colors || []).map(swimChartColor_), categories: [], series: [], labelSeries: []
    };
    if (!dom) return swimChartHashed_(out);
    var n = dom.r2 - dom.r1 + 1;
    for (var r = 0; r < n; r++) out.categories.push(String(at(texts, dom.r1 + r, dom.c1)).trim());
    var k = 0;
    for (var j = 1; j < list.length; j++) {
      var b = list[j];
      if (!b) continue;
      for (var c = b.c1; c <= b.c2; c++, k++) {
        var vals = [], labels = [];
        for (var x = 0; x < n; x++) {
          var v = at(values, b.r1 + x, c);
          var num = typeof v === 'number' && isFinite(v) ? v : null;
          vals.push(num);
          labels.push(num === null ? '' : String(at(texts, b.r1 + x, c)).trim());
        }
        var head = String(at(texts, b.r1 - 1, c) || '').trim();
        out.series.push({
          name: (spec.labels || [])[k] || (head && isNaN(Number(head)) ? head : ''),
          color: swimChartColor_((spec.colors || [])[k] || SWIM_COLORS.athlete),
          type: spec.type === 'line' || spec.type === 'radar' ? 'line' : (spec.type === 'combo' ? ((spec.seriesTypes || [])[k] || 'bars') : 'bars'),
          axis: (spec.secondAxis || []).indexOf(k) >= 0 ? 1 : 0,
          values: vals,
          texts: labels
        });
      }
    }
    if (!spec.noLabels && n <= 16) out.labelSeries = swimChartLabelSeries_(spec);
    return swimChartHashed_(out);
  });
}

function swimChartHashed_(data) {
  data.hash = swimHash_(SWIM_CHART_DATA_VERSION + '|' + JSON.stringify(data));
  return data;
}

/**
 * Τα γραφήματα στις θέσεις τους (μετά τις τιμές): η εικόνα του Kinovea όταν είναι σχεδιασμένη από τις τρέχουσες τιμές, αλλιώς
 * το γράφημα του Google. Αλλάζουν μόνο όταν άλλαξε κάτι (stored: η προηγούμενη θέση, από το ευρετήριο). Επιστρέφει
 * { data: οι τιμές όλων, missing: τα κλειδιά χωρίς εικόνα, placement: η νέα υπογραφή }.
 */
function swimPlaceChartItems_(sheet, layout, race, fresh, stored) {
  var specs = layout.charts || [];
  var data = swimChartData_(sheet, specs);
  var images = race.chartImages || {};
  var items = specs.map(function (spec, i) {
    var im = images[spec.key];
    var it = { spec: spec, fileId: im && data[i] && im.hash === data[i].hash ? im.fileId : '' };
    if (SWIM_PDF_CHARTS) {
      // 2.9.5: ένα γράφημα χωρίς καμία τιμή (π.χ. χωρίς χεριές) δεν μπαίνει ούτε ζητιέται από το Kinovea: στο PDF κενός χώρος,
      // όχι ένα κουτί μόνο με τον τίτλο. Μια εικόνα του Kinovea για άλλες τιμές (άλλαξαν μετά το σχέδιό της): stale.
      it.none = !swimChartHasValues_(data[i]);
      it.stale = !it.fileId && !it.none && !!im;
    }
    return it;
  });
  var signature = function () {
    return swimHash_(JSON.stringify(items.map(function (it) {
      return [it.spec.key, it.spec.row, it.spec.col, it.spec.offsetX || 0, it.spec.width, it.spec.height, it.none ? 'none' : it.fileId];
    })));
  };
  var placement = signature();
  if (fresh || placement !== stored) {
    if (!fresh) swimRemoveChartItems_(sheet, specs);
    items.forEach(function (it) {
      if (it.none) return;
      if (it.fileId && swimInsertChartImage_(sheet, it.spec, it.fileId)) return;
      it.fileId = '';
      swimInsertEmbeddedChart_(sheet, it.spec);
    });
    placement = signature();
  }
  var keys = function (f) { return items.filter(f).map(function (it) { return it.spec.key; }); };
  return {
    data: data,
    missing: keys(function (it) { return !it.fileId && !it.none; }),
    stale: keys(function (it) { return it.stale && !it.fileId; }),
    placement: placement
  };
}

/** (2.9.5) Έχει το γράφημα έστω και μία τιμή (αριθμό) σε κάποια σειρά του; */
function swimChartHasValues_(data) {
  return !!data && (data.series || []).some(function (s) {
    return (s.values || []).some(function (v) { return typeof v === 'number' && isFinite(v); });
  });
}

/** Πόσο κρατά η σημείωση ότι τα γραφήματα ενός αγώνα ζητήθηκαν ξανά (s): μέσα σε αυτό τον χρόνο όχι δεύτερη φορά. */
var SWIM_CHARTS_AGAIN_TIME = 600;

/**
 * (2.9.5) Μπορεί το PDF να ζητήσει ξανά από το Kinovea τα γραφήματα με εικόνα για παλιότερες τιμές; Μία φορά ανά αγώνα μέσα σε
 * SWIM_CHARTS_AGAIN_TIME (στην προσωρινή μνήμη του script), ώστε να μη γίνει ποτέ κύκλος χωρίς τέλος· χωρίς τη μνήμη, όχι.
 */
function swimChartsAgain_(raceId) {
  if (!SWIM_PDF_CHARTS) return false;
  var cache = null;
  try { cache = CacheService.getScriptCache(); } catch (e) { cache = null; }
  if (!cache) return false;
  try {
    var key = 'swimChartsAgain|' + swimHash_(String(raceId));
    if (cache.get(key)) return false;
    cache.put(key, '1', SWIM_CHARTS_AGAIN_TIME);
    return true;
  } catch (e2) {
    return false;
  }
}

/** Το γράφημα του Google στη θέση του (ένα γράφημα που δεν υποστηρίζεται, π.χ. radar, δοκιμάζεται ως γραμμή). */
function swimInsertEmbeddedChart_(sheet, spec) {
  try {
    swimInsertChart_(sheet, spec);
  } catch (e) {
    if (spec.type === 'radar') {
      try { var alt = JSON.parse(JSON.stringify(spec)); alt.type = 'line'; swimInsertChart_(sheet, alt); } catch (e2) { /* παράλειψη */ }
    }
  }
}

/** Η εικόνα του Kinovea στη θέση και στο μέγεθος του γραφήματος. false αν δεν μπήκε (π.χ. σβήστηκε από το Drive). */
function swimInsertChartImage_(sheet, spec, fileId) {
  var im = null;
  try {
    var blob = DriveApp.getFileById(fileId).getBlob();
    im = sheet.insertImage(blob, spec.col, spec.row, spec.offsetX || 0, spec.offsetY || 0);
    im.setWidth(spec.width).setHeight(spec.height);
  } catch (e) {
    try { if (im) im.remove(); } catch (e2) { /* τίποτα */ }
    return false;
  }
  try { im.setAltTextTitle('swim-chart:' + spec.key); } catch (e3) { /* παλιότερο Apps Script */ }
  return true;
}

/** Βγάζει από το φύλλο τα γραφήματα και τις εικόνες τους (με τον τίτλο swim-chart ή στη θέση ενός γραφήματος). */
function swimRemoveChartItems_(sheet, specs) {
  sheet.getCharts().forEach(function (ch) { try { sheet.removeChart(ch); } catch (e) { /* τίποτα */ } });
  var at = {};
  specs.forEach(function (s) { at[s.row + ':' + s.col] = true; });
  var images = [];
  try { images = sheet.getImages(); } catch (e) { return; }
  images.forEach(function (im) {
    var title = '', where = '';
    try { title = String(im.getAltTextTitle() || ''); } catch (e) { /* χωρίς τίτλο */ }
    try { var a = im.getAnchorCell(); where = a.getRow() + ':' + a.getColumn(); } catch (e2) { /* χωρίς θέση */ }
    if (title.indexOf('swim-chart:') === 0 || at[where]) {
      try { im.remove(); } catch (e3) { /* τίποτα */ }
    }
  });
}

/** Το φυσικό πλάτος / ύψος μιας εικόνας πάνω από τα κελιά (px). */
function swimImageSize_(im, what) {
  try { return Number(im['getInherent' + what]()); } catch (e) { /* παλιότερο Apps Script */ }
  try { return Number(im['get' + what]()); } catch (e2) { return 0; }
}

/** Σταθερό ύψος γραμμών, ακόμη κι αν το κείμενο θα ήθελε περισσότερο (όπως το layout υπολόγισε τις σελίδες). */
function swimForceRowHeights_(sheet, row, n, height) {
  if (n <= 0) return;
  if (typeof sheet.setRowHeightsForced === 'function') sheet.setRowHeightsForced(row, n, height);
  else sheet.setRowHeights(row, n, height);
}

/**
 * Οι ρυθμίσεις ενός γραφήματος. Η περιοχή του γραφήματος (chartArea) σε px: με ποσοστά (π.χ. '74%') η οθόνη το
 * δείχνει σωστά, αλλά το PDF του Google σχεδίαζε μόνο τον τίτλο (χωρίς άξονες, γραμμές και υπόμνημα).
 */
function swimChartOptions_(spec) {
  var text = { fontName: SWIM_FONT, fontSize: 12, color: SWIM_COLORS.dim };
  var o = {
    title: spec.title,
    titleTextStyle: { fontName: SWIM_FONT, fontSize: 14, bold: true, color: SWIM_COLORS.title },
    width: spec.width,
    height: spec.height,
    fontName: SWIM_FONT,
    backgroundColor: '#FFFFFF',
    legend: { position: spec.type === 'pie' ? 'right' : 'bottom', textStyle: text },
    useFirstColumnAsDomain: true
  };
  var twoAxes = !!(spec.secondAxis && spec.secondAxis.length);
  if (spec.type !== 'pie') {
    var left = 70, right = twoAxes ? 70 : 24, top = 48, bottom = 72;
    o.chartArea = { left: left, top: top, width: Math.max(120, spec.width - left - right), height: Math.max(90, spec.height - top - bottom) };
  }
  if (spec.colors) o.colors = spec.colors.map(swimChartColor_);
  if (spec.type === 'line') { o.pointSize = 6; o.lineWidth = 3; o.curveType = 'none'; o.interpolateNulls = true; }
  if (spec.type === 'combo') o.seriesType = 'bars';
  if (spec.stacked) o.isStacked = true;
  if (spec.type === 'pie') { o.pieHole = 0.45; o.pieSliceTextStyle = { fontName: SWIM_FONT, fontSize: 12, color: '#FFFFFF' }; }

  var series = {};
  var get = function (i) { series[i] = series[i] || {}; return series[i]; };
  (spec.labels || []).forEach(function (l, i) { get(i).labelInLegend = l; });
  (spec.seriesTypes || []).forEach(function (t, i) { get(i).type = t; });
  (spec.secondAxis || []).forEach(function (i) { get(i).targetAxisIndex = 1; });
  // Οι τιμές πάνω στη σειρά του αθλητή (όταν τα σημεία είναι λίγα), ώστε το γράφημα να διαβάζεται και τυπωμένο (2.5). Όχι από την
  // 2.9.5 (SWIM_PDF_CHARTS, εκτός αν τις ζητά το spec: ο έλεγχος από το μενού): με αυτές το Google σχεδίαζε στο PDF και στην εικόνα
  // του γραφήματος (getAs) μόνο τον τίτλο· στα PDF 2.5–2.9.4 σωζόταν μόνο η «Σύνθεση αγώνα», η πίτα χωρίς τιμές πάνω της (όπως με
  // την περιοχή σε % ως την 2.2.1). Οι εικόνες του Kinovea έχουν τις τιμές τους (labelSeries).
  if (!spec.noLabels && (!SWIM_PDF_CHARTS || spec.dataLabels)) swimChartLabelSeries_(spec).forEach(function (i) { get(i).dataLabel = 'value'; });
  if (Object.keys(series).length) o.series = series;

  if (spec.type !== 'pie') {
    var axis = function (title) { return { title: title || '', textStyle: text, titleTextStyle: { fontName: SWIM_FONT, fontSize: 12, italic: false, color: SWIM_COLORS.dim }, gridlines: { color: '#E6ECF2' } }; };
    o.vAxes = { 0: axis(spec.vTitle) };
    if (twoAxes) o.vAxes[1] = axis(spec.v2Title);
    o.hAxis = { title: spec.hTitle || '', textStyle: text, titleTextStyle: { fontName: SWIM_FONT, fontSize: 12, italic: false, color: SWIM_COLORS.dim } };
  }
  return o;
}

/**
 * Οι σειρές με τις τιμές τους πάνω στο γράφημα: του αθλητή («Αγώνας»/«Αθλητής», αλλιώς η πρώτη), σε γραμμές και στήλες
 * με έως 16 σημεία. Όχι στις πίτες και στο radar.
 */
function swimChartLabelSeries_(spec) {
  if (spec.type !== 'line' && spec.type !== 'column' && spec.type !== 'combo') return [];
  var m = /^[A-Z]+(\d+):[A-Z]+(\d+)$/.exec(String((spec.ranges || [])[0] || ''));
  var points = m ? Number(m[2]) - Number(m[1]) + 1 : 0;
  if (!points || points > 16) return [];
  var labels = spec.labels || [];
  var i = labels.indexOf(swimT_('Αγώνας'));
  if (i < 0) i = labels.indexOf(swimT_('Αθλητής'));
  return [i >= 0 ? i : 0];
}

function swimChartBuilder_(sheet, type) {
  var builder = sheet.newChart();
  if (type === 'line') return builder.asLineChart();
  if (type === 'column') return builder.asColumnChart();
  if (type === 'combo') return builder.asComboChart();
  if (type === 'pie') return builder.asPieChart();
  if (type === 'radar') return builder.setChartType(Charts.ChartType.RADAR);
  return builder;
}

function swimInsertChart_(sheet, spec, options) {
  var make = function (opts) {
    var builder = swimChartBuilder_(sheet, spec.type);
    spec.ranges.forEach(function (a1) { builder.addRange(sheet.getRange(a1)); });
    if (spec.transpose) builder.setTransposeRowsAndColumns(true);
    // 2.9.5: και οι τιμές σε κρυμμένες γραμμές ή στήλες (αν κρυφτεί μέρος του φύλλου, το γράφημα δεν αδειάζει).
    if (SWIM_PDF_CHARTS) {
      try { builder.setHiddenDimensionStrategy(Charts.ChartHiddenDimensionStrategy.SHOW_BOTH); } catch (e0) { /* παλιότερο Apps Script */ }
    }
    Object.keys(opts).forEach(function (k) { builder.setOption(k, opts[k]); });
    builder.setPosition(spec.row, spec.col, spec.offsetX || 0, spec.offsetY || 0);
    return builder.build();
  };
  var o = options || swimChartOptions_(spec);
  var chart;
  try {
    chart = make(o);
  } catch (e) {
    // Χωρίς τις τιμές πάνω στις σειρές, αν αυτό το γράφημα δεν τις δέχεται.
    if (options || spec.noLabels) throw e;
    var plain = JSON.parse(JSON.stringify(spec));
    plain.noLabels = true;
    chart = make(swimChartOptions_(plain));
  }
  sheet.insertChart(chart);
}

function swimWriteInputs_(sheet, race, analysis, layout) {
  var plan = swimInputPlan_(race, analysis, layout);
  plan.columns.forEach(function (col) {
    if (!col.values.length) return;
    sheet.getRange(col.row, col.col, col.values.length, 1).setValues(col.values.map(function (v) { return [v]; }));
  });
  plan.blocks.forEach(function (b) {
    if (!b.values.length || !b.values[0].length) return;
    sheet.getRange(b.r, b.c, b.values.length, b.values[0].length).setValues(b.values);
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
    .setValue(swimTF_('Ενημέρωση {0} · έκδοση {1}', Utilities.formatDate(new Date(), Session.getScriptTimeZone(), 'dd/MM/yyyy HH:mm:ss'), SWIM_VERSION));
}

// ---------------------------------------------------------------------------------------------
// Elite αναφορές: χρόνοι σύγκρισης για το Δ στα περάσματα (στο spreadsheet των αναφορών)
//
// Μορφή: μία γραμμή ανά σημείο ελέγχου. Το όνομα (και τα στοιχεία του αγωνίσματος) γράφονται μόνο
// στην πρώτη γραμμή κάθε αναφοράς· οι επόμενες γραμμές με κενό όνομα ανήκουν στην ίδια αναφορά.
// Έτσι επικολλάς κατευθείαν τις στήλες Distance / Time από το Excel ανάλυσης.
// ---------------------------------------------------------------------------------------------

var SWIM_REFERENCES_SHEET = 'Elite αναφορές';
var SWIM_REF_HEADERS = ['Αναφορά', 'Φύλο', 'Στυλ', 'Απόσταση', 'Πισίνα', 'Πηγή / σημειώσεις', 'Σημείο (μ.)', 'Χρόνος (s)', 'Race ID', 'Χεριές τμήματος'];
var SWIM_REF = { name: 0, gender: 1, style: 2, distance: 3, pool: 4, source: 5, d: 6, t: 7, raceId: 8, strokes: 9 };
var SWIM_REF_NOTES = [
  'Όνομα της αναφοράς (π.χ. "WR 2024 – Pan Zhanle" ή "EJC 2025 Final – 1ος"). Γράφεται μόνο στην πρώτη γραμμή: ' +
    'οι επόμενες γραμμές με κενό όνομα είναι τα υπόλοιπα περάσματα της ίδιας αναφοράς.',
  'Ανδρών / Γυναικών (ή κενό). Αν ο αθλητής έχει φύλο, το Kinovea δείχνει μόνο τις αναφορές του ίδιου φύλου.',
  'fs, bk, br, fly, im (ή ελεύθερο, ύπτιο, πρόσθιο, πεταλούδα, μικτή).',
  'Απόσταση του αγωνίσματος: 50, 100, 200, 400, 800, 1500 ή 4x100, 4x200 για σκυταλοδρομίες.',
  '50 ή 25 (μέτρα πισίνας), 25y για πισίνα 25 γιαρδών. Κενό = 50.',
  'Ελεύθερο κείμενο (διοργάνωση, ημερομηνία, πηγή των χρόνων).',
  'Απόσταση του περάσματος σε μέτρα (ή γιάρδες σε πισίνα 25y): 15, 25, 35, 45, 50, 100… Δεν χρειάζονται όλα τα σημεία: το Δ βγαίνει όπου υπάρχει χρόνος.',
  'Χρόνος από το σήμα εκκίνησης: 26.12 ή 1:02.35.',
  'Συμπληρώνεται αυτόματα όταν η αναφορά προέρχεται από ανάλυση του Kinovea.',
  'Προαιρετικά: χεριές του τμήματος που τελειώνει σε αυτό το σημείο (όπως η στήλη Χεριές του Excel). Με αυτές το report συγκρίνει και συχνότητα, DPS και ΔΑΚ.'
];

function swimReferencesSheet_(ss) {
  var sh = ss.getSheetByName(SWIM_REFERENCES_SHEET);
  if (sh) {
    // Παλιότερη μορφή (χωρίς τη στήλη των χεριών): προσθήκη της κεφαλίδας.
    var width = sh.getLastColumn();
    if (width < SWIM_REF_HEADERS.length) {
      for (var c = Math.max(width, 1); c < SWIM_REF_HEADERS.length; c++) {
        sh.getRange(1, c + 1).setValue(SWIM_REF_HEADERS[c]).setFontWeight('bold').setBackground(SWIM_COLORS.header).setNote(SWIM_REF_NOTES[c]);
      }
    }
    return sh;
  }
  sh = ss.insertSheet(SWIM_REFERENCES_SHEET);
  var header = sh.getRange(1, 1, 1, SWIM_REF_HEADERS.length);
  header.setValues([SWIM_REF_HEADERS]).setFontWeight('bold').setBackground(SWIM_COLORS.header);
  for (var i = 0; i < SWIM_REF_HEADERS.length; i++) sh.getRange(1, i + 1).setNote(SWIM_REF_NOTES[i]);
  sh.setFrozenRows(1);
  // Κείμενο στη στήλη του χρόνου, ώστε το 1:02.35 να μένει όπως γράφεται.
  sh.getRange(2, SWIM_REF.t + 1, Math.max(1, sh.getMaxRows() - 1), 1).setNumberFormat('@');
  sh.getRange(2, SWIM_REF.distance + 1, Math.max(1, sh.getMaxRows() - 1), 1).setNumberFormat('@');
  [260, 90, 60, 80, 60, 260, 90, 90, 120, 90].forEach(function (w, i) { sh.setColumnWidth(i + 1, w); });
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

/** Φύλο: 'M' (Ανδρών), 'F' (Γυναικών) ή '' όταν δεν είναι γνωστό. */
function swimGenderCode_(v) {
  var g = swimNormText_(v);
  if (!g) return '';
  if (/^(ΓΥΝ|ΘΗΛ|ΚΟΡ|ΝΕΑΝ|ΠΑΓΚΟΡ|FEM|WOM|GIRL|LAD|F$|W$|Γ$|Θ$)/.test(g)) return 'F';
  if (/^(ΑΝΔ|ΑΡΡ|ΑΓΟΡ|ΕΦΗΒ|ΠΑΜΠ|ΠΑΙΔ|MAL|MEN|MAN|BOY|M$|Α$|A$)/.test(g)) return 'M';
  return '';
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
      if (!style) { out.skipped.push(swimTF_('γραμμή {0}: άγνωστο στυλ "{1}"', rowNumber, row[SWIM_REF.style])); continue; }
      if (!dist.distance) { out.skipped.push(swimTF_('γραμμή {0}: λείπει η απόσταση', rowNumber)); continue; }
      current = {
        id: '', name: name, gender: String(row[SWIM_REF.gender] || '').trim(), style: style,
        distance: dist.distance, relay: dist.relay, pool: swimPoolValue_(row[SWIM_REF.pool]), poolUnit: swimPoolUnit_(null, row[SWIM_REF.pool]),
        source: String(row[SWIM_REF.source] || '').trim(), raceId: String(row[SWIM_REF.raceId] || '').trim(), times: []
      };
      var poolKey = current.pool + (current.poolUnit === 'yd' ? 'y' : '');
      var id = current.raceId || ['sheet', name, style, (dist.relay ? '4x' + dist.distance / 4 : dist.distance), poolKey, current.gender].join('|');
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
    if (t === null) { out.skipped.push(swimTF_('γραμμή {0}: μη έγκυρος χρόνος "{1}"', rowNumber, display[i][SWIM_REF.t])); continue; }
    var nRaw = row[SWIM_REF.strokes];
    var n = swimNum_(typeof nRaw === 'string' ? nRaw.replace(',', '.') : nRaw);
    var p = { d: d, t: Math.round(t * 1000) / 1000 };
    if (n !== null && n >= 0) p.n = n;
    current.times.push(p);
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

function swimSaveReference_(raw) {
  var ref = raw || {};
  var name = String(ref.name || '').trim() || 'Αναφορά';
  var style = swimStyleCode_(ref.style);
  var distance = swimNum_(ref.distance) || 0;
  if (!style || !distance) throw new Error(swimT_('Η αναφορά χρειάζεται στυλ και απόσταση.'));
  var relay = !!ref.relay;
  var seen = {};
  var times = (ref.times || []).map(function (p) { return { d: swimNum_(p && p.d), t: swimNum_(p && p.t), n: swimNum_(p && p.n) }; })
    .filter(function (p) { if (p.d === null || p.d <= 0 || p.t === null || p.t <= 0 || seen[p.d]) return false; seen[p.d] = true; return true; })
    .sort(function (a, b) { return a.d - b.d; });
  if (!times.length) throw new Error(swimT_('Η αναφορά δεν έχει χρόνους.'));

  var raceId = String(ref.raceId || '').trim();
  var sh = swimReferencesSheet_(swimReportsSpreadsheet_());
  swimDeleteReferenceBlock_(sh, raceId);

  var distanceLabel = relay ? '4x' + distance / 4 : String(distance);
  var poolLabel = (swimNum_(ref.pool) || 50) + (swimPoolUnit_(ref.poolUnit, ref.pool) === 'yd' ? 'y' : '');
  var rows = times.map(function (p, i) {
    var t = String(Math.round(p.t * 100) / 100);
    var n = p.n === null || p.n === undefined ? '' : p.n;
    return i === 0
      ? [name, String(ref.gender || ''), style, distanceLabel, poolLabel, String(ref.source || ''), p.d, t, raceId, n]
      : ['', '', '', '', '', '', p.d, t, '', n];
  });
  var start = sh.getLastRow() + 1;
  var needed = start + rows.length - 1 - sh.getMaxRows();
  if (needed > 0) sh.insertRowsAfter(sh.getMaxRows(), needed);
  sh.getRange(start, SWIM_REF.distance + 1, rows.length, 1).setNumberFormat('@');
  sh.getRange(start, SWIM_REF.pool + 1, rows.length, 1).setNumberFormat('@');
  sh.getRange(start, SWIM_REF.t + 1, rows.length, 1).setNumberFormat('@');
  sh.getRange(start, 1, rows.length, SWIM_REF_HEADERS.length).setValues(rows);
  sh.getRange(start, 1, 1, SWIM_REF_HEADERS.length).setFontWeight('bold');
  return { ok: true, version: SWIM_VERSION, name: name, count: rows.length, row: start, sheet: SWIM_REFERENCES_SHEET };
}

function swimReferenceFromRace_(race, name) {
  var label = name || [race.athlete, [race.competition, race.round].filter(function (x) { return x; }).join(' ')]
    .filter(function (x) { return x; }).join(' – ') || 'Αναφορά';
  var analysis = swimAnalyze_(race);
  return {
    raceId: race.id, name: label, gender: race.gender, style: race.style, distance: race.distance, relay: race.relay, pool: race.pool,
    poolUnit: race.poolUnit,
    source: [race.competition, race.round, race.date].filter(function (x) { return x; }).join(' · '),
    times: analysis.rows.filter(function (r) { return r.t !== null && r.t > 0; })
      .map(function (r) { return r.strokes === null ? { d: r.d, t: r.t } : { d: r.d, t: r.t, n: r.strokes }; })
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
// Συγκρίσεις: PB (άλλη ανάλυση του αθλητή), elite αναφορά, τελικός (αναφορές της ίδιας πηγής), επίπεδα
// ---------------------------------------------------------------------------------------------

/** Αγώνας για ανάλυση από μια αναφορά (χρόνοι ανά σημείο και, αν υπάρχουν, χεριές ανά τμήμα). */
function swimReferenceRace_(ref) {
  var strokes = {}, any = false;
  var times = (ref.times || []).map(function (p) {
    var d = swimNum_(p && p.d), t = swimNum_(p && p.t), n = swimNum_(p && p.n);
    if (d !== null && n !== null) { strokes[d] = n; any = true; }
    return { d: d, t: t };
  }).filter(function (p) { return p.d !== null && p.d > 0 && p.t !== null && p.t > 0; });
  var race = swimNormalizeRace_({
    id: 'ref:' + String(ref.id || ref.name || ''), athlete: String(ref.name || 'Αναφορά'), gender: String(ref.gender || ''),
    style: ref.style, relay: !!ref.relay, distance: ref.distance, pool: ref.pool, poolUnit: ref.poolUnit,
    checkpoints: times, strokeRounding: 0
  });
  if (any) race.segmentStrokes = strokes;
  return race;
}

function swimSameEvent_(a, b) {
  return a && b && a.style === b.style && Number(a.distance) === Number(b.distance) && !!a.relay === !!b.relay &&
    Number(a.pool) === Number(b.pool) && (a.poolUnit || 'm') === (b.poolUnit || 'm');
}

/**
 * Σύντομη περιγραφή ενός αγώνα: "Final · EJC 2026 · 2.7.2026 · 2:00.38". shown: για το report, με τη φάση στη γλώσσα του
 * (χωρίς: όπως τη λίστα των αναλύσεων που κρατά το Kinovea).
 */
function swimRaceLabel_(race, analysis, shown) {
  var parts = [shown ? swimL_(race.round) : race.round, race.competition, race.date].filter(function (x) { return x; });
  var ft = analysis && analysis.summary.finalTime;
  if (ft) parts.push(swimFormatTime_(ft));
  return parts.join(' · ') || race.athlete || (shown ? swimT_('Αγώνας') : 'Αγώνας');
}

function swimFormatTime_(t) {
  if (t === null || t === undefined || !isFinite(t)) return '';
  if (t < 60) return (Math.round(t * 100) / 100).toFixed(2);
  var m = Math.floor(t / 60), s = t - m * 60;
  var ss = (Math.round(s * 100) / 100).toFixed(2);
  if (Number(ss) >= 60) { m++; ss = '0.00'; }
  return m + ':' + (Number(ss) < 10 ? '0' : '') + ss;
}

/**
 * Δεδομένα σύγκρισης του αγώνα (χρησιμοποιεί τα φύλλα: ευρετήριο, αναφορές, επίπεδα):
 * { pb: {id, label, race, analysis}, elite: {...}, finalists: [{label, race, analysis}], benchmarks: {...} }.
 * compare.pb: το Race ID μιας άλλης ανάλυσης, "auto" για τον καλύτερο χρόνο του αθλητή ή κενό.
 */
function swimComparisons_(race) {
  var out = { pb: null, elite: null, finalists: [], benchmarks: null };
  var pbId = race.compare && race.compare.pb ? String(race.compare.pb) : '';
  if (pbId === 'auto') {
    try { pbId = swimAutoPb_(race); } catch (e) { pbId = ''; }
  }
  if (pbId && pbId !== race.id) {
    try {
      var stored = swimStoredRace_(pbId);
      if (stored) {
        var pr = swimNormalizeRace_(stored.race);
        if (swimSameEvent_(pr, race)) {
          var pa = swimAnalyze_(pr);
          out.pb = { id: pbId, label: 'PB: ' + swimRaceLabel_(pr, pa), race: pr, analysis: pa };
        }
      }
    } catch (e) { /* χωρίς PB */ }
  }

  if (race.reference && race.reference.times && race.reference.times.length) {
    var er = swimReferenceRace_(race.reference);
    if (swimSameEvent_(er, race)) {
      out.elite = { id: String(race.reference.id || ''), label: String(race.reference.name || 'Elite'), race: er, analysis: swimAnalyze_(er) };
      // Ο τελικός: όλες οι αναφορές του ίδιου αγωνίσματος με την ίδια πηγή (π.χ. οι 8 φιναλίστ).
      var source = String(race.reference.source || '').trim();
      if (source) {
        try {
          swimReferences_().references.forEach(function (r) {
            if (String(r.source || '').trim() !== source || !swimSameEvent_(r, race)) return;
            if (swimGenderCode_(r.gender) && swimGenderCode_(race.gender) && swimGenderCode_(r.gender) !== swimGenderCode_(race.gender)) return;
            var fr = swimReferenceRace_(r);
            out.finalists.push({ label: r.name, race: fr, analysis: swimAnalyze_(fr) });
          });
        } catch (e) { out.finalists = []; }
        if (out.finalists.length < 2) out.finalists = [];
      }
    }
  }

  // Ο τελικός του αγωνίσματος από το Kinovea (π.χ. οι 8 του ολυμπιακού τελικού): προηγείται των αναφορών της ίδιας πηγής.
  if (race.finals && race.finals.swimmers && race.finals.swimmers.length >= 2) {
    var fromKinovea = [];
    race.finals.swimmers.forEach(function (s) {
      try {
        var fr = swimReferenceRace_({ id: 'final:' + (s.rank || '') + ':' + s.name, name: s.name, gender: race.gender, style: race.style,
          relay: race.relay, distance: race.distance, pool: race.pool, poolUnit: race.poolUnit, times: s.times });
        if (!swimSameEvent_(fr, race)) return;
        fromKinovea.push({ label: (s.rank ? s.rank + '. ' : '') + s.name, race: fr, analysis: swimAnalyze_(fr) });
      } catch (e) { /* χωρίς αυτόν */ }
    });
    if (fromKinovea.length >= 2) {
      out.finalists = fromKinovea;
      out.finalsLabel = race.finals.label;
    }
  }

  try { out.benchmarks = swimBenchmarksFor_(race, swimBenchmarks_()); } catch (e) { out.benchmarks = null; }
  return out;
}

/** Οι χρόνοι μιας ανάλυσης στα σημεία ελέγχου (για τη σύγκριση στο βίντεο, 2.8.0): [{ d, t }]. */
function swimCheckpointTimes_(race) {
  return (race.checkpoints || []).filter(function (c) { return c && c.d > 0 && c.t !== null && c.t !== undefined && isFinite(c.t) && c.t > 0; })
    .map(function (c) { return { d: c.d, t: Math.round(c.t * 1000) / 1000 }; });
}

/**
 * Οι άλλες αναλύσεις του ίδιου αθλητή στο ίδιο αγώνισμα και την ίδια πισίνα (για το PB), ταξινομημένες από τον καλύτερο
 * χρόνο. Διαβάζει πρώτα τις στήλες χωρίς τα JSON και ανοίγει το JSON μόνο για όσες ταιριάζουν. Από την 2.8.0 και με τους
 * χρόνους κάθε ανάλυσης (times), για σύγκριση στο βίντεο.
 */
function swimAnalysesFor_(query) {
  var q = query || {};
  var index = swimIndexSheet_(swimReportsSpreadsheet_());
  var last = index.getLastRow();
  var out = [];
  if (last < 2) return out;
  var want = swimNormalizeRace_({ style: q.style, distance: q.distance, relay: q.relay, pool: q.pool, poolUnit: q.poolUnit, checkpoints: [] });
  var athleteId = String(q.athleteId === undefined || q.athleteId === null ? '' : q.athleteId).trim();
  var athlete = swimNormText_(q.athlete);
  if (!athleteId && !athlete) return out;
  var eventLabel = swimIndexEventLabel_(want);
  var data = index.getRange(2, 1, last - 1, SWIM_IDX.sheetId).getValues();
  data.forEach(function (row, i) {
    var id = String(row[SWIM_IDX.id]);
    if (!id || id === String(q.raceId || '')) return;
    if (athleteId ? String(row[3]).trim() !== athleteId : swimNormText_(row[2]) !== athlete) return;
    if (String(row[4]) !== eventLabel || swimDistanceValue_(row[5]).distance !== want.distance) return;
    var r;
    try { r = swimNormalizeRace_(JSON.parse(String(index.getRange(i + 2, SWIM_IDX.json + 1).getValue()))); } catch (e) { return; }
    if (r.kind === 'training' || r.dq || !swimSameEvent_(r, want)) return;
    var ft = swimNum_(row[9]);
    out.push({ id: id, label: swimRaceLabel_(r, { summary: { finalTime: ft } }), finalTime: ft, date: r.date, round: r.round, competition: r.competition,
      times: swimCheckpointTimes_(r) });
  });
  out.sort(function (a, b) { return (a.finalTime || 1e9) - (b.finalTime || 1e9); });
  return out;
}

/** Πόσες αναλύσεις άλλων αθλητών επιστρέφονται για σύγκριση (οι καλύτεροι χρόνοι). */
var SWIM_EVENT_ANALYSES = 40;

/**
 * Πόσες αναλύσεις του αγωνίσματος κρατιούνται στην προσωρινή μνήμη (πριν βγουν του ίδιου αθλητή), για πόσο (s), και πόσα JSON
 * διαβάζονται το πολύ για να βρεθούν (το ευρετήριο δεν έχει φύλο ούτε πισίνα: τα άλλα ανοίγουν και αφήνονται).
 */
var SWIM_EVENT_CACHE = 60, SWIM_EVENT_CACHE_TIME = 3600, SWIM_EVENT_READS = 120;

/** Η στιγμή της τελευταίας αποθήκευσης ανάλυσης στο ευρετήριο (αλλάζει το κλειδί της προσωρινής μνήμης). */
function swimIndexStamp_() {
  try { return PropertiesService.getScriptProperties().getProperty('swimIndexStamp') || '0'; } catch (e) { return '0'; }
}

function swimTouchIndexStamp_() {
  try { PropertiesService.getScriptProperties().setProperty('swimIndexStamp', String(Date.now())); } catch (e) { /* χωρίς σήμανση */ }
}

/**
 * Αναλύσεις άλλων αθλητών στο ίδιο αγώνισμα, πισίνα και φύλο (όταν είναι γνωστό), για σύγκριση στο βίντεο (2.8.0): οι
 * καλύτεροι χρόνοι, με τους χρόνους στα σημεία ελέγχου. Διαβάζονται μόνο τα JSON των καλύτερων και μένουν στην προσωρινή
 * μνήμη του script για μία ώρα (μέχρι να μπει νέα ανάλυση): το Kinovea τις ζητά σε κάθε φόρτωση αγώνα.
 */
function swimEventAnalyses_(query) {
  var q = query || {};
  var index = swimIndexSheet_(swimReportsSpreadsheet_());
  var last = index.getLastRow();
  if (last < 2) return [];
  var want = swimNormalizeRace_({ style: q.style, distance: q.distance, relay: q.relay, pool: q.pool, poolUnit: q.poolUnit, checkpoints: [] });
  var athleteId = String(q.athleteId === undefined || q.athleteId === null ? '' : q.athleteId).trim();
  var athlete = swimNormText_(q.athlete);
  var gender = swimGenderCode_(q.gender);
  var eventLabel = swimIndexEventLabel_(want);
  // Το κλειδί: το αγώνισμα με την πισίνα του, το φύλο, το πλήθος των αναλύσεων και η τελευταία αποθήκευση ανάλυσης.
  var key = ['swimEvent', SWIM_VERSION, SWIM_LANG_, eventLabel, want.distance, want.pool, want.poolUnit || 'm', gender || '-', last,
    swimIndexStamp_()].join('|');
  var cache = null;
  try { cache = CacheService.getScriptCache(); } catch (e) { cache = null; }
  var list = cache ? swimCacheGet_(cache, key) : null;
  if (!Array.isArray(list)) {
    list = swimEventCandidates_(index, last, want, gender, eventLabel);
    if (cache) swimCachePut_(cache, key, list, SWIM_EVENT_CACHE_TIME);
  }
  var out = [];
  for (var k = 0; k < list.length && out.length < SWIM_EVENT_ANALYSES; k++) {
    var c = list[k];
    if (c.id === String(q.raceId || '')) continue;
    // Όχι ο ίδιος αθλητής (αυτές οι αναλύσεις είναι στη λίστα του PB).
    if ((athleteId && c.athleteId === athleteId) || (athlete && c.athleteKey === athlete)) continue;
    out.push({ id: c.id, athlete: c.athlete, gender: c.gender, label: c.label, finalTime: c.finalTime, date: c.date, round: c.round,
      competition: c.competition, times: c.times });
  }
  return out;
}

/** Μέγεθος ενός κομματιού της προσωρινής μνήμης (χαρακτήρες: ως 2 bytes ο καθένας, κάτω από τα 100 KB μιας τιμής). */
var SWIM_CACHE_PART = 45000;

/**
 * Μια τιμή της προσωρινής μνήμης σε κομμάτια (ένας αγώνας 1500μ. έχει 150 χρόνους: 60 αναλύσεις δεν χωρούν σε μία τιμή):
 * στο κλειδί το πλήθος των κομματιών, στα κλειδιά «κλειδί|i» τα κομμάτια. Αν κάτι λείπει, null (ξαναδιαβάζεται).
 */
function swimCacheGet_(cache, key) {
  try {
    var n = Number(cache.get(key));
    if (!(n >= 1 && n <= 40)) return null;
    var keys = [];
    for (var i = 0; i < n; i++) keys.push(key + '|' + i);
    var parts = cache.getAll(keys), text = '';
    for (var j = 0; j < n; j++) {
      if (typeof parts[keys[j]] !== 'string') return null;
      text += parts[keys[j]];
    }
    return JSON.parse(text);
  } catch (e) {
    return null;
  }
}

function swimCachePut_(cache, key, value, seconds) {
  try {
    var text = JSON.stringify(value), n = Math.max(1, Math.ceil(text.length / SWIM_CACHE_PART)), parts = {};
    if (n > 40) return;
    for (var i = 0; i < n; i++) parts[key + '|' + i] = text.substring(i * SWIM_CACHE_PART, (i + 1) * SWIM_CACHE_PART);
    cache.putAll(parts, seconds);
    cache.put(key, String(n), seconds);
  } catch (e) { /* χωρίς προσωρινή μνήμη: διαβάζεται ξανά την επόμενη φορά */ }
}

/** Οι καλύτερες αναλύσεις του αγωνίσματος (ως SWIM_EVENT_CACHE) με τους χρόνους τους: το JSON μόνο τους διαβάζεται. */
function swimEventCandidates_(index, last, want, gender, eventLabel) {
  var data = index.getRange(2, 1, last - 1, SWIM_IDX.sheetId).getValues();
  var match = [];
  data.forEach(function (row, i) {
    var id = String(row[SWIM_IDX.id]);
    if (!id) return;
    if (String(row[4]) !== eventLabel || swimDistanceValue_(row[5]).distance !== want.distance) return;
    var ft = swimNum_(row[9]);
    if (!(ft > 0)) return;
    match.push({ i: i, id: id, ft: ft, athleteId: String(row[3]).trim(), athleteKey: swimNormText_(row[2]) });
  });
  match.sort(function (a, b) { return a.ft - b.ft; });
  var out = [];
  for (var k = 0; k < match.length && k < SWIM_EVENT_READS && out.length < SWIM_EVENT_CACHE; k++) {
    var m = match[k], r;
    try { r = swimNormalizeRace_(JSON.parse(String(index.getRange(m.i + 2, SWIM_IDX.json + 1).getValue()))); } catch (e) { continue; }
    if (r.kind === 'training' || r.dq || !swimSameEvent_(r, want)) continue;
    if (gender && swimGenderCode_(r.gender) && swimGenderCode_(r.gender) !== gender) continue;
    var times = swimCheckpointTimes_(r);
    if (!times.length) continue;
    out.push({ id: m.id, athleteId: m.athleteId, athleteKey: m.athleteKey, athlete: r.athlete, gender: r.gender,
      label: swimRaceLabel_(r, { summary: { finalTime: m.ft } }), finalTime: m.ft, date: r.date, round: r.round, competition: r.competition,
      times: times });
  }
  return out;
}

/** Ημερομηνία "2.7.2026", "02/07/2026" ή "2026-07-02" → 20260702 (για σύγκριση), αλλιώς null. */
function swimDateKey_(s) {
  var t = String(s === null || s === undefined ? '' : s).trim();
  var m = t.match(/^(\d{4})-(\d{1,2})-(\d{1,2})/);
  if (m) return Number(m[1]) * 10000 + Number(m[2]) * 100 + Number(m[3]);
  m = t.match(/^(\d{1,2})[.\/-](\d{1,2})[.\/-](\d{2,4})/);
  if (!m) return null;
  var y = Number(m[3]);
  if (y < 100) y += 2000;
  return y * 10000 + Number(m[2]) * 100 + Number(m[1]);
}

/** PB αυτόματα: ο καλύτερος χρόνος του αθλητή στο ίδιο αγώνισμα, όχι μεταγενέστερος από τον αγώνα. */
function swimAutoPb_(race) {
  var list = swimAnalysesFor_({ athleteId: race.athleteId, athlete: race.athlete, style: race.style, distance: race.distance,
    relay: race.relay, pool: race.pool, poolUnit: race.poolUnit, raceId: race.id });
  var day = swimDateKey_(race.date);
  for (var i = 0; i < list.length; i++) {
    if (!list[i].finalTime) continue;
    var d = swimDateKey_(list[i].date);
    if (day !== null && d !== null && d > day) continue;
    return list[i].id;
  }
  return '';
}

// ---------------------------------------------------------------------------------------------
// Επίπεδα (πίνακες 2015–2024 του Excel): εκκίνηση 0-15μ., στροφή τοίχος → 15μ. (κεφάλι), στροφές μικτής
// ---------------------------------------------------------------------------------------------

var SWIM_BENCH_SHEET = 'Επίπεδα';
var SWIM_BENCH_LEVELS = ['Καλύτερη Επίδοση', 'Ολυμπ./Παγκόσμιο 1ος', 'Top 3', 'Top 8', 'Top 16', 'Junior μετάλλιο'];
var SWIM_BENCH_HEADERS = ['Δείκτης', 'Φύλο', 'Στυλ', 'Απόσταση', 'Πισίνα', 'Στροφή'].concat(SWIM_BENCH_LEVELS).concat(['Πηγή']);

function swimBenchmarksSheet_(ss) {
  var sh = ss.getSheetByName(SWIM_BENCH_SHEET);
  if (sh) return sh;
  sh = ss.insertSheet(SWIM_BENCH_SHEET);
  sh.getRange(1, 1, 1, SWIM_BENCH_HEADERS.length).setValues([SWIM_BENCH_HEADERS]).setFontWeight('bold').setBackground(SWIM_COLORS.header);
  sh.getRange(1, 1).setNote('"Εκκίνηση 0-15μ." (σήμα → κεφάλι στα 15μ.), "Στροφή τοίχος-15μ." (επαφή → κεφάλι στα +15μ.) ή, για τη μικτή, "Στροφή" με το είδος στη στήλη Στροφή.');
  sh.getRange(1, 6).setNote('Μόνο για τη μικτή: π.χ. "Πεταλούδα → Ύπτιο" ή "Fly → Back".');
  sh.setFrozenRows(1);
  [150, 80, 70, 80, 60, 150, 110, 140, 70, 70, 70, 110, 150].forEach(function (w, i) { sh.setColumnWidth(i + 1, w); });
  return sh;
}

/** Όλες οι γραμμές του φύλλου "Επίπεδα". */
function swimBenchmarks_() {
  var ss = swimReportsSpreadsheet_();
  var sh = swimBenchmarksSheet_(ss);
  var last = sh.getLastRow();
  if (last < 2) return [];
  var values = sh.getRange(2, 1, last - 1, SWIM_BENCH_HEADERS.length).getValues();
  return swimParseBenchmarks_(values);
}

/** Γραμμές → [{kind: 'start'|'turn'|'im', gender, style, distance, pool, poolUnit, from, to, label, values: [6]}]. */
function swimParseBenchmarks_(values) {
  var out = [];
  values.forEach(function (row) {
    var kindText = swimNormText_(row[0]);
    if (!kindText) return;
    var dist = swimDistanceValue_(row[3]);
    var turnText = String(row[5] || '').trim();
    var kind = turnText ? 'im' : (kindText.indexOf('ΕΚΚΙΝ') >= 0 || kindText.indexOf('START') >= 0 ? 'start' : 'turn');
    var levels = [];
    for (var i = 0; i < SWIM_BENCH_LEVELS.length; i++) {
      var v = row[6 + i];
      levels.push(typeof v === 'number' ? v : swimNum_(String(v === null || v === undefined ? '' : v).replace(',', '.')));
    }
    if (!levels.some(function (v) { return v !== null; })) return;
    var b = {
      kind: kind, gender: swimGenderCode_(row[1]), style: swimStyleCode_(row[2]), distance: dist.distance, relay: dist.relay,
      pool: swimPoolValue_(row[4]), poolUnit: swimPoolUnit_(null, row[4]), values: levels, source: String(row[12] || '').trim(), label: turnText
    };
    if (kind === 'im') {
      var parts = turnText.split(/→|->|-/);
      b.from = parts.length > 1 ? swimStyleCode_(parts[0]) : null;
      b.to = parts.length > 1 ? swimStyleCode_(parts[parts.length - 1]) : null;
    }
    if (b.style) out.push(b);
  });
  return out;
}

/** Τα επίπεδα του αγωνίσματος του αγώνα: { start, turn, im: [...] } ή null. */
function swimBenchmarksFor_(race, list) {
  if (!list || !list.length) return null;
  var g = swimGenderCode_(race.gender);
  var match = function (b) {
    return b.style === race.style && b.distance === race.distance && b.pool === race.pool && b.poolUnit === race.poolUnit &&
      (!g || !b.gender || b.gender === g);
  };
  var pick = function (kind) {
    var cand = list.filter(function (b) { return b.kind === kind && match(b); });
    cand.sort(function (a, b) { return (b.gender === g ? 1 : 0) - (a.gender === g ? 1 : 0); });
    return cand.length ? cand[0] : null;
  };
  var res = { levels: SWIM_BENCH_LEVELS, start: pick('start'), turn: pick('turn'), im: list.filter(function (b) { return b.kind === 'im' && match(b); }) };
  return res.start || res.turn || res.im.length ? res : null;
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

/** Η γραμμή του ευρετηρίου για ένα Race ID (διαβάζει μόνο τη στήλη των id, όχι τα JSON). */
function swimIndexRow_(index, id) {
  var last = index.getLastRow();
  if (last < 2 || !id) return null;
  var ids = index.getRange(2, SWIM_IDX.id + 1, last - 1, 1).getValues();
  for (var i = 0; i < ids.length; i++) if (String(ids[i][0]) === String(id)) return i + 2;
  return null;
}

function swimFindIndexEntry_(index, id) {
  var row = swimIndexRow_(index, id);
  if (!row) return null;
  var data = index.getRange(row, 1, 1, SWIM_INDEX_HEADERS.length).getValues()[0];
  var prevBase = null;
  try { prevBase = swimSheetBaseName_(swimNormalizeRace_(JSON.parse(String(data[SWIM_IDX.json])))); } catch (e) { prevBase = null; }
  return { row: row, sheetId: String(data[SWIM_IDX.sheetId]), layoutSig: String(data[SWIM_IDX.layout]), prevBaseName: prevBase };
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
  if (raw.length > 49000) raw = JSON.stringify(swimCompactRace_(race));
  var rowValues = [race.id, link, race.athlete, race.athleteId, swimIndexEventLabel_(race), race.distanceLabel,
    race.round, race.competition, race.date, finalTime === '' || finalTime === null || finalTime === undefined ? '' : finalTime,
    Utilities.formatDate(new Date(), Session.getScriptTimeZone(), 'dd/MM/yyyy HH:mm'), race.video, String(sheet.getSheetId()), signature, raw];
  var r = entry ? entry.row : index.getLastRow() + 1;
  index.getRange(r, 1, 1, rowValues.length).setValues([rowValues]);
  swimTouchIndexStamp_();
}

/**
 * "fs", "medley relay", "fs σκέλος", "τεχνική fs": η στήλη Αγώνισμα του ευρετηρίου (και το φίλτρο της λίστας PB).
 * Τα σκέλη σκυταλοδρομιών (εκκίνηση με αλλαγή) έχουν δική τους ετικέτα, ώστε να μη μπαίνουν στα PB των ατομικών αγωνισμάτων.
 */
function swimIndexEventLabel_(race) {
  var label = (SWIM_STYLE_LABELS[race.style] || race.style) + (race.relay ? ' relay' : '') + (race.relayLeg ? ' σκέλος' : '');
  return race.kind === 'training' ? 'τεχνική ' + label : label;
}

/**
 * Πολύ μεγάλος αγώνας για ένα κελί (όριο 50.000 χαρακτήρων, π.χ. 1500μ. με όλες τις χεριές): κρατάμε ό,τι χρειάζεται
 * για να ξαναφτιαχτεί το φύλλο, με τους χρόνους σε 2 δεκαδικά και χωρίς τα στιγμιότυπα πέρα από τα στοιχεία τους.
 */
function swimCompactRace_(race) {
  var c = JSON.parse(JSON.stringify(race));
  var r2 = function (arr) { return (arr || []).map(function (t) { return Math.round(t * 100) / 100; }); };
  ['strokes', 'kicks', 'breakouts', 'feetOff', 'entries'].forEach(function (k) { c[k] = r2(c[k]); });
  if (JSON.stringify(c).length > 49000) {
    c.kicks = [];
    if (c.tech) c.tech.snapshots = (c.tech.snapshots || []).map(function (s) { return { id: s.id, t: s.t, phase: s.phase, stage: s.stage, event: s.event, comment: String(s.comment || '').substring(0, 500), fileId: s.fileId, eliteFileId: s.eliteFileId, eliteLabel: s.eliteLabel }; });
  }
  return c;
}

function swimSheetBaseName_(race) {
  var styleLabel = SWIM_STYLE_LABELS[race.style] || race.style;
  var unit = race.poolUnit === 'yd' ? 'y' : '';
  var parts = race.kind === 'training'
    ? [race.athlete || 'Προπόνηση', 'Τεχνική', styleLabel, race.date]
    : [race.athlete || 'Αγώνας', race.distanceLabel + unit + styleLabel, race.pool === 25 ? (unit ? 'SCY' : 'SCM') : '', race.round];
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

/**
 * PDF του φύλλου στον φάκελο των reports. area { rows, cols }: η περιοχή που τυπώνεται (από το layout), ώστε το
 * πλάτος της σελίδας να είναι ακριβώς οι στήλες του report και οι σελίδες να πέφτουν εκεί που υπολογίστηκαν.
 * quick (2.8.0): η πρώτη σελίδα «Με μια ματιά» (swimBuildQuickLook_), πριν από την αναφορά, σε αντίγραφο του φύλλου.
 */
function swimExportPdf_(ss, sheet, area, fresh, quick) {
  if (!sheet) throw new Error(swimT_('Δεν βρέθηκε το φύλλο για PDF.'));
  SpreadsheetApp.flush();
  swimPrepareCharts_(sheet, fresh);
  // Το Google δεν σχεδιάζει πάντα στο PDF τα γραφήματα ενός φύλλου που δεν είναι ανοιχτό: κάθε γράφημα μπαίνει ως εικόνα
  // στη θέση του για την εξαγωγή, και μετά επιστρέφει.
  var blob = null, withQuick = false;
  if (quick && area && area.rows > 0 && quick.cols === area.cols) {
    // Η πρώτη σελίδα «Με μια ματιά» σε ένα αντίγραφο του φύλλου· ό,τι κι αν αποτύχει εκεί, το PDF όπως πριν (χωρίς αυτήν).
    var copy = null;
    try {
      copy = swimQuickLookSheet_(ss, sheet, quick);
      var print = { rows: area.rows + copy.rows, cols: area.cols };
      swimChartsAsImages_(copy.sheet, print);
      blob = swimPdfFetch_(swimPdfUrl_(ss, copy.sheet, print));
      withQuick = true;
    } catch (e) {
      blob = null;
    } finally {
      if (copy) {
        try { ss.deleteSheet(copy.sheet); } catch (e2) { /* σβήνεται την επόμενη φορά */ }
      }
    }
  }
  if (!blob) {
    var swapped = [];
    try {
      swapped = swimChartsAsImages_(sheet, area);
      blob = swimPdfFetch_(swimPdfUrl_(ss, sheet, area));
    } finally {
      swimRestoreCharts_(sheet, swapped);
    }
  }
  var fileName = sheet.getName() + '.pdf';
  var folder = swimReportFolder_();
  var old = folder.getFilesByName(fileName);
  while (old.hasNext()) old.next().setTrashed(true);
  var file = folder.createFile(blob.setName(fileName));
  var result = { url: file.getUrl(), name: fileName, id: file.getId() };
  // Για το Kinovea: η πρώτη σελίδα έγινε ή όχι (ζητήθηκε αλλά δεν έγινε: false).
  if (quick) result.quick = withQuick;
  return result;
}

/**
 * Πριν από το PDF: οι τιμές κάθε γραφήματος υπολογίζονται (διαβάζονται από το φύλλο) και, αν τα γραφήματα μόλις
 * φτιάχτηκαν, λίγη αναμονή ώστε το Google να τα έχει σχεδιάσει· αλλιώς μπορεί να βγουν άδεια στο PDF.
 */
function swimPrepareCharts_(sheet, fresh) {
  var charts = [];
  try { charts = sheet.getCharts(); } catch (e) { return; }
  // Με όλα τα γραφήματα ως εικόνες του Kinovea δεν υπάρχει τίποτα να περιμένει.
  if (!charts.length) return;
  try {
    charts.forEach(function (chart) { chart.getRanges().forEach(function (range) { range.getValues(); }); });
  } catch (e) { /* τα γραφήματα σχεδιάζονται όπως είναι */ }
  if (fresh) Utilities.sleep(4000);
}

/**
 * Κάθε γράφημα του φύλλου ως εικόνα (σχεδιασμένη από το ίδιο το Google, με τις τιμές του) στη θέση του, και το γράφημα έξω
 * από την περιοχή που τυπώνεται. Επιστρέφει ό,τι χρειάζεται για να γυρίσουν πίσω (swimRestoreCharts_).
 */
function swimChartsAsImages_(sheet, area) {
  var swapped = [];
  if (!area || !(area.rows > 0)) return swapped;
  var park = area.rows + 30;
  try {
    if (sheet.getMaxRows() < park + 40) sheet.insertRowsAfter(sheet.getMaxRows(), park + 40 - sheet.getMaxRows());
  } catch (e) { return swapped; }
  sheet.getCharts().forEach(function (chart) {
    var image = null;
    try {
      var info = chart.getContainerInfo();
      var options = chart.getOptions();
      var w = Number(options.get('width')) || SWIM_PAIR.width, h = Number(options.get('height')) || SWIM_CHART_H;
      var blob = chart.getAs('image/png');
      image = sheet.insertImage(blob, info.getAnchorColumn(), info.getAnchorRow(), info.getOffsetX(), info.getOffsetY());
      image.setWidth(w).setHeight(h);
      var item = { image: image, row: info.getAnchorRow(), col: info.getAnchorColumn(), x: info.getOffsetX(), y: info.getOffsetY(), chart: chart };
      sheet.updateChart(chart.modify().setPosition(park, 1, 0, 0).build());
      item.parked = true;
      swapped.push(item);
    } catch (e) {
      // Το γράφημα μένει όπως είναι (και η εικόνα του, αν μπήκε, φεύγει).
      try { if (image) image.remove(); } catch (e2) { /* τίποτα */ }
    }
  });
  if (swapped.length) SpreadsheetApp.flush();
  return swapped;
}

function swimRestoreCharts_(sheet, swapped) {
  (swapped || []).forEach(function (s) {
    try { s.image.remove(); } catch (e) { /* τίποτα */ }
    if (!s.parked) return;
    try {
      var chart = sheet.getCharts().filter(function (c) { return c.getChartId && s.chart.getChartId && c.getChartId() === s.chart.getChartId(); })[0] || s.chart;
      sheet.updateChart(chart.modify().setPosition(s.row, s.col, s.x, s.y).build());
    } catch (e) { /* μένει έξω από την περιοχή του PDF */ }
  });
}

/** A4 κατακόρυφα, περιθώρια σε ίντσες: τα ίδια στην εξαγωγή και στον υπολογισμό των σελίδων. */
var SWIM_PDF = { width: 8.27, height: 11.69, top: 0.40, bottom: 0.40, left: 0.25, right: 0.25, footer: 0.24 };
/** Αλλάζει όταν αλλάζουν οι ρυθμίσεις της σελίδας ή η μέτρηση: το ύψος της σελίδας ξαναμετριέται μία φορά. */
var SWIM_PDF_CALIBRATION = 1;
var SWIM_PDF_CALIBRATION_SHEET = '_swim_pdf_pages';

function swimPdfUrl_(ss, sheet, area) {
  var p = SWIM_PDF;
  var url = 'https://docs.google.com/spreadsheets/d/' + ss.getId() + '/export?format=pdf&gid=' + sheet.getSheetId() +
    '&size=A4&portrait=true&fitw=true&gridlines=false&printtitle=false&sheetnames=false&pagenum=CENTER&fzr=false' +
    '&top_margin=' + p.top.toFixed(2) + '&bottom_margin=' + p.bottom.toFixed(2) +
    '&left_margin=' + p.left.toFixed(2) + '&right_margin=' + p.right.toFixed(2) + '&horizontal_alignment=CENTER';
  if (area && area.rows > 0 && area.cols > 0) url += '&r1=0&c1=0&r2=' + area.rows + '&c2=' + area.cols;
  return url;
}

function swimPdfFetch_(url) {
  var options = { headers: { Authorization: 'Bearer ' + ScriptApp.getOAuthToken() }, muteHttpExceptions: true };
  var resp = UrlFetchApp.fetch(url, options);
  // Πολλές εξαγωγές στη σειρά: το Google ζητά λίγη αναμονή.
  for (var i = 1; i <= 3 && resp.getResponseCode() === 429; i++) {
    Utilities.sleep(1500 * i);
    resp = UrlFetchApp.fetch(url, options);
  }
  if (resp.getResponseCode() !== 200) throw new Error(swimTF_('Αποτυχία εξαγωγής PDF (HTTP {0}).', resp.getResponseCode()));
  return resp.getBlob();
}

/** Πλήθος σελίδων ενός PDF: το /Count του δέντρου σελίδων ή, αλλιώς, τα αντικείμενα /Type /Page. */
function swimPdfPageCount_(blob) {
  var text = blob.getDataAsString('ISO-8859-1');
  var count = 0, m;
  var re = /\/Type\s*\/Pages\b[^>]*?\/Count\s+(\d+)|\/Count\s+(\d+)[^>]*?\/Type\s*\/Pages\b/g;
  while ((m = re.exec(text))) count = Math.max(count, Number(m[1] || m[2]));
  if (count > 0) return count;
  var pages = text.match(/\/Type\s*\/Page(?![A-Za-z])/g);
  return pages ? pages.length : 0;
}

/**
 * Ύψος σελίδας από τις διαστάσεις του A4 (px του φύλλου, για πλάτος width px): το PDF μετρά και 1 px ανά στήλη και
 * κρατά ~0.24'' για τον αριθμό της σελίδας (από ένα PDF του Google Sheets). Η μέτρηση (swimCalibratePdf_) το διορθώνει.
 */
function swimPdfEstimatedHeight_(width) {
  var p = SWIM_PDF;
  return Math.floor((width + 15) * (p.height - p.top - p.bottom - p.footer) / (p.width - p.left - p.right));
}

function swimPdfStored_(width) {
  try { return JSON.parse(swimProps_().getProperty('SWIM_PDF_PAGE_' + width) || 'null'); } catch (e) { return null; }
}

/**
 * Ύψος μιας σελίδας του PDF σε px του φύλλου, για report πλάτους width px. Μετριέται μία φορά από το ίδιο το Google
 * (swimCalibratePdf_) και κρατιέται στις ιδιότητες του script. Αν η μέτρηση αποτύχει, ο υπολογισμός από το A4
 * (και νέα δοκιμή μετά από μία ώρα).
 */
function swimPdfPageHeight_(ss, width) {
  var stored = swimPdfStored_(width);
  if (stored && stored.v === SWIM_PDF_CALIBRATION && stored.h > 0) return stored.h;
  if (stored && stored.v === SWIM_PDF_CALIBRATION && stored.failed && Date.now() - stored.failed < 3600 * 1000)
    return swimPdfEstimatedHeight_(width);
  var h = null, error = '';
  try { h = swimCalibratePdf_(ss, width); } catch (e) { error = String(e && e.message ? e.message : e); }
  var props = swimProps_();
  if (h > 0) {
    props.setProperty('SWIM_PDF_PAGE_' + width, JSON.stringify({ v: SWIM_PDF_CALIBRATION, h: h, est: swimPdfEstimatedHeight_(width), at: new Date().toISOString() }));
    return h;
  }
  props.setProperty('SWIM_PDF_PAGE_' + width, JSON.stringify({ v: SWIM_PDF_CALIBRATION, failed: Date.now(), error: error || 'άγνωστο πλήθος σελίδων' }));
  return swimPdfEstimatedHeight_(width);
}

/**
 * Μέτρηση της σελίδας: σε ένα προσωρινό φύλλο με στήλες του ίδιου πλάτους, με όσες εξαγωγές PDF χρειάζονται (~12),
 * βρίσκει πόσες γραμμές των 21 px και πόσα px ακόμη χωρούν σε μία σελίδα (ακρίβεια 1 px). Το φύλλο σβήνεται στο τέλος.
 */
function swimCalibratePdf_(ss, width) {
  // Προσωρινό φύλλο με δικό του όνομα· όσα έμειναν από διακοπή (πάνω από 10 λεπτά) σβήνονται.
  var now = Date.now(), prefix = SWIM_PDF_CALIBRATION_SHEET + '_';
  ss.getSheets().forEach(function (s) {
    var name = s.getName();
    if (name.indexOf(prefix) === 0 && now - Number(name.substring(prefix.length)) > 10 * 60 * 1000) {
      try { ss.deleteSheet(s); } catch (e) { /* σε χρήση */ }
    }
  });
  var sh = ss.insertSheet(prefix + now);
  try {
    var widths = [], left = width;
    for (var c = 1; left > 0; c++) { var w = Math.min(swimColumnWidth_(c), left); widths.push(w); left -= w; }
    if (sh.getMaxColumns() < widths.length) sh.insertColumnsAfter(sh.getMaxColumns(), widths.length - sh.getMaxColumns());
    widths.forEach(function (w2, i) { sh.setColumnWidth(i + 1, w2); });
    var est = swimPdfEstimatedHeight_(width);
    var maxRows = Math.ceil(est * 1.25 / SWIM_ROW_H) + 2;
    if (sh.getMaxRows() < maxRows) sh.insertRowsAfter(sh.getMaxRows(), maxRows - sh.getMaxRows());
    swimForceRowHeights_(sh, 1, maxRows, SWIM_ROW_H);
    sh.getRange(1, 1).setValue('·');
    // Μία σελίδα για n γραμμές των 21 px και (αν extra > 0) μία ακόμη γραμμή extra px;
    var onePage = function (n, extra) {
      var last = extra > 0 ? n + 1 : n;
      if (extra > 0) swimForceRowHeights_(sh, last, 1, extra);
      var mark = sh.getRange(last, widths.length);
      mark.setValue('·');
      SpreadsheetApp.flush();
      var pages = swimPdfPageCount_(swimPdfFetch_(swimPdfUrl_(ss, sh, { rows: last, cols: widths.length })));
      mark.clearContent();
      if (extra > 0) swimForceRowHeights_(sh, last, 1, SWIM_ROW_H);
      if (!(pages > 0)) throw new Error('δεν διαβάστηκε το πλήθος των σελίδων του PDF');
      return pages === 1;
    };
    var lo = Math.floor(est * 0.85 / SWIM_ROW_H), hi = Math.ceil(est * 1.2 / SWIM_ROW_H);
    if (!onePage(lo, 0) || onePage(hi, 0)) return null;
    while (hi - lo > 1) {
      var mid = Math.floor((lo + hi) / 2);
      if (onePage(mid, 0)) lo = mid; else hi = mid;
    }
    var a = 0, b = SWIM_ROW_H;
    while (b - a > 1) {
      var m = Math.floor((a + b) / 2);
      if (onePage(lo, m)) a = m; else b = m;
    }
    return lo * SWIM_ROW_H + a;
  } finally {
    ss.deleteSheet(sh);
  }
}

// ---------------------------------------------------------------------------------------------
// PDF: η πρώτη σελίδα «Με μια ματιά» (2.8.0)
//
// Πριν από την πλήρη αναφορά (ίδια όπως πριν), μία σελίδα για μια γρήγορη ματιά: ο χρόνος και οι συγκρίσεις, οι κύριοι
// δείκτες, τα βασικά σημεία του αγώνα σε προτάσεις, οι χρόνοι ανά τμήμα / 50 / 100 με μπάρες ταχύτητας, η εκκίνηση, οι
// στροφές, ο τερματισμός και το καρέ του τερματισμού. Φτιάχνεται μόνο για το PDF, σε αντίγραφο του φύλλου του αγώνα: γραμμές
// στην αρχή του με ύψος μία σελίδα του PDF (λίγο λιγότερο), ώστε η πλήρης αναφορά να αρχίζει στη 2η σελίδα με τις ίδιες
// αλλαγές σελίδας. Το αντίγραφο σβήνεται μετά την εξαγωγή· το φύλλο του αγώνα δεν αλλάζει.
// ---------------------------------------------------------------------------------------------

/** px που μένουν κάτω στην πρώτη σελίδα: η πρώτη γραμμή της αναφοράς (48 px) δεν χωρά εκεί και αρχίζει στη 2η σελίδα. */
var SWIM_QUICK_SLACK = 4;
/** Τα αντίγραφα του φύλλου για το PDF (σβήνονται· όσα έμειναν από διακοπή, μετά από 10 λεπτά). */
var SWIM_QUICK_PREFIX = '_swim_pdf_';
var SWIM_QUICK_COLORS = {
  good: '#1E8E5A', bad: '#C62828', neutral: '#14243A', goodBg: '#E5F5EC', badBg: '#FCEBEB', bar: '#2E86DE', soft: '#F6F8FB'
};

function swimQuickNum_(v) {
  return typeof v === 'number' && isFinite(v) ? v : null;
}

/** "+0.45" / "-0.30" (αθλητής − σύγκριση: αρνητικό = πιο γρήγορα). */
function swimQuickDelta_(v) {
  if (v === null || v === undefined || !isFinite(v)) return '';
  var a = Math.round(Math.abs(v) * 100) / 100;
  return (a < 0.005 ? '±' : (v > 0 ? '+' : '-')) + a.toFixed(2);
}

function swimQuickFixed_(v, n) {
  return v === null || v === undefined || !isFinite(v) ? '' : (Math.round(v * Math.pow(10, n)) / Math.pow(10, n)).toFixed(n);
}

/** Οι χρόνοι μιας ανάλυσης ανά απόσταση. */
function swimQuickTimes_(analysis) {
  var m = {};
  (analysis && analysis.rows ? analysis.rows : []).forEach(function (r) {
    if (r.t !== null && r.t !== undefined && isFinite(r.t)) m[r.d] = r.t;
  });
  return m;
}

/**
 * Τα τμήματα του πίνακα: κάθε σημείο ελέγχου έως τα 100, ανά 50 έως τα 400, ανά 100 πιο πάνω (mode 'cp' | '50' | '100').
 * { label, d, cum, split, vel, strokes, sr, dps, dak, dPb, dRef, phase }.
 */
function swimQuickSegments_(race, analysis, pbT, refT, mode) {
  var k = race.k || 1, U = race.poolUnit === 'yd' ? 'yd' : '';
  var delta = function (map, d, t) { return t !== null && map[d] !== undefined ? t - map[d] : null; };
  var out = [];
  if (mode === 'cp') {
    analysis.rows.forEach(function (r) {
      if (r.t === null || r.t === undefined) return;
      out.push({ label: r.prevD + '–' + r.d + U, d: r.d, cum: r.t, split: swimQuickNum_(r.segTime), vel: swimQuickNum_(r.vel), strokes: swimQuickNum_(r.strokes),
        sr: swimQuickNum_(r.sr), dps: swimQuickNum_(r.dps), dak: null, dPb: delta(pbT, r.d, r.t), dRef: delta(refT, r.d, r.t), phase: !r.isFree });
    });
    return out;
  }
  var own = swimQuickTimes_(analysis);
  var blocks = analysis.blocks || [];
  var step = mode === '100' ? 100 : 50;
  for (var b = 0; b + step <= race.distance + 1e-9; b += step) {
    var e = b + step;
    var inside = blocks.filter(function (x) { return x.from >= b - 1e-9 && x.to <= e + 1e-9; });
    if (!inside.length) continue;
    var cum = own[e] !== undefined ? own[e] : null;
    var start = b === 0 ? 0 : (own[b] !== undefined ? own[b] : null);
    var split = cum !== null && start !== null ? cum - start : null;
    var avg = function (key) {
      var v = inside.map(function (x) { return x[key]; }).filter(function (x) { return x !== null && x !== undefined && isFinite(x); });
      return v.length ? v.reduce(function (a, c) { return a + c; }, 0) / v.length : null;
    };
    var strokes = inside.some(function (x) { return x.strokes !== null && x.strokes !== undefined; })
      ? inside.reduce(function (a, x) { return a + (x.strokes || 0); }, 0) : null;
    out.push({ label: b + '–' + e + U, d: e, cum: cum, split: split, vel: split ? step * k / split : null, strokes: strokes,
      sr: avg('sr'), dps: avg('dps'), dak: avg('dak'), dPb: delta(pbT, e, cum), dRef: delta(refT, e, cum), phase: false });
  }
  return out;
}

/**
 * Τα δεδομένα της πρώτης σελίδας (καθαρή συνάρτηση, για τα tests): ο χρόνος, οι συγκρίσεις, οι δείκτες, τα βασικά σημεία,
 * τα τμήματα του πίνακα, η εκκίνηση, οι στροφές και ο τερματισμός. kpi: οι τιμές του φύλλου (μετρούν και όσα γράφτηκαν με το χέρι).
 */
function swimQuickLookData_(race, analysis, extras, kpi, mode) {
  var x = extras || {}, s = analysis.summary || {}, k = kpi || {}, n = swimQuickNum_;
  var U = swimUnitLabel_(race);
  var ft = n(k.finalTime) !== null ? k.finalTime : n(s.finalTime);
  var own = swimQuickTimes_(analysis);
  var pbA = x.pb ? x.pb.analysis : null, refA = x.elite ? x.elite.analysis : null;
  var pbT = swimQuickTimes_(pbA), refT = swimQuickTimes_(refA);
  var d = { U: U, ft: ft, dq: !!race.dq };
  d.avgVel = n(k.avgVel) !== null ? k.avgVel : (ft ? race.distance * (race.k || 1) / ft : null);
  d.strokes = n(k.totalStrokes) !== null ? k.totalStrokes : n(s.totalStrokes);
  d.sr = n(k.sr) !== null ? k.sr : n(s.avgSr);
  d.dps = n(k.dps) !== null ? k.dps : n(s.avgDps);
  d.dak = n(k.dak) !== null ? k.dak : n(s.avgDak);
  // 2.9.5: ο ΔΑΚ κάθε 50άρι [{ k, v }] (του φύλλου, αλλιώς της ανάλυσης).
  d.dak50 = !SWIM_DAK50 ? [] : (Array.isArray(k.dak50) ? k.dak50
    : (analysis.blocks || []).map(function (b) { return { k: Math.round(b.to / 50), v: n(b.dak) }; }));

  var compare = function (c, a) {
    if (!c || !a) return null;
    var t = n(a.summary ? a.summary.finalTime : null);
    return { time: t, delta: t !== null && ft !== null ? ft - t : null, label: swimRaceLabel_(c.race, null, true), name: String(c.label || '') };
  };
  d.pb = compare(x.pb, pbA);
  d.ref = compare(x.elite, refA);
  if (d.ref) d.ref.label = d.ref.name;

  var half = race.distance / 2;
  if (race.distance >= 100 && ft !== null && own[half] !== undefined) d.halves = { first: own[half], second: ft - own[half], at: half };

  var st = analysis.start || {}, m = race.manual || {};
  d.reaction = n(m.reaction);
  d.t15 = n(st.t15);
  d.ref15 = refT[15] !== undefined ? refT[15] : null;
  d.breakout = n(st.breakout);
  d.breakoutDist = n(m.breakoutDist);
  d.kicks = n(st.kicks);
  d.turns = (analysis.turns || []).filter(function (t) { return !t.exchange; }).map(function (t) {
    return { wall: t.wall, inOut: n(t.inOut), turnTime: n(t.turnTime), out15: n(t.out15), underwater: n(t.underwaterTime) };
  });
  var inOuts = d.turns.filter(function (t) { return t.inOut !== null; });
  d.turnAvg = inOuts.length ? inOuts.reduce(function (a, t) { return a + t.inOut; }, 0) / inOuts.length : null;
  d.turnBest = inOuts.length ? inOuts.reduce(function (a, t) { return a === null || t.inOut < a.inOut ? t : a; }, null) : null;
  var refInOuts = refA ? (refA.turns || []).filter(function (t) { return !t.exchange && n(t.inOut) !== null; }) : [];
  d.refTurnAvg = refInOuts.length ? refInOuts.reduce(function (a, t) { return a + t.inOut; }, 0) / refInOuts.length : null;
  var fin = analysis.finish || {};
  d.last5 = n(fin.last5);
  d.last15 = n(fin.last15);
  d.lastStroke = n(fin.lastStrokeGap);
  d.warnings = (analysis.warnings || []).length;

  d.mode = mode || (race.distance <= 100 ? 'cp' : (race.distance <= 400 ? '50' : '100'));
  d.segments = swimQuickSegments_(race, analysis, pbT, refT, d.mode);
  d.blocks = (analysis.blocks || []).map(function (b) { return { from: b.from, to: b.to, time: n(b.time), sr: n(b.sr), vel: n(b.vel) }; });
  d.refBlocks = refA ? (refA.blocks || []).map(function (b) { return { from: b.from, to: b.to, time: n(b.time) }; }) : [];
  d.cleanVels = analysis.rows.filter(function (r) { return r.isFree && n(r.vel) !== null; }).map(function (r) { return { d: r.d, from: r.prevD, vel: r.vel }; });
  d.points = swimQuickPoints_(race, d);
  return d;
}

/**
 * Τα βασικά σημεία του αγώνα σε σύντομες προτάσεις: { tag, text, tone ('good' | 'bad' | 'neutral') }, με τη σειρά σημασίας
 * (PB, αναφορά, ρυθμός, εκκίνηση, στροφές, χεριές, ταχύτητα, τερματισμός, έλεγχοι).
 */
function swimQuickPoints_(race, d) {
  var out = [], U = d.U, T = swimFormatTime_;
  var add = function (tag, text, tone) { if (text) out.push({ tag: tag, text: text, tone: tone || 'neutral' }); };

  if (d.pb && d.pb.delta !== null) {
    if (d.pb.delta < -0.005) add(swimT_('PB'), swimTF_('Νέο ατομικό ρεκόρ: {0} από τον καλύτερο χρόνο του αθλητή ({1}, {2}).', swimQuickDelta_(d.pb.delta), T(d.pb.time), d.pb.label), 'good');
    else add(swimT_('PB'), swimTF_('{0} από τον καλύτερο χρόνο του αθλητή ({1}, {2}).', swimQuickDelta_(d.pb.delta), T(d.pb.time), d.pb.label), d.pb.delta > 0.005 ? 'bad' : 'neutral');
  }

  if (d.ref && d.ref.delta !== null) {
    // Το 50 με τη μεγαλύτερη διαφορά από την αναφορά.
    var worst = null;
    d.blocks.forEach(function (b) {
      var r = d.refBlocks.filter(function (x) { return x.from === b.from && x.to === b.to; })[0];
      if (!r || b.time === null || r.time === null) return;
      var diff = b.time - r.time;
      if (worst === null || Math.abs(diff) > Math.abs(worst.diff)) worst = { from: b.from, to: b.to, diff: diff };
    });
    var where = worst && d.blocks.length > 1 ? swimTF_(' · η μεγαλύτερη διαφορά στο {0}–{1}{2} ({3})', worst.from, worst.to, U, swimQuickDelta_(worst.diff)) : '';
    add(swimT_('ΑΝΑΦΟΡΑ'), swimTF_('{0} από την αναφορά {1} ({2}){3}.', swimQuickDelta_(d.ref.delta), d.ref.name, T(d.ref.time), where),
      d.ref.delta < -0.005 ? 'good' : 'neutral');
  }

  if (d.halves) {
    var diff = d.halves.second - d.halves.first;
    var text = swimTF_('1ο μισό {0} · 2ο μισό {1} ({2}).', T(d.halves.first), T(d.halves.second), swimQuickDelta_(diff));
    // Ανά 50 μετά το πρώτο (χωρίς την εκκίνηση): το πιο γρήγορο και το πιο αργό.
    var later = d.blocks.filter(function (b) { return b.from > 0 && b.time !== null; });
    if (later.length >= 2) {
      var fast = later.reduce(function (a, b) { return b.time < a.time ? b : a; });
      var slow = later.reduce(function (a, b) { return b.time > a.time ? b : a; });
      text += swimTF_(' Μετά το 1ο 50: πιο γρήγορο το {0}–{1} ({2}), πιο αργό το {3}–{4} ({5}).', fast.from, fast.to, T(fast.time), slow.from, slow.to, T(slow.time));
    }
    add(swimT_('ΡΥΘΜΟΣ'), text, 'neutral');
  }

  if (d.t15 !== null || d.reaction !== null) {
    var parts = [];
    if (d.t15 !== null) parts.push(swimTF_('15{0} σε {1}', U, T(d.t15)) + (d.ref15 !== null ? swimTF_(' ({0} από την αναφορά)', swimQuickDelta_(d.t15 - d.ref15)) : ''));
    if (d.reaction !== null) parts.push(swimTF_('αντίδραση {0}', swimQuickFixed_(d.reaction, 2)));
    if (d.breakoutDist !== null) parts.push(swimTF_('έξοδος από το νερό στα {0}{1}', swimQuickFixed_(d.breakoutDist, 1), U));
    if (d.kicks !== null && d.kicks > 0) parts.push(swimTF_('{0} υποβρύχιοι δελφινισμοί', d.kicks));
    var startText = parts.join(' · ');
    add(swimT_('ΕΚΚΙΝΗΣΗ'), startText.charAt(0).toUpperCase() + startText.substring(1) + '.',
      d.ref15 !== null && d.t15 !== null ? (d.t15 - d.ref15 <= 0 ? 'good' : 'neutral') : 'neutral');
  }

  if (d.turnAvg !== null) {
    var turnText = d.turns.filter(function (t) { return t.inOut !== null; }).length > 1
      ? swimTF_('Στροφές (-5/+15): μέσος όρος {0} · η πιο γρήγορη στα {1}{2} ({3})', swimQuickFixed_(d.turnAvg, 2), d.turnBest.wall, U, swimQuickFixed_(d.turnBest.inOut, 2))
      : swimTF_('Στροφή (-5/+15) στα {0}{1}: {2}', d.turnBest.wall, U, swimQuickFixed_(d.turnAvg, 2));
    if (d.refTurnAvg !== null) turnText += swimTF_(' ({0} από την αναφορά)', swimQuickDelta_(d.turnAvg - d.refTurnAvg));
    add(swimT_('ΣΤΡΟΦΕΣ'), turnText + '.', d.refTurnAvg !== null && d.turnAvg - d.refTurnAvg <= 0 ? 'good' : 'neutral');
  }

  if (d.sr !== null || d.dps !== null) {
    var strokeText = [d.sr !== null ? swimTF_('Συχνότητα {0} κύκλοι/λεπτό', swimQuickFixed_(d.sr, 1)) : '',
      d.dps !== null ? swimTF_('μήκος χεριάς {0} m', swimQuickFixed_(d.dps, 2)) : ''].filter(function (t) { return t; }).join(' · ');
    var withSr = d.blocks.filter(function (b) { return b.sr !== null; });
    if (withSr.length >= 2) {
      var change = withSr[withSr.length - 1].sr - withSr[0].sr;
      if (Math.abs(change) >= 1) strokeText += swimTF_(' · στο τελευταίο 50 η συχνότητα {0} κατά {1}', change > 0 ? swimT_('ανέβηκε') : swimT_('έπεσε'), swimQuickFixed_(Math.abs(change), 1));
    }
    add(swimT_('ΧΕΡΙΕΣ'), strokeText.charAt(0).toUpperCase() + strokeText.substring(1) + '.', 'neutral');
  }

  if (d.cleanVels.length >= 2) {
    var best = d.cleanVels.reduce(function (a, b) { return b.vel > a.vel ? b : a; });
    var lastV = d.cleanVels[d.cleanVels.length - 1];
    var drop = best.vel > 0 ? (best.vel - lastV.vel) / best.vel : 0;
    if (drop >= 0.03 && lastV !== best)
      add(swimT_('ΤΑΧΥΤΗΤΑ'), swimTF_('Η ταχύτητα της καθαρής κολύμβησης έπεσε {0}% από το καλύτερο τμήμα ({1}–{2}{3}: {4} m/s) στο τελευταίο ({5}–{6}{3}: {7} m/s).',
        Math.round(drop * 100), best.from, best.d, U, swimQuickFixed_(best.vel, 2), lastV.from, lastV.d, swimQuickFixed_(lastV.vel, 2)), drop >= 0.08 ? 'bad' : 'neutral');
  }

  if (d.last5 !== null) {
    add(swimT_('ΤΕΡΜΑΤΙΣΜΟΣ'), swimTF_('Τελευταία 5{0}: {1}', U, swimQuickFixed_(d.last5, 2)) +
      (d.lastStroke !== null ? swimTF_(' · από την τελευταία χεριά στον τοίχο {0}', swimQuickFixed_(d.lastStroke, 2)) : '') + '.', 'neutral');
  }

  if (d.warnings > 0) add(swimT_('ΕΛΕΓΧΟΣ'), swimTF_('{0} προειδοποιήσεις στα δεδομένα: δες την πλήρη αναφορά.', d.warnings), 'bad');
  return out;
}

/** Οι στήλες c1..c2 ενωμένες, με την ίδια μορφή σε όλες (φόντο). */
function swimQuickSet_(g, r, c1, c2, v, fmt) {
  g.set(r, c1, v, fmt);
  for (var c = c1 + 1; c <= c2; c++) g.set(r, c, '', { bg: fmt && fmt.bg ? fmt.bg : undefined });
  if (c2 > c1) g.merge(r, c1, r, c2);
}

/** Πλάτος (px) των στηλών c1..c2. */
function swimQuickWidth_(c1, c2) {
  var w = 0;
  for (var c = c1; c <= c2; c++) w += swimColumnWidth_(c);
  return w;
}

/**
 * Η πρώτη σελίδα σε γραμμές με ακριβές ύψος (px): { grid, heights: [ύψος της γραμμής 1, 2, …], cols, images, total, level }.
 * page: { height (px της σελίδας του PDF), cols (οι στήλες της αναφοράς) }. Αν το περιεχόμενο δεν χωρά, λιγότερα: σημεία,
 * μικρότερη εικόνα, τμήματα ανά 50 / 100, χωρίς τη λωρίδα εκκίνησης-στροφών-τερματισμού.
 */
function swimBuildQuickLook_(race, analysis, extras, kpi, page) {
  var H = page && page.height > 0 ? page.height : 0, W = page && page.cols > 0 ? page.cols : 15;
  if (!(H > 200) || W < 12) return null;
  var budget = H - SWIM_QUICK_SLACK;
  var modes = race.distance <= 100 ? ['cp', '50', null] : (race.distance <= 400 ? ['50', '100', null] : ['100', null]);
  var levels = [
    { points: 7, image: 300, mode: modes[0], strip: true },
    { points: 5, image: 230, mode: modes[0], strip: true },
    { points: 5, image: 230, mode: modes[1] || modes[0], strip: true },
    { points: 4, image: 200, mode: modes[1] || modes[0], strip: false },
    { points: 3, image: 0, mode: modes[1] || modes[0], strip: false },
    { points: 3, image: 0, mode: null, strip: false }
  ];
  for (var i = 0; i < levels.length; i++) {
    var q = swimQuickLayout_(race, analysis, extras, kpi, W, levels[i], 0);
    if (q.total <= budget) {
      // Το υπόλοιπο της σελίδας κενό πριν από την τελευταία γραμμή (στο κάτω μέρος της σελίδας): η αναφορά αρχίζει στην επόμενη.
      var rest = Math.floor(budget - q.total);
      if (rest >= 1) q = swimQuickLayout_(race, analysis, extras, kpi, W, levels[i], rest);
      q.level = i;
      return q;
    }
  }
  return null;
}

function swimQuickLayout_(race, analysis, extras, kpi, W, level, spacer) {
  var g = new SwimGrid_(), heights = [], images = [], C = SWIM_COLORS, Q = SWIM_QUICK_COLORS;
  var d = swimQuickLookData_(race, analysis, extras, kpi, level.mode || undefined);
  var r = 0;
  var row = function (h) { r++; heights.push(h); return r; };
  var blank = function (h, bg) { var x = row(h); if (bg) for (var c = 1; c <= W; c++) g.set(x, c, '', { bg: bg }); return x; };
  var T = swimFormatTime_;
  var hv = swimHeaderValues_(race);

  // ---- Τίτλος (όπως της αναφοράς).
  var r1 = row(48);
  swimQuickSet_(g, r1, 1, 9, 'SWIMMING RACE ANALYSIS', { bold: true, size: 18, color: C.titleText, bg: C.title });
  swimQuickSet_(g, r1, 10, W, swimT_('ΜΕ ΜΙΑ ΜΑΤΙΑ'), { bold: true, size: 13, color: C.accent, bg: C.title, align: 'right' });
  var r2 = row(26);
  swimQuickSet_(g, r2, 1, 9, swimT_('Κ.Ο.Ε. · Ανάλυση αγώνα με Kinovea'), { size: 9, color: C.subText, bg: C.title });
  swimQuickSet_(g, r2, 10, W, swimT_('Η πλήρης ανάλυση ακολουθεί στις επόμενες σελίδες'), { size: 9, color: C.subText, bg: C.title, align: 'right' });
  blank(5, C.accent);
  blank(10);

  // ---- Αθλητής και αγώνας.
  var r5 = row(38);
  swimQuickSet_(g, r5, 1, 8, hv.athlete || swimT_('Αγώνας'), { bold: true, size: 18, color: C.title, fmt: '@' });
  swimQuickSet_(g, r5, 9, W, hv.eventLine, { bold: true, size: 13, color: C.title, align: 'right', fmt: '@' });
  var r6 = row(24);
  swimQuickSet_(g, r6, 1, 8, hv.clubLine, { size: 10, color: C.dim, fmt: '@' });
  swimQuickSet_(g, r6, 9, W, hv.compLine, { size: 10, color: C.dim, align: 'right', fmt: '@' });
  blank(14);

  // ---- Ο χρόνος και οι συγκρίσεις.
  var tone = function (delta) { return delta === null ? C.title : (delta < -0.005 ? Q.good : (delta > 0.005 ? Q.bad : C.title)); };
  var heroes = [
    { c1: 1, c2: 4, cap: swimT_('ΤΕΛΙΚΟΣ ΧΡΟΝΟΣ'), val: d.dq ? 'DQ' : (d.ft !== null ? T(d.ft) : '–'),
      sub: d.avgVel !== null ? swimTF_('μέση ταχύτητα {0} m/s', swimQuickFixed_(d.avgVel, 2)) : '', color: C.title, bg: C.kpi },
    { c1: 5, c2: 8, cap: swimT_('ΣΕ ΣΧΕΣΗ ΜΕ ΤΟ PB'), val: d.pb && d.pb.delta !== null ? swimQuickDelta_(d.pb.delta) : '–',
      sub: d.pb ? d.pb.label + (d.pb.time !== null ? ' · ' + T(d.pb.time) : '') : swimT_('χωρίς προηγούμενο αγώνα'), color: tone(d.pb ? d.pb.delta : null), bg: C.kpi },
    { c1: 9, c2: 12, cap: swimT_('ΣΕ ΣΧΕΣΗ ΜΕ ΤΗΝ ΑΝΑΦΟΡΑ'), val: d.ref && d.ref.delta !== null ? swimQuickDelta_(d.ref.delta) : '–',
      sub: d.ref ? d.ref.name + (d.ref.time !== null ? ' · ' + T(d.ref.time) : '') : swimT_('χωρίς αναφορά'), color: tone(d.ref ? d.ref.delta : null), bg: C.kpi },
    d.halves
      ? { c1: 13, c2: W, cap: swimTF_('1Ο / 2Ο ΜΙΣΟ ({0}{1})', d.halves.at, d.U), val: T(d.halves.first) + ' / ' + T(d.halves.second),
        sub: swimTF_('διαφορά {0}', swimQuickDelta_(d.halves.second - d.halves.first)), color: C.title, bg: C.kpi, small: true }
      : { c1: 13, c2: W, cap: swimT_('ΧΕΡΙΕΣ'), val: d.strokes !== null ? swimQuickFixed_(d.strokes, d.strokes % 1 ? 1 : 0) : '–',
        sub: swimT_('σύνολο του αγώνα'), color: C.title, bg: C.kpi }
  ];
  var h1 = row(22), h2 = row(54), h3 = row(36);
  heroes.forEach(function (h, i) {
    var bg = i === 0 ? '#E8F1FB' : h.bg;
    swimQuickSet_(g, h1, h.c1, h.c2, h.cap, { bold: true, size: 8, color: C.dim, bg: bg, align: 'center' });
    swimQuickSet_(g, h2, h.c1, h.c2, h.val, { bold: true, size: h.small ? 17 : (i === 0 ? 30 : 24), color: h.color, bg: bg, align: 'center', fmt: '@' });
    swimQuickSet_(g, h3, h.c1, h.c2, h.sub, { size: 8, color: C.dim, bg: bg, align: 'center', wrap: true, fmt: '@' });
  });
  blank(14);

  // ---- Δείκτες.
  // 2.9.5: κάτω από τον μέσο όρο του ΔΑΚ ο ΔΑΚ κάθε 50άρι, όταν χωράει σε μία γραμμή του πλακιδίου (π.χ. τα δύο 50άρια των 100).
  var dak50 = d.dak50.length >= 2 && d.dak50.some(function (x) { return x.v !== null; }) ? swimDak50Text_(d.dak50) : '';
  if (dak50 && swimTextLines_(dak50, swimQuickWidth_(9, 10) - 5, swimFontSize_(8) * 4 / 3, false) > 1) dak50 = '';
  var tiles = [
    { cap: swimT_('ΑΝΤΙΔΡΑΣΗ'), val: swimQuickFixed_(d.reaction, 2), sub: 'sec' },
    { cap: swimTF_('ΕΚΚΙΝΗΣΗ 0-15{0}', d.U.replace('.', '').toUpperCase()), val: d.t15 !== null ? T(d.t15) : '', sub: swimT_('από το σήμα') },
    { cap: swimT_('ΣΥΧΝΟΤΗΤΑ'), val: swimQuickFixed_(d.sr, 1), sub: swimT_('κύκλοι/λεπτό') },
    { cap: swimT_('ΜΗΚΟΣ ΧΕΡΙΑΣ'), val: swimQuickFixed_(d.dps, 2), sub: swimT_('m/χεριά') },
    { cap: swimT_(dak50 ? 'ΔΑΚ (μ.ο.)' : 'ΔΑΚ'), val: swimQuickFixed_(d.dak, 2), sub: dak50 || swimT_('μ.ο. 50άρι'), accent: true },
    { cap: swimT_('ΣΤΡΟΦΕΣ -5/+15'), val: swimQuickFixed_(d.turnAvg, 2), sub: swimT_('μέσος όρος (sec)') },
    { cap: swimTF_('ΤΕΛΕΥΤΑΙΑ 5{0}', d.U.replace('.', '').toUpperCase()), val: swimQuickFixed_(d.last5, 2), sub: 'sec' }
  ];
  var k1 = row(20), k2 = row(38), k3 = row(22);
  tiles.forEach(function (t, i) {
    var c1 = 1 + 2 * i, c2 = i === tiles.length - 1 ? W : c1 + 1;
    var bg = t.accent ? C.kpiDak : C.kpi;
    swimQuickSet_(g, k1, c1, c2, t.cap, { bold: true, size: 8, color: C.dim, bg: bg, align: 'center' });
    swimQuickSet_(g, k2, c1, c2, t.val || '–', { bold: true, size: 18, color: t.accent ? C.dakText : C.title, bg: bg, align: 'center', fmt: '@' });
    swimQuickSet_(g, k3, c1, c2, t.sub, { size: 8, color: C.dim, bg: bg, align: 'center', fmt: '@' });
  });
  blank(16);

  // ---- Βασικά σημεία (αριστερά) και το καρέ του τερματισμού (δεξιά).
  var frame = race.finalFrame && race.finalFrame.fileId && level.image > 0 ? race.finalFrame.fileId : '';
  var textEnd = frame ? 8 : W;
  var sec = row(28);
  swimQuickSet_(g, sec, 1, textEnd, swimT_('Βασικά σημεία'), { bg: C.section, color: C.sectionText, bold: true, size: 11 });
  if (frame) swimQuickSet_(g, sec, textEnd + 1, W, swimT_('Ο τερματισμός'), { bg: C.section, color: C.sectionText, bold: true, size: 11 });
  var points = d.points.slice(0, level.points);
  var pointsTop = r + 1, used = 0;
  var textWidth = swimQuickWidth_(2, textEnd);
  points.forEach(function (p) {
    var h = Math.max(34, swimTextHeight_(p.text, textWidth, 10, false) + 8);
    var x = row(h);
    used += h;
    var bg = p.tone === 'good' ? Q.good : (p.tone === 'bad' ? Q.bad : Q.neutral);
    g.set(x, 1, p.tag, { bold: true, size: 8, color: '#FFFFFF', bg: bg, align: 'center', wrap: true, fmt: '@' });
    swimQuickSet_(g, x, 2, textEnd, p.text, { size: 10, color: C.text, wrap: true, fmt: '@', bg: p.tone === 'good' ? Q.goodBg : (p.tone === 'bad' ? Q.badBg : Q.soft) });
  });
  if (!points.length) {
    var none = row(30);
    used += 30;
    swimQuickSet_(g, none, 1, textEnd, swimT_('Δεν υπάρχουν ακόμη αρκετά δεδομένα για τα βασικά σημεία.'), { size: 10, color: C.dim, fmt: '@' });
  }
  if (frame) {
    var boxW = swimQuickWidth_(textEnd + 1, W) - 12;
    var boxH = Math.min(level.image, Math.round(boxW * 9 / 16));
    if (used < boxH + 8) {
      // Χώρος για την εικόνα κάτω από τα σημεία.
      var extra = boxH + 8 - used;
      while (extra > 0) { var hh = Math.min(30, extra); row(hh); extra -= hh; }
    }
    images.push({ fileId: frame, row: pointsTop, col: textEnd + 1, offsetX: 6, offsetY: 4, width: boxW, height: boxH, fit: true,
      x: swimQuickWidth_(1, textEnd) + 6 });
  }
  blank(16);

  // ---- Χρόνοι ανά τμήμα / 50 / 100 με μπάρες ταχύτητας.
  if (level.mode && d.segments.length) {
    var title = d.mode === 'cp' ? swimT_('Χρόνοι ανά σημείο ελέγχου') : (d.mode === '50' ? swimTF_('Χρόνοι ανά 50{0}', d.U) : swimTF_('Χρόνοι ανά 100{0}', d.U));
    var st = row(28);
    swimQuickSet_(g, st, 1, W, title, { bg: C.section, color: C.sectionText, bold: true, size: 11 });
    var head = row(34);
    var hfmt = { bg: C.header, color: C.headerText, bold: true, wrap: true, align: 'center', size: 8 };
    var cols = [
      [1, 1, swimT_('Τμήμα')], [2, 2, swimT_('Χρόνος')], [3, 3, swimT_('Ενδιάμεσος')], [4, 4, 'm/s'], [5, 8, swimT_('Ταχύτητα')],
      [9, 9, swimT_('Χεριές')], [10, 10, swimT_('Συχνότητα')], [11, 11, swimT_('Μήκος χεριάς')], [12, 12, swimT_('Δ PB')],
      [13, 14, swimT_('Δ αναφοράς')], [15, W, d.mode === 'cp' ? (SWIM_DAK50 ? swimT_('ΔΑΚ 50άρι') : '') : swimT_('ΔΑΚ')]
    ];
    // 2.9.5: ο ΔΑΚ κάθε 50άρι στη γραμμή όπου τελειώνει (ανά σημείο ελέγχου) ή στη γραμμή του (ανά 50), όπως στο φύλλο.
    var dakAt = {};
    d.dak50.forEach(function (x) { dakAt[x.k * 50] = x.v; });
    var dakOf = function (s) {
      if (SWIM_DAK50 && d.mode !== '100' && dakAt[s.d] !== undefined) return dakAt[s.d];
      return d.mode === 'cp' ? null : s.dak;
    };
    cols.forEach(function (c) { swimQuickSet_(g, head, c[0], c[1], c[2], hfmt); });
    // Η κλίμακα των μπαρών από την καθαρή κολύμβηση (η εκκίνηση και οι στροφές, πιο γρήγορες, ως το τέλος της μπάρας).
    var vmax = 0, vmin = Infinity;
    var scaled = d.segments.filter(function (s) { return s.vel !== null && !s.phase; });
    if (scaled.length < 2) scaled = d.segments.filter(function (s) { return s.vel !== null; });
    scaled.forEach(function (s) { vmax = Math.max(vmax, s.vel); vmin = Math.min(vmin, s.vel); });
    var first = r + 1;
    d.segments.forEach(function (s, i) {
      var x = row(d.segments.length > 12 ? 24 : 28);
      var bg = i % 2 ? '#FFFFFF' : Q.soft;
      var cell = function (c1, c2, v, extra) {
        var f = { bg: bg, align: 'center', size: 10, color: C.text, fmt: '@' };
        if (extra) for (var key in extra) if (Object.prototype.hasOwnProperty.call(extra, key)) f[key] = extra[key];
        swimQuickSet_(g, x, c1, c2, v, f);
      };
      cell(1, 1, s.label, { bold: true, color: C.headerText, bg: s.phase ? C.phase : C.label });
      cell(2, 2, s.cum !== null ? T(s.cum) : '', { bold: true });
      cell(3, 3, s.split !== null ? swimQuickFixed_(s.split, 2) : '');
      cell(4, 4, swimQuickFixed_(s.vel, 2), s.phase ? { color: C.dim } : null);
      // Μπάρα ταχύτητας (SPARKLINE): από λίγο κάτω από την πιο αργή ως την πιο γρήγορη, οι συμβατικές (εκκίνηση, στροφή) αχνές.
      var lo = vmin < Infinity ? Math.max(0, vmin - 0.25 * (vmax - vmin) - 0.05) : 0;
      var spark = s.vel !== null && vmax > lo
        ? '=SPARKLINE({' + swimQuickFixed_(Math.max(0, Math.min(vmax, s.vel) - lo), 3) + '},{"charttype","bar";"max",' + swimQuickFixed_(vmax - lo, 3) + ';"color1","' + (s.phase ? '#B0BEC5' : Q.bar) + '"})'
        : '';
      cell(5, 8, spark, { fmt: undefined });
      cell(9, 9, s.strokes !== null ? swimQuickFixed_(s.strokes, s.strokes % 1 ? 1 : 0) : '');
      cell(10, 10, swimQuickFixed_(s.sr, 1));
      cell(11, 11, swimQuickFixed_(s.dps, 2));
      cell(12, 12, swimQuickDelta_(s.dPb), { bold: true, color: tone(s.dPb) });
      cell(13, 14, swimQuickDelta_(s.dRef), { bold: true, color: tone(s.dRef) });
      cell(15, W, swimQuickFixed_(dakOf(s), 2), { color: C.dakText, bold: true });
    });
    g.box(head, 1, r, W);
    blank(16);
  }

  // ---- Εκκίνηση, στροφές, τερματισμός.
  if (level.strip) {
    var panels = [
      { c1: 1, c2: 5, title: swimT_('Εκκίνηση'), lines: [
        [swimT_('Αντίδραση'), swimQuickFixed_(d.reaction, 2)],
        [swimTF_('Χρόνος 15{0}', d.U), d.t15 !== null ? T(d.t15) : ''],
        [swimT_('Έξοδος από το νερό'), [d.breakout !== null ? swimQuickFixed_(d.breakout, 2) + ' s' : '', d.breakoutDist !== null ? swimQuickFixed_(d.breakoutDist, 1) + ' ' + d.U : ''].filter(function (v) { return v; }).join(' · ')],
        [swimT_('Υποβρύχιοι δελφινισμοί'), d.kicks !== null ? String(d.kicks) : '']
      ] },
      { c1: 6, c2: 10, title: swimT_('Στροφές'), lines: swimQuickTurnLines_(d) },
      { c1: 11, c2: W, title: swimT_('Τερματισμός'), lines: [
        [swimTF_('Τελευταία 5{0}', d.U), swimQuickFixed_(d.last5, 2)],
        [swimTF_('Τελευταία 15{0}', d.U), swimQuickFixed_(d.last15, 2)],
        [swimT_('Τελευταία χεριά → τοίχος'), swimQuickFixed_(d.lastStroke, 2)],
        [swimT_('Χεριές του αγώνα'), d.strokes !== null ? swimQuickFixed_(d.strokes, d.strokes % 1 ? 1 : 0) : '']
      ] }
    ];
    var ph = row(28);
    panels.forEach(function (p) { swimQuickSet_(g, ph, p.c1, p.c2, p.title, { bg: C.section, color: C.sectionText, bold: true, size: 11 }); });
    var lines = Math.max.apply(null, panels.map(function (p) { return p.lines.length; }));
    var firstLine = r + 1;
    for (var li = 0; li < lines; li++) {
      var x = row(26);
      panels.forEach(function (p) {
        var line = p.lines[li] || ['', ''];
        var bg = li % 2 ? '#FFFFFF' : Q.soft;
        swimQuickSet_(g, x, p.c1, p.c2 - 2, line[0], { size: 10, color: C.dim, bg: bg, fmt: '@' });
        swimQuickSet_(g, x, p.c2 - 1, p.c2, line[0] ? (line[1] || '–') : '', { size: 10, bold: true, color: C.text, bg: bg, align: 'right', fmt: '@' });
      });
    }
    panels.forEach(function (p) { g.box(firstLine, p.c1, r, p.c2); });
    blank(12);
  }

  // ---- Τέλος της σελίδας.
  if (spacer > 0) blank(spacer);
  var f = row(22);
  swimQuickSet_(g, f, 1, W, swimTF_('Ακολουθεί η πλήρης ανάλυση του αγώνα · Swim Analysis {0}', SWIM_VERSION), { size: 8, color: C.dim, align: 'right', fmt: '@' });

  var total = heights.reduce(function (a, h) { return a + h; }, 0);
  return { grid: g, heights: heights, cols: W, images: images, total: total, data: d };
}

/** Οι γραμμές των στροφών: έως τρεις μία-μία, αλλιώς μέσος όρος, η πιο γρήγορη και η πιο αργή (-5/+15). */
function swimQuickTurnLines_(d) {
  var list = d.turns.filter(function (t) { return t.inOut !== null || t.turnTime !== null; });
  var U = d.U;
  if (!list.length) return [[swimT_('Χωρίς στροφές με χρόνους'), '']];
  if (list.length <= 3) {
    var lines = list.map(function (t) {
      return [swimTF_('Στα {0}{1} (-5/+15)', t.wall, U), [swimQuickFixed_(t.inOut, 2), t.turnTime !== null ? swimTF_('στροφή {0}', swimQuickFixed_(t.turnTime, 2)) : '']
        .filter(function (v) { return v; }).join(' · ')];
    });
    if (d.turnAvg !== null && list.length > 1) lines.push([swimT_('Μέσος όρος -5/+15'), swimQuickFixed_(d.turnAvg, 2)]);
    return lines;
  }
  var timed = list.filter(function (t) { return t.inOut !== null; });
  var slow = timed.length ? timed.reduce(function (a, t) { return t.inOut > a.inOut ? t : a; }) : null;
  return [
    [swimT_('Μέσος όρος -5/+15'), swimQuickFixed_(d.turnAvg, 2)],
    [swimT_('Η πιο γρήγορη'), d.turnBest ? swimTF_('{0} στα {1}{2}', swimQuickFixed_(d.turnBest.inOut, 2), d.turnBest.wall, U) : ''],
    [swimT_('Η πιο αργή'), slow ? swimTF_('{0} στα {1}{2}', swimQuickFixed_(slow.inOut, 2), slow.wall, U) : ''],
    [swimT_('Στροφές με χρόνο'), String(timed.length)]
  ];
}

/** Η πρώτη σελίδα στις γραμμές 1..n ενός φύλλου (μόλις μπήκαν): τιμές, μορφή, ενώσεις, πλαίσια, ύψη, εικόνες. */
function swimRenderQuickLook_(sheet, q) {
  var g = q.grid, n = q.heights.length, W = q.cols;
  var values = [], bgs = [], weights = [], colors = [], sizes = [], aligns = [], wraps = [], byFormat = {};
  for (var r = 1; r <= n; r++) {
    var vr = [], br = [], wr = [], cr = [], zr = [], ar = [], wp = [];
    for (var c = 1; c <= W; c++) {
      var cell = g.get(r, c) || {};
      vr.push(cell.v === undefined || cell.v === null ? '' : cell.v);
      if (cell.fmt) (byFormat[cell.fmt] = byFormat[cell.fmt] || []).push(swimA1_(r, c));
      br.push(cell.bg || '#ffffff');
      wr.push(cell.bold ? 'bold' : 'normal');
      cr.push(cell.color || SWIM_COLORS.text);
      zr.push(swimFontSize_(cell.size));
      ar.push(cell.align || 'left');
      wp.push(!!cell.wrap);
    }
    values.push(vr); bgs.push(br); weights.push(wr); colors.push(cr); sizes.push(zr); aligns.push(ar); wraps.push(wp);
  }
  var range = sheet.getRange(1, 1, n, W);
  range.breakApart();
  range.setBorder(false, false, false, false, false, false);
  Object.keys(byFormat).forEach(function (fmt) { sheet.getRangeList(byFormat[fmt]).setNumberFormat(fmt); });
  range.setFontFamily(SWIM_FONT);
  range.setValues(values);
  range.setBackgrounds(bgs);
  range.setFontWeights(weights);
  range.setFontStyles(values.map(function (vr) { return vr.map(function () { return 'normal'; }); }));
  range.setFontColors(colors);
  range.setFontSizes(sizes);
  range.setHorizontalAlignments(aligns);
  range.setWraps(wraps);
  range.setVerticalAlignment('middle');
  g.merges.forEach(function (m) { sheet.getRange(m[0], m[1], m[2] - m[0] + 1, m[3] - m[1] + 1).merge(); });
  g.boxes.forEach(function (b) {
    sheet.getRange(b[0], b[1], b[2] - b[0] + 1, b[3] - b[1] + 1).setBorder(true, true, true, true, true, true, SWIM_COLORS.border, SpreadsheetApp.BorderStyle.SOLID);
  });
  var from = 1;
  for (var i = 2; i <= n + 1; i++) {
    if (i <= n && q.heights[i - 1] === q.heights[from - 1]) continue;
    swimForceRowHeights_(sheet, from, i - from, q.heights[from - 1]);
    from = i;
  }
  (q.images || []).forEach(function (img) {
    try {
      var blob = DriveApp.getFileById(img.fileId).getBlob();
      var im = sheet.insertImage(blob, img.col, img.row, img.offsetX, img.offsetY);
      var w = img.width, h = img.height;
      var w0 = swimImageSize_(im, 'Width'), h0 = swimImageSize_(im, 'Height');
      if (img.fit && w0 > 0 && h0 > 0) {
        var s = Math.min(img.width / w0, img.height / h0);
        w = Math.max(1, Math.round(w0 * s));
        h = Math.max(1, Math.round(h0 * s));
        var at = swimColumnAt_(img.x + Math.round((img.width - w) / 2));
        im.setAnchorCell(sheet.getRange(img.row, at.col)).setAnchorCellXOffset(at.offset);
      }
      im.setWidth(w).setHeight(h);
    } catch (e) { /* η σελίδα χωρίς την εικόνα */ }
  });
}

/**
 * Αντίγραφο του φύλλου του αγώνα με την πρώτη σελίδα στην αρχή (γραμμές με ύψος μία σελίδα): οι εικόνες και τα γραφήματα
 * της αναφοράς κατεβαίνουν μαζί με τις γραμμές τους. Επιστρέφει { sheet, rows }.
 */
function swimQuickLookSheet_(ss, sheet, q) {
  var now = Date.now();
  ss.getSheets().forEach(function (s) {
    // Μόνο τα αντίγραφα που ξέμειναν (όνομα: το πρόθεμα και η ώρα τους), όχι άλλα φύλλα με όμοιο όνομα.
    var name = s.getName(), stamp = name.substring(SWIM_QUICK_PREFIX.length);
    if (name.indexOf(SWIM_QUICK_PREFIX) === 0 && /^\d+$/.test(stamp) && now - Number(stamp) > 10 * 60 * 1000) {
      try { ss.deleteSheet(s); } catch (e) { /* σε χρήση */ }
    }
  });
  var count = function (sh) {
    var c = { images: 0, charts: 0 };
    try { c.images = sh.getImages().length; } catch (e) { /* παλιότερο Apps Script */ }
    try { c.charts = sh.getCharts().length; } catch (e2) { /* τίποτα */ }
    return c;
  };
  var source = count(sheet);
  var copy = sheet.copyTo(ss);
  try {
    copy.setName(SWIM_QUICK_PREFIX + now);
    // Το αντίγραφο πρέπει να έχει όλες τις εικόνες και τα γραφήματα της αναφοράς· αλλιώς το PDF γίνεται όπως πριν.
    var copied = count(copy);
    if (copied.images !== source.images || copied.charts !== source.charts) throw new Error('copy without the images of the report');
    var n = q.heights.length;
    var images = [], charts = [];
    try { images = copy.getImages().map(function (im) { var a = im.getAnchorCell(); return { row: a.getRow(), col: a.getColumn() }; }); } catch (e) { images = []; }
    try { charts = copy.getCharts().map(function (ch) { var i = ch.getContainerInfo(); return { row: i.getAnchorRow(), col: i.getAnchorColumn(), x: i.getOffsetX(), y: i.getOffsetY() }; }); } catch (e2) { charts = []; }
    copy.insertRowsBefore(1, n);
    // Όσα δεν κατέβηκαν μόνα τους με τις γραμμές τους κατεβαίνουν εδώ (ξανά από το φύλλο, με την ίδια σειρά).
    try {
      var nowImages = copy.getImages();
      if (nowImages.length === images.length) nowImages.forEach(function (im, i) {
        var a = im.getAnchorCell();
        if (a.getRow() === images[i].row && a.getColumn() === images[i].col) im.setAnchorCell(copy.getRange(images[i].row + n, images[i].col));
      });
    } catch (e3) { /* όπως είναι */ }
    try {
      var nowCharts = copy.getCharts();
      if (nowCharts.length === charts.length) nowCharts.forEach(function (ch, i) {
        var info = ch.getContainerInfo(), x = charts[i];
        if (info.getAnchorRow() === x.row && info.getAnchorColumn() === x.col)
          copy.updateChart(ch.modify().setPosition(x.row + n, x.col, x.x, x.y).build());
      });
    } catch (e4) { /* όπως είναι */ }
    // Όλα στη θέση τους, μία σελίδα πιο κάτω· αλλιώς το PDF όπως πριν.
    var moved = copy.getImages().map(function (im) { return im.getAnchorCell().getRow(); }).sort(function (a, b) { return a - b; });
    var want = images.map(function (x) { return x.row + n; }).sort(function (a, b) { return a - b; });
    if (JSON.stringify(moved) !== JSON.stringify(want)) throw new Error('the images of the report did not move');
    var movedCharts = copy.getCharts().map(function (ch) { return ch.getContainerInfo().getAnchorRow(); }).sort(function (a, b) { return a - b; });
    var wantCharts = charts.map(function (x) { return x.row + n; }).sort(function (a, b) { return a - b; });
    if (JSON.stringify(movedCharts) !== JSON.stringify(wantCharts)) throw new Error('the charts of the report did not move');
    swimRenderQuickLook_(copy, q);
    SpreadsheetApp.flush();
    return { sheet: copy, rows: n };
  } catch (e) {
    try { ss.deleteSheet(copy); } catch (e2) { /* σβήνεται την επόμενη φορά */ }
    throw e;
  }
}

/**
 * Μενού: πώς σχεδιάζει το Google τα γραφήματα στο PDF. Σε προσωρινό φύλλο, το ίδιο γράφημα με τις ρυθμίσεις των
 * reports, με απλούστερες και με τις ρυθμίσεις της έκδοσης 2.2.0· από το μέγεθος του PDF φαίνεται ποιο σχεδιάστηκε
 * ολόκληρο (ένα γράφημα μόνο με τον τίτλο του προσθέτει ελάχιστα).
 */
function swimChartsCheck() {
  var html = '<div style="font-family:Arial;font-size:13px;line-height:1.5">' + swimChartsCheckHtml_(swimReportsSpreadsheet_()) + '</div>';
  SpreadsheetApp.getUi().showModalDialog(HtmlService.createHtmlOutput(html).setWidth(520).setHeight(320), 'Γραφήματα στο PDF');
}

function swimChartsCheckHtml_(ss) {
  var sh = ss.insertSheet('_swim_chart_check_' + Date.now());
  var results = [];
  try {
    var data = [['Απόσταση', 'Αγώνας', 'PB'], [15, 2.33, 2.34], [25, 1.79, 1.76], [35, 1.74, 1.76], [45, 1.73, 1.72], [50, 1.72, 1.73]];
    sh.getRange(1, 1, data.length, 3).setValues(data);
    var spec = { type: 'line', title: 'Ταχύτητα ανά τμήμα (m/s)', ranges: ['A2:A6', 'B2:C6'], row: 8, col: 1, width: SWIM_PAIR.width,
      height: SWIM_CHART_H, colors: [SWIM_COLORS.athlete, SWIM_COLORS.pb], vTitle: 'm/s', hTitle: 'μ.', labels: ['Αγώνας', 'PB'] };
    var variants = [
      { name: 'απλό γράφημα (μέτρο σύγκρισης)', options: function () { return { title: spec.title, width: spec.width, height: spec.height }; } },
      { name: 'ρυθμίσεις των reports', options: function () { return swimChartOptions_(spec); } },
      { name: 'χωρίς άξονες και περιοχή', options: function () { var o = swimChartOptions_(spec); delete o.vAxes; delete o.hAxis; delete o.chartArea; return o; } },
      { name: 'περιοχή σε % (έκδοση 2.2.0)', options: function () { var o = swimChartOptions_(spec); o.chartArea = { left: 70, top: 48, width: '74%', height: '58%' }; return o; } }
    ];
    // 2.9.5: και με τις τιμές πάνω στη σειρά του αθλητή, όπως τα γραφήματα του Google στα report 2.5–2.9.4.
    if (SWIM_PDF_CHARTS) {
      variants.push({ name: 'τιμές πάνω στη σειρά (έκδοση 2.5–2.9.4)', options: function () {
        var labeled = JSON.parse(JSON.stringify(spec));
        labeled.dataLabels = true;
        return swimChartOptions_(labeled);
      } });
    }
    var area = { rows: spec.row + SWIM_CHART_ROWS, cols: 8 };
    SpreadsheetApp.flush();
    var empty = swimPdfFetch_(swimPdfUrl_(ss, sh, area)).getBytes().length;
    variants.forEach(function (v) {
      sh.getCharts().forEach(function (c) { sh.removeChart(c); });
      swimInsertChart_(sh, spec, v.options());
      SpreadsheetApp.flush();
      Utilities.sleep(3000);
      var res = { name: v.name, bytes: swimPdfFetch_(swimPdfUrl_(ss, sh, area)).getBytes().length - empty };
      // 2.9.5: και η εικόνα του γραφήματος (getAs), όπως μπαίνει στο PDF όταν δεν υπάρχει εικόνα του Kinovea.
      if (SWIM_PDF_CHARTS) {
        try { res.image = sh.getCharts()[0].getAs('image/png').getBytes().length; } catch (e3) { res.image = null; }
      }
      results.push(res);
    });
  } catch (e) {
    return '<p style="color:#c06e00">⚠ Ο έλεγχος δεν ολοκληρώθηκε: ' + String(e && e.message ? e.message : e) + '</p>';
  } finally {
    ss.deleteSheet(sh);
  }
  var ref = results[0].bytes;
  if (!(ref > 1500)) return '<p style="color:#c06e00">⚠ Ούτε το απλό γράφημα σχεδιάστηκε στο PDF (' + ref + ' bytes): το πρόβλημα δεν είναι οι ρυθμίσεις.</p>';
  var drawn = function (r) { return r.bytes >= 0.5 * ref; };
  // 2.9.5: η εικόνα του γραφήματος (getAs) με το μέγεθός της, απέναντι στην εικόνα του απλού γραφήματος.
  var refImage = results[0].image;
  var imageCell = function (r) {
    if (r.image === undefined) return '';
    if (r.image === null || !(refImage > 0)) return '<td>εικόνα: –</td>';
    return '<td style="text-align:right">εικόνα: ' + r.image + ' bytes ' + (r.image >= 0.5 * refImage ? '✔' : '✖') + '</td>';
  };
  var rows = results.map(function (r) {
    return '<tr><td>' + r.name + '</td><td style="text-align:right">' + r.bytes + ' bytes</td><td>' + (drawn(r) ? '✔ ολόκληρο' : '✖ μόνο ο τίτλος') + '</td>' +
      imageCell(r) + '</tr>';
  }).join('');
  var verdict = drawn(results[1])
    ? '<p style="color:#2e8b57">✔ Τα γραφήματα των reports σχεδιάζονται ολόκληρα στο PDF.</p>'
    : '<p style="color:#c06e00">⚠ Με τις ρυθμίσεις των reports το γράφημα δεν σχεδιάστηκε. Στείλε αυτόν τον πίνακα για διόρθωση.</p>';
  return verdict + '<table style="border-collapse:collapse" cellpadding="4">' + rows + '</table>';
}

/** Μενού: ξαναμετρά το ύψος της σελίδας του PDF και δείχνει το αποτέλεσμα. */
function swimPdfPagesCheck() {
  var ss = swimReportsSpreadsheet_();
  var props = swimProps_();
  var width = swimLayoutWidth_(15);
  [15, 17].forEach(function (nc) { props.deleteProperty('SWIM_PDF_PAGE_' + swimLayoutWidth_(nc)); });
  var h = swimPdfPageHeight_(ss, width);
  var stored = swimPdfStored_(width);
  var measured = !!(stored && stored.h > 0);
  var html = '<div style="font-family:Arial;font-size:13px;line-height:1.5">' +
    (measured
      ? '<p style="color:#2e8b57">✔ Μετρήθηκε: μία σελίδα A4 του PDF χωράει <b>' + h + ' px</b> του φύλλου (πλάτος ' + width + ' px).</p>'
      : '<p style="color:#c06e00">⚠ Η μέτρηση δεν έγινε' + (stored && stored.error ? ' (' + stored.error + ')' : '') +
        '. Χρησιμοποιείται ο υπολογισμός από τις διαστάσεις του A4: ' + h + ' px.</p>') +
    '<p>Τα reports στήνονται ξανά με αυτές τις αλλαγές σελίδας στην επόμενη ενημέρωσή τους από το Kinovea ' +
    'ή με «PDF της επιλεγμένης ανάλυσης».</p></div>';
  SpreadsheetApp.getUi().showModalDialog(HtmlService.createHtmlOutput(html).setWidth(480).setHeight(210), 'Σελίδες PDF');
}

// ---------------------------------------------------------------------------------------------
// Εικόνες: στιγμιότυπα της τεχνικής ανάλυσης και βιβλιοθήκη elite καρέ (στο Drive, φάκελος "Swim Race Reports")
// ---------------------------------------------------------------------------------------------

var SWIM_SNAPSHOT_FOLDER = 'Στιγμιότυπα';
var SWIM_ELITE_FOLDER = 'Elite καρέ';
var SWIM_ELITE_SHEET = 'Elite καρέ';
var SWIM_ELITE_HEADERS = ['Καρέ', 'Στυλ', 'Φάση', 'Στάδιο', 'Αθλητής', 'Πηγή', 'Σχόλιο', 'Εικόνα (Drive)', 'Προστέθηκε'];
var SWIM_ELITE = { label: 0, style: 1, kind: 2, stage: 3, athlete: 4, source: 5, comment: 6, file: 7, added: 8 };

/** Υποφάκελος του "Swim Race Reports" (δημιουργείται την πρώτη φορά). */
function swimSubFolder_(name) {
  var parent = swimReportFolder_();
  var it = parent.getFoldersByName(name);
  return it.hasNext() ? it.next() : parent.createFolder(name);
}

/** Όνομα αρχείου χωρίς χαρακτήρες που μπερδεύουν το Drive ή τα URL. */
function swimSafeName_(s) {
  var name = String(s === null || s === undefined ? '' : s).replace(/[\\\/:*?"<>|#%\[\]\r\n\t]+/g, '_').replace(/\s+/g, ' ').trim();
  return (name || 'image').substring(0, 100);
}

/** Το id ενός αρχείου του Drive από id ή σύνδεσμο (…/file/d/<id>/view ή …?id=<id>). */
function swimDriveId_(v) {
  var s = String(v === null || v === undefined ? '' : v).trim();
  var m = s.match(/\/d\/([\w-]{20,})/) || s.match(/[?&]id=([\w-]{20,})/);
  if (m) return m[1];
  return /^[\w-]{20,}$/.test(s) ? s : '';
}

/** Εικόνα σε base64 (JPEG ή PNG) → αρχείο στον φάκελο. Με το ίδιο όνομα, το παλιό αρχείο πηγαίνει στον κάδο. */
function swimSaveImageFile_(folder, name, mime, data) {
  if (!data) throw new Error(swimT_('Λείπει η εικόνα.'));
  var type = /png/i.test(String(mime || '')) ? 'image/png' : 'image/jpeg';
  var bytes = Utilities.base64Decode(String(data));
  // Τα Google Sheets δέχονται εικόνες έως 2 MB (και έως 1 εκατ. pixel) πάνω από τα κελιά.
  if (bytes.length > 2 * 1024 * 1024) throw new Error(swimT_('Η εικόνα είναι μεγαλύτερη από 2 MB.'));
  var fileName = swimSafeName_(name) + (type === 'image/png' ? '.png' : '.jpg');
  var old = folder.getFilesByName(fileName);
  while (old.hasNext()) old.next().setTrashed(true);
  return folder.createFile(Utilities.newBlob(bytes, type, fileName));
}

/** Στιγμιότυπο της τεχνικής ανάλυσης: { raceId, snapshotId, mime, data } → { fileId }. */
function swimSaveSnapshot_(req) {
  var raceId = String(req.raceId || ''), snapId = String(req.snapshotId || '');
  if (!raceId || !snapId) throw new Error(swimT_('Λείπει το id του αγώνα ή του στιγμιότυπου.'));
  var file = swimSaveImageFile_(swimSubFolder_(SWIM_SNAPSHOT_FOLDER), raceId + '_' + snapId, req.mime, req.data);
  return { ok: true, version: SWIM_VERSION, snapshotId: snapId, fileId: file.getId(), url: file.getUrl() };
}

/**
 * Πολλές εικόνες ενός αγώνα μαζί (π.χ. τα γραφήματα του report από το Kinovea): { raceId, images: [{ id, mime, data }] } →
 * { files: [{ id, fileId } ή { id, error }] }. Κάθε εικόνα αντικαθιστά την προηγούμενη με το ίδιο id.
 */
function swimSaveImages_(req) {
  var raceId = String(req.raceId || '');
  var list = Array.isArray(req.images) ? req.images : [];
  if (!raceId || !list.length) throw new Error(swimT_('Λείπει το id του αγώνα ή οι εικόνες.'));
  if (list.length > 30) throw new Error(swimT_('Έως 30 εικόνες σε κάθε αποστολή.'));
  var folder = swimSubFolder_(SWIM_SNAPSHOT_FOLDER);
  var files = list.map(function (x) {
    var id = String(x && x.id || '');
    if (!/^[\w-]{1,60}$/.test(id)) return { id: id, error: swimT_('μη έγκυρο id εικόνας') };
    try {
      return { id: id, fileId: swimSaveImageFile_(folder, raceId + '_' + id, x.mime, x.data).getId() };
    } catch (e) {
      return { id: id, error: String(e && e.message ? e.message : e) };
    }
  });
  return { ok: true, version: SWIM_VERSION, files: files };
}

function swimEliteSheet_(ss) {
  var sh = ss.getSheetByName(SWIM_ELITE_SHEET);
  if (sh) return sh;
  sh = ss.insertSheet(SWIM_ELITE_SHEET);
  sh.getRange(1, 1, 1, SWIM_ELITE_HEADERS.length).setValues([SWIM_ELITE_HEADERS]).setFontWeight('bold').setBackground(SWIM_COLORS.header);
  sh.getRange(1, 1).setNote('Σύντομος τίτλος, π.χ. "Pan Zhanle – είσοδος στο νερό".');
  sh.getRange(1, 2).setNote('fs, bk, br, fly (ή ελεύθερο, ύπτιο, πρόσθιο, πεταλούδα).');
  sh.getRange(1, 3).setNote('Εκκίνηση ή Στροφή.');
  sh.getRange(1, 4).setNote('Το στάδιο της κίνησης, π.χ. Θέση ετοιμότητας, Αποδέσμευση, Πτήση, Είσοδος, Υποβρύχια, Έξοδος, Προσέγγιση, Περιστροφή, Επαφή, Αποκόλληση.');
  sh.getRange(1, 8).setNote('Η εικόνα στο Drive: σύνδεσμος (https://drive.google.com/file/d/…) ή id. Συμπληρώνεται μόνο του όταν το καρέ αποθηκεύεται από το Kinovea. ' +
    'Για δική σου εικόνα: ανέβασέ την στον φάκελο "' + SWIM_REPORT_FOLDER + '/' + SWIM_ELITE_FOLDER + '" και επικόλλησε εδώ τον σύνδεσμό της.');
  sh.setFrozenRows(1);
  [240, 60, 90, 160, 160, 200, 300, 330, 100].forEach(function (w, i) { sh.setColumnWidth(i + 1, w); });
  return sh;
}

function swimEliteKind_(v) {
  var s = swimNormText_(v);
  return s.indexOf('ΣΤΡΟΦ') === 0 || s.indexOf('TURN') === 0 ? 'turn' : 'start';
}

function swimEliteFrameFromRow_(row) {
  var fileId = swimDriveId_(row[SWIM_ELITE.file]);
  var label = String(row[SWIM_ELITE.label] === null || row[SWIM_ELITE.label] === undefined ? '' : row[SWIM_ELITE.label]).trim();
  if (!fileId || !label) return null;
  return {
    id: fileId, fileId: fileId, label: label, style: swimStyleCode_(row[SWIM_ELITE.style]) || '', kind: swimEliteKind_(row[SWIM_ELITE.kind]),
    stage: String(row[SWIM_ELITE.stage] || '').trim(), athlete: String(row[SWIM_ELITE.athlete] || '').trim(),
    source: String(row[SWIM_ELITE.source] || '').trim(), comment: String(row[SWIM_ELITE.comment] || '').trim()
  };
}

/** Η βιβλιοθήκη elite καρέ: [{ id, fileId, label, style, kind ('start'|'turn'), stage, athlete, source, comment }]. */
function swimEliteFrames_() {
  var sh = swimEliteSheet_(swimReportsSpreadsheet_());
  var last = sh.getLastRow();
  if (last < 2) return [];
  return sh.getRange(2, 1, last - 1, SWIM_ELITE_HEADERS.length).getValues()
    .map(swimEliteFrameFromRow_).filter(function (f) { return f; });
}

/** Νέο elite καρέ από το Kinovea: { frame: {label, style, kind, stage, athlete, source, comment}, mime, data }. */
function swimSaveEliteFrame_(req) {
  var f = req.frame || {};
  var label = String(f.label || '').trim();
  if (!label) throw new Error(swimT_('Το elite καρέ χρειάζεται τίτλο.'));
  var file = swimSaveImageFile_(swimSubFolder_(SWIM_ELITE_FOLDER), 'elite ' + Utilities.getUuid().substring(0, 8) + ' ' + label, req.mime, req.data);
  var row = [label, swimStyleCode_(f.style) || String(f.style || ''), f.kind === 'turn' ? 'Στροφή' : 'Εκκίνηση', String(f.stage || ''),
    String(f.athlete || ''), String(f.source || ''), String(f.comment || ''), 'https://drive.google.com/file/d/' + file.getId() + '/view',
    Utilities.formatDate(new Date(), Session.getScriptTimeZone(), 'dd/MM/yyyy')];
  var sh = swimEliteSheet_(swimReportsSpreadsheet_());
  var r = sh.getLastRow() + 1;
  if (r > sh.getMaxRows()) sh.insertRowsAfter(sh.getMaxRows(), r - sh.getMaxRows());
  sh.getRange(r, 1, 1, row.length).setValues([row]);
  return { ok: true, version: SWIM_VERSION, frame: swimEliteFrameFromRow_(row) };
}

/** Μια εικόνα για προεπισκόπηση στο Kinovea, μόνο από τα στιγμιότυπα ή τη βιβλιοθήκη elite καρέ. */
function swimGetImage_(req) {
  var id = swimDriveId_(req.fileId);
  if (!id) throw new Error(swimT_('Λείπει το id της εικόνας.'));
  var file = DriveApp.getFileById(id);
  if (!swimImageAllowed_(file, id)) throw new Error(swimT_('Η εικόνα δεν είναι στα στιγμιότυπα ή στη βιβλιοθήκη elite καρέ.'));
  var blob = file.getBlob();
  var bytes = blob.getBytes();
  if (bytes.length > 4 * 1024 * 1024) throw new Error(swimT_('Η εικόνα είναι πολύ μεγάλη για προεπισκόπηση (πάνω από 4 MB).'));
  return { ok: true, version: SWIM_VERSION, fileId: id, name: file.getName(), mime: blob.getContentType(), data: Utilities.base64Encode(bytes) };
}

function swimImageAllowed_(file, id) {
  var allowed = {};
  [SWIM_SNAPSHOT_FOLDER, SWIM_ELITE_FOLDER].forEach(function (name) {
    try { allowed[swimSubFolder_(name).getId()] = true; } catch (e) { /* χωρίς φάκελο */ }
  });
  var parents = file.getParents();
  while (parents.hasNext()) if (allowed[parents.next().getId()]) return true;
  return swimEliteFrames_().some(function (f) { return f.fileId === id; });
}

function swimOpenEliteFrames() {
  var ss = swimReportsSpreadsheet_();
  var sh = swimEliteSheet_(ss);
  var url = ss.getUrl() + '#gid=' + sh.getSheetId();
  var folder = '';
  try { folder = swimSubFolder_(SWIM_ELITE_FOLDER).getUrl(); } catch (e) { folder = ''; }
  var html = '<div style="font-family:Arial;font-size:13px;line-height:1.5">' +
    '<p><a href="' + url + '" target="_blank">Άνοιγμα του φύλλου "' + SWIM_ELITE_SHEET + '"</a>' +
    (folder ? ' · <a href="' + folder + '" target="_blank">φάκελος εικόνων</a>' : '') + '</p>' +
    '<p>Καρέ elite κολυμβητών για σύγκριση με τα στιγμιότυπα της τεχνικής ανάλυσης. Αποθηκεύονται από το Kinovea ' +
    '(Τεχνική ανάλυση → Elite καρέ → Αποθήκευση ως elite) ή με το χέρι: εικόνα στον φάκελο και σύνδεσμος στη στήλη "Εικόνα (Drive)".</p></div>';
  SpreadsheetApp.getUi().showModalDialog(HtmlService.createHtmlOutput(html).setWidth(480).setHeight(220), 'Elite καρέ');
}

function swimOpenBenchmarks() {
  var ss = swimReportsSpreadsheet_();
  var sh = swimBenchmarksSheet_(ss);
  var url = ss.getUrl() + '#gid=' + sh.getSheetId();
  var html = '<div style="font-family:Arial;font-size:13px;line-height:1.5">' +
    '<p><a href="' + url + '" target="_blank">Άνοιγμα του φύλλου "' + SWIM_BENCH_SHEET + '"</a></p>' +
    '<p>Πίνακες επιπέδων (Δεδομένα 2015–2024): εκκίνηση 0-15μ., στροφή τοίχος → 15μ. και στροφές της μικτής, ανά φύλο και αγώνισμα. ' +
    'Επικόλλησε τις γραμμές από το αρχείο επιπέδων: τα report δείχνουν σε ποιο επίπεδο είναι ο αθλητής.</p></div>';
  SpreadsheetApp.getUi().showModalDialog(HtmlService.createHtmlOutput(html).setWidth(480).setHeight(200), 'Επίπεδα');
}

// ---------------------------------------------------------------------------------------------
// Βιβλιοθήκη: οι αναλύσεις που έγιναν με το παλιό Excel (και τα PDF τους) ως αγώνες του συστήματος
//
// Το αρχείο "Βιβλιοθήκη αναλύσεων.json" (στον φάκελο "Swim Race Reports" του Drive) έχει έναν αγώνα ανά ανάλυση:
// χρόνους στα σημεία ελέγχου, χεριές ανά τμήμα και τιμές εκκίνησης / στροφών / τερματισμού όπως γράφτηκαν στο Excel,
// και ό,τι βρέθηκε στον έλεγχο (αντίγραφα φύλλων, διαφορετικές εκδόσεις, ύποπτοι χρόνοι).
//   1. "Φόρτωση για έλεγχο": φύλλο "Βιβλιοθήκη (έλεγχος)" με μία γραμμή ανά αγώνα και το ταίριασμα με το μητρώο.
//   2. Έλεγχος: μητρώο αθλητή, ενέργεια, διορθώσεις χρόνων (π.χ. "150=111.62"), elite αναφορά.
//   3. "Καταχώριση": οι επιλεγμένοι αγώνες μπαίνουν στο ευρετήριο και στο "Αναλύσεις Αγώνων", χωρίς φύλλο report
//      (το report φτιάχνεται όταν το ζητήσεις). Μετά φαίνονται στα PB του Kinovea και στις συγκρίσεις.
// ---------------------------------------------------------------------------------------------

var SWIM_LIBRARY_FILE = 'Βιβλιοθήκη αναλύσεων.json';
var SWIM_LIBRARY_SHEET = 'Βιβλιοθήκη (έλεγχος)';
var SWIM_LIB_HEADERS = ['Εισαγωγή', 'Ενέργεια', 'Κατάσταση', 'Αθλητής (αρχεία)', 'Αθλητής (μητρώο)', 'Μητρώο', 'Φύλο', 'Αγώνισμα',
  'Πισίνα', 'Διοργάνωση', 'Φάση', 'Ημερομηνία', 'Τελικός χρόνος', 'Elite αναφορά', 'Έλεγχος', 'Σημειώσεις', 'Πηγή',
  'Διορθώσεις χρόνων', 'Race ID'];
var SWIM_LIB = { on: 0, action: 1, status: 2, athlete: 3, regName: 4, regId: 5, gender: 6, event: 7, pool: 8, competition: 9,
  round: 10, date: 11, final: 12, elite: 13, check: 14, notes: 15, source: 16, fix: 17, id: 18 };
var SWIM_LIB_ACTIONS = ['Εισαγωγή', 'Χωρίς τους ύποπτους χρόνους', 'Παράλειψη'];
/** Στήλες που συμπληρώνει ο χρήστης: κρατιούνται όταν ξαναφορτώνεται το αρχείο. */
var SWIM_LIB_KEEP = ['on', 'action', 'regId', 'gender', 'competition', 'round', 'date', 'elite', 'fix'];

// ---- Ονόματα: ταίριασμα με το μητρώο (ίδιοι κανόνες με τον έλεγχο των αρχείων)

var SWIM_FIRST_NAMES = {
  'ΓΙΩΡΓΟΣ': 'ΓΕΩΡΓΙΟΣ', 'ΝΙΚΟΣ': 'ΝΙΚΟΛΑΟΣ', 'ΝΙΚΟΛΑΣ': 'ΝΙΚΟΛΑΟΣ', 'ΝΙΚΟΛΟΣ': 'ΝΙΚΟΛΑΟΣ', 'ΔΗΜΗΤΡΗΣ': 'ΔΗΜΗΤΡΙΟΣ', 'ΒΑΣΙΛΗΣ': 'ΒΑΣΙΛΕΙΟΣ',
  'ΒΑΓΓΕΛΗΣ': 'ΕΥΑΓΓΕΛΟΣ', 'ΑΝΤΩΝΗΣ': 'ΑΝΤΩΝΙΟΣ', 'ΜΙΧΑΛΗΣ': 'ΜΙΧΑΗΛ', 'ΓΙΑΝΝΗΣ': 'ΙΩΑΝΝΗΣ', 'ΚΩΣΤΑΣ': 'ΚΩΝΣΤΑΝΤΙΝΟΣ', 'ΘΑΝΑΣΗΣ': 'ΑΘΑΝΑΣΙΟΣ',
  'ΠΑΝΟΣ': 'ΠΑΝΑΓΙΩΤΗΣ', 'ΣΤΑΘΗΣ': 'ΕΥΣΤΑΘΙΟΣ', 'ΜΑΝΟΛΗΣ': 'ΕΜΜΑΝΟΥΗΛ', 'ΜΑΝΟΣ': 'ΕΜΜΑΝΟΥΗΛ', 'ΧΑΡΗΣ': 'ΧΑΡΑΛΑΜΠΟΣ', 'ΤΑΚΗΣ': 'ΠΑΝΑΓΙΩΤΗΣ',
  'ΝΙΚΟΛΕΤΤΑ': 'ΝΙΚΟΛΕΤΑ', 'ΜΑΡΙΛΕΙΑ': 'ΜΑΡΙΛΙΑ', 'ΚΩΝΣΤΑΝΤΙΝΑ': 'ΚΩΝΣΤΑΝΤΙΝΑ', 'ILIA': 'ILYA'
};

/** Λέξεις ενός ονόματος: κεφαλαία χωρίς τόνους, "Κων/νος" → ΚΩΝΣΤΑΝΤΙΝΟΣ, υποκοριστικά → επίσημο όνομα. */
function swimNameTokens_(name) {
  var s = swimNormText_(String(name || '').replace(/\([^)]*\)?/g, ' '));
  s = s.replace(/ΚΩΝ\s*\/\s*ΝΟΣ/g, 'ΚΩΝΣΤΑΝΤΙΝΟΣ').replace(/ΚΩΝ\s*\/\s*ΝΑ/g, 'ΚΩΝΣΤΑΝΤΙΝΑ');
  return s.replace(/[^A-ZΑ-Ω0-9]+/g, ' ').trim().split(' ').filter(function (t) { return t; })
    .map(function (t) { return SWIM_FIRST_NAMES[t] || t; });
}

/**
 * 2: ίδιες λέξεις (σε οποιαδήποτε σειρά), 1: συμβατά (το ένα όνομα έχει λιγότερα μικρά ονόματα ή αρχικά, π.χ. "Παπαδοπούλου Μ."),
 * 0: διαφορετικά. Πάντα χρειάζεται τουλάχιστον μία κοινή ολόκληρη λέξη (το επώνυμο).
 */
function swimNameMatch_(a, b) {
  var ta = swimNameTokens_(a), tb = swimNameTokens_(b);
  if (!ta.length || !tb.length) return 0;
  if (ta.slice().sort().join(' ') === tb.slice().sort().join(' ')) return 2;
  if (ta.length > tb.length) { var tmp = ta; ta = tb; tb = tmp; }
  var used = {}, full = 0;
  for (var i = 0; i < ta.length; i++) {
    var x = ta[i], hit = -1, j;
    for (j = 0; j < tb.length; j++) if (!used[j] && tb[j] === x) { hit = j; if (x.length >= 4) full++; break; }
    if (hit < 0 && x.length <= 2) for (j = 0; j < tb.length; j++) if (!used[j] && tb[j].indexOf(x) === 0) { hit = j; break; }
    if (hit < 0) return 0;
    used[hit] = true;
  }
  return full >= 1 ? 1 : 0;
}

/** Ευρετήριο του μητρώου ανά λέξη (≥ 4 γράμματα), για γρήγορο ταίριασμα. */
function swimRegistryIndex_(athletes) {
  var byToken = {}, byId = {};
  athletes.forEach(function (a) {
    byId[String(a.id)] = a;
    swimNameTokens_(a.name).forEach(function (t) {
      if (t.length < 4) return;
      (byToken[t] = byToken[t] || []).push(a);
    });
  });
  return { byToken: byToken, byId: byId, athletes: athletes };
}

/** { athlete, quality: 2|1, candidates: [...] } για τα ονόματα ενός αγώνα της βιβλιοθήκης (και το φύλο, αν είναι γνωστό). */
function swimMatchRegistry_(names, gender, reg) {
  var seen = {}, cand = [];
  names.forEach(function (n) {
    swimNameTokens_(n).forEach(function (t) {
      (reg.byToken[t] || []).forEach(function (a) { if (!seen[a.id]) { seen[a.id] = true; cand.push(a); } });
    });
  });
  var g = swimGenderCode_(gender);
  var scored = [];
  cand.forEach(function (a) {
    var ag = swimGenderCode_(a.gender);
    if (g && ag && g !== ag) return;
    var q = 0;
    names.forEach(function (n) { q = Math.max(q, swimNameMatch_(n, a.name)); });
    if (q > 0) scored.push({ a: a, q: q });
  });
  if (!scored.length) return { athlete: null, quality: 0, candidates: [] };
  var best = Math.max.apply(null, scored.map(function (s) { return s.q; }));
  var top = scored.filter(function (s) { return s.q === best; }).map(function (s) { return s.a; });
  return { athlete: top.length === 1 ? top[0] : null, quality: best, candidates: top };
}

// ---- Το αρχείο της βιβλιοθήκης

/** Το αρχείο JSON: στον φάκελο "Swim Race Reports" (ή όπου δείχνει το SWIM_LIBRARY_FILE_ID). */
function swimLibraryFile_(askIfMissing) {
  var props = swimProps_();
  var id = props.getProperty('SWIM_LIBRARY_FILE_ID');
  if (id) {
    try { return DriveApp.getFileById(id); } catch (e) { /* σβήστηκε: ξαναψάχνουμε */ }
  }
  var it = swimReportFolder_().getFilesByName(SWIM_LIBRARY_FILE);
  if (it.hasNext()) {
    var f = it.next();
    props.setProperty('SWIM_LIBRARY_FILE_ID', f.getId());
    return f;
  }
  if (!askIfMissing) return null;
  var ui = SpreadsheetApp.getUi();
  var res = ui.prompt('Βιβλιοθήκη αναλύσεων',
    'Δεν βρέθηκε το "' + SWIM_LIBRARY_FILE + '" στον φάκελο "' + SWIM_REPORT_FOLDER + '" του Drive.\n' +
    'Ανέβασέ το εκεί και ξαναδοκίμασε, ή επικόλλησε εδώ τον σύνδεσμο του αρχείου στο Drive:', ui.ButtonSet.OK_CANCEL);
  if (res.getSelectedButton() !== ui.Button.OK) return null;
  var fid = swimDriveId_(res.getResponseText());
  if (!fid) throw new Error('Δεν αναγνωρίστηκε σύνδεσμος του Drive.');
  var file = DriveApp.getFileById(fid);
  props.setProperty('SWIM_LIBRARY_FILE_ID', file.getId());
  return file;
}

/** { meta, races, byId } από το αρχείο. */
function swimLibraryRead_(file) {
  var data = JSON.parse(file.getBlob().getDataAsString('UTF-8'));
  if (!data || data.app !== 'kinovea-swim-library' || !Array.isArray(data.races))
    throw new Error('Το αρχείο δεν είναι αρχείο βιβλιοθήκης αναλύσεων του Kinovea.');
  var byId = {};
  data.races.forEach(function (r) { byId[String(r.id)] = r; });
  return { meta: data.meta || {}, races: data.races, byId: byId };
}

/** "100 fly", "4x100 medley relay", "100 fs σκέλος". */
function swimLibraryEventText_(r) {
  return r.distanceLabel + ' ' + (SWIM_STYLE_LABELS[r.style] || r.style) + (r.relay ? ' relay' : '') + (r.relayLeg ? ' σκέλος' : '');
}

function swimLibrarySheet_(ss) {
  var sh = ss.getSheetByName(SWIM_LIBRARY_SHEET);
  if (sh) return sh;
  sh = ss.insertSheet(SWIM_LIBRARY_SHEET);
  sh.getRange(1, 1, 1, SWIM_LIB_HEADERS.length).setValues([SWIM_LIB_HEADERS]).setFontWeight('bold').setBackground(SWIM_COLORS.header);
  var notes = {};
  notes[SWIM_LIB.on] = 'Τσεκαρισμένο: ο αγώνας μπαίνει στη βιβλιοθήκη με την "Καταχώριση".';
  notes[SWIM_LIB.action] = 'Εισαγωγή όπως είναι, χωρίς τους χρόνους που βρέθηκαν ύποπτοι (στήλη Έλεγχος), ή παράλειψη.';
  notes[SWIM_LIB.regId] = 'Ο αριθμός μητρώου (unique_id) του αθλητή. Συμπληρώθηκε όπου το όνομα ταίριαξε με το μητρώο· ' +
    'άλλαξέ τον αν χρειάζεται (το όνομα του μητρώου μπαίνει στην καταχώριση). Κενό: ο αγώνας μπαίνει με το όνομα των αρχείων.';
  notes[SWIM_LIB.gender] = 'Α ή Γ. Από το μητρώο ή από το Excel.';
  notes[SWIM_LIB.elite] = 'Τσεκαρισμένο: μπαίνει και στο φύλλο "Elite αναφορές" για σύγκριση (προτείνεται για ξένους αθλητές).';
  notes[SWIM_LIB.check] = 'Ό,τι χρειάζεται έλεγχο πριν την καταχώριση.';
  notes[SWIM_LIB.fix] = 'Διόρθωση χρόνων πριν την καταχώριση: απόσταση=χρόνος, π.χ. "150=111.62; 185=139.04". Κενός χρόνος σβήνει το σημείο: "150=".';
  Object.keys(notes).forEach(function (c) { sh.getRange(1, Number(c) + 1).setNote(notes[c]); });
  sh.setFrozenRows(1);
  sh.setFrozenColumns(4);
  [70, 170, 120, 210, 210, 90, 50, 120, 60, 230, 110, 90, 80, 70, 420, 360, 300, 170, 120].forEach(function (w, i) { sh.setColumnWidth(i + 1, w); });
  return sh;
}

function swimLibraryFormatTime_(t) {
  return t === null || t === undefined || t === '' ? '' : swimFormatTime_(Number(t));
}

/** Μενού: φόρτωση του αρχείου στο φύλλο ελέγχου (κρατά ό,τι έχει ήδη συμπληρωθεί ανά Race ID). */
function swimLibraryLoad() {
  var ui = SpreadsheetApp.getUi();
  var file = swimLibraryFile_(true);
  if (!file) return;
  var res = swimLibraryLoad_(file);
  ui.alert('Βιβλιοθήκη αναλύσεων',
    res.total + ' αγώνες στο φύλλο "' + SWIM_LIBRARY_SHEET + '".\n' +
    '• ' + res.matched + ' ταίριαξαν με το μητρώο\n' +
    '• ' + res.review + ' χρειάζονται έλεγχο (πορτοκαλί, στήλη "Έλεγχος")\n' +
    '• ' + res.foreign + ' ξένοι αθλητές (προτείνονται ως elite αναφορές)\n' +
    (res.already ? '• ' + res.already + ' είναι ήδη στη βιβλιοθήκη (δεν είναι τσεκαρισμένοι)\n' : '') +
    '\nΈλεγξε και μετά: Swim Analysis → Βιβλιοθήκη παλιών αναλύσεων → 2. Καταχώριση των επιλεγμένων.', ui.ButtonSet.OK);
}

function swimLibraryLoad_(file) {
  var lib = swimLibraryRead_(file);
  var ss = swimDataSpreadsheet_();
  var sh = swimLibrarySheet_(ss);
  var reg = swimRegistryIndex_(swimAthletes_().athletes);
  var index = swimIndexSheet_(swimReportsSpreadsheet_());
  var inIndex = {};
  var lastIdx = index.getLastRow();
  if (lastIdx >= 2) index.getRange(2, 1, lastIdx - 1, 1).getValues().forEach(function (r) { inIndex[String(r[0])] = true; });

  // ό,τι έχει ήδη συμπληρωθεί
  var kept = {};
  var last = sh.getLastRow();
  if (last >= 2) {
    sh.getRange(2, 1, last - 1, SWIM_LIB_HEADERS.length).getValues().forEach(function (row) {
      var id = String(row[SWIM_LIB.id] || '');
      if (id) kept[id] = row;
    });
  }

  var out = [], st = { total: 0, matched: 0, review: 0, foreign: 0, already: 0 };
  lib.races.forEach(function (r) {
    // παραλλαγές του ίδιου ονόματος (π.χ. "Κων/νος", αρχικά)· όχι τα ονόματα άλλων αθλητών από αντιγραμμένες κεφαλίδες
    var names = [r.athlete].concat((r.athleteVariants || []).filter(function (v) { return swimNameMatch_(v, r.athlete) > 0; }));
    var m = r.foreign || r.team ? { athlete: null, quality: 0, candidates: [] } : swimMatchRegistry_(names, r.gender, reg);
    var regChecks = [];
    if (!r.foreign && !r.team) {
      if (!m.athlete && m.candidates.length > 1)
        regChecks.push('Πολλοί αθλητές του μητρώου ταιριάζουν: ' + m.candidates.slice(0, 6).map(function (a) {
          return a.name + ' (' + a.id + (a.birthYear ? ', ' + a.birthYear : '') + ')'; }).join(' / ') + ' — γράψε το σωστό Μητρώο.');
      else if (!m.athlete) regChecks.push('Δεν βρέθηκε στο μητρώο: μπαίνει με το όνομα των αρχείων (ή γράψε το Μητρώο).');
      else if (m.quality === 1) regChecks.push('Ταίριασμα με το μητρώο από παραλλαγή του ονόματος: ' + m.athlete.name + '. Έλεγξε ότι είναι ο σωστός αθλητής.');
    }
    var g = swimGenderCode_(m.athlete && m.athlete.gender ? m.athlete.gender : r.gender);
    var already = !!inIndex[String(r.id)];
    var dflt = r.action === 'skip' ? 'Παράλειψη' : (r.action === 'dropSuspect' ? 'Χωρίς τους ύποπτους χρόνους' : 'Εισαγωγή');
    var source = (r.sources && r.sources.chosen) ? (r.sources.chosen.path ? r.sources.chosen.path + ' › ' + r.sources.chosen.sheet : 'PDF: ' + r.sources.chosen.pdf) : '';
    if (r.sources && r.sources.pdfs && r.sources.pdfs.length && r.sources.chosen && r.sources.chosen.path) source += ' (+ PDF)';
    var row = [];
    row[SWIM_LIB.on] = !already && r.action !== 'skip';
    row[SWIM_LIB.action] = dflt;
    row[SWIM_LIB.athlete] = r.athlete;
    row[SWIM_LIB.regName] = m.athlete ? m.athlete.name : '';
    row[SWIM_LIB.regId] = m.athlete ? m.athlete.id : '';
    row[SWIM_LIB.gender] = g === 'F' ? 'Γ' : (g === 'M' ? 'Α' : '');
    row[SWIM_LIB.event] = swimLibraryEventText_(r);
    row[SWIM_LIB.pool] = (r.pool || 50) + 'μ.';
    row[SWIM_LIB.competition] = r.competition || '';
    row[SWIM_LIB.round] = r.round || '';
    row[SWIM_LIB.date] = r.date || '';
    row[SWIM_LIB.final] = swimLibraryFormatTime_(r.final);
    row[SWIM_LIB.elite] = !!r.foreign;
    row[SWIM_LIB.notes] = (r.notes || []).join('\n');
    row[SWIM_LIB.source] = source;
    row[SWIM_LIB.fix] = '';
    row[SWIM_LIB.id] = r.id;
    var prev = kept[String(r.id)];
    if (prev) SWIM_LIB_KEEP.forEach(function (k) { row[SWIM_LIB[k]] = prev[SWIM_LIB[k]]; });
    var regId = String(row[SWIM_LIB.regId] === null || row[SWIM_LIB.regId] === undefined ? '' : row[SWIM_LIB.regId]).trim();
    var chosen = regId ? reg.byId[regId] : null;
    row[SWIM_LIB.regName] = chosen ? chosen.name : '';

    // ό,τι πρέπει να ελεγχθεί (χωρίς όσα έλυσε ήδη ο χρήστης: άλλο Μητρώο που υπάρχει στο φύλλο αθλητών, Φύλο)
    var checks = (r.flags || []).slice();
    if (regId && !chosen) checks.push('Ο αριθμός μητρώου ' + regId + ' δεν υπάρχει στο φύλλο αθλητών.');
    else if (!(chosen && regId !== String(m.athlete ? m.athlete.id : ''))) checks = checks.concat(regChecks);
    if (!String(row[SWIM_LIB.gender] || '').trim() && (r.foreign || r.team))
      checks.push('Συμπλήρωσε το Φύλο (Α ή Γ): χρειάζεται για τη σύγκριση με τις elite αναφορές.');
    row[SWIM_LIB.check] = checks.join('\n');
    row[SWIM_LIB.status] = already ? 'Στη βιβλιοθήκη' : (checks.length ? 'Έλεγχος' : 'Έτοιμο');
    for (var c = 0; c < SWIM_LIB_HEADERS.length; c++) if (row[c] === undefined || row[c] === null) row[c] = '';
    out.push(row);
    st.total++;
    if (regId) st.matched++;
    if (checks.length && !already) st.review++;
    if (r.foreign) st.foreign++;
    if (already) st.already++;
  });

  if (last >= 2) sh.getRange(2, 1, last - 1, SWIM_LIB_HEADERS.length).clearContent();
  // λιγότεροι αγώνες από την προηγούμενη φόρτωση: χωρίς κουτάκια στις γραμμές που περισσεύουν
  if (last > out.length + 1) sh.getRange(out.length + 2, 1, last - out.length - 1, SWIM_LIB_HEADERS.length).clearDataValidations();
  if (out.length) {
    if (sh.getMaxRows() < out.length + 1) sh.insertRowsAfter(sh.getMaxRows(), out.length + 1 - sh.getMaxRows());
    // κείμενο: αλλιώς το Sheets κάνει το "28.06" ή το "8.5.2026" ημερομηνία
    [SWIM_LIB.regId, SWIM_LIB.event, SWIM_LIB.date, SWIM_LIB.final, SWIM_LIB.fix].forEach(function (c) {
      sh.getRange(2, c + 1, out.length, 1).setNumberFormat('@');
    });
    sh.getRange(2, 1, out.length, SWIM_LIB_HEADERS.length).setValues(out);
    sh.getRange(2, SWIM_LIB.on + 1, out.length, 1).setDataValidation(SpreadsheetApp.newDataValidation().requireCheckbox().build());
    sh.getRange(2, SWIM_LIB.elite + 1, out.length, 1).setDataValidation(SpreadsheetApp.newDataValidation().requireCheckbox().build());
    sh.getRange(2, SWIM_LIB.action + 1, out.length, 1).setDataValidation(
      SpreadsheetApp.newDataValidation().requireValueInList(SWIM_LIB_ACTIONS, true).build());
    sh.getRange(2, SWIM_LIB.check + 1, out.length, 2).setWrap(true);
    var all = sh.getRange(2, 1, out.length, SWIM_LIB_HEADERS.length);
    var col = swimColLetter_(SWIM_LIB.status + 1);
    sh.setConditionalFormatRules([
      SpreadsheetApp.newConditionalFormatRule().whenFormulaSatisfied('=$' + col + '2="Έλεγχος"').setBackground('#FFF1DC').setRanges([all]).build(),
      SpreadsheetApp.newConditionalFormatRule().whenFormulaSatisfied('=LEFT($' + col + '2,5)="Στη β"').setBackground('#EEF2F6').setFontColor('#5B6B7B').setRanges([all]).build(),
      SpreadsheetApp.newConditionalFormatRule().whenFormulaSatisfied('=LEFT($' + col + '2,6)="Καταχω"').setBackground('#E6F4EA').setRanges([all]).build()
    ]);
  }
  var props = swimProps_();
  props.setProperty('SWIM_LIBRARY_FILE_ID', file.getId());
  return st;
}

/** "150=111.62; 185=139.04; 95=" → { 150: 111.62, 185: 139.04, 95: null }. */
function swimLibraryFixes_(text) {
  var out = {};
  String(text || '').split(/[;\n]+/).forEach(function (part) {
    var m = part.trim().match(/^(\d+(?:[.,]\d+)?)\s*[=:]\s*(.*)$/);
    if (!m) return;
    var d = Number(m[1].replace(',', '.'));
    var v = m[2].trim();
    out[d] = v === '' ? null : swimRefTime_(v.replace(',', '.'), v);
  });
  return out;
}

/** Ο αγώνας (όπως τον στέλνει το Kinovea) από την εγγραφή της βιβλιοθήκης και τη γραμμή του φύλλου ελέγχου. */
function swimLibraryRace_(rec, row, reg) {
  var regId = String(row[SWIM_LIB.regId] === null || row[SWIM_LIB.regId] === undefined ? '' : row[SWIM_LIB.regId]).trim();
  var a = regId ? reg.byId[regId] : null;
  if (regId && !a) throw new Error('ο αριθμός μητρώου ' + regId + ' δεν υπάρχει στο φύλλο αθλητών');
  var cps = (rec.checkpoints || []).map(function (c) { return { d: Number(c.d), t: Number(c.t) }; })
    .sort(function (x, y) { return x.d - y.d; });
  var strokes = {}, anyStrokes = false;
  Object.keys(rec.segmentStrokes || {}).forEach(function (k) {
    var n = swimNum_(rec.segmentStrokes[k]);
    if (isFinite(Number(k)) && n !== null && n >= 0) { strokes[Number(k)] = n; anyStrokes = true; }
  });
  var find = function (d) { for (var j = 0; j < cps.length; j++) if (Math.abs(cps[j].d - d) < 1e-9) return j; return -1; };
  // Σβήνει ένα σημείο. merge: οι χεριές του τμήματος πάνε στο επόμενο σημείο (το τμήμα μεγαλώνει, το σύνολο μένει).
  var removeAt = function (d, merge) {
    var i = find(d);
    if (i < 0) return;
    cps.splice(i, 1);
    if (strokes[d] === undefined) return;
    if (merge && !rec.strokesPer50 && i < cps.length) strokes[cps[i].d] = (strokes[cps[i].d] || 0) + strokes[d];
    delete strokes[d];
  };
  // Χωρίς τους ύποπτους χρόνους: φεύγουν και οι χεριές τους (γράφτηκαν μαζί, πιθανόν από το ίδιο αντιγραμμένο φύλλο).
  if (String(row[SWIM_LIB.action]) === SWIM_LIB_ACTIONS[1] && rec.drop && rec.drop.length) {
    rec.drop.forEach(function (d) { if (Number(d) !== Number(rec.distance)) removeAt(Number(d), false); });
  }
  var fixes = swimLibraryFixes_(row[SWIM_LIB.fix]);
  Object.keys(fixes).forEach(function (k) {
    var d = Number(k), v = fixes[k];
    if (v === null) { removeAt(d, true); return; }
    if (v === undefined || !(v > 0)) return;
    var i = find(d);
    if (i >= 0) cps[i].t = v; else cps.push({ d: d, t: v });
    cps.sort(function (x, y) { return x.d - y.d; });
  });
  var gender = String(row[SWIM_LIB.gender] || '');
  var race = {
    id: rec.id, kind: 'race', source: rec.origin === 'pdf' ? 'pdf' : 'excel',
    athlete: a ? a.name : rec.athlete, athleteId: a ? a.id : '', club: a ? a.club : '', category: a ? a.category : '', birthYear: a ? a.birthYear : '',
    gender: swimGenderCode_(gender) || swimGenderCode_(a ? a.gender : '') || rec.gender || '',
    style: rec.style, relay: !!rec.relay, distance: rec.distance, distanceLabel: rec.distanceLabel, pool: rec.pool || 50, poolUnit: 'm',
    competition: String(row[SWIM_LIB.competition] || rec.competition || ''), location: rec.location || '',
    round: String(row[SWIM_LIB.round] || rec.round || ''), date: String(row[SWIM_LIB.date] || rec.date || ''),
    checkpoints: cps, segmentStrokes: anyStrokes ? strokes : null, strokesPer50: !!rec.strokesPer50, manual: rec.manual || {},
    relayLeg: rec.relayLeg || '', dq: !!rec.dq, strokeRounding: 0, compare: { pb: 'auto' }
  };
  if (rec.foreign && rec.country) race.club = rec.country;
  return race;
}

/** Μενού: καταχώριση των τσεκαρισμένων γραμμών του φύλλου ελέγχου. */
function swimLibraryImport() {
  var ui = SpreadsheetApp.getUi();
  var file = swimLibraryFile_(true);
  if (!file) return;
  var res = swimLibraryImport_(file);
  ui.alert('Βιβλιοθήκη αναλύσεων',
    'Καταχωρίστηκαν ' + res.imported + ' αγώνες' + (res.updated ? ' (' + res.updated + ' ενημερώθηκαν)' : '') + '.\n' +
    (res.references ? 'Elite αναφορές: ' + res.references + '.\n' : '') +
    (res.errors.length ? '\nΔεν μπήκαν:\n' + res.errors.slice(0, 10).join('\n') + '\n' : '') +
    '\nΟι αγώνες φαίνονται στο "' + SWIM_SUMMARY_SHEET + '" και στα PB του Kinovea. ' +
    'Για report: επίλεξε γραμμές στο "' + SWIM_SUMMARY_SHEET + '" → Swim Analysis → Report για τις επιλεγμένες αναλύσεις.', ui.ButtonSet.OK);
}

function swimLibraryImport_(file) {
  var lib = swimLibraryRead_(file);
  var ss = swimDataSpreadsheet_();
  var sh = ss.getSheetByName(SWIM_LIBRARY_SHEET);
  if (!sh || sh.getLastRow() < 2) throw new Error('Πρώτα: Swim Analysis → Βιβλιοθήκη παλιών αναλύσεων → 1. Φόρτωση για έλεγχο.');
  var rows = sh.getRange(2, 1, sh.getLastRow() - 1, SWIM_LIB_HEADERS.length).getValues();
  var reg = swimRegistryIndex_(swimAthletes_().athletes);

  var reports = swimReportsSpreadsheet_();
  var index = swimIndexSheet_(reports);
  var idxRow = {};
  var lastIdx = index.getLastRow();
  if (lastIdx >= 2) index.getRange(2, 1, lastIdx - 1, 1).getValues().forEach(function (r, i) { idxRow[String(r[0])] = i + 2; });
  var summary = swimSummarySheet_(ss);
  var sumRow = {}, sumPdf = {};
  var lastSum = summary.getLastRow();
  if (lastSum >= 2) {
    summary.getRange(2, 1, lastSum - 1, SWIM_SUMMARY_HEADERS.length).getValues().forEach(function (r, i) {
      sumRow[String(r[SWIM_SUM.raceId])] = i + 2;
      sumPdf[String(r[SWIM_SUM.raceId])] = r[SWIM_SUM.pdf];
    });
  }

  var now = Utilities.formatDate(new Date(), Session.getScriptTimeZone(), 'dd/MM/yyyy HH:mm');
  var newIdx = [], newSum = [], refs = [], status = [], errors = [];
  var res = { imported: 0, updated: 0, references: 0, errors: errors };
  rows.forEach(function (row, i) {
    if (row[SWIM_LIB.on] !== true || String(row[SWIM_LIB.action]) === SWIM_LIB_ACTIONS[2]) return;
    var id = String(row[SWIM_LIB.id] || '');
    var rec = lib.byId[id];
    if (!rec) { errors.push(row[SWIM_LIB.athlete] + ' ' + row[SWIM_LIB.event] + ': δεν υπάρχει στο αρχείο (' + id + ')'); return; }
    var race, analysis;
    try {
      race = swimNormalizeRace_(swimLibraryRace_(rec, row, reg));
      analysis = swimAnalyze_(race);
    } catch (e) {
      errors.push(row[SWIM_LIB.athlete] + ' ' + row[SWIM_LIB.event] + ': ' + (e && e.message ? e.message : e));
      return;
    }
    var raw = JSON.stringify(race);
    if (raw.length > 49000) raw = JSON.stringify(swimCompactRace_(race));
    var ft = analysis.summary.finalTime;
    var iv = [race.id, 'χωρίς report', race.athlete, race.athleteId, swimIndexEventLabel_(race), race.distanceLabel, race.round,
      race.competition, race.date, ft === null ? '' : ft, now, '', '', '', raw];
    if (idxRow[id]) {
      // ήδη στο ευρετήριο: ενημέρωση, το φύλλο report (αν υπάρχει) ξαναφτιάχνεται όταν ζητηθεί
      var old = index.getRange(idxRow[id], 1, 1, SWIM_INDEX_HEADERS.length).getValues()[0];
      iv[SWIM_IDX.link] = old[SWIM_IDX.link] || iv[SWIM_IDX.link];
      iv[SWIM_IDX.sheetId] = old[SWIM_IDX.sheetId];
      index.getRange(idxRow[id], 1, 1, iv.length).setValues([iv]);
      res.updated++;
    } else {
      newIdx.push(iv);
      idxRow[id] = -1;
    }
    var sv = swimSummaryValues_(race, analysis, swimKpisFromAnalysis_(analysis), '', sumPdf[id] || '');
    if (sumRow[id]) summary.getRange(sumRow[id], 1, 1, sv.length).setValues([sv]);
    else { newSum.push(sv); sumRow[id] = -1; }
    if (row[SWIM_LIB.elite] === true) {
      var label = [race.athlete + (race.relayLeg ? ' (σκέλος)' : ''), [race.competition, race.round].filter(function (x) { return x; }).join(' ')]
        .filter(function (x) { return x; }).join(' – ');
      refs.push(swimReferenceFromRace_(race, label));
    }
    status.push([i, 'Καταχωρίστηκε ' + now]);
    res.imported++;
  });

  if (newIdx.length) {
    var start = index.getLastRow() + 1;
    if (index.getMaxRows() < start + newIdx.length - 1) index.insertRowsAfter(index.getMaxRows(), start + newIdx.length - 1 - index.getMaxRows());
    index.getRange(start, 1, newIdx.length, SWIM_INDEX_HEADERS.length).setValues(newIdx);
  }
  if (newSum.length) {
    var s0 = summary.getLastRow() + 1;
    if (summary.getMaxRows() < s0 + newSum.length - 1) summary.insertRowsAfter(summary.getMaxRows(), s0 + newSum.length - 1 - summary.getMaxRows());
    summary.getRange(s0, 1, newSum.length, SWIM_SUMMARY_HEADERS.length).setValues(newSum);
  }
  refs.forEach(function (ref) {
    try { swimSaveReference_(ref); res.references++; } catch (e) { errors.push(ref.name + ': ' + (e && e.message ? e.message : e)); }
  });
  if (status.length) {
    var stCol = rows.map(function (r) { return [r[SWIM_LIB.status]]; });
    var onCol = rows.map(function (r) { return [r[SWIM_LIB.on] === true]; });
    status.forEach(function (s) { stCol[s[0]][0] = s[1]; onCol[s[0]][0] = false; });
    sh.getRange(2, SWIM_LIB.status + 1, rows.length, 1).setValues(stCol);
    sh.getRange(2, SWIM_LIB.on + 1, rows.length, 1).setValues(onCol);
  }
  return res;
}

/** Μενού: report (φύλλο) για τις γραμμές που είναι επιλεγμένες στο "Αναλύσεις Αγώνων" (π.χ. αγώνες της βιβλιοθήκης). */
function swimReportsSelected() {
  swimSelectedBulk_(false);
}

/** Νέο PDF (με τις σελίδες και τα γράμματα της τρέχουσας έκδοσης) για κάθε επιλεγμένη ανάλυση. */
function swimPdfsSelected() {
  swimSelectedBulk_(true);
}

/** Για τις επιλεγμένες γραμμές του «Αναλύσεις Αγώνων»: ξανά το report (και, με pdf, νέο PDF), ως ~4.5 λεπτά ανά εκτέλεση. */
function swimSelectedBulk_(pdf) {
  var ui = SpreadsheetApp.getUi();
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var sheet = ss.getActiveSheet();
  if (sheet.getName() !== SWIM_SUMMARY_SHEET) { ui.alert('Επίλεξε γραμμές στο φύλλο "' + SWIM_SUMMARY_SHEET + '" και ξαναδοκίμασε.'); return; }
  var rows = {};
  sheet.getActiveRangeList().getRanges().forEach(function (rg) {
    for (var r = rg.getRow(); r < rg.getRow() + rg.getNumRows(); r++) if (r >= 2) rows[r] = true;
  });
  var list = Object.keys(rows).map(Number).sort(function (a, b) { return a - b; });
  if (!list.length) { ui.alert('Επίλεξε γραμμές αγώνων (όχι την κεφαλίδα).'); return; }
  var t0 = Date.now(), done = 0, left = 0, failed = [];
  var reports = swimReportsSpreadsheet_();
  var index = swimIndexSheet_(reports);
  list.forEach(function (r) {
    if (Date.now() - t0 > 4.5 * 60 * 1000) { left++; return; }
    var id = String(sheet.getRange(r, SWIM_SUM.raceId + 1).getValue() || '');
    var stored = id ? swimStoredRace_(id) : null;
    if (!stored) return;
    try {
      if (!pdf) index.getRange(stored.row, SWIM_IDX.layout + 1).setValue('');
      // Το report στη γλώσσα του αγώνα όπως κρατήθηκε (2.7.7), αλλιώς στα ελληνικά.
      var made = swimWithLang_(stored.race.lang, function () { return swimUpsertRace_(stored.race); });
      if (pdf) {
        var info = swimExportPdf_(reports, swimSheetById_(reports, made.sheetId), made.print, made.rebuilt, made.quick);
        swimSetSummaryPdf_(id, info.url);
      }
      done++;
    } catch (e) {
      failed.push('γραμμή ' + r + ': ' + (e && e.message ? e.message : e));
    }
  });
  ui.alert((pdf ? 'PDF: ' : 'Report: ') + done + (left ? '. Έμειναν ' + left + ' (όριο χρόνου): ξαναδιάλεξέ τες και ξανατρέξε.' : '.') +
    (failed.length ? '\nΔεν έγιναν: ' + failed.join(' · ') : ''));
}

function swimOpenLibrary() {
  var ss = swimDataSpreadsheet_();
  var sh = ss.getSheetByName(SWIM_LIBRARY_SHEET);
  var file = null;
  try { file = swimLibraryFile_(false); } catch (e) { file = null; }
  var html = '<div style="font-family:Arial;font-size:13px;line-height:1.5">' +
    '<p>Οι αναλύσεις του παλιού Excel (και των PDF τους) ως αγώνες της εφαρμογής, για PB και συγκρίσεις.</p>' +
    '<ol><li>Ανέβασε το <b>' + SWIM_LIBRARY_FILE + '</b> στον φάκελο <b>' + SWIM_REPORT_FOLDER + '</b> του Drive' +
    (file ? ' <span style="color:#2e8b57">✔ βρέθηκε</span>' : '') + '.</li>' +
    '<li><b>1. Φόρτωση για έλεγχο</b>: φύλλο "' + SWIM_LIBRARY_SHEET + '" με έναν αγώνα ανά γραμμή.</li>' +
    '<li>Έλεγξε τις πορτοκαλί γραμμές (στήλη "Έλεγχος"), το Μητρώο, και διόρθωσε χρόνους αν θέλεις.</li>' +
    '<li><b>2. Καταχώριση των επιλεγμένων</b>.</li></ol>' +
    (sh ? '<p><a href="' + ss.getUrl() + '#gid=' + sh.getSheetId() + '" target="_blank">Άνοιγμα του φύλλου ελέγχου</a></p>' : '') + '</div>';
  SpreadsheetApp.getUi().showModalDialog(HtmlService.createHtmlOutput(html).setWidth(520).setHeight(300), 'Βιβλιοθήκη αναλύσεων');
}

// ---------------------------------------------------------------------------------------------
// Μενού & ρύθμιση
// ---------------------------------------------------------------------------------------------

function swimOnOpen(e) {
  SpreadsheetApp.getUi().createMenu('Swim Analysis')
    .addItem('1. Αρχική ρύθμιση', 'swimSetup')
    .addItem('2. Στοιχεία σύνδεσης για το Kinovea', 'swimShowConnectionInfo')
    .addSeparator()
    .addItem('Άνοιγμα των αναφορών (reports)', 'swimOpenReports')
    .addItem('PDF της επιλεγμένης ανάλυσης', 'swimPdfSelected')
    .addItem('Επαναδημιουργία της επιλεγμένης ανάλυσης', 'swimRebuildSelected')
    .addItem('Σελίδες PDF: νέα μέτρηση', 'swimPdfPagesCheck')
    .addItem('Έλεγχος γραφημάτων στο PDF', 'swimChartsCheck')
    .addItem('Report για τις επιλεγμένες αναλύσεις', 'swimReportsSelected')
    .addItem('PDF για τις επιλεγμένες αναλύσεις', 'swimPdfsSelected')
    .addSeparator()
    .addSubMenu(SpreadsheetApp.getUi().createMenu('Βιβλιοθήκη παλιών αναλύσεων')
      .addItem('Οδηγίες', 'swimOpenLibrary')
      .addItem('1. Φόρτωση για έλεγχο', 'swimLibraryLoad')
      .addItem('2. Καταχώριση των επιλεγμένων', 'swimLibraryImport'))
    .addSeparator()
    .addItem('Elite αναφορές (άνοιγμα)', 'swimOpenReferences')
    .addItem('Η επιλεγμένη ανάλυση ως elite αναφορά', 'swimReferenceSelected')
    .addItem('Επίπεδα 2015–2024 (άνοιγμα)', 'swimOpenBenchmarks')
    .addItem('Elite καρέ (άνοιγμα)', 'swimOpenEliteFrames')
    .addSeparator()
    .addItem('Δοκιμαστικός αγώνας (demo)', 'swimCreateDemo')
    .addSeparator()
    .addItem('Νέο token (ακυρώνει το παλιό)', 'swimResetToken')
    .addToUi();
}

function swimSetup() {
  var props = swimProps_();
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  if (ss) props.setProperty('SWIM_DATA_SPREADSHEET_ID', ss.getId());
  if (!props.getProperty('SWIM_TOKEN')) props.setProperty('SWIM_TOKEN', Utilities.getUuid().replace(/-/g, ''));

  var data = swimDataSpreadsheet_();
  var reports = swimReportsSpreadsheet_();
  swimIndexSheet_(reports);
  swimReferencesSheet_(reports);
  swimBenchmarksSheet_(reports);
  swimEliteSheet_(reports);
  swimSummarySheet_(data);
  swimReportFolder_();
  try { swimSubFolder_(SWIM_SNAPSHOT_FOLDER); swimSubFolder_(SWIM_ELITE_FOLDER); } catch (e) { /* δημιουργούνται με την πρώτη εικόνα */ }
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
    return '<p style="color:#c06e00">⚠ Το project δεν έχει doPost, οπότε το Kinovea δεν μπορεί να στείλει δεδομένα. ' +
      'Φτιάξε νέο αρχείο (+ → Σενάριο, π.χ. SwimDoPost) μόνο με τη γραμμή:<br>' +
      '<code>function doPost(e) { return swimDoPost(e); }</code><br>και κάνε νέα έκδοση της ανάπτυξης.</p>';
  return '<p style="color:#c06e00">⚠ Το project έχει ήδη δικό του doPost. Βάλε στην πρώτη γραμμή του:<br>' +
    '<code>var swimResponse = swimTryHandlePost(e); if (swimResponse) return swimResponse;</code></p>';
}

function swimResetToken() {
  swimProps_().setProperty('SWIM_TOKEN', Utilities.getUuid().replace(/-/g, ''));
  swimShowConnectionInfo();
}

function swimShowConnectionInfo() {
  var token = swimGetToken_() || '(τρέξε πρώτα: Swim Analysis → 1. Αρχική ρύθμιση)';
  var reportsUrl = '';
  try { reportsUrl = swimReportsSpreadsheet_().getUrl(); } catch (e) { reportsUrl = ''; }
  var athletes = 0, athleteSheet = '';
  try { var a = swimAthletes_(); athletes = a.athletes.length; athleteSheet = a.sheet; } catch (e) { athletes = 0; }
  var html =
    '<div style="font-family:Arial;font-size:13px;line-height:1.5">' +
    '<p><b>Token</b> (επικόλλησέ το στην καρτέλα Σύνδεση του πάνελ στο Kinovea):</p>' +
    '<input style="width:100%;font-size:13px" readonly value="' + token + '" onclick="this.select()">' +
    '<p style="margin-top:14px"><b>Διεύθυνση Web App</b>: αντέγραψε τη διεύθυνση που τελειώνει σε <code>/exec</code> ' +
    'από το Apps Script → Ανάπτυξη → Διαχείριση αναπτύξεων → ενεργή ανάπτυξη → Web app → URL. ' +
    'Μετά από κάθε αλλαγή κώδικα: ✏️ → Έκδοση: Νέα έκδοση → Deploy (η διεύθυνση μένει ίδια).</p>' +
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
  var row = swimIndexRow_(index, raceId);
  if (!row) return null;
  var data = index.getRange(row, 1, 1, SWIM_INDEX_HEADERS.length).getValues()[0];
  try { return { row: row, race: JSON.parse(String(data[SWIM_IDX.json])), sheetId: String(data[SWIM_IDX.sheetId]) }; } catch (e) { return null; }
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
  swimWithLang_(stored.race.lang, function () { return swimUpsertRace_(stored.race); });
  ui.alert('Η ανάλυση δημιουργήθηκε ξανά.');
}

function swimPdfSelected() {
  var ui = SpreadsheetApp.getUi();
  var id = swimSelectedRaceId_();
  var stored = id ? swimStoredRace_(id) : null;
  if (!stored) { ui.alert('Επίλεξε μια γραμμή στο φύλλο "' + SWIM_SUMMARY_SHEET + '" και ξαναδοκίμασε.'); return; }
  var reports = swimReportsSpreadsheet_();
  // Πρώτα το φύλλο, με τις αλλαγές σελίδας του PDF (και για αγώνα χωρίς report, π.χ. από τη βιβλιοθήκη), στη γλώσσα του αγώνα.
  var made = swimWithLang_(stored.race.lang, function () { return swimUpsertRace_(stored.race); });
  var sheet = swimSheetById_(reports, made.sheetId);
  var info = swimExportPdf_(reports, sheet, made.print, made.rebuilt, made.quick);
  swimSetSummaryPdf_(id, info.url);
  var html = '<div style="font-family:Arial;font-size:13px"><p>Το PDF αποθηκεύτηκε στον φάκελο <b>' + SWIM_REPORT_FOLDER + '</b>.</p>' +
    '<p><a href="' + info.url + '" target="_blank">Άνοιγμα ' + info.name + '</a></p></div>';
  ui.showModalDialog(HtmlService.createHtmlOutput(html).setWidth(420).setHeight(140), 'Report PDF');
}

/** Δοκιμαστικός αγώνας 100μ. ελεύθερο (με τεχνική ανάλυση εκκίνησης και στροφής), για να δεις το report χωρίς Kinovea. */
function swimDemoRace_() {
  var cps = [15, 25, 35, 45, 50, 65, 75, 85, 95, 100];
  var times = [6.10, 10.95, 16.02, 21.12, 23.80, 30.15, 35.55, 41.02, 46.55, 49.42];
  var strokes = [];
  var t;
  for (t = 4.95; t < 23.6; t += 0.615) strokes.push(Math.round(t * 100) / 100);
  for (t = 28.70; t < 49.2; t += 0.64) strokes.push(Math.round(t * 100) / 100);
  return {
    id: 'demo-100fs', athlete: 'Αθλητής Δοκιμής', athleteId: '', club: 'Δοκιμαστικό σωματείο', category: '', style: 'fs', relay: false,
    distance: 100, distanceLabel: '100', pool: 50, poolUnit: 'm',
    competition: 'Δοκιμή συστήματος', location: 'Αθήνα', round: 'Final', date: Utilities.formatDate(new Date(), Session.getScriptTimeZone(), 'd.M.yyyy'),
    checkpoints: cps.map(function (d, i) { return { d: d, t: times[i] }; }),
    strokes: strokes,
    kicks: [1.5, 1.9, 2.3, 2.7, 3.1, 3.5, 3.9, 25.0, 25.4, 25.8, 26.2, 26.6, 27.0],
    breakouts: [4.62, 28.35],
    feetOff: [0.68, 24.40],
    entries: [0.98],
    manual: { reaction: 0.62, entryDistHands: 3.4, entryDistHips: 3.1, underwaterDist: 11.8, breakoutDist: 12.1,
      turns: { '50': { underwaterDist: 9.1 } }, finishLastStrokeDist: 0.35 },
    tech: {
      phases: [
        { id: 'start', kind: 'start', values: { reaction: 0.21, handsOff: 0.39, backFoot: 0.55, blockTime: 0.68, blockPhase: 0.47, flight: 0.30,
          entryTime: 0.98, entryDist: 3.4, t5: 1.62, t10: 3.71, t15: 6.10, v0_5: 3.09, v5_10: 2.39, v10_15: 2.10, underwater: 3.64, kicks: 7,
          kickRate: 2.5, kickDist: 1.6, breakoutTime: 4.62, breakoutDist: 12.1, uwVel: 2.45, firstStroke: 4.95 } },
        { id: 'turn50', kind: 'turn', wall: 50, values: { in5: 2.68, lastStrokeGap: 0.55, lastStrokeDist: 1.1, rotation: 0.42, contact: 0.27,
          turnTime: 0.82, out5: 1.35, out10: 3.55, out15: 6.35, inOut5: 4.03, inOut10: 6.23, inOut15: 9.03, underwater: 3.95, kicks: 6,
          kickRate: 2.4, kickDist: 1.5, breakoutTime: 28.35, breakoutDist: 9.1, uwVel: 2.30, firstStroke: 28.70 } }
      ],
      angles: [
        { id: 'a1', name: 'Γωνία γονάτου (ετοιμότητα)', t: 0.05, deg: 128, phase: 'start' },
        { id: 'a2', name: 'Γωνία εισόδου', t: 0.98, deg: 38, phase: 'start' },
        { id: 'a3', name: 'Γωνία γονάτου (επαφή)', t: 24.2, deg: 92, phase: 'turn50' }
      ],
      snapshots: []
    }
  };
}

function swimCreateDemo() {
  var res = swimUpsertRace_(swimDemoRace_());
  var html = '<div style="font-family:Arial;font-size:13px"><p>Δημιουργήθηκε ο δοκιμαστικός αγώνας.</p>' +
    '<p><a href="' + res.sheetUrl + '" target="_blank">Άνοιγμα του report</a></p>' +
    '<p style="color:#666">Μπήκε και μία γραμμή στο φύλλο "' + SWIM_SUMMARY_SHEET + '" (μπορείς να τη διαγράψεις).</p></div>';
  SpreadsheetApp.getUi().showModalDialog(HtmlService.createHtmlOutput(html).setWidth(420).setHeight(170), 'Demo');
}

// ---------------------------------------------------------------------------------------------
// Τα αγγλικά του report (2.7.7): ελληνικό κείμενο → αγγλικά, για τα swimT_, swimTF_ (με τα ίδια κενά {0}, {1}…) και
// swimL_. Οι όροι όπως στο Kinovea (SwimLangEnglish): ΔΑΚ → SPI, Συχνότητα → Stroke rate, Έξοδος → Breakout, μ. → m.
// ---------------------------------------------------------------------------------------------

var SWIM_EN_ = {
  // Μηνύματα προς το Kinovea (σφάλματα και προειδοποιήσεις)
  'Λάθος token. Αντέγραψε ξανά το token από το μενού Swim Analysis → Στοιχεία σύνδεσης.':
    'Wrong token. Copy the token again from the menu Swim Analysis → "Στοιχεία σύνδεσης".',
  'Το φύλλο είναι απασχολημένο, θα ξαναπροσπαθήσει.': 'The sheet is busy, it will try again.',
  'Δεν βρέθηκε το Google Sheet. Τρέξε πρώτα τη συνάρτηση swimSetup από το Apps Script.':
    'The Google Sheet was not found. First run the function swimSetup from the Apps Script.',
  'Χρειάζονται το ονοματεπώνυμο και η χώρα (κωδικός τριών γραμμάτων, π.χ. ITA).':
    'The full name and the country are needed (three-letter code, e.g. ITA).',
  'Οι αθλητές της Ελλάδας είναι στο μητρώο της ΚΟΕ, με τον αριθμό μητρώου τους.':
    'The swimmers of Greece are in the KOE registry, with their registration number.',
  'Μοιάζει με αθλητή που υπάρχει ήδη στους διεθνείς.': 'Looks like a swimmer who is already among the international swimmers.',
  'Χρειάζονται ο κωδικός του αθλητή και το όνομα.': 'The code of the swimmer and the name are needed.',
  'Λείπουν τα δεδομένα του αγώνα.': 'The data of the race are missing.',
  'Λείπει το id του αγώνα.': 'The id of the race is missing.',
  'Δεν υπάρχουν σημεία ελέγχου (αποστάσεις).': 'There are no checkpoints (distances).',
  'Ο χρόνος στα {0} δεν είναι μεγαλύτερος από τον προηγούμενο.': 'The time at {0} is not greater than the previous one.',
  'γραμμή {0}: άγνωστο στυλ "{1}"': 'row {0}: unknown stroke "{1}"',
  'γραμμή {0}: λείπει η απόσταση': 'row {0}: the distance is missing',
  'γραμμή {0}: μη έγκυρος χρόνος "{1}"': 'row {0}: invalid time "{1}"',
  'Η αναφορά χρειάζεται στυλ και απόσταση.': 'The reference needs a stroke and a distance.',
  'Η αναφορά δεν έχει χρόνους.': 'The reference has no times.',
  'Δεν βρέθηκε το φύλλο για PDF.': 'The sheet for the PDF was not found.',
  'Αποτυχία εξαγωγής PDF (HTTP {0}).': 'The PDF export failed (HTTP {0}).',
  'Λείπει η εικόνα.': 'The image is missing.',
  'Η εικόνα είναι μεγαλύτερη από 2 MB.': 'The image is larger than 2 MB.',
  'Λείπει το id του αγώνα ή του στιγμιότυπου.': 'The id of the race or of the snapshot is missing.',
  'Λείπει το id του αγώνα ή οι εικόνες.': 'The id of the race or the images are missing.',
  'Έως 30 εικόνες σε κάθε αποστολή.': 'Up to 30 images in each upload.',
  'μη έγκυρο id εικόνας': 'invalid image id',
  'Το elite καρέ χρειάζεται τίτλο.': 'The elite frame needs a title.',
  'Λείπει το id της εικόνας.': 'The id of the image is missing.',
  'Η εικόνα δεν είναι στα στιγμιότυπα ή στη βιβλιοθήκη elite καρέ.': 'The image is not among the snapshots or in the elite frame library.',
  'Η εικόνα είναι πολύ μεγάλη για προεπισκόπηση (πάνω από 4 MB).': 'The image is too large for a preview (over 4 MB).',
  // Επικεφαλίδα και πλακίδια
  'Κ.Ο.Ε. · Ανάλυση αγώνα με Kinovea': 'KOE · Race analysis with Kinovea',
  'Κ.Ο.Ε. · Τεχνική ανάλυση με Kinovea': 'KOE · Technical analysis with Kinovea',
  'TECHNICAL ANALYSIS · ΕΚΚΙΝΗΣΕΙΣ & ΣΤΡΟΦΕΣ': 'TECHNICAL ANALYSIS · STARTS & TURNS',
  'Ενημέρωση {0} · έκδοση {1}': 'Updated {0} · version {1}',
  'Τεχνική ανάλυση (προπόνηση)': 'Technical analysis (training)',
  'Πισίνα ': 'Pool ',
  ' (σκυταλοδρομία)': ' (relay)',
  ' (σκέλος σκυταλοδρομίας)': ' (relay leg)',
  'Ακύρωση (DQ)': 'Disqualified (DQ)',
  '  (Διάδρομος {0})': '  (Lane {0})',
  'Μητρώο ': 'Reg. no. ',
  'ΔΑΚ': 'SPI',
  'ΔΑΚ (μ.ο.)': 'SPI (mean)',
  'ΔΑΚ 50άρι': 'SPI of the 50',
  'Δείκτης Απόδοσης Κολύμβησης (μ.ο. 50άρι)': 'Swimming Performance Index (average of the 50s)',
  'ΤΕΛΙΚΟΣ ΧΡΟΝΟΣ': 'FINAL TIME',
  'λεπτά:δευτερόλεπτα': 'minutes:seconds',
  'ΜΕΣΗ ΤΑΧΥΤΗΤΑ': 'AVERAGE SPEED',
  'ΣΥΧΝΟΤΗΤΑ': 'STROKE RATE',
  'κύκλοι/λεπτό · ολόκληρα 50άρια': 'cycles/min · whole 50s',
  'κύκλοι/λεπτό · καθαρή κολύμβηση': 'cycles/min · clean swimming',
  'ΜΗΚΟΣ ΧΕΡΙΑΣ': 'DISTANCE PER STROKE',
  'm/χεριά (yd: ÷0.9144)': 'm/stroke (yd: ÷0.9144)',
  'm/χεριά · ολόκληρα 50άρια': 'm/stroke · whole 50s',
  'm/χεριά · καθαρή κολύμβηση': 'm/stroke · clean swimming',
  'ΕΚΚΙΝΗΣΗ 0-15': 'START 0-15',
  'δευτερόλεπτα από το σήμα': 'seconds from the signal',
  'ΣΤΡΟΦΕΣ': 'TURNS',
  '% του χρόνου (-5/+15)': '% of the time (-5/+15)',
  'ΠΡΟΣΠΑΘΕΙΕΣ': 'ATTEMPTS',
  '{0} εκκινήσεις · {1} στροφές': '{0} starts · {1} turns',
  'ΚΑΛΥΤΕΡΟ 0-15': 'BEST 0-15',
  'εκκίνηση (s)': 'start (s)',
  'ΚΑΛΥΤΕΡΟ ΤΟΙΧΟΣ→15': 'BEST WALL→15',
  'στροφή (s)': 'turn (s)',
  'ΧΡΟΝΟΣ ΣΤΡΟΦΗΣ': 'TURN TIME',
  'καλύτερος (s)': 'best (s)',
  'ΔΕΛΦΙΝΙΣΜΟΙ': 'DOLPHIN KICKS',
  'μέσος όρος': 'average',
  'ΣΥΧΝ. ΔΕΛΦΙΝΙΣΜΩΝ': 'KICK RATE',
  'δελφ./s (μ.ο.)': 'kicks/s (avg.)',
  'ΕΞΟΔΟΣ': 'BREAKOUT',
  '{0} από τον τοίχο (μ.ο.)': '{0} from the wall (avg.)',
  // Μονάδες και στυλ
  'μ.': 'm',
  'Ελεύθερο': 'Freestyle',
  'Ύπτιο': 'Backstroke',
  'Πρόσθιο': 'Breaststroke',
  'Πεταλούδα': 'Butterfly',
  'Μικτή': 'IM',
  // Εκκίνηση, χρόνοι και χεριές, ανά 50
  'Εκκίνηση (χρόνοι από το σήμα εκκίνησης)': 'Start (times from the start signal)',
  'Χρόνος Αντίδρασης – πρώτη κίνηση (sec)': 'Reaction time – first movement (sec)',
  'Χρόνος Πάνω στον βατήρα (sec)': 'Time on the block (sec)',
  'Χρόνος έως την είσοδο (sec)': 'Time to the entry (sec)',
  'Απόσταση εισόδου (Χέρια) ({0})': 'Entry distance (hands) ({0})',
  'Απόσταση εισόδου (Ισχία) ({0})': 'Entry distance (hips) ({0})',
  'Ταχύτητα εισόδου (Χέρια) (m/s)': 'Entry speed (hands) (m/s)',
  'Ταχύτητα εισόδου (Ισχία) (m/s)': 'Entry speed (hips) (m/s)',
  'Ταχύτητα εισόδου (Πόδια) (m/s)': 'Entry speed (feet) (m/s)',
  'Χρόνος έως την έξοδο από το νερό (sec)': 'Time to the breakout (sec)',
  'Υποβρύχιοι Δελφινισμοί': 'Underwater dolphin kicks',
  'Απόσταση κάτω από το νερό ({0})': 'Underwater distance ({0})',
  'Απόσταση Εξόδου από το νερό (κεφάλι) ({0})': 'Breakout distance (head) ({0})',
  'Μέση ταχύτητα από το σήμα έως την έξοδο (m/s)': 'Average speed from the signal to the breakout (m/s)',
  'Πτήση: βατήρας → είσοδος (sec)': 'Flight: block → entry (sec)',
  'Χρόνος 15{0} (sec)': '15{0} time (sec)',
  'Χρόνοι & Χεριές (Timing Data)': 'Times & Strokes (Timing Data)',
  'Χεριές τμήματος': 'Segment strokes',
  'Χρόνος τμήματος (s)': 'Segment time (s)',
  'Μέση ταχύτητα από εκκίνηση (m/s)': 'Average speed from the start (m/s)',
  'SR από χρόνους χεριών (Cycles/min)': 'SR from stroke times (Cycles/min)',
  'Στυλ': 'Stroke',
  'Μήκος κύκλου SL (m/κύκλο)': 'Cycle length SL (m/cycle)',
  'DPS (yd/χεριά)': 'DPS (yd/stroke)',
  'Ανά 50{0} · ΔΑΚ (Δείκτης Απόδοσης Κολύμβησης)': 'Per 50{0} · SPI (Swimming Performance Index)',
  'Χρόνος {0} (s)': '{0} time (s)',
  'Χρόνος (σωρευτικός)': 'Time (cumulative)',
  'Χεριές {0}': '{0} strokes',
  'StrRate καθαρής κολύμβησης (Cycles/min)': 'StrRate of clean swimming (Cycles/min)',
  'DPS καθαρής κολύμβησης (m/stroke)': 'DPS of clean swimming (m/stroke)',
  'Μέση ταχύτητα (m/s)': 'Average speed (m/s)',
  'Διαφορά από προηγ. 50 (s)': 'Difference from prev. 50 (s)',
  'Χρόνος 100{0} (s)': '100{0} time (s)',
  'SL καθαρής κολύμβησης (m/κύκλο)': 'SL of clean swimming (m/cycle)',
  'ΔΑΚ ανά 10yd': 'SPI per 10yd',
  'Μέση ταχύτητα (yd/s)': 'Average speed (yd/s)',
  'Σύνολο': 'Total',
  // Στροφές, τερματισμός, καθαρή κολύμβηση, σύνθεση
  'Στροφές': 'Turns',
  'Τοίχος ({0})': 'Wall ({0})',
  'Χρόνος προσέγγισης 5{0} (s)': '5{0} approach time (s)',
  'Χρόνος -5/+15{0} (s)': '-5/+15{0} time (s)',
  'Χρόνος στροφής (s)': 'Turn time (s)',
  'Μέτρα κάτω από το νερό ({0})': 'Underwater distance ({0})',
  'Χρόνος κάτω από το νερό (s)': 'Underwater time (s)',
  'Δελφινισμοί': 'Dolphin kicks',
  'Μέση ταχύτητα -5/+15{0} (m/s)': 'Average speed -5/+15{0} (m/s)',
  'Μέση ταχύτητα από στροφή έως έξοδο (m/s)': 'Average speed from the turn to the breakout (m/s)',
  'Ποσοστό στον τελικό χρόνο': 'Share of the final time',
  'Τελευταία χεριά → τοίχος (s)': 'Last stroke → wall (s)',
  'Τοίχος → κεφάλι στα +15{0} (s)': 'Wall → head at +15{0} (s)',
  'Είδος στροφής': 'Turn type',
  'αλλαγή σκυταλοδρομίας': 'relay exchange',
  'ανάποδη': 'flip',
  'ανοιχτή': 'open',
  'Τερματισμός': 'Finish',
  'Χρόνος τελευταίων 15{0} (s)': 'Last 15{0} time (s)',
  'Χρόνος τελευταίων 5{0} (s)': 'Last 5{0} time (s)',
  'Χεριές τελευταίων 5{0}': 'Last 5{0} strokes',
  'Προσέγγιση τοίχου σε sec (τελευταία χεριά)': 'Wall approach time in sec (last stroke)',
  'Προσέγγιση τοίχου σε μήκος {0} (τελευταία χεριά)': 'Wall approach distance in {0} (last stroke)',
  'Μέση ταχύτητα τελευταίων 5{0} (m/s)': 'Average speed of the last 5{0} (m/s)',
  'Ειδικοί τομείς (καθαρή κολύμβηση)': 'Clean swimming segments',
  'Τμήμα': 'Segment',
  'Χρόνος (s)': 'Time (s)',
  'Ταχύτητα (m/s)': 'Speed (m/s)',
  'Μεταβολή ταχύτητας (m/s)': 'Speed change (m/s)',
  'Ποσοστό του συνολικού χρόνου': 'Share of the total time',
  'Χεριές': 'Strokes',
  'Σύνθεση αγώνα (ποσοστό του συνολικού χρόνου)': 'Race composition (share of the total time)',
  'Φάση': 'Phase',
  'Ποσοστό': 'Share',
  'Εκκίνηση: 0-15{0} · Στροφές: -5/+15{0} · Τερματισμός: τελευταία 5{0}': 'Start: 0-15{0} · Turns: -5/+15{0} · Finish: last 5{0}',
  'Εκκίνηση': 'Start',
  'Κολύμβηση': 'Swimming',
  'ΔΑΚ = (Χεριές + Χρόνος από τα 15{0} έως τον τοίχο) / (μέτρα / 10), για κάθε μήκος: σε πισίνα 50μ. /3,5 όπως στο Excel, σε πισίνα 25 /1,0 ανά μήκος.':
    'SPI = (Strokes + Time from 15{0} to the wall) / (meters / 10), for each length: in a 50m pool /3.5 as in the Excel, in a 25 pool /1.0 per length.',
  ' Σε γιάρδες και «ανά 10yd».': ' In yards also "per 10yd".',
  'StrRate, DPS, SL μόνο στην καθαρή κολύμβηση (15{0} μετά τον τοίχο έως 5{0} πριν τον επόμενο). DPS: μέτρα ανά χεριά (ελ./υπτ. ανά χέρι) · SL: μέτρα ανά κύκλο.':
    'StrRate, DPS, SL only in clean swimming (15{0} after the wall to 5{0} before the next one). DPS: meters per stroke (free/back: per arm) · SL: meters per cycle.',
  'Πορτοκαλί ταχύτητα: τμήμα εκκίνησης / στροφής / τελευταίων 5{0} – συμβατική (στον τοίχο μετράει η επαφή, όχι το κεφάλι). Σύγκρινε εκεί τους χρόνους.':
    'Orange speed: start / turn / last 5{0} segment – conventional (at the wall the contact counts, not the head). Compare the times there.',
  'Χρόνος στροφής: ανάποδη (ελεύθερο/ύπτιο) από την τελευταία είσοδο χεριού έως την αποκόλληση · ανοιχτή (πεταλούδα, πρόσθιο, αλλαγή στυλ) από την επαφή στον τοίχο · σκυταλοδρομία: χρόνος αλλαγής.':
    'Turn time: flip (freestyle/backstroke) from the last hand entry to the push-off · open (butterfly, breaststroke, change of stroke) from the wall contact · relay: exchange time.',
  'SR από χρόνους χεριών: από τα διαστήματα των πατημάτων · Λευκά: πληκτρολόγηση · Γαλάζια: αυτόματα από το Kinovea · Γκρι: τύποι.':
    'SR from stroke times: from the intervals of the presses · White: typed · Light blue: automatic from Kinovea · Grey: formulas.',
  ' Ταχύτητες σε m/s (1yd = 0,9144μ.), με τις τιμές σε yd/s στις τελευταίες στήλες.':
    ' Speeds in m/s (1yd = 0.9144m), with the values in yd/s in the last columns.',
  // Συγκριτική αναφορά (PB – αγώνας – elite)
  'Συγκριτική αναφορά: PB – αγώνας – elite': 'Comparison: PB – race – elite',
  'PB: (δεν επιλέχθηκε)': 'PB: (not selected)',
  'Elite: (δεν επιλέχθηκε)': 'Elite: (not selected)',
  'Περάσματα και ταχύτητα ανά τμήμα': 'Splits and speed per segment',
  'Απόσταση ({0})': 'Distance ({0})',
  'Χρόνος PB': 'Time PB',
  'Χρόνος': 'Time',
  'Χρόνος elite': 'Time elite',
  'Δ από PB (s)': 'Δ from PB (s)',
  'Δ από elite (s)': 'Δ from elite (s)',
  'Ταχ. PB (m/s)': 'Speed PB (m/s)',
  'Ταχ. (m/s)': 'Speed (m/s)',
  'Ταχ. elite (m/s)': 'Speed elite (m/s)',
  'Χρόνος τμήματος PB': 'Segment time PB',
  'Χρόνος τμήματος': 'Segment time',
  'Χρόνος τμήματος elite': 'Segment time elite',
  'Μεταβολή ταχ. PB': 'Speed change PB',
  'Μεταβολή ταχ.': 'Speed change',
  'Μεταβολή ταχ. elite': 'Speed change elite',
  'Συχνότητα και μήκος χεριάς (καθαρή κολύμβηση)': 'Stroke rate and distance per stroke (clean swimming)',
  'Χεριές PB': 'Strokes PB',
  'Χεριές elite': 'Strokes elite',
  'Ανά 50{0}: χρόνος, ΔΑΚ, Swolf, συχνότητα': 'Per 50{0}: time, SPI, Swolf, stroke rate',
  'ΔΑΚ PB': 'SPI PB',
  'ΔΑΚ elite': 'SPI elite',
  'Τοίχος→+15 PB': 'Wall→+15 PB',
  'Τοίχος→+15': 'Wall→+15',
  'Τοίχος→+15 elite': 'Wall→+15 elite',
  'Χρόνος στροφής PB': 'Turn time PB',
  'Χρόνος στροφής': 'Turn time',
  'Υποβρύχια PB': 'Underwater PB',
  'Υποβρύχια': 'Underwater',
  'Δελφινισμοί PB': 'Dolphin kicks PB',
  'Εκκίνηση, φάσεις και τελικός χρόνος': 'Start, phases and final time',
  'Αντίδραση (s)': 'Reaction (s)',
  'Βατήρας (s)': 'Block (s)',
  'Είσοδος (s)': 'Entry (s)',
  'Πτήση (s)': 'Flight (s)',
  'Έξοδος (s)': 'Breakout (s)',
  'Εκκίνηση 0-15{0} (s)': 'Start 0-15{0} (s)',
  'Στροφές -5/+15 (s)': 'Turns -5/+15 (s)',
  'Τελευταία 5{0} (s)': 'Last 5{0} (s)',
  'Κολύμβηση (s)': 'Swimming (s)',
  'Τελικός χρόνος (s)': 'Final time (s)',
  'Αγώνας': 'Race',
  // Επίπεδα 2015–2024
  'Επίπεδα (Δεδομένα 2015–2024)': 'Levels (Data 2015–2024)',
  'Δείκτης': 'Indicator',
  'Αθλητής': 'Swimmer',
  'Επίπεδο αθλητή': 'Swimmer\'s level',
  'κάτω από {0}': 'below {0}',
  'Καλύτερη Επίδοση': 'Best performance',
  'Ολυμπ./Παγκόσμιο 1ος': 'Olympic/World 1st',
  'Junior μετάλλιο': 'Junior medal',
  'Εκκίνηση 0-15{0}': 'Start 0-15{0}',
  'Στροφή → +15{0} (μ.ό.)': 'Turn → +15{0} (avg.)',
  'Στροφή → +15{0} (καλύτερη)': 'Turn → +15{0} (best)',
  'Στροφή {0}: {1} → {2}': 'Turn {0}: {1} → {2}',
  'Για τα γραφήματα': 'For the charts',
  'Επίπεδο': 'Level',
  'Εκκίνηση: επίπεδα': 'Start: levels',
  'Εκκίνηση: αθλητής': 'Start: swimmer',
  'Στροφή: επίπεδα': 'Turn: levels',
  'Στροφή: αθλητής': 'Turn: swimmer',
  // Μικτή, σκυταλοδρομίες, τελικός, γραφικά του βίντεο
  'Διαδρομές της σκυταλοδρομίας': 'Legs of the relay',
  'Συμμετοχή των στυλ στον συνολικό χρόνο': 'Share of each stroke in the total time',
  'Διαδρομή': 'Leg',
  '{0}η διαδρομή': 'Leg {0}',
  '{0}η: {1}': 'Leg {0}: {1}',
  'Κατανομή ανά 50{0}: αθλητής – {1} ({2} φιναλίστ)': 'Pacing per 50{0}: swimmer – {1} ({2} finalists)',
  'Κατανομή ανά 50{0}: αθλητής – τελικός ({1} αναφορές της ίδιας πηγής)': 'Pacing per 50{0}: swimmer – final ({1} references of the same source)',
  'Μέσος όρος τελικού': 'Average of the final',
  'Καλύτερος': 'Best',
  'Δ από μ.ο. (s)': 'Δ from the average (s)',
  'Ο αγώνας ανάμεσα στους φιναλίστ: ': 'The race among the finalists: ',
  'Τιμή': 'Value',
  'Θέση': 'Place',
  'Νικητής': 'Winner',
  'Μέσος όρος': 'Average',
  'Από τον νικητή': 'From the winner',
  'Ο αγώνας με τα γραφικά του βίντεο (Kinovea)': 'The race with the graphics of the video (Kinovea)',
  'Ο αγώνας ανάμεσα στους φιναλίστ στο βίντεο: ': 'The race among the finalists on the video: ',
  'Ο αγώνας απέναντι στην αναφορά στο βίντεο': 'The race against the reference on the video',
  ' · καρέ στο ': ' · frame at ',
  'Τερματισμός στο βίντεο (Kinovea)': 'Finish on the video (Kinovea)',
  '(δεν βρέθηκε η εικόνα στο Drive)': '(the image was not found in the Drive)',
  // Γραφήματα
  'Γραφήματα': 'Charts',
  'Ταχύτητα ανά τμήμα (m/s)': 'Speed per segment (m/s)',
  'Συχνότητα χεριάς (κύκλοι/λεπτό)': 'Stroke rate (cycles/min)',
  'κύκλοι/λεπτό': 'cycles/min',
  'Μήκος χεριάς DPS (m/χεριά)': 'Distance per stroke DPS (m/stroke)',
  'm/χεριά': 'm/stroke',
  'Διαφορά χρόνου από PB και elite (s)': 'Time difference from PB and elite (s)',
  's (+ πιο αργά)': 's (+ slower)',
  'Δ από PB': 'Δ from PB',
  'Δ από elite': 'Δ from elite',
  'Συχνότητα – μήκος χεριάς – ταχύτητα': 'Stroke rate – distance per stroke – speed',
  'm/χεριά · m/s': 'm/stroke · m/s',
  'Συχνότητα': 'Stroke rate',
  'Μήκος χεριάς': 'Distance per stroke',
  'Ταχύτητα': 'Speed',
  'Καθαρή κολύμβηση: χρόνος και μεταβολή ταχύτητας': 'Clean swimming: time and speed change',
  'Μεταβολή ταχύτητας': 'Speed change',
  'Ανά 50{0}: χρόνος, χεριές και συχνότητα': 'Per 50{0}: time, strokes and stroke rate',
  's · χεριές': 's · strokes',
  'Swolf & ΔΑΚ ανά 50{0}': 'Swolf & SPI per 50{0}',
  'ΔΑΚ ανά 50{0}: PB – αγώνας – elite': 'SPI per 50{0}: PB – race – elite',
  'ΔΑΚ & ταχύτητα ανά 50{0}': 'SPI & speed per 50{0}',
  'Στροφές -5/+15{0}: PB – αγώνας – elite (s)': 'Turns -5/+15{0}: PB – race – elite (s)',
  'Στροφές τοίχος → +15{0}: PB – αγώνας – elite (s)': 'Turns wall → +15{0}: PB – race – elite (s)',
  'Στροφές: χρόνος -5/+15{0} και μέση ταχύτητα': 'Turns: -5/+15{0} time and average speed',
  'Χρόνος -5/+15': 'Time -5/+15',
  'Μέση ταχύτητα': 'Average speed',
  'Σύνθεση αγώνα': 'Race composition',
  'Φάσεις: PB – αγώνας – elite (s)': 'Phases: PB – race – elite (s)',
  'Εκκίνηση 0-15{0}: επίπεδα και αθλητής (s)': 'Start 0-15{0}: levels and swimmer (s)',
  'Επίπεδα 2015–2024': 'Levels 2015–2024',
  'Στροφή τοίχος → +15{0}: επίπεδα και αθλητής (s)': 'Turn wall → +15{0}: levels and swimmer (s)',
  'Στροφές μικτής: αθλητής και επίπεδα': 'IM turns: swimmer and levels',
  'Ολυμπ./Παγκ. 1ος': 'Olymp./World 1st',
  'Διαδρομές: ποσοστό του χρόνου': 'Legs: share of the time',
  'Συμμετοχή των στυλ στον χρόνο': 'Share of each stroke in the time',
  'Ανά 50{0}: αθλητής – μ.ο. τελικού – καλύτερος (s)': 'Per 50{0}: swimmer – average of the final – best (s)',
  // Τεχνική ανάλυση
  'Αντίδραση – πρώτη κίνηση': 'Reaction – first movement',
  'Χέρια φεύγουν από τον βατήρα': 'Hands leave the block',
  'Πίσω πόδι φεύγει': 'Back foot leaves',
  'Αποδέσμευση (μπροστινό πόδι)': 'Block release (front foot)',
  'Φάση βατήρα: πρώτη κίνηση → αποδέσμευση': 'Block phase: first movement → release',
  'Πτήση: αποδέσμευση → είσοδος': 'Flight: release → entry',
  'Είσοδος χεριών': 'Hand entry',
  'Απόσταση εισόδου χεριών': 'Hand entry distance',
  'Προσέγγιση: -5 → τοίχος': 'Approach: -5 → wall',
  'Τελευταία χεριά → τοίχος': 'Last stroke → wall',
  'Απόσταση τελευταίας χεριάς από τον τοίχο': 'Distance of the last stroke from the wall',
  'Περιστροφή: αρχή → επαφή': 'Rotation: start → contact',
  'Επαφή στον τοίχο: επαφή → αποκόλληση': 'Wall contact: contact → push-off',
  'Χρόνος στα 5': 'Time at 5',
  'Χρόνος στα 10': 'Time at 10',
  'Χρόνος στα 15': 'Time at 15',
  'Τοίχος → +5': 'Wall → +5',
  'Τοίχος → +10': 'Wall → +10',
  'Τοίχος → +15': 'Wall → +15',
  'Ταχύτητα 0 → 5': 'Speed 0 → 5',
  'Ταχύτητα 5 → 10': 'Speed 5 → 10',
  'Ταχύτητα 10 → 15': 'Speed 10 → 15',
  'Υποβρύχια φάση (έως την έξοδο)': 'Underwater phase (to the breakout)',
  'Υποβρύχιοι δελφινισμοί': 'Underwater dolphin kicks',
  'Συχνότητα δελφινισμών': 'Kick rate',
  'Απόσταση ανά δελφινισμό': 'Distance per kick',
  'Απόσταση εξόδου (κεφάλι)': 'Breakout distance (head)',
  'Μέση ταχύτητα υποβρύχιας φάσης': 'Average speed of the underwater phase',
  'Πρώτη χεριά μετά την έξοδο': 'First stroke after the breakout',
  'δελφ./s': 'kicks/s',
  'Γωνία: ': 'Angle: ',
  'Καλύτερη': 'Best',
  'Γωνίες': 'Angles',
  'Γωνία': 'Angle',
  'Μοίρες (°)': 'Degrees (°)',
  'Στιγμιότυπο': 'Snapshot',
  '(η εικόνα δεν έχει ανέβει ακόμη)': '(the image has not been uploaded yet)',
  'Τεχνική ανάλυση εκκίνησης και στροφών': 'Technical analysis of the start and the turns',
  'Εκκίνηση (χρόνοι από το σήμα)': 'Start (times from the signal)',
  'Στροφές (χρόνοι από την επαφή στον τοίχο)': 'Turns (times from the wall contact)',
  'Στιγμιότυπα και σχόλια': 'Snapshots and comments',
  'Τιμές ανά προσπάθεια (χρόνοι από το σήμα)': 'Values per attempt (times from the signal)',
  'Εκκινήσεις': 'Starts',
  'Τιμές ανά προσπάθεια (χρόνοι από την επαφή στον τοίχο)': 'Values per attempt (times from the wall contact)',
  'Δεδομένα των γραφημάτων (από τους πίνακες πιο πάνω)': 'Data of the charts (from the tables above)',
  'Προσπάθεια': 'Attempt',
  'Εκκινήσεις: χρόνοι από το σήμα': 'Starts: times from the signal',
  'Εκκινήσεις: χρόνοι στα 5 / 10 / 15{0} (s)': 'Starts: times at 5 / 10 / 15{0} (s)',
  'Στροφές: χρόνοι από την επαφή': 'Turns: times from the contact',
  'Στροφές: τοίχος → +5 / +10 / +15{0} (s)': 'Turns: wall → +5 / +10 / +15{0} (s)',
  'Έξοδος ({0})': 'Breakout ({0})',
  'Εκκινήσεις: υποβρύχια φάση': 'Starts: underwater phase',
  'Εκκινήσεις: δελφινισμοί και απόσταση εξόδου': 'Starts: dolphin kicks and breakout distance',
  'Στροφές: υποβρύχια φάση': 'Turns: underwater phase',
  'Στροφές: δελφινισμοί και απόσταση εξόδου': 'Turns: dolphin kicks and breakout distance',
  'Επαφή': 'Contact',
  'Περιστροφή': 'Rotation',
  'Στροφές: φάσεις της στροφής': 'Turns: phases of the turn',
  'Στροφές: χρόνος στροφής, επαφή και περιστροφή (s)': 'Turns: turn time, contact and rotation (s)',
  'δελφινισμοί': 'dolphin kicks',
  // Τιμές που έρχονται από το Kinovea στα ελληνικά (swimL_): στάδια και γεγονότα των στιγμιοτύπων, γωνίες, φάσεις, τελικός, φάσεις του αγώνα, φύλο
  'Στροφή': 'Turn',
  'Θέση ετοιμότητας': 'Ready position',
  'Φάση βατήρα': 'Block phase',
  'Αποδέσμευση': 'Release',
  'Πτήση': 'Flight',
  'Είσοδος στο νερό': 'Water entry',
  'Υποβρύχια φάση': 'Underwater phase',
  'Έξοδος από το νερό': 'Breakout',
  'Προσέγγιση': 'Approach',
  'Επαφή στον τοίχο': 'Wall contact',
  'Αποκόλληση': 'Push-off',
  'Σήμα εκκίνησης': 'Start signal',
  'Αντίδραση': 'Reaction',
  'Αποδέσμευση από τον βατήρα': 'Block release',
  'Αποκόλληση από τον τοίχο': 'Push-off from the wall',
  'Πρώτος δελφινισμός': 'First dolphin kick',
  'Τελευταία χεριά': 'Last stroke',
  'Πρώτη χεριά': 'First stroke',
  'Αρχή περιστροφής': 'Rotation start',
  'Γόνατο': 'Knee',
  'Ισχίο': 'Hip',
  'Κορμός': 'Trunk',
  'Αγκώνας': 'Elbow',
  'Ώμος': 'Shoulder',
  'Αστράγαλος': 'Ankle',
  'Κεφάλι': 'Head',
  'Γωνία εισόδου στο νερό': 'Water entry angle',
  'Γωνία ώθησης': 'Push-off angle',
  'Γωνία πτήσης': 'Flight angle',
  'Προκριματικός': 'Heats',
  'Ημιτελικός': 'Semi-final',
  'Τελικός': 'Final',
  'Ανδρών': 'Men',
  'Γυναικών': 'Women',
  'Περάσματα': 'Splits',
  'Μήκος κύκλου': 'Cycle length',
  'Αποκόλληση από τον βατήρα': 'Block release',
  '15μ. εκκίνηση': '15m start',
  'Έξοδος: χρόνος': 'Breakout: time',
  'Έξοδος: απόσταση': 'Breakout: distance',
  '5μ. πριν + 15μ. μετά': '5m before + 15m after',
  '15μ. μετά τον τοίχο': '15m after the wall',
  '5μ. πριν τον τοίχο': '5m before the wall',
  'Έξοδος: χρόνος από τον τοίχο': 'Breakout: time from the wall',
  'Τελευταία 5μ.': 'Last 5m',
  'χεριές': 'strokes',
  'κύκλοι/λ.': 'cycles/min',
  'μ./κύκλο': 'm/cycle',
  // 2.8.0: η πρώτη σελίδα του PDF «Με μια ματιά»
  'Νέο ατομικό ρεκόρ: {0} από τον καλύτερο χρόνο του αθλητή ({1}, {2}).': 'New personal best: {0} from the best time of the swimmer ({1}, {2}).',
  '{0} από τον καλύτερο χρόνο του αθλητή ({1}, {2}).': '{0} from the best time of the swimmer ({1}, {2}).',
  ' · η μεγαλύτερη διαφορά στο {0}–{1}{2} ({3})': ' · the largest difference in {0}–{1}{2} ({3})',
  'ΑΝΑΦΟΡΑ': 'REFERENCE',
  '{0} από την αναφορά {1} ({2}){3}.': '{0} from the reference {1} ({2}){3}.',
  '1ο μισό {0} · 2ο μισό {1} ({2}).': '1st half {0} · 2nd half {1} ({2}).',
  ' Μετά το 1ο 50: πιο γρήγορο το {0}–{1} ({2}), πιο αργό το {3}–{4} ({5}).': ' After the 1st 50: fastest {0}–{1} ({2}), slowest {3}–{4} ({5}).',
  'ΡΥΘΜΟΣ': 'PACING',
  '15{0} σε {1}': '15{0} in {1}',
  ' ({0} από την αναφορά)': ' ({0} from the reference)',
  'αντίδραση {0}': 'reaction {0}',
  'έξοδος από το νερό στα {0}{1}': 'breakout at {0}{1}',
  '{0} υποβρύχιοι δελφινισμοί': '{0} underwater dolphin kicks',
  'ΕΚΚΙΝΗΣΗ': 'START',
  'Στροφές (-5/+15): μέσος όρος {0} · η πιο γρήγορη στα {1}{2} ({3})': 'Turns (-5/+15): average {0} · fastest at {1}{2} ({3})',
  'Στροφή (-5/+15) στα {0}{1}: {2}': 'Turn (-5/+15) at {0}{1}: {2}',
  'Συχνότητα {0} κύκλοι/λεπτό': 'Stroke rate {0} cycles/min',
  'μήκος χεριάς {0} m': 'distance per stroke {0} m',
  ' · στο τελευταίο 50 η συχνότητα {0} κατά {1}': ' · in the last 50 the stroke rate {0} by {1}',
  'ανέβηκε': 'went up',
  'έπεσε': 'went down',
  'ΧΕΡΙΕΣ': 'STROKES',
  'ΤΑΧΥΤΗΤΑ': 'SPEED',
  'Η ταχύτητα της καθαρής κολύμβησης έπεσε {0}% από το καλύτερο τμήμα ({1}–{2}{3}: {4} m/s) στο τελευταίο ({5}–{6}{3}: {7} m/s).': 'The clean swimming speed fell {0}% from the best segment ({1}–{2}{3}: {4} m/s) to the last one ({5}–{6}{3}: {7} m/s).',
  'ΤΕΡΜΑΤΙΣΜΟΣ': 'FINISH',
  'Τελευταία 5{0}: {1}': 'Last 5{0}: {1}',
  ' · από την τελευταία χεριά στον τοίχο {0}': ' · from the last stroke to the wall {0}',
  'ΕΛΕΓΧΟΣ': 'CHECK',
  '{0} προειδοποιήσεις στα δεδομένα: δες την πλήρη αναφορά.': '{0} warnings in the data: see the full report.',
  'ΜΕ ΜΙΑ ΜΑΤΙΑ': 'AT A GLANCE',
  'Η πλήρης ανάλυση ακολουθεί στις επόμενες σελίδες': 'The full analysis follows on the next pages',
  'μέση ταχύτητα {0} m/s': 'average speed {0} m/s',
  'ΣΕ ΣΧΕΣΗ ΜΕ ΤΟ PB': 'VS PB',
  'χωρίς προηγούμενο αγώνα': 'no previous race',
  'ΣΕ ΣΧΕΣΗ ΜΕ ΤΗΝ ΑΝΑΦΟΡΑ': 'VS REFERENCE',
  'χωρίς αναφορά': 'no reference',
  '1Ο / 2Ο ΜΙΣΟ ({0}{1})': '1ST / 2ND HALF ({0}{1})',
  'διαφορά {0}': 'difference {0}',
  'σύνολο του αγώνα': 'whole race',
  'ΑΝΤΙΔΡΑΣΗ': 'REACTION',
  'ΕΚΚΙΝΗΣΗ 0-15{0}': 'START 0-15{0}',
  'από το σήμα': 'from the signal',
  'μ.ο. 50άρι': 'mean per 50',
  'ΣΤΡΟΦΕΣ -5/+15': 'TURNS -5/+15',
  'μέσος όρος (sec)': 'average (sec)',
  'ΤΕΛΕΥΤΑΙΑ 5{0}': 'LAST 5{0}',
  'Βασικά σημεία': 'Key points',
  'Ο τερματισμός': 'The finish',
  'Δεν υπάρχουν ακόμη αρκετά δεδομένα για τα βασικά σημεία.': 'Not enough data yet for the key points.',
  'Χρόνοι ανά σημείο ελέγχου': 'Times per checkpoint',
  'Χρόνοι ανά 50{0}': 'Times per 50{0}',
  'Χρόνοι ανά 100{0}': 'Times per 100{0}',
  'Ενδιάμεσος': 'Split',
  'Δ αναφοράς': 'Δ reference',
  'Χρόνος 15{0}': '15{0} time',
  'Τελευταία 5{0}': 'Last 5{0}',
  'Τελευταία 15{0}': 'Last 15{0}',
  'Χεριές του αγώνα': 'Strokes of the race',
  'Ακολουθεί η πλήρης ανάλυση του αγώνα · Swim Analysis {0}': 'The full analysis of the race follows · Swim Analysis {0}',
  'Χωρίς στροφές με χρόνους': 'No turns with times',
  'Στα {0}{1} (-5/+15)': 'At {0}{1} (-5/+15)',
  'στροφή {0}': 'turn {0}',
  'Μέσος όρος -5/+15': 'Average -5/+15',
  'Η πιο γρήγορη': 'Fastest',
  '{0} στα {1}{2}': '{0} at {1}{2}',
  'Η πιο αργή': 'Slowest',
  'Στροφές με χρόνο': 'Turns with a time'
};
