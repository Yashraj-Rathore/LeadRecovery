"use client";

import { useCallback, useEffect, useState } from "react";

import { AutomationStatus, securePost } from "../../lib/api";

export function AutomationControl({ role }: { role: string }) {
  const [status, setStatus] = useState<AutomationStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const canManage = role === "Owner" || role === "Manager";

  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      const response = await fetch("/api/v1/automation", {
        cache: "no-store",
        credentials: "same-origin",
        signal,
      });
      if (!response.ok) throw new Error("Automation status is unavailable.");
      setStatus((await response.json()) as AutomationStatus);
      setError(null);
    } catch (loadError) {
      if (loadError instanceof DOMException && loadError.name === "AbortError") return;
      setError("Automation status unavailable");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  async function updateTenantAutomation() {
    if (!status) return;
    setSubmitting(true);
    setNotice(null);
    setError(null);
    const enabling = !status.tenantEnabled;
    try {
      const { response, payload } = await securePost<AutomationStatus>(
        "/api/v1/automation/tenant",
        {
          enabled: enabling,
          expectedRowVersion: status.tenantRowVersion,
          reasonCode: "TenantRequest",
        },
      );
      if (response.status === 409) {
        await load();
        setError("Automation changed in another session. Review the latest state.");
        return;
      }

      if (!response.ok || !payload || !("tenantEnabled" in payload)) {
        throw new Error("Automation could not be updated.");
      }

      setStatus(payload);
      setNotice(
        enabling
          ? "Tenant automation resumed. New eligible work may now be scheduled."
          : `${payload.cancelledActionCount} pending automated ${payload.cancelledActionCount === 1 ? "action" : "actions"} cancelled.`,
      );
    } catch {
      setError("Automation could not be updated. Try again or contact an owner.");
    } finally {
      setSubmitting(false);
    }
  }

  const label = loading
    ? "Checking automation"
    : !status
      ? "Automation unknown"
      : !status.globalEnabled
        ? "Platform paused"
        : status.tenantEnabled
          ? "Automation on"
          : "Tenant paused";
  const tone = !status?.effectiveEnabled ? "paused" : "active";

  if (!canManage) {
    return (
      <div className={`automation-switch-status automation-switch-${tone}`} title={error ?? label}>
        <span aria-hidden="true" />
        {label}
      </div>
    );
  }

  return (
    <details className="automation-control">
      <summary className={`automation-switch-status automation-switch-${tone}`}>
        <span aria-hidden="true" />
        {label}
      </summary>
      <div className="automation-control-panel">
        <strong>{label}</strong>
        <p>
          {!status?.globalEnabled
            ? "The platform-wide switch is off. Inbound messages and the dashboard remain available."
            : status.tenantEnabled
              ? "Approved automated recovery and follow-up work can run for this tenant."
              : "Automated sends are paused. Manual staff messages remain available."}
        </p>
        {status && (
          <button
            className={status.tenantEnabled ? "warning-button" : "secondary-button"}
            disabled={submitting}
            onClick={() => void updateTenantAutomation()}
            type="button"
          >
            {submitting
              ? "Updating…"
              : status.tenantEnabled
                ? "Pause tenant automation"
                : "Resume tenant automation"}
          </button>
        )}
        {notice && (
          <p className="automation-control-notice" role="status">
            {notice}
          </p>
        )}
        {error && (
          <p className="automation-control-error" role="alert">
            {error}
          </p>
        )}
      </div>
    </details>
  );
}
