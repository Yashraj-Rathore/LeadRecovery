"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";

import { AssignableUser, AuthSession, LeadPage, LeadSummary } from "../../lib/api";
import {
  formatLabel,
  formatRelativeTime,
  formatTimestamp,
  getInitials,
} from "../../lib/presentation";
import { WorkspaceHeader } from "./workspace-header";

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

function cssToken(value: string): string {
  return value.toLowerCase().replace(/[^a-z0-9]/g, "");
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
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);

  const query = useMemo(() => {
    const params = new URLSearchParams({ pageSize: "50", assignment });
    if (status) params.set("status", status);
    if (urgency) params.set("urgency", urgency);
    if (assignedUserId) params.set("assignedUserId", assignedUserId);
    return params.toString();
  }, [assignedUserId, assignment, status, urgency]);

  const filtersActive = Boolean(
    status || urgency || assignment !== "all" || assignedUserId,
  );

  const clearFilters = useCallback(() => {
    setStatus("");
    setUrgency("");
    setAssignment("all");
    setAssignedUserId("");
  }, []);

  const load = useCallback(
    async (signal?: AbortSignal, showLoading = true) => {
      if (showLoading) setLoading(true);
      else setIsRefreshing(true);
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
        setLastUpdated(new Date());
      } catch (loadError) {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setError("The inbox could not be loaded. Check your connection and try again.");
      } finally {
        if (showLoading) setLoading(false);
        else setIsRefreshing(false);
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

  const needsAttention = leads.filter(
    (lead) => lead.status === "NeedsHuman" || lead.urgency === "CriticalReview",
  ).length;
  const unread = leads.filter((lead) => lead.hasUnreadCustomerActivity).length;
  const unassigned = leads.filter((lead) => !lead.assignedUserId).length;

  return (
    <>
      <WorkspaceHeader session={session} />
      <main id="main-content" className="dashboard-shell">
        <header className="page-header">
          <div>
            <p className="eyebrow">Operations</p>
            <h1>Lead inbox</h1>
            <p className="page-description">
              Prioritize new replies, take over urgent work, and keep every lead moving.
            </p>
          </div>
          <div className="queue-status" aria-label="Inbox automatically refreshes every ten seconds">
            <span className="signal-dot" aria-hidden="true" />
            Auto-refresh on
          </div>
        </header>

        <section className="summary-grid" aria-label="Inbox summary">
          <article className="summary-card">
            <span className="summary-icon" aria-hidden="true">↗</span>
            <div>
              <span>In this view</span>
              <strong>{loading ? "—" : leads.length}</strong>
            </div>
            <small>Filtered lead count</small>
          </article>
          <article className={`summary-card ${needsAttention > 0 ? "summary-attention" : ""}`}>
            <span className="summary-icon" aria-hidden="true">!</span>
            <div>
              <span>Needs attention</span>
              <strong>{loading ? "—" : needsAttention}</strong>
            </div>
            <small>Human review or critical</small>
          </article>
          <article className="summary-card">
            <span className="summary-icon" aria-hidden="true">●</span>
            <div>
              <span>Unread replies</span>
              <strong>{loading ? "—" : unread}</strong>
            </div>
            <small>New customer activity</small>
          </article>
          <article className="summary-card">
            <span className="summary-icon" aria-hidden="true">◇</span>
            <div>
              <span>Unassigned</span>
              <strong>{loading ? "—" : unassigned}</strong>
            </div>
            <small>Ready for an owner</small>
          </article>
        </section>

        <section className="filter-panel" aria-labelledby="inbox-filters-heading">
          <div className="filter-heading">
            <div>
              <p className="eyebrow">Focus the queue</p>
              <h2 id="inbox-filters-heading">Filters</h2>
            </div>
            {filtersActive ? <span className="active-filter-badge">Filtered</span> : null}
          </div>
          <div className="filter-controls">
            <label>
              Status
              <select value={status} onChange={(event) => setStatus(event.target.value)}>
                <option value="">All statuses</option>
                {statuses.map((item) => (
                  <option value={item} key={item}>{formatLabel(item)}</option>
                ))}
              </select>
            </label>
            <label>
              Urgency
              <select value={urgency} onChange={(event) => setUrgency(event.target.value)}>
                <option value="">All urgency levels</option>
                {urgencies.map((item) => (
                  <option value={item} key={item}>{formatLabel(item)}</option>
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
                <option value="">Any team member</option>
                {assignees.map((user) => (
                  <option value={user.userId} key={user.userId}>{user.displayName}</option>
                ))}
              </select>
            </label>
          </div>
          <div className="filter-actions">
            <button
              className="text-button"
              type="button"
              onClick={clearFilters}
              disabled={!filtersActive}
            >
              Clear filters
            </button>
            <button
              className="quiet-button refresh-button"
              type="button"
              onClick={() => setRefreshKey((key) => key + 1)}
              disabled={loading || isRefreshing}
            >
              <span aria-hidden="true">↻</span>
              {isRefreshing ? "Refreshing…" : "Refresh"}
            </button>
          </div>
        </section>

        <section className="inbox-panel" aria-labelledby="active-leads-heading" aria-busy={loading}>
          <div className="panel-heading">
            <div>
              <p className="eyebrow">Current queue</p>
              <h2 id="active-leads-heading">Active leads</h2>
            </div>
            <div className="panel-meta" aria-live="polite">
              {lastUpdated ? (
                <span>
                  Updated <time dateTime={lastUpdated.toISOString()}>{formatRelativeTime(lastUpdated.toISOString())}</time>
                </span>
              ) : null}
              <span className="tenant-badge">Tenant protected</span>
            </div>
          </div>

          {loading ? (
            <div className="skeleton-list" role="status" aria-live="polite">
              <span className="sr-only">Loading the latest leads…</span>
              {[0, 1, 2, 3].map((item) => (
                <div className="skeleton-row" aria-hidden="true" key={item}>
                  <span className="skeleton-avatar" />
                  <span className="skeleton-copy" />
                  <span className="skeleton-pill" />
                  <span className="skeleton-copy skeleton-copy-short" />
                  <span className="skeleton-button" />
                </div>
              ))}
            </div>
          ) : error ? (
            <div className="empty-state empty-state-error" role="alert">
              <span className="empty-state-icon" aria-hidden="true">!</span>
              <h3>We couldn’t load the inbox</h3>
              <p>{error}</p>
              <button className="primary-button" type="button" onClick={() => setRefreshKey((key) => key + 1)}>
                Try again
              </button>
            </div>
          ) : leads.length === 0 ? (
            <div className="empty-state">
              <span className="empty-state-icon" aria-hidden="true">✓</span>
              <h3>{filtersActive ? "No leads match these filters" : "The inbox is clear"}</h3>
              <p>
                {filtersActive
                  ? "Try broadening the filters to bring more leads back into view."
                  : "New missed-call activity will appear here automatically."}
              </p>
              {filtersActive ? (
                <button className="quiet-button" type="button" onClick={clearFilters}>Clear filters</button>
              ) : null}
            </div>
          ) : (
            <div className="lead-list">
              <div className="lead-list-header" aria-hidden="true">
                <span>Customer</span>
                <span>Status</span>
                <span>Ownership</span>
                <span>Last activity</span>
                <span />
              </div>
              {leads.map((lead) => {
                const needsReview = lead.status === "NeedsHuman" || lead.urgency === "CriticalReview";
                const customerLabel = lead.displayName ?? "Unknown caller";
                return (
                  <article
                    className={`lead-row ${needsReview ? "lead-row-attention" : ""} ${
                      lead.hasUnreadCustomerActivity ? "lead-row-unread" : ""
                    }`}
                    key={lead.id}
                  >
                    <div className="lead-identity">
                      <span className="lead-avatar" aria-hidden="true">{getInitials(customerLabel)}</span>
                      <div>
                        <div className="lead-name-line">
                          <h3>{customerLabel}</h3>
                          {lead.hasUnreadCustomerActivity ? (
                            <span className="unread-dot"><span className="sr-only">New customer reply</span></span>
                          ) : null}
                        </div>
                        <p>{lead.primaryPhoneE164}</p>
                        <span className="source-label">{formatLabel(lead.source)}</span>
                      </div>
                    </div>
                    <div className="lead-status-cell">
                      <span className={`status-pill status-${cssToken(lead.status)}`}>
                        {formatLabel(lead.status)}
                      </span>
                      <span className={`urgency-badge urgency-${cssToken(lead.urgency)}`}>
                        {formatLabel(lead.urgency)}
                      </span>
                    </div>
                    <div className="lead-source">
                      <span>Assigned to</span>
                      <strong>{lead.assignedUserName ?? "Unassigned"}</strong>
                      <small className={`automation-state automation-${cssToken(lead.automationState)}`}>
                        {formatLabel(lead.automationState)}
                      </small>
                    </div>
                    <div className="lead-age">
                      <span>Customer activity</span>
                      <strong title={formatTimestamp(lead.lastActivityAtUtc)}>
                        {formatRelativeTime(lead.lastActivityAtUtc)}
                      </strong>
                      {lead.hasUnreadCustomerActivity ? <small>Unread reply</small> : <small>Up to date</small>}
                    </div>
                    <Link className="open-lead-link" href={`/leads/${lead.id}`}>
                      Open<span className="sr-only"> lead for {customerLabel}</span>
                      <span aria-hidden="true">→</span>
                    </Link>
                  </article>
                );
              })}
            </div>
          )}
        </section>
      </main>
    </>
  );
}
