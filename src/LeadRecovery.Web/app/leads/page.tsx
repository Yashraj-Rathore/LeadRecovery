import { headers } from "next/headers";
import { redirect } from "next/navigation";

import { AuthSession, getApiBaseUrl, LeadPage } from "../../lib/api";
import { LogoutButton } from "./logout-button";

async function authenticatedFetch(path: string): Promise<Response> {
  const requestHeaders = await headers();
  return fetch(`${getApiBaseUrl()}${path}`, {
    cache: "no-store",
    headers: { cookie: requestHeaders.get("cookie") ?? "" },
  });
}

function formatAge(createdAtUtc: string): string {
  const minutes = Math.max(
    0,
    Math.floor((Date.now() - new Date(createdAtUtc).getTime()) / 60_000),
  );
  if (minutes < 60) {
    return `${minutes}m ago`;
  }

  const hours = Math.floor(minutes / 60);
  return hours < 24 ? `${hours}h ago` : `${Math.floor(hours / 24)}d ago`;
}

export default async function LeadsPage() {
  const [sessionResponse, leadsResponse] = await Promise.all([
    authenticatedFetch("/api/v1/auth/me"),
    authenticatedFetch("/api/v1/leads/?pageSize=25"),
  ]);
  if (sessionResponse.status === 401 || leadsResponse.status === 401) {
    redirect("/login");
  }

  if (!sessionResponse.ok || !leadsResponse.ok) {
    return (
      <main className="dashboard-shell">
        <section className="error-state" role="alert">
          <p className="eyebrow">LeadRecovery</p>
          <h1>The inbox could not be loaded.</h1>
          <p>Refresh the page or try again in a moment.</p>
        </section>
      </main>
    );
  }

  const session = (await sessionResponse.json()) as AuthSession;
  const leads = (await leadsResponse.json()) as LeadPage;

  return (
    <main className="dashboard-shell">
      <header className="dashboard-header">
        <div>
          <p className="eyebrow">{session.tenantName}</p>
          <h1>Lead inbox</h1>
          <p className="muted">See who needs attention and what should happen next.</p>
        </div>
        <div className="session-block">
          <span>
            {session.displayName} - {session.role}
          </span>
          <LogoutButton />
        </div>
      </header>

      <section className="summary-strip" aria-label="Inbox summary">
        <div>
          <span>Open leads</span>
          <strong>{leads.items.length}</strong>
        </div>
        <div>
          <span>Needs attention</span>
          <strong>{leads.items.filter((lead) => lead.status === "NeedsHuman").length}</strong>
        </div>
        <div>
          <span>Automation</span>
          <strong>Protected</strong>
        </div>
      </section>

      <section className="inbox-panel" aria-labelledby="active-leads-heading">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">Current queue</p>
            <h2 id="active-leads-heading">Active leads</h2>
          </div>
          <span className="tenant-badge">Tenant scoped</span>
        </div>

        {leads.items.length === 0 ? (
          <div className="empty-state">
            <h3>No leads yet</h3>
            <p>New missed-call and messaging leads will appear here.</p>
          </div>
        ) : (
          <div className="lead-list">
            {leads.items.map((lead) => (
              <article className="lead-row" key={lead.id}>
                <div className="lead-identity">
                  <span className="lead-avatar" aria-hidden="true">
                    {(lead.displayName ?? lead.primaryPhoneE164).slice(0, 1).toUpperCase()}
                  </span>
                  <div>
                    <h3>{lead.displayName ?? "Unknown caller"}</h3>
                    <p>{lead.primaryPhoneE164}</p>
                  </div>
                </div>
                <div className="lead-source">
                  <span>Source</span>
                  <strong>{lead.source}</strong>
                </div>
                <div>
                  <span className={`status-pill status-${lead.status.toLowerCase()}`}>
                    {lead.status}
                  </span>
                </div>
                <div className="lead-age">
                  <span>Created</span>
                  <strong>{formatAge(lead.createdAtUtc)}</strong>
                </div>
              </article>
            ))}
          </div>
        )}
      </section>
    </main>
  );
}
