# Penelopa

[English](README.md) | 简体中文

**面向 Blazor WebAssembly 的图元对齐编辑器。**

Penelopa 在六区域可停靠工作区中承载 SkiaSharp 绘制画布、图元树、对齐操作和属性编辑器，并为圆形、矩形、三角形图元提供六方向对齐（左 / 水平居中 / 右 / 上 / 垂直居中 / 下）。

> **在线演示：** https://HestiaLab.github.io/Penelopa/

## 界面预览

![Penelopa 编辑器（亮色模式）](docs/screenshot.png)

六区域可停靠工作区：SkiaSharp 绘制画布、图元树、对齐工具与属性编辑器。

## 功能

- 六区域可停靠工作区，含 Atlas 顶栏/状态栏外壳（两者是有意保持的静态占位）
- 响应式侧边停靠区；1440/1280/1024/900/800/700 视口下画布宽度约
  731/643/504/436/381/327 CSS px。Atlas 0.3.0 的 160–300px 分割约束仅用于
  splitter 拖拽，初始比例轨道不会被钳位
- SkiaSharp 画布与颜色键命中检测，Ctrl+滚轮缩放、空白拖拽平移、Fit 适配内容
- 圆形 / 矩形 / 三角形图元、容器与 hit-through 钻取选择
- 基于整体包围盒的六方向对齐，支持幂等检查
- 属性编辑器：领域校验、单选/多选编辑（共享值、冲突占位、原子写入）、
  重命名、删除，以及按事务撤销/重做
- 对根级图元分组/取消分组：使用 identity 容器、单事务撤销；非 identity
  容器的取消分组有明确的禁用与拒绝路径
- 画布键盘路径：方向键移动 1 世界单位（Shift 为 10），Delete 删除，
  Escape 取消手势
- 属性标签、内联错误、树选中状态和 polite 状态实时区域；允许浏览器页面缩放
- 由 `--penelopa-*` CSS 变量驱动的明亮工作室主题（亮/暗双模式）

## 项目结构

| 项目                       | 职责                              |
| ------------------------ | ------------------------------- |
| `src/Penelopa`           | Blazor WebAssembly 应用（承载可停靠工作区） |
| `src/Penelopa.Core`      | 无 UI 依赖的图元模型、历史与对齐算法            |
| `src/Penelopa.Rendering` | SkiaSharp 画布渲染与命中检测             |
| `tests/`                 | xUnit、bUnit（UI）与 Playwright（E2E）测试 |
| `docs/adr`               | 坐标空间、历史事务、选择模型与分组 ADR          |

`Penelopa.Core` 不依赖 Blazor 或 SkiaSharp。画布上的浏览器输入通过颜色键缓冲区命中检测转换，经共享图元服务提交，再由 Blazor 投影回各面板。

## 开发

环境要求：

- .NET 6 SDK（`global.json` 固定 6.0.428）
- `wasm-tools` workload——SkiaSharp WebAssembly 原生资源需要：

```sh
dotnet workload install wasm-tools
```

- SkiaSharp 固定 **2.88.9**（最后一个支持 net6.0 WebAssembly 的稳定版本线；4.x 要求 net8.0+）。
- Node.js/npm 对普通 .NET 构建是**可选**的：提交的
  `src/Penelopa/wwwroot/js/penelopa.js` 会在本机没有
  `node_modules/typescript` 时直接使用。修改 `ts/penelopa.ts`、运行
  JS/TS 与 Playwright 测试时需要 Node。若希望本地构建重新编译
  TypeScript，先执行 `npm ci`（CI 总是如此）。

```sh
dotnet restore Penelopa.sln
dotnet build Penelopa.sln --no-restore
dotnet test Penelopa.sln --no-build --no-restore
dotnet format Penelopa.sln --verify-no-changes --no-restore
```

运行应用：

```sh
dotnet run --project src/Penelopa
```

TypeScript 门禁与 JS 测试：

```sh
npm ci
npm run check:js
npm run test:js
```

E2E 测试需要 Node 与 Playwright：

```sh
npm ci
npm ci --prefix tests/e2e
npx --prefix tests/e2e playwright install chromium
npm run test:e2e
```

Windows 上可设置 `PLAYWRIGHT_CHANNEL=chrome` 复用系统 Chrome。

覆盖率（Core ≥90%、Rendering ≥85% 行）：

```sh
dotnet test Penelopa.sln --collect:"XPlat Code Coverage" --results-directory artifacts/coverage
python scripts/check-coverage.py
```

NuGet 使用已提交的 `packages.lock.json`；CI 以
`dotnet restore Penelopa.sln --locked-mode` 恢复，并审计两个 npm 根目录与
NuGet 依赖的已知漏洞。本地普通 `dotnet restore` 可能更新锁文件。

依赖审计策略：High/Critical 使 CI 失败。bUnit 仅用于测试的 DOM 解析器仍带有
中危 AngleSharp mXSS 公告（AngleSharp 1.5 修复，但与 bUnit 1.28/1.40 的 API
不兼容）。它无生产暴露，记录为 S-01 测试栈迁移的前置项；审计未被关闭，也不将
风险报告为零。

## 文档

| 文档                                                     | 内容                                  |
| ------------------------------------------------------ | ----------------------------------- |
| [架构 (zh)](docs/zh/architecture.md)                     | 分层、渲染、对齐语义、可访问性、工具链说明               |
| [架构 (en)](docs/en/architecture.md)                     | English architecture document       |
| [ADR 0001](docs/adr/0001-coordinate-spaces-and-setbounds.md) | 坐标空间与 `SetBounds` 契约               |
| [ADR 0002](docs/adr/0002-history-transactions-and-snapshots.md) | 历史事务与快照内容                       |
| [ADR 0003](docs/adr/0003-selection-model.md)             | 选择规范化与多选编辑                          |
| [ADR 0004](docs/adr/0004-grouping-deferred.md)           | 分组与取消分组现有图元                           |

GitHub Pages 部署工作流保持手动触发（`workflow_dispatch`），发布是显式操作。

## 许可

Penelopa 以 [MIT 许可](LICENSE) 发布。
