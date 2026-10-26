import { test, expect, Page } from '@playwright/test';
import fs from 'fs';
import path from 'path';
import {
  collectSimulationErrors,
  expectSimulationClean,
} from './simulation-helpers';

const samplePath = path.resolve(
  __dirname,
  '../../../src/Penelopa.Web/wwwroot/samples/loop-study-01.penelopa.json');

async function loadSample(page: Page): Promise<void> {
  await page.goto('/');
  await page.getByRole('button', { name: 'Load Loop Study 01', exact: true }).click();
  await expect(page.locator('.penelopa-canvas-notice')).toContainText(
    'Sample loaded. Select Simulate → Play.');
}

test('empty canvas loads the bundled sample and opens its two-track simulation', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await loadSample(page);
  expect(await page.evaluate(() => localStorage.getItem('penelopa.document'))).toBeNull();

  await page.getByRole('tab', { name: 'Primitives', exact: true }).click();
  await expect(page.locator('.penelopa-tree-item.selected')).toContainText(
    'Step Indicator');

  await page.getByRole('link', { name: 'Simulate', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Simulation', exact: true })).toBeVisible();
  await expect(
    page.getByRole('button', { name: /Step Indicator/ }),
  ).toBeVisible();
  await expect(page.locator('.penelopa-simulation-track-label')).toHaveCount(2);
  await expect(page.locator('.penelopa-simulation-time')).toHaveText('0.000s');

  await page.locator('.penelopa-simulation-transport')
    .getByRole('button', { name: 'Play', exact: true }).click();
  await expect.poll(async () => {
    const text = await page.locator('.penelopa-simulation-time').textContent();
    return Number.parseFloat(text ?? '0');
  }).toBeGreaterThan(0.1);

  await page.locator('.penelopa-simulation-transport')
    .getByRole('button', { name: 'Stop', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Play', exact: true })).toBeVisible();
  await expectSimulationClean(page, errors);
});

test('import file confirms, replaces in memory, and does not write localStorage', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await page.goto('/');
  const chooserPromise = page.waitForEvent('filechooser');
  await page.getByText('Import file…', { exact: true }).click();
  const chooser = await chooserPromise;
  await chooser.setFiles(samplePath);

  await expect(
    page.getByRole('button', { name: 'Replace document', exact: true }),
  ).toBeVisible();
  await expect(page.getByText('19 primitive(s), 5 active track(s).')).toBeVisible();

  await page.getByRole('button', { name: 'Replace document', exact: true }).click();
  await expect(page.locator('.penelopa-import-undo')).toContainText(
    'Imported loop-study-01.penelopa.json.');
  await expect(
    page.getByRole('button', { name: 'Undo import', exact: true }),
  ).toBeVisible();
  expect(await page.evaluate(() => localStorage.getItem('penelopa.document'))).toBeNull();

  await page.getByRole('tab', { name: 'Primitives', exact: true }).click();
  await expect(page.locator('.penelopa-tree-item.selected')).toContainText(
    'Step Indicator');

  await page.getByRole('link', { name: 'Simulate', exact: true }).click();
  await expect(page.locator('.penelopa-simulation-track-label')).toHaveCount(2);
  await expectSimulationClean(page, errors);
});

test('export downloads a reusable v2 file without writing localStorage', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await loadSample(page);

  const downloadPromise = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export file…', exact: true }).click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toMatch(/^penelopa-\d{8}-\d{6}\.penelopa\.json$/);

  const downloadedPath = await download.path();
  expect(downloadedPath).not.toBeNull();
  const json = fs.readFileSync(downloadedPath!, 'utf8');
  const document = JSON.parse(json) as {
    Version: number;
    Primitives: unknown[];
    Simulation: { Loop: boolean; Tracks: unknown[] };
  };
  expect(document.Version).toBe(2);
  expect(document.Primitives).toHaveLength(13);
  expect(document.Simulation.Loop).toBe(true);
  expect(document.Simulation.Tracks).toHaveLength(5);
  expect(await page.evaluate(() => localStorage.getItem('penelopa.document'))).toBeNull();

  await page.goto('/');
  await page.locator('input[type=file]').setInputFiles({
    name: download.suggestedFilename(),
    mimeType: 'application/json',
    buffer: fs.readFileSync(downloadedPath!),
  });
  await expect(
    page.getByRole('button', { name: 'Replace document', exact: true }),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Replace document', exact: true }).click();
  await page.getByRole('tab', { name: 'Primitives', exact: true }).click();
  await expect(page.locator('.penelopa-tree-item.selected')).toContainText(
    'Step Indicator');
  await expectSimulationClean(page, errors);
});

test('invalid import keeps the current document and reports a readable error', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await page.goto('/');
  await page.getByRole('button', { name: 'Circle', exact: true }).click();

  await page.locator('input[type=file]').setInputFiles({
    name: 'broken.penelopa.json',
    mimeType: 'application/json',
    buffer: Buffer.from('not json'),
  });

  await expect(page.locator('.penelopa-tool-message.is-error').last()).toContainText(
    'not a valid or supported Penelopa document');
  await expect(
    page.getByRole('button', { name: 'Replace document', exact: true }),
  ).toHaveCount(0);
  await page.getByRole('tab', { name: 'Primitives', exact: true }).click();
  await expect(page.locator('.penelopa-tree-item')).toHaveCount(1);
  await expectSimulationClean(page, errors);
});

test('dragging a document over the whole editor imports it and opens Tools', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await page.goto('/');
  await page.getByRole('tab', { name: 'Primitives', exact: true }).click();
  const json = fs.readFileSync(samplePath, 'utf8');

  await page.evaluate((content) => {
    const shell = document.querySelector('.penelopa-shell');
    const dataTransfer = new DataTransfer();
    dataTransfer.items.add(new File(
      [content],
      'dropped.penelopa.json',
      { type: 'application/json' }));
    (window as unknown as { __penelopaDrop: DataTransfer }).__penelopaDrop = dataTransfer;
    shell!.dispatchEvent(new DragEvent('dragenter', {
      bubbles: true,
      cancelable: true,
      dataTransfer,
    }));
  }, json);

  await expect(page.locator('.penelopa-shell')).toHaveClass(/penelopa-drop-active/);

  await page.evaluate(() => {
    const shell = document.querySelector('.penelopa-shell');
    const dataTransfer =
      (window as unknown as { __penelopaDrop: DataTransfer }).__penelopaDrop;
    shell!.dispatchEvent(new DragEvent('dragover', {
      bubbles: true,
      cancelable: true,
      dataTransfer,
    }));
    shell!.dispatchEvent(new DragEvent('drop', {
      bubbles: true,
      cancelable: true,
      dataTransfer,
    }));
  });

  await expect(page.locator('.penelopa-tools')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Replace document', exact: true }))
    .toBeVisible();
  await expect(page.locator('.penelopa-shell')).not.toHaveClass(/penelopa-drop-active/);

  await page.getByRole('button', { name: 'Replace document', exact: true }).click();
  await expect(page.locator('.penelopa-import-undo'))
    .toContainText('Imported dropped.penelopa.json.');
  expect(await page.evaluate(() => localStorage.getItem('penelopa.document'))).toBeNull();

  await page.getByRole('tab', { name: 'Primitives', exact: true }).click();
  await expect(page.locator('.penelopa-tree-item.selected'))
    .toContainText('Step Indicator');
  await expectSimulationClean(page, errors);
});


test('undo import restores the previous document, selection, and document history', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await page.goto('/');
  await page.getByRole('button', { name: 'Circle', exact: true }).click();
  await page.getByRole('tab', { name: 'Primitives', exact: true }).click();
  await page.locator('.penelopa-tree-item').filter({ hasText: 'Circle' }).click();
  await page.getByRole('tab', { name: 'Tools', exact: true }).click();

  const radius = page.locator('.penelopa-prop-field')
    .filter({ hasText: 'Radius' })
    .locator('input');
  await radius.fill('20');
  await radius.press('Tab');
  await expect.poll(async () => radius.inputValue()).toBe('20');

  await page.locator('input[type=file]').setInputFiles(samplePath);
  await page.getByRole('button', { name: 'Replace document', exact: true }).click();
  await expect(page.locator('.penelopa-import-undo')).toBeVisible();

  await page.getByRole('button', { name: 'Undo import', exact: true }).click();

  await expect(page.locator('.penelopa-import-undo')).toHaveCount(0);
  await expect(page.locator('.penelopa-properties-title')).toHaveText('Circle');
  await expect.poll(async () => radius.inputValue()).toBe('20');
  await page.getByRole('tab', { name: 'Primitives', exact: true }).click();
  await expect(page.locator('.penelopa-tree-item')).toHaveCount(1);

  await page.keyboard.press('Control+z');
  await expect.poll(async () => radius.inputValue()).toBe('5');
  await expectSimulationClean(page, errors);
});

test('leaving playback does not discard the import rollback; undo restores the empty document', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await page.goto('/');
  await page.locator('input[type=file]').setInputFiles(samplePath);
  await page.getByRole('button', { name: 'Replace document', exact: true }).click();
  await expect(page.locator('.penelopa-import-undo')).toBeVisible();

  await page.getByRole('link', { name: 'Simulate', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Simulation', exact: true })).toBeVisible();
  await page.locator('.penelopa-simulation-transport')
    .getByRole('button', { name: 'Play', exact: true }).click();
  await expect.poll(async () => {
    const text = await page.locator('.penelopa-simulation-time').textContent();
    return Number.parseFloat(text ?? '0');
  }).toBeGreaterThan(0.1);

  await page.getByRole('button', { name: 'Back to editor', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Undo import', exact: true }),
  ).toBeVisible();

  await page.getByRole('button', { name: 'Undo import', exact: true }).click();
  await expect(page.locator('.penelopa-import-undo')).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Start with a sample', exact: true }))
    .toBeVisible();
  await expectSimulationClean(page, errors);
});

test('a successful save discards the import rollback', async ({ page }) => {
  const errors = collectSimulationErrors(page);
  await page.goto('/');
  await page.locator('input[type=file]').setInputFiles(samplePath);
  await page.getByRole('button', { name: 'Replace document', exact: true }).click();
  await expect(page.locator('.penelopa-import-undo')).toBeVisible();

  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.locator('.penelopa-import-undo')).toHaveCount(0);
  await expect(page.locator('.penelopa-tool-message').last()).toContainText('Document saved.');
  await expectSimulationClean(page, errors);
});
