import { expect, Page, test } from "@playwright/test";

type LeadPage = {
  items: Array<{ id: string; displayName: string | null }>;
};

function required(name: string): string {
  const value = process.env[name];
  if (!value) {
    throw new Error(`${name} is required for this test.`);
  }

  return value;
}

async function login(page: Page, email: string, password: string) {
  await page.goto("/login");
  await page.getByLabel("Email address").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page).toHaveURL(/\/leads$/);
  await expect(page.getByRole("heading", { name: "Lead inbox" })).toBeVisible();
}

test("login renders only the active tenant and cross-tenant detail stays hidden", async ({
  page,
}) => {
  await login(
    page,
    required("E2E_BETA_OWNER_EMAIL"),
    required("E2E_BETA_OWNER_PASSWORD"),
  );
  await expect(page.getByText("Beta HVAC")).toBeVisible();
  await expect(page.getByText("Beta tenant lead")).toBeVisible();

  const betaPageResponse = await page.request.get("/api/v1/leads/?pageSize=25");
  expect(betaPageResponse.ok()).toBeTruthy();
  const betaPage = (await betaPageResponse.json()) as LeadPage;
  const betaLeadId = betaPage.items.at(0)?.id;
  expect(betaLeadId).toBeTruthy();

  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(page).toHaveURL(/\/login$/);

  await login(page, required("E2E_OWNER_EMAIL"), required("E2E_OWNER_PASSWORD"));
  await expect(page.getByText("Alpha Plumbing")).toBeVisible();
  await expect(page.getByText("Urgent plumbing caller")).toBeVisible();
  await expect(page.getByText("Booking request")).toBeVisible();
  await expect(page.getByText("Beta tenant lead")).toHaveCount(0);

  const crossTenantResponse = await page.request.get(`/api/v1/leads/${betaLeadId}`);
  expect(crossTenantResponse.status()).toBe(404);
});
