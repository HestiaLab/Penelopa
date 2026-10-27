# ADR 0007 — Document file import/export and the bundled sample

- Status: Accepted — implemented; document-version policy updated by [ADR 0008](0008-v2-only-documents.md)
- Date: 2026-10-26
- Related: [Document format v2](../requirements/simulation.md), [ADR 0005](0005-simulation-keyframe-timeline.md)

> The v1-readable statements in this ADR are superseded: Import, Load, and the
> sample accept only version 2 documents. See
> [ADR 0008](0008-v2-only-documents.md).

## Context

Documents and simulation tracks are persisted only through the browser's
localStorage (`penelopa.document`). Users cannot share a scene as a file,
back it up outside the browser, or hand it to another person, and a fresh
visitor cannot see the simulation without building a scene from scratch.

The document format already has a strict v2 JSON serializer with version,
byte, node, depth and domain validation (`DocumentSerializer`). Adding XML
would introduce a second format, a second parser, a second validation path
and a new XXE surface without a current XML producer.

## Decision

1. **JSON v2 is the only file format.** Import accepts only the JSON produced
   by `DocumentSerializer` for version 2, with a `.json` or `.penelopa.json`
   extension. Versionless, v1, negative, and future versions are rejected with
   an explicit version error (ADR 0008). Export always writes the current v2
   format. There is no XML/XSD adapter in this implementation.
2. **File I/O lives in the editor's Document section.** `Save`/`Load` keep
   their existing localStorage meaning. `Import file…` and `Export file…`
   operate on files only; neither writes localStorage.
3. **Import replaces, after parsing and confirmation.** A single file is read
   (4MB limit, strict UTF-8 with an optional BOM), parsed with the existing v2
   validator, and summarized with primitive/track counts and warnings. The user
   confirms before the document, selection and simulation configuration are
   replaced. Invalid files leave all current state untouched. A pending
   confirmation is canceled if the document or simulation configuration changes.
4. **Export uses the Save payload.** It serializes the current in-memory
   document, selection, global loop and active tracks. Dormant tracks are
   excluded with the same warning used by Save. The browser downloads a Blob;
   there is no server round-trip and no automatic Save.
5. **The public sample is a first-class v2 file.** `wwwroot/samples/loop-study-01.penelopa.json`
   is a checked-in document with the sample scene and animation. The empty
   editor offers `Load Loop Study 01`; loading it stays in the editor and
   selects `Step Indicator`, so opening `/simulation` immediately shows its
   two tracks. The sample never autoplays.
6. **Read-only presentation hides debug axes.** The simulation renderer skips
   the screen-space coordinate axes; the editor keeps them for orientation.
7. **Workspace drag/drop reuses the same input pipeline.** A file dropped
   anywhere over the editor displays a drop overlay and is forwarded to the
   existing hidden `InputFile`; if necessary the editor activates the Tools
   tab first. Non-file drags are ignored, multiple files use the existing
   single-file rejection, and no URL or directory drop is supported. A drop
   while another document operation is running, or while the import control is
   unavailable, is rejected with a workspace-level alert instead of being
   queued. The visible `Import file…` control remains the keyboard/accessibility
   path, and its hidden input is reset after each read so the same file can be
   selected again.
8. **One-level import rollback.** A successful import captures the previous
   document, selection, simulation configuration, Loop and both undo/redo
   stacks. `Undo import` restores all of them without creating a document or
   configuration history entry; a redo of the import itself is not offered.
   The snapshot is invalidated by a persistent document/configuration change,
   a successful save, another import/Load, or a page reload. Selection,
   viewport, Export and playback do not invalidate it. The command is exposed
   only in the editor's Document section.
9. **Preview restore is transient.** Leaving a playback/preview session emits
   a `Transient` document notification. Editors refresh their values, but
   content-snapshot consumers (including import rollback) do not treat it as a
   user edit.

## Consequences

- Documents are v2-only: v1 and versionless files are rejected with an
  explicit error, and Save/Export always write v2. Users with v1 data must
  export it with an older release before upgrading; the first save keeps the
  old localStorage payload in `penelopa.document.backup` (one slot).
- File failures are all-or-nothing at the parse boundary; a successful import
  still clears the current history stacks, and `Undo import` restores the
  captured ones.
- The rollback snapshot keeps the previous document/history in memory until it
  is invalidated; there is no persistence and no multi-level undo.
- The sample is an exportable, inspectable JSON file rather than a parallel
  demo format; changing the scene requires regenerating the sample from the
  editor.
- Drag/drop adds a browser gesture but no second parser or confirmation flow.

## References

- `src/Penelopa.Web/Services/DocumentFileService.cs`
- `src/Penelopa.Web/Services/DocumentImportRollbackService.cs`
- `src/Penelopa.Web/Services/DocumentStorage.cs`
- `src/Penelopa.Web/Components/Panels/ToolPanel.razor`
- `src/Penelopa.Web/Components/Panels/CanvasPanel.razor`
- `src/Penelopa.Web/Components/PenelopaWorkspace.razor`
- `src/Penelopa.Web/ts/penelopa.ts`
- `src/Penelopa.Web/wwwroot/samples/loop-study-01.penelopa.json`
- `src/Penelopa.Core/Services/DocumentSerializer.cs`
- `src/Penelopa.Core/Services/DocumentHistory.cs`
- `src/Penelopa.Core/Simulation/SimulationConfigStore.cs`
