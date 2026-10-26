import { expect, Page } from '@playwright/test';

/**
 * Collects page errors and console errors for a test. The Blazor error
 * boundary only produces console output, so pageerror alone missed load
 * crashes.
 */
export function collectSimulationErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(`pageerror: ${error.message}`));
  page.on('console', message => {
    if (message.type() === 'error') {
      errors.push(`console.error: ${message.text()}`);
    }
  });
  return errors;
}

export async function expectNoBlazorErrorUi(page: Page): Promise<void> {
  await expect(page.locator('#blazor-error-ui')).toBeHidden();
}

export async function expectSimulationClean(
  page: Page,
  errors: string[],
): Promise<void> {
  await expectNoBlazorErrorUi(page);
  expect(errors).toEqual([]);
}

export async function waitForAnimationFrames(
  page: Page,
  count = 2,
): Promise<void> {
  await page.evaluate(async (frameCount) => {
    for (let index = 0; index < frameCount; index++) {
      await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
    }
  }, count);
}

export async function addRectangleAndAnimate(page: Page): Promise<void> {
  await page.goto('/');
  await page.getByRole('button', { name: 'Rectangle', exact: true }).click();
  await page.getByRole('link', { name: 'Simulate', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Simulation', exact: true })).toBeVisible();

  await page.getByRole('button', { name: 'Rectangle', exact: true }).click();
  const posXRow = page.locator('.penelopa-simulation-property')
    .filter({ hasText: 'PosX' })
    .first();
  await posXRow.getByRole('button', { name: 'Animate PosX', exact: true }).click();
  await expect(posXRow).toContainText('2 keyframe(s)');
  await posXRow.getByRole('button', { name: /PosX/ }).click();
  await expect(
    page.getByRole('button', { name: 'Add keyframe', exact: true }),
  ).toBeVisible();

  const rows = page.locator('.penelopa-simulation-keyframe-table tbody tr');
  await expect(rows).toHaveCount(2);
  await rows.nth(1).locator('.penelopa-simulation-keyframe-select').click();
  const value = page.getByLabel('Value', { exact: true });
  await value.fill('100');
  await value.press('Tab');
  await expect(rows.nth(1)).toContainText('100');
}

export async function storedSimulationTrackCount(page: Page): Promise<number> {
  return page.evaluate(() => {
    const json = localStorage.getItem('penelopa.document');
    if (!json) {
      return 0;
    }

    const document = JSON.parse(json) as { Simulation?: { Tracks?: unknown[] } };
    return document.Simulation?.Tracks?.length ?? 0;
  });
}
