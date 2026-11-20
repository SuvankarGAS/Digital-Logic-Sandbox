using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DigitalLogicSandbox.BuildTools
{
    /// <summary>
    /// Builds the Windows release player: <c>Builds/Windows/DigitalLogicSandbox.exe</c> (64-bit, Mono, no
    /// development flag). Run it from the menu, from code, or headless with
    /// <c>Unity -batchmode -quit -projectPath . -executeMethod DigitalLogicSandbox.BuildTools.ReleaseBuild.BuildWindowsFromCommandLine</c>.
    /// The settings are checked first so a misconfigured build fails with a reason instead of shipping broken.
    /// </summary>
    public static class ReleaseBuild
    {
        public const string MainScene = "Assets/DigitalLogicSandbox/Scenes/Main.unity";
        public const string OutputPath = "Builds/Windows/DigitalLogicSandbox.exe";

        /// <summary>Shaders the app looks up by name; each must reach the player (via a material or Always Included).</summary>
        public static readonly string[] RequiredShaders =
        {
            "DigitalLogicSandbox/Unlit",
            "DigitalLogicSandbox/Grid",
            "DigitalLogicSandbox/Wire",
        };

        /// <summary>
        /// Problems with the build settings: the enabled scenes must be exactly the main scene, and every required
        /// shader must be included by a build dependency. Empty when the build can go ahead.
        /// </summary>
        public static List<string> CheckSettings(IReadOnlyList<string> enabledScenes, ICollection<string> includedShaders)
        {
            var problems = new List<string>();
            if (enabledScenes == null || enabledScenes.Count != 1 || enabledScenes[0] != MainScene)
            {
                problems.Add("The build must contain exactly one scene, " + MainScene + ".");
            }

            for (int i = 0; i < RequiredShaders.Length; i++)
            {
                if (includedShaders == null || !includedShaders.Contains(RequiredShaders[i]))
                {
                    problems.Add("Shader '" + RequiredShaders[i] + "' would not be in the build. Reference it from a " +
                                 "material the scene uses, or add it to Always Included Shaders.");
                }
            }

            return problems;
        }

        [MenuItem("Digital Logic Sandbox/Build Windows (Release)")]
        public static void BuildWindowsFromMenu()
        {
            BuildReport report = BuildWindows(out string summary);
            EditorUtility.DisplayDialog("Windows build", summary, "OK");
            if (report != null && report.summary.result == BuildResult.Succeeded)
            {
                EditorUtility.RevealInFinder(OutputPath);
            }
        }

        /// <summary>Entry point for <c>-executeMethod</c>; exits with code 1 when the build fails.</summary>
        public static void BuildWindowsFromCommandLine()
        {
            BuildReport report = BuildWindows(out _);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(report != null && report.summary.result == BuildResult.Succeeded ? 0 : 1);
            }
        }

        /// <summary>Checks the settings and builds. Returns null (with the reasons in the summary) when refused.</summary>
        public static BuildReport BuildWindows(out string summary)
        {
            List<string> problems = CheckSettings(EnabledScenes(), ShadersReachableFrom(EnabledScenes()));
            if (problems.Count > 0)
            {
                summary = "Build refused:\n- " + string.Join("\n- ", problems);
                Debug.LogError(summary);
                return null;
            }

            var options = new BuildPlayerOptions
            {
                scenes = new[] { MainScene },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary result = report.summary;
            summary = "Windows build " + result.result + " in " + result.totalTime.TotalSeconds.ToString("0") + " s: " +
                      OutputPath + " (" + (result.totalSize / (1024f * 1024f)).ToString("0.0") + " MB, " +
                      result.totalErrors + " errors, " + result.totalWarnings + " warnings).";
            if (result.result == BuildResult.Succeeded)
            {
                string managed = Path.Combine(Path.GetDirectoryName(OutputPath), "DigitalLogicSandbox_Data", "Managed");
                summary += File.Exists(Path.Combine(managed, "MCPForUnity.Runtime.dll"))
                    ? " MCPForUnity.Runtime.dll is included (unused at runtime)."
                    : " MCPForUnity.Runtime.dll is not included.";
                Debug.Log(summary);
            }
            else
            {
                Debug.LogError(summary);
            }

            return report;
        }

        private static List<string> EnabledScenes()
        {
            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    scenes.Add(scene.path);
                }
            }

            return scenes;
        }

        /// <summary>Names of shaders the scenes depend on (through materials) plus the Always Included list.</summary>
        private static HashSet<string> ShadersReachableFrom(List<string> scenes)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string dependency in AssetDatabase.GetDependencies(scenes.ToArray(), true))
            {
                if (dependency.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                {
                    Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(dependency);
                    if (shader != null)
                    {
                        names.Add(shader.name);
                    }
                }
            }

            var graphics = new SerializedObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                "ProjectSettings/GraphicsSettings.asset"));
            SerializedProperty included = graphics.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; included != null && i < included.arraySize; i++)
            {
                if (included.GetArrayElementAtIndex(i).objectReferenceValue is Shader shader)
                {
                    names.Add(shader.name);
                }
            }

            return names;
        }
    }
}
