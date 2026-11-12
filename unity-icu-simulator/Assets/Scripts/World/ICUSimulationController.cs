using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public sealed class ICUSimulationController : MonoBehaviour
{
    private const float InteractionDistance = 1.8f;
    private const float NavigationTimeout = 60f;
    private readonly Dictionary<string, WorldObjectId> worldObjects = new Dictionary<string, WorldObjectId>();
    private readonly Dictionary<string, string> deviceStates = new Dictionary<string, string>();
    private readonly List<string> eventLog = new List<string>();

    private VlmApiClient apiClient;
    private NavMeshAgent agent;
    private Camera robotCamera;
    private DemoCameraFollow demoCameraFollow;
    private Transform carrySocket;
    private Rigidbody carriedBody;
    private WorldObjectId carriedObject;
    private ActionPlanDto pendingPlan;
    private Coroutine execution;
    private string command = "Move to the monitor.";
    private string status = "Building ICU simulation";
    private string currentAction = "Idle";
    private string lastPlan = "";
    private string apiStatus = "Checking VLM gateway...";
    private bool isStopped;
    private Vector2 logScroll;
    private GUIStyle headerStyle;
    private GUIStyle bodyStyle;
    private GUIStyle smallStyle;

    private void Start()
    {
        CreateEnvironment();
        BakeNavigation();
        CreateRobot();
        if (agent != null && !agent.isOnNavMesh)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(agent.transform.position, out hit, 4f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
        }
        apiClient = gameObject.AddComponent<VlmApiClient>();
        StartCoroutine(apiClient.CheckHealth(result => apiStatus = result));
        SetStatus(agent != null && agent.isOnNavMesh
            ? "Ready — enter an instruction and request a plan."
            : "Scene ready; navigation mesh is unavailable. Reopen the scene after package resolution.");
    }

    private void CreateEnvironment()
    {
        RenderSettings.ambientLight = new Color(0.56f, 0.62f, 0.7f);
        RenderSettings.skybox = null;
        CreatePrimitive("ICU Room Floor", PrimitiveType.Cube, new Vector3(0f, -0.2f, 0f),
            new Vector3(24f, 0.4f, 18f), new Color(0.75f, 0.79f, 0.82f), true);

        CreateWall("North Wall", new Vector3(0f, 2f, 9f), new Vector3(24f, 4f, 0.25f));
        CreateWall("West Wall", new Vector3(-12f, 2f, 0f), new Vector3(0.25f, 4f, 18f));
        CreateWall("East Wall", new Vector3(12f, 2f, 0f), new Vector3(0.25f, 4f, 18f));

        WorldObjectId bed = CreateTarget(
            "Patient Bed", "bed_01", "LANDMARK", PrimitiveType.Cube,
            new Vector3(-5f, 0.65f, 2f), new Vector3(3.6f, 1.25f, 2.1f),
            new Color(0.69f, 0.79f, 0.86f));
        AddChildBlock(bed.transform, "Mattress", new Vector3(0f, 0.14f, 0f),
            new Vector3(0.96f, 0.12f, 0.9f), Color.white);
        AddChildBlock(bed.transform, "Headboard", new Vector3(0f, 0.58f, 0.47f),
            new Vector3(1f, 0.28f, 0.06f), new Color(0.34f, 0.48f, 0.58f));

        WorldObjectId platform = CreateTarget(
            "Medical Platform", "med_platform_01", "PLACEMENT_SURFACE", PrimitiveType.Cube,
            new Vector3(5f, 0.9f, 3.5f), new Vector3(2.1f, 0.24f, 1.4f),
            new Color(0.25f, 0.53f, 0.63f));
        AddChildBlock(platform.transform, "Platform Pedestal", new Vector3(0f, -0.46f, 0f),
            new Vector3(0.25f, 0.82f, 0.25f), new Color(0.33f, 0.39f, 0.43f));

        WorldObjectId monitor = CreateTarget(
            "Patient Monitor", "monitor_01", "SIMULATED_DEVICE", PrimitiveType.Cube,
            new Vector3(-8f, 1.8f, -5f), new Vector3(1.3f, 1.1f, 0.35f),
            new Color(0.08f, 0.13f, 0.17f));
        AddChildBlock(monitor.transform, "Monitor Screen", new Vector3(0f, 0.08f, -0.51f),
            new Vector3(0.82f, 0.62f, 0.04f), new Color(0.08f, 0.3f, 0.34f));
        deviceStates["monitor_01"] = "OFF";

        WorldObjectId infusion = CreateTarget(
            "Infusion Device", "infusion_01", "SIMULATED_DEVICE", PrimitiveType.Cube,
            new Vector3(8f, 1.25f, -4.5f), new Vector3(0.7f, 1.1f, 0.55f),
            new Color(0.85f, 0.88f, 0.88f));
        AddChildBlock(infusion.transform, "Infusion Display", new Vector3(0f, 0.3f, -0.51f),
            new Vector3(0.48f, 0.22f, 0.035f), new Color(0.15f, 0.43f, 0.35f));
        deviceStates["infusion_01"] = "STANDBY";

        CreateTarget(
            "Red Supply Box", "supply_box_red", "PICKABLE", PrimitiveType.Cube,
            new Vector3(-2f, 0.42f, -2f), new Vector3(0.85f, 0.8f, 0.85f),
            new Color(0.79f, 0.19f, 0.18f));
        CreateTarget(
            "Blue Supply Box", "supply_box_blue", "PICKABLE", PrimitiveType.Cube,
            new Vector3(2f, 0.42f, -4f), new Vector3(0.85f, 0.8f, 0.85f),
            new Color(0.15f, 0.34f, 0.8f));

        CreateLight("ICU Key Light", new Vector3(-3f, 7f, -1f), 1.3f, 100f);
        CreateLight("ICU Fill Light", new Vector3(6f, 6f, 5f), 0.8f, 85f);
        CreateDemoCamera();
    }

    private void CreateRobot()
    {
        GameObject root = new GameObject("ICU Robot");
        root.transform.position = new Vector3(0f, 0.03f, -7f);
        root.AddComponent<BoxCollider>().size = new Vector3(0.9f, 0.18f, 0.9f);
        WorldObjectId identity = root.AddComponent<WorldObjectId>();
        identity.Id = "icu_robot_01";
        identity.ObjectType = "ROBOT";

        AddChildBlock(root.transform, "Robot Base", new Vector3(0f, 0.16f, 0f),
            new Vector3(0.88f, 0.28f, 0.88f), new Color(0.16f, 0.23f, 0.28f));
        AddChildBlock(root.transform, "Robot Body", new Vector3(0f, 0.72f, 0f),
            new Vector3(0.56f, 0.9f, 0.52f), new Color(0.2f, 0.57f, 0.65f));
        AddChildBlock(root.transform, "Robot Head", new Vector3(0f, 1.28f, 0.04f),
            new Vector3(0.42f, 0.28f, 0.43f), new Color(0.1f, 0.19f, 0.23f));
        AddChildBlock(root.transform, "Robot Status Light", new Vector3(0f, 1.48f, 0.04f),
            new Vector3(0.13f, 0.08f, 0.13f), Color.green);

        carrySocket = new GameObject("Carry Socket").transform;
        carrySocket.SetParent(root.transform, false);
        carrySocket.localPosition = new Vector3(0f, 0.55f, 0.72f);

        GameObject cameraObject = new GameObject("Robot Camera");
        cameraObject.transform.SetParent(root.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.32f, 0.42f);
        cameraObject.transform.localRotation = Quaternion.Euler(9f, 0f, 0f);
        robotCamera = cameraObject.AddComponent<Camera>();
        robotCamera.fieldOfView = 76f;
        robotCamera.enabled = false;

        agent = root.AddComponent<NavMeshAgent>();
        agent.speed = 2.7f;
        agent.acceleration = 9f;
        agent.angularSpeed = 240f;
        agent.stoppingDistance = 0.35f;
        agent.radius = 0.38f;
        agent.height = 1.55f;

        SphereCollider proximity = root.AddComponent<SphereCollider>();
        proximity.isTrigger = true;
        proximity.radius = InteractionDistance;

        demoCameraFollow.Target = root.transform;
    }

    private void BakeNavigation()
    {
        GameObject surfaceObject = new GameObject("Runtime NavMesh Surface");
        NavMeshSurface surface = surfaceObject.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = ~0;
        surface.BuildNavMesh();
    }

    private WorldObjectId CreateTarget(
        string displayName, string id, string type, PrimitiveType primitive,
        Vector3 position, Vector3 scale, Color color)
    {
        GameObject target = GameObject.CreatePrimitive(primitive);
        target.name = displayName;
        target.transform.position = position;
        target.transform.localScale = scale;
        SetColor(target, color);

        WorldObjectId identity = target.AddComponent<WorldObjectId>();
        identity.Id = id;
        identity.ObjectType = type;
        identity.gameObject.AddComponent<FloatingLabel>().Text = displayName;
        worldObjects.Add(id, identity);

        if (type == "PICKABLE")
        {
            Rigidbody body = target.AddComponent<Rigidbody>();
            body.mass = 1f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }
        return identity;
    }

    private GameObject CreatePrimitive(
        string objectName, PrimitiveType primitive, Vector3 position,
        Vector3 scale, Color color, bool collider)
    {
        GameObject result = GameObject.CreatePrimitive(primitive);
        result.name = objectName;
        result.transform.position = position;
        result.transform.localScale = scale;
        SetColor(result, color);
        if (!collider)
        {
            Collider shape = result.GetComponent<Collider>();
            if (shape != null)
            {
                Destroy(shape);
            }
        }
        return result;
    }

    private void CreateWall(string wallName, Vector3 position, Vector3 scale)
    {
        CreatePrimitive(wallName, PrimitiveType.Cube, position, scale,
            new Color(0.82f, 0.86f, 0.89f), true);
    }

    private void CreateLight(string lightName, Vector3 position, float intensity, float range)
    {
        GameObject lightObject = new GameObject(lightName);
        lightObject.transform.position = position;
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.intensity = intensity;
        light.range = range;
    }

    private void CreateDemoCamera()
    {
        GameObject cameraObject = new GameObject("Demo Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 16f, -18f);
        cameraObject.transform.rotation = Quaternion.Euler(38f, 0f, 0f);
        cameraObject.AddComponent<Camera>();
        cameraObject.AddComponent<AudioListener>();
        demoCameraFollow = cameraObject.AddComponent<DemoCameraFollow>();
    }

    private static void AddChildBlock(
        Transform parent, string childName, Vector3 localPosition, Vector3 localScale, Color color)
    {
        GameObject child = GameObject.CreatePrimitive(PrimitiveType.Cube);
        child.name = childName;
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPosition;
        child.transform.localScale = localScale;
        SetColor(child, color);
    }

    private static void SetColor(GameObject target, Color color)
    {
        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = color;
        }
    }

    private void RequestPlan()
    {
        if (execution != null)
        {
            SetStatus("A plan is already executing. Cancel it before requesting another.");
            return;
        }
        if (string.IsNullOrWhiteSpace(command))
        {
            SetStatus("Enter a natural-language command first.");
            return;
        }
        if (robotCamera == null || apiClient == null)
        {
            SetStatus("Robot camera or VLM gateway client is unavailable.");
            return;
        }

        pendingPlan = null;
        SetStatus("Capturing robot-camera image and requesting an action plan...");
        StartCoroutine(CaptureAndRequestPlan());
    }

    private IEnumerator CaptureAndRequestPlan()
    {
        yield return new WaitForEndOfFrame();
        RenderTexture renderTarget = RenderTexture.GetTemporary(640, 360, 16);
        RenderTexture previous = robotCamera.targetTexture;
        robotCamera.targetTexture = renderTarget;
        robotCamera.Render();
        RenderTexture.active = renderTarget;
        Texture2D screenshot = new Texture2D(640, 360, TextureFormat.RGB24, false);
        screenshot.ReadPixels(new Rect(0f, 0f, 640f, 360f), 0, 0);
        screenshot.Apply();
        byte[] imageBytes = screenshot.EncodeToPNG();
        RenderTexture.active = null;
        robotCamera.targetTexture = previous;
        RenderTexture.ReleaseTemporary(renderTarget);
        Destroy(screenshot);

        apiClient.Interpret(command, imageBytes, BuildWorldStateJson(), (plan, error) =>
        {
            if (!string.IsNullOrEmpty(error))
            {
                SetStatus(error);
                AddLog(error);
                return;
            }
            pendingPlan = plan;
            lastPlan = plan.summary + "\nStatus: " + plan.status
                + " | Confidence: " + plan.confidence.ToString("0.00")
                + " | Confirmation: " + (plan.requires_confirmation ? "required" : "user review");
            foreach (RobotActionDto action in plan.actions)
            {
                lastPlan += "\n" + action.sequence + ". " + action.action
                    + (string.IsNullOrEmpty(action.target_id) ? "" : " " + action.target_id);
            }
            AddLog("Plan received: " + plan.status + " — " + plan.summary);
            SetStatus(plan.status == "READY"
                ? "Plan ready. Review it and press Confirm & Execute."
                : plan.summary);
        });
    }

    private string BuildWorldStateJson()
    {
        StringBuilder json = new StringBuilder(1024);
        json.Append("{\"scene_id\":\"icu_room_01\",\"robot\":{\"id\":\"icu_robot_01\",");
        json.Append("\"status\":\"").Append(isStopped ? "STOPPED" : status.ToUpperInvariant()).Append("\",");
        json.Append("\"carrying_object_id\":");
        json.Append(carriedObject == null ? "null" : "\"" + carriedObject.Id + "\"");
        json.Append("},\"objects\":[");
        bool first = true;
        foreach (KeyValuePair<string, WorldObjectId> entry in worldObjects)
        {
            if (!first) json.Append(',');
            first = false;
            json.Append("{\"id\":\"").Append(entry.Key).Append("\",\"type\":\"")
                .Append(entry.Value.ObjectType).Append("\",\"visible\":")
                .Append(entry.Value.IsVisible ? "true" : "false").Append('}');
        }
        json.Append("],\"devices\":[");
        first = true;
        foreach (KeyValuePair<string, string> device in deviceStates)
        {
            if (!first) json.Append(',');
            first = false;
            json.Append("{\"id\":\"").Append(device.Key).Append("\",\"state\":\"")
                .Append(device.Value).Append("\"}");
        }
        json.Append("]}");
        return json.ToString();
    }

    private void ExecutePlan()
    {
        if (pendingPlan == null || pendingPlan.status != "READY")
        {
            SetStatus("There is no executable READY plan to confirm.");
            return;
        }
        if (pendingPlan.actions == null || pendingPlan.actions.Length == 0
            || pendingPlan.actions.Length > 6)
        {
            SetStatus("Plan rejected: action count is invalid.");
            pendingPlan = null;
            return;
        }
        if (pendingPlan.confidence < 0.55f)
        {
            SetStatus("Plan rejected: confidence is below the execution threshold.");
            pendingPlan = null;
            return;
        }
        if (isStopped)
        {
            SetStatus("Reset the emergency stop before executing a new plan.");
            return;
        }
        execution = StartCoroutine(ExecuteActions(pendingPlan));
    }

    private IEnumerator ExecuteActions(ActionPlanDto plan)
    {
        for (int index = 0; index < plan.actions.Length; index++)
        {
            if (isStopped)
            {
                yield break;
            }
            RobotActionDto action = plan.actions[index];
            string reason = action.sequence != index + 1 ? "sequence number is invalid." : "";
            if (action.sequence != index + 1 || !ValidateAction(action, out reason))
            {
                FailPlan("Action validation failed: " + reason);
                yield break;
            }

            currentAction = action.action + (string.IsNullOrEmpty(action.target_id)
                ? "" : " " + action.target_id);
            AddLog("Started " + currentAction);
            bool success = false;
            switch (action.action)
            {
                case "MOVE_TO":
                    yield return MoveTo(action.target_id, result => success = result);
                    break;
                case "RETURN_HOME":
                    yield return MoveToPosition(new Vector3(0f, agent.transform.position.y, -7f),
                        result => success = result);
                    break;
                case "PICK":
                    success = PickObject(action.target_id);
                    break;
                case "DROP":
                    success = DropObject(null);
                    break;
                case "PLACE_ON":
                    success = PlaceObject(action.target_id, action.parameters == null
                        ? null : action.parameters.object_id);
                    break;
                case "STOP":
                    agent.ResetPath();
                    success = true;
                    break;
                case "DEVICE_POWER_ON":
                case "DEVICE_POWER_OFF":
                case "DEVICE_START_SIMULATION":
                case "DEVICE_STOP_SIMULATION":
                case "DEVICE_STANDBY":
                case "DEVICE_SHOW_DEMO_SCREEN":
                    success = ApplyDeviceAction(action.action, action.target_id);
                    break;
                default:
                    FailPlan("Unsupported action: " + action.action);
                    yield break;
            }

            if (!success)
            {
                FailPlan("Action failed: " + currentAction);
                yield break;
            }
            AddLog("Completed " + currentAction);
        }

        currentAction = "Idle";
        execution = null;
        pendingPlan = null;
        SetStatus("Plan completed.");
    }

    private bool ValidateAction(RobotActionDto action, out string reason)
    {
        reason = "";
        string[] knownActions =
        {
            "MOVE_TO", "STOP", "RETURN_HOME", "PICK", "DROP", "PLACE_ON",
            "DEVICE_POWER_ON", "DEVICE_POWER_OFF", "DEVICE_START_SIMULATION",
            "DEVICE_STOP_SIMULATION", "DEVICE_STANDBY", "DEVICE_SHOW_DEMO_SCREEN"
        };
        if (Array.IndexOf(knownActions, action.action) < 0)
        {
            reason = "unsupported action.";
            return false;
        }
        if (action.action == "STOP" || action.action == "RETURN_HOME")
        {
            return true;
        }
        WorldObjectId target = null;
        if (action.action != "DROP"
            && (string.IsNullOrEmpty(action.target_id)
                || !worldObjects.TryGetValue(action.target_id, out target)
                || !target.IsVisible))
        {
            reason = "target is unknown or not visible.";
            return false;
        }
        if (action.action == "PICK" && (target.ObjectType != "PICKABLE" || carriedObject != null
            || !planContainsMoveTo(action, action.target_id)))
        {
            reason = "pick requires an empty gripper and a pickable target.";
            return false;
        }
        if (action.action == "PLACE_ON"
            && (target.ObjectType != "PLACEMENT_SURFACE" || carriedObject == null
                || action.parameters == null || action.parameters.object_id != carriedObject.Id
                || !planContainsMoveTo(action, action.target_id)))
        {
            reason = "place target or carried-object ID is invalid.";
            return false;
        }
        if (action.action == "DROP" && carriedObject == null)
        {
            reason = "the robot is not carrying an object.";
            return false;
        }
        if (action.action.StartsWith("DEVICE_", StringComparison.Ordinal))
        {
            if (target.ObjectType != "SIMULATED_DEVICE" || !deviceStates.ContainsKey(action.target_id))
            {
                reason = "device target is not a registered simulated device.";
                return false;
            }
            if (action.target_id == "monitor_01"
                && action.action != "DEVICE_POWER_ON" && action.action != "DEVICE_POWER_OFF"
                && action.action != "DEVICE_SHOW_DEMO_SCREEN")
            {
                reason = "action is not allowed for the monitor.";
                return false;
            }
            if (action.target_id == "infusion_01"
                && action.action != "DEVICE_STANDBY" && action.action != "DEVICE_START_SIMULATION"
                && action.action != "DEVICE_STOP_SIMULATION")
            {
                reason = "action is not allowed for the infusion simulation.";
                return false;
            }
            if (!planContainsMoveTo(action, action.target_id))
            {
                reason = "device action requires a preceding move to that device.";
                return false;
            }
        }
        return true;
    }

    private bool planContainsMoveTo(RobotActionDto currentAction, string targetId)
    {
        if (pendingPlan == null || pendingPlan.actions == null)
        {
            return false;
        }
        int currentIndex = Array.IndexOf(pendingPlan.actions, currentAction);
        for (int index = 0; index < currentIndex; index++)
        {
            RobotActionDto item = pendingPlan.actions[index];
            if (item.action == "MOVE_TO" && item.target_id == targetId)
            {
                return true;
            }
        }
        return false;
    }

    private IEnumerator MoveTo(string targetId, Action<bool> completed)
    {
        if (!worldObjects.TryGetValue(targetId, out WorldObjectId target))
        {
            completed(false);
            yield break;
        }
        Collider targetCollider = target.GetComponent<Collider>();
        if (targetCollider == null)
        {
            SetStatus("Target has no interaction collider: " + target.name);
            completed(false);
            yield break;
        }
        float approachRadius = Mathf.Max(
            targetCollider.bounds.extents.x,
            targetCollider.bounds.extents.z) + 0.65f;
        Vector3 approachPoint = target.transform.position;
        bool foundPath = false;
        float bestDistance = float.MaxValue;
        for (int index = 0; index < 8; index++)
        {
            float angle = index * Mathf.PI / 4f;
            Vector3 candidate = target.transform.position
                + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * approachRadius;
            candidate.y = agent.transform.position.y;
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(candidate, out hit, 0.65f, NavMesh.AllAreas)
                || Mathf.Abs(hit.position.y - agent.transform.position.y) > 0.6f)
            {
                continue;
            }
            NavMeshPath candidatePath = new NavMeshPath();
            if (!agent.CalculatePath(hit.position, candidatePath)
                || candidatePath.status != NavMeshPathStatus.PathComplete)
            {
                continue;
            }
            float distance = Vector3.Distance(agent.transform.position, hit.position);
            if (distance < bestDistance)
            {
                approachPoint = hit.position;
                bestDistance = distance;
                foundPath = true;
            }
        }
        if (!foundPath)
        {
            SetStatus("No reachable interaction point near " + target.name + ".");
            completed(false);
            yield break;
        }
        yield return MoveToPosition(approachPoint, completed);
    }

    private IEnumerator MoveToPosition(Vector3 destination, Action<bool> completed)
    {
        if (agent == null || !agent.isOnNavMesh || isStopped)
        {
            SetStatus("Navigation is unavailable or the emergency stop is active.");
            completed(false);
            yield break;
        }
        NavMeshPath path = new NavMeshPath();
        if (!agent.CalculatePath(destination, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            SetStatus("Target is unreachable on the baked NavMesh.");
            completed(false);
            yield break;
        }
        if (!agent.SetDestination(destination))
        {
            completed(false);
            yield break;
        }
        SetStatus("Robot navigating...");
        float elapsed = 0f;
        while (!isStopped && elapsed < NavigationTimeout)
        {
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f)
            {
                agent.ResetPath();
                SetStatus("Target reached.");
                completed(true);
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
        agent.ResetPath();
        SetStatus(isStopped ? "Emergency stop engaged." : "Navigation timed out.");
        completed(false);
    }

    private bool PickObject(string objectId)
    {
        if (carriedObject != null || !worldObjects.TryGetValue(objectId, out WorldObjectId target)
            || target.ObjectType != "PICKABLE"
            || DistanceToInteractionSurface(target) > InteractionDistance)
        {
            SetStatus("Pick rejected: empty gripper, valid payload, and proximity are required.");
            return false;
        }
        carriedObject = target;
        carriedBody = target.GetComponent<Rigidbody>();
        if (carriedBody != null)
        {
            carriedBody.isKinematic = true;
            carriedBody.detectCollisions = false;
        }
        target.transform.SetParent(carrySocket, true);
        target.transform.localPosition = Vector3.zero;
        target.transform.localRotation = Quaternion.identity;
        return true;
    }

    private bool PlaceObject(string surfaceId, string objectId)
    {
        if (carriedObject == null || carriedObject.Id != objectId
            || !worldObjects.TryGetValue(surfaceId, out WorldObjectId surface)
            || surface.ObjectType != "PLACEMENT_SURFACE"
            || DistanceToInteractionSurface(surface) > InteractionDistance)
        {
            SetStatus("Place rejected: target, carried object, or interaction distance is invalid.");
            return false;
        }
        Vector3 placement = surface.transform.position + Vector3.up * 0.7f;
        return ReleaseCarriedObject(placement);
    }

    private bool DropObject(string unused)
    {
        if (carriedObject == null)
        {
            return false;
        }
        Vector3 dropPosition = agent.transform.position + agent.transform.forward * 0.8f;
        dropPosition.y = 0.45f;
        return ReleaseCarriedObject(dropPosition);
    }

    private bool ReleaseCarriedObject(Vector3 position)
    {
        carriedObject.transform.SetParent(null, true);
        carriedObject.transform.position = position;
        if (carriedBody != null)
        {
            carriedBody.position = position;
            carriedBody.rotation = carriedObject.transform.rotation;
            carriedBody.detectCollisions = true;
            carriedBody.isKinematic = false;
            carriedBody.linearVelocity = Vector3.zero;
            carriedBody.angularVelocity = Vector3.zero;
        }
        carriedObject = null;
        carriedBody = null;
        return true;
    }

    private bool ApplyDeviceAction(string action, string deviceId)
    {
        if (!worldObjects.TryGetValue(deviceId, out WorldObjectId device)
            || DistanceToInteractionSurface(device) > InteractionDistance)
        {
            SetStatus("Device action rejected: robot is not within interaction distance.");
            return false;
        }

        string current = deviceStates[deviceId];
        string next;
        if (deviceId == "monitor_01")
        {
            if (action == "DEVICE_POWER_ON" && current == "OFF") next = "ON";
            else if (action == "DEVICE_POWER_OFF" && current != "OFF") next = "OFF";
            else if (action == "DEVICE_SHOW_DEMO_SCREEN" && current == "ON") next = "DEMO_SCREEN";
            else return InvalidDeviceTransition(deviceId, action, current);
        }
        else
        {
            if (action == "DEVICE_STANDBY" && current != "OFF") next = "STANDBY";
            else if (action == "DEVICE_START_SIMULATION" && current == "STANDBY") next = "RUNNING_SIMULATION";
            else if (action == "DEVICE_STOP_SIMULATION" && current == "RUNNING_SIMULATION") next = "STANDBY";
            else return InvalidDeviceTransition(deviceId, action, current);
        }

        deviceStates[deviceId] = next;
        Renderer renderer = device.GetComponent<Renderer>();
        if (renderer != null && deviceId == "monitor_01")
        {
            renderer.material.color = next == "OFF"
                ? new Color(0.08f, 0.13f, 0.17f)
                : new Color(0.1f, 0.65f, 0.4f);
        }
        AddLog(deviceId + " state changed: " + current + " -> " + next);
        return true;
    }

    private bool InvalidDeviceTransition(string deviceId, string action, string current)
    {
        SetStatus("Invalid simulated device transition: " + deviceId + " is " + current
            + "; action " + action + " was rejected.");
        return false;
    }

    private float DistanceToInteractionSurface(WorldObjectId target)
    {
        Collider targetCollider = target.GetComponent<Collider>();
        return targetCollider == null
            ? Vector3.Distance(agent.transform.position, target.transform.position)
            : Vector3.Distance(
                agent.transform.position,
                targetCollider.ClosestPoint(agent.transform.position));
    }

    private void FailPlan(string reason)
    {
        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
        currentAction = "Failed";
        execution = null;
        pendingPlan = null;
        SetStatus(reason + " Remaining actions were cancelled.");
        AddLog(reason);
    }

    private void CancelPlan()
    {
        if (execution != null)
        {
            StopCoroutine(execution);
            execution = null;
        }
        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
        pendingPlan = null;
        currentAction = "Cancelled";
        SetStatus("Plan cancelled.");
        AddLog("Plan cancelled by user.");
    }

    private void EmergencyStop()
    {
        isStopped = true;
        if (execution != null)
        {
            StopCoroutine(execution);
            execution = null;
        }
        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
        pendingPlan = null;
        currentAction = "Emergency stop";
        SetStatus("EMERGENCY STOP — reset the simulation before further actions.");
        AddLog("Emergency stop engaged; carried object remains attached.");
    }

    private void ResetStop()
    {
        isStopped = false;
        currentAction = "Idle";
        SetStatus("Emergency stop reset. Enter a new command.");
        AddLog("Emergency stop reset by user.");
    }

    private void SetStatus(string message)
    {
        status = message;
    }

    private void AddLog(string message)
    {
        Debug.Log("[ICU Assist] " + message);
        eventLog.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + message);
        if (eventLog.Count > 100) eventLog.RemoveAt(eventLog.Count - 1);
    }

    private void OnGUI()
    {
        EnsureStyles();
        float panelWidth = Mathf.Min(430f, Screen.width - 24f);
        GUI.Box(new Rect(12f, 12f, panelWidth, Mathf.Min(575f, Screen.height - 24f)), GUIContent.none);
        GUILayout.BeginArea(new Rect(24f, 20f, panelWidth - 24f, Mathf.Min(560f, Screen.height - 40f)));
        GUILayout.Label("ICU Assist — VLM Robot Simulation", headerStyle);
        GUILayout.Label(apiStatus, smallStyle);
        if (GUILayout.Button("Refresh Gateway"))
        {
            apiStatus = "Checking VLM gateway...";
            StartCoroutine(apiClient.CheckHealth(result => apiStatus = result));
        }
        GUILayout.Space(6f);

        GUILayout.Label("Natural-language instruction", bodyStyle);
        command = GUILayout.TextField(command, 250, GUILayout.Height(30f));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Request Plan", GUILayout.Height(32f))) RequestPlan();
        if (GUILayout.Button("Confirm & Execute", GUILayout.Height(32f))) ExecutePlan();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Cancel Plan")) CancelPlan();
        GUI.backgroundColor = new Color(1f, 0.35f, 0.28f);
        if (GUILayout.Button("EMERGENCY STOP")) EmergencyStop();
        GUI.backgroundColor = Color.white;
        if (GUILayout.Button("Reset Stop")) ResetStop();
        GUILayout.EndHorizontal();

        GUILayout.Space(5f);
        GUILayout.Label("Status: " + status, bodyStyle);
        GUILayout.Label("Current action: " + currentAction, smallStyle);
        GUILayout.Label("Robot: " + (agent != null && agent.hasPath ? "MOVING" : isStopped ? "STOPPED" : "IDLE")
            + " | Carrying: " + (carriedObject == null ? "nothing" : carriedObject.name), smallStyle);
        GUILayout.Label("Monitor: " + GetDeviceState("monitor_01")
            + " | Infusion simulation: " + GetDeviceState("infusion_01"), smallStyle);

        if (!string.IsNullOrEmpty(lastPlan))
        {
            GUILayout.Space(5f);
            GUILayout.Label("Action-plan preview", bodyStyle);
            GUILayout.TextArea(lastPlan, GUILayout.Height(100f));
        }
        GUILayout.Label("Event log", bodyStyle);
        logScroll = GUILayout.BeginScrollView(logScroll, GUILayout.Height(105f));
        foreach (string entry in eventLog) GUILayout.Label(entry, smallStyle);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private string GetDeviceState(string id)
    {
        return deviceStates.TryGetValue(id, out string value) ? value : "UNKNOWN";
    }

    private void EnsureStyles()
    {
        if (headerStyle != null) return;
        headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 19,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.08f, 0.19f, 0.24f) }
        };
        bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            wordWrap = true,
            normal = { textColor = new Color(0.1f, 0.16f, 0.2f) }
        };
        smallStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            wordWrap = true,
            normal = { textColor = new Color(0.13f, 0.2f, 0.24f) }
        };
    }
}
