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

test("browser document responses enforce the frontend security baseline", async ({
  page,
}) => {
  const response = await page.goto("/login");

  expect(response).not.toBeNull();
  const headers = response!.headers();
  expect(headers["content-security-policy"]).toContain("frame-ancestors 'none'");
  expect(headers["permissions-policy"]).toContain("camera=()");
  expect(headers["referrer-policy"]).toBe("strict-origin-when-cross-origin");
  expect(headers["x-content-type-options"]).toBe("nosniff");
  expect(headers["x-frame-options"]).toBe("DENY");
});

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

  const reportResponse = await page.request.get("/api/v1/reports/pilot");
  expect(reportResponse.ok()).toBeTruthy();
  const report = (await reportResponse.json()) as {
    missedCalls: number;
    recoveryMessagesSent: number;
    leadsWithInboundReply: number;
    methodology: string;
  };
  expect(report.missedCalls).toBe(1);
  expect(report.recoveryMessagesSent).toBe(1);
  expect(report.leadsWithInboundReply).toBe(1);
  expect(report.methodology).toContain("do not estimate revenue");
  const csvResponse = await page.request.get("/api/v1/reports/pilot.csv");
  expect(csvResponse.ok()).toBeTruthy();
  expect(await csvResponse.text()).toContain("missed_calls");
  await page.getByRole("link", { name: "Pilot report" }).click();
  await expect(page.getByRole("heading", { name: "Recovery report" })).toBeVisible();

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

  await page.getByRole("link", { name: /Open lead for Urgent plumbing caller/ }).click();
  await expect(page.getByRole("heading", { name: "Urgent plumbing caller" })).toBeVisible();
  await expect(page.getByText("Missed call captured")).toBeVisible();
  await expect(page.getByText(/Sorry we missed your call to Alpha Plumbing/)).toBeVisible();
  await expect(page.getByText("Delivered", { exact: true })).toBeVisible();
  await expect(page.getByText("Send initial recovery SMS")).toHaveCount(0);
  await expect(page.getByText("Automation: Active")).toBeVisible();
  await expect(page.getByText("AI-generated suggestion")).toBeVisible();
  await expect(page.getByText("Low confidence")).toBeVisible();
  await expect(page.getByText("Human review required.")).toBeVisible();
  await expect(page.getByText("Suggested staff reply · Not sent")).toBeVisible();
  await expect(page.getByText(/never sends a message or triggers a customer action/)).toBeVisible();

  await page.getByRole("button", { name: "Edit suggestion" }).click();
  await page.getByLabel("Urgency", { exact: true }).last().selectOption("High");
  const correctedSummary =
    "Staff confirmed the active leak and that a callback is the correct next step.";
  await page.getByLabel("Corrected summary").fill(correctedSummary);
  await page.getByLabel("Correction reason").fill("Customer details were verified by staff.");
  await page.getByRole("button", { name: "Save correction" }).click();
  await expect(page.getByText("Edited by staff")).toBeVisible();
  await expect(page.getByText(correctedSummary)).toBeVisible();
  await expect(page.getByText("AI suggestion edited")).toBeVisible();

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
  await expect(page.getByText("Automation: Paused by staff")).toBeVisible();
  await expect(page.getByText("Send initial recovery SMS")).toHaveCount(0);

  await page.getByRole("button", { name: "Resume automation" }).click();
  await expect(page.getByText("Automation: Active")).toBeVisible();
  await expect(page.getByText("Send initial recovery SMS")).toHaveCount(0);
  await page.getByRole("button", { name: "Pause automation" }).click();
  await expect(page.getByText("Automation: Paused by staff")).toBeVisible();
  await expect(page.getByText("Send initial recovery SMS")).toHaveCount(0);
  await page.getByRole("button", { name: "Resume automation" }).click();
  await expect(page.getByText("Automation: Active")).toBeVisible();

  const timeline = page.getByLabel("Conversation timeline");
  const noteText = "Call the customer after 3 PM.";
  const existingNoteCount = await timeline.getByText(noteText, { exact: true }).count();
  await page.getByLabel("Note").fill(noteText);
  await page.getByRole("button", { name: "Add note" }).click();
  await expect(timeline.getByText(noteText, { exact: true })).toHaveCount(existingNoteCount + 1);

  const manualMessage = "Thanks. A team member will call you shortly.";
  const existingMessageCount = await timeline.getByText(manualMessage, { exact: true }).count();
  await page.getByLabel("Send manual SMS").fill(manualMessage);
  await page.getByRole("button", { name: "Send SMS" }).click();
  await expect(timeline.getByText(manualMessage, { exact: true })).toHaveCount(existingMessageCount + 1);
  await expect(timeline.getByText("Staff sent", { exact: true }).last()).toBeVisible();
  await expect(timeline.getByText("Queued", { exact: true }).last()).toBeVisible();

  await page.getByLabel("Next status").selectOption("Qualified");
  await page.getByLabel("Reason or context").fill("Staff confirmed required details by phone.");
  await page.getByRole("button", { name: "Update status" }).click();
  await expect(page.getByText("Qualified", { exact: true })).toBeVisible();

  await expect(page.getByRole("link", { name: "Open approved booking page" })).toBeVisible();
  await page.getByRole("button", { name: "Queue booking link" }).click();
  await expect(page.getByText("Booking offered", { exact: true })).toBeVisible();
  await expect(page.getByText("Send booking link")).toBeVisible();

  await page.getByLabel("Next status").selectOption("Booked");
  await page.getByLabel("Reason or context").fill("Customer confirmed the appointment.");
  await page.getByRole("button", { name: "Update status" }).click();
  await expect(page.getByText("Booked", { exact: true })).toBeVisible();
  await expect(page.getByText("Send booking link")).toHaveCount(0);
});

test("owner can pause and resume tenant automation without losing dashboard access", async ({
  page,
}) => {
  await login(page, required("E2E_OWNER_EMAIL"), required("E2E_OWNER_PASSWORD"));

  const automationSummary = page.locator("summary.automation-switch-status");
  await automationSummary.click();
  await expect(
    page.getByText("Approved automated recovery and follow-up work can run for this tenant."),
  ).toBeVisible();
  await page.getByRole("button", { name: "Pause tenant automation" }).click();
  await expect(automationSummary).toHaveText("Tenant paused");
  await expect(page.getByRole("status")).toContainText("pending automated");
  await expect(page.getByRole("heading", { name: "Lead inbox" })).toBeVisible();

  const leadsResponse = await page.request.get("/api/v1/leads/?pageSize=25");
  expect(leadsResponse.ok()).toBeTruthy();

  await page.getByRole("button", { name: "Resume tenant automation" }).click();
  await expect(automationSummary).toHaveText("Automation on");
  await expect(page.getByRole("status")).toContainText("Tenant automation resumed");
});

test("workspace navigation and primary actions remain usable on mobile", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/login");

  await page.keyboard.press("Tab");
  await expect(page.getByRole("link", { name: "Skip to main content" })).toBeFocused();

  await page.getByLabel("Email address").fill(required("E2E_OWNER_EMAIL"));
  await page.getByLabel("Password").fill(required("E2E_OWNER_PASSWORD"));
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page).toHaveURL(/\/leads$/);

  const viewportFits = await page.evaluate(() =>
    document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1,
  );
  expect(viewportFits).toBeTruthy();

  const statusFilter = page.getByLabel("Status");
  const statusBounds = await statusFilter.boundingBox();
  expect(statusBounds?.height ?? 0).toBeGreaterThanOrEqual(44);

  const firstLead = page.getByRole("link", { name: /Open lead for/ }).first();
  await expect(firstLead).toBeVisible();
  const leadBounds = await firstLead.boundingBox();
  expect(leadBounds?.height ?? 0).toBeGreaterThanOrEqual(44);
  await firstLead.click();

  await expect(page.getByRole("heading", { name: "Conversation timeline" })).toBeVisible();
  const overflowingElements = await page.evaluate(() => {
    const viewportWidth = document.documentElement.clientWidth;
    return Array.from(document.body.querySelectorAll<HTMLElement>("*"))
      .filter((element) => {
        if (!element.checkVisibility()) return false;
        const bounds = element.getBoundingClientRect();
        return bounds.left < -1 || bounds.right > viewportWidth + 1;
      })
      .map((element) => ({
        className: element.className,
        tagName: element.tagName,
        text: element.innerText.slice(0, 80),
      }));
  });
  expect(overflowingElements).toEqual([]);
});
