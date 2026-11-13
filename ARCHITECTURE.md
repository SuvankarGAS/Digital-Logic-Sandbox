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
   worklist and scratch spans, all sized from the compiled topology at construction. `Reset()` sets all nets
   LOW, evaluates every gate once and settles (so NOT outputs start HIGH). This is the only whole-circuit
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
   propagation. Step = one wave. Play = resume and flush. Reset = all LOW. `Load(compiled)` swaps in a
   recompiled circuit after an edit and **carries input values over by stable `PortId`**. A reload clears the
   change log, because it is a full refresh, not an animation.
6. **Sequential extension point.** `Settle()` is the combinational phase. A future clock step will run
   `Settle → commit ISequentialComponent state → Settle` around it without changing the propagation loop.

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

## 7. Hierarchy, inspection and versioning *(implemented – Phase 5)*

```
CircuitNavigator (engine-free, CircuitEditor/Navigation)
  levels: [Root(main circuit, root session, root history)]
          [Inspect(definition, session = owner's, path = node ids from that session's root)]
          [Draft(definition vN+1, own session, own history, DraftSource = vN)]   ← at most one
  TryOpen(node)       checks: custom gate · definition resolvable · id not already on the path · depth limit
  TryNavigateTo(i)    refuses to leave a draft with unsaved changes (unless discardChanges)
  TryBeginEdit()      replaces the top Inspect level by a Draft (CloneAsVersion(NextVersion))
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

## 8. Signal animation pipeline *(planned – Phase 6)*

`Simulation (settle) → SignalChangeBuffer → AnimationController → WireView property updates`.
One `AnimationController.LateUpdate()` drives all animations. For each changed net it schedules a
travelling highlight along the wire polyline, delayed by `wave × waveDelay`, so the visual order follows
the logical order. Animations are pooled structs in an array; a rapid re-toggle retargets the existing entry
for that net. Wires are pooled mesh views with `MaterialPropertyBlock`s (state, progress); no per-wire
coroutine or `Update()`. Disabling animation snaps views to the final state. DOTween handles the discrete presentation effects
around this pipeline: pin/indicator pulses on change, gate placement pop-in, selection feedback and
camera easing. Each effect is a recycled tween started from the same change events. LOW/HIGH/UNKNOWN/Z differ in
colour **and** in thickness/dash pattern for colour-blind accessibility.

## 9. Truth table generation *(planned – Phase 7)*

`TruthTableGenerator` compiles the target definition and creates a **fresh `SimulationContext`** with
`RecordChanges = false`, so the live context never changes and nothing needs restoring. When the target is
the currently simulated root, it reuses the already-compiled topology. Rows are enumerated in binary order with input 0 as the
most significant bit (`00, 01, 10, 11`). Work is time-sliced (rows per frame budget) via an iterator driven
by the UI, with progress reporting and a `CancellationToken`. Limits (max inputs, max rows) are configurable;
crossing a warning threshold requires confirmation. Circuits containing sequential components are rejected
with an explanation. Export: CSV and tab-separated clipboard text.

## 10. Persistence format *(planned – Phase 8)*

```json
{
  "schemaVersion": 1,
  "app": { "name": "Digital Logic Sandbox", "version": "0.1.0" },
  "settings": { "grid": {}, "animation": { "enabled": true, "speed": 1.0 }, "truthTable": {} },
  "rootCircuit": { "...CircuitDefinition DTO..." },
  "definitions": [ { "id": "…", "version": 2, "...": "..." } ]
}
```

- DTOs are separate from domain types; no Unity types are serialized.
- Each `(id, version)` is written once, regardless of instance count.
- Loading runs `IMigration` steps `vN → vN+1` over `JObject` before binding, then validates references.
- Saves are atomic: write `*.tmp`, flush, then `File.Replace` keeping `*.bak`. Autosave writes a separate
  recovery file; on startup an autosave newer than the project file is offered for recovery.

## 11. Undo / redo *(editing commands implemented – Phase 3; remaining commands – Phase 8)*

`ICommand { Execute(); Undo(); }` operating on the **model only**; views re-sync from model change events,
so undo restores the visual state automatically. Large operations (packaging, publishing a version) use
composite commands holding targeted snapshots, never whole-project serialization.

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
  node are rerouted.
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
  on direct manipulation and on destroy.

## 13. Extension points

| Future feature | Extension point |
|---|---|
| New primitives (only if ever needed) | Implement `IPrimitiveComponent`, register it |
| Sequential logic / clocks | `ISequentialComponent` + commit phase in the engine loop |
| Buses / multi-bit | `PortDefinition.Width`; net values become spans of `Signal` |
| Tri-state, contention | `Signal.HighImpedance` + net resolver replacing the single-driver rule |
| Displays (LED, 7-seg) | Output-side utility components with custom views; no logic change |
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
