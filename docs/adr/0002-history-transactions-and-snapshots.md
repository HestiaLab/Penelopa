# ADR 0002 — History transactions and snapshot contents

- Status: Accepted
- Date: 2026-10-25

## Context

Earlier history required every caller to remember `Capture()` before a
mutation. Alignment and property edits did not capture, so `Ctrl+Z` deleted
the last added primitive instead of undoing the edit. Gesture cancellation
recomputed bounds, which could not exactly restore every shape.

## Decision

1. `PrimitiveService` owns the only history. Every document mutation goes
   through `BeginTransaction()` → mutate → `CommitTransaction()` or
   `CancelTransaction()`.
2. Structural changes (`Add`, `RemoveRange`, `AddToContainer`), alignment,
   property edits (single and multi-owner) and keyboard nudges use the same
   boundary. Drag/resize gestures open the transaction lazily on their first
   real mutation and commit once on pointer-up.
3. A transaction produces **at most one** undo entry. A no-op (already
   aligned, unchanged value, zero movement) discards the pending snapshot.
4. `DocumentSnapshot` captures primitives **by reference**, plus every
   property value and every container's child list. It does **not** capture
   the selection: selection is UI state.
5. `CancelTransaction` applies the same pre-transaction snapshot as undo.
   Gesture cancellation therefore restores properties and children exactly.
6. Undo/redo re-applies the live selection after restoring the tree, keeping
   only primitives that still exist. Loading a document clears history.
7. Transactions are nestable; nested commits share the outer snapshot.

## Consequences

- Property edits, alignment, delete, rename and multi-selection edits are all
  undoable with exactly one entry.
- Inputs can call `TrySetProperty` without knowing about history. A rejected
  multi-owner edit restores every already-written value and leaves no entry.
- Because selection is not history, undoing a delete does not automatically
  re-select what was restored; the panels re-derive from the live selection.
- Snapshot capture is exception-safe: an unsupported property value aborts the
  capture without corrupting the transaction depth.
- Applying a snapshot detaches nodes from current parents that are not their
  target parent, and temporarily clears child ownership before reattaching.
  This lets undo/redo move nodes into or out of containers that only exist in
  one side of the history (for example a newly created group).

## References

- `tests/Penelopa.Core.Tests/TransactionHistoryTests.cs`
- `tests/Penelopa.Core.Tests/DocumentHistoryTests.cs`
- `tests/Penelopa.Core.Tests/ServiceMutationTests.cs`
