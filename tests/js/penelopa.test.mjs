import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const source = fs.readFileSync(
  new URL('../../src/Penelopa.Web/wwwroot/js/penelopa.js', import.meta.url),
  'utf8');

function createHarness() {
  const windowListeners = new Map();
  const documentListeners = new Map();
  const canvasListeners = new Map();
  const calls = [];
  const rect = { left: 10, top: 20, width: 800, height: 600, right: 810, bottom: 620 };

  const addListener = (map) => (type, handler) => {
    const handlers = map.get(type) ?? new Set();
    handlers.add(handler);
    map.set(type, handlers);
  };
  const removeListener = (map) => (type, handler) => {
    map.get(type)?.delete(handler);
  };

  // 内层 canvas：border-box 与 outer 对齐，内容原点再偏移 clientLeft/Top
  const surface = {
    clientLeft: 0,
    clientTop: 0,
    getBoundingClientRect: () => ({ left: rect.left, top: rect.top }),
  };
  let focused = false;
  const canvasEl = {
    addEventListener: addListener(canvasListeners),
    removeEventListener: removeListener(canvasListeners),
    // 每次返回副本，模拟浏览器读取最新布局
    getBoundingClientRect: () => ({ ...rect }),
    querySelector: (selector) => (selector === 'canvas' ? surface : null),
    setPointerCapture() {},
    releasePointerCapture() {},
    focus() { focused = true; },
    contains(target) { return target === this; },
    classList: { add() {}, remove() {} },
    dataset: {},
  };
  const mediaQueries = [];
  const storage = {
    data: new Map(),
    throwOnWrite: false,
    getItem(key) {
      return this.data.has(key) ? this.data.get(key) : null;
    },
    setItem(key, value) {
      if (this.throwOnWrite) {
        throw new Error(
          'quota exceeded\nError: at Storage.setItem (http://localhost/_framework/penelopa.js:1:1)');
      }
      this.data.set(key, String(value));
    },
  };
  const window = {
    devicePixelRatio: 1,
    localStorage: storage,
    addEventListener: addListener(windowListeners),
    removeEventListener: removeListener(windowListeners),
    matchMedia(query) {
      const handlers = new Set();
      const mq = {
        query,
        addEventListener(type, handler) {
          if (type === 'change') handlers.add(handler);
        },
        removeEventListener(type, handler) {
          if (type === 'change') handlers.delete(handler);
        },
        trigger() {
          for (const handler of [...handlers]) {
            handler({ matches: false });
          }
        },
      };
      mediaQueries.push(mq);
      return mq;
    },
  };
  const document = {
    visibilityState: 'visible',
    addEventListener: addListener(documentListeners),
    removeEventListener: removeListener(documentListeners),
  };
  class ResizeObserver {
    observe() {}
    disconnect() {}
  }

  const context = vm.createContext({
    window,
    document,
    ResizeObserver,
    requestAnimationFrame: (callback) => {
      callback();
      return 1;
    },
    console,
  });
  vm.runInContext(source, context);

  const dotNetRef = {
    invokeMethodAsync(name, ...args) {
      calls.push({ name, args });
      return Promise.resolve();
    },
  };
  const handle = window.penelopa.attachPointer(canvasEl, dotNetRef);

  return { window, document, canvasEl, surface, rect, calls, mediaQueries, windowListeners, documentListeners, canvasListeners, storage, handle, isFocused: () => focused };
}

function dispatch(map, type, event) {
  const handlers = map.get(type);
  assert.ok(handlers && handlers.size > 0, `listener '${type}' should be registered`);
  for (const handler of handlers) {
    handler(event);
  }
}

function wheelEvent(x, y, ctrlKey = true) {
  return {
    ctrlKey,
    clientX: x,
    clientY: y,
    deltaY: -100,
    defaultPrevented: false,
    preventDefault() {
      this.defaultPrevented = true;
    },
  };
}

function keyEvent(key, target, ctrlKey = true, shiftKey = false) {
  return {
    key,
    ctrlKey,
    shiftKey,
    target,
    defaultPrevented: false,
    preventDefault() {
      this.defaultPrevented = true;
    },
  };
}

test('first Ctrl+wheel works without a prior pointerdown', () => {
  const harness = createHarness();

  const event = wheelEvent(110, 70);

  assert.doesNotThrow(() => dispatch(harness.canvasListeners, 'wheel', event));
  assert.equal(event.defaultPrevented, true);
  assert.deepEqual(harness.calls, [{ name: 'OnWheel', args: [100, 50, -100] }]);
});

test('pointer coordinates use the inner canvas content origin', () => {
  const harness = createHarness();
  // 1px 内层边框：内容原点 = border-box 原点 + clientLeft/Top
  harness.surface.clientLeft = 1;
  harness.surface.clientTop = 1;

  dispatch(harness.canvasListeners, 'wheel', wheelEvent(110, 70));

  assert.deepEqual(harness.calls.at(-1), { name: 'OnWheel', args: [99, 49, -100] });
});

test('wheel uses fresh coordinates after scrolling', () => {
  const harness = createHarness();
  dispatch(harness.canvasListeners, 'wheel', wheelEvent(110, 70));

  harness.rect.left = 30;
  harness.rect.top = 40;
  dispatch(harness.documentListeners, 'scroll', {});

  dispatch(harness.canvasListeners, 'wheel', wheelEvent(130, 90));

  const last = harness.calls.at(-1);
  assert.deepEqual(last, { name: 'OnWheel', args: [100, 50, -100] });
});

test('non-Ctrl wheel is left to the page', () => {
  const harness = createHarness();

  const event = wheelEvent(110, 70, false);
  dispatch(harness.canvasListeners, 'wheel', event);

  assert.equal(event.defaultPrevented, false);
  assert.deepEqual(harness.calls, []);
});

test('Ctrl+Z inside text inputs is not hijacked', () => {
  const harness = createHarness();

  for (const target of [
    { tagName: 'INPUT', isContentEditable: false },
    { tagName: 'TEXTAREA', isContentEditable: false },
    { tagName: 'SELECT', isContentEditable: false },
    { tagName: 'DIV', isContentEditable: true },
  ]) {
    const event = keyEvent('z', target);
    dispatch(harness.windowListeners, 'keydown', event);
    assert.equal(event.defaultPrevented, false);
  }

  assert.deepEqual(harness.calls, []);
});

test('Ctrl+Z outside text inputs triggers undo', () => {
  const harness = createHarness();

  const event = keyEvent('z', { tagName: 'DIV', isContentEditable: false });
  dispatch(harness.windowListeners, 'keydown', event);

  assert.equal(event.defaultPrevented, true);
  assert.deepEqual(harness.calls, [{ name: 'OnUndo', args: [] }]);
});

test('Escape inside an input does not cancel the canvas gesture', () => {
  const harness = createHarness();

  const event = keyEvent('Escape', { tagName: 'INPUT', isContentEditable: false }, false);
  dispatch(harness.windowListeners, 'keydown', event);

  assert.deepEqual(harness.calls, []);
});

test('Delete deletes the selection outside text inputs', () => {
  const harness = createHarness();

  const event = keyEvent('Delete', { tagName: 'DIV', isContentEditable: false }, false);
  dispatch(harness.windowListeners, 'keydown', event);

  assert.equal(event.defaultPrevented, true);
  assert.deepEqual(harness.calls, [{ name: 'OnDelete', args: [] }]);
});

test('Delete inside a property input is left to the input', () => {
  for (const target of [
    { tagName: 'INPUT', isContentEditable: false },
    { tagName: 'TEXTAREA', isContentEditable: false },
    { tagName: 'SELECT', isContentEditable: false },
    { tagName: 'DIV', isContentEditable: true },
  ]) {
    const harness = createHarness();
    const event = keyEvent('Delete', target, false);
    dispatch(harness.windowListeners, 'keydown', event);
    assert.equal(event.defaultPrevented, false);
    assert.deepEqual(harness.calls, []);
  }
});

test('Arrow keys nudge the selection only while the canvas is focused', () => {
  const harness = createHarness();

  const focusedEvent = keyEvent('ArrowRight', harness.canvasEl, false);
  dispatch(harness.windowListeners, 'keydown', focusedEvent);
  assert.equal(focusedEvent.defaultPrevented, true);
  assert.deepEqual(harness.calls, [{ name: 'OnNudge', args: [1, 0] }]);

  harness.calls.length = 0;
  const outsideEvent = keyEvent('ArrowRight', { tagName: 'DIV', isContentEditable: false }, false);
  dispatch(harness.windowListeners, 'keydown', outsideEvent);
  assert.equal(outsideEvent.defaultPrevented, false);
  assert.deepEqual(harness.calls, []);
});

test('Shift+Arrow nudges by 10 world units and maps screen directions', () => {
  const harness = createHarness();

  dispatch(harness.windowListeners, 'keydown', keyEvent('ArrowUp', harness.canvasEl, false, true));
  dispatch(harness.windowListeners, 'keydown', keyEvent('ArrowDown', harness.canvasEl, false));
  dispatch(harness.windowListeners, 'keydown', keyEvent('ArrowLeft', harness.canvasEl, false, true));

  assert.deepEqual(harness.calls, [
    { name: 'OnNudge', args: [0, 10] },
    { name: 'OnNudge', args: [0, -1] },
    { name: 'OnNudge', args: [-10, 0] },
  ]);
});

test('pointer down focuses the canvas for keyboard editing', () => {
  const harness = createHarness();

  dispatch(harness.canvasListeners, 'pointerdown', {
    target: harness.canvasEl,
    pointerId: 1,
    pointerType: 'mouse',
    button: 0,
    isPrimary: true,
    clientX: 110,
    clientY: 70,
    ctrlKey: false,
    shiftKey: false,
    preventDefault() {},
  });

  assert.equal(harness.isFocused(), true);
});

test('DPR watcher notifies, re-arms, and dispose removes the listener', () => {
  const harness = createHarness();
  const calls = [];
  const ref = {
    invokeMethodAsync(name, ...args) {
      calls.push({ name, args });
      return Promise.resolve();
    },
  };

  const handle = harness.window.penelopa.watchDpr(ref);
  assert.deepEqual(calls, [{ name: 'OnDevicePixelRatioChanged', args: [1] }]);

  harness.window.devicePixelRatio = 2;
  harness.mediaQueries.at(-1).trigger();
  assert.deepEqual(calls, [
    { name: 'OnDevicePixelRatioChanged', args: [1] },
    { name: 'OnDevicePixelRatioChanged', args: [2] },
  ]);

  handle.dispose();
  for (const mq of harness.mediaQueries) {
    mq.trigger();
  }
  assert.equal(calls.length, 2);
});

test('dispose removes the wheel listener', () => {
  const harness = createHarness();
  harness.handle.dispose();

  const handlers = harness.canvasListeners.get('wheel');
  assert.ok(!handlers || handlers.size === 0);
  assert.deepEqual(harness.calls, []);
});

function createFrameHarness({ perf = true } = {}) {
  const documentListeners = new Map();
  const windowListeners = new Map();
  const rafMap = new Map();
  const calls = [];
  let nextRafId = 1;
  let focused = false;

  const addListener = (map) => (type, handler) => {
    const handlers = map.get(type) ?? new Set();
    handlers.add(handler);
    map.set(type, handlers);
  };
  const removeListener = (map) => (type, handler) => {
    map.get(type)?.delete(handler);
  };

  const target = {
    focus() { focused = true; },
    getBoundingClientRect: () => ({ left: 12, width: 240 }),
  };
  const window = {
    __PENELOPA_PERF__: perf,
    matchMedia: (query) => ({ matches: query === '(prefers-reduced-motion: reduce)' }),
    addEventListener: addListener(windowListeners),
    removeEventListener: removeListener(windowListeners),
  };
  const document = {
    visibilityState: 'visible',
    addEventListener: addListener(documentListeners),
    removeEventListener: removeListener(documentListeners),
    querySelector: (selector) => (selector === '#target' ? target : null),
  };
  const context = vm.createContext({
    window,
    document,
    requestAnimationFrame: (callback) => {
      const id = nextRafId++;
      rafMap.set(id, callback);
      return id;
    },
    cancelAnimationFrame: (id) => {
      rafMap.delete(id);
    },
    Promise,
    console,
  });
  vm.runInContext(source, context);

  let failFrames = false;
  const loopErrors = [];
  const dotNetRef = {
    invokeMethodAsync(name, ...args) {
      calls.push(name);
      if (name === 'OnSimulationFrame' && failFrames) {
        return Promise.reject(new Error('frame boom'));
      }

      if (name === 'OnSimulationLoopError') {
        loopErrors.push(args[0]);
      }

      return Promise.resolve(name === 'GetPaintCount' ? 7 : undefined);
    },
  };

  return {
    window,
    document,
    dotNetRef,
    calls,
    loopErrors,
    setFailFrames(value) {
      failFrames = value;
    },
    rafMap,
    target,
    isFocused: () => focused,
    flushFrame() {
      const entry = rafMap.entries().next().value;
      if (!entry) {
        return false;
      }

      rafMap.delete(entry[0]);
      entry[1]();
      return true;
    },
    dispatchVisibility() {
      for (const handler of documentListeners.get('visibilitychange') ?? []) {
        handler();
      }
    },
    dispatchWindowKey(event) {
      for (const handler of windowListeners.get('keydown') ?? []) {
        handler(event);
      }
    },
  };
}

test('simulation frame loop invokes .NET per frame and stops on dispose', async () => {
  const harness = createFrameHarness();
  const handle = harness.window.penelopa.startSimulationLoop(harness.dotNetRef);

  assert.equal(harness.flushFrame(), true);
  await new Promise((resolve) => setImmediate(resolve));
  assert.deepEqual(harness.calls, ['OnSimulationFrame']);
  assert.equal(harness.window.penelopa.getSimulationTickCount(), 1);

  handle.dispose();
  assert.equal(harness.rafMap.size, 0);
});

test('simulation performance counters are absent without the perf flag', () => {
  const harness = createFrameHarness({ perf: false });

  assert.equal(harness.window.penelopa.getSimulationTickCount, undefined);
  assert.equal(harness.window.penelopa.getSimulationPaintCount, undefined);
  assert.equal(harness.window.penelopa.getSimulationFrameCount, undefined);
});

test('simulation frame loop reports unexpected errors through the dotnet callback', async () => {
  const harness = createFrameHarness();
  harness.setFailFrames(true);
  const handle = harness.window.penelopa.startSimulationLoop(harness.dotNetRef);

  harness.flushFrame();
  await new Promise((resolve) => setImmediate(resolve));

  assert.equal(harness.loopErrors.length, 1);
  assert.match(harness.loopErrors[0], /frame boom/);
  handle.dispose();
});

test('getSimulationPaintCount reads the active loop and resets after dispose', async () => {
  const harness = createFrameHarness();
  assert.equal(await harness.window.penelopa.getSimulationPaintCount(), 0);

  const handle = harness.window.penelopa.startSimulationLoop(harness.dotNetRef);
  assert.equal(await harness.window.penelopa.getSimulationPaintCount(), 7);

  handle.dispose();
  assert.equal(await harness.window.penelopa.getSimulationPaintCount(), 0);
});

test('simulation frame loop pauses and notifies when the document hides', async () => {
  const harness = createFrameHarness();
  const handle = harness.window.penelopa.startSimulationLoop(harness.dotNetRef);

  harness.flushFrame();
  await new Promise((resolve) => setImmediate(resolve));
  harness.document.visibilityState = 'hidden';
  harness.dispatchVisibility();
  await new Promise((resolve) => setImmediate(resolve));

  assert.deepEqual(harness.calls, ['OnSimulationFrame', 'OnSimulationHidden']);
  assert.equal(harness.rafMap.size, 0);
  handle.dispose();
});

test('getElementRect returns the current width', () => {
  const harness = createFrameHarness();

  const rect = harness.window.penelopa.getElementRect(harness.target);

  assert.equal(rect.width, 240);
});

test('prefersReducedMotion reflects the media query', () => {
  const harness = createFrameHarness();

  assert.equal(harness.window.penelopa.prefersReducedMotion(), true);
});

test('simulation keyboard undo/redo ignores text inputs and disposes', () => {
  const harness = createFrameHarness();
  const handle = harness.window.penelopa.attachSimulationKeyboard(harness.dotNetRef);

  harness.dispatchWindowKey(keyEvent('z', { tagName: 'DIV', isContentEditable: false }, true));
  harness.dispatchWindowKey(keyEvent('z', { tagName: 'INPUT', isContentEditable: false }, true));
  harness.dispatchWindowKey(
    keyEvent('z', { tagName: 'DIV', isContentEditable: false }, true, true));

  assert.deepEqual(harness.calls, ['OnConfigUndo', 'OnConfigRedo']);

  handle.dispose();
  harness.dispatchWindowKey(keyEvent('z', { tagName: 'DIV', isContentEditable: false }, true));
  assert.deepEqual(harness.calls, ['OnConfigUndo', 'OnConfigRedo']);
});

test('pointer capture helpers call the element methods', () => {
  const harness = createFrameHarness();
  const captured = [];
  const element = {
    setPointerCapture(id) { captured.push(['capture', id]); },
    releasePointerCapture(id) { captured.push(['release', id]); },
  };

  harness.window.penelopa.capturePointer(element, 7);
  harness.window.penelopa.releasePointer(element, 7);

  assert.deepEqual(captured, [['capture', 7], ['release', 7]]);
});

test('storage helpers return results instead of throwing', () => {
  const harness = createHarness();
  harness.storage.data.set('penelopa.document', '{"Version":2}');

  const existing = harness.window.penelopa.readStorage('penelopa.document');
  assert.equal(existing.ok, true);
  assert.equal(existing.value, '{"Version":2}');
  assert.equal(existing.error, null);

  const missing = harness.window.penelopa.readStorage('missing');
  assert.equal(missing.ok, true);
  assert.equal(missing.value, null);
  assert.equal(missing.error, null);

  const written = harness.window.penelopa.writeStorage('penelopa.document', 'next');
  assert.equal(written.ok, true);
  assert.equal(written.error, null);
  assert.equal(harness.storage.data.get('penelopa.document'), 'next');

  harness.storage.throwOnWrite = true;
  const failed = harness.window.penelopa.writeStorage('penelopa.document', 'next');
  assert.equal(failed.ok, false);
  assert.match(failed.error, /quota exceeded/);
  assert.doesNotMatch(failed.error, / at /);
  assert.equal(harness.storage.data.get('penelopa.document'), 'next');
});

test('downloadTextFile creates a temporary blob link and revokes it', () => {
  let clicked = false;
  let appended = false;
  let removed = false;
  let revoked = null;
  const blobs = [];
  const anchor = {
    href: '',
    download: '',
    rel: '',
    style: {},
    click() { clicked = true; },
    remove() { removed = true; },
  };
  class TestBlob {
    constructor(parts, options) {
      this.parts = parts;
      this.options = options;
      blobs.push(this);
    }
  }
  const window = {
    addEventListener() {},
    removeEventListener() {},
  };
  const document = {
    createElement(tag) {
      assert.equal(tag, 'a');
      return anchor;
    },
    body: {
      appendChild(node) {
        assert.equal(node, anchor);
        appended = true;
      },
    },
  };
  const URL = {
    createObjectURL(blob) {
      assert.equal(blob, blobs[0]);
      return 'blob:penelopa';
    },
    revokeObjectURL(url) {
      revoked = url;
    },
  };
  const context = vm.createContext({
    window,
    document,
    Blob: TestBlob,
    URL,
    setTimeout(callback) { callback(); },
    console,
  });
  vm.runInContext(source, context);

  window.penelopa.downloadTextFile(
    'penelopa-20261026-120000.penelopa.json',
    '{"Version":2}');

  assert.equal(clicked, true);
  assert.equal(appended, true);
  assert.equal(removed, true);
  assert.equal(anchor.download, 'penelopa-20261026-120000.penelopa.json');
  assert.equal(anchor.href, 'blob:penelopa');
  assert.equal(anchor.rel, 'noopener');
  assert.equal(blobs[0].parts[0], '{"Version":2}');
  assert.equal(blobs[0].options.type, 'application/json;charset=utf-8');
  assert.equal(revoked, 'blob:penelopa');
});

function createDropHarness() {
  const rootListeners = new Map();
  const windowListeners = new Map();
  const classes = new Set();
  const changes = [];
  let inputAvailable = false;
  let toolsSelected = false;

  const addListener = (map) => (type, handler) => {
    const handlers = map.get(type) ?? new Set();
    handlers.add(handler);
    map.set(type, handlers);
  };
  const removeListener = (map) => (type, handler) => {
    map.get(type)?.delete(handler);
  };

  class TestDataTransfer {
    constructor() {
      this.files = [];
      this.items = { add: (file) => this.files.push(file) };
      this.types = ['Files'];
      this.dropEffect = 'none';
    }
  }
  class TestEvent {
    constructor(type, init) {
      this.type = type;
      this.bubbles = init?.bubbles ?? false;
    }
  }

  const input = {
    disabled: false,
    files: null,
    dispatchEvent(event) {
      changes.push(event);
      return true;
    },
  };
  const toolsTab = {
    textContent: 'Tools',
    getAttribute(name) {
      return name === 'aria-selected' ? (toolsSelected ? 'true' : 'false') : null;
    },
    click() {
      toolsSelected = true;
      inputAvailable = true;
    },
  };
  const root = {
    classList: {
      add(name) { classes.add(name); },
      remove(name) { classes.delete(name); },
      contains(name) { return classes.has(name); },
    },
    addEventListener: addListener(rootListeners),
    removeEventListener: removeListener(rootListeners),
    querySelector(selector) {
      assert.match(selector, /input\[type="file"\]/);
      return inputAvailable ? input : null;
    },
    querySelectorAll(selector) {
      assert.equal(selector, '[role="tab"]');
      return [toolsTab];
    },
  };
  const window = {
    addEventListener: addListener(windowListeners),
    removeEventListener: removeListener(windowListeners),
  };
  const context = vm.createContext({
    window,
    document: {},
    DataTransfer: TestDataTransfer,
    Event: TestEvent,
    setTimeout(callback) { callback(); },
    console,
  });
  vm.runInContext(source, context);

  return {
    window,
    root,
    input,
    toolsTab,
    classes,
    changes,
    rootListeners,
    windowListeners,
    isToolsSelected: () => toolsSelected,
    dispatch(type, event) {
      for (const handler of rootListeners.get(type) ?? []) {
        handler(event);
      }
    },
  };
}

test('attachFileDrop activates Tools, assigns the dropped file, and cleans up', () => {
  const harness = createDropHarness();
  const file = { name: 'scene.penelopa.json', size: 12 };
  const dataTransfer = { types: ['Files'], files: [file], dropEffect: 'none' };
  let prevented = false;
  const event = {
    dataTransfer,
    preventDefault() { prevented = true; },
  };
  const handle = harness.window.penelopa.attachFileDrop(harness.root);

  harness.dispatch('dragenter', event);
  assert.equal(harness.classes.has('penelopa-drop-active'), true);

  harness.dispatch('drop', event);

  assert.equal(prevented, true);
  assert.equal(harness.isToolsSelected(), true);
  assert.deepEqual(Array.from(harness.input.files), [file]);
  assert.equal(harness.changes.length, 1);
  assert.equal(harness.changes[0].type, 'change');
  assert.equal(harness.classes.has('penelopa-drop-active'), false);

  handle.dispose();
  assert.equal(harness.rootListeners.get('drop')?.size ?? 0, 0);
  assert.equal(harness.windowListeners.get('drop')?.size ?? 0, 0);
  assert.equal(harness.classes.has('penelopa-drop-active'), false);
});

test('attachFileDrop ignores non-file drags', () => {
  const harness = createDropHarness();
  const dataTransfer = { types: ['text/plain'], files: [], dropEffect: 'none' };
  const event = {
    dataTransfer,
    preventDefault() { throw new Error('non-file drag must not be prevented'); },
  };
  harness.window.penelopa.attachFileDrop(harness.root);

  harness.dispatch('dragenter', event);
  harness.dispatch('drop', event);

  assert.equal(harness.classes.has('penelopa-drop-active'), false);
  assert.equal(harness.isToolsSelected(), false);
  assert.equal(harness.changes.length, 0);
});
