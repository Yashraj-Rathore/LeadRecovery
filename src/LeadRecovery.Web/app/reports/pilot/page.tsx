import { headers } from "next/headers";
import { redirect } from "next/navigation";

import { AuthSession, getApiBaseUrl, PilotReport } from "../../../lib/api";
import { WorkspaceHeader } from "../../leads/workspace-header";
import { UiIcon } from "../../ui-icon";

async function load<T>(path: string): Promise<T | null> {
  const requestHeaders = await headers();
  const response = await fetch(`${getApiBaseUrl()}${path}`, {
    cache: "no-store",
    headers: { cookie: requestHeaders.get("cookie") ?? "" },
  });
  if (response.status === 401) return null;
  if (!response.ok) throw new Error("The pilot report could not be loaded.");
  return (await response.json()) as T;
}

function dateOnly(value: string): string {
  return value.slice(0, 10);
}

export default async function PilotReportPage({
  searchParams,
}: {
  searchParams: Promise<{ from?: string; to?: string }>;
}) {
  const query = await searchParams;
  const parameters = new URLSearchParams();
  if (query.from) parameters.set("from", query.from);
  if (query.to) parameters.set("to", query.to);
  const suffix = parameters.size ? `?${parameters.toString()}` : "";
  const [session, report] = await Promise.all([
    load<AuthSession>("/api/v1/auth/me"),
    load<PilotReport>(`/api/v1/reports/pilot${suffix}`),
  ]);
  if (!session || !report) redirect("/login");

  const metrics = [
    ["Missed calls", report.missedCalls, "Pilot baseline"],
    ["Recovery SMS sent", report.recoveryMessagesSent, "Initial messages"],
    ["Inbound replies", report.leadsWithInboundReply, `${report.replyRatePercent}% reply rate`],
    ["Booked", report.bookedLeads, `${report.bookingRatePercent}% operational rate`],
    ["Qualified", report.qualifiedLeads, "Current workflow status"],
    ["Needs review", report.needsHumanReview, "Human decision required"],
    ["Opt-outs", report.optOuts, "Automation suppressed"],
    ["Failed SMS", report.failedMessages, "Provider failures"],
  ] as const;

  return (
    <>
      <WorkspaceHeader session={session} current="pilot" />
      <main id="main-content" className="dashboard-shell report-shell">
        <header className="page-header report-header">
          <div>
            <p className="eyebrow">Pilot evidence</p>
            <h1>Recovery report</h1>
            <p className="page-description">
              A tenant-scoped view of missed-call response, replies, workflow outcomes, and safety signals.
            </p>
          </div>
          <a className="secondary-button" href={`/api/v1/reports/pilot.csv${suffix}`} download>
            <UiIcon name="download" size={16} />
            Export CSV
          </a>
        </header>

        <form className="report-range" method="get">
          <label>From<input type="date" name="from" defaultValue={query.from ?? dateOnly(report.fromUtc)} /></label>
          <label>To<input type="date" name="to" defaultValue={query.to ?? dateOnly(new Date(new Date(report.toUtcExclusive).getTime() - 86400000).toISOString())} /></label>
          <button className="primary-button" type="submit">
            <UiIcon name="refresh" size={16} />
            Update report
          </button>
        </form>

        <section className="report-metrics" aria-label="Pilot metrics">
          {metrics.map(([label, value, note]) => (
            <article className="report-metric" key={label}>
              <span>{label}</span><strong>{value}</strong><small>{note}</small>
            </article>
          ))}
        </section>

        <section className="report-evidence-grid">
          <article className="report-panel">
            <p className="eyebrow">Responsiveness</p>
            <h2>{report.medianFirstResponseMinutes === null ? "No response sample" : `${report.medianFirstResponseMinutes} min`}</h2>
            <p>Median time from captured missed call to the first sent recovery message.</p>
          </article>
          <article className="report-panel">
            <p className="eyebrow">Interpretation</p>
            <h2>Operational, not financial</h2>
            <p>{report.methodology}</p>
          </article>
        </section>
      </main>
    </>
  );
}
