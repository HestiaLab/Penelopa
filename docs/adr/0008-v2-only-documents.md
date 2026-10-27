# ADR 0008 — v2-only document loading

- Status: Accepted — implemented
- Date: 2026-10-27
- Supersedes: the v1-readable conclusions in [ADR 0005](0005-simulation-keyframe-timeline.md) and [ADR 0007](0007-document-file-import-export.md)
- Related: [Simulation animation requirements](../requirements/simulation.md)

## Context

`DocumentSerializer` historically accepted versionless and version 1 documents
as primitive-only scenes. Version 2 added the optional `simulation` section.
Keeping the legacy read path meant:

- two interpretations of the same file name and extension,
- a migration surface that no current writer produces,
- ambiguous messaging when a v1 file silently loses its simulation data,
- test fixtures that had to exercise both formats for every structural rule.

The application, the bundled sample, and Export all write version 2. The
product decision is to stop reading older documents rather than maintain a
second parser.

## Decision

1. **Every read path accepts only version 2.** Import, Load (including
   localStorage), and the bundled sample all run the same strict
   `DocumentSerializer.Deserialize` check. Versionless (0), v1, negative, and
   v3+ documents are rejected. The rejection reports the document's version so
   the Load and Import boundaries can show a specific reason.
2. **Save and Export always write version 2.** No reader accepts any other
   version, so writing anything else would create an unreadable file.
3. **No automatic migration.** The main application has no v1 parser,
   migration path, XML/XSD adapter, or automatic rewrite. A v1 user must export
   their document with an older release before upgrading.
4. **Legacy localStorage is not touched by Load.** A rejected v1
   `penelopa.document` entry is left in place; the current in-memory document,
   selection, simulation configuration, and undo histories are unchanged, and
   no Load confirmation appears.
5. **The first Save preserves the legacy payload in the backup slot.** Saving
   after an upgrade copies the existing (possibly v1) localStorage value to
   `penelopa.document.backup` and writes the new v2 payload to
   `penelopa.document`. There is only one backup slot, so a later successful
   save may overwrite it.
6. **The v1 rejection is explicit and testable.** Load and Import use
   context-specific errors that name version 1 and point at the migration
   path; malformed JSON and structural errors keep the generic invalid-document
   message.

## Consequences

- **Breaking change.** Existing v1 localStorage documents can no longer be
  loaded. Users must export them with an older release before upgrading.
- The legacy payload is not deleted automatically. It remains readable
  manually and is copied to the single backup slot on the first save.
- All document fixtures, E2E presets, and documentation now use version 2.
  Only the rejection-matrix tests keep v1/versionless inputs.
- `DocumentSerializer.ParseSimulation` no longer needs a version branch, and
  the serializer has a single interpretation of "document".
- A future migration tool, if needed, must be a separate offline deliverable;
  it is intentionally not part of this application.

## References

- `src/Penelopa.Core/Services/DocumentSerializer.cs`
- `src/Penelopa.Web/Services/DocumentStorage.cs`
- `src/Penelopa.Web/Services/DocumentFileService.cs`
- `src/Penelopa.Web/Components/Panels/ToolPanel.razor`
- `src/Penelopa.Web/Components/Panels/CanvasPanel.razor`
