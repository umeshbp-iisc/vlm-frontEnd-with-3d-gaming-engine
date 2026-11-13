using System;
using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

// ---------------------------------------------------------------------------
// JsonUtility world-state types (Improvement 2)
// ---------------------------------------------------------------------------

[Serializable]
public sealed class RobotStateJson
{
    public string id;
    public string status;
    public string carrying_object_id;
}

[Serializable]
public sealed class SceneObjectJson
{
    public string id;
    public string type;
    public bool   visible;
}

[Serializable]
public sealed class DeviceStateJson
{
    public string id;
    public string state;
}

[Serializable]
public sealed class WorldStateJson
{
    public string            scene_id;
    public RobotStateJson    robot;
    public SceneObjectJson[] objects;
    public DeviceStateJson[] devices;
}

// ---------------------------------------------------------------------------
// Conversation history (Improvement 3)
// ---------------------------------------------------------------------------

[Serializable]
public sealed class ConversationTurn
{
    public string instruction;
    public string planStatus;
    public string planSummary;
    public ConversationTurn(string i, string s, string su) { instruction=i; planStatus=s; planSummary=su; }
}

// ---------------------------------------------------------------------------
// Plan history entry (Feature 5)
// ---------------------------------------------------------------------------

public sealed class PlanHistoryEntry
{
    public string        Instruction;
    public ActionPlanDto Plan;
    public string        ExecutedAt;
    public PlanHistoryEntry(string i, ActionPlanDto p) { Instruction=i; Plan=p; ExecutedAt=DateTime.Now.ToString("HH:mm:ss"); }
}

// ---------------------------------------------------------------------------
// Main controller
// ---------------------------------------------------------------------------

public sealed class ICUSimulationController : MonoBehaviour
{
    private const float InteractionDistance  = 1.8f;
    private const float NavigationTimeout    = 60f;
    private const int   MaxConversationTurns = 6;
    private const int   MaxPlanHistory       = 20;

    // world
    private readonly Dictionary<string, WorldObjectId> worldObjects = new Dictionary<string, WorldObjectId>();
    private readonly Dictionary<string, string>        deviceStates = new Dictionary<string, string>();
    private readonly List<string>                      eventLog     = new List<string>();

    // history
    private readonly List<ConversationTurn> conversationHistory = new List<ConversationTurn>();
    private readonly List<PlanHistoryEntry> planHistory         = new List<PlanHistoryEntry>();

    // components
    private VlmApiClient     apiClient;
    private NavMeshAgent     agent;
    private Camera           robotCamera;
    private DemoCameraFollow demoCameraFollow;
    private Transform        carrySocket;
    private Rigidbody        carriedBody;
    private WorldObjectId    carriedObject;
    private Transform        armBone;

    // runtime state
    private ActionPlanDto pendingPlan;
    private Coroutine     execution;
    private string        command       = "Move to the monitor.";
    private string        status        = "Building ICU simulation";
    private string        currentAction = "Idle";
    private string        lastPlan      = "";
    private string        apiStatus     = "Checking VLM gateway...";
    private bool          isStopped;

    // PiP (Feature 6)
    private RenderTexture pipTexture;
    private bool          showPip = true;
    private bool          isCapturingFrame;

    // clarification suggestions (Improvement 4)
    private string[] clarificationSuggestions = new string[0];

    // plan history UI (Feature 5)
    private bool    showHistory;
    private Vector2 historyScroll;
    private Vector2 logScroll;

    // GUI styles
    private GUIStyle headerStyle;
    private GUIStyle bodyStyle;
    private GUIStyle smallStyle;
    private GUIStyle suggestionStyle;

    // =========================================================================
    // Unity lifecycle
    // =========================================================================

    private void Start()
    {
        CreateEnvironment();
        BakeNavigation();
        CreateRobot();

        if (agent != null && !agent.isOnNavMesh)
            if (NavMesh.SamplePosition(agent.transform.position, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                agent.Warp(hit.position);

        pipTexture = new RenderTexture(320, 180, 16);
        if (robotCamera != null) { robotCamera.targetTexture = pipTexture; robotCamera.enabled = true; }

        apiClient = gameObject.AddComponent<VlmApiClient>();
        StartCoroutine(apiClient.CheckHealth(result => apiStatus = result));

        SetStatus(agent != null && agent.isOnNavMesh
            ? "Ready — enter an instruction and request a plan."
            : "Scene ready; navigation mesh is unavailable.");
    }

    private void OnDestroy() { if (pipTexture != null) pipTexture.Release(); }

    private void LateUpdate()
    {
        if (robotCamera == null || isCapturingFrame) return;
        robotCamera.enabled = showPip;
    }

    // =========================================================================
    // Improvement 2 – JsonUtility world state
    // =========================================================================

    private string BuildWorldStateJson()
    {
        var objs = new List<SceneObjectJson>();
        foreach (var kv in worldObjects)
            objs.Add(new SceneObjectJson { id=kv.Key, type=kv.Value.ObjectType, visible=kv.Value.IsVisible });
        var devs = new List<DeviceStateJson>();
        foreach (var kv in deviceStates)
            devs.Add(new DeviceStateJson { id=kv.Key, state=kv.Value });
        var ws = new WorldStateJson
        {
            scene_id = "icu_room_01",
            robot    = new RobotStateJson
            {
                id=                 "icu_robot_01",
                status=             isStopped ? "STOPPED" : status.ToUpperInvariant(),
                carrying_object_id= carriedObject != null ? carriedObject.Id : null
            },
            objects = objs.ToArray(),
            devices = devs.ToArray()
        };
        string json = JsonUtility.ToJson(ws);
        if (carriedObject == null)
            json = json.Replace("\"carrying_object_id\":\"\"", "\"carrying_object_id\":null");
        return json;
    }

    // =========================================================================
    // Improvement 3 – conversation context
    // =========================================================================

    private string BuildConversationContext()
    {
        if (conversationHistory.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        sb.Append("Recent conversation history (oldest first):\n");
        foreach (var t in conversationHistory)
            sb.Append($"  User: {t.instruction}\n  Result: [{t.planStatus}] {t.planSummary}\n");
        return sb.ToString();
    }

    private void RecordTurn(string instr, string st, string su)
    {
        conversationHistory.Add(new ConversationTurn(instr, st, su));
        if (conversationHistory.Count > MaxConversationTurns) conversationHistory.RemoveAt(0);
    }

    // =========================================================================
    // Improvement 4 – clarification suggestions
    // =========================================================================

    private string[] ExtractSuggestions(string summary)
    {
        string[] templates = {
            "Move to the patient bed.", "Move to patient bed 2.", "Move to the medical platform.",
            "Move to the monitor.", "Move to the infusion device.", "Move to the medicine cabinet.",
            "Move to the waste bin.", "Pick up the red supply box.", "Pick up the blue supply box.",
            "Place it on the medical platform.", "Drop the object here.", "Turn on the monitor.",
            "Turn off the monitor.", "Show the demo screen on the monitor.",
            "Start the infusion device simulation.", "Stop the infusion device simulation.", "Return home."
        };
        string lower = summary.ToLowerInvariant();
        var results = new List<string>();
        foreach (var t in templates)
        {
            string key = t.ToLowerInvariant().Replace(".", "").Replace("the ", "");
            if (lower.Contains(key.Substring(0, Mathf.Min(key.Length, 12)))) results.Add(t);
            if (results.Count >= 3) break;
        }
        if (results.Count == 0) { results.Add("Move to the monitor."); results.Add("Pick up the red supply box."); results.Add("Return home."); }
        return results.ToArray();
    }

    // =========================================================================
    // Plan request
    // =========================================================================

    private void RequestPlan()
    {
        if (execution != null)              { SetStatus("A plan is already executing. Cancel it first."); return; }
        if (string.IsNullOrWhiteSpace(command)) { SetStatus("Enter a natural-language command first."); return; }
        if (robotCamera == null || apiClient == null) { SetStatus("Robot camera or gateway unavailable."); return; }
        pendingPlan = null;
        clarificationSuggestions = new string[0];
        SetStatus("Capturing robot-camera image and requesting an action plan...");
        StartCoroutine(CaptureAndRequestPlan());
    }

    private IEnumerator CaptureAndRequestPlan()
    {
        yield return new WaitForEndOfFrame();

        isCapturingFrame = true;
        if (robotCamera != null) robotCamera.enabled = false;

        RenderTexture rt = RenderTexture.GetTemporary(640, 360, 16);
        robotCamera.targetTexture = rt;
        robotCamera.Render();
        RenderTexture.active = rt;
        Texture2D shot = new Texture2D(640, 360, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, 640, 360), 0, 0);
        shot.Apply();
        byte[] imageBytes = shot.EncodeToPNG();
        RenderTexture.active      = null;
        robotCamera.targetTexture = pipTexture;
        RenderTexture.ReleaseTemporary(rt);
        Destroy(shot);
        isCapturingFrame = false;

        string ctx = BuildConversationContext();
        string fullInstruction = string.IsNullOrEmpty(ctx) ? command : ctx + "Current instruction: " + command;

        apiClient.Interpret(fullInstruction, imageBytes, BuildWorldStateJson(), (plan, error) =>
        {
            if (!string.IsNullOrEmpty(error)) { SetStatus(error); AddLog(error); return; }
            pendingPlan = plan;
            RecordTurn(command, plan.status, plan.summary);
            lastPlan = plan.summary
                     + "\nStatus: " + plan.status
                     + " | Confidence: " + plan.confidence.ToString("0.00")
                     + " | Confirmation: " + (plan.requires_confirmation ? "required" : "user review");
            if (plan.actions != null)
                foreach (var a in plan.actions)
                    lastPlan += "\n" + a.sequence + ". " + a.action
                              + (string.IsNullOrEmpty(a.target_id) ? "" : " " + a.target_id);
            AddLog("Plan received: " + plan.status + " — " + plan.summary);
            if (plan.status == "READY")
            { clarificationSuggestions = new string[0]; SetStatus("Plan ready. Review and press Confirm & Execute."); }
            else
            { clarificationSuggestions = ExtractSuggestions(plan.summary); SetStatus(plan.summary); }
        });
    }

    private void ExecutePlan()
    {
        if (pendingPlan == null || pendingPlan.status != "READY") { SetStatus("No executable READY plan."); return; }
        if (pendingPlan.actions == null || pendingPlan.actions.Length == 0 || pendingPlan.actions.Length > 6)
            { SetStatus("Plan rejected: invalid action count."); pendingPlan = null; return; }
        if (pendingPlan.confidence < 0.55f) { SetStatus("Plan rejected: confidence too low."); pendingPlan = null; return; }
        if (isStopped) { SetStatus("Reset emergency stop before executing."); return; }
        planHistory.Insert(0, new PlanHistoryEntry(command, pendingPlan));
        if (planHistory.Count > MaxPlanHistory) planHistory.RemoveAt(planHistory.Count - 1);
        execution = StartCoroutine(ExecuteActions(pendingPlan));
    }

    private IEnumerator ExecuteActions(ActionPlanDto plan)
    {
        for (int i = 0; i < plan.actions.Length; i++)
        {
            if (isStopped) yield break;
            var action = plan.actions[i];
            string reason = "";
            if (action.sequence != i + 1 || !ValidateAction(action, out reason))
                { FailPlan("Validation failed: " + (string.IsNullOrEmpty(reason) ? "bad sequence." : reason)); yield break; }
            currentAction = action.action + (string.IsNullOrEmpty(action.target_id) ? "" : " " + action.target_id);
            AddLog("Started " + currentAction);
            bool success = false;
            switch (action.action)
            {
                case "MOVE_TO":    yield return MoveTo(action.target_id, r => success = r); break;
                case "RETURN_HOME": yield return MoveToPosition(new Vector3(0f, agent.transform.position.y, -7f), r => success = r); break;
                case "PICK":       yield return AnimateArmExtend(); success = PickObject(action.target_id); if (!success) yield return AnimateArmRetract(); break;
                case "DROP":       success = DropObject(null); yield return AnimateArmRetract(); break;
                case "PLACE_ON":   success = PlaceObject(action.target_id, action.parameters?.object_id); yield return AnimateArmRetract(); break;
                case "STOP":       agent.ResetPath(); success = true; break;
                case "DEVICE_POWER_ON": case "DEVICE_POWER_OFF": case "DEVICE_START_SIMULATION":
                case "DEVICE_STOP_SIMULATION": case "DEVICE_STANDBY": case "DEVICE_SHOW_DEMO_SCREEN":
                    success = ApplyDeviceAction(action.action, action.target_id); break;
                default: FailPlan("Unsupported action: " + action.action); yield break;
            }
            if (!success) { FailPlan("Action failed: " + currentAction); yield break; }
            AddLog("Completed " + currentAction);
        }
        currentAction = "Idle"; execution = null; pendingPlan = null;
        SetStatus("Plan completed.");
    }

    // =========================================================================
    // Feature 2 – arm animation
    // =========================================================================

    private IEnumerator AnimateArmExtend() => AnimateArm(Quaternion.identity, Quaternion.Euler(60f, 0f, 0f), 0.4f);
    private IEnumerator AnimateArmRetract() => AnimateArm(armBone != null ? armBone.localRotation : Quaternion.identity, Quaternion.identity, 0.4f);
    private IEnumerator AnimateArm(Quaternion from, Quaternion to, float dur)
    {
        float e = 0f;
        while (e < dur) { e += Time.deltaTime; if (armBone != null) armBone.localRotation = Quaternion.Slerp(from, to, e / dur); yield return null; }
        if (armBone != null) armBone.localRotation = to;
    }

    // =========================================================================
    // Navigation
    // =========================================================================

    private IEnumerator MoveTo(string targetId, Action<bool> done)
    {
        if (!worldObjects.TryGetValue(targetId, out WorldObjectId target)) { done(false); yield break; }
        Collider col = target.GetComponent<Collider>();
        if (col == null) { SetStatus("No collider: " + target.name); done(false); yield break; }
        float radius = Mathf.Max(col.bounds.extents.x, col.bounds.extents.z) + 0.65f;
        Vector3 best = target.transform.position; float bestD = float.MaxValue; bool found = false;
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4f;
            Vector3 c = target.transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            c.y = agent.transform.position.y;
            if (!NavMesh.SamplePosition(c, out NavMeshHit hit, 0.65f, NavMesh.AllAreas) || Mathf.Abs(hit.position.y - agent.transform.position.y) > 0.6f) continue;
            var p = new NavMeshPath();
            if (!agent.CalculatePath(hit.position, p) || p.status != NavMeshPathStatus.PathComplete) continue;
            float d = Vector3.Distance(agent.transform.position, hit.position);
            if (d < bestD) { best = hit.position; bestD = d; found = true; }
        }
        if (!found) { SetStatus("No reachable point near " + target.name + "."); done(false); yield break; }
        yield return MoveToPosition(best, done);
    }

    private IEnumerator MoveToPosition(Vector3 dest, Action<bool> done)
    {
        if (agent == null || !agent.isOnNavMesh || isStopped) { SetStatus("Navigation unavailable."); done(false); yield break; }
        var path = new NavMeshPath();
        if (!agent.CalculatePath(dest, path) || path.status != NavMeshPathStatus.PathComplete) { SetStatus("Target unreachable."); done(false); yield break; }
        if (!agent.SetDestination(dest)) { done(false); yield break; }
        SetStatus("Robot navigating...");
        float elapsed = 0f;
        while (!isStopped && elapsed < NavigationTimeout)
        {
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f)
                { agent.ResetPath(); SetStatus("Target reached."); done(true); yield break; }
            elapsed += Time.deltaTime; yield return null;
        }
        agent.ResetPath(); SetStatus(isStopped ? "Emergency stop engaged." : "Navigation timed out."); done(false);
    }

    // =========================================================================
    // Pick / Place / Drop
    // =========================================================================

    private bool PickObject(string objectId)
    {
        if (carriedObject != null || !worldObjects.TryGetValue(objectId, out WorldObjectId t) || t.ObjectType != "PICKABLE" || DistanceTo(t) > InteractionDistance)
            { SetStatus("Pick rejected."); return false; }
        carriedObject = t; carriedBody = t.GetComponent<Rigidbody>();
        if (carriedBody != null) { carriedBody.isKinematic = true; carriedBody.detectCollisions = false; }
        t.transform.SetParent(carrySocket, true); t.transform.localPosition = Vector3.zero; t.transform.localRotation = Quaternion.identity;
        return true;
    }

    private bool PlaceObject(string surfaceId, string objectId)
    {
        if (carriedObject == null || carriedObject.Id != objectId || !worldObjects.TryGetValue(surfaceId, out WorldObjectId s) || s.ObjectType != "PLACEMENT_SURFACE" || DistanceTo(s) > InteractionDistance)
            { SetStatus("Place rejected."); return false; }
        return ReleaseCarried(s.transform.position + Vector3.up * 0.7f);
    }

    private bool DropObject(string _)
    {
        if (carriedObject == null) return false;
        Vector3 d = agent.transform.position + agent.transform.forward * 0.8f; d.y = 0.45f;
        return ReleaseCarried(d);
    }

    private bool ReleaseCarried(Vector3 pos)
    {
        carriedObject.transform.SetParent(null, true); carriedObject.transform.position = pos;
        if (carriedBody != null) { carriedBody.position = pos; carriedBody.rotation = carriedObject.transform.rotation; carriedBody.detectCollisions = true; carriedBody.isKinematic = false; carriedBody.linearVelocity = Vector3.zero; carriedBody.angularVelocity = Vector3.zero; }
        carriedObject = null; carriedBody = null; return true;
    }

    // =========================================================================
    // Device actions
    // =========================================================================

    private bool ApplyDeviceAction(string action, string deviceId)
    {
        if (!worldObjects.TryGetValue(deviceId, out WorldObjectId dev) || DistanceTo(dev) > InteractionDistance)
            { SetStatus("Device action rejected: not within range."); return false; }
        string cur = deviceStates[deviceId], next;
        if (deviceId == "monitor_01")
        {
            if      (action == "DEVICE_POWER_ON"        && cur == "OFF") next = "ON";
            else if (action == "DEVICE_POWER_OFF"        && cur != "OFF") next = "OFF";
            else if (action == "DEVICE_SHOW_DEMO_SCREEN" && cur == "ON")  next = "DEMO_SCREEN";
            else return BadTransition(deviceId, action, cur);
        }
        else
        {
            if      (action == "DEVICE_STANDBY"           && cur != "OFF")               next = "STANDBY";
            else if (action == "DEVICE_START_SIMULATION"  && cur == "STANDBY")            next = "RUNNING_SIMULATION";
            else if (action == "DEVICE_STOP_SIMULATION"   && cur == "RUNNING_SIMULATION") next = "STANDBY";
            else return BadTransition(deviceId, action, cur);
        }
        deviceStates[deviceId] = next;
        var r = dev.GetComponent<Renderer>();
        if (r != null && deviceId == "monitor_01")
            r.material.color = next == "OFF" ? new Color(0.08f, 0.13f, 0.17f) : new Color(0.1f, 0.65f, 0.4f);
        AddLog(deviceId + " → " + cur + " → " + next);
        return true;
    }

    private bool BadTransition(string id, string action, string cur)
        { SetStatus($"Invalid transition: {id} is {cur}; {action} rejected."); return false; }

    // =========================================================================
    // Validation
    // =========================================================================

    private bool ValidateAction(RobotActionDto action, out string reason)
    {
        reason = "";
        string[] known = { "MOVE_TO","STOP","RETURN_HOME","PICK","DROP","PLACE_ON","DEVICE_POWER_ON","DEVICE_POWER_OFF","DEVICE_START_SIMULATION","DEVICE_STOP_SIMULATION","DEVICE_STANDBY","DEVICE_SHOW_DEMO_SCREEN" };
        if (Array.IndexOf(known, action.action) < 0) { reason = "unsupported action."; return false; }
        if (action.action == "STOP" || action.action == "RETURN_HOME") return true;
        WorldObjectId target = null;
        if (action.action != "DROP" && (string.IsNullOrEmpty(action.target_id) || !worldObjects.TryGetValue(action.target_id, out target) || !target.IsVisible))
            { reason = "target unknown or not visible."; return false; }
        if (action.action == "PICK" && (target.ObjectType != "PICKABLE" || carriedObject != null || !HasMoveTo(action, action.target_id)))
            { reason = "pick requires empty gripper and pickable target."; return false; }
        if (action.action == "PLACE_ON" && (target.ObjectType != "PLACEMENT_SURFACE" || carriedObject == null || action.parameters == null || action.parameters.object_id != carriedObject.Id || !HasMoveTo(action, action.target_id)))
            { reason = "place target or carried-object invalid."; return false; }
        if (action.action == "DROP" && carriedObject == null) { reason = "not carrying anything."; return false; }
        if (action.action.StartsWith("DEVICE_", StringComparison.Ordinal))
        {
            if (target.ObjectType != "SIMULATED_DEVICE" || !deviceStates.ContainsKey(action.target_id)) { reason = "not a registered device."; return false; }
            if (!HasMoveTo(action, action.target_id)) { reason = "device action needs preceding MOVE_TO."; return false; }
        }
        return true;
    }

    private bool HasMoveTo(RobotActionDto current, string targetId)
    {
        if (pendingPlan?.actions == null) return false;
        int idx = Array.IndexOf(pendingPlan.actions, current);
        for (int i = 0; i < idx; i++)
            if (pendingPlan.actions[i].action == "MOVE_TO" && pendingPlan.actions[i].target_id == targetId) return true;
        return false;
    }

    private float DistanceTo(WorldObjectId t)
    {
        Collider c = t.GetComponent<Collider>();
        return c == null ? Vector3.Distance(agent.transform.position, t.transform.position) : Vector3.Distance(agent.transform.position, c.ClosestPoint(agent.transform.position));
    }

    // =========================================================================
    // Plan control
    // =========================================================================

    private void CancelPlan()
    {
        if (execution != null) { StopCoroutine(execution); execution = null; }
        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
        pendingPlan = null; currentAction = "Cancelled";
        SetStatus("Plan cancelled."); AddLog("Plan cancelled by user.");
    }

    private void EmergencyStop()
    {
        isStopped = true;
        if (execution != null) { StopCoroutine(execution); execution = null; }
        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
        pendingPlan = null; currentAction = "Emergency stop";
        SetStatus("EMERGENCY STOP — reset before further actions."); AddLog("Emergency stop engaged.");
    }

    private void ResetStop()
    {
        isStopped = false; currentAction = "Idle";
        SetStatus("Emergency stop reset. Enter a new command."); AddLog("Emergency stop reset.");
    }

    private void FailPlan(string reason)
    {
        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
        currentAction = "Failed"; execution = null; pendingPlan = null;
        SetStatus(reason + " Remaining actions cancelled."); AddLog(reason);
    }

    // Feature 5 – replay
    private void ReplayLastPlan()
    {
        if (planHistory.Count == 0) { SetStatus("No history to replay."); return; }
        if (execution != null)      { SetStatus("Cancel current plan first."); return; }
        if (isStopped)              { SetStatus("Reset emergency stop first."); return; }
        var e = planHistory[0];
        command = e.Instruction; pendingPlan = e.Plan;
        lastPlan = "Replaying: " + e.Instruction + " (" + e.ExecutedAt + ")";
        SetStatus("Plan restored. Press Confirm & Execute."); AddLog("Replaying: " + e.Instruction);
    }

    private void SetStatus(string msg) => status = msg;
    private string GetDeviceState(string id) => deviceStates.TryGetValue(id, out string v) ? v : "?";

    private void AddLog(string msg)
    {
        Debug.Log("[ICU Assist] " + msg);
        eventLog.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + msg);
        if (eventLog.Count > 100) eventLog.RemoveAt(eventLog.Count - 1);
    }

    // =========================================================================
    // OnGUI
    // =========================================================================

    private void OnGUI()
    {
        EnsureStyles();
        float panelW = Mathf.Min(430f, Screen.width - 24f);
        float panelH = Mathf.Min(620f, Screen.height - 24f);
        GUI.Box(new Rect(12f, 12f, panelW, panelH), GUIContent.none);
        GUILayout.BeginArea(new Rect(24f, 20f, panelW - 24f, panelH - 16f));

        GUILayout.Label("ICU Assist — VLM Robot Simulation", headerStyle);
        GUILayout.Label(apiStatus, smallStyle);
        if (GUILayout.Button("Refresh Gateway")) { apiStatus = "Checking..."; StartCoroutine(apiClient.CheckHealth(r => apiStatus = r)); }
        GUILayout.Space(4f);

        GUILayout.Label("Natural-language instruction", bodyStyle);
        command = GUILayout.TextField(command, 250, GUILayout.Height(30f));

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Request Plan",      GUILayout.Height(32f))) RequestPlan();
        if (GUILayout.Button("Confirm & Execute", GUILayout.Height(32f))) ExecutePlan();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Cancel Plan")) CancelPlan();
        GUI.backgroundColor = new Color(1f, 0.35f, 0.28f);
        if (GUILayout.Button("EMERGENCY STOP")) EmergencyStop();
        GUI.backgroundColor = Color.white;
        if (GUILayout.Button("Reset Stop")) ResetStop();
        GUILayout.EndHorizontal();

        // Improvement 4 – suggestion buttons
        if (clarificationSuggestions != null && clarificationSuggestions.Length > 0)
        {
            GUILayout.Space(3f);
            GUILayout.Label("Suggestions:", bodyStyle);
            foreach (var s in clarificationSuggestions)
                if (GUILayout.Button(s, suggestionStyle))
                    { command = s; clarificationSuggestions = new string[0]; RequestPlan(); }
        }

        GUILayout.Space(4f);
        GUILayout.Label("Status: " + status, bodyStyle);
        GUILayout.Label("Action: " + currentAction + "  |  Robot: " + (agent != null && agent.hasPath ? "MOVING" : isStopped ? "STOPPED" : "IDLE"), smallStyle);
        GUILayout.Label("Carrying: " + (carriedObject == null ? "nothing" : carriedObject.name), smallStyle);
        GUILayout.Label("Monitor: " + GetDeviceState("monitor_01") + "  |  Infusion: " + GetDeviceState("infusion_01"), smallStyle);

        if (!string.IsNullOrEmpty(lastPlan))
        {
            GUILayout.Space(4f);
            GUILayout.Label("Action plan", bodyStyle);
            GUILayout.TextArea(lastPlan, GUILayout.Height(90f));
        }

        // Feature 5 – history
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(showHistory ? "▲ Hide History" : "▼ Show History")) showHistory = !showHistory;
        if (GUILayout.Button("⟳ Replay Last")) ReplayLastPlan();
        GUILayout.EndHorizontal();

        if (showHistory && planHistory.Count > 0)
        {
            historyScroll = GUILayout.BeginScrollView(historyScroll, GUILayout.Height(80f));
            foreach (var entry in planHistory)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(entry.ExecutedAt + "  " + entry.Instruction, smallStyle);
                if (GUILayout.Button("Use", GUILayout.Width(36f)))
                { command = entry.Instruction; pendingPlan = entry.Plan; lastPlan = "Restored: " + entry.Instruction; SetStatus("Plan restored. Press Confirm & Execute."); showHistory = false; }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        GUILayout.Label("Event log", bodyStyle);
        logScroll = GUILayout.BeginScrollView(logScroll, GUILayout.Height(90f));
        foreach (var e in eventLog) GUILayout.Label(e, smallStyle);
        GUILayout.EndScrollView();

        GUILayout.EndArea();

        // Feature 6 – PiP
        DrawPip();
    }

    private void DrawPip()
    {
        if (pipTexture == null) return;
        float w = 240f, h = 135f;
        float x = Screen.width  - w - 12f;
        float y = Screen.height - h - 12f;
        if (GUI.Button(new Rect(x, y - 20f, w, 18f), showPip ? "▼ Robot Camera" : "▶ Robot Camera", smallStyle))
            showPip = !showPip;
        if (!showPip) return;
        GUI.DrawTexture(new Rect(x, y, w, h), pipTexture, ScaleMode.ScaleToFit, false);
        GUI.Label(new Rect(x + 4f, y + 2f, w, 16f), "Robot POV", smallStyle);
    }

    private void EnsureStyles()
    {
        if (headerStyle != null) return;
        headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.08f, 0.19f, 0.24f) } };
        bodyStyle   = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, wordWrap = true, normal = { textColor = new Color(0.1f, 0.16f, 0.2f) } };
        smallStyle  = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true, normal = { textColor = new Color(0.13f, 0.2f, 0.24f) } };
        suggestionStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Italic, normal = { textColor = new Color(0.05f, 0.35f, 0.55f) } };
    }

    // =========================================================================
    // Scene construction
    // =========================================================================

    private void CreateEnvironment()
    {
        RenderSettings.ambientLight = new Color(0.56f, 0.62f, 0.7f);
        RenderSettings.ambientMode  = UnityEngine.Rendering.AmbientMode.Flat;

        CreatePrimitive("ICU Room Floor", PrimitiveType.Cube, new Vector3(0f,-0.2f,0f), new Vector3(24f,0.4f,18f), new Color(0.75f,0.79f,0.82f), true);
        CreateWall("North Wall", new Vector3(0f,2f,9f),   new Vector3(24f,4f,0.25f));
        CreateWall("West Wall",  new Vector3(-12f,2f,0f), new Vector3(0.25f,4f,18f));
        CreateWall("East Wall",  new Vector3(12f,2f,0f),  new Vector3(0.25f,4f,18f));

        WorldObjectId bed = CreateTarget("Patient Bed","bed_01","LANDMARK",PrimitiveType.Cube,new Vector3(-5f,0.65f,2f),new Vector3(3.6f,1.25f,2.1f),new Color(0.69f,0.79f,0.86f));
        AddChildBlock(bed.transform,"Mattress",new Vector3(0f,0.14f,0f),new Vector3(0.96f,0.12f,0.9f),Color.white);
        AddChildBlock(bed.transform,"Headboard",new Vector3(0f,0.58f,0.47f),new Vector3(1f,0.28f,0.06f),new Color(0.34f,0.48f,0.58f));

        WorldObjectId bed2 = CreateTarget("Patient Bed 2","bed_02","LANDMARK",PrimitiveType.Cube,new Vector3(5f,0.65f,2f),new Vector3(3.6f,1.25f,2.1f),new Color(0.69f,0.79f,0.86f));
        AddChildBlock(bed2.transform,"Mattress 2",new Vector3(0f,0.14f,0f),new Vector3(0.96f,0.12f,0.9f),Color.white);
        AddChildBlock(bed2.transform,"Headboard 2",new Vector3(0f,0.58f,0.47f),new Vector3(1f,0.28f,0.06f),new Color(0.34f,0.48f,0.58f));

        WorldObjectId platform = CreateTarget("Medical Platform","med_platform_01","PLACEMENT_SURFACE",PrimitiveType.Cube,new Vector3(5f,0.9f,3.5f),new Vector3(2.1f,0.24f,1.4f),new Color(0.25f,0.53f,0.63f));
        AddChildBlock(platform.transform,"Platform Pedestal",new Vector3(0f,-0.46f,0f),new Vector3(0.25f,0.82f,0.25f),new Color(0.33f,0.39f,0.43f));

        WorldObjectId monitor = CreateTarget("Patient Monitor","monitor_01","SIMULATED_DEVICE",PrimitiveType.Cube,new Vector3(-8f,1.8f,-5f),new Vector3(1.3f,1.1f,0.35f),new Color(0.08f,0.13f,0.17f));
        AddChildBlock(monitor.transform,"Monitor Screen",new Vector3(0f,0.08f,-0.51f),new Vector3(0.82f,0.62f,0.04f),new Color(0.08f,0.3f,0.34f));
        deviceStates["monitor_01"] = "OFF";

        WorldObjectId infusion = CreateTarget("Infusion Device","infusion_01","SIMULATED_DEVICE",PrimitiveType.Cube,new Vector3(8f,1.25f,-4.5f),new Vector3(0.7f,1.1f,0.55f),new Color(0.85f,0.88f,0.88f));
        AddChildBlock(infusion.transform,"Infusion Display",new Vector3(0f,0.3f,-0.51f),new Vector3(0.48f,0.22f,0.035f),new Color(0.15f,0.43f,0.35f));
        deviceStates["infusion_01"] = "STANDBY";

        WorldObjectId cabinet = CreateTarget("Medicine Cabinet","medicine_cabinet_01","LANDMARK",PrimitiveType.Cube,new Vector3(-10f,1.5f,-7f),new Vector3(1.8f,2.0f,0.6f),new Color(0.9f,0.92f,0.94f));
        AddChildBlock(cabinet.transform,"Cabinet Door",new Vector3(0f,0f,-0.51f),new Vector3(0.85f,0.9f,0.04f),new Color(0.75f,0.82f,0.88f));
        AddChildBlock(cabinet.transform,"Cabinet Handle",new Vector3(0.3f,0f,-0.54f),new Vector3(0.05f,0.25f,0.04f),new Color(0.6f,0.65f,0.7f));

        WorldObjectId wasteBin = CreateTarget("Waste Bin","waste_bin_01","PLACEMENT_SURFACE",PrimitiveType.Cylinder,new Vector3(9f,0.4f,5f),new Vector3(0.6f,0.8f,0.6f),new Color(0.25f,0.52f,0.3f));
        AddChildBlock(wasteBin.transform,"Waste Bin Rim",new Vector3(0f,0.52f,0f),new Vector3(1.05f,0.06f,1.05f),new Color(0.2f,0.42f,0.24f));

        CreateTarget("Red Supply Box","supply_box_red","PICKABLE",PrimitiveType.Cube,new Vector3(-2f,0.42f,-2f),new Vector3(0.85f,0.8f,0.85f),new Color(0.79f,0.19f,0.18f));
        CreateTarget("Blue Supply Box","supply_box_blue","PICKABLE",PrimitiveType.Cube,new Vector3(2f,0.42f,-4f),new Vector3(0.85f,0.8f,0.85f),new Color(0.15f,0.34f,0.8f));

        CreateLight("ICU Key Light",  new Vector3(-3f,7f,-1f), 1.3f, 100f);
        CreateLight("ICU Fill Light", new Vector3(6f,6f,5f),   0.8f, 85f);
        CreateDemoCamera();
    }

    private void CreateRobot()
    {
        GameObject root = new GameObject("ICU Robot");
        root.transform.position = new Vector3(0f, 0.03f, -7f);
        root.AddComponent<BoxCollider>().size = new Vector3(0.9f, 0.18f, 0.9f);
        WorldObjectId id = root.AddComponent<WorldObjectId>(); id.Id="icu_robot_01"; id.ObjectType="ROBOT";

        AddChildBlock(root.transform,"Robot Base",   new Vector3(0f,0.16f,0f),    new Vector3(0.88f,0.28f,0.88f),new Color(0.16f,0.23f,0.28f));
        AddChildBlock(root.transform,"Robot Body",   new Vector3(0f,0.72f,0f),    new Vector3(0.56f,0.9f,0.52f), new Color(0.2f,0.57f,0.65f));
        AddChildBlock(root.transform,"Robot Head",   new Vector3(0f,1.28f,0.04f), new Vector3(0.42f,0.28f,0.43f),new Color(0.1f,0.19f,0.23f));
        AddChildBlock(root.transform,"Status Light", new Vector3(0f,1.48f,0.04f), new Vector3(0.13f,0.08f,0.13f),Color.green);
        AddChildBlock(root.transform,"Upper Arm",    new Vector3(0.42f,0.72f,0f), new Vector3(0.14f,0.55f,0.14f),new Color(0.18f,0.5f,0.6f));

        GameObject foreArm = new GameObject("Fore Arm"); foreArm.transform.SetParent(root.transform,false);
        foreArm.transform.localPosition = new Vector3(0.42f,0.5f,0f); armBone = foreArm.transform;
        GameObject gripper = GameObject.CreatePrimitive(PrimitiveType.Cube); gripper.name="Gripper";
        gripper.transform.SetParent(foreArm.transform,false); gripper.transform.localPosition=new Vector3(0f,-0.22f,0.22f);
        gripper.transform.localScale=new Vector3(0.18f,0.12f,0.4f); SetColor(gripper,new Color(0.1f,0.19f,0.23f));

        carrySocket = new GameObject("Carry Socket").transform;
        carrySocket.SetParent(foreArm.transform,false); carrySocket.localPosition=new Vector3(0f,-0.22f,0.5f);

        GameObject camGo = new GameObject("Robot Camera"); camGo.transform.SetParent(root.transform,false);
        camGo.transform.localPosition=new Vector3(0f,1.32f,0.42f); camGo.transform.localRotation=Quaternion.Euler(9f,0f,0f);
        robotCamera=camGo.AddComponent<Camera>(); robotCamera.fieldOfView=76f; robotCamera.enabled=false;

        agent=root.AddComponent<NavMeshAgent>(); agent.speed=2.7f; agent.acceleration=9f;
        agent.angularSpeed=240f; agent.stoppingDistance=0.35f; agent.radius=0.38f; agent.height=1.55f;
        var sc=root.AddComponent<SphereCollider>(); sc.isTrigger=true; sc.radius=InteractionDistance;
        demoCameraFollow.Target=root.transform;
    }

    private void BakeNavigation()
    {
        NavMeshSurface s=new GameObject("NavMesh Surface").AddComponent<NavMeshSurface>();
        s.collectObjects=CollectObjects.All; s.useGeometry=NavMeshCollectGeometry.PhysicsColliders;
        s.layerMask=~0; s.BuildNavMesh();
    }

    private WorldObjectId CreateTarget(string name,string id,string type,PrimitiveType prim,Vector3 pos,Vector3 scale,Color color)
    {
        GameObject go=GameObject.CreatePrimitive(prim); go.name=name; go.transform.position=pos; go.transform.localScale=scale; SetColor(go,color);
        WorldObjectId wid=go.AddComponent<WorldObjectId>(); wid.Id=id; wid.ObjectType=type;
        wid.gameObject.AddComponent<FloatingLabel>().Text=name; worldObjects.Add(id,wid);
        if (type=="PICKABLE") { var rb=go.AddComponent<Rigidbody>(); rb.mass=1f; rb.interpolation=RigidbodyInterpolation.Interpolate; }
        return wid;
    }

    private GameObject CreatePrimitive(string name,PrimitiveType prim,Vector3 pos,Vector3 scale,Color color,bool collider)
    {
        GameObject go=GameObject.CreatePrimitive(prim); go.name=name; go.transform.position=pos; go.transform.localScale=scale; SetColor(go,color);
        if (!collider) { var c=go.GetComponent<Collider>(); if (c) Destroy(c); } return go;
    }

    private void CreateWall(string n,Vector3 pos,Vector3 scale) => CreatePrimitive(n,PrimitiveType.Cube,pos,scale,new Color(0.82f,0.86f,0.89f),true);
    private void CreateLight(string n,Vector3 pos,float intensity,float range) { var go=new GameObject(n); go.transform.position=pos; var l=go.AddComponent<Light>(); l.type=LightType.Point; l.intensity=intensity; l.range=range; }

    private void CreateDemoCamera()
    {
        var go=new GameObject("Demo Camera"); go.tag="MainCamera";
        go.transform.position=new Vector3(0f,16f,-18f); go.transform.rotation=Quaternion.Euler(38f,0f,0f);
        var cam=go.AddComponent<Camera>(); cam.clearFlags=CameraClearFlags.SolidColor; cam.backgroundColor=new Color(0.15f,0.17f,0.20f);
        go.AddComponent<AudioListener>(); demoCameraFollow=go.AddComponent<DemoCameraFollow>();
    }

    private static void AddChildBlock(Transform parent,string name,Vector3 lp,Vector3 ls,Color color)
    {
        GameObject c=GameObject.CreatePrimitive(PrimitiveType.Cube); c.name=name;
        c.transform.SetParent(parent,false); c.transform.localPosition=lp; c.transform.localScale=ls; SetColor(c,color);
    }

    private static void SetColor(GameObject go,Color color) { var r=go.GetComponent<Renderer>(); if (r) r.material.color=color; }
}
