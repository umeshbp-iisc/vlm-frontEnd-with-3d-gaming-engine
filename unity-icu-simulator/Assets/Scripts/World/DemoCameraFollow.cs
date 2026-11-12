using UnityEngine;

public sealed class DemoCameraFollow : MonoBehaviour
{
    public Transform Target;

    private void LateUpdate()
    {
        if (Target == null) return;
        transform.position = Vector3.Lerp(
            transform.position,
            Target.position + new Vector3(0f, 14f, -13f),
            Time.deltaTime * 1.3f);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(Target.position + Vector3.up - transform.position),
            Time.deltaTime * 1.3f);
    }
}
