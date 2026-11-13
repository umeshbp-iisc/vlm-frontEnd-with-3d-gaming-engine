using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public sealed class VlmApiClient : MonoBehaviour
{
    [SerializeField] private string baseUrl = "http://127.0.0.1:8765";
    [SerializeField, Min(5)] private int timeoutSeconds = 120;

    public void Interpret(
        string instruction,
        byte[] image,
        string worldStateJson,
        Action<ActionPlanDto, string> completed)
    {
        StartCoroutine(SendInterpretRequest(instruction, image, worldStateJson, completed));
    }

    private IEnumerator SendInterpretRequest(
        string instruction,
        byte[] image,
        string worldStateJson,
        Action<ActionPlanDto, string> completed)
    {
        string requestId = Guid.NewGuid().ToString();
        var sections = new System.Collections.Generic.List<IMultipartFormSection>
        {
            new MultipartFormDataSection("instruction", instruction),
            new MultipartFormDataSection("world_state", worldStateJson),
            new MultipartFormDataSection("request_id", requestId),
            new MultipartFormFileSection("image", image, "robot-camera.png", "image/png")
        };

        using (UnityWebRequest request = UnityWebRequest.Post(
                   baseUrl.TrimEnd('/') + "/api/v1/commands/interpret", sections))
        {
            request.timeout = timeoutSeconds;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                string detail = request.downloadHandler.text;
                string error = "VLM request to " + request.url + " failed ("
                    + request.responseCode + "): " + request.error
                    + (string.IsNullOrEmpty(detail) ? "" : "\n" + detail);
                Debug.LogWarning(error);
                completed(null, error);
                yield break;
            }

            ActionPlanDto plan;
            try
            {
                plan = JsonUtility.FromJson<ActionPlanDto>(request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                completed(null, "Could not parse VLM response: " + exception.Message);
                yield break;
            }

            if (plan == null || plan.actions == null || plan.request_id != requestId)
            {
                completed(null, "VLM response is missing required fields or has a mismatched request ID.");
                yield break;
            }

            completed(plan, null);
        }
    }

    public IEnumerator CheckHealth(Action<string> completed)
    {
        using (UnityWebRequest request = UnityWebRequest.Get(
                   baseUrl.TrimEnd('/') + "/api/v1/health"))
        {
            request.timeout = 5;
            yield return request.SendWebRequest();
            string result = request.result == UnityWebRequest.Result.Success
                ? request.downloadHandler.text
                : "Gateway check failed at " + request.url + ": " + request.error
                    + ". Start the gateway and click Refresh Gateway.";
            if (request.result != UnityWebRequest.Result.Success) Debug.LogWarning(result);
            completed(result);
        }
    }
}
