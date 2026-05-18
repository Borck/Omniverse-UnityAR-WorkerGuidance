# Visual Effects

This guide covers the two custom shaders that give the AR experience its holographic look,
and the runtime components that drive them.

---

## At a glance

| Effect | Applies to | Component | Shader |
|---|---|---|---|
| **Holographic skin with double-tap heartbeat** | Per-step animated parts (downloaded GLBs) | `HologramApplier` | `Hologram.shader` |
| **Slice-plane materialize, then steady hologram skin** | Static fixture model overlay | `FixtureOverlay` | `FixtureReveal.shader` (transition) → `Hologram.shader` (steady, no pulse) |

Both shaders target Unity's **Built-in** render pipeline (no URP package required) and
use single-pass transparent forward rendering. They are designed to be cheap enough for
the VUZIX M4000's mobile GPU.

---

## 1. Hologram shader (animated parts)

`Assets/App/Shaders/Resources/Hologram.shader`, applied via `HologramApplier`.

### What it does

- Translucent cyan body so workers can still see real machinery through the part
- **Fresnel rim glow** — bright cyan edges that intensify at glancing angles
- **Scrolling scan lines** moving slowly upward across the world-space surface
- **Double-tap heartbeat pulse** — two close peaks ("lub-dub") then a rest, looping every 2 seconds (~30 BPM). Both brightness and alpha are modulated, so the part visibly fades in and out

### Properties

| Property | Default | Range | Purpose |
|---|---|---|---|
| `_BaseColor` | `(0.2, 0.8, 1.0, 1.0)` | RGBA | Body color (cyan) |
| `_RimColor` | `(0.4, 1.0, 1.0, 1.0)` | RGBA | Edge glow color |
| `_RimPower` | `2.5` | 0.5–8 | Sharpness of the fresnel falloff (higher = thinner rim) |
| `_RimIntensity` | `2.0` | 0–5 | Brightness multiplier on the rim |
| `_Alpha` | `0.18` | 0–1 | Body alpha at rest |
| `_ScanlineSpeed` | `0.6` | 0–5 | Scan line scroll speed |
| `_ScanlineDensity` | `60` | 1–200 | Lines per world-space unit |
| `_ScanlineIntensity` | `0.4` | 0–1 | Contrast between bright and dim lines |
| `_PulseSpeed` | `0.5` | 0–5 | `1 / period`. `0.5` = one heartbeat cycle every 2 seconds |
| `_PulseAmount` | `0.75` | 0–1 | Depth of the heartbeat. `0` = no pulse |

### Heartbeat shape

```hlsl
// Two close peaks at phase 0.08 and 0.24 of each cycle, then a long rest.
float phase = frac(_Time.y * _PulseSpeed);
float peak1 = exp(-pow((phase - 0.08) * 14.0, 2.0));
float peak2 = exp(-pow((phase - 0.24) * 14.0, 2.0));
float beat  = saturate(peak1 + peak2);
float pulse = lerp(1.0 - _PulseAmount, 1.0 + _PulseAmount, beat);
```

### Toggling at runtime

`AppBootstrap.useHologramShader` controls the static `HologramApplier.Enabled` flag.
When `false`, models load with their original GLB materials. The toggle is read once at
`Start()` — change the Inspector value before pressing Play.

---

## 2. Fixture-reveal shader

`Assets/App/Shaders/Resources/FixtureReveal.shader`, applied transiently by `FixtureOverlay`
during appear/disappear animations.

### What it does

- A **slice plane at world Y = `_RevealY`** discards every pixel above it
- A thin **glow band** just below the plane brightens from `_BaseColor` toward `_GlowColor`
- Below the band, the surface is a flat translucent body color

By animating `_RevealY` from the bottom of the model's bounds to the top, the model
"materializes" — the cyan slice sweeps up, leaving the surface visible behind it.

### Properties

| Property | Default | Range | Purpose |
|---|---|---|---|
| `_BaseColor` | `(0.75, 0.78, 0.82, 1.0)` | RGBA | Body color below the band (overridden per-renderer in `FixtureOverlay` from the original material) |
| `_GlowColor` | `(0.4, 1.0, 1.0, 1.0)` | RGBA | Slice band color |
| `_RevealY` | `-1000` | float | World Y where the slice plane sits (animated by `FixtureOverlay`) |
| `_GlowBandWidth` | `0.04` | 0–1 | Thickness of the glowing slice band in world units |
| `_BodyAlpha` | `0.85` | 0–1 | Body alpha below the band |
| `_GlowIntensity` | `2.5` | 0–5 | Brightness multiplier on the glow color |

---

## 3. `FixtureOverlay` lifecycle

Attached at runtime to the Vuforia `ObserverBehaviour` GameObject inside
`AppBootstrap.OnSessionStepActivated → ResolveAndPresentStepAsset → onLoaded`.

### State machine

```
                    Vuforia status: TRACKED / EXTENDED_TRACKED
                                    │
                                    ▼
[hidden]  ──────────►  [revealing]  ──── 0.7s ────►  [steady hologram]
                            ▲                                │
                            │                                │
                            └──── re-acquired during ────────┘
                                    hide debounce
                                    (0.3s window)
                                    │
                                    ▼
                  [hiding]  ◄────  Vuforia: NO_POSE / LIMITED
                     │
                     │  0.7s slice top→bottom
                     ▼
                  [hidden] (instance.SetActive(false))
```

### Phase details

- **Reveal (0.7 s)** — `FixtureReveal` shader applied to all renderers. `_RevealY` animates from `bounds.min.y` → `bounds.max.y`. `_GlowIntensity` = 2.5 (`InitialGlowIntensity` constant).
- **Steady hologram (until tracking lost)** — `Hologram` shader applied (a separate static instance with `_PulseAmount = 0`, `_PulseSpeed = 0` so it does **not** pulse like the animated parts). The fixture is calm and consistent; the animated parts pulse on top of it.
- **Hide debounce (0.3 s)** — Vuforia flickers between `TRACKED` ↔ `NO_POSE` regularly; we wait 0.3 s before starting the hide animation. If status returns to `TRACKED` during the wait, the hide is aborted and the fixture stays visible.
- **Hide (0.7 s)** — `FixtureReveal` shader re-applied. `_RevealY` animates `bounds.max.y` → `bounds.min.y`.

### Instantiation

The fixture prefab is set on `AppBootstrap.fixtureOverlayPrefab` in the Inspector. If
the field is empty, the overlay is silently skipped (the rest of the app works normally).
The instance is parented to the observer transform with `localScale = Vector3.one * 0.1f`
because the source OBJ is in millimeters while Vuforia uses meters.

### Tunable constants (top of `FixtureOverlay.cs`)

```csharp
private const float RevealDuration = 0.7f;
private const float HideDebounceSeconds = 0.3f;
private const float InitialGlowIntensity = 2.5f;
```

Feel free to expose these as `[SerializeField]` fields if you want runtime tuning.

---

## Performance notes (VUZIX M4000)

- Both shaders are single-pass transparent forward.
- The hologram shader does fresnel + scanlines + heartbeat in ~10 ALU ops per pixel — negligible.
- `FixtureOverlay` only swaps materials twice per tracking event (reveal start, reveal end). The slice animation updates one float per material per frame.
- The biggest cost is **transparency overdraw** when the fixture and animated parts overlap. If you observe frame drops, lower the fixture's `_BodyAlpha` to reduce visible overdraw, or apply a depth pre-pass to the fixture.

---

## See also

- [`unity-client.md`](./unity-client.md) — `AppBootstrap` Inspector reference, runtime asset flow
- The shaders live at `client-unity/Assets/App/Shaders/Resources/` so they are always
  included in the build via Unity's Resources mechanism.
