using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ICUDemoSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/ICU_Main.unity";

    [MenuItem("ICU Assist/Build Demo Scene")]
    public static void Build()
    {
        Directory.CreateDirectory("Assets/Scenes");
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject controller = new GameObject("ICU Simulation Controller");
        controller.AddComponent<ICUSimulationController>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };
        AssetDatabase.SaveAssets();
        Debug.Log("ICU demo scene created at " + ScenePath);
    }

    [MenuItem("ICU Assist/Build Windows Player")]
    public static void BuildWindowsPlayer()
    {
        string output = Path.GetFullPath("../Builds/ICU_Assist/ICU_Assist.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = output,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new System.InvalidOperationException(
                "Windows player build failed: " + report.summary.result);
        }
        Debug.Log("Windows player built at " + output);
    }
}
