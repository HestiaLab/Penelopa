# Penelopa

[English](README.md) | 简体中文

**面向 Blazor WebAssembly 的图元对齐编辑器。**

Penelopa 在六区域可停靠工作区中承载 SkiaSharp 绘制画布、图元树、对齐操作和属性编辑器，并为圆形、矩形、三角形图元提供六方向对齐（左 / 水平居中 / 右 / 上 / 垂直居中 / 下）。

> **在线演示：** https://HestiaLab.github.io/Penelopa/

## 界面预览

![Penelopa 编辑器（亮色模式）](docs/screenshot.png)

六区域可停靠工作区：SkiaSharp 绘制画布、图元树、对齐工具与属性编辑器。

## 功能

- 六区域可停靠工作区，含顶栏与状态栏
- 对称侧边停靠区（各 300px），中央绘制画布填满整个编辑区
- SkiaSharp 画布与颜色键命中检测（画布随面板自适应尺寸）
- 圆形 / 矩形 / 三角形图元与可编辑属性系统
- 基于整体包围盒的六方向对齐，支持幂等检查
- 由图元属性模型驱动的属性编辑器
- 由 `--penelopa-*` CSS 变量驱动的明亮工作室主题（亮/暗双模式）

## 项目结构

| 项目                       | 职责                              |
| ------------------------ | ------------------------------- |
| `src/Penelopa`           | Blazor WebAssembly 应用（承载可停靠工作区） |
| `src/Penelopa.Core`      | 无 UI 依赖的图元模型与对齐算法               |
| `src/Penelopa.Rendering` | SkiaSharp 画布渲染与命中检测             |
| `tests/`                 | Core 与 Rendering 的 xUnit 测试项目   |

`Penelopa.Core` 不依赖 Blazor 或 SkiaSharp。画布上的浏览器输入通过颜色键缓冲区命中检测转换，经共享图元服务提交，再由 Blazor 投影回各面板。

## 开发

环境要求：

- .NET 6 SDK（`global.json` 固定 6.0.428）
- `wasm-tools` workload——SkiaSharp WebAssembly 原生资源需要：

```sh
dotnet workload install wasm-tools
```

- SkiaSharp 固定 **2.88.9**（最后一个支持 net6.0 WebAssembly 的稳定版本线；4.x 要求 net8.0+）。

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

## 文档

| 文档                                 | 内容               |
| ---------------------------------- | ---------------- |
| [架构 (zh)](docs/zh/architecture.md) | 分层、渲染、对齐语义、工具链说明 |

## 许可

Penelopa 以 [MIT 许可](LICENSE) 发布。
