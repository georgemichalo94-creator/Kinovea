// The international athletes (2.7.6): unique codes in their own sheet, duplicates, other spellings of the registry's names.
// Fictional names only.
const { makeContext } = require('./mock_gas');
const assert = require('assert');
const athletes = [
  ['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ', 'ΕΤΟΣ ΓΕΝΝΗΣΗΣ', 'ΣΩΜΑΤΕΙΟ'],
  [900001, 'ΔΟΚΙΜΗ ΑΛΦΑ', 'Γυναικών', 2008, 'Ο.Κ. ΔΟΚΙΜΩΝ'],
  [900002, 'ΔΟΚΙΜΟΣ ΒΗΤΑ', 'Ανδρών', 2007, 'Ο.Κ. ΔΟΚΙΜΩΝ'],
];
const G = makeContext({ athletes });
const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);

// Before any: the registry only, no aliases.
let a = call({ action: 'athletes' });
assert.ok(a.ok, JSON.stringify(a));
assert.strictEqual(a.athletes.length, 2);
assert.deepStrictEqual(a.athletes[0].aliases, []);

// With a World Aquatics code: WA + code; again: the same.
let r = call({ action: 'addAthlete', athlete: { name: 'TESTER Anna', nation: 'ita', gender: 'Γυναικών', birthYear: '2007', waId: '123456' } });
assert.ok(r.ok && r.created && r.id === 'WA123456', JSON.stringify(r));
r = call({ action: 'addAthlete', athlete: { name: 'Anna TESTER', nation: 'ITA', waId: '123456' } });
assert.ok(r.ok && r.existed && r.id === 'WA123456', JSON.stringify(r));

// Without: X-<NAT>-0001, then the next one of the nation.
r = call({ action: 'addAthlete', athlete: { name: 'EXAMPLE Bruno', nation: 'FRA', birthYear: '2006' } });
assert.ok(r.ok && r.id === 'X-FRA-0001', JSON.stringify(r));
r = call({ action: 'addAthlete', athlete: { name: 'SAMPLE Carl', nation: 'FRA', birthYear: '2005' } });
assert.ok(r.ok && r.id === 'X-FRA-0002', JSON.stringify(r));

// Same name, nation and year of birth (words in another order, accents): the same athlete.
r = call({ action: 'addAthlete', athlete: { name: 'Bruno Exámple', nation: 'FRA', birthYear: '2006' } });
assert.ok(r.ok && r.existed && r.id === 'X-FRA-0001', JSON.stringify(r));

// Same name and nation, the year unknown: asked (duplicates), then forced as a new one.
r = call({ action: 'addAthlete', athlete: { name: 'SAMPLE Carl', nation: 'FRA' } });
assert.ok(!r.ok && r.duplicates && r.duplicates.length === 1 && r.duplicates[0].id === 'X-FRA-0002', JSON.stringify(r));
r = call({ action: 'addAthlete', force: true, athlete: { name: 'SAMPLE Carl', nation: 'FRA' } });
assert.ok(r.ok && r.id === 'X-FRA-0003', JSON.stringify(r));

// Same name, another nation or another year: another athlete.
r = call({ action: 'addAthlete', athlete: { name: 'EXAMPLE Bruno', nation: 'BEL', birthYear: '2006' } });
assert.ok(r.ok && r.id === 'X-BEL-0001', JSON.stringify(r));
r = call({ action: 'addAthlete', athlete: { name: 'EXAMPLE Bruno', nation: 'FRA', birthYear: '2001' } });
assert.ok(r.ok && r.created && r.id === 'X-FRA-0004', JSON.stringify(r));

// Refused: no nation, Greece (the registry), no name.
assert.strictEqual(call({ action: 'addAthlete', athlete: { name: 'NOBODY', nation: '' } }).ok, false);
assert.strictEqual(call({ action: 'addAthlete', athlete: { name: 'DOKIMI Alfa', nation: 'GRE' } }).ok, false);
assert.strictEqual(call({ action: 'addAthlete', athlete: { name: '', nation: 'ITA' } }).ok, false);

// Other spellings: of the registry (own sheet), of an international athlete (his row); once each.
r = call({ action: 'addAlias', id: '900001', alias: 'DOKIMI Alfa' });
assert.ok(r.ok && r.created, JSON.stringify(r));
r = call({ action: 'addAlias', id: '900001', alias: 'Alfa  Dokimi' });
assert.ok(r.ok && r.existed, JSON.stringify(r));
r = call({ action: 'addAlias', id: 'WA123456', alias: 'TESTER-ROSSI Anna' });
assert.ok(r.ok && r.created, JSON.stringify(r));
r = call({ action: 'addAlias', id: 'WA123456', alias: 'Anna TESTER' });
assert.ok(r.ok && r.existed, JSON.stringify(r));

// The list: the registry with its aliases, then the international athletes (nation, World Aquatics code, aliases).
a = call({ action: 'athletes' });
assert.ok(a.ok, JSON.stringify(a));
assert.strictEqual(a.athletes.length, 2 + 6, a.athletes.map(x => x.id).join(','));
assert.deepStrictEqual(a.athletes.find(x => x.id === '900001').aliases, ['DOKIMI Alfa']);
const wa = a.athletes.find(x => x.id === 'WA123456');
assert.ok(wa.intl && wa.nation === 'ITA' && wa.waId === '123456' && wa.birthYear === '2007', JSON.stringify(wa));
assert.deepStrictEqual(wa.aliases, ['TESTER-ROSSI Anna']);
assert.ok(G._ss.getSheetByName('Διεθνείς αθλητές') && G._ss.getSheetByName('Άλλες γραφές ονομάτων'));
console.log('test_intl: ok');
