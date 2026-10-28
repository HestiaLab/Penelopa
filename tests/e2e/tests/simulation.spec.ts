import { test, expect, Page } from '@playwright/test';
import {
  addRectangleAndAnimate,
  collectSimulationErrors,
  expectSimulationClean,
  waitForAnimationFrames,
} from './simulation-helpers';

async function seedSimulationDocument(page: Page): Promise<void> {
  const rectangleId = '00000000-0000-0000-0000-000000000001';
  await page.addInitScript((id) => {
    localStorage.setItem('penelopa.document', JSON.stringify({
      Version: 2,
      Primitives: [
        {
          Id: id,
          Type: 'Rectangle',
          Props: {
            Name: 'Animated Rectangle',
            PosX: 10,
            PosY: 10,
            Width: 10,
            Height: 10,
            Color: 4294967295,
          },
        },
      ],
      SelectionIds: [],
      Simulation: {
        Loop: false,
        Tracks: [
          {
            PrimitiveId: id,
            Property: 'PosX',
            Keyframes: [
              { Time: 0, Value: 10, Interpolation: 'linear' },
              { Time: 1, Value: 100, Interpolation: 'linear' },
            ],
          },
        ],
      },
    }));
  }, rectangleId);
}

test('simulation smoke loads, plays, pauses, and stops a saved animation', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await seedSimulationDocument(page);

  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Load', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Load', exact: true }).click();
  await page.getByRole('button', { name: 'Replace', exact: true }).click();
  await expect(page.locator('.penelopa-tool-message').last()).toContainText('Document loaded.');

  await page.getByRole('link', { name: 'Simulate', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Simulation', exact: true })).toBeVisible();
  await expect(
    page.getByRole('treeitem', { name: /Animated Rectangle/ }),
  ).toBeVisible();
  await expect(page.locator('.penelopa-simulation-time')).toHaveText('0.000s');
  expect(await page.evaluate(() => Object.hasOwn(window.penelopa, 'getSimulationTickCount')))
    .toBe(false);
  expect(await page.evaluate(() => Object.hasOwn(window.penelopa, 'getSimulationPaintCount')))
    .toBe(false);

  await page.getByRole('treeitem', { name: /Animated Rectangle/ }).click();
  await expect(
    page.locator('.penelopa-simulation-property').filter({ hasText: 'PosX' }),
  ).toBeVisible();
  await expect(
    page.locator('.penelopa-simulation-track-label').filter({ hasText: 'PosX' }),
  ).toBeVisible();

  await page.getByRole('button', { name: 'Play', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Pause', exact: true })).toBeVisible();
  await expect.poll(async () => {
    const text = await page.locator('.penelopa-simulation-time').textContent();
    return Number.parseFloat(text ?? '0');
  }).toBeGreaterThan(0.1);

  await page.getByRole('button', { name: 'Pause', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Resume', exact: true })).toBeVisible();
  const paused = await page.locator('.penelopa-simulation-time').textContent();
  await waitForAnimationFrames(page);
  await expect(page.locator('.penelopa-simulation-time')).toHaveText(paused ?? '');

  await page.getByRole('button', { name: 'Stop', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Play', exact: true })).toBeVisible();
  await expect(page.locator('.penelopa-simulation-time')).toHaveText('0.000s');

  await page.getByRole('button', { name: 'Back to editor', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Simulate', exact: true })).toBeFocused();

  await expectSimulationClean(page, errors);
});

test('simulation page shows an empty state without a document', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await page.goto('/simulation');

  await expect(page.getByRole('heading', { name: 'Simulation', exact: true })).toBeVisible();
  await expect(
    page.getByText('No primitives yet. Return to the editor to add one or load the sample.'),
  ).toBeVisible();
  await expect(page.getByRole('button', { name: 'Play', exact: true })).toBeDisabled();
  await expectSimulationClean(page, errors);
});

test('simulation tree supports keyboard selection and keeps config undo shortcuts', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await page.goto('/');
  await page.getByRole('button', { name: 'Circle', exact: true }).click();
  await page.getByRole('button', { name: 'Rectangle', exact: true }).click();
  await page.getByRole('link', { name: 'Simulate', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Simulation', exact: true })).toBeVisible();

  const tree = page.getByRole('tree', { name: 'Simulation primitives' });
  await expect(tree).toBeVisible();
  await tree.focus();
  await page.keyboard.press('ArrowDown');

  await expect.poll(async () => {
    const activeId = await tree.getAttribute('aria-activedescendant');
    return activeId === null
      ? null
      : await page.locator(`#${activeId} .penelopa-tree-label`).textContent();
  }).toBe('Rectangle');

  await page.keyboard.press('Space');
  await expect(page.locator('.penelopa-properties-title')).toHaveText('Rectangle');
  await expect(page.getByRole('treeitem', { name: 'Rectangle', exact: true }))
    .toHaveAttribute('aria-selected', 'true');

  const posXRow = page.locator('.penelopa-simulation-property')
    .filter({ hasText: 'PosX' })
    .first();
  await posXRow.getByRole('button', { name: 'Animate PosX', exact: true }).click();
  await expect(posXRow).toContainText('2 keyframe(s)');

  await tree.focus();
  await page.keyboard.press('Control+z');
  await expect(page.locator('.penelopa-simulation-property-meta')
    .filter({ hasText: 'keyframe' })).toHaveCount(0);
  await expectSimulationClean(page, errors);
});

test('simulation editing creates a track and plays the edited keyframe', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await addRectangleAndAnimate(page);

  const current = page.locator('.penelopa-simulation-property')
    .filter({ hasText: 'PosX' })
    .first()
    .locator('.penelopa-simulation-current');
  await expect(current).toHaveText('10.000');

  await page.getByRole('button', { name: 'Play', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Pause', exact: true })).toBeVisible();
  await expect.poll(async () => {
    const text = await current.textContent();
    return Number.parseFloat(text ?? '0');
  }).toBeGreaterThan(10);

  await page.getByRole('button', { name: 'Stop', exact: true }).click();
  await expect(current).toHaveText('10.000');
  await expectSimulationClean(page, errors);
});

test('simulation timeline supports scrub, add keyframe, undo/redo, and stepping', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await addRectangleAndAnimate(page);

  const rows = page.locator('.penelopa-simulation-keyframe-table tbody tr');
  await page.locator('.penelopa-simulation-scrubber').evaluate((element) => {
    const input = element as HTMLInputElement;
    input.value = '0.5';
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
  await expect(page.locator('.penelopa-simulation-time')).toHaveText('0.500s');

  await page.getByRole('button', { name: 'Add keyframe', exact: true }).click();
  await expect(rows).toHaveCount(3);

  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect(rows).toHaveCount(2);

  await page.getByRole('button', { name: 'Redo', exact: true }).click();
  await expect(rows).toHaveCount(3);

  await page.keyboard.press('Control+z');
  await expect(rows).toHaveCount(2);
  await page.keyboard.press('Control+y');
  await expect(rows).toHaveCount(3);

  await page.getByRole('button', { name: 'Stop', exact: true }).click();
  await expect(page.locator('.penelopa-simulation-time')).toHaveText('0.000s');

  await page.getByRole('button', { name: 'Step forward 1/60 second' }).click();
  await expect(page.locator('.penelopa-simulation-time')).toHaveText('0.017s');

  await page.getByRole('button', { name: 'Step back 1/60 second' }).click();
  await expect(page.locator('.penelopa-simulation-time')).toHaveText('0.000s');
  await expectSimulationClean(page, errors);
});

test('simulation page reports and removes dormant tracks', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await addRectangleAndAnimate(page);

  await page.getByRole('button', { name: 'Back to editor', exact: true }).click();
  await page.getByRole('tab', { name: 'Primitives', exact: true }).click();
  await page.locator('.penelopa-tree')
    .getByRole('treeitem', { name: 'Rectangle', exact: true })
    .click();
  await page.keyboard.press('Delete');

  await page.getByRole('link', { name: 'Simulate', exact: true }).click();
  await expect(page.getByText(/1 inactive track/)).toBeVisible();

  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByText(/will not be saved/)).toBeVisible();
  await page.getByRole('button', { name: 'Save active only', exact: true }).click();
  await expect(page.getByText('Document saved.')).toBeVisible();

  await page.getByRole('button', { name: 'Remove inactive', exact: true }).click();
  await expect(page.getByText(/inactive track/)).toHaveCount(0);
  await expectSimulationClean(page, errors);
});

test('simulation save persists animation config across reload and load', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await addRectangleAndAnimate(page);

  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByText('Document saved.')).toBeVisible();

  await page.goto('/');
  await page.getByRole('button', { name: 'Load', exact: true }).click();
  await page.getByRole('button', { name: 'Replace', exact: true }).click();
  await expect(page.locator('.penelopa-tool-message').last())
    .toContainText('Document loaded.');

  await page.getByRole('link', { name: 'Simulate', exact: true }).click();
  await page.getByRole('treeitem', { name: /^Rectangle/ }).click();
  const posXRow = page.locator('.penelopa-simulation-property')
    .filter({ hasText: 'PosX' })
    .first();
  await posXRow.getByRole('button', { name: /PosX/ }).click();
  await expect(
    page.getByRole('button', { name: 'Add keyframe', exact: true }),
  ).toBeVisible();
  const rows = page.locator('.penelopa-simulation-keyframe-table tbody tr');
  await expect(rows).toHaveCount(2);
  await expect(rows.nth(1)).toContainText('100');
  await expectSimulationClean(page, errors);
});

test('simulation loop, finished replay, and save availability', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await addRectangleAndAnimate(page);

  const save = page.getByRole('button', { name: 'Save', exact: true });
  const loop = page.getByRole('button', { name: 'Loop', exact: true });

  await loop.click();
  await expect(loop).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('button', { name: 'Play', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Pause', exact: true })).toBeVisible();
  await expect(save).toBeDisabled();
  await page.waitForFunction(() => {
    const time = Number.parseFloat(
      document.querySelector('.penelopa-simulation-time')?.textContent ?? '0',
    );
    const state = window as unknown as { simulationSawLateTime?: boolean };
    if (time >= 0.8) {
      state.simulationSawLateTime = true;
    }

    return state.simulationSawLateTime === true && time < 0.5;
  }, undefined, { timeout: 3_000 });
  await expect(page.getByRole('button', { name: 'Pause', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Stop', exact: true }).click();
  await expect(save).toBeEnabled();

  await loop.click();
  await expect(loop).toHaveAttribute('aria-pressed', 'false');
  await page.getByRole('button', { name: 'Play', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Replay', exact: true }),
  ).toBeVisible({ timeout: 3_000 });
  await expect(save).toBeDisabled();

  await page.getByRole('button', { name: 'Replay', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Pause', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Stop', exact: true }).click();
  await expect(save).toBeEnabled();
  await expectSimulationClean(page, errors);
});

test('leaving the simulation page while playing restores base values', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await addRectangleAndAnimate(page);

  await page.getByRole('button', { name: 'Play', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Pause', exact: true })).toBeVisible();
  await expect.poll(async () => {
    const text = await page.locator('.penelopa-simulation-time').textContent();
    return Number.parseFloat(text ?? '0');
  }).toBeGreaterThan(0.1);
  await page.getByRole('button', { name: 'Back to editor', exact: true }).click();

  await page.getByRole('tab', { name: 'Primitives', exact: true }).click();
  await page.locator('.penelopa-tree')
    .getByRole('treeitem', { name: 'Rectangle', exact: true })
    .click();
  const posX = page.locator('.penelopa-prop-field')
    .filter({ hasText: 'PosX' })
    .locator('input');
  await expect(posX).toHaveValue('10');
  await expectSimulationClean(page, errors);
});

test('simulation honors reduced motion and does not autoplay', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await addRectangleAndAnimate(page);

  await expect(page.getByText(/Reduced motion is enabled/)).toBeVisible();
  await expect(page.getByRole('img', { name: /Simulation canvas/ })).toBeVisible();
  await expect(page.locator('.penelopa-simulation-time')).toHaveText('0.000s');
  await expect(page.getByRole('button', { name: 'Play', exact: true })).toBeVisible();
  await expectSimulationClean(page, errors);
});

test('simulation pauses when the document becomes hidden', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await addRectangleAndAnimate(page);

  await page.getByRole('button', { name: 'Play', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Pause', exact: true })).toBeVisible();

  await page.evaluate(() => {
    Object.defineProperty(document, 'visibilityState', {
      configurable: true,
      get: () => 'hidden',
    });
    document.dispatchEvent(new Event('visibilitychange'));
  });

  try {
    await expect(page.getByRole('button', { name: 'Resume', exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Stop', exact: true }).click();
    await expectSimulationClean(page, errors);
  } finally {
    await page.evaluate(() => {
      delete (document as unknown as { visibilityState?: string }).visibilityState;
    });
  }
});

test('simulation timeline drags a keyframe and supports undo/redo', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await addRectangleAndAnimate(page);

  const rows = page.locator('.penelopa-simulation-keyframe-table tbody tr');
  const firstKeyframe = page.getByRole('button', {
    name: /PosX keyframe 1 at 0\.000 seconds/,
  });
  await expect(firstKeyframe).toBeVisible();
  const box = await firstKeyframe.boundingBox();
  expect(box).not.toBeNull();
  if (!box) {
    throw new Error('Keyframe marker has no bounding box.');
  }

  const centerX = box.x + box.width / 2;
  const centerY = box.y + box.height / 2;
  await page.mouse.move(centerX, centerY);
  await page.mouse.down();
  // pointerdown 选中该标记即说明 Blazor 已完成拖动准备，再开始横向移动。
  await expect(firstKeyframe).toHaveAttribute('aria-pressed', 'true');
  await page.mouse.move(centerX + 60, centerY, { steps: 8 });
  await page.mouse.up();

  await expect(rows.nth(0).locator('.penelopa-simulation-keyframe-select'))
    .not.toHaveText('0.000s');

  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect(rows.nth(0).locator('.penelopa-simulation-keyframe-select'))
    .toHaveText('0.000s');

  await page.getByRole('button', { name: 'Redo', exact: true }).click();
  await expect(rows.nth(0).locator('.penelopa-simulation-keyframe-select'))
    .not.toHaveText('0.000s');
  await expectSimulationClean(page, errors);
});
