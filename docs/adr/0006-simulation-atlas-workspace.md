# ADR 0006 — Simulation page as an Atlas dockable workspace

- Status: Accepted — implemented
- Date: 2026-10-26
- Requirements: [Simulation animation v1](../requirements/simulation.md)

## Context

The editor has used the Atlas.Blazor 0.3.0 dockable workspace since the
workspace extraction; its side panels, canvas document, splitters and chrome
all come from Atlas. The simulation page was implemented separately as a
hand-built fixed three-column grid with a transport bar, page-level notices and
a full-width bottom timeline. That kept the simulation requirements simple but
left two different shell implementations and made the simulation regions
impossible to resize or rearrange.

The requirement originally fixed the simulation layout (SIM-F-013). The
product decision is to keep the same default information architecture while
allowing users to adjust it, provided that no required region can be lost.
Atlas.Blazor is already a transitive dependency of the web project, so this
does not add a package or upgrade .NET/SkiaSharp.

Atlas 0.3.0 has several constraints that shape the design:

- a tool group must own a unique logical `ToolbarRegion`, even when the
  application hides the Atlas toolbar;
- document groups cannot be collapsed;
- a splitter and its expand chevron become `visibility: hidden` when either
  child is collapsed;
- the host exposes no layout persistence or `ResetWorkspace` parameter;
- the default keyboard map includes a close-document shortcut.

## Decision

1. **Dedicated route and workspace.** `/simulation` keeps its own route,
   selection and page state. It creates a `penelopa-simulation` Atlas
   workspace in `OnInitialized` through `IAtlasWorkspaceFactory`, hosts it with
   `WorkspaceOwnership.External`, `EnableInteractions=true` and disposes it
   when the page is disposed. The editor and simulation workspaces remain
   isolated.
2. **Default layout.** The page uses a top chrome slot and a root vertical
   split: a work area above and a full-width timeline below. The work area is
   a horizontal split of a fixed 220px primitives tool group, a flexible
   canvas document and a fixed 320px inspector tool group. The timeline track
   is fixed at 208px. Atlas constraints apply to splitter interaction; initial
   fixed tracks are not clamped by 0.3.0.
3. **Required regions are protected.** The tree, inspector and timeline are
   tool items with only `CanMove`; the canvas is a document item with only
   `CanMove`. None can be closed, auto-hidden, or removed. The canvas document
   cannot be collapsed (an Atlas invariant); the three tool groups can be
   collapsed, moved and recombined, and must remain reachable in every layout.
   Re-entering the page rebuilds the default layout; layout is not persisted.
4. **Collapse recovery without an Atlas toolbar.** The page keeps Atlas
   toolbars collapsed to avoid a second chrome row. Tool groups therefore own
   logical regions for Atlas validation, but the regions are not presented as
   toolbar entries. Splitter collapse is enabled for the tree, inspector and
   timeline. Because Atlas 0.3.0 hides the whole splitter—including its expand
   chevron—when a child is collapsed, the simulation stylesheet keeps that
   chevron visible and positions splitter buttons at the track centre. This is
   a scoped compatibility shim, not a general Atlas change.
5. **Chrome.** The fixed `TopToolbarContent` hosts the existing navigation,
   transport and the page-level notice strip (errors, validation, save
   confirmation, reduced-motion and dormant details). No Atlas status bar is
   used. The heading remains the `FocusOnNavigate` target.
6. **Keyboard.** The page filters the Atlas default command map to remove
   `CloseActiveDocument` (Ctrl+W/F4) because the canvas cannot be closed. The
   focus-region and tab-navigation shortcuts remain. Simulation configuration
   undo/redo (Ctrl+Z/Ctrl+Y) is unchanged.
7. **Playback and layout are independent.** Layout moves, collapses and
   splitter resizes are allowed while `Playing`; they must not stop playback,
   change the current time or lose the session's base-value snapshot. The
   runtime stays page-owned while Atlas content remounts.
8. **Reduced-motion rendering is isolated.** `prefers-reduced-motion` is read
   by a small child component rather than the page. This avoids a page render
   after navigation that would replace the focused heading in the Atlas top
   slot.
9. **Verification.** Workspace shape, capabilities, regions and toolbar state
   are covered by bUnit definition tests. Playwright covers the default
   regions, absence of close entry points, keyboard splitter resize,
   collapse/expand recovery, layout changes during playback, the existing
   simulation flows, focus and the 1024px overflow gate. The performance
   harness is rerun after the migration.

## Rejected alternatives

- **Keep the hand-built fixed grid.** Rejected because it duplicates Atlas
  chrome, prevents resizing/rearranging and diverges from the editor shell.
- **Open simulation as a tab inside the editor workspace.** Rejected because
  the dedicated route, local selection, session-exit semantics and
  editor/simulation isolation would need a new navigation model.
- **Use Atlas in a fixed, interaction-disabled shell.** Rejected because it
  adds Atlas initialization and DOM without providing the requested
  dockability.
- **Allow closing or auto-hiding required panels without a recovery path.**
  Rejected: Atlas 0.3.0 has no reopen or reset UI, so a required panel could
  become unreachable.
- **Persist the layout in localStorage.** Rejected for this iteration because
  the 0.3.0 host has no persistence parameter; versioning, invalid-layout
  handling and a reset entry point would be additional scope. The layout
  resets on entry by design.
- **Show Atlas toolbars and/or a status bar.** Rejected because the custom top
  strip already owns transport and notices; the extra chrome would duplicate
  entries and consume canvas space.
- **Move the chrome above the Atlas host.** Kept in `TopToolbarContent` to
  follow the accepted chrome decision and let Atlas own the work-area
  geometry.
- **Upgrade Atlas or patch it in this repository.** Out of scope; the page uses
  the pinned 0.3.0 package. The splitter-chevron visibility shim is isolated in
  `simulation.css` and should be revisited on an Atlas upgrade.

## Consequences

- The simulation DOM, height chain and outer CSS move under Atlas; the inner
  tree, inspector, canvas and timeline components keep their class names and
  behaviour, so existing simulation tests and E2E selectors remain valid.
- The page no longer has a single custom grid to maintain; layout changes are
  expressed in `SimulationWorkspaceDefinition`.
- The canvas document cannot be collapsed. Tree, inspector and timeline can
  be collapsed and restored; their expand chevrons remain visible after
  collapse through the scoped CSS shim.
- Layout is intentionally per-entry; users cannot preserve a custom
  arrangement across visits in this iteration.
- Fixed 220/320/208 defaults preserve the accepted 1440 and 1024 experience;
  narrower viewports may squeeze the canvas rather than promising the full
  editing layout (SIM-NF-020).
- Atlas keyboard focus and splitter interactions become part of the
  simulation accessibility surface; the close shortcut is deliberately
  removed.
- The reduced-motion hint now updates independently of the page, preventing a
  post-navigation render from disturbing heading focus.
- Coverage, formatting, full E2E and the 100-track performance benchmark must
  remain green.

## References

- [Simulation animation v1 requirements](../requirements/simulation.md)
- [ADR 0005 — Simulation keyframe timeline and preview isolation](0005-simulation-keyframe-timeline.md)
- [Architecture — Simulation](../en/architecture.md#simulation)
- `src/Penelopa.Web/Components/Simulation/SimulationWorkspaceDefinition.cs`
- `src/Penelopa.Web/Pages/SimulationPage.razor`
- `src/Penelopa.Web/wwwroot/css/simulation.css`
