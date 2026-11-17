using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Batch-mode Quest build: unity build <project> --target Android --execute-method QuestBuild.Build
public static class QuestBuild
{
    public const string Output = "Builds/FireFighters.apk";

    [MenuItem("Tools/Fire Fighters/Build Quest APK")]
    public static void Build()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, locationPathName = Output, target = BuildTarget.Android, options = BuildOptions.None });
        var s = report.summary;
        Debug.Log($"[QuestBuild] {s.result} errors={s.totalErrors} size={s.totalSize / 1048576}MB time={s.totalTime} -> {s.outputPath}");
        if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
    }
}
