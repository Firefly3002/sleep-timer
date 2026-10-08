"""Create new Sleep Timer nature tracks from the two user-selected Pixabay recordings.

Put the downloaded source MP3s in tools/audio-source/ (that folder is ignored by
Git). FFmpeg equalizes and levels each recording, mixes in a Sleep Timer-authored
stereo chord pad, and crossfades the ending into the beginning for smoother repeats.
"""

from __future__ import annotations

import shutil
import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE_DIR = ROOT / "tools" / "audio-source"
OUTPUT_DIR = ROOT / "src" / "SleepTimer.App" / "Assets" / "Audio"
SAMPLE_RATE = 48_000


def pad_expressions(style: str) -> tuple[str, str]:
    swell = "(0.82+0.18*sin(2*PI*t/30))"
    if style == "rain":
        left = "(0.024*sin(2*PI*55*t)+0.014*sin(2*PI*82.5*t+0.1)+0.008*sin(2*PI*110*t+0.25))"
        right = "(0.017*sin(2*PI*55*t+0.1)+0.020*sin(2*PI*82.5*t+0.2)+0.009*sin(2*PI*110*t+0.35))"
    elif style == "forest":
        left = "(0.018*sin(2*PI*41.2666666667*t)+0.013*sin(2*PI*55*t+0.1)+0.008*sin(2*PI*82.5*t+0.2))"
        right = "(0.013*sin(2*PI*41.2666666667*t+0.1)+0.017*sin(2*PI*55*t+0.2)+0.009*sin(2*PI*82.5*t+0.35))"
    else:
        raise ValueError(f"Unknown pad style: {style}")
    return f"{left}*{swell}", f"{right}*{swell}"


def media_duration(path: Path, ffprobe: str) -> float:
    result = subprocess.run(
        [
            ffprobe,
            "-v", "error",
            "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1",
            str(path),
        ],
        check=True,
        capture_output=True,
        text=True,
    )
    return float(result.stdout.strip())


def mix_track(
    ffmpeg: str,
    ffprobe: str,
    source: Path,
    destination: Path,
    title: str,
    style: str,
    low_cut: int,
    high_cut: int,
    source_gain_db: int,
) -> None:
    duration = media_duration(source, ffprobe)
    left, right = pad_expressions(style)
    pad_source = f"aevalsrc=exprs={left}|{right}:s={SAMPLE_RATE}:d={duration:.6f}"
    crossfade_seconds = 3.0
    core_start = crossfade_seconds
    core_end = duration - crossfade_seconds
    filter_graph = ";".join(
        [
            f"[0:a]aformat=sample_rates={SAMPLE_RATE}:channel_layouts=stereo,highpass=f={low_cut},lowpass=f={high_cut},volume={source_gain_db}dB[src]",
            f"[1:a]aformat=sample_rates={SAMPLE_RATE}:channel_layouts=stereo[pad]",
            "[src][pad]amix=inputs=2:duration=first:dropout_transition=0:weights='1 1':normalize=0[mix]",
            "[mix]asplit=3[head-in][tail-in][core-in]",
            f"[head-in]atrim=start=0:end={crossfade_seconds},asetpts=PTS-STARTPTS[head]",
            f"[tail-in]atrim=start={core_end:.6f}:end={duration:.6f},asetpts=PTS-STARTPTS[tail]",
            f"[core-in]atrim=start={core_start}:end={core_end:.6f},asetpts=PTS-STARTPTS[core]",
            f"[tail][head]acrossfade=d={crossfade_seconds}:c1=qsin:c2=qsin[edge]",
            "[edge][core]concat=n=2:v=0:a=1,alimiter=limit=0.92[out]",
        ]
    )
    subprocess.run(
        [
            ffmpeg,
            "-hide_banner",
            "-loglevel", "error",
            "-y",
            "-i", str(source),
            "-f", "lavfi",
            "-i", pad_source,
            "-filter_complex", filter_graph,
            "-map", "[out]",
            "-map_metadata", "-1",
            "-metadata", f"title={title} - Sleep Timer mix",
            "-metadata", "artist=Sleep Timer, with source audio by Eryliaa",
            "-c:a", "libmp3lame",
            "-b:a", "192k",
            "-ar", str(SAMPLE_RATE),
            str(destination),
        ],
        check=True,
    )


def main() -> None:
    ffmpeg = shutil.which("ffmpeg")
    ffprobe = shutil.which("ffprobe")
    if not ffmpeg or not ffprobe:
        raise SystemExit("FFmpeg and ffprobe are required to mix the nature tracks.")

    rain_source = SOURCE_DIR / "gentle-rain.mp3"
    forest_source = SOURCE_DIR / "night-forest.mp3"
    missing = [path.name for path in (rain_source, forest_source) if not path.is_file()]
    if missing:
        raise SystemExit(
            "Download the two Pixabay source MP3s into tools/audio-source before mixing: "
            + ", ".join(missing)
        )

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    mix_track(ffmpeg, ffprobe, rain_source, OUTPUT_DIR / "rainy-night.mp3", "Gentle rain", "rain", 55, 14_000, 6)
    mix_track(ffmpeg, ffprobe, forest_source, OUTPUT_DIR / "night-forest.mp3", "Night forest", "forest", 40, 10_500, 12)
    print(f"Mixed nature tracks into {OUTPUT_DIR}")


if __name__ == "__main__":
    main()
