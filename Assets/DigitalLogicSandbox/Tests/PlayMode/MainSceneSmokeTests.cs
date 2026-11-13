using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>Verifies the application entry scene loads and is configured for a 2D editor workspace.</summary>
    public sealed class MainSceneSmokeTests
    {
        private const string MainSceneName = "Main";

        [UnityTest]
        public IEnumerator MainScene_Loads_WithOrthographicMainCamera()
        {
            yield return SceneManager.LoadSceneAsync(MainSceneName, LoadSceneMode.Single);

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null, "Main scene must contain a camera tagged MainCamera.");
            Assert.That(camera.orthographic, Is.True, "The circuit workspace camera must be orthographic.");
        }
    }
}
