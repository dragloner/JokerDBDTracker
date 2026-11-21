"""Render intro sound design from a 48 kHz PCM16 stereo decode of Sounds/psi.mp3.

Usage: python tools/render_intro_audio.py artifacts/psi-source.wav
Requires numpy. The original player MP3 is never modified.
"""
import sys
import wave
from pathlib import Path

import numpy as np

RATE = 48000
OUTPUT = Path(__file__).resolve().parents[1] / "Assets" / "Intro"


def filtered(signal, low, high):
    frequencies = np.fft.rfftfreq(len(signal), 1 / RATE)
    response = (1 - np.exp(-(frequencies / low) ** 4)) * np.exp(-(frequencies / high) ** 4)
    return np.fft.irfft(np.fft.rfft(signal, axis=0) * response[:, None], n=len(signal), axis=0)


def loop_layer(source, length, speed):
    # Overlapping tapered grains preserve the original texture without hard loop seams.
    grain_length = int(len(source) / speed)
    positions = np.arange(grain_length) * speed
    grain = np.column_stack([np.interp(positions, np.arange(len(source)), source[:, c]) for c in range(2)])
    overlap = int(RATE * 1.4)
    envelope = np.ones(grain_length)
    fade = np.sin(np.linspace(0, np.pi / 2, overlap)) ** 2
    envelope[:overlap] = fade
    envelope[-overlap:] = fade[::-1]
    output = np.zeros((length, 2))
    weights = np.zeros(length)
    step = grain_length - overlap
    for start in range(-step, length, step):
        a, b = max(0, start), min(length, start + grain_length)
        offset = a - start
        output[a:b] += grain[offset:offset + b - a] * envelope[offset:offset + b - a, None]
        weights[a:b] += envelope[offset:offset + b - a]
    return output / np.maximum(weights[:, None], 1e-9)


def save(name, signal, peak, fade_in, fade_out):
    signal = signal - signal.mean(axis=0)
    for seconds, reverse in ((fade_in, False), (fade_out, True)):
        count = min(int(seconds * RATE), len(signal))
        ramp = np.sin(np.linspace(0, np.pi / 2, count)) ** 2
        if reverse:
            signal[-count:] *= ramp[::-1, None]
        else:
            signal[:count] *= ramp[:, None]
    signal *= peak / max(float(np.abs(signal).max()), 1e-9)
    assert np.isfinite(signal).all() and np.abs(signal).max() < 1
    with wave.open(str(OUTPUT / name), "wb") as output:
        output.setparams((2, 2, RATE, 0, "NONE", "not compressed"))
        output.writeframes(np.rint(signal * 32767).astype("<i2").tobytes())
    print(f"{name}: {len(signal) / RATE:.2f}s, peak {np.abs(signal).max():.3f}, RMS {np.sqrt(np.mean(signal ** 2)):.3f}")


with wave.open(sys.argv[1], "rb") as input_audio:
    if (input_audio.getframerate(), input_audio.getnchannels(), input_audio.getsampwidth()) != (RATE, 2, 2):
        raise ValueError("Expected 48 kHz stereo PCM16 source")
    source = np.frombuffer(input_audio.readframes(input_audio.getnframes()), dtype="<i2").reshape(-1, 2) / 32768.0

source = filtered(source, 65, 4400)
length = RATE * 24
time = np.arange(length) / RATE
recognizable = loop_layer(source, length, 1.0)
depth = filtered(loop_layer(source, length, .68), 45, 950)
mix = recognizable * (.62 + .07 * np.sin(2 * np.pi * time / 12))[:, None] + depth * .38
# Sparse, quiet reflections add space without smearing the entire source into hiss.
for delay, gain in ((.173, .13), (.397, .09), (.719, .055)):
    offset = int(delay * RATE)
    mix[offset:] += recognizable[:-offset, ::-1] * gain
save("psi-ambience.wav", mix.copy(), .65, 1.5, 1.8)
save("psi-wake.wav", mix[RATE * 3:RATE * 5].copy(), .42, .2, 1.2)
save("psi-enter.wav", mix[RATE * 8:RATE * 8 + int(RATE * .8)].copy(), .48, .07, .5)
