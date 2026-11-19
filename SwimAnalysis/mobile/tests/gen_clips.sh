#!/bin/bash
# Synthetic clips for the checks (no pool, no person): a test pattern whose every frame carries its number as 12 black
# or white squares at the top left, with a start signal of two tones at 2.0137 s and a clap at 4.25 s in the sound.
# Usage: gen_clips.sh <output folder>
set -e
OUT="${1:?output folder}"
HERE="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$OUT"
cd "$OUT"

code() { echo "color=black:size=192x16:rate=$1:duration=$2,geq=lum='255*mod(floor(N/pow(2\,floor(X/16)))\,2)':cb=128:cr=128"; }
clip() { # name rate seconds audio.wav [video options...] -- [output options...]
  local name="$1" rate="$2" secs="$3" wav="$4"; shift 4
  [ -s "$name" ] && return 0
  ffmpeg -hide_banner -loglevel error -y -f lavfi -i "testsrc2=size=640x360:rate=$rate:duration=$secs" -f lavfi -i "$(code "$rate" "$secs")" \
    -i "$wav" -filter_complex "[0][1]overlay=0:0[v]" -map "[v]" -map 2:a "$@" "tmp_$name" && mv "tmp_$name" "$name"
  echo "made $name"
}

[ -s s48.wav ] || python3 "$HERE/gen_audio.py" s48.wav 48000 6 2.0137 4.25
[ -s s44.wav ] || python3 "$HERE/gen_audio.py" s44.wav 44100 6 2.0137 4.25 2
[ -s s48s.wav ] || python3 "$HERE/gen_audio.py" s48s.wav 48000 3 1.0137 2.25

X264="-c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p"
AAC="-c:a aac -b:a 96k"
clip a_60_bf.mp4      60          6 s48.wav  $X264 $AAC -shortest
clip b_5994_fast.mp4  60000/1001  6 s44.wav  $X264 $AAC -shortest -movflags +faststart
clip c_30_nobf.mov    30          6 s48.wav  $X264 -bf 0 $AAC -shortest
clip h_2997.mp4       30000/1001  6 s48.wav  $X264 $AAC -shortest
clip e_240.mp4        240         3 s48s.wav $X264 $AAC -shortest
clip g_vp9.webm       60          6 s48.wav  -c:v libvpx-vp9 -b:v 600k -deadline realtime -cpu-used 8 -c:a libopus -b:a 64k -shortest

# Two signals as clear as each other (a false start, then the start), no signal at all (a clap only), no sound at all,
# and a picture taller than wide.
[ -s s48two.wav ] || python3 "$HERE/gen_audio.py" s48two.wav 48000 6 1.5071 -1 1 4.0137
[ -s s48clap.wav ] || python3 "$HERE/gen_audio.py" s48clap.wav 48000 6 -1 3.0
clip i_two.mp4        60          6 s48two.wav  $X264 $AAC -shortest
clip k_clap.mp4       60          6 s48clap.wav $X264 $AAC -shortest
if [ ! -s j_silent.mp4 ]; then
  ffmpeg -hide_banner -loglevel error -y -f lavfi -i "testsrc2=size=640x360:rate=60:duration=4" -f lavfi -i "$(code 60 4)" \
    -filter_complex "[0][1]overlay=0:0[v]" -map "[v]" $X264 tmp_j_silent.mp4 && mv tmp_j_silent.mp4 j_silent.mp4
  echo "made j_silent.mp4"
fi
if [ ! -s l_tall.mp4 ]; then
  ffmpeg -hide_banner -loglevel error -y -f lavfi -i "testsrc2=size=360x640:rate=30:duration=4" -f lavfi -i "$(code 30 4)" -i s48.wav \
    -filter_complex "[0][1]overlay=0:0[v]" -map "[v]" -map 2:a $X264 $AAC -shortest tmp_l_tall.mp4 && mv tmp_l_tall.mp4 l_tall.mp4
  echo "made l_tall.mp4"
fi

# Frames of uneven length: one frame in seven is dropped.
if [ ! -s d_vfr.mp4 ]; then
  ffmpeg -hide_banner -loglevel error -y -f lavfi -i "testsrc2=size=640x360:rate=60:duration=6" -f lavfi -i "$(code 60 6)" -i s48.wav \
    -filter_complex "[0][1]overlay=0:0,select='not(eq(mod(n\,7)\,3))'[v]" -map "[v]" -map 2:a -fps_mode vfr $X264 $AAC -shortest tmp_d_vfr.mp4 && mv tmp_d_vfr.mp4 d_vfr.mp4
  echo "made d_vfr.mp4"
fi

# The sound starts 0.1 s after the picture (an empty edit in the file).
if [ ! -s f_offset.mp4 ]; then
  ffmpeg -hide_banner -loglevel error -y -f lavfi -i "testsrc2=size=640x360:rate=60:duration=6" -f lavfi -i "$(code 60 6)" -itsoffset 0.1 -i s48.wav \
    -filter_complex "[0][1]overlay=0:0[v]" -map "[v]" -map 2:a $X264 $AAC -shortest tmp_f_offset.mp4 && mv tmp_f_offset.mp4 f_offset.mp4
  echo "made f_offset.mp4"
fi
ls -la "$OUT" | grep -v tmp_
