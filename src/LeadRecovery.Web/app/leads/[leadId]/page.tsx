import { headers } from "next/headers";
import { redirect } from "next/navigation";

import { AuthSession, getApiBaseUrl } from "../../../lib/api";
import { LeadDetailView } from "./lead-detail";

async function getSession(): Promise<AuthSession | null> {
  const requestHeaders = await headers();
  const response = await fetch(`${getApiBaseUrl()}/api/v1/auth/me`, {
    cache: "no-store",
    headers: { cookie: requestHeaders.get("cookie") ?? "" },
  });
  if (response.status === 401) return null;
  if (!response.ok) throw new Error("The authenticated session could not be loaded.");
  return (await response.json()) as AuthSession;
}

export default async function LeadDetailPage({
  params,
}: {
  params: Promise<{ leadId: string }>;
}) {
  const [session, route] = await Promise.all([getSession(), params]);
  if (!session) redirect("/login");
  return <LeadDetailView leadId={route.leadId} session={session} />;
}
