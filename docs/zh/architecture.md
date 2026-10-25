# Penelopa 架构

## 概述

Penelopa 是一个消费 Atlas.Blazor 0.3.0 NuGet 包的 Blazor WebAssembly 图元对齐编辑器，使用其可停靠工作区外壳。编辑器逻辑分层设计，每个关注点都可以独立测试。

## 分层

```text
src/Penelopa（WASM 应用）
  ├── Components/PenelopaWorkspace.razor   Atlas 六区域可停靠布局
  ├── Components/PenelopaTopToolbar.razor  通过 Atlas 顶栏插槽承载
  ├── Components/PenelopaStatusBar.razor   通过 Atlas 状态栏插槽承载
  ├── Components/Panels/                   Tools / PrimitiveTree / Canvas / Properties
  └── wwwroot/css/app.css                  编辑器外壳样式 + 明亮工作室主题
src/Penelopa.Rendering                     SkiaSharp 画布 + 颜色键命中检测
src/Penelopa.Core                          图元模型 + 对齐算法
```

### Penelopa.Core

纯领域逻辑，不依赖 UI 或 SkiaSharp：

- `Primitives/` — `Primitive` 基类与 `Circle` / `Rectangle` / `Triangle` 子类，各自暴露可编辑的 `PropValue` 属性包（`Float`、`Double`、`Int`、`Uint`、`Bool`、`String`）。
- `Primitives/ColorKeyManager` — 为每个图元映射唯一的打包 uint 颜色键，用于命中检测。键从 `0xFF000001` 开始，绕过 `0xFF000000`（黑色永远不是有效键）。
- `Alignment/` — `AlignType` 枚举（Left/HCenter/Right/Top/VCenter/Bottom）、`Box`（轴对齐包围盒）、`Transform` 与 `AlignExtensions.Align`——将选中项移动到其对齐值与整体包围盒匹配。操作幂等：已对齐的选择返回 false 且不移动。
- `Services/IPrimitiveService` — 通过 DI 在各面板间共享的集合 + 选择状态（注册为单例）。

### Penelopa.Rendering

抽取 SkiaSharp 渲染，使命中检测逻辑无需浏览器即可单元测试：

- `CanvasRenderer` 持有一张绘制位图和一张离屏命中位图，尺寸跟随宿主上报的
  渲染目标（`Render` 接收 SKGLView 的 `e.Info` 尺寸，因此画布在任何显示
  DPI 下都填满宿主面板）。两张位图共享同一个 `ViewTransform`（y-flip 世界
  变换并按设备像素比缩放）；`Render` 清空两者，绘制每个图元（可见颜色画到
  绘制位图、颜色键画到命中位图），然后 blit 结果、绘制坐标轴指示，再绘制
  选中覆盖层（包围盒 + 四个角手柄，屏幕固定 8px 大小）。命中位图绘制关闭
  抗锯齿，保证 1px 边缘可命中。
- `ViewTransform` 把世界坐标（模型空间，Y 向上，原点在画布中心）映射到
  视图像素（Y 向下），以物理分辨率渲染：100% 缩放下 1 个世界单位等于
  1 个 CSS 像素，渲染目标尺寸为 CSS 尺寸乘以 `devicePixelRatio`。它还携带
  视图缩放（围绕光标滚轮）与平移（空白拖拽），渲染、选中覆盖层与命中
  检测全部跟随同一变换。颜色键命中位图按物理像素索引，因此任意缩放级别
  下 CSS 指针位置都经过与内容定位相同的路径。
- `HitTest(cssX, cssY)` 通过共享的 `ViewTransform` 换算浏览器 CSS 坐标，
  并把颜色键像素解析回图元。

### Penelopa（WASM 应用）

- `PenelopaWorkspace.razor` 通过程序式 API 承载工作区：构建
  `AtlasWorkspaceDefinition`（`PenelopaWorkspaceDefinition`），把结果
  `IAtlasWorkspace` 传给 `AtlasWorkspaceHost`，并指定
  `WorkspaceOwnership.External`（组件负责释放）。布局为六区域工作区：
  inline-start 停靠区（Tools + Primitives，下方空槽位）、中央文档组
  （Diagram）、inline-end 停靠区（Properties，下方空槽位）、以及含两个
  空槽位的 block-end 停靠区。左右停靠区初始各 300px，侧面板对称；四个
  空组保持折叠，仅保留工具栏槽位。`PenelopaTopToolbar` 与
  `PenelopaStatusBar` 通过 `TopToolbarContent` / `StatusBarContent` 宿主
  插槽提供，四个面板通过 `ContentRoutes` 注册，使用
  `PenelopaContentKinds` 中的种类常量。
- `PenelopaWorkspaceDefinition.cs` 以程序式方式构建布局树
  （`SplitNode` / `GroupNode` / `DockItem` 数组与工具栏状态），复刻声明式
  缺省值：内部停靠区与底部停靠区使用 0.5 比例基准，文档组使用 Scroll
  溢出与 Adjacent 激活，空折叠组为 Persistent 且无选中项。
- `CanvasPanel.razor` 承载 `SKGLView`（`EnableRenderLoop=true`，默认
  `IgnorePixelScaling=false` 物理分辨率渲染）；画布元素填满视口（无固定
  内容尺寸），视图本身由 `ViewTransform` 缩放/平移管理：滚轮围绕光标
  缩放、空白拖拽平移、Fit 按钮（`renderer.FitToContent`）把内容居中到
  视口，因此不再需要滚动条。接线 `OnPaintSurface` → `CanvasRenderer.Render`
  （传事件的 `e.Info` 尺寸与当前 `devicePixelRatio`），以及 `mousedown` →
  `HitTest` → 选择（Ctrl 点击追加）。浏览器 `OffsetX/Y` 相对画布元素，
  视口变化不影响命中检测。`wwwroot/js/penelopa.js`（由 `ts/penelopa.ts` 编译，编译产物随仓库提交以便无 node 环境运行）用 matchMedia 监听
  `devicePixelRatio` 变化，跨 DPI 显示器拖动时保持同步，并承载指针层
  （捕获、画布相对 CSS 坐标、抑制合成鼠标事件、rAF 节流、滚轮缩放），
  以语义回调方式上报交互控制器。

### 容器与钻取

`Container`（Core）组合子图元并施加仿射 `LocalTransform`——A661 风格的
旋转/偏移/翻转容器是同一类型的工厂变体。容器自身无可见形状；子图元在
其变换内渲染（`DrawNode` 在 Save/Restore 下把矩阵同时施加到可见与命中
画布，颜色键命中与所见一致）。子图元持有局部坐标；`Primitive.Translate` /
`SetBounds` / `ContainsWorldPoint` 通过父链（`Container.ToLocalVector` /
`ToLocalPoint` / `ToLocalBounds`）把世界输入换算到局部，因此在旋转容器
内拖动跟随指针、缩放保持固定角锚定。容器世界包围盒是变换后子元素的
AABB，选择框与对齐因此无需改动；轴对齐容器可非等比缩放，旋转容器等比
缩放以避免矩阵剪切。

钻取选择（hit-through）通过 `CanvasRenderer.PickAll` 收集一点处的候选：
颜色键顶层图元及其祖先链，按 根 → 最深叶子 排序（已确认的方向）。
`EditorInteractionController` 每次点击从最外层候选开始，在同一位置重复
点击（4 世界单位与 500ms 内）推进索引，停在最深叶子；移开或点击空白
重置上下文。Ctrl 点击跳过钻取（追加/切换），树面板可直接选择任意祖先，
ESC 取消手势。

### 历史与持久化

`PrimitiveService` 持有 `DocumentHistory`（快照式 undo/redo）。快照按引用
捕获图元树，外加每个图元的属性值、容器子元素与选择；每次结构变更
（`Add`/`Remove`/`AddToContainer`）与每次画布手势前都会拍快照，因此
undo 恢复操作前状态。应用快照会重建根列表、恢复属性值与子元素、
重新注册颜色键、按引用恢复选择。Ctrl+Z / Ctrl+Shift+Z（或 Ctrl+Y）
通过指针层的按键处理驱动 undo/redo。

`DocumentSerializer` 把文档往返为 JSON：图元按类型存储其属性值
（颜色键不序列化——加载时重建），容器嵌套子元素，选择保存为图元 Id
以便加载后按新树重解析。Tools 面板的 Save/Load 按钮把文档持久化到
`localStorage`（`penelopa.document`）。

### 交互

编辑手势由 Core 层的 `EditorInteractionController` 驱动——一个小的状态机
（Idle / Pressed / Dragging / Resizing），接收世界坐标指针位置与分层
`HitTestResult`，在按下时快照选择集与几何，提交时一次性通知各面板。
ESC、pointer-cancel、捕获丢失或窗口失焦恢复快照并回到 Idle。点击多选
集合中已选成员时延迟到抬起才做决定：同一手势要么塌缩选择（单击）要么
拖动整组；Ctrl 点击切换成员资格。缩放保持被拖手柄对角的固定（
`ResizeMath`，带最小尺寸约束与穿越固定角时的镜像翻转），图元通过
`SetBounds(bounds, anchor)` 把自身几何拟合到目标包围盒：圆保持正圆且
固定角在圆上，三角形按归一化位置映射顶点。未来的钻取选择（hit-through）
将通过 `ContainsWorldPoint` 与祖先链收集候选。
- `ToolPanel.razor` 提供 Add（Circle/Rectangle/Triangle）与 Align（六方向）操作。
- `PrimitiveTreePanel.razor` 列出全部图元，点击选择。
- `PropertyPanel.razor` 通过 `Panels/Props/` 中的类型化输入组件渲染选中图元的 `PropValue` 属性包。

## 高度链

Atlas 工作区由其父级确定尺寸；编辑器外壳必须将 `html/body/#app/.penelopa-main/.penelopa-shell/.atlas-v2-workspace` 链到 100% 高度（见 `wwwroot/css/app.css`）。顶栏与状态栏在 Atlas 宿主内部渲染，分别位于工作区上方与下方。

## 对齐语义

六方向对齐基于选中项包围盒的**整体**：

| AlignType | 参考值 | 位移 |
|---|---|---|
| Left | `union.MinX` | `dx = ref.MinX - box.MinX` |
| HCenter | `union.CenterX` | `dx = ref.CenterX - box.CenterX` |
| Right | `union.MaxX` | `dx = ref.MaxX - box.MaxX` |
| Top | `union.MaxY` | `dy = ref.MaxY - box.MaxY` |
| VCenter | `union.CenterY` | `dy = ref.CenterY - box.CenterY` |
| Bottom | `union.MinY` | `dy = ref.MinY - box.MinY` |

Top/Bottom 使用 MaxY/MinY 是因为画布做了 y-flip（屏幕 y 向下增长）；可见顶边是更大的世界 Y。少于两个选中项时返回 false 且不移动。

## 工具链说明

- `global.json` 固定 SDK 6.0.428。使用 SDK 10 构建会触发 net6.0 WASM 的 RazorSourceGenerator 增量输入 bug，因此锁定版本。
- SkiaSharp 固定 2.88.9：4.x 的 `SkiaSharp.NativeAssets.WebAssembly.targets` 要求 net8.0+。显式引用 `SkiaSharp.NativeAssets.Linux` 是因为 2.88.9 只传递拉取 Win32/macOS 原生资源；缺少时 Linux CI 测试会报 `DllNotFoundException: libSkiaSharp`。
- `wasm-tools` workload 用于把 SkiaSharp 原生资源链接进 WASM 构建；CI 在 restore 前安装。
- `NuGet.Config` 通过 `packageSourceMapping` 把所有包映射到 nuget.org，使中央包管理（CPM）不会在 GitHub Actions runner 上触发 NU1507（runner 默认带有 `library-packs` 源）。
