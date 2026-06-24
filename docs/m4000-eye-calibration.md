# Vuzix M4000 Eye-Offset Calibration

Why holograms land off the real fixture on optical see-through, and how
the calibration corrects it across all distances and angles.

## 1. The problem: viewpoint mismatch

The M4000 is an **optical see-through** waveguide display. Two
viewpoints are involved:

| Viewpoint | Whose | What it does |
|---|---|---|
| **Camera** | M4000 hardware | Vuforia tracks from here, and renders the hologram as the camera would see it |
| **Eye** | The operator's eye | Looks at the rendered framebuffer through the waveguide optics |

These are NOT the same point. Measured on the M4000, the camera sits
**~5.5 cm to one side** of the eye and **~4 cm in front** of it. So
Vuforia draws "what the camera sees," and your eye looks at that
drawing from a different position. The two viewpoints disagree, and
the hologram lands offset from the real part.

In short: Vuforia renders for the wrong viewpoint, because it does
not know where the operator's eye is.

## 2. Why the offset is distance-dependent (parallax)

This is the key insight that determines what kind of correction is
needed. The mismatch between the camera viewpoint and the eye viewpoint
is **parallax** — and parallax depends on distance:

```
angular shift ≈ atan(camera_eye_offset / distance_to_fixture)
```

| Working distance | Angular shift from 5.5 cm lateral offset |
|---|---|
| 50 cm (hands-on assembly) | ~6.3° — very noticeable |
| 1 m | ~3.1° |
| 2 m | ~1.6° — small |
| 5 m | ~0.6° — almost invisible |

Two consequences:

1. A simple "shift the hologram 6° to the right" fix only works at
   one distance. Move closer or farther and it breaks.
2. The fix has to be **distance-aware**: more correction up close,
   less correction far away.

## 3. Why Vuforia doesn't already fix this

Vuforia ships with eye-camera transforms for officially supported
optical see-through devices (HoloLens, Magic Leap — gated by
`IDeviceInfo.IsSeethruEyewearDevice`). On those devices it accepts a
per-user eye calibration and injects an eye-to-camera transform when
rendering.

The M4000 is **not** on Vuforia's supported optical see-through list.
Vuforia treats it as a regular Android device and renders from the
camera viewpoint with no eye-offset compensation. The Vuzix firmware
calibrates the display optics (so the waveguide image is geometrically
correct) but does not feed an eye position back to Vuforia.

So the camera-eye correction has to be done **in our app**.

## 4. The solution: a viewpoint shift via content offset

We don't have a way to ask Unity to "render from the eye instead of
the camera." But there's a mathematical equivalence we can exploit:

> Translating the rendering **viewpoint** by `−e` produces the same
> image as translating all rendered **content** by `+e`, for a fixed
> set of geometry.

So instead of moving the camera by the camera→eye vector, we move all
the **tracked content** by the inverse: shift the
`AnimationRoot` (the parent of every tracked virtual thing) by the
calibrated offset, expressed in camera space.

A fixed camera-space content shift, run through the normal perspective
projection, **auto-corrects parallax at every distance**. We don't
compute "how much shift per 5 cm of distance" — the perspective math
does it for free:

- Object at 50 cm → correction is large in pixels (because parallax
  is large there).
- Object at 2 m → correction is small in pixels (because parallax is
  small there).

…all from one fixed eye-offset vector. The calibration captures the
true **3D camera→eye vector** once; the perspective projection does
the per-distance arithmetic on every frame.

## 5. What we apply each frame

`AppBootstrap.ApplyEyeOffset` runs in `LateUpdate` (after Vuforia
updates the observer pose):

```csharp
Vector3 worldShift = cam.transform.TransformVector(_eyeOffset.OffsetMeters);
_activeObserverTransform.localPosition = parent.InverseTransformVector(worldShift);
```

- `_eyeOffset.OffsetMeters` is the persisted camera-space offset
  vector in metres (X = right, Y = up, Z = forward).
- `cam.transform.TransformVector(...)` rotates that into world space
  using the camera's **current orientation**. So if the operator
  tilts their head, the offset rotates with the head — which is
  correct, because the eye-camera relationship is rigid (eyes move
  *with* the head, not separately).
- The result is written to `AnimationRoot.localPosition`, expressed
  in the parent's space (the Vuforia observer).

`AnimationRoot` is the parent of both the step-animation `ModelAnchor`
and the fixture `OverlayAnchor`, so a single offset moves both — the
whole tracked scene is corrected together.

This composes cleanly with the FOV zoom: the FOV scaling runs on the
projection matrix, the eye offset runs on the content transform.
They don't interfere.

## 6. Why one calibration step isn't enough

If you only calibrate from **one** pose (say, standing close and
perpendicular), you tune the lateral X/Y until the hologram aligns at
that pose. **But many different 3D offsets produce that same 2D
alignment from a head-on view.** You can't distinguish them — the Z
(depth) component is invisible when you're looking square at the
fixture.

The moment you change pose — view from an angle, or step closer/
farther — the wrong Z reveals itself as drift.

Solution: calibrate at **two distances**. That gives the algebra
enough constraints to pin down the full 3D vector. Once Z is right,
the offset is the *true* rigid camera→eye vector, and the correction
is correct at every head pose, not just perpendicular.

This is why the panel implements a **guided two-step flow**, not free
nudging.

## 7. Helping the worker: physical Z seed

The M4000's camera sits a known distance **in front of** the eye —
roughly 4 cm. So we **seed Z to +0.04 m** in `EyeOffsetCalibration`
(see `DefaultForwardMeters`). This means:

- The depth component is approximately correct **before any worker
  calibration runs**.
- Even an operator who only does the first (lateral) step gets a much
  better experience than starting from zero Z.
- The fine-tune during the calibration is small adjustments around an
  already-good baseline, not a search across the full range.

`ResetOffset()` resets X/Y to 0 but keeps Z at the seed — a reset
never throws away the physical depth.

## 8. The two-step calibration procedure (worker)

1. Open the **Eye** tab in the control drawer.
2. **Step 1/2 (close & perpendicular):**
   - Stand at normal close working distance, face the fixture squarely.
   - Tap `◄ Left / Right ► / ▲ Up / ▼ Down` until the hologram sits
     exactly on the real part.
   - Tap **Next ▶**.
3. **Step 2/2 (far):**
   - Step back to roughly **2× the original distance**.
   - If the hologram has drifted, tap `Near − / Far +` until it
     re-aligns.
   - Touch up with `◄ Left / Right ►` if needed.
   - Tap **Finish ✓**.
4. The calibration is persisted. From now on, the hologram stays
   aligned at any distance and any head angle (subject to the limits
   in §10).

If at some point alignment drifts at an angle, that points at an
incomplete depth calibration — open the panel and tap
**Recalibrate** to walk the two-step flow again.

## 9. Code modules

### 9.1 `client-unity/Assets/App/Runtime/EyeOffsetCalibration.cs`

**Role:** State holder. Lives on `Camera.main`. Stores the
camera-space offset vector (metres), persists each component to
`PlayerPrefs`, exposes `Nudge(dx, dy, dz)` and `ResetOffset()`. Z
defaults to `DefaultForwardMeters = 0.04f` so the depth component is
seeded.

### 9.2 `client-unity/Assets/App/UI/EyeOffsetPanel.cs`

**Role:** Draws the Eye section of the control drawer. Implements
the guided two-step flow (`_step = 0 → 1 → 2`). `ResetGuide()`
restarts the flow when the section opens. `DrawContent()` switches
between step 1, step 2, and the done screen. Nudges
`EyeOffsetCalibration.OffsetMeters` in 5 mm increments.

### 9.3 `client-unity/Assets/App/Runtime/AppBootstrap.cs`

**Role:**
- Attaches `EyeOffsetCalibration` to `Camera.main` at startup
  (`EnsureEyeOffset` in `Start`).
- Creates `EyeOffsetPanel` and binds it into the control drawer.
- Runs `ApplyEyeOffset()` in `LateUpdate`: transforms the
  camera-space offset to world space and writes it as
  `_activeObserverTransform.localPosition`.

## 10. What this calibration does NOT fix

The eye-offset calibration corrects **viewpoint parallax**. It does
not correct:

- **Vuforia tracking error at oblique angles.** Vuforia's
  model-target pose estimate gets less accurate as the camera view
  becomes edge-on. Residual that changes with viewing **angle** (not
  distance) after a good calibration usually points here. Best
  mitigation: train workers to view roughly square to the fixture.
- **Drift from the GLB's exported origin not being at the part
  geometric centre.** Affects the FOV-zoom path, not the eye-offset
  path. See `docs/m4000-fov-tuning.md` §8.1.
- **Waveguide micro-distortion** at the eyebox edges. The M4000's
  optics are not perfectly flat across the full ~28° display FOV;
  alignment may degrade slightly when the hologram sits at the
  display extremes. Mitigation: keep the hologram near eye centre.

## 11. File reference

- `client-unity/Assets/App/Runtime/EyeOffsetCalibration.cs` — state +
  PlayerPrefs persistence
- `client-unity/Assets/App/UI/EyeOffsetPanel.cs` — guided two-step
  panel in the control drawer
- `client-unity/Assets/App/Runtime/AppBootstrap.cs` —
  `EnsureEyeOffset`, `ApplyEyeOffset` (per-frame `LateUpdate`)
- `docs/m4000-fov-tuning.md` — companion doc explaining the FOV zoom,
  which is geometrically independent of the eye offset
