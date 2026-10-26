# ADR 0005 — Simulation keyframe timeline and preview isolation

- Status: Accepted — implemented
- Date: 2026-10-25 (accepted); 2026-10-26 (implemented)
- Requirements: [Simulation animation v1](../requirements/simulation.md)

## Context

Penelopa is a Blazor WebAssembly primitive editor. The document is a tree of
`Primitive` objects with `PropValue` fields, edited through
`IPrimitiveService`, recorded by `DocumentHistory` transactions, serialized by
`DocumentSerializer` (format version 1) and persisted explicitly to
localStorage. The SkiaSharp canvas is rendered on demand
(`EnableRenderLoop=false`): every document, selection or viewport change calls
`Invalidate`. The application currently has a single `/` route and a minimal,
static top toolbar.

The requested feature is a simulation page where users configure primitive
properties and a timer makes widgets move. During review the primary use case
was clarified as **design validation / debugging**, not runtime event testing.
That changes the scope from a one-shot preview to an inspection tool:

- one property may need an A → B → C trajectory, so a single from/to tween is
  insufficient;
- users must be able to seek to arbitrary times, step by frame and read the
  effective property values;
- the same time must always produce the same values, regardless of frame rate;
- playback must not mutate the saved document or pollute the editor history.

The existing model has no animation layer, no timer and no simulation page, so
this ADR records the model chosen for v1.

## Decision

1. **Dedicated route, read-only canvas.** Add `/simulation` as a separate
   page. It shares the singleton document with the editor but has its own
   selection. The canvas is read-only: no hit testing, drag, resize or
   selection handles. Users select a widget from a read-only tree/list. The
   editor retains a `Simulate` entry in the top toolbar and the simulation page
   provides `Back to editor`.
2. **Widgets are existing primitives.** `Circle`, `Rectangle`, `Triangle` and
   `Container` are the movable objects; no new runtime widget type is
   introduced. Only `FloatPropValue` properties are animatable in v1. Colour,
   integer, Boolean and string properties are out of scope.
3. **The simulation configuration is a separate layer.** Tracks are keyed by
   `(Primitive.Id, property name)` and are not stored in `Primitive.Props`.
   They do not enter the editor's transaction history. They are persisted as
   an optional `simulation` section in a version 2 document.
4. **Global absolute keyframe timeline.** Each `(primitive, Float property)`
   pair has at most one track. A track has one or more keyframes
   `(time, value, interpolation)`. All tracks share the absolute timeline
   `0..T`, where `T` is the last keyframe time across active tracks. Playback
   loops the whole timeline or not at all; per-track loops are not supported.
   Before the first and after the last keyframe the nearest value is held.
5. **Interpolation.** Segments support `linear`, `ease-in`, `ease-out`,
   `ease-in-out`, `step` and `cubic-bezier(x1, y1, x2, y2)`. All Bézier control
   points are restricted to `[0, 1]`, so the interpolation factor never leaves
   `[0, 1]` and valid endpoints cannot produce invalid intermediate geometry
   (for example a negative radius). Ease presets use the CSS control points
   (`0.42, 0, 1, 1`, `0, 0, 0.58, 1`, `0.42, 0, 0.58, 1`).
6. **Property-local semantics.** Keyframe values are raw property values and
   are applied in the parent space, exactly like property-panel edits. A
   translated child of a rotated container follows the container's local axes;
   the UI labels values as relative to the parent. World-space endpoints and
   trajectory previews are deferred.
7. **Preview isolation.** At the start of a session the runtime captures the
   base values of every Float property in the document. During playback it
   writes evaluated values directly to those properties without raising
   document-change events or creating history entries. `Stop`, leaving the
   page, or an error restores every captured value exactly. `Save` is enabled
   only in `Ready`; saving during a preview is forbidden because it would
   serialize transient values.
8. **Playback states and clock.** The page has `Ready`, `Preview`, `Playing`,
   `Paused` and `Finished` states. A single monotonic frame clock targets
   60 FPS and computes elapsed time from timestamps rather than accumulating a
   fixed step. Frame stepping is `1/60 s`; seeking while playing pauses first;
   a hidden tab pauses automatically. Track readouts are throttled to 20 Hz.
   While `Playing`, the page enables the SKGLView render loop so paint is not
   coalesced with the simulation rAF to 30 Hz; all other states return to
   on-demand rendering to keep idle CPU near zero.
9. **Persistence and failure semantics.** Document format 2 contains an
   optional `simulation` section with the loop flag and tracks. Version 1
   documents load with no simulation. Dormant tracks (target primitive
   deleted) are retained in memory for undo, are not saved, and are pruned when
   an external document is loaded. Loading resolves targets first and prunes
   dormant/orphan tracks before structural and value validation; a structurally
   invalid active track still drops the whole simulation section with a warning.
   Geometry and primitive property validation remain strict.
10. **Simulation configuration has its own undo/redo.** A separate snapshot
    stack of up to 50 entries covers track/keyframe edits and drags, is cleared
    on page entry and document load, and never shares entries with
    `DocumentHistory`.
11. **Read-only rendering path.** During playback the renderer can skip the
    colour-key hit buffer and selection overlay. The performance target is
    500 primitives with 100 active tracks at an average of at least 55 FPS in
    the reference Chrome/1440×900 environment, measured from SKGLView paint
    callbacks rather than only from JS-to-.NET frame ticks.
12. **Accessibility.** Playback never starts automatically; transport
    controls are native buttons; state and error changes use
    `aria-live="polite"` while 20 Hz values are not announced. The canvas is
    exposed as an image, and keyframes have a keyboard-editable inspector path
    in addition to timeline dragging. The page heading is focusable and
    focused on navigation, and keyframe markers have an enlarged invisible hit
    area while retaining their visual size. `prefers-reduced-motion` is
    respected by not autoplaying.

## Rejected alternatives

- **Single from → to tween per property.** Rejected after the design-validation
  use case was established: it cannot express an A → B → C trajectory without
  introducing a second overlapping-track mechanism.
- **Per-track independent loops.** Rejected because absolute times, a single
  playhead and a bounded scrubber need one shared timeline. A global loop
  covers recurring playback without ambiguous phases.
- **World-space endpoints.** Rejected because rotation, non-uniform scale and
  flipped containers have no single linear interpretation in world space; it
  would also disagree with the values shown in the property editor.
- **Storing animation in `Primitive.Props` or document history.** Rejected
  because per-frame writes would create history entries and couple playback to
  editing transactions. The configuration is a separate layer; only the
  persisted summary enters the document format.
- **Committing the final pose or applying a preview.** Deferred: `Stop` has a
  single meaning (restore the base document), and saving transient values is
  blocked. An explicit "apply pose" command would need its own transaction and
  history semantics.
- **Automatic keyframing.** Rejected for v1: modifying a value while previewing
  would silently create keys. Keyframes are added explicitly at the playhead.
- **A timeline showing every widget.** Rejected because the document can hold
  hundreds of primitives; the timeline shows only the selected widget's tracks,
  with badges in the tree for animated widgets.
- **Saving from any playback state.** Rejected because it would write
  evaluated values. `Save` is Ready-only and instructs the user to stop first.
- **Colour / integer / Boolean / string animation.** Out of scope for v1.
- **Scripts, events or runtime assertions.** A different use case (runtime
  behaviour testing) and a different product scope.

## Consequences

- The document format moves to version 2. Version 1 documents remain readable;
  older builds that only understand version 1 will reject version 2, which is
  the existing forward-version policy.
- A new UI-independent simulation model/evaluator, a configuration service, a
  simulation page, a render-only path and serializer changes are required.
  The editor top toolbar stops being purely static, so the README,
  architecture notes and UI tests that assert static chrome must be updated.
- During a session, code that reads primitive properties observes temporary
  animated values. `Save` and any future export path must be gated on `Ready`
  or read from the captured base snapshot.
- `T` is derived from the last keyframe; adding a keyframe extends the
  timeline. There is no separate duration field. A timeline with no active
  tracks or `T = 0` has no playable motion.
- Dormant tracks survive editor undo but not document save/load. The Save flow
  must explicitly warn that they will be dropped; the page shows their details
  and supports per-track removal as well as remove-all, both undoable.
- Browser navigation is a session exit: leaving the route stops the runtime
  before awaiting JS teardown and notifies the shared primitive service once,
  so editor panels show the restored base values without a reselection.
- Document arrays containing `null` primitive/child entries are rejected as
  invalid input. The load boundary converts any remaining unexpected exception
  into a load error with a single-line UI summary and keeps the current
  document, while the full exception is logged.
- Evaluation is deterministic for a given `t`; the frame rate affects only
  sampling, not the mapping from time to property values.
- The timeline needs zoom, horizontal scrolling and a keyboard-editable
  inspector to remain usable with long durations and without pointer input.
- Coverage gates apply per simulation source file and to
  `DocumentSerializer.cs`, in addition to assembly-level gates. Performance is
  tracked with tick, actual .NET frame and paint counters; the JS counter API
  is only attached when the PERF harness opts in via `__PENELOPA_PERF__`.
- Coverage and performance must be verified by Core unit tests, bUnit,
  Playwright and the existing performance harness.

## References

- [Simulation animation v1 requirements](../requirements/simulation.md)
- [ADR 0001 — Coordinate spaces and the `SetBounds` contract](0001-coordinate-spaces-and-setbounds.md)
- [ADR 0002 — History transactions and snapshot contents](0002-history-transactions-and-snapshots.md)
- [ADR 0003 — Selection model](0003-selection-model.md)
- `src/Penelopa.Core/Services/DocumentSerializer.cs`
- `src/Penelopa.Core/Services/PrimitiveService.cs`
- `src/Penelopa.Web/Components/PenelopaTopToolbar.razor`
- `src/Penelopa.Web/Components/Panels/CanvasPanel.razor`
- `src/Penelopa.Web/Components/Panels/ToolPanel.razor`
