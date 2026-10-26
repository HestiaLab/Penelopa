# Penelopa 架构

## 概述

Penelopa 是一个消费 Atlas.Blazor 0.3.0 NuGet 包的 Blazor WebAssembly 图元对齐编辑器，使用其可停靠工作区外壳。编辑器逻辑分层设计，每个关注点都可以独立测试。

## 分层

```text
src/Penelopa.Web（WASM 应用）
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

- `Primitives/` — `Primitive` 基类与 `Circle` / `Rectangle` / `Triangle` 子类，各自暴露可编辑的 `PropValue` 属性包（`Float`、`Double`、`Int`、`Uint`、`Color`、`Bool`、`String`）。`PropValue.TrySetText` 在领域层校验（有限数值、正尺寸、颜色文本 `#RRGGBB` / `#RRGGBBAA`、去空白非空且不超过 128 字符的名称）；未知 `PropValue` 子类型在历史/序列化中抛出 `NotSupportedException`，不再静默丢弃。
- `Alignment/` — `AlignType` 枚举（Left/HCenter/Right/Top/VCenter/Bottom）、`Box`（轴对齐包围盒）、`Transform` 与 `AlignExtensions.Align`——将选中项移动到其对齐值与整体包围盒匹配。操作幂等：已对齐的选择返回 false 且不移动。
- `Services/IPrimitiveService` — 文档与选区的唯一状态源，通过 DI 在各面板间共享（单例）。`GetAll()` / `GetSelection()` 返回只读快照，`GetState()` 返回不可变 `DocumentState`。结构操作校验容器属于本文档，拒绝自身/重复父级/环路；内部维护文档归属，阻止同一图元挂到两个文档。`GroupSelection` / `TryUngroup` 实现 ADR-0004 的根级分组规则。
- `Services/DocumentHistory` — 快照式事务。快照按引用保存图元树、属性值与容器子元素；选择不进入快照（见 ADR-0002）。每次修改最多生成一个撤销条目，取消手势与撤销共用同一快照。
- `Simulation/` — 不可变关键帧配置（`SimulationConfig` / `SimulationTrack` / `SimulationKeyframe`）、结构与领域校验、确定性求值器（线性、缓动预设、阶跃与 `[0,1]` 控制点的 cubic-bezier）、全局时间轴、预览隔离的 `SimulationRuntime`、单例 `SimulationConfigStore`（50 步配置历史与 Dormant 解析）以及采样式 `SimulationBoundsEstimator`。不依赖 Blazor、SkiaSharp 或 JS。

### Penelopa.Rendering

抽取 SkiaSharp 渲染，使命中检测逻辑无需浏览器即可单元测试：

- `CanvasRenderer` 是编排器：持有绘制位图与两张画布，协调
  `ViewportController`、`ScenePainter`、`HitTestBuffer` 与 `SelectionOverlay`。
  `Render` 接收 SKGLView 的 `e.Info` 尺寸，清空表面，绘制场景（可见颜色画到
  绘制位图、颜色键画到命中位图），blit 绘制位图并叠加选择覆盖层。它实现
  `IDisposable`。
- `ViewportController` 持有共享的 `ViewTransform`（y-flip 世界变换并按 DPR
  缩放）、缩放/平移/Fit 及世界↔CSS 换算。渲染、选择命中与指针映射共用它。
  面板 resize/DPR 变化时更新缓冲/DPR 并保留 zoom/pan；指针层使用内层
  canvas 内容原点，1px 边框不会造成命中偏移。
- `ScenePainter` 绘制图元树与坐标轴。坐标轴改为在屏幕空间、图元下方绘制：
  原点取世界 `(0,0)` 的投影，可见轴线跨越整个视口，箭头和标签被限制在
  视口内，因此缩放/平移后不会缩短或被裁切。可见/命中画布使用相同的容器
  矩阵；画笔、三角形/箭头路径与文本字体在实例内复用，避免逐图元逐帧分配；
  命中绘制关闭抗锯齿，保证 1px 边缘可命中。
- `HitTestBuffer` 持有离屏位图、每帧颜色键映射与像素→图元查找。每帧从
  `0xFF000001` 分配到 `0xFFFFFFFF`（黑色 `0xFF000000` 是背景，永远不作为
  键）。单帧容量为 16,777,215 个图元，耗尽时抛出 `InvalidOperationException`，
  不会回绕到背景/透明颜色。不存在持久 `ColorKeyManager`；键只需在“渲染 →
  命中”一个周期内稳定。
- `SelectionOverlay` 绘制选择框与四个屏幕固定 8px 角柄。角柄命中半径随选区
  屏幕尺寸自适应：大选区 5 CSS px，小选区缩到短边 20%（下限 1.5px），因此
  默认 10×10 图元的四角命中区不会覆盖整个本体。自身或祖先含旋转/剪切的
  选区不显示角柄——世界 AABB 无法用局部轴缩放精确表达；选择框仍然绘制。
- `HitTest(cssX, cssY)` 通过共享的 `ViewTransform` 换算浏览器 CSS 坐标，
  并把颜色键像素解析回图元。
- 视口变化保留用户意图：面板尺寸或设备像素比变化时更新缓冲/DPR，但保留
  `Zoom` 与 `PanX/PanY`。指针层以内层 canvas 的内容原点
  （`getBoundingClientRect() + clientLeft/clientTop`）计算坐标，消除 1px
  边框造成的命中偏移。`CanvasRenderer` 实现 `IDisposable`；面板销毁时释放
  位图、画笔与字体。

### Penelopa（WASM 应用）

- `PenelopaWorkspace.razor` 通过程序式 API 承载工作区：构建
  `AtlasWorkspaceDefinition`（`PenelopaWorkspaceDefinition`），把结果
  `IAtlasWorkspace` 传给 `AtlasWorkspaceHost`，并指定
  `WorkspaceOwnership.External`（组件负责释放）。布局为六区域工作区：
  inline-start 停靠区（Tools + Primitives，下方空槽位）、中央文档组
  （Diagram）、inline-end 停靠区（Properties，下方空槽位）、以及含两个
  空槽位的 block-end 停靠区。侧栏不再固定 300px：左栏使用 22% 比例基准，
  属性栏占剩余分割的 30%，并在分割节点上记录 160–300px 约束（Atlas 0.3.0
  在 splitter 交互中使用这些约束，初始比例轨道本身不会被钳位）。
  1440/1280/1024/900/800/700 视口下画布约 731/643/504/436/381/327 CSS px；
  900px 以下紧凑 CSS 缩小面板内边距和控件尺寸，添加/对齐/属性控件仍可
  访问且无水平裁切。四个空组保持折叠，仅保留工具栏槽位。`PenelopaTopToolbar` 与
  `PenelopaStatusBar` 通过 `TopToolbarContent` / `StatusBarContent` 宿主
  插槽提供，四个面板通过 `ContentRoutes` 注册，使用
  `PenelopaContentKinds` 中的种类常量。
- `PenelopaWorkspaceDefinition.cs` 以程序式方式构建布局树
  （`SplitNode` / `GroupNode` / `DockItem` 数组与工具栏状态），复刻声明式
  缺省值：内部停靠区与底部停靠区使用 0.5 比例基准，文档组使用 Scroll
  溢出与 Adjacent 激活，空折叠组为 Persistent 且无选中项。
- `CanvasPanel.razor` 承载 `SKGLView`（`EnableRenderLoop=false`，默认
  `IgnorePixelScaling=false` 物理分辨率渲染）。渲染改为按需：文档/选择变化、
  pointer/wheel/pan 手势、DPR 变化与 Fit 时调用 `SKGLView.Invalidate()`。
  画布元素填满视口（无固定内容尺寸），视图本身由 `ViewTransform` 缩放/平移
  管理：Ctrl+滚轮围绕光标缩放（普通滚轮留给页面）、空白拖拽平移、
  Fit 按钮（`renderer.FitToContent`）
  把内容居中到视口，因此不再需要滚动条。接线 `OnPaintSurface` → `CanvasRenderer.Render`
  （传事件的 `e.Info` 尺寸与当前 `devicePixelRatio`），以及 `mousedown` →
  `HitTest` → 选择（Ctrl 点击追加）。画布元素可聚焦（`tabindex="0"`、
  `role="application"`、`aria-label` 和仅供读屏的说明段落）：方向键按 1 个
  世界单位移动顶层选区（Shift 为 10），Delete 通过单条事务删除选区，
  pointerdown 会把焦点移到画布。指针层使用 `clientX/clientY` 与内层 canvas
  内容原点换算，视口/布局变化不会造成命中偏移。
  `wwwroot/js/penelopa.js`（由 `ts/penelopa.ts` 编译，编译产物随仓库提交以便无 node 环境运行）用 matchMedia 监听
  `devicePixelRatio` 变化，跨 DPI 显示器拖动时保持同步，并承载指针层
  （捕获、画布相对 CSS 坐标、抑制合成鼠标事件、rAF 节流、滚轮缩放），
  以语义回调方式上报交互控制器。指针坐标相对内层 canvas 内容原点（排除
  1px 边框），因此与绘制表面一致；原点在 wheel / pointerenter / resize /
  scroll / `ResizeObserver` 失效时惰性刷新，页面加载后第一次 Ctrl+滚轮即可
  缩放，无需先点击画布。事件目标为
  `input`/`textarea`/`select`/`contenteditable` 时跳过编辑器快捷键，
  输入框内的 Ctrl+Z 交给浏览器做文本撤销。`watchDpr` 返回可释放句柄；
  面板先移除 `matchMedia` 监听再释放 `DotNetObjectReference`，避免重建后
  调用已释放引用。

### 容器与钻取

`Container`（Core）组合子图元并施加仿射 `LocalTransform`——A661 风格的
旋转/偏移/翻转容器是同一类型的工厂变体。容器自身无可见形状；子图元在
其变换内渲染（`DrawNode` 在 Save/Restore 下把矩阵同时施加到可见与命中
画布，颜色键命中与所见一致）。子图元持有局部坐标；`Primitive.Translate` /
`SetBounds` / `ContainsWorldPoint` 通过父链（`Container.ToLocalVector` /
`ToLocalPoint` / `WorldToLocalBounds`）把世界输入换算到局部，因此在旋转
容器内拖动跟随指针。

`SetBounds` 契约是显式的：`Primitive.SetBounds(worldBounds, worldAnchor)`
只做一次世界 → 父级局部转换，然后调用
`SetBoundsLocal(localBounds, localAnchor)`。`SetBoundsLocal` 只读取并返回
局部包围盒（圆返回自身局部框，三角形用局部框归一化顶点），不得调用
`GetWorldBoundingBox`；结果再转换回世界一次。`WorldToLocalBounds` 沿祖先
链求逆，因此嵌套容器映射正确。容器世界包围盒是变换后子元素的 AABB，
选择框与对齐因此无需改动。旋转/剪切祖先使世界 AABB 缩放无法精确表达：
此时 `CanResizeInWorld` 为 false，UI 隐藏角柄而不是绘制不跟随指针的框。
轴对齐容器（偏移/翻转）可非等比缩放；旋转容器的程序式调用仍保持原有
局部等比缩放语义。

钻取选择（hit-through）通过 `CanvasRenderer.PickAll` 收集一点处的候选：
颜色键顶层图元及其祖先链，按 根 → 最深叶子 排序（已确认的方向）。
`EditorInteractionController` 每次点击从最外层候选开始，在同一位置重复
点击（4 世界单位与 500ms 内）推进索引，停在最深叶子；移开或点击空白
重置上下文。Ctrl 点击跳过钻取（追加/切换），树面板可直接选择任意祖先，
ESC 取消手势。

所有选择入口（`SetSelected`、`SetSelectedRange`、`AppendSelected`）统一
规范化：祖先已选中时排除其后代。拖拽、对齐和多选并集框因此只作用于
顶层选择，子图元不会同时经由自身与容器被变换两次。

### 文档状态

`PrimitiveService` 是唯一状态源。`GetState()` 返回不可变的 `DocumentState`
快照（根列表与选区列表均为副本），`GetAll()` / `GetSelection()` 返回只读
快照，一个 `Changed(DocumentChange)` 事件携带变更类别（结构/选择/几何）和
新快照。`CanvasPanel`、`PrimitiveTreePanel` 与 `PropertyPanel` 初始化时读取
`GetState()`，之后全部从 `Changed` 派生，不再各自维护集合。`ReplaceAll`
总会发出结构 + 选择变更，因此加载“无选择”文档会清空属性面板，而不是保留
陈旧目标。

属性面板编辑整个选区，而非只看第一个图元。它只显示所有选中项都存在的
同名同类型属性；值相同正常显示，值不同显示空字段和 `Multiple values`
占位。写入经 `TrySetProperty(owners, name, text)`：逐项校验，任一项拒绝时
回滚全部已写入值，成功时只产生一个历史条目。`Name` 是普通可编辑字符串
属性（去空白、非空、最多 128 字符）。Tools 面板的 `Delete` 按钮和 Delete
键调用 `RemoveRange`：一次事务删除全部顶层选中项，清理失效选区（包括被删
容器的后代），可撤销。

### 分组与取消分组

Tools 面板提供 Group 与 Ungroup。分组要求至少两个已规范化的根级选中项：
服务在第一个选中根的索引处创建 identity 偏移容器 `Group`，按根顺序把选中项
移入容器（世界位置与 z 序不变），选中容器，并提交一个事务。撤销恢复原根列表；
选择不进入历史，因此已不存在的分组容器会从选区中过滤掉，选区变为空。

取消分组只接受 `LocalTransform` 为 identity 的根容器（偏移 0、旋转 0、缩放 1；
360° 在浮点容差内接受）。嵌套容器和非 identity 容器会被拒绝并显示可读错误，
按钮同时禁用；被拒绝的命令不产生历史条目。成功的取消分组把子项按原顺序合并回
容器索引处的根列表，移除容器并选中子项，可撤销。快照恢复会先把节点从非目标
父级上摘下，并在安装根列表后重建文档归属，因此只存在于某一历史状态的分组容器
也能正确恢复。

### 历史与持久化

`PrimitiveService` 持有 `DocumentHistory`（快照式 undo/redo；见
[ADR-0002](../adr/0002-history-transactions-and-snapshots.md)）。快照按引用
捕获图元树，外加每个图元的属性值与容器子元素；选择是 UI 状态、不被捕获，
undo/redo 后按引用重新应用仍存活的选区。捕获对异常安全：不支持的属性会
中止事务且不会破坏历史深度。

所有文档修改都经过服务层事务边界：结构变更
（`Add`/`RemoveRange`/`AddToContainer`）、对齐、单选/多选属性编辑、键盘
微移、拖拽/缩放手势都调用 `BeginTransaction` → 修改 →
`CommitTransaction`，恰好产生一个撤销条目。
`CancelTransaction` 应用同一份事务前快照，精确恢复属性与容器子元素，
因此 ESC/pointer-cancel 不再重新计算 `SetBounds`。手势在首次真实修改时
惰性开启事务；无修改点击与已对齐操作不产生历史条目。应用快照会重建
根列表、恢复属性值与子元素，并按引用重新选择存活图元。恢复子项前会先把
节点从非目标父级上摘下并临时清除归属，安装根列表后再统一重建归属。
Ctrl+Z / Ctrl+Shift+Z（或 Ctrl+Y）通过指针层的按键处理驱动 undo/redo。

属性输入在领域层校验（`PropValue.TrySetText`）：半径/宽高必须为正，
数值必须有限且在编辑器量级内，颜色接受 `#RRGGBB` / `#RRGGBBAA`
（属性面板按 R/G/B/A 四通道编辑），名称去空白后
非空且不超过 128 字符。非法文本显示
字段错误且不写入；反序列化用同一套约束校验每个属性，非法文档整体拒绝。

`DocumentSerializer` 把文档往返为 JSON：图元按类型存储其属性值
（颜色键不序列化——加载时重建），容器嵌套子元素，选择保存为图元 Id
以便加载后按新树重解析。文档带 `Version` 2；缺失版本/v1 按无仿真配置
接受，未来/负版本明确拒绝。可选 `simulation` 段保存循环开关与轨道
（图元 Id、Float 属性名、带时间/值/插值的关键帧）。加载时几何严格校验，
仿真段独立校验：先解析目标，目标图元/属性已不存在的轨道在校验前逐条
剪除并警告；Active 轨道结构性非法时整段丢弃并警告，但文档照常加载。原始
轨道/关键帧数量上限在剪除前检查。JSON 数组中为 `null` 的图元或子节点按
无效文档拒绝，不会解引用崩溃。Dormant 轨道不会写入保存结果。加载
仍校验非空且唯一的 Id、拒绝非容器带子元素，并限制节点数（10,000）、
嵌套深度（64）与 UTF-8 字节数（4 MiB）。

`DocumentStorage` 是 Tools 面板与仿真页共享的保存/加载边界：序列化 v2
文档、把上一版写入 `penelopa.document.backup`，并把 JS 异常转换为可显示
错误。localStorage 访问经由 `penelopa.readStorage`/`writeStorage` 帮助函数
返回结果对象而非抛异常，因此配额/安全限制等预期失败不会触发 Blazor 全局
错误覆盖层。加载边界还会把任何未预期异常转换为 `DocumentLoadResult` 错误，
避免损坏负载击穿渲染器；完整异常写入日志，UI 只显示单行摘要。Tools 面板
先解析校验再要求确认，失败时保留当前文档；成功时总是应用解析后的选区
（包括空选区）。面板使用单一 pending 操作（Dormant 保存确认或加载确认）：
开始加载会取消未确认的 Dormant 保存；确认保存时重新计算当前活动/失效
轨道，绝不复用旧快照。加载会同时替换文档与仿真配置。数据存储于
`localStorage` 的 `penelopa.document`。

### 交互

编辑手势由 Core 层的 `EditorInteractionController` 驱动——一个小的状态机
（Idle / Pressed / Dragging / Resizing），接收世界坐标指针位置与分层
`HitTestResult`，在按下时快照选择集与几何，提交时一次性通知各面板。
ESC、pointer-cancel、捕获丢失或窗口失焦恢复快照并回到 Idle。点击多选
集合中已选成员时延迟到抬起才做决定：同一手势要么塌缩选择（单击）要么
拖动整组；Ctrl 点击切换成员资格。缩放保持被拖手柄对角的固定（
`ResizeMath`，带最小尺寸约束与穿越固定角时的镜像翻转），图元通过
`SetBounds(bounds, anchor)` 把自身几何拟合到目标包围盒：圆保持正圆且
固定角在圆上，三角形按归一化位置映射顶点。取消手势通过共享的事务快照
回滚，而不是重新计算包围盒。
- `ToolPanel.razor` 提供 Add（Circle/Rectangle/Triangle）、Align（六方向）、
  Delete-selection、Group 与 Ungroup 操作；它们都走服务事务。无选区时 Delete
  禁用；Group 要求至少两个根图元，Ungroup 要求一个 identity 根容器。按钮下方
  的提示同时面向读屏与视觉用户说明这两个条件。
- `PrimitiveTreePanel.razor` 列出全部图元，点击选择；选中项暴露
  `aria-pressed="true"`。
- `PropertyPanel.razor` 通过 `Panels/Props/` 中的类型化输入组件渲染单选或
  多选公共 `PropValue` 属性包。每个字段有稳定 id、`<label for>` 关联，并以
  `aria-invalid` / `aria-describedby` 指向错误信息；change 时经
  `IPrimitiveService.TrySetProperty` 提交。`PropEditorBase` 集中处理冲突
  显示与批量写入。
- 画布可聚焦并具备键盘路径（见下节）。

### 仿真页

`/simulation` 页面（`Pages/SimulationPage.razor`）与编辑页共享单例
`IPrimitiveService` 与 `SimulationConfigStore`，但维护自己的本地选区。画布
只读：无命中测试、拖拽、缩放或选区覆盖层；可见表面暴露为 `role="img"`，
Fit 按钮仍是普通按钮。布局为固定三栏（图元树、画布、检视器）加传输条与
全局时间轴。

`SimulationRuntime` 是确定性的预览会话。首次 Play/定位/步进时捕获所有
Float 属性的基础值，把轨道解析为 Active/Dormant，并在请求时刻求值；运行时
临时写入属性值，在 Stop、离开页面或异常时恢复基础值，且从不产生文档历史
条目。离开页面（含浏览器后退）会在等待任何 JS 句柄之前同步停止会话，并
只向共享图元服务通知一次，使编辑器的属性面板/画布无需重新选择即可重读
恢复后的基础值。状态为 Ready / Preview / Playing / Paused / Finished，所有
活动轨道共享绝对时间轴 `0..T`（`T` 为最后一个关键帧时间）；Loop 循环整条
时间轴，否则到达 `T` 后进入 Finished 并保持末帧。

JS `requestAnimationFrame` 循环调用 `OnSimulationFrame`；页面用单调
`Stopwatch` 推进运行时（不累加帧步长），在 Ready/Paused/Finished 停止循环，
标签页隐藏时自动暂停。播放期间启用 SKGLView render loop：仅靠按需
`Invalidate` 会与仿真 rAF 的顺序合并到约 30 paint/s；Ready/Paused/Finished
恢复按需渲染。`SimulationReadoutChannel` 以 20Hz 把时间与轨道值发布给小组件（时间读数、逐属性当前值、播放头、原生滑杆），
检视器表格与时间轴标记不在读数路径上重渲染，图元树或整页也不重渲染。

渲染走只读路径（`CanvasRenderer.Render(..., renderHitBuffer: false)`，
`ScenePainter` 接受 null 命中画布），播放时跳过颜色键缓冲与选区覆盖层。
`SimulationBoundsEstimator` 采样关键帧时间与相邻中点、合并世界包围盒并恢复
基础值，用于进入页面时的动画范围 Fit 与 Fit 按钮。

检视器用 **Animate** 建轨（播放头与 `+1s` 各一帧），在播放头添加/更新关键帧，
编辑时间/值/插值/自定义 cubic-bezier 控制点，删除关键帧/轨道，并显示实时
轨道值；值标注为相对父级。时间轴只展示所选图元的轨道，含标尺、原生 range
播放头、缩放/滚动与可聚焦关键帧标记。点击刻度会 seek，双击标记走与检视器
相同的 Go to 路径。拖动标记会用 pointer capture 重定时，但仅在真正移动后
才捕获指针（点击/双击仍以标记为目标），吸附 1/60s、保持 1ms 最小间隔并夹在
相邻帧之间；检视器是键盘替代路径并提供 Go to。关键帧选择以时间为锚点，
删除或撤销相邻关键帧后仍跟随同一帧，不会跳到别的索引。

配置编辑在 Ready/Preview/Paused/Finished 可用，Playing 中只读。独立的 50 步
配置历史（按钮以及焦点不在文本控件时的 Ctrl+Z/Ctrl+Y）在进入页面与加载文档
时清空，且从不与文档历史共享条目。非法活动轨道会阻止播放，页面以 polite 汇总区域列出全部阻断原因，字段编辑
仍使用内联错误；
Dormant 轨道显示在页面横幅中，提供数量、可展开明细（图元 Id、属性、关键帧
数）、逐条删除与全部清理，清理可撤销。Save 仅在 Ready 可用，写入文档与
活动仿真配置；存在 Dormant 轨道时必须确认并排除它们；移除最后一个失效
轨道会清除待确认的保存。

周期性能基准实测：500 图元 + 100 活动轨道 59.9–60.2 JS rAF tick/s、59.9–60.0 实际
.NET frame/s、59.9–60.1 SKGLView paint/s；Stop 后 Ready 状态在参考 headless
Chromium/1440×900 环境下空闲主线程 CPU 2.94–3.75%；选中 200 关键帧轨道播放时
约 60 tick/s、约 60 paint/s（多次运行 59.79–60.18 / 59.76–60.04）。性能探针分别读取 rAF 尝试、`OnSimulationFrame`
执行次数与 `OnPaintSurface` 回调次数；仅看 tick 会高估绘制帧。上述计数器仅在
PERF 基准于应用启动前设置 `__PENELOPA_PERF__` 时挂到 `window.penelopa`；
普通编辑页/仿真页不暴露性能计数器 API。

## 可访问性

- 仿真页提供导航时聚焦的 `h1`（`tabindex="-1"`）、从不自动播放、画布 `role="img"`、原生传输
  按钮与 `aria-live="polite"` 状态/错误区域；关键帧字段有关联标签与
  `aria-describedby`，检视器提供不依赖拖拽的键盘编辑路径；关键帧标记视觉
  尺寸不变但扩大了不可见命中区。页面检测
  `prefers-reduced-motion` 并提示，但仍允许用户主动播放；20Hz 时间/数值读数
  有意不设为实时区域。
- 画布是 `role="application"` 的可聚焦表面，名称为 `Drawing canvas`，
  并通过视觉隐藏段落说明键盘操作。方向键按 1 世界单位移动顶层选区
  （Shift = 10），Delete 删除，Escape 取消手势。编辑器快捷键会忽略
  `input`/`textarea`/`select`/`contenteditable` 上的事件，文本编辑
  （包括 Ctrl+Z 和 Delete）交给浏览器。
- 属性输入与图元树暴露名称/状态；状态栏是 polite 实时区域
  （`role="status" aria-live="polite"`）。
- viewport meta 允许页面缩放（不再有 `maximum-scale` / `user-scalable=no`）。
- bUnit 覆盖面板标签/ARIA 接线；Playwright 覆盖画布键盘路径、输入框快捷键、
  ARIA 属性与页面缩放。

## 响应式布局

侧栏使用比例分割而非固定 300px 轨道；900px 以下紧凑 CSS 缩小面板内边距、
按钮与标签内边距。1440/1280/1024/900/800/700 视口实测画布宽度为
731/643/504/436/381/327 CSS px，无文档级水平溢出，四个面板仍可访问。
Playwright 布局测试在各宽度断言画布宽度、无水平溢出，以及
Add/Align/Properties 控件可见。另有特征测试记录：Atlas 0.3.0 不会把初始
比例轨道钳位到 160px 最小宽度；若升级后行为改变，需要同步修订该测试与本节。

仿真页为固定三栏加底部时间轴；列宽在 1024px 与 820px 处收窄，时间轴在缩放
时横向滚动、轨道较多时纵向滚动，并复用 `--penelopa-*` 主题变量。

## 扩展图元与属性

目前没有类型注册表。新增图元类型仍需修改：`Primitives/` 模型、
`ScenePainter.DrawNode` 分支、`DocumentSerializer` 的类型与属性名映射，
以及（若引入新的 `PropValue` 子类型）`PropertyPanel` 的编辑器分支。未知
`PropValue` 子类型会在历史/序列化中显式失败，并在进入文档前被拒绝。
类型注册表/访问者属于更大的重构，Phase C 有意未引入。

## 高度链

Atlas 工作区由其父级确定尺寸；编辑器外壳必须将 `html/body/#app/.penelopa-main/.penelopa-shell/.atlas-v2-workspace` 链到 100% 高度（见 `wwwroot/css/app.css`）。顶栏与状态栏在 Atlas 宿主内部渲染，分别位于工作区上方与下方。仿真页将 `.penelopa-simulation` 链入 `html/body/#app/.penelopa-main`，并使用自己的顶栏/主体/时间轴纵向 Flex 布局（见 `wwwroot/css/simulation.css`）。

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

## 架构决策（ADR）

| ADR | 主题 |
| --- | --- |
| [0001](../adr/0001-coordinate-spaces-and-setbounds.md) | 坐标空间与 `SetBounds` 契约 |
| [0002](../adr/0002-history-transactions-and-snapshots.md) | 历史事务与快照内容 |
| [0003](../adr/0003-selection-model.md) | 选择规范化与多选编辑 |
| [0004](../adr/0004-grouping-deferred.md) | 分组与取消分组现有图元 |
| [0005](../adr/0005-simulation-keyframe-timeline.md) | 关键帧仿真时间轴、预览隔离与持久化 |

## 工具链说明

- `global.json` 固定 SDK 6.0.428。使用 SDK 10 构建会触发 net6.0 WASM 的 RazorSourceGenerator 增量输入 bug，因此锁定版本。
- SkiaSharp 固定 2.88.9：4.x 的 `SkiaSharp.NativeAssets.WebAssembly.targets` 要求 net8.0+。显式引用 `SkiaSharp.NativeAssets.Linux` 是因为 2.88.9 只传递拉取 Win32/macOS 原生资源；缺少时 Linux CI 测试会报 `DllNotFoundException: libSkiaSharp`。
- `wasm-tools` workload 用于把 SkiaSharp 原生资源链接进 WASM 构建；CI 在 restore 前安装。
- `NuGet.Config` 通过 `packageSourceMapping` 把所有包映射到 nuget.org，使中央包管理（CPM）不会在 GitHub Actions runner 上触发 NU1507（runner 默认带有 `library-packs` 源）。
- Node 对普通构建是可选的：仅当 `node_modules/typescript` 存在时
  `BuildTypeScript` 才运行，否则使用提交的
  `wwwroot/js/penelopa.js`。修改 TS、运行 JS 与 Playwright 测试需要
  Node/npm；CI 总是先 `npm ci`，编译 TS 并检查生成 JS 是否漂移。
- 每个项目都提交 `packages.lock.json`；CI 使用
  `dotnet restore --locked-mode`，本地普通 restore 可能更新锁文件。
- Phase C 删除了 Bootstrap 与 Open Iconic 字体包，剩余表单/面板样式收敛到
  `wwwroot/css/app.css`；应用只发布 Atlas.Blazor CSS、应用样式与编辑器资源。
- 测试：xUnit 覆盖 Core/Rendering；bUnit（`tests/Penelopa.Web.Tests`）覆盖面板
  状态、ARIA 接线与持久化 UI；Playwright（`tests/e2e`）在真实 Chromium 中
  驱动桌面与窄视口编辑器。CI 运行 JS/Node 测试、生成 JS 漂移检查、覆盖率
  门禁（Core ≥90%、Rendering ≥85%，`Simulation/` 与 `DocumentSerializer`
  文件级 ≥90%）、Chromium E2E，以及依赖审计
  （两个 npm 根目录的 `npm audit` 与
  `dotnet list package --vulnerable --include-transitive`）。NuGet 审计在
  high/critical 时失败。bUnit 仅用于测试的 DOM 解析器仍带有中危 AngleSharp
  mXSS 公告——修复版 AngleSharp 1.5 与 bUnit 1.28/1.40 的 API 不兼容。
  这是已接受、仅测试环境的风险，无生产暴露，并记录为 S-01 测试栈迁移的
  前置项。高危的 `Microsoft.Extensions.Caching.Memory` 已通过固定 6.0.2 修复。
- GitHub Pages 部署保持 `workflow_dispatch` 手动触发，发布是显式操作，
  不会随每次 `main` 合并自动上线。发布产物把 `<base href>` 改写为
  `/Penelopa/`，并生成入口页的 `404.html` 副本，使 `/Penelopa/simulation`
  等项目站深链接进入 Blazor 路由而不是 GitHub Pages 404。
- 按需渲染后，`PERF=1` 的 Playwright 周期基准实测：空文档空闲主线程 CPU 约 0.03%、500 矩形空闲约 0.02%（headless Chromium，Debug 开发服务器）；原审查中的持续渲染空文档空闲约 28%。基准位于 `tests/e2e/tests/perf.spec.ts` 与 `tests/e2e/tests/simulation-perf.spec.ts`，未设置 `PERF=1` 时跳过。
