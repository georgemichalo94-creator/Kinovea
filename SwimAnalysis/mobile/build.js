// Puts the page together: src/page.html with the scripts of src/ inside it → app.html, the one file that is published.
'use strict';
const fs = require('fs'), path = require('path');
const src = f => fs.readFileSync(path.join(__dirname, 'src', f), 'utf8');
const strip = code => code.replace(/\nif \(typeof module !== 'undefined' && module\.exports\) module\.exports = \w+;\n?$/, '\n');
const order = ['horn.js', 'mp4.js', 'metrics.js', 'pdf.js', 'player.js', 'sound.js', 'report.js', 'app.js'];
const boot = `
// Start. A page that was open during an update comes back to the analysis it was showing.
(function () {
  var hot = window.claude && window.claude.hot;
  function start(data) { window.swimApp = swimAppStart(data || {}); }
  if (hot && hot.ready) hot.ready(start); else start((hot && hot.data) || {});
})();
`;
const scripts = order.map(f => '// ---- ' + f + ' ----\n' + strip(src(f)).trim()).join('\n\n') + '\n' + boot;
if (scripts.includes('</script')) throw new Error('a script closes the script tag');
const page = src('page.html').replace('/*SCRIPTS*/', () => scripts);
fs.writeFileSync(path.join(__dirname, 'app.html'), page);
console.log('app.html', (page.length / 1024).toFixed(1) + ' kB,', page.split('\n').length, 'lines');
