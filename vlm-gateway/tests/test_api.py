from __future__ import annotations

import io
from uuid import uuid4

from fastapi.testclient import TestClient
from PIL import Image

from app.main import (
    ActionPlan,
    ActionType,
    MockVisionAdapter,
    PlanStatus,
    RobotAction,
    WorldState,
    add_required_approach_actions,
    create_app,
    validate_plan,
)

WORLD = WorldState(
    scene_id="icu_room_01",
    robot={"id": "icu_robot_01", "status": "IDLE", "carrying_object_id": None},
    objects=[
        {"id": "bed_01", "type": "LANDMARK", "visible": True},
        {"id": "med_platform_01", "type": "PLACEMENT_SURFACE", "visible": True},
        {"id": "infusion_01", "type": "SIMULATED_DEVICE", "visible": True},
        {"id": "monitor_01", "type": "SIMULATED_DEVICE", "visible": True},
        {"id": "supply_box_red", "type": "PICKABLE", "visible": True},
        {"id": "supply_box_blue", "type": "PICKABLE", "visible": True},
    ],
    devices=[
        {"id": "monitor_01", "state": "OFF"},
        {"id": "infusion_01", "state": "STANDBY"},
    ],
)


def png_bytes() -> bytes:
    image = Image.new("RGB", (32, 32), color="white")
    output = io.BytesIO()
    image.save(output, format="PNG")
    return output.getvalue()


def client() -> TestClient:
    return TestClient(create_app(MockVisionAdapter()))


def request(
    api: TestClient,
    instruction: str = "Move to the bed.",
    world_state: WorldState = WORLD,
    image: bytes | None = None,
) -> object:
    return api.post(
        "/api/v1/commands/interpret",
        data={
            "instruction": instruction,
            "world_state": world_state.model_dump_json(),
            "request_id": str(uuid4()),
        },
        files={
            "image": (
                "scene.png",
                png_bytes() if image is None else image,
                "image/png",
            )
        },
    )


def test_health_and_model_info() -> None:
    api = client()
    health = api.get("/api/v1/health")
    assert health.status_code == 200
    assert health.json() == {
        "status": "ok",
        "model_provider": "mock",
        "model_available": True,
    }
    model = api.get("/api/v1/model")
    assert model.status_code == 200
    assert model.json()["mock_mode"] is True


def test_move_command_returns_known_target() -> None:
    response = request(client())
    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "READY"
    assert body["actions"] == [
        {
            "sequence": 1,
            "action": "MOVE_TO",
            "target_id": "bed_01",
            "parameters": {},
        }
    ]


def test_pick_and_place_generates_valid_sequential_plan() -> None:
    api = client()
    pick = request(api, "Pick up the red supply box.")
    assert pick.status_code == 200, pick.text
    assert [item["action"] for item in pick.json()["actions"]] == ["MOVE_TO", "PICK"]

    carrying = WORLD.model_copy(
        update={
            "robot": WORLD.robot.model_copy(
                update={"carrying_object_id": "supply_box_red"}
            )
        }
    )
    place = request(api, "Place it on the medical platform.", carrying)
    assert place.status_code == 200, place.text
    assert [item["action"] for item in place.json()["actions"]] == [
        "MOVE_TO",
        "PLACE_ON",
    ]
    assert place.json()["actions"][1]["parameters"] == {"object_id": "supply_box_red"}


def test_drop_at_current_location_uses_no_target() -> None:
    carrying = WORLD.model_copy(
        update={
            "robot": WORLD.robot.model_copy(
                update={"carrying_object_id": "supply_box_red"}
            )
        }
    )
    response = request(client(), "Drop the object here.", carrying)
    assert response.status_code == 200, response.text
    assert response.json()["actions"][0]["action"] == "DROP"
    assert response.json()["actions"][0]["target_id"] is None


def test_approach_steps_are_inserted_before_interactions() -> None:
    plan = ActionPlan(
        request_id=uuid4(),
        status=PlanStatus.READY,
        summary="Pick the red box.",
        confidence=0.9,
        requires_confirmation=False,
        actions=[
            RobotAction(
                sequence=1,
                action=ActionType.PICK,
                target_id="supply_box_red",
            ),
            RobotAction(
                sequence=2,
                action=ActionType.MOVE_TO,
                target_id="med_platform_01",
            ),
            RobotAction(
                sequence=3,
                action=ActionType.PLACE_ON,
                target_id="med_platform_01",
                parameters={"object_id": "supply_box_red"},
            ),
        ],
    )

    normalized = add_required_approach_actions(plan)

    assert [action.action for action in normalized.actions] == [
        ActionType.MOVE_TO,
        ActionType.PICK,
        ActionType.MOVE_TO,
        ActionType.PLACE_ON,
    ]
    assert [action.sequence for action in normalized.actions] == [1, 2, 3, 4]


def test_device_action_requires_confirmation() -> None:
    response = request(client(), "Switch on the monitor.")
    assert response.status_code == 200, response.text
    assert response.json()["requires_confirmation"] is True
    assert [action["action"] for action in response.json()["actions"]] == [
        "MOVE_TO",
        "DEVICE_POWER_ON",
    ]


def test_unsafe_and_ambiguous_requests_do_not_execute() -> None:
    unsafe = request(client(), "Give the patient 5 mg of medicine.")
    assert unsafe.status_code == 200
    assert unsafe.json()["status"] == "REJECTED"
    assert unsafe.json()["actions"] == []

    ambiguous = request(client(), "Go there.")
    assert ambiguous.status_code == 200
    assert ambiguous.json()["status"] == "NEEDS_CLARIFICATION"
    assert ambiguous.json()["actions"] == []


def test_invalid_world_state_and_images_are_rejected() -> None:
    api = client()
    bad_world = api.post(
        "/api/v1/commands/interpret",
        data={
            "instruction": "Move to bed",
            "world_state": "{}",
            "request_id": str(uuid4()),
        },
        files={"image": ("scene.png", png_bytes(), "image/png")},
    )
    assert bad_world.status_code == 422
    invalid_image = request(api, image=b"not an image")
    assert invalid_image.status_code == 400
    empty_image = request(api, image=b"")
    assert empty_image.status_code == 400


def test_validator_rejects_unknown_target_and_unsafe_device_transition() -> None:
    unknown = ActionPlan(
        request_id=uuid4(),
        status=PlanStatus.READY,
        summary="Go somewhere.",
        confidence=1.0,
        requires_confirmation=False,
        actions=[
            RobotAction(sequence=1, action=ActionType.MOVE_TO, target_id="unknown")
        ],
    )
    try:
        validate_plan(unknown, WORLD)
    except ValueError as exc:
        assert "target" in str(exc)
    else:
        raise AssertionError("Unknown target should be rejected.")

    invalid_device = ActionPlan(
        request_id=uuid4(),
        status=PlanStatus.READY,
        summary="Turn on the infusion device.",
        confidence=1.0,
        requires_confirmation=True,
        actions=[
            RobotAction(sequence=1, action=ActionType.MOVE_TO, target_id="infusion_01"),
            RobotAction(
                sequence=2,
                action=ActionType.DEVICE_POWER_ON,
                target_id="infusion_01",
            ),
        ],
    )
    try:
        validate_plan(invalid_device, WORLD)
    except ValueError as exc:
        assert "permitted" in str(exc)
    else:
        raise AssertionError("Unsupported device action should be rejected.")
