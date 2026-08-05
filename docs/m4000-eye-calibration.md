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

These are NOT the same point. The camera → eye displacement that the
calibration has to correct is built from **three physically distinct
offsets** — two fixed by the hardware, one that varies per person:

| Offset | Direction | Value | Type |
|---|---|---|---|
| Camera → waveguide, **forward** | Z (depth) | ~1 cm | fixed (device) |
| Camera → waveguide centre, **lateral** | X (side) | ~6 cm | fixed (device) |
| Waveguide → eye (**eye relief**) | Z (depth) | ~1.5–3.5 cm, **varies** | per-person |

Combined into the single camera → eye vector the calibration applies:

- **X (lateral) ≈ 6 cm** — the eye sits behind the waveguide centre, so the
  camera → eye lateral offset ≈ the camera → waveguide-centre lateral offset.
- **Z (forward) ≈ 2.5–4.5 cm** — the camera is ~1 cm ahead of the waveguide
  (fixed) **plus** the eye is ~1.5–3.5 cm behind it (the eye relief).
- **Y (vertical)** — small (the vertical camera-to-waveguide gap).

The **eye relief is the only per-person variable**: it grows when a worker
wears prescription glasses under the headset, or simply seats the glasses
further from the eye for comfort. So the two device offsets (6 cm lateral,
1 cm forward) are a fixed, measure-once default, and each worker only
fine-tunes the **Z** a little for their own eye relief.

So Vuforia draws "what the camera sees," and your eye looks at that drawing
from a different position. The two viewpoints disagree, and the hologram
lands offset from the real part — Vuforia renders for the wrong viewpoint,
because it does not know where the operator's eye is.

## 2. Why the offset is distance-dependent (parallax)

This is the key insight that determines what kind of correction is
needed. The mismatch between the camera viewpoint and the eye viewpoint
is **parallax** — and parallax depends on distance:

```
angular shift ≈ atan(camera_eye_offset / distance_to_fixture)
```

| Working distance | Angular shift from 6 cm lateral offset |
|---|---|
| 50 cm (hands-on assembly) | ~6.8° — very noticeable |
| 1 m | ~3.4° |
| 2 m | ~1.7° — small |
| 5 m | ~0.7° — almost invisible |

Two consequences:

1. A simple "shift the hologram 6° to the right" fix only works at
   one distance. Move closer or farther and it breaks.
2. The fix has to be **distance-aware**: more correction up close,
   less correction far away.

## 2b. The parallax model — the math

This is the eye-offset counterpart to the size math in
`docs/m4000-fov-tuning.md` §1b: a closed-form relationship between the physical
camera→eye offset, the viewing distance, and the on-glass misalignment — and why
**one fixed 3-D vector** corrects it at **every** distance. If you read one
section to understand the calibration, read this.

### 2b.1 Everything is an angle (again)

As in the FOV math, what the eye perceives is an **angular** offset, not a length.
A lateral gap `s` between hologram and part, seen at distance `D`, is the angle:
```
θ = atan(s / D)   ≈ s / D   (radians, small angle)
```
The model is about how the fixed camera→eye offset becomes this θ, and how to
cancel it.

### 2b.2 Two viewpoints, one offset

Work in **camera space**: camera at the origin, looking along +z (forward), +x
right, +y up. The eye is displaced from the camera by the **camera→eye vector**
```
a = (eₓ, e_y, −e_z)
```
- `eₓ` — lateral offset (camera → waveguide centre ≈ 6 cm)
- `e_y` — vertical offset (small)
- `e_z` — forward offset; the camera is *ahead* of the eye, so the eye sits at
  −e_z in z (`e_z ≈ 1 cm device + eye relief`)

Vuforia knows the part's pose relative to the **camera** and renders the hologram
as the camera would see it; the worker views from the **eye**. That disagreement
is the parallax. (Signs below assume this axis convention; on the real unit the
panel's +/− buttons resolve the actual direction empirically.)

### 2b.3 The parallax error

Take the part `P = (0, 0, D)` straight ahead at camera-distance `D`. The eye at
`a` sees it along `P − a = (−eₓ, −e_y, D + e_z)`, so its angle off the eye's
forward axis is:
```
θ_lateral  = atan( eₓ / (D + e_z) )   ≈ eₓ / D
θ_vertical = atan( e_y / (D + e_z) )  ≈ e_y / D
```
The camera renders `P` on-axis (angle 0), the display shows it at angle 0 to the
eye, but the eye sees the **real** part at θ ≠ 0 → the hologram lands θ off the
part. Since θ ∝ 1/D, the error is **parallax: big up close, small far away**
(§2 table). The `+e_z` means the forward offset slightly *reduces* the lateral
error (the eye is a touch farther from the part than the camera).

### 2b.4 The correction: moving the viewpoint ≡ moving the content

The identity that makes this tractable:

> Rendering a scene from viewpoint `V + δ` gives the same image as rendering from
> `V` after translating every object by `−δ`.

We are forced to render from the **camera** (`V`) but want the **eye**'s image
(`V + a`). So `δ = a`, and the fix is to translate the tracked content by `−a`.
Define the **correction vector**
```
c = −a = (−eₓ, −e_y, +e_z)
```
and shift the tracked content by `c` in camera space, rotated into the world by
the camera's orientation:
```
P_world = P_tracked + R_camera · c
```
`AppBootstrap.ApplyEyeOffset` does exactly this each frame, and the stored
calibration `(X, Y, Z)` **is** `c`:
```
X = −eₓ ≈ −6 cm      Y = −e_y (small)      Z = +e_z ≈ +4 cm
```
which is why a good lateral calibration comes out **negative** (X ≈ −5.5 cm) and
the forward seed is **positive** (Z = +0.04 m, §7).

### 2b.5 Why one fixed vector corrects every distance (the master result)

`c` is a **constant** — no `D` in it. The shifted content is
`P + c = (−eₓ, −e_y, D + e_z)`. Rendered from the camera through normal
perspective, its angle is
```
atan( eₓ / (D + e_z) )
```
— **identical to the eye's parallax** from §2b.3, at *every* `D`. The
**perspective divide by the (shifted) depth `D + e_z`** supplies the distance
dependence for free; the `+e_z` term is what makes the cancellation **exact**,
not just first-order. So:

> **The correction is distance-*independent* in code (one fixed 3-D vector) but
> distance-*correct* in result (perspective supplies the 1/D).**

This is the eye-offset analogue of the FOV master formula: there a fixed
projection *scale* gives constant magnification; here a fixed content
*translation* gives distance-correct parallax. A flat 2-D pixel shift, by
contrast, is only correct at one distance.

### 2b.6 Recovering the offset from what you see (the inverse)

Calibration is the inverse: observe the misalignment, solve for `c`.

**Lateral / vertical (X, Y) — one distance suffices.** Hologram and part are both
at distance `D`, so the visible gap *is* a length at that plane. To null angle θ
you shift by the gap itself:
```
|X| = eₓ = (D + e_z)·tan(θ_lateral) = (lateral gap measured at the part)
|Y| = e_y = (D + e_z)·tan(θ_vertical) = (vertical gap at the part)
```
Distance-independent to measure — the gap and the correction both scale with
distance, so they cancel. Dial X/Y until the hologram sits on the part.

**Forward (Z) — needs two distances.** Head-on, `e_z` produces no lateral shift;
it only changes how the lateral error scales with distance. The parallax is a
**line in `D`**:
```
1/θ(D) = (D + e_z)/eₓ = (1/eₓ)·D + e_z/eₓ
```
Measure θ at two distances `(D₁, θ₁)`, `(D₂, θ₂)`:
```
eₓ  = (D₁ − D₂) · θ₁ θ₂ / (θ₂ − θ₁)
e_z = eₓ / θ₁ − D₁
```
That is the mathematical content of the two-step procedure (§8): step 1 fixes
X/Y up close, step 2 fixes Z by making the alignment *hold* at the far distance.

### 2b.7 Why depth is ambiguous at one distance

At a single distance the only observable is the ratio `eₓ / (D + e_z)` — **one
equation, two unknowns** (`eₓ`, `e_z`). Infinitely many `(eₓ, e_z)` pairs project
identically head-on, so `e_z` cannot be pinned from one view. A second distance
adds the equation that separates them. This is a genuine rank-deficiency of the
single-distance measurement, not a UX choice.

### 2b.8 The three-offset decomposition

`a` (equivalently `c`) splits into a **fixed device part** and a **per-person
part**:
```
a = a_device + a_person
a_device = ( eₓ≈6,  e_y≈0.5,  e_z,device≈1 ) cm       (fixed hardware)
a_person = ( 0,     0,        eye_relief≈1.5–3.5 ) cm  (per worker, in Z)
```
Only the **eye relief** (a Z term) varies between workers, so X and Y are
measure-once device constants and the per-worker fine-tune is essentially a **Z**
nudge (§1).

### 2b.9 Composition with the FOV zoom (orthogonality)

The two corrections act on **different objects** and commute:
- **FOV zoom** scales the projection intrinsics (`m₀₀, m₁₁`) — it changes *angles*.
- **Eye offset** translates the content (`P + R·c`) — it changes *position*.

A point rendered as `scale · K · (P + R·c)` receives the magnification (on `K`)
and the parallax shift (on the point) **independently** — neither disturbs the
other. This is precisely why **Size = FOV (a scale)** and **Position = eye-offset
(a translation)** are tuned separately and must not be crossed.

### 2b.10 Worked example

`a = (6, 0.5, 4) cm` → `c = (−6, −0.5, +4) cm`, part straight ahead, so
`D + e_z` = 49 cm and 94 cm.

| Quantity | at D = 45 cm | at D = 90 cm |
|---|---|---|
| Eye parallax `θ = atan(eₓ/(D+e_z))` | `atan(6/49)` = **7.0°** | `atan(6/94)` = **3.7°** |
| Physical gap at the part `(D+e_z)·tanθ` | 6.0 cm | 6.0 cm |
| Correction render angle `atan(eₓ/(D+e_z))` | −7.0° | −3.7° |
| **Residual after correction** | **0** | **0** |

One fixed `c = (−6, −0.5, +4)` cm cancels the parallax at **both** distances —
the point of §2b.5. Recovering it from the two angles (radians θ₁ = 0.1218,
θ₂ = 0.0637): `eₓ = (45−90)(0.1218·0.0637)/(0.0637−0.1218) ≈ 6.0`,
`e_z = 6/0.1218 − 45 ≈ 4.0`. ✓

### 2b.11 What the model does NOT correct

The correction is a pure parallax model (a fixed 3-D translation). It does **not**
absorb **distance-independent** errors, and forcing it to will make things drift
with distance:
- a **fixed display boresight** (camera optical axis ≠ display axis) — a constant
  angular offset; a 3-D shift over/under-corrects it as `D` changes;
- a **GLB origin** not at the part centre — a constant world offset;
- Vuforia tracking error and waveguide edge distortion (§10).

Rule of thumb: if a residual is the **same angle at all distances**, it is one of
these — not parallax — and chasing it with X/Y/Z will only break other distances.

### 2b.12 The 4×4 matrix view (and why it is *not* a projection edit)

FOV tuning works by editing Vuforia's **projection matrix** `P`
(`docs/m4000-fov-tuning.md` §3). The eye offset is a different operation entirely:
it **never touches the projection matrix** — it is a rigid **translation** in the
content transform.

**Where it sits in the pipeline.** Every vertex goes through
```
vertex_clip = P · V · M · vertex_local
```
- `P` — projection (camera → clip). Vuforia writes it; the FOV tuner edits it.
- `V` — view (world → camera). Vuforia writes it from the tracked pose.
- `M` — model (object → world). Carries the tracked content's pose.

The eye correction inserts a world-space translation `d = R_camera · c` on the
tracked content:
```
vertex_clip = P · V · T(d) · M · vertex_local
```
where `T(d)` is the pure-translation 4×4
```
        | 1  0  0  dₓ  |
T(d)  = | 0  1  0  d_y |
        | 0  0  1  d_z |
        | 0  0  0  1   |
```
The whole offset lives in the **translation column (4th column): `m₀₃, m₁₃,
m₂₃`**. In code it is not even a separate multiply — `AppBootstrap.ApplyEyeOffset`
sets `AnimationRoot.localPosition = c` (rotated into the parent frame), so `M`'s
own translation part carries it.

**Which matrix, which elements — vs FOV.**

| Correction | Matrix | Elements written |
|---|---|---|
| FOV zoom (size) | Projection `P` | `m₀₀, m₁₁` (FOV) + `m₀₂, m₁₂` (principal point) |
| Eye offset (position) | Model `M` (or View `V`) | translation column `m₀₃, m₁₃, m₂₃` |

Disjoint matrices, disjoint elements → this is the matrix-level reason the two
corrections **compose without interference** (§2b.9). FOV scales the intrinsics;
the eye offset shifts the point; they never write the same number.

**Why it must be a translation, not a projection principal-point shift.** It is
tempting to correct the offset by nudging the projection principal point
(`m₀₂/m₁₂`), the way the FOV anchor does. That is **wrong for parallax**:

- A **principal-point shift** displaces the image by a constant amount in NDC —
  **the same at every depth `z`**. It is a **distance-independent** 2-D shift.
- A **3-D translation** (`T(d)` in `M`/`V`) shifts a vertex at depth `z`, and the
  **perspective divide (÷z)** turns it into an on-screen shift of `≈ d/z` —
  **distance-dependent**.

Parallax is distance-dependent (§2b.3), so it must be the 3-D translation. This is
the matrix-level statement of the master result (§2b.5): the `1/D` comes from
perspective dividing a fixed 3-D translation, not from any edit to `P`.

**Corollary — where a fixed boresight belongs.** The flip side is useful. A fixed
display **boresight** (camera optical axis ≠ display axis) is a *constant angular*
offset — distance-independent — which is **exactly** what a projection
principal-point shift produces. So the two errors have two different homes:

| Error | Nature | Correct home in the matrices |
|---|---|---|
| **Parallax** (camera ↔ eye) | distance-dependent | 3-D translation in `M`/`V` (`m₀₃, m₁₃, m₂₃`) |
| **Boresight / display misalignment** | distance-independent (constant angle) | projection principal point `m₀₂, m₁₂` |

Putting a constant boresight into the parallax translation (or vice-versa) is what
produces the distance-drift warned about in §2b.11: each is then only correct at
the distance you tuned it. Kept in their proper matrices they decouple — parallax
in the transform, boresight in the projection — and both hold across distance.

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
roughly 4 cm (≈1 cm camera→waveguide, fixed by the device, plus ~3 cm of
nominal eye relief; see the three-offset breakdown in §1). So we **seed Z
to +0.04 m** in `EyeOffsetCalibration` (see `DefaultForwardMeters`). This
means:

- The depth component is approximately correct **before any worker
  calibration runs**.
- Even an operator who only does the first (lateral) step gets a much
  better experience than starting from zero Z.
- The fine-tune during the calibration is small adjustments around an
  already-good baseline, not a search across the full range.
- That small per-worker fine-tune is essentially **their eye relief** —
  the one per-person offset: larger if they wear prescription glasses
  under the headset, smaller if the glasses sit close to the eye.

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
  optics are not perfectly flat across the full ~28° **diagonal** display FOV
  (≈14° vertical); alignment may degrade slightly when the hologram sits at the
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
