# Operator Guide — AR Assembly Guidance System

> **Who is this guide for?**  
> This guide is written for production operators, team leads, and anyone who needs to
> set up and run the AR assembly guidance system — no programming knowledge required.

---

## 1. What Does This System Do?

The AR Assembly Guidance System shows assembly workers step-by-step instructions
directly in their AR glasses or on a tablet camera view.

When a worker looks at the assembly fixture, the glasses overlay a 3D model showing
exactly which part goes where and how to install it. Each step is confirmed by the
worker before the system advances to the next one.

**Key point: Nothing is stored on the glasses or tablet ahead of time.**  
All 3D models, instructions, and markers are sent from the server at the moment the
worker starts. This means you can update instructions or add new assembly jobs without
reinstalling the app on the device.

---

## 2. Before You Start

### What You Need

| Item | Details |
|------|---------|
| A PC or laptop running the server | Windows, macOS, or Linux. Needs to be on the same network as the AR device. |
| .NET 8 Runtime | Free download from [microsoft.com/dotnet](https://dotnet.microsoft.com/download) |
| The AR device | HoloLens, phone, or tablet with the guidance app installed |
| A Wi-Fi network (or cable) | Both the PC and the AR device must be connected to the same network |
| Your 3D model files | One `.glb` file per assembly step |
| Your Vuforia marker files | One `.zip` or `.dat` file for the fixture marker |

### Network Check

- Find the IP address of your PC:
  - **Windows**: Open Command Prompt → type `ipconfig` → look for "IPv4 Address"
  - **Mac/Linux**: Open Terminal → type `ip addr` or `ifconfig`
- Make sure the AR device can reach the PC. Try pinging the PC's IP from a phone on
  the same Wi-Fi.

---

## 3. Starting the Server

### Windows (double-click)

1. Open the `test-server` folder on the PC.
2. Double-click **`start-server.bat`**.
3. A black terminal window opens. Wait until you see:

   ```
   Now listening on: http://0.0.0.0:5000
   ```

4. The server is running. **Do not close the terminal window** — closing it stops the server.

### macOS / Linux (terminal)

1. Open a terminal.
2. Navigate to the `test-server` folder:
   ```bash
   cd /path/to/Omniverse-UnityAR-WorkerGuidance/test-server
   bash start-server.sh
   ```
3. Wait for:
   ```
   Now listening on: http://0.0.0.0:5000
   ```

---

## 4. Opening the Web Administration Panel

1. On any computer on the same network, open a web browser (Chrome, Edge, Firefox).
2. Type the following in the address bar and press **Enter**:

   ```
   http://<server-ip>:5000
   ```

   Replace `<server-ip>` with the IP address of the PC running the server.  
   Example: `http://192.168.1.42:5000`

3. You will see the **Guidance Admin** home page, which shows a list of submitted jobs.

> **Tip:** If you are opening the browser on the same PC as the server, you can use:
> `http://localhost:5000`

---

## 5. Creating an Assembly Job

An **Assembly Job** is a set of steps that guide a worker through one assembly task.

### Step-by-Step

1. Click **Submit New Job** (or the **Submit Job** button in the top-right corner).

2. Fill in the **Job Details**:

   | Field | What to enter |
   |-------|--------------|
   | **Job ID** | A unique name for this job, e.g. `engine-bracket-v1`. Use only letters, numbers, and hyphens. |
   | **Workflow Version** | A version label, e.g. `v1.0`. |

3. Fill in the **first step** in the "Step 1" section:

   | Field | What to enter |
   |-------|--------------|
   | **Step ID** | A unique ID for this step, e.g. `step-01` |
   | **Part ID** | The part identifier, e.g. `BracketA` |
   | **Display Name** | What the worker sees as the step title, e.g. `Install Bracket A` |
   | **Instructions** | Short instruction text shown in the AR display |
   | **Safety Notes** | Any warnings (one per line), e.g. `Wear gloves` |
   | **Asset Version** | A version key for the 3D model, e.g. `v1` |
   | **Target ID** | The name of the AR marker, e.g. `fixture-01` |
   | **Target Version** | A version key for the marker file, e.g. `v1` |
   | **Anchor Type** | Usually `ModelTarget` for Vuforia |
   | **Animation Name** | Name of the animation inside the GLB file (optional) |
   | **GLB Model File** | Click to upload the `.glb` 3D model for this step |
   | **Vuforia Target File** | Click to upload the `.zip` or `.dat` marker file |

4. To add more steps, click **+ Add Step** and fill in the next step's details.

5. Click **Submit & Notify Unity**.

   The server will:
   - Save your files
   - Generate the job manifest
   - Send Step 1 to any connected AR devices automatically

---

## 6. What Happens on the AR Device

After you click **Submit & Notify Unity**:

1. The AR device receives a message from the server.
2. The app **downloads** the 3D model and the marker file for Step 1 from the server.
   (This takes a few seconds on the first load; subsequent loads use the cached file.)
3. The step instructions appear in the AR display.
4. The worker points the device at the assembly fixture — when the camera recognises the
   marker, the 3D guidance overlay appears, showing where to install the part.

**No action is required on the AR device to receive a new job.**

---

## 7. Progressing Through Steps

1. The worker follows the 3D guidance overlay and installs the part.
2. When finished, the worker presses the **Confirm** button (or the gesture/button
   configured on the device).
3. The app tells the server the step is complete.
4. The server automatically sends Step 2, which the app downloads and displays.
5. This continues until all steps are done.

If the server connection drops during a step, the app **freezes on the current step**
and automatically reconnects. Once reconnected, it sends the pending completion and
continues.

---

## 8. Troubleshooting

| Problem | Likely Cause | Fix |
|---------|-------------|-----|
| "Disconnected" shown on device | Device cannot reach the server | Check that both devices are on the same Wi-Fi. Verify the server is running and the IP address is correct in the app settings. |
| Server terminal says "Address already in use" | Another program is using port 5000 | Close other programs, or change the port in `Program.cs` and restart. |
| 3D model does not appear | GLB file was not uploaded, or wrong Asset Version | Re-submit the job with the correct GLB file and matching Asset Version. |
| Tracking does not work | Wrong marker file, or device not pointed at fixture | Upload the correct Vuforia `.dat`/`.zip` file. Ensure the worker points the device directly at the fixture. |
| Web page says "404 Not Found" | Job ID was mistyped, or server restarted | The server's job store resets on restart (files remain). Re-submit the job via the web form. |
| App stuck on "Searching for target" | Tracking confidence is low | Ensure the fixture is well lit and clearly visible. Clean the camera lens. |

---

## 9. Glossary

| Term | Plain-Language Definition |
|------|--------------------------|
| **Assembly Job** | A named set of steps that guide a worker through one complete assembly task. |
| **Step** | A single instruction within a job — e.g. "Install Bracket A". |
| **GLB** | A file format for 3D models (`.glb`). The system downloads this to show the 3D guidance overlay. |
| **Vuforia Model Target** | A camera-recognition "marker" that tells the AR app exactly where the physical fixture is in space. |
| **gRPC** | The internal communication protocol between the server and the AR device. You do not need to interact with this directly. |
| **Assembly Sequence** | The ordered list of steps in an Assembly Job. |
| **Manifest** | A JSON file generated by the server that lists all the files needed for a job. Created automatically when you submit a job. |
| **Asset Version** | A label (e.g. `v1`) that identifies a specific version of a 3D model or marker. If the version does not change, the device uses its cached copy without downloading again. |
| **persistentDataPath** | The folder on the AR device where downloaded models and markers are stored. |
