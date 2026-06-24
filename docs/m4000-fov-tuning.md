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
| **Camera FOV** | ~37° vertical | The vertical angle the M4000's tracking camera sees of the world. Vuforia derives its projection matrix from this. |
| **Waveguide / display FOV** | ~28° vertical | The angular slice of the framebuffer that the optics actually project to the eye. |

These don't match. The consequence:

- Vuforia renders the virtual world at **37°** into the framebuffer.
- The waveguide then squeezes that whole framebuffer into the eye's
  **28°** window.
- 37° of rendered content compressed into 28° of eye-angle ⇒ virtual
  content appears **smaller than the real fixture**. The "everything
  looks far away" experience.

The FOV tuner narrows the render-side FOV so virtual content fills more
of the waveguide, with an on-glass slider for live tuning. The
"true size" line — where the hologram matches the real part 1:1 — is
exactly the **display FOV**: render at 28° and the hologram subtends
the same eye angle as the real part.

| Render FOV (slider) | Effect through the 28° display |
|---|---|
| **37°** (slider max, = native) | 37° squeezed into 28° → smaller than real (Vuforia default) |
| **28°** | 28° shown in 28° → **angular 1:1, true size** |
| **< 28°** | narrow world shown in 28° → magnified / scope zoom |

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
genuine zoom-in. Approximate effective zoom:

| Slider | Scale | Effect |
|---|---|---|
| **37°** | ~1.0× | Matches Vuforia native — slider's "no zoom" position |
| 30° | ~1.25× | Mild zoom (preset) |
| **28°** | ~1.32× | **Angular 1:1 with the waveguide — true size** |
| 24° | ~1.57× | Moderate |
| **18°** (default) | ~2.1× | Sensible baseline; out-of-box ON setting (preset) |
| 14° | ~2.7× | Strong scope |
| **12°** | ~3.2× | Heavy zoom (preset) |
| 8° | ~4.8× | Maximum magnification |

Constants live in `CameraFovOverride.cs`: `MinFovDegrees = 8f`,
`MaxFovDegrees = 37f`, `DefaultFovDegrees = 18f`.

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
~5.5 cm offset between the M4000's camera and the operator's eye —
that's what the eye-offset calibration is for. See
`docs/m4000-eye-calibration.md`.

### 8.3 No Vuzix waveguide calibration data

The mapping between camera FOV and what the operator actually sees
through the waveguide is approximated by eye, not by intrinsics. With
Vuzix-provided calibration we could plug it into
`VuforiaConfiguration.DeviceTrackerConfiguration.UseThirdPartySeethroughEyewear`
for proper optical-see-through registration. Until then, this slider
is the closest we get to true 1:1 at the 28° "true size" mark.

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
