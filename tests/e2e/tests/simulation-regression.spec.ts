import { test, expect } from '@playwright/test';
import {
  addRectangleAndAnimate,
  collectSimulationErrors,
  expectNoBlazorErrorUi,
  expectSimulationClean,
  storedSimulationTrackCount,
} from './simulation-helpers';

test('editor dormant save confirmation is cancelled by Load and never overwrites the loaded config', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await addRectangleAndAnimate(page);

  await page.getByRole('button', { name: 'Back to editor', exact: true }).click();
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByText('Document saved.')).toBeVisible();
  await expect.poll(() => storedSimulationTrackCount(page)).toBe(1);

  await page.getByRole('tab', { name: 'Primitives', exact: true }).click();
  await page.locator('.penelopa-tree')
    .getByRole('button', { name: 'Rectangle', exact: true })
    .click();
  await page.keyboard.press('Delete');
  await page.getByRole('tab', { name: 'Tools', exact: true }).click();

  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByText(/will not be saved/)).toBeVisible();

  await page.getByRole('button', { name: 'Load', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Replace', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Save active only', exact: true })).toHaveCount(0);

  await page.getByRole('button', { name: 'Replace', exact: true }).click();
  await expect(page.locator('.penelopa-tool-message').last()).toContainText('Document loaded.');
  await expect(page.getByRole('button', { name: 'Save active only', exact: true })).toHaveCount(0);
  await expect(page.getByText('Document saved.')).toHaveCount(0);

  const save = page.getByRole('button', { name: 'Save', exact: true });
  await expect(save).toBeEnabled();
  await save.click();
  await expect(page.getByText('Document saved.')).toBeVisible();
  await expect.poll(() => storedSimulationTrackCount(page)).toBe(1);

  await expectSimulationClean(page, errors);
});

test('browser back while playing restores the editor base value without reselecting', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await addRectangleAndAnimate(page);

  await page.getByRole('button', { name: 'Back to editor', exact: true }).click();
  await page.getByRole('tab', { name: 'Primitives', exact: true }).click();
  await page.locator('.penelopa-tree')
    .getByRole('button', { name: 'Rectangle', exact: true })
    .click();
  const editorCanvas = page.getByRole('application', { name: 'Drawing canvas' });
  const baseCanvas = await editorCanvas.screenshot();

  await page.getByRole('link', { name: 'Simulate', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Simulation', exact: true })).toBeVisible();

  await page.getByRole('button', { name: 'Play', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Pause', exact: true })).toBeVisible();
  await expect.poll(async () => {
    const text = await page.locator('.penelopa-simulation-time').textContent();
    return Number.parseFloat(text ?? '0');
  }).toBeGreaterThan(0.2);

  await page.goBack();

  const posX = page.locator('.penelopa-prop-field')
    .filter({ hasText: 'PosX' })
    .locator('input');
  await expect(posX).toBeVisible();
  await expect(posX).toHaveValue('10');
  await expect(editorCanvas).toBeVisible();
  await expect.poll(async () => (await editorCanvas.screenshot()).equals(baseCanvas)).toBe(true);
  await expectNoBlazorErrorUi(page);

  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByText('Document saved.')).toBeVisible();
  await expect.poll(() => page.evaluate(() => {
    const json = localStorage.getItem('penelopa.document');
    if (!json) {
      return null;
    }

    const document = JSON.parse(json) as {
      Primitives?: { Props?: { PosX?: number } }[];
    };
    return document.Primitives?.[0]?.Props?.PosX ?? null;
  })).toBe(10);

  await expectSimulationClean(page, errors);
});

test('dormant details list removes one track and undo restores it', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await addRectangleAndAnimate(page);

  const posYRow = page.locator('.penelopa-simulation-property')
    .filter({ hasText: 'PosY' })
    .first();
  await posYRow.getByRole('button', { name: 'Animate PosY', exact: true }).click();
  await expect(posYRow).toContainText('2 keyframe(s)');

  await page.getByRole('button', { name: 'Back to editor', exact: true }).click();
  await page.getByRole('tab', { name: 'Primitives', exact: true }).click();
  await page.locator('.penelopa-tree')
    .getByRole('button', { name: 'Rectangle', exact: true })
    .click();
  await page.keyboard.press('Delete');
  await page.getByRole('link', { name: 'Simulate', exact: true }).click();

  await expect(page.getByText('2 inactive track(s)').first()).toBeVisible();
  await page.getByText('Show inactive tracks', { exact: true }).click();
  const rows = page.locator('.penelopa-simulation-dormant-list li');
  await expect(rows).toHaveCount(2);
  await expect(rows.nth(0)).toContainText('PosX');
  await expect(rows.nth(1)).toContainText('PosY');

  await page.getByRole('button', { name: /Remove inactive track PosX/ }).click();
  await expect(page.getByText('1 inactive track(s)').first()).toBeVisible();
  await expect(rows).toHaveCount(1);
  await expect(rows.nth(0)).toContainText('PosY');

  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByText(/1 inactive track\(s\) will not be saved/)).toBeVisible();
  await page.getByRole('button', { name: 'Cancel save', exact: true }).click();

  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect(page.getByText('2 inactive track(s)').first()).toBeVisible();
  await expect(rows).toHaveCount(2);

  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByText(/2 inactive track\(s\) will not be saved/)).toBeVisible();
  await page.getByRole('button', { name: 'Remove inactive', exact: true }).click();
  await expect(page.getByText(/inactive track/)).toHaveCount(0);
  await expect(page.getByText(/will not be saved/)).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeEnabled();

  await expectSimulationClean(page, errors);
});

test('entering the simulation focuses the heading and returning restores the Simulate link focus', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await page.goto('/');
  await page.getByRole('link', { name: 'Simulate', exact: true }).click();

  const heading = page.getByRole('heading', { name: 'Simulation', exact: true });
  await expect(heading).toBeVisible();
  await expect(heading).toBeFocused();

  await page.getByRole('button', { name: 'Back to editor', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Simulate', exact: true })).toBeFocused();
  await expectSimulationClean(page, errors);
});

test('ruler ticks seek and double-clicking a keyframe goes to it', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await addRectangleAndAnimate(page);

  await page.locator('.penelopa-simulation-tick')
    .filter({ hasText: '0.5s' })
    .click();
  await expect(page.locator('.penelopa-simulation-time')).toHaveText('0.500s');

  await page.getByRole('button', {
    name: /PosX keyframe 2 at 1\.000 seconds/,
  }).dblclick();
  await expect(page.locator('.penelopa-simulation-time')).toHaveText('1.000s');
  await expect(page.locator('.penelopa-simulation-status')).toHaveText('Finished');
  await expectSimulationClean(page, errors);
});

test('browser back from preview restores base values without reselecting', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await addRectangleAndAnimate(page);
  await page.getByRole('button', { name: 'Back to editor', exact: true }).click();
  await page.getByRole('tab', { name: 'Primitives', exact: true }).click();
  await page.locator('.penelopa-tree')
    .getByRole('button', { name: 'Rectangle', exact: true })
    .click();
  await page.getByRole('link', { name: 'Simulate', exact: true }).click();

  await page.locator('.penelopa-simulation-scrubber').evaluate((element) => {
    const input = element as HTMLInputElement;
    input.value = '0.5';
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
  await expect(page.locator('.penelopa-simulation-time')).toHaveText('0.500s');

  await page.goBack();

  const posX = page.locator('.penelopa-prop-field')
    .filter({ hasText: 'PosX' })
    .locator('input');
  await expect(posX).toBeVisible();
  await expect(posX).toHaveValue('10');
  await expectSimulationClean(page, errors);
});

test('deleting a keyframe before the selected one keeps the same keyframe selected', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await addRectangleAndAnimate(page);

  await page.locator('.penelopa-simulation-scrubber').evaluate((element) => {
    const input = element as HTMLInputElement;
    input.value = '0.25';
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
  await page.getByRole('button', { name: 'Add keyframe', exact: true }).click();
  await page.locator('.penelopa-simulation-scrubber').evaluate((element) => {
    const input = element as HTMLInputElement;
    input.value = '0.75';
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
  await page.getByRole('button', { name: 'Add keyframe', exact: true }).click();

  const rows = page.locator('.penelopa-simulation-keyframe-table tbody tr');
  await expect(rows).toHaveCount(4);
  await rows.nth(2).locator('.penelopa-simulation-keyframe-select').click();
  await expect(page.locator('.penelopa-simulation-keyframe-editor'))
    .toContainText('Keyframe 3');

  await rows.nth(1)
    .getByRole('button', { name: /Delete keyframe 2/ })
    .click();

  await expect(rows).toHaveCount(3);
  await expect(rows.nth(1)).toHaveClass(/selected/);
  await expect(rows.nth(1).locator('.penelopa-simulation-keyframe-select'))
    .toHaveText('0.750s');
  await expect(page.locator('.penelopa-simulation-keyframe-editor'))
    .toContainText('Keyframe 2');
  await expectSimulationClean(page, errors);
});

test('simulation page has no horizontal overflow at 1024x768', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await page.setViewportSize({ width: 1024, height: 768 });
  await addRectangleAndAnimate(page);

  const metrics = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    bodyScrollWidth: document.body.scrollWidth,
    innerWidth: window.innerWidth,
  }));
  expect(metrics.scrollWidth).toBeLessThanOrEqual(metrics.innerWidth);
  expect(metrics.bodyScrollWidth).toBeLessThanOrEqual(metrics.innerWidth);
  await expect(page.getByRole('img', { name: /Simulation canvas/ })).toBeVisible();
  await expect(page.locator('.penelopa-simulation-timeline')).toBeVisible();
  await expectSimulationClean(page, errors);
});

test('save failure shows a safe single-line message without a stack trace', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await page.goto('/');
  await page.getByRole('button', { name: 'Rectangle', exact: true }).click();
  await page.evaluate(() => {
    const original = Storage.prototype.setItem;
    Storage.prototype.setItem = function (key: string, value: string) {
      if (key === 'penelopa.document') {
        throw new Error(
          'quota exceeded\nError: at Storage.setItem (http://127.0.0.1/_framework/penelopa.js:1:1)',
        );
      }

      return original.call(this, key, value);
    };
  });

  await page.getByRole('button', { name: 'Save', exact: true }).click();
  const message = page.locator('.penelopa-tool-message.is-error');
  await expect(message).toContainText('could not be saved');
  const text = (await message.textContent()) ?? '';
  expect(text).not.toContain(' at ');
  expect(text).not.toContain('_framework');
  expect(text).not.toContain('\n');
  expect(await page.evaluate(() => localStorage.getItem('penelopa.document'))).toBeNull();
  await expectNoBlazorErrorUi(page);

  expect(errors.filter(error => error.startsWith('pageerror:'))).toEqual([]);
  // 本次故意让 setItem 抛错：JS 存储帮助函数按设计把完整异常写入 console.error，
  // 这是该失败注入用例的预期噪声；UI 文本已在上方断言为安全单行摘要。
});

test('loading a document with a null primitive reports a readable error and keeps the current document', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await page.addInitScript(() => {
    localStorage.setItem('penelopa.document', JSON.stringify({
      Version: 2,
      Primitives: [null],
      SelectionIds: [],
    }));
  });

  await page.goto('/');
  await page.getByRole('button', { name: 'Rectangle', exact: true }).click();
  await page.getByRole('button', { name: 'Load', exact: true }).click();

  await expect(page.locator('.penelopa-tool-message')).toContainText(/invalid or unsupported/i);
  await expectNoBlazorErrorUi(page);

  await page.getByRole('tab', { name: 'Primitives', exact: true }).click();
  await expect(page.locator('.penelopa-tree')
    .getByRole('button', { name: 'Rectangle', exact: true })).toBeVisible();

  await expectSimulationClean(page, errors);
});
