import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const source = fs.readFileSync(
  new URL('../../src/Penelopa/wwwroot/js/penelopa.js', import.meta.url),
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
  };
  const mediaQueries = [];
  const window = {
    devicePixelRatio: 1,
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

  return { window, document, canvasEl, surface, rect, calls, mediaQueries, windowListeners, documentListeners, canvasListeners, handle, isFocused: () => focused };
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
