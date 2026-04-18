import { defineConfig, devices } from '@playwright/test'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import dotenv from 'dotenv'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const repoRoot = path.resolve(__dirname, '..')

dotenv.config({ path: path.join(repoRoot, '.env') })

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:3000'

function isLocalBaseUrl(url: string): boolean {
  try {
    const { hostname } = new URL(url)
    return hostname === 'localhost' || hostname === '127.0.0.1'
  } catch {
    return false
  }
}

const startLocalServers = isLocalBaseUrl(baseURL)

const e2eEmail = (process.env.E2E_EMAIL ?? '').trim()
const e2ePassword = (process.env.E2E_PASSWORD ?? '').trim()
/** When set, Playwright uses your dev DB so an existing user can log in (CI keeps isolated e2e DB). */
const useDevSqlite =
  startLocalServers && !!e2eEmail && !!e2ePassword && process.env.E2E_ISOLATED_DB !== '1'

const sqliteAbsolute = useDevSqlite
  ? path.join(repoRoot, 'Backend', 'finance.db')
  : path.join(repoRoot, 'e2e-finance.db')

export default defineConfig({
  testDir: 'e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: 'html',
  use: {
    baseURL,
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: startLocalServers
    ? [
        {
          command: 'dotnet run --project Backend/Backend.csproj --urls http://localhost:5293',
          cwd: repoRoot,
          url: 'http://localhost:5293/health',
          reuseExistingServer: !process.env.CI,
          timeout: 180_000,
          env: {
            ...process.env,
            ASPNETCORE_ENVIRONMENT: 'Development',
            ConnectionStrings__DefaultConnection: `Data Source=${sqliteAbsolute.replace(/\\/g, '/')}`,
          },
        },
        {
          command: 'npm run dev',
          cwd: __dirname,
          url: 'http://localhost:3000',
          reuseExistingServer: !process.env.CI,
          timeout: 120_000,
        },
      ]
    : undefined,
})
