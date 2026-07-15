"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";

import {
  AssignableUser,
  AuthSession,
  LeadPage,
  LeadSummary,
} from "../../lib/api";
import { LogoutButton } from "./logout-button";

const statuses = [
  "New",
  "Contacting",
  "AwaitingCustomer",
  "Qualified",
  "BookingOffered",
  "NeedsHuman",
  "Booked",
  "Closed",
  "ClosedWon",
];
const urgencies = ["Unknown", "Low", "Normal", "High", "CriticalReview"];

function formatAge(timestamp: string): string {
  const minutes = Math.max(
    0,
    Math.floor((Date.now() - new Date(timestamp).getTime()) / 60_000),
  );
  if (minutes < 60) {
    return `${minutes}m ago`;
  }

  const hours = Math.floor(minutes / 60);
  return hours < 24 ? `${hours}h ago` : `${Math.floor(hours / 24)}d ago`;
}

export function LeadInbox({ session }: { session: AuthSession }) {
  const router = useRouter();
  const [leads, setLeads] = useState<LeadSummary[]>([]);
  const [assignees, setAssignees] = useState<AssignableUser[]>([]);
  const [status, setStatus] = useState("");
  const [urgency, setUrgency] = useState("");
  const [assignment, setAssignment] = useState("all");
  const [assignedUserId, setAssignedUserId] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);

  const query = useMemo(() => {
    const params = new URLSearchParams({ pageSize: "50", assignment });
    if (status) params.set("status", status);
    if (urgency) params.set("urgency", urgency);
    if (assignedUserId) params.set("assignedUserId", assignedUserId);
    return params.toString();
  }, [assignedUserId, assignment, status, urgency]);

  const load = useCallback(
    async (signal?: AbortSignal, showLoading = true) => {
      if (showLoading) setLoading(true);
      setError(null);
      try {
        const [leadsResponse, usersResponse] = await Promise.all([
          fetch(`/api/v1/leads/?${query}`, {
            cache: "no-store",
            credentials: "same-origin",
            signal,
          }),
          fetch("/api/v1/leads/assignees", {
            cache: "no-store",
            credentials: "same-origin",
            signal,
          }),
        ]);
        if (leadsResponse.status === 401 || usersResponse.status === 401) {
          router.replace("/login");
          return;
        }

        if (!leadsResponse.ok || !usersResponse.ok) {
          throw new Error("The inbox could not be loaded.");
        }

        const page = (await leadsResponse.json()) as LeadPage;
        setLeads(page.items);
        setAssignees((await usersResponse.json()) as AssignableUser[]);
      } catch (loadError) {
        if (loadError instanceof DOMException && loadError.name === "AbortError") {
          return;
        }

        setError("The inbox could not be loaded. Try again in a moment.");
      } finally {
        if (showLoading) setLoading(false);
      }
    },
    [query, router],
  );

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load, refreshKey]);

  useEffect(() => {
    const timer = window.setInterval(() => void load(undefined, false), 10_000);
    return () => window.clearInterval(timer);
  }, [load]);

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
            {session.displayName} · {session.role}
          </span>
          <LogoutButton />
        </div>
      </header>

      <section className="summary-strip" aria-label="Inbox summary">
        <div>
          <span>Visible leads</span>
          <strong>{leads.length}</strong>
        </div>
        <div>
          <span>Needs attention</span>
          <strong>{leads.filter((lead) => lead.status === "NeedsHuman").length}</strong>
        </div>
        <div>
          <span>Unread replies</span>
          <strong>{leads.filter((lead) => lead.hasUnreadCustomerActivity).length}</strong>
        </div>
      </section>

      <section className="filter-panel" aria-labelledby="inbox-filters-heading">
        <div>
          <p className="eyebrow">Focus the queue</p>
          <h2 id="inbox-filters-heading">Filters</h2>
        </div>
        <label>
          Status
          <select value={status} onChange={(event) => setStatus(event.target.value)}>
            <option value="">All statuses</option>
            {statuses.map((item) => (
              <option value={item} key={item}>
                {item}
              </option>
            ))}
          </select>
        </label>
        <label>
          Urgency
          <select value={urgency} onChange={(event) => setUrgency(event.target.value)}>
            <option value="">All urgency levels</option>
            {urgencies.map((item) => (
              <option value={item} key={item}>
                {item}
              </option>
            ))}
          </select>
        </label>
        <label>
          Assignment
          <select
            value={assignment}
            onChange={(event) => {
              setAssignment(event.target.value);
              setAssignedUserId("");
            }}
          >
            <option value="all">Anyone</option>
            <option value="unassigned">Unassigned</option>
            <option value="mine">Assigned to me</option>
          </select>
        </label>
        <label>
          Assigned user
          <select
            value={assignedUserId}
            onChange={(event) => {
              setAssignedUserId(event.target.value);
              setAssignment("all");
            }}
          >
            <option value="">Any user</option>
            {assignees.map((user) => (
              <option value={user.userId} key={user.userId}>
                {user.displayName}
              </option>
            ))}
          </select>
        </label>
        <button className="quiet-button" type="button" onClick={() => setRefreshKey((key) => key + 1)}>
          Refresh
        </button>
      </section>

      <section className="inbox-panel" aria-labelledby="active-leads-heading" aria-busy={loading}>
        <div className="panel-heading">
          <div>
            <p className="eyebrow">Current queue</p>
            <h2 id="active-leads-heading">Active leads</h2>
          </div>
          <span className="tenant-badge">Tenant scoped</span>
        </div>

        {loading ? (
          <div className="loading-state" role="status" aria-live="polite">
            <span className="loading-bar" aria-hidden="true" />
            Loading the latest leads…
          </div>
        ) : error ? (
          <div className="empty-state" role="alert">
            <h3>Inbox unavailable</h3>
            <p>{error}</p>
            <button className="primary-button" type="button" onClick={() => setRefreshKey((key) => key + 1)}>
              Try again
            </button>
          </div>
        ) : leads.length === 0 ? (
          <div className="empty-state">
            <h3>No leads match these filters</h3>
            <p>Clear a filter or wait for new missed-call activity.</p>
          </div>
        ) : (
          <div className="lead-list">
            {leads.map((lead) => (
              <article className="lead-row" key={lead.id}>
                <div className="lead-identity">
                  <span className="lead-avatar" aria-hidden="true">
                    {(lead.displayName ?? lead.primaryPhoneE164).slice(0, 1).toUpperCase()}
                  </span>
                  <div>
                    <h3>{lead.displayName ?? "Unknown caller"}</h3>
                    <p>{lead.primaryPhoneE164}</p>
                    {lead.hasUnreadCustomerActivity ? <span className="unread-label">New reply</span> : null}
                  </div>
                </div>
                <div className="lead-source">
                  <span>Assignment</span>
                  <strong>{lead.assignedUserName ?? "Unassigned"}</strong>
                </div>
                <div>
                  <span className={`status-pill status-${lead.status.toLowerCase()}`}>
                    {lead.status}
                  </span>
                  <span className="urgency-label">{lead.urgency}</span>
                </div>
                <div className="lead-age">
                  <span>Last activity</span>
                  <strong>{formatAge(lead.lastActivityAtUtc)}</strong>
                  <small>{lead.automationState}</small>
                </div>
                <Link className="open-lead-link" href={`/leads/${lead.id}`}>
                  Open lead<span className="sr-only"> {lead.displayName ?? lead.primaryPhoneE164}</span>
                </Link>
              </article>
            ))}
          </div>
        )}
      </section>
    </main>
  );
}
