# Development Log – Digital Logic Sandbox

## Phase status

| Phase | Title | Status |
|---|---|---|
| 0 | Project foundation | ✅ Complete (2026-11-13) |
| 1 | Core circuit model | ✅ Complete (2026-11-13) |
| 2 | Simulation engine | ✅ Complete (2026-11-13) |
| 3 | Visual circuit editor | ⏳ Next |
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

Phases 0–1 committed as `e301066`.

---

## Phase 2 – Simulation engine (2026-11-13)

### Implemented
- **Utilities**: `IntRingQueue`, a circular-array FIFO worklist (deliberately not a priority queue).
- **Simulation.Engine**:
  - `CircuitCompiler`: union-find hierarchy flattening, floating net, guards for circularity/depth/missing
    references, flat-graph cycle warnings.
  - `CompiledCircuit`: immutable CSR topology plus the root interface mapping.
  - `CompiledInstance`: the per-instance hierarchy map.
  - `CompileResult`, `SimulationOptions`.
  - `SimulationRunner`: Play / Pause / Step / Reset / Load with inputs preserved by port id, SetInput,
    ToggleInput, and `Loaded` / `StateChanged` events.
- **Simulation.Context**: `SimulationContext`, the allocation-free event-driven propagation with waves, an
  evaluation budget, and one-wave stepping.
- **Simulation.Propagation**: `SignalChange`, `SignalChangeLog` (capped, drained by consumers),
  `SettleResult`/`SettleStatus`.
- **Simulation.Diagnostics**: `SimulationDiagnostics`, which turns oscillations into actionable errors
  naming the gates involved.

### Decisions
1. **Union-find flattening** instead of resolving boundary aliases in expansion order. It is
   order-independent, handles pass-through wires and feedback through custom gates, and stays linear-time.
2. **Single shared floating net (index 0)** for unconnected inputs: never driven, reads LOW, and no per-pin
   special cases in the hot loop.
3. **FIFO worklist = wave scheduler.** The queue order alone yields propagation depth, so the animation
   layer gets logical ordering for free. There is no timing model and no priority queue.
4. **Feedback compiles with a warning; oscillation is a runtime status.** This keeps the editor usable while
   a loop is half-built and prepares for latches. The budget is `max(10 000, 64 × gates)`.
5. **Reset and Load are full refreshes:** the change log is cleared so views snap instead of animating.
6. **Contexts are cheap and independent.** Truth tables will create their own context over the same
   compiled topology instead of snapshotting and restoring the live state.
7. **Editing recompiles the whole circuit** (about 10 ms for 5,000 gates) and the runner carries inputs over.
   Incremental compilation is deferred until profiling shows a need (CLAUDE.md §15.12).

### Test results
- EditMode: **157 / 157 passed** (42 new):
  - SimulationEngineTests: AND/NOT/NAND/OR tables, OR from packaged NAND, packaged OR used as a gate,
    11-level nesting, independent instances (distinct nets and values), hierarchy map inner signals,
    fan-out, floating inputs (gate and custom-gate pins), pass-through custom gate, unrelated inputs evaluate
    nothing else, same-value no-op, propagation stops at masked gates, change log wave ordering, fan-out
    same wave, recording disabled, StepWave, odd NOT ring detected as oscillation without hanging plus
    diagnostic, reset, independent contexts, compile failures (missing, circular, too deep).
  - SimulationRunnerTests: running vs paused, step, play flush, toggle, reset, reload preserves inputs by
    port id, events, guard without a loaded circuit.
  - SimulationPerformanceTests: zero GC allocation for toggle+settle and StepWave (constraint verified
    to detect allocations); random 500- and 5,000-gate circuits match an independent reference evaluator;
    300 nested OR instances (1,800 gates).
  - IntRingQueueTests.
- Measured: 5,000-gate random circuit compiles in about 10 ms; about 54 evaluations per toggle on average.
- PlayMode: **1 / 1 passed**. Console: 0 errors, 0 warnings.

### Known issues
- Unity editor restart still pending for the Input System native backend (needed in Phase 3).
- Runtime evaluation budget is per settle; a slowly oscillating loop that happens to stabilise within the
  budget is reported as stable (correct behaviour for combinational semantics).

### Pending (Phase 3 – Visual circuit editor)
- Scene composition root (`App`), `InputActionAsset` with the Workspace action map, EventSystem with
  `InputSystemUIInputModule`.
- Workspace camera (pan/zoom with DOTween easing), grid rendering, grid snapping.
- Pooled gate/pin/wire views driven by `CircuitDefinition.Changed` and the live `SimulationRunner`.
- Placement from the library, selection (single/multi/rectangle), drag-move, wire creation with preview,
  compatible-pin highlighting via `ConnectionRules`, orthogonal routing, deletion.
- Input switches and output indicators; recompile on edit through `SimulationRunner.Load`.
