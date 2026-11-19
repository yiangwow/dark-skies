using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AlienSkies.EditorTools
{
    /// <summary>Headless Windows build so a second instance can join local duels.</summary>
    public static class PlayerBuilder
    {
        [MenuItem("AlienSkies/Build Windows Player")]
        public static void Build()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/AlienSkies/Scenes/MovementPlayground.unity" },
                locationPathName = "Builds/AlienSkies.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Debug.Log($"PlayerBuilder: {report.summary.result} ({report.summary.totalErrors} errors)");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
