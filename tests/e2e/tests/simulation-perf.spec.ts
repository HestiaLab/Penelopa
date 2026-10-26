import { test, expect, Page } from '@playwright/test';
import {
  collectSimulationErrors,
  expectSimulationClean,
} from './simulation-helpers';

const perfEnabled = process.env.PERF === '1';

async function seedLargeDocument(
  page: Page,
  firstTrackKeyframes = 2,
): Promise<void> {
  await page.addInitScript(() => {
    (window as unknown as { __PENELOPA_PERF__: boolean }).__PENELOPA_PERF__ = true;
  });
  await page.addInitScript((keyframeCount) => {
    const primitives = [];
    const tracks = [];

    for (let index = 0; index < 500; index++) {
      const id = crypto.randomUUID();
      const posX = (index % 25) * 20;
      const posY = Math.floor(index / 25) * 20;
      primitives.push({
        Id: id,
        Type: 'Rectangle',
        Props: {
          Name: `Rect ${index}`,
          PosX: posX,
          PosY: posY,
          Width: 10,
          Height: 10,
          Color: 4294967295,
        },
      });

      if (index < 100) {
        const keyframes = index === 0 && keyframeCount > 2
          ? Array.from({ length: keyframeCount }, (_, frame) => ({
            Time: (frame * 5) / (keyframeCount - 1),
            Value: posX + frame,
            Interpolation: 'linear',
          }))
          : [
            { Time: 0, Value: posX, Interpolation: 'linear' },
            { Time: 5, Value: posX + 200, Interpolation: 'linear' },
          ];
        tracks.push({
          PrimitiveId: id,
          Property: 'PosX',
          Keyframes: keyframes,
        });
      }
    }

    localStorage.setItem('penelopa.document', JSON.stringify({
      Version: 2,
      Primitives: primitives,
      SelectionIds: [],
      Simulation: {
        Loop: true,
        Tracks: tracks,
      },
    }));
  }, firstTrackKeyframes);
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

async function loadDocument(page: Page): Promise<void> {
  await page.goto('/');
  await page.getByRole('button', { name: 'Load', exact: true }).click();
  await page.getByRole('button', { name: 'Replace', exact: true }).click();
  await expect(page.locator('.penelopa-tool-message').last())
    .toContainText('Document loaded.');
  await page.getByRole('link', { name: 'Simulate', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Simulation', exact: true })).toBeVisible();
}

test.describe('simulation performance', () => {
  test.skip(!perfEnabled, 'Set PERF=1 to run the periodic simulation benchmark.');

  test('500 primitives and 100 active tracks render at >= 55 FPS', async ({ page }) => {
    test.setTimeout(90_000);
    const errors = collectSimulationErrors(page);
    await seedLargeDocument(page);
    await loadDocument(page);

    await expect(page.getByRole('button', { name: 'Play', exact: true })).toBeEnabled();
    await page.getByRole('button', { name: 'Play', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Pause', exact: true })).toBeVisible();

    const readTicks = () => page.evaluate(
      () => (window as unknown as {
        penelopa: { getSimulationTickCount(): number };
      }).penelopa.getSimulationTickCount());
    const readPaints = () => page.evaluate(
      () => (window as unknown as {
        penelopa: { getSimulationPaintCount(): Promise<number> };
      }).penelopa.getSimulationPaintCount());
    const readFrames = () => page.evaluate(
      () => (window as unknown as {
        penelopa: { getSimulationFrameCount(): Promise<number> };
      }).penelopa.getSimulationFrameCount());

    const startTicks = await readTicks();
    const startPaints = await readPaints();
    const startFrames = await readFrames();
    const startedAt = Date.now();
    await page.waitForTimeout(5_000);
    const endTicks = await readTicks();
    const endPaints = await readPaints();
    const endFrames = await readFrames();
    const elapsedSeconds = (Date.now() - startedAt) / 1000;
    const fps = (endTicks - startTicks) / elapsedSeconds;
    const paintFps = (endPaints - startPaints) / elapsedSeconds;
    const frameFps = (endFrames - startFrames) / elapsedSeconds;

    console.log(
      `simulation ${fps.toFixed(1)} tick/s, ${frameFps.toFixed(1)} .NET frame/s, `
      + `${paintFps.toFixed(1)} paint/s (${endTicks - startTicks} ticks, `
      + `${endFrames - startFrames} frames, ${endPaints - startPaints} paints / `
      + `${elapsedSeconds.toFixed(2)}s)`,
    );

    await page.getByRole('button', { name: 'Stop', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Play', exact: true })).toBeVisible();
    await expect(page.locator('.penelopa-simulation-time')).toHaveText('0.000s');
    await page.waitForTimeout(500);

    const idleCpu = await idleCpuPercent(page, 2_000);
    console.log(`simulation idle main-thread CPU: ${idleCpu.toFixed(2)}%`);

    expect(fps).toBeGreaterThanOrEqual(55);
    expect(frameFps).toBeGreaterThanOrEqual(55);
    expect(paintFps).toBeGreaterThanOrEqual(55);
    expect(idleCpu).toBeLessThan(5);
    await expectSimulationClean(page, errors);
  });

  test('selected 200-keyframe track keeps >= 59 tick and >= 55 paint per second', async ({ page }) => {
    test.setTimeout(90_000);
    const errors = collectSimulationErrors(page);
    await seedLargeDocument(page, 200);
    await loadDocument(page);

    await page.getByRole('button', { name: 'Rect 0, has animation', exact: true }).click();
    const posXRow = page.locator('.penelopa-simulation-property')
      .filter({ hasText: 'PosX' })
      .first();
    await expect(posXRow).toContainText('200 keyframe(s)');
    await expect(page.locator('.penelopa-simulation-keyframe')).toHaveCount(200);

    await page.getByRole('button', { name: 'Play', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Pause', exact: true })).toBeVisible();

    const readTicks = () => page.evaluate(
      () => (window as unknown as {
        penelopa: { getSimulationTickCount(): number };
      }).penelopa.getSimulationTickCount());
    const readPaints = () => page.evaluate(
      () => (window as unknown as {
        penelopa: { getSimulationPaintCount(): Promise<number> };
      }).penelopa.getSimulationPaintCount());

    const startTicks = await readTicks();
    const startPaints = await readPaints();
    const startedAt = Date.now();
    await page.waitForTimeout(5_000);
    const endTicks = await readTicks();
    const endPaints = await readPaints();
    const elapsedSeconds = (Date.now() - startedAt) / 1000;
    const ticksPerSecond = (endTicks - startTicks) / elapsedSeconds;
    const paintFps = (endPaints - startPaints) / elapsedSeconds;

    console.log(
      `selected 200-keyframe track ${ticksPerSecond.toFixed(2)} tick/s, `
      + `${paintFps.toFixed(2)} paint/s `
      + `(${endTicks - startTicks} ticks, ${endPaints - startPaints} paints / ${elapsedSeconds.toFixed(2)}s)`,
    );

    await page.getByRole('button', { name: 'Stop', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Play', exact: true })).toBeVisible();
    await expect(page.locator('.penelopa-simulation-time')).toHaveText('0.000s');

    expect(ticksPerSecond).toBeGreaterThanOrEqual(59);
    expect(paintFps).toBeGreaterThanOrEqual(55);
    await expectSimulationClean(page, errors);
  });

  test('60s looping playback keeps DOM, listener and heap counts bounded', async ({ page }) => {
    test.setTimeout(120_000);
    const errors = collectSimulationErrors(page);
    await seedLargeDocument(page);
    await loadDocument(page);

    await page.getByRole('button', { name: 'Play', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Pause', exact: true })).toBeVisible();
    await page.waitForTimeout(2_000);

    const session = await page.context().newCDPSession(page);
    await session.send('Performance.enable');
    const readHeap = async () => {
      const metrics = await session.send('Performance.getMetrics');
      return metrics.metrics.find((metric) => metric.name === 'JSHeapUsedSize')?.value ?? 0;
    };

    const beforeDom = await session.send('Memory.getDOMCounters');
    const beforeHeap = await readHeap();
    await page.waitForTimeout(60_000);
    const afterDom = await session.send('Memory.getDOMCounters');
    const afterHeap = await readHeap();
    await session.detach();

    const heapGrowthMb = (afterHeap - beforeHeap) / (1024 * 1024);
    console.log(
      `60s soak: heap ${beforeHeap.toFixed(1)} -> ${afterHeap.toFixed(1)} bytes (+${heapGrowthMb.toFixed(1)} MiB), `
      + `nodes ${beforeDom.nodes} -> ${afterDom.nodes}, `
      + `listeners ${beforeDom.jsEventListeners} -> ${afterDom.jsEventListeners}`,
    );

    await page.getByRole('button', { name: 'Stop', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Play', exact: true })).toBeVisible();

    expect(afterDom.documents - beforeDom.documents).toBeLessThanOrEqual(1);
    expect(afterDom.nodes - beforeDom.nodes).toBeLessThanOrEqual(200);
    expect(afterDom.jsEventListeners - beforeDom.jsEventListeners).toBeLessThanOrEqual(2);
    expect(heapGrowthMb).toBeLessThan(8);
    await expectSimulationClean(page, errors);
  });
});
