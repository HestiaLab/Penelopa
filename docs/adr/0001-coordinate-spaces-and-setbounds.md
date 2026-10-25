# ADR 0001 — Coordinate spaces and the `SetBounds` contract

- Status: Accepted
- Date: 2026-10-25

## Context

Primitives live in a mutable tree of containers. Each container applies an
affine `LocalTransform` (offset, rotation, flip) to its children. Before this
decision, `SetBoundsLocal` implementations mixed local, parent-local and world
boxes: circles returned their world box and triangles called
`GetWorldBoundingBox()` from inside the "local" method. Resizing a child of an
offset/rotated container therefore corrupted the geometry and ESC could not
restore it.

## Decision

1. `Primitive.SetBounds(worldBounds, worldAnchor)` is the only world-aware
   entry point. It converts the world box/anchor to the **parent's local
   space exactly once**, calls `SetBoundsLocal`, and converts the result back
   to world exactly once.
2. `SetBoundsLocal(bounds, anchor)` receives and returns **parent-local**
   boxes only. It must not read world state (`GetWorldBoundingBox`,
   world positions, or ancestor-aware helpers).
3. `Translate(deltaX, deltaY)` takes a world delta and converts it through
   `Container.ToLocalVector` before updating local properties.
4. `WorldToLocalBounds` walks the ancestor chain via
   `Container.LocalTransform.Invert()`; `ToWorldBounds` walks it forward.
5. A world AABB cannot be represented by local-axis scaling when the
   primitive or an ancestor has rotation/shear. `CanResizeInWorld` is false
   there; the selection overlay hides handles instead of drawing a box that
   does not follow the pointer. Programmatic calls keep the uniform local
   scale semantics.

## Consequences

- Axis-aligned containers (offset/flip) resize exactly; circles keep their
  aspect and fixed corner; triangles map vertices by normalized local
  position.
- Rotated/sheared containers expose no resize handles. This is an explicit
  product constraint, not a silent approximation.
- Gesture cancellation restores the transaction snapshot; it never recomputes
  bounds, so every primitive type is restored exactly.
- Regression coverage lives in `SetBoundsCoordinateSpaceTests`,
  `ContainerTests` and the container-resize E2E case.
