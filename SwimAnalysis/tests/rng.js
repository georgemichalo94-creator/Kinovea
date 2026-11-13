// The random numbers of the fuzz tests, from a fixed seed (mulberry32: whole numbers of 32 bits, a period of 2^32). The generator
// before, (seed * 1103515245 + 12345) % 2^31 in the doubles of JavaScript, lost the low bits of its product and fell into a cycle of
// 10 466 numbers after a few thousand, whatever the seed: the "random" analyses repeated after some dozens.
function makeRandom(seed) {
  let a = seed >>> 0;
  return function () {
    a = (a + 0x6D2B79F5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

module.exports = { makeRandom };
