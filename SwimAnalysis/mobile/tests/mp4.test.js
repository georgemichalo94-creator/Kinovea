// Checks of the tables read from MP4 / MOV files, against ffprobe and ffmpeg on the synthetic clips of gen_clips.sh.
// Usage: node mp4.test.js <folder of the clips>
'use strict';
const fs = require('fs'), path = require('path'), { execFileSync } = require('child_process');
const mp4 = require('../src/mp4.js')(), horn = require('../src/horn.js')();
const dir = process.argv[2];
if (!dir) { console.error('usage: node mp4.test.js <folder of the clips>'); process.exit(2); }
let failed = 0;
function check(name, ok, detail) { console.log((ok ? 'ok   ' : 'FAIL ') + name + (detail ? '  ' + detail : '')); if (!ok) failed++; }

function nodeReader(file) {
  const fd = fs.openSync(file, 'r'), size = fs.statSync(file).size;
  let reads = 0, bytes = 0;
  return {
    size, stats: () => ({ reads, bytes }),
    read: async (from, to) => {
      const len = Math.max(0, Math.min(size, to) - from), buf = Buffer.alloc(len);
      fs.readSync(fd, buf, 0, len, from); reads++; bytes += len;
      return buf.buffer.slice(buf.byteOffset, buf.byteOffset + len);
    }
  };
}
function probeFrames(file) {
  const out = execFileSync('ffprobe', ['-v', 'error', '-select_streams', 'v:0', '-show_entries', 'frame=best_effort_timestamp_time', '-of', 'csv=p=0', file], { maxBuffer: 1 << 26 }).toString();
  return out.split('\n').map(s => parseFloat(s)).filter(v => !isNaN(v)).sort((a, b) => a - b);
}
function probeAudio(file) {
  const out = execFileSync('ffprobe', ['-v', 'error', '-select_streams', 'a:0', '-show_entries', 'stream=sample_rate,channels', '-of', 'csv=p=0', file]).toString().trim();
  return out ? { rate: +out.split(',')[0], channels: +out.split(',')[1] } : null;
}
// The signals of the clips: at 2.0137 s of their sound (1.0137 s in the short one), 0.1 s later where the sound is
// delayed; two of them in one clip, none in another (a clap only).
function signalsAt(name) {
  if (name.startsWith('i_')) return [1.5071, 4.0137];
  if (name.startsWith('k_')) return [];
  return [(name.startsWith('e_') ? 1.0137 : 2.0137) + (name.startsWith('f_') ? 0.1 : 0)];
}

(async () => {
  for (const name of fs.readdirSync(dir).filter(f => /\.(mp4|mov)$/.test(f)).sort()) {
    const file = path.join(dir, name), reader = nodeReader(file), r = await mp4.parse(reader);
    if (!r || !r.video) { check(name + ': tables read', false); continue; }
    const truth = probeFrames(file), t = r.video.times;
    let worst = 0;
    for (let i = 0; i < Math.min(t.length, truth.length); i++) worst = Math.max(worst, Math.abs(t[i] - truth[i]));
    check(`${name}: ${t.length} frames as ffprobe`, t.length === truth.length && worst < 2e-6,
      `ffprobe ${truth.length}, worst ${(worst * 1e6).toFixed(1)} µs, ${r.video.fps.toFixed(3)} fps, uneven ${(r.video.uneven * 100).toFixed(0)} %, first ${t[0].toFixed(4)}`);

    const a = r.audio, pa = probeAudio(file);
    if (!pa) { check(`${name}: no sound track`, a === null); continue; }
    check(`${name}: sound track`, !!a && a.rate === pa.rate && a.config.channels === pa.channels,
      a ? `rate ${a.rate} ch ${a.config.channels} first sample at ${a.start.toFixed(5)} s, ${a.frames} frames` : 'none');
    if (!a) continue;

    const before = reader.stats(), sound = await mp4.readSound(reader, a, 1000), after = reader.stats();
    const ref = path.join(dir, 'ref_' + name + '.aac');
    execFileSync('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-y', '-i', file, '-vn', '-c:a', 'copy', '-f', 'adts', ref]);
    const want = fs.readFileSync(ref);
    check(`${name}: sound as ffmpeg copies it`, !!sound && Buffer.compare(Buffer.from(sound.bytes), want) === 0,
      sound ? `${sound.bytes.length} bytes (ffmpeg ${want.length}), ${sound.frames} frames in ${after.reads - before.reads} reads` : 'none');

    // The whole way: the sound decoded as it is in the file (every frame, nothing trimmed), the signal heard in it,
    // and its time in the film from the tables.
    const pcm = execFileSync('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-i', ref, '-f', 'f32le', '-ac', '1', '-ar', String(a.rate), 'pipe:1'], { maxBuffer: 1 << 27 });
    const samples = new Float32Array(pcm.buffer, pcm.byteOffset, Math.floor(pcm.length / 4));
    const choice = horn.choose(horn.find(horn.to16k(samples, a.rate))), signals = signalsAt(name);
    const heard = choice.list.map(b => b.onset + a.start).sort((p, q) => p - q);
    check(`${name}: every frame decoded`, samples.length === sound.frames * 1024, `${samples.length} samples, ${sound.frames} × 1024 = ${sound.frames * 1024}`);
    check(`${name}: ${signals.length} signal${signals.length === 1 ? '' : 's'} heard${signals.length ? ' at ' + signals.map(v => v.toFixed(4)).join(', ') + ' s of the film' : ''}`,
      heard.length === signals.length && signals.every((v, n) => Math.abs(heard[n] - v) < 0.004) && choice.clear === (signals.length === 1),
      heard.map((v, n) => `${v.toFixed(4)} (${((v - signals[n]) * 1000).toFixed(1)} ms)`).join(', ') + (choice.best ? `, score ${choice.best.score.toFixed(1)}, taken without asking: ${choice.clear}` : 'none'));

    const part = await mp4.readSound(reader, a, 1.0);
    check(`${name}: the first second only`, !!part && part.frames === Math.ceil(a.rate / 1024) && Buffer.compare(Buffer.from(part.bytes), want.subarray(0, part.bytes.length)) === 0,
      part ? `${part.frames} frames` : 'none');
  }

  // A file that is not MP4, and a cut one: no tables, no exception.
  const webm = path.join(dir, 'g_vp9.webm');
  if (fs.existsSync(webm)) check('webm: no tables', (await mp4.parse(nodeReader(webm))) === null);
  const cut = path.join(dir, 'cut.mp4');
  fs.writeFileSync(cut, fs.readFileSync(path.join(dir, 'a_60_bf.mp4')).subarray(0, 300000));
  let r = 'threw';
  try { r = await mp4.parse(nodeReader(cut)); } catch (e) { r = 'threw ' + e; }
  check('cut file: no tables', r === null, String(r && r.video ? 'video' : r));
  fs.unlinkSync(cut);

  console.log(failed ? `\n${failed} FAILED` : '\nall ok');
  process.exit(failed ? 1 : 0);
})();
