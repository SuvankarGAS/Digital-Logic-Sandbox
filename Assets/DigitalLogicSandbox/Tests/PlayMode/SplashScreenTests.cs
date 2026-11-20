using System.Collections;
using DigitalLogicSandbox.App;
using DigitalLogicSandbox.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>
    /// The animated start-up intro: off by default in the editor, blocks the editor while it plays, finishes on its
    /// own, can be skipped, and cleans up its canvas and materials.
    /// </summary>
    public sealed class SplashScreenTests
    {
        private AppBootstrap _app;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            _app = AppBootstrap.Instance;
        }

        [TearDown]
        public void TearDown()
        {
            if (SplashScreen.Current != null)
            {
                Object.Destroy(SplashScreen.Current.gameObject);
            }
        }

        [Test]
        public void NotShownInEditorByDefault()
        {
            Assert.IsFalse(SplashScreen.IsShowing, "Play mode in the editor must start without the intro.");
        }

        [UnityTest]
        public IEnumerator PlaysThroughAndCleansUp()
        {
            int completed = 0;
            SplashScreen splash = SplashScreen.Show(_app.Theme, _app.Shell.Factory, () => completed++);
            GameObject root = splash.gameObject;
            Assert.IsTrue(SplashScreen.IsShowing);

            yield return null;
            var group = root.GetComponent<CanvasGroup>();
            Assert.IsTrue(group.blocksRaycasts, "The intro swallows clicks while it plays.");
            Assert.AreEqual(1000, root.GetComponent<Canvas>().sortingOrder);
            Assert.Greater(root.GetComponent<Canvas>().sortingOrder, _app.Shell.Canvas.sortingOrder);

            float timeout = Time.realtimeSinceStartup + 15f;
            while (root != null && Time.realtimeSinceStartup < timeout)
            {
                if (splash != null && splash.IsExiting)
                {
                    Assert.IsFalse(group.blocksRaycasts, "The editor becomes usable once the dissolve starts.");
                }

                yield return null;
            }

            Assert.IsTrue(root == null, "The intro destroys itself when finished.");
            Assert.IsFalse(SplashScreen.IsShowing);
            Assert.AreEqual(1, completed, "Completion runs exactly once.");
        }

        [UnityTest]
        public IEnumerator SkipJumpsToTheDissolve()
        {
            int completed = 0;
            SplashScreen splash = SplashScreen.Show(_app.Theme, _app.Shell.Factory, () => completed++);
            yield return null;
            splash.Skip();
            Assert.IsTrue(splash.IsExiting);
            Assert.Less(splash.ElapsedTime, SplashScreen.AutoExitTime);

            float timeout = Time.realtimeSinceStartup + SplashScreen.ExitDuration * 2f + 5f;
            while (splash != null && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            Assert.IsTrue(splash == null, "A skipped intro still finishes its dissolve and removes itself.");
            Assert.AreEqual(1, completed);
        }

        [UnityTest]
        public IEnumerator ShowingAgainReplacesTheCurrentSplash()
        {
            int first = 0;
            SplashScreen a = SplashScreen.Show(_app.Theme, _app.Shell.Factory, () => first++);
            SplashScreen b = SplashScreen.Show(_app.Theme, _app.Shell.Factory, null);
            yield return null;
            Assert.AreEqual(1, first, "The replaced splash completes.");
            Assert.IsTrue(a == null);
            Assert.AreSame(b, SplashScreen.Current);
        }
    }
}
