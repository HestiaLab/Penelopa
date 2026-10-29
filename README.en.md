# Penelopa

English | [简体中文](README.md)

**A primitive alignment editor and keyframe simulation workspace for Blazor WebAssembly.**

**Positioning**: Blazor WASM developers and design validators who arrange primitives in the browser, apply six-direction alignment, and verify layout and animation with keyframe simulation; a static site with no server or database.
**Status**: 0.0.0-dev (active development on main), no versioned Release yet (evidence: no tags, empty Releases; `Version` in `Directory.Build.props` is `0.0.0-dev`). The live demo is deployed manually via `deploy-pages.yml` and may lag behind main.

[![CI](https://github.com/HestiaLab/Penelopa/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/HestiaLab/Penelopa/actions/workflows/ci.yml)
[![Deploy to GitHub Pages](https://github.com/HestiaLab/Penelopa/actions/workflows/deploy-pages.yml/badge.svg?branch=main)](https://github.com/HestiaLab/Penelopa/actions/workflows/deploy-pages.yml)
[![License](https://img.shields.io/github/license/HestiaLab/Penelopa)](LICENSE)

[Live demo](https://HestiaLab.github.io/Penelopa/) · [Quick start](#quick-start) · [Documentation](#documentation) · [简体中文](README.md)

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/images/screenshot-dark.png">
  <img src="docs/assets/images/screenshot-light.png" alt="Penelopa six-region workspace with the Loop Study 01 sample loaded: Tools, Primitives, Diagram canvas, Properties, and the status bar" width="1440">
</picture>

Penelopa pairs a SkiaSharp drawing canvas, primitive tree, six-direction alignment, and a typed property editor inside a six-region dockable workspace. A dedicated `/simulation` page turns Float properties into keyframe tracks for design validation and debugging.
The bundled Loop Study 01 sample (five tracks, 14 keyframes, 9 seconds) loads and plays in one click. The workspace is built on Atlas.Blazor 0.3.0 and uses Astrea.Components for the primitive tree.

> **v2-only documents:** Load and Import accept only version 2 `.penelopa.json` documents. Version 1 and versionless files are rejected with a migration hint; export v1 data with an older release before upgrading. The current behavior is specified in the [软件需求规格说明书](docs/软件需求规格说明书.md).

## Preview

![Simulation workspace playing the Loop Study 01 sample](docs/assets/images/simulation.png)

The simulation workspace plays the bundled 9-second Loop Study 01 sample with five keyframe tracks.

## Try the sample

1. Open the live demo.
2. In the empty editor, choose **Load Loop Study 01**.
3. Select **Simulate**, then **Play**.

> **Load Loop Study 01** only appears on an empty canvas. If the browser already stores a document, clear the site data first; the sample ships at `src/Penelopa.Web/wwwroot/samples/loop-study-01.penelopa.json`.

## Quick start

Requirements: .NET 6 SDK (`global.json` pins 6.0.428 with `rollForward: latestFeature`, accepting any 6.0.4xx) and the `wasm-tools` workload. Node.js is optional for a plain .NET build and is required only for the TypeScript and Playwright test suites. Note: .NET 6 is out of support; new environments should assess runtime security updates.

```sh
dotnet workload install wasm-tools
dotnet restore Penelopa.sln --locked-mode
dotnet build Penelopa.sln --no-restore
dotnet test Penelopa.sln --no-build --no-restore
dotnet run --project src/Penelopa.Web --no-build --urls http://127.0.0.1:5170
```

Local verification (Windows, .NET 6.0.428): restore is about 607 MB; build completes with 0 warnings and 0 errors; 547 tests pass. The first build takes about 30 seconds excluding restore. The default URL is `https://localhost:7162`; override it with `--urls` (5170 above).

The application is a static WebAssembly site; no server or database is required. Toolchain details, quality gates, and the dependency policy are in the [软件开发计划](docs/软件开发计划.md) and [软件测试规范](docs/软件测试规范.md).

## Documentation

| Document | Content |
| --- | --- |
| [软件开发计划](docs/软件开发计划.md) | Process, milestones, quality gates, configuration management, and risks |
| [软件需求规格说明书](docs/软件需求规格说明书.md) | Product requirements, acceptance criteria, limits, and non-goals |
| [软件概要设计](docs/软件概要设计.md) | Architecture, modules, data flows, and key approaches |
| [软件详细设计](docs/软件详细设计.md) | Contracts, algorithms, document format, state machines, and file mapping |
| [软件测试规范](docs/软件测试规范.md) | Test levels, environments, coverage and performance gates, defect severity |
| [软件测试报告](docs/软件测试报告.md) | Frozen results, coverage, performance, and requirement traceability at `48cdf6d` |

## License

Penelopa is released under the [MIT License](LICENSE).
