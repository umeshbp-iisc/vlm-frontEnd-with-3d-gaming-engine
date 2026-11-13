from __future__ import annotations

import io
import json
import os
import re
from enum import StrEnum
from typing import Annotated, Any, Protocol
from uuid import UUID

import httpx
import ollama
from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from PIL import Image, UnidentifiedImageError
from pydantic import BaseModel, ConfigDict, Field, ValidationError

MAX_IMAGE_BYTES = int(os.getenv("MAX_IMAGE_BYTES", "5242880"))
MIN_CONFIDENCE = float(os.getenv("MIN_CONFIDENCE", "0.55"))
OLLAMA_BASE_URL = os.getenv("OLLAMA_BASE_URL", "http://127.0.0.1:11434")
VLM_MODEL = os.getenv("VLM_MODEL", "qwen2.5vl:3b")
VLM_PROVIDER = os.getenv("VLM_PROVIDER", "ollama").lower()


class StrictModel(BaseModel):
    model_config = ConfigDict(extra="forbid", str_strip_whitespace=True)


class ObjectType(StrEnum):
    LANDMARK = "LANDMARK"
    PLACEMENT_SURFACE = "PLACEMENT_SURFACE"
    SIMULATED_DEVICE = "SIMULATED_DEVICE"
    PICKABLE = "PICKABLE"


class ActionType(StrEnum):
    MOVE_TO = "MOVE_TO"
    STOP = "STOP"
    RETURN_HOME = "RETURN_HOME"
    PICK = "PICK"
    DROP = "DROP"
    PLACE_ON = "PLACE_ON"
    DEVICE_POWER_ON = "DEVICE_POWER_ON"
    DEVICE_POWER_OFF = "DEVICE_POWER_OFF"
    DEVICE_START_SIMULATION = "DEVICE_START_SIMULATION"
    DEVICE_STOP_SIMULATION = "DEVICE_STOP_SIMULATION"
    DEVICE_STANDBY = "DEVICE_STANDBY"
    DEVICE_SHOW_DEMO_SCREEN = "DEVICE_SHOW_DEMO_SCREEN"


class PlanStatus(StrEnum):
    READY = "READY"
    NEEDS_CLARIFICATION = "NEEDS_CLARIFICATION"
    REJECTED = "REJECTED"
    ERROR = "ERROR"


class SceneObject(StrictModel):
    id: str = Field(min_length=1, max_length=80)
    type: ObjectType
    visible: bool = True


class RobotState(StrictModel):
    id: str
    status: str
    carrying_object_id: str | None = None


class DeviceState(StrictModel):
    id: str
    state: str


class WorldState(StrictModel):
    scene_id: str
    robot: RobotState
    objects: list[SceneObject] = Field(max_length=64)
    devices: list[DeviceState] = Field(max_length=32)


class RobotAction(StrictModel):
    sequence: int = Field(ge=1, le=6)
    action: ActionType
    target_id: str | None = None
    parameters: dict[str, Any] = Field(default_factory=dict)


class ActionPlan(StrictModel):
    request_id: UUID
    status: PlanStatus
    summary: str = Field(max_length=500)
    confidence: float = Field(ge=0, le=1)
    requires_confirmation: bool
    actions: list[RobotAction] = Field(max_length=6)
    warnings: list[str] = Field(default_factory=list, max_length=10)


class HealthResponse(StrictModel):
    status: str
    model_provider: str
    model_available: bool


class ModelInfo(StrictModel):
    model_provider: str
    model_name: str
    model_available: bool
    mock_mode: bool


class VisionLanguageModel(Protocol):
    provider: str
    model_name: str

    async def is_available(self) -> bool: ...

    async def interpret(
        self,
        instruction: str,
        image_bytes: bytes,
        world_state: WorldState,
        request_id: UUID,
    ) -> ActionPlan: ...


_UNSAFE_TERMS = re.compile(
    r"\b(diagnos(?:e|is)|treat(?:ment)?|dos(?:e|age)|administer(?:ing)? (?:medication|medicine)|"
    r"give (?:the )?(?:patient|person) .{0,35}(?:mg|medicine|medication)|"
    r"suppress (?:the )?alarm|patient record|medical record|real patient)\b",
    re.IGNORECASE,
)
_COMPLETION_CLAIM = re.compile(
    r"\b(moved|picked up|placed|dropped|switched on|turned on|powered on|"
    r"completed|finished|successfully)\b",
    re.IGNORECASE,
)
_DEVICE_ACTIONS = {
    "monitor_01": {
        ActionType.DEVICE_POWER_ON,
        ActionType.DEVICE_POWER_OFF,
        ActionType.DEVICE_SHOW_DEMO_SCREEN,
    },
    "infusion_01": {
        ActionType.DEVICE_STANDBY,
        ActionType.DEVICE_START_SIMULATION,
        ActionType.DEVICE_STOP_SIMULATION,
    },
}


def _target_for_instruction(instruction: str, world: WorldState) -> SceneObject | None:
    text = instruction.lower()
    aliases = {
        "bed_01": ("patient bed", "first bed", "bed 1", "bed one"),
        "bed_02": ("patient bed 2", "second bed", "bed 2", "bed two", "second patient bed"),
        "med_platform_01": (
            "medical platform",
            "med platform",
            "medical trolley",
            "platform",
        ),
        "infusion_01": ("infusion",),
        "monitor_01": ("monitor",),
        "supply_box_red": ("red supply box", "red box", "supply box red"),
        "supply_box_blue": ("blue supply box", "blue box", "supply box blue"),
        "medicine_cabinet_01": ("medicine cabinet", "cabinet", "drug cabinet"),
        "waste_bin_01": ("waste bin", "bin", "trash", "rubbish bin"),
    }
    objects = {obj.id: obj for obj in world.objects}
    for object_id, names in aliases.items():
        obj = objects.get(object_id)
        if obj and any(name in text for name in names):
            return obj
    return None


def _action(
    sequence: int, action: ActionType, target_id: str | None = None, **parameters: Any
) -> RobotAction:
    return RobotAction(
        sequence=sequence, action=action, target_id=target_id, parameters=parameters
    )


def add_required_approach_actions(plan: ActionPlan) -> ActionPlan:
    if plan.status != PlanStatus.READY:
        return plan
    actions: list[RobotAction] = []
    requires_confirmation = plan.requires_confirmation
    for action in plan.actions:
        needs_approach = action.action in {
            ActionType.PICK,
            ActionType.PLACE_ON,
            ActionType.DEVICE_POWER_ON,
            ActionType.DEVICE_POWER_OFF,
            ActionType.DEVICE_START_SIMULATION,
            ActionType.DEVICE_STOP_SIMULATION,
            ActionType.DEVICE_STANDBY,
            ActionType.DEVICE_SHOW_DEMO_SCREEN,
        }
        immediately_preceded_by_move = bool(
            actions
            and actions[-1].action == ActionType.MOVE_TO
            and actions[-1].target_id == action.target_id
        )
        if needs_approach and not immediately_preceded_by_move:
            actions.append(
                RobotAction(
                    sequence=len(actions) + 1,
                    action=ActionType.MOVE_TO,
                    target_id=action.target_id,
                )
            )
        actions.append(action.model_copy(update={"sequence": len(actions) + 1}))
        if action.action in _DEVICE_ACTIONS.get(action.target_id or "", set()):
            requires_confirmation = True
    if len(actions) > 6:
        raise ValueError("Required approach steps would exceed the six-action plan limit.")
    return plan.model_copy(
        update={"actions": actions, "requires_confirmation": requires_confirmation}
    )


class MockVisionAdapter:
    provider = "mock"
    model_name = "deterministic-mock"

    async def is_available(self) -> bool:
        return True

    async def interpret(
        self,
        instruction: str,
        image_bytes: bytes,
        world_state: WorldState,
        request_id: UUID,
    ) -> ActionPlan:
        del image_bytes
        text = instruction.strip().lower()
        base = {
            "request_id": request_id,
            "confidence": 0.99,
            "requires_confirmation": False,
            "warnings": [],
        }
        if not text:
            return ActionPlan(
                **{**base, "confidence": 0.0},
                status=PlanStatus.NEEDS_CLARIFICATION,
                summary="Enter a command so I can help.",
                actions=[],
            )
        if _UNSAFE_TERMS.search(text):
            return ActionPlan(
                **{
                    **base,
                    "warnings": [
                        "This simulation accepts safe, non-clinical tasks only."
                    ],
                },
                status=PlanStatus.REJECTED,
                summary="Clinical decisions and real-patient operations are not supported.",
                actions=[],
            )
        if text in {"stop", "emergency stop"} or "stop moving" in text:
            return ActionPlan(
                **base,
                status=PlanStatus.READY,
                summary="Stop robot movement.",
                actions=[_action(1, ActionType.STOP)],
            )
        if "return home" in text or "go home" in text:
            return ActionPlan(
                **base,
                status=PlanStatus.READY,
                summary="Return to the home position.",
                actions=[_action(1, ActionType.RETURN_HOME)],
            )

        target = _target_for_instruction(text, world_state)
        if target is None and "drop" in text:
            if not world_state.robot.carrying_object_id:
                return ActionPlan(
                    **base,
                    status=PlanStatus.REJECTED,
                    summary="The robot is not carrying an object.",
                    actions=[],
                )
            return ActionPlan(
                **base,
                status=PlanStatus.READY,
                summary="Drop the carried object at the robot's current position.",
                actions=[_action(1, ActionType.DROP)],
            )
        if target is None:
            return ActionPlan(
                **{**base, "confidence": 0.35},
                status=PlanStatus.NEEDS_CLARIFICATION,
                summary="Please name a visible target, such as the bed, monitor, or supply box.",
                actions=[],
            )
        target_id = target.id
        actions: list[RobotAction] = []

        def append(
            action_type: ActionType,
            action_target: str | None = target_id,
            **params: Any,
        ) -> None:
            actions.append(
                _action(len(actions) + 1, action_type, action_target, **params)
            )

        carrying = world_state.robot.carrying_object_id
        if "pick" in text or "pick up" in text or "pick-up" in text:
            if target.type != ObjectType.PICKABLE:
                return ActionPlan(
                    **base,
                    status=PlanStatus.REJECTED,
                    summary="That target cannot be picked up.",
                    actions=[],
                )
            append(ActionType.MOVE_TO)
            append(ActionType.PICK)
        elif any(word in text for word in ("place", "put", "drop")) and (
            target.type == ObjectType.PLACEMENT_SURFACE or "drop" in text
        ):
            if not carrying:
                return ActionPlan(
                    **base,
                    status=PlanStatus.REJECTED,
                    summary="The robot is not carrying an object.",
                    actions=[],
                )
            append(ActionType.MOVE_TO)
            append(
                ActionType.DROP if "drop" in text else ActionType.PLACE_ON,
                target_id,
                **({} if "drop" in text else {"object_id": carrying}),
            )
        elif target.type == ObjectType.SIMULATED_DEVICE:
            if target_id == "monitor_01":
                device_action = (
                    ActionType.DEVICE_POWER_OFF
                    if "off" in text
                    else ActionType.DEVICE_SHOW_DEMO_SCREEN
                    if "demo" in text
                    else ActionType.DEVICE_POWER_ON
                )
            else:
                device_action = (
                    ActionType.DEVICE_START_SIMULATION
                    if "start" in text or "run" in text
                    else ActionType.DEVICE_STOP_SIMULATION
                    if "stop" in text
                    else ActionType.DEVICE_STANDBY
                )
            append(ActionType.MOVE_TO)
            append(device_action)
            base["requires_confirmation"] = True
        elif "move" in text or "go to" in text or "navigate" in text:
            append(ActionType.MOVE_TO)
        else:
            return ActionPlan(
                **{**base, "confidence": 0.4},
                status=PlanStatus.NEEDS_CLARIFICATION,
                summary="Should I move to the target, pick it up, or control a device?",
                actions=[],
            )
        return ActionPlan(
            **base,
            status=PlanStatus.READY,
            summary=instruction.strip(),
            actions=actions,
        )


class OllamaVisionAdapter:
    provider = "ollama"

    def __init__(
        self, model_name: str = VLM_MODEL, base_url: str = OLLAMA_BASE_URL
    ) -> None:
        self.model_name = model_name
        self._client = ollama.AsyncClient(host=base_url, timeout=120.0)
        self._base_url = base_url.rstrip("/")

    async def is_available(self) -> bool:
        async with httpx.AsyncClient(timeout=2.0, proxy=None) as client:
            response = await client.get(f"{self._base_url}/api/tags")
            response.raise_for_status()
            names = {model["name"] for model in response.json().get("models", [])}
            return self.model_name in names or f"{self.model_name}:latest" in names

    async def interpret(
        self,
        instruction: str,
        image_bytes: bytes,
        world_state: WorldState,
        request_id: UUID,
    ) -> ActionPlan:
        prompt = f"""You operate a safe ICU ROBOT SIMULATION, not a real hospital.
Return exactly one JSON object matching this schema:
{{"request_id":"{request_id}","status":"READY|NEEDS_CLARIFICATION|REJECTED","summary":"short text","confidence":0.0,"requires_confirmation":false,"actions":[{{"sequence":1,"action":"MOVE_TO|STOP|RETURN_HOME|PICK|DROP|PLACE_ON|DEVICE_POWER_ON|DEVICE_POWER_OFF|DEVICE_START_SIMULATION|DEVICE_STOP_SIMULATION|DEVICE_STANDBY|DEVICE_SHOW_DEMO_SCREEN","target_id":"object id or null","parameters":{{}}}}],"warnings":[]}}
Use no more than 6 sequential actions. Use ONLY IDs and object types in this world state:
{world_state.model_dump_json()}
Rules: reject diagnosis, treatment, dosage, medication administration, alarm suppression, real patients, or unsupported commands. Ask for clarification instead of guessing. Never claim actions have completed. Pick/place and every device action must include a MOVE_TO before acting. Device actions always require confirmation. For PLACE_ON, parameters must include object_id for the carried object. Return no markdown or extra fields.
User instruction: {instruction}"""
        try:
            response = await self._client.chat(
                model=self.model_name,
                messages=[{"role": "user", "content": prompt, "images": [image_bytes]}],
                format="json",
                options={"temperature": 0.0},
            )
            raw = response.message.content
            plan = ActionPlan.model_validate_json(raw)
            if plan.request_id != request_id:
                raise ValueError("VLM response request_id does not match the request.")
            return plan
        except (
            ollama.ResponseError,
            ValidationError,
            ValueError,
            json.JSONDecodeError,
        ) as exc:
            raise RuntimeError(
                f"VLM returned an unavailable or invalid action plan: {exc}"
            ) from exc


def validate_plan(plan: ActionPlan, world_state: WorldState) -> ActionPlan:
    if plan.status != PlanStatus.READY:
        if plan.actions:
            raise ValueError("Non-ready plans must not contain executable actions.")
        return plan
    if not plan.actions:
        raise ValueError("Ready plans must contain at least one action.")
    if len(plan.actions) > 6:
        raise ValueError("Plans may contain at most six actions.")
    objects = {obj.id: obj for obj in world_state.objects}
    if len(objects) != len(world_state.objects):
        raise ValueError("World state contains duplicate object IDs.")
    device_states = {device.id for device in world_state.devices}
    if len(device_states) != len(world_state.devices):
        raise ValueError("World state contains duplicate device IDs.")
    carries = world_state.robot.carrying_object_id
    requires_confirmation = plan.requires_confirmation
    for index, action in enumerate(plan.actions, start=1):
        if action.sequence != index:
            raise ValueError("Action sequences must be contiguous and start at one.")
        target = objects.get(action.target_id) if action.target_id is not None else None
        if action.action in {
            ActionType.MOVE_TO,
            ActionType.PICK,
            ActionType.PLACE_ON,
        } and (target is None or not target.visible):
            raise ValueError(
                f"Action target is missing or not visible: {action.target_id}"
            )
        if action.action == ActionType.PICK:
            if target.type != ObjectType.PICKABLE or carries:
                raise ValueError(
                    "Pick requires a visible pickable target and an empty gripper."
                )
            preceding_move = any(
                prior.action == ActionType.MOVE_TO and prior.target_id == target.id
                    for prior in plan.actions[index - 2 : index - 1]
            )
            if not preceding_move:
                raise ValueError("PICK requires a preceding MOVE_TO for that object.")
            carries = target.id
        elif action.action in {ActionType.PLACE_ON, ActionType.DROP}:
            if not carries:
                raise ValueError("Drop/place requires an object to be carried.")
            if (
                action.action == ActionType.PLACE_ON
                and target.type != ObjectType.PLACEMENT_SURFACE
            ):
                raise ValueError("PLACE_ON target must be a placement surface.")
            if action.action == ActionType.PLACE_ON:
                if action.parameters.get("object_id") != carries:
                    raise ValueError(
                        "PLACE_ON object_id must match the carried object."
                    )
                preceding_move = any(
                    prior.action == ActionType.MOVE_TO and prior.target_id == target.id
                    for prior in plan.actions[index - 2 : index - 1]
                )
                if not preceding_move:
                    raise ValueError(
                        "PLACE_ON requires a preceding MOVE_TO for that surface."
                    )
            carries = None
        elif action.action in {
            ActionType.DEVICE_POWER_ON,
            ActionType.DEVICE_POWER_OFF,
            ActionType.DEVICE_SHOW_DEMO_SCREEN,
            ActionType.DEVICE_STANDBY,
            ActionType.DEVICE_START_SIMULATION,
            ActionType.DEVICE_STOP_SIMULATION,
        }:
            if target is None or target.type != ObjectType.SIMULATED_DEVICE:
                raise ValueError("Device action target must be a simulated device.")
            if target.id not in device_states:
                raise ValueError("Device action target is absent from device state.")
            if action.action not in _DEVICE_ACTIONS.get(target.id, set()):
                raise ValueError("Action is not permitted for this device.")
            requires_confirmation = True
            preceding_move = any(
                prior.action == ActionType.MOVE_TO and prior.target_id == target.id
                for prior in plan.actions[index - 2 : index - 1]
            )
            if not preceding_move:
                raise ValueError(
                    "Device action requires a preceding MOVE_TO for that device."
                )
        elif action.action == ActionType.PLACE_ON:
            preceding_move = any(
                prior.action == ActionType.MOVE_TO and prior.target_id == target.id
                for prior in plan.actions[: index - 1]
            )
            if not preceding_move:
                raise ValueError(
                    "PLACE_ON requires a preceding MOVE_TO for that surface."
                )
    if plan.confidence < MIN_CONFIDENCE:
        raise ValueError("Plan confidence is below the execution threshold.")
    return plan.model_copy(update={"requires_confirmation": requires_confirmation})


def create_app(adapter: VisionLanguageModel | None = None) -> FastAPI:
    if adapter is None:
        if VLM_PROVIDER == "mock":
            adapter = MockVisionAdapter()
        elif VLM_PROVIDER == "ollama":
            adapter = OllamaVisionAdapter()
        else:
            raise ValueError(f"Unsupported VLM_PROVIDER: {VLM_PROVIDER}")

    app = FastAPI(title="ICU Assist VLM Gateway", version="1.0.0")
    app.state.vlm = adapter

    @app.get("/api/v1/health", response_model=HealthResponse)
    async def health() -> HealthResponse:
        try:
            available = await app.state.vlm.is_available()
        except (httpx.HTTPError, RuntimeError) as exc:
            raise HTTPException(
                status_code=503, detail=f"VLM health check failed: {exc}"
            ) from exc
        return HealthResponse(
            status="ok" if available else "unavailable",
            model_provider=app.state.vlm.provider,
            model_available=available,
        )

    @app.get("/api/v1/model", response_model=ModelInfo)
    async def model_info() -> ModelInfo:
        try:
            available = await app.state.vlm.is_available()
        except (httpx.HTTPError, RuntimeError) as exc:
            raise HTTPException(
                status_code=503, detail=f"VLM health check failed: {exc}"
            ) from exc
        return ModelInfo(
            model_provider=app.state.vlm.provider,
            model_name=app.state.vlm.model_name,
            model_available=available,
            mock_mode=app.state.vlm.provider == "mock",
        )

    @app.post("/api/v1/commands/interpret", response_model=ActionPlan)
    async def interpret_command(
        instruction: Annotated[str, Form(min_length=1, max_length=1000)],
        world_state: Annotated[str, Form()],
        request_id: Annotated[UUID, Form()],
        image: Annotated[UploadFile, File()],
    ) -> ActionPlan:
        try:
            parsed_world = WorldState.model_validate_json(world_state)
        except ValidationError as exc:
            raise HTTPException(status_code=422, detail=exc.errors()) from exc
        if _UNSAFE_TERMS.search(instruction):
            return ActionPlan(
                request_id=request_id,
                status=PlanStatus.REJECTED,
                summary="Clinical decisions and real-patient operations are not supported.",
                confidence=1.0,
                requires_confirmation=False,
                actions=[],
                warnings=["This simulation accepts safe, non-clinical tasks only."],
            )
        image_bytes = await image.read(MAX_IMAGE_BYTES + 1)
        if len(image_bytes) > MAX_IMAGE_BYTES:
            raise HTTPException(
                status_code=413, detail="Image exceeds the configured size limit."
            )
        if not image_bytes:
            raise HTTPException(status_code=400, detail="Image must not be empty.")
        try:
            with Image.open(io.BytesIO(image_bytes)) as decoded:
                if decoded.width > 4096 or decoded.height > 4096:
                    raise HTTPException(
                        status_code=413, detail="Image dimensions exceed 4096x4096."
                    )
                decoded.verify()
        except (UnidentifiedImageError, OSError) as exc:
            raise HTTPException(
                status_code=400, detail="Uploaded file is not a valid image."
            ) from exc
        try:
            plan = await app.state.vlm.interpret(
                instruction.strip(), image_bytes, parsed_world, request_id
            )
            if plan.request_id != request_id:
                raise ValueError("VLM response request_id does not match the request.")
            plan = add_required_approach_actions(plan)
            if plan.status == PlanStatus.READY and _COMPLETION_CLAIM.search(plan.summary):
                plan = plan.model_copy(update={"summary": f"Proposed plan: {instruction.strip()}"})
            return validate_plan(plan, parsed_world)
        except (httpx.HTTPError, ollama.ResponseError) as exc:
            raise HTTPException(
                status_code=503, detail=f"VLM request failed: {exc}"
            ) from exc
        except (ValueError, RuntimeError, ValidationError) as exc:
            raise HTTPException(
                status_code=502, detail=f"VLM returned an invalid plan: {exc}"
            ) from exc

    return app


app = create_app()
