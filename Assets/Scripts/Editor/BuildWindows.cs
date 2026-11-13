using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AlchemistsArsenal.EditorTools
{
    /// <summary>
    /// The Windows x64 player, built from the command line:
    /// <c>Unity -batchmode -quit -projectPath . -executeMethod AlchemistsArsenal.EditorTools.BuildWindows.Build -buildPath build/AlchemistsArsenal.exe</c>.
    /// Exits with 1 if the build fails, so a script can stop there.
    /// </summary>
    public static class BuildWindows
    {
        [MenuItem("Alchemist/Build Windows x64")]
        public static void Build()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-buildPath");
            string path = i >= 0 && i + 1 < args.Length ? args[i + 1] : "build/AlchemistsArsenal.exe";

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = path,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary sum = report.summary;
            Debug.Log($"[Build] {sum.result}: {sum.outputPath}, {sum.totalSize / 1048576f:0.0} MB, {sum.totalErrors} errors, version {PlayerSettings.bundleVersion}");
            if (Application.isBatchMode) EditorApplication.Exit(sum.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
