// The sound of a video in the browser: read from its place in the file (mp4.js), decoded, and searched for the start
// signal (horn.js). The time of every sample in the film is known from the tables of the file, and from a tiny sound
// with a click at a known place that tells how many samples the decoder of this phone drops at the start of a stream.
function swimSoundFactory(Mp4, hornFactory) {
  'use strict';

  var Horn = hornFactory();

  // 0.3 s of silence at 48 kHz with one click, as AAC (ADTS). Decoded with nothing trimmed, the click is at sample 8224.
  var CLICK = '//FMQAOf/N4CAExhdmM2MC4zMS4xMDIAAjBADv/xTEABf/wBGCAH//FMQAF//AEYIAf/8UxAAX/8ARggB//xTEABf/wBGCAH//FMQAF//AEYIAf/8UxAAX/8ARhAB//xTEAdn/wBGJuMPr+v4PgH319L+//9wB94/H0CcABAMDAHkxPs9pATbEWTCus0EAJoAudzSuLKgvHP4e9/OJUeTQ6WzEADlwn6fYZcI40IgCThxCRAEIULARk2M/DEBi0zo+e8X7EwAPIpMYOPsAFq37PlYpBht6M7rFgZLcOQUElAvErrSMO8RtJJRLhKsUjEsEaICUOpY0sjBnUDFJOjy04iqBaiCRUy2IiEfKn4KqyK1dyfFH9FWXk0HNBIIeL//HEPmozwlAOgJEJW4iLEVAQkw0pjIoD/0JVINQomUmWyIwz8olBPbr+A//FMQAF//AEY4Af/8UxAAX/8ARggB//xTEABf/wBGCAH//FMQAF//AEYIAf/8UxAAX/8ARggB//xTEABf/wBGCAH//FMQAF//AEYIAf/8UxAAX/8ARggBw==';
  var CLICK_AT = 8224, CLICK_RATE = 48000;
  var lead = null;

  function bytes(base64) {
    var s = atob(base64), out = new Uint8Array(s.length), i;
    for (i = 0; i < s.length; i++) out[i] = s.charCodeAt(i);
    return out;
  }

  function decode(buffer, rate) {
    var Ctx = window.OfflineAudioContext || window.webkitOfflineAudioContext, ctx;
    if (!Ctx) return Promise.reject(new Error('no decoder'));
    try { ctx = new Ctx(1, 1, rate); } catch (e) { ctx = new Ctx(1, 1, 44100); }
    return new Promise(function (resolve, reject) {
      var p = ctx.decodeAudioData(buffer, resolve, reject);
      if (p && p.then) p.then(resolve, reject);
    });
  }

  /** Samples that the decoder of this browser drops at the start of an AAC stream (0 when it gives every frame whole). */
  async function decoderLead() {
    if (lead !== null) return lead;
    var buf = await decode(bytes(CLICK).buffer, CLICK_RATE), x = buf.getChannelData(0), p = 0, i;
    for (i = 1; i < x.length; i++) if (Math.abs(x[i]) > Math.abs(x[p])) p = i;
    if (Math.abs(x[p]) < 0.2) throw new Error('click not found');
    var l = Math.round(CLICK_AT - p * CLICK_RATE / buf.sampleRate);
    if (l < -2200 || l > 4300) throw new Error('lead ' + l);
    lead = l;
    return l;
  }

  function mono(buf, seconds) {
    var n = seconds ? Math.min(buf.length, Math.floor(seconds * buf.sampleRate)) : buf.length;
    var out = new Float32Array(n), channels = buf.numberOfChannels, c, ch, i;
    for (c = 0; c < channels; c++) {
      ch = buf.getChannelData(c);
      for (i = 0; i < n; i++) out[i] += ch[i];
    }
    if (channels > 1) for (i = 0; i < n; i++) out[i] /= channels;
    return out;
  }

  function workerSource() {
    return 'var H = (' + hornFactory.toString() + ')();\n' +
      'onmessage = function (e) {\n' +
      '  try {\n' +
      '    var x = e.data.samples, r = e.data.rate, l = H.loudness(x, r, 0.002), beeps = H.find(H.to16k(x, r));\n' +
      '    postMessage({ beeps: beeps, values: l.values, step: l.step }, [l.values.buffer]);\n' +
      '  } catch (err) { postMessage({ error: String(err) }); }\n' +
      '};\n';
  }

  /** The signals heard and the loudness of the sound, worked out away from the screen where the phone allows it. */
  function detect(samples, rate) {
    return new Promise(function (resolve) {
      function direct() {
        setTimeout(function () {
          var l = Horn.loudness(samples, rate, 0.002);
          resolve({ beeps: Horn.find(Horn.to16k(samples, rate)), values: l.values, step: l.step, worker: false });
        }, 30);
      }
      var w, url, done = false, timer;
      try {
        url = URL.createObjectURL(new Blob([workerSource()], { type: 'text/javascript' }));
        w = new Worker(url);
      } catch (e) {
        return direct();
      }
      function end(result) {
        if (done) return;
        done = true;
        clearTimeout(timer);
        try { w.terminate(); URL.revokeObjectURL(url); } catch (e) { /* gone already */ }
        if (result) resolve(result); else direct();
      }
      timer = setTimeout(function () { end(null); }, 90000);
      w.onmessage = function (e) {
        if (e.data && e.data.beeps) end({ beeps: e.data.beeps, values: e.data.values, step: e.data.step, worker: true });
        else end(null);
      };
      w.onerror = function () { end(null); };
      var copy = new Float32Array(samples);
      w.postMessage({ samples: copy, rate: rate }, [copy.buffer]);
    });
  }

  /**
   * The sound of a video: { status: 'ok' | 'none', path, start, lead, seconds, loud: { values, step, start },
   * beeps: [...each with 'at': its onset in the film (s)], choice: { list, best, clear }, note }.
   * info: the tables of the file (or null: the whole file is decoded, when it is small enough).
   */
  async function analyse(file, info, seconds) {
    var r = { status: 'none', path: '', start: 0, lead: 0, seconds: 0, note: '', beeps: [], choice: { list: [], best: null, clear: false } };
    var samples = null, rate = 0, limit = seconds || 120;
    if (info && info.audio) {
      try {
        var l = await decoderLead(), a = info.audio;
        var sound = await Mp4.readSound(Mp4.fileReader(file), a, limit);
        if (sound) {
          var buf = await decode(sound.bytes.buffer, a.rate), expect = sound.frames * a.frameTicks / a.timescale;
          // What the decoder gives must be the sound that was read: every frame, less what it drops at the start.
          if (Math.abs(buf.duration - (expect - l / a.rate)) < 0.06) {
            samples = mono(buf); rate = buf.sampleRate;
            r.path = 'stream'; r.lead = l; r.start = a.start + l / a.rate;
          } else {
            r.note = 'sound of ' + buf.duration.toFixed(3) + ' s for ' + expect.toFixed(3) + ' s read';
          }
        }
      } catch (e) {
        r.note = String((e && e.message) || e);
      }
    }
    if (!samples && file.size <= 150 * 1048576) {
      try {
        var whole = await decode(await Mp4.fileReader(file).read(0, file.size), 48000);
        samples = mono(whole, limit); rate = whole.sampleRate;
        r.path = 'file'; r.start = 0; r.lead = 0;
      } catch (e) {
        r.note = (r.note ? r.note + '; ' : '') + String((e && e.message) || e);
      }
    }
    if (!samples || samples.length < rate) return r;

    var d = await detect(samples, rate);
    r.status = 'ok';
    r.seconds = samples.length / rate;
    r.worker = d.worker;
    r.loud = { values: d.values, step: d.step, start: r.start };
    r.beeps = d.beeps.map(function (b) { b.at = b.onset + r.start; return b; });
    r.choice = Horn.choose(r.beeps);
    return r;
  }

  return { analyse: analyse, decoderLead: decoderLead };
}

if (typeof module !== 'undefined' && module.exports) module.exports = swimSoundFactory;
