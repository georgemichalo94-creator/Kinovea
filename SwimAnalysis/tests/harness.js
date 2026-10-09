// Loads the Apps Script file into a Node VM and exposes the pure functions.
const fs = require('fs');
const vm = require('vm');
const path = require('path');
const src = fs.readFileSync(path.join(__dirname, '../GoogleSheet/SwimRaceAnalysis.gs'), 'utf8');
const ctx = {
  console,
  Utilities: { formatDate: () => '01/01/2026 00:00:00', getUuid: () => 'uuid' },
  Session: { getScriptTimeZone: () => 'Europe/Athens' },
};
vm.createContext(ctx);
vm.runInContext(src, ctx);
module.exports = ctx;
