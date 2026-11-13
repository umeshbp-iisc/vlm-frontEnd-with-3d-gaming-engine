using NUnit.Framework;
using UnityEngine;

public sealed class ActionPlanDtoTests
{
    [Test]
    public void DeserializesMoveAndPickPlan()
    {
        const string json =
            "{\"request_id\":\"test-id\",\"status\":\"READY\",\"summary\":\"Pick a box\","
            + "\"confidence\":0.95,\"requires_confirmation\":false,\"warnings\":[],"
            + "\"actions\":[{\"sequence\":1,\"action\":\"MOVE_TO\","
            + "\"target_id\":\"supply_box_red\",\"parameters\":{}},"
            + "{\"sequence\":2,\"action\":\"PICK\","
            + "\"target_id\":\"supply_box_red\",\"parameters\":{}}]}";

        ActionPlanDto plan = JsonUtility.FromJson<ActionPlanDto>(json);

        Assert.That(plan, Is.Not.Null);
        Assert.That(plan.status, Is.EqualTo("READY"));
        Assert.That(plan.actions, Has.Length.EqualTo(2));
        Assert.That(plan.actions[0].action, Is.EqualTo("MOVE_TO"));
        Assert.That(plan.actions[1].sequence, Is.EqualTo(2));
        Assert.That(plan.actions[1].target_id, Is.EqualTo("supply_box_red"));
    }

    [Test]
    public void DeserializesPlaceObjectParameter()
    {
        const string json =
            "{\"sequence\":1,\"action\":\"PLACE_ON\","
            + "\"target_id\":\"med_platform_01\","
            + "\"parameters\":{\"object_id\":\"supply_box_blue\"}}";

        RobotActionDto action = JsonUtility.FromJson<RobotActionDto>(json);

        Assert.That(action.parameters, Is.Not.Null);
        Assert.That(action.parameters.object_id, Is.EqualTo("supply_box_blue"));
    }
}
