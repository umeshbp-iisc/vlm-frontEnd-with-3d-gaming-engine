using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class ICURuntimeSmokeTests
{
    [UnitySetUp]
    public IEnumerator LoadScene()
    {
        yield return SceneManager.LoadSceneAsync("ICU_Main");
        yield return null;
    }

    [UnityTest]
    public IEnumerator IdleRobotStaysStillWhileDemoCameraFollows()
    {
        GameObject robot = GameObject.Find("ICU Robot");
        Vector3 start = robot.transform.position;
        yield return new WaitForSeconds(1f);

        Assert.That(Vector3.Distance(robot.transform.position, start), Is.LessThan(0.05f),
            "The camera must not move the robot while it is idle.");
        Assert.That(robot.GetComponent<DemoCameraFollow>(), Is.Null);
        DemoCameraFollow follow = GameObject.Find("Demo Camera").GetComponent<DemoCameraFollow>();
        Assert.That(follow, Is.Not.Null);
        Assert.That(follow.Target, Is.EqualTo(robot.transform));
    }

    [UnityTest]
    public IEnumerator RobotPicksPlacesAndReturnsHome()
    {
        ICUSimulationController controller = Object.FindAnyObjectByType<ICUSimulationController>();
        GameObject box = GameObject.Find("Red Supply Box");
        yield return RunPlan(controller,
            Action(1, "MOVE_TO", "supply_box_red"),
            Action(2, "PICK", "supply_box_red"));

        Assert.That(box.transform.parent, Is.EqualTo(GameObject.Find("Carry Socket").transform));
        Assert.That(box.GetComponent<Rigidbody>().isKinematic, Is.True);

        RobotActionDto place = Action(2, "PLACE_ON", "med_platform_01");
        place.parameters = new ActionParametersDto { object_id = "supply_box_red" };
        yield return RunPlan(controller, Action(1, "MOVE_TO", "med_platform_01"), place);
        yield return new WaitForSeconds(1f);

        Assert.That(box.transform.parent, Is.Null);
        Assert.That(box.GetComponent<Rigidbody>().isKinematic, Is.False);
        Collider platform = GameObject.Find("Medical Platform").GetComponent<Collider>();
        Assert.That(box.transform.position.x, Is.EqualTo(platform.bounds.center.x).Within(0.2f));
        Assert.That(box.transform.position.z, Is.EqualTo(platform.bounds.center.z).Within(0.2f));
        Assert.That(box.GetComponent<Collider>().bounds.min.y,
            Is.EqualTo(platform.bounds.max.y).Within(0.1f));

        yield return RunPlan(controller, Action(1, "RETURN_HOME", null));
        Vector3 position = GameObject.Find("ICU Robot").transform.position;
        Assert.That(Vector2.Distance(new Vector2(position.x, position.z), new Vector2(0f, -7f)),
            Is.LessThan(0.5f));
    }

    [UnityTest]
    public IEnumerator RobotReachesElevatedDevicesAndChangesTheirStates()
    {
        ICUSimulationController controller = Object.FindAnyObjectByType<ICUSimulationController>();
        yield return RunPlan(controller,
            Action(1, "MOVE_TO", "monitor_01"),
            Action(2, "DEVICE_POWER_ON", "monitor_01"),
            Action(3, "DEVICE_SHOW_DEMO_SCREEN", "monitor_01"));
        Assert.That(DeviceState(controller, "monitor_01"), Is.EqualTo("DEMO_SCREEN"));

        yield return RunPlan(controller,
            Action(1, "MOVE_TO", "infusion_01"),
            Action(2, "DEVICE_START_SIMULATION", "infusion_01"),
            Action(3, "DEVICE_STOP_SIMULATION", "infusion_01"));
        Assert.That(DeviceState(controller, "infusion_01"), Is.EqualTo("STANDBY"));
    }

    [UnityTest]
    public IEnumerator LiveGatewayPlanMovesRobotToMonitor()
    {
        if (System.Environment.GetEnvironmentVariable("ICU_TEST_LIVE_GATEWAY") != "1")
        {
            Assert.Ignore("Set ICU_TEST_LIVE_GATEWAY=1 with the gateway and Ollama running.");
        }
        ICUSimulationController controller = Object.FindAnyObjectByType<ICUSimulationController>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Camera camera = GameObject.Find("Robot Camera").GetComponent<Camera>();
        RenderTexture target = RenderTexture.GetTemporary(640, 360, 16);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        Texture2D image = new Texture2D(640, 360, TextureFormat.RGB24, false);
        byte[] bytes;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0f, 0f, 640f, 360f), 0, 0);
            image.Apply();
            bytes = image.EncodeToPNG();
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(target);
            Object.Destroy(image);
        }
        string world = (string)typeof(ICUSimulationController)
            .GetMethod("BuildWorldStateJson", flags).Invoke(controller, null);
        ActionPlanDto received = null;
        string error = null;
        bool completed = false;
        controller.GetComponent<VlmApiClient>().Interpret(
            "Move to the monitor.", bytes, world, (plan, failure) =>
            {
                received = plan;
                error = failure;
                completed = true;
            });
        float deadline = Time.realtimeSinceStartup + 130f;
        while (!completed && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(completed, Is.True, "Unity's HTTP request must complete.");
        Assert.That(error, Is.Null);
        Assert.That(received, Is.Not.Null);
        Assert.That(received.status, Is.EqualTo("READY"), received.summary);
        Assert.That(received.confidence, Is.GreaterThanOrEqualTo(0.55f));
        Assert.That(received.actions.Length, Is.EqualTo(1));
        Assert.That(received.actions[0].action, Is.EqualTo("MOVE_TO"));
        Assert.That(received.actions[0].target_id, Is.EqualTo("monitor_01"));
        yield return RunReadyPlan(controller, received);
        Collider monitor = GameObject.Find("Patient Monitor").GetComponent<Collider>();
        Vector3 position = GameObject.Find("ICU Robot").transform.position;
        Assert.That(Vector3.Distance(position, monitor.ClosestPoint(position)), Is.LessThan(1.8f));
    }

    [UnityTest]
    public IEnumerator SceneBuildsICUObjectsAndPlacesRobotOnNavMesh()
    {
        Assert.That(GameObject.Find("Patient Bed"), Is.Not.Null);
        Assert.That(GameObject.Find("Medical Platform"), Is.Not.Null);
        Assert.That(GameObject.Find("Patient Monitor"), Is.Not.Null);
        Assert.That(GameObject.Find("Infusion Device"), Is.Not.Null);
        Assert.That(GameObject.Find("Red Supply Box"), Is.Not.Null);
        Assert.That(GameObject.Find("Blue Supply Box"), Is.Not.Null);

        GameObject robot = GameObject.Find("ICU Robot");
        Assert.That(robot, Is.Not.Null);
        NavMeshAgent agent = robot.GetComponent<NavMeshAgent>();
        Assert.That(agent, Is.Not.Null);
        Assert.That(agent.isOnNavMesh, Is.True);

        WorldObjectId bed = GameObject.Find("Patient Bed").GetComponent<WorldObjectId>();
        Collider bedCollider = bed.GetComponent<Collider>();
        float approachRadius = Mathf.Max(
            bedCollider.bounds.extents.x,
            bedCollider.bounds.extents.z) + 0.65f;
        bool hasReachableApproach = false;
        for (int index = 0; index < 8; index++)
        {
            float angle = index * Mathf.PI / 4f;
            Vector3 candidate = bed.transform.position
                + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * approachRadius;
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(candidate, out hit, 0.65f, NavMesh.AllAreas)
                || Mathf.Abs(hit.position.y - agent.transform.position.y) > 0.6f)
            {
                continue;
            }
            NavMeshPath path = new NavMeshPath();
            if (agent.CalculatePath(hit.position, path)
                && path.status == NavMeshPathStatus.PathComplete)
            {
                hasReachableApproach = true;
                break;
            }
        }
        Assert.That(hasReachableApproach, Is.True, "Robot must have a complete path to the bed.");
        yield return null;
    }

    private static RobotActionDto Action(int sequence, string action, string target)
    {
        return new RobotActionDto { sequence = sequence, action = action, target_id = target };
    }

    private static IEnumerator RunPlan(ICUSimulationController controller, params RobotActionDto[] actions)
    {
        yield return RunReadyPlan(controller, new ActionPlanDto
        {
            status = "READY", confidence = 1f, actions = actions
        });
    }

    private static IEnumerator RunReadyPlan(ICUSimulationController controller, ActionPlanDto plan)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo pending = typeof(ICUSimulationController).GetField("pendingPlan", flags);
        FieldInfo status = typeof(ICUSimulationController).GetField("status", flags);
        FieldInfo execution = typeof(ICUSimulationController).GetField("execution", flags);
        pending.SetValue(controller, plan);
        typeof(ICUSimulationController).GetMethod("ExecutePlan", flags).Invoke(controller, null);

        float deadline = Time.realtimeSinceStartup + 65f;
        while (pending.GetValue(controller) != null && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
        Assert.That(status.GetValue(controller), Is.EqualTo("Plan completed."),
            "The complete action plan must finish successfully.");
        Assert.That(execution.GetValue(controller), Is.Null);
    }

    private static string DeviceState(ICUSimulationController controller, string id)
    {
        return (string)typeof(ICUSimulationController).GetMethod("GetDeviceState",
            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, new object[] { id });
    }
}
