"use strict";
// Penelopa 浏览器辅助脚本。
// 暴露当前设备像素比并监听其变化（窗口跨不同 DPI 的显示器移动会触发 matchMedia 变更），
// 使 .NET 画布渲染器的 CSS→物理映射保持同步。
// 同时挂载画布指针层：指针捕获、画布相对 CSS 坐标、合成鼠标事件抑制、rAF 节流 move 事件，
// 全部以语义回调形式报告给 .NET。
//
// 由 TypeScript 编译（见 tsconfig.json）；生成的 wwwroot/js/penelopa.js
// 随仓库提交，使项目在无 node 环境下也能运行。
window.penelopa = window.penelopa || {};
const simulationPerfEnabled = window.__PENELOPA_PERF__ === true;
// 页面拆卸期间 .NET 引用可能已释放；指针/键盘回调不应因此产生未处理异常。
function invoke(dotNetRef, methodName, ...args) {
    try {
        void dotNetRef.invokeMethodAsync(methodName, ...args).catch(() => {
            /* disposed */
        });
    }
    catch (_a) {
        /* disposed */
    }
}
window.penelopa.getDpr = function () {
    return window.devicePixelRatio || 1;
};
// localStorage 访问失败（配额、隐私模式、安全限制）不应作为未处理 JS 异常
// 穿过互操作边界；返回结果对象，由 .NET 边界显示安全摘要。
function describeStorageError(error) {
    var _a, _b;
    const message = error instanceof Error ? error.message : String(error);
    const firstLine = (_b = (_a = message.split(/\r?\n/)[0]) === null || _a === void 0 ? void 0 : _a.trim()) !== null && _b !== void 0 ? _b : '';
    const stackMarker = firstLine.indexOf(' at ');
    const summary = stackMarker > 0 ? firstLine.slice(0, stackMarker) : firstLine;
    return summary.startsWith('Error: ') ? summary.slice(7) : summary || 'unknown error';
}
window.penelopa.readStorage = function (key) {
    try {
        return { ok: true, value: window.localStorage.getItem(key), error: null };
    }
    catch (error) {
        console.error('[Penelopa] localStorage read failed', error);
        return { ok: false, value: null, error: describeStorageError(error) };
    }
};
window.penelopa.writeStorage = function (key, value) {
    try {
        window.localStorage.setItem(key, value);
        return { ok: true, error: null };
    }
    catch (error) {
        console.error('[Penelopa] localStorage write failed', error);
        return { ok: false, error: describeStorageError(error) };
    }
};
// 通过临时 Blob URL 触发浏览器下载；不经过服务器，文件内容不会离开本地。
window.penelopa.downloadTextFile = function (fileName, content) {
    const blob = new Blob([content], { type: 'application/json;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.rel = 'noopener';
    anchor.style.display = 'none';
    document.body.appendChild(anchor);
    try {
        anchor.click();
    }
    finally {
        anchor.remove();
        setTimeout(() => URL.revokeObjectURL(url), 0);
    }
};
// 编辑页工作区的文件拖放层。拖放产生的 FileList 交给现有 InputFile，
// 因而复用同一套确认、替换和取消逻辑；如果当前不在 Tools 标签，
// 会先激活它，再等待输入控件挂载。
window.penelopa.attachFileDrop = function (rootEl) {
    let depth = 0;
    let disposed = false;
    let dropToken = 0;
    const hasFiles = (event) => {
        var _a;
        const types = (_a = event.dataTransfer) === null || _a === void 0 ? void 0 : _a.types;
        return !!types && Array.from(types).indexOf('Files') >= 0;
    };
    const show = () => {
        rootEl.classList.add('penelopa-drop-active');
    };
    const hide = () => {
        depth = 0;
        rootEl.classList.remove('penelopa-drop-active');
    };
    const findInput = () => {
        const input = rootEl.querySelector('.penelopa-file-button input[type="file"]');
        return input ? input : null;
    };
    const activateToolsTab = () => {
        var _a;
        const tabs = rootEl.querySelectorAll('[role="tab"]');
        for (const tab of Array.from(tabs)) {
            if (((_a = tab.textContent) !== null && _a !== void 0 ? _a : '').trim() === 'Tools') {
                if (tab.getAttribute('aria-selected') !== 'true') {
                    tab.click();
                }
                return;
            }
        }
    };
    const assign = (files, token) => {
        if (disposed || token !== dropToken) {
            return;
        }
        const input = findInput();
        if (!input || input.disabled) {
            return;
        }
        try {
            // FileList 可能在 drop 事件结束后被清空；复制 File 引用，
            // 在真正投递时再构造一个新的 DataTransfer。
            const transfer = new DataTransfer();
            for (const file of files) {
                transfer.items.add(file);
            }
            input.files = transfer.files;
            input.dispatchEvent(new Event('change', { bubbles: true }));
        }
        catch (error) {
            console.error('[Penelopa] file drop failed', error);
        }
    };
    const deliver = (files, attempts, token) => {
        if (disposed || token !== dropToken) {
            return;
        }
        const input = findInput();
        if (input && !input.disabled) {
            // 新挂载的 InputFile 在 DOM 出现后还要一帧才完成事件绑定。
            setTimeout(() => assign(files, token), 100);
            return;
        }
        if (attempts <= 0) {
            console.warn('[Penelopa] file drop could not find the import input');
            return;
        }
        activateToolsTab();
        setTimeout(() => deliver(files, attempts - 1, token), 50);
    };
    const onDragEnter = (event) => {
        if (!hasFiles(event)) {
            return;
        }
        depth++;
        show();
    };
    const onDragOver = (event) => {
        if (!hasFiles(event)) {
            return;
        }
        event.preventDefault();
        if (event.dataTransfer) {
            event.dataTransfer.dropEffect = 'copy';
        }
        show();
    };
    const onDragLeave = () => {
        depth--;
        if (depth <= 0) {
            hide();
        }
    };
    const onDrop = (event) => {
        var _a;
        if (!hasFiles(event)) {
            hide();
            return;
        }
        event.preventDefault();
        const files = ((_a = event.dataTransfer) === null || _a === void 0 ? void 0 : _a.files)
            ? Array.from(event.dataTransfer.files)
            : [];
        hide();
        if (files.length === 0) {
            return;
        }
        const token = ++dropToken;
        deliver(files, 20, token);
    };
    const onDragEnd = () => hide();
    rootEl.addEventListener('dragenter', onDragEnter);
    rootEl.addEventListener('dragover', onDragOver);
    rootEl.addEventListener('dragleave', onDragLeave);
    rootEl.addEventListener('drop', onDrop);
    rootEl.addEventListener('dragend', onDragEnd);
    window.addEventListener('dragend', onDragEnd);
    window.addEventListener('drop', onDragEnd);
    window.addEventListener('blur', onDragEnd);
    return {
        dispose() {
            disposed = true;
            dropToken++;
            rootEl.removeEventListener('dragenter', onDragEnter);
            rootEl.removeEventListener('dragover', onDragOver);
            rootEl.removeEventListener('dragleave', onDragLeave);
            rootEl.removeEventListener('drop', onDrop);
            rootEl.removeEventListener('dragend', onDragEnd);
            window.removeEventListener('dragend', onDragEnd);
            window.removeEventListener('drop', onDragEnd);
            window.removeEventListener('blur', onDragEnd);
            hide();
        },
    };
};
window.penelopa.watchDpr = function (dotNetRef) {
    let mq = null;
    let disposed = false;
    const onChange = () => {
        if (disposed) {
            return;
        }
        // 为新比率重新挂载监听；每次分辨率变更触发一次，然后监听下一个比率。
        if (mq) {
            mq.removeEventListener('change', onChange);
        }
        const dpr = window.devicePixelRatio || 1;
        mq = window.matchMedia('(resolution: ' + dpr + 'dppx)');
        mq.addEventListener('change', onChange);
        invoke(dotNetRef, 'OnDevicePixelRatioChanged', dpr);
    };
    onChange();
    return {
        dispose() {
            disposed = true;
            if (mq) {
                mq.removeEventListener('change', onChange);
                mq = null;
            }
        },
    };
};
let simulationTickCount = 0;
let simulationDotNetRef = null;
// 仿真页的请求动画帧循环。每次 rAF 触发一次 OnSimulationFrame；
// .NET 回调未完成时跳过当前帧，避免积压。标签页隐藏时停止循环并通知页面暂停。
window.penelopa.startSimulationLoop = function (dotNetRef) {
    let rafId = 0;
    let disposed = false;
    let busy = false;
    let hidden = false;
    simulationTickCount = 0;
    if (simulationPerfEnabled) {
        simulationDotNetRef = dotNetRef;
    }
    const stop = () => {
        if (rafId !== 0) {
            cancelAnimationFrame(rafId);
            rafId = 0;
        }
    };
    const tick = () => {
        if (disposed || hidden) {
            return;
        }
        rafId = requestAnimationFrame(tick);
        if (busy) {
            return;
        }
        busy = true;
        if (simulationPerfEnabled) {
            simulationTickCount++;
        }
        Promise.resolve()
            .then(() => dotNetRef.invokeMethodAsync('OnSimulationFrame'))
            .catch(error => {
            const message = error instanceof Error ? error.message : String(error);
            void dotNetRef.invokeMethodAsync('OnSimulationLoopError', message).catch(() => {
                /* 页面可能已释放 */
            });
        })
            .then(() => {
            busy = false;
        });
    };
    const onVisibilityChange = () => {
        if (document.visibilityState !== 'hidden') {
            return;
        }
        hidden = true;
        stop();
        void dotNetRef.invokeMethodAsync('OnSimulationHidden').catch(() => {
            /* 页面可能已释放 */
        });
    };
    document.addEventListener('visibilitychange', onVisibilityChange);
    rafId = requestAnimationFrame(tick);
    return {
        dispose() {
            disposed = true;
            stop();
            document.removeEventListener('visibilitychange', onVisibilityChange);
            if (simulationDotNetRef === dotNetRef) {
                simulationDotNetRef = null;
            }
        },
    };
};
// 仿真页的配置撤销/重做快捷键。焦点在文本控件内时交还浏览器原生撤销。
window.penelopa.attachSimulationKeyboard = function (dotNetRef) {
    const onKeyDown = (event) => {
        const target = event.target;
        if (target) {
            const tag = target.tagName;
            if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || target.isContentEditable) {
                return;
            }
        }
        const modifier = event.ctrlKey || event.metaKey;
        const key = event.key.toLowerCase();
        if (modifier && !event.shiftKey && key === 'z') {
            event.preventDefault();
            void dotNetRef.invokeMethodAsync('OnConfigUndo').catch(() => {
                /* 页面可能已释放 */
            });
            return;
        }
        if (modifier && (key === 'y' || (event.shiftKey && key === 'z'))) {
            event.preventDefault();
            void dotNetRef.invokeMethodAsync('OnConfigRedo').catch(() => {
                /* 页面可能已释放 */
            });
        }
    };
    window.addEventListener('keydown', onKeyDown);
    return {
        dispose() {
            window.removeEventListener('keydown', onKeyDown);
        },
    };
};
if (simulationPerfEnabled) {
    const perf = window.penelopa;
    perf.getSimulationTickCount = function () {
        return simulationTickCount;
    };
    const readSimulationCounter = function (methodName) {
        const dotNetRef = simulationDotNetRef;
        if (!dotNetRef) {
            return Promise.resolve(0);
        }
        return dotNetRef.invokeMethodAsync(methodName)
            .then(value => typeof value === 'number' ? value : 0)
            .catch(() => 0);
    };
    // 性能基准用：读取 SKGLView 实际 paint 回调次数，避免把 tick 当作绘制帧。
    perf.getSimulationPaintCount = function () {
        return readSimulationCounter('GetPaintCount');
    };
    // 性能基准用：读取 .NET 实际执行的仿真帧数（tick 是 JS 回调尝试数）。
    perf.getSimulationFrameCount = function () {
        return readSimulationCounter('GetFrameCount');
    };
}
window.penelopa.getElementRect = function (element) {
    return { width: element.getBoundingClientRect().width };
};
window.penelopa.capturePointer = function (element, pointerId) {
    try {
        element.setPointerCapture(pointerId);
    }
    catch (_a) {
        /* 捕获是尽力而为；move 事件仍可能到达元素 */
    }
};
window.penelopa.releasePointer = function (element, pointerId) {
    try {
        element.releasePointerCapture(pointerId);
    }
    catch (_a) {
        /* 已释放 */
    }
};
window.penelopa.prefersReducedMotion = function () {
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
};
// 将指针交互层挂载到画布元素。
// 返回 { dispose } 句柄供清理使用。
window.penelopa.attachPointer = function (canvasEl, dotNetRef) {
    const state = {
        activePointerId: null,
        interacting: false,
        rect: null,
        lastPos: null,
        rafPending: false,
    };
    function refreshRect() {
        // 内层 canvas 可能带 1px 边框；指针坐标必须相对内容原点，
        // 与 SKGLView 的绘制尺寸/命中缓冲区保持一致。
        const surface = canvasEl.querySelector('canvas');
        if (surface) {
            const rect = surface.getBoundingClientRect();
            state.rect = {
                left: rect.left + surface.clientLeft,
                top: rect.top + surface.clientTop,
            };
        }
        else {
            const rect = canvasEl.getBoundingClientRect();
            state.rect = { left: rect.left, top: rect.top };
        }
    }
    // 布局/滚动变化后矩形失效，下一次坐标转换时重新读取。
    function invalidateRect() {
        state.rect = null;
    }
    function currentRect() {
        let rect = state.rect;
        if (!rect) {
            refreshRect();
            rect = state.rect;
        }
        return rect;
    }
    function toCss(e) {
        // 画布内容相对 CSS 坐标；origin 是视口相对的且已包含滚动偏移，
        // 因此在滚动容器内结果正确，无需额外计算。
        // 惰性刷新：首个事件可能是未按下的 Ctrl+滚轮，origin 尚未初始化。
        const origin = currentRect();
        return {
            x: e.clientX - origin.left,
            y: e.clientY - origin.top,
        };
    }
    function onPointerDown(e) {
        // 让覆盖层控件（Fit 按钮）处理自身的点击：
        // 下方的合成鼠标抑制不能吞掉它们的事件。
        const target = e.target;
        if (target && target.closest && target.closest('.penelopa-canvas-fit')) {
            return;
        }
        if (state.interacting) {
            return; // 已在交互中
        }
        if (e.pointerType !== 'mouse' && !e.isPrimary) {
            return; // 次级触摸指针不驱动编辑
        }
        if (e.pointerType === 'mouse' && e.button !== 0) {
            return; // 仅主键；右键交给 contextmenu
        }
        state.interacting = true;
        state.activePointerId = e.pointerId;
        refreshRect();
        try {
            canvasEl.focus({ preventScroll: true });
        }
        catch (_a) {
            /* 旧浏览器可能不支持 focus 选项；焦点仍会落到可聚焦元素上 */
        }
        try {
            canvasEl.setPointerCapture(e.pointerId);
        }
        catch (_b) {
            /* 捕获是尽力而为；move 事件仍会到达元素 */
        }
        canvasEl.classList.add('penelopa-dragging'); // touch-action: none
        e.preventDefault(); // 抑制拖拽后的合成鼠标事件
        const pos = toCss(e);
        invoke(dotNetRef, 'OnPointerDown', pos.x, pos.y, e.button, e.ctrlKey, e.shiftKey, e.pointerId);
    }
    function onPointerMove(e) {
        if (!state.interacting || e.pointerId !== state.activePointerId) {
            return;
        }
        state.lastPos = toCss(e);
        if (state.rafPending) {
            return;
        }
        state.rafPending = true;
        requestAnimationFrame(() => {
            state.rafPending = false;
            if (state.lastPos) {
                invoke(dotNetRef, 'OnPointerMove', state.lastPos.x, state.lastPos.y);
            }
        });
    }
    function endInteraction() {
        state.interacting = false;
        state.activePointerId = null;
        state.lastPos = null;
        canvasEl.classList.remove('penelopa-dragging');
    }
    function onPointerUp(e) {
        if (!state.interacting || e.pointerId !== state.activePointerId) {
            return;
        }
        const pos = toCss(e);
        endInteraction();
        try {
            canvasEl.releasePointerCapture(e.pointerId);
        }
        catch (_a) {
            /* 已释放 */
        }
        invoke(dotNetRef, 'OnPointerUp', pos.x, pos.y);
    }
    function onCancel() {
        if (!state.interacting) {
            return;
        }
        endInteraction();
        invoke(dotNetRef, 'OnPointerCancel');
    }
    function onLostPointerCapture(e) {
        if (e.pointerId === state.activePointerId) {
            onCancel();
        }
    }
    function onWindowBlur() {
        if (state.interacting) {
            onCancel();
        }
    }
    function onVisibilityChange() {
        if (document.visibilityState === 'hidden' && state.interacting) {
            onCancel();
        }
    }
    function onKeyDown(e) {
        // 文本编辑控件内的按键（包括 Ctrl+Z）交给控件自身处理，
        // 否则会删除图元并劫持输入框的文本撤销。
        const target = e.target;
        if (target) {
            const tag = target.tagName;
            if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || target.isContentEditable) {
                return;
            }
        }
        if (e.key === 'Escape') {
            invoke(dotNetRef, 'OnEscape');
            return;
        }
        if (e.key === 'Delete') {
            e.preventDefault();
            invoke(dotNetRef, 'OnDelete');
            return;
        }
        // 方向键只在可聚焦的画布容器本身获得焦点时移动选区，
        // 避免劫持页面/工具栏（包括画布内 Fit 按钮）的键盘导航。
        const canvasFocused = target === canvasEl;
        if (canvasFocused) {
            const step = e.shiftKey ? 10 : 1;
            let nudge = null;
            switch (e.key) {
                case 'ArrowLeft':
                    nudge = [-step, 0];
                    break;
                case 'ArrowRight':
                    nudge = [step, 0];
                    break;
                case 'ArrowUp':
                    nudge = [0, step];
                    break;
                case 'ArrowDown':
                    nudge = [0, -step];
                    break;
            }
            if (nudge) {
                e.preventDefault();
                invoke(dotNetRef, 'OnNudge', nudge[0], nudge[1]);
                return;
            }
        }
        const mod = e.ctrlKey || e.metaKey;
        const key = e.key.toLowerCase();
        if (mod && !e.shiftKey && key === 'z') {
            e.preventDefault();
            invoke(dotNetRef, 'OnUndo');
            return;
        }
        if (mod && (key === 'y' || (e.shiftKey && key === 'z'))) {
            e.preventDefault();
            invoke(dotNetRef, 'OnRedo');
        }
    }
    function onPointerEnter() {
        refreshRect();
    }
    function onWheel(e) {
        // 仅 Ctrl+滚轮缩放（浏览器约定）；普通滚轮留给页面/容器滚动。
        if (!e.ctrlKey) {
            return;
        }
        e.preventDefault();
        // 滚轮不经过 pointerdown，必须在这里刷新（首次事件时 rect 仍为 null）。
        refreshRect();
        const pos = toCss(e);
        invoke(dotNetRef, 'OnWheel', pos.x, pos.y, e.deltaY);
    }
    // 面板尺寸（Atlas splitter）变化时 getBoundingClientRect 会变化，无需等待滚动事件。
    const resizeObserver = typeof ResizeObserver === 'undefined'
        ? null
        : new ResizeObserver(() => invalidateRect());
    resizeObserver === null || resizeObserver === void 0 ? void 0 : resizeObserver.observe(canvasEl);
    canvasEl.addEventListener('pointerdown', onPointerDown);
    canvasEl.addEventListener('pointermove', onPointerMove);
    canvasEl.addEventListener('pointerup', onPointerUp);
    canvasEl.addEventListener('pointercancel', onCancel);
    canvasEl.addEventListener('lostpointercapture', onLostPointerCapture);
    canvasEl.addEventListener('pointerenter', onPointerEnter);
    canvasEl.addEventListener('wheel', onWheel, { passive: false });
    window.addEventListener('blur', onWindowBlur);
    window.addEventListener('resize', invalidateRect);
    document.addEventListener('scroll', invalidateRect, true);
    document.addEventListener('visibilitychange', onVisibilityChange);
    window.addEventListener('keydown', onKeyDown);
    canvasEl.dataset.pointerReady = 'true';
    return {
        dispose() {
            resizeObserver === null || resizeObserver === void 0 ? void 0 : resizeObserver.disconnect();
            canvasEl.removeEventListener('pointerdown', onPointerDown);
            canvasEl.removeEventListener('pointermove', onPointerMove);
            canvasEl.removeEventListener('pointerup', onPointerUp);
            canvasEl.removeEventListener('pointercancel', onCancel);
            canvasEl.removeEventListener('lostpointercapture', onLostPointerCapture);
            canvasEl.removeEventListener('pointerenter', onPointerEnter);
            canvasEl.removeEventListener('wheel', onWheel);
            window.removeEventListener('blur', onWindowBlur);
            window.removeEventListener('resize', invalidateRect);
            document.removeEventListener('scroll', invalidateRect, true);
            document.removeEventListener('visibilitychange', onVisibilityChange);
            window.removeEventListener('keydown', onKeyDown);
            canvasEl.classList.remove('penelopa-dragging');
            delete canvasEl.dataset.pointerReady;
        },
    };
};
