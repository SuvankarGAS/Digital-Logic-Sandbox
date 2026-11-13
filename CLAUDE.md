# CLAUDE.md / MASTER IMPLEMENTATION PROMPT
# Digital Logic Sandbox – Unity

## 1. Project Overview

Build a production-ready Digital Logic Sandbox application using Unity.

The application allows users to construct digital circuits using logic gates, connect them using wires, simulate their behavior in real time, and package completed circuits into reusable custom logic gates.

The central feature is **recursive circuit composition**.

Users must be able to:

1. Place built-in logic gates in a workspace.
2. Connect their input and output pins using wires.
3. Change input values and observe signal propagation.
4. Combine multiple gates to create new logical operations.
5. Select and package a completed circuit into a reusable custom gate.
6. Reuse custom gates in the same or another circuit.
7. Combine previously created custom gates to create increasingly complex gates.
8. Save and load circuits and custom gate libraries.
9. Inspect the internal implementation of custom gates.
10. Automatically generate truth tables for completed circuits.
11. Visually observe signals travelling through circuit connections.

The application should be inspired by the circuit-building and computer-simulation concepts demonstrated in Sebastian Lague's Exploring How Computers Work series.

Use the videos as functional and educational inspiration. Do not copy proprietary assets or source code.

## 2. Technical Requirements

- Engine: Unity 2022.3.62f3.
- Language: C# 9.0.
- Rendering: Unity 2D.
- Target: Desktop-first, mouse and keyboard interaction.
- UI: Unity UI (UGUI) with TextMeshPro.
- Architecture: Modular, extensible, maintainable.
- Design: Data-driven and component-based.
- Serialization: JSON with a versioned data format.
- Code quality: Production-ready.

Important restrictions:

- Do not use .NET PriorityQueue.
- Use namespace block syntax, not file-scoped namespaces.
- Avoid unnecessary dependencies and third-party packages.
- Avoid unnecessary allocations during simulation.
- Do not use Unity MonoBehaviour.Update() independently on every gate to calculate logic.
- Do not make Unity GameObjects the source of truth for circuit data.
- Do not tightly couple the simulation engine to the UI.
- Do not hardcode gate-specific behavior into the circuit editor.
- Do not introduce architectural changes midway through development without a strong technical reason.

## 3. Initial Application Scope

The initial version must start with ONLY TWO BUILT-IN LOGIC GATES:

1. AND Gate.
2. NOT Gate.

Do not add OR, NAND, NOR, XOR, XNOR, flip-flops, or other built-in logic gates to the initial library.

Users must construct these gates themselves.

### NAND Example

Create a circuit consisting of:

- One AND gate.
- One NOT gate.

Connect the AND output to the NOT input.

Package the circuit as NAND.

### OR Example

Implement:

OR(A, B) = NOT(AND(NOT(A), NOT(B)))

This requires:

- Three NOT gates.
- One AND gate.

Package the circuit as OR.

The resulting OR gate must behave like a built-in gate when placed in another circuit.

The application must never depend on the name of a gate to determine its behavior. A custom gate's behavior must come from its underlying circuit definition.

## 4. Application Layout

Create a clean, modern, professional digital circuit editor.

### A. Top Toolbar

Include:

- New Project.
- Open Project.
- Save.
- Save As.
- Undo.
- Redo.
- Delete.
- Zoom In.
- Zoom Out.
- Reset Camera.
- Simulation Play/Pause.
- Simulation Step.
- Simulation Reset.
- Circuit Properties.
- Generate Truth Table.
- Inspect Circuit.

### B. Left Panel – Gate Library

Organize the gate library into:

**Built-in Gates**
- AND.
- NOT.

**My Custom Gates**
- Display all user-created custom gates.
- Display gate name and number of inputs and outputs.
- Support search and categorization.
- Support drag-and-drop placement.
- Automatically update when a new custom gate is packaged.

### C. Central Workspace

Requirements:

- Large pannable workspace.
- Zoom support.
- Optional background grid.
- Grid snapping.
- Gate placement.
- Gate selection.
- Multiple selection.
- Drag and drop.
- Wire creation.
- Wire deletion.
- Gate deletion.
- Connection highlighting.
- Selection outlines.
- Context menus.
- Multi-level circuit navigation.
- Signal visualization.

### D. Right Panel – Properties Inspector

For gates:

- Name.
- Gate type.
- Input count.
- Output count.
- Position.
- Custom gate information.

For wires:

- Source.
- Destination.
- Signal value.

For circuits:

- Circuit name.
- Input interface.
- Output interface.
- Packaging options.
- Truth table options.

### E. Bottom Panel – Simulation Information

Display:

- Simulation status.
- Current evaluation count.
- Circuit validation errors.
- Connection errors.
- Current input/output values.
- Simulation warnings.
- Signal propagation status.

Keep the interface clean and avoid unnecessary information.

## 5. Core Circuit Architecture

Implement a graph-based circuit architecture independent of Unity visual objects.

A circuit consists of:

1. Circuit Definition.
2. Circuit Nodes.
3. Input Ports.
4. Output Ports.
5. Connections.
6. Signal Nets.
7. Circuit Metadata.

### Circuit Definition

Include:

- Unique ID.
- Name.
- Description.
- Version.
- Input port definitions.
- Output port definitions.
- Internal node definitions.
- Internal connections.
- Circuit layout information.
- Creation metadata.

### Circuit Nodes

Every node must have:

- Unique node ID.
- Component definition ID.
- Position.
- Rotation.
- Input ports.
- Output ports.
- Node metadata.

Support:

1. Primitive nodes.
2. Custom circuit nodes.

### Primitive Nodes

Represent built-in components such as AND and NOT.

Their logic implementation must be independent of visual representation.

### Custom Circuit Nodes

A custom node references a previously packaged circuit definition.

It exposes only its external input and output ports.

Its internal implementation remains encapsulated when used in another circuit.

### Connections and Nets

Connections identify:

- Source node ID.
- Source port ID.
- Destination node ID.
- Destination port ID.

Use a proper net model rather than treating every visual wire segment as an independent signal.

Initially support:

- One output driving multiple inputs.
- One driver per logical net.
- No multiple active drivers.
- No invalid self-connections unless explicitly supported.

Keep the architecture extensible for buses and tri-state drivers.

## 6. Logic Simulation Engine

Implement a dedicated simulation engine independent of MonoBehaviour lifecycle methods.

### Signal Model

Initially:

- LOW = 0.
- HIGH = 1.

Keep the signal representation extensible for:

- UNKNOWN.
- HIGH-IMPEDANCE.
- Other future signal states.

### Built-in Gate Logic

AND:

Output = InputA AND InputB.

NOT:

Output = NOT InputA.

Use an extensible component evaluation contract or equivalent strategy.

Do not scatter gate-specific switch statements throughout the application.

### Evaluation Strategy

Implement event-driven combinational simulation.

When an input changes:

1. Update the input signal.
2. Identify dependent nodes.
3. Mark affected nodes dirty.
4. Evaluate affected nodes.
5. Propagate changed outputs.
6. Continue until the circuit stabilizes.

Requirements:

- Avoid unnecessary whole-circuit evaluation.
- Reuse collections and buffers.
- Avoid unnecessary allocations.
- Detect combinational feedback loops.
- Implement a maximum propagation safety limit.
- Provide useful diagnostics.

Keep combinational propagation and sequential state updates separate so future clocks and flip-flops can be introduced without rewriting the engine.

### Simulation Controls

Implement:

- Play.
- Pause.
- Reset.
- Manual input toggling.
- Step evaluation.
- Real-time signal visualization.

## 7. Circuit Editor Interaction

### Gate Placement

- Drag from library into workspace.
- Grid snapping.
- Automatic visual creation.
- Duplicate and delete.

### Gate Selection

- Single selection.
- Multi-selection.
- Drag selected gates.
- Selection rectangle.
- Context menu.

### Wire Creation

1. Click output pin.
2. Drag towards input pin.
3. Show preview wire.
4. Snap to compatible pins.
5. Release to establish connection.

Additional requirements:

- Highlight compatible pins.
- Reject invalid connections.
- Display connected signal values.
- Allow wire deletion.
- Support multiple connections from one output.
- Support orthogonal wire routing.
- Reroute wires when gates move.

Simulation must not depend on the visual wire path.

### Input Controls

Provide circuit input boundary controls that allow users to change signals between 0 and 1.

These are circuit utility components, not additional built-in logic gates.

### Output Controls

Provide output indicators displaying:

- LOW (0).
- HIGH (1).

## 8. Custom Gate Packaging System

This is the core feature.

Allow users to select a completed circuit and choose "Create Custom Gate".

### Packaging Interface

Allow users to specify:

- Gate name.
- Description.
- Category.
- Icon.
- Input port names.
- Output port names.
- Input ordering.
- Output ordering.

### Input Interface

Allow users to map external input ports to internal circuit input boundaries.

Example:

External inputs:
- A.
- B.

### Output Interface

Allow users to map internal circuit signals to external outputs.

Example:

External output:
- OUT.

### Packaging Validation

Validate:

- At least one valid output.
- Required inputs are defined.
- No invalid connections.
- No missing component references.
- No duplicate port identifiers.
- No unresolved internal references.
- No invalid hierarchy references.
- No unsupported combinational feedback.
- No circular custom-gate dependencies.

Allow intentional unused inputs with warnings.

### Packaging Result

After successful packaging:

1. Generate a unique ID.
2. Create a reusable circuit definition.
3. Store the complete internal graph.
4. Store input/output interfaces.
5. Store visual metadata.
6. Add it to the custom gate library.
7. Make it immediately available for placement.

Keep the original circuit intact unless the user explicitly chooses to replace it.

### Custom Gate Instantiation

When placed:

- Display one encapsulated component.
- Show only external pins.
- Evaluate using the referenced definition.
- Support multiple independent instances.
- Ensure simulation state is never shared accidentally.
- Allow internal inspection through navigation.

### Nested Custom Gates

Support custom gates containing other custom gates.

Examples:

- AND + NOT = NAND.
- NAND combinations = OR.
- Half Adders = Full Adder.
- Full Adders = Multi-bit Adder.

Use shared circuit definitions and references instead of duplicating internal graphs.

Implement hierarchy depth limits and circular-reference validation.

## 9. Hierarchical Custom Gate Inspection

This is a mandatory feature.

Users must be able to inspect the internal implementation of any custom gate.

### Opening a Custom Gate

Provide these interaction methods:

- Double-click a custom gate.
- Right-click → Open Internal Circuit.
- Inspector → Inspect Circuit.

Opening a gate should navigate to its internal circuit workspace.

### Breadcrumb Navigation

Display a navigation path such as:

Main Circuit → ALU → Full Adder → Half Adder.

Users must be able to click any breadcrumb to navigate back to that circuit level.

### Internal Circuit Workspace

When a custom gate is opened:

- Display its internal gates.
- Display its internal connections.
- Display its input boundaries.
- Display its output boundaries.
- Preserve internal gate positions.
- Allow normal circuit inspection.
- Allow editing when the user has entered edit mode.

External inputs and outputs must remain identifiable.

### Read-only and Edit Modes

Support two modes:

**Inspection Mode**
- View internal structure.
- Follow signal propagation.
- Inspect port values.
- Do not modify the circuit.

**Edit Mode**
- Add and remove components.
- Modify connections.
- Change gate positions.
- Modify input/output mappings.
- Save changes.

### Custom Gate Versioning

Modifying a custom gate must not silently corrupt circuits referencing it.

Use stable definition IDs and explicit versions.

When a custom gate changes:

1. Create a new version.
2. Identify circuits referencing the previous version.
3. Provide an option to keep existing references or update them.
4. Validate dependent circuits.
5. Avoid accidental data loss.

### Nested Navigation Safety

Prevent:

- Circular hierarchy navigation.
- Infinite recursion.
- Invalid custom gate references.
- Excessively deep hierarchy.

Provide meaningful error messages.

## 10. Animated Signal Propagation

This is a mandatory feature and one of the application's most important visual experiences.

The user must be able to see signals travelling through circuit wires when input values change.

### Signal Visualization

Use distinguishable visual states:

- LOW: Neutral or inactive wire appearance.
- HIGH: Bright or energized wire appearance.
- UNKNOWN: Warning-style appearance.
- HIGH-IMPEDANCE: Distinct future visual state.

Use a consistent visual language across the entire application.

Do not rely on color alone. Provide additional visual distinctions where appropriate.

### Signal Animation

When an input changes:

1. Update the simulation model immediately.
2. Identify affected nets.
3. Detect which signal values changed.
4. Trigger visual animations for the affected connections.
5. Animate the signal travelling from the source towards connected destinations.
6. Update gate input/output indicators.
7. Complete the visual transition.

The animation must visually represent the logical propagation order.

### Animation Requirements

- Animate only changed signals.
- Avoid animating unrelated wires.
- Support multiple simultaneous signal changes.
- Support fan-out connections.
- Handle rapid input toggling.
- Handle overlapping animations.
- Allow configurable animation speed.
- Allow animation to be disabled.
- Keep simulation correctness independent of animation completion.

### Important Simulation Separation

Simulation must never wait for visual animation to complete.

Logical propagation happens immediately in the simulation engine.

Visual animation is a separate presentation layer that consumes simulation events.

Do not use animation duration to determine logical signal timing.

### Wire Rendering

Create a reusable wire-rendering system.

Support:

- Straight wires.
- Orthogonal wires.
- Connection points.
- Signal states.
- Animated signal particles or travelling highlights.
- Wire selection.
- Wire highlighting.
- Wire rerouting.

Avoid unnecessary LineRenderer/GameObject creation during runtime.

Reuse visual objects and update only affected connections.

### User Controls

Provide:

- Signal animation enabled/disabled.
- Animation speed.
- Signal visualization mode.
- Reset visual state.

### Performance

The animation system must not cause simulation performance to degrade.

Avoid creating a coroutine or individual MonoBehaviour.Update() process for every wire.

Prefer a centralized animation controller or batched visual updates.

## 11. Automatic Truth Table Generator

This is a mandatory feature.

Users must be able to generate a truth table for any completed combinational circuit or packaged custom gate.

The truth table should validate the actual circuit implementation rather than use predefined truth table data.

### Truth Table Workflow

Provide a "Generate Truth Table" action in:

- Top toolbar.
- Circuit context menu.
- Custom gate inspector.
- Circuit properties panel.

When selected:

1. Identify the circuit's external input ports.
2. Identify the circuit's external output ports.
3. Calculate the number of possible input combinations.
4. Evaluate each combination using the actual simulation engine.
5. Collect the resulting output values.
6. Display the results in a dedicated truth table panel.

### Input Combinations

For N binary inputs:

Number of combinations = 2^N.

Generate combinations in a deterministic order.

For two inputs:

| A | B |
|---|---|
| 0 | 0 |
| 0 | 1 |
| 1 | 0 |
| 1 | 1 |

Do not hardcode the number of rows for specific gates.

### Truth Table Output

Example for a packaged NAND gate:

| A | B | OUT |
|---|---|---|
| 0 | 0 | 1 |
| 0 | 1 | 1 |
| 1 | 0 | 1 |
| 1 | 1 | 0 |

Example for a packaged OR gate:

| A | B | OUT |
|---|---|---|
| 0 | 0 | 0 |
| 0 | 1 | 1 |
| 1 | 0 | 1 |
| 1 | 1 | 1 |

These are expected test results, not predefined implementation data.

### Multiple Inputs and Outputs

Support:

- Any valid number of input ports within configured limits.
- Multiple output ports.
- Custom input names.
- Custom output names.
- Stable port ordering.

### Simulation Isolation

Truth table generation must not corrupt the current circuit simulation state.

Before evaluating combinations:

1. Capture current simulation state.
2. Create an isolated evaluation context or equivalent safe snapshot.
3. Evaluate each input combination.
4. Collect outputs.
5. Restore the original simulation state.

Do not modify the user's live circuit input values permanently.

For combinational circuits, use the combinational simulation engine.

For sequential circuits introduced in future phases, provide a separate analysis mode that requires explicit initialization and clock-sequence definitions. Do not claim that an ordinary combinational truth table fully describes a stateful circuit.

### Truth Table Limits

Generating 2^N combinations becomes expensive as the number of inputs increases.

Implement:

- Configurable maximum input count.
- Configurable maximum row count.
- Clear warnings for large tables.
- Cancellation support.
- Progress feedback for expensive generation.
- Avoid freezing the main editor.

Do not generate enormous tables automatically.

### Truth Table UI

Create a dedicated panel with:

- Circuit/gate name.
- Input port headers.
- Output port headers.
- Generated rows.
- Total combination count.
- Generation status.
- Regenerate button.
- Export to CSV.
- Copy table.

Keep the table readable and responsive.

### Truth Table Validation

Add automated tests proving:

- AND truth table.
- NOT truth table.
- NAND created from AND and NOT.
- OR created from custom gates.
- Multiple independent outputs.
- Correct input ordering.
- Current simulation state remains unchanged.
- Large input counts trigger configured limits.
- Invalid circuits report useful errors.

## 12. Circuit Navigation

Support hierarchical circuit editing.

Example:

Main Circuit
  └── Custom Gate
        └── Internal Circuit
              └── Another Custom Gate

Provide breadcrumbs.

Users must be able to:

- Open custom gates.
- Inspect internal circuits.
- Edit internal implementations.
- Return to parent circuits.
- Inspect signals at different hierarchy levels.

Maintain independent visual state for each circuit workspace where appropriate.

## 13. Saving and Loading

Implement complete project persistence.

A project must contain:

- Main circuit definition.
- All required custom gate definitions.
- Circuit instances.
- Node positions.
- Connections.
- Gate metadata.
- Project settings.
- Version information.

Use a versioned JSON schema.

Do not serialize Unity GameObjects, Transforms, or MonoBehaviour references.

Implement:

- New project.
- Save.
- Save As.
- Open.
- Autosave.
- Recovery from interrupted saves.
- Missing reference validation.
- Data migration support.

Custom gate definitions must have stable IDs.

Avoid duplicating custom gate definitions when saving multiple instances.

Persist:
- Custom gate hierarchy.
- Gate versions.
- Input/output interfaces.
- Internal circuit layouts.
- Project settings.

The project must remain usable after restarting the application.

## 14. Undo and Redo

Implement a command-based undo/redo system.

Support:

- Add gate.
- Remove gate.
- Move gate.
- Connect wire.
- Disconnect wire.
- Change input value.
- Rename custom gate.
- Package circuit.
- Change circuit properties.
- Modify custom gate interface.
- Edit internal custom gate implementation.

Undo and redo must restore both the circuit model and visual representation.

Avoid duplicating the entire scene or serializing the entire project for each operation.

Prefer reversible commands and suitable snapshots for complex operations.

## 15. Performance Requirements

Design for:

- Hundreds of gates initially.
- Thousands of gates in larger projects.
- Multiple instances of custom gates.
- Deeply nested circuits.
- Frequent input changes.
- Continuous signal visualization.
- Large truth table generation.

Requirements:

1. Centralized simulation.
2. Dirty-node evaluation.
3. Reusable collections.
4. Minimal garbage generation.
5. No LINQ in hot simulation paths.
6. Cached node and port lookups.
7. Update only changed signal visuals.
8. Avoid unnecessary GameObject creation and destruction.
9. Separate simulation from animation.
10. Keep truth table generation isolated from live simulation.
11. Support cancellation of expensive analysis operations.
12. Profile before introducing advanced optimization techniques.

Do not introduce Jobs, Burst, ECS, or multithreading without profiling evidence.

## 16. Future Expansion Requirements

The initial implementation must only include AND and NOT.

The architecture must support these future extensions.

### Phase A – Additional Combinational Logic

- NAND.
- OR.
- NOR.
- XOR.
- XNOR.
- Buffer.
- Multiplexer.
- Decoder.
- Encoder.

These should be constructible as custom gates first.

### Phase B – Sequential Logic

- SR Latch.
- D Latch.
- Clock.
- D Flip-Flop.
- Registers.
- Counters.

Introduce sequential simulation scheduling and well-defined clock semantics.

### Phase C – Multi-bit Logic

- Multi-bit inputs.
- Multi-bit outputs.
- Buses.
- Bus splitting.
- Bus merging.
- Tri-state outputs.
- Bus contention detection.

### Phase D – Visual Components

- LEDs.
- Seven-segment displays.
- Binary displays.
- Hexadecimal displays.
- Numeric output displays.

### Phase E – Computer Components

- Half Adder.
- Full Adder.
- Multi-bit Adder.
- ALU.
- Multiplexers.
- Memory cells.
- RAM.
- Registers.
- Larger computer architectures.

These are future extensions, not requirements for the first MVP.

## 17. Code Architecture and Folder Structure

Use the following modular structure:

Assets/DigitalLogicSandbox/

    Core/
        Circuit/
        Nodes/
        Ports/
        Connections/
        Signals/
        Validation/

    Simulation/
        Engine/
        Evaluation/
        Propagation/
        Diagnostics/
        Context/

    Components/
        Abstractions/
        BuiltIn/
        Custom/

    Editor/
        Workspace/
        Interaction/
        Selection/
        Placement/
        Wiring/
        Camera/
        Navigation/

    GateLibrary/
        BuiltIn/
        Custom/
        Packaging/

    TruthTable/
        Generation/
        Evaluation/
        Export/
        UI/

    Animation/
        Signals/
        Wires/
        Controllers/

    Persistence/
        Serialization/
        Deserialization/
        Migration/
        Recovery/

    Commands/
        UndoRedo/

    Rendering/
        Gates/
        Wires/
        Signals/

    UI/
        Toolbar/
        Panels/
        Inspector/
        ContextMenus/
        Dialogs/

    Utilities/

    Tests/
        EditMode/
        PlayMode/

    Prefabs/
    Scenes/
    ScriptableObjects/

Use assembly definitions where appropriate.

Suggested namespaces:

DigitalLogicSandbox.Core
DigitalLogicSandbox.Core.Circuit
DigitalLogicSandbox.Core.Nodes
DigitalLogicSandbox.Core.Signals
DigitalLogicSandbox.Simulation
DigitalLogicSandbox.Components
DigitalLogicSandbox.Editor
DigitalLogicSandbox.GateLibrary
DigitalLogicSandbox.TruthTable
DigitalLogicSandbox.Animation
DigitalLogicSandbox.Persistence
DigitalLogicSandbox.Commands
DigitalLogicSandbox.UI

Use SOLID principles where they provide practical value.

Avoid overengineering.

## 18. Testing Requirements

### Gate Tests

AND:
- 0 AND 0 = 0.
- 0 AND 1 = 0.
- 1 AND 0 = 0.
- 1 AND 1 = 1.

NOT:
- NOT 0 = 1.
- NOT 1 = 0.

### Circuit Tests

- Connect two gates.
- Connect one output to multiple inputs.
- Change input and verify propagation.
- Verify disconnected inputs are handled safely.
- Detect invalid connections.
- Detect combinational cycles.

### Custom Gate Tests

- Package NAND from AND and NOT.
- Verify NAND truth table.
- Package OR.
- Verify OR truth table.
- Place multiple instances of the same custom gate.
- Verify independent evaluation.
- Create a custom gate containing another custom gate.
- Save and reload nested gates.
- Verify packaging does not modify the original circuit.
- Reject circular custom gate references.

### Hierarchical Inspection Tests

- Open custom gate internal circuit.
- Navigate through multiple nested levels.
- Navigate back using breadcrumbs.
- Preserve internal layouts.
- Edit internal implementation.
- Create a new gate version.
- Verify older references remain valid.
- Reject invalid hierarchy references.

### Animation Tests

- LOW to HIGH transition.
- HIGH to LOW transition.
- Multiple simultaneous signal changes.
- Fan-out propagation.
- Rapid input toggling.
- Animation disabled.
- Simulation remains correct when animation is disabled.
- Animation never changes logical signal values.

### Truth Table Tests

- Correct AND truth table.
- Correct NOT truth table.
- Correct NAND truth table.
- Correct OR truth table.
- Multiple outputs.
- Stable input ordering.
- Simulation state preservation.
- Maximum row limit.
- Cancellation behavior.

### Persistence Tests

- Save and load circuit.
- Restore nodes and connections.
- Restore custom gate definitions.
- Restore nested references.
- Restore custom gate versions.
- Restore animation settings.
- Restore project settings.
- Verify schema migration.

### Performance Tests

- Hundreds of connected gates.
- Thousands of connected gates.
- Measure evaluation counts.
- Measure garbage allocations.
- Verify unrelated inputs do not cause full circuit evaluation.
- Verify wire animations do not create excessive allocations.
- Verify large truth table generation does not freeze the editor.

## 19. Development Phases

Follow these phases in order.

### Phase 0 – Project Foundation

- Inspect existing Unity project.
- Identify dependencies and conflicts.
- Create folder structure.
- Create assembly definitions.
- Establish coding conventions.
- Create initial scene.

Deliverable: Clean project foundation.

### Phase 1 – Core Circuit Model

- Circuit definitions.
- Node definitions.
- Ports.
- Connections.
- Signal representation.
- Validation.

Deliverable: Testable graph-based circuit model.

### Phase 2 – Simulation Engine

- AND.
- NOT.
- Signal propagation.
- Dirty-node evaluation.
- Cycle detection.
- Diagnostics.

Deliverable: Fully testable headless simulation engine.

### Phase 3 – Visual Circuit Editor

- Workspace.
- Camera movement.
- Zoom.
- Grid.
- Gate placement.
- Selection.
- Wire drawing.
- Input controls.
- Output indicators.

Deliverable: Users can visually construct and simulate circuits.

### Phase 4 – Custom Gate Packaging

- Circuit selection.
- Packaging interface.
- Input/output mapping.
- Validation.
- Custom gate generation.
- Custom gate library.
- Custom gate instantiation.

Deliverable: Users can construct and package NAND and OR.

### Phase 5 – Hierarchical Composition and Inspection

- Nested custom gates.
- Circuit navigation.
- Breadcrumbs.
- Internal inspection.
- Edit mode.
- Gate versioning.
- Hierarchy validation.

Deliverable: Fully functional recursive custom gate composition.

### Phase 6 – Animated Signal Visualization

- Centralized animation controller.
- Signal change events.
- Wire animations.
- Signal states.
- Animation controls.
- Performance optimization.

Deliverable: Visually understandable signal propagation.

### Phase 7 – Truth Table Generator

- Input combination generation.
- Isolated simulation evaluation.
- Output collection.
- Truth table UI.
- CSV export.
- Copy functionality.
- Generation limits and cancellation.

Deliverable: Automatic truth table generation for combinational circuits.

### Phase 8 – Persistence and Undo/Redo

- Save/load.
- JSON serialization.
- Custom gate persistence.
- Versioning.
- Recovery.
- Undo/redo.

Deliverable: Complete persistent project workflow.

### Phase 9 – Polish and Optimization

- UI refinement.
- Better wire routing.
- Context menus.
- Error messages.
- Performance profiling.
- Automated testing.
- Integration testing.

Deliverable: Production-ready initial release.

## 20. Final Acceptance Criteria

The first release is complete only when this complete workflow works:

1. Launch application.
2. Create a new project.
3. See only AND and NOT in the built-in gate library.
4. Place two NOT gates and one AND gate.
5. Connect them to construct NAND.
6. Provide two external inputs.
7. Change input values.
8. Observe correct outputs.
9. Observe animated signal propagation.
10. Generate the NAND truth table automatically.
11. Verify all four input combinations.
12. Select the circuit.
13. Open custom gate packaging.
14. Name the gate NAND.
15. Define two inputs and one output.
16. Package the circuit.
17. See NAND in the custom gate library.
18. Place NAND in another circuit.
19. Open NAND and inspect its internal implementation.
20. Navigate back using breadcrumbs.
21. Construct OR using previously created gates.
22. Package OR.
23. Generate and verify the OR truth table.
24. Save the project.
25. Close and reopen the application.
26. Verify all circuits, custom gates, hierarchy, and settings are restored.

Everything must work without hardcoded NAND or OR implementations.

## 21. Instructions for Claude Code

Act as a senior Unity engineer and software architect.

Before implementing:

1. Inspect the complete existing Unity project.
2. Identify existing dependencies.
3. Understand the current scene structure.
4. Identify conflicts.
5. Propose an implementation plan.
6. Confirm that recursive custom gate composition is supported.
7. Confirm that simulation, visualization, and truth table evaluation are properly separated.

Then implement phase by phase.

For every phase:

- Implement complete working functionality.
- Create all necessary C# files.
- Create prefabs and ScriptableObjects when required.
- Configure assembly definitions.
- Wire up Unity components.
- Create automated tests.
- Run available tests.
- Fix compilation errors.
- Verify integration with previous phases.
- Update documentation.

Do not merely generate code snippets or pseudocode.

Do not leave critical methods with TODO comments or empty implementations.

Do not move to the next phase with unresolved critical issues.

When multiple technical approaches are possible, choose the simplest scalable solution and document the decision.

Maintain:

### DEVELOPMENT_LOG.md

Document:
- Completed phases.
- Implemented features.
- Architectural decisions.
- Known issues.
- Pending tasks.
- Test results.

### ARCHITECTURE.md

Document:
- System responsibilities.
- Data flow.
- Simulation lifecycle.
- Circuit graph model.
- Custom gate packaging.
- Hierarchy and versioning.
- Signal animation pipeline.
- Truth table generation.
- Persistence format.
- Extension points.

### Final Architectural Principles

1. The application must be a generic digital logic circuit simulator, not a collection of hardcoded logic gates.
2. AND and NOT are the only initial built-in logic primitives.
3. Everything else should be constructible by combining and packaging existing components.
4. Custom gates must support recursive composition.
5. Simulation must be independent of Unity visual objects.
6. Animated visualization must never determine logical simulation results.
7. Truth table generation must evaluate the actual circuit implementation.
8. Custom gate inspection must support nested navigation.
9. All project data must be persistable and versioned.
10. Prioritize correctness, extensibility, performance, maintainability, and user experience.

**The most important objective is to make circuit composition, packaging, reuse, and inspection work reliably. Everything else should be built around this foundation.**