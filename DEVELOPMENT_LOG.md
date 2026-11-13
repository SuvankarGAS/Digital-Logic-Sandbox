# Development Log – Digital Logic Sandbox

## Phase status

| Phase | Title | Status |
|---|---|---|
| 0 | Project foundation | ✅ Complete (2026-11-13) |
| 1 | Core circuit model | ✅ Complete (2026-11-13) |
| 2 | Simulation engine | ⏳ Next |
| 3 | Visual circuit editor | — |
| 4 | Custom gate packaging | — |
| 5 | Hierarchical composition & inspection | — |
| 6 | Animated signal visualization | — |
| 7 | Truth table generator | — |
| 8 | Persistence & undo/redo | — |
| 9 | Polish & optimization | — |

---

## Phase 0 – Project foundation (2026-11-13)

### Initial project inspection
- Unity **2022.3.62f3**, URP 14.0.12 with the 2D Renderer (`Assets/Settings`), template `SampleScene` only.
- Packages: 2D feature set, UGUI, TextMeshPro 3.0.9, Test Framework 1.1.33, Timeline, Visual Studio IDE,
  `com.coplaydev.unity-mcp` (editor automation tooling).
- Player: .NET Standard 2.1 API level, legacy Input Manager (`activeInputHandler: 0`), C# 9.
- No existing scripts, no conflicts. Not a git repository.

### Implemented
- Full folder structure per CLAUDE.md §17 under `Assets/DigitalLogicSandbox/` (+ `App/`).
- 14 runtime assembly definitions + EditMode/PlayMode test assemblies (see ARCHITECTURE.md §2).
  Core, Components, Simulation, GateLibrary, TruthTable, Commands, Persistence and Utilities are compiled
  with `noEngineReferences: true`.
- `AssemblyInfo.cs` in every runtime assembly granting `InternalsVisibleTo` to the test assemblies.
- Architecture fitness tests (`Tests/EditMode/ArchitectureTests.cs`) enforcing the dependency graph,
  engine-freedom, and inclusion of every runtime assembly in the player build.
- PlayMode smoke test verifying the main scene loads with an orthographic main camera.
- Main scene: template scene moved to `Assets/DigitalLogicSandbox/Scenes/Main.unity` (GUID preserved),
  orthographic camera (size 10, dark background), set as the only build scene.
- TMP Essential Resources imported (`Assets/TextMesh Pro`).
- Player settings: product name "Digital Logic Sandbox", windowed, resizable, 1600×900, run in background.
- `com.unity.nuget.newtonsoft-json` 3.2.1 added as an explicit dependency (was only transitive via MCP).
- Repository hygiene: `.gitignore` (Unity), `.gitattributes` (LF, Unity YAML merge), `.editorconfig`
  (block-scoped namespaces as error, naming rules), `git init`.
- `ARCHITECTURE.md` with the committed design for all phases and coding conventions.

### Architectural decisions
1. **Engine-free domain.** Simulation, model, library, truth tables, commands and persistence cannot reference
   Unity at all — enforced by the compiler and by tests, not by convention.
2. **`Editor/` folder → `DigitalLogicSandbox.CircuitEditor` assembly/namespace** to avoid shadowing
   `UnityEditor.Editor` and Unity's editor-only special folder semantics (proved shipped by a test).
3. **`App/` composition root** added beyond the §17 list.
4. **Newtonsoft.Json** chosen for persistence (dictionaries, polymorphism, `JObject` migrations).
5. **Hierarchy flattening at compile time** with an instance-path map: guarantees independent instance
   state, single-pass event-driven propagation, and per-instance inspection (ARCHITECTURE.md §5).
6. **Truth tables use a fresh simulation context** on the same compiled topology, so the live state is never
   mutated (no capture/restore needed).
7. **Model-geometry hit-testing** instead of physics colliders, keeping GameObjects as pure views.
8. **Legacy Input Manager** kept: desktop mouse/keyboard needs are covered; avoids adding the Input System.

### Test results
- EditMode: **36 / 36 passed** (ArchitectureTests: player inclusion ×14, allowed dependencies ×14,
  engine-free ×8).
- PlayMode: **1 / 1 passed** (MainSceneSmokeTests).
- Console: 0 errors, 0 warnings after compile.

### Known issues / notes
- `com.coplaydev.unity-mcp` tracks the `#main` branch (unpinned). It is development tooling only; pin to a
  tag or remove before release builds.
- The scene has no `Light2D`. Circuit visuals will use unlit materials, so lighting is irrelevant; revisit
  if lit sprites are ever introduced.
- Runtime assemblies currently contain only `AssemblyInfo.cs`; content arrives from Phase 1 onwards.

---

## Dependency update – Input System & DOTween (2026-11-13)

Requested by the user before Phase 1.

- **Input System** `com.unity.inputsystem` 1.19.0 installed. Active Input Handling switched to
  *Input System Package (New)* **before** installing, which avoids the blocking "enable backends" dialog.
  ⚠ The native backend switch only fully applies after an **editor restart**.
- **DOTween** (free, Demigiant) imported from the local Asset Store cache into
  `Assets/Plugins/Demigiant/DOTween`. `DOTween.Modules.asmdef` was generated through DOTween's own
  `ASMDEFManager`. `Assets/Resources/DOTweenSettings.asset` was created by DOTween setup.
- **Problem found:** DOTween.dll and Newtonsoft.Json.dll were auto-referenced by *every* assembly,
  including Core and Simulation. **Fix:** every runtime asmdef now uses `overrideReferences: true` with an
  explicit `precompiledReferences` list. New test
  `ArchitectureTests.RuntimeAssembly_OnlyReferencesAllowedThirdPartyLibraries` enforces the allowlist:
  - Newtonsoft.Json → Persistence
  - DOTween / DOTween.Modules → Animation, CircuitEditor, TruthTable.UI, UI, App
  - Unity.InputSystem → CircuitEditor, UI, App
- DOTween's role is presentation tweens only. Per-wire propagation stays in the central animation tick
  (ARCHITECTURE.md §2, §8).

---

## Phase 1 – Core circuit model (2026-11-13)

### Implemented
- **Core.Signals**: `Signal` (Low/High/Unknown/HighImpedance), `SignalExtensions`.
- **Core.Circuit**: `DefinitionId`, `DefinitionRef`, `CircuitDefinition`, `CircuitChange`/`CircuitChangeKind`,
  `IDefinitionProvider`, `IComponentResolver`.
- **Core.Nodes**: `NodeId`, `NodeDefinition`, `ComponentRef`, `ComponentKind`, `CircuitPoint`, `Rotation`.
- **Core.Ports**: `PortId`, `PortDirection`, `PortDescriptor`, `ComponentSignature`, `InterfacePort`,
  `BoundarySignatures`.
- **Core.Connections**: `PinRef`, `ConnectionDefinition`, `ConnectionError`, `Net`, `NetBuilder`.
- **Core.Validation**: `Diagnostic`/`DiagnosticCode`/`DiagnosticSeverity`, `ValidationReport`,
  `ValidationOptions`, `ConnectionRules`, `CircuitValidator`, `HierarchyValidator`.
- **Utilities**: `CycleFinder` (iterative Tarjan SCC over CSR arrays).
- **Components**: `IPrimitiveComponent`, `AndGate`, `NotGate`, `BuiltInComponentIds`, `PrimitiveRegistry`
  (default = AND + NOT only), `ComponentCatalog` (the `IComponentResolver`).
- **GateLibrary.Custom**: `DefinitionLibrary`, a versioned, freeze-on-publish store.

### Decisions
1. **Connections keyed by destination pin.** This makes "one driver per input" a structural invariant
   instead of a validation step, and gives `Disconnect(PinRef)` an O(1) lookup.
2. **PortIds unique across directions and independent of order**, so interface edits (rename/reorder)
   never break referencing circuits.
3. **Boundary nodes ↔ interface ports one-to-one**, maintained by the model, so packaging needs no extra
   mapping structure.
4. **Feedback loops are reported, not blocked** at wiring time, because Phase B latches need feedback.
   Packaging will reject them through the validator.
5. **A definition may not contain any version of itself.** This stops a draft v2 from embedding its own v1,
   which would otherwise be technically acyclic but confusing.
6. **3-valued primitive logic** (LOW-dominant AND, UNKNOWN propagation) is in place now, so later
   uninitialised and tri-state analysis needs no primitive changes.
7. **AND/NOT implemented with their signatures in Phase 1**, because validation needs pin layouts. Their
   evaluation is two lines each and fully tested now; the engine (Phase 2) only adds scheduling.
8. Node metadata is limited to `Label` for now (YAGNI). It can grow into a key/value bag without changing
   the format, because persistence DTOs are separate (Phase 8).

### Test results
- EditMode: **115 / 115 passed**:
  - ArchitectureTests 50 (layering, engine-free, player inclusion, third-party allowlist)
  - PrimitiveComponentTests (AND/NOT binary and 3-valued tables, registry has only AND+NOT, unique port ids)
  - CircuitDefinitionTests (ids, boundaries/interface, fan-out, single driver, self-connection, remove
    cascades, rename/reorder, deep clone, versions, freeze, change events, restore)
  - CircuitValidatorTests (NAND/OR valid, OR built from a packaged NAND valid, missing components,
    warnings, duplicate names, no outputs, cycles, diamond is not a cycle, circular and self-version
    references, depth limit, nested missing definitions, connection pin-direction rules, custom signature
    derived from its definition)
  - CycleFinderTests (including a 100,000-vertex ring without stack overflow)
  - DefinitionLibraryTests (freeze, duplicate versions, old versions stay resolvable, latest per id, events)
- PlayMode: **1 / 1 passed**.
- Console: 0 errors, 0 warnings.

### Known issues
- Restart the Unity editor once so the Input System native backend is active (required before Phase 3
  input work; Phase 2 is headless and unaffected).
- Cycle detection is conservative for custom gates (assumes every output depends on every input). This is
  sufficient for combinational-only circuits and can be refined with per-definition dependency summaries
  when sequential logic arrives.

### Pending (Phase 2 – Simulation engine)
- `CircuitCompiler`: hierarchical flattening to CSR topology plus a `HierarchyMap` (instance path → nets),
  with depth and cycle guards.
- `SimulationContext`: per-context signal state, preallocated ring-buffer worklist, dirty flags.
- Event-driven propagation with wave numbers, evaluation budget and oscillation diagnostics.
- `SignalChangeBuffer` for presentation consumers; play/pause/step/reset controls.
- Tests: propagation, fan-out, NAND/OR behaviour, independent instances, nested gates, unrelated inputs
  don't trigger full evaluation, zero allocations per settle.
