import Link from "next/link";

import { AuthSession } from "../../lib/api";
import { getInitials } from "../../lib/presentation";
import { UiIcon } from "../ui-icon";
import { AutomationControl } from "./automation-control";
import { LogoutButton } from "./logout-button";

export function WorkspaceHeader({
  session,
  current = "inbox",
}: {
  session: AuthSession;
  current?: "inbox" | "pilot";
}) {
  return (
    <header className="workspace-header">
      <div className="workspace-header-inner">
        <Link className="brand-lockup" href="/leads" aria-label="LeadRecovery inbox">
          <span className="brand-mark" aria-hidden="true">
            <span />
          </span>
          <span>
            <strong>LeadRecovery</strong>
            <small>{session.tenantName}</small>
          </span>
        </Link>

        <nav className="primary-nav" aria-label="Primary navigation">
          <Link href="/leads" aria-current={current === "inbox" ? "page" : undefined}>
            <UiIcon name="inbox" size={16} />
            Inbox
          </Link>
          <Link href="/reports/pilot" aria-current={current === "pilot" ? "page" : undefined}>
            <UiIcon name="chart" size={16} />
            Pilot report
          </Link>
        </nav>

        <div className="session-block">
          <AutomationControl role={session.role} />
          <span className="user-avatar" aria-hidden="true">
            {getInitials(session.displayName)}
          </span>
          <span className="session-identity">
            <strong>{session.displayName}</strong>
            <small>{session.role}</small>
          </span>
          <LogoutButton />
        </div>
      </div>
    </header>
  );
}
