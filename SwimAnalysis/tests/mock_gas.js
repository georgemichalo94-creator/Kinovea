// Minimal mock of the Apps Script services, to run doPost end-to-end and catch runtime errors.
const fs = require('fs'); const vm = require('vm'); const path = require('path');
const src = fs.readFileSync(path.join(__dirname, '../GoogleSheet/SwimRaceAnalysis.gs'), 'utf8');

function makeSheet(ss, name, id) {
  const cells = {}; const merges = []; const charts = []; const images = [];
  let maxRows = 1000, maxCols = 26;
  const sheet = {
    _cells: cells, _charts: charts, _images: images, _name: name,
    getName: () => sheet._name, setName: n => { sheet._name = n; },
    getSheetId: () => id, getParent: () => ss,
    getMaxRows: () => maxRows, getMaxColumns: () => maxCols,
    insertRowsAfter: (a, n) => { maxRows += n; }, insertColumnsAfter: (a, n) => { maxCols += n; },
    deleteRows: (row, n) => {
      const moved = {};
      for (const k in cells) {
        const [r, c] = k.split(':').map(Number);
        if (r < row) moved[k] = cells[k];
        else if (r >= row + n) moved[(r - n) + ':' + c] = cells[k];
      }
      for (const k in cells) delete cells[k];
      Object.assign(cells, moved);
      maxRows -= n;
    },
    getLastRow: () => { let m = 0; for (const k in cells) { const r = +k.split(':')[0]; if (cells[k].v !== '' && cells[k].v !== null && r > m) m = r; } return m; },
    getLastColumn: () => { let m = 0; for (const k in cells) { const c = +k.split(':')[1]; if (cells[k].v !== '' && cells[k].v !== null && c > m) m = c; } return m; },
    clear: () => { for (const k in cells) delete cells[k]; },
    getCharts: () => charts.slice(), removeChart: c => charts.splice(charts.indexOf(c), 1),
    insertChart: c => charts.push(c),
    newChart: () => {
      const b = { ranges: [], opts: {}, type: null, transpose: false, pos: null };
      const api = {
        asLineChart: () => { b.type = 'line'; return api; }, asColumnChart: () => { b.type = 'column'; return api; },
        asPieChart: () => { b.type = 'pie'; return api; }, asComboChart: () => { b.type = 'combo'; return api; },
        setChartType: t => { b.type = t; return api; }, setTransposeRowsAndColumns: v => { b.transpose = v; return api; },
        addRange: r => { if (!r || !r._nr) throw new Error('bad chart range'); b.ranges.push(r); return api; },
        setOption: (k, v) => { if (v === undefined) throw new Error('undefined chart option ' + k); b.opts[k] = v; return api; },
        setPosition: (r, c) => { b.pos = [r, c]; return api; }, build: () => b,
      };
      return api;
    },
    getImages: () => images.slice(),
    insertImage: (blob, col, row) => {
      if (!blob || !blob._bytes) throw new Error('insertImage needs a blob');
      const im = { blob, col, row, w: null, h: null };
      im.setWidth = w => { im.w = w; return im; }; im.setHeight = h => { im.h = h; return im; };
      im.remove = () => { images.splice(images.indexOf(im), 1); };
      images.push(im);
      return im;
    },
    getRangeList: list => { if (!Array.isArray(list) || !list.length) throw new Error('bad rangelist'); return { setNumberFormat: f => { if (typeof f !== 'string') throw new Error('fmt'); } }; },
    setRowHeight: () => {}, setColumnWidth: () => {}, setHiddenGridlines: () => {}, setFrozenRows: () => {}, hideColumns: () => {},
    getRange: function (a, b, c, d) {
      let r, col, nr = 1, nc = 1;
      if (typeof a === 'string') {
        const m = a.match(/^([A-Z]+)(\d+)(?::([A-Z]+)(\d+))?$/);
        const L = s => s.split('').reduce((x, ch) => x * 26 + ch.charCodeAt(0) - 64, 0);
        r = +m[2]; col = L(m[1]); if (m[3]) { nr = +m[4] - r + 1; nc = L(m[3]) - col + 1; }
      } else { r = a; col = b; nr = c || 1; nc = d || 1; }
      const rng = {
        _r: r, _c: col, _nr: nr, _nc: nc,
        getValues: () => Array.from({ length: nr }, (_, i) => Array.from({ length: nc }, (_, j) => { const x = cells[(r + i) + ':' + (col + j)]; return x ? (String(x.v).startsWith('=') ? 0 : x.v) : ''; })),
        getDisplayValues: () => Array.from({ length: nr }, (_, i) => Array.from({ length: nc }, (_, j) => { const x = cells[(r + i) + ':' + (col + j)]; return x ? String(x.v) : ''; })),
        getFormulas: () => Array.from({ length: nr }, (_, i) => Array.from({ length: nc }, (_, j) => { const x = cells[(r + i) + ':' + (col + j)]; return x && String(x.v).startsWith('=') ? x.v : ''; })),
        getValue: () => { const x = cells[r + ':' + col]; return x ? x.v : ''; },
        getRow: () => r,
        setValues: vals => { if (vals.length !== nr || vals[0].length !== nc) throw new Error(`setValues size ${vals.length}x${vals[0].length} != ${nr}x${nc}`); vals.forEach((row, i) => row.forEach((v, j) => { cells[(r + i) + ':' + (col + j)] = { v }; })); return rng; },
        setValue: v => { cells[r + ':' + col] = { v }; return rng; },
        merge: () => { merges.push([r, col, nr, nc]); return rng; }, breakApart: () => rng,
      };
      ['setNumberFormats','setBackgrounds','setFontWeights','setFontStyles','setFontColors','setFontSizes','setHorizontalAlignments','setWraps','setVerticalAlignments'].forEach(fn => {
        rng[fn] = arr => { if (arr.length !== nr || arr[0].length !== nc) throw new Error(fn + ' size mismatch'); return rng; };
      });
      ['setVerticalAlignment','setFontFamily','setBorder','setFontWeight','setBackground','setNote','setNumberFormat'].forEach(fn => { rng[fn] = () => rng; });
      return rng;
    },
  };
  return sheet;
}

function makeBlob(bytes, type, name) {
  const blob = { _bytes: bytes || [], _type: type || 'application/octet-stream', _name: name || '' };
  blob.setName = n => { blob._name = n; return blob; };
  blob.getBytes = () => blob._bytes; blob.getContentType = () => blob._type; blob.getName = () => blob._name;
  return blob;
}
const iter = arr => { let i = 0; return { hasNext: () => i < arr.length, next: () => arr[i++] }; };
// Drive: φάκελοι με αρχεία (id 33 χαρακτήρων όπως στο Drive), ώστε να ελέγχονται τα στιγμιότυπα και τα elite καρέ.
function makeDrive() {
  const files = {}; let seq = 0;
  const newId = p => (p + String(++seq)).padEnd(33, 'x');
  function makeFolder(name, parent) {
    const id = newId('folder');
    const folder = { _name: name, _files: [], _folders: [], getId: () => id, getName: () => name, getUrl: () => 'https://drive/folders/' + id,
      getFoldersByName: n => iter(folder._folders.filter(f => f._name === n)),
      createFolder: n => { const f = makeFolder(n, folder); folder._folders.push(f); return f; },
      getFilesByName: n => iter(folder._files.filter(f => f.getName() === n && !f._trashed)),
      createFile: blob => {
        const fid = newId('file');
        const f = { _blob: blob, _trashed: false, getId: () => fid, getName: () => blob._name || 'file', getUrl: () => 'https://drive.google.com/file/d/' + fid + '/view',
          getBlob: () => blob, setTrashed: v => { f._trashed = v; }, getParents: () => iter([folder]), moveTo: () => {} };
        folder._files.push(f); files[fid] = f; return f;
      } };
    return folder;
  }
  const root = makeFolder('root', null);
  const drive = {
    _files: files, _root: root,
    getFileById: id => { if (files[id]) return files[id]; return { moveTo: () => {}, getId: () => id, getParents: () => iter([]), getBlob: () => makeBlob([1, 2, 3], 'image/jpeg', 'x'), getName: () => 'x' }; },
    getFoldersByName: n => iter(root._folders.filter(f => f._name === n)),
    createFolder: n => root.createFolder(n),
  };
  return drive;
}

let nextId = 100;
function makeSpreadsheet(name, id) {
  const sheets = [];
  const ss = {
    getName: () => name, getUrl: () => 'https://docs.google.com/spreadsheets/d/' + id + '/edit', getId: () => id,
    getSheetByName: n => sheets.find(s => s.getName() === n) || null,
    insertSheet: (n, idx) => { if (sheets.find(s => s.getName() === n)) throw new Error('dup sheet ' + n); const s = makeSheet(ss, n, nextId++); sheets.push(s); return s; },
    getSheets: () => sheets,
    deleteSheet: sh => sheets.splice(sheets.indexOf(sh), 1),
  };
  return ss;
}
function makeContext(opts) {
  opts = opts || {};
  const ss = makeSpreadsheet('KOE App', 'DATA');
  const all = { DATA: ss };
  if (opts.athletes) {
    const sh = ss.insertSheet('Στοιχεία Αθλητών/τριων');
    opts.athletes.forEach((row, i) => sh.getRange(i + 1, 1, 1, row.length).setValues([row]));
  }
  const triggers = [];
  const props = { SWIM_TOKEN: 'tok' };
  let uuidSeq = 0;
  const drive = makeDrive();
  const ctx = {
    console, JSON, Math, Date, Object, Array, String, Number, isFinite,
    SpreadsheetApp: { getActiveSpreadsheet: () => ss, flush: () => {}, BorderStyle: { SOLID: 'solid' },
      openById: id => { if (!all[id]) throw new Error('no ss ' + id); return all[id]; },
      create: name => { const n = makeSpreadsheet(name, 'R' + Object.keys(all).length); n.insertSheet('Sheet1'); all[n.getId()] = n; return n; } },
    Logger: { log: () => {} },
    PropertiesService: { getScriptProperties: () => ({ getProperty: k => props[k] || null, setProperty: (k, v) => { props[k] = v; } }) },
    LockService: { getScriptLock: () => ({ waitLock: () => {}, releaseLock: () => {} }) },
    ContentService: { createTextOutput: t => ({ text: t, setMimeType() { return this; } }), MimeType: { JSON: 'json' } },
    Utilities: { formatDate: () => '09/10/2026 23:59:00', getUuid: () => 'uuid-' + (++uuidSeq) + '-0000',
      base64Decode: d => Array.from(Buffer.from(String(d), 'base64')), base64Encode: b => Buffer.from(b).toString('base64'),
      newBlob: (bytes, type, name) => makeBlob(bytes, type, name) },
    Charts: { ChartType: { RADAR: 'radar' } },
    Session: { getScriptTimeZone: () => 'Europe/Athens' },
    DriveApp: drive,
    UrlFetchApp: { fetch: () => ({ getResponseCode: () => 200, getBlob: () => ({ setName() { return this; } }) }) },
    ScriptApp: { getOAuthToken: () => 'oauth', getProjectTriggers: () => triggers, deleteTrigger: t => triggers.splice(triggers.indexOf(t), 1),
      newTrigger: fn => ({ forSpreadsheet: () => ({ onOpen: () => ({ create: () => triggers.push({ getHandlerFunction: () => fn }) }) }) }) },
  };
  vm.createContext(ctx);
  vm.runInContext(src, ctx);
  ctx._ss = ss; ctx._all = all; ctx._triggers = triggers; ctx._drive = drive;
  return ctx;
}
module.exports = { makeContext };
