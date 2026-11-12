using UnityEngine;

public sealed class FloatingLabel : MonoBehaviour
{
    public string Text;
    public Color Color = Color.white;

    private void OnGUI()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            return;
        }

        Vector3 screen = camera.WorldToScreenPoint(transform.position);
        if (screen.z <= 0f)
        {
            return;
        }

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color }
        };
        GUI.Label(
            new Rect(screen.x - 90f, Screen.height - screen.y - 32f, 180f, 22f),
            Text,
            style);
    }
}
