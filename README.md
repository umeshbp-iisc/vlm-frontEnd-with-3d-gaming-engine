# ICU Assist: VLM-Controlled Robot Simulation

This repository contains a Unity 6 ICU scene, a C# robot/action pipeline, and
a Python FastAPI gateway. It is a simulation prototype; it does not operate
real medical equipment or make clinical decisions.

For step-by-step startup commands, supported instructions, rebuilding, testing,
and troubleshooting, see the [execution guide](execution.md).

## Start the VLM gateway

In PowerShell:

```powershell
.\scripts\start-vlm.ps1
```

Keep Ollama running with the `qwen2.5vl:3b` model available. The API health
endpoint is `http://127.0.0.1:8765/api/v1/health`. For deterministic
development without a model, use `$env:VLM_PROVIDER = "mock"`.

## Open and run the Unity scene

1. Open `unity-icu-simulator` in Unity Hub using the installed Unity 6 editor.
2. Open `Assets/Scenes/ICU_Main.unity`.
3. Press Play. The ICU room, targets, robot, runtime NavMesh, and control UI are
   created when the scene starts.
4. With the Python service running, enter a command such as
   `Move to the monitor.` and choose **Request Plan**.
5. Review the returned plan, then choose **Confirm & Execute**. Test pick and
   place with `Pick up the red supply box.` followed by
   `Place it on the medical platform.`.
6. Use **EMERGENCY STOP** to halt movement. **Reset Stop** is required before
   issuing another plan.

If the scene asset is missing, use **ICU Assist > Build Demo Scene** in the
Unity Editor. The Unity HTTP client uses localhost port 8765 by default.

After updating scripts, exit Play mode, allow Unity to recompile, and press
Play again to rebuild the runtime scene. The robot should stay still until a
plan is confirmed; only the demo camera follows it. For device interactions,
try `Turn on the monitor.` and review the plan before executing.

## Run the standalone Windows application

The standalone executable does not update when you edit Unity scripts. To
rebuild it, exit Play mode and choose **ICU Assist > Build Windows Player**.
The output is `Builds\ICU_Assist\ICU_Assist.exe` relative to this repository.
Keep the entire output folder together, including `ICU_Assist_Data`,
`UnityPlayer.dll`, and the other runtime files.

Start Ollama and the Python gateway first, then launch from the repository root:

```powershell
Invoke-RestMethod "http://127.0.0.1:8765/api/v1/health"
Start-Process ".\Builds\ICU_Assist\ICU_Assist.exe"
```

Health should report `status: ok` and `model_available: true`. If the app was
opened before the gateway, click **Refresh Gateway** to retry the startup
health check. A health-check failure is separate from the result of a command.
Enter an instruction, click **Request Plan**, wait for `READY`, and click
**Confirm & Execute**. Old copies of the executable still contain old code.
Request failures show the URL and server error details; execution events are
also recorded in Unity's player log.

## Tests

```powershell
Set-Location ".\vlm-gateway"
.\.venv\Scripts\python.exe -m pytest -q
```

Unity tests can be run from **Window > General > Test Runner**. Run both the
EditMode and PlayMode tabs. PlayMode tests cover idle stability, pick and place
(including the released box settling on the platform), return-home navigation,
and simulated monitor/infusion state changes. They use local action plans, so
Ollama and the Python gateway are not required for these tests.

For the optional real-model PlayMode test, start the gateway in Ollama mode and
set `$env:ICU_TEST_LIVE_GATEWAY = "1"` before launching the Unity test runner
process. `LiveGatewayPlanMovesRobotToMonitor` sends a rendered robot-camera
image through the Unity HTTP client and gateway, validates the returned plan,
executes it, and checks that the robot reaches the monitor. This test needs a
graphics device; do not use `-nographics`.
