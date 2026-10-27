# Penelopa

English | [简体中文](README.zh.md)

**A primitive alignment editor and keyframe simulation workspace for Blazor WebAssembly.**

Penelopa pairs a SkiaSharp drawing canvas, primitive tree, six-direction alignment, and a typed property editor inside a six-region dockable workspace. A dedicated `/simulation` page turns Float properties into keyframe tracks for design validation and debugging.

> **Live demo:** https://HestiaLab.github.io/Penelopa/

> **v2-only documents:** Load and Import accept only version 2 `.penelopa.json` documents. Version 1 and versionless files are rejected with a migration hint; export v1 data with an older release before upgrading. The current behavior is specified in the [软件需求规格说明书](docs/软件需求规格说明书.md).

## Preview

![Penelopa editor with the Loop Study 01 sample loaded](docs/assets/images/screenshot.png)

Six-region dockable workspace with a SkiaSharp canvas, primitive tree, alignment tools, and property editor.

![Simulation workspace playing the Loop Study 01 sample](docs/assets/images/simulation.png)

The simulation workspace plays the bundled 9-second Loop Study 01 sample with five keyframe tracks.

## Try the sample

1. Open the live demo.
2. In the empty editor, choose **Load Loop Study 01**.
3. Select **Simulate**, then **Play**.

## Quick start

Requirements: .NET 6 SDK (pinned to 6.0.428) and the `wasm-tools` workload. Node.js is optional for a plain .NET build and is required only for the TypeScript and Playwright test suites.

```sh
dotnet workload install wasm-tools
dotnet restore Penelopa.sln
dotnet build Penelopa.sln --no-restore
dotnet test Penelopa.sln --no-build --no-restore
dotnet run --project src/Penelopa.Web
```

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
