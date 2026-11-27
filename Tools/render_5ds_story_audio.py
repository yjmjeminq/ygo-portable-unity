"""Render the original Beyond Destiny soundtrack and Foley as game-ready stereo PCM WAVs.

Run with Python and NumPy. All melodies, harmony, timbres and sound sources are defined here.
"""
import argparse
from dataclasses import dataclass
import hashlib
import math
from pathlib import Path
import wave

import numpy as np


RATE = 22050
TAU = 2 * math.pi
ASSETS = Path(__file__).resolve().parents[1] / "Assets/StreamingAssets/TagForce/5DS/Audio"
MAJOR = (0, 2, 4, 5, 7, 9, 11)
MINOR = (0, 2, 3, 5, 7, 8, 10)
DORIAN = (0, 2, 3, 5, 7, 9, 10)


@dataclass(frozen=True)
class Theme:
    tempo: int
    key: int
    scale: tuple
    chords: tuple
    melody: tuple
    lead: str
    pulse: str
    meter: int = 4


# Chord roots and melody are scale degrees. The rest (-1) leaves room for dialogue.
THEMES = {
    "rain_signal": Theme(64, 47, MINOR, (0, 3, 5, 1), (0, -1, 2, -1, 1, -1, 5, 4), "reed", "drone"),
    "black_rose": Theme(126, 50, MINOR, (0, 5, 3, 4), (4, 7, 6, 2, 3, 1, 2, 4), "strings", "march"),
    "psychic_battle": Theme(150, 48, DORIAN, (0, 3, 1, 6), (7, 4, 6, 9, 7, 2, 5, 4), "brass", "drive"),
    "scars": Theme(72, 49, MINOR, (0, 5, 2, 4), (4, -1, 2, 1, 0, -1, 2, 1), "piano", "space"),
    "festival": Theme(118, 53, MAJOR, (0, 3, 1, 4), (4, 7, 8, 10, 6, 5, 4, 2), "brass", "march"),
    "infiltration": Theme(105, 46, MINOR, (0, 2, 1, 4), (0, -1, 1, 4, -1, 3, -1, 2), "synth", "pulse"),
    "nocturne": Theme(76, 52, DORIAN, (0, 5, 3, 4), (2, 4, -1, 6, 5, 2, 1, -1), "guitar", "space"),
    "arcadia": Theme(86, 45, MINOR, (0, 1, 3, 6), (2, 1, -1, 5, 4, -1, 1, 6), "strings", "pulse"),
    "arcadia_low": Theme(80, 44, MINOR, (0, 1, 3, 6), (2, 1, -1, 5, 4, -1, 1, 6), "strings", "drone"),
    "overture": Theme(107, 51, MINOR, (0, 6, 3, 4), (0, 3, 4, -1, 7, 6, 5, 3), "brass", "march"),
    "backlash": Theme(148, 47, MINOR, (0, 1, 5, 6), (0, 4, 5, 8, 7, 6, 3, 1), "synth", "drive"),
    "ruins": Theme(69, 48, MINOR, (0, 5, 3, 4), (5, -1, 4, 2, 3, -1, 1, 0), "cello", "space"),
    "tomorrow": Theme(82, 52, MAJOR, (0, 5, 3, 4), (4, 2, 5, -1, 7, 6, 4, 3), "piano", "space"),
    "garage": Theme(114, 55, MAJOR, (0, 2, 3, 4), (0, 2, 5, 4, 6, 5, 2, -1), "guitar", "bounce"),
    "violet_fire": Theme(78, 48, MINOR, (0, 2, 5, 4), (4, 2, 3, -1, 7, 5, 4, 3), "piano", "resolve"),
    "star_miracle": Theme(132, 53, MAJOR, (3, 0, 5, 4), (3, 5, 7, 9, 8, 6, 4, 2), "strings", "march"),
    "night_circuit": Theme(120, 49, DORIAN, (0, 3, 6, 4), (4, -1, 9, 7, 6, 4, 5, -1), "synth", "drive"),
    "scar_kiss": Theme(66, 55, MAJOR, (5, 3, 0, 4), (2, 4, -1, 5, 4, 3, 1, -1), "piano", "resolve"),
    "synchro_killer": Theme(150, 44, MINOR, (0, 2, 6, 1), (0, 1, 4, 6, 5, 3, 1, 4), "synth", "drive"),
    "lily_waltz": Theme(104, 52, MINOR, (0, 3, 5, 4), (7, 5, 4, 3, 6, -1, 4, 2), "strings", "waltz", 3),
    "fleur_charge": Theme(158, 52, MINOR, (0, 5, 3, 4), (2, 4, 7, 9, 11, 8, 7, 4), "guitar", "drive"),
    "mask_breaks": Theme(141, 50, DORIAN, (0, 3, 5, 6), (0, 3, 7, 9, 8, 6, 5, 3), "strings", "drive"),
    "zone": Theme(73, 45, MINOR, (0, 2, 1, 4), (0, -1, 3, 2, 1, -1, 4, -1), "choir", "drone"),
    "bifrost": Theme(148, 54, DORIAN, (0, 3, 5, 6), (0, 4, 7, 6, 9, 7, 4, 5), "brass", "drive"),
    "rescue": Theme(83, 54, MAJOR, (5, 3, 0, 4), (2, 4, 7, 6, 8, 7, 5, 4), "strings", "resolve"),
    "home": Theme(86, 53, MAJOR, (0, 2, 3, 4), (0, 2, 4, -1, 7, 5, 4, 2), "piano", "bounce"),
}


def timeline(duration):
    return np.arange(round(RATE * duration), dtype=np.float64) / RATE


def sweep(t, start, end):
    return np.sin(TAU * (start * t + (end - start) * t * t / (2 * t[-1])))


def envelope(t, attack, release):
    return np.minimum(1, t / attack) * np.minimum(1, (t[-1] - t) / release)


def noise(t, rng, color="bright"):
    white = rng.standard_normal(t.size)
    if color == "bright":
        return white * .33
    size = 57 if color == "dark" else 13
    return np.convolve(white, np.ones(size) / math.sqrt(size), mode="same") * .29


def instrument(name, midi, duration, rng):
    t = timeline(duration)
    hz = 440 * 2 ** ((midi - 69) / 12)
    p = TAU * hz * t
    if name in ("piano", "guitar"):
        ratios = (1, 2.005, 3.01, 4.016) if name == "piano" else (1, 2, 3, 5)
        decay = .62 if name == "piano" else .36
        sound = sum(np.sin(p * ratio) * np.exp(-t / (decay / (1 + .52 * i))) / (1 + i * 1.9)
                    for i, ratio in enumerate(ratios))
        sound += noise(t, rng, "warm") * np.exp(-t / .027) * .12
        return sound * envelope(t, .011, .12) * .66
    if name == "bass":
        return (.82 * np.sin(p) + .16 * np.sin(2 * p)) * envelope(t, .025, .11)
    if name in ("reed", "synth"):
        tone = np.sin(p + .13 * np.sin(TAU * 4.25 * t)) + .24 * np.sin(2 * p) + .15 * np.sin(3 * p)
        if name == "synth":
            tone += .28 * np.sin(1.006 * p) + .18 * np.sin(4 * p)
        return tone * envelope(t, .095 if name == "reed" else .03, .17) * .48
    if name == "brass":
        overtones = .78 * np.sin(p) + .39 * np.sin(2 * p) + .23 * np.sin(3 * p) + .065 * np.sin(5 * p)
        return overtones * envelope(t, .1, .15) * (.66 + .08 * np.sin(TAU * 3.1 * t))
    if name == "choir":
        color = np.sin(p) + .23 * np.sin(2 * p) + .16 * np.sin(4 * p) + .08 * np.sin(7 * p)
        return color * envelope(t, .38, .36) * (.76 + .11 * np.sin(TAU * .76 * t))
    vibrato = .25 * np.sin(TAU * 4.2 * t) * np.minimum(1, t / .34)
    color = np.sin(p + vibrato) + .21 * np.sin(2 * p + vibrato) + .13 * np.sin(3 * p)
    color += .27 * np.sin(p * 1.004 - vibrato)
    return color * envelope(t, .24 if name == "cello" else .18, .22) * .56


def stereo_add(mix, values, time, level, pan=0.0):
    start = round(RATE * time)
    stop = min(len(mix), start + len(values))
    if stop <= start:
        return
    gains = np.sqrt(np.array([1 - pan, 1 + pan]) / 2)
    mix[start:stop] += values[:stop-start, None] * (level * gains)


def delay_wrap(mix, offsets, weights):
    delayed = mix.copy()
    for i, (offset, weight) in enumerate(zip(offsets, weights)):
        delayed += np.roll(mix[:, ::-1] if i % 2 == 0 else mix, round(RATE * offset), axis=0) * weight
    return delayed


def percussion(kind, rng):
    t = timeline(.42 if kind == "kick" else .25)
    if kind == "kick":
        phase = TAU * (41 * t + 8.2 * (1 - np.exp(-t * 15)))
        wavelet = np.sin(phase) * np.exp(-t * 12) + noise(t, rng, "warm") * np.exp(-t * 95) * .25
    elif kind == "snare":
        wavelet = noise(t, rng, "warm") * np.exp(-t * 15) + .36 * np.sin(TAU * 174 * t) * np.exp(-t * 33)
    else:
        wavelet = noise(t, rng) * np.exp(-t * 34)
    return wavelet * envelope(t, .006, .050)


def score(theme, rng):
    beat = 60 / theme.tempo
    bar_duration = beat * theme.meter
    percussive = theme.pulse in ("march", "drive", "bounce", "pulse")
    sparse = theme.pulse in ("space", "drone")
    duration = 8 * bar_duration
    mix = np.zeros((round(RATE * duration), 2))
    tail = np.zeros_like(mix)
    for bar in range(8):
        start = bar * bar_duration
        root = theme.chords[bar % len(theme.chords)]
        swell = .85 + .18 * math.sin((bar + .5) * math.pi / 8)
        def add(name, d, octave, offset, length, level, pan):
            midi = theme.key + 12 * octave + theme.scale[d % 7] + 12 * (d // 7)
            stereo_add(tail if name in ("piano", "guitar") else mix,
                       instrument(name, midi, length * beat, rng), start + offset * beat, level * swell, pan)

        bass_pattern = (0, 1.5, 2.5, 3.25) if theme.pulse == "drive" else (0, 2) if percussive else (0,)
        for i, offset in enumerate(bass_pattern):
            add("bass", root + (4 if i == 2 else 0), -1, offset,
                min(1.4 if percussive else theme.meter, theme.meter - offset + .05), .18 if percussive else .12, .0)

        for voice, interval in enumerate((0, 2, 4)):
            add("choir" if theme.lead == "choir" else "cello" if sparse else "strings",
                root + interval, 0, 0, theme.meter + .12, .072 if sparse else .042, (-.55, .15, .56)[voice])
        if theme.pulse != "drone":
            for i in range(theme.meter * 2):
                if sparse and i % 2:
                    continue
                d = root + (0, 4, 2, 4, 7, 4, 2, 4)[i]
                add("guitar" if theme.lead == "piano" else "piano", d, 0, i / 2, .79,
                    .058 if theme.pulse in ("space", "resolve") else .078, -.33 if i % 2 else .28)

        offsets = (0, 1.5) if theme.meter == 3 else (0, 1.75)
        for i, at in enumerate(offsets):
            degree = theme.melody[(bar * 2 + i) % len(theme.melody)]
            if degree < 0:
                continue
            if bar >= 4 and i == 1 and bar % 2 == 0:
                degree += 2
            add(theme.lead, degree, 1, at, 1.32 if percussive else 1.85,
                .31 if theme.pulse in ("resolve", "space", "waltz") else .25, -.15 if i == 0 else .15)
        if percussive:
            kicks = (0, 1.5, 2, 3.25) if theme.pulse == "drive" else (0, 2.5)
            for at in kicks:
                stereo_add(mix, percussion("kick", rng), start + at * beat, .19 if theme.pulse == "drive" else .12)
            for at in (1, 3):
                stereo_add(mix, percussion("snare", rng), start + at * beat, .11, .06)
            for at in np.arange(.5, theme.meter, .5):
                stereo_add(mix, percussion("hat", rng), start + at * beat, .045, -.38 if at % 1 else .38)
    # A stereo reflection rings underneath the dry signal and wraps seamlessly across repeat playback.
    return delay_wrap(tail, (.13, .21, .34), (.19, .11, .07)) + delay_wrap(mix, (.075, .155), (.05, .025))


def soft_ticks(mix, rng, times, level, variant="steps"):
    for i, at in enumerate(times):
        t = timeline(.31)
        freq = {"steps": 80, "door": 57, "clink": 930, "dishes": 1410, "paper": 420,
                "cloth": 215, "water_drop": 730, "metal": 1160, "glass": 2100}[variant]
        attack = noise(t, rng, "dark" if freq < 260 else "warm") * np.exp(-t * (14 if variant in ("cloth", "paper") else 36))
        ring = (.44 * sweep(t, freq * 1.14, freq * .84) + .31 * np.sin(TAU * freq * 2.24 * t)) * np.exp(-t * (16 if freq < 900 else 31))
        stereo_add(mix, (attack + ring) * envelope(t, .004, .05), at, level, (-.34, .26, -.06)[i % 3])


def foley(name, rng, ambience):
    if ambience:
        t = timeline(8.0)
        z = np.zeros((t.size, 2))
        streams = [noise(t, rng, "warm"), noise(t, rng, "dark"), noise(t, rng)]
        if name in ("rain", "storm"):
            rain = streams[2] * .38 + streams[0] * .2
            stereo_add(z, rain, 0, .55, -.27)
            stereo_add(z, np.roll(rain, 1879), 0, .48, .4)
            if name == "storm":
                rumble = noise(t, rng, "dark") * np.exp(-np.square((t - 3.9) * .84))
                stereo_add(z, rumble, 0, .38)
        elif name == "clock":
            soft_ticks(z, rng, np.arange(.45, 7.6, .97), .48, "clink")
        elif name == "machine_room":
            bed = (.41 * np.sin(TAU * 55 * t) + .14 * np.sin(TAU * 110 * t) + .17 * streams[0])
            stereo_add(z, bed, 0, .39, -.13)
            soft_ticks(z, rng, (.75, 2.4, 2.57, 4.7, 6.62, 7.05), .37, "clink")
        elif name == "wind":
            gust = (.61 + .20 * np.cos(TAU * t / 4) + .08 * np.sin(TAU * t / 2)) * (.63 * streams[1] + .52 * streams[0])
            stereo_add(z, gust, 0, .63, -.48)
            stereo_add(z, np.roll(gust, 7339), 0, .42, .36)
        elif name == "arena":
            crowd = sum(streams[i % 3] * (.37 + .18 * np.sin(TAU * (i + 1) * t / 8 + i)) for i in range(6)) / 2.6
            stereo_add(z, crowd, 0, .44, -.4)
            stereo_add(z, np.roll(crowd, 2979), 0, .43, .45)
            soft_ticks(z, rng, np.arange(.19, 7.2, .28), .09, "paper")
        return z

    lengths = {"tire": 1.28, "engine": 1.58, "siren": 2.08, "collapse": 1.75, "roar": 1.47,
               "shockwave": 1.25, "heartbeat": 1.1, "steps": 1.32, "dishes": 1.2, "fire": 1.47,
               "synchro": 1.39, "chime": 1.02}
    t = timeline(lengths.get(name, .85))
    mix = np.zeros((len(t), 2))
    if name in ("steps", "door", "clink", "dishes", "paper", "cloth", "water_drop", "metal", "glass"):
        times = {"steps": (.045, .39, .77, 1.01), "door": (.01, .18, .48), "clink": (.08, .31),
                 "dishes": (.02, .32, .54, .92), "paper": (.05, .19, .44), "cloth": (.04, .22),
                 "water_drop": (.04, .39), "metal": (.05, .22, .39), "glass": (.015, .09, .15, .23, .34, .45)}
        soft_ticks(mix, rng, times[name], .83 if name in ("glass", "door") else .56, name)
    elif name in ("siren", "engine", "tire", "radio", "electric", "fire", "swish"):
        if name == "siren":
            bend = TAU * (570 * t + 29 * np.sin(TAU * t * .71))
            core = np.sin(bend) * .7 + .17 * np.sin(bend * 2)
        elif name == "engine":
            core = sum(sweep(t, 45 * i, 102 * i) / i for i in range(1, 7)) * .42
            core += noise(t, rng, "dark") * .21
        elif name == "tire":
            core = .48 * sweep(t, 1180, 680) + .20 * sweep(t, 1410, 830) + .43 * noise(t, rng, "warm")
        elif name == "radio":
            core = noise(t, rng) * (.24 + .49 * (np.sin(TAU * 14 * t) ** 8)) + np.sin(TAU * 820 * t) * np.exp(-t * 16) * .4
        elif name == "electric":
            core = noise(t, rng) * (.13 + .63 * (np.sin(TAU * 14 * t) ** 18)) + .29 * sweep(t, 150, 240)
        elif name == "fire":
            core = noise(t, rng, "dark") * .72 + noise(t, rng) * (.2 + .32 * np.sin(TAU * 7 * t) ** 10)
        else:
            core = noise(t, rng, "warm") * .47 + .13 * sweep(t, 315, 141)
        stereo_add(mix, core * envelope(t, .12 if name in ("engine", "tire") else .05, .26), 0, .75, -.28)
        stereo_add(mix, np.roll(core, 371) * envelope(t, .13, .28), 0, .24, .56)
    elif name in ("device", "synchro", "chime", "summon"):
        notes = {"device": (78, 81, 85), "synchro": (67, 74, 81, 86),
                 "chime": (76, 69), "summon": (62, 69, 74)}
        for i, note in enumerate(notes[name]):
            lead = "piano" if name in ("chime", "synchro") else "reed"
            stereo_add(mix, instrument(lead, note, .38, rng), .105 * i, .45, (-.46, .42, -.14, .13)[i])
        if name in ("synchro", "summon"):
            stereo_add(mix, sweep(t, 160, 500) * envelope(t, .42, .5) * .22, .1, .29)
            stereo_add(mix, percussion("kick", rng), .39, .63)
    elif name == "heartbeat":
        for at in (.01, .14, .56, .70):
            stereo_add(mix, percussion("kick", rng), at, .48 if at in (.01, .56) else .26)
    elif name in ("impact", "collapse", "roar", "shockwave"):
        decay = 8 if name == "impact" else 3.2
        body = noise(t, rng, "dark") * .80 + noise(t, rng, "warm") * .33 + .38 * sweep(t, 114, 40)
        if name == "roar":
            body += .31 * sweep(t, 187, 82) * (1 + .23 * np.sin(TAU * 29 * t))
        elif name == "shockwave":
            body += .62 * sweep(t, 590, 98) * (.66 + .34 * np.sin(TAU * 33 * t))
        stereo_add(mix, body * np.exp(-t * decay) * envelope(t, .017, .11), 0, .9)
        if name == "collapse":
            soft_ticks(mix, rng, (.09, .24, .37, .57, .69, 1.0, 1.15), .53, "door")
        else:
            stereo_add(mix, percussion("kick", rng), .023, .46)
    else:
        raise ValueError(f"Unknown Foley asset {name}")
    return delay_wrap(mix, (.036, .08), (.13, .055))


def render_wave(path, wave_data, target_peak, *, loop=False):
    # Final ramp prevents clicks on both hardware and software mixers.
    ramp = round(RATE * (.037 if loop else .008))
    wave_data[:ramp] *= np.linspace(0, 1, ramp)[:, None]
    wave_data[-ramp:] *= np.linspace(1, 0, ramp)[:, None]
    peak = np.max(np.abs(wave_data))
    if not np.isfinite(peak) or peak < .0001:
        raise ValueError(f"Silent or invalid audio {path.name}")
    pcm = np.round(wave_data * (target_peak * 32767 / peak)).astype("<i2")
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as file:
        file.setparams((2, 2, RATE, len(pcm), "NONE", "not compressed"))
        file.writeframes(pcm.tobytes())


def rng_for(name):
    return np.random.default_rng(int.from_bytes(hashlib.sha256(name.encode()).digest()[:8], "little"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--only", nargs="+", help="Render just these cue names")
    args = parser.parse_args()
    foley_catalog = {
        "Ambience": ("storm", "rain", "wind", "clock", "arena", "machine_room"),
        "Effects": ("dishes", "chime", "siren", "heartbeat", "steps", "door", "tire", "engine", "radio", "device",
                    "synchro", "glass", "roar", "fire", "shockwave", "electric", "collapse", "metal", "water_drop",
                    "paper", "cloth", "summon", "impact", "swish", "clink"),
    }
    expected = set(THEMES) | {name for names in foley_catalog.values() for name in names}
    wanted = set(args.only) if args.only else expected
    if wanted - expected:
        parser.error(f"Unknown audio names: {', '.join(sorted(wanted - expected))}")
    for name, theme in THEMES.items():
        if name in wanted:
            render_wave(ASSETS / "Music" / f"{name}.wav", score(theme, rng_for(name)), .61, loop=True)
    for folder, names in foley_catalog.items():
        for name in names:
            if name not in wanted:
                continue
            ambient = folder == "Ambience"
            render_wave(ASSETS / folder / f"{name}.wav", foley(name, rng_for(name), ambient), .41 if ambient else .60,
                        loop=ambient)
    print(f"Rendered {len(wanted)} original Beyond Destiny WAV files")


if __name__ == "__main__":
    main()
