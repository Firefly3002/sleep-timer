# Sleep Timer audio credits

## App-authored audio

Moonlit ambient, Soft piano, Soft binaural, and the three end cues are original procedural compositions generated for Sleep Timer by `tools/generate_sleep_audio.py`. They contain no third-party audio samples.

The binaural track is an original 110 Hz / 114 Hz stereo tone beneath a slow pentatonic pad. The YouTube video “Pentatonic Waves by AcousticSheep LLC | Binaural Beats for Falling Asleep” was a listening reference only; no part of its audio is included or copied: https://www.youtube.com/watch?v=lTeFC6kVSQU

## Nature mix sources

- **Gentle rain** is an edited, repeat-ready mix based on “Gentle Rain for Relaxation and Sleep” by **Eryliaa**, published on Pixabay May 6, 2025: https://pixabay.com/sound-effects/nature-gentle-rain-for-relaxation-and-sleep-337279/
- **Night forest** is an edited, repeat-ready mix based on “Night Forest with Frogs and Crickets for Sleep” by **Eryliaa**, published on Pixabay December 17, 2025: https://pixabay.com/sound-effects/nature-night-forest-with-frogs-and-crickets-for-sleep-451153/

Both recordings are used under the Pixabay Content License (https://pixabay.com/service/license-summary/). The app's versions are new mixes: each is equalized, leveled, blended with a Sleep Timer-authored stereo chord pad, crossfaded at the repeat point, and peak-limited. These mixes contain audible original layers; the unmodified Pixabay files are not included in the repository or installer. Contributor credit is included here.

To regenerate the two nature mixes, put the downloaded source files in the ignored `tools/audio-source` folder as `gentle-rain.mp3` and `night-forest.mp3`, then run `python tools/mix_reference_audio.py`. The source recordings are not redistributed by that script. Python 3 and FFmpeg are required.

## Distribution

The app includes only the authored audio and the edited mixes described above. No separate third-party music or sound-effect file is shipped in its original form.
