// The video, one frame at a time. A step shows exactly the next frame and a mark carries the time of the frame on
// screen: the times of the frames come from the tables of the file (mp4.js); without them they are measured from the
// playback. Where the browser tells which frame it has just drawn, every step waits for that frame and is checked
// against it.
function swimPlayerFactory(Mp4) {
  'use strict';

  var video = null, table = null, step = 1 / 30, first = 0, count = 1, offset = 0, k = 0;
  var source = 'none';             // 'tables' (of the file), 'measured' (from the playback), 'guess' (30 per second)
  var canWatch = typeof HTMLVideoElement !== 'undefined' && 'requestVideoFrameCallback' in HTMLVideoElement.prototype;
  var echo = null;                 // true: the browser reports the time asked, false: the time of the frame, null: unknown
  var busy = false, want = null, asked = 0, askedAt = 0, timer = 0;
  var shown = NaN, shownAt = 0, anchor = null, playing = false, corrected = 0;
  var loaded = false, primed = false, steady = false, url = null, generation = 0;
  var handlers = { frame: [], state: [] }, waiters = [];

  // The usual rates of cameras: a rate measured from the playback is one of them when it is within 1.5 %.
  var RATES = [24, 25, 30, 48, 50, 60, 90, 100, 120, 240];

  function clamp(v, lo, hi) { return v < lo ? lo : v > hi ? hi : v; }
  function clock() { return typeof performance !== 'undefined' ? performance.now() : Date.now(); }
  function emit(name) { handlers[name].forEach(function (f) { try { f(); } catch (e) { /* a listener must not stop the video */ } }); }
  function on(name, f) { handlers[name].push(f); }

  /** Time of the film (s) of frame i, and of the frame after it. */
  function T(i) { return table ? table[i] : first + i * step; }
  function after(i) { return i + 1 < count ? T(i + 1) : T(i) + step; }

  /** The frame on screen at time t of the film: the last one that starts at or before it. */
  function frameAt(t) {
    // Without tables the times that the browser reports are rounded (to the millisecond in some files): a quarter of a frame of room.
    if (!table) return clamp(Math.floor((t - first) / step + 0.25), 0, count - 1);
    var eps = Math.min(1e-4, step / 4), lo = 0, hi = count - 1, mid;
    while (lo < hi) {
      mid = (lo + hi + 1) >> 1;
      if (table[mid] <= t + eps) lo = mid; else hi = mid - 1;
    }
    return lo;
  }

  /**
   * Where to seek to see frame i, on the clock of the browser: the middle of its time on screen. Without tables, a
   * frame near the one on screen is counted from the time that the browser reported for that one.
   */
  function target(i) {
    if (!table && anchor && Math.abs(i - anchor.frame) <= 12) return anchor.time + (i - anchor.frame + 0.5) * step;
    return T(i) + 0.5 * (after(i) - T(i)) + offset;
  }

  /** Time of the film (s) of the frame on screen. */
  function current() {
    if (!table && anchor && anchor.frame === k) return anchor.time - offset;
    return T(k);
  }

  function settle() {
    var list = waiters; waiters = [];
    list.forEach(function (f) { f(); });
  }

  /**
   * After a seek: waits until the browser has drawn the frame asked. If it keeps showing another one, that one is
   * the frame on screen (and the one that a mark would get).
   */
  function confirm(g) {
    var started = clock();
    (function look() {
      if (g !== generation || busy || playing) return;
      var fresh = shownAt > askedAt && !isNaN(shown), i = fresh ? frameAt(shown - offset) : -1;
      if (fresh && i === k) {
        if (!table) anchor = { frame: k, time: shown };
        steady = true;
        return settle();
      }
      if (clock() - started > 180) {
        if (fresh && (table ? Math.abs(T(i) + offset - shown) < 0.3 * step : true)) {
          corrected++; k = i;
          if (!table) anchor = { frame: k, time: shown };
          emit('frame');
        }
        steady = true;
        return settle();
      }
      setTimeout(look, 12);
    })();
  }

  function finish(g) {
    if (!busy || g !== generation) return;
    clearTimeout(timer);
    busy = false; k = asked;
    emit('frame');
    if (want !== null) pump();
    else if (canWatch && echo === false) confirm(g);
    else { steady = true; settle(); }
  }

  function pump() {
    if (busy || want === null || playing || !loaded) return;
    var i = want, g = generation, t;
    want = null; asked = i; askedAt = clock();
    t = clamp(target(i), 0, Math.max(0, (video.duration || 1e9) - 0.001));
    busy = true; steady = false;
    timer = setTimeout(function () { finish(g); }, 2500);
    try { video.currentTime = t; } catch (e) { finish(g); return; }
    if (!video.seeking) setTimeout(function () { finish(g); }, 0);
  }

  /** Shows frame i (the last one asked wins when the steps come faster than the phone seeks). */
  function go(i) {
    if (!loaded) return;
    if (playing) pause();
    want = clamp(Math.round(i), 0, count - 1);
    pump();
  }

  /** Resolves when the frame asked is on screen. */
  function shownFrame(i) {
    return new Promise(function (resolve) {
      if (!loaded) return resolve();
      // Already on screen (and checked): nothing to wait for.
      if (steady && !busy && !playing && want === null && clamp(Math.round(i), 0, count - 1) === k) return resolve();
      waiters.push(resolve);
      go(i);
    });
  }

  function watch() {
    if (!canWatch || !video) return;
    var g = generation;
    video.requestVideoFrameCallback(function (t, meta) {
      if (g !== generation) return;
      shown = meta.mediaTime; shownAt = clock();
      if (playing) {
        var i = frameAt(shown - offset);
        if (i !== k) { k = i; emit('frame'); }
      }
      watch();
    });
  }

  function poll() {
    if (!playing || canWatch) return;
    var i = frameAt(video.currentTime - offset);
    if (i !== k) { k = i; emit('frame'); }
    requestAnimationFrame(poll);
  }

  function play(rate) {
    if (!loaded || playing) return;
    if (busy || want !== null) { waiters.push(function () { play(rate); }); return; }
    if (k >= count - 2) { try { video.currentTime = Math.max(0, T(0) + offset); } catch (e) { /* stays where it is */ } }
    playing = true; anchor = null; steady = false;
    settle();
    try { video.playbackRate = rate || 1; } catch (e) { /* the phone keeps its own speed */ }
    var p = video.play();
    if (p && p.then) p.then(function () { primed = true; }, function () { playing = false; emit('state'); });
    emit('state');
    poll();
  }

  /**
   * Called from a tap: some phones draw nothing (and refuse to play by themselves) until the person has started the
   * video once. A moment of playback, then back to the frame.
   */
  function prime() {
    if (!loaded || primed || playing || busy) return;
    var i = k, p;
    try { p = video.play(); } catch (e) { return; }
    if (p && p.then) p.then(function () { primed = true; video.pause(); want = i; pump(); }, function () { /* stays as it is */ });
  }

  function pause() {
    if (!playing) return;
    playing = false;
    video.pause();
    var t = canWatch && !isNaN(shown) && echo !== true ? shown : video.currentTime;
    k = frameAt(t - offset);
    emit('state');
    want = k; pump();
  }

  function seekTo(t) {
    return new Promise(function (resolve) {
      var done = false;
      function end() { if (done) return; done = true; video.removeEventListener('seeked', end); resolve(); }
      video.addEventListener('seeked', end);
      setTimeout(end, 2500);
      try { video.currentTime = t; } catch (e) { end(); }
      if (!video.seeking) setTimeout(end, 0);
    });
  }

  /** The time of the frame drawn after 'since', as the browser reports it (NaN when it does not). */
  function drawn(since) {
    return new Promise(function (resolve) {
      var tries = 0;
      (function look() {
        if (shownAt > since) return resolve(shown);
        if (!canWatch || ++tries > 24) return resolve(NaN);
        setTimeout(look, 25);
      })();
    });
  }

  /** Length of a frame (s) from a short playback, when the file has no tables to read. */
  function measure() {
    return new Promise(function (resolve) {
      if (!canWatch) return resolve(null);
      var times = [], g = generation, stopped = false;
      function stop() {
        if (stopped) return;
        stopped = true;
        video.pause();
        var d = [], i, min, steps = 0, fps, best = 0;
        for (i = 1; i < times.length; i++) if (times[i] - times[i - 1] > 1e-4) d.push(times[i] - times[i - 1]);
        if (d.length < 6) return resolve(null);
        // The shortest steps are one frame; a longer one is as many frames as it holds of them. The whole stretch
        // over its frames is the length of one (the times of some files are rounded, each step alone is not exact).
        min = d.slice().sort(function (a, b) { return a - b; })[Math.floor(d.length / 4)];
        for (i = 0; i < d.length; i++) steps += Math.max(1, Math.round(d[i] / min));
        fps = steps / (times[times.length - 1] - times[0]);
        RATES.forEach(function (r) { if (Math.abs(r - fps) < Math.abs(best - fps)) best = r; });
        resolve({ step: Math.abs(best - fps) <= 0.015 * best ? 1 / best : 1 / fps });
      }
      (function listen() {
        video.requestVideoFrameCallback(function (t, meta) {
          if (g !== generation || stopped) return;
          if (times.length === 0 || meta.mediaTime > times[times.length - 1]) times.push(meta.mediaTime);
          if (times.length >= 40) stop(); else listen();
        });
      })();
      try { video.playbackRate = 0.5; } catch (e) { /* its own speed */ }
      var p = video.play();
      if (p && p.catch) p.catch(stop);
      setTimeout(stop, 1800);
    });
  }

  function unload() {
    generation++;
    clearTimeout(timer);
    busy = false; want = null; playing = false; loaded = false; steady = false; settle();
    table = null; step = 1 / 30; first = 0; count = 1; offset = 0; k = 0; source = 'none'; echo = null; anchor = null;
    shown = NaN; shownAt = 0; corrected = 0; primed = false;
    if (video) { try { video.pause(); video.removeAttribute('src'); video.load(); } catch (e) { /* nothing to free */ } }
    if (url) { try { URL.revokeObjectURL(url); } catch (e) { /* already gone */ } url = null; }
  }

  /**
   * Opens a video file. element: the <video> of the page. Returns { info (tables of the file or null), width, height,
   * duration }; rejects with a message for the coach when the phone cannot play the file.
   */
  async function load(element, file) {
    if (video !== element) {
      video = element;
      video.addEventListener('seeked', function () { finish(generation); });
      video.addEventListener('ended', function () { if (playing) { playing = false; k = count - 1; emit('state'); emit('frame'); } });
    }
    unload();
    var g = generation, other = 'Άνοιξε άλλο βίντεο.';
    url = URL.createObjectURL(file);
    video.muted = true; video.playsInline = true; video.preload = 'auto';

    await new Promise(function (resolve, reject) {
      var done = false;
      function ok() { if (done) return; done = true; clear(); resolve(); }
      function bad() {
        if (done) return;
        done = true; clear();
        reject(new Error('Το κινητό δεν μπορεί να παίξει αυτό το βίντεο. Δοκίμασε να το τραβήξεις ή να το αποθηκεύσεις ως MP4 (H.264).'));
      }
      function clear() { video.removeEventListener('loadedmetadata', ok); video.removeEventListener('error', bad); clearTimeout(t); }
      var t = setTimeout(bad, 20000);
      video.addEventListener('loadedmetadata', ok);
      video.addEventListener('error', bad);
      video.src = url;
      video.load();
    });
    if (g !== generation) throw new Error(other);

    var info = null;
    try { info = await Mp4.parse(Mp4.fileReader(file)); } catch (e) { info = null; }
    if (g !== generation) throw new Error(other);

    // The phones draw nothing before the first playback: a moment of it, without sound.
    try { await video.play(); video.pause(); primed = true; } catch (e) { primed = false; }
    if (g !== generation) throw new Error(other);

    var duration = isFinite(video.duration) ? video.duration : 0, measured = null;
    if (info && info.video) {
      var t = info.video.times, end = t[t.length - 1] + info.video.step;
      // Tables that tell quite another length than the phone plays (a retimed slow motion) are not the frames shown.
      if (duration > 0 && (end > duration + 0.5 || end < 0.8 * duration - 0.5)) info.video = null;
    }
    if (info && info.video) {
      table = info.video.times; step = info.video.step; count = table.length; source = 'tables';
    } else {
      measured = await measure();
      if (g !== generation) throw new Error(other);
      if (measured) { step = measured.step; source = 'measured'; } else { step = 1 / 30; source = 'guess'; }
      first = 0;
      count = Math.max(1, Math.round(duration / step));
    }
    watch();

    // The clock of the browser against the times of the file: the first frame, then one in the middle of its time.
    var since = clock();
    await seekTo(0);
    var m0 = await drawn(since);
    if (count > 4) {
      since = clock();
      var t3 = T(3) + 0.5 * (after(3) - T(3));
      await seekTo(t3);
      var m3 = await drawn(since);
      echo = isNaN(m3) || isNaN(m0) ? null : Math.abs(m3 - t3) < 2e-5;
      if (echo === false) {
        if (table) {
          var d = m0 - T(0);
          offset = Math.abs(d) < 1 && Math.abs(d) > 1e-4 ? d : 0;
        } else {
          first = m0 > 0 && m0 < 1 ? m0 : 0;
        }
      }
    }
    if (g !== generation) throw new Error(other);
    loaded = true;
    k = 0;
    await shownFrame(0);
    if (g !== generation) throw new Error(other);
    return { info: info, width: video.videoWidth, height: video.videoHeight, duration: duration };
  }

  /** Changes the frames per second by hand (only when they could not be read from the file). */
  function setFps(fps) {
    if (table || !loaded || !(fps > 0)) return;
    var t = current();
    step = 1 / fps; source = 'guess'; anchor = null;
    count = Math.max(1, Math.round((isFinite(video.duration) ? video.duration : 0) / step));
    go(frameAt(t));
    emit('state');
  }

  /** A picture of frame i, at most 'width' pixels wide (for the report). */
  async function capture(i, width) {
    await shownFrame(i);
    await new Promise(function (resolve) { requestAnimationFrame(function () { requestAnimationFrame(resolve); }); });
    var w = video.videoWidth || 16, h = video.videoHeight || 9, s = Math.min(1, width / w);
    var c = document.createElement('canvas');
    c.width = Math.max(1, Math.round(w * s)); c.height = Math.max(1, Math.round(h * s));
    c.getContext('2d').drawImage(video, 0, 0, c.width, c.height);
    return c;
  }

  return {
    load: load, unload: unload, on: on, go: go, shownFrame: shownFrame, capture: capture, setFps: setFps,
    step: function (d) { go((want !== null ? want : busy ? asked : k) + d); },
    play: play, pause: pause, prime: prime,
    time: T, frameAt: frameAt, current: current,
    state: function () {
      return {
        loaded: loaded, frame: want !== null ? want : busy ? asked : k, shown: k, count: count, step: step, fps: 1 / step,
        source: source, playing: playing, busy: busy, duration: video && isFinite(video.duration) ? video.duration : 0
      };
    },
    diagnostics: function () {
      return {
        source: source, fps: 1 / step, frames: count, watch: !canWatch ? 'none' : echo === null ? 'unknown' : echo ? 'echo' : 'frame',
        offset: offset, corrected: corrected, primed: primed
      };
    }
  };
}

if (typeof module !== 'undefined' && module.exports) module.exports = swimPlayerFactory;
