using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AlchemistsArsenal.EditorTools
{
    /// <summary>
    /// The players, built from the command line:
    /// <c>Unity -batchmode -quit -projectPath . -executeMethod AlchemistsArsenal.EditorTools.BuildWindows.Build -buildPath build/AlchemistsArsenal.exe</c>
    /// and <c>...BuildWindows.BuildLinux -buildPath build-linux/AlchemistsArsenal.x86_64</c>.
    /// Exits with 1 if the build fails, so a script can stop there.
    /// </summary>
    public static class BuildWindows
    {
        [MenuItem("Alchemist/Build Windows x64")]
        public static void Build() => Run(BuildTarget.StandaloneWindows64, "build/AlchemistsArsenal.exe", null);

        /// <summary>
        /// Linux x64 on Mono (the project's backend; IL2CPP for Linux from Windows needs a separate
        /// toolchain). If the project is ever switched to IL2CPP, this build still uses Mono and puts it back.
        /// </summary>
        [MenuItem("Alchemist/Build Linux x64")]
        public static void BuildLinux() => Run(BuildTarget.StandaloneLinux64, "build-linux/AlchemistsArsenal.x86_64", ScriptingImplementation.Mono2x);

        private static void Run(BuildTarget target, string defaultPath, ScriptingImplementation? backend)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-buildPath");
            string path = i >= 0 && i + 1 < args.Length ? args[i + 1] : defaultPath;

            var standalone = NamedBuildTarget.Standalone;
            ScriptingImplementation before = PlayerSettings.GetScriptingBackend(standalone);
            BuildReport report;
            try
            {
                if (backend.HasValue && backend.Value != before) PlayerSettings.SetScriptingBackend(standalone, backend.Value);
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                    locationPathName = path,
                    target = target,
                    options = BuildOptions.None,
                });
            }
            finally
            {
                if (PlayerSettings.GetScriptingBackend(standalone) != before) PlayerSettings.SetScriptingBackend(standalone, before);   // leave ProjectSettings untouched
            }
            BuildSummary sum = report.summary;
            Debug.Log($"[Build] {target} {sum.result}: {sum.outputPath}, {sum.totalSize / 1048576f:0.0} MB, {sum.totalErrors} errors, version {PlayerSettings.bundleVersion}");
            if (Application.isBatchMode) EditorApplication.Exit(sum.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
