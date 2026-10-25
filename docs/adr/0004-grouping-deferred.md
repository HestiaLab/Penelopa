# ADR 0004 — Grouping existing primitives

- Status: Accepted — implemented in the Phase C follow-up
- Date: 2026-10-25

## Context

Phase C added delete, rename and multi-selection property editing. Grouping
("put the current top-level selection into a new container while preserving
world positions") was initially deferred. This ADR records the coordinate,
selection and undo model that was implemented so future changes do not invent
a different one.

## Decision

1. **Eligibility.** `GroupSelection` requires at least two selected primitives
   and every selected primitive must be a **document root**
   (`Parent == null`). A drilled child selected alone does not qualify, because
   moving it into a new root container would require rebasing its geometry
   through its current ancestor chain. Validation happens before the
   transaction starts; a rejected command produces no history entry.
2. **Identity container.** Grouping creates an offset container named `Group`
   with `Offset = (0, 0)`, `Rotation = 0`, `ScaleX = ScaleY = 1`. It is
   inserted at the index of the first selected root in the document root list.
   Selected roots are removed from the root list and appended as children in
   their original root order, so z-order and world positions are unchanged.
   The container becomes the selection and is renamed through the existing
   `Name` editor.
3. **One transaction.** All structure and selection changes run inside one
   `BeginTransaction`/`CommitTransaction` pair and emit one
   `Structure | Selection` change notification. The `ReplaceAll` ownership
   model and `AddToContainer` validation continue to enforce a single parent
   and no cycles.
4. **Undo/redo.** Selection is UI state and is not captured (ADR-0002). Undo
   restores the previous root list and children; the grouped container no
   longer exists, so the live selection is filtered out and becomes empty.
   Redo restores the container but leaves the selection empty. Reselecting the
   children would require an explicit selection payload in history; this ADR
   deliberately does not add one.
5. **Ungroup.** `TryUngroup` only accepts a **root** container whose
   `LocalTransform` is identity (offset 0, rotation 0, scale 1; rotations that
   are identity within floating-point tolerance, such as 360°, are accepted).
   Its children are merged into the root list at the container's index in their
   existing order, the container is removed, and the children become the
   selection. This runs in one transaction and is undoable. Nested containers
   and containers with a non-identity transform are rejected with a readable
   error; the Tools panel disables the button and explains the identity
   requirement. Rejecting is intentional: merging children out of a
   non-identity container without baking its transform would change their
   world positions.
6. **Snapshot restoration.** Undo/redo detaches snapshot nodes from parents
   that are not their target parent before reattaching them, and assigns
   document ownership after the root list is installed. This is required for
   containers that exist only in the redo snapshot (such as a newly created
   group), which are not part of the document when their children are
   reattached.

## Consequences

- Grouping a root-level selection preserves world geometry exactly, including
  nested containers; ungrouping an identity container is its inverse.
- A group can contain nested containers and non-identity containers; ungrouping
  the group itself is safe because only the outer identity transform is
  removed.
- Non-identity ungroup is an explicit product limitation, not a silent
  approximation. Baking transforms remains a separate future operation.
- Undoing a group clears the selection instead of reselecting the restored
  children; this is consistent with the history contract in ADR-0002.
- Coverage lives in `GroupingTests`, `ToolPanelTests` and the Playwright
  grouping/ungrouping flow.
