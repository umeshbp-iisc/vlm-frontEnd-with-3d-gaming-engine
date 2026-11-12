using UnityEngine;

public sealed class WorldObjectId : MonoBehaviour
{
    public string Id;
    public string ObjectType;
    public bool IsVisible => gameObject.activeInHierarchy && gameObject.GetComponentInChildren<Renderer>() != null;
}
