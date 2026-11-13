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
| 6 | Animated signal visualization | ⏳ Next |
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
