using System;

[Serializable]
public sealed class ActionPlanDto
{
    public string request_id;
    public string status;
    public string summary;
    public float confidence;
    public bool requires_confirmation;
    public RobotActionDto[] actions;
    public string[] warnings;
}

[Serializable]
public sealed class RobotActionDto
{
    public int sequence;
    public string action;
    public string target_id;
    public ActionParametersDto parameters;
}

[Serializable]
public sealed class ActionParametersDto
{
    public string object_id;
}
