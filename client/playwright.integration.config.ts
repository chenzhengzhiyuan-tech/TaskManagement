import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './e2e-integration',
  fullyParallel: false,
  workers: 1,
  reporter: [['list']],
  use: {
    baseURL: 'http://127.0.0.1:5173',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    viewport: { width: 1366, height: 768 },
    ...devices['Desktop Edge'],
    channel: 'msedge',
  },
  webServer: [
    {
      command: 'powershell -ExecutionPolicy Bypass -File ../server/run-integration.ps1',
      url: 'http://127.0.0.1:5080/api/health',
      reuseExistingServer: false,
      timeout: 120_000,
    },
    {
      command: 'pnpm exec vite --mode integration --host 127.0.0.1',
      url: 'http://127.0.0.1:5173',
      reuseExistingServer: false,
      timeout: 30_000,
    },
  ],
})
