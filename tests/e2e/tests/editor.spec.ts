import { test, expect, Page } from '@playwright/test';

interface CanvasInfo {
  left: number;
  top: number;
  width: number;
  height: number;
}

async function ready(page: Page): Promise<void> {
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Circle', exact: true })).toBeVisible();
  await page.waitForTimeout(300);
}

async function canvasInfo(page: Page): Promise<CanvasInfo> {
  return page.locator('.penelopa-canvas canvas').evaluate((canvas) => {
    const rect = canvas.getBoundingClientRect();
    return {
      left: rect.left + canvas.clientLeft,
      top: rect.top + canvas.clientTop,
      width: canvas.width / (window.devicePixelRatio || 1),
      height: canvas.height / (window.devicePixelRatio || 1),
    };
  });
}

function clientOf(info: CanvasInfo, worldX: number, worldY: number, zoom = 1) {
  return {
    x: info.left + info.width / 2 + worldX * zoom,
    y: info.top + info.height / 2 - worldY * zoom,
  };
}

async function clickWorld(page: Page, worldX: number, worldY: number, zoom = 1, ctrl = false): Promise<void> {
  const info = await canvasInfo(page);
  const point = clientOf(info, worldX, worldY, zoom);
  if (ctrl) {
    await page.keyboard.down('Control');
  }

  await page.mouse.click(point.x, point.y);

  if (ctrl) {
    await page.keyboard.up('Control');
  }
}

async function dragWorld(
  page: Page,
  fromX: number,
  fromY: number,
  toX: number,
  toY: number,
  zoom = 1,
): Promise<void> {
  const info = await canvasInfo(page);
  const from = clientOf(info, fromX, fromY, zoom);
  const to = clientOf(info, toX, toY, zoom);
  await page.mouse.move(from.x, from.y);
  await page.mouse.down();
  await page.mouse.move(to.x, to.y, { steps: 8 });
  await page.waitForTimeout(80);
  await page.mouse.up();
}

async function addPrimitive(page: Page, name: string): Promise<void> {
  await page.getByRole('button', { name, exact: true }).click();
  await page.waitForTimeout(200);
}

function field(page: Page, name: string) {
  return page.locator('.penelopa-prop-field').filter({ hasText: name });
}

async function properties(page: Page): Promise<Record<string, string>> {
  return page.evaluate(() => {
    const result: Record<string, string> = {};
    for (const group of document.querySelectorAll('.penelopa-properties .penelopa-prop-field')) {
      const label = group.querySelector('label')?.textContent?.trim() ?? '';
      const input = group.querySelector('input, select') as HTMLInputElement | HTMLSelectElement | null;
      result[label] = input?.value ?? '';
    }

    return result;
  });
}

test('first Ctrl+wheel zooms without a prior canvas press', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await ready(page);

  const info = await canvasInfo(page);
  const center = clientOf(info, 0, 0);
  await page.mouse.move(center.x, center.y);
  await page.keyboard.down('Control');
  for (let i = 0; i < 10; i++) {
    await page.mouse.wheel(0, -100);
  }
  await page.keyboard.up('Control');
  await page.waitForTimeout(200);

  await addPrimitive(page, 'Circle');
  const zoom = Math.pow(1.1, 10);
  const zoomed = clientOf(await canvasInfo(page), 10, 10, zoom);
  await page.mouse.click(zoomed.x, zoomed.y);

  await expect(page.locator('.penelopa-properties-title')).toHaveText('Circle');
  expect(errors).toEqual([]);
});

test('default 10x10 rectangle is dragged, not resized', async ({ page }) => {
  await ready(page);
  await addPrimitive(page, 'Rectangle');
  await clickWorld(page, 15, 15);
  await dragWorld(page, 15, 15, 55, 15);

  await expect.poll(async () => (await properties(page)).PosX).toBe('50');
  const values = await properties(page);
  expect(values.PosY).toBe('10');
  expect(values.Width).toBe('10');
  expect(values.Height).toBe('10');
});

test('10x10 container allows drilling to its child', async ({ page }) => {
  await ready(page);
  await addPrimitive(page, 'Offset');
  await addPrimitive(page, 'Circle');

  await clickWorld(page, 30, 30);
  await expect(page.locator('.penelopa-properties-title')).toHaveText('Offset Container');
  await page.waitForTimeout(80);
  await clickWorld(page, 30, 30);

  await expect(page.locator('.penelopa-properties-title')).toHaveText('Circle');
});

test('property edit and align are undoable', async ({ page }) => {
  await ready(page);
  await addPrimitive(page, 'Circle');
  await clickWorld(page, 10, 10);
  const radius = page.locator('.penelopa-prop-field').filter({ hasText: 'Radius' }).locator('input');
  await radius.fill('20');
  await radius.press('Tab');
  await expect.poll(async () => (await properties(page)).Radius).toBe('20');

  await clickWorld(page, 150, -200);
  await page.keyboard.press('Control+z');
  await page.waitForTimeout(200);
  await clickWorld(page, 10, 10);
  await expect.poll(async () => (await properties(page)).Radius).toBe('5');

  await addPrimitive(page, 'Rectangle');
  await clickWorld(page, 18, 18);
  await clickWorld(page, 13, 7, 1, true);
  await page.getByRole('button', { name: '← Left', exact: true }).click();
  await page.waitForTimeout(200);
  await clickWorld(page, 8, 18);
  await expect.poll(async () => (await properties(page)).PosX).toBe('5');

  await page.keyboard.press('Control+z');
  await page.waitForTimeout(200);
  await clickWorld(page, 18, 18);
  await expect.poll(async () => (await properties(page)).PosX).toBe('10');

  await page.getByRole('tab', { name: 'Primitives' }).click();
  await expect(page.locator('.penelopa-tree-item')).toHaveCount(2);
});

test('Ctrl+Z inside a property input is left to the input and keeps the primitive', async ({ page }) => {
  await ready(page);
  await addPrimitive(page, 'Circle');
  await clickWorld(page, 10, 10);
  const radius = page.locator('.penelopa-prop-field').filter({ hasText: 'Radius' }).locator('input');
  await radius.fill('20');
  await radius.press('Tab');
  await expect.poll(async () => (await properties(page)).Radius).toBe('20');

  // 全局处理器跳过输入控件：不 preventDefault，也不触发文档撤销
  const prevented = await page.evaluate(() => {
    const input = [...document.querySelectorAll('.penelopa-prop-field')]
      .find((group) => group.querySelector('label')?.textContent?.trim() === 'Radius')
      ?.querySelector('input') as HTMLInputElement | null;
    if (!input) {
      return true;
    }

    input.focus();
    const event = new KeyboardEvent('keydown', {
      bubbles: true,
      cancelable: true,
      key: 'z',
      ctrlKey: true,
    });
    input.dispatchEvent(event);
    return event.defaultPrevented;
  });

  expect(prevented).toBe(false);
  expect((await properties(page)).Radius).toBe('20');

  // 真实按键交给浏览器原生文本撤销，图元不能被删除
  await radius.focus();
  await page.keyboard.press('Control+z');
  await page.waitForTimeout(150);
  await page.getByRole('tab', { name: 'Primitives' }).click();
  await expect(page.locator('.penelopa-tree-item')).toHaveCount(1);
});

test('triangle resize inside a container is exactly undone by ESC', async ({ page }) => {
  await ready(page);
  await addPrimitive(page, 'Offset');
  await addPrimitive(page, 'Triangle');
  await clickWorld(page, 80, 75);
  await page.waitForTimeout(80);
  await clickWorld(page, 80, 75);
  await expect(page.locator('.penelopa-properties-title')).toHaveText('Triangle');
  const original = await properties(page);

  await dragWorld(page, 90, 70, 120, 40);
  await expect.poll(async () => (await properties(page)).Vertex1Y).toBe('20');
  expect((await properties(page)).Vertex2X).toBe('100');
  expect((await properties(page)).Vertex3Y).toBe('60');

  await page.keyboard.press('Control+z');
  await page.waitForTimeout(200);
  await expect.poll(async () => (await properties(page)).Vertex1Y).toBe('50');

  const info = await canvasInfo(page);
  const from = clientOf(info, 90, 70);
  const to = clientOf(info, 120, 40);
  await page.mouse.move(from.x, from.y);
  await page.mouse.down();
  await page.mouse.move(to.x, to.y, { steps: 8 });
  await page.waitForTimeout(80);
  await page.keyboard.press('Escape');
  await page.mouse.up();
  await page.waitForTimeout(200);

  const restored = await properties(page);
  expect(restored.Vertex1X).toBe(original.Vertex1X);
  expect(restored.Vertex1Y).toBe(original.Vertex1Y);
  expect(restored.Vertex2X).toBe(original.Vertex2X);
  expect(restored.Vertex2Y).toBe(original.Vertex2Y);
  expect(restored.Vertex3X).toBe(original.Vertex3X);
  expect(restored.Vertex3Y).toBe(original.Vertex3Y);
});

test('parent and child cannot be selected together; group drag moves the child once', async ({ page }) => {
  await ready(page);
  await addPrimitive(page, 'Offset');
  await addPrimitive(page, 'Triangle');
  await clickWorld(page, 150, 0);
  await addPrimitive(page, 'Rectangle');

  await clickWorld(page, 80, 75);
  await page.waitForTimeout(80);
  await clickWorld(page, 80, 75);
  await expect(page.locator('.penelopa-properties-title')).toHaveText('Triangle');
  await clickWorld(page, 80, 75, 1, true);
  await expect(page.locator('.penelopa-properties-title')).toHaveText('Offset Container');
  await clickWorld(page, 18, 18, 1, true);

  await page.getByRole('tab', { name: 'Primitives' }).click();
  const items = page.locator('.penelopa-tree-item');
  const containerItem = items.filter({ hasText: 'Offset Container' });
  const triangleItem = items.filter({ hasText: 'Triangle' });
  const rectangleItem = items.filter({ hasText: 'Rectangle' });
  await expect(containerItem).toHaveClass(/selected/);
  await expect(rectangleItem).toHaveClass(/selected/);
  await expect(triangleItem).not.toHaveClass(/selected/);

  await dragWorld(page, 15, 15, 55, 15);

  await containerItem.click();
  await expect.poll(async () => (await properties(page))['Offset X']).toBe('60');
  await triangleItem.click();
  await expect.poll(async () => (await properties(page)).Vertex1X).toBe('50');
  await rectangleItem.click();
  await expect.poll(async () => (await properties(page)).PosX).toBe('50');
});

test('loading a no-selection document clears the property panel', async ({ page }) => {
  await ready(page);
  await addPrimitive(page, 'Circle');
  await clickWorld(page, 150, -200);
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.locator('.penelopa-tool-message')).toHaveText('Document saved.');

  await clickWorld(page, 10, 10);
  await expect(page.locator('.penelopa-properties-title')).toHaveText('Circle');
  await page.getByRole('button', { name: 'Load', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Replace', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Replace', exact: true }).click();

  await expect(page.locator('.penelopa-properties-empty')).toBeVisible();
  await expect(page.locator('.penelopa-tool-message')).toHaveText('Document loaded.');
  await page.getByRole('tab', { name: 'Primitives' }).click();
  await expect(page.locator('.penelopa-tree-item')).toHaveCount(1);
  await expect(page.locator('.penelopa-tree-item.selected')).toHaveCount(0);
});

test('canvas keyboard path moves with arrows, deletes with Delete, and undoes', async ({ page }) => {
  await ready(page);
  await addPrimitive(page, 'Circle');
  await clickWorld(page, 10, 10);
  await expect(page.locator('.penelopa-properties-title')).toHaveText('Circle');

  const canvas = page.locator('.penelopa-canvas');
  await canvas.focus();
  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('ArrowUp');
  await page.keyboard.press('Shift+ArrowRight');
  await expect.poll(async () => (await properties(page)).CenterX).toBe('21');
  expect((await properties(page)).CenterY).toBe('11');

  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await properties(page)).CenterX).toBe('11');

  await page.keyboard.press('Delete');
  await page.getByRole('tab', { name: 'Primitives' }).click();
  await expect(page.locator('.penelopa-tree-item')).toHaveCount(0);

  await page.keyboard.press('Control+z');
  await expect(page.locator('.penelopa-tree-item')).toHaveCount(1);
});

test('Delete inside a property input is left to the input and does not delete the primitive', async ({ page }) => {
  await ready(page);
  await addPrimitive(page, 'Circle');
  await clickWorld(page, 10, 10);

  const centerX = field(page, 'CenterX').locator('input');
  await centerX.focus();
  await page.keyboard.press('Delete');
  await page.getByRole('tab', { name: 'Primitives' }).click();
  await expect(page.locator('.penelopa-tree-item')).toHaveCount(1);

  await clickWorld(page, 10, 10);
  await page.keyboard.press('Delete');
  await expect(page.locator('.penelopa-tree-item')).toHaveCount(0);
});

test('rename primitive updates the properties title and tree', async ({ page }) => {
  await ready(page);
  await addPrimitive(page, 'Circle');
  await clickWorld(page, 10, 10);

  const name = field(page, 'Name').locator('input');
  await name.fill('  Renamed Circle  ');
  await name.press('Tab');

  await expect(page.locator('.penelopa-properties-title')).toHaveText('Renamed Circle');
  await page.getByRole('tab', { name: 'Primitives' }).click();
  await expect(page.locator('.penelopa-tree-item')).toHaveText('Renamed Circle');
});

test('empty rename is rejected and keeps the old name', async ({ page }) => {
  await ready(page);
  await addPrimitive(page, 'Circle');
  await clickWorld(page, 10, 10);

  const name = field(page, 'Name').locator('input');
  await name.fill('   ');
  await name.press('Tab');

  await expect(field(page, 'Name').locator('.penelopa-prop-error')).toHaveText('Name cannot be empty.');
  await expect(name).toHaveAttribute('aria-invalid', 'true');
  expect(await name.getAttribute('aria-describedby')).toBeTruthy();
  await expect(page.locator('.penelopa-properties-title')).toHaveText('Circle');
});

test('multi-selection edits shared properties, shows conflicts, and undoes in one step', async ({ page }) => {
  await ready(page);
  await addPrimitive(page, 'Circle');
  await clickWorld(page, 10, 10);
  await field(page, 'CenterX').locator('input').fill('100');
  await field(page, 'CenterX').locator('input').press('Tab');

  await addPrimitive(page, 'Circle');
  await clickWorld(page, 150, -200);
  await clickWorld(page, 100, 10);
  await clickWorld(page, 10, 10, 1, true);
  await expect(page.locator('.penelopa-properties-title')).toHaveText('2 primitives selected');

  await field(page, 'Radius').locator('input').fill('20');
  await field(page, 'Radius').locator('input').press('Tab');
  await expect.poll(async () => (await properties(page)).Radius).toBe('20');

  await clickWorld(page, 150, -200);
  await clickWorld(page, 100, 10);
  await expect.poll(async () => (await properties(page)).Radius).toBe('20');

  await page.keyboard.press('Control+z');
  await clickWorld(page, 150, -200);
  await clickWorld(page, 100, 10);
  await clickWorld(page, 10, 10, 1, true);
  await expect.poll(async () => (await properties(page)).Radius).toBe('5');

  await clickWorld(page, 150, -200);
  await clickWorld(page, 100, 10);
  await field(page, 'Radius').locator('input').fill('9');
  await field(page, 'Radius').locator('input').press('Tab');
  await clickWorld(page, 150, -200);
  await clickWorld(page, 100, 10);
  await clickWorld(page, 10, 10, 1, true);
  await expect(page.locator('.penelopa-properties-title')).toHaveText('2 primitives selected');
  await expect(field(page, 'Radius').locator('input')).toHaveValue('');
  await expect(field(page, 'Radius').locator('input')).toHaveAttribute('placeholder', 'Multiple values');
});

test('grouping and ungrouping preserve world positions and selection', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await ready(page);
  await addPrimitive(page, 'Circle');
  await addPrimitive(page, 'Rectangle');

  // 圆与矩形在 (10,10) 重叠；分别点选只在各自形状内的位置。
  await clickWorld(page, 7, 7);
  await clickWorld(page, 18, 18, 1, true);
  await expect(page.locator('.penelopa-properties-title')).toHaveText('2 primitives selected');

  await page.getByRole('button', { name: 'Group', exact: true }).click();
  await page.getByRole('tab', { name: 'Primitives' }).click();
  const items = page.locator('.penelopa-tree-item');
  await expect(items).toHaveCount(3);
  await expect(items.nth(0)).toHaveText('Group');
  await expect(items.nth(1)).toHaveText('Circle');
  await expect(items.nth(2)).toHaveText('Rectangle');
  await expect(items.nth(0)).toHaveClass(/selected/);

  await items.filter({ hasText: 'Circle' }).click();
  await expect(page.locator('.penelopa-properties-title')).toHaveText('Circle');
  await expect.poll(async () => (await properties(page)).CenterX).toBe('10');
  await expect.poll(async () => (await properties(page)).CenterY).toBe('10');
  await expect.poll(async () => (await properties(page)).Radius).toBe('5');

  await items.filter({ hasText: 'Rectangle' }).click();
  await expect.poll(async () => (await properties(page)).PosX).toBe('10');
  await expect.poll(async () => (await properties(page)).PosY).toBe('10');

  await items.filter({ hasText: 'Group' }).click();
  await page.getByRole('tab', { name: 'Tools' }).click();
  await expect(page.getByRole('button', { name: 'Ungroup', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Ungroup', exact: true }).click();

  await page.getByRole('tab', { name: 'Primitives' }).click();
  await expect(items).toHaveCount(2);
  await expect(items.nth(0)).toHaveText('Circle');
  await expect(items.nth(1)).toHaveText('Rectangle');
  await expect(page.locator('.penelopa-properties-title')).toHaveText('2 primitives selected');

  await page.keyboard.press('Control+z');
  await page.waitForTimeout(200);
  await expect(items).toHaveCount(3);
  await expect(items.nth(0)).toHaveText('Group');
  expect(errors).toEqual([]);
});

test('ungroup is disabled for non-identity containers', async ({ page }) => {
  await ready(page);
  await addPrimitive(page, 'Offset');

  await expect(page.getByRole('button', { name: 'Group', exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Ungroup', exact: true })).toBeDisabled();
  await expect(page.locator('#penelopa-group-hint')).toContainText('identity');
});

test('canvas and property inputs expose accessible names and state', async ({ page }) => {
  await ready(page);

  const canvas = page.locator('.penelopa-canvas');
  await expect(canvas).toHaveAttribute('tabindex', '0');
  await expect(canvas).toHaveAttribute('role', 'application');
  await expect(canvas).toHaveAttribute('aria-label', 'Drawing canvas');
  await expect(page.locator('#penelopa-canvas-help')).toHaveCount(1);
  await expect(page.locator('[data-testid="penelopa-status-bar"]')).toHaveAttribute('aria-live', 'polite');

  const viewport = await page.locator('meta[name="viewport"]').getAttribute('content');
  expect(viewport).not.toContain('user-scalable');
  expect(viewport).not.toContain('maximum-scale');

  await addPrimitive(page, 'Circle');
  await clickWorld(page, 10, 10);
  const radius = field(page, 'Radius');
  const input = radius.locator('input');
  const id = await input.getAttribute('id');
  expect(id).toBeTruthy();
  expect(await radius.locator('label').getAttribute('for')).toBe(id);

  await page.getByRole('tab', { name: 'Primitives' }).click();
  await expect(page.locator('.penelopa-tree-item')).toHaveAttribute('aria-pressed', 'true');

  await input.focus();
  await input.fill('-5');
  await input.press('Tab');
  await expect(radius.locator('.penelopa-prop-error')).toBeVisible();
  await expect(input).toHaveAttribute('aria-invalid', 'true');
  const describedBy = await input.getAttribute('aria-describedby');
  expect(describedBy).toBeTruthy();
  await expect(page.locator(`#${describedBy}`)).toBeVisible();
});

test('responsive widths keep the canvas usable and controls reachable', async ({ page }) => {
  await ready(page);
  await addPrimitive(page, 'Circle');
  await clickWorld(page, 10, 10);

  // Atlas 0.3.0 applies the 160–300px SplitConstraints to splitter drags only;
  // the initial proportional tracks are narrower than that at 700px. These
  // assertions therefore verify usability, not a 160px runtime minimum.
  const widths = [
    { width: 1440, minCanvas: 700 },
    { width: 1280, minCanvas: 610 },
    { width: 1024, minCanvas: 480 },
    { width: 900, minCanvas: 410 },
    { width: 800, minCanvas: 360 },
    { width: 700, minCanvas: 300 },
  ];

  for (const { width, minCanvas } of widths) {
    await page.setViewportSize({ width, height: 900 });
    await page.waitForTimeout(250);

    const metrics = await page.evaluate(() => {
      const canvas = document.querySelector('.penelopa-canvas canvas')!.getBoundingClientRect();
      const panelOverflow = (selector: string) => {
        const panel = document.querySelector(selector)!;
        return panel.scrollWidth - panel.clientWidth;
      };
      return {
        canvasWidth: canvas.width,
        documentOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
        toolsOverflow: panelOverflow('.penelopa-tools'),
        propertiesOverflow: panelOverflow('.penelopa-properties'),
      };
    });
    expect(metrics.canvasWidth, `canvas width at ${width}px`).toBeGreaterThanOrEqual(minCanvas);
    expect(metrics.documentOverflow).toBeLessThanOrEqual(0);
    expect(metrics.toolsOverflow, `Tools horizontal overflow at ${width}px`).toBeLessThanOrEqual(0);
    expect(metrics.propertiesOverflow, `Properties horizontal overflow at ${width}px`).toBeLessThanOrEqual(0);

    await expect(page.getByRole('button', { name: 'Circle', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: '← Left', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Group', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Ungroup', exact: true })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Tools' })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Primitives' })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Properties' })).toBeVisible();
    await expect(field(page, 'Radius').locator('input')).toBeVisible();

    const box = await page.getByRole('button', { name: 'Circle', exact: true }).boundingBox();
    expect(box, `Circle button at ${width}px`).not.toBeNull();
    expect(box!.x + box!.width).toBeLessThanOrEqual(width);
  }
});

test('Atlas 0.3.0 initial proportional tracks are not clamped to the split minimum', async ({ page }) => {
  await page.setViewportSize({ width: 700, height: 900 });
  await ready(page);

  const widths = await page.evaluate(() => ({
    tools: document.querySelector('.penelopa-tools')!.getBoundingClientRect().width,
    properties: document.querySelector('.penelopa-properties')!.getBoundingClientRect().width,
  }));

  // Characterization test: in Atlas 0.3.0 SplitConstraints only affect splitter
  // interactions, not the initial proportional tracks. Revisit on Atlas upgrade.
  expect(widths.tools).toBeLessThan(160);
  expect(widths.properties).toBeLessThan(160);
});

test('responsive narrow layout still adds, selects, aligns, groups, deletes, and undoes', async ({ page }) => {
  await page.setViewportSize({ width: 700, height: 900 });
  await ready(page);
  await addPrimitive(page, 'Circle');
  await clickWorld(page, 10, 10);
  await field(page, 'CenterX').locator('input').fill('100');
  await field(page, 'CenterX').locator('input').press('Tab');

  await addPrimitive(page, 'Circle');
  await clickWorld(page, 150, -200);
  await clickWorld(page, 100, 10);
  await clickWorld(page, 10, 10, 1, true);
  await expect(page.locator('.penelopa-properties-title')).toHaveText('2 primitives selected');

  await page.getByRole('button', { name: '← Left', exact: true }).click();
  await expect.poll(async () => (await properties(page))['CenterX']).toBe('10');

  await page.getByRole('button', { name: 'Group', exact: true }).click();
  await expect(page.locator('.penelopa-properties-title')).toHaveText('Group');
  await expect(page.getByRole('button', { name: 'Ungroup', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Ungroup', exact: true }).click();
  await expect(page.locator('.penelopa-properties-title')).toHaveText('2 primitives selected');

  await page.keyboard.press('Delete');
  await page.getByRole('tab', { name: 'Primitives' }).click();
  await expect(page.locator('.penelopa-tree-item')).toHaveCount(0);
  await page.keyboard.press('Control+z');
  await expect(page.locator('.penelopa-tree-item')).toHaveCount(2);
});
