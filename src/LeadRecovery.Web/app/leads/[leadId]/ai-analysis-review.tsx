"use client";

import { FormEvent, useEffect, useState } from "react";

import { AiAnalysisReview, AiAnalysisValues } from "../../../lib/api";
import { formatLabel, formatTimestamp } from "../../../lib/presentation";

const urgencyOptions = ["Unknown", "Low", "Normal", "High", "CriticalReview"];

type ReviewMode = "view" | "edit" | "reject";

type ReviewMutation = (
  actionName: string,
  path: string,
  body: unknown,
) => Promise<boolean>;

function confidencePresentation(confidence: number): {
  label: string;
  className: string;
} {
  if (confidence < 0.65) {
    return { label: "Low confidence", className: "confidence-low" };
  }

  if (confidence < 0.85) {
    return { label: "Review recommended", className: "confidence-review" };
  }

  return { label: "High confidence", className: "confidence-high" };
}

function normalizedFormValues(values: AiAnalysisValues): AiAnalysisValues {
  return {
    serviceCategory: values.serviceCategory,
    urgency: values.urgency,
    summary: values.summary,
    city: values.city ?? "",
    postalCode: values.postalCode ?? "",
    preferredCallbackWindow: values.preferredCallbackWindow ?? "",
    suggestedReply: values.suggestedReply ?? "",
  };
}

export function AiAnalysisReviewCard({
  leadId,
  analysis,
  canManage,
  pendingAction,
  onMutate,
}: {
  leadId: string;
  analysis: AiAnalysisReview;
  canManage: boolean;
  pendingAction: string | null;
  onMutate: ReviewMutation;
}) {
  const [mode, setMode] = useState<ReviewMode>("view");
  const [values, setValues] = useState<AiAnalysisValues>(
    normalizedFormValues(analysis.suggestion),
  );
  const [correctionReason, setCorrectionReason] = useState("");
  const confidence = confidencePresentation(analysis.confidence);
  const isPending = analysis.reviewStatus === "Pending";
  const displayValues = analysis.reviewedValues ?? analysis.suggestion;
  const reviewPath = `/api/v1/leads/${leadId}/ai-analyses/${analysis.id}`;
  const headingId = `ai-analysis-${analysis.id}`;

  useEffect(() => {
    setMode("view");
    setValues(normalizedFormValues(analysis.reviewedValues ?? analysis.suggestion));
    setCorrectionReason(analysis.correctionReason ?? "");
  }, [analysis.reviewStatus, analysis.rowVersion]);

  function updateValue<Key extends keyof AiAnalysisValues>(
    key: Key,
    value: AiAnalysisValues[Key],
  ) {
    setValues((current) => ({ ...current, [key]: value }));
  }

  async function accept() {
    await onMutate("AI suggestion acceptance", `${reviewPath}/accept`, {
      expectedRowVersion: analysis.rowVersion,
      correctionReason: null,
    });
  }

  async function submitEdit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const succeeded = await onMutate("AI suggestion edit", `${reviewPath}/edit`, {
      ...values,
      city: values.city?.trim() || null,
      postalCode: values.postalCode?.trim() || null,
      preferredCallbackWindow: values.preferredCallbackWindow?.trim() || null,
      suggestedReply: values.suggestedReply?.trim() || null,
      correctionReason: correctionReason.trim() || null,
      expectedRowVersion: analysis.rowVersion,
    });
    if (succeeded) setMode("view");
  }

  async function submitReject(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const succeeded = await onMutate(
      "AI suggestion rejection",
      `${reviewPath}/reject`,
      {
        expectedRowVersion: analysis.rowVersion,
        correctionReason: correctionReason.trim() || null,
      },
    );
    if (succeeded) setMode("view");
  }

  return (
    <section
      className={`ai-review-card ${confidence.className}`}
      aria-labelledby={headingId}
    >
      <header className="ai-review-heading">
        <div>
          <div className="ai-label-row">
            <span className="ai-generated-label">
              <span aria-hidden="true">✦</span> AI-generated suggestion
            </span>
            <span className={`review-status review-status-${analysis.reviewStatus.toLowerCase()}`}>
              {analysis.reviewStatus === "Pending"
                ? "Awaiting staff review"
                : `${formatLabel(analysis.reviewStatus)} by staff`}
            </span>
          </div>
          <h2 id={headingId}>Lead analysis</h2>
          <p>
            Created {formatTimestamp(analysis.createdAtUtc)} · Schema{" "}
            {analysis.schemaVersion}
          </p>
        </div>
        <span className="confidence-badge">
          <strong>{Math.round(analysis.confidence * 100)}%</strong>
          {confidence.label}
        </span>
      </header>

      {(analysis.requiresHumanReview || analysis.confidence < 0.65) ? (
        <div className="ai-review-warning" role="status">
          <strong>Human review required.</strong>{" "}
          {analysis.confidence < 0.65
            ? "Confidence is below the safe application threshold. Do not apply category or urgency automatically."
            : "The model flagged this suggestion for staff judgment."}
        </div>
      ) : null}

      {mode === "edit" ? (
        <form className="ai-edit-form" onSubmit={submitEdit}>
          <div className="ai-form-grid">
            <label>
              Service category
              <select
                aria-label="Service category"
                value={values.serviceCategory}
                onChange={(event) => updateValue("serviceCategory", event.target.value)}
                disabled={pendingAction !== null}
                required
              >
                <option value="Unknown">Unknown</option>
                {analysis.allowedCategories.map((category) => (
                  <option value={category} key={category}>{formatLabel(category)}</option>
                ))}
              </select>
            </label>
            <label>
              Urgency
              <select
                aria-label="Urgency"
                value={values.urgency}
                onChange={(event) => updateValue("urgency", event.target.value)}
                disabled={pendingAction !== null}
                required
              >
                {urgencyOptions.map((urgency) => (
                  <option value={urgency} key={urgency}>{formatLabel(urgency)}</option>
                ))}
              </select>
            </label>
          </div>

          <label>
            Corrected summary
            <textarea
              aria-label="Corrected summary"
              value={values.summary}
              onChange={(event) => updateValue("summary", event.target.value)}
              maxLength={1000}
              rows={4}
              disabled={pendingAction !== null}
              required
            />
          </label>

          <fieldset>
            <legend>Extracted details</legend>
            <div className="ai-form-grid ai-extracted-grid">
              <label>
                City
                <input
                  aria-label="City"
                  value={values.city ?? ""}
                  onChange={(event) => updateValue("city", event.target.value)}
                  maxLength={200}
                  disabled={pendingAction !== null}
                />
              </label>
              <label>
                Postal code
                <input
                  aria-label="Postal code"
                  value={values.postalCode ?? ""}
                  onChange={(event) => updateValue("postalCode", event.target.value)}
                  maxLength={200}
                  disabled={pendingAction !== null}
                />
              </label>
              <label>
                Preferred callback window
                <input
                  aria-label="Preferred callback window"
                  value={values.preferredCallbackWindow ?? ""}
                  onChange={(event) =>
                    updateValue("preferredCallbackWindow", event.target.value)
                  }
                  maxLength={200}
                  disabled={pendingAction !== null}
                />
              </label>
            </div>
          </fieldset>

          <label>
            Suggested staff reply <span>Draft only — never sent automatically</span>
            <textarea
              aria-label="Suggested staff reply"
              value={values.suggestedReply ?? ""}
              onChange={(event) => updateValue("suggestedReply", event.target.value)}
              maxLength={1000}
              rows={3}
              disabled={pendingAction !== null}
            />
          </label>

          <label>
            Correction reason <span>Optional</span>
            <textarea
              aria-label="Correction reason"
              value={correctionReason}
              onChange={(event) => setCorrectionReason(event.target.value)}
              maxLength={500}
              rows={2}
              disabled={pendingAction !== null}
              placeholder="What did staff correct or clarify?"
            />
          </label>

          <div className="ai-review-actions">
            <button
              className="primary-button"
              type="submit"
              disabled={pendingAction !== null || !values.summary.trim()}
            >
              {pendingAction === "AI suggestion edit"
                ? "Saving correction…"
                : "Save correction"}
            </button>
            <button
              className="quiet-button"
              type="button"
              disabled={pendingAction !== null}
              onClick={() => setMode("view")}
            >
              Cancel
            </button>
          </div>
        </form>
      ) : (
        <>
          <div className="ai-analysis-grid">
            <div className="ai-analysis-summary">
              <span className="field-kicker">
                {analysis.reviewStatus === "Edited"
                  ? "Staff-corrected summary"
                  : "AI summary"}
              </span>
              <p>{displayValues.summary}</p>
            </div>
            <dl className="ai-analysis-facts">
              <div>
                <dt>Service category</dt>
                <dd>{formatLabel(displayValues.serviceCategory)}</dd>
              </div>
              <div>
                <dt>Urgency</dt>
                <dd>{formatLabel(displayValues.urgency)}</dd>
              </div>
              <div>
                <dt>City</dt>
                <dd>{displayValues.city ?? "Not extracted"}</dd>
              </div>
              <div>
                <dt>Postal code</dt>
                <dd>{displayValues.postalCode ?? "Not extracted"}</dd>
              </div>
              <div>
                <dt>Callback window</dt>
                <dd>{displayValues.preferredCallbackWindow ?? "Not extracted"}</dd>
              </div>
            </dl>
          </div>

          {analysis.reasonCodes.length > 0 ? (
            <div className="ai-reason-codes" aria-label="AI reason codes">
              {analysis.reasonCodes.map((code) => (
                <span key={code}>{formatLabel(code)}</span>
              ))}
            </div>
          ) : null}

          {displayValues.suggestedReply ? (
            <div className="ai-draft">
              <span>Suggested staff reply · Not sent</span>
              <p>{displayValues.suggestedReply}</p>
            </div>
          ) : null}

          {analysis.reviewedAtUtc ? (
            <p className="ai-review-metadata">
              {formatLabel(analysis.reviewStatus)} by{" "}
              {analysis.reviewedByUserName ?? "an authorized staff member"} on{" "}
              {formatTimestamp(analysis.reviewedAtUtc)}.
              {analysis.correctionReason
                ? ` Reason: ${analysis.correctionReason}`
                : ""}
            </p>
          ) : null}
        </>
      )}

      {mode === "reject" ? (
        <form className="ai-reject-form" onSubmit={submitReject}>
          <label>
            Rejection reason <span>Optional</span>
            <textarea
              aria-label="Rejection reason"
              value={correctionReason}
              onChange={(event) => setCorrectionReason(event.target.value)}
              maxLength={500}
              rows={2}
              disabled={pendingAction !== null}
              placeholder="Why is this suggestion not useful?"
            />
          </label>
          <div className="ai-review-actions">
            <button
              className="danger-button"
              type="submit"
              disabled={pendingAction !== null}
            >
              {pendingAction === "AI suggestion rejection"
                ? "Rejecting…"
                : "Confirm rejection"}
            </button>
            <button
              className="quiet-button"
              type="button"
              disabled={pendingAction !== null}
              onClick={() => setMode("view")}
            >
              Cancel
            </button>
          </div>
        </form>
      ) : null}

      {isPending && mode === "view" ? (
        <div className="ai-review-footer">
          <p>
            <strong>Staff guidance only.</strong> Accepting, editing, or rejecting
            this suggestion never sends a message or triggers a customer action.
          </p>
          {canManage ? (
            <div className="ai-review-actions">
              <button
                className="primary-button"
                type="button"
                disabled={pendingAction !== null}
                onClick={() => void accept()}
              >
                {pendingAction === "AI suggestion acceptance"
                  ? "Accepting…"
                  : "Accept suggestion"}
              </button>
              <button
                className="quiet-button"
                type="button"
                disabled={pendingAction !== null}
                onClick={() => setMode("edit")}
              >
                Edit suggestion
              </button>
              <button
                className="danger-text-button"
                type="button"
                disabled={pendingAction !== null}
                onClick={() => setMode("reject")}
              >
                Reject suggestion
              </button>
            </div>
          ) : (
            <span className="read-only-note">
              Your role can view this suggestion but cannot review it.
            </span>
          )}
        </div>
      ) : null}
    </section>
  );
}
