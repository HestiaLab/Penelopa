# Penelopa 仿真动画 v1 代码审查报告

> **历史注记（2026-10-27）：** 本报告是 2026-10-26 基线的只读审查记录。
> 文中“v1 兼容/可读”的结论已被
> [ADR 0008](../adr/0008-v2-only-documents.md) 取代：当前应用只接受 v2 文档，
> 并对 v1 明确拒绝。测试计数与性能数字也是当时基线的快照。

| 项目 | 内容 |
| --- | --- |
| 报告日期 | 2026-10-26 |
| 审查基线 | 工作树未提交变更，`main@8a189e9`；21 个已跟踪文件修改（+1400/−107）及新增仿真文件 |
| 审查范围 | Core 仿真引擎、文档 v2 与共享存储、Rendering 只读路径、`/simulation` 页面与组件、TS/JS 互操作、单测/bUnit/JS/E2E/性能、需求/ADR/计划/架构/README |
| 参考审查 | `docs/reviews/2026-10-25-architecture-code-review.md` |
| 审查方式 | 静态阅读 + 全量门禁执行 + Chrome 定向复现 + 覆盖率实跑 |
| 变更情况 | 只读审查；除本报告外未修改源码、未提交、未推送、未创建 PR。旧覆盖率报告已移入 `artifacts/coverage-review-old-20261026/`，审查产生的 `artifacts/coverage` 保留 |
| 环境 | Windows 10.0.26300 x64；.NET SDK 6.0.428；Node 24.18.0；Playwright 1.64.1 headless Chromium（`--enable-unsafe-swiftshader`）；Debug 构建；1440×900（布局另测 1024×768） |
| 说明 | 需求文档中的测试、覆盖率与性能数字均按“待验证声明”处理；下文数字全部来自本次命令或浏览器复现。`[验]` = 实际命令/浏览器验证，`[码]` = 代码推断，`[未]` = 未验证 |

---

## 0. 结论

**结论：阻塞（不可合并/发布）。** 本次实现的仿真核心、预览隔离主路径、时间语义和测试门禁整体质量高，实测门禁全绿且文档中的测试/覆盖率/性能数字基本可复现；但审查发现 **1 个 P0（动画配置数据丢失）、3 个 P1**，其中 P0 位于本次新增的 Dormant 保存确认与编辑器 Load 的交叉路径，属于 Save/Load 核心流程的静默数据丢失。

| 级别 | 数量 | 摘要 |
| --- | ---: | --- |
| P0 | 1 | 编辑器 Dormant 保存确认在 Load 后仍存活；“Save active only”把旧/空仿真配置覆盖到刚加载的文档 |
| P1 | 3 | 浏览器后退离开仿真页时编辑器属性面板/画布停留在动画值；含 `Primitives:[null]` 的文档加载崩溃；Dormant 明细与逐条删除未实现 |
| P2 | 11 | 20Hz 读数重渲染整块检视器/时间轴（200 关键帧选中时 tick 57.25）；“相对父级”未标注；刻度点击/双击 Go to 缺失；进入页焦点未到 h1；关键帧索引漂移；E2E 无 `console.error` 断言且漏关键路径；性能指标/覆盖率门禁证据不足等 |
| P3 | 10 | 预览主按钮标签、`role=alert`、保存失败泄漏 JS 堆栈、仿真页 pending 横幅陈旧、死代码/测试钩子、Fit 不缓存、文档小错等 |

**发布风险摘要**

- 功能主路径（建轨、编辑、播放/暂停/停止、Loop/Finished/Replay、保存重载、Dormant 横幅与清理）是可用的，E2E 30 项、性能 4 项、458 个 .NET 测试、21 个 JS 测试全部通过。
- 但 **“仿真只做预览、不污染文档”** 的承诺在 **浏览器后退** 路径上只对模型成立，对编辑器 UI 不成立；用户会看到 57.484 而模型实际是 10，重新选择后才恢复。若用户据显示值继续编辑，可能把动画中间值写回文档。
- **测试全绿不能证明 Save/Load 状态机正确**：P0 的场景没有任何测试覆盖；E2E 只监听 `pageerror`，而 `Primitives:[null]` 的崩溃只产生 `console.error` + 错误边界，正好落入假绿盲区。
- **Dormant 能力缺口**：需求与计划承诺“明细入口 + 逐条删除”，实现只有数量 + 全部删除；这不阻断主流程，但直接违反 SIM-F-081。

**如果只能先做三件事**

1. 修 **S-01（P0）**：Load/Cancel/Confirm 时彻底清除 Dormant 保存确认，或改为在确认时重新计算当前文档的 active 配置；绝不能用“旧配置快照 + 新文档”保存。
2. 修 **S-02（P1）**：路由退出时在页面初始化前同步恢复基础值，并通知编辑器刷新；补 `page.goBack()` 的 E2E。
3. 修 **S-03（P1）**：文档反序列化对数组元素 null 做防御，并让 Load 边界永不把未处理异常抛到渲染器；E2E 增加 `console.error` 与错误边界断言。

**不应破坏的优点**

- Core 仿真引擎与 Blazor/Skia 解耦；求值、时间轴、校验、运行时、配置存储的单测覆盖了大多数边界（首尾保持、step、缓动、Loop、Finished、恢复、Dormant、50 步撤销）。
- 预览隔离在 **Stop/Back 按钮** 路径上逐值恢复；`RuntimeDoesNotCreateDocumentHistory` 证明不进文档历史；保存只在 Ready 开放。
- 只读渲染路径保留原编辑器签名/行为，46 个 Rendering 测试通过；颜色键/选择覆盖层未回归。
- 路由、顶栏、树、检视器、时间轴、传输条的可访问名称/标签/`aria-live` 基础工作扎实；无自动播放，20Hz 数值未设为 live region。
- 文档 v2 的容错分级（几何严格、仿真段独立丢弃、孤儿剪除、警告）方向正确；v1 兼容与 4MiB/数量上限测试齐全。

---

## 1. 需求追溯矩阵

### 1.1 功能需求（SIM-F）

状态：**met** = 实现与需求一致且已有证据；**partial** = 主路径存在但承诺有缺口；**missing** = 未实现。

| ID | 状态 | 证据 |
| --- | --- | --- |
| SIM-F-001 | met | [码] 仅解析现有 `Circle/Rectangle/Triangle/Container`，无新运行时类型（`SimulationPrimitiveIndex.cs`、`SimulationResolver.cs`）。 |
| SIM-F-002 | met | [验] `SimulationCanvas.razor` 无点选/拖拽/命中处理器；E2E 通过画布只读检查。 |
| SIM-F-003 | **partial** | [验] `Stop`/Back 逐值恢复（`SimulationRuntimeTests.Preview_EvaluatesAndStopRestoresEveryFloatProperty`、E2E smoke）；浏览器后退后模型恢复但编辑器 UI 陈旧（**S-02**）。 |
| SIM-F-004 | met | [码] `SimulationConfig` 独立于 `Primitive.Props`，键为 `(PrimitiveId, PropertyName)`（`SimulationTrack.cs`）。 |
| SIM-F-005 | met | [验] 编辑器撤销/删除不触碰配置；删除→Dormant、撤销→Active 已手工验证（**S-04** 的保存确认缺口另计）。 |
| SIM-F-010 | met | [验] 顶栏 `Simulate` 链接、仿真页 `Back to editor`；E2E smoke/dormant。 |
| SIM-F-011 | met | [验] 共享 `IPrimitiveService` 单例；直接访问空文档显示 empty state（E2E `simulation page shows an empty state`）。 |
| SIM-F-012 | met | [验] Back/Save/Play/Pause/Stop/Loop/时间读数/±1/60 均在顶部传输区；bUnit `SimulationTransport_*`。 |
| SIM-F-013 | met | [验] 三栏 + 底部时间轴；1440×900 与 1024×768 手工布局检查无溢出。 |
| SIM-F-014 | met | [验] `role="img"`、无交互；E2E `getByRole('img', {name:/Simulation canvas/})`。 |
| SIM-F-015 | met | [码] 页面只维护 `_selectedId/_selectedProperty`，从不调用 `PrimitiveService.SetSelected*`；初始选中只读复制。 |
| SIM-F-016 | met | [验] 递归树、层级、动画徽标；bUnit `SimulationTree_RendersHierarchySelectionAndBadge`。 |
| SIM-F-017 | met | [验] 时间轴按 `Primitive.Id` 过滤；bUnit `SimulationTimeline_RendersSelectedTracks`。 |
| SIM-F-018 | met | [验] 无图元/无选中/无轨道/无活动轨道均有空状态或禁用提示；E2E empty/editing。 |
| SIM-F-019 | **partial** | [验] Back 按钮先 `Stop` 再导航；浏览器后退只恢复模型，编辑器面板/画布保持动画值（**S-02**）。 |
| SIM-F-020 | met | [验] 每 `(Id, 属性)` 唯一；`AddTrack_RejectsDuplicateMissingAndInvalidValues`。 |
| SIM-F-021 | met | [验] 播放头 + `min(t+1, 3600)` 两帧，端点回退；`AddTrack_*` 三个测试。 |
| SIM-F-022 | met | [验] 时间/值/1ms/属性约束校验；`SimulationValidatorTests`。 |
| SIM-F-023 | met | [验] linear/ease/step/cubic-bezier；`SimulationEvaluatorTests`、`CubicBezierSolverTests`。 |
| SIM-F-024 | met | [码] 最后一帧插值控件隐藏；`SimulationKeyframeEditor` 的 `IsLast` 分支。 |
| SIM-F-025 | met | [验] 全局 `T`/Loop；`SimulationTimelineTests`、`SimulationRuntimeTests.Loop_*`。 |
| SIM-F-026 | **partial** | [码] 值确为属性原值/父空间，但 UI 未标注“相对父级”（**S-07**）。 |
| SIM-F-027 | met | [验] 2000 轨道/200 帧/10000 帧上限；`SimulationValidatorTests` 三个 limit 用例。 |
| SIM-F-030 | met | [验] 首尾保持、区间归一化、step；`SimulationEvaluatorTests`。 |
| SIM-F-031 | met | [验] 控制点限定 `[0,1]`，eased 不越界；`CubicBezierSolverTests`、`ValidateStructure_InvalidBezier`。 |
| SIM-F-032 | met | [验] 每帧先写全部基础值再写 Active；`SimulationRuntime.Apply`、runtime readout 测试。 |
| SIM-F-033 | met | [验] 会话快照/Stop 恢复；删除轨道后属性回退（`UpdateConfig_RemovingAllTracksReturnsToReadyAndBase`）。 |
| SIM-F-034 | met | [验] 直接写 `FloatPropValue.Value`，不触发服务通知/历史；`RuntimeDoesNotCreateDocumentHistory`。 |
| SIM-F-040 | met | [验] Ready 基础值、播放头 0 不应用首帧；`Ready_UsesBaseValuesAndIsPlayable`。 |
| SIM-F-041 | met | [验] seek/step 进入 Preview；首次 Play 建会话；`StepAndSeek_FromReady*`。 |
| SIM-F-042 | met | [验] Ready→0、Preview/Paused→当前、Finished→0；`Finished_CanSeekBackAndReplayFromZero`。 |
| SIM-F-043 | met | [验] Loop 取模、非 Loop Finished 保持末帧；`Loop_WrapsAndNeverFinishes`、E2E Loop/Replay。 |
| SIM-F-044 | met | [验] Stop 恢复、时间 0、Ready；E2E smoke、runtime 测试。 |
| SIM-F-045 | met | [码][验] 播放中 SetLoop 立即生效；`SetLoopAndReapplyCurrentFrameUpdateState`，E2E Loop；播放中关闭 Loop 的 Finished 未单独 E2E。 |
| SIM-F-046 | met | [验] 无 Active/T≤0 时 Play/seek/step 禁用并提示；`EmptyAndZeroDurationConfigsBlockPlayback`、E2E empty。 |
| SIM-F-047 | met | [验] Finished 编辑 T 变化→Paused/Ready；`UpdateConfig_FromFinished*`、`UpdateConfig_ToZeroDuration*`。 |
| SIM-F-050 | met | [码] 单一 rAF + 单调 `Stopwatch`，非固定步长；`SimulationPage`/`penelopa.ts`。 |
| SIM-F-051 | met | [验] 隐藏标签页暂停；JS 单测 `simulation frame loop pauses...`、E2E hidden。 |
| SIM-F-052 | met | [验] F3 时间、20Hz 读数、暂停/定位/编辑强制刷新；`SimulationTransport_UpdatesTimeReadoutFromChannel`、E2E。 |
| SIM-F-053 | **partial** | [验] 画布每帧 Invalidate、读数 20Hz；但 20Hz 重渲染整块检视器/时间轴，200 关键帧选中时 tick 57.25（**S-06**）。 |
| SIM-F-054 | **partial** | [验] 原生 range/拖动 seek、Playing 自动暂停；刻度标签点击不 seek（`pointer-events:none`，无 click；**S-08**）。 |
| SIM-F-055 | met | [验] ±1/60、夹取、Ready→Preview；E2E step、runtime 测试。 |
| SIM-F-056 | met | [码] 默认适配、缩放 1–8、横向滚动、自适应刻度；`SimulationTimeline.BuildTicks`、bUnit。 |
| SIM-F-057 | met | [验] 给定 t 与帧率无关；evaluator/runtime 测试。 |
| SIM-F-060 | met | [验] 全部 Float 属性、Animate、轨道/当前值/编辑入口；bUnit `SimulationInspector_*`、E2E。 |
| SIM-F-061 | met | [验] 播放头处两帧、捕获当前值；store/E2E。 |
| SIM-F-062 | met | [验] Add 捕获当前求值、同刻更新；`SetKeyframeValue_InsertsAndUpdatesExistingTime`、E2E。 |
| SIM-F-063 | met | [验] 时间/值/插值/4 控制点；bUnit `SimulationKeyframeEditor_*`、E2E。 |
| SIM-F-064 | met | [验] 1ms、冲突拒绝就地报错；store/validator 测试。 |
| SIM-F-065 | met | [验] 领域约束；`TryValidateKeyframeValue_ReportsConstraints`。 |
| SIM-F-066 | met | [验] 拖动/1/60/1ms/邻居夹取/Esc；E2E drag + 本次手工 Esc（0.000s 保持）。 |
| SIM-F-067 | **partial** | [验] 单击只选中、Go to 按钮；无双击 Go to（**S-08**）。 |
| SIM-F-068 | met | [验] 删最后一帧删轨；`RemoveKeyframe_RemovesTrackWhenLastFrameIsDeleted`；UI 由 store 驱动。 |
| SIM-F-069 | met | [验] Ready/Preview/Paused/Finished 可编辑、Playing 只读、编辑后重算；代码/E2E。 |
| SIM-F-070 | met | [验] 50 步、按钮 + Ctrl+Z/Y、输入框跳过；store 测试、JS 测试、E2E；拖动一次撤销。 |
| SIM-F-071 | met | [码] 进入页/加载文档清空配置撤销；`SimulationPage.OnInitialized`、`ReplaceConfig`。 |
| SIM-F-072 | met | [码] 非 Playing 撤销/重做走 `TryUpdateConfig` 立即重算；`SimulationPage.HandleConfigChanged`。 |
| SIM-F-080 | **partial** | [验] 有效 Dormant 忽略/不阻塞；结构非法的 Dormant 会使整段仿真被丢弃并阻塞播放（**S-05**）。 |
| SIM-F-081 | **partial** | [验] 数量 + 全部删除 + 可撤销；无明细入口、无逐条删除（**S-04**）。 |
| SIM-F-082 | met | [验] 手工验证：删除→撤销→重进仿真，Dormant 横幅消失、Play 可用。 |
| SIM-F-083 | **partial** | [验] 正常路径排除 Dormant + 确认；编辑器 pending 保存跨 Load 存活，可写入旧/空配置（**S-01**）。 |
| SIM-F-084 | met | [验] 孤儿轨道剪除 + 警告（`Deserialize_OrphanTracks_ArePrunedWithWarning`）；非 Float 由 resolver 剪除。 |
| SIM-F-090 | met | [验] v2 + 可选 `simulation`；`RoundTrip_SimulationConfig...`。 |
| SIM-F-091 | met | [验] v1 无配置、保存 v2、未来版本拒绝；`Deserialize_VersionOne...` 及既有版本测试。 |
| SIM-F-092 | met | [码] 两个 Save 都走 `DocumentStorage` 同一负载/备份；仿真页 Save Ready-only。 |
| SIM-F-093 | met | [验] 仅 Ready 可用；E2E Playing/Finished 保存禁用；Preview/Paused 由 `CanSave` 代码保证。 |
| SIM-F-094 | **partial** | [验] Load 替换文档/配置并清空配置撤销；但旧 pending 保存可在 Load 后覆盖（**S-01**）。 |
| SIM-F-095 | met | [码] 确认文案明确“document and simulation configuration”。 |
| SIM-F-096 | met | [验] 非法仿真段丢弃 + 警告、文档照常加载；`Deserialize_InvalidSimulationSection...` 等。 |
| SIM-F-097 | met | [验] 手工注入 `localStorage.setItem` 抛错：错误提示、主存储不变；消息含 JS 堆栈（**S-16** 质量）。 |
| SIM-F-100 | met | [验] 播放入口统一 `TryGetPlaybackBlockers`；runtime 测试。 |
| SIM-F-101 | **partial** | [码] store 阻止非法值入库，因此 UI 无法制造 active 非法态；页面只显示首个 issue，无“对应行 + 汇总”错误 UI（**S-15**）。 |
| SIM-F-102 | met | [验] 输入/模型不持有非法状态；store 每条编辑校验。 |
| SIM-F-103 | **partial** | [验] 求值忽略 Dormant；结构校验不忽略（**S-05**）。 |
| SIM-F-104 | **partial** | [码] 页面捕获预期的三类异常并 Stop/恢复；无异常恢复单测，未预期异常会被 JS `.catch` 静默吞掉（**S-21**）。 |
| SIM-F-105 | met | [验] 手工保存失败：错误提示、文档/配置保持未保存（消息质量见 **S-16**）。 |

### 1.2 非功能需求（SIM-NF）

| ID | 状态 | 证据 |
| --- | --- | --- |
| SIM-NF-001 | **partial** | [验] 500 图元 + 100 轨道 5s 采样 **59.9 tick/s**、idle CPU 2.53%；tick 是 JS→.NET 回调计数，不是实际绘制帧；性能用例未选中任何图元/轨道（**S-13**）。 |
| SIM-NF-002 | **partial** | [验] 60s soak：JS heap 5,634,108→5,538,932 bytes（约 −0.1 MiB）、DOM 3021→2749、listeners 62→32；只覆盖 JS heap/DOM，.NET WASM 线性内存未测，32 MiB 阈值偏宽。 |
| SIM-NF-003 | met | [验] Stop 后 idle 2.53%（<5 门禁）；编辑器 idle 0.06%/500 矩形 0.02%。 |
| SIM-NF-004 | met | [码] `renderHitBuffer:false`、null hit canvas、二分查找；Rendering 46 测试通过。 |
| SIM-NF-005 | **partial** | [验] 100 轨道无选中时 59.9 tick；选中 200 关键帧轨道时 **57.25 tick**，检视器/时间轴整体 20Hz 重渲染（**S-06**）。 |
| SIM-NF-010 | met | [验] 无自动播放；reduced-motion 页面仍等待用户按 Play。 |
| SIM-NF-011 | met | [验] 传输条全部原生 `<button>`，名称可区分；bUnit/E2E。 |
| SIM-NF-012 | **partial** | [码][验] 状态/Dormant 使用 polite；错误用 `role="alert"`（assertive），ToolPanel 文档消息无 live region（**S-18**）。 |
| SIM-NF-013 | met | [验] 画布 `role="img"` + 描述；E2E。 |
| SIM-NF-014 | met | [码] 关键帧时间/值/插值/Bezier 均有 label；错误 `aria-describedby`。 |
| SIM-NF-015 | met | [码] 检视器表格可编辑时间/值/插值，Go to 按钮；时间轴拖动不是唯一路径。 |
| SIM-NF-016 | met | [验] reduced-motion 提示 + 不自动播放；E2E。 |
| SIM-NF-017 | **partial** | [验] 返回编辑器焦点到 Simulate 链接（A，E2E）；进入仿真页焦点为 `BODY`，未到 h1（**S-09**）。 |
| SIM-NF-018 | met | [码] 新控件复用 `--penelopa-*`；对比度未实测（[未]）。 |
| SIM-NF-020 | met | [验] 1024×768 无页面溢出，三栏 + 时间轴可见；1440×900 正常；820/更窄未自动化。 |
| SIM-NF-021 | met | [码] 无 .NET/SkiaSharp/前端框架升级；依赖文件未变。 |
| SIM-NF-022 | met | [验] UI 文案英文；需求/ADR 中文/英文按仓库惯例。 |
| SIM-NF-030 | met | [验] Core/Rendering/Ui/JS/E2E/perf 均存在并通过。 |
| SIM-NF-031 | **partial** | [验] Core 92.73%、Rendering 91.91% 通过整程序集门禁；新代码按文件看 `SimulationBoundsEstimator` 83.5% < 90%（**S-14**）。 |
| SIM-NF-032 | **partial** | [验] 进入/返回、建轨、播放/暂停/停止、定位/步进、Loop/Finished/Replay、保存重载、Dormant 清理、100 轨道性能均有；**无 `console.error` 断言**，漏浏览器后退/进入焦点/刻度点击/双击等（**S-11/S-12**）。 |
| SIM-NF-033 | met | [码] README/架构已清理“静态顶栏/Version 1”，链接与表格存在；但架构第 405 行“h1 focused”与实现不符（**S-23**）。 |

### 1.3 第 5 节验收标准

| # | 状态 | 证据 |
| --- | --- | --- |
| 1 | met | [验] Rectangle/PosX 建轨、播放头拖动 Current 同步；E2E 2 帧；本次补测画布像素 t=0 vs 0.5 哈希不同（`4cfa0b5b…` vs `27aa1a70…`）。验收要求的“3 帧”未单独自动化，但 3 帧由 Add keyframe 已覆盖。 |
| 2 | met | [验] Play/Pause/Resume/Stop 状态与按钮 E2E；Stop 恢复；不进文档历史由 runtime 单测。 |
| 3 | met | [验] Loop 无缝回绕/Finished 保持/Replay 从 0；runtime 测试 + E2E。 |
| 4 | met | [验] ±1/60 与拖动、确定性；E2E + evaluator 测试。 |
| 5 | met | [验] Preview 下 Add/Undo/Redo；E2E；配置撤销不影响文档历史由架构分隔保证（未单测交叉）。 |
| 6 | **partial** | [验] 非法值/时间/Bezier 在 store 中被拒绝并显示内联错误；但 active 非法态无法进入，所以“阻止播放 + 汇总区域”没有真正的 UI 验证（**S-15**）。 |
| 7 | **partial** | [验] Dormant 保存确认/清理、删除撤销恢复、加载孤儿剪除/警告；缺明细/逐条（**S-04**）；撤销/剪除无 E2E（仅手工/单测）。 |
| 8 | **partial** | [验] Save 后刷新/Load 复现轨道；Loop/插值完整性未断言，仿真画布不改几何未断言（模型层只读由 runtime 测试）。 |
| 9 | **partial** | [码] 键盘可完成树选择、Animate、编辑、Go to、播放/暂停/停止；进入焦点失败；无键盘-only E2E；20Hz 未 live（代码）。 |
| 10 | **partial** | [验] 59.9 tick、idle 2.53%；实际绘制 FPS 未测；200 关键帧选中 57.25 tick（**S-13**）。 |

---

## 2. 问题清单

> 严重级别定义沿用任务：P0 数据丢失/崩溃/核心流程不可用/安全漏洞/需求硬性违反；P1 明显正确性/性能/无障碍缺陷或必需能力缺失且无合理绕过；P2 次要正确性/测试缺口/可维护性；P3 风格/文档小错/优化建议。

### 2.1 汇总表

| ID | 级别 | 文件:行 | 问题 | 证据 | 影响 | 修复建议 | 建议测试 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| S-01 | **P0** | `ToolPanel.razor:38-40,237-254,294-326` | Dormant 保存确认跨 Load 存活；确认时用旧配置 + 当前文档保存 | 浏览器复现：Load 后 pending 横幅仍在；Save active only 后 `Simulation.Tracks` 1→0，提示“Document saved.” | 静默丢失刚加载的仿真配置；若旧配置有其他 Active 轨道还会写出孤儿轨道；违反 SIM-F-083/094/095 | Load 开始/取消/确认时清除并取消 `_pendingSave`；确认时重新计算 active/dormant；或在开始确认时原子快照文档+配置；用单一 pending 操作状态机 | bUnit 状态机 + E2E：Save(dormant)→Load→Replace→断言无旧横幅、JSON 保留 loaded tracks |
| S-02 | **P1** | `SimulationPage.razor:765-790`；`SimulationRuntime.cs:362-393`；`PropertyPanel.razor:63-69` | 浏览器后退/任意路由退出时，在 JS 拆卸 await 之后才 `Stop()`；模型恢复但编辑器面板/画布已用动画值初始化且无通知 | 播放 0.494s 后 `page.goBack()`：PosX 输入 20ms/1.6s 为 `58.951`（另一轮为 `57.484`），无重选；重新选择后 `10`；立即 Save 写入 `10`（模型已恢复） | 编辑器显示与模型不一致；用户可能据显示值编辑；画布/命中缓冲可能陈旧；违反 SIM-F-003/019 的“精确恢复”可观察承诺 | `DisposeAsync` 第一行同步 `Stop()`；订阅 `Navigation.LocationChanged` 在路由切换前 Stop；恢复后调用 `PrimitiveService.NotifyPrimitivesChanged` 或让编辑器重读；E2E 用 `goBack` 断言无需重选即为基值 | Play→`page.goBack()`→断言 PosX 输入=10、画布截图与基值一致、无 pageerror |
| S-03 | **P1** | `DocumentSerializer.cs:398-427,139-148`；`DocumentStorage.cs:64-90` | 文档数组元素为 null（`Primitives:[null]`/`Children:[null]`）时 `FromDto(null)` 抛 NRE，未被 catch 过滤，也不在 Load 边界 catch | 浏览器：Load 后 console.error `NullReferenceException at DocumentSerializer`、Atlas content render failure、`#blazor-error-ui` 可见；**无 pageerror** | 不可信 localStorage 可使编辑器/加载流程崩溃；本次持久化改动未加固 | 在 `Deserialize`/`FromDto` 对 primitives/children 逐项 null 检查并返回可诊断失败；Load 边界兜底为 `DocumentLoadResult` 错误；补测试 | 单测 `Primitives:[null]`、`Children:[null]`；E2E 临时文档 + `console.error`/错误边界断言 |
| S-04 | **P1** | `SimulationPage.razor:87-98,483-494`；`requirements/simulation.md:149`；`plans/...:334` | Dormant 只有数量 + 全部删除；无明细入口、无逐条删除 | 代码仅渲染 count + `Remove inactive`；无列表/每行 Delete | 用户无法看到哪些轨道失效，只能“全删”；SIM-F-081 能力缺失，无合理替代 | 增加 `<details>`/表格列出 PrimitiveId/属性/关键帧数，逐行 `TryRemoveTrack`（store 已支持无解析删除）并保留撤销 | bUnit 明细渲染/逐条删除/撤销；E2E 两条 Dormant 轨道只删一条 |
| S-05 | P2 | `DocumentSerializer.cs:258-264,276-280`；`SimulationValidator.cs:26-61`；`SimulationRuntime.cs:34` | 结构非法的 Dormant/孤儿轨道会导致整段仿真配置被丢弃；valid active tracks 一并丢失 | 浏览器 seed：1 条有效 Active + 1 条时间 -1 的孤儿；Load 警告“configuration was ignored”，track 0 个、Play 禁用 | 违反 SIM-F-080/103“Dormant 不阻塞/不参与校验”的精神；与 SIM-F-096“整段丢弃”存在需求冲突 | 先解析+解析目标，剪除/隔离 Dormant 后再对 Active 做结构校验；或明确产品规则并更新需求/文档 | 单测：有效 Active + 非法 Dormant→Active 保留、Dormant 剪除+警告；浏览器复现 |
| S-06 | P2 | `SimulationInspector.razor:232-241`；`SimulationTimeline.razor:225-234` | 20Hz readout 触发整个检视器（含最多 200 行表）与整个时间轴（最多 200 标记）`StateHasChanged` | 200 关键帧轨道选中、播放 4s：tick **57.25/s**（无选中 100 轨道 59.9）；代码确认整组件重渲染 | 最大合法轨道下 60FPS 目标出现可测下降；SIM-NF-005“20Hz 不掉帧”未满足/未证明 | 将时间/当前值拆为小组件；时间轴播放头独立更新，关键帧列表只在配置/选择变化时渲染；必要时读数降到 10Hz 但保持 20Hz 数据 | 性能用例：选中 200 帧轨道 + 播放，断言 tick ≥59（或真实绘制帧） |
| S-07 | P2 | `SimulationInspector.razor:27-59`；`SimulationKeyframeEditor.razor:12-38` | 属性值/关键帧值没有“相对父级”标注 | 搜索 `relative` / `parent space` / `相对父` 无 UI 文案；需求 SIM-F-026 明确要求 | 容器内动画可能被误解为世界坐标；语义承诺缺失 | 在检视器/关键帧编辑器加固定提示“Values are relative to the parent.”（可按容器上下文更具体） | bUnit/静态断言提示存在 |
| S-08 | P2 | `SimulationTimeline.razor:68-82,93-101`；`simulation.css:492-501` | 刻度标签 `pointer-events:none` 且无 click；关键帧只有 click/pointerdown/keydown，无 dblclick | 浏览器：点击第二个刻度时间仍 0.000s；双击第 2 帧时间仍 0.500s | SIM-F-054“刻度点击 seek”、SIM-F-067“双击 Go to”缺失；只能使用 range/按钮 | 给 ruler 加点击→百分比时间映射（或让刻度可点）；给关键帧加 `@ondblclick` 调 Go to；保留现有按钮 | E2E：刻度点击 seek、双击 marker seek、键盘 Enter/Go to |
| S-09 | P2 | `App.razor:4`；`SimulationPage.razor:20`；`architecture.md:405` | 进入 `/simulation` 时 `FocusOnNavigate` 目标 h1 不可聚焦，实际焦点为 BODY | 浏览器 `document.activeElement` = BODY（tag/cls 空）；返回编辑器焦点为 A “Simulate” | 违反 SIM-NF-017；文档“h1 focused on navigation”失实；键盘用户需重新 Tab | 给 h1 加 `tabindex="-1"`（CSS 已有 `h1:focus`），或改用 `focusElement` 显式聚焦；更新文档 | E2E：进入后 `activeElement` 为 h1；返回为 Simulate 链接 |
| S-10 | P2 | `SimulationPage.razor:496-523`；`SimulationInspector.razor:358-376` | `ReconcileSelection` 只做越界清空，不处理“删除/撤销发生在选中帧之前”的索引漂移 | 代码推断：4 帧时选中 index 2(1.0s)，删除 index 1(0.5s) 后 index 2 变为 1.5s，选中态/编辑器跳到另一帧 | 用户可能编辑错关键帧；不是数据丢失但属于 UI 正确性缺陷 | 用稳定 keyframe id 或时间作为选择标识；删除/插入/撤销后按标识重定位；无法定位则清空 | bUnit/UI：4 帧选中第 3 帧→删除第 2 帧→断言选中跟随原帧或清空，不跳到别的帧 |
| S-11 | P2 | `tests/e2e/tests/simulation.spec.ts:67-68`（多处重复） | 所有仿真 E2E 只收集 `pageerror`，未监听 `console.error`/错误边界 | [验] `Primitives:[null]` 崩溃只产生 console.error + `#blazor-error-ui`，无 pageerror；若换成仿真 corrupt doc，现有断言会全绿 | 测试假绿：核心加载/渲染错误可绕过“无错误”断言；违背 SIM-NF-032“断言无控制台错误” | 统一 helper 收集 pageerror + console.error（过滤已知无害）+ 错误边界可见性；失败时打印 | 所有仿真 E2E 使用 helper；新增 corrupt doc 用例 |
| S-12 | P2 | `simulation.spec.ts:99-105,255-265,347`；`:314-320` | 固定 `waitForTimeout`（150/100/250/1200/100/500ms）、1.2s Finished、100ms 拖动、visibilityState 覆写；缺关键路径 | 本次本地 30 项通过；代码/测试审阅发现上述等待；无浏览器后退/进入焦点/Esc/保存失败/键盘-only/1024/索引漂移/Dormant+撤销用例 | CI（Ubuntu、慢机）flaky 风险；发现的两处真实缺陷恰好落在未覆盖路径 | 用 `expect.poll`/`toBeVisible({timeout})` 替代固定等待；拖动前等待 pointer capture/JS 就绪；补缺失路径；visibility 测试保留但注明合成 | 在 CI 增加上述用例；至少 Chromium + 一次 Firefox smoke |
| S-13 | P2 | `simulation-perf.spec.ts:90-104,157`；`requirements:178-182`；`architecture.md:399-401` | 性能“FPS”实际是 `getSimulationTickCount`（JS→.NET 回调次数），不是绘制帧；100 轨道用例未选中任何图元/轨道；heap 阈值 32MB；.NET 内存未测 | [验] 59.9 ticks/5.01s；选中 200 帧轨道 57.25；开销/绘制未独立计数；JS heap 只测首尾 | 文档“约 60 FPS/20Hz 不掉帧”证据不足；慢绘制或 20Hz 面板开销可能被 tick 指标掩盖 | 增加绘制计数（SKGLView paint 回调/帧时间）或 CDP frame 统计；性能用例选中一个 100 轨道图元与 200 帧轨道；heap 多次采样并收紧阈值；文档区分 tick FPS 与 paint FPS | 双指标性能用例 + 选中/最大帧场景；记录 p50/p95 frame time |
| S-14 | P2 | `scripts/check-coverage.py:9-15` | 覆盖率门禁是整程序集行覆盖，不是“新增核心代码 ≥90%”；`SimulationBoundsEstimator` 83.5% | [验] Core 92.73%、Rendering 91.91%；Simulation/ 汇总 93.46%，但 Estimator 83.5%、DocumentSerializer 91.4% | SIM-NF-031 的“新增代码”要求未被门禁证明；Estimator 的 downsample/异常恢复分支未覆盖 | 按文件/新增行（diff coverage）加门禁；补齐 Estimator 的 >512 采样、异常恢复、空/rotation 变体；CI 保留整程序集门禁 | `scripts/check-coverage.py` 增加 per-file/new-code 阈值；目标 Estimator ≥90% |
| S-15 | P2 | `SimulationValidator.cs:102-114`；`SimulationPage.razor:52-55,749-756`；`SimulationInspector.razor:22-25` | 需求要求非法 Active 轨道在“对应行 + 汇总区域”显示错误；实现只有首个 issue/内联操作错误，且 store 阻止非法值入库使该状态不可达 | [码] `TryAccept` 拒绝非法编辑；页面 `SetPlaybackError` 取 `issues[0]`；无逐行错误渲染；无 E2E | SIM-F-101 的形式要求未满足；用户看到的是通用提示而非可定位错误 | 若保留“store 永不含非法值”，在需求/文档中明确并删除不可达的 per-row 承诺；否则允许无效草稿并在对应行+汇总显示 | 单测/ bUnit：构造非法 config（绕过 store）→页面显示行/汇总；或需求修订评审 |
| S-16 | P3 | `DocumentStorage.cs:51-60`；`SimulationPage.razor:54-59` | 保存失败消息直接拼接 JSException.Message，包含完整 JS 堆栈/框架 URL/换行 | [验] 手工让 setItem 抛错，页面显示 `quota exceeded\nError: ... at Storage.setItem...` 多行堆栈 | 技术噪音、信息泄漏、可能破坏单行布局；不影响存储正确性 | 对 JS 异常只显示安全摘要（如 “quota exceeded”），完整异常写 console/日志 | UI 测试保存失败消息不含 ` at ` / `_framework` |
| S-17 | P3 | `SimulationTransport.razor:93-99`；`requirements:105` | Preview 主按钮显示 “Resume”，需求状态表写 “Play” | 代码/需求表对照 | 文案与需求不一致；行为正确 | 统一文案或更新需求表 | bUnit 断言 Preview 按钮名称 |
| S-18 | P3 | `SimulationPage.razor:54,59,63`；`SimulationInspector.razor:24`；`SimulationTimeline.razor:114`；`ToolPanel.razor:74`；`requirements:188` | 错误/保存消息使用 `role="alert"`（assertive）；ToolPanel 文档消息无 live region；要求错误汇总 polite | 代码；E2E 未测播报 | 无障碍语义比需求更强/不一致，可能打断读屏；保存/加载错误在编辑器不播报 | 汇总错误用 `role="status"`/`aria-live="polite"`，保留危险操作为 alert；ToolPanel 消息加 polite 区域 | 静态/axe 或 bUnit role 断言 |
| S-19 | P3 | `SimulationPage.razor:335-342,442-481` | 仿真页 Dormant 保存确认在移除 Dormant/编辑配置后仍保留；banner 显示 0 inactive、Save 仍禁用 | 代码：`HandleConfigChanged` 不清理 `_pendingDormantSave`；`RemoveDormantTracks` 走配置变更 | 用户困惑，需点 Save active only/Cancel；不丢数据（Save 使用实时 ExportActive） | 配置/文档变化时取消 pending 或重新计算并更新提示；Save 不应因 0 个 dormant 保持禁用 | bUnit/E2E：确认态→Remove inactive→断言横幅消失或计数更新、Save 可用 |
| S-20 | P3 | `penelopa.ts:216-252`；`SimulationConfigStore.cs:31`；`simulation.css:177` | 生产代码含测试专用 `getSimulationTickCount`、未使用 `focusElement`、`getElementRect.left`、仅测试使用的 `DormantCount`、未用 CSS 类 | grep：`focusElement` 仅 JS 测试；tick 仅 perf test；`left` 未被组件使用；`DormantCount` 仅 store 测试引用；`penelopa-simulation-readout` 无组件使用 | 生产接口面积/死代码；测试钩子泄漏 | 将 tick 计数改为测试注入/仅 debug；删除未用 API/CSS 或使用 `focusElement` 修 S-09 | 静态 lint/引用检查 |
| S-21 | P3 | `SimulationPage.razor:599-603`；`penelopa.ts:139-145` | `OnConfigUndo/Redo` 无 `_disposed` 守卫；rAF `.catch` 静默吞未预期 .NET 异常 | 代码；无测试 | 拆卸边缘可能操作已离开页面的配置；未预期异常会导致播放静默停摆且无错误 UI（SIM-F-104 缺口） | Invoke 方法加 `_disposed` 检查；JS 把未处理异常上报给页面（或页面 catch 所有异常并 Stop） | JS 单测延迟调用；UI 测试异常恢复 |
| S-22 | P3 | `SimulationPage.razor:470-476`；`plans/...:470-471`；`CanvasRenderer.cs:51-76,109-124` | Fit 估算每次 Ready 配置编辑都全量重算（计划要求按配置/文档版本缓存）；render-only 仍 `EnsureBuffersFor` 分配命中位图 | 代码；500/100 编辑未测；10k 图元规模风险 | 大文档编辑可能卡顿；额外内存 | 按 config/document 版本缓存 Estimate；render-only 跳过 hit buffer 分配/尺寸 | 基准：10k 图元 Ready 编辑/ Fit 时间；内存 |
| S-23 | P3 | `architecture.md:405`；`:399-401`；`plans:144,334,470-471`；`README.md:123-128`；`README.zh` 性能段 | 文档失实/遗漏：h1 聚焦、tick 当 FPS、Dormant 明细、Newton、缓存、测试文件名、README 未列 simulation-perf | 对照代码/本次复现 | 误导维护者/验收；降低文档可信度 | 更新文档到实际实现；记录偏差；README 增加 `simulation-perf.spec.ts`；区分 tick/paint FPS | 文档评审 |
| S-24 | P3 | `SimulationPage.razor:765-790`；`simulation.css:520-533`；`SimulationKeyframeEditor.razor:50-57` | 编辑器从仿真页返回会重建页面（viewport/dock 可能重置）；关键帧 marker 0.7rem（11px）触发目标偏小；`@key` select 可能丢焦点；对比度未验证 | 代码/手工（1024/选中保留；viewport 未测）；WCAG 未测 | UX/无障碍轻微回归 | 保留/恢复编辑器视口与布局，或明确接受；增大 marker 点击区；避免 select 重建或用 JS 恢复焦点；跑对比度/axe | E2E 返回后 zoom/pan；axe/对比度 |
| S-25 | P3 | `ToolPanel.razor:237-254`；`SimulationPage.razor:328-366` | Save 在 in-flight 不禁用；确认按钮与主 Save 共用路径，极快双击可能第二次直接保存；pending 配置在文档变化后可能含新 Dormant | 代码；无复现（时序边缘） | 确认语义弱化/陈旧负载（与 S-01 同源） | 加 `_saving` 标志/禁用按钮；确认动作使用独立 handler；文档变化时取消 pending | bUnit 双击/文档变更状态测试 |

### 2.2 P0/P1 详细说明

#### S-01 [P0] 编辑器 Dormant 保存确认跨 Load 导致仿真配置丢失

**位置**

- `src/Penelopa.Web/Components/Panels/ToolPanel.razor:38-40`（Save 仅按 `_pendingSave` 禁用，Load 始终可点）
- `src/Penelopa.Web/Components/Panels/ToolPanel.razor:237-254`（存在 Dormant 时设置 `_pendingSave` 为 **当时的** active 配置）
- `src/Penelopa.Web/Components/Panels/ToolPanel.razor:294-306`（LoadDocument 不清理 `_pendingSave`）
- `src/Penelopa.Web/Components/Panels/ToolPanel.razor:310-326`（ConfirmLoad 只清 `_pendingLoad`，替换文档/配置，不处理旧 pending save）

**复现（本次 Chrome 实测）**

1. 添加 Rectangle → 进入 Simulate → Animate `PosX` → Back → Save。此时存储的文档 A 有 1 条轨道。
2. 回到 Primitives，删除 Rectangle（轨道变 Dormant）。
3. 点 Tools → Save：出现“1 simulation track(s) ... will not be saved”。`_pendingSave` = 当时 `ExportActive()`（因为唯一轨道已 Dormant，实际为 **0 条轨道**）。
4. 不改确认，点 Load → Replace，加载文档 A（重新带回 1 条轨道）。**pending 保存横幅仍然存在**。
5. 点“Save active only”。`SaveDocumentCore(_pendingSave)` 使用**当前文档 A** + **第 3 步的旧配置（0 轨道）**：
   - 保存前 `Simulation.Tracks` = 1（文档 A）
   - 保存后 `Simulation.Tracks` = 0
   - UI 显示 “Document saved.”
6. 结果：刚加载文档的动画配置被静默删除；`penelopa.document.backup` 虽保存了上一版，但 UI 没有恢复入口。

如果第 3 步之前还有其它 Active 轨道，pending 快照会包含**旧文档**的轨道；加载新文档后保存会把旧文档的 PrimitiveId 轨道写入新文档（下次 Load 会被当作孤儿剪除），同样是数据损坏。

**影响**

- P0：持久化数据丢失/污染，且用户被“Document saved.”告知成功。
- 违反 SIM-F-083（Dormant 不写入、确认语义）、SIM-F-094（Load 整体替换）、SIM-F-095（确认文案）。
- 该路径完全由本次新增的 pending 保存状态引入；旧编辑器 Save 没有确认状态，不会出现。

**最小修复方案**

1. 在 `LoadDocument()` 开始（读取前或确认前）调用取消逻辑：`_pendingSave = null; _pendingDormantCount = 0;`，并刷新消息；或在 `ConfirmLoad()` 中同样清理。最简单：Load 一律取消 pending save，并向用户提示“Load 已取消未确认的保存”。
2. 更稳妥：确认时**重新计算** `active = SimulationConfigStore.ExportActive()` 与 `dormant = Config.Tracks.Count - active.Tracks.Count`；若数量/内容变化，重新展示确认。绝不复用旧快照。
3. 引入单一 `_pendingOperation`（None/SaveDormant/LoadConfirm）状态机；两个确认互斥，任何文档/配置变更都取消 pending 保存或重新计算。
4. Save 门禁用 `_isSaving` 防止 in-flight 双击（S-25）。

**验证方式**

- bUnit/ToolPanel：Save(dormant) → Load 读取（不确认）→ 断言 pending 横幅消失、Save 可用；再 Load→Replace→断言配置保留。
- E2E：按上述步骤，直接读取 `localStorage['penelopa.document'].Simulation.Tracks`，断言仍为 1，且无“Document saved.”误报/旧横幅。
- 回归：正常 Save(dormant)→Save active only 仍只写 Active 配置；Load 无 sim/v1 仍清空配置。

---

#### S-02 [P1] 浏览器后退离开仿真页后编辑器显示动画值

**位置**

- `src/Penelopa.Web/Pages/SimulationPage.razor:758-763`（Back 按钮：先 `_runtime.Stop()` 再导航——正确路径）
- `src/Penelopa.Web/Pages/SimulationPage.razor:765-790`（`DisposeAsync`：先 await `_keyboardHandle.DisposeAsync()`、`StopFrameLoopAsync()`，第 789 行才 `_runtime?.Stop()`）
- `src/Penelopa.Core/Simulation/SimulationRuntime.cs:362-393`（写/恢复属性不发文档通知）
- `src/Penelopa.Web/Components/Panels/PropertyPanel.razor:63-69`、`CanvasPanel.razor:61-67`（新页面初始化时从 `GetState()` 快照值）

**复现（本次 Chrome 实测）**

1. 添加 Rectangle 并在编辑器选中（PosX=10）。
2. 进入 Simulate，Animate PosX，第二帧 Value=100，Play，等到 0.494s。
3. **浏览器后退**（不是页面内 Back 按钮）。
4. 不重新选择，直接读编辑器 PosX 输入：20ms = `58.951`，1.6s 仍为 `58.951`；重新在树中选中 Rectangle 后 = `10`。
5. 此时点编辑器 Save，存储的 PosX = `10`（模型已恢复）；即模型正确、UI 陈旧。

**根因**

- 新编辑器页面在旧仿真页 `DisposeAsync` 的 JS await 完成前就已创建，`PropertyPanel`/`CanvasPanel` 从服务读取到仍被动画改写的值并渲染。
- 随后 `_runtime.Stop()` 恢复模型，但运行时按设计不触发文档变更通知，编辑器不会重绘/刷新输入。
- 因此“离开页面时精确恢复基础值”在**模型**层成立，在**用户可见状态**层不成立；画布命中缓冲也可能是旧值。

**影响**

- SIM-F-003/SIM-F-019 的可观察承诺失败；用户看到 57 但模型是 10。
- 如果用户据显示值继续编辑（例如把 57 改为 60），会意外写入动画中间值；若只是保存，当前实测不会污染几何（Save 10），但这是依赖实现细节而非契约。
- 测试只用页面内 Back 按钮，未覆盖需求明确列出的“浏览器后退”。

**最小修复方案**

1. 在 `DisposeAsync` 的**第一行**（任何 await 之前）调用 `_runtime?.Stop()`；任何后续 await 都不应延迟恢复。
2. 订阅 `NavigationManager.LocationChanged`，在非 `/simulation` 路由时同步 Stop；`DisposeAsync` 仍作为兜底。
3. 恢复后通知编辑器刷新：在页面/服务边界调用 `PrimitiveService.NotifyPrimitivesChanged(_index.Primitives)`（或 `PrimitiveService.NotifyPrimitivesChanged(_roots)`）；这不是每帧写入，允许在会话结束触发一次。若担心通知时序，编辑器页面在初始化后应从服务读快照（已做），再依靠通知刷新。
4. 考虑将“恢复+通知”封装到运行时/页面单一 `EndSession()`，Stop/Back/Dispose/异常全部走同一路径。

**验证方式**

- E2E：`Play`→`page.goBack()`→**不重新选择**，断言 PosX 输入为 10、画布截图/像素与基值一致；再点击编辑器 Save 读取 localStorage PosX=10。
- 单测/UI：模拟“新面板在 Stop 前初始化”的时序（可在 bUnit 中先渲染面板并读取动画值，再调用页面 Dispose/Stop，断言通知后刷新）。
- 回归：页面内 Back 仍工作；无 pageerror；焦点回到 Simulate。

---

#### S-03 [P1] 含 null 图元/子节点的文档加载导致渲染器崩溃

**位置**

- `src/Penelopa.Core/Services/DocumentSerializer.cs:398-427`（`FromDto` 未检查 `dto is null`；`dto.Id` 直接解引用）
- `src/Penelopa.Core/Services/DocumentSerializer.cs:139-148`（catch 过滤不含 `NullReferenceException`）
- `src/Penelopa.Web/Services/DocumentStorage.cs:64-90`（Load 边界 catch 也不含 NRE）

**复现（本次 Chrome 实测）**

```json
{"Version":2,"Primitives":[null],"SelectionIds":[]}
```

写入 `localStorage['penelopa.document']`，点击 Load：

- `console.error`: `System.NullReferenceException ... at Penelopa.Core.Services.DocumentSerializer...`
- `console.error`: `Atlas content render failure ATLAS-CONTENT-RENDER-FAILED-...: NullReferenceException`
- `#blazor-error-ui` 可见，Tools 面板显示 “Content could not be rendered.”
- **没有 pageerror**（因此现有 E2E 的 `errors` 仍为空）

`Children:[null]` 同样会在递归 `FromDto` 崩溃。该路径并非仿真专属，但本次新增 v2 持久化/共享 Load 未加固；需求要求 localStorage 按不可信输入处理，且 SIM-F-096 的“文档始终返回”承诺不成立。

**影响**

- 崩溃/核心加载流程不可用；恶意或损坏的 localStorage 可让编辑器进入错误边界。
- 与 SIM-F-084/096 的“坏数据可诊断加载”方向相悖。
- E2E 的 pageerror-only 断言无法捕获（见 S-11）。

**最小修复方案**

1. 在 `Deserialize` 的 `dto.Primitives` 循环前检查每个元素非 null；`FromDto` 首行 `if (dto is null) throw new ArgumentException("Primitive entry is null.");`；`container.AddChild(FromDto(childDto...))` 同理。抛出 ArgumentException 会被现有过滤捕获，返回 null，UI 显示“invalid or unsupported”。
2. 或在 `FromDto` 中对 `dto.Children` 的 null 元素抛出 FormatException；统一返回可诊断失败。
3. 在 `DocumentStorage.LoadAsync` 增加最终边界 catch（记录异常并返回 `DocumentLoadResult` 错误），保证即使序列化器未来漏掉某类异常也不会击穿渲染器；但不要用它替代具体 null 检查。
4. 补单测 `Primitives:[null]`、`Children:[null]`、`SelectionIds:[null]`（后者可能已由 JsonException 处理）。

**验证方式**

- 单测：`Deserialize` 返回 null 或 `DeserializeResult`（不可抛 NRE）；DocumentStorage 返回 error。
- E2E：写入坏文档→Load→断言错误消息可见、无 `#blazor-error-ui`、`console.error` 为空；同时保留 pageerror 收集。
- 回归：合法 v1/v2、未知版本、几何非法、非法仿真段仍按原行为。

---

#### S-04 [P1] Dormant 明细与逐条删除缺失

**位置**

- `src/Penelopa.Web/Pages/SimulationPage.razor:87-98`（只显示 count + “Remove inactive”）
- `src/Penelopa.Web/Pages/SimulationPage.razor:483-494`（只调用 `RemoveDormantTracks()` 全部删除）
- `docs/requirements/simulation.md:149`（SIM-F-081 明确“明细入口 + 逐条删除”）
- `docs/plans/simulation-implementation-plan.md:334`（计划“横幅、明细、逐条/全部清理”）

**实测/代码证据**

- 页面没有任何 Dormant 轨道列表/展开入口；无逐行删除按钮。
- store 已有 `TryRemoveTrack(primitiveId, propertyName)`（`SimulationConfigStore.cs:171-189`），可在不解析目标图元的情况下按 key 删除，因此 UI 侧实现成本不高。
- 删除全部是可撤销的（`RemoveDormantTracks_RemovesAndUndoRestores` 通过）。

**影响**

- 用户无法判断哪条轨道失效、无法只清理一条；只能全删所有 Dormant（可能误删将来会通过撤销恢复的图元对应轨道）。
- 需求硬性能力缺失，文档状态“已实现”高估。

**最小修复方案**

- 横幅增加 `<details>`/列表：显示 `PrimitiveId`（可截断）、`PropertyName`、关键帧数；每行一个 “Remove” 按钮，调用 `ConfigStore.TryRemoveTrack(track.PrimitiveId, track.PropertyName, out error)`；保留 “Remove all”。
- 通过 `@key` 稳定渲染；列表区域使用 `role="region"`/表格语义；更新 `aria-live` 计数。
- 在 `SimulationPage` 中把 `_dormantTracks` 传给新组件或内联渲染；`HandleConfigChanged` 已刷新。
- 测试：bUnit 两条 Dormant 轨道→只删一条→另一条仍在、Undo 恢复；E2E 同样。

---

## 3. 测试与环境结果

### 3.1 门禁与命令（本次实际运行）

| 命令 | 结果 |
| --- | --- |
| `git status --short` / `git diff --stat` | 21 个已跟踪修改（+1400/−107），新增 `src/Penelopa.Core/Simulation/`、`src/Penelopa.Web/Components/Simulation/`、`SimulationPage.razor`、`src/Penelopa.Web/Services/`、`docs/{requirements,plans,adr/0005}`、仿真 CSS/测试；`.qoder-credits/` 按要求忽略 |
| `dotnet build Penelopa.sln --no-restore` | **成功，0 警告/0 错误** |
| `dotnet test Penelopa.sln --no-restore` | **374 Core + 46 Rendering + 38 Ui = 458 passed，0 failed，0 skipped** |
| `dotnet format Penelopa.sln --verify-no-changes --no-restore` | **exit 0** |
| `npm run check:js` | 通过（tsc --noEmit） |
| `npm run test:js` | **21 passed**（含帧循环/可见性/键盘/rect/focus/capture） |
| TS ↔ JS 同步 | build 前/后 `penelopa.js` MD5 均为 `4c22ec35ab72b129344fe3691e181c47`，**无漂移** |
| `cd tests/e2e && npx playwright test` | **30 passed，4 skipped**（perf 未开 PERF）；与文档 30 一致 |
| `cd tests/e2e && PERF=1 npx playwright test tests/perf.spec.ts tests/simulation-perf.spec.ts` | **4 passed**；详情见 3.3 |
| 覆盖率（旧报告移出后） | **Core 92.73%（2869/3094），Rendering 91.91%（545/593）**，`check-coverage.py` 通过；旧报告已移至 `artifacts/coverage-review-old-20261026/` |

### 3.2 覆盖率新数据（本次 `--collect:"XPlat Code Coverage"`）

| 文件 | 行覆盖 |
| --- | ---: |
| `Simulation/` 汇总 | **93.46%（1100/1177）** |
| `SimulationBoundsEstimator.cs` | **83.5%（91/109）** |
| `SimulationConfigStore.cs` | 90.4%（312/345） |
| `SimulationRuntime.cs` | 95.2%（236/248） |
| `SimulationValidator.cs` | 96.2%（152/158） |
| `DocumentSerializer.cs` | 91.4%（320/350） |
| `Primitives/PropValue.cs` | 88.3%（182/206，含既有未覆盖行） |
| `SimulationEvaluator/Timeline/Resolver/...` | 96.5%–100% |

整程序集门禁通过；若按 SIM-NF-031 “新增核心代码 ≥90%”逐文件理解，Estimator 未达标（S-14）。

### 3.3 性能实测（Windows Debug，headless Chromium 1440×900）

| 场景 | 结果 |
| --- | --- |
| 空文档 idle CPU | **0.06%** |
| 500 矩形 idle CPU（编辑器） | **0.02%** |
| 500 图元 + 100 Active 轨道 | **59.9 tick/s**（300 ticks / 5.01s），**idle 2.53%** |
| 60s Loop soak | JS heap **5,634,108 → 5,538,932 bytes（−0.1 MiB）**；DOM **3021→2749**；listeners **62→32** |
| 选中 200 关键帧轨道播放 | **57.25 tick/s**（本次补充） |
| 500/100 并选中一个带轨道图元 | **60.03 tick/s**（本次补充） |

- 文档中的 59.9–60.1、3.5%、5.6MiB、3021→2749、62→32 与本次结果一致或接近（本次 idle 更低）；数字不是伪造。
- 但 “FPS” 是 JS→.NET tick，不等于实际绘制帧；100 轨道用例未选中图元/轨道；200 帧选中场景掉到 57.25（S-06/S-13）。

### 3.4 定向浏览器复现（本次补充，非仓库测试）

| 检查 | 结果 |
| --- | --- |
| 画布是否随播放头变化 | t=0 与 t=0.5 的 canvas 截图 SHA-256 不同（4269/4268 bytes）→ **通过** |
| 进入仿真页焦点 | `activeElement` = **BODY** → **失败**（S-09） |
| Back to editor 焦点 | `activeElement` = `A` “Simulate” → **通过** |
| 浏览器后退恢复 | 模型 Save=10，但 PosX 输入 20ms/1.6s 均为 58.951/57.484 → **失败**（S-02） |
| Esc 取消拖动 | 第 1 帧仍 `0.000s` → **通过**（但无 E2E） |
| 刻度点击 seek | 点击第 2 个 tick，时间 0.000→0.000 → **失败**（S-08） |
| 双击关键帧 Go to | 0.500→0.500 → **失败**（S-08） |
| 1024×768 布局 | `scrollWidth=1024`，三栏 + 时间轴全部可见，canvas 542×518 → **通过** |
| 保存失败（setItem 抛错） | 错误提示显示，storage 仍 null，无 pageerror → 行为通过；消息含完整堆栈（S-16） |
| 删除→撤销→重进仿真 | inactive banner 0，Play enabled → **通过**（S-04 的逐条仍缺） |
| 有效 Active + 非法孤儿轨道加载 | 整段配置被丢弃、活动轨道丢失、Play 禁用 → **失败/需求冲突**（S-05） |
| `Primitives:[null]` 加载 | NRE、console.error、Atlas 错误边界、无 pageerror → **失败**（S-03/S-11） |

### 3.5 未验证（明确）

- 实际**绘制帧率**（仅 tick/回调计数；无 SKGLView paint 计数器或 CDP frame 时间）。
- .NET WebAssembly 线性内存/原生内存是否在 60s 内有增长；只测了 JS heap/DOM。
- Firefox/Safari/移动端/触摸/真实 DPR 切换；对比度/WCAG/axe；820px 以下布局。
- CI 的 Ubuntu 环境是否与本地 Windows 性能/E2E 时序一致；性能用 Debug 构建，非 Release。
- 10k 图元、2000 轨道、10000 关键帧下的编辑/Fit 延迟；4MiB 上限边界。
- 保存/加载与浏览器多标签并发、长时间 soak（>60s）、本地存储 quota 之外的安全限制。
- 所有 76 条 SIM-F 的完整人工逐条浏览器验收；矩阵中的 [码] 行仅为静态推断。

---

## 4. 测试缺口与 flaky 风险

### 4.1 已有测试的优点

- Core 单测覆盖面广：求值边界、缓动/Bezier、时间轴循环/夹取、运行时状态机/恢复、配置 50 步撤销/Dormant/ExportActive、序列化 v1/v2/非法段/孤儿/值约束。
- bUnit 覆盖树/传输条/检视器/关键帧编辑器/时间轴基本渲染；JS harness 覆盖帧循环/可见性/键盘/焦点/rect/capture。
- E2E 覆盖 11 条仿真主流程，30 项全绿；性能 4 项有真实数字。

### 4.2 缺口（建议新增）

| 缺口 | 建议测试 |
| --- | --- |
| **浏览器后退/前进**离开（P1 实际缺陷） | Play→`page.goBack()`→不重选断言属性/画布基值；前进/后退双向 |
| 进入焦点到 h1 | `activeElement` 断言 |
| 刻度点击/双击 Go to | tick click seek、marker dblclick seek |
| Esc 取消拖动 | 已有手工通过，纳入 E2E |
| 保存失败（quota/JS 异常） | 注入 setItem 抛错；断言错误提示、存储不变、无堆栈 |
| `Primitives:[null]`/`Children:[null]` | 单测 + E2E console.error/错误边界 |
| 非法 Active + 非法 Dormant/孤儿混合 | 断言 Active 保留、Dormant 剪除+警告（S-05 决策后） |
| v1 加载/无 sim 清空配置/清空配置撤销 | E2E 或 bUnit 页面级 |
| 键盘-only 全路径 | 从 Simulate 链接到建轨/编辑/Go to/Play/Pause/Stop，只用 Tab/Enter/方向键 |
| 关键帧索引漂移 | 4 帧选中第 3→删第 2/撤销→断言选择不跳到别的帧 |
| Dormant + 配置撤销/清理 | 逐条删除+Undo、Remove all+Undo、保存确认+Undo |
| 1024×768 仿真布局 | 断言无横向溢出、所有控件可见 |
| 画布实际变化 | 截图 hash/像素 smoke（不做脆弱的像素级断言） |
| 20Hz 播报 | accessibility snapshot/aria-live 断言，确认数值不 live |
| 20Hz 性能 | 选中 200 帧轨道 + 播放，真实绘制/tick 门禁 |
| 保存 in-flight/双击 | 断言只保存一次、确认不可绕过 |
| Dormant 明细/逐条 | S-04 修复后 |
| 取消拖动时 pointer capture 丢失 | 模拟 lostpointercapture |
| Fit 在 Preview/Paused/Finished | 断言 bounds 更新且当前帧重新应用、属性无残留 |

### 4.3 Flaky 风险

- `simulation.spec.ts:99,102,104`（150/100/250ms）、`:255`（1.2s Finished）、`:347`（拖动前 100ms）、`:314-320`（覆写 `visibilityState`）。
  - 1.2s 等待对 1s 时长在慢机/CI 上仍可能不足；后面的 `toBeVisible({timeout:3000})` 会兜底，但等待本身浪费时间且掩盖真实时序。
  - 拖动前 100ms 可能不足以完成 `BeginDragAsync` 的 select→JS rect→pointer capture；在 Ubuntu CI 上可能偶发 `0.000s`。
  - visibilityState 覆写不会恢复；测试内后续无影响，但建议用 Playwright 的 `page.evaluate` + `context` 隔离并恢复。
- 性能测试的固定 5s/60s 采样可接受；但 heap 阈值 32MB（0.53MB/s 泄漏也能通过）、DOM +200、listeners +2 偏宽；未多次采样。
- E2E 只跑 Chromium/headless；UI/WebGL 在 Firefox/Safari/移动端未验证。
- E2E 全量 30 项在本地一次通过，未在 CI 复跑；本次未做多次重复以估计 flake rate。

### 4.4 “假绿”风险

1. **pageerror-only**：S-03 的加载崩溃只产生 console.error + 错误边界；现有断言仍通过（S-11）。
2. **tick 计数当 FPS**：S-13；慢绘制/20Hz 面板开销可能不影响 tick。
3. **未选中图元的性能用例**：100 轨道用例没有选中/展开检视器与时间轴，未覆盖 20Hz 消费者（S-06）。
4. **Save/Load 只测 happy path**：Save(dormant)→Load 交叉、Save 失败、加载警告的 JSON 内容均未断言；P0 因此漏网。
5. **保存后重载**只断言 2 行/Value 100，不断言 Loop/插值/几何未变；验收 #8 的证据不足。
6. **覆盖率门禁**整程序集，不证明新增代码；Estimator 83.5%。

---

## 5. 性能、无障碍、安全、文档一致性评估

### 5.1 性能

- **达标**：500 图元 + 100 轨道的仿真播放 59.9 tick/s；Stop 后 idle 2.53%（<5%）；编辑器 idle 0.06%/500 矩形 0.02%；60s soak JS heap/DOM/listener 稳定。
- **风险/不足**：
  - tick ≠ paint；缺少真实绘制帧/帧时间指标。
  - 20Hz 读数重渲染整个检视器（最多 200 行表）与时间轴（最多 200 marker）；选中 200 帧轨道时 57.25 tick，低于 60 目标。应拆小组件/播放头子组件。
  - 性能用例未选中图元/轨道，未覆盖上述读取消耗路径。
  - `SimulationBoundsEstimator` 每次 Ready 配置编辑都全量采样（计划要求缓存）；10k 图元/2000 轨道规模下可能卡顿。
  - render-only 仍分配/保留命中位图（`CanvasRenderer.EnsureBuffersFor`），内存/初始化略高，但不影响正确性。
  - .NET WASM 内存未测；heap 阈值宽。文档应避免把 JS heap/DOM 稳定写成“内存无增长”。

### 5.2 无障碍

- **做得好**：画布 `role="img"` + 描述；原生按钮/名称；状态/Dormant polite live；20Hz 数值不 live；时间/值/插值/Bezier 有 label 与 `aria-describedby`；关键帧时间轴有键盘可编辑检视器 + Go to；不自动播放；reduced-motion 提示；返回焦点到 Simulate 链接。
- **缺口**：
  - 进入页 h1 焦点失败（BODY）——SIM-NF-017 未满足，文档失实（S-09）。
  - 错误/保存确认使用 `role="alert"`，需求要求错误汇总 `aria-live="polite"`；ToolPanel 文档消息无 live（S-18）。
  - 关键帧 marker 0.7rem（约 11px）触发目标偏小；Fit/工具栏按钮尺寸可接受。
  - `@key="_interpolation"` 重建 select 可能丢键盘焦点（未实测）。
  - 对比度/WCAG 未测；没有 axe 自动化。
  - 无完整键盘-only E2E；拖动有 Esc（手工通过）。
  - `role="img"` 与 Fit 按钮的兼容性正确（按钮在 img 外）；树/时间轴仍可键盘访问。

### 5.3 安全

- **正向**：用户内容（图元名/属性名/错误）以文本渲染，无 `MarkupString`/`innerHTML`；属性名来自固定模型，`FieldId` 有 sanitize；`focusElement` 虽接收 selector 但当前生产未调用；文档/仿真段有类型、数量、时间、值、Bezier、4MiB 校验；重复 Id/空 Id/未知类型/未知插值/未来版本被拒绝；Dormant/孤儿剪除；无 CSP 变更（既有缺口）。
- **缺陷**：
  - `Primitives:[null]`/`Children:[null]` 崩溃（S-03）——不可信 localStorage 的输入处理不完整。
  - 保存错误泄漏 JS 堆栈/框架 URL（S-16）。
  - 无 CSP/安全头（既有，非本次范围）；localStorage 仅同源，但 XSS 风险仍取决于整体应用；本次未引入新的 HTML 注入面。
  - `data-testid` 进入生产 DOM 和焦点 selector（S-20/S-23 风格），不构成安全漏洞但属测试泄漏。

### 5.4 文档一致性

- **一致且可复现**：
  - 测试数量：374+46+38=458、21 JS、30 Playwright、4 perf；与本次完全一致。
  - 覆盖率：Core 92.73%、Rendering 91.91%；本次完全一致。
  - 性能：59.9 tick、heap 5.6MiB、DOM 3021→2749、listeners 62→32；与文档数字一致（idle 本次 2.53% vs 文档约 3.5%，属近似/环境差异）。
  - 顶栏“静态”已清理为“状态栏静态、顶栏承载 Simulate”；README/架构已更新；Version 1 已改为 v2；链接/表格存在。
- **不一致/过度承诺**：
  - `docs/en/architecture.md:405` “h1 focused on navigation” 与实测 BODY 不符。
  - `:399-401` 把 tick 回调写成 FPS；`requirements` 也把 59.9–60.1 称为 FPS。
  - 需求/计划宣称 Dormant 明细/逐条（`:149`/`:334`）未实现；阶段偏差未记录。
  - 计划 `:144` “Newton + 二分” 实际纯二分；`:470-471` Estimator 缓存未实现；阶段 1 测试文件名与实际不同；偏差记录不完整。
  - README `:123-128` 的 PERF 命令只列 `tests/perf.spec.ts`，未列 `tests/simulation-perf.spec.ts`；README.zh 无性能段。
  - 需求 `:301` 的 E2E 覆盖列表未包含浏览器后退/进入焦点/刻度/双击/console.error；SIM-NF-032 的“断言无控制台错误”未做。
  - 需求状态“已实现”对 SIM-F-026/054/067/081/101/103 等高估；建议逐条修订为 partial 或明确未实现项。
  - “60s 内存无增长”仅在 JS heap/DOM 范围成立；requirements §10 对 .NET 内存的 caveat 写得好，但 plan/architecture 的措辞应同步。

---

## 6. 修复计划（P0 → P3）

### P0（必须，先于合并）

**1. S-01 ToolPanel pending save 跨 Load 数据丢失**

- 范围：`src/Penelopa.Web/Components/Panels/ToolPanel.razor`（Load/Save 确认状态）、可能的单一 pending 状态类型；不需要改 Core。
- 最小实现：
  - `LoadDocument()` 开始：取消/清空 `_pendingSave`、`_pendingDormantCount`；或禁用 Load while `_pendingSave` 并提示。
  - `ConfirmLoad()`/`CancelLoad()`：同样清理（防未来调用路径）。
  - `ConfirmDormantSave()`：在确认时重新 `ExportActive()` 并重新计算 dormant；如果变化，重新展示确认，不直接用旧 payload。
  - 单一 `_pendingOperation` 可避免 Save/Load 两个确认共存。
- 验证：bUnit 状态机 + E2E（Save dormant → Load → Replace → 读取 localStorage；断言 Track 数不变、无旧横幅/误报成功的保存；正常 dormant 保存仍排除）。
- 退出标准：所有 Load/Save/Cancel 交叉状态有测试；无可达路径把旧配置写到新文档。

### P1（高优先，建议同一迭代）

**2. S-02 浏览器后退恢复可见状态**

- 范围：`SimulationPage.razor`（Dispose/导航/通知）、可能 `DocumentStorage` 无关；Core runtime 不改语义。
- 实现顺序：`Stop()` 首行 → 订阅/处理 `LocationChanged` → 恢复后 `PrimitiveService.NotifyPrimitivesChanged`（或新增编辑器重同步机制）→ 补 E2E。
- 验证：`page.goBack()` 后无需重选，PosX 输入=10、画布基值、Save=10；Back 按钮/Play/Preview/Paused/Finished 路径。
- 退出标准：所有退出路径（Back、浏览器后退/前进、直接导航、Dispose、异常）模型与编辑器可见状态一致。

**3. S-03 null 文档元素加固**

- 范围：`DocumentSerializer.Deserialize/FromDto`、`DocumentStorage.LoadAsync` 边界。
- 实现：null 检查 + 现有 catch 过滤/边界兜底；补单测与 E2E（console.error/error boundary）。
- 退出标准：任何合法 JSON 形状的坏元素都不会抛 NRE 到渲染器；Load 显示可读错误并保留当前文档。

**4. S-04 Dormant 明细/逐条**

- 范围：`SimulationPage.razor`、可能新增 `SimulationDormantList.razor`；store 的 `TryRemoveTrack` 已足够。
- 实现：明细列表 + 行删除 + 保留全部删除/撤销；a11y 表格/按钮名称。
- 验证：bUnit/E2E 两条只删一条、Undo、保存确认仍正确。
- 退出标准：SIM-F-081 完整满足；需求状态可改为 met。

### P2（下一轮，与 P0/P1 可并行但不应阻塞紧急修复）

1. **S-05**：明确 Dormant 结构非法语义；优先“先解析/剪除 Dormant，再对 Active 校验”；单测有效 Active + 非法 Dormant + 浏览器复现。
2. **S-06**：拆分 20Hz 读数消费者（时间/每轨当前值/播放头小组件）；避免整表/整时间轴重渲染；性能用例选中 200 帧轨道，目标 ≥59 tick 或真实绘制帧。
3. **S-07**：加入“Values are relative to the parent”提示；更新截图/文档。
4. **S-08**：ruler click seek、marker dblclick Go to；E2E。
5. **S-09**：h1 `tabindex="-1"`/显式聚焦；E2E 进入焦点；更新架构文档。
6. **S-10**：稳定关键帧选择标识/删除后索引调整；bUnit。
7. **S-11**：E2E 统一 console.error + error boundary 断言；新增 S-03 用例。
8. **S-12**：用 polling/expect 替代固定等待；补浏览器后退/进入焦点/Esc/保存失败/键盘-only/1024/索引漂移/Dormant+撤销用例；CI 至少 Chromium。
9. **S-13**：真实绘制帧/帧时间指标；性能用例选中轨道；heap 多次采样/收紧；文档区分 tick/paint。
10. **S-14**：覆盖率门禁增加新增代码/文件级；Estimator 补 83.5% 缺口；CI 保留整程序集。
11. **S-15**：产品确认 SIM-F-101 的“行 + 汇总”是否仍要求；否则修订需求/文档，删除不可达承诺；或允许无效草稿并渲染行错误。

### P3（清理/文档）

- S-16 安全摘要保存/加载错误；S-17 统一 Preview 标签；S-18 错误汇总 polite/ToolPanel live；S-19 仿真页 pending 横幅清理；S-20 移除/隔离测试钩子与死代码；S-21 dispose/undo 守卫与异常上报；S-22 Estimator 缓存/跳过 hit buffer；S-23 文档修订（h1、FPS、Dormant 明细、Newton/cache、README perf、测试文件名、偏差记录）；S-24 viewport/标记尺寸/对比度；S-25 in-flight Save/双击/文档变化取消 pending。

---

## 7. 未决问题与需要作者确认的假设

1. **SIM-F-096 与 SIM-F-080/103 的冲突**：任一结构非法的 Dormant/孤儿轨道是否应导致整段仿真被丢弃？本次实现选择“整段丢弃”（符合 SIM-F-096 字面），但会丢失有效 Active 轨道（与 SIM-F-080/103 精神冲突）。请产品/需求确认优先级；建议先解析目标、剪除 Dormant，再校验 Active。
2. **SIM-F-081 的“明细入口 + 逐条删除”是否 v1 必须**：需求与计划都写了，当前实现只有全部清理；若确认必须，应作为 P1 修复并更新测试。
3. **SIM-F-101 的“对应行 + 汇总”错误 UI**：store 设计为永不接受非法配置，因此该状态不可达；是保留 store 强校验并删除该承诺，还是允许草稿非法并渲染行/汇总错误？
4. **性能“FPS”的定义**：验收要求 55FPS/20Hz 不掉帧；当前用 tick 回调计数。是否接受 tick 作为代理，还是需要 SKGLView 绘制计数/帧时间？
5. **新增代码覆盖率口径**：SIM-NF-031 的 90% 是整程序集还是新增文件/新增行？按文件看 Estimator 83.5% 未达标。
6. **编辑器从仿真页返回是否必须保留 zoom/pan/dock**：本次页面重建会重置视口/布局（selection/document/config 保留）；需求未写，但对设计验证工作流有实际影响。
7. **Preview 主按钮文案**：需求状态表写 Play，实现显示 Resume；以哪个为准？
8. **“相对父级”提示的范围**：是全局提示、仅容器子项还是每个值旁？实现完全缺失；需 UI 文案确认。
9. **浏览器后退的预期**：需求 SIM-F-019 明确要求先结束会话并恢复；是否接受“模型恢复但编辑器面板/画布延迟刷新”，还是必须无陈旧显示（本次按后者判 P1）。
10. **Dormant 保存确认的取消语义**：Load 是否应自动取消未确认的 Save、还是禁止 Load；需要产品确认交互。

---

## 8. 审查限制

- 性能/E2E 为本地 Windows Debug + headless Chromium 单次运行，不是 CI/Release 多机基准；CI 的 Ubuntu 时序可能不同。
- 未审计 Atlas.Blazor 内部实现；未做完整 WCAG/axe、安全渗透、供应链签名、恶意文档模糊测试。
- 未验证 .NET WASM 线性内存、原生 Skia 内存、GPU 内存；只测 JS heap/DOM/listener。
- 未覆盖 Firefox/Safari/移动端/触摸/真实 DPR 切换/820px 以下布局。
- 未做长时间（>60s）soak、多标签并发、保存/加载竞态、10k 图元/2000 轨道/10000 关键帧规模基准。
- 部分矩阵项为静态推断（标 `[码]`），未逐条浏览器验收；明确标 `[未]` 的项没有运行证据。
- 本报告不修改业务代码；P0/P1 修复方案等待作者确认后执行。

---

*本报告只读生成；未修改源码、未提交、未推送。*
