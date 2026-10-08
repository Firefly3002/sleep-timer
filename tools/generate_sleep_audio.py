"""Create the original, sample-free sleep audio bundled with Sleep Timer.

Requires Python 3 and FFmpeg (for compressing the three longer loops to MP3).
The short one-shot cues are written as WAV files.
"""

from __future__ import annotations

import math
import shutil
import subprocess
import sys
import tempfile
import wave
from array import array
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src" / "SleepTimer.App" / "Assets" / "Audio"
SAMPLE_RATE = 22_050
MUSIC_SECONDS = 180
TAU = math.tau


def midi(note: int) -> float:
    return 440.0 * (2.0 ** ((note - 69) / 12.0))


def envelope(t: float, duration: float) -> float:
    edge = min(1.0, max(0.0, t / 0.12), max(0.0, (duration - t) / 0.12))
    return edge * edge * (3.0 - 2.0 * edge)


def write_wav(path: Path, seconds: float, sample_fn) -> None:
    total = int(SAMPLE_RATE * seconds)
    with wave.open(str(path), "wb") as target:
        target.setnchannels(1)
        target.setsampwidth(2)
        target.setframerate(SAMPLE_RATE)
        for first in range(0, total, SAMPLE_RATE):
            count = min(SAMPLE_RATE, total - first)
            samples = array("h")
            for frame in range(first, first + count):
                value = max(-0.98, min(0.98, sample_fn(frame / SAMPLE_RATE, seconds)))
                samples.append(int(value * 32767))
            target.writeframesraw(samples.tobytes())


def pad_sample(chords, t: float, seconds: float, level: float = 0.12) -> float:
    chord_seconds = 30.0
    chord_index = int(t // chord_seconds) % len(chords)
    next_index = (chord_index + 1) % len(chords)
    within = t % chord_seconds
    blend = max(0.0, min(1.0, (within - 23.0) / 7.0))
    blend = blend * blend * (3.0 - 2.0 * blend)
    value = 0.0
    for notes, gain in ((chords[chord_index], 1.0 - blend), (chords[next_index], blend)):
        for note, weight in notes:
            frequency = midi(note)
            value += gain * weight * (
                0.72 * math.sin(TAU * frequency * t)
                + 0.20 * math.sin(TAU * frequency * 2.003 * t + 0.2)
                + 0.08 * math.sin(TAU * frequency * 3.997 * t + 0.7)
            )
    return level * value * envelope(t, seconds)


def write_music_loops(temp: Path, ffmpeg: str) -> None:
    ambient_chords = [
        [(38, 0.56), (50, 0.36), (57, 0.23), (62, 0.16), (66, 0.11)],
        [(35, 0.54), (47, 0.35), (54, 0.23), (59, 0.16), (62, 0.10)],
        [(31, 0.54), (43, 0.36), (50, 0.24), (55, 0.16), (59, 0.11)],
        [(33, 0.54), (45, 0.36), (52, 0.23), (57, 0.16), (61, 0.10)],
        [(38, 0.56), (50, 0.36), (57, 0.23), (62, 0.16), (66, 0.11)],
        [(35, 0.54), (47, 0.35), (54, 0.23), (59, 0.16), (62, 0.10)],
    ]
    write_wav(
        temp / "moonlit-ambient.wav",
        MUSIC_SECONDS,
        lambda t, duration: pad_sample(ambient_chords, t, duration, 0.16),
    )

    piano_notes = [62, 66, 69, 73, 71, 69, 66, 62, 59, 62, 66, 69, 67, 66, 62, 59]
    note_gap = 2.25

    def piano_sample(t: float, duration: float) -> float:
        current = int(t / note_gap)
        value = 0.0
        for index in range(max(0, current - 2), current + 1):
            start = index * note_gap
            age = t - start
            if not 0.0 <= age < 5.2:
                continue
            note = piano_notes[index % len(piano_notes)]
            frequency = midi(note)
            strike = (1.0 - math.exp(-age * 24.0)) * math.exp(-age * 0.83)
            tone = (
                math.sin(TAU * frequency * age)
                + 0.40 * math.sin(TAU * frequency * 2.01 * age + 0.1)
                + 0.18 * math.sin(TAU * frequency * 3.97 * age + 0.3)
                + 0.07 * math.sin(TAU * frequency * 6.02 * age + 0.5)
            )
            value += tone * strike * 0.12
        return value * envelope(t, duration)

    write_wav(temp / "soft-piano.wav", MUSIC_SECONDS, piano_sample)

    for track in ("moonlit-ambient", "soft-piano"):
        subprocess.run(
            [
                ffmpeg,
                "-hide_banner",
                "-loglevel",
                "error",
                "-y",
                "-i",
                str(temp / f"{track}.wav"),
                "-codec:a",
                "libmp3lame",
                "-b:a",
                "128k",
                "-ar",
                str(SAMPLE_RATE),
                str(OUTPUT / f"{track}.mp3"),
            ],
            check=True,
        )


def tone(phase: float, harmonics: tuple[tuple[float, float], ...]) -> float:
    return sum(weight * math.sin(phase * multiple + offset) for multiple, weight, offset in harmonics)


def write_cues() -> None:
    cues = {
        "soft-chime": 2.5,
        "warm-bell": 2.8,
        "night-bird": 2.6,
    }

    def soft_chime(t: float, _duration: float) -> float:
        value = 0.0
        for start, note, gain in ((0.0, 76, 0.22), (0.34, 81, 0.19), (0.74, 83, 0.16)):
            age = t - start
            if age >= 0:
                frequency = midi(note)
                partials = (
                    math.sin(TAU * frequency * age)
                    + 0.34 * math.sin(TAU * frequency * 2.76 * age + 0.2)
                    + 0.12 * math.sin(TAU * frequency * 5.41 * age + 0.4)
                )
                value += gain * partials * math.exp(-age * 2.0)
        return value

    def warm_bell(t: float, _duration: float) -> float:
        value = 0.0
        for start, note, gain in ((0.0, 72, 0.20), (0.48, 79, 0.18), (0.96, 76, 0.16)):
            age = t - start
            if age >= 0:
                frequency = midi(note)
                partials = (
                    math.sin(TAU * frequency * age)
                    + 0.30 * math.sin(TAU * frequency * 2.01 * age + 0.3)
                    + 0.10 * math.sin(TAU * frequency * 3.93 * age + 0.6)
                )
                value += gain * partials * math.exp(-age * 1.65)
        return value

    def night_bird(t: float, _duration: float) -> float:
        value = 0.0
        for start, base, gain in ((0.08, 970.0, 0.13), (0.62, 1120.0, 0.12), (1.18, 890.0, 0.10)):
            age = t - start
            if 0.0 <= age < 0.78:
                glide = base + 85.0 * math.sin(math.pi * min(age / 0.78, 1.0))
                phase = TAU * (base * age + 85.0 * (0.78 / math.pi) * (1.0 - math.cos(math.pi * age / 0.78)))
                value += gain * math.sin(phase) * math.exp(-age * 2.4)
        return value

    generators = {"soft-chime": soft_chime, "warm-bell": warm_bell, "night-bird": night_bird}
    for cue, seconds in cues.items():
        write_wav(OUTPUT / f"{cue}.wav", seconds, generators[cue])


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        raise SystemExit("FFmpeg is required to encode the bundled music loops as MP3.")
    with tempfile.TemporaryDirectory(prefix="sleep-timer-audio-") as temp_directory:
        write_music_loops(Path(temp_directory), ffmpeg)
    write_cues()
    source_audio = ROOT / "tools" / "audio-source"
    required_sources = ("gentle-rain.mp3", "night-forest.mp3", "ocean-waves.mp3")
    if all((source_audio / name).is_file() for name in required_sources):
        subprocess.run([sys.executable, str(ROOT / "tools" / "mix_reference_audio.py")], check=True)
    else:
        print("Nature mixes left unchanged. Add the three Pixabay MP3s to tools/audio-source and run tools/mix_reference_audio.py to regenerate them.")
    print(f"Generated bundled audio in {OUTPUT}")


if __name__ == "__main__":
    main()
