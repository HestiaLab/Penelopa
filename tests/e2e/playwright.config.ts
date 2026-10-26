import { defineConfig } from '@playwright/test';

const channel = process.env.PLAYWRIGHT_CHANNEL;

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  timeout: 30_000,
  expect: {
    timeout: 10_000,
  },
  reporter: [['list']],
  use: {
    baseURL: 'http://127.0.0.1:5170',
    viewport: { width: 1440, height: 900 },
    headless: true,
    ...(channel ? { channel } : { launchOptions: { args: ['--enable-unsafe-swiftshader'] } }),
  },
  webServer: {
    command:
      'dotnet run --project ../../src/Penelopa.Web --no-build --no-launch-profile --urls http://127.0.0.1:5170',
    url: 'http://127.0.0.1:5170',
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
  },
});
