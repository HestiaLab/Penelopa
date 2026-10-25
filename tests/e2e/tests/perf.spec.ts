import { test, expect, Page } from '@playwright/test';

const perfEnabled = process.env.PERF === '1';

async function ready(page: Page): Promise<void> {
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Circle', exact: true })).toBeVisible();
  await page.waitForTimeout(500);
}

async function idleCpuPercent(page: Page, durationMs: number): Promise<number> {
  const session = await page.context().newCDPSession(page);
  await session.send('Performance.enable');
  const readTaskDuration = async () => {
    const metrics = await session.send('Performance.getMetrics');
    return metrics.metrics.find((metric) => metric.name === 'TaskDuration')?.value ?? 0;
  };

  const before = await readTaskDuration();
  await page.waitForTimeout(durationMs);
  const after = await readTaskDuration();
  await session.detach();
  return ((after - before) / (durationMs / 1000)) * 100;
}

test.describe('performance', () => {
  test.skip(!perfEnabled, 'Set PERF=1 to run the periodic performance benchmark.');

  test('empty document stays under 5% main-thread CPU while idle', async ({ page }) => {
    await ready(page);

    const cpu = await idleCpuPercent(page, 3000);

    console.log(`empty idle main-thread CPU: ${cpu.toFixed(2)}%`);
    expect(cpu).toBeLessThan(5);
  });

  test('500 rectangles stay under 50% main-thread CPU while idle', async ({ page }) => {
    await ready(page);
    await page.evaluate(() => {
      const primitives = Array.from({ length: 500 }, (_, index) => ({
        Id: crypto.randomUUID(),
        Type: 'Rectangle',
        Props: {
          Name: `Rect ${index}`,
          PosX: (index % 25) * 20,
          PosY: Math.floor(index / 25) * 20,
          Width: 10,
          Height: 10,
          Color: 4294967295,
        },
      }));
      localStorage.setItem('penelopa.document', JSON.stringify({
        Version: 1,
        Primitives: primitives,
        SelectionIds: [],
      }));
    });

    await page.getByRole('button', { name: 'Load', exact: true }).click();
    await page.getByRole('button', { name: 'Replace', exact: true }).click();
    await expect(page.locator('.penelopa-tool-message')).toHaveText('Document loaded.');
    await page.waitForTimeout(1000);

    const cpu = await idleCpuPercent(page, 3000);

    console.log(`500 rectangles idle main-thread CPU: ${cpu.toFixed(2)}%`);
    expect(cpu).toBeLessThan(50);
  });
});
