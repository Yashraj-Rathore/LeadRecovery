import { expect, Page, test } from "@playwright/test";

type LeadPage = {
  items: Array<{ id: string; displayName: string | null }>;
};

type LeadDetail = {
  lead: {
    id: string;
    assignedUserId: string | null;
    rowVersion: string;
    automationState: string;
  };
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
  await expect(page.getByRole("heading", { name: "Beta tenant lead" })).toBeVisible();

  const betaPageResponse = await page.request.get("/api/v1/leads/?pageSize=25");
  expect(betaPageResponse.ok()).toBeTruthy();
  const betaPage = (await betaPageResponse.json()) as LeadPage;
  const betaLeadId = betaPage.items.at(0)?.id;
  expect(betaLeadId).toBeTruthy();

  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(page).toHaveURL(/\/login$/);

  await login(page, required("E2E_OWNER_EMAIL"), required("E2E_OWNER_PASSWORD"));
  await expect(page.getByText("Alpha Plumbing")).toBeVisible();
  await expect(page.getByRole("heading", { name: "Urgent plumbing caller" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Booking request" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Beta tenant lead" })).toHaveCount(0);

  const crossTenantResponse = await page.request.get(`/api/v1/leads/${betaLeadId}`);
  expect(crossTenantResponse.status()).toBe(404);
});

test("staff operates a lead with accessible filters, conflict recovery, and safe messaging", async ({
  page,
}) => {
  await login(page, required("E2E_OWNER_EMAIL"), required("E2E_OWNER_PASSWORD"));

  const statusFilter = page.getByLabel("Status");
  await statusFilter.focus();
  await page.keyboard.press("Tab");
  await expect(page.getByLabel("Urgency")).toBeFocused();
  await statusFilter.selectOption("NeedsHuman");
  await expect(page.getByRole("heading", { name: "Urgent plumbing caller" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Booking request" })).toHaveCount(0);

  await page.getByRole("link", { name: /Open lead Urgent plumbing caller/ }).click();
  await expect(page.getByRole("heading", { name: "Urgent plumbing caller" })).toBeVisible();
  await expect(page.getByText("Missed call captured")).toBeVisible();
  await expect(page.getByText("SendInitialRecoverySms")).toBeVisible();
  await expect(page.getByText("Automation: Active")).toBeVisible();

  const detailResponse = await page.request.get(`/api/v1/leads/${page.url().split("/").at(-1)}`);
  expect(detailResponse.ok()).toBeTruthy();
  const detail = (await detailResponse.json()) as LeadDetail;
  const sessionResponse = await page.request.get("/api/v1/auth/me");
  const session = (await sessionResponse.json()) as { userId: string };
  const csrfResponse = await page.request.get("/api/v1/auth/csrf");
  const csrf = (await csrfResponse.json()) as { token: string };
  const externalAssignment = await page.request.post(
    `/api/v1/leads/${detail.lead.id}/assignment`,
    {
      headers: { "X-CSRF-TOKEN": csrf.token },
      data: {
        assignedUserId: detail.lead.assignedUserId === null ? session.userId : null,
        expectedRowVersion: detail.lead.rowVersion,
      },
    },
  );
  expect(externalAssignment.ok()).toBeTruthy();

  await page.getByRole("button", { name: "Pause automation" }).click();
  await expect(page.getByRole("alert").filter({
    hasText: "This lead changed while you were viewing it",
  })).toContainText(
    "This lead changed while you were viewing it",
  );
  await page.getByRole("button", { name: "Pause automation" }).click();
  await expect(page.getByText("Automation: PausedByUser")).toBeVisible();
  await expect(page.getByText("SendInitialRecoverySms")).toHaveCount(0);

  await page.getByRole("button", { name: "Resume automation" }).click();
  await expect(page.getByText("Automation: Active")).toBeVisible();
  await expect(page.getByText("SendInitialRecoverySms")).toBeVisible();
  await page.getByRole("button", { name: "Pause automation" }).click();
  await expect(page.getByText("Automation: PausedByUser")).toBeVisible();
  await expect(page.getByText("SendInitialRecoverySms")).toHaveCount(0);
  await page.getByRole("button", { name: "Resume automation" }).click();
  await expect(page.getByText("Automation: Active")).toBeVisible();

  await page.getByLabel("Note").fill("Call the customer after 3 PM.");
  await page.getByRole("button", { name: "Add note" }).click();
  await expect(page.getByText("Call the customer after 3 PM.")).toBeVisible();

  await page.getByLabel("Send manual SMS").fill(
    "Thanks. A team member will call you shortly.",
  );
  await page.getByRole("button", { name: "Send SMS" }).click();
  await expect(page.getByText("Thanks. A team member will call you shortly.")).toBeVisible();
  await expect(page.getByText("Manual", { exact: true })).toBeVisible();
  await expect(page.getByText("Queued", { exact: true })).toBeVisible();

  await page.getByLabel("Next status").selectOption("Qualified");
  await page.getByLabel("Reason or context").fill("Staff confirmed required details by phone.");
  await page.getByRole("button", { name: "Update status" }).click();
  await expect(page.getByText("Qualified", { exact: true })).toBeVisible();

  await expect(page.getByRole("link", { name: "Open approved booking page" })).toBeVisible();
  await page.getByRole("button", { name: "Queue booking link" }).click();
  await expect(page.getByText("BookingOffered", { exact: true })).toBeVisible();
  await expect(page.getByText("SendBookingLink")).toBeVisible();

  await page.getByLabel("Next status").selectOption("Booked");
  await page.getByLabel("Reason or context").fill("Customer confirmed the appointment.");
  await page.getByRole("button", { name: "Update status" }).click();
  await expect(page.getByText("Booked", { exact: true })).toBeVisible();
  await expect(page.getByText("SendBookingLink")).toHaveCount(0);
});
