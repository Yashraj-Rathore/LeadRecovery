import { defineConfig, devices } from "@playwright/test";
import path from "node:path";

const repositoryRoot = path.resolve(process.cwd(), "../..");

function required(name: string): string {
  const value = process.env[name];
  if (!value) {
    throw new Error(`${name} is required for the end-to-end environment.`);
  }

  return value;
}

const databaseConnectionString = required("CONNECTIONSTRINGS__DATABASE");
const ownerEmail = required("E2E_OWNER_EMAIL");
const ownerPassword = required("E2E_OWNER_PASSWORD");
const staffEmail = required("E2E_STAFF_EMAIL");
const staffPassword = required("E2E_STAFF_PASSWORD");
const betaOwnerEmail = required("E2E_BETA_OWNER_EMAIL");
const betaOwnerPassword = required("E2E_BETA_OWNER_PASSWORD");
const alphaUrgentPhone = required("E2E_ALPHA_URGENT_PHONE");
const alphaBookingPhone = required("E2E_ALPHA_BOOKING_PHONE");
const betaLeadPhone = required("E2E_BETA_LEAD_PHONE");

export default defineConfig({
  testDir: "./tests",
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: process.env.CI ? "github" : "list",
  use: {
    baseURL: "http://127.0.0.1:3000",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
  webServer: [
    {
      command:
        "dotnet run --project src/LeadRecovery.Api/LeadRecovery.Api.csproj " +
        "--configuration Release --no-build --no-launch-profile",
      cwd: repositoryRoot,
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: "Development",
        ASPNETCORE_URLS: "http://127.0.0.1:8080",
        CONNECTIONSTRINGS__DATABASE: databaseConnectionString,
        DemoSeed__Enabled: "true",
        DemoSeed__OwnerEmail: ownerEmail,
        DemoSeed__OwnerPassword: ownerPassword,
        DemoSeed__StaffEmail: staffEmail,
        DemoSeed__StaffPassword: staffPassword,
        DemoSeed__BetaOwnerEmail: betaOwnerEmail,
        DemoSeed__BetaOwnerPassword: betaOwnerPassword,
        DemoSeed__AlphaUrgentPhone: alphaUrgentPhone,
        DemoSeed__AlphaBookingPhone: alphaBookingPhone,
        DemoSeed__BetaLeadPhone: betaLeadPhone,
      },
      url: "http://127.0.0.1:8080/health/live",
      timeout: 120_000,
      reuseExistingServer: false,
    },
    {
      command:
        "node src/LeadRecovery.Web/node_modules/next/dist/bin/next " +
        "start src/LeadRecovery.Web --hostname 127.0.0.1 --port 3000",
      cwd: repositoryRoot,
      env: {
        ...process.env,
        API_BASE_URL: "http://127.0.0.1:8080",
      },
      url: "http://127.0.0.1:3000/login",
      timeout: 120_000,
      reuseExistingServer: false,
    },
  ],
});
