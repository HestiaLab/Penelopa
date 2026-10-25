# Penelopa 架构与代码质量审查报告

| 项目 | 内容 |
| --- | --- |
| 报告日期 | 2026-10-25 |
| 审查基线 | `main@6ae72e6`（工作区除 `.qoder-credits/` 外干净） |
| 审查范围 | 全仓库：Core、Rendering、Blazor WASM UI、TypeScript、测试、CI、工具链、文档 |
| 外部边界 | Atlas.Blazor 0.3.0 仅作为黑盒评估用法与耦合风险，未审计其内部实现 |
| 审查方式 | 静态阅读 + 门禁执行 + 覆盖率插桩 + Chrome 端到端实跑 + Release 性能测量 |
| 变更情况 | 只读审查；除本报告外未修改仓库，未提交、未推送、未创建 Issue |
| 说明 | 本报告由 AI 辅助完成；关键结论均附 `file:line`、实测值或复现步骤，建议维护者复核后转为任务 |

---

## 0. 执行摘要

**结论：工程基础扎实，但核心编辑路径仍有 1 个 P0、5 个 P1。当前不适合在没有回归测试保护的情况下继续增加图元类型或交互。**

门禁是全绿的：构建 0 警告/0 错误，133 个单测全部通过，`dotnet format` 与 `tsc` 均通过。但审查发现，**现有测试完全没有覆盖发现 P0/P1 的路径**：WASM UI、TypeScript、浏览器集成测试均为零。因此“CI 绿”不能作为功能正确的证据。

最需要先处理的是三条架构裂缝，而不是逐个补 bug：

1. **坐标空间契约不统一**：`SetBoundsLocal` 的入参/返回值混用局部、父级、世界坐标；容器内三角形的缩放会直接算错，取消手势也无法恢复（P0）。
2. **历史与 UI 通知依赖调用方自觉**：对齐、属性编辑不进入历史；`Ctrl+Z` 会删除上一次结构操作新增的对象，而不是撤销刚做的编辑（P1）。
3. **选择集是未规范化的扁平集合**：容器与其子图元可以同时选中，拖拽和对齐会重复施加变换（P1）。

另有三个直接阻断日常编辑的缺陷：属性输入负值会抛异常打断渲染循环（P1）；新页面首次 `Ctrl+滚轮` 因 `rect` 为空而抛错，缩放不可用（P1）；默认 10×10 图元的角柄命中区覆盖全身，拖拽实际变成缩放，小容器无法点击钻取（P1）。

**如果只能先做三件事**：修坐标空间（F-01）→ 建立事务/命令边界并覆盖所有修改（F-03）→ 给属性输入加校验（F-02）。之后立即补 UI/E2E 测试，否则无法安全重构。

**不应破坏的优点**：Core 对 Blazor/Skia 的零依赖、交互控制器的可测试性、颜色键命中测试与 `ViewTransform` 的共享设计、以及现有工程卫生（Nullable、警告即错误、中心化包管理、固定 SDK、格式化/类型检查门禁）。

---

## 1. 审查维度结论

| 维度 | 结论 | 主要依据 |
| --- | --- | --- |
| 架构与模块边界 | **够用** | 三层依赖方向正确，Core 纯净；但历史事务、选择模型、UI 状态边界是主要债务，`CanvasRenderer` 职责过重 |
| 代码质量与可维护性 | **够用偏弱** | 命名、注释、测试结构良好；坐标空间与 `SetBounds` 返回契约不一致，601 行渲染器集中过多职责，服务暴露可变集合 |
| 正确性与测试有效性 | **弱** | 算法层测试扎实，但容器内缩放、选择组合、历史覆盖等核心路径有 P0/P1；WASM/TS 零测试，关键渲染分支未覆盖 |
| 安全 | **够用** | 无已知依赖漏洞，无 `MarkupString`/`innerHTML` 注入面，数据仅存 `localStorage`；但加载校验、文档格式版本、CSP 缺失 |
| 性能 | **弱** | 持续渲染循环 + 每帧分配；Release 空文档空闲约占 28% CPU，500 个简单矩形达到 99.8% CPU |
| 可访问性 | **弱** | 画布不可聚焦、无角色/键盘路径；属性输入与标签无语义关联；树/状态栏缺少 ARIA 状态；禁用了页面缩放 |
| 工具链长期风险 | **战略项** | .NET 6 已 EOL、Node 20 已 EOL、SkiaSharp 2.88.9 与 Atlas 0.3.0 偏旧；`Penelopa.csproj` 含机器相关的 VS 硬编码 |

---

## 2. 做得好的地方（不要破坏）

1. **依赖方向干净**：`Penelopa.Core` 不引用 Blazor/SkiaSharp；`Rendering → Core`；WASM 宿主同时依赖二者，没有反向依赖或环。
2. **交互控制器可测试**：`EditorInteractionController` 通过 `IEditorInteractionHost` 与 UI 解耦，状态机（Idle/Pressed/Dragging/Resizing）和拖拽阈值/取消语义有明确测试；行覆盖 100%，分支覆盖 97.4%。
3. **命中测试设计合理**：可见位图与颜色键命中位图共享同一 `ViewTransform`，避免 DOM/几何双份计算；DPR、Y 翻转、缩放/平移都有单元测试；容器绘制与命中使用同一矩阵。
4. **工程卫生强**：`Nullable`、`TreatWarningsAsErrors`、`AnalysisLevel 6.0`、确定性构建、中心化包管理、固定 SDK 6.0.428、CI 双平台矩阵、`dotnet format` 与 `tsc` 门禁、PR 模板，这些在小型项目中属于高水准。
5. **文档与注释有价值**：DPI、Emscripten、SkiaSharp 原生资产、颜色键边缘、坐标系 Y 翻转等非显然约束都有解释；中英双语文档和 PR 模板体现了维护意识。
6. **已验证的可用路径**：添加三类图元、单选/多选、六向对齐、普通图形拖拽/缩放、拖拽/缩放的撤销重做、容器嵌套与下钻（较大子图元）、保存/加载并恢复选择、深浅主题、初始化后的缩放/平移/Fit。

---

## 3. 发现清单

### 总览

| ID | 级别 | 标题 | 主要位置 |
| --- | --- | --- | --- |
| F-01 | **P0** | 容器内 `SetBounds` 坐标空间不变量失效 | `Primitive.cs`、`Container.cs`、交互控制器 |
| F-02 | P1 | 属性输入无校验，负值触发渲染循环异常 | `FloatProp.razor`、`Primitive.cs`、`Box.cs` |
| F-03 | P1 | 对齐/属性编辑未纳入历史，`Ctrl+Z` 造成破坏性撤销 | `ToolPanel.razor`、`DocumentHistory.cs`、`penelopa.ts` |
| F-04 | P1 | 父子图元同时选中导致双重变换 | 控制器、`PrimitiveService`、对齐扩展 |
| F-05 | P1 | 首次 `Ctrl+滚轮` 空引用，缩放不可用 | `penelopa.ts` |
| F-06 | P1 | 默认小图元角柄覆盖全身，拖拽/钻取被劫持 | `CanvasRenderer.cs`、默认图元尺寸 |
| F-07 | P2 | UI 状态重复与事件语义不完整，出现陈旧属性面板 | `PrimitiveService`、`CanvasPanel`、`PropertyPanel` |
| F-08 | P2 | 渲染器职责过重、持续渲染与每帧分配，性能上限低 | `CanvasRenderer.cs`、`CanvasPanel.razor` |
| F-09 | P2 | 面板尺寸/DPR 变化重置视图；指针坐标系有 1px 偏移 | `CanvasRenderer.cs`、CSS、TS 指针层 |
| F-10 | P2 | 文档反序列化缺少 ID/版本/规模校验，坏数据可崩溃 | `DocumentSerializer`、`ToolPanel` |
| F-11 | P2 | 测试金字塔缺失：UI/TS/E2E 为零，关键渲染分支未覆盖 | `tests/`、CI |
| F-12 | P2 | 固定 300px 双栏导致窄窗口画布不可用 | `PenelopaWorkspaceDefinition.cs`、实测宽度 |
| F-13 | P2 | 可访问性缺口：标签、画布键盘、ARIA、页面缩放 | Razor、`index.html`、CSS |
| F-14 | P2 | DPR `matchMedia` 监听未释放，可能泄漏并调用已释放引用 | `penelopa.ts`、`CanvasPanel` |
| F-15 | P2 | 架构/README/构建注释与实现漂移；无 Node 构建文档 | `docs/`、`README.md`、`.csproj` |
| F-16 | P3 | 颜色键无回绕保护；缩放下坐标轴不覆盖视口 | `CanvasRenderer.cs`、文档 |
| F-17 | P3 | 长期编辑器的能力缺口：删除/重命名/分组、多选属性编辑等 | UI 面板 |
| F-18 | P3 | 封装与扩展性：暴露可变集合、未知属性静默跳过、缺少类型注册表 | 服务、`PropValue`、渲染/序列化 switch |
| F-19 | P3 | 资源与流程：Bootstrap/图标未使用、发布体积、CI 门禁不足 | `index.html`、`app.css`、CI |

---

### F-01 [P0] 容器内 `SetBounds` 坐标空间不变量失效

**位置**

- `src/Penelopa.Core/Primitives/Primitive.cs:80-90`（父容器转换）
- `src/Penelopa.Core/Primitives/Primitive.cs:297-311`（`Triangle.SetBoundsLocal` 使用 `GetWorldBoundingBox()`）
- `src/Penelopa.Core/Primitives/Primitive.cs:171-185`（`Circle.SetBoundsLocal` 返回世界包围盒）
- `src/Penelopa.Core/Primitives/Container.cs:137-154`、`187-210`
- 调用方：`src/Penelopa.Core/Interaction/EditorInteractionController.cs:180-185`、`239-244`

**问题**

`SetBoundsLocal` 的契约没有在类型之间统一：`Rectangle` 返回局部包围盒（`Primitive.cs:240`），`Circle`/`Triangle` 返回世界包围盒；`Triangle` 还在“局部空间”的方法里调用 `GetWorldBoundingBox()` 参与缩放计算。只要图元位于非单位变换的容器内，局部/父级/世界坐标就会混用。

**实测证据（核心层，仓库外临时复现工程）**

- 容器 `Offset(20,20)`，三角形局部顶点 `(50,50)(70,50)(60,60)`；目标世界框 `Box(70,70,120,100)`、锚点 `(70,70)`：
  - 期望：世界框变为 `(70,70)-(120,100)`；
  - 实际：世界框变为 `(20,10)-(70,40)`，顶点被大幅拉偏。
- 同一三角形缩放后按 ESC 取消：
  - 实际复位到 `(62,63.33)-(82,73.33)`，局部顶点变为 `42.17,43.05 / 62.17,43.05 / 52.17,53.05`，并非原始值。
- 圆在偏移容器内 `SetBounds` 到 `(20,20,40,40)`：世界框正确变为 `(20,20)-(40,40)`，但返回值为 `(40,40)-(60,60)`（少一次父级偏移）。
- 旋转 45° 容器内的矩形缩放：目标世界框约 `47.07×40`，实际变为约 `87.07×87.07`，锚点也不在固定角。

**浏览器实测**

- 在容器内下钻选中三角形，拖拽右下角后属性面板显示 `Vertex1X=28.75, Vertex1Y=28.51`（原始 `50/50`）；形状明显错位（截图：`15-triangle-child-resized.png`）。
- ESC 取消后属性面板仍显示原值（陈旧，见 F-07）；重新在树中选中三角形后，值变为 `42.17,43.05`，证明模型已损坏（截图：`16b-triangle-esc-reselect.png`）。
- `Ctrl+Z` 可以恢复（快照保存的是属性值），但这是唯一补救，且一旦继续做其他结构操作就可能丢失。

**影响**

这是核心编辑手势的正确性缺陷，会污染选择框、对齐、保存文档和后续命中测试；ESC/pointer-cancel 的“恢复”承诺对这类图元不成立。按契约设为 P0。

**修复方向**

1. 统一 `SetBoundsLocal` 契约：只接收/返回**局部**包围盒，禁止在内部调用 `GetWorldBoundingBox()`；父级转换只在 `Primitive.SetBounds` 中做一次。
2. `Circle.SetBoundsLocal` 返回 `GetLocalBoundingBox()`；`Triangle.SetBoundsLocal` 用局部包围盒做归一化映射。
3. 取消手势不要重新计算 `SetBounds`；用事务快照恢复属性/子元素（与历史共享同一套恢复逻辑），并发出变更通知。
4. 旋转容器内的子图元世界 AABB 拟合本身可能不可表示：短期应明确禁用手势或改为“局部轴缩放 + 提示”，不要假装跟随指针；长期单独设计世界空间拟合（可能需要允许剪切或约束手柄）。
5. 增加参数化测试：每种图元 ×（根、Offset、Rotation、Flip、嵌套容器）×（SetBounds、Cancel、返回框、选择框、命中）。

---

### F-02 [P1] 属性输入无校验，负值触发渲染循环异常

**位置**

- `src/Penelopa/Components/Panels/Props/FloatProp.razor:6`（`@bind="PropValue.Value"` 直接写入）
- `src/Penelopa.Core/Primitives/PropValue.cs:81-88`（`FloatPropValue` 无校验）
- `src/Penelopa.Core/Primitives/Primitive.cs:156-163`、`219-226`（负半径/负宽高生成逆包围盒）
- `src/Penelopa.Core/Alignment/Box.cs:18`（`minX > maxX` 抛 `ArgumentException`）
- 异常落点：`src/Penelopa.Rendering/CanvasRenderer.cs:297` → `Render` → `CanvasPanel.OnPaintSurface`

**实测证据**

- 选中圆，把 `Radius` 改为 `-5`：控制台出现未处理异常
  `System.ArgumentException: minX cannot be greater than maxX`，调用栈为
  `Box..ctor → Circle.GetLocalBoundingBox → Primitive.GetWorldBoundingBox → CanvasRenderer.DrawSelectionOverlay → CanvasPanel.OnPaintSurface`。
- 画布停止绘制内容（只剩轴/选择残留），页面继续运行；负宽度矩形同样可触发。
- 属性输入没有 `min`、范围提示或校验反馈；`UintProp` 只有 `min=0`，`FloatProp`/`DoubleProp` 完全开放。

**影响**

用户可以通过正常 UI 输入让渲染循环抛异常，属于稳定性/正确性问题；若文档已保存，坏值会持久化。

**修复方向**

- 在领域层定义每个属性的约束（半径/宽高 > 0、有限浮点数、颜色范围等），提供 `TrySet`/验证结果，属性面板显示错误而不是写入非法值。
- `Box` 对用户输入不要直接抛到渲染循环；在构造图元/读取属性时做规范化或返回空/错误状态。
- 文档加载同样走校验；渲染循环加最后一道防护（捕获并显示错误，而不是静默破坏画布）。
- 增加 `NaN/Infinity/负数/零/极大值` 的单元与 UI 测试。

---

### F-03 [P1] 对齐/属性编辑未纳入历史，`Ctrl+Z` 造成破坏性撤销

**位置**

- `src/Penelopa/Components/Panels/ToolPanel.razor:92-97`（`Align` 只调用 `AlignExtensions` 和 `NotifyPrimitivesChanged`，没有 `CaptureForGesture`）
- `src/Penelopa/Components/Panels/Props/FloatProp.razor:6`（属性直接绑定，未通知也未捕获历史）
- `src/Penelopa.Core/Services/DocumentHistory.cs:5-9`（注释明确“只在结构变更或拖拽/缩放开始前捕获”）
- `src/Penelopa/ts/penelopa.ts:186-202`（全局 `keydown`，未排除输入控件）

**实测证据**

- 添加 Circle/Rectangle/Triangle，选中 Circle+Triangle，左对齐：三角形 `Vertex1X` 从 `50` 变为 `5`（正确）。
- 按 `Ctrl+Z`：树从 `Circle/Rectangle/Triangle` 变为 `Circle/Rectangle`——**撤销的是最后一次 Add，而不是对齐**，三角形被删除。
- 选中圆，把 `Radius` 改为 `20`，按 `Ctrl+Z`：树变为空，圆被删除（应为撤销半径编辑）。
- 焦点在 `Radius` 输入框内按 `Ctrl+Z`：同样删除圆，并阻止了输入框自身的文本撤销。

**影响**

对齐和属性编辑是编辑器最高频操作，却不可撤销；撤销还会误删最近新增对象，属于用户可感知的数据丢失。全局快捷键还会劫持输入框的编辑行为。

**修复方向**

1. 在服务层引入**事务/命令边界**：`BeginTransaction(kind) → 修改 → Commit/Cancel`；所有修改路径（Add/Remove/AddToContainer、对齐、属性编辑、拖拽/缩放）统一经过它。
2. 属性输入在 `change`/`blur`（或防抖）时提交事务；拖拽/缩放沿用现有 `BeginMutation`，但由服务而不是控制器/面板决定快照。
3. 撤销栈按事务去重，避免拖拽过程中产生多个条目；限制栈深度并记录操作类型，便于 UI 显示。
4. `keydown` 处理器跳过 `input/textarea/select/contenteditable`，或使用 `document.activeElement` 判断；为编辑器快捷键增加作用域。
5. 增加回归测试：对齐后可撤销、属性编辑后可撤销、输入框内 `Ctrl+Z` 只影响文本。

---

### F-04 [P1] 父子图元同时选中导致双重变换

**位置**

- `src/Penelopa.Core/Interaction/EditorInteractionController.cs:87-105`（Ctrl 点击追加“最外层候选”）
- `src/Penelopa.Core/Interaction/EditorInteractionController.cs:108-115`（多选成员延迟提交）
- `src/Penelopa.Core/Interaction/EditorInteractionController.cs:310-324`（拖拽选择集内所有图元）
- `src/Penelopa.Core/Services/PrimitiveService.cs:9`、`100-104`（扁平 `HashSet`，无祖先/后代规范化）
- `src/Penelopa.Core/Alignment/AlignExtensions.cs:24-45`（每项独立计算偏移并移动）

**实测证据**

- 容器 `Offset(20,20)` + 三角形子项；下钻选中三角形后 Ctrl+点击同一位置，选择集变为 `[Offset Container, Triangle]`（这是文档描述的正常 Ctrl 行为）。
- 从三角形中心拖拽 `+40` 世界单位：
  - `Triangle.Vertex1X` 从 `50` → `90`（局部 +40）；
  - `Offset X` 从 `20` → `60`（容器 +40）；
  - 子图元世界位置实际 +80，选择框与指针脱节。
- 核心层复现：容器 + 子项 + 外部矩形一起左对齐时，容器和子项都被各自平移，子项相对容器的布局被破坏（同容器内另一个子项被压到同一位置）。

**影响**

多选拖拽、多选对齐、后续可能的复制/分组都会出现“子图元动两次”。用户通过文档明确支持的 Ctrl 操作即可进入该状态，不是边缘输入。

**修复方向**

- 选择集规范化：若祖先已选中，自动排除其后代；或反过来只允许选择最深的叶子并在需要时提升。选择变化时统一走一个 `NormalizeSelection`。
- 对于“容器 + 子项”确有同时编辑需求，定义组变换语义（只对最外层移动一次，子项保持局部坐标不变）。
- 对齐算法按规范化后的顶层选择计算并只移动一次。
- 增加测试：Ctrl 选择父子、多选拖拽、多选对齐、撤销、选择框显示。

---

### F-05 [P1] 首次 `Ctrl+滚轮` 空引用，缩放不可用

**位置**

- `src/Penelopa/ts/penelopa.ts:62-65`（`rect: null`）
- `src/Penelopa/ts/penelopa.ts:74-81`（`toCss` 直接 `state.rect as DOMRect`）
- `src/Penelopa/ts/penelopa.ts:103`（只在 `pointerdown` 时 `refreshRect()`）
- `src/Penelopa/ts/penelopa.ts:204-211`（`onWheel` 未初始化 rect 就调用 `toCss`）

**实测证据**

- 页面加载后、未在画布上按下鼠标，直接在画布上 `Ctrl+滚轮`：
  - `pageerror: Cannot read properties of null (reading 'left')`
  - 缩放前后截图 MD5 完全一致，说明滚轮事件未生效。
- 先在画布上点击一次（初始化 `rect`）后再 `Ctrl+滚轮`，截图发生变化，缩放生效。

**影响**

缩放是核心画布能力；新用户首次使用滚轮缩放会遇到静默失败 + 控制台异常。文档只写“滚轮围绕指针缩放”，未提示需要先点击，进一步增加困惑。

**修复方向**

- `toCss` 内惰性调用 `refreshRect()`，或在 `pointerenter`/`wheel`/`scroll` 时刷新；不要依赖 `pointerdown`。
- `rect` 失效（布局变化/滚动）时重新读取；对 `null` 做防御。
- 在指针层增加单元/E2E 测试：首事件为 wheel、窗口滚动后 wheel、DPR 变化后 wheel。

---

### F-06 [P1] 默认小图元角柄覆盖全身，拖拽/钻取被劫持

**位置**

- `src/Penelopa.Rendering/CanvasRenderer.cs:196-210`（角柄命中半径为 5 CSS px，角柄绘制 8px）
- `src/Penelopa.Rendering/CanvasRenderer.cs:221-250`（手柄优先级高于图元本体）
- `src/Penelopa.Core/Primitives/Primitive.cs:140-143`、`200-203`（默认圆半径 5、矩形 10×10）
- `src/Penelopa.Core/Interaction/EditorInteractionController.cs:75-82`（命中手柄直接进入 Resizing）

**实测证据**

- 默认 10×10 矩形：从 `(15,15)` 拖到 `(55,25)`，期望移动，实际变为缩放（结果 `PosX=10, PosY=20, Width=46.1, Height=3.79`），说明按下的点落在角柄命中区内。
- 10×10 容器+圆：在同一位置连续点击 3 次，选择始终停留在容器，无法钻取到子圆；拖动容器中心实际改变 `Offset`，而不是移动。
- 几何原因：一个 10×10 方框的四个 5px 命中方区覆盖整个方框，中心点距四角均为 5px，任意点都会命中至少一个角柄。

**影响**

新添加的默认圆/矩形在 100% 缩放下**无法用拖拽移动**；小容器中的子图元无法点击钻取。这是第一分钟就会遇到的交互阻断，用户只能先缩放才能编辑。

**修复方向**

- 角柄命中区按图元屏幕尺寸自适应：当图元小于 `2 × (handle + margin)` 时缩小/隐藏角柄，或优先本体。
- 为图元设置最小可视/可编辑尺寸，或新增时使用更大默认尺寸。
- 手柄可以绘制在包围盒外侧，避免与本体重叠。
- 增加 10×10、5×5、缩放后的命中测试，覆盖“点击中心应选择/移动而非缩放”。

---

### F-07 [P2] UI 状态重复与事件语义不完整，出现陈旧属性面板

**位置**

- `src/Penelopa.Core/Services/PrimitiveService.cs:59-70`（`ReplaceAll` 清空选择，只触发 `OnCollectionChanged`，不触发 `OnSelectionChanged`）
- `src/Penelopa/Components/Panels/CanvasPanel.razor:48-54`（订阅事件但不从服务初始化 `_primitives`/`_selection`）
- `src/Penelopa/Components/Panels/PropertyPanel.razor:56-60`（只依赖 `OnSelectionChanged` 更新目标）
- `src/Penelopa/Components/Panels/ToolPanel.razor:121-135`（加载后只在 `selected.Count > 0` 时设置选区）
- `src/Penelopa.Core/Interaction/EditorInteractionController.cs:224-248`（`Cancel` 不调用 `NotifyPrimitivesChanged`）

**实测证据**

- 保存一个“无选择”的文档，重新加载页面，先选中圆，再点 Load：
  - 树显示 `Circle`；
  - 树中没有任何选中项；
  - 右侧属性面板仍显示 `Circle` 的 `CenterX/CenterY/Radius/Color`（截图 `12-load-no-selection.png`）。
- 三角形在容器内缩放后按 ESC：属性面板仍显示原值，但重新选中后显示损坏后的值（F-01），说明取消路径缺少通知，面板是陈旧快照。
- `CanvasPanel` 若被重新创建（布局重建/内容路由重建），不会从单例服务加载已有文档和选区，画布会空白直到下一次变更事件。

**影响**

属性面板可能编辑一个已不在文档中、或已与画布不一致的图元；用户看到的值和实际模型分离，属于长期演进中的高风险状态管理问题。

**修复方向**

- 单一状态源：`DocumentStore`/不可变文档快照，面板全部从 store 派生，不再各自维护 `_primitives`/`_selection`。
- 用一条统一的 `Changed(changeKind)` 事件（结构/属性/选择/视口）替代四套语义不完整的 `Action`；`ReplaceAll`、`Cancel`、加载都必须发出选择与几何通知。
- `CanvasPanel.OnInitialized` 从 store 读取当前根列表和选区；所有面板同样初始化。
- 用 bUnit/集成测试覆盖“加载无选择文档”“取消手势”“撤销后选择”“面板重建”。

---

### F-08 [P2] 渲染器职责过重、持续渲染与每帧分配，性能上限低

**位置**

- `src/Penelopa.Rendering/CanvasRenderer.cs`（整个文件 601 行；`:14-27` 持有位图、颜色映射、视口与画笔）
- `src/Penelopa.Rendering/CanvasRenderer.cs:369-401`（每帧清空、重建两个画布与颜色映射）
- `src/Penelopa.Rendering/CanvasRenderer.cs:472-561`（每个图元每帧 `new SKPaint`/`new SKPath`）
- `src/Penelopa.Rendering/CanvasRenderer.cs:282-342`（每次绘制选择框新建画笔）
- `src/Penelopa/Components/Panels/CanvasPanel.razor:17`（`EnableRenderLoop="true"` 持续渲染）
- `src/Penelopa/Components/Panels/CanvasPanel.razor:200-211`（未释放 `CanvasRenderer`；渲染器本身未实现 `IDisposable`）

**实测证据（Release 发布产物，Chrome headless，1440×900）**

| 场景 | rAF/s | TaskDuration CPU | Script CPU | 说明 |
| --- | ---: | ---: | ---: | --- |
| 空文档空闲 | 145 | 28.4% | 24.3% | 无交互仍在持续重绘 |
| 500 个简单矩形 | 79 | 99.8% | 97.9% | 基本吃满单核；加载耗时约 1.2s |
| Debug 空文档 | 144 | 29.3% | 25.4% | 与 Release 接近，说明不是调试专属 |
| Debug 500 矩形 | 35 | 99.9% | 99.0% | 调试构建更慢 |

发布目录 `wwwroot` 约 28MB（包含 `.br`/`.gz` 副本）；`dotnet.wasm` 6.82MB（`.br` 2.04MB）。这属于初始加载成本，和稳态 CPU 是两个问题，但都指向“Skia + WASM + 持续渲染”缺少预算。

**影响**

笔记本/平板上的电池与发热、500+ 图元时的交互卡顿、长时间编辑的帧率下降；渲染器同时承担视口、绘制、命中、选择覆盖层、资源生命周期，难以单测和演进。

**修复方向**

- 拆分为 `ViewportController`（zoom/pan/fit/DPR）、`ScenePainter`（图元绘制）、`HitTestBuffer`（颜色键/查找）、`SelectionOverlay`（选择框/角柄）；Canvas 组件只做编排。
- 画笔/字体/路径按颜色或几何版本缓存；避免每帧 `new`；大文档考虑分层/脏矩形/仅重绘变化区域。
- 改为**按需渲染**（`RequestRender`/脏标记）而不是 `EnableRenderLoop`；保留连续渲染作为显式选项。
- `CanvasRenderer` 实现 `IDisposable`，释放位图/画笔/字体；`CanvasPanel.Dispose` 调用。
- 设定性能预算与基准（例如 Release 下 500 个矩形 <50% CPU、≥55fps；空文档 <5% CPU），加入 CI 或定期基准。

---

### F-09 [P2] 面板尺寸/DPR 变化重置视图；指针坐标系有 1px 偏移

**位置**

- `src/Penelopa.Rendering/CanvasRenderer.cs:146-157`（`SetDevicePixelRatio` 用默认 zoom/pan 重建）
- `src/Penelopa.Rendering/CanvasRenderer.cs:159-177`（`EnsureBuffersFor` 在尺寸/DPR 变化时同样重置）
- `src/Penelopa/wwwroot/css/app.css:504-513`（内层 canvas 有 1px border）
- `src/Penelopa/ts/penelopa.ts:74-81`（指针坐标相对外层 `.penelopa-canvas` div）

**实测证据**

- 视口缩放/平移后再改变画布尺寸，`CurrentViewTransform` 从 `zoom=2, pan=(20,10)` 变为 `zoom=1, pan=(0,0)`；DPR 变化同样重置。
- DOM 实测：canvas 的 `clientWidth=743`、`getBoundingClientRect().width=745.21875`，`clientHeight=782`、`rect.height=784.421875`，CSS `border:1px`；渲染器使用内容/绘制缓冲尺寸，指针层却相对 border box 计算。结果是在画布中心点击会映射到世界 `(+1.11, -1.21)`。
- 同一次拖拽缩放的预期结果是 `140×100`，浏览器实际写入 `141.109×101.211`，与 1px border 引起的偏移量一致。

**影响**

调整面板/跨显示器移动会丢失用户精心设置的缩放和平移；指针命中在边缘和缩放手柄上偏移约 1 个世界单位，影响精度与手感。

**修复方向**

- 尺寸/DPR 变化时保留 `Zoom/Pan`，只更新 `ViewWidth/ViewHeight/DevicePixelRatio`；必要时围绕视口中心调整 pan。
- 指针层从 `canvas.clientWidth/clientHeight` 或内容区域计算，或者把监听器挂到内层 canvas 并去除 border；也可把 border 移到外层容器用 `outline`/`box-shadow`。
- 增加浏览器集成测试：缩放后 resize/DPR 变化、中心/边缘点击、手柄缩放。

---

### F-10 [P2] 文档反序列化缺少 ID/版本/规模校验，坏数据可崩溃

**位置**

- `src/Penelopa.Core/Services/DocumentSerializer.cs:12-16`（DTO 无 schema 版本）
- `src/Penelopa.Core/Services/DocumentSerializer.cs:104-114`（直接采用 JSON 中的 `Id`）
- `src/Penelopa/Components/Panels/ToolPanel.razor:130-131`（`ToDictionary(p => p.Id)`）
- `src/Penelopa/Components/Panels/ToolPanel.razor:107-136`（加载无确认、无规模限制）

**实测证据**

- 构造两个相同 `Id` 的图元并写入 `localStorage`，点击 Load：
  `System.ArgumentException: An item with the same key has already been added`，页面进入错误边界（截图 `21-duplicate-id-load.png`）。
- 反序列化阶段接受重复 ID；崩溃发生在 UI 的 `ToDictionary`，不在边界内。
- 文档没有版本字段、最大节点数/深度/字节数限制；极大嵌套或大量图元会直接阻塞 UI。

**影响**

手工编辑/旧版本/未来迁移/被污染的 `localStorage` 都可能让加载崩溃；未来格式变更无法迁移；加载会静默覆盖当前文档。

**修复方向**

- 增加 `Version` 字段与迁移链；未知版本明确拒绝并提示。
- 反序列化时校验：非空且唯一的 `Id`、父引用一致、属性类型、节点/深度/总字节上限。
- 在 `LoadDocument` 内统一 try/catch，失败时保留旧文档并显示可读错误。
- 加载前确认覆盖；保存前备份上一版本（例如 `penelopa.document.backup`）。
- 为重复 ID、空 ID、超深嵌套、超大文档、未知版本增加测试。

---

### F-11 [P2] 测试金字塔缺失：UI/TS/E2E 为零，关键渲染分支未覆盖

**位置**

- 测试项目：`tests/Penelopa.Core.Tests`、`tests/Penelopa.Rendering.Tests`
- CI：`.github/workflows/ci.yml:26-27`（只 build/test），无覆盖率、无浏览器/E2E
- 现有测试重点偏算法：`tests/Penelopa.Core.Tests/SetBoundsAndHitTests.cs:73-114`（三角形仅根级）、`tests/Penelopa.Core.Tests/ContainerTests.cs:76-84`（容器 SetBounds 仅 Offset 容器）
- 覆盖缺口示例：`src/Penelopa.Rendering/CanvasRenderer.cs:410-423`（容器绘制）、`531-561`（三角形绘制）、`79-157`（zoom/pan/fit/DPI）均未覆盖

**实测数字**

| 程序集 | 行覆盖 | 分支覆盖 | 方法覆盖 |
| --- | ---: | ---: | ---: |
| `Penelopa.Core` | 90.4% | 80.05% | 89.9% |
| `Penelopa.Rendering` | 72.72% | 81.25% | 79.06% |
| `Penelopa`（WASM） | 无自动化测试 | — | — |
| TypeScript 指针层 | 无自动化测试 | — | — |

- 133 个测试全部通过，但 F-01～F-06 均未被现有测试捕获。
- Core 的高覆盖集中在几何/控制器的正向路径；`PrimitiveService.Remove`（`37-51`）、`ReplaceAll` 清历史（`62-64`）、容器 `ToLocalBounds/ToWorldBounds`（`139-154`）、旋转容器 SetBounds 分支（`194-199`）等未覆盖。

**影响**

当前重构（尤其是 F-01/F-03/F-04）没有安全网；P0/P1 可以再次回归。UI/TS 是最容易出集成缺陷的层，却完全依赖人工验收。

**修复方向**

- 单元层：补齐容器/子图元 SetBounds、取消、选择规范化、历史覆盖、坏输入、重复 ID、首事件 wheel、DPR/resize 保留视图、小图元角柄。
- 组件层：引入 bUnit 测试面板的事件订阅、加载无选择、取消通知、属性编辑提交。
- 端到端：Playwright 覆盖本报告第 6.3 节的主流程，纳入 CI；至少 Chromium。
- 渲染层：容器/旋转容器/三角形/缩放的像素或颜色键断言；必要时黄金图像。
- 覆盖率门禁：Core ≥90% 行、Rendering ≥85% 行作为起点；新增代码要求覆盖。
- CI 增加 `npm run check:js`、覆盖率上传、E2E、`git diff --exit-code` 检查编译后的 JS 是否与 TS 同步。

---

### F-12 [P2] 固定 300px 双栏导致窄窗口画布不可用

**位置**

- `src/Penelopa/Components/PenelopaWorkspaceDefinition.cs:84-95`（左右各 300px 固定像素）
- 布局说明：`README.md:19`、`docs/en/architecture.md:72-77`

**实测宽度（画布可视宽度）**

| 视口宽度 | 画布宽度 |
| ---: | ---: |
| 1440 | 745px |
| 1280 | 585px |
| 1100 | 405px |
| 1024 | 329px |
| 900 | 205px |
| 800 | 105px |
| ≤700 | 13px |

- 在 700px 及以下，双 300px 侧栏占满宽度，画布被压到 13px；`documentElement.scrollWidth` 等于视口宽度，没有横向滚动可救（截图 `18-width-800.png`、`19-width-571.png`）。
- 1024px 笔记本分屏时画布只有约 329px，仍可用但明显狭窄。

**影响**

长期编辑器需要在 1366×768 笔记本、分屏、远程桌面、平板等场景使用；当前固定双 300px 没有响应式策略。不是当前 P0/P1，但会限制产品定位。

**修复方向**

- 为侧栏引入最小/最大宽度与响应式断点：窄屏折叠为图标/抽屉，或允许用户收起侧栏。
- 评估 Atlas 工作区是否支持百分比/最小宽度；在 `PenelopaWorkspaceDefinition` 中集中定义断点。
- 加窗口 resize 的 E2E 布局断言。

---

### F-13 [P2] 可访问性缺口：标签、画布键盘、ARIA、页面缩放

**位置**

- `src/Penelopa/Components/Panels/Props/FloatProp.razor:4-6`、`DoubleProp.razor:4-6`、`IntProp.razor:4-6`、`UintProp.razor:4-6`（`label` 无 `for`，`input` 无 `id`/`aria-label`）
- `src/Penelopa/Components/Panels/CanvasPanel.razor:15-19`（画布容器无 `tabindex`/`role`/`aria-label`）
- `src/Penelopa/Components/Panels/PrimitiveTreePanel.razor:73-80`（选中只用 CSS class，无 `aria-pressed`/`aria-current`）
- `src/Penelopa/Components/PenelopaStatusBar.razor:3-8`（无 `aria-live`）
- `src/Penelopa/wwwroot/index.html:6`（`maximum-scale=1.0, user-scalable=no`）

**实测证据**

- 属性面板 4 个输入：`id=null`、`aria-label=null`、`labels.length=0`；4 个 `label` 的 `htmlFor=null`、`control=false`。
- 画布 div 与内层 canvas：`tabIndex=-1`、无 `role`、无 `aria-label`。
- 树按钮无 `aria-pressed`/`aria-current`；状态栏无 `aria-live`。
- Tab 顺序只经过工具栏/树/属性等 DOM 控件，画布完全不可达。
- 页面禁用双指缩放（`user-scalable=no`）。

**影响**

键盘和读屏用户无法完成画布编辑；属性输入缺少可访问名称；选中状态对辅助技术不可见。对于“长期演进编辑器”，这是必须补齐的产品质量，而非可选项。

**修复方向**

- 为输入建立 `id`+`label for` 或 `aria-labelledby`；为动态属性生成稳定 id。
- 画布提供可聚焦入口与键盘操作（方向键移动、Shift+方向键缩放、Tab 切换选择、Delete 删除），或提供等价的表单/树操作路径；设置 `role` 与说明。
- 树按钮加 `aria-pressed` 或 `aria-current`；状态栏加 `aria-live="polite"`；为角柄/选择提供可访问说明。
- 允许浏览器缩放，移除 `maximum-scale=1/user-scalable=no`。
- 引入 axe/Playwright 可访问性检查；至少覆盖键盘主路径。

---

### F-14 [P2] DPR `matchMedia` 监听未释放

**位置**

- `src/Penelopa/ts/penelopa.ts:40-57`（`watchDpr` 返回 `void`，注册 `matchMedia` 监听）
- `src/Penelopa/ts/penelopa.ts:224-237`（`attachPointer` 返回 dispose，但 DPR 监听不在其中）
- `src/Penelopa/Components/Panels/CanvasPanel.razor:200-211`（仅释放 pointer handle 与 `DotNetObjectReference`）

**影响**

面板/工作区重建后，旧的 `matchMedia` 监听仍持有已释放的 `DotNetObjectReference`；DPR 变化时可能调用已释放对象并产生未处理 Promise rejection，同时造成监听泄漏。当前单面板场景不易触发，但长期演进（多文档、布局重建）会遇到。

**修复方向**

- `watchDpr` 返回 `{ dispose }`，在 `CanvasPanel.Dispose` 调用；监听回调中处理引用失效。
- 把 DPR 监听纳入统一的 JS 生命周期句柄，和 pointer layer 一起释放。

---

### F-15 [P2] 架构/README/构建注释与实现漂移；无 Node 构建文档

**位置与漂移点**

| 文档声明 | 实际实现 |
| --- | --- |
| `docs/en/architecture.md:29`：`Primitives/ColorKeyManager` 映射唯一键并从 `0xFF000001` 回绕 | 不存在 `ColorKeyManager`；键在 `CanvasRenderer` 每帧重建（`:22-27`、`:387-390`、`:447-452`），无回绕 |
| `docs/en/architecture.md:91`：滚轮围绕指针缩放 | `penelopa.ts:204-211` 要求按住 Ctrl 才缩放 |
| `docs/en/architecture.md:135-140`：快照包含“选择”，并在“每次结构变更与每次画布手势”前捕获 | `DocumentHistory.cs:8` 明确选择不捕获；对齐/属性编辑不捕获 |
| `docs/en/architecture.md:140`：应用快照“重新注册颜色键” | 颜色键是渲染器每帧临时映射，无持久注册 |
| `docs/en/architecture.md:165`：未来才做 hit-through 钻取 | 已实现 `PickAll` + 控制器钻取（`:253-285`） |
| `README.md:19`：top toolbar 和 status bar 作为功能 | 两个组件是静态占位（`PenelopaTopToolbar.razor:3-5`、`PenelopaStatusBar.razor:3-8`） |
| `README.md:40-49`：Requirements 只列 .NET 6 与 wasm-tools | 构建目标 `Penelopa.csproj:28-31` 无条件执行 `npm run build:js`，无 Node 会失败 |
| `Penelopa.csproj:22-27`：编译产物已提交，所以“无 node 也能构建” | 同上，`Exec` 没有 Node 可用性条件，注释与行为矛盾 |
| `app.css:471-474`：画布保留最小内容尺寸、出现滚动条 | `.penelopa-canvas` 的 `min-width/min-height` 为 0，实际由 `ViewTransform` 平移/缩放，无内容滚动条 |

**影响**

README 和 architecture 是这个项目最强的资产之一；漂移会让新维护者按错误不变量修改代码。尤其“选择被快照”“所有手势前捕获”与当前实现相反，会直接误导 F-03/F-07 的修复。

**修复方向**

- 修完 F-01/F-03/F-07 后同步更新 en/zh 文档，并在 PR 模板中要求文档影响检查（现有模板已有该字段，但需执行）。
- 为坐标空间、选择模型、历史事务各写一页 ADR；把“哪些操作进入历史、选择是否持久化、颜色键生命周期”写成可测试的规范。
- 在 README 的 Requirements 增加 Node/npm 或把 TypeScript 编译改为可选（有 Node 才编译，否则使用已提交 JS）。
- 清理/更新 CSS 注释；若将来恢复滚动，明确与 `ViewTransform` 的关系。

---

### F-16 [P3] 颜色键无回绕保护；缩放下坐标轴不覆盖视口

**位置**

- `src/Penelopa.Rendering/CanvasRenderer.cs:22-27`、`387-390`、`447-452`
- `src/Penelopa.Rendering/CanvasRenderer.cs:564-599`
- `docs/en/architecture.md:29-31`（声称回绕）

**问题**

- `_nextColorKey++` 没有检查 `0xFF000000` 回绕或上限；超过约 1670 万个图元后可能产生无效/冲突键。当前规模不现实，但与文档承诺不一致，且缺少防御。
- 坐标轴以 `halfW/halfH` 世界长度绘制；缩放/平移后轴线不再覆盖画布，箭头和 X/Y 标签可能被裁掉。

**修复方向**

- 定义键上限并显式处理（抛错/回绕并检测冲突）；更新文档。
- 坐标轴改为屏幕空间绘制或按 zoom/pan 计算可视范围；增加 zoom/pan 下的像素测试。

---

### F-17 [P3] 长期编辑器的能力缺口

**位置/现状**

- `PenelopaTopToolbar.razor`、`PenelopaStatusBar.razor` 静态占位；无菜单、无命令面板、无保存状态指示。
- 没有删除/重命名 UI；`IPrimitiveService.Remove` 未被 UI 使用。
- 没有“把已有图元放入容器/分组选择”的入口；容器只能先创建、再往其中添加新图元（`ToolPanel.razor:62-81`）。
- 多选时属性面板只显示第一个选中项（`PropertyPanel.razor:56-60`）；`StringPropValue` 没有对应编辑组件，`Name` 不可编辑。
- 无手动主题切换（只跟随 `prefers-color-scheme`）；无加载覆盖确认。

**影响**

这些不是当前缺陷，但与“长期演进的编辑器”定位不符；在 F-01～F-06 修复后，应把它们纳入产品路线图，而不是继续扩展图元类型。

---

### F-18 [P3] 封装与扩展性

**位置**

- `src/Penelopa.Core/Services/PrimitiveService.cs:53-56`、`112`（返回内部 `List`/`HashSet` 的 `IEnumerable`）
- `src/Penelopa.Core/Services/PrimitiveService.cs:87/97/103/109`（事件直接传内部可变集合）
- `src/Penelopa.Core/Services/PrimitiveService.cs:17-31`（Add/AddToContainer 无存在性/环路校验）
- `src/Penelopa.Core/Primitives/Container.cs:113-122`（AddChild 只检查 `Parent != null`，无法阻止把祖先加入后代）
- `src/Penelopa.Core/Primitives/PropValue.cs:19-28`（未知子类型返回 `null`，历史/序列化静默跳过）
- 新增图元需要同时修改渲染 switch、序列化 switch、属性面板 switch、颜色键逻辑

**影响**

调用方可以绕过历史/事件直接改集合；未来的分组/重排 API 很容易制造环或孤儿；新增属性/图元类型会静默丢数据或需要多点修改。

**修复方向**

- 返回 `IReadOnlyList`/`ReadOnlyCollection` 或不可变快照；事件传只读副本。
- `AddToContainer` 校验同一文档、非祖先、非自身；或把结构操作收敛到文档聚合根。
- 用类型注册表/访问者替代多处 `switch`；未知 `PropValue` 显式报错而不是静默跳过。
- 为扩展点写“新增一种图元/属性”的贡献指南与测试模板。

---

### F-19 [P3] 资源与流程：Bootstrap/图标未使用、发布体积、CI 门禁不足

**位置**

- `src/Penelopa/wwwroot/index.html:9`（完整 Bootstrap 159KB）
- `src/Penelopa/wwwroot/css/app.css:1`（Open Iconic 9.2KB + 字体）
- `src/Penelopa/wwwroot/index.html:22`（指针脚本）
- `.github/workflows/ci.yml`（无覆盖率/E2E/依赖审计）
- `.github/workflows/deploy-pages.yml:4`（仅手动触发）
- 无 `packages.lock.json`；CI 不检查编译后的 `penelopa.js` 是否与 TS 同步

**实测**

- 发布目录约 28MB，`dotnet.wasm` 6.82MB（`.br` 2.04MB）；Bootstrap/图标字体实际 UI 未使用（项目只用到少量 `form-control`/`form-group` 类）。
- `npm audit` 0 漏洞；`dotnet list package --vulnerable --include-transitive` 未发现已知公告。
- 部署 workflow 仅 `workflow_dispatch`，README 的 live demo 可能与 `main` 漂移。

**修复方向**

- 删除未使用的 Bootstrap/图标或改为最小 CSS；评估 IL linking/AOT 与 SkiaSharp 资产裁剪，设定发布体积预算。
- CI 增加覆盖率、E2E、`npm audit`/`dotnet list package --vulnerable`、NuGet lock、JS 漂移检查；部署改为 main 推送或明确发布流程。
- Node 20 的 EOL 见战略项。

---

## 4. 战略项（不混入 P0–P2 日常整改）

| 项 | 现状 | 风险 | 建议 |
| --- | --- | --- | --- |
| .NET | `global.json:2-4` 固定 SDK 6.0.428；目标框架 net6.0 | .NET 6 已于 2024-11 EOL，不再有安全更新；新工具链兼容性下降 | 做 net8 迁移 spike，先跑通 SkiaSharp WASM 原生资产、wasm-tools、Atlas；再分批迁移 |
| Node | `ci.yml:23/39`、`deploy-pages.yml:40` 使用 Node 20 | Node 20 已于 2026-04 EOL | 升到 Node 22/24，更新 npm lock 与 CI |
| SkiaSharp | `Directory.Packages.props:9-11` 锁定 2.88.9 | 原生库较旧；4.x 要求 net8+，与当前 net6 冲突 | 随 .NET 升级一起评估 3.x/4.x、WASM 资产与尺寸；不要单独升级到 4.x |
| Atlas.Blazor | 0.3.0 | 工作区 API/行为漂移，且是黑盒依赖 | 跟踪版本、加工作区集成测试；在升级时对照 `PenelopaWorkspaceDefinition` |
| VS 构建 | `Penelopa.csproj:39-66` 硬编码 6.0.36 的 Emscripten/Node/Mono/AOT 路径 | 换机器/升级 SDK/非 Windows 即失效；维护成本高 | 以 CLI 为官方构建路径；net8 后删除该块；文档写明 VS 例外 |
| 固定 300px 双栏 | `PenelopaWorkspaceDefinition.cs:84-95` | 窄屏不可用 | 与 F-12 一起做响应式产品决策 |

这些项不进入本报告的 P0–P2 排序；建议在 Phase A/B 稳定后单独立项，避免迁移与业务修复互相干扰。

---

## 5. 整改路线图

### Phase A：止血（P0/P1，约 1.5–2 周，单人含测试）

| 顺序 | 项 | 依赖 | 退出标准 |
| --- | --- | --- | --- |
| 1 | F-01 坐标空间与 `SetBounds` 契约 | 无 | 根/Offset/Rotation/Flip/嵌套 × 三种图元的 SetBounds、Cancel、返回框测试通过；容器内缩放不再错位 |
| 2 | F-02 属性输入校验 | 可与 1 并行 | 负数/零/NaN/Infinity 不触发渲染异常；UI 显示错误；加载同样校验 |
| 3 | F-03 事务/命令边界 + 历史覆盖 | 依赖 1 的恢复语义 | 对齐/属性编辑可撤销；输入框内 Ctrl+Z 不删对象；每个修改路径有回归测试 |
| 4 | F-04 选择规范化 | 可与 3 并行 | 父子不再同时参与拖拽/对齐；Ctrl 选择、SelectAll、树选择行为一致 |
| 5 | F-05 指针首事件 rect | 无 | 页面加载后首次 Ctrl+滚轮即可缩放，无 pageerror |
| 6 | F-06 小图元角柄命中区 | 可与 4 并行 | 10×10 圆/矩形可拖动、可钻取；缩放后仍可操作 |

**Phase A 完成定义**：F-01～F-06 全部有自动化回归测试；浏览器 E2E 覆盖对应路径；文档同步坐标/历史/选择语义。

### Phase B：结构与正确性护栏（P2，约 2 周）

| 顺序 | 项 | 依赖 | 退出标准 |
| --- | --- | --- | --- |
| 7 | F-07 单一状态源/事件统一 | 依赖 3、4 | 面板全部从 store 派生；加载/取消/撤销后无陈旧值；bUnit 覆盖 |
| 8 | F-09 视口保留 + 指针偏移 | 无 | resize/DPR 后 zoom/pan 保留；中心/边缘点击偏差 <0.1 世界单位 |
| 9 | F-10 持久化校验/版本/确认 | 依赖 7 | 重复 ID/坏数据不崩溃；schema 版本可迁移；加载有确认 |
| 10 | F-11 测试金字塔 | 持续 | 覆盖率门禁 + Playwright E2E 入 CI；渲染容器/旋转/三角形/缩放用例补齐 |
| 11 | F-08 渲染器拆分/性能 | 依赖 10 的基准 | IDisposable；500 矩形 Release <50% CPU、≥55fps；空文档空闲 <5% 或按需渲染 |
| 12 | F-14 DPR 监听释放 | 依赖 8 | 面板销毁/重建无监听泄漏、无已释放引用调用 |

### Phase C：体验与长期健康（P2/P3，约 1–2 周）

- F-12 响应式布局；F-13 可访问性；F-15 文档/构建说明同步；F-16 颜色键/坐标轴；F-17 能力缺口（删除/重命名/分组/多选属性）；F-18 封装与扩展点；F-19 资源/CI 流程。
- 每项独立 PR，先加测试再改行为；文档与 ADR 随代码提交。

### 战略项

- S-01 .NET 8 + SkiaSharp/Atlas/Node 迁移；单独 spike，不与 Phase A/B 混做。
- 迁移完成后再评估 AOT、IL linking、发布体积和是否保留 fixed dock 布局。

**路线图原则**：先“不再产生错误数据”（F-01/F-02/F-04），再“错误可撤销/可见”（F-03/F-07），最后“更快更可用”（F-08/F-12/F-13）。不要先拆渲染器或升级框架，否则会把正确性缺陷一起搬走。

---

## 6. 验证附录

### 6.1 门禁基线

环境：Windows 10.0.26300 x64，SDK 6.0.428，wasm-tools 6.0.36，Node 24.18.1（CI 为 Node 20），Chrome + Playwright 1.55（headless）。

| 命令 | 结果 |
| --- | --- |
| `dotnet restore Penelopa.sln` | 成功 |
| `dotnet build Penelopa.sln --no-restore` | 成功，0 警告/0 错误 |
| `dotnet test Penelopa.sln --no-build --no-restore` | 133 passed（Core 109 + Rendering 24），0 failed |
| `dotnet format Penelopa.sln --verify-no-changes --no-restore` | 通过 |
| `npm ci` | 成功，npm audit 0 漏洞 |
| `npm run check:js` | 通过（TypeScript 类型检查） |
| `dotnet list Penelopa.sln package --vulnerable --include-transitive` | 未发现已知漏洞公告 |
| `dotnet publish ... -c Release` | 成功；发布目录约 28MB；`dotnet.wasm` 6.82MB（br 2.04MB） |

覆盖率采用临时全局工具 `coverlet.console 6.0.2`，未修改仓库；命令示例：

```sh
~/.dotnet/tools/coverlet tests/Penelopa.Core.Tests/bin/Debug/net6.0/Penelopa.Core.Tests.dll \
  --target dotnet --targetargs "test tests/Penelopa.Core.Tests/Penelopa.Core.Tests.csproj --no-build --no-restore" \
  --format cobertura --output /tmp/core.xml
```

### 6.2 覆盖率

| 程序集 | 行 | 分支 | 方法 |
| --- | ---: | ---: | ---: |
| `Penelopa.Core` | 90.4% | 80.05% | 89.9% |
| `Penelopa.Rendering` | 72.72% | 81.25% | 79.06% |
| `Penelopa`（WASM） | 无自动化测试 | — | — |
| TypeScript 指针层 | 无自动化测试 | — | — |

未覆盖的高价值路径：

- `CanvasRenderer`：`ZoomAt/PanBy/FitToContent/SetDevicePixelRatio`（约 `79-157`）、容器 `DrawNode`（`410-423`）、三角形绘制（`531-561`）、选择覆盖层多选分支。
- `Container`：`ToLocalBounds/ToWorldBounds`（`139-154`）、旋转容器 `SetBoundsLocal`（`194-199`）、空容器/零缩放边界（`229-251`）。
- `PrimitiveService`：容器内 `Remove`（`37-46`）、`ReplaceAll` 清历史（`62-64`）、`NotifyPrimitivesChanged`（`115`）。
- 所有 WASM 组件、TS 指针层、浏览器交互。

### 6.3 浏览器实跑清单

环境：Chrome headless，1440×900，DPR 1，浅色主题；Release 发布产物用于性能测量。截图保存在审查临时目录（未提交仓库）。

| 主流程 | 结果 | 说明 |
| --- | --- | --- |
| 应用加载/画布渲染/坐标轴 | 通过 | SKGLView canvas；无控制台异常 |
| 添加 Circle/Rectangle/Triangle | 通过 | 树中出现 3 项 |
| 单选（画布/树） | 通过 | 属性面板同步 |
| Ctrl 多选 | 通过 | 选中 Circle+Triangle |
| 六向对齐 | 通过 | Left/H-Center/Right/Top/V-Center/Bottom 均得到预期坐标 |
| 普通图形拖拽 | 通过（大图形） | 100×80 矩形 +40/+30 正确；默认 10×10 失败（F-06） |
| 普通图形缩放 | 通过（大图形） | 140×100 目标实际 141.109×101.211（F-09 的 1px 偏移） |
| 拖拽/缩放撤销、重做 | 通过 | Ctrl+Z / Ctrl+Shift+Z 恢复几何 |
| 容器嵌套 | 通过 | Offset 容器 + 子图元，树层级正确 |
| 钻取（drill-down） | 通过（较大子图元） | 三角形子项：首击容器、再击子项；小圆容器被角柄阻断（F-06） |
| 父+子同选拖拽 | 失败 | 子项局部 +40、容器 +40，世界 +80（F-04） |
| 对齐后 Ctrl+Z | 失败 | 删除最近新增的三角形，而非撤销对齐（F-03） |
| 属性编辑后 Ctrl+Z | 失败 | 删除圆，而非撤销半径（F-03） |
| 输入框内 Ctrl+Z | 失败 | 删除图元，输入框文本撤销被阻止（F-03） |
| 负半径输入 | 失败 | 渲染循环未处理 ArgumentException（F-02） |
| 首次 Ctrl+滚轮 | 失败 | `rect` null，pageerror，缩放不生效（F-05） |
| 初始化后缩放/平移/Fit | 通过 | 截图与点击命中均变化；Fit 有效 |
| 保存/加载并恢复选择 | 通过 | 圆形选中状态按 Id 恢复 |
| 加载无选择文档 | 失败 | 树无选中、属性面板仍显示旧圆（F-07） |
| 深浅主题 | 通过 | `prefers-color-scheme` 切换，body 背景 `rgb(28,29,32)` |
| 响应式 | 失败 | 700px 以下画布仅 13px（F-12） |
| 可访问性 | 失败 | 输入无标签关联、画布不可聚焦、树无 ARIA（F-13） |
| 重复 Id 加载 | 失败 | `ArgumentException`，错误边界（F-10） |
| 500 个矩形/性能 | 失败（性能上限） | Release 99.8% CPU、79 rAF/s（F-08） |

### 6.4 关键复现基线

**F-01（核心层最小复现）**

```csharp
var c = Container.CreateOffset("c", 20f, 20f);
var t = new Triangle();
t.Vertex1X.Value = 50; t.Vertex1Y.Value = 50;
t.Vertex2X.Value = 70; t.Vertex2Y.Value = 50;
t.Vertex3X.Value = 60; t.Vertex3Y.Value = 60;
c.AddChild(t);

var actual = t.SetBounds(new Box(70, 70, 120, 100), new Point(70, 70));
// 期望 actual ≈ (70,70)-(120,100)
// 实际世界框 (20,10)-(70,40)
```

**F-01（返回契约）**

```csharp
var c = Container.CreateOffset("c", 20f, 20f);
var circle = new Circle { CenterX = { Value = 10 }, CenterY = { Value = 10 }, Radius = { Value = 5 } };
c.AddChild(circle);
var returned = circle.SetBounds(new Box(20, 20, 40, 40), new Point(20, 30));
// 世界框正确 (20,20)-(40,40)，但 returned = (40,40)-(60,60)
```

**F-02（浏览器）**

1. Add Circle → 在树中选中 → 属性面板 Radius 填 `-5` → Tab。
2. 预期：字段报错、画布不变；实际：控制台 `ArgumentException: minX cannot be greater than maxX`，调用栈指向 `CanvasRenderer.DrawSelectionOverlay`。

**F-04（浏览器）**

1. Add Offset → Add Triangle。
2. 在三角形中心连点两次钻取到三角形。
3. Ctrl+点击同一位置，确认选中 `[Offset Container, Triangle]`。
4. 拖拽 `+40` 世界单位：`Triangle.Vertex1X` 从 50→90，`Offset X` 从 20→60；子项世界 +80。

**F-05（浏览器）**

1. 刷新页面，不点击画布。
2. 在画布上 Ctrl+滚轮：pageerror `Cannot read properties of null (reading 'left')`，截图不变。
3. 先在画布点击一次，再 Ctrl+滚轮：缩放生效。

**F-10（浏览器/存储）**

```json
{"Primitives":[
  {"Id":"11111111-1111-1111-1111-111111111111","Type":"Rectangle","Props":{"Name":"A"}},
  {"Id":"11111111-1111-1111-1111-111111111111","Type":"Circle","Props":{"Name":"B"}}
],"SelectionIds":[]}
```

写入 `localStorage["penelopa.document"]` 后 Load：`ToolPanel.LoadDocument` 的 `ToDictionary` 抛 `ArgumentException`。

### 6.5 性能测量

Release 发布产物通过本地静态服务器提供；Chrome headless，1440×900；单次 5 秒采样，基于 CDP `Performance.getMetrics` 与 `requestAnimationFrame` 计数。结果为指示性数据，不是多机基准。

| 场景 | rAF/s | TaskDuration | ScriptDuration | JSHeap | 备注 |
| --- | ---: | ---: | ---: | ---: | --- |
| 空文档空闲（Release） | 145 | 28.4% | 24.3% | 4.7MB | 持续渲染循环 |
| 500 矩形空闲（Release） | 79 | 99.8% | 97.9% | 6.4MB | 加载约 1.2s |
| 空文档空闲（Debug） | 144 | 29.3% | 25.4% | 5.4MB | 与 Release 接近 |
| 500 矩形空闲（Debug） | 35 | 99.9% | 99.0% | 6.9MB | 调试构建更慢 |

500 个矩形是人为压力基线，不代表目标规模；但说明持续全量重绘 + 每帧分配的扩展性不足。

### 6.6 审查限制

- Atlas.Blazor 作为黑盒，未审计其内部实现、内存和可访问性；工作区行为只通过浏览器和公开 API 验证。
- 未做完整安全审计（渗透、CSP、供应链签名、恶意文档模糊测试）；未做完整 WCAG 审核，只覆盖可自动/静态检查的关键项。
- 浏览器验证在 Chrome headless + Windows 上完成，未覆盖 Firefox/Safari/移动端/触摸/真实 DPR 切换；性能不是多机基准。
- 覆盖率工具为临时全局工具，未纳入仓库；WASM/TS 无覆盖率数据。
- 未做长时间内存/句柄泄漏 soak、超大文档（>500 图元）和并发/多标签测试。
- 未检查 GitHub Pages 实际线上版本与当前 `main` 的差异；部署 workflow 为手动触发。

---

## 7. 结论

Penelopa 的**分层、工程卫生和算法测试**明显高于一般小型 Blazor 项目；它已经具备成为长期编辑器的基础。当前的问题不是“代码写得乱”，而是**几个关键不变量没有在类型/事务/测试层面被强制**：

- 局部/父级/世界坐标只在注释里约定，没有由 `SetBoundsLocal` 的签名和测试保证；
- 历史快照要求调用方记得 `Capture()`，没有命令边界；
- 选择集只是一个 `HashSet`，没有祖先/后代约束；
- UI 面板各自镜像状态，没有单一状态源；
- 指针层和渲染器承担了过多隐式生命周期与坐标转换。

这些正是 F-01～F-09 的共同根因。建议按 Phase A 先“止血”，再用 Phase B 的结构改造把这些不变量固化，最后才扩展图元、容器和 UI 能力。**在 F-01～F-06 修复并有 UI/E2E 回归测试之前，不建议新增图元类型或继续扩大容器/交互功能。**

---

*本报告只读生成；未修改源码、未提交、未推送。*
