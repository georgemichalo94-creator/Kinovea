// The timing tables of an MP4 / MOV file, read without decoding it: the time of every frame of the video (so that a
// step is exactly one frame and a mark carries the time of its frame) and where the sound is in the file (so that only
// the sound is read, a few hundred kB, whatever the size of the video).
function swimMp4Factory() {
  'use strict';

  function fourcc(dv, o) {
    return String.fromCharCode(dv.getUint8(o), dv.getUint8(o + 1), dv.getUint8(o + 2), dv.getUint8(o + 3));
  }
  function u64(dv, o) { return dv.getUint32(o) * 4294967296 + dv.getUint32(o + 4); }
  function i64(dv, o) { return dv.getInt32(o) * 4294967296 + dv.getUint32(o + 4); }

  /** The boxes at the top of the file, up to 'moov' (the tables): its place, and whether the file is in fragments. */
  async function findMoov(reader) {
    var pos = 0, size = reader.size, guard = 0, fragments = false;
    while (pos + 8 <= size && guard++ < 100000) {
      var head = new DataView(await reader.read(pos, Math.min(size, pos + 16)));
      if (head.byteLength < 8) break;
      var len = head.getUint32(0), type = fourcc(head, 4), hdr = 8;
      if (len === 1) {
        if (head.byteLength < 16) break;
        len = u64(head, 8); hdr = 16;
      } else if (len === 0) {
        len = size - pos;
      }
      if (len < hdr) break;
      if (type === 'moov') return { start: pos + hdr, end: Math.min(size, pos + len) };
      if (type === 'moof') fragments = true;
      pos += len;
    }
    return fragments ? { fragments: true } : null;
  }

  function children(dv, start, end) {
    var out = [], o = start;
    while (o + 8 <= end) {
      var len = dv.getUint32(o), type = fourcc(dv, o + 4), hdr = 8;
      if (len === 1) {
        if (o + 16 > end) break;
        len = u64(dv, o + 8); hdr = 16;
      } else if (len === 0) {
        len = end - o;
      }
      if (len < hdr || o + len > end) break;
      out.push({ type: type, start: o + hdr, end: o + len });
      o += len;
    }
    return out;
  }

  function find(dv, box, path) {
    var cur = box, i, j, kids, next;
    for (i = 0; i < path.length; i++) {
      kids = children(dv, cur.start, cur.end); next = null;
      for (j = 0; j < kids.length; j++) if (kids[j].type === path[i]) { next = kids[j]; break; }
      if (!next) return null;
      cur = next;
    }
    return cur;
  }

  function parseTrack(dv, trak) {
    var t = { handler: '', enabled: true, edits: [] };
    var tkhd = find(dv, trak, ['tkhd']);
    if (tkhd && tkhd.end - tkhd.start >= 4) t.enabled = (dv.getUint32(tkhd.start) & 1) === 1;
    var mdia = find(dv, trak, ['mdia']);
    if (!mdia) return null;
    var hdlr = find(dv, mdia, ['hdlr']);
    if (hdlr && hdlr.end - hdlr.start >= 12) t.handler = fourcc(dv, hdlr.start + 8);
    var mdhd = find(dv, mdia, ['mdhd']);
    if (!mdhd || mdhd.end - mdhd.start < 20) return null;
    if (dv.getUint8(mdhd.start) === 1) {
      if (mdhd.end - mdhd.start < 32) return null;
      t.timescale = dv.getUint32(mdhd.start + 20);
    } else {
      t.timescale = dv.getUint32(mdhd.start + 12);
    }
    if (!t.timescale) return null;
    t.stbl = find(dv, mdia, ['minf', 'stbl']);
    if (!t.stbl) return null;

    var elst = find(dv, trak, ['edts', 'elst']);
    if (elst && elst.end - elst.start >= 8) {
      var version = dv.getUint8(elst.start), n = dv.getUint32(elst.start + 4), o = elst.start + 8, i;
      for (i = 0; i < n; i++) {
        if (version === 1) {
          if (o + 20 > elst.end) break;
          t.edits.push({ duration: u64(dv, o), mediaTime: i64(dv, o + 8) });
          o += 20;
        } else {
          if (o + 12 > elst.end) break;
          t.edits.push({ duration: dv.getUint32(o), mediaTime: dv.getInt32(o + 4) });
          o += 12;
        }
      }
    }
    return t;
  }

  /** Where the track starts in the film (s) and from which time of its own (ticks): the empty edits, then the first real one. */
  function start(track, movieTimescale) {
    var empty = 0, media = 0, i, e;
    for (i = 0; i < track.edits.length; i++) {
      e = track.edits[i];
      if (e.mediaTime < 0) {
        empty += e.duration / movieTimescale;
      } else {
        media = e.mediaTime;
        break;
      }
    }
    return { empty: empty, media: media };
  }

  /** Decode times (ticks) of the samples and how many of them. */
  function decodeTimes(dv, stbl, limit) {
    var stts = find(dv, stbl, ['stts']);
    if (!stts || stts.end - stts.start < 8) return null;
    var n = dv.getUint32(stts.start + 4), o = stts.start + 8, total = 0, runs = [], i, j;
    for (i = 0; i < n && o + 8 <= stts.end; i++, o += 8) {
      runs.push([dv.getUint32(o), dv.getUint32(o + 4)]);
      total += runs[i][0];
    }
    if (total === 0 || total > limit) return null;
    var dts = new Float64Array(total), k = 0, at = 0;
    for (i = 0; i < runs.length; i++) {
      for (j = 0; j < runs[i][0]; j++) { dts[k++] = at; at += runs[i][1]; }
    }
    return { dts: dts, runs: runs, end: at };
  }

  /** Times (s, in the film) at which the frames of the video are shown, in order. */
  function frameTimes(dv, track, movieTimescale) {
    var d = decodeTimes(dv, track.stbl, 2000000);
    if (!d) return null;
    var total = d.dts.length, cts = d.dts, i, k, o;
    var ctts = find(dv, track.stbl, ['ctts']);
    if (ctts && ctts.end - ctts.start >= 8) {
      cts = new Float64Array(d.dts);
      var m = dv.getUint32(ctts.start + 4), count, offset, q;
      o = ctts.start + 8; k = 0;
      for (i = 0; i < m && o + 8 <= ctts.end && k < total; i++, o += 8) {
        // Signed in both versions: the files of the phones write negative offsets in version 0 too.
        count = dv.getUint32(o); offset = dv.getInt32(o + 4);
        for (q = 0; q < count && k < total; q++, k++) cts[k] += offset;
      }
    }

    var s = start(track, movieTimescale), ts = track.timescale;
    var times = new Float64Array(total);
    for (i = 0; i < total; i++) times[i] = s.empty + (cts[i] - s.media) / ts;
    times.sort();

    // The usual step between two frames, and whether the frames keep it.
    var steps = new Float64Array(Math.max(0, total - 1));
    for (i = 1; i < total; i++) steps[i - 1] = times[i] - times[i - 1];
    var sorted = Float64Array.from(steps).sort(), step = sorted.length ? sorted[Math.floor(sorted.length / 2)] : 0, uneven = 0;
    for (i = 0; i < steps.length; i++) if (Math.abs(steps[i] - step) > 0.02 * step) uneven++;

    // The frames before the start of the film (cut by the edit) are not shown.
    var first = 0;
    while (first < total - 1 && times[first] < -0.5 * step) first++;
    if (first > 0) times = times.slice(first);
    return { times: times, step: step, fps: step > 0 ? 1 / step : 0, uneven: steps.length ? uneven / steps.length : 0 };
  }

  function descriptorLength(dv, o, end) {
    var len = 0, n = 0, b;
    while (o < end && n < 4) {
      b = dv.getUint8(o++); n++;
      len = (len << 7) | (b & 0x7F);
      if (!(b & 0x80)) break;
    }
    return { len: len, next: o };
  }

  var FREQUENCIES = [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350];

  /** The AAC settings (kind, rate, channels) from the 'esds' box, wherever the file keeps it. */
  function aacConfig(dv, stsd) {
    var o, at = -1, d, flags, kind;
    for (o = stsd.start + 16; o + 8 <= stsd.end; o++) {
      if (dv.getUint32(o) === 0x65736473) { at = o + 8; break; }   // 'esds', then version and flags
    }
    if (at < 0 || at >= stsd.end || dv.getUint8(at) !== 0x03) return null;
    d = descriptorLength(dv, at + 1, stsd.end); o = d.next;
    if (o + 3 > stsd.end) return null;
    flags = dv.getUint8(o + 2); o += 3;
    if (flags & 0x80) o += 2;
    if (flags & 0x40) { if (o >= stsd.end) return null; o += 1 + dv.getUint8(o); }
    if (flags & 0x20) o += 2;
    if (o >= stsd.end || dv.getUint8(o) !== 0x04) return null;
    d = descriptorLength(dv, o + 1, stsd.end); o = d.next;
    if (o + 13 >= stsd.end) return null;
    kind = dv.getUint8(o); o += 13;
    if (dv.getUint8(o) !== 0x05) return null;
    d = descriptorLength(dv, o + 1, stsd.end); o = d.next;
    if (d.len < 2 || o + 2 > stsd.end) return null;
    var b0 = dv.getUint8(o), b1 = dv.getUint8(o + 1);
    var config = { kind: kind, objectType: b0 >> 3, frequencyIndex: ((b0 & 7) << 1) | (b1 >> 7), channels: (b1 >> 3) & 0x0F };
    config.rate = FREQUENCIES[config.frequencyIndex] || 0;
    return config;
  }

  /** Place and size of the first samples of a track in the file. */
  function sampleRuns(dv, stbl, limit) {
    var stsz = find(dv, stbl, ['stsz']), stsc = find(dv, stbl, ['stsc']);
    var stco = find(dv, stbl, ['stco']), co64 = find(dv, stbl, ['co64']), table = stco || co64;
    if (!stsz || !stsc || !table || stsz.end - stsz.start < 12 || stsc.end - stsc.start < 8 || table.end - table.start < 8) return null;
    var fixed = dv.getUint32(stsz.start + 4), count = dv.getUint32(stsz.start + 8);
    if (!fixed && stsz.start + 12 + 4 * count > stsz.end) count = Math.floor((stsz.end - stsz.start - 12) / 4);
    var chunks = dv.getUint32(table.start + 4), width = stco ? 4 : 8;
    chunks = Math.min(chunks, Math.floor((table.end - table.start - 8) / width));
    var entries = Math.min(dv.getUint32(stsc.start + 4), Math.floor((stsc.end - stsc.start - 8) / 12));
    var n = Math.min(count, limit), sizes = new Uint32Array(n), offsets = new Float64Array(n), s = 0, e, o, first, per, next, c, q, off, sz;
    for (e = 0; e < entries && s < n; e++) {
      o = stsc.start + 8 + 12 * e;
      first = dv.getUint32(o); per = dv.getUint32(o + 4);
      next = e + 1 < entries ? dv.getUint32(o + 12) : chunks + 1;
      for (c = first; c < next && c <= chunks && s < n; c++) {
        off = stco ? dv.getUint32(table.start + 8 + 4 * (c - 1)) : u64(dv, table.start + 8 + 8 * (c - 1));
        for (q = 0; q < per && s < n; q++, s++) {
          sz = fixed || dv.getUint32(stsz.start + 12 + 4 * s);
          sizes[s] = sz; offsets[s] = off; off += sz;
        }
      }
    }
    return { count: s, sizes: sizes, offsets: offsets, total: count };
  }

  /**
   * Reads the tables of a file. reader: { size, read(start, end) → Promise<ArrayBuffer> }.
   * Returns null when the file is not an MP4 / MOV with its tables in one piece (the caller then measures the frames
   * from the playback), else { video: { times, step, fps, uneven }, audio: {...} or null }.
   */
  async function parse(reader) {
    var moov = await findMoov(reader);
    if (!moov || moov.fragments) return null;
    if (moov.end - moov.start > 96 * 1048576) return null;
    var dv = new DataView(await reader.read(moov.start, moov.end));
    var root = { start: 0, end: dv.byteLength }, movieTimescale = 0, i;
    var mvhd = find(dv, root, ['mvhd']);
    if (mvhd && mvhd.end - mvhd.start >= 20) {
      movieTimescale = dv.getUint8(mvhd.start) === 1 ? (mvhd.end - mvhd.start >= 32 ? dv.getUint32(mvhd.start + 20) : 0) : dv.getUint32(mvhd.start + 12);
    }
    if (!movieTimescale) return null;

    var tracks = children(dv, 0, dv.byteLength).filter(function (b) { return b.type === 'trak'; })
      .map(function (b) { return parseTrack(dv, b); }).filter(Boolean);

    // The video: the track shown (enabled) with the most frames.
    var video = null;
    tracks.filter(function (t) { return t.handler === 'vide'; }).forEach(function (t) {
      var f = frameTimes(dv, t, movieTimescale);
      if (!f || f.times.length < 2 || !(f.step > 0)) return;
      f.enabled = t.enabled;
      if (!video || (f.enabled && !video.enabled) || (f.enabled === video.enabled && f.times.length > video.times.length)) video = f;
    });

    // The sound: the first AAC (LC) track.
    var audio = null;
    for (i = 0; i < tracks.length && !audio; i++) {
      var t = tracks[i];
      if (t.handler !== 'soun') continue;
      var stsd = find(dv, t.stbl, ['stsd']);
      if (!stsd || stsd.end - stsd.start < 16) continue;
      var config = aacConfig(dv, stsd);
      if (!config || !config.rate || config.objectType !== 2 || config.channels < 1 || config.channels > 7) continue;
      if (config.kind !== 0x40 && config.kind !== 0x67) continue;
      var d = decodeTimes(dv, t.stbl, 5000000);
      if (!d || d.runs.length === 0) continue;
      var s = start(t, movieTimescale);
      audio = {
        config: config, rate: config.rate, timescale: t.timescale,
        frames: d.dts.length, frameTicks: d.runs[0][1],
        // The first sample decoded is at this time of the film (s): before zero when the first frames only prime the decoder.
        start: s.empty - s.media / t.timescale,
        stbl: t.stbl, dv: dv
      };
    }

    if (!video && !audio) return null;
    return { video: video, audio: audio };
  }

  /**
   * The sound of the first 'seconds' of the file as an AAC stream (ADTS: each frame behind a header of 7 bytes), read
   * straight from its places in the file. Returns { bytes, frames } or null.
   */
  async function readSound(reader, audio, seconds) {
    var perFrame = audio.frameTicks / audio.timescale;
    var limit = Math.max(1, Math.min(audio.frames, Math.ceil(seconds / perFrame)));
    var runs = sampleRuns(audio.dv, audio.stbl, limit);
    if (!runs || runs.count === 0) return null;
    var n = runs.count, total = 0, i;
    for (i = 0; i < n; i++) {
      if (runs.sizes[i] === 0 || runs.sizes[i] > 8184 || runs.offsets[i] + runs.sizes[i] > reader.size) return null;
      total += runs.sizes[i] + 7;
    }

    // Pieces of the file that follow each other are read at once.
    var pieces = [], from = 0;
    for (i = 1; i <= n; i++) {
      if (i === n || runs.offsets[i] !== runs.offsets[i - 1] + runs.sizes[i - 1] || runs.offsets[i] - runs.offsets[from] > 4194304) {
        pieces.push({ from: from, to: i, start: runs.offsets[from], end: runs.offsets[i - 1] + runs.sizes[i - 1] });
        from = i;
      }
    }

    var out = new Uint8Array(total), c = audio.config, profile = c.objectType - 1, at = 0, p, k, buf, src, pos, len, full;
    var positions = new Float64Array(n);
    for (i = 0; i < n; i++) { positions[i] = at; at += runs.sizes[i] + 7; }
    for (p = 0; p < pieces.length; p += 16) {
      var batch = pieces.slice(p, p + 16);
      var bufs = await Promise.all(batch.map(function (x) { return reader.read(x.start, x.end); }));
      for (k = 0; k < batch.length; k++) {
        buf = new Uint8Array(bufs[k]);
        if (buf.length < batch[k].end - batch[k].start) return null;
        src = 0;
        for (i = batch[k].from; i < batch[k].to; i++) {
          len = runs.sizes[i]; full = len + 7; pos = positions[i];
          out[pos] = 0xFF;
          out[pos + 1] = 0xF1;
          out[pos + 2] = (profile << 6) | (c.frequencyIndex << 2) | (c.channels >> 2);
          out[pos + 3] = ((c.channels & 3) << 6) | (full >> 11);
          out[pos + 4] = (full >> 3) & 0xFF;
          out[pos + 5] = ((full & 7) << 5) | 0x1F;
          out[pos + 6] = 0xFC;
          out.set(buf.subarray(src, src + len), pos + 7);
          src += len;
        }
      }
    }
    return { bytes: out, frames: n };
  }

  /** A reader over a File or Blob of the browser. */
  function fileReader(file) {
    return {
      size: file.size,
      read: function (from, to) {
        var part = file.slice(from, to);
        if (part.arrayBuffer) return part.arrayBuffer();
        return new Promise(function (resolve, reject) {
          var r = new FileReader();
          r.onload = function () { resolve(r.result); };
          r.onerror = function () { reject(r.error); };
          r.readAsArrayBuffer(part);
        });
      }
    };
  }

  return { parse: parse, readSound: readSound, fileReader: fileReader };
}

if (typeof module !== 'undefined' && module.exports) module.exports = swimMp4Factory;
