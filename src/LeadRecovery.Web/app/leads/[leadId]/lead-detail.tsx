"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { FormEvent, useCallback, useEffect, useRef, useState } from "react";

import { ApiProblem, AuthSession, LeadDetail, securePost } from "../../../lib/api";
import {
  formatLabel,
  formatRelativeTime,
  formatTimestamp,
  getInitials,
} from "../../../lib/presentation";
import { WorkspaceHeader } from "../workspace-header";

const closeReasons = [
  "LostNoResponse",
  "LostOutOfArea",
  "LostUnavailableService",
  "Duplicate",
  "Spam",
  "OptedOut",
];

function isLeadDetail(value: LeadDetail | ApiProblem | null): value is LeadDetail {
  return Boolean(value && "lead" in value);
}

function problemMessage(problem: ApiProblem | null): string {
  const fieldError = problem?.errors ? Object.values(problem.errors).flat().at(0) : null;
  return fieldError ?? problem?.detail ?? problem?.title ?? "The action could not be completed.";
}

function cssToken(value: string): string {
  return value.toLowerCase().replace(/[^a-z0-9]/g, "");
}

export function LeadDetailView({
  leadId,
  session,
}: {
  leadId: string;
  session: AuthSession;
}) {
  const router = useRouter();
  const [detail, setDetail] = useState<LeadDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [newActivity, setNewActivity] = useState(false);
  const [pendingAction, setPendingAction] = useState<string | null>(null);
  const [transition, setTransition] = useState("");
  const [reason, setReason] = useState("");
  const [closeReason, setCloseReason] = useState("LostNoResponse");
  const [minimumDetailsPresent, setMinimumDetailsPresent] = useState(true);
  const [manualBody, setManualBody] = useState("");
  const [noteBody, setNoteBody] = useState("");
  const timelineCount = useRef(0);
  const timelineHeading = useRef<HTMLHeadingElement>(null);
  const composerFocused = useRef(false);
  const canManage = session.role !== "ReadOnly";

  const load = useCallback(
    async (showLoading = true) => {
      if (showLoading) setLoading(true);
      else setIsRefreshing(true);

      try {
        const response = await fetch(`/api/v1/leads/${leadId}`, {
          cache: "no-store",
          credentials: "same-origin",
        });
        if (response.status === 401) {
          router.replace("/login");
          return;
        }

        if (response.status === 404) {
          setError("This lead does not exist in your tenant.");
          return;
        }

        if (!response.ok) throw new Error();
        const latest = (await response.json()) as LeadDetail;
        if (
          !showLoading &&
          composerFocused.current &&
          timelineCount.current > 0 &&
          latest.timeline.length > timelineCount.current
        ) {
          setNewActivity(true);
        }

        timelineCount.current = latest.timeline.length;
        setDetail(latest);
        setError(null);
      } catch {
        setError("The latest lead details could not be loaded.");
      } finally {
        if (showLoading) setLoading(false);
        else setIsRefreshing(false);
      }
    },
    [leadId, router],
  );

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    const timer = window.setInterval(() => void load(false), 8_000);
    return () => window.clearInterval(timer);
  }, [load]);

  async function mutate(actionName: string, path: string, body: unknown) {
    setPendingAction(actionName);
    setError(null);
    setNotice(null);
    try {
      const { response, payload } = await securePost(path, body);
      if (response.status === 401) {
        router.replace("/login");
        return false;
      }

      if (!response.ok) {
        const problem = isLeadDetail(payload) ? null : payload;
        if (problem?.current) {
          setDetail(problem.current);
          timelineCount.current = problem.current.timeline.length;
        }

        setError(
          response.status === 409 && problem?.title?.includes("changed")
            ? "This lead changed while you were viewing it. Review the latest status before trying again."
            : problemMessage(problem),
        );
        return false;
      }

      if (isLeadDetail(payload)) {
        setDetail(payload);
        timelineCount.current = payload.timeline.length;
      }

      setNotice(`${actionName} completed.`);
      return true;
    } catch {
      setError("The action could not be completed. Check your connection and try again.");
      return false;
    } finally {
      setPendingAction(null);
    }
  }

  async function copyPhone(phoneNumber: string) {
    try {
      await navigator.clipboard.writeText(phoneNumber);
      setError(null);
      setNotice("Phone number copied.");
    } catch {
      setNotice(null);
      setError("The phone number could not be copied. Select it manually instead.");
    }
  }

  function reviewNewActivity() {
    setNewActivity(false);
    timelineHeading.current?.scrollIntoView({ behavior: "smooth", block: "start" });
    timelineHeading.current?.focus({ preventScroll: true });
  }

  async function submitTransition(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!detail || !transition) return;
    const succeeded = await mutate("Status update", `/api/v1/leads/${leadId}/transitions`, {
      targetStatus: transition,
      reason: reason || null,
      closeReason: transition === "Closed" ? closeReason : null,
      minimumRequiredDetailsPresent: minimumDetailsPresent,
      expectedRowVersion: detail.lead.rowVersion,
    });
    if (succeeded) {
      setTransition("");
      setReason("");
    }
  }

  async function submitManualMessage(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!manualBody.trim()) return;
    const succeeded = await mutate("Manual SMS", `/api/v1/leads/${leadId}/messages`, {
      body: manualBody,
      idempotencyKey: `ui-${crypto.randomUUID()}`,
    });
    if (succeeded) setManualBody("");
  }

  async function submitNote(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!noteBody.trim()) return;
    const succeeded = await mutate("Internal note", `/api/v1/leads/${leadId}/notes`, {
      body: noteBody,
    });
    if (succeeded) setNoteBody("");
  }

  if (loading) {
    return (
      <>
        <WorkspaceHeader session={session} />
        <main id="main-content" className="dashboard-shell">
          <section className="loading-page" role="status" aria-live="polite">
            <div className="loading-brand-mark" aria-hidden="true"><span /></div>
            <p className="eyebrow">Opening lead</p>
            <h1>Loading the latest details…</h1>
            <span className="loading-bar" aria-hidden="true" />
          </section>
        </main>
      </>
    );
  }

  if (!detail) {
    return (
      <>
        <WorkspaceHeader session={session} />
        <main id="main-content" className="dashboard-shell">
          <section className="error-state" role="alert">
            <span className="empty-state-icon" aria-hidden="true">!</span>
            <h1>Lead unavailable</h1>
            <p>{error ?? "This lead could not be found."}</p>
            <Link className="primary-button button-link" href="/leads">Return to inbox</Link>
          </section>
        </main>
      </>
    );
  }

  const lead = detail.lead;
  const customerLabel = lead.displayName ?? "Unknown caller";

  return (
    <>
      <WorkspaceHeader session={session} />
      <main id="main-content" className="dashboard-shell detail-shell">
        <nav className="breadcrumb" aria-label="Breadcrumb">
          <Link href="/leads"><span aria-hidden="true">←</span> Lead inbox</Link>
          <span aria-hidden="true">/</span>
          <span aria-current="page">{customerLabel}</span>
        </nav>

        <header className="lead-detail-header">
          <div className="lead-detail-identity">
            <span className="lead-avatar lead-avatar-large" aria-hidden="true">
              {getInitials(customerLabel)}
            </span>
            <div>
              <p className="eyebrow">Lead workspace</p>
              <h1>{customerLabel}</h1>
              <div className="phone-line">
                <span>{lead.primaryPhoneE164}</span>
                <button type="button" className="text-button" onClick={() => void copyPhone(lead.primaryPhoneE164)}>
                  Copy phone
                </button>
              </div>
            </div>
          </div>
          <div className="lead-header-badges">
            <span className={`status-pill status-${cssToken(lead.status)}`}>{formatLabel(lead.status)}</span>
            <span className={`urgency-badge urgency-${cssToken(lead.urgency)}`}>{formatLabel(lead.urgency)}</span>
            <span className={`automation-badge automation-${cssToken(lead.automationState)}`}>
              <span className="automation-indicator" aria-hidden="true" />
              Automation: {formatLabel(lead.automationState)}
            </span>
          </div>
        </header>

        <dl className="lead-facts" aria-label="Lead summary">
          <div><dt>Source</dt><dd>{formatLabel(lead.source)}</dd></div>
          <div><dt>Assigned to</dt><dd>{lead.assignedUserName ?? "Unassigned"}</dd></div>
          <div>
            <dt>Last customer activity</dt>
            <dd title={formatTimestamp(lead.lastActivityAtUtc)}>{formatRelativeTime(lead.lastActivityAtUtc)}</dd>
          </div>
          <div><dt>Lead created</dt><dd>{formatTimestamp(lead.createdAtUtc)}</dd></div>
        </dl>

        <div className="live-region" aria-live="polite" aria-atomic="true">
          {error ? <p className="action-error" role="alert"><strong>Action needed.</strong> {error}</p> : null}
          {notice ? <p className="action-notice"><strong>Done.</strong> {notice}</p> : null}
          {newActivity ? (
            <button className="new-activity" type="button" onClick={reviewNewActivity}>
              <span className="signal-dot" aria-hidden="true" />
              New activity arrived. Review the updated timeline.
              <span aria-hidden="true">↓</span>
            </button>
          ) : null}
        </div>

        <div className="detail-grid">
          <section className="timeline-panel" aria-labelledby="timeline-heading">
            <div className="panel-heading">
              <div>
                <p className="eyebrow">Full history</p>
                <h2 id="timeline-heading" ref={timelineHeading} tabIndex={-1}>Conversation timeline</h2>
                <p className="panel-description">Calls, messages, notes, and workflow events in order.</p>
              </div>
              <button
                className="quiet-button refresh-button"
                type="button"
                onClick={() => void load(false)}
                disabled={isRefreshing}
              >
                <span aria-hidden="true">↻</span>
                {isRefreshing ? "Refreshing…" : "Refresh"}
              </button>
            </div>
            {detail.timeline.length === 0 ? (
              <div className="empty-state compact-empty-state">
                <span className="empty-state-icon" aria-hidden="true">◇</span>
                <h3>No activity yet</h3>
                <p>Calls, messages, system events, and notes will appear here.</p>
              </div>
            ) : (
              <ol className="timeline-list">
                {detail.timeline.map((item) => (
                  <li
                    className={`timeline-item timeline-${item.type.toLowerCase()} timeline-${cssToken(item.direction ?? "none")}`}
                    key={item.id}
                  >
                    <div className="timeline-marker" aria-hidden="true" />
                    <article className="timeline-content">
                      <div className="timeline-heading-row">
                        <div>
                          <strong>{item.label}</strong>
                          {item.kind === "Manual" ? <span className="manual-label">Staff sent</span> : null}
                        </div>
                        <time dateTime={item.occurredAtUtc}>{formatTimestamp(item.occurredAtUtc)}</time>
                      </div>
                      {item.body ? <p className="message-body">{item.body}</p> : null}
                      <div className="timeline-meta">
                        {item.direction ? <span>{formatLabel(item.direction)}</span> : null}
                        {item.status ? <span>{formatLabel(item.status)}</span> : null}
                        {item.actorName ? <span>by {item.actorName}</span> : null}
                      </div>
                      {item.failureDescription ? (
                        <p className="delivery-failure" role="status">
                          <strong>Delivery failed:</strong> {item.failureDescription}
                        </p>
                      ) : null}
                    </article>
                  </li>
                ))}
              </ol>
            )}

            {canManage ? (
              <form className="composer" onSubmit={submitManualMessage}>
                <div className="field-heading">
                  <label htmlFor="manual-message">Send manual SMS</label>
                  <span>Sent by your staff account</span>
                </div>
                <textarea
                  id="manual-message"
                  value={manualBody}
                  onChange={(event) => setManualBody(event.target.value)}
                  onFocus={() => { composerFocused.current = true; }}
                  onBlur={() => { composerFocused.current = false; }}
                  maxLength={1600}
                  rows={4}
                  placeholder="Write a clear, helpful reply…"
                  aria-describedby="manual-message-help manual-message-count"
                  required
                />
                <div className="composer-footer">
                  <span id="manual-message-help">Opt-out and eligibility checks run before sending.</span>
                  <span id="manual-message-count" className={manualBody.length > 1450 ? "character-warning" : ""}>
                    {manualBody.length} / 1600
                  </span>
                  <button
                    className="primary-button"
                    type="submit"
                    disabled={pendingAction !== null || !manualBody.trim()}
                  >
                    {pendingAction === "Manual SMS" ? "Queueing…" : "Send SMS"}
                  </button>
                </div>
              </form>
            ) : (
              <div className="read-only-note">Your role can view this conversation but cannot send messages.</div>
            )}
          </section>

          <aside className="lead-actions" aria-label="Lead details and actions" aria-busy={pendingAction !== null}>
            <section className="action-card">
              <div className="action-card-heading">
                <div><p className="eyebrow">Ownership</p><h2>Assignment</h2></div>
                {!canManage ? <span className="tenant-badge">View only</span> : null}
              </div>
              <label htmlFor="assignee">Assigned user</label>
              <select
                id="assignee"
                value={lead.assignedUserId ?? ""}
                disabled={!canManage || pendingAction !== null}
                onChange={(event) => void mutate("Assignment", `/api/v1/leads/${leadId}/assignment`, {
                  assignedUserId: event.target.value || null,
                  expectedRowVersion: lead.rowVersion,
                })}
              >
                <option value="">Unassigned</option>
                {detail.assignableUsers.map((user) => (
                  <option value={user.userId} key={user.userId}>
                    {user.displayName} ({formatLabel(user.role)})
                  </option>
                ))}
              </select>
              {canManage && lead.assignedUserId !== session.userId ? (
                <button
                  className="quiet-button full-width"
                  type="button"
                  disabled={pendingAction !== null}
                  onClick={() => void mutate("Assignment", `/api/v1/leads/${leadId}/assignment`, {
                    assignedUserId: session.userId,
                    expectedRowVersion: lead.rowVersion,
                  })}
                >
                  Assign to me
                </button>
              ) : null}
            </section>

            <section className="action-card">
              <div className="action-card-heading">
                <div><p className="eyebrow">Workflow</p><h2>Status</h2></div>
                <span className={`status-dot status-dot-${cssToken(lead.status)}`} aria-hidden="true" />
              </div>
              {canManage && detail.allowedTransitions.length > 0 ? (
                <form className="stacked-form" onSubmit={submitTransition}>
                  <label htmlFor="transition">Next status</label>
                  <select id="transition" value={transition} onChange={(event) => setTransition(event.target.value)} required>
                    <option value="">Choose an allowed transition</option>
                    {detail.allowedTransitions.map((item) => (
                      <option value={item} key={item}>{formatLabel(item)}</option>
                    ))}
                  </select>
                  {transition === "Closed" ? (
                    <>
                      <label htmlFor="close-reason">Close reason</label>
                      <select id="close-reason" value={closeReason} onChange={(event) => setCloseReason(event.target.value)}>
                        {closeReasons.map((item) => <option value={item} key={item}>{formatLabel(item)}</option>)}
                      </select>
                    </>
                  ) : null}
                  {transition === "Qualified" ? (
                    <label className="checkbox-label">
                      <input
                        type="checkbox"
                        checked={minimumDetailsPresent}
                        onChange={(event) => setMinimumDetailsPresent(event.target.checked)}
                      />
                      Required qualification details are present
                    </label>
                  ) : null}
                  <label htmlFor="transition-reason">Reason or context <span>Optional</span></label>
                  <textarea
                    id="transition-reason"
                    value={reason}
                    onChange={(event) => setReason(event.target.value)}
                    maxLength={500}
                    rows={3}
                    placeholder="Add context for the team…"
                  />
                  <button className="primary-button" type="submit" disabled={pendingAction !== null || !transition}>
                    {pendingAction === "Status update" ? "Updating…" : "Update status"}
                  </button>
                </form>
              ) : <p className="muted">No status transitions are currently available.</p>}
            </section>

            <section className="action-card automation-card">
              <div className="action-card-heading">
                <div><p className="eyebrow">Controls</p><h2>Automation</h2></div>
                <span className={`automation-indicator automation-${cssToken(lead.automationState)}`} aria-hidden="true" />
              </div>
              <p className="card-supporting-copy">
                {lead.automationState === "Active"
                  ? "Approved workflow actions can continue while this lead is eligible."
                  : `Automation is ${formatLabel(lead.automationState).toLowerCase()} for this lead.`}
              </p>
              {canManage && lead.automationState === "Active" ? (
                <button
                  className="warning-button full-width"
                  type="button"
                  disabled={pendingAction !== null}
                  onClick={() => void mutate("Automation pause", `/api/v1/leads/${leadId}/automation/pause`, {
                    expectedRowVersion: lead.rowVersion,
                  })}
                >
                  {pendingAction === "Automation pause" ? "Pausing…" : "Pause automation"}
                </button>
              ) : null}
              {canManage && lead.automationState === "PausedByUser" ? (
                <button
                  className="primary-button full-width"
                  type="button"
                  disabled={pendingAction !== null}
                  onClick={() => void mutate("Automation resume", `/api/v1/leads/${leadId}/automation/resume`, {
                    expectedRowVersion: lead.rowVersion,
                  })}
                >
                  {pendingAction === "Automation resume" ? "Resuming…" : "Resume automation"}
                </button>
              ) : null}
            </section>

            <section className="action-card">
              <div className="action-card-heading">
                <div><p className="eyebrow">Scheduled work</p><h2>Pending actions</h2></div>
                <span className="count-badge">{detail.pendingActions.length}</span>
              </div>
              {detail.pendingActions.length === 0 ? <p className="muted">No pending actions.</p> : (
                <ul className="pending-actions">
                  {detail.pendingActions.map((action) => (
                    <li key={action.id}>
                      <div>
                        <strong>{formatLabel(action.actionType)}</strong>
                        <span>{formatLabel(action.status)} · {formatTimestamp(action.scheduledForUtc)}</span>
                      </div>
                      {canManage && action.isCancellable ? (
                        <button
                          className="text-button"
                          type="button"
                          disabled={pendingAction !== null}
                          onClick={() => void mutate(
                            "Scheduled action cancellation",
                            `/api/v1/leads/${leadId}/scheduled-actions/${action.id}/cancel`,
                            {},
                          )}
                        >
                          Cancel
                        </button>
                      ) : null}
                    </li>
                  ))}
                </ul>
              )}
            </section>

            <section className="action-card">
              <div className="action-card-heading">
                <div><p className="eyebrow">Deterministic workflow</p><h2>Qualification</h2></div>
                <span className="count-badge">{detail.qualificationAnswers.length}</span>
              </div>
              {detail.qualificationAnswers.length === 0 ? (
                <p className="muted">No structured answers collected yet.</p>
              ) : (
                <dl className="qualification-list">
                  {detail.qualificationAnswers.map((answer) => (
                    <div key={answer.id}>
                      <dt>{answer.questionPrompt}</dt>
                      <dd>{answer.value ?? formatLabel(answer.outcome)}</dd>
                    </div>
                  ))}
                </dl>
              )}
              {detail.currentQualificationQuestion ? (
                <div className="next-question">
                  <span>Next question</span>
                  <strong>{detail.currentQualificationQuestion}</strong>
                </div>
              ) : null}
            </section>

            {detail.bookingUrl ? (
              <section className="action-card booking-card">
                <p className="eyebrow">Approved destination</p>
                <h2>Booking</h2>
                <p className="card-supporting-copy">Use only the tenant-approved booking destination.</p>
                <a className="secondary-button button-link full-width" href={detail.bookingUrl} target="_blank" rel="noreferrer">
                  Open approved booking page <span aria-hidden="true">↗</span>
                </a>
                {canManage && lead.status === "Qualified" ? (
                  <button
                    className="primary-button full-width"
                    type="button"
                    disabled={pendingAction !== null}
                    onClick={() => void mutate("Booking link", `/api/v1/leads/${leadId}/booking-link`, {
                      expectedRowVersion: lead.rowVersion,
                    })}
                  >
                    {pendingAction === "Booking link" ? "Queueing…" : "Queue booking link"}
                  </button>
                ) : null}
              </section>
            ) : null}

            {canManage ? (
              <section className="action-card">
                <p className="eyebrow">Team context</p>
                <h2>Add internal note</h2>
                <form className="stacked-form" onSubmit={submitNote}>
                  <label htmlFor="lead-note">Note</label>
                  <textarea
                    id="lead-note"
                    value={noteBody}
                    onChange={(event) => setNoteBody(event.target.value)}
                    maxLength={2000}
                    rows={3}
                    placeholder="Add context only your team can see…"
                    required
                  />
                  <button className="quiet-button" type="submit" disabled={pendingAction !== null || !noteBody.trim()}>
                    {pendingAction === "Internal note" ? "Adding…" : "Add note"}
                  </button>
                </form>
              </section>
            ) : null}
          </aside>
        </div>
      </main>
    </>
  );
}
