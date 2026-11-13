using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityAssembly = UnityEditor.Compilation.Assembly;

namespace DigitalLogicSandbox.Tests.EditMode
{
    /// <summary>
    /// Architecture fitness tests. They inspect Unity's real compilation graph and fail when
    /// a module takes a dependency that violates the layering documented in ARCHITECTURE.md.
    /// </summary>
    public sealed class ArchitectureTests
    {
        private const string Prefix = "DigitalLogicSandbox.";

        /// <summary>Assemblies that must compile without any UnityEngine/UnityEditor reference.</summary>
        private static readonly string[] EngineFreeAssemblies =
        {
            Prefix + "Utilities",
            Prefix + "Core",
            Prefix + "Components",
            Prefix + "Simulation",
            Prefix + "GateLibrary",
            Prefix + "TruthTable",
            Prefix + "Commands",
            Prefix + "Persistence",
        };

        /// <summary>
        /// The complete allowed project-internal dependency graph. Anything not listed is forbidden.
        /// Keep in sync with ARCHITECTURE.md §2.
        /// </summary>
        private static readonly Dictionary<string, string[]> AllowedDependencies = new Dictionary<string, string[]>
        {
            [Prefix + "Utilities"] = new string[0],
            [Prefix + "Core"] = new[] { "Utilities" },
            [Prefix + "Components"] = new[] { "Utilities", "Core" },
            [Prefix + "Simulation"] = new[] { "Utilities", "Core", "Components" },
            [Prefix + "GateLibrary"] = new[] { "Utilities", "Core", "Components", "Simulation" },
            [Prefix + "TruthTable"] = new[] { "Utilities", "Core", "Components", "Simulation" },
            [Prefix + "Commands"] = new[] { "Utilities", "Core", "Components", "GateLibrary" },
            [Prefix + "Persistence"] = new[] { "Utilities", "Core", "Components", "GateLibrary" },
            [Prefix + "Rendering"] = new[] { "Utilities", "Core", "Simulation" },
            [Prefix + "Animation"] = new[] { "Utilities", "Core", "Simulation", "Rendering" },
            [Prefix + "CircuitEditor"] = new[]
            {
                "Utilities", "Core", "Components", "Simulation", "GateLibrary", "Commands", "Rendering", "Animation",
            },
            [Prefix + "TruthTable.UI"] = new[] { "Utilities", "Core", "Simulation", "TruthTable" },
            [Prefix + "UI"] = new[]
            {
                "Utilities", "Core", "Components", "Simulation", "GateLibrary", "TruthTable", "TruthTable.UI",
                "Commands", "Persistence", "Rendering", "Animation", "CircuitEditor",
            },
            [Prefix + "App"] = new[]
            {
                "Utilities", "Core", "Components", "Simulation", "GateLibrary", "TruthTable", "TruthTable.UI",
                "Commands", "Persistence", "Rendering", "Animation", "CircuitEditor", "UI",
            },
        };

        /// <summary>Third-party libraries whose use is governed per assembly (DLL file names or asmdef names).</summary>
        private static readonly string[] GovernedThirdParty =
        {
            "Newtonsoft.Json", "DOTween", "DOTween.Modules", "Unity.InputSystem",
        };

        /// <summary>Which governed third-party libraries each assembly may reference. Absent = none.</summary>
        private static readonly Dictionary<string, string[]> AllowedThirdParty = new Dictionary<string, string[]>
        {
            [Prefix + "Persistence"] = new[] { "Newtonsoft.Json" },
            [Prefix + "Animation"] = new[] { "DOTween", "DOTween.Modules" },
            [Prefix + "CircuitEditor"] = new[] { "DOTween", "DOTween.Modules", "Unity.InputSystem" },
            [Prefix + "TruthTable.UI"] = new[] { "DOTween", "DOTween.Modules" },
            [Prefix + "UI"] = new[] { "DOTween", "DOTween.Modules", "Unity.InputSystem" },
            [Prefix + "App"] = new[] { "DOTween", "DOTween.Modules", "Unity.InputSystem" },
        };

        private static Dictionary<string, UnityAssembly> _playerAssemblies;

        [OneTimeSetUp]
        public void CollectAssemblies()
        {
            _playerAssemblies = new Dictionary<string, UnityAssembly>();
            foreach (UnityAssembly assembly in CompilationPipeline.GetAssemblies(AssembliesType.Player))
            {
                _playerAssemblies[assembly.name] = assembly;
            }
        }

        private static IEnumerable<string> RuntimeAssemblyNames => AllowedDependencies.Keys;

        [TestCaseSource(nameof(RuntimeAssemblyNames))]
        public void RuntimeAssembly_IsCompiledIntoPlayer(string assemblyName)
        {
            // Guards against Unity's special "Editor" folder semantics: the circuit-editor module lives in
            // a folder named Editor but must ship in the player build.
            Assert.That(_playerAssemblies.ContainsKey(assemblyName), Is.True,
                $"{assemblyName} is not part of the player build. Is it missing scripts or marked editor-only?");
        }

        [TestCaseSource(nameof(EngineFreeAssemblies))]
        public void EngineFreeAssembly_HasNoUnityReferences(string assemblyName)
        {
            UnityAssembly assembly = GetAssembly(assemblyName);

            foreach (string reference in assembly.compiledAssemblyReferences)
            {
                string fileName = Path.GetFileName(reference);
                Assert.That(fileName.StartsWith("UnityEngine") || fileName.StartsWith("UnityEditor"), Is.False,
                    $"{assemblyName} must stay engine-free but references {fileName}.");
            }

            foreach (UnityAssembly reference in assembly.assemblyReferences)
            {
                Assert.That(reference.name.StartsWith("Unity"), Is.False,
                    $"{assemblyName} must stay engine-free but references {reference.name}.");
            }
        }

        [TestCaseSource(nameof(RuntimeAssemblyNames))]
        public void RuntimeAssembly_OnlyReferencesAllowedModules(string assemblyName)
        {
            UnityAssembly assembly = GetAssembly(assemblyName);
            var allowed = new HashSet<string>();
            foreach (string module in AllowedDependencies[assemblyName])
            {
                allowed.Add(Prefix + module);
            }

            foreach (UnityAssembly reference in assembly.assemblyReferences)
            {
                if (!reference.name.StartsWith(Prefix))
                {
                    continue;
                }

                Assert.That(allowed.Contains(reference.name), Is.True,
                    $"Forbidden dependency: {assemblyName} -> {reference.name}. See ARCHITECTURE.md layering rules.");
            }
        }

        [TestCaseSource(nameof(RuntimeAssemblyNames))]
        public void RuntimeAssembly_OnlyReferencesAllowedThirdPartyLibraries(string assemblyName)
        {
            UnityAssembly assembly = GetAssembly(assemblyName);
            var allowed = new HashSet<string>();
            if (AllowedThirdParty.TryGetValue(assemblyName, out string[] libraries))
            {
                allowed.UnionWith(libraries);
            }

            var referenced = new List<string>();
            foreach (string reference in assembly.compiledAssemblyReferences)
            {
                referenced.Add(Path.GetFileNameWithoutExtension(reference));
            }

            foreach (UnityAssembly reference in assembly.assemblyReferences)
            {
                referenced.Add(reference.name);
            }

            foreach (string name in referenced)
            {
                if (System.Array.IndexOf(GovernedThirdParty, name) < 0)
                {
                    continue;
                }

                Assert.That(allowed.Contains(name), Is.True,
                    $"Forbidden third-party dependency: {assemblyName} -> {name}. See ARCHITECTURE.md §2.");
            }
        }

        private static UnityAssembly GetAssembly(string assemblyName)
        {
            Assert.That(_playerAssemblies.TryGetValue(assemblyName, out UnityAssembly assembly), Is.True,
                $"Assembly {assemblyName} was not found in the player compilation.");
            return assembly;
        }
    }
}
