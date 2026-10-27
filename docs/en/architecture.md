# Penelopa Architecture

## Overview

Penelopa is a Blazor WebAssembly primitive alignment editor that consumes the
Atlas.Blazor 0.3.0 NuGet package for its dockable workspace shell. The editor
logic is layered so each concern is independently testable.

## Layers

```text
src/Penelopa.Web (WASM app)
  ├── Components/PenelopaWorkspace.razor   Atlas six-region dockable layout
  ├── Components/PenelopaTopToolbar.razor  Hosted in the Atlas top toolbar slot
  ├── Components/PenelopaStatusBar.razor   Hosted in the Atlas status bar slot
  ├── Components/Panels/                   Tools / PrimitiveTree / Canvas / Properties
  └── wwwroot/css/app.css                  Editor shell styles + bright studio theme
src/Penelopa.Rendering                     SkiaSharp canvas + color-key hit testing
src/Penelopa.Core                          Primitive model + alignment algorithms
```

### Penelopa.Core

Pure domain logic with no UI or SkiaSharp dependency:

- `Primitives/` — `Primitive` base class and `Circle` / `Rectangle` / `Triangle`
  subclasses, each exposing an editable `PropValue` property bag (`Float`,
  `Double`, `Int`, `Uint`, `Color`, `Bool`, `String`). `PropValue.TrySetText`
  validates input in the domain (finite numbers, positive dimensions,
  `#RRGGBB`/`#RRGGBBAA` color text, trimmed non-empty names up to 128
  characters). Unknown `PropValue` subclasses throw `NotSupportedException`
  from history/serialization instead of being silently dropped.
- `Alignment/` — `AlignType` enum (Left/HCenter/Right/Top/VCenter/Bottom),
  `Box` (axis-aligned bounding box), `Transform`, and `AlignExtensions.Align`
  which moves all selected items so their alignment value matches the union
  bounding box. The operation is idempotent: already-aligned selections return
  false and nothing moves.
- `Services/IPrimitiveService` — single source of truth for the document and
  selection, shared across panels via DI (registered as a singleton).
  `GetAll()` / `GetSelection()` return read-only snapshots; `GetState()`
  returns an immutable `DocumentState`. Structural operations validate that
  the container belongs to this document, reject self/duplicate-parent/cycle
  moves, and maintain an internal document owner so primitives cannot be
  attached to two documents. `GroupSelection` and `TryUngroup` implement the
  root-level grouping rules in ADR-0004.
- `Services/DocumentHistory` — snapshot-based transactions. A snapshot keeps
  primitives by reference plus every property value and container child list;
  selection is not captured (see ADR-0002). Every mutation creates at most one
  undo entry, and cancellation applies the same snapshot as undo.
- `Simulation/` — immutable keyframe configuration (`SimulationConfig`,
  `SimulationTrack`, `SimulationKeyframe`), structural/domain validation, a
  deterministic evaluator (linear, ease presets, step and cubic-bezier with
  control points in `[0,1]`), the global timeline, the preview-isolated
  `SimulationRuntime`, the singleton `SimulationConfigStore` (50-step config
  history and dormant resolution), and the sampling
  `SimulationBoundsEstimator`. It has no Blazor, SkiaSharp or JS dependency.

### Penelopa.Rendering

SkiaSharp rendering extracted so hit-testing logic can be unit tested without
a browser:

- `CanvasRenderer` is an orchestrator: it owns the draw bitmap and the two
  canvases, and coordinates `ViewportController`, `ScenePainter`,
  `HitTestBuffer` and `SelectionOverlay`. `Render` receives the SKGLView
  `e.Info` size, clears both surfaces, paints the scene (visible color on
  the draw bitmap, color key on the hit bitmap), blits the draw bitmap, and
  draws the selection overlay. It implements `IDisposable`.
- `ViewportController` owns the shared `ViewTransform` (y-flip world
  transform scaled by DPR), zoom/pan/Fit and the world/CSS conversions.
  Rendering, selection hit testing and pointer mapping all use it. Panel
  resize and DPR changes update the buffer/DPR while preserving zoom/pan;
  the pointer layer measures the inner canvas content origin, so its 1px
  border does not offset hit testing.
- `ScenePainter` draws the primitive tree and coordinate axes. The axes are
  painted in screen space under the primitives: the origin is the projected
  world `(0,0)`, the visible axis lines span the full viewport, and the
  arrows/labels are clamped inside the viewport, so zooming out or panning no
  longer shortens or clips them. Visible and hit canvases receive the same
  container matrices. Paints, triangle/arrow paths and the text font are
  reused across primitives/frames instead of allocating per primitive; hit
  drawing disables antialiasing so 1px edges remain hittable.
- `HitTestBuffer` owns the off-screen bitmap, per-frame color-key map and
  pixel-to-primitive lookup. Keys are allocated from `0xFF000001` through
  `0xFFFFFFFF` for every frame (black `0xFF000000` is the background and is
  never a key). The capacity is 16,777,215 primitives per frame; exhausting it
  throws an `InvalidOperationException` instead of wrapping into the
  background/transparent range. There is no persistent `ColorKeyManager`;
  keys only need to be stable between one render and the following hit test.
- `SelectionOverlay` draws the selection box and four 8px screen handles.
  Handle hit testing uses an adaptive radius: 5 CSS px for large selections,
  shrinking to 20% of the shorter screen side (min 1.5 px) for tiny ones, so
  the four corner zones never swallow the whole body of a default 10×10
  primitive. Selections whose own/ancestor transform is not axis-aligned
  (rotated/sheared containers) expose no handles because a world AABB cannot
  be represented by local-axis scaling; their selection box is still drawn.
- `HitTest(cssX, cssY)` converts browser CSS coordinates through the shared
  `ViewTransform` and resolves the color-key pixel back to a primitive.
- Viewport changes preserve user intent: resizing the panel or changing the
  device pixel ratio updates the buffer/DPR while keeping `Zoom` and
  `PanX/PanY`. The pointer layer measures the inner canvas content origin
  (`getBoundingClientRect() + clientLeft/clientTop`), so its 1 CSS px border
  no longer offsets hit testing. `CanvasRenderer` is `IDisposable`; the host
  disposes its bitmaps, paints, and font when the panel is destroyed.

### Penelopa (WASM app)

- `PenelopaWorkspace.razor` hosts the workspace through the programmatic
  API: it builds the `AtlasWorkspaceDefinition` (via
  `PenelopaWorkspaceDefinition`) and passes the resulting `IAtlasWorkspace`
  to `AtlasWorkspaceHost` with `WorkspaceOwnership.External`, so the
  component owns disposal. The layout is a six-region workspace: an
  inline-start dock (Tools + Primitives over an empty lower slot), a central
  document group (Diagram), an inline-end dock (Properties over an empty
  lower slot), and a block-end dock with two empty slots. The side docks are
  responsive instead of fixed: the left dock uses a 22% proportional basis and
  the properties dock 30% of the remaining split, with 160–300px constraints
  recorded on the splits (Atlas 0.3.0 uses them for splitter interactions; the
  initial proportional tracks themselves are not clamped). At
  1440/1280/1024/900/800/700 viewports the canvas
  measures roughly 731/643/504/436/381/327 CSS px; below 900px compact CSS
  reduces panel padding and control sizes so the add/align/property controls
  remain reachable without horizontal clipping. The four empty groups stay
  collapsed and only keep their toolbar slots.
  `PenelopaTopToolbar` and `PenelopaStatusBar` are supplied through the
  `TopToolbarContent` / `StatusBarContent` host slots, and the four panels
  are registered through `ContentRoutes` using the kind constants in
  `PenelopaContentKinds`.
- `PenelopaWorkspaceDefinition.cs` builds the layout tree programmatically
  (`SplitNode` / `GroupNode` / `DockItem` arrays plus toolbar states),
  mirroring the declarative defaults: the inner dock splits and the bottom
  dock use a 0.5 proportional basis, the document group uses Scroll overflow
  with Adjacent activation, and the empty collapsed groups are persistent
  with no selected item.
- `CanvasPanel.razor` hosts the `SKGLView` (`EnableRenderLoop=false`;
  physical-resolution rendering via the default `IgnorePixelScaling=false`).
  Rendering is on demand: the panel calls `SKGLView.Invalidate()` when the
  document/selection changes, on pointer/wheel/pan gestures, on DPR changes
  and on Fit. The canvas element fills the viewport (no fixed content size),
  and the view itself is managed by `ViewTransform` zoom/pan: Ctrl+wheel
  zooms around the pointer (plain wheel is left to the page), dragging empty
  space pans, and the Fit button
  (`renderer.FitToContent`) centers the content in the viewport, so
  scrollbars are no longer needed. It wires `OnPaintSurface` →
  `CanvasRenderer.Render` with the event's `e.Info` size and the current
  `devicePixelRatio`, plus `mousedown` → `HitTest` → selection (Ctrl-click
  appends). The canvas element is keyboard focusable (`tabindex="0"`,
  `role="application"`, `aria-label` plus a visually hidden instruction
  paragraph): arrow keys move the top-level selection by 1 world unit
  (Shift+arrow = 10), Delete removes the selection through a single
  transaction, and pointer-down focuses the canvas. A small helper script
  (`wwwroot/js/penelopa.js`, compiled from `ts/penelopa.ts`; the compiled
  output is committed so the project runs without node) watches
  `devicePixelRatio` changes (matchMedia) so moves across displays with
  different DPI stay in sync, and hosts the pointer layer (capture,
  canvas-relative CSS coordinates, synthesized-mouse suppression, rAF
  throttling, wheel zoom) that reports semantic callbacks into the
  interaction controller. Pointer coordinates are relative to the inner
  canvas content origin (excluding its 1 px border), so hit testing matches
  the painted surface; the origin is refreshed lazily and on wheel /
  pointerenter / resize / scroll / `ResizeObserver` invalidation. The very
  first Ctrl+wheel after page load zooms without a preceding pointer press.
  Editor shortcuts are ignored when the event target is an
  `input`/`textarea`/`select`/`contenteditable`, leaving Ctrl+Z inside a
  property field to the browser's text undo. `watchDpr` returns a disposable
  handle; the panel removes the `matchMedia` listener before releasing the
  `DotNetObjectReference`, so a recreated panel cannot be called after
  disposal.

### Containers and drill-down

`Container` (Core) groups children and applies an affine `LocalTransform` —
the A661-style rotation / offset / flip containers are factory variants of
the same type. The container has no visible shape; children render inside
its transform (`DrawNode` applies the matrix to both the visible and hit
canvases under a Save/Restore, so color-key hit testing matches what is
drawn). Children hold local coordinates; `Primitive.Translate` /
`SetBounds` / `ContainsWorldPoint` convert world input through the parent
chain (`Container.ToLocalVector` / `ToLocalPoint` / `WorldToLocalBounds`),
so dragging inside a rotated container follows the pointer.

The `SetBounds` contract is explicit: `Primitive.SetBounds(worldBounds,
worldAnchor)` performs the world → parent-local conversion once, then calls
`SetBoundsLocal(localBounds, localAnchor)`. Implementations of
`SetBoundsLocal` only read and return local boxes (circle returns its local
box, triangle normalizes its vertices against its local box) and must not
call `GetWorldBoundingBox`; the result is converted back to world once.
`WorldToLocalBounds` walks the ancestor chain, so nested containers map
correctly. The container's world box is the AABB of its transformed
children, so selection boxes and alignment work unchanged. A rotated/sheared
ancestor makes exact world-AABB resizing unrepresentable; `CanResizeInWorld`
is false there and the UI hides the handles instead of drawing a box that
does not follow the pointer. Axis-aligned containers (offset/flip) resize
non-uniformly; rotated containers keep the existing uniform local-scale
semantics for programmatic calls.

Drill-down selection (hit-through) collects the candidates at a point with
`CanvasRenderer.PickAll`: the topmost color-key primitive plus its ancestor
chain, ordered root → deepest leaf (the confirmed direction). The
`EditorInteractionController` starts each click at the outermost candidate
and advances the index on repeated clicks at the same spot (within 4 world
units and 500 ms), stopping at the deepest leaf; moving away or clicking
empty space resets the context. Ctrl-click skips drilling (append/toggle),
the tree panel selects any ancestor directly, and ESC cancels gestures.

Selection is normalized at every entry point (`SetSelected`,
`SetSelectedRange`, `AppendSelected`): when an ancestor is selected its
descendants are excluded. Drag, align, and the multi-selection union box
therefore operate on top-level selections only, so a child is never
transformed twice through both itself and its container.

### Document state

`PrimitiveService` is the single source of truth. `GetState()` returns an
immutable `DocumentState` snapshot (copied root and selection lists),
`GetAll()` / `GetSelection()` return read-only snapshots, and one
`Changed(DocumentChange)` event carries the change flags (structure /
selection / geometry) plus the new snapshot. `CanvasPanel`,
`PrimitiveTreePanel`, and `PropertyPanel` initialize from `GetState()` and
re-derive everything from `Changed`; they no longer mirror separate
collections. `ReplaceAll` always emits structure + selection, so loading a
document with no selection clears the property panel instead of keeping a
stale target.

The property panel edits every selected primitive, not just the first. It
shows only properties whose name and runtime type exist on all selected
primitives; equal values are displayed normally, while differing values use
an empty field with a `Multiple values` placeholder. A write goes through
`TrySetProperty(owners, name, text)`, which validates every owner, rolls back
on any rejection, and commits one history entry. `Name` is a normal editable
string property (trimmed, non-empty, at most 128 characters). A `Delete`
button in the Tools panel and the Delete key call `RemoveRange`, which removes
all selected top-level primitives in one transaction, clears stale selection
(including descendants of removed containers), and is undoable.

### Grouping and ungrouping

The Tools panel exposes Group and Ungroup. Grouping requires at least two
normalized root-level selected primitives. The service creates an identity
offset container named `Group` at the first selected root's index, moves the
roots into it in root order (so world positions and z-order are unchanged),
selects the container, and commits one transaction. Undo restores the previous
root list; because selection is not history, the removed group is filtered out
and the selection becomes empty.

Ungroup only accepts a root container with an identity `LocalTransform`
(offset 0, rotation 0, scale 1; 360° is accepted within floating-point
tolerance). Nested containers and containers with a non-identity transform are
rejected with a readable error and the button is disabled; a rejected command
creates no history entry. An accepted ungroup merges the children back into the
root list at the container index, removes the container, selects the children,
and is undoable. Snapshot restoration detaches nodes from non-target parents
and assigns document ownership only after installing the root list, so group
containers that exist in only one history state restore correctly.

### History and persistence

`PrimitiveService` owns a `DocumentHistory` (snapshot-based undo/redo; see
[ADR-0002](../adr/0002-history-transactions-and-snapshots.md)).
A snapshot captures the primitive tree by reference plus every primitive's
property values and container children. Selection is UI state and is not
captured; undo/redo re-applies the live selection to surviving primitives.
Capture is exception-safe: an unsupported property aborts the transaction
without leaving the history depth corrupted.

All document mutations run through the service-level transaction boundary:
structural changes (`Add`/`RemoveRange`/`AddToContainer`), alignment,
single/multi-owner property edits, keyboard nudges, and drag/resize gestures
call `BeginTransaction`, mutate, then `CommitTransaction` for exactly one undo
entry. `CancelTransaction` applies
the same pre-transaction snapshot to restore every property and container
child, so ESC/pointer-cancel is exact (it never recomputes `SetBounds`).
Gestures open the transaction lazily on their first real mutation, and
no-op clicks or already-aligned alignments produce no history entry.
Applying a snapshot rebuilds the root list, restores values and children,
and re-selects surviving primitives by reference. Before reattaching a child,
restoration detaches it from any parent that is not its target parent and
temporarily clears ownership; ownership is rebuilt from the restored roots.
Ctrl+Z / Ctrl+Shift+Z (or Ctrl+Y) drive undo/redo through the pointer layer's
key handling.

Property inputs are validated in the domain (`PropValue.TrySetText`):
radius/width/height must be positive, numeric values must be finite and
within the editor magnitude, colors accept `#RRGGBB`/`#RRGGBBAA` (the
properties panel edits them as R/G/B/A channels), and names are trimmed,
non-empty and at most 128 characters. Invalid text shows
a field error and is never written; deserialization validates every
property with the same rules and rejects the document if a value is
invalid.

`DocumentSerializer` round-trips the document to JSON: primitives are
stored by type with their property values (color keys excluded — they are
rebuilt on load), containers nest their children, and the selection is
saved as primitive ids so it re-resolves against the rebuilt tree.
Documents carry `Version` 2; only version 2 documents are accepted.
Versionless, v1, negative and future versions are rejected, and the Import/Load
boundaries report the rejected version with a context-specific error (ADR 0008).
The
optional `simulation` section stores the loop flag and tracks (primitive id,
Float property name, ordered keyframes with time/value/interpolation). Loading
validates geometry strictly and validates the simulation section separately:
targets are resolved first, and tracks whose primitive/property no longer
exists are pruned with a warning before structural/value validation. A
structurally invalid active track still discards the whole simulation section
with a warning while the document loads. Raw track/keyframe count limits are
checked before pruning. Dormant tracks are never written on save. Loading validates non-empty unique ids, rejects children under
non-containers and enforces node (10,000), depth (64) and UTF-8 size (4 MiB)
limits. JSON arrays with `null` primitive or child entries are rejected as an
invalid document instead of dereferencing null.

`DocumentStorage` is the shared save/load boundary for the Tools panel and the
simulation page: it serializes document v2, keeps the previous payload in
`penelopa.document.backup`, and turns JS failures into displayable errors.
localStorage access goes through the `penelopa.readStorage`/`writeStorage`
helpers, which return result objects instead of throwing, so expected quota or
security failures do not reach Blazor's global error UI. Its load boundary
converts any remaining unexpected exception into a
`DocumentLoadResult` error, so a corrupt payload cannot tear down the renderer;
the full exception is logged while the UI shows a single-line summary. The
Tools panel parses and validates a load before asking for confirmation,
shows a readable error while keeping the current document on failure, and
always applies the resolved selection (including the empty one). Loading
replaces both the document and the simulation configuration. The panel uses a
single pending operation (dormant save or load confirmation); starting a load
cancels an unconfirmed dormant save, and a confirmed save recomputes the
current active/dormant tracks instead of reusing an old snapshot. Data lives
in `localStorage` under `penelopa.document`.

### Interaction

Editing gestures run through `EditorInteractionController` in Core — a
small state machine (Idle / Pressed / Dragging / Resizing) that receives
world-space pointer positions and a layered `HitTestResult`, snapshots
the selection and geometry at pointer-down, and notifies panels once on
commit. ESC, pointer-cancel, lost capture, or window blur restores the
snapshot and returns to Idle. Clicking an already-selected member of a
multi-selection defers the decision until pointer-up, so the same gesture
either collapses the selection (click) or drags the group; Ctrl-click
toggles membership. Resize keeps the corner opposite the dragged handle
fixed (`ResizeMath`, with a minimum size and mirror-flip when crossing
the fixed corner), and primitives fit themselves to the target bounds
through `SetBounds(bounds, anchor)`: circles keep their aspect with the
fixed corner on the boundary, triangles map their vertices by normalized
position. Cancelling a gesture rolls back through the shared transaction
snapshot rather than recomputing bounds.
- `ToolPanel.razor` exposes Add (Circle/Rectangle/Triangle), six-direction
  Align, Delete-selection, Group and Ungroup actions; Align, Delete, Group and
  Ungroup go through the service transaction. Delete is disabled without a
  selection; Group requires two or more root primitives and Ungroup requires
  one root container with an identity transform. The hint below the buttons
  explains both conditions for assistive technology and sighted users.
- `PrimitiveTreePanel.razor` lists all primitives; clicking selects. Selected
  items expose `aria-pressed="true"` for assistive technology.
- `PropertyPanel.razor` renders the single or common multi-selection
  `PropValue` bag through the typed input components in `Panels/Props/`.
  Each field has a stable id, a `<label for>` association, and
  `aria-invalid`/`aria-describedby` pointing at its error message. Inputs
  commit on change through `IPrimitiveService.TrySetProperty` (validation +
  single undo entry). `PropEditorBase` centralizes conflict display and
  atomic batch writes.

### Simulation

The `/simulation` page (`Pages/SimulationPage.razor`) shares the singleton
`IPrimitiveService` and `SimulationConfigStore` with the editor but keeps its
own local selection. Its canvas is read-only: no hit testing, drag, resize or
selection overlay, and the visual surface is exposed as `role="img"` while the
Fit button remains an ordinary button. The page hosts its own
`penelopa-simulation` Atlas workspace: the transport and page notices occupy
the fixed top slot, while the work area contains a protected primitives tool
group, a canvas document and a protected inspector tool group over a full-width
timeline tool group. The required regions cannot be closed or auto-hidden; tool
groups can be resized, collapsed, moved and regrouped, and the canvas document
can be resized and moved but not closed or split. The layout definition lives
in `Components/Simulation/SimulationWorkspaceDefinition.cs`.

`SimulationRuntime` is a deterministic preview session. On the first Play /
seek / step it captures every Float property's base value, resolves tracks into
active and dormant sets, and evaluates keyframes at the requested time. The
runtime writes evaluated values transiently and restores the base values on
`Stop`, page exit or an error; it never creates document-history entries. Page
exit (including browser back) stops the runtime synchronously before JS handles
are awaited and notifies the shared primitive service once, so the editor's
property panel and canvas re-read the restored values without a reselection.
It has five states (`Ready`, `Preview`, `Playing`, `Paused`, `Finished`) and a
single global absolute timeline `0..T`, where `T` is the last keyframe time
across active tracks. Playback either wraps the whole timeline (Loop) or reaches
`Finished` and holds the last frame.

A JS `requestAnimationFrame` loop calls `OnSimulationFrame`; the page advances
the runtime from a monotonic `Stopwatch` rather than accumulating frame deltas,
stops the loop in Ready/Paused/Finished, and pauses playback when the tab is
hidden. While `Playing`, the SKGLView render loop is enabled because on-demand
`Invalidate` calls can otherwise be coalesced with the simulation rAF to about
30 paint/s; Ready/Paused/Finished return to on-demand rendering. A
`SimulationReadoutChannel` publishes time
and track values at 20 Hz. Only small subscribers consume it (the time readout,
per-property current values, the playhead and the native scrubber), so the
inspector table and timeline markers are not re-rendered on the readout path
and the primitive tree or whole page stays untouched.

Rendering uses the render-only path
(`CanvasRenderer.Render(..., renderHitBuffer: false)` and a null hit canvas in
`ScenePainter`), so playback skips the color-key buffer and the selection
overlay. `SimulationBoundsEstimator` samples keyframe times and adjacent
midpoints, unions the world bounds and restores the base values; it drives the
initial animation-aware Fit and the Fit button.

The inspector creates a track with **Animate**, which places two keyframes at
the playhead and `+1s`; it adds/updates keyframes at the playhead, edits
time/value/interpolation/custom cubic-bezier control points, deletes
keyframes/tracks and shows live track values. Values are labelled as relative
to the parent. The timeline shows only the selected widget's tracks, with a
ruler, a native range playhead, zoom/scroll and focusable keyframe markers.
Clicking a ruler tick seeks to that time and double-clicking a marker invokes
the same Go to path as the inspector. Dragging a marker retimes it with pointer
capture acquired only after real movement (so click and double-click still
target the marker), 1/60s snapping, a 1 ms minimum gap and neighbour clamping;
the inspector is the keyboard alternative and provides Go to. Keyframe
selection is anchored by time, so deleting or undoing a neighbouring keyframe
follows the same keyframe instead of jumping to another index.

Configuration edits are allowed in Ready, Preview, Paused and Finished, but not
while Playing. A separate 50-step configuration history (buttons plus
Ctrl+Z/Ctrl+Y when focus is outside a text field) is cleared on page entry and
document load and never shares entries with the document history. Invalid
active tracks block playback; the page shows all blocking issues in a polite
summary while field edits keep inline errors. Dormant tracks are listed in a
page banner with counts, expandable details (primitive id, property, keyframe
count), per-track removal, and an explicit remove-all action; cleanup is
undoable. Save is enabled only in Ready and writes the document plus the active
simulation configuration; if dormant tracks exist, Save requires confirmation
and excludes them. Removing the last dormant track clears a pending save
confirmation.

Measured performance in the periodic harness: 500 primitives with 100 active
tracks run at 59.9–60.2 JS rAF ticks/s, 59.9–60.0 executed .NET frames/s and
59.9–60.1 SKGLView paint/s; Ready after Stop idles at 2.94–3.75% main-thread CPU in the reference
headless Chromium/1440×900 run. Selecting a 200-keyframe track and playing for
five seconds measures about 60 ticks/s and 60 paint/s (59.79–60.18 and
59.76–60.04 across runs). The harness reads three
separate counters (rAF attempts, `OnSimulationFrame` executions and
`OnPaintSurface` callbacks); tick alone was insufficient. These counters are
attached to `window.penelopa` only when the PERF harness sets
`__PENELOPA_PERF__` before app startup; normal editor/simulation pages expose
no performance-counter API.

## Accessibility

- The simulation page provides an `h1` (with `tabindex="-1"`) focused on
  navigation, never autoplays, exposes the canvas as `role="img"`, uses native
  transport buttons and `aria-live="polite"` state/error regions, associates labels and
  `aria-describedby` with keyframe fields, and keeps a keyboard-editable
  keyframe inspector as the alternative to timeline dragging. It reports
  `prefers-reduced-motion` with a hint while still allowing user-initiated
  playback. The 20 Hz time/value readouts are intentionally not live regions.
- The canvas is a focusable `role="application"` surface named
  `Drawing canvas`, with a visually hidden description of the keyboard path.
  Arrow keys move the top-level selection by 1 world unit (Shift = 10),
  Delete removes it, and Escape cancels an active gesture. Editor shortcuts
  ignore events whose target is an `input`/`textarea`/`select`
  /`contenteditable`, so text editing (including Ctrl+Z and Delete) is left
  to the browser.
- The property inputs and the primitive tree expose names/state as described
  above; the status bar is a polite live region (`role="status"
  aria-live="polite"`).
- The viewport meta tag allows browser zoom (no `maximum-scale` or
  `user-scalable=no`).
- bUnit covers label/ARIA wiring for the panels; the Playwright suite covers
  the canvas keyboard path, input shortcuts, ARIA attributes and page zoom.

## Responsive layout

Side docks use proportional splits rather than fixed 300px tracks. Compact
CSS below 900px reduces panel padding, button size and tab padding. Measured
canvas widths are 731/643/504/436/381/327 CSS px at
1440/1280/1024/900/800/700 viewport widths, with no document-level horizontal
overflow and all four panels still reachable. The Playwright layout test
asserts the canvas width, absence of horizontal overflow, and visibility of
the Add/Align/Properties controls at every width. A separate characterization
test records that Atlas 0.3.0 does not clamp the initial proportional tracks to
the 160px minimum; if an Atlas upgrade starts clamping them, that test and this
section must be revisited.

The simulation page uses an Atlas workspace
(`Components/Simulation/SimulationWorkspaceDefinition.cs`) with a 220px tree,
a flexible canvas, a 320px inspector and a 208px full-width timeline by
default. Atlas toolbars are collapsed to keep a single custom top strip; the
splitter controls collapse the tree, inspector and timeline, and a scoped CSS
rule keeps their expand chevrons visible after collapse because Atlas 0.3.0
hides the splitter with the collapsed child. The canvas is a document group
and cannot be collapsed. Layout is rebuilt on every entry and is not persisted.
The timeline scrolls horizontally when zoomed and vertically when many tracks
are present. The page reuses the `--penelopa-*` theme tokens.

## Extending primitives and properties

There is no type registry yet. Adding a primitive type still requires:
the model in `Primitives/`, a branch in `ScenePainter.DrawNode`, a branch in
`DocumentSerializer.FromDto`/`ToDto` (type name + property names), and a
property-editor branch in `PropertyPanel` if it introduces a new
`PropValue` subtype. Unknown `PropValue` subtypes fail loudly in history and
serialization; they are rejected before entering the document. A registry or
visitor is a larger refactor and was intentionally not added in Phase C.

## Height Chain

Atlas workspaces are sized by their parent; the editor shell must chain
`html/body/#app/.penelopa-main/.penelopa-shell/.atlas-v2-workspace` to 100%
height (see `wwwroot/css/app.css`). The top toolbar and status bar render
inside the Atlas host, above and below the work area. The simulation page adds
`.penelopa-simulation` to the same 100% chain; its Atlas host fills the page,
and the top transport/notice strip is rendered in the host's fixed top slot
(see `wwwroot/css/simulation.css`).

## Alignment Semantics

The six directions align against the **union** of the selected items'
bounding boxes:

| AlignType | Reference value | Translation |
|---|---|---|
| Left | `union.MinX` | `dx = ref.MinX - box.MinX` |
| HCenter | `union.CenterX` | `dx = ref.CenterX - box.CenterX` |
| Right | `union.MaxX` | `dx = ref.MaxX - box.MaxX` |
| Top | `union.MaxY` | `dy = ref.MaxY - box.MaxY` |
| VCenter | `union.CenterY` | `dy = ref.CenterY - box.CenterY` |
| Bottom | `union.MinY` | `dy = ref.MinY - box.MinY` |

Top/Bottom use MaxY/MinY because the canvas is y-flipped (screen y grows
down); the visible top edge is the larger world Y. Selection of fewer than
two items returns false without moving.

## Architecture decisions

| ADR | Topic |
| --- | --- |
| [0001](../adr/0001-coordinate-spaces-and-setbounds.md) | Coordinate spaces and the `SetBounds` contract |
| [0002](../adr/0002-history-transactions-and-snapshots.md) | History transactions and snapshot contents |
| [0003](../adr/0003-selection-model.md) | Selection normalization and multi-selection editing |
| [0004](../adr/0004-grouping-deferred.md) | Grouping and ungrouping existing primitives |
| [0005](../adr/0005-simulation-keyframe-timeline.md) | Simulation keyframe timeline, preview isolation and persistence |

## Toolchain Notes

- `global.json` pins SDK 6.0.428. Building with SDK 10 triggers the
  RazorSourceGenerator incremental-input bug for net6.0 WASM, so the version
  is pinned.
- SkiaSharp is pinned to 2.88.9: the 4.x line's
  `SkiaSharp.NativeAssets.WebAssembly.targets` requires net8.0+. The
  `SkiaSharp.NativeAssets.Linux` package is referenced explicitly because
  2.88.9 only pulls Win32/macOS native assets transitively; without it the
  Linux CI tests fail with `DllNotFoundException: libSkiaSharp`.
- The `wasm-tools` workload is required to link the SkiaSharp native assets
  into the WASM build; CI installs it before restore.
- `NuGet.Config` maps every package to nuget.org
  (`packageSourceMapping`) so central package management (CPM) does not trip
  NU1507 on GitHub Actions runners, which add a default `library-packs`
  source.
- Node is optional for a plain build: `BuildTypeScript` only runs when
  `node_modules/typescript` exists, otherwise the committed
  `wwwroot/js/penelopa.js` is used. Node/npm is required to modify the TS
  pointer layer and to run the JS and Playwright suites; CI always restores
  `npm ci` first, compiles TS, and fails if the committed JS drifts.
- NuGet uses `packages.lock.json` in every project. CI restores with
  `--locked-mode`; local restore without the flag may update the lock files.
- Bootstrap and the Open Iconic font package were removed in Phase C. The
  remaining form/panel styles live in `wwwroot/css/app.css`; the application
  ships only Atlas.Blazor CSS, the app stylesheet and the editor assets.
- Tests: xUnit covers Core/Rendering, bUnit (`tests/Penelopa.Web.Tests`) covers
  panel state, ARIA wiring and persistence UI, and Playwright (`tests/e2e`)
  drives the real editor in Chromium at desktop and narrow viewports. CI runs
  JS/Node tests, the generated JS drift check, coverage gates (Core ≥90%,
  Rendering ≥85%) plus per-file ≥90% gates for `Simulation/` and
  `DocumentSerializer`, the Chromium E2E job, and dependency audits
  (`npm audit` for both package roots and
  `dotnet list package --vulnerable --include-transitive`). The NuGet audit
  fails on high/critical findings. A moderate AngleSharp mXSS advisory remains
  in bUnit's test-only DOM parser because the fixed AngleSharp 1.5 line is
  API-incompatible with bUnit 1.28/1.40; this is an accepted, test-only risk
  with no production exposure and is tracked for the S-01 test-stack migration.
  The high `Microsoft.Extensions.Caching.Memory` finding was fixed by pinning
  6.0.2.
- GitHub Pages deployment remains `workflow_dispatch` only, so publishing is
  an explicit release action and cannot silently ship every `main` commit.
  The published copy rewrites `<base href>` to `/Penelopa/` and emits a
  `404.html` copy of the entry page, so project-site deep links such as
  `/Penelopa/simulation` boot the Blazor router instead of a Pages 404.
- With on-demand rendering, the periodic `PERF=1` Playwright benchmark
  measures ~0.03% idle main-thread CPU for an empty document and ~0.02% for
  500 rectangles (headless Chromium, Debug dev server); the former continuous
  loop was ~28% idle CPU in the original audit. The benchmark lives in
  `tests/e2e/tests/perf.spec.ts` and `tests/e2e/tests/simulation-perf.spec.ts`
  and is skipped unless `PERF=1`.
