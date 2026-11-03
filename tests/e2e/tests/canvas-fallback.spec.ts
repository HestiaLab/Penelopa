import { test, expect } from '@playwright/test';

// SkiaSharp 的 SKGLView 在无法创建 WebGL 上下文时抛 NullReferenceException，
// 会把整个 Blazor 页面推入错误页。此用例确认应用会降级到 CPU 光栅画布。
test.use({
  launchOptions: {
    args: ['--disable-webgl', '--disable-webgl2', '--disable-3d-apis'],
  },
});

test('canvas falls back to CPU raster rendering instead of failing the page', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Circle', exact: true })).toBeVisible();
  await expect(page.locator('.penelopa-canvas'))
    .toHaveAttribute('data-pointer-ready', 'true');

  const canvas = page.locator('.penelopa-canvas canvas');
  await expect(canvas).toBeVisible();
  await expect(page.locator('#blazor-error-ui')).toBeHidden();

  // 添加图元会触发一次重绘；光栅画布应出现非黑像素（至少坐标轴）。
  await page.getByRole('button', { name: 'Circle', exact: true }).click();
  await page.getByRole('tab', { name: 'Primitives' }).click();
  await expect(page.locator('.penelopa-tree-item')).toHaveCount(1);

  // 光栅画布仍需支持命中测试与键盘编辑；新圆的中心在 (10, 10)。
  const bounds = await canvas.boundingBox();
  await page.mouse.click(bounds!.x + bounds!.width / 2 + 10, bounds!.y + bounds!.height / 2 - 10);
  await expect(page.locator('.penelopa-properties-title')).toHaveText('Circle');
  await page.locator('.penelopa-canvas').focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.locator('.penelopa-prop-field').filter({ hasText: 'CenterX' }).locator('input'))
    .toHaveValue('11');

  await expect.poll(async () => canvas.evaluate((element) => {
    const htmlCanvas = element as HTMLCanvasElement;
    const context = htmlCanvas.getContext('2d');
    if (!context) {
      return -1;
    }

    const { data } = context.getImageData(0, 0, htmlCanvas.width, htmlCanvas.height);
    for (let index = 0; index < data.length; index += 4) {
      if (data[index] !== 0 || data[index + 1] !== 0 || data[index + 2] !== 0) {
        return 1;
      }
    }

    return 0;
  })).toBe(1);

  await page.getByRole('link', { name: 'Simulate', exact: true }).click();
  await expect(page.locator('canvas')).toBeVisible();
  await expect(page.locator('#blazor-error-ui')).toBeHidden();
});
