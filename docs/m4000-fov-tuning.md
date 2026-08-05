# Vuzix M4000 FOV Tuning

How the on-glass scope zoom works in the Unity client: what the underlying
math is, why naive projection-matrix overrides Y-flipped the world, what
finally fixed it (in-place scaling of Vuforia's matrix), and how we
additionally anchor the magnification on the tracked fixture to avoid
off-axis drift.

## 1. The problem we set out to solve

The Vuzix M4000 is an optical see-through monocular waveguide with **two
different FOVs at play**:

| FOV | Value | What it is |
|---|---|---|
| **Camera FOV** | ~37° vertical | The vertical angle the M4000's tracking camera sees of the world. Vuforia derives its projection matrix from this. Measured live from the matrix (`vfov = 2·atan(1/abs(m11))`) — check the logcat line for your unit's exact value. |
| **Waveguide / display FOV** | **~14° vertical** (28° **diagonal**) | The angular slice of the framebuffer the optics actually project to the eye. **The headline "28°" Vuzix spec is the _diagonal_ FOV.** For the ~16:9 WVGA panel that splits into ≈24.5° horizontal × **≈14° vertical**. |

> **Diagonal vs. vertical — the distinction that sets "true size."** The slider
> and the whole projection scale work in **vertical** FOV (the override scales
> `m11`, the vertical element). So the number that matters for matching size is
> the **vertical** display FOV ≈ **14°**, *not* the 28° diagonal headline. An
> earlier version of this doc treated 28° as the vertical display FOV and put
> "true size" at the 28° slider mark — that is wrong: at the 28° mark the hologram
> renders at only about **half** real angular size. Confirm the exact true-size
> slider value on glass (the setting where the hologram edges land on the real
> part); it is expected around **13–16°**.

These don't match. The consequence:

- Vuforia renders the virtual world at **~37°** vertical into the framebuffer.
- The waveguide then shows that framebuffer across only the eye's **~14°**
  vertical window.
- ~37° of rendered content displayed in ~14° of eye-angle ⇒ virtual content
  appears **much smaller than the real fixture** (about a third of real size at
  native — see §1b.4). The "everything looks far away" experience.

The FOV tuner narrows the render-side FOV so virtual content fills more of the
waveguide, with an on-glass slider for live tuning. The **"true size" line** —
where the hologram matches the real part 1:1 — is where the **render FOV equals
the _vertical_ display FOV**, i.e. the slider set to ≈ **14°** (confirm on glass).

| Render FOV (slider) | Effect through the ~14° vertical display |
|---|---|
| **37°** (slider max, = native) | ~37° shown in ~14° → ~⅓ real size (Vuforia default, "far away") |
| **28°** | still ~½ real size — **not** true size (28° is the diagonal spec) |
| **≈14°** | render ≈ vertical display FOV → **angular 1:1, true size** |
| **< 14°** | narrow world shown in ~14° → magnified past real / scope zoom |

## 1b. The size relationship: real ↔ camera ↔ display (the math)

This section is the theoretical foundation under everything else: a closed-form
relationship between an object's **real-life** size, what the **camera**
captures, and what the eye **sees** through the waveguide — and how it depends
on distance. If you read one section to understand the device, read this one.

### 1b.1 Everything is an angle, not a length

The eye, the camera, and the display all perceive **angular size**, not absolute
centimetres. An object of real height `H` at distance `D` from a viewpoint
subtends an angle:

```
θ = 2 · atan( H / (2·D) )          (≈ H/D radians for small angles)
```

Every "size" below means angular size. This is the single abstraction that makes
the whole system tractable.

### 1b.2 The three viewpoints

```
   REAL PART ──(θ_cam)──►  CAMERA  ──render──►  FRAMEBUFFER  ──display──►  EYE
        │                  FOV_cam               fraction f               FOV_display
        └────────────────(θ_real, naked eye, distance D_eye)──────────────────┘
```

1. **Naked eye → real part** (the ground truth, "true size"):
   ```
   θ_real = 2 · atan( H / (2·D_eye) )
   ```
   where `D_eye` = eye-to-part distance.

2. **Camera → part** (what is captured). The part subtends `θ_cam` at the camera
   (distance `D_cam`), and fills this **fraction of the frame height**:
   ```
   f = tan(θ_cam / 2) / tan(FOV_render / 2)
   ```
   `FOV_render` is the FOV we actually render at — Vuforia's native camera FOV by
   default (~37°), or the FOV-tuner slider value when the override is on.

3. **Eye → display** (what you see). The display paints that fraction `f` across
   the display FOV, so the eye perceives the hologram at angle `θ_seen`:
   ```
   tan(θ_seen / 2) = f · tan(FOV_display / 2)
   ```

### 1b.3 The master formula

Chain the three together (substitute `f`, then `θ_cam ≈ H/D_cam` and
`θ_real ≈ H/D_eye` for the small-angle ratio). The **magnification of what you
see versus real life** is:

```
            what you SEE           tan(FOV_display / 2)        D_eye
   M  =  ───────────────────  =  ───────────────────────  ×  ─────────
              real life             tan(FOV_render / 2)         D_cam
```

Two factors, and they are the entire story:

| Factor | Name | Distance-dependent? |
|---|---|---|
| `tan(FOV_display/2) / tan(FOV_render/2)` | **FOV ratio** — dominant term | **No** |
| `D_eye / D_cam` = `1 + (forward_offset / D_cam)` | **Eye-offset term** — small | **Yes** |

- **FOV ratio** is the baseline size mismatch and the *only* thing the FOV slider
  changes. It does not depend on distance.
- **Eye-offset term** comes from the eye sitting ~4–7 cm *behind* the camera
  (`D_eye = D_cam + forward_offset`), so the eye is slightly farther from the
  part. It makes the hologram slightly bigger than the FOV ratio alone predicts,
  and it fades toward 1.0 as distance grows.

### 1b.4 What the dominant term predicts

With raw Vuforia (`FOV_render` = native **37°**), **vertical** display **≈14°**
(the 28° diagonal spec → ~14° vertical), ignoring the small term:

```
M ≈ tan(7°) / tan(18.5°) = 0.123 / 0.335 ≈ 0.37
```

The hologram is **~37 % of real size, independent of distance** — that *is* the
"everything looks small / far away" effect, and it is pure optics
(`FOV_display / FOV_camera`). (An earlier version used 28° as the *vertical*
display FOV and got 0.75; the correct vertical figure is ~14°, hence ~0.37.)

It also derives the **true-size point** directly: set `M = 1` (ignoring the small
term) ⇒ `tan(FOV_render/2) = tan(FOV_display/2)` ⇒
```
FOV_render = FOV_display ≈ 14°   →   hologram = real size
```
That is *why* ≈14° is the special slider value: it is the render FOV where the
**vertical** display FOV cancels the render FOV. (Not 28° — that is the diagonal.)

### 1b.5 The eye-offset (distance) term, quantified

`D_eye/D_cam = 1 + forward_offset/D_cam`. For a 5 cm forward offset:

| Distance to part `D_cam` | `D_eye / D_cam` | Extra size vs. FOV ratio |
|---|---|---|
| 30 cm | 1.17 | +17 % |
| 50 cm | 1.10 | +10 % |
| 1 m | 1.05 | +5 % |
| 2 m | 1.025 | +2.5 % |

So distance *does* matter — but as a **second-order ~10–17 % correction up
close** that vanishes with distance, riding on top of the distance-independent
FOV ratio.

### 1b.6 Worked example

Part `H` = 10 cm at `D_cam` = 50 cm; camera 37°, **vertical** display ≈14°, eye
5 cm behind the camera (`D_eye` = 55 cm):

| Quantity | Computation | Result |
|---|---|---|
| Real part (naked eye) | `2·atan(5/55)` | **10.4°** |
| Hologram @ native 37° | `M = 0.37 × 1.10 = 0.40` | **4.2°** (much smaller — "far away") |
| Hologram @ 28° | `M = 0.49 × 1.10 = 0.54` | **5.6°** (still about half — *not* true size) |
| Hologram @ 18° (default) | `M = (tan7/tan9) × 1.10 = 0.78 × 1.10 = 0.85` | **8.9°** (slightly under real) |
| Hologram @ ≈14° (true size) | `M = 1.00 × 1.10 = 1.10` | **11.4°** (~10 % bigger, from eye-offset) |

### 1b.7 Two magnification references (a common point of confusion)

"18° ≈ 2.1× zoom" (§5) and "18° ≈ 0.85× real" (above) are **both correct** — they
use different reference points:

- **2.1×** = hologram @ 18° vs. hologram @ **native 37°** =
  `tan(18.5°)/tan(9°)`. This is the override's own zoom factor (what
  `CameraFovOverride` multiplies the projection by). It is always measured
  against native, so it is unaffected by the display-FOV correction.
- **~0.85×** = hologram @ 18° vs. **real life** =
  `tan(7°)/tan(9°) × (D_eye/D_cam) = 0.78 × 1.10`. This uses the **vertical**
  display ≈14° as the reference, because true size happens at ≈14°. Note this is
  **less than 1** — at the 18° default the hologram is still slightly *smaller*
  than real; you reach 1:1 only near 14°.

Mixing these two references is the classic source of "wait, which number is the
zoom?" confusion. (The earlier doc compounded it by using 28° as the real-life
reference, which wrongly made 18° look like 1.7× real instead of ~0.85×.)

### 1b.8 Do we need the focal length / virtual-image distance?

The M4000 waveguide does **not** image directly on the retina — a collimator
forms a **virtual image at a fixed focal distance (~2 m)**. Natural question: is
there another distance to put in the formula?

**For angular size: no. The focal length is already inside `FOV_display`.**

A waveguide is a **collimating** system: the microdisplay sits at the focal plane
of a collimator of focal length `F`. A pixel at height `y` on the microdisplay
exits at angle:
```
α = atan( y / F )
```
and the whole microdisplay (half-height `Y`) therefore spans:
```
FOV_display / 2 = atan( Y / F )
```
So `F` is precisely the factor that turns display pixels into angles — and the
moment we express the result as a **FOV (28°)**, that conversion is already done.
`FOV_display` *is* `2·atan(Y/F)`. Adding `F` again would double-count. Working in
angles is exactly what absorbs the optics into one measured number.

Likewise the **eye-to-display distance (eye relief)** does **not** enter the size
math: a collimated display emits ~parallel rays per image point, so the eye sees
the same angle wherever it sits in the eyebox (eye relief affects the *eyebox*
and the swim, not size).

**Where the ~2 m focal distance *does* matter — a different axis than size:**

| Effect | Caused by the 2 m virtual image | Size impact |
|---|---|---|
| **Focus / accommodation** | Eye must focus at 2 m for the hologram, but at (e.g.) 50 cm for the real part — cannot be sharp on both at once (vergence–accommodation conflict) | none |
| **"Floating far" depth *feel*** | Accommodation cue says "2 m" even when the hologram is correctly sized/registered on a near part | none (a depth cue, separate from the size shrink) |
| **Comfort / eye strain** | Same conflict over long sessions | none |

So "looks far away" has **two independent contributors**: the **size** shrink
(FOV ratio, §1b.4) and the **focus depth** cue (fixed 2 m accommodation). Neither
the focal length nor the eye relief belongs in the angular-size formula — both are
absorbed by, or orthogonal to, `FOV_display`.

## 2. What didn't work — and why

The textbook way to change the render FOV is to write to
`Camera.projectionMatrix` (or `Camera.fieldOfView`) after Vuforia writes
its matrix — typically in `LateUpdate` or in URP's
`RenderPipelineManager.beginCameraRendering`. We tried **three** variants
of this approach:

1. **Replace with a fresh `Matrix4x4.Perspective(fov, aspect, near, far)`**
   in `LateUpdate`. → World rendered **Y-flipped**.
2. **`ResetProjectionMatrix()` + set `Camera.fieldOfView`** inside
   `beginCameraRendering` so Unity's auto-projection path took over.
   → Still Y-flipped.
3. **Negate `m[1,1]` on the fresh matrix + `GL.invertCulling = true`** to
   compensate the flip. → No flip, but the model shifted to the right and
   zoomed wrong at the "no-zoom" slider value, because the fresh matrix
   threw away Vuforia's principal-point offset and its actual FOV
   calibration.

### Root cause of the Y-flip

URP 17.x with **RenderGraph enabled** (the default in Unity 6) applies a
platform-dependent Y-flip when rendering to an intermediate texture (the
default render path on Android Direct3D/Vulkan). **Vuforia's projection
matrix has a negative `m[1,1]`** that bakes this flip in. URP renders
correctly when that sign convention is preserved.

A fresh `Matrix4x4.Perspective` produces a positive-`m[1,1]` matrix in
OpenGL convention. URP applies its expected flip on top of that → net
Y-flip in the rendered image. Negating `m[1,1]` ourselves fixed the
flip but discarded two pieces of Vuforia's calibration:

- The **principal point offset** (`m[0,2]`, `m[1,2]`) — the M4000's
  camera optical centre isn't exactly at image centre; Vuforia
  compensates with these offsets. Without them the model rendered
  shifted to one side.
- The **actual vertical FOV** — our `Matrix4x4.Perspective(60°, ...)`
  assumed Vuforia uses ~60° native FOV, but it's actually ~37°. Setting
  our matrix to 60° at "no zoom" actually zoomed *out* relative to
  Vuforia.

## 3. The fix: don't replace, just modulate

Instead of building a fresh matrix, **read Vuforia's matrix and scale
only the two FOV-controlling diagonal elements**:

```csharp
var p = cam.projectionMatrix;
float currentTanHalf = 1f / Mathf.Abs(p.m11);
float targetTanHalf  = Mathf.Tan(fovDegrees * 0.5f * Mathf.Deg2Rad);
float scale = currentTanHalf / targetTanHalf;
p.m00 *= scale;
p.m11 *= scale;
cam.projectionMatrix = p;
```

This preserves everything else by construction:

- **`m[1,1]` stays negative** (Vuforia's sign × positive scale = still
  negative), so URP RenderGraph treats our override identically to
  Vuforia's own writes — no Y-flip path is exercised,
  `GL.invertCulling` is not needed.
- **Principal point** (`m[0,2]`, `m[1,2]`) untouched → no lateral
  shift.
- **Near/far precision** (`m[2,2]`, `m[2,3]`) untouched.
- **Reference FOV is Vuforia's actual native vfov** (recovered each
  frame via `vfov = 2·atan(1/|m[1,1]|)`), not a guess.

This is the projection-space equivalent of a pure radial zoom around
the principal point. It produces the "projected" perspective feel
(parallax amplification when the head moves) without breaking
anything URP, Vuforia, or the platform expects from the matrix.

## 4. Fixture-anchored magnification

Pure radial zoom magnifies around the **principal point** (roughly
screen centre). The tracked fixture is essentially never exactly on
that point, so plain projection zoom makes the fixture **flow outward
toward the screen edge** as you zoom in — perceptible as a sideways
drift of the part you're trying to overlay.

Fix: after the diagonal scale, nudge the principal point so the
fixture's screen position stays fixed. With the fixture at camera-
space `(X, Y, Z)`:

```csharp
if (AnchorTransform != null)
{
    Vector3 viewPos = cam.worldToCameraMatrix.MultiplyPoint3x4(AnchorTransform.position);
    if (viewPos.z < -1e-3f) // strictly in front of camera
    {
        float u = viewPos.x / viewPos.z;
        float v = viewPos.y / viewPos.z;
        p.m02 += origM00 * (1f - scale) * u;
        p.m12 += origM11 * (1f - scale) * v;
    }
}
```

### Derivation (why this formula)

The horizontal NDC coordinate of a point at `(X, Y, Z)` after projection is:

```
ndc.x = m00·(X/Z) + m02·(−1/Z)·Z / (−Z/Z)  ≡  −m00·(X/Z) − m02
```

Requiring `ndc.x` to be invariant under `m00 → scale·m00` gives
`m02' = m02 + m00·(1 − scale)·(X/Z)`, which is the formula above. The
fixture's screen position becomes invariant under the zoom; everything
*else* still flows outward (correct projected behaviour for off-anchor
geometry).

`AppBootstrap` feeds the live tracked `_activeObserverTransform` to the
override each frame, so the anchor follows the fixture as Vuforia
updates pose. When no fixture is tracked, the override falls back to
plain principal-point magnification (harmless — nothing on screen to
drift anyway).

## 5. The slider-to-projection mapping

The slider exposes the target vertical FOV in **degrees**. The mapping
to the in-place matrix scale uses the half-angle tangent ratio:

```
scale = tan(detectedVuforiaVfov / 2) / tan(targetFovDegrees / 2)
```

`detectedVuforiaVfov` is recovered each frame from the matrix Vuforia
just wrote (`vfov = 2·atan(1/|m[1,1]|)`). It is **not** a constant —
it depends on device/resolution mode — so we don't hard-code it.

### Calibrated slider values for M4000

The slider top equals Vuforia's native (~37°) so the entire slider is
genuine zoom-in. **Scale** = zoom vs. native (`tan(18.5°)/tan(slider/2)`, what the
code multiplies by). **≈× real** = size vs. real life through the ~14° vertical
display (`tan(7°)/tan(slider/2)`, FOV-ratio only — multiply by ~1.05–1.1 up close
for the eye-offset term):

| Slider | Scale (vs native) | ≈× real | Effect |
|---|---|---|---|
| **37°** | ~1.0× | ~0.37× | Matches Vuforia native — much smaller than real ("far away") |
| 30° | ~1.25× | ~0.46× | Mild zoom (preset) |
| **28°** | ~1.34× | ~0.49× | ~half real — **not** true size (28° is the *diagonal* spec) |
| 24° | ~1.57× | ~0.58× | Moderate |
| **18°** (default) | ~2.1× | ~0.78× | Out-of-box ON setting (preset) — still slightly under real |
| **≈14°** | ~2.7× | **~1.0×** | **Angular 1:1 — true size (render ≈ vertical display FOV)** |
| **12°** | ~3.2× | ~1.17× | Heavy zoom (preset) — larger than real |
| 8° | ~4.8× | ~1.76× | Maximum magnification |

Constants live in `CameraFovOverride.cs`: `MinFovDegrees = 8f`,
`MaxFovDegrees = 37f`, `DefaultFovDegrees = 18f`. **Note:** the 18° default renders
at ~0.78× real (slightly small); for true-size overlay set the slider to ≈14°.
Confirm the exact true-size value on glass — see §1's diagonal-vs-vertical note.

## 6. Code modules

Three files. Each has one job; the state owner, the UI, and the
attachment/anchor-feed are deliberately split.

### 6.1 `client-unity/Assets/App/Runtime/CameraFovOverride.cs`

**Role:** Owns slider state (`OverrideEnabled`, `FovDegrees`), persists
`FovDegrees` to `PlayerPrefs`, and runs the per-frame
projection-matrix write that performs the zoom. Lives on `Camera.main`
(attached at runtime by `AppBootstrap`).

Subscribes to `RenderPipelineManager.beginCameraRendering` in
`OnEnable`. On disable (and when `OverrideEnabled` flips off), calls
`Camera.ResetProjectionMatrix()` to hand the camera back to
Vuforia/URP cleanly.

Notable design choices:
- **`OverrideEnabled` defaults `true`** but is **not persisted** — every
  launch starts ON regardless of any prior session, so a future
  regression that breaks rendering can't survive a process restart
  (uninstall is never required for recovery).
- **`FovDegrees` is persisted**, so the operator's preferred zoom level
  survives across sessions.
- `AnchorTransform` is a public property set by `AppBootstrap` each
  frame; when non-null the fixture-anchored compensation runs.
- A one-shot diagnostic log line prints Vuforia's detected native vfov
  the first frame the override fires, so we can verify device-level
  assumptions on logcat.

### 6.2 `client-unity/Assets/App/UI/FovTunerPanel.cs`

**Role:** Draws the FOV section of the control drawer. Exposes a
slider (`MinFovDegrees`..`MaxFovDegrees`), preset buttons (12°/18°/30°),
and a Zoom On toggle. Edits `CameraFovOverride.FovDegrees` /
`OverrideEnabled` directly. No own window — `ControlDrawer` calls its
`DrawContent()` inside the drawer layout.

The panel finds `CameraFovOverride` lazily via
`Camera.main.GetComponent<>()` and adds it if missing.

### 6.3 `client-unity/Assets/App/Runtime/AppBootstrap.cs`

**Role:** Three small responsibilities for this feature.

1. **Attach the override** at startup (`EnsureCameraFovOverride` in
   `Start`). Caches `_fovOverride` so it can feed the anchor each frame.
2. **Feed the fixture anchor** in `LateUpdate`:
   `_fovOverride.AnchorTransform = _activeObserverTransform`.
   `_activeObserverTransform` is the `AnimationRoot` under the live
   Vuforia observer.
3. **Host the drawer.** Creates `ControlDrawer` and binds the three
   section panels (status, FOV, eye calibration).

## 7. End-to-end per-frame path

1. Vuforia's tracking update writes the fixture pose into the
   `ObserverBehaviour` transform and writes its calibrated projection
   matrix into `Camera.projectionMatrix`.
2. `AppBootstrap.LateUpdate` sets
   `_fovOverride.AnchorTransform = _activeObserverTransform`.
3. URP's `RenderPipelineManager.beginCameraRendering` fires for the AR
   camera.
4. `CameraFovOverride.OnBeginCameraRendering`:
   - Reads `cam.projectionMatrix` (Vuforia's).
   - Computes `scale = currentTanHalf / targetTanHalf` from
     `m[1,1]` and the slider FOV.
   - Multiplies `m[0,0]` and `m[1,1]` by `scale`.
   - If `AnchorTransform` is in front of camera, nudges `m[0,2]` /
     `m[1,2]` by the fixture-anchor compensation.
   - Writes the modified matrix back.
5. URP renders the camera using the modulated projection. Because
   `m[1,1]` is still negative and only diagonal/principal-point
   elements changed, URP's render graph and Vuforia's
   video-background renderer see exactly the matrix structure they
   expect.
6. The waveguide displays the central angular slice (~28°) of the
   framebuffer. The fixture appears at its tracked position; the model
   and overlay appear magnified around it.

No `LateUpdate` writes to `Camera.projectionMatrix`. No
`GL.invertCulling`. No `Matrix4x4.Perspective` construction. Just
**read Vuforia's matrix, scale two diagonals, nudge two off-diagonals,
write back**.

## 8. Tradeoffs to know

### 8.1 Off-anchor content still flows toward edges

Fixture-anchor compensation only pins **one point** (the observer
origin). Geometry far from that anchor still spreads outward as you
zoom — that's correct projection behaviour and unavoidable in a true
FOV zoom. In practice it's invisible because the fixture's interesting
content sits near the anchor.

### 8.2 Camera-eye parallax is a *separate* problem

The FOV zoom is correct in **camera space**. It does not correct the
~6 cm lateral offset between the M4000's camera and the operator's eye —
that's what the eye-offset calibration is for. See
`docs/m4000-eye-calibration.md`.

### 8.3 No Vuzix waveguide calibration data

The mapping between camera FOV and what the operator actually sees
through the waveguide is approximated by eye, not by intrinsics. With
Vuzix-provided calibration we could plug it into
`VuforiaConfiguration.DeviceTrackerConfiguration.UseThirdPartySeethroughEyewear`
for proper optical-see-through registration. Until then, this slider
is the closest we get to true 1:1, at the **≈14° vertical** "true size" mark
(not 28° — that is the diagonal spec; see §1).

### 8.4 In the Unity Editor (PC webcam) the source FOV is degenerate

When you Play in the Editor, Vuforia uses the PC **webcam**, not the M4000
camera. The webcam can report a near-degenerate projection — observed
`vfov ≈ 1°` — and the diagnostic log then prints e.g.
`[CameraFovOverride] Vuforia native vfov=1.0°, target=18.0°, scale=0.06x`.
The override faithfully modulates whatever matrix is present, so in the Editor
the result can look wrong / off-screen.

**This is an editor-webcam artifact, not a device bug.** On the M4000 the source
matrix is the real ~37° projection and the override behaves correctly. Do **not**
add clamps or "sanity gates" that skip the override for extreme source FOVs — on
the device those could clamp out or disturb the real calibrated Vuzix projection.
Validate FOV behaviour **on glass**, not against the editor webcam.

## 8b. Alternative approach explored: scaling the content transform

Before the projection-matrix approach, we shipped a version that achieved the
zoom by **scaling the tracked content transform** (`AnimationRoot.localScale`)
instead of touching the camera. It is worth recording because it has different
trade-offs and is preserved on the `abdul-FOv-control` branch history.

- **How it worked:** a uniform `localScale = k` on the AR content, with
  `k = tan(refFov/2)/tan(targetFov/2)`. No projection write at all, so the
  Y-flip problem never arose.
- **Pro:** rock-solid registration — the model grows *around the fixture anchor*
  and stays locked to the part; no off-axis flow, no projection risk.
- **Con:** it is **not** a true FOV change. It makes objects bigger but does not
  reproduce the "projected" perspective feel (parallax amplification as the head
  moves), and it scales *only* content parented under the AR anchor.
- **Why we moved to projection:** on-glass testing judged the projection-matrix
  zoom to give more accurate, more natural AR placement for the assembly task.
  The two are geometrically equivalent for **angular size** but differ for
  off-axis parallax. Transform-scaling remains a valid fallback if a future
  Unity/URP/Vuforia change ever breaks the projection path.

## 9. Recovery if a future update breaks rendering

`OverrideEnabled` defaults `true` but is **not persisted**. Every
launch is a fresh ON. If a future Unity / URP / Vuforia update changes
the projection-matrix path in a way that breaks rendering:

- **During a session:** open the FOV tab in the drawer → toggle
  **Zoom On** ✗. `CameraFovOverride` calls
  `Camera.ResetProjectionMatrix()` and Vuforia takes back over within
  one frame.
- **Worst case** (no UI visible either): relaunch — `OverrideEnabled`
  resets to its default. If even that's broken, flip the field
  default to `false` in `CameraFovOverride.cs` and rebuild;
  PlayerPrefs are not involved.

## 10. File reference

- `client-unity/Assets/App/Runtime/CameraFovOverride.cs` — state holder +
  the projection-matrix write
- `client-unity/Assets/App/UI/FovTunerPanel.cs` — slider section of the
  control drawer
- `client-unity/Assets/App/UI/ControlDrawer.cs` — left-edge drawer that
  hosts the FOV section + eye-calibration + runtime status
- `client-unity/Assets/App/Runtime/AppBootstrap.cs` — attaches the
  override, feeds the anchor in `LateUpdate`
- `client-unity/Assets/App/UI/ImguiTheme.cs` — IMGUI sizing helper
