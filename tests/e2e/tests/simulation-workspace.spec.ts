import { test, expect } from '@playwright/test';
import {
  addRectangleAndAnimate,
  collectSimulationErrors,
  expectSimulationClean,
} from './simulation-helpers';

test('simulation workspace exposes the protected default layout', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await addRectangleAndAnimate(page);

  const workspace = page.locator('.atlas-v2-workspace');
  await expect(workspace).toBeVisible();
  await expect(workspace).toHaveAttribute(
    'data-atlas-workspace-id',
    'penelopa-simulation',
  );

  await expect(page.locator('.penelopa-simulation-tree-panel')).toBeVisible();
  await expect(page.locator('.penelopa-simulation-canvas-panel')).toBeVisible();
  await expect(page.locator('.penelopa-simulation-inspector-panel')).toBeVisible();
  await expect(page.locator('.penelopa-simulation-timeline')).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Canvas', exact: true })).toBeVisible();
  await expect(
    page.locator('.atlas-v2-panel-header').filter({ hasText: 'Primitives' }),
  ).toBeVisible();
  await expect(
    page.locator('.atlas-v2-panel-header').filter({ hasText: 'Inspector' }),
  ).toBeVisible();
  await expect(
    page.locator('.atlas-v2-panel-header').filter({ hasText: 'Timeline' }),
  ).toBeVisible();

  await expect(page.locator('.atlas-v2-toolbar')).toHaveCount(0);
  await expect(workspace.getByRole('button', { name: /^Close/ })).toHaveCount(0);

  const metrics = await page.evaluate(() => {
    const box = (selector: string) => {
      const element = document.querySelector(selector)?.getBoundingClientRect();
      return element
        ? { width: Math.round(element.width), height: Math.round(element.height) }
        : null;
    };

    return {
      tree: box('.penelopa-simulation-tree-panel'),
      canvas: box('.penelopa-simulation-canvas-panel'),
      inspector: box('.penelopa-simulation-inspector-panel'),
      timeline: box('.penelopa-simulation-timeline'),
      timelineGroup: box('.atlas-v2-group[data-atlas-group-id="timeline-group"]'),
      scrollWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth,
    };
  });

  expect(metrics.tree?.width).toBeGreaterThan(200);
  expect(metrics.tree?.width).toBeLessThan(240);
  expect(metrics.inspector?.width).toBeGreaterThan(300);
  expect(metrics.inspector?.width).toBeLessThan(340);
  expect(metrics.timelineGroup?.height).toBeGreaterThan(190);
  expect(metrics.timelineGroup?.height).toBeLessThan(220);
  expect(metrics.canvas?.width).toBeGreaterThan(600);
  expect(metrics.scrollWidth).toBeLessThanOrEqual(metrics.innerWidth);
  await expectSimulationClean(page, errors);
});

test('splitter keyboard resize and collapse keep regions reachable', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await addRectangleAndAnimate(page);

  const tree = page.locator('.penelopa-simulation-tree-panel');
  const before = await tree.boundingBox();
  expect(before).not.toBeNull();

  const resizeSplit = page.getByRole('separator', {
    name: 'Resize split work-area',
  });
  await expect(resizeSplit).toBeVisible();
  await resizeSplit.focus();
  await page.keyboard.press('ArrowRight');
  await page.waitForTimeout(700);

  const after = await tree.boundingBox();
  expect(after).not.toBeNull();
  expect(Math.abs(after!.width - before!.width)).toBeGreaterThan(4);

  const collapseTree = page.locator(
    '[data-atlas-split-id="work-area"] [data-atlas-collapse-child="first"]',
  );
  await expect(collapseTree).toHaveAttribute('aria-label', 'Collapse region');
  await collapseTree.click();
  await expect(tree).toBeHidden();

  const expandTree = page.locator(
    '[data-atlas-split-id="work-area"] [data-atlas-collapse-child="first"]',
  );
  await expect(expandTree).toHaveAttribute('aria-label', 'Expand region');
  await expandTree.click();
  await expect(tree).toBeVisible();

  const inspector = page.locator('.penelopa-simulation-inspector-panel');
  const inspectorToggle = page.locator(
    '[data-atlas-split-id="right"] [data-atlas-collapse-child="second"]',
  );
  await inspectorToggle.click();
  await expect(inspector).toBeHidden();
  await expect(inspectorToggle).toHaveAttribute('aria-label', 'Expand region');
  await inspectorToggle.click();
  await expect(inspector).toBeVisible();

  const timeline = page.locator('.penelopa-simulation-timeline');
  const timelineToggle = page.locator(
    '[data-atlas-split-id="root"] [data-atlas-collapse-child="second"]',
  );
  await timelineToggle.click();
  await expect(timeline).toBeHidden();
  await expect(timelineToggle).toHaveAttribute('aria-label', 'Expand region');
  await timelineToggle.click();
  await expect(timeline).toBeVisible();
  await expectSimulationClean(page, errors);
});

test('layout adjustments during playback keep the session running', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await addRectangleAndAnimate(page);

  const secondTime = page.getByLabel('Time (s)', { exact: true });
  await secondTime.fill('3');
  await secondTime.press('Tab');
  const rows = page.locator('.penelopa-simulation-keyframe-table tbody tr');
  await expect(rows.nth(1)).toContainText('3.000s');

  await page.getByRole('button', { name: 'Play', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Pause', exact: true })).toBeVisible();

  const time = page.locator('.penelopa-simulation-time');
  await expect.poll(async () => Number.parseFloat((await time.textContent()) ?? '0'))
    .toBeGreaterThan(0.2);

  const collapseTree = page.locator(
    '[data-atlas-split-id="work-area"] [data-atlas-collapse-child="first"]',
  );
  await collapseTree.click();
  await expect(page.locator('.penelopa-simulation-tree-panel')).toBeHidden();

  const firstReading = Number.parseFloat((await time.textContent()) ?? '0');
  await page.waitForTimeout(300);
  const secondReading = Number.parseFloat((await time.textContent()) ?? '0');
  expect(secondReading).toBeGreaterThan(firstReading);

  const expandTree = page.locator(
    '[data-atlas-split-id="work-area"] [data-atlas-collapse-child="first"]',
  );
  await expandTree.click();
  await expect(page.locator('.penelopa-simulation-tree-panel')).toBeVisible();
  await page.getByRole('button', { name: 'Pause', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Resume', exact: true })).toBeVisible();
  await expectSimulationClean(page, errors);
});
