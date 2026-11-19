# Synthetic sound for the tests: pool-like noise, a start signal of two tones at an exact time (a second one at
# another time when asked), a clap (click) at another.
# Usage: gen_audio.py out.wav rate seconds beep_at click_at [channels [second_beep_at]]   (a time below 0: none)
import sys, wave, numpy as np
out, rate, seconds, beep_at, click_at = sys.argv[1], int(sys.argv[2]), float(sys.argv[3]), float(sys.argv[4]), float(sys.argv[5])
channels = int(sys.argv[6]) if len(sys.argv) > 6 else 1
rng = np.random.default_rng(12345)
n = int(round(seconds * rate))
w = rng.normal(0, 1, n)
low = np.zeros(n); acc = 0.0
for i in range(n):
    acc = 0.9 * acc + 0.1 * w[i]; low[i] = acc
x = 0.012 * w + 0.05 * low
beeps = [beep_at] + ([float(sys.argv[7])] if len(sys.argv) > 7 else [])
for at in beeps:
    if at < 0: continue
    a = int(round(at * rate)); m = int(round(0.5 * rate)); t = (a + np.arange(m)) / rate
    env = np.minimum(1, np.minimum(np.arange(m) / (0.004 * rate), (m - np.arange(m)) / (0.004 * rate)))
    x[a:a + m] += 0.25 * env * (np.sin(2 * np.pi * 895 * t) + np.sin(2 * np.pi * 895 * 8 / 7 * t))
if click_at >= 0:
    a = int(round(click_at * rate)); m = int(0.03 * rate)
    x[a:a + m] += 0.8 * rng.normal(0, 1, m) * np.exp(-np.arange(m) / (0.004 * rate))
x = np.clip(x, -1, 1)
pcm = (x * 32767).astype('<i2')
if channels == 2: pcm = np.repeat(pcm[:, None], 2, axis=1)
with wave.open(out, 'wb') as f:
    f.setnchannels(channels); f.setsampwidth(2); f.setframerate(rate); f.writeframes(pcm.tobytes())
print(out, n, 'samples')
