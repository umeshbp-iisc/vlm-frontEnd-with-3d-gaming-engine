# ICU Assist: Step-by-Step Execution Guide

## 1. Understand what needs to run

Start the components in this order:

1. **Ollama**, serving the `qwen2.5vl:3b` vision model on port **11434**.
2. **Python gateway**, serving the application API on port **8765**.
3. **Unity application**, either the standalone Windows build or the Unity Editor.

The application sends a robot-camera image, your instruction, and world state
to the gateway. The model proposes actions, the gateway validates them, and
Unity executes them only after you confirm.

Visual Studio 2026 is for editing/debugging C#; it does not need to be open.
VS Code is optional. All commands below use Windows PowerShell.

This is a simulation only. It does not control real medical equipment.

## 2. Open PowerShell in the project directory

Run this in each new terminal where you will run project commands:

```powershell
Set-Location "C:\Users\10314026\OneDrive - BD\IIS Mtech\Fourth Semester\Intelligence User Interface\MidSemProj\vlm3Dgaming"
```

The instructions assume the existing dependencies and Python virtual
environment are installed. Check the environment:

```powershell
Test-Path ".\vlm-gateway\.venv\Scripts\python.exe"
ollama --version
ollama list
```

`Test-Path` should print `True`, and the model list should contain
`qwen2.5vl:3b`.

Only if the model is missing, download it:

```powershell
ollama pull qwen2.5vl:3b
```

If the Python environment is missing, follow the setup instructions in the
[gateway README](vlm-gateway/README.md) before proceeding.

## 3. Check or start Ollama

Check whether Ollama is responding:

```powershell
Invoke-RestMethod "http://127.0.0.1:11434/api/tags"
```

If it responds, leave the existing Ollama service running. You do not need to
run `ollama run` separately.

If the connection fails, start Ollama in a separate terminal:

```powershell
ollama serve
```

Keep that terminal open. If the command reports that the address is already
in use, check the endpoint again instead of starting another instance.

## 4. Start the Python gateway

In a terminal at the project root:

```powershell
$env:VLM_PROVIDER = "ollama"
$env:VLM_MODEL = "qwen2.5vl:3b"
.\scripts\start-vlm.ps1
```

Expected output includes:

```text
Uvicorn running on http://127.0.0.1:8765
```

**Keep this terminal open.** It is the server and will not return to the
prompt until stopped. The script uses the project's virtual environment;
manual activation is not needed.

### If PowerShell blocks the script

Instead of changing execution policies, use the direct command below from
the project root. Do not start it if the script already started the gateway.

```powershell
$env:VLM_PROVIDER = "ollama"
$env:VLM_MODEL = "qwen2.5vl:3b"
Set-Location ".\vlm-gateway"
.\.venv\Scripts\python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8765
```

## 5. Verify the gateway

In another terminal:

```powershell
Invoke-RestMethod "http://127.0.0.1:8765/api/v1/health" |
    Format-List
```

Expected:

```text
status          : ok
model_provider  : ollama
model_available : True
```

Check the selected model if needed:

```powershell
Invoke-RestMethod "http://127.0.0.1:8765/api/v1/model" |
    Format-List
```

You can open the API documentation:

```powershell
Start-Process "http://127.0.0.1:8765/docs"
```

The documentation page is not the robot-control screen. Use the Unity
application for that. A successful health check confirms model availability,
not that an instruction has already been processed successfully.

## 6. Launch the application

Choose **one** of the following options.

### Option A: Standalone Windows application

From the project root:

```powershell
Test-Path ".\Builds\ICU_Assist\ICU_Assist.exe"
Start-Process ".\Builds\ICU_Assist\ICU_Assist.exe"
```

Use the current build in this location, not an older executable copied
elsewhere. Keep the entire `Builds\ICU_Assist` folder together, including
`ICU_Assist_Data`, `UnityPlayer.dll`, and the other runtime files.

The standalone application starts the simulation automatically. It does not
have the Unity Editor's Play button.

If the executable is missing or scripts have changed, rebuild it using
Option B and **ICU Assist > Build Windows Player**.

### Option B: Unity Editor

From the project root:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe" `
    -projectPath "$PWD\unity-icu-simulator"
```

Alternatively, open Unity Hub and add/open the `unity-icu-simulator` folder
using Unity **6000.6.6f1**.

1. Wait for asset import and compilation to finish.
2. Open `Assets\Scenes\ICU_Main.unity` in the Project panel.
3. Press **Play**.
4. Select the **Game** tab.

The scene builds the room, robot, objects, and navigation mesh at runtime;
an empty-looking scene before Play can be normal.

If the scene asset is missing, choose **ICU Assist > Build Demo Scene**.

### Rebuild the standalone application after code changes

1. Close the standalone application so its files are not locked.
2. In Unity, exit Play mode.
3. Wait for script compilation.
4. Choose **ICU Assist > Build Windows Player**.
5. Wait for the build to complete successfully.
6. Launch `Builds\ICU_Assist\ICU_Assist.exe` again.

Editing source files or restarting an old executable does **not** rebuild it.

## 7. Execute your first instruction

In the Unity application's control panel:

1. Check the gateway status. If it failed at startup, click **Refresh Gateway**.
2. Enter:

   ```text
   Move to the monitor.
   ```

3. Click **Request Plan** once.
4. Wait for the response. The first request can take longer while the model
   loads. Avoid repeatedly clicking the button.
5. Check that the response is **READY** and includes:

   ```text
   MOVE_TO monitor_01
   ```

6. Click **Confirm & Execute**.
7. Wait for **Plan completed.** The robot should approach the monitor.

Requesting a plan alone does not move the robot. Do not execute a plan whose
actions differ from your intent. Use **Cancel Plan** instead.

If the response is `NEEDS_CLARIFICATION`, provide a more specific instruction.
If it is `REJECTED` or an error is displayed, read the message before retrying.

## 8. Run a complete demonstration

Start with a fresh simulation: restart the standalone application, or exit and
re-enter Play mode in Unity. This resets the objects and device states.

For **every row**, enter the instruction, click **Request Plan**, review it,
click **Confirm & Execute**, and wait for completion before the next row.

| Step | Instruction | Expected result |
|---|---|---|
| 1 | `Move to the patient bed.` | Robot approaches the bed. |
| 2 | `Pick up the red supply box.` | Robot approaches and carries the red box. |
| 3 | `Place it on the medical platform.` | Robot approaches the platform and releases the box onto it. |
| 4 | `Turn on the monitor.` | Robot approaches the monitor; simulated state becomes ON. |
| 5 | `Show the demo screen on the monitor.` | Monitor state becomes DEMO_SCREEN. |
| 6 | `Turn off the monitor.` | Monitor state becomes OFF. |
| 7 | `Start the infusion device simulation.` | Device state becomes RUNNING_SIMULATION. |
| 8 | `Stop the infusion device simulation.` | Device returns to STANDBY. |
| 9 | `Return home.` | Robot returns to its starting position. |

Pick/place attaches and releases a payload; there is no animated robotic arm.
Device operations are simulated state changes. Check the device-state display
and event log rather than expecting real equipment behavior.

## 9. Supported instruction examples

### Navigation

```text
Move to the patient bed.
Move to the medical platform.
Move to the monitor.
Move to the infusion device.
Move to the red supply box.
Move to the blue supply box.
Return home.
```

### Carrying objects

```text
Pick up the red supply box.
Pick up the blue supply box.
Place it on the medical platform.
Drop the object here.
```

- Only the supply boxes are pickable.
- The robot carries one object at a time.
- Pick up a box before asking to place or drop it.
- The registered placement surface is the medical platform.

### Monitor

```text
Turn on the monitor.
Show the demo screen on the monitor.
Turn off the monitor.
```

The monitor starts OFF. Turn it on before requesting the demo screen.
Turn it off from ON or DEMO_SCREEN.

### Infusion-device simulation

```text
Start the infusion device simulation.
Stop the infusion device simulation.
Set the infusion device to standby.
```

The device starts in STANDBY. Start it before asking to stop a running
simulation. These instructions do not administer medication.

### Combined instructions in Ollama mode

```text
Pick up the red supply box and place it on the medical platform.
Pick up the blue supply box and place it on the medical platform.
Turn on the monitor and show its demo screen.
```

The model may request clarification or return a different plan; always review
it. Plans are limited to six actions, including movement. Separate requests
are easier to diagnose and are recommended for the first demonstration.

### Stop and cancellation

| Control | Behavior |
|---|---|
| Typed `Stop.` | Requests a STOP plan; still requires planning and confirmation. |
| **Cancel Plan** | Cancels a pending/executing plan and clears navigation. |
| **EMERGENCY STOP** | Immediately halts execution; carried payload remains attached. |
| **Reset Stop** | Clears the stop state; request a new plan afterward. |
| **Refresh Gateway** | Retries the gateway health check. |

For an immediate halt, use **EMERGENCY STOP**, not a typed instruction.

### Unsupported tasks

- Opening/closing cabinets.
- Picking up furniture or devices.
- Carrying multiple objects at once.
- Placing objects on unregistered surfaces such as the bed.
- Clinical diagnosis, treatment, dosage, medication administration, or alarm
  suppression.
- Controlling real medical equipment or interacting with real patients.

## 10. Optional: Run in mock mode

Mock mode tests the Unity-to-gateway connection without real AI inference.
It is not a substitute for the VLM demonstration.

Stop the current gateway with **Ctrl+C**. From the project root:

```powershell
$env:VLM_PROVIDER = "mock"
.\scripts\start-vlm.ps1
```

Use **Refresh Gateway** in the application. The health response should show
`model_provider: mock`.

Test one instruction at a time:

```text
Move to the patient bed.
Pick up the red supply box.
Place it on the medical platform.
Return home.
```

The current mock parser is limited: combined tasks are not reliably supported,
and references to the monitor or infusion device are interpreted as device
operations, even when phrased as movement-only requests. Review the plan.

To switch back, stop the mock gateway with **Ctrl+C** and run:

```powershell
$env:VLM_PROVIDER = "ollama"
$env:VLM_MODEL = "qwen2.5vl:3b"
.\scripts\start-vlm.ps1
```

## 11. Run automated tests

### Python gateway tests

From the project root:

```powershell
.\scripts\run-python-tests.ps1
```

If script execution is blocked, use:

```powershell
Set-Location ".\vlm-gateway"
.\.venv\Scripts\python.exe -m pytest -q
```

These tests use the mock adapter and do not require a running gateway or Ollama.

### Unity local tests

1. Open the project in Unity and exit Play mode.
2. Open **Window > General > Test Runner**.
3. Select **EditMode > Run All**.
4. Select **PlayMode > Run All**.

Local PlayMode tests cover idle stability, pick/place, return-home navigation,
and simulated device transitions. The optional live-gateway test is skipped
unless explicitly enabled.

### Optional full-path test with Ollama

Keep the gateway running in Ollama mode. Close the Unity Editor after saving
your work, then launch it from a project-root terminal with:

```powershell
$env:ICU_TEST_LIVE_GATEWAY = "1"
& "C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe" `
    -projectPath "$PWD\unity-icu-simulator"
```

In **Test Runner > PlayMode**, run `LiveGatewayPlanMovesRobotToMonitor`.
It renders a robot-camera image, requests a plan through the Unity HTTP client
and real gateway/model, executes it, and checks proximity to the monitor.
Do not run this test with `-nographics`.

The environment variable must be set before launching Unity; setting it in a
terminal does not change an already running Editor.

## 12. Troubleshooting

| Symptom | Action |
|---|---|
| Gateway connection refused or health check times out | Start the gateway, verify port 8765 with the health command, then click Refresh Gateway. |
| Gateway health is OK but an instruction times out | Check the gateway terminal and Ollama; retain the exact request error. Health alone does not prove inference is working. |
| Model unavailable | Run `ollama list`; confirm `qwen2.5vl:3b` is installed and the port 11434 endpoint responds. |
| Robot stays still after Request Plan | Wait for READY, inspect actions, then click Confirm & Execute. |
| Old behavior remains after code changes | Rebuild and launch the current executable in `Builds\ICU_Assist`. |
| Robot remains stopped | Click Reset Stop, request a new plan, and confirm it. |
| Device command is rejected | Check its current state and follow the transition order in the demonstration. |
| Place/drop is rejected | Confirm the Carrying display names the expected box. |
| Port 8765 is already in use | Check the health endpoint; an existing gateway may already be running. Do not start a second one. |
| Unity scene appears empty | Open ICU_Main, press Play, and select the Game tab. |

Inspect Ollama's currently loaded models:

```powershell
Invoke-RestMethod "http://127.0.0.1:11434/api/ps" |
    ConvertTo-Json -Depth 6
```

Find the process listening on the gateway port:

```powershell
Get-NetTCPConnection -LocalPort 8765 -State Listen |
    Select-Object LocalAddress, LocalPort, OwningProcess
```

Read recent standalone application logs:

```powershell
Get-Content "$env:USERPROFILE\AppData\LocalLow\DefaultCompany\unity-icu-simulator\Player.log" -Tail 80
```

Read recent Ollama logs:

```powershell
Get-Content "$env:LOCALAPPDATA\Ollama\server.log" -Tail 80
```

When reporting a problem, include the exact gateway/status error, the action
plan shown, whether you clicked Confirm & Execute, and whether you used the
standalone build or Unity Editor. Avoid sharing credentials or unrelated logs.

## 13. Shut down

1. Finish or cancel the current task.
2. Close the standalone window, or exit Play mode in Unity.
3. Press **Ctrl+C** in the gateway terminal.
4. If you manually started `ollama serve`, stop that terminal with **Ctrl+C**
   when you no longer need it.

## Quick launch summary

With Ollama running and the current standalone build available:

```powershell
# Terminal 1: start and keep the gateway running.
Set-Location "C:\Users\10314026\OneDrive - BD\IIS Mtech\Fourth Semester\Intelligence User Interface\MidSemProj\vlm3Dgaming"
$env:VLM_PROVIDER = "ollama"
$env:VLM_MODEL = "qwen2.5vl:3b"
.\scripts\start-vlm.ps1
```

```powershell
# Terminal 2: verify the gateway, then open the application if healthy.
Set-Location "C:\Users\10314026\OneDrive - BD\IIS Mtech\Fourth Semester\Intelligence User Interface\MidSemProj\vlm3Dgaming"
$health = Invoke-RestMethod "http://127.0.0.1:8765/api/v1/health"
if ($health.status -eq "ok" -and $health.model_available) {
    Start-Process ".\Builds\ICU_Assist\ICU_Assist.exe"
} else {
    throw "Gateway/model is not ready. Check the gateway and Ollama."
}
```

In the application: **enter instruction > Request Plan > review READY plan >
Confirm & Execute > wait for Plan completed**.
