import { defineConfig } from '@playwright/test';

// The end-to-end tests run the real thing: the API in Release serving the built Angular app (run `ng build` first,
// `npm run e2e` does), on a database created for the run and dropped afterwards.
//
// Locally the database is SQL Express with Windows authentication. In CI, point E2E_SQL_SERVER at the SQL Server
// service container, e.g. "Server=localhost,1433;User Id=sa;Password=...;TrustServerCertificate=True".
const port = 5199;
process.env['E2E_DB'] ??= `TeamBoardE2E_${Date.now()}`;
const sqlServer = process.env['E2E_SQL_SERVER'] ?? 'Server=localhost\\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True';

export default defineConfig({
  testDir: 'e2e',
  fullyParallel: false,
  workers: 1, // the tests share one server and one database
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [['list']],
  globalTeardown: './e2e/global-teardown.ts',
  use: {
    baseURL: `http://localhost:${port}`,
    // The installed Chrome, so no browser has to be downloaded. CI's runners have it too.
    channel: process.env['PW_CHANNEL'] ?? 'chrome',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  webServer: {
    command: 'dotnet run --project ../TaskManager.API --configuration Release --no-launch-profile',
    url: `http://localhost:${port}/health/ready`,
    timeout: 180_000,
    reuseExistingServer: false,
    env: {
      ASPNETCORE_ENVIRONMENT: 'Production',
      ASPNETCORE_URLS: `http://localhost:${port}`,
      Https__Redirect: 'false', // plain http on localhost
      Database__MigrateOnStartup: 'true',
      RateLimit__AuthPermitPerMinute: '1000',
      Serilog__MinimumLevel__Default: 'Warning',
      Jwt__Key: 'e2e-only-key-0123456789-0123456789-0123456789',
      ConnectionStrings__DefaultConnection: `${sqlServer};Database=${process.env['E2E_DB']}`,
      Spa__Root: '../client/dist/client/browser',
    },
  },
});
