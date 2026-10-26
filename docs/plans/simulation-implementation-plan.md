# 仿真动画 v1 实施计划

- 状态：已完成（Implemented）
- 日期：2026-10-25（计划）；2026-10-26（完成）
- 依据：[仿真动画 v1 需求](../requirements/simulation.md) / [ADR 0005](../adr/0005-simulation-keyframe-timeline.md)
- 目标：按可独立验证的阶段交付；每个阶段有明确的测试门禁，未过门禁不进入下一阶段。

## 0. 执行结果（2026-10-26）

- 阶段 1–6 全部完成；仿真页、Core 引擎、文档 v2、共享保存服务、时间轴/检视器、Save/Dormant UX、无障碍与性能门禁均已落地。
- 测试（2026-10-26 评审修复后）：386 Core + 46 Rendering + 50 UI = 482 .NET；24 JS；40 Playwright 非性能用例（5 项性能用例跳过）；5 项性能用例以 `PERF=1` 运行。
- 覆盖率：Core 93.38%，Rendering 91.91%；`Simulation/` 与 `DocumentSerializer` 文件级 ≥90% 门禁通过（Estimator 100%，DocumentSerializer 92.23%）；格式检查通过。
- 性能：500 图元 + 100 Active 轨道 59.9–60.2 tick/s / 59.9–60.0 实际 .NET frame/s / 59.9–60.1 paint/s；选中 200 关键帧轨道约 60 tick/s / 约 60 paint/s（59.79–60.18 / 59.76–60.04）；Stop 后空闲主线程 CPU 2.94–3.75%（低于 5% 门禁）；60 秒循环 JS heap +0.4–0.5 MiB（收紧到 8 MiB 阈值内）、DOM/监听器计数稳定。tick 是 rAF 尝试数，frame 是 `OnSimulationFrame` 执行数，paint 是 SKGLView 回调数。
- 评审修复（2026-10-26）：Dormant 保存确认与 Load 互斥并在确认时重算；路由退出同步 Stop 并通知编辑器刷新；`null` 图元/子节点与加载边界加固；Dormant 明细与逐条删除。对应回归测试见 `simulation-regression.spec.ts` 与 `ToolPanelTests`/`DocumentStorageTests`。
- 第二轮评审修复：加载时 Dormant/孤儿轨道先剪除再校验 Active；保留 store 强校验并修订 SIM-F-101；替换编辑页与仿真 E2E 固定等待；Estimator 补充分支测试并启用文件级覆盖率门禁；增加真实 paint 指标，播放时启用 SKGLView render loop（按需 invalidate 会合并到约 30 paint/s），停止后关闭；清理死代码并补 live region/marker 命中区；性能计数器改为仅 `PERF=1` 或设置 `__PENELOPA_PERF__` 时暴露。
- 实施偏差：
  - 页面文件命名为 `SimulationPage.razor`（避免与 Core `Simulation` 命名空间/类型在代码中混淆）；路由仍为 `/simulation`。
  - 自定义 cubic-bezier 的 4 个数值控制点直接放在 `SimulationKeyframeEditor` 中（未单独拆 `SimulationBezierEditor`），以保持草稿/错误状态与关键帧编辑一致。
  - 额外修复编辑页画布销毁顺序与 JS interop 拆卸期间的未处理异常（导航到仿真页后 Ctrl+Z/Y 不再产生 pageerror）。

## 1. 实施原则

1. **引擎与 UI 分离。** 求值、时间轴、校验、会话状态机、配置存储放在
   `Penelopa.Core`，不依赖 Blazor、SkiaSharp 或 JS；应用层只负责页面、
   帧时钟、渲染和交互。这样可以用假时钟做确定性单测，也避免重蹈
   `Core` 依赖 UI 的覆辙。
2. **不新增项目。** 仿真核心放在 `Penelopa.Core/Simulation/` 命名空间，
   沿用现有项目与锁文件，`DocumentSerializer` 可以直接引用核心模型。
3. **配置不可变、快照撤销。** 轨道与关键帧使用不可变记录；每次编辑创建
   新配置并复用未变轨道，配置撤销只需保存配置引用，避免逐帧/逐键深拷贝。
4. **预览隔离是硬约束。** 运行时会话临时改写属性；所有退出路径必须恢复
   基础值；Save 只在 Ready 可用。任何实现捷径都不得把中间帧写入文档。
5. **复用现有能力。** 画布复用 `CanvasRenderer` / `ScenePainter` /
   `ViewportController`；持久化复用 `DocumentSerializer` 与 localStorage；
   测试沿用 xUnit / bUnit / Playwright / 性能基准。
6. **先引擎后界面。** 先让时间语义、校验和恢复可被单测证明，再接入页面；
   不用 UI 调试核心数学。

## 2. 架构落点与数据流

### 2.1 分层

| 层 | 位置 | 职责 |
| --- | --- | --- |
| Core 仿真模型 | `src/Penelopa.Core/Simulation/` | 轨道、关键帧、插值、校验、时间轴、求值器、运行时状态机、配置存储、动画范围估算 |
| Core 持久化 | `src/Penelopa.Core/Services/DocumentSerializer.cs` | 文档 v2、可选 `simulation` 段、v1 兼容、加载警告 |
| 应用层 | `src/Penelopa.Web/` | `/simulation` 页面、组件、帧时钟、JS 互操作、共享保存服务 |
| 渲染层 | `src/Penelopa.Rendering/` | 只读渲染路径（跳过命中缓冲与选区覆盖层） |
| 测试 | `tests/` | Core 单测、Rendering 单测、bUnit、JS 单测、Playwright、性能 |

### 2.2 运行时数据流

```text
Editor top toolbar ──► /simulation page ──► local selection
                              │
                              ├─ SimulationConfigStore (singleton, keyed by Primitive.Id)
                              │        │ config Changed
                              │        ▼
                              └─► SimulationRuntime (Core)
                                       │ Advance(now) / Seek / Stop
                                       ▼
                              FloatPropValue temporary writes
                                       │
                    ┌──────────────────┴─────────────────┐
                    ▼                                    ▼
          SKGLView.Invalidate                20Hz readout publisher
          (render-only path)                 (time + track current values)
                    │                                    │
                    ▼                                    ▼
              read-only canvas                  transport / inspector
```

- **时钟**：页面在 Playing 时启动一个 JS `requestAnimationFrame` 循环；
  每次回调把 `performance.now()` 交给 `SimulationRuntime.Advance(nowSeconds)`。
  会话只在 Blazor 同步上下文推进；`OnPaintSurface` 只读取属性并绘制，
  不修改会话状态，避免 GL 渲染线程与 UI 线程竞争。
- **读写分离**：运行期直接写 `FloatPropValue.Value`，不发文档变更通知；
  画布在每帧回调后 `Invalidate()`；20Hz 读数通过独立发布器推给小组件，
  不让整页（含 500 节点树）按 60FPS 重渲染。
- **保存**：编辑页与仿真页共用一个 `DocumentStorage` 服务；保存时传入
  `SimulationConfigStore.ExportActive()`，Dormant 轨道排除并需要确认。

### 2.3 关键实现决策（计划采用）

1. **帧时钟用 JS rAF，而不是 C# 定时器。** 原因：与显示刷新同步、可在
   标签页隐藏时停止、时间戳单调、可控且可测。风险是 60 次/秒的 JS→.NET
   调用；在阶段 3 用 100 轨道做性能检查，若回调开销超预期，回退到
   `SKGLView.EnableRenderLoop` + 在 `OnPaintSurface` 中推进会话的方案。
2. **画布只读但不“盲切”。** 复用 `CanvasRenderer.FitToContent`，并新增
   **动画范围估算**：在进入 Ready、Stop 后或点击 Fit 时，按全局关键帧
   时间及相邻时间中点采样，临时写入求值结果、合并所有图元世界包围盒、
   再恢复基础值；用结果适配视口。否则单个图元从 10 移动到 100 时会飞出
   初始 Fit 范围。v1 不提供画布平移/缩放；Fit 按钮覆盖检查需求。
3. **时间轴用百分比布局。** 轨道与关键帧的横向位置按 `t / T` 的百分比
   定位，内容宽度用 CSS 缩放倍数扩展，外层横向滚动；这样无需先测量像素
   宽度即可适配。播放头使用覆盖在标尺上的原生 `<input type="range">`
   作为 scrubber，天然支持键盘；关键帧拖动用少量 JS 读取元素矩形并做
   pointer capture。
4. **本地选中不污染编辑器选区。** 页面进入时若编辑器恰好只有一个选中项，
   可以用它做初始选中（只读复制），否则从空选中开始；此后只改页面本地
   状态。
5. **配置保存在 Core，运行时放在 Core，应用层只做编排。** 配置存储是
   singleton；运行时每次进入页面创建，退出销毁。这样配置跨页面保留，
   运行时不会在编辑器页后台运行。
6. **持久化错误分级。** 几何/属性继续严格校验；`simulation` 段结构性
   非法时整段丢弃并警告，文档照常加载；指向不存在图元/非 Float 属性的
   轨道逐条剪除并计入警告。

## 3. 阶段拆分

### 阶段 1 — 仿真核心引擎（L）

**目标**：在没有 UI 的情况下证明时间语义、校验、配置编辑与恢复行为正确。

**新增文件**

- `src/Penelopa.Core/Simulation/SimulationLimits.cs`
- `src/Penelopa.Core/Simulation/SimulationPrimitiveIndex.cs`
- `src/Penelopa.Core/Simulation/SimulationConfig.cs`
- `src/Penelopa.Core/Simulation/SimulationTrack.cs`
- `src/Penelopa.Core/Simulation/SimulationKeyframe.cs`
- `src/Penelopa.Core/Simulation/InterpolationKind.cs`
- `src/Penelopa.Core/Simulation/CubicBezier.cs`
- `src/Penelopa.Core/Simulation/SimulationEvaluator.cs`
- `src/Penelopa.Core/Simulation/CubicBezierSolver.cs`
- `src/Penelopa.Core/Simulation/SimulationTimeline.cs`
- `src/Penelopa.Core/Simulation/SimulationValidator.cs`
- `src/Penelopa.Core/Simulation/SimulationRuntime.cs`
- `src/Penelopa.Core/Simulation/SimulationReadout.cs`
- `src/Penelopa.Core/Simulation/SimulationConfigStore.cs`
- `src/Penelopa.Core/Simulation/SimulationBoundsEstimator.cs`

**修改文件**

- `src/Penelopa.Core/Primitives/PropValue.cs`：给 `FloatPropValue` 增加
  `ValidateValue(float value, out string? error)`，用于不修改当前值地校验
  关键帧端值。

**工作项**

1. **模型与限制**：不可变 `SimulationConfig`（Loop + Tracks）、
   `SimulationTrack`（PrimitiveId + 属性名 + 关键帧数组）、
   `SimulationKeyframe`（时间、值、插值、可选贝塞尔控制点）；
   `SimulationLimits` 集中定义时间、值、数量与撤销深度。
2. **求值器**：首尾保持、区间归一化、`step`、四种缓动预设、
   cubic-bezier 求解（二分求解、端点精确、控制点在 `[0,1]`）。
3. **时间轴**：`T` 计算、循环取模、夹取、帧步进边界；保证给定 `t`
   的结果与帧率无关。
4. **校验器**：结构校验（时间、值、插值、贝塞尔、数量）、目标解析
   （图元存在且属性为 Float）、播放前校验；区分可修复错误与 Dormant。
5. **运行时**：基础值快照（遍历所有 Float 属性）、Active/Dormant 解析、
   Ready/Preview/Playing/Paused/Finished 状态机、`Advance/Seek/Step/Stop`、
   `UpdateConfig`（暂停编辑后重算、`T` 变化夹取、无活动轨道回 Ready）、
   只读 `SimulationReadout`、异常安全恢复。
6. **配置存储**：轨道增删、关键帧增改删、Loop 设置（不作为配置快速撤销
   项）、每 `(Id, 属性)` 唯一、编辑校验、50 步配置撤销/重做、
   `ExportActive()`（排除 Dormant）、`ReplaceConfig`（加载时清空撤销）。
7. **动画范围估算**：采样并临时写入/恢复，返回所有图元世界包围盒并集；
   用于页面 Fit；保证不产生文档通知、历史条目或残留属性值。

**测试**

- `tests/Penelopa.Core.Tests/Simulation/SpecificationEvaluationTests.cs`
  （首尾保持、线性、四种缓动、step、贝塞尔端点/单调/非法控制点）。
- `SimulationTimelineTests.cs`（T、循环、夹取、1/60 步进）。
- `SimulationRuntimeTests.cs`（状态机、基础值精确恢复、无历史条目、
  Dormant、UpdateConfig、异常恢复、给定 t 确定性）。
- `SimulationConfigStoreTests.cs`（唯一轨道、排序/间隔、50 步撤销、
  ExportActive/Dormant、加载替换）。
- `SimulationBoundsEstimatorTests.cs`（覆盖端点/容器旋转、恢复无残留）。
- `SimulationValidationTests.cs`（值约束、时间、贝塞尔、限制）。
- `PropertyValidationTests.cs` 增补 `FloatPropValue.ValidateValue`。

**门禁**

```sh
dotnet test tests/Penelopa.Core.Tests/Penelopa.Core.Tests.csproj
dotnet format Penelopa.sln --verify-no-changes --no-restore
```

新增 Core 代码行覆盖率 ≥90%；所有恢复路径逐值断言；配置快照复用未变轨道
（可用引用相等断言验证）。

---

### 阶段 2 — 持久化与编辑页 Save/Load 集成（M）

**目标**：文档格式升到 v2，仿真配置可保存/加载，编辑页 Save/Load 不回退。

**修改/新增文件**

- `src/Penelopa.Core/Services/DocumentSerializer.cs`：v2 DTO、可选
  `simulation` 段、v1 兼容、加载警告、限制校验。
- `src/Penelopa.Web/Services/DocumentStorage.cs`（新）：从 ToolPanel 提取
  localStorage Save/Load、备份、异常包装。
- `src/Penelopa.Web/Program.cs`：注册 `SimulationConfigStore`（singleton）与
  `DocumentStorage`。
- `src/Penelopa.Web/Components/Panels/ToolPanel.razor`：改用共享保存服务；
  保存时包含 Active 配置；Dormant 保存确认；加载替换配置并显示警告。
- `tests/Penelopa.Core.Tests/DocumentSerializerTests.cs`：v2 与容错用例。
- `tests/Penelopa.Web.Tests/ToolPanelTests.cs`：注册新服务、更新断言。

**工作项**

1. **序列化 DTO**：`SimulationDto` / `TrackDto` / `KeyframeDto`；
   保持现有 PascalCase 风格；关键帧时间用 double、值用 float。
2. **版本与兼容**：`CurrentVersion = 2`；v1 读取为无仿真；版本 >2
   继续拒绝；总字节上限沿用 4MB。
3. **加载容错**：先按现有严格规则解析图元；再解析 `simulation`。
   - 结构非法（未知插值、非法时间/值/贝塞尔、超限）→ 整段丢弃 + 警告。
   - 目标图元不存在 / 属性不是 Float → 逐轨剪除 + 警告。
   - 文档始终返回，除非图元本身无效。
4. **保存**：`DocumentSerializer.Serialize` 增加可选
   `SimulationConfig? simulation = null`，旧调用保持编译；导出顺序按
   文档遍历顺序 + 属性声明顺序，保证稳定 JSON。
5. **共享存储服务**：`DocumentStorage.SaveAsync` / `LoadAsync`；
   localStorage 异常返回失败结果；备份逻辑保持不变。
6. **ToolPanel 集成**：
   - Save 传入 `SimulationConfigStore.ExportActive()`。
   - 若存在 Dormant，显示确认；取消则不写。
   - Load 确认文案说明“文档和仿真配置都会被替换”；确认后
     `ReplaceConfig` 并清空配置撤销；显示加载警告。
7. **注册服务**：`IPrimitiveService` 仍为 singleton；配置存储注入它；
   bUnit 测试同步注册。

**门禁**

```sh
dotnet test Penelopa.sln --no-restore
```

v1 JSON 仍能加载；v2 往返稳定；Dormant 不写入；非法仿真段不阻塞文档；
ToolPanel 既有 Save/Load/Group/Delete 测试全绿。

---

### 阶段 3 — 页面骨架、只读画布、帧时钟与 Fit（M/L）

**目标**：`/simulation` 可进入/退出，Ready 显示基础布局，Preview 能按 t
求值渲染，帧时钟可启动/停止并恢复。

**新增文件**

- `src/Penelopa.Web/Pages/Simulation.razor`（`@page "/simulation"`，含 `<h1>`）
- `src/Penelopa.Web/Components/Simulation/SimulationTree.razor`
- `src/Penelopa.Web/Components/Simulation/SimulationCanvas.razor`
- `src/Penelopa.Web/Components/Simulation/SimulationTransport.razor`（骨架）
- `src/Penelopa.Web/Components/Simulation/SimulationInspector.razor`（骨架）
- `src/Penelopa.Web/Components/Simulation/SimulationTimeline.razor`（骨架）
- `src/Penelopa.Web/wwwroot/css/simulation.css`
- `tests/Penelopa.Web.Tests/Simulation/SimulationPageShellTests.cs`
- `tests/e2e/tests/simulation.spec.ts`（首个 smoke 用例）

**修改文件**

- `src/Penelopa.Web/Components/PenelopaTopToolbar.razor`：增加 Simulate 入口。
- `src/Penelopa.Web/wwwroot/index.html`：引入 `simulation.css`。
- `src/Penelopa.Rendering/CanvasRenderer.cs` / `ScenePainter.cs`：增加
  render-only 路径（可选参数或新方法），跳过命中缓冲/选区覆盖层。
- `src/Penelopa.Web/ts/penelopa.ts` 与 `wwwroot/js/penelopa.js`：新增
  `startSimulationLoop`、可见性通知、`getElementRect`、
  `prefersReducedMotion`；同步编译并提交 JS。
- `tests/js/penelopa.test.mjs`：扩展 vm harness 测帧循环与可见性。
- `tests/Penelopa.Rendering.Tests/`：render-only 用例。

**工作项**

1. **路由与入口**：顶栏 `<a href="/simulation">Simulate</a>`；仿真页
   Back to editor；`h1` 供 `FocusOnNavigate` 聚焦；返回后把焦点还给
   Simulate 入口（`FocusOnNavigate` + `h1 tabindex="-1"`）。
2. **布局与样式**：顶部传输条、左侧树、中间画布、右侧检视器、底部时间轴；
   使用 CSS Grid/Flex 填满 `MainLayout`，在 1024/1440 下可用；空状态
   （无图元、无选中、无轨道）。
3. **本地选择**：只读递归树；不调用编辑器选区服务；徽标预留给阶段 4。
4. **画布**：复用 `CanvasRenderer`；新组件用 `SKGLView`
   (`EnableRenderLoop=false`)；首帧/文档替换时按动画范围估算 Fit；
   提供 Fit 按钮；无点选、拖拽、缩放手柄；`role="img"` + 描述。
5. **时钟与会话接线**：页面创建 `SimulationRuntime`；Play/Seek/Step 调用
   运行时；JS 帧循环回调 `Advance`；Pause/Stop/Finished/隐藏标签页停止
   循环；`DisposeAsync` 恢复基础值并释放 JS 句柄/DotNet 引用。
6. **渲染路径**：仿真画布调用 render-only，不重建命中缓冲；现有编辑页
   路径保持原签名/行为。
7. **动画范围 Fit**：进入 Ready、Stop 后、点击 Fit 时调用
   `SimulationBoundsEstimator`；若处于暂停会话，Fit 后重新应用当前帧。
   Playing 中禁用 Fit（或先 Pause），估算器不得与帧循环并发写属性。

**门禁**

```sh
dotnet test Penelopa.sln --no-restore
npm run check:js
npm run test:js
cd tests/e2e && npx playwright test --grep "simulation smoke"
```

页面可进入/返回；Ready 画布与编辑器基础布局一致；帧循环启停后属性精确
恢复；无控制台错误；render-only 不污染命中缓冲。

---

### 阶段 4 — 时间轴、检视器与关键帧编辑（L）

**目标**：完成可调试的编辑闭环：建轨、加帧、改值、拖时间、定位、读数、
本地撤销。

**新增 / 完善文件**

- `src/Penelopa.Web/Components/Simulation/SimulationInspector.razor`（完善）
- `src/Penelopa.Web/Components/Simulation/SimulationTrackList.razor`（新增）
- `src/Penelopa.Web/Components/Simulation/SimulationKeyframeEditor.razor`（新增）
- `src/Penelopa.Web/Components/Simulation/SimulationBezierEditor.razor`（新增）
- `src/Penelopa.Web/Components/Simulation/SimulationTimeline.razor`（完善）
- `src/Penelopa.Web/Services/SimulationReadoutPublisher.cs`（新增）
- `tests/Penelopa.Web.Tests/Simulation/SimulationInspectorTests.cs`（新增）
- `tests/Penelopa.Web.Tests/Simulation/SimulationTimelineTests.cs`（新增）

**工作项**

1. **检视器**：所选图元全部 Float 属性；未绑定 Animate；已绑定轨道显示
   当前值、关键帧列表、Add keyframe、Remove track。
2. **轨创建与编辑**：调用配置存储创建两帧（播放头、+1s，夹取/回退规则）；
   Add keyframe 捕获当前求值；时间/值/插值/贝塞尔编辑；最后一帧插值禁用。
3. **校验与错误**：值走 `FloatPropValue.ValidateValue`；时间 1ms、严格
   递增、最小间隔；冲突就地报错；错误关联 `aria-describedby`。
4. **时间轴**：
   - 仅当前选中图元轨道；左侧属性名，右侧百分比定位关键帧。
   - 标尺 + 原生 range scrubber + 视觉播放头；Playing 中 seek 先暂停。
   - 关键帧按钮可选择；Go to（按钮/双击）；拖动重定时（JS 矩形 +
     pointer capture、吸附 1/60、夹取、Esc 取消、一次拖动一条撤销）。
   - 缩放倍数 + 横向滚动；刻度按 T/缩放自适应。
5. **实时读数**：20Hz 发布器；时间显示 3 位小数；轨道 Current 值；
   暂停/定位/编辑后立即刷新；不触发整页 60FPS 重渲染。
6. **配置撤销**：按钮 + Ctrl+Z/Ctrl+Y（输入框内不劫持）；Playing 中禁用；
   Preview/Paused/Finished 中撤销或重做后运行时立即按当前 t 重算；
   进入页面/加载文档时清空。
7. **Dormant 展示**：横幅、明细、逐条/全部清理；清理作为配置编辑可撤销，
   Playing 中禁用。Playing 中不得修改任何配置。
8. **状态隔离**：Playing 只读；Ready/Preview/Paused/Finished 可编辑；
   编辑后 T 变化时夹取播放头并按状态规则处理。

**门禁**

```sh
dotnet test Penelopa.sln --no-restore
cd tests/e2e && npx playwright test --grep "simulation editing"
```

验收项 #1、#4、#5、#6 通过；20Hz 读数不引起画布掉帧；拖动/键盘两条路径
都能修改关键帧；配置撤销不影响文档历史。

---

### 阶段 5 — 播放状态闭环、保存/加载与 Dormant UX（M）

**目标**：完成 Play/Pause/Resume/Stop/Replay、Loop/Finished、Save 仅
Ready、Dormant 保存确认与加载警告。

**修改文件**

- `src/Penelopa.Web/Pages/Simulation.razor`：会话编排、Save/Load/Dormant 确认。
- `src/Penelopa.Web/Components/Simulation/SimulationTransport.razor`：完整传输条。
- `src/Penelopa.Web/Components/Panels/ToolPanel.razor`：Dormant 保存确认与加载警告联动。
- `tests/e2e/tests/simulation.spec.ts`：完整播放/保存/Dormant 流程。

**工作项**

1. **传输条完整行为**：
   - Ready → Play（建立会话、从 0）；Preview/Paused → Resume；
     Finished → Replay（从 0）；Playing → Pause；Stop 始终恢复基础值。
   - Loop 开关持久化；Playing 中切换立即生效；Loop 关闭到 T 进入 Finished。
   - 隐藏标签页自动 Pause；返回保持 Paused。
2. **错误与恢复**：播放前校验 Active 轨道，失败阻止并显示行/汇总错误；
   运行期异常停止、恢复、提示。
3. **Save**：仿真页 Save 仅 Ready；共享 `DocumentStorage`；Dormant 存在时
   确认“不会保存这些轨道”；保存失败显示错误。
4. **Load 联动**：ToolPanel 确认后替换配置、清空配置撤销、显示警告；
   无 `simulation` 段时清空配置。
5. **删除/撤销联动**：编辑器删除图元 → Dormant；撤销恢复 → Active；
   加载外部文档 → 剪除孤儿并警告。
6. **E2E 全流程**：创建轨道 → 编辑 → 播放/暂停/停止恢复 → 保存 → 刷新
   → Load → 复现；Dormant 场景。

**门禁**

```sh
dotnet test Penelopa.sln --no-restore
cd tests/e2e && npx playwright test --grep "simulation"
```

验收项 #2、#3、#7、#8 通过；所有退出路径恢复基础值；保存负载不含 Dormant；
v1/v2 加载行为符合需求。

---

### 阶段 6 — 无障碍、性能、回归与文档（M）

**工作项**

1. **无障碍**：键盘完整路径、焦点进入/返回、`aria-live=polite` 状态与错误、
   `role=img` 画布、20Hz 读数不播报、reduced-motion 提示、对比度。
2. **性能**：
   - 500 图元 + 100 Active 轨道，5 秒平均 ≥55 tick/s 且 ≥55 paint/s；60 秒循环无持续内存增长；
     Ready/Stop 空闲 CPU 接近零。
   - 检查 render-only、20Hz 发布器、二分查找、配置快照结构共享。
   - 若 rAF 互操作超预算，执行计划 2.3 的回退方案并记录决策。实际执行：播放期间启用 SKGLView render loop，真实 paint 由约 30/s 提升到约 60/s；其余状态仍按需渲染。
3. **响应式**：1440×900 与 1024×768 主验收；更窄允许滚动/折叠。
4. **回归**：编辑页全部既有测试；v1 文档兼容；格式检查；覆盖率。
5. **文档**：更新 README/README.zh 中“顶栏为静态装饰”的描述；在架构文档
   增加仿真分层与数据流；需求文档状态改为已实现；ADR 保持与实现一致。
6. **最终验收**：逐条核对需求第 5 节验收标准，记录结果。

**门禁**

```sh
dotnet test Penelopa.sln --collect:"XPlat Code Coverage" --results-directory artifacts/coverage
python3 scripts/check-coverage.py
dotnet format Penelopa.sln --verify-no-changes --no-restore
npm run check:js && npm run test:js
npm run test:e2e
cd tests/e2e && PERF=1 npx playwright test tests/simulation-perf.spec.ts
```

新增 Core 覆盖率 ≥90%；Rendering 若改动保持 ≥85%；无未解决的控制台错误；
需求验收全部通过。

## 4. 依赖与关键路径

```text
阶段 1 (Core)
   ├──► 阶段 2 (持久化) ──┐
   └──► 阶段 3 (页面/时钟) ┴──► 阶段 4 (编辑) ──► 阶段 5 (播放/保存) ──► 阶段 6 (硬化)
```

- 阶段 2 与阶段 3 可在阶段 1 后并行；两者都改 `Program.cs`，DI 变更应
  由同一人/同一小批次合并，避免注册冲突。
- 阶段 4 依赖阶段 3 的画布/时钟与阶段 2 的配置存储；阶段 5 依赖阶段 4
  的编辑和阶段 2 的 Save/Load。
- 动画范围估算在阶段 1，Fit 集成在阶段 3；不要等到阶段 6 才发现移动图元
  飞出视口。

## 5. 测试矩阵

| 层 | 重点 | 入口 |
| --- | --- | --- |
| Core 单测 | 求值、时间轴、状态机、校验、配置存储、恢复、序列化 | `tests/Penelopa.Core.Tests` |
| Rendering 单测 | render-only 不污染命中缓冲；可见绘制不变 | `tests/Penelopa.Rendering.Tests` |
| JS 单测 | 帧循环启停/错误上报、可见性、矩形测量、存储帮助函数、减少动态 | `tests/js/penelopa.test.mjs` |
| bUnit | 传输条禁用态、检视器编辑、时间轴 a11y、Dormant 横幅、Save 仅 Ready | `tests/Penelopa.Web.Tests` |
| Playwright | 进入/返回、建轨、播放/暂停/停止、定位/步进、Loop/Finished、保存/加载、Dormant；浏览器后退、焦点、刻度/双击、选择漂移、坏文档、安全错误摘要 | `tests/e2e/tests/simulation.spec.ts`、`tests/e2e/tests/simulation-regression.spec.ts` |
| 性能 | 500 图元 + 100 轨道 ≥55 tick/paint、选中 200 关键帧、内存、空闲 CPU | `tests/e2e/tests/simulation-perf.spec.ts` |

测试数据：

- v2 文档 seed（500 图元 + 100 轨道）通过 localStorage 写入，再在编辑页
  Load，随后进入 `/simulation`；避免用 UI 逐条创建。
- E2E 优先断言 DOM 读数、状态文本、按钮禁用态；Skia/WebGL 像素只做
  smoke 截图，不做易碎的像素级断言。
- 帧率用 `requestAnimationFrame` 计数 + CDP `TaskDuration` 双指标；
  100 轨道用例在 `PERF=1` 下运行。

## 6. 性能与内存策略

- **求值**：缓存 Active 轨道解析结果；每帧只写有轨道覆盖的属性，并在
  轨道删除时把该属性写回基础值；关键帧查找用二分。
- **UI**：60FPS 只更新画布；20Hz 读数通过 `SimulationReadoutChannel`
  推给时间/数值小组件，避免树/时间轴整页重渲染。
- **渲染**：仿真只走 draw canvas；不注册命中键、不画选区、不重建命中
  位图；Fit 只在需要时调用。
- **配置快照**：不可变轨道数组共享；一次编辑只复制被修改轨道的关键帧
  数组；撤销上限 50。
- **Fit 估算**：只采样全局关键帧时间及相邻中点（上限固定，如 512 个
  采样点）；当前实现只在 Ready 配置变化与显式 Fit 时重算，未按配置/文档版本缓存（10k 图元基准后再决定）。
- **回退**：若 rAF 互操作无法稳定 ≥55 paint/s，切换到 SKGLView render loop（已执行：播放时启用，停止后关闭）
  驱动，保持运行时与 UI 读数节流不变。

## 7. 风险与缓解

| 风险 | 影响 | 缓解 |
| --- | --- | --- |
| 60FPS JS→.NET 互操作开销 | 掉帧、CPU 高 | 阶段 3 早测；回调只做推进/失效；必要时回退 render loop |
| GL 线程与 Blazor 状态竞争 | 数据竞争/闪烁 | 会话只在 UI 回调推进；`OnPaintSurface` 只读绘制 |
| Renderer 改动破坏命中测试 | 编辑页回归 | 新增 render-only 重载；保留原路径；既有 Rendering 测试全跑 |
| 序列化 v2 破坏 v1 保存/加载 | 数据不可用 | 可选参数、v1 兼容测试、加载容错分级 |
| 时间轴拖拽/指针捕获 | 交互不稳、不可键盘操作 | 百分比布局 + 原生 range scrubber；检视器表格作为键盘路径；JS 测拖拽 |
| 动画移出视口 | 验证时看不到图元 | 动画范围估算 + Fit；阶段 3 验收单独覆盖 |
| Dormant 保存导致数据丢失 | 用户配置丢失 | 保存前明确确认；不写 Dormant；加载警告；E2E 覆盖 |
| bUnit 无法渲染 SKGLView | UI 测试受限 | 逻辑下沉到页面控制器/子组件；画布由 E2E 覆盖 |
| 既有“静态顶栏”文档/测试 | 文档失真、测试失败 | 阶段 3/6 更新 README、架构说明与相关断言 |
| 撤销快照占用大 | 内存增长 | 不可变结构共享；50 步上限；10k 关键帧上限 |
| 100 轨道性能不达标 | 验收失败 | 阶段 3/4 提前基准；render-only；20Hz 读数；必要时降采样读数 |

## 8. 需要确认的实现层默认值

以下不改变已确认的产品需求，但会影响实现，建议按推荐值执行：

1. **动画范围 Fit + Fit 按钮**（推荐）。v1 不提供画布平移/缩放；若你更
   希望只读画布可平移缩放，需要增加指针导航层与测试。
2. **JS rAF 帧时钟**（推荐）。若性能检查失败，回退 SKGLView render loop，
   但仍是同一套 Core 运行时。
3. **初始选中**：若编辑器当前只有一个选中图元，仿真页用它作为初始本地
   选中，否则为空；不修改编辑器选区。
4. **播放头**：原生 range scrubber + 视觉播放头；关键帧拖动用 JS 矩形
   测量与 pointer capture。
5. **保存服务抽取**：`DocumentStorage` 同时服务 ToolPanel 与仿真页；
   Dormant 保存在两处都走确认。
6. **仿真核心不新增项目**：放 `Penelopa.Core/Simulation`，保持
   `DocumentSerializer` 可引用。

## 9. 完成定义

- 需求第 5 节 10 条验收全部通过。
- Core 覆盖率 ≥90%，`Simulation/` 与 `DocumentSerializer` 文件级 ≥90%，Rendering（若改动）≥85%，格式检查通过。
- 单元、bUnit、JS、E2E、性能测试全部通过且无已知 flaky。
- 500 图元 + 100 轨道平均 ≥55 tick 与 ≥55 真实 paint/s；暂停/停止后空闲
  CPU 接近零；60 秒循环无持续内存增长。
- 键盘可完成需求列出的完整路径；状态播报不包含 20Hz 数值刷屏。
- 文档、README、架构说明与 ADR 与实现一致；需求状态更新为已实现。
