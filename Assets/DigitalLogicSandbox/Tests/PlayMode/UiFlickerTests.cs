using System.Collections;
using DigitalLogicSandbox.App;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Core.Nodes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>
    /// Panels rebuilt on selection/history changes must not flash: a freshly built button has to show its resting
    /// tint in the same frame instead of cross-fading in from Unity's default white tint.
    /// </summary>
    public sealed class UiFlickerTests
    {
        private AppBootstrap _app;
        private WorkspaceController _workspace;

        private IEnumerator LoadMain()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            _app = AppBootstrap.Instance;
            _workspace = _app.Workspace;
            _workspace.WorkspaceCamera.Animate = false;
            _app.Projects.RememberLastProject = false;
            _app.Projects.AutosaveIntervalSeconds = 0f;
        }

        private static void AssertRestingTint(Component root)
        {
            int checkedButtons = 0;
            foreach (Button button in root.GetComponentsInChildren<Button>())
            {
                if (button.targetGraphic == null || button.transition != Selectable.Transition.ColorTint)
                {
                    continue;
                }

                ColorBlock colors = button.colors;
                Color expected = (button.interactable ? colors.normalColor : colors.disabledColor) * colors.colorMultiplier;
                Color actual = button.targetGraphic.canvasRenderer.GetColor();
                Assert.That(Mathf.Abs(actual.r - expected.r) + Mathf.Abs(actual.g - expected.g) +
                            Mathf.Abs(actual.b - expected.b) + Mathf.Abs(actual.a - expected.a), Is.LessThan(0.01f),
                    button.name + " shows " + actual + " instead of its resting tint " + expected);
                checkedButtons++;
            }

            Assert.That(checkedButtons, Is.GreaterThan(0), "no buttons were checked under " + root.name);
        }

        [UnityTest]
        public IEnumerator RebuiltInspectorButtons_ShowTheirRestingTintImmediately()
        {
            yield return LoadMain();
            _app.Shell.Inspector.Pinned = true;
            yield return null;

            // Same frame as the rebuild: no fade may be in progress.
            _app.Shell.Inspector.Rebuild();
            AssertRestingTint(_app.Shell.Inspector);

            // Also after a toggle-style history change, which rebuilds the card on the next status update.
            NodeId input = _workspace.PlaceNode(ComponentRef.InputBoundary, Vector2.zero);
            _workspace.ToggleInput(input);
            yield return null;
            AssertRestingTint(_app.Shell.Inspector);
        }
    }
}
