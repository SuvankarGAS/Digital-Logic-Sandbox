using System.Collections.Generic;
using System.IO;
using DigitalLogicSandbox.App;
using DigitalLogicSandbox.BuildTools;
using NUnit.Framework;

namespace DigitalLogicSandbox.Tests.EditMode
{
    /// <summary>Release build settings checks and the player smoke test's command line.</summary>
    public sealed class ReleaseTests
    {
        [Test]
        public void CheckSettings_AcceptsTheMainSceneWithAllShaders()
        {
            var shaders = new HashSet<string>(ReleaseBuild.RequiredShaders) { "Sprites/Default" };
            Assert.That(ReleaseBuild.CheckSettings(new[] { ReleaseBuild.MainScene }, shaders), Is.Empty);
        }

        [Test]
        public void CheckSettings_ReportsWrongScenesAndEachMissingShader()
        {
            var shaders = new HashSet<string> { "DigitalLogicSandbox/Unlit" };
            List<string> problems = ReleaseBuild.CheckSettings(new[] { ReleaseBuild.MainScene, "Assets/Other.unity" },
                shaders);
            Assert.That(problems.Count, Is.EqualTo(3));
            Assert.That(problems[0], Does.Contain("exactly one scene"));
            Assert.That(problems[1], Does.Contain("DigitalLogicSandbox/Grid"));
            Assert.That(problems[2], Does.Contain("DigitalLogicSandbox/Wire"));
            Assert.That(ReleaseBuild.CheckSettings(new string[0], null).Count, Is.EqualTo(4));
        }

        [Test]
        public void TheProjectsOwnSettings_PassTheChecks()
        {
            // The same check the build runs, against this project (shaders reach the build via the theme's materials).
            var scenes = new List<string>();
            foreach (UnityEditor.EditorBuildSettingsScene scene in UnityEditor.EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    scenes.Add(scene.path);
                }
            }

            var shaders = new HashSet<string>();
            foreach (string dependency in UnityEditor.AssetDatabase.GetDependencies(scenes.ToArray(), true))
            {
                var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>(dependency);
                if (shader != null)
                {
                    shaders.Add(shader.name);
                }
            }

            Assert.That(ReleaseBuild.CheckSettings(scenes, shaders), Is.Empty);
        }

        [Test]
        public void SmokeTestFolder_ComesFromTheCommandLine()
        {
            Assert.That(SmokeTest.FolderFromArguments(new[] { "app.exe" }), Is.Null);
            Assert.That(SmokeTest.FolderFromArguments(null), Is.Null);
            Assert.That(SmokeTest.FolderFromArguments(new[] { "app.exe", "-logFile", "x.log", "-dlsSmokeTest", "Out" }),
                Is.EqualTo(Path.GetFullPath("Out")));
            Assert.That(SmokeTest.FolderFromArguments(new[] { "app.exe", "-DLSSMOKETEST", "-logFile", "x.log" }),
                Is.EqualTo(Path.GetFullPath("DlsSmokeTest")), "A missing folder uses a default.");
        }
    }
}
