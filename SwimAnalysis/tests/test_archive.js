// 2.10.0: the report PDF in the sheet "Αρχεία Αθλητών/τριων" of the KOE App (one row per race, the registry number as profile_id,
// the file in the folder of the files of the app), and the PDF itself and the coaches' emails for the send package of Kinovea.
// Fictional names, numbers and emails only.
const { makeContext } = require('./mock_gas');
const assert = require('assert');

const athletes = [
  ['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ', 'ΕΤΟΣ ΓΕΝΝΗΣΗΣ', 'ΣΩΜΑΤΕΙΟ', 'EMAIL ΠΡΟΠΟΝΗΤΗ', 'EMAIL ΠΡΟΠΟΝΗΤΗ2'],
  [900001, 'ΔΟΚΙΜΗ ΑΛΦΑ', 'Γυναικών', 2008, 'Ο.Κ. ΔΟΚΙΜΩΝ', 'coach.one@example.org', 'Coach.Two@example.org; coach.one@example.org'],
  [900002, 'ΔΟΚΙΜΟΣ ΒΗΤΑ', 'Ανδρών', 2007, 'Ο.Κ. ΔΟΚΙΜΩΝ', 'not-an-email', ''],
];
const FILES = 'Αρχεία Αθλητών/τριων';
const PREFIX = 'Αρχεία Αθλητών τριων_Files_';
const GMT = 'Tue Oct 27 2026 10:00:00 GMT+0200 (Eastern European Standard Time)';

function setup(opts) {
  opts = opts || {};
  const G = makeContext({ athletes });
  G.swimShowConnectionInfo = () => {};
  G.swimSetup();
  if (opts.noSheet) return G;
  const sh = G._ss.insertSheet(opts.sheetName || FILES);
  const header = opts.header || ['unique_id', 'profile_id', 'Ημερομηνία εισαγωγής', 'Περιγραφή Αρχείου', 'Αρχείο'];
  sh.getRange(1, 1, 1, header.length).setValues([header]);
  (opts.rows || []).forEach((row, i) => sh.getRange(i + 2, 1, 1, row.length).setValues([row]));
  if (opts.folder !== false) {
    // The folder of the files of the app (AppSheet: appsheet/data/<app>/<table>_Files_), with a file uploaded from the app.
    const app = G._drive._root.createFolder('appsheet').createFolder('data').createFolder('KOEApp-1');
    G._filesFolder = app.createFolder(PREFIX);
    (opts.uploaded || []).forEach(n => G._filesFolder.createFile(G.Utilities.newBlob([1], 'application/pdf', n)));
    // Another folder with the same name elsewhere (e.g. a copy): the one of the uploaded file wins.
    if (opts.decoy) G._decoy = G._drive._root.createFolder(PREFIX);
  }
  G._files = sh;
  return G;
}

const call = (G, body) => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
const RACE_ID = '0123456789abcdef0123456789abcdef';
function race(extra) {
  return Object.assign({ id: RACE_ID, athlete: 'ΔΟΚΙΜΗ ΑΛΦΑ', athleteId: '900001', club: 'Ο.Κ. ΔΟΚΙΜΩΝ', style: 'fs', distance: 50, pool: 50,
    round: 'Τελικός', competition: 'Δοκιμαστικοί', date: '25.10.2026',
    checkpoints: [15, 25, 35, 45, 50].map((d, i) => ({ d, t: [6.1, 10.9, 16.0, 21.1, 23.8][i] })),
    strokes: [5, 5.6, 6.2, 6.8, 7.4, 8, 8.6, 9.2, 9.8, 10.4, 11, 11.6, 12.2, 12.8, 13.4, 14, 14.6, 15.2, 15.8, 16.4, 17, 17.6, 18.2, 18.8, 19.4, 20, 20.6, 21.2, 21.8, 22.4, 23],
    kicks: [], breakouts: [5.0], feetOff: [0.7], entries: [0.98], manual: {} }, extra || {});
}
const filesRows = G => G._files.getRange(2, 1, Math.max(1, G._files.getLastRow() - 1), G._files.getLastColumn()).getValues();

// 1. A new row: unique_id from the id of the race, the registry number (a number, as in the other rows), the date and the
// description written like the other rows, the file in the folder of the app with a name like the app's.
let G = setup({ rows: [['a1b2c3d4', 900002, GMT, 'ΑΡΧΕΙΟ ΑΝΑΛΥΣΗΣ ΑΓΩΝΑ', PREFIX + '/a1b2c3d4.Αρχείο.100000.pdf']],
  uploaded: ['a1b2c3d4.Αρχείο.100000.pdf'], decoy: true });
let r = call(G, { action: 'pdf', race: race() });
assert.ok(r.ok && r.pdf && r.pdf.url, JSON.stringify(r));
assert.ok(r.archive && r.archive.ok && !r.archive.updated && r.archive.inFolder, JSON.stringify(r.archive));
assert.strictEqual(r.archive.id, '01234567');
assert.strictEqual(r.pdf.data, undefined, 'no PDF data unless asked');
assert.strictEqual(r.coaches, undefined, 'no emails unless asked');
let rows = filesRows(G);
assert.strictEqual(rows.length, 2);
assert.strictEqual(rows[1][0], '01234567');
assert.strictEqual(rows[1][1], 900001, 'registry number as a number');
assert.ok(/GMT\+0200|GMT[+-]\d{4}/.test(String(rows[1][2])), 'date like String(new Date()): ' + rows[1][2]);
assert.strictEqual(rows[1][3], 'ΑΡΧΕΙΟ ΑΝΑΛΥΣΗΣ ΑΓΩΝΑ');
assert.ok(new RegExp('^' + PREFIX + '/01234567\\.Αρχείο\\.\\d{6}\\.pdf$').test(rows[1][4]), rows[1][4]);
const inFolder = G._filesFolder._files.map(f => f.getName());
assert.ok(inFolder.indexOf(rows[1][4].split('/')[1]) >= 0, 'file in the folder of the app: ' + inFolder.join(', '));
assert.strictEqual(G._decoy._files.length, 0, 'not in the other folder with the same name');
const copy = G._filesFolder._files.find(f => f.getName() === rows[1][4].split('/')[1]);
assert.ok(Buffer.from(copy.getBlob().getBytes()).toString('latin1').startsWith('%PDF'), 'the PDF itself');
assert.ok(G._props.SWIM_FILES_FOLDER_ID, 'folder remembered');
// The report keeps its own name in its folder (the copy has the name of the app).
assert.ok(G._drive.getFilesByName(r.pdf.name).hasNext(), 'report PDF kept with its name');

// 2. Again (a new PDF of the same race), with the PDF and the coaches' emails for the package: the same row, a new file.
r = call(G, { action: 'pdf', race: race(), pdfData: true, coaches: true });
assert.ok(r.ok && r.archive.ok && r.archive.updated, JSON.stringify(r.archive));
rows = filesRows(G);
assert.strictEqual(rows.length, 2, 'one row per race');
assert.ok(Buffer.from(r.pdf.data, 'base64').toString('latin1').startsWith('%PDF'), 'PDF in the answer');
assert.deepStrictEqual(r.coaches, ['coach.one@example.org', 'Coach.Two@example.org'], 'coach emails, valid and once');

// 3. The athlete corrected (same race): the same row moves to the other athlete; his invalid email is left out.
r = call(G, { action: 'pdf', race: race({ athlete: 'ΔΟΚΙΜΟΣ ΒΗΤΑ', athleteId: '900002' }), coaches: true });
assert.ok(r.archive.ok && r.archive.updated, JSON.stringify(r.archive));
rows = filesRows(G);
assert.strictEqual(rows.length, 2);
assert.strictEqual(rows[1][1], 900002);
assert.deepStrictEqual(r.coaches, []);

// 4. Not in the KOE registry (international athlete): no row, the PDF as always; archive: false: not even tried.
r = call(G, { action: 'pdf', race: race({ id: 'ffffffffffffffffffffffffffffffff', athleteId: 'WA123456', athlete: 'TESTER Anna' }), coaches: true });
assert.ok(r.ok && r.pdf.url && r.archive && !r.archive.ok && r.archive.reason === 'registry', JSON.stringify(r.archive));
assert.deepStrictEqual(r.coaches, []);
r = call(G, { action: 'pdf', race: race({ id: 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee' }), archive: false });
assert.ok(r.ok && r.archive === undefined);
assert.strictEqual(filesRows(G).length, 2);

// 5. A technique analysis: its own description. An update: no archive.
r = call(G, { action: 'pdf', race: race({ id: 'abcdefabcdefabcdefabcdefabcdefab', kind: 'training' }) });
assert.ok(r.archive.ok, JSON.stringify(r.archive));
rows = filesRows(G);
assert.strictEqual(rows[2][3], 'ΑΡΧΕΙΟ ΤΕΧΝΙΚΗΣ ΑΝΑΛΥΣΗΣ');
r = call(G, { action: 'update', race: race({ id: '11111111111111111111111111111111' }) });
assert.ok(r.ok && r.archive === undefined);

// 6. A row of the app with the same unique_id (not a report): the next code of the race.
G = setup({ rows: [['01234567', 900002, GMT, 'ΙΑΤΡΙΚΗ ΒΕΒΑΙΩΣΗ', PREFIX + '/01234567.Αρχείο.090000.pdf']], uploaded: ['01234567.Αρχείο.090000.pdf'] });
r = call(G, { action: 'pdf', race: race() });
assert.ok(r.archive.ok && r.archive.id === '89abcdef', JSON.stringify(r.archive));
assert.strictEqual(filesRows(G)[0][3], 'ΙΑΤΡΙΚΗ ΒΕΒΑΙΩΣΗ', 'the row of the app untouched');

// 7. Dates as dates of the sheet (and their format), a formula column carried on, an id that is not 32 hex (MD5).
G = setup({ header: ['unique_id', 'profile_id', 'Ημερομηνία εισαγωγής', 'Περιγραφή Αρχείου', 'Αρχείο', 'Όνομα'],
  rows: [['c0ffee00', 900002, new Date(2026, 9, 20, 9, 0, 0), 'ΑΡΧΕΙΟ ΑΝΑΛΥΣΗΣ ΑΓΩΝΑ', PREFIX + '/c0ffee00.Αρχείο.090000.pdf', '=VLOOKUP(R[0]C[-4],x,2,0)']],
  uploaded: ['c0ffee00.Αρχείο.090000.pdf'] });
G._files.getRange(2, 3).setNumberFormat('dd/mm/yyyy hh:mm');
r = call(G, { action: 'pdf', race: race({ id: 'race-7' }) });
assert.ok(r.archive.ok, JSON.stringify(r.archive));
rows = filesRows(G);
assert.ok(rows[1][2] instanceof Date, 'a date of the sheet');
assert.strictEqual(G._files.getRange(3, 3).getNumberFormat(), 'dd/mm/yyyy hh:mm', 'same date format');
assert.strictEqual(G._files.getRange(3, 6).getFormulasR1C1()[0][0], '=VLOOKUP(R[0]C[-4],x,2,0)', 'formula carried on');
assert.ok(/^[0-9a-f]{8}$/.test(r.archive.id), r.archive.id);
assert.strictEqual(r.archive.id, require('crypto').createHash('md5').update('race-7').digest('hex').substring(0, 8));

// 8. No folder of the app anywhere: the link of the PDF in Drive (it opens from the app too).
G = setup({ folder: false });
r = call(G, { action: 'pdf', race: race() });
assert.ok(r.archive.ok && !r.archive.inFolder, JSON.stringify(r.archive));
rows = filesRows(G);
assert.strictEqual(rows[0][4], r.pdf.url);
assert.ok(rows[0][2] instanceof Date, 'no other rows: a date of the sheet');

// 9. No sheet "Αρχεία Αθλητών/τριων": the PDF as always, with the reason.
G = setup({ noSheet: true });
r = call(G, { action: 'pdf', race: race(), pdfData: true });
assert.ok(r.ok && r.pdf.url && r.pdf.data && !r.archive.ok && r.archive.reason === 'sheet', JSON.stringify(r.archive));

// 10. The sheet found by its name without the slash and accents; English messages.
G = setup({ sheetName: 'Αρχεια Αθλητων τριων', folder: false });
r = call(G, { action: 'pdf', race: race() });
assert.ok(r.archive.ok, JSON.stringify(r.archive));
r = call(G, { action: 'pdf', lang: 'en', race: race({ id: 'dddddddddddddddddddddddddddddddd', athleteId: '' }) });
assert.ok(!r.archive.ok && /not in the KOE registry/.test(r.archive.message), JSON.stringify(r.archive));

console.log('test_archive OK');
