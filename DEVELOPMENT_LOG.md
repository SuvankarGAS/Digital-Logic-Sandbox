# Development Log – Digital Logic Sandbox

## Phase status

| Phase | Title | Status |
|---|---|---|
| 0 | Project foundation | ✅ Complete (2026-11-13) |
| 1 | Core circuit model | ✅ Complete (2026-11-13) |
| 2 | Simulation engine | ✅ Complete (2026-11-13) |
| 3 | Visual circuit editor | ✅ Complete (2026-11-13) |
| 4 | Custom gate packaging | ✅ Complete (2026-11-14) |
| 5 | Hierarchical composition & inspection | ✅ Complete (2026-11-14) |
| 6 | Animated signal visualization | ✅ Complete (2026-11-14) |
| 7 | Truth table generator | ✅ Complete (2026-11-14) |
| 8 | Persistence & undo/redo | ✅ Complete (2026-11-14) |
| 9 | Polish & optimization | ✅ Complete (2026-11-14) |
| B | Sequential logic (future-expansion phase B) | ✅ Complete (2026-11-14) |
| C | Multi-bit logic: buses, tri-state, contention (future-expansion phase C) | ✅ Complete (2026-11-14) |
| D | Visual components: LED, seven-segment, binary, hex, decimal displays (future-expansion phase D) | ✅ Complete (2026-11-14) |
| E | Computer components: example gate pack up to a 4-bit ALU and 16×4 RAM (future-expansion phase E) | ✅ Complete (2026-11-17) |

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
- ~~Unity editor restart pending for the Input System backend~~ (resolved; `ENABLE_INPUT_SYSTEM` active).
- Runtime evaluation budget is per settle; a slowly oscillating loop that happens to stabilise within the
  budget is reported as stable (correct behaviour for combinational semantics).

### Pending (Phase 3)
Delivered – see the Phase 3 section below.

---

## Phase 3 – Visual circuit editor (2026-11-13)

### Implemented
- **Commands** (`Commands/`): `ICommand`, `CommandHistory` (undo/redo stacks, max depth 500, re-entrancy
  guard, `Record` for already-applied live drags), `CompositeCommand`; circuit commands `AddNodeCommand`,
  `RemoveNodesCommand` (nodes and/or wires), `ConnectCommand`, `TransformNodesCommand` (move/rotate),
  `DuplicateNodesCommand` (copies internal connections only). All restore original node ids, interface port
  ids and port order via `NodeSnapshot`. `BoundaryNames` generates A, B, C… / OUT, OUT2….
- **Rendering** (`Rendering/`): URP HLSL shaders `DigitalLogicSandbox/Unlit` and `/Grid`, materials
  `DlsUnlit.mat` / `DlsGrid.mat`, `VisualTheme` ScriptableObject (`ScriptableObjects/DefaultVisualTheme.asset`),
  generated sprites (`RenderAssets`), `GateLayout` / `GateGeometry`, `WireRouter` (orthogonal, backward
  detours), `CircuitGeometry` (cached gate shapes, pin positions and wire paths, shared by drawing and
  hit-testing), pooled `GateView` / `WireView` (`ViewPool<T>`), `GridView` (one procedural quad),
  `SelectionRectView`, and `CircuitPresenter`, which maps model events and simulation net changes to views
  (net → targets index; only changed nets are touched).
- **CircuitEditor** (`Editor/`): `CircuitSession` (model + compiler + runner; recompiles at most once per frame
  and only on structural changes; inputs preserved by port id), `WorkspaceController` (public editing API,
  frame orchestration), `WorkspaceInteraction` (state machine: wiring by drag or click-click with
  compatible-pin highlighting; select / multi-select / rectangle; drag-move with half-grid snapping; click on
  an input switch toggles it; pan with middle / right / Space+left; scroll zoom at cursor; shortcuts),
  `WorkspaceControls` (Input System action map built in code; optional `InputActionAsset` override),
  `WorkspaceCamera` (DOTween-eased zoom buttons / reset / frame), `WorkspaceHitTester` (model-based picking:
  pins → bodies → wires), `SelectionModel`, `GridSnap`, `PlacementRequest`.
- **UI** (`UI/`): code-built UGUI via `UiFactory`; `AppShell` (canvas, scaler), `ToolbarView` (Undo, Redo,
  Delete, Zoom +/-, Reset View, Grid, Play/Pause, Step, Reset Sim; enabled states follow history, selection
  and simulation), `LibraryPanel` (built-ins listed from the primitive registry, Circuit I/O, My Custom Gates
  grouped by category, search, click-to-arm or drag-to-place, auto-refresh on `DefinitionAdded`),
  `StatusBarView` (status, evaluation count, first error/warning, live input/output values).
- **App**: `AppBootstrap` composition root in `Scenes/Main.unity` (theme and camera references). It creates
  the EventSystem with `InputSystemUIInputModule`, initialises DOTween, wires services and starts an empty
  "Main Circuit".

### Decisions
- **Half-unit grid snapping.** Pins are 1 unit apart and centred on the gate, so gate centres on the 0.5 grid
  keep every pin on the 0.5 grid and aligned wires run straight.
- **Input toggles are not undoable yet.** They are live simulation state, not model edits. Revisit in Phase 8
  (CLAUDE.md lists "change input value" as an undoable command).
- **Click on an input switch toggles it.** Input boundaries are selected by dragging, Shift-click or rectangle.
- **Toggle syncs first.** `ToggleInput` / `SetInput` call `Sync()` so edits made earlier in the same frame are
  compiled before the input is addressed (found via screenshot check; regression test added).
- **Live drag, recorded once.** Dragging moves the model directly (views and wires follow through events); on
  release one `TransformNodesCommand` is `Record`ed. Moves never recompile.
- **Hit-testing is linear** over nodes/wires per pointer query. Fast enough for hundreds of gates; a spatial
  grid will be added in Phase 9 if profiling shows a need.
- **Namespace `CircuitEditor.CameraControl`** for `Editor/Camera/`, to avoid shadowing `UnityEngine.Camera`.
- Rendering now references `Components` (titles from primitives) and TextMeshPro (gate labels); the
  ArchitectureTests allowlist was updated accordingly.
- UI strings use ASCII/Latin-1 glyphs only (the default TMP font lacks ▶, →, −).

### Test results
- EditMode: **189 / 189 passed** (new: `CircuitCommandTests`, `CircuitSessionTests`, `SelectionModelTests`,
  `LayoutAndRoutingTests`, `GeometryAndHitTestTests`, `WorkspaceControlsTests`).
- PlayMode: **7 / 7 passed** (new: `WorkspaceIntegrationTests` covering build/wire/toggle with view checks,
  undo/redo of connections and deletion, move/rotate/duplicate undo with wire rerouting, snapping and
  paused stepping, and the same-frame toggle regression; `AppBootstrapTests` covering Main scene
  composition, only AND/NOT built in, search filter and toolbar state).
- Manual: a NAND circuit built in play mode renders correctly (screenshot verified); console clean.

### Known issues / deferred
- ~~Right panel (properties inspector)~~: delivered in Phase 4.
- Context menus: Phase 9 (right button currently pans / cancels).
- Toolbar project actions (New/Open/Save/Save As): Phase 8. Truth Table / Inspect / Circuit Properties:
  Phases 4–7. Buttons are added when their systems exist (no dead buttons).
- Signal travel animation: Phase 6 (views currently switch to the new value immediately).

---

## Phase 4 – Custom gate packaging (2026-11-14)

### Implemented
- **Packaging engine** (`GateLibrary/Packaging`, engine-free):
  - `PackagingSpec`: name, description, category, icon, ordered and named external inputs/outputs, optional
    node subset (the selection).
  - `CircuitPackager.Package(source, spec)`: builds a *new* definition (new `DefinitionId`, version 1) by
    copying the nodes, internal connections and layout with their ids. Interface order and names come from
    the spec, and port ids are kept, so the mapping to internal boundaries is stable. It then validates and
    returns a `PackagingResult` (definition + report).
  - The source circuit is never modified.
- **Validation** (packaging adds these on top of `CircuitValidator` and `HierarchyValidator`):
  - Errors: missing name; every boundary in scope mapped exactly once, to a port of the right direction;
    empty or duplicate port names; at least one *connected* output; missing components or definitions;
    combinational feedback; circular references; depth limit.
  - Warnings: unused inputs, wires cut by a partial selection, no inputs (constant gate), and a name that
    duplicates an existing gate (allowed, because behaviour never depends on the name).
  - New diagnostic codes: `MissingName`, `ExternalConnection`, `NoInputs`, `DuplicateGateName`,
    `InterfaceMismatch`.
- **Library**: `DefinitionLibrary.Remove` (only to undo a publication; refused while another definition
  references the version), `DefinitionRemoved` event, `FindReferencingDefinition`, `ContainsName`.
- **Commands**: `PublishDefinitionCommand` (undo withdraws the gate), `RenameInterfacePortCommand`,
  `MoveInterfacePortCommand`, `SetCircuitPropertiesCommand`. "Package and clear workspace" is one
  `CompositeCommand` (publish + remove packaged nodes), so a single undo restores everything.
- **Editor**:
  - `WorkspaceController` packaging API: `CreatePackagingSpec`, `PreviewPackaging`, `PackageCustomGate`.
  - `RenameInterfacePort` and `SetCircuitProperties`.
  - `InputSuspended` blocks workspace input while a modal is open.
  - Library add/remove triggers a recompile.
- **Rendering**: custom gates draw pin labels (external port names) inside the body; their width accounts
  for the labels.
- **UI**:
  - `PackagingDialog` (modal): scope toggle for selection vs whole circuit, metadata fields, input/output
    name editing and reordering, live validation messages from the real packager, an optional "remove
    packaged parts" checkbox, Enter/Esc, and an undoable result.
  - `InspectorPanel` (right panel):
    - Circuit view: name, description and category (undoable edits); input/output interface with rename
      and live values; gate/wire counts; Create Custom Gate.
    - Gate view: type, pins, position, rotation, node id, boundary port name and order, live value, and
      custom gate details (definition, version, category, internal size, versions, description).
    - Wire view: source, destination, live signal, delete.
    - Multi-selection view: summary and Create Custom Gate from selection.
  - Toolbar: "Create Gate". Library panel refreshes on publish and on undo.
  - `UiFactory` gained Toggle, Caption, Property, FieldRow, Column and DestroyChildren helpers.

### Decisions
- **Packaging copies, nesting references.** The packaged graph is a deep copy of the source circuit, so
  later edits to the scratch circuit never affect the gate. Custom gates *inside* it are stored as
  `DefinitionRef`s to the shared definitions, never duplicated.
- **No "replace selection with instance" yet.** CLAUDE.md lets users keep the original circuit (default) or
  explicitly replace it. Phase 4 offers "remove the packaged parts" instead. Automatic rewiring of an instance
  in place of a partial selection is deferred to Phase 9; exposing cut wires needs explicit boundaries anyway.
- **The dialog validates live** by running the same packager that commits, so what the user sees is exactly
  what will be checked.
- **Undoing a publication is allowed** because instances placed afterwards are later in the history and are
  undone first; the library still refuses removals that would break another definition.

### Test results
- EditMode: **210 / 210 passed** (new `PackagingTests`, 21 cases):
  - NAND from AND+NOT; OR from packaged NAND (nested references); OR from primitives.
  - Original circuit unchanged; port names, order and id stability; layout and metadata preserved.
  - Multiple independent instances; partial-selection packaging.
  - Every validation error and warning listed above; circular-reference rejection.
  - Publish undo/redo; library removal guard; interface/property command undo; default spec.
- PlayMode: **12 / 12 passed**:
  - `PackagingIntegrationTests`: build NAND in the workspace → package → place an instance → full truth
    table through views. Compose OR from three NAND instances and package it (nested). Undo of
    package-and-clear restores circuit, views and simulation. Invalid packaging publishes nothing.
  - `PackagingDialogTests`: real Main-scene dialog – name required, input suspended while open, gate
    appears in the library, inspector headers.
- Manual: screenshots verified the dialog, library entry, labelled NAND instance and inspector details.

### Known issues / deferred
- Opening a custom gate to see its internals, breadcrumbs, edit mode and versioning: Phase 5.
- Truth table buttons in the inspector and dialog: Phase 7.
- Custom gates live only for the session until persistence lands in Phase 8.
- The icon is stored and shown nowhere yet except the inspector metadata; it will be used for the
  library/gate badge in Phase 9.

---

## Phase 5 – Hierarchical composition, inspection and versioning (2026-11-14)

### Implemented
- **Navigation** (`Editor/Navigation`, no Unity objects):
  - `NavigationLevel` comes in three kinds:
    - Root: main circuit.
    - Inspect: read-only internals of one instance.
    - Draft: editable next version of a gate.
  - `CircuitNavigator`:
    - Opens a custom gate with checks for missing definition, circular path and depth limit, and gives
      meaningful messages.
    - Breadcrumb navigation, edit mode, discard, stack validation and instance-scope resolution.
- **Workspace** (`WorkspaceController`):
  - `Circuit`, `Session` and `History` now follow the shown level.
  - New API: `OpenNode`, `OpenSelected`, `NavigateTo`, `NavigateUp`, `BeginEdit`, `DiscardDraft`,
    `ValidateDraft`, `AnalyzeDraftSave`, `SaveDraft(mode)`, `RequestContextMenu`.
  - New events: `NavigationChanged`, `Notification`, `ContextMenuRequested`.
  - Read-only guards on every editing call. Packaging is allowed only in the main circuit.
  - Camera view saved and restored per level.
- **Interaction**:
  - Double-click a custom gate to open it.
  - Right-click without dragging opens the context menu (right-drag still pans).
  - Escape with an empty selection goes up one level.
  - No wiring, dragging or input toggling in read-only levels.
- **Versioning** (`GateLibrary/Custom`):
  - `DefinitionVersioning`: drafts, plan (optionally cascading), impact analysis, `DependsOn`.
  - `ComponentRetargeting`: exact, reversible swap of references, keeping connections by port id.
  - `RetargetReferencesCommand`; `PublishDefinitionCommand` gained a description.
  - `CircuitPackager.ValidateDefinition` reuses the packaging rules for drafts.
  - New diagnostic code `CircuitReadOnly`.
- **Rendering**: gate titles show `vN` for versions above 1; the library shows the version.
- **UI**:
  - `NavigationBar`: breadcrumbs, mode badge, Up / Edit / Save Version / Discard; discarding unsaved edits
    takes a second click to confirm.
  - `ContextMenuView`: items for the node, wire or empty space.
  - `VersionSaveDialog`: impact summary; keep references, update the main circuit, or update everywhere;
    validation messages.
  - Inspector: mode section with Edit, Save, Discard and Back; "Open Internal Circuit"; fields disabled when
    read-only.
  - Toolbar: "Inspect" button.
  - Status bar: transient notifications and an Inspecting/Editing prefix.

### Decisions
- **Inspection shows a specific instance.** An inspected gate is drawn from the shared definition but
  coloured by the parent simulation's nets for that instance path, so users follow real signals inside nested
  gates. No second simulation is created for inspection.
- **Drafts simulate alone.** A draft is not the gate in context: it gets its own session with free inputs, so
  edits can be tested before saving without disturbing the main circuit.
- **One draft at a time, saved to the root history.** This keeps undo understandable. Saving is one composite
  step on the main circuit's history; the draft's own edit history ends with it.
- **Discard returns to inspecting the published version** instead of jumping to the parent.
- **Port ids carry connections across versions.** Interfaces keep their port ids, so retargeting preserves
  every connection whose pin still exists; removed pins drop their connections, and the user is told how many.

### Test results
- EditMode: **232 / 232 passed**.
  - `NavigationTests` (11): open/refuse cases; nested levels with breadcrumbs back; per-instance live signals;
    depth limit; circular navigation; layout preserved; draft creation without touching the published version;
    unsaved-draft guard; discard returns to inspection; stack validation.
  - `VersioningTests` (11): next version keeps the id; old references stay valid and keep their behaviour;
    retargeting keeps ids/layout/connections; dropped connections and exact revert; command undo/redo;
    plan with and without cascade (dependencies first, old versions untouched); impact analysis changes
    nothing; `DependsOn`; draft validation (no output, self-reference); composite publish undo order.
- PlayMode: **20 / 20 passed**.
  - `HierarchyIntegrationTests` (7): open a gate and watch live nested signals (including parent changes);
    read-only enforcement; breadcrumbs restore views, camera and history; save updating the main circuit
    (AND behaviour, title "NAND v2", undo/redo); save keeping references; self-containment, invalid and
    no-change saves refused, discard; draft simulation independent of the main circuit.
  - `NavigationUiTests` (1): real Main scene – context menu "Open Internal Circuit", breadcrumbs, mode badge,
    Edit → modify → Save Version dialog (default mode, impact text) → back at root with v2, status message,
    crumb click.
- Manual: screenshots of inspection (breadcrumb, live AND/NOT values, inspector), the save dialog, and the
  main circuit with "NAND v2" after saving.

### Known issues / deferred
- "Frame content" uses the full screen and ignores the side panels, so content can sit under the inspector.
  Planned for Phase 9 (polish).
- Updating references inside *other circuits* is limited to the main circuit and, with "update everywhere",
  the latest versions of library gates. Per-reference choices across several open projects arrive with
  persistence in Phase 8.
- Editing a gate opened from the library panel (without an instance) is not offered yet; open an instance.
- Edits made while inspecting inside a draft are not possible by design (inspection is read-only); go back
  to the draft level to edit.

---

## Phase 6 – Animated signal visualization (2026-11-14)

### Implemented
- **Signal change events**: each `SignalChangeLog` entry carries the propagation *wave* in which the net changed.
  The workspace hands the log to the animator once per frame, after the session settles.
- **`SignalAnimationController`** (`Animation/Controllers`, plain C#, ticked once from `WorkspaceController.LateUpdate`):
  - One pulse per changed net. Its front travels from the driver along every wire of the net (fan-out).
  - Wave *k* starts when wave *k-1*'s longest wire arrives, plus a small gap, so the visuals follow the logical order.
  - Batches longer than `MaxBatchSeconds` are compressed to fit, so deep circuits never lag behind.
  - Rapid toggling: a pulse from an earlier batch on the same net snaps to its end before the new one starts,
    so stale states are never shown. Glitches inside one batch are handled the same way.
  - Pulses, trip lists and timing arrays are pooled, and ticking allocates nothing.
  - Logs with structural edits, overflowed logs, or animation disabled fall back to instant display.
  - When the presenter rebinds, all pulses are dropped.
- **Presenter split** (`CircuitPresenter`):
  - `ApplyNetSource`: driver pins and input switches update the moment a net changes.
  - `ApplyWireArrival`: the wire, the destination pin and the output lamp update when the front arrives.
  - Other additions: `GetNetWires`, `RefreshSignals`, `SignalsRefreshed`, `SetVisualizationMode`.
- **`WireView`**:
  - `SetTransition(from, to, progress)` draws the travelling front with the line gradient and width curve: new
    value behind the front, a bright highlight at it, old value ahead.
  - Path sampling: `Length`, `GetPointAt`.
- **`SignalStyle`**: Standard and HighContrast modes. LOW/HIGH/UNKNOWN/Z always differ in **width as well as colour**.
- **`SignalParticlePool`**: fixed-capacity, lazily created dots that mark each front (optional).
- **`SignalAnimationSettings`** (serialized on `AppBootstrap`):
  - Options: Enabled, Speed (0.25x–4x), ShowParticles, MaxBatchSeconds, MaxAnimatedWires.
  - A `Changed` event applies changes live.
- **UI**:
  - Toolbar "Signals" popup: animate toggle, speed -/+, particles, high contrast, "Reset visual state".
  - The status bar shows "propagating" while pulses run.

### Decisions
- **The simulation never waits.** The engine settles synchronously. The animator only replays the recorded
  waves, and a wave is a visual timing unit, not a gate delay. Tests prove that net values are final before
  any animation starts and are never changed by it.
- **Wires use a batched controller instead of DOTween tweens.** Hundreds of wires can change at once, and one
  allocation-free loop over pooled pulses is cheaper and easier to keep deterministic than one tween per wire.
  DOTween remains in use for camera easing and UI effects.
- **Width as well as colour** encodes signal state, so the visuals do not rely on colour alone.
- **Tests run with animation off** (`WorkspaceSetup.SignalAnimation == null` → instant), so earlier phases are
  unaffected. The app enables it through the serialized settings.
- Animation settings will be persisted with the project in Phase 8.

### Test results
- EditMode: **232 / 232 passed**.
- PlayMode: **37 / 37 passed** (17 new).
  - `SignalAnimationTests` (16):
    - Transitions: LOW→HIGH with immediate simulation; HIGH→LOW.
    - Ordering and concurrency: wave order; fan-out; simultaneous changes; rapid toggling.
    - Disabled animation: disabled; disabled mid-animation; logic never changed by animation.
    - Robustness: reset visual state; deep-circuit compression; structural edits mid-animation.
    - Allocations: zero-allocation ticking (`Is.Not.AllocatingGCMemory`), with a control test proving the
      constraint detects allocations.
    - Visualization-mode widths; settings clamping.
  - `SignalSettingsUiTests` (1): in the real Main scene, the toolbar "Signals" popup drives the shared settings.
- Manual: play-mode screenshot of a NAND built from AND + NOT, with the Signals popup open, energized
  HIGH wires and the LOW output wire.

### Known issues / deferred
- Wire routing is still the simple orthogonal router from Phase 3, so pulses follow it. Better routing comes in
  Phase 9.
- Inside an inspected gate, nets are recoloured instantly, not animated, because the animator follows the
  shown level's log. (Resolved: see "Known-issue cleanup 2".)
- Animation settings are not persisted until Phase 8.

---

## Phase 7 – Truth table generator (2026-11-14)

### Implemented
- **Generation** (`TruthTable/Generation`, engine-free):
  - `TruthTableGenerator.Plan(definition | compiled)`: compiles the circuit, takes columns from the interface
    order, and checks limits, outputs and feedback. It evaluates nothing.
  - `Start(plan)` returns a job; `Generate(definition, token)` runs synchronously.
  - `TruthTablePlan`: report, row count, `CanGenerate`, `RequiresConfirmation`, `FirstError`.
  - `TruthTableData`: rows in binary order with the first input as the most significant bit. Inputs are derived
    from the row index and only outputs are stored (one byte per cell). Also `RowOf`, `Symbol` (0/1/X/Z) and
    `CompletedRows`.
  - `TruthTableLimits` (serialized on `AppBootstrap`): MaxInputs 16, MaxRows 65,536, ConfirmRows 1,024,
    frame budget 6 ms, all capped by an absolute ceiling of 24 inputs.
  - `TruthTableTargets.ForComponent`: a custom gate resolves to its packaged definition. A primitive gets a
    one-gate harness, so AND and NOT are also tabulated by the engine.
  - New diagnostic code `TruthTableTooLarge`.
- **Evaluation** (`TruthTable/Evaluation/TruthTableJob`):
  - Incremental, on the caller's thread: `Advance(rows)`, `AdvanceFor(ms)`, `RunToCompletion(token)`,
    `Cancel()`.
  - Reports status, progress, evaluation count and elapsed time.
  - Owns a fresh `SimulationContext` with recording off, released when finished (statistics are kept).
  - A row that does not settle fails the job with an explanation.
- **Export** (`TruthTable/Export/TruthTableExporter`): RFC 4180 CSV (`ToCsv` / `WriteCsv`), tab-separated text
  for the clipboard, and a file-system safe `SuggestFileName`. Only completed rows are written.
- **UI**:
  - `TruthTableJobDriver` (TruthTable.UI) advances one job per frame within the time budget.
  - `TruthTableDialog` (UI/Dialogs) shows the title, input/output summary and "2^N = rows". It has a fixed
    header with inputs and outputs (outputs in accent colour, divider between them).
  - The row list is virtualized: a pool of 40 rows, each a single rich-text line. HIGH values are bold and
    coloured, and pending rows show dots.
  - It shows status, warnings and errors.
  - Buttons: Regenerate, Copy, Export CSV, Generate (confirmation for large tables), Cancel and Close.
    Escape cancels a running job, otherwise closes the dialog.
- **Entry points**:
  - Toolbar "Truth Table".
  - Context menu: "Generate Truth Table..." on empty space, "Truth Table" on any gate.
  - Inspector: a circuit-properties "Generate Truth Table..." button, and a "Truth Table" button for a
    selected gate, including custom gates.
  - Works at every navigation level: main circuit, inspected gate and draft.
- `WorkspaceController.PostNotification` lets panels post status-bar messages.

### Decisions
- **Isolation by construction, not by snapshot/restore.** The compiled circuit is immutable and each job
  evaluates it in its own context. The live context, its inputs, change log and evaluation counter are never
  touched, so nothing needs capturing or restoring. A test proves this even when the job reuses the live
  runner's compiled circuit.
- **Main-thread time slicing instead of threads.** The engine is single-threaded and cheap per row. A frame
  budget keeps the editor responsive without synchronisation, and cancellation is just a flag. This follows
  CLAUDE.md: no multithreading without profiling evidence.
- **Fresh compile for UI requests.** The dialog always compiles the shown definition. This works the same for
  inspected gates, drafts and harnesses, and costs little next to 2^N evaluations. `Plan(CompiledCircuit)`
  remains available for reuse.
- **Feedback loops are refused.** A loop can hold state, so a plain truth table would misdescribe it. A
  sequential analysis mode with explicit initialization and clock sequences belongs to the sequential phase.
- **Binary order with the first input as the MSB.** It is deterministic and matches the CLAUDE.md examples.
  Column order follows the interface order, so reordering ports reorders columns.
- **CSV files go to `persistentDataPath/Exports`**, because the runtime has no native save dialog. The file is
  written to a temp file and then moved, and the path is shown in the status bar. Copy uses the system
  clipboard as tab-separated text (pastes into spreadsheets).
- **The panel lives in the UI assembly.** It needs `UiFactory` and the theme. TruthTable.UI holds only the
  frame-budget driver, which keeps the documented layering intact.

### Test results
- EditMode: **258 / 258 passed** (26 new, `TruthTableTests`):
  - Tables: AND and NOT (engine harness); NAND from AND + NOT; OR from primitives; OR from packaged NANDs;
    nested wrappers; multiple independent outputs.
  - Ordering: binary order with the first input as MSB and interface order; reordering ports; zero inputs.
  - Isolation: live nets, inputs, evaluation count and change log unchanged; definition unchanged.
  - Limits: input limit, row limit, confirmation threshold, absolute ceiling.
  - Errors: no outputs; missing gate definition; feedback loop refused.
  - Cancellation: cancel keeps produced rows; cancellation token.
  - Performance: time-sliced progress; incremental evaluation (no full re-evaluation per row).
  - Export: CSV and TSV content, escaping, partial export, file names.
- PlayMode: **40 / 40 passed** (3 new, `TruthTableUiTests`, real Main scene):
  - Toolbar → NAND table rows, live simulation and history unchanged, copy, CSV export, close.
  - Context menu on a custom gate; disabled circuit item and useful error with no outputs; primitive harness.
  - A 65,536-row table needs confirmation, runs across frames with progress, cancels keeping rows,
    regenerates, scrolls to the last row through the virtualized list, and keeps a fixed object pool.
- Manual: play-mode screenshots of the NAND table and of a 3-input, 2-output table (aligned columns, divider,
  full-width stripes).

### Known issues / deferred
- Truth table limits are not persisted with the project yet (Phase 8 persistence).
- Library items have no truth table entry of their own; place the gate, or use the inspector or context menu. (Resolved: see "Known-issue cleanup 2".)
- No sequential analysis mode yet. It needs clocked components, which arrive with sequential logic.
- Very wide tables (many long port names) scroll horizontally. Columns are capped at 150 px and names are
  truncated with an ellipsis.

---

## Phase 8 – Persistence and undo/redo (2026-11-14)

### Implemented
- **Versioned JSON project format** (`Persistence`):
  - `ProjectFormat`: schema v1, `.dlsproj` extension, format tag.
  - `ProjectDocument` + `ProjectSettingsData`: grid, snapping, visualization mode, animation, truth table
    limits, camera.
  - Internal DTOs (`ProjectDto`, `CircuitDto`, `NodeDto`, `ConnectionDto`) kept separate from domain types.
    No Unity types are serialized.
  - `ProjectSerializer`: camelCase and indented output; each `(definition id, version)` is written once,
    however many instances exist; input values are sorted for stable diffs.
  - `ProjectDeserializer`:
    - Parse, then check the format tag, then migrate on `JObject`, then bind.
    - Validates references and freezes published definitions last.
    - Damaged or foreign files raise `ProjectFormatException` with a readable message.
    - Recoverable problems become warnings and the project still loads: a missing custom gate, an unknown
      primitive, a dangling connection, a duplicate definition.
  - `ProjectMigrator` / `IProjectMigration`: steps `vN → vN+1`. Newer files and missing steps are refused.
  - `ProjectFileStore`:
    - Atomic save: `*.tmp`, flush, `File.Replace` keeping `*.bak`.
    - Load falls back to `.tmp` (interrupted save), then `.bak` (backup).
    - Autosave files, recovery candidates, project listing.
- **Exact round trip:** node, port and definition ids, interface order, timestamps and id allocators are all
  restored (`CircuitDefinition.RestoreIdAllocators`). Deleted ids are never reused, so port retargeting
  across versions stays correct after a reload.
- **App/ProjectController** (implements `UI.IProjectService`):
  - New, Open, Save, Save As.
  - Dirty tracking from history, settings, library and animation changes.
  - Autosave every 60 s to `<project>.autosave` (`Autosave/Untitled.dlsproj.autosave` for untitled projects).
  - Start-up recovery prompt; the last project reopens in builds.
  - In the editor, restoring is opt-in (`AppBootstrap._restoreLastSessionInEditor`) so play-mode sessions and
    tests start clean.
- **UI:**
  - Toolbar New / Open / Save / Save As, and the project title with a `*` marker for unsaved changes.
  - Ctrl+N, Ctrl+O, Ctrl+S and Ctrl+Shift+S.
  - `ConfirmDialog` (Save / Don't save / Cancel; Recover / Discard).
  - `ProjectFileDialog`: in-app picker, because the Unity runtime has no native file dialogs. It lists the
    projects folder newest first, asks before overwriting, and accepts a bare name or a full path.
- **Undo/redo completion:**
  - `SetInputValueCommand`: input toggles are now undoable.
  - `RenameCustomGateCommand`: renames every version through `CircuitDefinition.UpdateMetadata`. Metadata
    can change on published definitions without a new version, because behaviour is unchanged.
  - Interface reordering in the inspector (`MoveInterfacePortCommand`, ^/v buttons).
  - Gate rename from the inspector.
  - `DefinitionLibrary.DefinitionUpdated` and `Clear()`.

### Decisions
- **Metadata is not versioned.** Name, description, category and icon never affect behaviour. Renaming a
  gate therefore updates all its versions in place instead of forcing a new version on every dependent.
- **The project owns the custom gate library.** New and Open replace the library, so projects are
  self-contained.
- **History resets on open/new.** Undo does not cross project boundaries.
- **Input values are saved for the main circuit only.** Inner levels are derived simulation state.
- **In-app file dialog.** It avoids a platform-specific dependency. A full path still works for any folder.

### Test results
- EditMode: **283 / 283 passed** (25 new):
  - `ProjectSerializationTests`:
    - Round trips: exact round trip; definitions, versions and metadata; one copy per definition version;
      loaded nested gates simulate; references to older versions kept; settings and input values.
    - Load problems: missing gate; invalid connection; unknown primitive; damaged files.
    - Migrations: migration applied; newer schema and missing step refused; shipped migrator.
  - `ProjectFileStoreTests`: atomic save with backup; recovery from `.bak` and `.tmp`; unreadable file;
    autosave offered only when newer; listing.
  - `ProjectCommandTests`: rename across versions with undo; metadata allowed on published definitions but
    structural edits refused; interface reorder; input value undo; library clear order; id allocators.
- PlayMode: **48 / 48 passed** (8 new, `ProjectPersistenceTests`, real Main scene):
  - Round trip: Save As, then New, then Open restores circuit, library, inputs, settings and a clean
    history, and the loaded gate can still be opened.
  - Prompts: unsaved-changes prompt (Cancel and Don't save); Save from the prompt on an untitled project.
  - File dialog: overwrite confirmation and the Open list.
  - Undo: undoable input toggle; gate rename shown in the library, and undo.
  - Autosave: recover; discard.
- Fixed during testing: the file dialog filled its list while still inactive, so TMP could not measure the
  item labels (NullReferenceException once a project existed). It now activates before populating.
- Manual: play-mode screenshot of the toolbar (title `Untitled *`) and the Save As dialog.

### Known issues / deferred
- No native OS file dialog (see Decisions).
- An unsaved draft of a gate being edited is not part of the saved project until Save Version. Saving shows
  a reminder.
- Autosave runs on the main thread. Projects are small JSON files, so this is fine; profile in Phase 9.

---

## Phase 9 – Polish and optimization (2026-11-14)

### Implemented
- **Wire routing** (`Rendering/Wires/WireRouter`):
  - Routing follows pin direction, so rotated gates get wires that leave and enter on the correct side.
    Before, every output was assumed to face right.
  - Detours avoid both endpoint gate bodies. Candidates are L, Z and five-segment shapes, scored by length
    and bends, with penalties for crossing a body or doubling back.
  - Facing, aligned pins still get a straight wire.
  - Allocation-free: tested with 1 000 routes and zero bytes allocated.
  - The preview wire uses the same router. The snapped target pin's side and body are used.
- **Framing respects the panels:**
  - `WorkspaceCamera.Frame(bounds, viewport)` and `ComputeFrame` centre content in the uncovered area.
  - `AppShell.GetWorkspaceScreenRect` measures the library, inspector, toolbar, breadcrumbs and status bar.
  - This fixes the Phase 5 known issue: content no longer sits under the inspector.
- **Library polish:**
  - The custom gate icon (up to 4 characters) is shown as a badge on library entries and before the pin
    summary on gate bodies.
  - Search also matches category and description.
  - Right-clicking a library entry opens a context menu with Place and Truth Table. Truth tables are now
    available straight from the library (Phase 7 known issue).
- **Diagnostics:** `CircuitPresenter.WireRerouteCount` and `NetSignalUpdateCount`, with `ResetDiagnostics()`.

### Profiling results (editor, this machine)
- Simulation, 5 000 random gates: compile 9 ms; 64 toggles in 9 ms, about 54 evaluations per toggle.
- Workspace, 2 040 nodes and 2 020 wires:
  - First frame (compile and build all views): about 1.2 s.
  - Toggle one input: 13 ms, with exactly 100 evaluations (one chain). Only that chain's nets are pushed to
    views.
  - Move one gate: 10 ms, and exactly 2 wires are rerouted.
- Persistence, 2 140 nodes and 2 220 connections (620 KiB):
  - Serialize: 33 ms.
  - Atomic save: 37 ms.
  - Load: 113 ms.
  - Main-thread autosave is therefore acceptable (Phase 8 known issue closed). No threading was introduced,
    as the guidelines require profiling evidence first.

### Decisions
- **Endpoint-aware routing instead of global A\*.**
  - It fixes the visible defects (rotated pins, wires through their own gates) at constant cost per wire.
  - Global obstacle avoidance would make each route depend on every gate. Moving one gate would then
    invalidate unrelated wires.
- **Icons are short text, not images.** No asset pipeline is needed. Icons serialize as plain strings, and
  they render with the existing TMP font.

### Test results
- EditMode: **290 / 290 passed** (7 new):
  - Router: rotated source, avoiding both bodies, straight line between facing pins, zero allocations.
  - Camera framing: viewport centring; a degenerate viewport is ignored.
  - Large-project persistence profiling.
- PlayMode: **50 / 50 passed** (2 new):
  - `WorkspacePerformanceTests`: thousands of gates; checks evaluation counts, net updates, reroutes and
    that no views are created on toggle.
  - `AcceptanceWorkflowTests`: the section 20 workflow end to end in the Main scene.
    - Build NAND, toggle the inputs and check animation.
    - Generate the truth table and check the live state is unchanged.
    - Package NAND through the dialog, then reuse and inspect it with breadcrumbs.
    - Build OR from three NANDs, package it and generate its truth table.
    - Save, start a new project and reopen; check the two-level hierarchy and that settings are restored.
- Manual: play-mode screenshots.
  - A rotated NOT, with its input entering from the top.
  - A backward wire routed around both bodies.
  - The icon badge in the library and on the gate.
  - Framing centred between the panels.

### Known issues / deferred
- Wires avoid only their own endpoint gates, not third-party gates in between.
- Opening a custom gate from the library without an instance is still not offered. Place an instance, or
  use the library Truth Table. (Resolved: library preview, see "Editor quality: open custom gates from the
  library".)
- `com.coplaydev.unity-mcp` still tracks `#main`. It is development tooling; pin or remove it before
  release builds.
- `ProjectSettings/EditorSettings.asset` has an uncommitted local change: Enter Play Mode Options was
  turned on with domain and scene reload disabled. Its origin is unknown, so it was left out of the commits.
  (Resolved: committed on request in `fcfdabd`.)


## Phase B – Sequential logic (2026-11-14)

No sequential primitive was added. Latches, flip-flops, registers and counters are built from AND and NOT
feedback loops and packaged like any other gate. The only new component is the **clock**, a circuit utility
like the input and output boundaries.

### Implemented
- **Unit-delay timing** (`SimulationContext`):
  - Propagation runs in waves. Every gate in a wave reads the values committed at the start of that wave.
    Outputs are buffered and committed together at the end of the wave.
  - Results are deterministic and do not depend on gate creation order. A reversed-order flip-flop matched
    over 200 random steps.
  - The number of waves equals the logic depth.
- **Power-on state** (`SimulationOptions.PowerOnState`):
  - Gate-driven nets start unknown (X).
  - `Resolved` (the default): unknown loop nets are forced LOW one at a time, in compiled gate order, with a
    settle after each. The result is binary and repeatable.
  - `Unknown`: values stay X until inputs or the clock drive them.
- **Oscillation collapse:** when a loop uses up the evaluation budget, its switching gates are set to X and
  the circuit settles again (X is a fixed point). Status `Oscillating`, warning code `Oscillation`. Nothing
  hangs.
- **Feedback allowed everywhere:**
  - Validation, packaging and the compiler report loops as `FeedbackLoop` warnings instead of errors.
  - Self-connections (one-node loops) are allowed.
- **Global clock:**
  - `ComponentKind.Clock` / `ComponentRef.Clock`, with one output pin "CLK".
  - All clock nodes anywhere in the hierarchy share one net (`CompiledCircuit.ClockNet`), so packaged
    clocked gates tick together.
  - `CompiledCircuit.IsSequential` is true for feedback or a clock.
  - `SimulationRunner`:
    - `Advance(dt)` ticks in real time while running: one edge per half period, at most 8 edges per call,
      and a backlog is dropped.
    - `TickClock()` applies one edge by hand.
    - `Step()` starts the next clock edge when the circuit is idle.
    - Settings and counters: `ClockFrequency` (0.1–50 Hz, default 2), `ClockEnabled`,
      `ClockEdges`/`ClockCycles`.
    - The `ClockTicked` event fires on each edge.
    - Ticking allocates no garbage.
- **Sequence analysis** (`TruthTable/Generation/SequenceAnalysis.cs`):
  - The truth table refuses sequential circuits (`SequentialCircuit`) and points here.
  - `SequenceSpec` holds one 0/1 pattern per input, the step count, the power-on state, and whether to pulse
    the clock each step.
  - `SequenceAnalyzer` runs in a fresh context that records nothing. Row 0 is power-on. Each later step sets
    the inputs and settles. If the circuit is clocked, it then sets the clock HIGH, settles, sets it LOW and
    settles.
  - Unstable rows (power-on included) are flagged and reported.
  - Results export as CSV or tab-separated text.
- **Editor and UI:**
  - The library's "Circuit I/O" section has a Clock entry. It is drawn as a utility box titled CLK with a
    live state indicator.
  - Clicking a clock node at the top simulation level applies one edge.
  - The workspace ticks the shown level's runner in `LateUpdate`, before `Sync`, so clock edges animate like
    any other change.
  - `WorkspaceController.ClockFrequency`, `ClockEnabled` and `PowerOnState` are shared by the main circuit
    and drafts through one `SimulationOptions` instance. Changing them raises `SettingsChanged`, which marks
    the project dirty.
  - The inspector has a "Clock & power-on" section, shown for the circuit and for clock nodes: frequency,
    enabled, power on as X, and the live cycle count. It also has a "Sequence Analysis..." button.
  - The status bar has a clock cell, for example `CLK 1 · 2 Hz · cycle 12`, or "manual".
  - `SequenceAnalysisDialog` has:
    - One pattern field per input (default: a binary count).
    - A steps field, plus pulse-clock and power-on toggles.
    - A `<pos>`-aligned table.
    - Run, Copy and Export CSV buttons. Esc closes it.
  - The truth-table dialog shows "Sequence Analysis..." when it refuses a sequential target.
  - Clocks are utilities, like boundaries: they have no truth table entry and are not counted as gates.
- **Persistence, schema v2:**
  - Node kind `clock`.
  - `settings.simulation` with `clockFrequency`, `clockEnabled` and `powerOnState`.
  - `AddSimulationSettingsMigration` upgrades v1 files.
  - Simulation settings are applied before the main circuit is compiled, so the power-on mode takes effect
    on load.

### Decisions
- **Build memory from gates, not primitives.** This follows CLAUDE.md ("everything else constructible"). It
  needs a well-defined timing model, hence unit delay: with plain FIFO evaluation, a cross-coupled latch
  depends on scheduling order.
- **Resolved power-on by default.** With X power-on, a toggle flip-flop (D = QN) can never leave X, so
  counters would never count. X power-on stays available for teaching and verification.
- **One global clock net.** It is simple and matches the single-clock designs in the reference material.
  Multiple clock domains can come later as named clocks.
- **Real-time ticking only for the shown level.** Hidden sessions would otherwise build up change logs that
  nothing reads.
- **A power-on mode change takes effect at the next Reset or load**, so it doesn't wipe the user's switches.

### Test results
- EditMode: **319 / 319 passed** (29 new or changed):
  - `SequentialLogicTests` (16): SR latch, X power-on, repeatability, D latch, rising-edge DFF, creation-order
    independence, waves equal depth, 4-bit ripple counter, register, nested clocks sharing a net,
    combinational circuits stay X-free, feedback warning, real-time ticking in the runner, `Step` through
    edges, `TickClock` and its event, zero-allocation ticking.
  - `SequenceAnalysisTests` (10): pattern parsing, holding the last value, a DFF driven through its CLK
    input, clocked counter, explicit X power-on, a combinational sequence, isolation from the live context,
    limits and mismatches, oscillation reporting, export format.
  - Persistence (3): round trip of a clock node and the simulation settings, the v1→v2 migration, defaults
    filled in when settings are missing.
- PlayMode: **55 / 55 passed** (5 new, `SequentialWorkspaceTests`):
  - The library entry and how the clock renders.
  - Real-time ticking, enable/disable, manual edges and paused stepping.
  - Truth-table hand-off to sequence analysis, with the live clock untouched; Copy and CSV export.
  - Invalid patterns are rejected.
  - A clocked project and its settings survive Save, New Project and Open.
- Manual: play-mode screenshots of the CLK box, the inspector clock section, the status bar clock cell and
  the sequence analysis dialog.

### Known issues / deferred
- Only one clock domain: all clock nodes share one frequency. (Resolved: clock dividers, see "Known-issue
  cleanup".)
- The sequence analysis panel shows up to 256 steps (`MaxDisplaySteps`). The analyzer's own limit is 1 024. (Resolved: see "Known-issue cleanup 2".)
- While a draft is being edited only the draft's clock ticks. The main circuit's clock waits until you
  return to it. (Resolved: see "Known-issue cleanup 2".)
---

## Phase C – Multi-bit logic (2026-11-14)

Buses, bus splitting and merging, tri-state drivers and bus contention detection. The built-in gate library
is still exactly AND and NOT; every bus component is a circuit utility, like the boundaries and the clock.

### Implemented
- **Widths in the model.**
  - `PortDescriptor.Width` and `InterfacePort.Width` (1–32 bits, `PortDescriptor.MaxWidth`).
  - Parameterised utilities carry `Width` and `Arity` on `ComponentRef`, so a node's signature is a pure
    function of its reference (`BoundarySignatures.ForUtility`, cached).
  - `CircuitDefinition.AddInputBoundary/AddOutputBoundary(name, position, width)`.
  - Custom gate signatures include port widths, so packaged gates expose bus pins.
- **New utility kinds** (`ComponentKind` 5–8):
  - `Splitter(w)`: a w-bit bus in, one pin per bit out (bit 0 = least significant).
  - `Merger(w)`: the reverse.
  - `BusJoin(w, n)`: 2–8 drivers of w bits onto one shared bus.
  - `TriState(w)`: data D (w bits), enable EN, output OUT. EN=0 outputs Z.
  - `ComponentRef.IsWiring` and `IsUtility` cover them. `TriStateBuffer` is the per-bit evaluator and is
    deliberately **not** registered as a primitive.
- **Validation.**
  - `ConnectionRules` rejects connections between pins of different widths (`ConnectionError.WidthMismatch`).
  - `CircuitValidator` reports width mismatches already in a circuit (`DiagnosticCode.WidthMismatch`).
  - The editor explains the rejection: "Cannot connect a 4-bit pin to a 1-bit pin. Use a splitter or merger…".
- **Compiler.**
  - Every output pin gets one raw net per bit.
  - Splitters, mergers and bus joins are compiled away with union-find. They add no gates and no delay.
  - Primitives and tri-state drivers with bus pins are bit-sliced: one gate per bit, with single-bit pins
    such as EN shared by all slices.
  - Unions with the floating net are deferred until all drivers are known. An unconnected bus-join input
    therefore contributes nothing instead of pulling the bus LOW.
  - Nets with more than one gate driver (only possible through a bus join) get CSR arrays of their driving
    output slots.
  - Joining a root input or the clock to another driver is a compile error (`InvalidBusJoin`).
- **Compiled interface flattened to bits.**
  - `InputCount` and `OutputCount` count bits. `GetInputBit`, `IndexOfInput(port, bit)` and `GetInputWidth`
    address individual bits.
  - `IndexOfInput(port)` is bit 0, so single-bit circuits behave exactly as before.
- **Shared-bus resolution** (`SimulationContext`).
  - The last value of each driver is kept per output slot. The bus is resolved once per wave: all Z → Z,
    any X → X, HIGH and LOW together → X plus contention, otherwise the agreed value.
  - `ContentionCount` and `IsContended(net)` expose contention, and `SimulationDiagnostics.ReportContention`
    produces a `BusContention` warning.
  - `TryGetInputValue`, `TryGetOutputValue` and `SetInputValue(port, ulong)` read and write whole ports.
  - Circuits without bus joins allocate no slot buffers and skip all of this.
- **Runner and session.**
  - `SimulationRunner.Load` carries input values over per bit, so a resized bus keeps the bits both
    versions have. `SetInputValue(port, ulong)` sets a whole port.
  - `CircuitSession` gained `GetInputBits`, `SetInputBits`, `GetBoundaryBit`, `TryGetOutputBits` and
    `FormatBoundaryValue` ("10 (1010)", or "? (1X10)" when a bit is not binary).
- **Undo.**
  - `SetInputValueCommand` stores whole values. Each bit flip and each typed value is one undo step.
  - Add, duplicate and delete snapshots keep boundary widths, and packaging keeps port widths.
- **Truth tables and sequence analysis.**
  - One column per bit, most significant first, named "A[3]"…"A[0]" (`TruthTableColumn.FromPorts`).
  - Columns map to compiled bit indices, so row order still counts in binary across all columns.
  - The sequence dialog shows one pattern field per bit.
- **Persistence (schema v3).**
  - Nodes have an optional `width` (omitted when 1) and `arity`, and there are new node kinds `splitter`,
    `merger`, `busjoin` and `tristate`.
  - Input values stay "Low"/"High" for 0/1 and are decimal otherwise, so v2 files read unchanged.
  - `AddBusSupportMigration` (v2→v3) is a no-op step.
- **Editor.**
  - The library has a "Buses" section with *Bus width* and *Join inputs* fields. Its entries are Bus Input,
    Bus Output, Splitter, Merger, Bus Join, Tri-state, and Tri-state (1-bit).
  - Bus boundaries show "NAME = value" above a row of bit cells, most significant bit on the left. Clicking
    a cell toggles that bit.
  - Splitters and mergers are narrow labelled bars. JOIN and TRI blocks have pin labels, and bus pins are
    labelled with their width ("D[4]").
  - Bus wires are drawn 1.9× thicker and show the aggregate state of their bits: X if any bit is X, HIGH if
    any bit is HIGH, Z if all bits float, otherwise LOW.
  - The presenter maps each bus pin or wire to all of its nets and recomputes the aggregate when any bit
    changes. Bus lamps update per bit.
  - Inspector: width, live value, a "Set value" field for bus inputs, and the bus component types.
  - Status bar: bus values ("A=10 (1010)") and a "Bus contention" status with an explanation.

### Decisions
- **Tri-state is a utility, not a gate.** CLAUDE.md requires the built-in library to stay AND/NOT, and the
  acceptance tests check `PrimitiveRegistry.CreateDefault().All.Count == 2`. A tri-state driver cannot be
  built from AND/NOT (they cannot produce Z), so it is offered as bus wiring alongside splitters and joins.
- **Flatten buses to bits in the compiler** instead of introducing multi-bit net values. The engine,
  animation, truth tables and sequence analysis keep working on single-bit nets. Widths exist only in the
  model and the views. This is the simplest scalable option and adds no per-bit cost to existing circuits.
- **Shared buses only through an explicit bus join.** One driver per net remains the normal rule, so
  accidental multi-driver wiring stays impossible and contention is always a visible design decision.
- **Width parameters on `ComponentRef`.** Signatures stay pure functions of the reference, so validation,
  layout, the compiler and persistence need no node-level side data.
- **Deferred floating ties** keep compilation order-independent while letting unconnected join inputs
  float instead of driving LOW.

### Test results
- EditMode: **335 / 335 passed** (16 new):
  - `BusLogicTests` (13): flattened interface, splitter/merger compile-away and 4-bit inversion, bit-sliced
    tri-state, the tri-state truth table, a shared bus with the enabled driver, contention and its diagnostic,
    an unconnected join input, `InvalidBusJoin`, width mismatch in rules and validation, bus ports through
    custom gates, per-bit input carry-over in the runner, bus truth-table columns, and width-1 circuits
    unchanged.
  - `BusPersistenceTests` (3): v3 round trip of widths, kinds and values (and the loaded circuit simulates),
    single-bit files omit `width` and keep "High", and v2→v3 migration.
- PlayMode: **61 / 61 passed** (6 new, `BusWorkspaceTests`):
  - Library bus entries follow the width setting, and built-in gates are still exactly 2.
  - A bus inverter with bit cells, thick wires, readouts, undo and typed values.
  - Bit-cell hit testing.
  - Width-mismatch rejection with its explanation.
  - A shared bus with tri-state drivers, contention, and the status bar message.
  - A bus project survives Save, New Project and Open.
- Manual: play-mode screenshots of a 4-bit inverter (thick bus wire, splitter/merger bars, "A = 10" /
  "OUT = 5"), TRI and JOIN blocks, and an 8-bit input showing 165 = 10100101 with the inspector bus fields.

### Known issues / deferred
- A bus join's driver count and a component's width are fixed when it is placed. To change one, place a new
  component (the library fields set the size). (Resolved: inspector Size section, see "Editor quality: change
  width and style after placement".)
- Signal animation runs per bit net. On a bus wire the travelling front uses the changed bit's colours and
  settles to the aggregate state on arrival.
- Gates have no bus pins (AND/NOT stay single-bit); use splitters and mergers, or package bit-sliced custom
  gates. Primitive bit-slicing is in the compiler for future multi-bit primitives.
- Bus widths up to 32 bits. Truth tables of bus circuits count each bit as an input, so the normal limits
  apply.

## Phase D – Visual components (2026-11-14)

LEDs, seven-segment displays, binary displays, hexadecimal displays and numeric (decimal) displays. Displays
are pure observers: they read the nets wired to their inputs and drive nothing. The simulation engine,
compiler, truth tables and the built-in gate library (still exactly AND and NOT) are unchanged.

### Implemented
- **Model.** One new utility kind, `ComponentKind.Display = 9`, with a `DisplayStyle`
  (`Led`, `SevenSegment`, `Binary`, `Hex`, `Decimal`, `SignedDecimal`) stored on `ComponentRef`
  (`ComponentRef.Display(style, width)`, `ComponentRef.DisplayStyle`). `DisplayStyles` gives width limits,
  persistence ids and display names. `IsUtility` covers displays, so they never appear in truth tables or gate
  counts.
- **Signatures** (`BoundarySignatures.Display`): a seven-segment display has eight single-bit inputs
  a, b, c, d, e, f, g, dp (`BitPin(0..7)`); an LED has one input; the bus styles have one input of the display
  width (1–32 bits, signed decimal 2–32). No outputs. Width-mismatched connections are rejected as for buses.
- **Compiler and engine:** nothing to do. A display has no outputs, so it compiles to nothing; the presenter
  reads its input nets through `CompiledInstance.GetInputPinNet` (unconnected inputs read the floating LOW net).
- **Validation:** unconnected display inputs (e.g. an unused decimal point) are not reported as warnings.
- **Decoding** (`Rendering.Displays.DisplayEncoding`, pure and allocation-free): conventional seven-segment
  glyphs for 0–F, digit/lamp/input counts per style, right-aligned unsigned and two's-complement decimal digits
  with a minus sign, dashes when a bus bit is UNKNOWN or Z, and a plain-text reading (`FormatValue`: "ON",
  "b c", "10100101", "A5", "165", "-91", "?").
- **Rendering.**
  - `GateLayout.DisplaySize` sizes each style; `GateGeometry` carries `DisplayStyle` and a rotated
    `DisplayArea`. Seven-segment displays label their pins a–g, dp.
  - `DisplayVisual` (owned and pooled by `GateView`) draws lamps (LED, binary; MSB on the left) or
    seven-segment digits on a dark screen, scaled upright into the display area. Lit segments use the HIGH
    colour and unlit ones a dim tone; a raw segment that is X or Z uses that signal's colour; an unreadable bus
    value shows dashes in the UNKNOWN colour. Sprites are created only when a larger display needs more.
  - `CircuitPresenter` registers one `DisplayInputs` target on every distinct net a display reads; any change
    re-reads those nets and redraws the display. With animation on, a display updates when the signal arrives
    over its wire (a seven-segment display per segment, bus displays as a whole).
- **Editor UI.** A "Displays" library section: LED, 7-Segment, and Binary/Hex/Decimal/Signed displays that
  follow the *Bus width* field. The inspector shows the display type, width and a live "Shows" reading.
- **Persistence (schema v4).** Node kind `display` with `style` (`led`, `sevensegment`, `binary`, `hex`,
  `decimal`, `signeddecimal`) and the usual optional `width`. `AddDisplaysMigration` (v3→v4) is a no-op step.

### Decisions
- **One `Display` kind with a style** instead of five kinds: one switch case per site, and a new style only
  touches `DisplayStyles`, `DisplayEncoding`, the layout and the library.
- **Displays read the model, not the engine.** No simulation change and no evaluation cost, and displays work
  at every hierarchy level (a display inside a custom gate shows that instance's values when inspected).
- **Raw-segment seven-segment display plus decoded hex/decimal displays.** Users can build their own BCD or
  hex decoders from AND/NOT and drive the raw segments, or show a bus value directly.
- **Decoding is pure and kept apart from the views,** so it is unit-tested without a scene.

### Test results
- EditMode: **346 / 346 passed** (11 new, `DisplayTests`): references and width limits (primitives still 2),
  signatures, hex glyphs, digit counts, hex/decimal/signed encoding including -128, dashes for unreadable
  values, text readings, layout (labelled seven-segment pins, rotated display area), validation (no unused-input
  warnings, width mismatch rejected, still compiles), v4 round trip, and v3 files still load.
- PlayMode: **66 / 66 passed** (5 new, `DisplayWorkspaceTests`): library entries follow the bus width; hex,
  decimal, signed and binary displays show 0xA5 as A5 / 165 / -91 / 10100101 and follow undo; LED and
  seven-segment follow single-bit inputs; displays update when an animated signal arrives; a display project
  survives Save, New Project and Open.
- Manual: play-mode screenshot of an 8-bit input driving hex, decimal, signed and binary displays, an LED, and
  a seven-segment digit drawing "3" with the decimal point.

### Known issues / deferred
- A display's style and width are fixed when it is placed (as for bus components); place a new one to change
  them. (Resolved: inspector style picker.)
- LEDs have a single colour (the HIGH signal colour); per-LED colours would need one more `ComponentRef`
  parameter. (Resolved: LED colours, see "Editor quality: LED colours".)
- Decimal displays are integer only (no fixed-point or BCD input mode); BCD can be shown with 4-bit hex
  displays, one per digit.

## UI and art redesign (2026-11-15)

Goal: a minimal, calm, satisfying interface that does not overwhelm a newcomer.

### Implemented
- New design system (ARCHITECTURE §18): floating cards over a full-bleed workspace, an ink-blue palette with a
  periwinkle accent and teal HIGH glow, Nunito text font, Lucide icons, tooltips, switch toggles and primary
  buttons.
- Toolbar: about 20 text buttons replaced by a project pill with a file menu and a centred icon tool bar.
  Selection tools appear only while something is selected. Zoom, frame and grid moved to a bottom-right
  cluster.
- Progressive disclosure:
  - The properties card shows only on selection, or when pinned.
  - The breadcrumb pill shows only inside custom gates.
  - The status bar became a pill that expands into details.
  - In the library, advanced sections start collapsed, search expands matches, and descriptions moved into
    tooltips.
  - The inspector drops the node ID and empty pin rows, and shows clock settings only when the circuit has a
    clock.
- First-run hint on the empty canvas.
- Workspace art: dot grid with vignette, soft gate shadows, glowing HIGH wires (also during propagation
  animation), glowing lit switches and lamps, quieter LOW pins, thinner outlines.
- Dialogs: dimmed scrim, card windows, accent primary actions, icon close and reorder buttons, subtler
  truth-table stripes.

### Decisions
- All public UI members used by tests and other systems were kept (`TitleText` still ends with "*" when
  unsaved; the dot is presentation only). The file-menu items are the same `NewButton`/`OpenButton`/... buttons.
- Library tiles show the first letter of a gate's name (or the custom icon text) rather than per-gate
  symbols, so no gate-specific presentation is hardcoded.
- The theme asset now stores only material and font references, so code defaults define the palette. Saving
  new values from the Inspector still overrides them.

### Test results
- EditMode: **346 / 346 passed**. PlayMode: **66 / 66 passed** (including the thousands-of-gates
  performance test with wire glow enabled).
- Manual: play-mode screenshots of the empty state, a NAND circuit, the truth-table dialog, the file menu and
  a tooltip.

### Known issues / deferred
- The breadcrumb pill and the signal settings popover share the top-centre position. When both are open
  inside a custom gate, the popover covers the crumbs until it is closed. (Resolved: the popover opens below
  the breadcrumbs, see "Known-issue cleanup".)
- The fonts use the Latin subset. Missing glyphs (such as →) fall back to LiberationSans. (Resolved: see "Known-issue cleanup 2".)

## Wire rendering and routing (2026-11-15)

Goal: wires that look right. Before this change, glow showed seams at corners, overlapping fan-out wires
stacked brighter, edges were aliased, branch points were unmarked, and wires ran straight through unrelated
gates.

### Implemented
- `DlsWire.shader` and the `DlsWire.mat` material: wires are drawn as signed-distance capsules with
  anti-aliased edges, round joints and a soft glow. `BlendOp Max` makes overlaps merge instead of brighten.
- `WireView` rewritten from two LineRenderers to one quad-per-segment mesh plus a `MaterialPropertyBlock`.
  The public API is unchanged except that `Line` was replaced by `Width`, `PointCount`, `GetPathPoint` and
  `SetJunction`. Transitions keep the travelling front, now with a soft bright head.
- Junction dots where a fan-out wire leaves the shared trunk (`CircuitPresenter.UpdateJunctions`).
- Obstacle-aware routing: wires detour around gates in their way. `SpatialHash` indexes gate bodies and wire
  bounds, so adding, moving or deleting a gate reroutes only the wires it blocks or unblocks.
- Removed the unused `RenderAssets.GlowStrip` and `SortingOrders.WireGlow`.

### Test results
- EditMode: **350 / 350 passed**. New tests cover the obstacle detour, ignoring off-path obstacles, shared-trunk
  measurement, and invalidation when a gate is added across a wire and moved away.
- PlayMode: **66 / 66 passed**. The thousands-of-gates test still reroutes exactly 2 wires on a move.
- Manual: play-mode screenshots of a detour around a gate, a LOW fan-out with a junction dot, and a HIGH
  fan-out with merged glow.

### Known issues / deferred
- Routing is still candidate-based rather than a full maze search. In very dense areas a wire can still
  cross a gate when no candidate shape avoids every obstacle (capped at 10 per wire).
  (Resolved: maze routing, see "Editor quality: maze wire routing".)
- Wires do not avoid each other. Parallel wires from different sources can overlap on the same lane.
  (Resolved: lane penalty in the maze router.)

## Editor quality: maze wire routing (2026-11-16)

### Implemented
- `Rendering/Wires/GridRouter.cs`: an A* maze router over a 0.5-unit rectilinear grid, with lines through the
  stub ends.
  - Cost: length + bends + a lane penalty for running along another net + a small crossing cost.
  - Every gate in the search area is an obstacle.
  - Hand-written binary heap and static growing buffers.
- `CircuitGeometry` uses the maze router for settled wires.
  - It collects every gate body and other nets' cached segments in the search area.
  - It retries with a wider area, then falls back to `WireRouter` (`RouteFallbackCount`).
- Interactive routing: dragging gates uses the fast candidate router. Releasing (or cancelling) the drag
  reroutes those wires with the maze router (`CircuitPresenter.SetInteractiveRouting`, called from
  `WorkspaceInteraction`).

### Decisions
- Fast router while dragging, maze router at rest. Maze routing every wire on every drag frame was
  unnecessary, and the candidate router is what users already see while dragging.
- The search is bounded (area margin, line, node and expansion caps), with a fallback, so one pathological
  wire cannot stall the editor.
- Lane occupancy uses the existing cached routes and wire spatial index. It is not a separate global map,
  so only wires near the one being routed are considered.

### Test results
- EditMode: **359 / 359 passed**. New `GridRouterTests` cover:
  - the straight case;
  - a detour around a row of gates;
  - pin normals;
  - lane avoidance;
  - determinism;
  - failure when walled in;
  - zero allocation after warm-up;
  - drag-then-settle in `CircuitGeometry`;
  - a geometry-level row of gates.
- PlayMode: **67 / 67 passed**. The new `DenseGrid_MazeRoutingStaysResponsive` test uses 1,200 gates and
  1,102 wires that must weave between them. Results: load 1,463 ms (views and routing) with 0 fallbacks,
  drag frame 11 ms, settle 2 ms.

### Known issues / deferred
- Routes depend on the order wires are routed in. A wire routed early is not rerouted when a later one
  could have made room for it. (Resolved: rip-up and reroute, see "Editor quality: rip-up-and-reroute and
  wire crossings".)
- Crossings are allowed, with a small cost. There are no wire bridges or hops. (Resolved: hop, gap or plain
  crossings.)

## Editor quality: open custom gates from the library (2026-11-16)

### Implemented
- New `NavigationLevelKind.Preview`. `CircuitNavigator.TryOpenDefinition` / `CanOpenDefinition` and
  `WorkspaceController.OpenDefinition` / `CanOpenDefinition` open a published gate without an instance.
  - It is read-only, with its own simulation session, and its inputs can be toggled.
  - It replaces the levels above the main circuit.
- The library context menu has "Open Internal Circuit" for custom gates. Double-clicking a custom gate in the
  library opens it too, and the tooltip says so.
- Edit works from a preview. Discarding the draft returns to the preview, and leaving it releases the preview
  session.
- The navigation bar shows "PREVIEW · READ-ONLY" and the Edit button. The inspector explains that the preview
  is isolated.
- `NavigationLevel.IsEditable` is now true only for Root and Draft levels (it used to mean "not Inspect").

### Test results
- EditMode: **365 / 365 passed**. New `PreviewNavigationTests` cover:
  - an isolated session;
  - replacing opened levels;
  - nested open from a preview;
  - edit and discard from a preview;
  - refusing a missing definition or an unsaved draft;
  - dropping the preview when its definition is removed.
- PlayMode: **68 / 68 passed**. The new `OpenFromLibrary_PreviewsGateWithoutAnInstance` test checks that the
  main circuit is untouched, the preview is read-only, and edit, discard and back all work.
- Fixed a timing flake in `SequentialWorkspaceTests.TruthTable_RefusesClockedCircuits_…`. The live clock
  could tick between frames before the assertion. The test now measures only across the synchronous
  analysis run.

## Editor quality: change width and style after placement (2026-11-16)

### Implemented
- `Commands/Circuit/ChangeComponentCommand`: swaps a node's component for another of the same kind.
  - It keeps the node's id, position, rotation, label and interface port.
  - It reconnects every original connection that still passes `ConnectionRules` (the pin exists and the
    widths match).
  - Undo restores the node and all original connections. `NodeSnapshot.WithComponent` supports this.
- `WorkspaceController.ChangeComponent` (by component, or by width / drivers / style) and `TryBuildVariant`.
  `IsResizable` covers bus boundaries, splitters, mergers, bus joins, tri-state drivers and displays.
  - Dropped connections are reported, and the message says undo brings them back.
  - Resizing an input switch also records its value, so undo restores every bit even after narrowing.
- Inspector "Size" section for editable resizable components: a width field, a driver-count field (bus
  join) and a display style picker (LED, 7-seg, BIN, HEX, DEC, S-DEC). Switching style clamps the width
  to what the new style supports.

### Test results
- EditMode: **370 / 370 passed**. New `ChangeComponentCommandTests` cover:
  - narrowing a splitter, with undo and redo;
  - widening a merger;
  - a bus boundary keeping its interface port and order;
  - variant validation;
  - rejecting a different kind.
- PlayMode: **69 / 69 passed**. The new `ChangingDisplayStyleAndBusWidth_AfterPlacement` test covers style
  changes from both the workspace and the inspector buttons, narrowing a bus, and undoing back to the
  original value.

## Editor quality: LED colours (2026-11-16)

### Implemented
- `Core/Nodes/LedColors`: six colours (signal, red, orange, green, blue, pink) with stable ids.
- `ComponentRef.LedColor` and `WithLedColor`. The colour is part of the reference's equality, hash and
  `ToString`, and `Utility(...)` takes it for loading. It never changes pins or simulation.
- `VisualTheme.LedColor(i)` holds a serialized palette, and index 0 is the HIGH signal colour.
  `GateGeometry.LedColor` carries the colour to `DisplayVisual`, which lights LEDs in it.
- Persistence schema **v5**: optional `"color"` on display nodes, written only when it is not the default.
  `AddLedColorsMigration` is a no-op, and unknown ids fall back to the default.
- `WorkspaceController.ChangeLedColor`. An undoable inspector swatch row marks the current colour with a check
  mark. Switching to another style and back to LED keeps the colour.

### Test results
- EditMode: **372 / 372 passed**. New tests check:
  - the colour is part of the reference, is validated, and leaves pins and layout unchanged;
  - the colour survives a save and load;
  - the default colour is omitted from files;
  - a v4 file migrates to the default colour;
  - an unknown id falls back to the default.
- PlayMode: **70 / 70 passed**. The new `LedColour_FromTheInspector_SurvivesSaveAndOpen` test covers the
  swatch click, the wire being kept, the logic being unchanged, save and reopen, and undo.

### Known issues / deferred
- Project files saved by this version (schema 5) cannot be opened by older builds. The newer-version error
  message explains this.

## Fix: button colour flicker on input toggle / drag-select (2026-11-16)

### Cause
- Toggling an input (a history change) and drag-selecting (selection changes on most frames) both rebuild
  the inspector card, so every button in it is destroyed and created again.
- A new `Button` takes Unity's default white tint in `OnEnable`. Assigning our `ColorBlock` afterwards then
  starts a 0.08 s cross-fade from white to the real colour.
- Buttons that are set `interactable = false` after creation also faded from bright to dim.
- Repeated rebuilds during a drag-select turned this into a continuous flicker.

### Fix
- `UiFactory.SetColorsInstant` assigns a colour block with a zero fade first, so the tint jumps straight to
  its target, and then restores the fade. It is used for every button built by `UiFactory` and for the
  per-site overrides: LED swatches, breadcrumbs, context-menu rows, the file menu, and the first state of
  `ButtonVisual`.
- `UiFactory.SnapTransitions(root)` snaps every selectable under a root to its current state. It runs at the
  end of `InspectorPanel.Rebuild`.
- Hover, press and real interactable changes still fade.

### Test results
- EditMode: **372 / 372 passed**.
- PlayMode: **71 / 71 passed**. New test `UiFlickerTests.RebuiltInspectorButtons_ShowTheirRestingTintImmediately`
  checks that every inspector button shows its resting or disabled tint in the same frame as a rebuild, and
  again after an input toggle. Before the snap was added, this test caught the disabled Truth Table button
  still mid-fade.

## Fix: context menu had no background (2026-11-16)

- Cause: `UiFactory.Card` keeps the card's shadow, fill and border as child decorations of the menu.
  `ContextMenuView.Show` / `ShowForLibraryEntry` cleared the old rows with `UiFactory.DestroyChildren(_menu)`,
  which destroyed those decorations too. From the first time it opened, the menu was rows on a transparent
  hit image.
- Fix: the new `ContextMenuView.ClearItems()` destroys only the row buttons it created.
- Test: `UiFlickerTests.ContextMenu_KeepsItsBackground_AcrossReopens` opens the menu on empty space, then on a
  node, then on empty space again. Each time it checks that the shadow, fill and border are present, that the
  fill uses the theme colour, and that old rows don't pile up.
- Results: EditMode **372 / 372**, PlayMode **72 / 72**.

## Editor quality: rip-up-and-reroute and wire crossings (2026-11-16)

Goal: settled routes that do not depend on the order wires were routed in and that improve when room opens
up, and crossings of unrelated nets that can't be mistaken for connections.

### Implemented
- **Rip-up and reroute** (`CircuitGeometry.ImproveRoutes`, run by the presenter after each change, after
  `Bind` and when a drag settles). It reconsiders:
  - wires shaped by other nets near a dropped route;
  - other nets' wires overlapping a new route;
  - wires lying on the ideal (lane-free) route of a newly routed wire that had to detour.
  Detoured wires' ideal routes become soft lanes (`GridRouter.SoftLaneCost`), so an earlier wire can step
  off a lane it took only by tie-breaking. A new route is accepted when `GridRouter.PathCost` (same lanes)
  drops by more than 0.05, or when it yields to a demand route while giving up at most two crossings' worth
  of cost. Each wire is tried at most twice per pass. The budget is 64 searches per change, or
  `max(64, wires / 4)` on bind. Nothing runs during a drag.
- `GridRouter`:
  - `LastLaneInfluenced`, `PathCost`, `SoftCost` and `OverlapLength`, all allocation-free;
  - a soft-lane overload of `TryRoute`;
  - lanes claim parallel lines closer than `LaneProximity` (0.3), so a wire on an off-grid stub line no
    longer gets a neighbour 0.25 away;
  - the straight-wire shortcut now also requires that no other net runs along the line.
- **Crossings.** `Rendering/Wires/WireCrossings` finds where a wire's vertical segments cross other nets.
  Fan-out siblings and touches near bends and pins are excluded.
  - `CircuitGeometry.GetCrossings` and `DrainCrossingChanges` cache the points and queue changes.
  - `WireView.SetCrossings` draws a semicircular hop, a gap (split quad, path distances kept), or a plain
    crossing. Only the vertical wire is decorated. Routing, hit-testing and junctions are unchanged.
- **Setting.** `WireCrossingStyle` (Hop by default):
  - set through `CircuitPresenter.SetWireCrossingStyle` and `WorkspaceController.WireCrossingStyle`;
  - a Hop / Gap / Plain row in the Signal Settings popover (the current choice has an accent, bold label);
  - persisted as the optional `settings.wireCrossings`. A missing field loads as Hop, so there is no schema
    bump.
- Diagnostics: `CircuitGeometry.ImprovementAttempts` / `ImprovedRoutes` and
  `CircuitPresenter.WireImprovementCount`.

### Decisions
- **Only the vertical wire is decorated.** Rectilinear crossings always pair one vertical and one horizontal
  segment, so the rule is deterministic and the horizontal wire (often a long bus or signal line) stays
  straight.
- **Hops are drawn, not routed.** The geometry path stays orthogonal, so routing costs, hit-testing,
  junction dots and simulation never see the hop.
- **Acceptance by cost decrease** rather than a global negotiated-congestion loop. It is local, bounded and
  monotone in the common case, and cheap enough to run on every change.
- **Straight shortcut respects lanes.** Before this change a straight wire could be drawn on top of another
  net, which is the worst possible routing result. The search now decides, and the improvement pass usually
  moves the other wire out of the way.

### Test results
- EditMode: **395 / 395 passed** (23 new).
  - `RouteImprovementTests` (9):
    - an earlier wire steps aside so a detoured wire runs straight;
    - the result is the same in either connection order;
    - convergence (a second pass changes nothing);
    - a wire straightens when the wire in its way is removed, or its gate moves;
    - nothing happens during a drag, and it runs on settle;
    - unrelated wires are not searched again;
    - the budget is respected.
  - `WireCrossingsTests` (7): a crossing is found on the vertical wire only; T-touches, near-bend points and
    duplicates are ignored; fan-out wires don't cross; moving or removing a wire queues the wires it crossed.
  - `GridRouterTests` (+6): lane influence, `PathCost` terms, the searched route beats an overlapping
    straight one, soft lanes break ties, no allocations, and too-close lanes are avoided.
  - `PersistenceTests`: the setting round-trips, and a missing setting loads as Hop.
- PlayMode: **77 / 77 passed** (5 new, `WireCrossingWorkspaceTests`):
  - Hop is the default, and only the vertical wire hops (more points, longer drawn length);
  - Gap and Plain change only the drawing;
  - a signal travels over a hop and settles on the right value;
  - moving gates updates the crossings;
  - popover buttons set the style, which survives Save, New and Open.
- Performance (`DenseGrid_MazeRoutingStaysResponsive`, 1,200 gates and 1,102 wires; baseline measured on the
  same machine and day, before the change):

  | | Load | Drag frame | Settle |
  |---|---|---|---|
  | Before | 2,588 ms | 20 ms | 3 ms |
  | After | 2,703 ms | 16 ms | 24 ms |

  Load and drag frame are within run-to-run noise. Settle grew because of 35 improvement searches, 3 of
  them accepted. The thousands-of-gates test still reroutes only the affected wires.
- Manual: play-mode screenshots of hops (including a LOW wire hopping a HIGH one), gaps, and the popover
  with the crossing row. This check found the 0.25-unit near-parallel lanes that led to `LaneProximity`.

### Known issues / deferred
- Settling a drag in a very dense area costs about 20 ms more, for the improvement searches.
- Improvement is local: a better layout that needs several wires to move together at a temporary cost is not
  found.
- Hops always bulge towards +x. Crossings closer than a hop's diameter on one segment are drawn as one hop
  (the second is skipped). (Resolved: hop sides and bridges, see "Known-issue cleanup".)

## Known-issue cleanup (2026-11-17)

Three of the four small known issues are fixed. The fourth, the font fallback, is still open (see below).

### Implemented
- **Signal settings popover vs breadcrumbs.** `SignalSettingsPanel` now takes the `NavigationBar`. While the bar
  is shown, the popover opens below it (`TopOffset`), and it moves again on `NavigationChanged` and
  `StatusChanged`, so it follows navigation while open.
- **Clock dividers** (schema v6):
  - Each clock node has a divider, ÷1 … ÷64 (`ComponentRef.ClockDivider`/`ClockExponent`, `ClockDivided`,
    `WithClockDivider`). It is stored in the reference's arity byte, so equality and hashing already cover it.
  - The compiler gives each divider its own net (`CompiledCircuit.GetClockNet(k)`, `ClockDomainCount`).
    Clocks with the same divider share that net in every scope, custom gates included. `ClockNet` is the
    fastest domain present.
  - `ClockSchedule.Level(k, edge)`: the project frequency is the base rate. After base edge E, domain k is
    HIGH when `(E + 2^k - 1) >> k` is odd, so every domain rises with the base clock on edge 1.
  - `SimulationContext.ApplyClockEdge(edge)` (allocation-free) drives all present domains. `SetClock` drives all of
    them at once. `SimulationRunner` and `SequenceAnalyzer` both go through `ApplyClockEdge`, so a sequence
    step is still one base cycle.
  - UI:
    - The inspector has a divider picker (÷1 … ÷64), shows the resulting rate (e.g. "0.5 Hz (base ÷ 4)"),
      and labels the type "Clock ÷4".
    - The frequency field is now "Base rate (Hz)".
    - The clock box reads "CLK ÷4".
    - The status bar adds "· N clocks" when more than one domain is present.
    - `WorkspaceController.ChangeClockDivider` makes the change undoable through `ChangeComponentCommand`.
  - Persistence: an optional node `divider`, written only when above 1. `AddClockDividersMigration`
    (5→6) is a no-op. An invalid divider loads as ÷1.
- **Hop sides and bridges:**
  - `WireCrossings.Find` returns `(x, y, side)`. A hop bulges towards -x when another net's vertical wire or a
    gate body lies within `HopReach` (0.354) on the +x side and -x is free. Otherwise it bulges towards +x.
    Wires on the usual half-unit lanes never block a hop.
  - `CircuitGeometry` gathers the gate bodies near a wire for this.
  - `WireView.AddHops` merges crossings closer than `2r + MergeSpacing` into one bridge (a quarter arc up, a
    straight run, a quarter arc down). A single crossing is still a semicircle. Gap style merges
    overlapping gaps the same way.
  - `WireView.DrawnPoints` exposes the drawn path for tests.

### Decisions
- **Dividers, not free frequencies.** Power-of-two dividers of one base clock keep every edge on one timeline:
  domains stay in phase, timing is deterministic, and a sequence-analysis step keeps its meaning. Unrelated
  frequencies would need an event-time scheduler. Nothing in the reference material needs one yet.
- **Divider is part of the component reference**, like the LED colour. Changing it is a `ChangeComponentCommand`
  and needs no new command type.
- **Default hop side stays +x**, so existing drawings don't change unless something is in the way.

### Test results
- EditMode: **405 / 405 passed** (10 new):
  - `ClockDividerTests` (7):
    - the ÷1/÷2/÷4/÷64 schedule;
    - divider on `ComponentRef` (validation, clamping, equality);
    - same-divider nets shared across the hierarchy;
    - runner ticking per domain and reset;
    - a single base clock behaves as before;
    - ticking several domains allocates nothing;
    - sequence analysis steps of one base cycle.
  - `PersistenceTests` (+1): divider round trip, a v5 file loading as ÷1 through the v5→v6 migration, and a
    damaged divider falling back to ÷1.
  - `ChangeComponentCommandTests` (+1): changing a divider keeps the wire, and undo/redo work.
  - `WireCrossingsTests` (+1): the hop side defaults to +x and flips away from a wire or gate. Both sides
    blocked keeps +x; half-unit lanes and distant wires don't block.
- PlayMode: **80 / 80 passed** (3 new):
  - The popover opens below the breadcrumbs and moves back at the top level.
  - The inspector divider picker: box title, status bar, per-domain tick traces, Save/Open, undo.
  - Close crossings share one bridge on their side; far-apart crossings each get a hop; gap style merges.
- Manual (play mode, screenshots):
  - ÷1 and ÷4 clocks driving LEDs: inspector picker, "Runs at 0.5 Hz (base ÷ 4)", status bar "2 clocks".
  - A vertical wire hopping two horizontal wires.
  - The play-mode check found that the type row still said "Clock (global)" and that the frequency label was
    cut off. Both are fixed.

### Known issues / deferred
- **Missing glyphs (not fixed).** (Resolved: see "Known-issue cleanup 2".)
  - The bundled Nunito TTFs are Latin subsets. Today → comes from TMP's dynamic LiberationSans fallback
    and ÷ from the static LiberationSans asset, so these characters render, but in a different typeface.
  - The planned fix, building a fallback from an OS font at runtime, does not work with TMP 3.0.9:
    `TMP_FontAsset.CreateFontAsset(Font)` returns null for `Font.CreateDynamicFontFromOSFont`, and the
    family-name overload only exists in TMP 3.2+.
  - The remaining option is to bundle a wider OFL font, such as full Nunito or a Noto symbols font, as an extra
    fallback. That adds a third-party asset, so it waits for a decision.
- A gate that moves next to a wire without rerouting it keeps the wire's old hop side until that wire's
  crossings are recomputed. (Resolved: see "Known-issue cleanup 2".)

## Known-issue cleanup 2 (2026-11-17)

The remaining small known issues from earlier phases. Two had already been fixed by later work and only needed
a test and a note; four are fixed here.

### Already fixed (now covered by tests)
- **Animation inside an inspected gate.** Phase 6 recoloured inner nets instantly. Since Phase 5's per-instance
  inspection, `CircuitPresenter.SetSimulation(context, scope)` maps the inspected instance's nets, and the
  workspace feeds the root change log to the animator, so inner wires get pulses like any other.
- **Truth table from the library.** A library item's context menu has had a Truth Table entry
  (`ComponentTruthTableRequested` → `TruthTableDialog.OpenForComponent`) since the library preview work.

### Implemented
- **Full Nunito.** The three bundled TTFs were Latin subsets (230 glyphs). They are replaced by static Regular
  (400), SemiBold (600) and Bold (700) instances of the official variable font from `googlefonts/nunito` (SIL OFL
  1.1, same copyright line), cut with fontTools in a throwaway environment; nothing new is added to the project.
  - Each file has 938 glyphs. Every non-ASCII character in the runtime UI strings (÷ × ≈ ≥ › … – • · °) now
    renders in Nunito. The `.ttf.meta` GUIDs are unchanged, so the SDF assets kept their references; their
    dynamic atlases were cleared once to regenerate.
  - `→` is not in Nunito either, but it appears only in code comments, never on screen (breadcrumbs use the
    Lucide chevron). LiberationSans stays as the last fallback for text users type.
- **Hop side after a nearby gate moves.** `CircuitGeometry.UpdateNodeIndex` now also calls
  `DirtyCrossingsNear` for the gate's old and new bodies: every wire with cached crossings within
  `WireCrossings.HopReach` of them is queued. Before, only wires within the body margin (0.25) were touched,
  through rerouting, so a gate 0.25–0.354 from a wire's edge could block or free a hop without updating it.
- **Main clock keeps running during a draft.** `WorkspaceController.LateUpdate` also advances the root session's
  runner when a draft or preview is shown, and clears the root's change log (nothing draws it). Returning
  rebinds the presenter, which redraws from the live context.
- **Sequence analysis up to 1 024 steps.** `MaxDisplaySteps` is now `SequenceSpec.DefaultMaxSteps`. The table is
  a fixed header above a virtualised list: 32 pooled row texts, refilled on scroll with
  `TMP_Text.SetText(StringBuilder)`. One text block of 1 025 rows would exceed TMP's mesh limit. New
  `DisplayedRowCount`, `GetVisibleRowText(row)` and `ScrollToRow(row)`; `TableText` returns the header and the
  rows in view.

### Decisions
- **Instances, not the variable font.** The variable file's default instance is weight 200 (ExtraLight), and the
  existing font assets expect one file per weight. Static instances keep those three assets as they are.
- **Background root clock, no background drawing.** The draft's views are what is on screen; drawing the main
  circuit's changes would be wasted work, and a full redraw on return is already part of navigation.

### Test results
- EditMode: **406 / 406 passed** (1 new): a gate moved within hop reach of a wire's edge flips the hop to -x
  without rerouting either wire, and moving it away flips it back. With the fix disabled the test fails
  ("V's hop side depends on G").
- PlayMode: **83 / 83 passed** (3 new):
  - Inside an inspected NAND, raising both main inputs creates pulses on the inner wires; the NOT output keeps
    its old value until the pulse arrives, and the simulated output is already correct.
  - The main clock ticks while a draft is edited, its change log stays empty, and the lamp view shows the live
    value after returning.
  - A 1 024-step sequence: 1 025 rows, fewer than 100 text objects in the dialog, the last row appears after
    scrolling, Copy has every row, and 1 025 steps are refused with the limit message.
- Manual (play mode screenshot): a ÷1 and a ÷4 clock analysed over 1 024 cycles, scrolled to cycle 1010; the
  header stays in place and the columns line up.

### Known issues / deferred
- Decimal displays are still integer only (no fixed-point or BCD mode).
- No native OS file dialog (Phase 8 decision).
- `com.coplaydev.unity-mcp` still tracks `#main`; pin or remove it before release builds.

## Phase E – Computer components: example gate pack (2026-11-17)

As in Phase B, no primitive was added. The computer parts are ordinary custom gates built from AND and NOT, shipped
as an optional pack that File → "Add example gates" puts into My Gates. A new project still offers only AND and NOT
(acceptance step 3).

### Implemented
- **`GateLibrary/Examples/ExampleGatePack.cs`.** 17 gates, each from AND/NOT, splitters/mergers and earlier pack
  gates (referenced, never copied):

  | Category | Gates |
  |---|---|
  | Examples · Logic | NAND, OR, NOR, XOR |
  | Examples · Routing | MUX 2:1, MUX 4:1 (S is a 2-bit bus), Decoder 2→4, Decoder 4→16 |
  | Examples · Arithmetic | Half Adder, Full Adder, Adder 4-bit (A, B, S are 4-bit buses), ALU 4-bit |
  | Examples · Memory | D Latch, D Flip-Flop (rising edge, master-slave), Register 4-bit (LOAD), Counter 4-bit (ripple), RAM 16×4 |

  - ALU: OP 0 add, 1 subtract (B XOR OP0, carry in OP0), 2 AND, 3 OR; flags COUT (no borrow when subtracting, 0
    for logic) and ZERO.
  - RAM: ADDR[4], DIN[4], WE, CLK → DOUT[4]; 64 D-latch cells, a 4→16 decoder for write enables, 20 MUX 4:1 for
    reads; 1 041 gates flattened. Change ADDR only while CLK is LOW.
  - Sequential parts take a CLK input, so they work with any clock and divider.
  - Fixed IDs (`example.*`): loading twice is refused; a taken name gets an "(example)" suffix.
- **`LayeredLayout`.** Column per logic depth (feedback ignored), inputs left, outputs right, barycentre placement
  with a top-down overlap sweep, snapped to the 0.5 grid.
- **`Rendering/GeometryNodeSizer`.** Measures node bodies with `CircuitGeometry` for the layout.
- **`WorkspaceController.LoadExampleGates()`.** Main circuit only; validates the staged pack with the packaging
  rules, then publishes it as one `CompositeCommand` (one undo removes it). Notification: "Added 17 example gates
  to My Gates." plus any renames.
- **UI.** File menu item "Add example gates" (`ToolbarView.ExampleGatesButton`, `ExampleGatesRequested`), wired
  in `AppShell`. The library already groups by category, so the pack appears under its four headers.

### Decisions
- **A pack, not built-in gates.** CLAUDE.md requires a new project to start with only AND and NOT, and every other
  gate to be constructible. An optional pack of ordinary custom gates keeps both, and doubles as a worked example
  users can open.
- **RAM from gates.** A RAM primitive would be faster and larger, but could not be opened and would break the
  AND/NOT principle. At 16×4 the gate version is about a thousand gates and simulates comfortably.
- **Level-sensitive RAM writes.** Edge-triggered cells would need 64 flip-flops (twice the gates). Latches plus the
  "change ADDR while CLK is LOW" rule match how simple SRAM is taught.

### Test results
- EditMode: **427 / 427 passed** (21 new, `ExamplePackTests`):
  - All 17 gates validate and contain only AND/NOT, boundaries, wiring and earlier pack gates. The Full Adder
    shares the Half Adder definition.
  - Exhaustive tables against C# references: NAND/OR/NOR/XOR, both multiplexers (64 rows for 4:1), both decoders,
    half/full adder, Adder 4-bit (512 rows), ALU (1 024 rows with COUT and ZERO).
  - D latch transparency and hold; DFF captures on rising edges only; register LOAD/hold; counter counts 20 edges
    with wrap; RAM writes and reads all 16 words, WE = 0 leaves data intact, overwrites keep neighbours.
  - RAM: one-bit address changes allocate nothing and cost fewer evaluations than one pass over the gates
    (measured 239–583 of 1 041); raising WE while CLK is LOW evaluates at most 2 gates.
  - Layout: no overlapping bodies, inputs left of outputs, in every gate.
  - Second load refused; a user NAND makes the pack's "NAND (example)"; undo/redo of the whole pack; JSON round
    trip, then 9 - 5 = 4 on the reloaded ALU.
- PlayMode: **85 / 85 passed** (2 new):
  - The File-menu item adds 17 items under the Examples headers; a second load is refused; undo/redo.
  - A placed ALU computes 12 - 5 = 7 and 12 + 5 = 1 (wrapped); opening ALU → Adder 4-bit shows three breadcrumbs,
    and the first one returns with the result unchanged.
- Manual (play mode screenshot): the ALU's internal layout reads left to right with no overlaps; its first gate
  column (12 gates) is tall but clear.

### Known issues / deferred
- Flip-flops power on with Q = 1 under the resolved power-on rule (loop nets are forced LOW one at a time, which
  leaves this master-slave pair HIGH), so the counter starts at 15 and reads 0 after the first edge. It is
  deterministic; a reset input would make the start state explicit.
- The RAM's DOUT can glitch through the read multiplexers while several address bits change at once (up to 17
  waves). It settles correctly; it only matters for animation.
- Generated layouts are tidy but not hand-tuned; the RAM (112 nodes) is large to browse.
- No CPU yet (out of scope for this phase).
