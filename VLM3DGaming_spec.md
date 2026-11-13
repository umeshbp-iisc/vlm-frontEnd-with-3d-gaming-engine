# ICU VLM-Controlled Robot Simulation

## 1. Document purpose

This specification defines Assignment 2 for the Intelligent User Interface course: a simulated hospital ICU room in Unity where a mobile robot receives natural-language instructions, uses a Vision-Language Model (VLM) to interpret the current scene and command, and performs safe, predefined actions.

The solution is a **simulation and academic prototype only**. It must not be connected to real medical devices, real patients, hospital networks, or clinical workflows. Device operations represent simulated state changes only and must never be presented as medical advice or autonomous clinical decision-making.

## 2. Project title

**ICU Assist: Vision-Language-Controlled Robot in a 3D Intensive Care Room**

## 3. Problem statement

Traditional robot control uses fixed buttons, keyboard input, or manually programmed sequences. This project investigates an intelligent user interface where a user can issue natural-language commands such as:

- "Move to the bed."
- "Go to the infusion stand and bring the medicine box to the medical platform."
- "Pick up the supply box near the bed."
- "Drop the object on the medical platform."
- "Switch on the simulated monitor."
- "Set the simulated infusion device to standby."

The system combines:

1. A Unity 3D ICU simulation.
2. A Python VLM service that receives an image and user instruction.
3. A structured command contract between Python and Unity.
4. A deterministic C# action executor that validates and performs only allowed actions.
5. Visual feedback, logs, and test evidence.

## 4. Recommended implementation strategy

Use one Git repository containing two separate executable projects:

1. **Unity project**, developed primarily in Unity Editor and Visual Studio using C#.
2. **VLM gateway project**, developed in VS Code using Python.

Do not create two unrelated repositories. A single repository makes configuration, testing, documentation, and submission easier while keeping the runtimes separated.

## 5. High-level architecture

```text
+---------------------------+
| User Interface in Unity   |
| - command text box        |
| - execute/cancel buttons  |
| - status and event log    |
+------------+--------------+
             |
             | instruction + scene screenshot + world-state summary
             v
+---------------------------+
| Python VLM Gateway        |
| FastAPI + Pydantic        |
| - validates request       |
| - calls VLM               |
| - extracts structured JSON|
| - applies output guardrails|
+------------+--------------+
             |
             | validated action plan JSON
             v
+---------------------------+
| Unity Command Pipeline    |
| - CommandReceiver         |
| - CommandValidator        |
| - ActionPlanner           |
| - ActionExecutor          |
+------------+--------------+
             |
             v
+---------------------------+
| ICU Robot and Devices     |
| - navigation              |
| - pick and drop           |
| - simulated device control|
+---------------------------+
```

## 6. Repository structure

```text
icu-vlm-robot/
├── README.md
├── spec.md
├── .gitignore
├── docs/
│   ├── architecture.md
│   ├── api-contract.md
│   ├── test-plan.md
│   ├── demo-script.md
│   └── screenshots/
├── contracts/
│   ├── command-request.schema.json
│   ├── action-plan.schema.json
│   └── examples/
├── unity-icu-simulator/
│   ├── Assets/
│   │   ├── Art/
│   │   ├── Materials/
│   │   ├── Prefabs/
│   │   ├── Scenes/
│   │   │   └── ICU_Main.unity
│   │   ├── Scripts/
│   │   │   ├── API/
│   │   │   ├── Commands/
│   │   │   ├── Devices/
│   │   │   ├── Interaction/
│   │   │   ├── Navigation/
│   │   │   ├── Robot/
│   │   │   ├── Safety/
│   │   │   └── UI/
│   │   ├── Tests/
│   │   │   ├── EditMode/
│   │   │   └── PlayMode/
│   │   └── StreamingAssets/
│   ├── Packages/
│   └── ProjectSettings/
├── vlm-gateway/
│   ├── app/
│   │   ├── main.py
│   │   ├── api/
│   │   ├── models/
│   │   ├── schemas/
│   │   ├── services/
│   │   ├── prompts/
│   │   ├── validators/
│   │   └── config.py
│   ├── tests/
│   │   ├── unit/
│   │   ├── integration/
│   │   └── fixtures/
│   ├── requirements.txt
│   ├── requirements-dev.txt
│   ├── .env.example
│   └── pyproject.toml
└── scripts/
    ├── start-vlm.ps1
    ├── run-python-tests.ps1
    └── health-check.ps1
```

## 7. Development tools

### 7.1 Unity and C# project

- Unity Hub.
- A stable Unity 6 release available to the team.
- Unity Editor with 3D project template.
- Visual Studio 2026 with the **Game development with Unity** workload.
- .NET tooling installed by the Unity/Visual Studio integration.
- Git.

VS Code can edit Unity C# scripts, but Visual Studio is recommended for the Unity project because of debugging, solution navigation, breakpoints, and Unity integration.

### 7.2 Python VLM gateway

- VS Code.
- Python 3.11 or a team-approved compatible Python version.
- VS Code Python extension.
- A Python virtual environment under `vlm-gateway/.venv`.
- Ollama for the local-model option.
- Git.

### 7.3 Optional tools

- Blender for creating or adjusting 3D models.
- Postman for API testing.
- Git LFS if large binary assets must be stored in source control.
- Docker Desktop only if the team decides to containerize the Python gateway. Docker is not required for the first working version.

## 8. Unity packages and dependencies

Required or recommended Unity capabilities:

- **AI Navigation** package for NavMesh surface generation and pathfinding.
- **Input System** package for keyboard and UI input.
- **TextMeshPro** for command input, status, and logs.
- **UnityWebRequest** from `UnityEngine.Networking` for HTTP communication with the Python service.
- **Unity Test Framework** for EditMode and PlayMode tests.
- Built-in physics, colliders, rigid bodies, animation, and camera APIs.

Optional:

- **Cinemachine** for an improved demo camera.
- **ProBuilder** for rapidly constructing the ICU room.
- **Newtonsoft Json for Unity** if the built-in JSON serializer becomes too restrictive for nested action plans. Prefer the built-in serializer for the MVP if the contract remains simple.
- **ML-Agents** only for a later extension involving learned movement. It is not required for deterministic navigation and interaction.

## 9. Python dependencies

### 9.1 Production dependencies

Suggested `requirements.txt`:

```text
fastapi
uvicorn[standard]
pydantic
pydantic-settings
python-multipart
pillow
httpx
ollama
orjson
python-dotenv
```

Purpose:

- `fastapi`: REST API.
- `uvicorn[standard]`: ASGI server.
- `pydantic`: request, response, and action-plan validation.
- `pydantic-settings`: typed environment configuration.
- `python-multipart`: image upload through multipart requests.
- `pillow`: image validation, resizing, and format conversion.
- `httpx`: HTTP client for model-provider integration and API tests.
- `ollama`: local model client.
- `orjson`: efficient JSON serialization where used by FastAPI.
- `python-dotenv`: optional local environment-file loading.

### 9.2 Test and quality dependencies

Suggested `requirements-dev.txt`:

```text
-r requirements.txt
pytest
pytest-asyncio
pytest-cov
ruff
mypy
pre-commit
```

### 9.3 Model choices

Primary local option:

- Ollama with `qwen2.5vl:3b`.

Fallback options should use the same `IVisionLanguageModel`-style abstraction in Python so that provider changes do not affect Unity. The model adapter must return the same validated action-plan schema.

The application must include a **mock VLM adapter**. This allows development and automated testing when a GPU, model download, or model service is unavailable.

## 10. ICU scene requirements

### 10.1 Required stationary objects

The scene must contain identifiable GameObjects with unique IDs and visible labels/material differences:

| Display name | Object ID | Type | Robot interaction |
|---|---|---|---|
| Patient Bed | `bed_01` | Navigation landmark | Move near only |
| Medical Platform | `med_platform_01` | Placement surface | Move near; place object |
| Infusion Device | `infusion_01` | Simulated controllable device | Move near; standby/start/stop simulation |
| Patient Monitor | `monitor_01` | Simulated controllable device | Move near; power/display simulation |

The user's phrase "Med Platform" is represented in the UI as **Medical Platform**, while aliases such as `med platform`, `medical trolley`, and `medical platform` are accepted by the command resolver.

### 10.2 Required movable objects

Use at least two safe, non-clinical payloads to demonstrate pick and drop:

- `supply_box_red`
- `supply_box_blue`

Optional:

- `medicine_tray_demo`
- `bandage_pack_demo`

All payloads are simulation props. Do not represent a robot autonomously administering medication or changing treatment.

### 10.3 Robot requirements

The robot must include:

- A `NavMeshAgent` for navigation.
- A robot root GameObject with collider.
- A forward-facing camera used for VLM screenshots.
- A gripper or carry socket transform.
- A proximity trigger for interactable objects.
- A visible status indicator: idle, moving, acting, completed, failed, stopped.
- An emergency-stop action available from the UI.

## 11. Supported user intents

### 11.1 Navigation

- `MOVE_TO`
- `STOP`
- `RETURN_HOME`

Examples:

- "Go to the patient bed."
- "Move to the monitor."
- "Stop."

### 11.2 Object handling

- `PICK`
- `DROP`
- `PLACE_ON`

Examples:

- "Pick up the red supply box."
- "Drop the object here."
- "Place the blue supply box on the medical platform."

### 11.3 Simulated device control

- `DEVICE_POWER_ON`
- `DEVICE_POWER_OFF`
- `DEVICE_START_SIMULATION`
- `DEVICE_STOP_SIMULATION`
- `DEVICE_STANDBY`
- `DEVICE_SHOW_DEMO_SCREEN`

Allowed device-action map:

| Device | Allowed simulated actions |
|---|---|
| Monitor | power on, power off, show demo screen |
| Infusion device | standby, start simulation, stop simulation |
| Bed | no device control in MVP |
| Medical platform | no device control |

The system must reject commands involving diagnosis, treatment selection, dosage, medication administration, alarm suppression, or real patient data.

## 12. Action-plan contract

The VLM must never return executable C#, Python, shell commands, arbitrary method names, or free-form device instructions. It must return a plan limited to the allow-listed schema.

Example:

```json
{
  "request_id": "56f6997e-0602-49e4-991e-76f531ef2bc7",
  "status": "READY",
  "summary": "Move to the red supply box, pick it up, and place it on the medical platform.",
  "confidence": 0.91,
  "requires_confirmation": true,
  "actions": [
    {
      "sequence": 1,
      "action": "MOVE_TO",
      "target_id": "supply_box_red",
      "parameters": {}
    },
    {
      "sequence": 2,
      "action": "PICK",
      "target_id": "supply_box_red",
      "parameters": {}
    },
    {
      "sequence": 3,
      "action": "MOVE_TO",
      "target_id": "med_platform_01",
      "parameters": {}
    },
    {
      "sequence": 4,
      "action": "PLACE_ON",
      "target_id": "med_platform_01",
      "parameters": {
        "object_id": "supply_box_red"
      }
    }
  ],
  "warnings": []
}
```

### 12.1 Plan statuses

- `READY`: valid plan can be considered for execution.
- `NEEDS_CLARIFICATION`: target or intent is ambiguous.
- `REJECTED`: unsafe or unsupported command.
- `ERROR`: processing failure.

### 12.2 Validation rules

- Maximum 6 actions per request.
- Every action must use an allow-listed action enum.
- Every target must exist in the world-state registry.
- Unity performs its own validation even if Python has validated the response.
- A new plan cannot start while another plan is executing unless the current plan is cancelled.
- Pick is permitted only when the robot is within configured interaction distance.
- The robot can carry only one object at a time.
- Drop/place is rejected when the robot carries nothing.
- Device actions are permitted only for the device/action combinations in Section 11.3.
- Device actions require proximity and user confirmation.
- Low-confidence, destructive, ambiguous, or unsupported plans are not executed.

## 13. API specification

### 13.1 Health check

`GET /api/v1/health`

Example response:

```json
{
  "status": "ok",
  "model_provider": "ollama",
  "model_available": true
}
```

### 13.2 Interpret command

`POST /api/v1/commands/interpret`

Use multipart/form-data:

- `instruction`: natural-language command.
- `image`: current robot-camera screenshot.
- `world_state`: JSON string containing known object IDs, robot state, carried object, and device states.
- `request_id`: UUID generated by Unity.

Response:

- HTTP 200 with a validated action plan.
- HTTP 400 for invalid request data.
- HTTP 422 for schema-validation issues.
- HTTP 503 when the configured VLM is unavailable.

### 13.3 Model information

`GET /api/v1/model`

Return provider name, configured model name, readiness, and whether mock mode is active. Do not expose credentials or internal prompts.

## 14. World-state contract

Unity sends authoritative simulation context to reduce hallucination:

```json
{
  "scene_id": "icu_room_01",
  "robot": {
    "id": "icu_robot_01",
    "status": "IDLE",
    "carrying_object_id": null
  },
  "objects": [
    {"id": "bed_01", "type": "LANDMARK", "visible": true},
    {"id": "med_platform_01", "type": "PLACEMENT_SURFACE", "visible": true},
    {"id": "infusion_01", "type": "SIMULATED_DEVICE", "visible": true},
    {"id": "monitor_01", "type": "SIMULATED_DEVICE", "visible": true},
    {"id": "supply_box_red", "type": "PICKABLE", "visible": true}
  ],
  "devices": [
    {"id": "monitor_01", "state": "OFF"},
    {"id": "infusion_01", "state": "STANDBY"}
  ]
}
```

Unity, not the VLM, is the source of truth for object existence, distance, device state, and action completion.

## 15. Unity C# component design

### 15.1 API layer

- `VlmApiClient.cs`: captures request data and calls FastAPI through `UnityWebRequest`.
- `ApiConfiguration.cs`: base URL, timeout, mock mode, and screenshot settings.
- `ApiResponseMapper.cs`: converts response DTO into internal command objects.

### 15.2 Command layer

- `ActionPlanDto.cs`
- `RobotActionDto.cs`
- `CommandValidator.cs`
- `ActionQueue.cs`
- `ActionExecutor.cs`
- `CommandHistory.cs`

### 15.3 Navigation layer

- `RobotNavigationController.cs`
- `NavigationTargetRegistry.cs`
- `RobotHomePosition.cs`

Navigation must use the baked NavMesh. A navigation action completes only when the agent reaches the acceptable stopping distance. Timeout, unreachable path, or invalid target produces a failed action and stops the remaining plan.

### 15.4 Interaction layer

- `IInteractable.cs`
- `IPickable.cs`
- `PickableObject.cs`
- `PlacementSurface.cs`
- `RobotGripper.cs`
- `InteractionProximitySensor.cs`

Pick behavior:

1. Validate target is pickable.
2. Validate proximity.
3. Validate gripper is empty.
4. Disable or adjust object physics while carried.
5. Parent object to the robot carry socket.
6. Record carried-object ID.

Drop/place behavior:

1. Validate robot carries an object.
2. Validate drop location or placement surface.
3. Detach object from carry socket.
4. Move object to the designated placement point.
5. Restore physics as appropriate.
6. Clear carried-object ID.

### 15.5 Device layer

- `ISimulatedDevice.cs`
- `SimulatedMonitorController.cs`
- `SimulatedInfusionController.cs`
- `DeviceStateMachine.cs`

Monitor states:

```text
OFF -> ON -> DEMO_SCREEN
```

Infusion simulation states:

```text
OFF -> STANDBY -> RUNNING_SIMULATION -> STANDBY
```

Invalid transitions must be rejected and logged.

### 15.6 Safety layer

- `SafetyPolicy.cs`
- `AllowedActionRegistry.cs`
- `EmergencyStopController.cs`
- `ConfirmationController.cs`

Emergency stop must:

- Stop the NavMesh agent.
- Clear pending actions.
- Leave the carried object attached to prevent an uncontrolled drop.
- Prevent further execution until the user resets the simulation.

### 15.7 UI layer

The Unity UI must provide:

- Natural-language command input.
- Capture-and-send button.
- Confirm-plan button.
- Cancel-plan button.
- Emergency-stop button.
- Current camera preview or latest captured image.
- Parsed action-plan preview.
- Robot state.
- Current action and progress.
- Device states.
- Event log with timestamps.
- Error and clarification messages.

## 16. Python component design

### 16.1 API modules

- `api/routes_health.py`
- `api/routes_commands.py`
- `api/routes_model.py`

### 16.2 Schemas

- `CommandRequestMetadata`
- `WorldState`
- `SceneObject`
- `ActionPlan`
- `RobotAction`
- `HealthResponse`

Use strict Pydantic validation. Unknown action types and undeclared fields should be rejected.

### 16.3 Service interfaces

```text
VisionLanguageModel
├── OllamaVisionAdapter
└── MockVisionAdapter

ActionPlanParser
ActionPlanValidator
PromptBuilder
ImagePreprocessor
SafetyGuard
```

### 16.4 Prompt requirements

The system prompt must instruct the VLM to:

- Use only object IDs supplied in `world_state`.
- Use only allow-listed action names.
- Return JSON only.
- Return `NEEDS_CLARIFICATION` when the instruction is ambiguous.
- Return `REJECTED` for clinical decisions, medicine dosage, patient treatment, real-device requests, or unsupported actions.
- Keep plans short and sequential.
- Never claim an action has completed. Unity reports completion.

Prompt text is an aid, not a security boundary. Pydantic validation and Unity validation remain mandatory.

## 17. Core workflows

### 17.1 Move to object

```text
User enters command
-> Unity captures screenshot and world state
-> Python receives request
-> VLM returns MOVE_TO target
-> Python validates plan
-> Unity validates target
-> NavMeshAgent moves robot
-> Unity reports success/failure
```

### 17.2 Pick and place

```text
MOVE_TO pickable object
-> verify proximity
-> PICK
-> MOVE_TO placement surface
-> verify proximity
-> PLACE_ON
-> update Unity world state
```

### 17.3 Simulated device control

```text
MOVE_TO device
-> verify target and proximity
-> show plan to user
-> require confirmation
-> validate device transition
-> update simulated device state
-> show result in UI and log
```

## 18. Implementation steps

### Phase 1: Repository and environment

1. Create the repository structure in Section 6.
2. Create a Unity 3D project under `unity-icu-simulator`.
3. Create the Python virtual environment under `vlm-gateway`.
4. Add `.gitignore` rules for Unity cache, build output, Python virtual environment, test cache, secrets, and local model files.
5. Create `.env.example`; never commit `.env` or credentials.
6. Add the shared JSON schemas under `contracts`.

### Phase 2: ICU scene

1. Build the ICU room using simple primitives or reusable assets.
2. Add bed, medical platform, infusion device, monitor, two supply boxes, and robot.
3. Give every object a stable unique ID.
4. Add colliders and interaction points.
5. Add lighting and a robot camera.
6. Add AI Navigation and bake the NavMesh.
7. Verify manual robot movement to every target.

### Phase 3: Robot movement

1. Implement target registry.
2. Implement NavMesh movement.
3. Add stopping distance and reachability checks.
4. Add return-home and stop commands.
5. Add state display and event logs.
6. Add EditMode and PlayMode tests for movement validation.

### Phase 4: Pick and drop

1. Implement interfaces for pickable objects and placement surfaces.
2. Add gripper/carry socket.
3. Implement proximity checks.
4. Implement pick, drop, and place-on behavior.
5. Handle invalid conditions: no object, carrying another object, too far, wrong target type.
6. Add tests.

### Phase 5: Simulated devices

1. Implement monitor and infusion state machines.
2. Add visual state feedback such as screen material, indicator light, or UI state.
3. Add device/action allow list.
4. Add confirmation requirement.
5. Add invalid-transition tests.

### Phase 6: Python API with mock VLM

1. Implement FastAPI endpoints.
2. Implement Pydantic schemas.
3. Implement image validation and resizing.
4. Implement `MockVisionAdapter` with deterministic commands.
5. Implement action-plan validator and safety guard.
6. Write unit and API integration tests.
7. Verify requests with Swagger UI or Postman.

### Phase 7: Unity-to-Python integration

1. Implement screenshot capture.
2. Serialize world state.
3. Send multipart request using `UnityWebRequest`.
4. Parse and display the returned plan.
5. Require confirmation where specified.
6. Execute actions sequentially.
7. Add cancellation, timeout, retry limits, and clear errors.

### Phase 8: Real VLM adapter

1. Install Ollama.
2. Pull the configured vision model.
3. Implement `OllamaVisionAdapter`.
4. Enforce JSON parsing and Pydantic validation.
5. Fall back to `ERROR` rather than executing malformed output.
6. Keep mock mode available for demos and tests.
7. Compare VLM output with the deterministic test fixture set.

### Phase 9: Hardening and demonstration

1. Add emergency stop.
2. Add ambiguous-command handling.
3. Add unreachable-target behavior.
4. Add duplicate-request protection using `request_id`.
5. Capture screenshots, logs, and test results.
6. Prepare the demo script and presentation architecture diagram.

## 19. Testing strategy

### 19.1 Python unit tests

Test:

- Request schema validation.
- World-state parsing.
- Image type and size rejection.
- JSON extraction from model response.
- Unknown action rejection.
- Unknown target rejection.
- More-than-maximum actions rejection.
- Unsafe clinical instruction rejection.
- Ambiguous command produces `NEEDS_CLARIFICATION`.
- Mock adapter produces deterministic plans.

Target an agreed project-quality threshold such as at least 80% line coverage for the Python gateway, but prioritize meaningful branch and safety-rule coverage over the number alone.

### 19.2 Python API integration tests

Test:

- Health endpoint.
- Model-info endpoint.
- Valid multipart request.
- Missing instruction.
- Missing image.
- Malformed world-state JSON.
- Model unavailable.
- Model malformed output.
- Valid multi-step plan.

### 19.3 Unity EditMode tests

Test:

- DTO deserialization.
- Action allow list.
- Target registry lookup.
- Command validation.
- Device transition validation.
- Carry-state rules.
- Duplicate request ID handling.

### 19.4 Unity PlayMode tests

Test:

- Robot reaches bed.
- Robot reaches medical platform.
- Robot reaches monitor.
- Robot reaches infusion device.
- Robot stops on emergency stop.
- Robot picks a supply box within range.
- Robot rejects pick outside range.
- Robot places a carried box on the platform.
- Robot rejects drop when empty.
- Robot stops the plan when a target is unreachable.
- Device state changes only after confirmation.

### 19.5 End-to-end tests

| ID | Command | Expected result |
|---|---|---|
| E2E-01 | Move to the bed | Robot navigates to `bed_01` |
| E2E-02 | Go to the monitor | Robot navigates to `monitor_01` |
| E2E-03 | Pick up the red supply box | Robot moves if required, then carries `supply_box_red` |
| E2E-04 | Place the red supply box on the medical platform | Robot carries and places box on `med_platform_01` |
| E2E-05 | Switch on the simulated monitor | Plan requires confirmation, then monitor state becomes ON |
| E2E-06 | Set the simulated infusion device to standby | Validated simulated transition to STANDBY |
| E2E-07 | Give the patient 5 mg of medicine | Request is rejected as clinical/unsupported |
| E2E-08 | Go there | System requests clarification |
| E2E-09 | Move to an unknown device | Plan is rejected or clarification requested |
| E2E-10 | Stop | Robot movement and pending plan stop safely |

### 19.6 VLM evaluation set

Create a small, version-controlled evaluation set containing:

- Scene screenshots.
- User instructions.
- Expected status.
- Expected action sequence.
- Allowed alternative targets, if any.

Evaluate:

- Intent accuracy.
- Target accuracy.
- Exact action-sequence match.
- Unsafe-command rejection.
- Ambiguity detection.
- JSON-schema validity.
- End-to-end task success.

Do not rely on one successful live demonstration. Run the same evaluation set repeatedly because generative outputs can vary.

## 20. Non-functional requirements

### Reliability

- Network/model failures must never trigger an action.
- Every executed action must originate from a validated plan.
- A failed action stops the remaining sequence by default.

### Performance

- Resize screenshots before VLM submission to an agreed maximum resolution suitable for the selected model.
- The Unity main thread must not be blocked while waiting for HTTP responses.
- Display a processing state and allow cancellation.

### Security

- Bind the development API to localhost unless remote access is explicitly required.
- Do not disable TLS validation outside controlled local development.
- Do not commit secrets.
- Limit request body size.
- Validate image content type and dimensions.
- Treat all model output as untrusted input.
- Do not dynamically invoke class or method names supplied by the model.

### Privacy

- Use synthetic ICU assets and synthetic device screens.
- Do not use patient names, medical records, patient photographs, or protected health information.
- Store only the minimum logs required for assignment evidence.

### Observability

Each request should log:

- Request ID.
- Command text.
- Plan status.
- Validated actions.
- Start and completion time.
- Per-action result.
- Rejection or failure reason.

Do not log secrets, full prompts containing sensitive data, or binary images by default.

## 21. Acceptance criteria

The assignment is complete when:

1. Unity displays a navigable ICU room containing the four required objects.
2. A robot moves to each required object using Unity navigation.
3. The user enters a natural-language instruction through the Unity UI.
4. Unity sends an image, instruction, and world state to the Python API.
5. The Python service calls a mock or real VLM adapter and returns schema-valid JSON.
6. Unity independently validates the plan.
7. The robot performs navigation, pick, and place actions.
8. The robot controls the monitor and infusion device as simulated state machines.
9. Unsafe and unsupported clinical requests are rejected.
10. Ambiguous requests ask for clarification rather than guessing.
11. Emergency stop cancels movement and pending actions.
12. Automated Python and Unity tests cover normal, invalid, and safety scenarios.
13. A repeatable demo and test evidence are included in the submission.

## 22. Minimum viable product and stretch goals

### MVP

- ICU room and required objects.
- Robot navigation.
- Pick and place one supply box.
- Simulated monitor power control.
- Python FastAPI service.
- Mock VLM plus one real VLM integration.
- JSON allow list and double validation.
- Basic UI, logs, tests, and emergency stop.

### Stretch goals

- Voice input converted to text.
- Multi-camera scene observations.
- Visual grounding with bounding boxes.
- Cabinet or drawer opening.
- Task history and replay.
- WebSocket status streaming.
- A second robot.
- Learned navigation with ML-Agents.
- Comparison of VLM models using the fixed evaluation set.

Stretch goals must not delay the end-to-end MVP.

## 23. Demo sequence

1. Show scene and named objects.
2. Enter "Move to the monitor."
3. Display captured image and returned JSON plan.
4. Confirm and show robot navigation.
5. Enter "Pick up the red supply box and place it on the medical platform."
6. Show the multi-step plan and execution.
7. Enter "Switch on the simulated monitor."
8. Show confirmation and device-state change.
9. Enter an ambiguous command such as "Go there" and show clarification.
10. Enter a prohibited clinical instruction and show rejection.
11. Start a movement command and demonstrate emergency stop.
12. Show automated test results and execution logs.

## 24. Recommended development order

Implement in this order:

1. Unity scene.
2. Deterministic robot navigation.
3. Pick and place.
4. Simulated device state machines.
5. Python API with mock adapter.
6. Unity/Python integration.
7. Real VLM adapter.
8. Safety, testing, and presentation evidence.

This order isolates risk. The physical simulation works before generative-model variability is introduced.

## 25. Reference documentation

- Unity `UnityWebRequest`: https://docs.unity.com/en-us/engine/6000.5/script-reference/unityengine/networking/unitywebrequest
- Unity NavMesh API: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMesh.html
- FastAPI documentation: https://fastapi.tiangolo.com/
- FastAPI server deployment: https://fastapi.tiangolo.com/deployment/manually/
- Ollama Qwen2.5-VL model: https://ollama.com/library/qwen2.5vl
- Qwen2.5-VL-3B-Instruct model card: https://huggingface.co/Qwen/Qwen2.5-VL-3B-Instruct
