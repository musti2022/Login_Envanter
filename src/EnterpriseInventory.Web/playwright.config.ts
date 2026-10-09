import { randomBytes } from 'node:crypto'
import { defineConfig, devices } from '@playwright/test'

// End-to-end tests: the production build of the React app (vite preview) in Chromium, talking through the Vite
// proxy to the real API in Development with the fake directory and a real SQL Server database.
// See e2e/README.md for the one-time setup.

const apiUrl = 'https://localhost:7262'
const webUrl = 'http://localhost:4174'

const connection = process.env.EI_E2E_SQL_CONNECTION
if (!connection) {
  throw new Error('EI_E2E_SQL_CONNECTION is not set: point it at a database with the migrations applied (see e2e/README.md).')
}
if (!process.env.NODE_EXTRA_CA_CERTS) {
  throw new Error('NODE_EXTRA_CA_CERTS is not set: export the ASP.NET Core development certificate for Node (see README.md).')
}

// A new password for the fake users on every run, so none is ever written down. Workers inherit it.
process.env.EI_E2E_PASSWORD ??= randomBytes(18).toString('base64url')
const password = process.env.EI_E2E_PASSWORD

const fakeUsers = [
  { UserName: 'e2e.admin', DisplayName: 'E2E Yönetici' },
  { UserName: 'e2e.outsider', DisplayName: 'E2E Grup Dışı', IsAllowedGroupMember: 'false' },
  { UserName: 'e2e.disabled', DisplayName: 'E2E Pasif', IsDisabled: 'true' },
]

const apiEnvironment: Record<string, string> = {
  ASPNETCORE_ENVIRONMENT: 'Development',
  ASPNETCORE_URLS: apiUrl,
  ConnectionStrings__DefaultConnection: connection,
  ActiveDirectory__Mode: 'Fake',
  // Every sign-in of the suite comes from the same address.
  RateLimiting__LoginPermitLimit: '100',
}
fakeUsers.forEach((user, index) => {
  for (const [key, value] of Object.entries({ ...user, Password: password })) {
    apiEnvironment[`ActiveDirectory__FakeUsers__${index}__${key}`] = value
  }
})

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: webUrl,
    locale: 'tr-TR',
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      command: 'dotnet run --project ../EnterpriseInventory.Api --no-launch-profile',
      url: `${apiUrl}/api/health/ready`,
      env: apiEnvironment,
      reuseExistingServer: false,
      timeout: 180_000,
      stdout: 'ignore',
      stderr: 'pipe',
    },
    {
      command: 'npm run build && npx vite preview --port 4174 --strictPort',
      // Answers only when the proxy reaches the API over verified HTTPS.
      url: `${webUrl}/api/health/live`,
      env: { VITE_DEV_API_PROXY_TARGET: apiUrl },
      reuseExistingServer: false,
      timeout: 180_000,
    },
  ],
})
