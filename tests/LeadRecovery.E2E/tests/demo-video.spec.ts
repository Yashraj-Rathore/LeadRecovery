import { expect, Page, test } from "@playwright/test";
import path from "node:path";

const assets = path.resolve(process.cwd(), "../../docs/pilot/assets");

function required(name: string): string {
  const value = process.env[name];
  if (!value) throw new Error(`${name} is required for demo capture.`);
  return value;
}

async function caption(
  page: Page,
  kicker: string,
  title: string,
  body: string,
  durationMs: number,
) {
  await page.evaluate(({ kicker, title, body }) => {
    document.querySelector("#demo-caption")?.remove();
    const overlay = document.createElement("aside");
    overlay.id = "demo-caption";
    overlay.setAttribute("aria-hidden", "true");
    overlay.innerHTML = `<small>${kicker}</small><strong>${title}</strong><span>${body}</span>`;
    Object.assign(overlay.style, {
      position: "fixed", zIndex: "99999", left: "32px", right: "32px", bottom: "24px",
      display: "grid", gridTemplateColumns: "auto 1fr", gap: "2px 18px", alignItems: "center",
      padding: "16px 20px", border: "1px solid rgba(255,255,255,.24)", borderRadius: "16px",
      background: "rgba(10,35,29,.94)", color: "white", boxShadow: "0 18px 48px rgba(0,0,0,.24)",
      fontFamily: "Inter,Segoe UI,sans-serif",
    });
    const small = overlay.querySelector("small") as HTMLElement;
    const strong = overlay.querySelector("strong") as HTMLElement;
    const span = overlay.querySelector("span") as HTMLElement;
    Object.assign(small.style, { gridRow: "1 / span 2", color: "#9fe3c9", fontWeight: "800", letterSpacing: ".12em", textTransform: "uppercase" });
    Object.assign(strong.style, { fontSize: "21px", lineHeight: "1.15" });
    Object.assign(span.style, { color: "#d7e8e1", fontSize: "14px" });
    document.body.append(overlay);
  }, { kicker, title, body });
  await page.waitForTimeout(durationMs);
  await page.locator("#demo-caption").evaluate((element) => element.remove());
}

test("capture the fictional pilot walkthrough", async ({ page }) => {
  await page.goto("/login");
  await caption(page, "LeadRecovery", "A missed call becomes an owned next step", "Fictional demo tenant · one-minute product tour", 2500);
  await page.getByLabel("Email address").fill(required("E2E_OWNER_EMAIL"));
  await page.getByLabel("Password").fill(required("E2E_OWNER_PASSWORD"));
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Lead inbox" })).toBeVisible();
  await caption(page, "01 · Prioritize", "One focused operations queue", "Human-review and urgent work rise above routine follow-up.", 5500);
  await page.screenshot({ path: path.join(assets, "01-inbox.png") });

  await page.getByRole("link", { name: /Open lead for Urgent plumbing caller/ }).click();
  await expect(page.getByRole("heading", { name: "Urgent plumbing caller" })).toBeVisible();
  await caption(page, "02 · Recover", "The full missed-call thread stays together", "The initial SMS was delivered, the customer replied, and the lead now requires staff review.", 7500);
  await page.screenshot({ path: path.join(assets, "02-missed-call-thread.png") });

  await page.getByText("AI-generated suggestion").scrollIntoViewIfNeeded();
  await caption(page, "03 · Review", "AI assists; a person decides", "Low-confidence and safety-sensitive suggestions are clearly routed to human review.", 7500);
  await page.screenshot({ path: path.join(assets, "03-human-review.png") });

  await page.getByRole("heading", { name: "Conversation timeline" }).scrollIntoViewIfNeeded();
  await caption(page, "04 · Verify", "Every customer and workflow event is visible", "Calls, messages, status, delivery, notes, and scheduled work share one auditable timeline.", 7000);

  await page.getByRole("link", { name: "Pilot report" }).click();
  await expect(page.getByRole("heading", { name: "Recovery report" })).toBeVisible();
  await caption(page, "05 · Measure", "A baseline report teams can export", "Track response, replies, bookings, failures, opt-outs, and review workload without making revenue claims.", 9000);
  await page.screenshot({ path: path.join(assets, "04-pilot-report.png") });

  await caption(page, "Proof · Idempotency", "Duplicate provider callbacks do not duplicate work", "Reproduce with eng/Invoke-DemoProof.ps1 · DuplicateCallbackHasNoDuplicateEffect", 6500);
  await caption(page, "Proof · Consent", "STOP suppresses every future automated send", "The proof test verifies idempotent opt-out, pending-action cancellation, and send blocking.", 6500);
  await caption(page, "Pilot-ready", "Configure. Demonstrate. Measure.", "Onboarding validates before activation, and all demo people, businesses, numbers, and outcomes are fictional.", 3500);
});
