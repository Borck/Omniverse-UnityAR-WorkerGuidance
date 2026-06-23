# Vuzix M4000 FOV Tuning

How the on-glass scope zoom works in the Unity client: what the underlying math
is, why naive projection-matrix overrides failed on this exact stack, and the
matrix-scaling pattern that ended up giving correct AR registration.

## 1. The problem we set out to solve

The Vuzix M4000 is an optical see-through monocular waveguide. Its display has
roughly a **28° angular field of view** at a fixed focal distance of ~2 m. By
contrast, the device's front camera — which Vuforia uses for tracking and from
which it derives the projection it writes onto the Unity camera every frame —
has an angular FOV of roughly **50–60°** depending on resolution mode.

These two FOVs don't match. The consequence:

- Vuforia computes a perspective matrix that's correct for **the camera's view**
  of the world.
- That matrix is what Unity uses to project tracked virtual geometry into the
  framebuffer.
- The waveguide only physically displays a 28° angular slice of that framebuffer.
- A part that "looks correct size" in the camera's wider 50–60° view ends up
  taking up a much smaller angular slice of what the operator actually sees
  through the waveguide.

End-user symptom on glass: *"holograms look far away."* They are angularly
correct relative to the camera, but visually small relative to the waveguide.

The FOV tuner narrows the camera's effective FOV so virtual content fills more
of the waveguide, with a slider for on-glass tuning of the magnification.

## 2. Two FOVs that aren't the same thing

| Layer | What it is | Who controls it |
|---|---|---|
| **Waveguide hardware FOV (~28°)** | The angular slice of the framebuffer the optics actually displays | Fixed by physical optics; no software control |
| **Camera projection FOV** | The vertical angle of the perspective frustum in the projection matrix | Normally `Camera.fieldOfView` — but **Vuforia overrides this every frame** with a matrix derived from the device camera intrinsics |
| **Apparent angular size of a rendered object** | What the operator sees through the waveguide | A function of both the projection FOV and the physical FOV |

Setting `Camera.fieldOfView` in the Inspector has no visible effect at runtime
because Vuforia, in its per-frame update, writes a `Camera.projectionMatrix`
that supersedes the Inspector value. The Inspector FOV slider is effectively
cosmetic when Vuforia is active.

## 3. What didn't work, and why

The textbook way to override Vuforia's projection is to write to
`Camera.projectionMatrix` (or `Camera.fieldOfView` + `ResetProjectionMatrix`)
*later* in the frame than Vuforia does — typically `LateUpdate` or, more
reliably, `RenderPipelineManager.beginCameraRendering` (URP's pre-render
callback).

We tried **three variants** of this approach before finding what works:

1. **Replace with a fresh `Matrix4x4.Perspective(fov, aspect, near, far)`** in
   `LateUpdate`. → World rendered Y-flipped.
2. **`ResetProjectionMatrix()` + set `Camera.fieldOfView`** inside
   `beginCameraRendering` so Unity's auto-projection path took over. → Still
   Y-flipped.
3. **Negate `m[1,1]` on the fresh matrix + `GL.invertCulling = true`** to
   compensate the flip. → No flip, but the model shifted to the right and
   zoomed wrong at the "no-zoom" slider value, because the fresh idealized
   matrix had thrown away Vuforia's principal-point offset and its actual FOV
   calibration.

### Root cause of the Y-flip

URP 17.x with **RenderGraph enabled** (the default in Unity 6) applies a
platform-dependent Y-flip when rendering to an intermediate texture (the
default render path on Android Direct3D/Vulkan). Vuforia's projection matrix
has a **negative `m[1,1]`** that bakes this flip in; URP renders correctly when
that sign convention is preserved.

The naive replacements built with `Matrix4x4.Perspective` produce a
positive-`m[1,1]` matrix in OpenGL convention. URP applies its expected flip on
top of *that* → net Y-flip in the rendered image. Negating `m[1,1]` ourselves
fixed the flip but threw away two pieces of Vuforia's calibration we shouldn't
discard:

- The **principal point offset** (`m[0,2]`, `m[1,2]`) — the M4000's camera
  optical centre isn't exactly at image centre; Vuforia compensates with these
  offsets. Without them, the model rendered shifted toward one side.
- The **actual vertical FOV** — our `Matrix4x4.Perspective(60, ...)` assumed
  Vuforia uses ~60° native FOV, but it's actually ~50°. Setting our matrix to
  60° at slider "no zoom" actually *zoomed out* relative to Vuforia's view.

### The fix: don't replace, just modulate

Instead of building a fresh matrix, **read Vuforia's matrix and scale only the
two FOV-controlling diagonal elements**:

```csharp
var p = cam.projectionMatrix;
float currentTanHalf = 1f / Mathf.Abs(p.m11);
float targetTanHalf  = Mathf.Tan(fovDegrees * 0.5f * Mathf.Deg2Rad);
float scale = currentTanHalf / targetTanHalf;
p.m00 *= scale;
p.m11 *= scale;
cam.projectionMatrix = p;
```

`m[1,1]` stays negative (Vuforia's sign × positive scale = still negative), so
URP RenderGraph treats our override identically to Vuforia's own writes — no
flip path is exercised, `GL.invertCulling` is not needed. The principal point
offset, near/far precision, and every other matrix element are preserved
unchanged. The result on glass: real FOV-based zoom with correct AR
registration of the model on the physical fixture.

## 4. The slider-to-projection mapping

The slider exposes the target vertical FOV in degrees. The mapping to actual
projection-matrix scale uses the half-angle tangent ratio (a property of how
perspective projection magnification relates to FOV):

```text
scale = tan(detectedVuforiaVfov / 2) / tan(targetFovDegrees / 2)
```

`detectedVuforiaVfov` is recovered from the matrix Vuforia just wrote
(`vfov = 2 * atan(1 / abs(m[1,1]))`). It's not a constant — it depends on
device/resolution mode — so we don't guess.

### M4000 calibration: measured vfov is ~37°

On the M4000 specifically, on-device diagnostics show Vuforia's native vertical
FOV is **~36.8°** (logged at runtime by `CameraFovOverride` once per session).
That is much narrower than typical AR cameras (which are usually 50–70° vfov),
and it materially affects what the slider does. Setting the slider above 37°
zooms *out* below Vuforia native, which is essentially never what we want; so
the slider range is **clamped to [8°, 36°]** in `MaxFovDegrees`. Every position
on the slider produces a genuine zoom-in.

Slider values produce these scales on M4000:

| Slider value | Effective zoom | Perceived effect |
|---|---|---|
| 36° | ~1.0× | Matches Vuforia native — slider's "no zoom" point |
| 30° | ~1.25× | Mild zoom |
| 24° | ~1.57× | Moderate |
| **18°** (default) | ~2.1× | Sensible baseline; out-of-box ON setting |
| 14° | ~2.7× | Strong scope |
| 12° | ~3.2× | Heavy zoom (preset button) |
| 8° | ~4.8× | Maximum magnification |

The `12° / 18° / 30°` preset buttons in the slider band are calibrated for
M4000: heavy zoom, the default, and a mild near-baseline. If we deploy to a
device with a different camera vfov, both the default and the presets would
need re-calibration.

## 5. Code modules

Three files. The state owner, the UI, and the bootstrap-time attachment are
intentionally split so each has one job.

### 5.1 `client-unity/Assets/App/Runtime/CameraFovOverride.cs`

**Role:** Owns the slider state (`OverrideEnabled` and `FovDegrees`), persists
the FOV value via `PlayerPrefs`, and does the per-frame projection-matrix write
that actually performs the zoom.

Attached to `Camera.main` at runtime by `AppBootstrap`. Subscribes to
`RenderPipelineManager.beginCameraRendering` in `OnEnable` and writes the
modulated matrix when `OverrideEnabled` is true. On disable (and when
`OverrideEnabled` flips off), calls `Camera.ResetProjectionMatrix()` to hand
the camera back to Vuforia/URP cleanly.

Notable design choices:

- **`OverrideEnabled` defaults `true`.** The projection FOV is validated as
  correct on glass; new operators get the right experience out of the box. The
  flag is also intentionally **not persisted** — every launch starts ON
  regardless of any prior session, so a future regression that breaks rendering
  can't survive a restart (uninstall is never required for recovery).
- **`FovDegrees` is persisted.** The operator's preferred zoom level survives
  across sessions.
- **No `LateUpdate`, no transform writes, no `GL.invertCulling`.** The single
  responsibility is the projection-matrix write in `OnBeginCameraRendering`.

### 5.2 `client-unity/Assets/App/UI/FovTunerPanel.cs`

**Role:** IMGUI band at the top of the view. Hidden by default; toggled on/off
via the "Tune FOV" checkbox in the top-right HUD on `AppBootstrap`. When
visible, renders:

- A live value label (`FOV: 28.0°`)
- A `GUILayout.HorizontalSlider` spanning the available width
- Three preset buttons (`8°`, `28°`, `60°`) for one-tap snapping
- An `On` checkbox that toggles `CameraFovOverride.OverrideEnabled`

The panel finds `CameraFovOverride` lazily via `Camera.main.GetComponent<>()`
and adds it if missing. It only reads/writes the component's public properties.

Sizing uses `ImguiTheme.cs` — see the M4000 UI sizing doc for the rationale on
why the runtime UI is IMGUI rather than Canvas.

### 5.3 `client-unity/Assets/App/Runtime/AppBootstrap.cs`

**Role:** Two small responsibilities for this feature.

1. **Attach the override component at startup.** In `Start()`,
   `EnsureCameraFovOverride()` finds `Camera.main` and ensures a
   `CameraFovOverride` instance is on it. From that point on, the component
   handles itself; `AppBootstrap` doesn't need to keep a reference.
2. **Toggle the tuner panel's visibility.** The "Tune FOV" checkbox in the
   `OnGUI`-rendered HUD top-right flips `fovTunerPanel.Visible` so the slider
   band is hidden during normal worker operation and only appears when the
   operator wants to adjust.

## 6. End-to-end per-frame path

1. Vuforia's tracking update writes the model target pose into the
   `ObserverBehaviour` transform and writes its calibrated projection matrix
   into `Camera.projectionMatrix`.
2. URP's `RenderPipelineManager.beginCameraRendering` fires for the AR camera.
3. `CameraFovOverride.OnBeginCameraRendering` reads the projection matrix
   (Vuforia's), scales `m[0,0]` and `m[1,1]` by `currentTanHalf / targetTanHalf`,
   and writes it back.
4. URP renders the camera with the modulated projection. Because `m[1,1]` is
   still negative and every other element is unchanged, URP's render-graph and
   Vuforia's video-background renderer see exactly the matrix structure they
   expect.
5. The waveguide displays the central angular slice. The model appears
   magnified by the FOV ratio, anchored correctly to the physical fixture.

## 7. Tradeoffs to know

This approach is robust but is real FOV-based zoom, which has one geometric
consequence worth understanding.

### Off-axis content flows toward the edges as you zoom in

Narrowing the FOV is how a real camera lens zooms: anything off the optical
axis flows outward toward the screen edges as you zoom in. The fixture is
essentially never perfectly on the camera's optical axis (the operator isn't
pointing the camera *exactly* at the fixture's tracked centroid), so as you
slide from 60° → 8°, the fixture's screen position drifts slightly in whatever
direction it was already off-axis.

This is geometrically correct and matches how a physical zoom lens behaves. It
is *not* what a transform-scaling approach would do (which scales around the
object's own anchor and keeps the anchor stationary on screen), and an earlier
iteration of this code used that approach as a workaround for the URP
RenderGraph flip. The projection-path was chosen as the production answer
because it produces **correct AR registration for placement of animated parts
on the fixture** — the assembly workflow needs the model to occupy the right
physical space, not just visually fill the waveguide. The slight off-axis flow
during head movement is the price of correct registration.

### What we don't have

- No Vuzix waveguide calibration data. The mapping between camera FOV and what
  the operator actually sees through the waveguide is approximate; we tune by
  eye, not by intrinsics. With Vuzix-provided calibration, we could plug it
  into `VuforiaConfiguration.DeviceTrackerConfiguration.UseThirdPartySeethroughEyewear`
  for proper optical-see-through registration. Until then, this slider is the
  best we have.

## 8. Recovery if the override ever breaks rendering

The `OverrideEnabled` flag is non-persistent and defaults true, but every
launch is a fresh ON. If a future Unity / URP / Vuforia update changes the
projection-matrix path in a way that breaks rendering:

- During a session: open Tune FOV in the HUD, toggle **On** ✗. `CameraFovOverride`
  calls `Camera.ResetProjectionMatrix()` and Vuforia takes back over within one
  frame.
- Worst case (no IMGUI visible either): just relaunch — `OverrideEnabled` resets
  to its default `true`. If even that's broken, set the field's default to
  `false` in `CameraFovOverride.cs` and rebuild; PlayerPrefs aren't involved.

## 9. File reference

- `client-unity/Assets/App/Runtime/CameraFovOverride.cs` — state holder + the
  one `OnBeginCameraRendering` write
- `client-unity/Assets/App/UI/FovTunerPanel.cs` — IMGUI slider band
- `client-unity/Assets/App/Runtime/AppBootstrap.cs` — `EnsureCameraFovOverride`
  attachment, `Tune FOV` HUD toggle
- `client-unity/Assets/App/UI/ImguiTheme.cs` — IMGUI sizing helper used by the
  panel
