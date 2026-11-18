# Architecture – Digital Logic Sandbox

Status: **Phase 2 (simulation engine) complete.** Sections marked *(planned)* describe the committed design for
later phases; they become *(implemented)* as each phase lands. Changing a committed decision requires a
strong technical reason recorded in `DEVELOPMENT_LOG.md`.

---

## 1. Guiding principles

1. The circuit **model** (pure C# data) is the single source of truth. GameObjects are disposable views.
2. Only **AND** and **NOT** are logic primitives. Everything else is a packaged circuit definition.
3. Behaviour is derived from structure, never from names. No code anywhere may branch on a gate's name.
4. Simulation is headless and engine-free; visualization consumes simulation **events** and never feeds back.
5. Truth tables run the real engine in an **isolated context**; the live simulation is never touched.
6. Every persisted artefact is versioned (project schema and individual gate definitions).

---

## 2. Module layering (enforced)

Each module is its own assembly. Arrows mean "may reference". The graph is enforced by
`Tests/EditMode/ArchitectureTests.cs`, which fails if any assembly gains an unlisted dependency or if an
engine-free assembly references `UnityEngine`/`UnityEditor`.

```
                        ┌───────────── engine-free (noEngineReferences) ─────────────┐
 Utilities ◄─ Core ◄─ Components ◄─ Simulation ◄─┬─ GateLibrary ◄─┬─ Commands
                                                  │                └─ Persistence (+Newtonsoft.Json)
                                                  └─ TruthTable
                        └────────────────────────────────────────────────────────────┘
                        ┌──────────────── Unity-dependent ───────────────────────────┐
 Rendering (Core, Simulation) ◄─ Animation
 CircuitEditor (Core..GateLibrary, Commands, Rendering, Animation)
 TruthTable.UI (TruthTable, Simulation, TMP, UGUI)
 UI (everything above, TMP, UGUI)
 App (composition root: references everything; owns the scene bootstrap)
                        └────────────────────────────────────────────────────────────┘
```

| Assembly | Folder | Engine | Responsibility |
|---|---|---|---|
| `DigitalLogicSandbox.Utilities` | `Utilities/` | no | Pooled collections, ring buffer queue, ID generation, guards |
| `DigitalLogicSandbox.Core` | `Core/` | no | Circuit definitions, nodes, ports, connections, nets, signals, structural validation |
| `DigitalLogicSandbox.Components` | `Components/` | no | Component evaluation contract, AND/NOT primitives, custom-component references |
| `DigitalLogicSandbox.Simulation` | `Simulation/` | no | Hierarchy compiler (flattening), event-driven engine, contexts, diagnostics, change events |
| `DigitalLogicSandbox.GateLibrary` | `GateLibrary/` | no | Definition library (versioned), packaging + packaging validation, dependency graph |
| `DigitalLogicSandbox.TruthTable` | `TruthTable/` | no | Combination generation, isolated evaluation, CSV/text export, limits, cancellation |
| `DigitalLogicSandbox.Commands` | `Commands/` | no | Reversible command model, undo/redo history |
| `DigitalLogicSandbox.Persistence` | `Persistence/` | no | DTOs, JSON (de)serialization, schema migration, atomic save, autosave recovery |
| `DigitalLogicSandbox.Rendering` | `Rendering/` | yes | Pooled gate/wire/pin views, wire mesh generation, signal styling |
| `DigitalLogicSandbox.Animation` | `Animation/` | yes | Central signal-animation controller consuming simulation change events |
| `DigitalLogicSandbox.CircuitEditor` | `Editor/` | yes | Workspace, camera, selection, placement, wiring, hit-testing, navigation |
| `DigitalLogicSandbox.TruthTable.UI` | `TruthTable/UI/` | yes | Truth table panel |
| `DigitalLogicSandbox.UI` | `UI/` | yes | Toolbar, library panel, inspector, bottom panel, dialogs, context menus |
| `DigitalLogicSandbox.App` | `App/` | yes | Composition root, settings, application lifecycle |
| `DigitalLogicSandbox.Tests.EditMode` | `Tests/EditMode/` | editor | Headless unit/integration/architecture tests |
| `DigitalLogicSandbox.Tests.PlayMode` | `Tests/PlayMode/` | yes | Scene/view integration tests |

**Decision – `Editor/` folder vs. namespace.** The folder is named `Editor/` as specified, but the assembly
and namespace are `DigitalLogicSandbox.CircuitEditor`. Reasons: (a) a namespace segment called `Editor`
shadows `UnityEditor.Editor` inside any `DigitalLogicSandbox.*` code and breaks custom inspectors;
(b) Unity treats loose scripts in `Editor/` folders as editor-only. Because the folder is covered by an
asmdef, the special-folder rule does not apply; `ArchitectureTests.RuntimeAssembly_IsCompiledIntoPlayer`
proves the assembly ships in player builds.

**Decision – App assembly.** Added beyond the §17 list as the single composition root, so no feature module
needs to know about every other module.

**Decision – Newtonsoft.Json.** `com.unity.nuget.newtonsoft-json` (Unity-maintained, already in the package
cache) is an explicit dependency, referenced only by `Persistence` and tests. `JsonUtility` cannot express
dictionaries, polymorphic payloads or `JObject`-based schema migrations, which §13 requires.

**Decision – Input System.** `com.unity.inputsystem` 1.19.0 with *Active Input Handling = Input System Package
(New)*. Only `CircuitEditor`, `UI` and `App` may reference `Unity.InputSystem`. UI uses
`InputSystemUIInputModule` (not `StandaloneInputModule`). Editor interactions are driven by an
`InputActionAsset` so bindings (pan, zoom, delete, undo/redo, duplicate) are remappable in one place.

**Decision – DOTween (free, Demigiant).** Installed in `Assets/Plugins/Demigiant/DOTween` with the
`DOTween.Modules` asmdef. Only `Animation`, `CircuitEditor`, `TruthTable.UI`, `UI` and `App` may reference it.
DOTween drives *presentation* tweens (panel transitions, camera zoom/pan easing, gate placement/selection
feedback, pin highlights, indicator pulses). It never runs simulation logic. Per-wire signal propagation is
not one DOTween tween per wire: it stays in the central `AnimationController` tick (§8), because thousands of
concurrent wire highlights must be allocation-free and ordered by propagation wave.

**Third-party governance.** Every runtime asmdef sets `overrideReferences: true`, so precompiled DLLs
(DOTween.dll, Newtonsoft.Json.dll) are never auto-referenced. `ArchitectureTests` holds an allowlist of
which assembly may use which library and fails on anything else.

---

## 3. Circuit graph model *(implemented – Phase 1)*

```
CircuitDefinition                (Core.Circuit; frozen once published; identity = DefinitionId + Version)
 ├─ Id: DefinitionId             stable GUID string, never reused, never derived from the name
 ├─ Version: int                 explicit; a new version is CloneAsVersion(n) of the previous one
 ├─ Name, Description, Category, Icon, CreatedUtc, ModifiedUtc
 ├─ Inputs / Outputs: InterfacePort[]   (PortId, Name); display order = list order
 ├─ Nodes: NodeDefinition[]      (Core.Nodes)
 │    ├─ Id: NodeId              positive int, unique within the circuit, allocated by the circuit
 │    ├─ Component: ComponentRef → Primitive("builtin.and") | Custom(DefinitionRef) | InputBoundary | OutputBoundary
 │    ├─ Position: CircuitPoint (engine-free float grid units), Rotation (90° steps)
 │    ├─ Label                   node metadata
 │    └─ InterfacePort: PortId   boundary nodes only
 └─ Connections: ConnectionDefinition[]  PinRef(source node, port) → PinRef(dest node, port)
```

Key rules and how they are enforced:

| Rule | Enforced by |
|---|---|
| Unique node ids; restore keeps ids (`RestoreNode`/`RestoreBoundary`) | `CircuitDefinition` |
| One driver per input pin (connections keyed by destination) | `CircuitDefinition.TryConnect` |
| No connection from a node to itself | `CircuitDefinition.TryConnect` |
| Removing a node removes its connections; removing a boundary removes its interface port | `CircuitDefinition.RemoveNode` |
| Published definitions are immutable | `Freeze()`; every mutator throws afterwards |
| Pins exist and have the right direction | `ConnectionRules.Check` (editor) / `CircuitValidator` (load, package) |

- **PortIds are unique across all of a component's inputs and outputs** and independent of display order,
  so renaming or reordering an interface never breaks connections in circuits that use the gate.
- **Boundary nodes** (input switches / output indicators) are *utility components*, not logic gates. Each
  is bound one-to-one to an `InterfacePort`, so packaging needs no separate mapping layer. Their fixed pin
  layout is `BoundarySignatures` (single pin, id 0).
- **Nets** are derived, never stored (`NetBuilder`): one driver output plus all inputs it drives.
  Unconnected inputs read `Low` and produce a warning.
- **Signal** is `enum Signal : byte { Low, High, Unknown, HighImpedance }`.
- **Change notification.** Every mutation raises `CircuitDefinition.Changed` with an allocation-free
  `CircuitChange` (kind + node/port/connection). Views, the simulation and the inspector sync from this
  event, which is also what lets undo restore the visuals automatically.
- `ComponentSignature` is the one shape description for every component kind (ordered `PortDescriptor`s).
  A custom gate's signature is derived from (and cached by) its definition's interface.

### Validation (`Core.Validation`)

`CircuitValidator` reports `Diagnostic`s (severity, code, actionable message, node/pin/definition for UI
highlighting) into a reusable `ValidationReport`:

- Errors: missing component/definition, invalid source/destination pin, dangling connection, boundary/port
  mismatch, duplicate port name within a direction, no outputs (when packaging), combinational cycle,
  circular reference, hierarchy too deep.
- Warnings: unconnected gate input, unused interface input (allowed intentionally), undriven output,
  unnamed port.
- **Cycles:** iterative Tarjan SCC (`Utilities.CycleFinder`, no recursion, tested with 100k vertices) on
  the node graph. Custom gates are treated conservatively as all-inputs→all-outputs. Wiring a loop is
  allowed in the editor (future latches need feedback); it is reported, not blocked.
- **Hierarchy** (`HierarchyValidator`): DFS over custom references with memoised subtree heights. A
  definition may not contain *any version of itself*, directly or transitively. Recursion is bounded by
  `MaxHierarchyDepth` (default 32).

## 4. Components and evaluation contract *(implemented – Phase 1)*

`IPrimitiveComponent` (`Components.Abstractions`) declares `Id`, `DisplayName`, `Signature` and
`void Evaluate(ReadOnlySpan<Signal> inputs, Span<Signal> outputs)`. Implementations are stateless,
allocation-free singletons shared by every instance and context. `AndGate` and `NotGate` are the only
implementations, and `PrimitiveRegistry.CreateDefault()` registers exactly those two. They use 3-valued logic:
AND gives LOW if any input is LOW, else HIGH if all are HIGH, else UNKNOWN; NOT inverts binary values and
maps the rest to UNKNOWN.

`ComponentCatalog : IComponentResolver` is the only place that dispatches on `ComponentKind`: primitives come
from the registry, custom gates from an `IDefinitionProvider` (the `DefinitionLibrary`), boundaries have fixed
signatures. Everything else (validation, editor, renderer, simulation compiler) asks the resolver, so **no
gate-specific code exists outside the two primitive classes**. Sequential primitives (Phase B) will add a
separate `ISequentialComponent` with `Commit()`, keeping combinational evaluation pure.

`DefinitionLibrary` (`GateLibrary.Custom`) stores published versions keyed by id and version, freezes them on
insertion, and never replaces them. It exposes the latest version and the next version number, and raises
`DefinitionAdded` so the library panel can refresh.

## 5. Simulation lifecycle *(implemented – Phase 2)*

```
CircuitDefinition ──CircuitCompiler──► CompiledCircuit (immutable, shareable)
   (+ ComponentCatalog)                   │  CSR arrays: gate inputs/outputs, net readers, net drivers
                                          │  CompiledInstance tree (hierarchy map)
                                          ▼
                     SimulationContext (per consumer: live editor, truth table, tests)
                        Signal[] nets · bool[] queued · IntRingQueue worklist · SignalChangeLog
                                          ▲
                     SimulationRunner (Play / Pause / Step / Reset / Load / SetInput / ToggleInput)
```

1. **Compile** (`Simulation.Engine.CircuitCompiler`). Expands the hierarchy recursively. Every output pin in
   every expanded scope gets a raw net id. Custom-gate boundaries add no logic: they are resolved with
   **union-find**. An inner input boundary is unioned with the net feeding the custom node's input pin, and
   the custom node's output pin is unioned with the net feeding the inner output boundary. Raw ids are then
   compacted into dense nets, and net 0 is the shared **floating** (constant LOW) net that every unconnected
   input reads. Because of union-find, expansion order doesn't matter, pass-through wiring works, and
   feedback through custom gates compiles.
   - Each placed custom gate is expanded separately: **instances never share nets/state**. Definitions are
     shared by reference.
   - Guards: circular references (by `DefinitionId` on the current path), `MaxHierarchyDepth`, missing
     definitions/primitives → `CompileResult` fails with diagnostics; nothing recurses unboundedly.
   - Feedback loops in the flat gate graph are found with `CycleFinder` and reported as **warnings**
     (`CompiledCircuit.CombinationalCycles`); the engine guards them at runtime.
2. **Context** (`Simulation.Context.SimulationContext`). Holds the net values, dirty flags, the ring-buffer
   worklist and scratch spans, all sized from the compiled topology at construction. `Reset()` drives inputs
   LOW, sets gate-driven nets unknown, evaluates every gate once and settles (so NOT outputs start HIGH), then
   resolves feedback loops according to the power-on state (section 15). This is the only whole-circuit
   evaluation.
3. **Propagate (event-driven).** `SetInput` writes the net and schedules only its readers. `Settle()` pops
   gates FIFO, evaluates them through `IPrimitiveComponent.Evaluate(span, span)`, and schedules fan-out only
   for outputs whose value changed. FIFO order naturally groups work into **waves**. An evaluation budget
   (`max(10 000, 64 × gates)`) turns oscillation into `SettleStatus.Oscillating`, clears the pending work
   and keeps the gates still switching for `SimulationDiagnostics`. `StepWave()` processes exactly one wave.
4. **Events.** Every net change is appended to `SignalChangeLog` as `SignalChange(net, previous, current,
   wave)`: wave 0 is the input change, wave *n* the outputs of the *n*-th gate layer reached. Consumers drain
   it and call `Clear()`. It is capped: if nobody drains it, `Overflowed` asks for a full refresh. Contexts
   used for analysis set `RecordChanges = false`. The engine never waits for a consumer.
5. **Controls** (`SimulationRunner`). Running = settle on each input change. Paused = apply inputs, defer
   propagation. Step = one wave. Play = resume and flush. Reset = power-on (inputs and clock LOW, loops re-initialised). `Load(compiled)` swaps in a
   recompiled circuit after an edit and **carries input values over by stable `PortId`**. A reload clears the
   change log, because it is a full refresh, not an animation.
6. **Sequential logic (Phase B).** Memory is built from feedback loops of AND/NOT gates, so the engine needs
   no sequential primitive. The timing model, power-on state and clock are described in section 15.

**Hierarchy map.** `CompiledInstance` mirrors the expanded scopes (root → child per custom node id) and maps
each scope's output pins to dense nets. `GetInputPinNet` resolves an input pin through its connection, or to
the floating net. Opening a custom gate in the editor will look up the instance for that exact placement and
colour its internal wires from the same live context. `CompiledCircuit.DescribeGate(g)` gives paths like
`Main/N3:OR/N2:NAND/N4(AND)` for diagnostics.

**Measured (EditMode, 2026-11-13):** a 5,000-gate random AND/NOT network compiles in about 10 ms. A single
input toggle evaluates **about 54 gates on average** (not 5,000), and results match an independent
topological reference evaluator on every vector. Toggling, settling and stepping allocate **0 bytes**
(verified with Unity's `AllocatingGCMemory` constraint).

## 6. Custom gate packaging *(implemented – Phase 4)*

```
PackagingDialog ──edits──► PackagingSpec (name, desc, category, icon, ordered/named ports, node subset)
       │ live preview                               │
       ▼                                            ▼
WorkspaceController.PreviewPackaging / PackageCustomGate ──► CircuitPackager.Package(source, spec)
                                                                   │  copy nodes/connections/layout (ids kept)
                                                                   │  interface from spec (port ids kept)
                                                                   │  validate (CircuitValidator + packaging rules)
                                                                   ▼
                                                     PackagingResult(definition | null, report)
                                                                   │ success
                                                                   ▼
                            CommandHistory ◄── PublishDefinitionCommand (+ optional RemoveNodesCommand)
                                                                   │
                                                     DefinitionLibrary.Add → frozen, DefinitionAdded
                                                                   │
                                     LibraryPanel refresh · session recompile · instances placeable
```

- **Identity.** Each packaged gate gets a fresh `DefinitionId` at version 1. Instances reference
  `DefinitionRef(id, version)`; the gate's behaviour is its graph, never its name.
- **Copy vs reference.** The packaged graph is a deep copy of the scratch circuit; the source circuit is
  untouched. Custom gates inside the packaged graph stay references to shared definitions, so nesting never
  duplicates graphs.
- **Interface.** External ports are the circuit's boundary nodes, ordered and renamed by the spec. Port ids
  are kept, which keeps instance connections stable across renames and reorders.
- **Validation.**
  - Errors: name; complete one-to-one boundary mapping with matching directions; port names non-empty and
    unique; at least one connected output; all components and definitions resolvable; no combinational
    feedback; no circular hierarchy; depth limit.
  - Warnings: unused inputs, cut wires (partial selection), no inputs, duplicate gate name.
- **Undo.** Publishing is a command. The library only allows withdrawing a version that no other
  definition references. Package-and-clear is a composite, undone as one step.
- **Instancing.** The compiler expands each instance into its own gates and nets (Phase 2), so instances
  never share state. Custom gates show only their external pins, labelled with the interface names.

- **Changing a placed component.** `ChangeComponentCommand` swaps a node's `ComponentRef` for another of the
  same kind (width, bus-join drivers, display style). The node is removed and restored with the same id,
  position and interface port, so the presenter, geometry and session see an ordinary remove and add.
  Original connections are re-added through `ConnectionRules`, so only pins that still exist with a matching
  width survive. Undo restores the original node and every original connection. For input switches the
  workspace adds a value-restoring `SetInputValueCommand` that is undone last.

## 7. Hierarchy, inspection and versioning *(implemented – Phase 5)*

```
CircuitNavigator (engine-free, CircuitEditor/Navigation)
  levels: [Root(main circuit, root session, root history)]
          [Inspect(definition, session = owner's, path = node ids from that session's root)]
          [Draft(definition vN+1, own session, own history, DraftSource = vN)]   ← at most one
          [Preview(published definition, own preview session, no history)]   ← opened from the library
  TryOpen(node)       checks: custom gate · definition resolvable · id not already on the path · depth limit
  TryNavigateTo(i)    refuses to leave a draft with unsaved changes (unless discardChanges)
  TryOpenDefinition(ref) replaces every level above Root by a Preview (refused while a draft has unsaved changes)
  TryBeginEdit()      replaces the top Inspect or Preview level by a Draft (CloneAsVersion(NextVersion))
  DiscardDraft()      drops the draft and goes back to inspecting vN
  ResolveScope(level) walks Compiled.Root.TryGetChild(path) → CompiledInstance whose nets colour the views
  ValidateStack()     drops levels whose node vanished or no longer references the shown definition
```

- **One presenter, many levels.** `WorkspaceController` always works on `Navigator.Current`: `Circuit`,
  `Session` and `History` follow the level. On a level change the presenter is rebound, and
  `SetSimulation(context, scope)` makes an inspected gate show the **live signals of that particular
  instance** (the same `SimulationContext` as its parent; changes reach the views through the normal change
  log). Each level remembers its camera view.
- **Read-only inspection.** Inspect levels have no history; every editing call (place, connect, delete,
  duplicate, rotate, move, rename, properties, packaging) is refused with a `Notification`. Inputs inside an
  instance cannot be toggled because the parent drives them (`CanToggleInputs` is true only at a session root).
  The interaction layer never starts wiring or dragging there.
- **Library preview.** A published gate can be opened without placing it (library context menu
  "Open Internal Circuit", or double-click a library entry). The Preview level is read-only. Its own
  `CircuitSession` simulates the definition as a session root, so its inputs can be toggled without touching
  the main circuit. Nested gates open from it as normal Inspect levels on the preview session, and Edit turns
  it into a draft. The navigator disposes the preview session once no level uses it. A draft started from the
  preview keeps the session alive, because discarding the draft returns to the preview.
- **Edit mode.** A draft is a mutable copy with the same `DefinitionId` and the next version number. It has its
  own `CircuitSession` (inputs toggleable, so the user can test the edit in isolation) and its own undo
  history. Published versions are frozen and never modified. Placing a gate that contains the draft's gate
  (`DefinitionVersioning.DependsOn`) is refused, and the validator reports circular references.
- **Saving a version** (`WorkspaceController.SaveDraft(mode)`):
  1. The draft is validated with the packaging rules (`CircuitPackager.ValidateDefinition`).
  2. `DefinitionVersioning.Plan` builds the publish set and the old → new reference map. With
     `UpdateEverywhere`, every latest definition that uses a planned definition gets a retargeted new version
     too (fixed point, dependencies first).
  3. One `CompositeCommand` on the **root** history: `PublishDefinitionCommand` for each new version, then
     `RetargetReferencesCommand` on the main circuit unless the mode is `KeepReferences`.
  4. Navigation returns to the main circuit. One undo withdraws everything.
- **Retargeting** (`ComponentRetargeting`) swaps a node's `ComponentRef` and keeps its id, position, rotation
  and label. Connections are re-made wherever the new version still has the pin, matched by stable `PortId`;
  the rest are reported as dropped. `Revert` restores the exact previous state.
- **Impact analysis** (`AnalyzeImpact`): instances in the main circuit, direct dependents, number of cascaded
  versions and the connections each mode would drop. It runs on probes, so nothing changes. The save dialog
  shows it.
- **Old references stay valid.** Old versions remain in the library. A circuit that keeps v1 keeps v1's
  behaviour. Gate titles show `vN` for N > 1, so mixed versions are distinguishable.
- **UI.**
  - `NavigationBar`: breadcrumbs, a mode badge and Edit / Save / Discard.
  - `ContextMenuView`: right-click without dragging.
  - `VersionSaveDialog`.
  - Inspector: mode section and "Open Internal Circuit".
  - Toolbar: "Inspect".
  - Opening a gate: double-click, context menu, inspector button or toolbar.

## 8. Signal animation pipeline *(implemented – Phase 6)*

`CircuitSession.Settle → SignalChangeLog (net, old, new, wave) → SignalAnimationController.Consume →
Tick(dt) in WorkspaceController.LateUpdate → CircuitPresenter.ApplyNetSource / ApplyWireArrival → WireView.SetTransition`.

- **Separation.** The engine settles immediately. The controller only reads the change log and drives views.
  It never writes signals, and the simulation never waits for it.
- **Scheduling.**
  - Every changed net becomes one pooled `Pulse` holding a `WireTrip` per wire of the net (fan-out).
  - Wave *k* starts when wave *k-1*'s longest wire arrives, plus `WaveGap`. Travel time is wire length divided
    by `UnitsPerSecond`; both scale with the speed setting.
  - A batch longer than `MaxBatchSeconds` is scaled down to fit.
- **Updates.**
  - At a pulse's start the source side updates (driver pins, input switches).
  - Each wire draws its transition at the current progress.
  - On arrival the destination side updates (wire, destination pin, output lamp).
- **Robustness.**
  - A new change on a net that is still animating from an earlier batch first completes the old pulse.
  - Presenter rebinds (structural edits, navigation) drop every pulse.
  - Logs that overflowed or exceed `MaxAnimatedWires` are applied instantly.
  - Disabling animation completes everything to the true state.
- **Allocation.** Pulses, trip lists, wave arrays and particles are pooled, and steady-state ticking
  allocates nothing (verified by a test). There are no per-wire coroutines or `Update()` calls.
- **Visual language.** `SignalStyle` (Standard / HighContrast) maps LOW/HIGH/UNKNOWN/Z to colour **and** line
  width. The travelling front is a bright gradient key plus a wider width-curve key, with an optional pooled
  particle dot.
- **Settings.** `SignalAnimationSettings`: Enabled, Speed 0.25x–4x, ShowParticles, MaxBatchSeconds,
  MaxAnimatedWires. They are serialized on `AppBootstrap` and edited live from the toolbar "Signals" popup.
  "Reset visual state" drops pulses and repaints true values.
- **DOTween** is used for camera easing and UI effects, not for wires.

## 9. Truth table generation *(implemented – Phase 7)*

`TruthTableDialog → TruthTableGenerator.Plan(definition) → TruthTablePlan → Start → TruthTableJob
(fresh SimulationContext) ← TruthTableJobDriver.Update (frame budget) → TruthTableData → virtualized rows /
TruthTableExporter`.

- **Plan.** The generator compiles the target with recording off, so the live context never changes and nothing
  needs restoring. Columns come from the interface order. It checks the limits (`TruthTableLimits`: max
  inputs, max rows, confirmation threshold, frame budget; absolute ceiling of 24 inputs), at least one output,
  and the absence of combinational cycles. Errors and warnings are `Diagnostic`s in the plan's report, and
  planning evaluates nothing. `Plan(CompiledCircuit)` reuses an existing compiled circuit.
- **Targets.** The UI asks for the shown circuit at any navigation level, a placed custom gate (its
  definition), or a primitive. `TruthTableTargets.ForComponent` builds a one-gate harness for a primitive, so
  every table comes from the engine.
- **Evaluation.** `TruthTableJob` owns its own `SimulationContext`, which starts in the all-LOW settled state.
  For each row in binary order (first input = MSB) it sets the inputs, settles and stores the outputs.
  - Event-driven propagation evaluates only what changed between consecutive rows.
  - A non-settling row fails the job.
  - Work is sliced with `Advance(rows)` or `AdvanceFor(ms)` on the main thread; no threads are used.
  - `Cancel()` and `RunToCompletion(CancellationToken)` stop it, and produced rows stay valid.
- **Storage.** Inputs are implied by the row index, and outputs are stored as one `Signal` byte per cell.
- **Sequential circuits.** Circuits with feedback or a clock (`CompiledCircuit.IsSequential`) are refused with
  the code `SequentialCircuit`. The explanation points to **sequence analysis** (section 15), which runs
  explicit input and clock steps from a chosen power-on state.
- **UI.**
  - `TruthTableJobDriver` (TruthTable.UI) is the only per-frame component.
  - `TruthTableDialog` (UI) has a fixed header and a virtualized list of 40 pooled rows, each one rich-text
    line with `<pos>` columns, so 65,536 rows cost the same as 4.
  - Tables above the confirmation threshold wait for "Generate".
  - Export: CSV written atomically to `persistentDataPath/Exports`; tab-separated text copied to the clipboard.

## 10. Persistence format *(implemented – Phase 8)*

```json
{
  "schemaVersion": 3,
  "format": "DigitalLogicSandbox.Project",
  "app": { "name": "Digital Logic Sandbox", "version": "0.1.0" },
  "name": "My project",
  "savedUtc": "2026-11-14T10:00:00Z",
  "settings": { "gridVisible": true, "snapToGrid": true, "visualizationMode": "Standard",
                "animation": { "enabled": true, "speed": 1.0, "showParticles": true, "maxBatchSeconds": 3.0 },
                "truthTable": { "maxInputs": 16, "maxRows": 65536, "confirmRows": 1024 },
                "camera": { "hasValue": true, "x": 0, "y": 0, "size": 12 },
                "simulation": { "clockFrequency": 2.0, "clockEnabled": true, "powerOnState": "Resolved" } },
  "rootCircuit": { "id": "…", "version": 1, "nodes": [ … ], "connections": [ … ], "nextNodeId": 9, … },
  "definitions": [ { "id": "…", "version": 2, "published": true, "inputs": [ … ], "nodes": [ … ] } ],
  "inputValues": [ { "port": 1, "value": "High" } ]
}
```

- **Modules:** `Persistence.Serialization` (DTOs and writer), `Persistence.Deserialization` (reader and
  binder), `Persistence.Migration` (schema steps), `Persistence.Recovery` (file store).
  `App.ProjectController` maps the editor state to and from a `ProjectDocument`. The UI only sees
  `UI.IProjectService`.
- DTOs are separate from domain types; no Unity types are serialized. A node is `primitive`, `custom`
  (definition id and version), `input` or `output` (interface port), or `clock`.
- **Schema history:** v1 is the initial release. v2 (Phase B) adds `clock` nodes and `settings.simulation`,
  and `AddSimulationSettingsMigration` fills in the defaults when opening v1 files. v3 (Phase C) adds optional
  node `width`/`arity`, the bus node kinds and decimal bus input values (`AddBusSupportMigration`, no-op).
  See section 16. v4 (Phase D) adds the `display` node kind with a `style`
  (`AddDisplaysMigration`, no-op); see section 17. v5 adds an optional LED `color` on display nodes
  (`AddLedColorsMigration`, no-op: files without it use the default colour). v6 adds an optional clock
  `divider` (`AddClockDividersMigration`, no-op: files without it run every clock at the base rate). v7 adds the
  `constant` node kind with a `value` (`AddConstantsMigration`, no-op); see section 20.
- Each `(id, version)` is written once, regardless of instance count. Instances reference definitions by id
  and version, so old versions stay pinned exactly as in memory.
- **Loading:**
  1. Parse.
  2. Check the format tag.
  3. Run the `IProjectMigration` steps `vN → vN+1` over the `JObject`.
  4. Bind the DTOs. Ids, interface order, timestamps and id allocators are restored, so a round trip is exact.
  5. Validate references and freeze published definitions.
  - Fatal problems raise `ProjectFormatException`. Recoverable ones become `ValidationReport` warnings:
    a missing gate, an unknown primitive, a dangling connection.
- **Saving** is atomic: write `*.tmp`, flush, then `File.Replace` keeping `*.bak`. Opening falls back to
  `.tmp`, then `.bak`.
- **Autosave** writes `<project>.autosave`, or `Autosave/Untitled.dlsproj.autosave` for untitled projects.
  - At start-up, an autosave newer than its project is offered for recovery (Recover / Discard).
  - Saving deletes the autosave.
  - Leaving a project deliberately (Don't save) also deletes it.
- **Adding a schema change:** bump `ProjectFormat.CurrentSchemaVersion`, add an `IProjectMigration` with
  `FromVersion = old`, and register it in `ProjectMigrator.CreateDefault()`.

## 11. Undo / redo *(implemented – Phases 3–8)*

`ICommand { Execute(); Undo(); }` operating on the **model only**; views re-sync from model change events,
so undo restores the visual state automatically. Large operations (packaging, publishing a version) use
composite commands holding targeted snapshots, never whole-project serialization.

- **Commands covered:**
  - Add, remove, move and duplicate gates.
  - Connect and disconnect.
  - Rename nodes and interface ports; reorder the interface.
  - Change an input value: `SetInputValueCommand` acts on the `CircuitSession`.
  - Package a circuit; publish a gate version; retarget references.
  - Rename a custom gate: `RenameCustomGateCommand` updates the metadata of every version without creating
    a new version.
  - Change circuit properties.
- Each navigation level has its own history. Library-wide operations (packaging, renaming a gate) run on the
  root history.
- History is cleared when a project is opened or created.

## 12. Editor interaction *(implemented – Phase 3)*

```
Input System actions --> WorkspaceInteraction (state machine, one Update)
                                  | calls public API
UI panels (toolbar, library) --> WorkspaceController --> CommandHistory --> CircuitDefinition (model)
                                  |                                              | Changed events
                                  | LateUpdate: Sync()                           v
                                  +--> CircuitSession.RebuildIfDirty --> CircuitCompiler --> SimulationRunner
                                  +--> CircuitPresenter.ApplyChanges(change log) <------------------+
                                             |
                                    pooled GateView / WireView (pure views)
```

- **Single input reader.** `WorkspaceInteraction` polls `WorkspaceControls` (an `InputActionMap`; the default
  is built in code and can be replaced by an asset) once per frame. Gates have no input handlers. Pointer
  input is ignored over UI, and shortcuts are ignored while a text field has focus (predicates injected by
  the App).
- **Model-based hit-testing** (`WorkspaceHitTester`) against `CircuitGeometry`: pins (screen-constant radius)
  → gate bodies → wires (distance to path). No colliders.
- **Geometry cache.** `CircuitGeometry` caches gate layouts and wire routes. The presenter forwards each model
  change to it *before* updating views, so drawing and picking always agree. Only wires touching a moved
  node, or crossing its old or new body, are rerouted. The geometry queues those wires
  (`DrainInvalidatedWires`), and the presenter reroutes each view once per change.
- **Recompile policy.** Structural changes (nodes, connections, interface) mark the session dirty; moves,
  rotations, labels and renames do not. Recompilation happens at most once per frame in `Sync()` (or
  immediately before an input toggle), and input values carry over by port id.
- **Signal views.** `CircuitPresenter.SetSimulation(context, scope)` builds a net → view-target index for the
  displayed `CompiledInstance` (the root for now; a nested instance when inspecting in Phase 5). Each frame,
  only nets in the change log are pushed to views. Phase 6 will put the animation controller between the
  change log and `ApplyNetSignal`.
- **Placement.** Clicking a library item arms placement: a ghost follows the pointer, a click places it, and
  Shift keeps placing. Dragging an item places it on release. Positions snap to a 0.5-unit grid so pins align.
- **Camera.** Drag and scroll apply immediately. Toolbar zoom/reset/frame use DOTween easing, which is killed
  on direct manipulation and on destroy. Framing fits content into the screen area left between the docked
  panels: the UI supplies it as `WorkspaceController.VisibleScreenArea`, and the math is the pure
  `WorkspaceCamera.ComputeFrame`.
- **Wire routing** *(Phase 9)*. `WireRouter` is purely visual and deterministic:
  - Each pin has an outward normal that follows gate rotation (`CircuitGeometry.GetPinNormal`). The wire
    leaves the source along its normal and enters the destination against it, with a 0.5-unit stub at
    each end.
  - Between the stubs, the router scores a fixed set of orthogonal candidates: L shapes, Z shapes through
    a free column or row, and five-segment detours. Free coordinates are the midpoint, the outer edges of
    each endpoint body, the outer edges of both bodies together, and the gap between them.
  - The cost is length plus bends. Entering an endpoint gate's body, or doubling back over a stub, carries
    a large penalty.
  - Scratch buffers are static, so routing allocates nothing.
  - **Obstacles.** Other gates near the wire (up to `WireRouter.MaxObstacles`, nearest first, endpoint gates
    and gates overlapping them excluded) are passed in. Crossing one costs less than crossing an endpoint
    gate. Only when the best plain shape still crosses an obstacle does a second pass add lanes just outside
    each obstacle (one stub away), so unobstructed wires pay nothing.
  - **Maze routing for settled wires.** `GridRouter` is the default router and `WireRouter` is the fast
    fallback.
    - It runs A* over a rectilinear grid: a line every 0.5 units, plus lines through both stub ends so
      off-grid pins are exact nodes.
    - The grid is clipped to the endpoints' bounds plus 3 units. If no route is found it retries once with
      12 units.
    - Search states are (node, heading). The cost is length, plus 1 per bend, plus 4 per unit when running in
      a lane another net already uses, plus 0.3 per crossing. U-turns are not allowed.
    - A lane also claims every parallel line closer than `GridRouter.LaneProximity` (0.3). Grid lines are
      0.5 apart, so this only affects off-grid stub lines, where two wires would otherwise look like one.
    - The straight-wire shortcut is taken only when nothing (no gate and no other net's lane) lies on the
      line. Otherwise the search decides.
    - `GridRouter.PathCost` evaluates the same terms on an existing route, so two routes can be compared.
    - Obstacles: every gate body in the area, inflated by `BodyMargin`.
    - Lanes: the cached routes of other sources' wires in the area (from the wire index). Fan-out wires of
      the same output may share their trunk.
    - Buffers are static and only grow, and the heap is written by hand (no .NET `PriorityQueue`), so
      routing allocates nothing after warm-up.
    - Limits: at most 400 lines per axis, 60,000 nodes and 40,000 expansions. Beyond them it falls back to
      `WireRouter` (counted in `CircuitGeometry.RouteFallbackCount`).
    - Order matters, since earlier routes become lanes for later ones. Reroutes run in a fixed order, and the
      improvement pass below removes most of the order dependence.
  - **Rip-up and reroute** (`CircuitGeometry.ImproveRoutes`). After every change the presenter has applied
    (and after `Bind` and drag settle), a bounded pass reconsiders the wires that could do better:
    - *Freed room.* Wires whose last search was shaped by other nets (`GridRouter.LastLaneInfluenced`) and
      that lie near a route dropped since the last pass.
    - *Overlaps.* Other nets' wires that run along a newly routed wire, followed by that wire.
    - *Regret.* A newly routed, lane-influenced wire is searched once more with no lanes. If that ideal
      route is cheaper, it is recorded as *demand*, and the wires lying on it are queued, then the wire itself.
      Those wires are searched again with soft lanes along the demand (`SoftLaneCost` 0.75 per unit). This
      lets an earlier wire step off a lane it chose only by tie-breaking.
    - Acceptance: a new route replaces the old one when `PathCost` (under the same lanes) drops by more than
      0.05, or when it steps off a demand route while giving up at most two crossings' worth of cost.
      Overlap and crossing costs are shared by the two wires involved, so improvements lower the layout's
      total cost. Each wire is tried at most twice per pass, and a budget caps the searches (64 per change,
      or `max(64, wires / 4)` on `Bind`). Together these guarantee termination.
    - Accepted routes cascade: wires near the vacated route are offered the room. Nothing runs while
      dragging. Seeds collected during a drag (as one union rectangle) are used on settle.
    - `CircuitPresenter.WireImprovementCount` counts improved views separately from `WireRerouteCount`.
  - **Crossings.** `WireCrossings.Find` lists the points where a wire's *vertical* segments strictly cross
    another net's horizontal segments. Points closer than 0.3 to a bend, pin or T-touch are skipped, and so
    are fan-out wires of the same source. Only the vertical wire is decorated, so the horizontal one stays
    straight. Each point is `(x, y, side)`: a hop bulges towards +x unless another net's vertical wire or a gate
    body lies within `WireCrossings.HopReach` (0.354) on that side and the -x side is clear.
    - `CircuitGeometry.GetCrossings` caches them per wire. Any route change queues that wire and every wire
      near its old and new bounds (`DrainCrossingChanges`). A gate that is added, moved, rotated or removed
      also queues every wire within hop reach of its old and new bodies, because a hop's side depends on the
      bodies beside it even when the wire keeps its route. The presenter pushes them to the views after
      reroutes and improvements, but not during a drag.
    - `WireView.SetCrossings(points, style)` draws `WireCrossingStyle.Hop`, `Gap` or `Plain`:
      - Hop: an 8-segment semicircle of radius 0.18 (x1.3 on buses) bulging to the crossing's side. Crossings
        closer than a hop's diameter plus `WireView.MergeSpacing` (0.12) share one bridge: a quarter arc up, a
        straight run beside the wire and a quarter arc down, on the side of its first crossing. It is inserted
        into the drawn polyline, so the travelling front and particles follow the arc.
      - Gap: the crossed segment's quad is split around the crossing (+/-0.2). Gaps that overlap or nearly
        touch merge into one. The pieces keep the full segment's path distances, so a transition jumps the
        gap.
      - Plain: the wire is drawn unchanged.
    - Routing, hit-testing and junction dots always use the plain geometry path.
    - The style is a project setting (`settings.wireCrossings`, optional, default `"Hop"`; no schema bump),
      chosen in the Signal Settings popover.
  - **Interactive routing.** While gates are dragged, `WorkspaceInteraction` sets
    `CircuitPresenter.SetInteractiveRouting(true)`. Wires invalidated during the drag use the candidate
    router and are remembered. When the drag ends or is cancelled, those wires are invalidated again and
    maze-routed.
  - **Spatial indexes.** `CircuitGeometry` keeps two uniform-grid `SpatialHash` indexes: gate bodies (for
    obstacle queries) and wire route bounds. When a gate is added, moved, reshaped or removed, only wires
    whose bounds touch its old or new body are invalidated. That covers both newly blocked wires and wires
    that can now take a shorter path. The node index is built lazily on the first route after `Bind`.
  - **Junction dots.** Fan-out wires from one output share their first stretch. The presenter measures each
    wire's longest common prefix with its siblings (`SharedPrefixLength`) and puts a dot where it leaves the
    trunk. Dots are recomputed only for sources whose wires were created, removed or rerouted.
- **Diagnostics.** `CircuitPresenter.WireRerouteCount` / `NetSignalUpdateCount` let tests and profiling
  confirm that only affected views are touched.

## 13. Extension points

| Future feature | Extension point |
|---|---|
| New primitives (only if ever needed) | Implement `IPrimitiveComponent`, register it |
| Sequential logic / clocks | Done (Phase B): gate-level feedback, unit delay, global clock net (section 15) |
| Multiple clock domains | Done: divider domains (÷1 … ÷64) derived from one base clock (section 15) |
| Unrelated clock frequencies | Would need an event-time scheduler in `SimulationRunner` and sequence analysis |
| Buses / multi-bit | Done (Phase C): pin widths, splitter/merger, buses flattened to bit nets (section 16) |
| Tri-state, contention | Done (Phase C): tri-state utility, bus join, shared-net resolver (section 16) |
| Multi-bit primitives | Signatures with wide pins are bit-sliced by the compiler automatically |
| Displays (LED, 7-seg, hex, decimal) | Done (Phase D): `Display` utility kind + `DisplayStyle`, presenter-driven views (section 17) |
| Fixed values (ROM words, masks) | Done (Phase E+): `Constant` utility, compiled to AND/NOT gates (section 20) |
| New display styles | Add a `DisplayStyle`, its decoding in `DisplayEncoding`, a size in `GateLayout.DisplaySize` and a library entry |
| New file schema | Add an `IMigration` |

---

## 14. Coding conventions

- C# 9, **block-scoped namespaces** (enforced as an error in `.editorconfig`). Namespace = assembly name +
  subfolder (e.g. `DigitalLogicSandbox.Core.Circuit`).
- `PascalCase` types/members/constants, `_camelCase` private fields, `I`-prefixed interfaces.
- Braces always; `using` directives outside the namespace; one public type per file, file named after it.
- Engine-free assemblies must not use `UnityEngine` (compile-enforced).
- Third-party libraries only where allowlisted (§2, test-enforced). No legacy `UnityEngine.Input` calls.
- Hot paths (engine, animation tick, hit-testing): no LINQ, no closures/lambdas, no boxing, no `foreach` over
  interfaces; preallocate and reuse buffers.
- Internals are visible to the test assemblies (`AssemblyInfo.cs`); prefer `internal` over `public` for
  implementation details.
- No `Update()` on per-gate/per-wire MonoBehaviours; a few central controllers tick everything.
- Every phase ships with tests; `ArchitectureTests` must stay green.

## 15. Sequential logic *(implemented – Phase B)*

Memory is built from gates. A latch is two cross-coupled NANDs (AND + NOT), a flip-flop is two packaged
latches, and so on. The engine has no sequential primitive. What makes this well-defined:

- **Unit-delay waves.** `SimulationContext.RunWave` evaluates every gate queued for the wave against the net
  values committed at the start of the wave. It writes their outputs to pending buffers
  (`_pendingNets` / `_pendingValues`). `CommitWave` then applies the changes together and queues the readers
  for the next wave. Every gate therefore has exactly one wave of delay, so races resolve the same way
  whatever the gate creation order. Combinational results are unchanged, and the wave count equals the
  logic depth. Budget checks happen once per wave.
- **Power-on (`Reset`).** Gate-driven nets start `Unknown`, every gate is queued, and the circuit settles.
  If the circuit has cycles and `PowerOnState.Resolved` is set (the default), `ResolveLoops` takes the
  remaining unknown loop nets in compiled gate order, forces each one LOW and settles. This repeats until
  none remain. Each forced net counts once (`ResolvedLoopNets`). The result is binary and repeatable for a
  given circuit. `PowerOnState.Unknown` skips this step, so loops stay X until inputs drive them.
- **Oscillation.** When a settle runs out of budget, `HandleOscillation` sets the outputs of the gates still
  switching to `Unknown`. Since NOT X = X and AND with X is X unless another input is 0, the circuit settles.
  The status is `Oscillating`, and `SimulationDiagnostics` reports an `Oscillation` warning.
- **Clock.** `ComponentKind.Clock` is a utility node (`ComponentRef.IsUtility`) with no inputs and one output.
  - **Divider domains.** Each clock node carries a divider ÷2^k, k = 0…6 (`ComponentRef.ClockDivider`, stored
    in the reference's arity byte). The compiler unions every clock node with the same divider, in the whole
    hierarchy, into one net (`CompiledCircuit.GetClockNet(k)`), so a packaged counter and the circuit around
    it share a domain. `ClockNet` is the fastest domain present; `ClockDomainCount` counts them.
  - **Schedule** (`ClockSchedule.Level`). The project frequency is the base rate. Edges are counted on the base
    clock: after base edge E (from 1) domain k is HIGH when `(E + 2^k - 1) >> k` is odd. Every domain is LOW
    at power-on and all rise together on edge 1, so the domains stay in phase and timing is deterministic.
  - `SimulationContext.ApplyClockEdge(edge)` sets each present domain to its scheduled level (allocation-free)
    and schedules the readers of the ones that changed. `SetClock(value)` drives every domain at once.
  - `SimulationRunner` owns time: `Advance(dt)` applies one edge per half period while it is running and the
    clock is enabled. It is capped at 8 edges per call and drops any backlog.
  - `TickClock()` applies a manual edge, and `Step()` starts the next edge when the circuit is idle.
  - The workspace calls `Advance` for the shown level in `LateUpdate`, before `Sync`. Clock edges therefore
    flow through the normal change log and animation pipeline. The simulation never waits for animation.
  - While a draft or library preview is shown, the main circuit's runner is advanced too, in the background.
    Nothing draws it, so its change log is cleared after each advance. Returning to it rebinds the presenter,
    which redraws every view from the live context.
- **Feedback is legal.** Validation and the compiler report loops as `FeedbackLoop` warnings (listed by packaging and
  the compiler, but left out of the status bar headline, where every memory made it permanent), and
  self-connections are allowed. `CompiledCircuit.IsSequential` (cycles or a clock) is what consumers check.
- **Sequence analysis** (`TruthTable.Generation.SequenceAnalyzer`):
  - It is the analysis mode for sequential circuits. The input is a `SequenceSpec`: per-input 0/1 patterns
    (an input holds its last value), the step count, `PowerOnState`, and `PulseClock`.
  - It compiles into a fresh, non-recording context; the live context is never touched. Row 0 is the
    power-on state.
  - Each step sets the inputs and settles. If the circuit is clocked, the step then applies one base clock
    cycle (two `ApplyClockEdge` calls, each followed by a settle). Slower domains change only on the steps
    their schedule says.
  - Unstable rows are flagged. Each result describes one stimulus, not the whole circuit: the dialog and
    the docs say so, and the truth table keeps refusing these circuits.
  - The dialog shows up to the analyzer's limit (1 024 steps). A fixed header sits above a virtualised list:
    a pool of 32 row texts is refilled from the table as it scrolls, so the row count never reaches TMP's mesh
    limit. Copy and CSV export always cover every row.
- **Settings.** `ClockFrequency` (the base rate), `ClockEnabled` and `PowerOnState` are project settings,
  stored in `settings.simulation` (schema v2). A clock's divider is part of its node: `"divider": 4`, written
  only when above 1 (schema v6). The inspector's divider picker changes it through `ChangeComponentCommand`. The main and draft sessions share them through one `SimulationOptions`
  instance and the navigator.

## 16. Multi-bit logic *(implemented – Phase C)*

- **Model.** Widths live on pins (`PortDescriptor.Width`, `InterfacePort.Width`, 1–32 bits) and on the
  parameterised utility references (`ComponentRef.Width`, `ComponentRef.Arity`). A node's signature is a
  pure function of its reference (`BoundarySignatures.ForUtility`), so layout, validation, the compiler and
  persistence all agree.

  | Kind | Pins | Behaviour |
  |---|---|---|
  | Input / Output boundary (width w) | one w-bit pin | bus interface port |
  | `Splitter(w)` | `Pin` (w bits) → `BitPin(0..w-1)` | wiring, compiled away |
  | `Merger(w)` | `BitPin(0..w-1)` → `Pin` (w bits) | wiring, compiled away |
  | `BusJoin(w, n)` | `BitPin(0..n-1)` (w bits each) → `Pin` | shared bus, resolved at runtime |
  | `TriState(w)` | D (w), EN (1) → OUT (w) | EN=0 → Z; bit-sliced `TriStateBuffer` |

  Bit 0 is the least significant bit. `BitPin(i)` has port id `i + 1`. All of these are utilities: the
  built-in gate library stays AND and NOT.
- **Validation.** Connected pins must have equal widths (`ConnectionError.WidthMismatch`,
  `DiagnosticCode.WidthMismatch`). Width changes go through splitters and mergers.
- **Compilation.** Buses are flattened to single-bit nets, so the engine never sees a bus:
  1. Every output pin allocates one raw net per bit. Splitters, mergers and joins union bits (no gates).
  2. Gates with bus pins are sliced into one gate per bit. Single-bit pins feed every slice.
  3. Floating ties are deferred. A class with a real driver (a gate, a root input or the clock) ignores
     them, so an unconnected join input floats instead of driving LOW.
  4. Nets with several gate drivers (created only by bus joins) get CSR driver lists
     (`NetSharedOffsets`, `NetSharedSlots`). Root inputs or the clock on a shared bus are rejected
     (`InvalidBusJoin`).
  5. The root interface is flattened to bits in port order, bit 0 first (`InputBits`, `OutputBits`).
- **Runtime.** For shared nets the context stores each driver's last output per slot and resolves the bus
  when a driver changes, once per wave:

  | Drivers | Bus |
  |---|---|
  | all Z | Z |
  | any X | X |
  | HIGH and LOW | X, contended (`ContentionCount`, `BusContention` warning) |
  | otherwise | the agreed value |

  Circuits without joins allocate nothing extra. Whole-port helpers: `SetInputValue(port, ulong)`,
  `TryGetInputValue`, `TryGetOutputValue`.
- **Presentation.**
  - Bus pins and wires register under every bit net and show an aggregate: X if any bit is X, HIGH if any
    bit is HIGH, Z if all bits float, otherwise LOW.
  - Bus wires are drawn thicker (`WireView.IsBus`).
  - Bus boundaries show bit cells (`GateGeometry.BitCells`, most significant bit on the left) and a
    "NAME = value" readout. Clicking a cell toggles that bit with an undo step.
- **Analysis.** Truth tables and sequence analysis use one column per bit, MSB first, named `A[3]`…`A[0]`.
  `TruthTableColumn.Index` maps each column to its compiled bit.
- **Persistence (schema v3).**
  - Optional node `width` and `arity`, and node kinds `splitter`, `merger`, `busjoin` and `tristate`.
  - Input values are "Low"/"High" for 0/1 and decimal otherwise, e.g.
    `{ "kind": "input", "port": 1, "width": 8 }` and `{ "port": 1, "value": "165" }`.

## 17. Visual components *(implemented – Phase D)*

- **Model.** `ComponentKind.Display` with a `DisplayStyle` on `ComponentRef` (`ComponentRef.Display(style,
  width)`). Displays are utilities: no outputs, no internal circuit, no truth-table entry.

  | Style | Inputs | Shows |
  |---|---|---|
  | `Led` | IN (1 bit) | one lamp |
  | `SevenSegment` | a, b, c, d, e, f, g, dp (1 bit each) | one digit, raw segments |
  | `Binary` | IN (w bits) | w lamps, MSB on the left |
  | `Hex` | IN (w bits) | ceil(w/4) hex digits |
  | `Decimal` | IN (w bits) | unsigned value, right-aligned |
  | `SignedDecimal` | IN (w ≥ 2 bits) | two's-complement value with a minus sign |

- **Data flow.** Displays are not compiled (they drive nothing). After each compile the presenter resolves the
  nets a display reads (`CompiledInstance.GetInputPinNet`) and registers a `DisplayInputs` target on each
  distinct net. Signal changes (or an animated wire arrival) push the input signals into `GateView`, which
  decodes them with `DisplayEncoding` and recolours its pooled `DisplayVisual` sprites. Nothing in Core,
  Simulation or TruthTable knows about display rendering.
- **Decoding.** `DisplayEncoding` is pure: segment masks (bit 0 = a … bit 6 = g, bit 7 = dp), glyphs 0–F,
  decimal and signed digits, dashes when a bit is X or Z, and `FormatValue` for the inspector and tests.
- **Layout.** `GateLayout.DisplaySize` gives each style's body and content area; content is drawn upright and
  scaled into the rotated `GateGeometry.DisplayArea`.
- **Persistence (schema v4).** `{ "kind": "display", "style": "hex", "width": 8 }`.
- **LED colour (schema v5).** `ComponentRef.LedColor` is an index into `LedColors` (signal, red, orange, green,
  blue, pink). It is part of the reference (equality and hash) but has no effect on signatures or
  simulation. `VisualTheme.LedColor(i)` supplies the colours, and index 0 is the HIGH signal colour. It is
  saved as `"color": "green"` only when it is not the default. Unknown ids load as the default. In the
  inspector, a swatch row marks the current colour with a check, so the choice is never shown by colour
  alone. Changes go through `ChangeComponentCommand`.

## 18. Visual design system *(UI redesign, 2026-11-15)*

One visual language, defined in `VisualTheme` (colour tokens, sizes, fonts) and applied by `UiFactory` (UI) and
the workspace views (`GateView`, `WireView`, `GridView`).

- **Principles.** The workspace is the hero: it is full-bleed, and panels float over it as rounded cards with
  soft shadows. The palette is calm (deep ink background, one periwinkle accent, a teal glow for HIGH). Icons
  replace text where the meaning is clear, with a tooltip on every icon button. The UI uses progressive
  disclosure: only what a newcomer needs is visible at first.
- **Layout.** Project pill and file menu (top-left), icon tool bar (top-centre), library card (left), status
  pill (bottom-centre), view controls (bottom-right). Several panels appear only when needed:
  - The breadcrumb pill appears only inside a custom gate (`NavigationBar` hides itself at the root).
  - The properties card appears only while something is selected, or when pinned with the toolbar
    "Circuit properties" button (`InspectorPanel.Pinned`). It grows with its content.
  - The selection tools (Inspect, Delete) appear in the tool bar only while something is selected.
  - The status details (evaluation count, live values) expand from the status pill on click.
  - The library opens Gates, Inputs & Outputs and My Gates. Timing, Buses and Displays start collapsed, and
    search expands whatever matches.
  - An `EmptyStateView` explains the first step on an empty canvas and fades out once a part is placed.
- **Typography.** Nunito (SIL OFL), the full character set (938 glyphs, including
  ÷ ≈ ≥ ›), with Regular, SemiBold and Bold weights linked through the font asset's
  weight table. `UiFactory.Text` maps `FontStyles.Bold` to SemiBold for softer emphasis. Lucide (ISC) supplies
  icons and is a fallback of the text font, so `UiFactory.Icon` labels keep Nunito as their primary font. This
  avoids TMP warnings, because TMP looks up '_' and '…' only in the primary font. Glyph constants are in
  `UI/Icons.cs`. The fonts are dynamic TMP assets under `UI/Resources/DLSFonts`, referenced by the theme, and
  `Resources.Load` is the fallback for runtime-default themes.
- **Controls.**
  - `UiFactory.Card` draws a transparent hit root with Shadow, Fill and Border children. These children ignore
    layout, so layout groups on the card work as on a plain panel.
  - `IconButton` is a ghost button with a hover tile. `MakePrimary` gives a button the accent style.
  - `Toggle` is a switch, animated by `SwitchVisual`.
  - `ButtonVisual` keeps labels and icons in step with the disabled, hover and active states.
  - There is one shared `TooltipView`, and `TooltipTrigger` components feed it.
- **Workspace art.**
  - `DlsGrid.shader` draws a screen-constant dot grid over a centre-lifted background with a vignette.
  - Gates get a 9-sliced `RenderAssets.SoftShadow` (sorting order `GateShadow`, below wires), a hairline
    outline and a SemiBold title. Lit switches and lamps get a radial `Glow` sprite.
  - **Wires** are meshes drawn with `DlsWire.shader` (`VisualTheme.WireMaterial`), not LineRenderers:
    - Each `WireView` builds one quad per straight segment, extended by the core half width plus
      `VisualTheme.WireGlowRadius`. UV0 holds the distance along and across the segment; UV1 holds the
      path distance at the segment start and the segment length.
    - The fragment shader evaluates a capsule signed-distance field. That gives an anti-aliased core
      (`fwidth`), perfectly round joints, and a soft quadratic glow, with no seams at bends.
    - Blending is `BlendOp Max` on premultiplied colour. Overlapping capsules (bends, shared fan-out
      trunks, junction dots) form a union instead of stacking brighter.
    - State comes in through a per-view `MaterialPropertyBlock`: new and old colour, front colour, half
      widths, glow amounts, front distance and the junction disc. A signal transition is drawn as the new
      state up to the front path distance, with a Gaussian bright head, and the old state beyond it.
    - The mesh is rebuilt only by `SetPath`. Signal, hover, selection and transition changes touch only the
      property block.
    - Glow is `GlowStrength × 1.6` for HIGH, half that for UNKNOWN, and none for LOW and Z.
      `GlowStrength` 0 disables it.
- **Colour space.** The project renders in linear space, so low-alpha white overlays look much brighter than
  their numbers suggest. Keep stripes and tints at around 2% alpha.

## 19. Example gate pack *(implemented – Phase E)*

- **What it is.** `GateLibrary.Examples.ExampleGatePack` builds 26 ordinary custom gates: NAND, OR, NOR, XOR,
  AND/OR/XOR 4-bit; MUX 2:1, MUX 4:1, MUX 2:1 4-bit, MUX 4:1 4-bit, MUX 4:1 8-bit, Decoder 2→4, Decoder 4→16;
  Half Adder, Full Adder, Adder 4-bit, ALU 4-bit; D Latch, Latch 4-bit, D Flip-Flop, Register 4-bit, Counter 4-bit
  and RAM 16×4; ROM 16×8 and CPU 4-bit (section 20). A new project never contains them; File → "Add example
  gates" adds them.
- **Bus-width building blocks.** The bitwise, bus-multiplexer and 4-bit latch gates exist so the big gates are
  built from a few bus-level parts instead of rows of single-bit ones (the RAM is 16 Latch 4-bit words and five
  MUX 4:1 4-bit, the ROM five MUX 4:1 8-bit, the CPU's datapath rows are MUX 2:1 4-bit). Flattened, the circuits
  are gate for gate the same as the earlier bit-level versions (the CPU still compiles to 2 842 gates).
- **Built from AND and NOT only.** Each gate contains AND/NOT primitives, boundaries, splitters/mergers and
  instances of *earlier* pack gates, referenced by ID (the Full Adder holds two Half Adder instances, not copies).
  No primitive, simulator path or UI code knows any pack gate by name: once published they are indistinguishable
  from gates the user packaged.
- **Stable IDs.** IDs are fixed (`example.alu4`, …) so a second load is detected and refused, and saved projects
  keep their references. A name already taken in the library gets an "(example)" suffix; names are settled before a
  gate is built because staged definitions are read-only.
- **Loading.** `ExampleGatePack.Prepare(primitives, library, sizer)` builds the pack into a private staging
  library (so gates resolve each other), then validates every gate with `CircuitPackager.ValidateDefinition`.
  `WorkspaceController.LoadExampleGates` publishes the result as one `CompositeCommand` of
  `PublishDefinitionCommand`s, so a single undo removes the whole pack. Like packaging, it is only offered on the
  main circuit.
- **Layout** (`LayeredLayout`, a layered Sugiyama-style layout; also behind the editor's "Tidy layout"):
  1. Columns: feedback edges (DFS back edges) are ignored; inputs come first, outputs last, every other node right
     of its predecessors (constants and clocks start one column right of the inputs). A balancing pass then moves
     nodes with more outgoing than incoming wires (constants, splitters) right, next to their users.
  2. Lanes: a wire spanning several columns gets a virtual node in each column it crosses, shared per source pin.
  3. Order: up to 24 alternating barycenter sweeps on pin heights, each followed by adjacent swaps, keeping the
     order with the fewest crossings (merge-sort count per column pair). Boundary columns keep interface order.
  4. Heights: nodes move to the median height of the pins they connect to, with order and spacing kept by an
     isotonic (pool-adjacent-violators) fit; lanes weigh more, so long wires run straight. Snapped to 0.5.
  5. Column gaps grow with the wires that change height between two columns (3.5 to 7 units).
  Sizes come from a `NodeSizer` delegate (the workspace passes `Rendering.GeometryNodeSizer`) and pin heights from
  `LayeredLayout.SignaturePins`, which restates the renderer's pin rule (1 unit apart, centred, first on top); a
  test checks the two agree, so GateLibrary needs no reference to Rendering. `ComputePositions` returns positions
  without touching the circuit; `Arrange` applies them.
- **Part labels.** Builders name key parts with node labels ("PC", "Z flag", "Data RAM", "Word 7", "Read"). The
  renderer draws a custom or primitive gate's label above its body in the accent colour (`GateGeometry.Caption`).
- **Tidy layout.** `WorkspaceController.TidyLayout` (workspace context menu, editable circuits only) computes the
  same layout for the current circuit and applies it as one `TransformNodesCommand`, resetting rotations so the
  circuit reads left to right. Projects saved with the earlier pack keep their positions until tidied.
- **Memory without a primitive.** The RAM is 16 Latch 4-bit words (64 D-latch cells of cross-coupled NANDs), a
  4→16 decoder for the write enables (`WE · CLK · select`) and five MUX 4:1 4-bit for reading: 1 041 AND/NOT
  gates once flattened. Writes are
  level-sensitive while CLK is HIGH, so the address must only change while CLK is LOW.
- **Extension point.** More packs follow the same shape: a builder that returns definitions in dependency order with
  fixed IDs, then `Prepare` and one composite publish command.

## 20. Constant utility and the CPU demo *(implemented – Phase E+)*

- **Constant** (`ComponentKind.Constant`). A source utility with a fixed value of 1 to 8 bits. Like a clock divider
  or LED colour, the value lives in the reference (`ComponentRef.Constant(width, value)`, stored in the arity
  byte), so the signature (one output `Pin` of the width) is still a pure function of the reference.
  - The compiler builds each bit from the two primitives: a 1 is NOT of the floating (LOW) net, a 0 is AND of it.
    The engine is unchanged, `Reset()` settles constants with the rest of the logic in every context (live,
    truth table, sequence analysis), and each bit is a real driver for contention checks.
  - Editing: `WorkspaceController.ChangeConstantValue` and width changes go through `ChangeComponentCommand`
    (undoable). The inspector offers a value field (decimal, `0x`, `0b`), one toggle per bit and a label.
  - Node labels: `SetNodeLabelCommand` makes them undoable. A label is drawn before a constant's value, replaces
    a display's BIN/HEX/DEC caption, and is drawn above custom and primitive gates (section 19); boundaries are
    named by their port instead.
  - Persistence: `"kind": "constant"` with `width` and `value` (schema v7).
- **Power-on at 0.** The resolved power-on rule forces the first unknown net of each loop LOW, in the order the
  cycle search reaches the loop's gates. The pack's D latch builds its reset-side NAND first, so the search enters
  the loop through QN's gate, and every latch, flip-flop, register and counter (now synchronous: a register loading
  Q + 1) powers on storing 0. Tests pin this.
- **ROM 16×8 and CPU 4-bit** (`Examples · Computer`). The ROM selects word W(ADDR) through 40 MUX 4:1; programs are
  Constants wired to its W inputs. The CPU is a single-cycle accumulator machine:
  - opcode = INSTR[7:4], operand n = INSTR[3:0]: NOP, LDI, LDA, STA, ADD, SUB, ADDI, SUBI, JMP, JZ, JC, OUT, HLT
    (C–E execute as NOP);
  - datapath from pack gates: Decoder 4→16, registers for PC, A and OUT, two flag flip-flops, ALU 4-bit, Adder 4-bit
    for PC + 1 (CIN = NOT HLT), MUX 2:1 rows, and a RAM 16×4 for data;
  - all registers load on the rising CLK edge; the RAM gets NOT CLK, so STA writes during the LOW half while its
    address and data are stable (under unit delay the PC → ROM → decode path is far deeper than the inversion).
  - Flattened with a program ROM: 2 842 gates. A clock edge costs up to about one pass over them, because a new
    instruction ripples through most of a single-cycle datapath; edges allocate nothing.
- **CPU demo** (`GateLibrary.Examples.CpuDemo`). `AddComputer` places the ROM, the CPU and one labelled Constant per
  program word (two staggered columns, each word level with its ROM pin), `Build` adds a clock and displays and
  returns definitions + main circuit + a 4 Hz clock rate, and `Assemble`/`Disassemble` encode the instruction set.
  `ProjectController.OpenCpuDemo` turns it into a new unsaved project; the File menu reaches it through the same
  unsaved-changes prompt as New. The program is Fibonacci (1, 1, 2, 3, 5, 8, then HLT on carry).
