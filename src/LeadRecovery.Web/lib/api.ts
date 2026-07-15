export type AuthSession = {
  userId: string;
  displayName: string;
  email: string;
  tenantId: string;
  tenantName: string;
  role: string;
};

export type LeadSummary = {
  id: string;
  displayName: string | null;
  primaryPhoneE164: string;
  source: string;
  status: string;
  urgency: string;
  automationState: string;
  assignedUserId: string | null;
  assignedUserName: string | null;
  lastActivityAtUtc: string;
  hasUnreadCustomerActivity: boolean;
  rowVersion: string;
  createdAtUtc: string;
};

export type LeadPage = {
  items: LeadSummary[];
  nextCursor: string | null;
};

export type LeadTimelineItem = {
  id: string;
  type: "Call" | "Sms" | "System" | "Note";
  label: string;
  body: string | null;
  direction: string | null;
  kind: string | null;
  status: string | null;
  failureDescription: string | null;
  actorName: string | null;
  occurredAtUtc: string;
};

export type PendingAction = {
  id: string;
  actionType: string;
  status: string;
  scheduledForUtc: string;
  attemptCount: number;
};

export type AssignableUser = {
  userId: string;
  displayName: string;
  role: string;
};

export type LeadDetail = {
  lead: LeadSummary;
  timeline: LeadTimelineItem[];
  pendingActions: PendingAction[];
  assignableUsers: AssignableUser[];
  allowedTransitions: string[];
};

export type ApiProblem = {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
  current?: LeadDetail | null;
};

export async function securePost(
  path: string,
  body: unknown,
): Promise<{ response: Response; payload: LeadDetail | ApiProblem | null }> {
  const csrfResponse = await fetch("/api/v1/auth/csrf", {
    cache: "no-store",
    credentials: "same-origin",
  });
  if (!csrfResponse.ok) {
    throw new Error("Unable to initialize a secure request.");
  }

  const csrf = (await csrfResponse.json()) as { token: string };
  const response = await fetch(path, {
    method: "POST",
    credentials: "same-origin",
    headers: {
      "Content-Type": "application/json",
      "X-CSRF-TOKEN": csrf.token,
    },
    body: JSON.stringify(body),
  });
  let payload: LeadDetail | ApiProblem | null = null;
  if (response.headers.get("content-type")?.includes("json")) {
    payload = (await response.json()) as LeadDetail | ApiProblem;
  }

  return { response, payload };
}

export function getApiBaseUrl(): string {
  const apiBaseUrl = process.env.API_BASE_URL;
  if (!apiBaseUrl) {
    throw new Error("API_BASE_URL is required for server-side API requests.");
  }

  return apiBaseUrl;
}
