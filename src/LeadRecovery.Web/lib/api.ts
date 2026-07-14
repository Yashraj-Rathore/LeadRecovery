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
  createdAtUtc: string;
};

export type LeadPage = {
  items: LeadSummary[];
  nextCursor: string | null;
};

export function getApiBaseUrl(): string {
  const apiBaseUrl = process.env.API_BASE_URL;
  if (!apiBaseUrl) {
    throw new Error("API_BASE_URL is required for server-side API requests.");
  }

  return apiBaseUrl;
}
