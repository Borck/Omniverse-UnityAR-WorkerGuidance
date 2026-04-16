# Quick-Start Cheat Sheet

**AR Assembly Guidance System — 15 Steps from Zero to Running**

---

### One-time setup (do once)

1. **Install .NET 8**: Download from [microsoft.com/dotnet](https://dotnet.microsoft.com/download).

2. **Prepare your files** for each assembly step:
   - One `.glb` 3D model file per step
   - One Vuforia marker file (`.zip` or `.dat`) for the fixture

---

### Every session

3. **Start the server**:
   - Windows: double-click `test-server\start-server.bat`
   - Mac/Linux: `bash test-server/start-server.sh`

4. **Wait** until you see `Now listening on: http://0.0.0.0:5000`.

5. **Find the server's IP address**:
   - Windows: open Command Prompt → `ipconfig` → note "IPv4 Address"
   - Mac/Linux: `ip addr` or `ifconfig`

6. **Open the admin panel** in a web browser:
   ```
   http://<server-ip>:5000
   ```

7. Click **Submit New Job**.

8. Enter the **Job ID** (e.g. `engine-bracket-v1`) and **Workflow Version** (e.g. `v1.0`).

9. Fill in **Step 1**: Step ID, Part ID, Display Name, Instructions, Asset Version,
   Target ID, Target Version.

10. Upload the **GLB model file** for Step 1.

11. Upload the **Vuforia marker file** for Step 1.

12. Click **+ Add Step** for each additional step and repeat fields 9–11.

13. Click **Submit & Notify Unity** — the server sends Step 1 to all connected AR devices.

14. **On the AR device**: the app automatically downloads Step 1 and shows the 3D overlay.

15. **Worker** follows the overlay, installs the part, presses **Confirm** →
    the server sends Step 2 → repeat until all steps are done.

---

### Troubleshooting quick-reference

| Symptom | Fix |
|---------|-----|
| Device shows "Disconnected" | Same Wi-Fi? Correct server IP in app settings? |
| 3D model not showing | Wrong GLB file or Asset Version — re-submit job |
| Tracking not working | Point device at fixture; upload correct marker file |
| Web page unreachable | Server running? Firewall blocking port 5000? |

---

> For detailed instructions see **[`docs/operator-guide.md`](./operator-guide.md)**  
> For technical documentation see **[`docs/architecture.md`](./architecture.md)**
