using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Rendering.Displays;
using DigitalLogicSandbox.Rendering.Gates;
using DigitalLogicSandbox.TruthTable.Evaluation;
using DigitalLogicSandbox.TruthTable.Generation;
using UnityEngine;

namespace DigitalLogicSandbox.App
{
    /// <summary>
    /// A scripted check run inside a player build: <c>DigitalLogicSandbox.exe -dlsSmokeTest &lt;folder&gt;</c>.
    /// It exercises what a build can break and the editor cannot show (stripped shaders, fonts, serialization,
    /// file access): it runs the CPU demo to HALT, generates the NAND truth table, saves, reopens and compares the
    /// project, and takes a screenshot. It logs <c>SMOKE OK</c> or <c>SMOKE FAIL: reason</c> and quits with exit
    /// code 0 or 1. Projects and autosaves go to the folder, so the user's own files are never touched.
    /// </summary>
    public sealed class SmokeTest : MonoBehaviour
    {
        public const string Argument = "-dlsSmokeTest";
        public const string PassMarker = "SMOKE OK";
        public const string FailMarker = "SMOKE FAIL: ";

        private const int MaxClockEdges = 400;

        private AppBootstrap _app;
        private string _folder;

        /// <summary>The smoke test folder from the command line, or null when the argument is absent.</summary>
        public static string FolderFromArguments(IReadOnlyList<string> arguments)
        {
            if (arguments == null)
            {
                return null;
            }

            for (int i = 0; i < arguments.Count; i++)
            {
                if (!string.Equals(arguments[i], Argument, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool hasValue = i + 1 < arguments.Count && !arguments[i + 1].StartsWith("-", StringComparison.Ordinal);
                return Path.GetFullPath(hasValue ? arguments[i + 1] : "DlsSmokeTest");
            }

            return null;
        }

        public static SmokeTest Attach(AppBootstrap app, string folder)
        {
            var test = app.gameObject.AddComponent<SmokeTest>();
            test._app = app;
            test._folder = folder;
            return test;
        }

        private IEnumerator Start()
        {
            yield return null; // let the shell lay itself out
            string failure;
            try
            {
                failure = CheckShaders() ?? RunCpuDemo() ?? CheckTruthTable() ?? SaveAndReopen();
            }
            catch (Exception exception)
            {
                failure = exception.GetType().Name + ": " + exception.Message + "\n" + exception.StackTrace;
            }

            if (failure == null)
            {
                // Screenshot of the reopened CPU demo, for a human look at fonts, grid and wires.
                _app.Workspace.Sync();
                yield return null;
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(_folder, "smoke.png"));
                yield return null;
                yield return null;
            }

            Debug.Log(failure == null ? PassMarker : FailMarker + failure);
            Application.Quit(failure == null ? 0 : 1);
        }

        private string CheckShaders()
        {
            VisualTheme theme = _app.Theme;
            return CheckShader(theme.UnlitMaterial, "DigitalLogicSandbox/Unlit") ??
                   CheckShader(theme.GridMaterial, "DigitalLogicSandbox/Grid") ??
                   CheckShader(theme.WireMaterial, "DigitalLogicSandbox/Wire");
        }

        private static string CheckShader(Material material, string expected)
        {
            if (material == null || material.shader == null)
            {
                return "No material for " + expected + ".";
            }

            return material.shader.name == expected && material.shader.isSupported
                ? null
                : "Material uses '" + material.shader.name + "' (supported: " + material.shader.isSupported +
                  "), expected " + expected + ".";
        }

        private string RunCpuDemo()
        {
            WorkspaceController workspace = _app.Workspace;
            _app.Projects.OpenCpuDemo();
            workspace.ClockEnabled = false;
            NodeId halt = Labelled("HALT");
            NodeId output = Labelled("OUT");
            if (!halt.IsValid || !output.IsValid)
            {
                return "The CPU demo has no HALT or OUT display.";
            }

            int edges = 0;
            while (Shown(halt) != "ON")
            {
                if (edges++ >= MaxClockEdges || !workspace.TickClock())
                {
                    return "The CPU demo did not halt within " + MaxClockEdges + " clock edges.";
                }
            }

            string expected = DisplayEncoding.FormatValue(DisplayStyle.Decimal, 4, 8, 0);
            string shown = Shown(output);
            return shown == expected
                ? null
                : "The CPU demo halted showing '" + shown + "', expected '" + expected + "'.";
        }

        private string CheckTruthTable()
        {
            CircuitDefinition nand = null;
            foreach (CircuitDefinition definition in _app.Library.AllVersions)
            {
                if (definition.Name == "NAND")
                {
                    nand = definition;
                }
            }

            if (nand == null)
            {
                return "The CPU demo's library has no NAND gate.";
            }

            TruthTableJob job = new TruthTableGenerator(_app.Catalog, _app.TruthTableLimits).Generate(nand);
            job.RunToCompletion();
            TruthTableData table = job.Table;
            if (table == null || !table.IsComplete || table.RowCount != 4)
            {
                return "NAND truth table did not complete: " + job.Status + " " + job.Error;
            }

            for (int row = 0; row < 4; row++)
            {
                bool a = table.GetInput(row, 0) == Signal.High;
                bool b = table.GetInput(row, 1) == Signal.High;
                Signal expected = a && b ? Signal.Low : Signal.High;
                if (table.GetOutput(row, 0) != expected)
                {
                    return "NAND row " + row + " is wrong.";
                }
            }

            return null;
        }

        private string SaveAndReopen()
        {
            WorkspaceController workspace = _app.Workspace;
            int nodes = workspace.RootCircuit.Nodes.Count;
            int connections = workspace.RootCircuit.Connections.Count;
            int definitions = _app.Library.AllVersions.Count;
            string path = Path.Combine(_folder, "smoke" + Persistence.ProjectFormat.FileExtension);
            if (!_app.Projects.SaveAs(path, out string message))
            {
                return "Save failed: " + message;
            }

            _app.Projects.NewProject();
            if (workspace.RootCircuit.Nodes.Count != 0)
            {
                return "New project is not empty.";
            }

            if (!_app.Projects.Open(path, out message))
            {
                return "Open failed: " + message;
            }

            if (workspace.RootCircuit.Nodes.Count != nodes || workspace.RootCircuit.Connections.Count != connections ||
                _app.Library.AllVersions.Count != definitions)
            {
                return "The reopened project differs: " + workspace.RootCircuit.Nodes.Count + "/" + nodes + " nodes, " +
                       workspace.RootCircuit.Connections.Count + "/" + connections + " connections, " +
                       _app.Library.AllVersions.Count + "/" + definitions + " definitions.";
            }

            return null;
        }

        private NodeId Labelled(string label)
        {
            foreach (NodeDefinition node in _app.Workspace.RootCircuit.Nodes)
            {
                if (node.Label == label)
                {
                    return node.Id;
                }
            }

            return NodeId.None;
        }

        private string Shown(NodeId display)
        {
            _app.Workspace.Sync();
            return _app.Workspace.Presenter.TryGetGateView(display, out GateView view) ? view.DisplayText : null;
        }
    }
}
