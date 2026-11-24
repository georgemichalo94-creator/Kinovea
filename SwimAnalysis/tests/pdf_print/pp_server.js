// pp_server.js (2.38.0): the web app of the KOE App for the CI check of the PDF of the 2nd version from end to end: the script itself
// (SwimRaceAnalysis.gs with SwimReportV2.gs, on the mock of Apps Script) answering the requests of the Kinovea of the CI over HTTP, as
// the web app of Google does. Fictional races only. GET /log: what came (action, size, the pages of a PDF to keep, the order).
// A request with "slow" in its race id waits 3 s (a request in flight while others come).
//   node pp_server.js <port>
'use strict';
const http = require('http');
const { makeContext } = require('../mock_gas');

const port = +(process.argv[2] || 18734);
const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [990001, 'Αθλητής Δείγμα', 'Ανδρών']] });
G.swimShowConnectionInfo = () => {};
G.swimSetup();
const log = [];

const pages = b64 => {
  try { return (Buffer.from(String(b64 || ''), 'base64').toString('latin1').match(/\/Type\s*\/Page(?![A-Za-z])/g) || []).length; } catch (e) { return -1; }
};

http.createServer((req, res) => {
  if (req.method === 'GET' && req.url.startsWith('/log')) {
    res.setHeader('Content-Type', 'application/json');
    res.end(JSON.stringify(log));
    return;
  }
  const chunks = [];
  req.on('data', c => chunks.push(c));
  req.on('end', () => {
    const body = Buffer.concat(chunks).toString('utf8');
    let o = {};
    try { o = JSON.parse(body); } catch (e) { /* the script answers it */ }
    const race = o.race || {};
    const item = { action: o.action, raceId: o.raceId || race.id || '', bytes: body.length, v2: o.v2 === true, at: Date.now() };
    if (o.action === 'pdfStore') { item.pdfPages = pages(o.pdf); item.pdfShortPages = o.pdfShort ? pages(o.pdfShort) : 0; }
    const answer = () => {
      let text;
      try {
        text = G.swimDoPost({ postData: { contents: body } }).text;
      } catch (e) {
        text = JSON.stringify({ ok: false, error: String(e && e.message || e) });
      }
      try {
        const r = JSON.parse(text);
        item.ok = !!r.ok;
        item.error = r.error || '';
        item.answerV2 = !!r.v2;
        item.v2Pages = r.v2 ? r.v2.pages : 0;
        item.pdfUrl = r.pdf ? r.pdf.url : '';
      } catch (e) { item.ok = false; item.error = 'not JSON'; }
      log.push(item);
      res.setHeader('Content-Type', 'application/json; charset=utf-8');
      res.end(text);
    };
    if (String(item.raceId).indexOf('slow') >= 0) setTimeout(answer, 3000); else answer();
  });
}).listen(port, '127.0.0.1', () => console.log('pp_server on ' + port));
