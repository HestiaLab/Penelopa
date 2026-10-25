# ADR 0003 — Selection model and multi-selection editing

- Status: Accepted
- Date: 2026-10-25

## Context

The selection used to be a flat `HashSet`. A container and its child could be
selected at the same time, so drag and alignment applied the child's
transform twice (once through itself, once through its parent).

## Decision

1. **Normalize at every entry point.** `SetSelected`, `SetSelectedRange` and
   `AppendSelected` remove any primitive that has a selected ancestor. Tree
   selection, canvas clicks, Ctrl toggles and drill-down all use these
   methods.
2. Drag, alignment, keyboard nudges and multi-selection property edits operate
   on the normalized top-level selection only. A child is never transformed
   directly while its container is selected.
3. Drill-down still starts at the outermost candidate and advances to the
   deepest leaf on repeated clicks at the same spot. Selecting a child alone
   is allowed (no selected ancestor); the resulting selection is a single
   child.
4. Undo/redo does not capture selection. After restoring the tree, the live
   selection is filtered to surviving references.
5. Multi-selection property editing exposes only properties whose **name and
   runtime type** exist on every selected primitive. Equal values are shown
   normally; differing values render as an empty field with the
   `Multiple values` placeholder. Writing a value updates every owner
   atomically in one transaction.
6. The property panel title is the primitive name for a single selection and
   `N primitives selected` otherwise.

## Consequences

- Parent+child double transforms are impossible through the supported UI
  paths.
- Mixed selections (for example a container and a circle) only show `Name`,
  not shape-specific geometry or container transform properties.
- Clearing a conflicted field without typing does not write an empty value.
- Coverage lives in `SelectionNormalizationTests` and the multi-selection
  bUnit/E2E tests.
