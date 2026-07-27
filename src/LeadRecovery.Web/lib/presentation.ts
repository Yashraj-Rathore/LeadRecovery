const exactLabels: Record<string, string> = {
  AwaitingCustomer: "Awaiting customer",
  BookingOffered: "Booking offered",
  ClosedWon: "Closed — won",
  CriticalReview: "Critical review",
  LostNoResponse: "Lost — no response",
  LostOutOfArea: "Lost — out of area",
  LostUnavailableService: "Lost — service unavailable",
  NeedsHuman: "Needs human review",
  PausedByUser: "Paused by staff",
  SuppressedOptOut: "Suppressed — opted out",
  SendBookingLink: "Send booking link",
  SendFollowUpSms: "Send follow-up SMS",
  SendInitialRecoverySms: "Send initial recovery SMS",
  SendQualificationQuestion: "Send qualification question",
};

export function formatLabel(value: string): string {
  if (!value) return "Unknown";
  if (exactLabels[value]) return exactLabels[value];

  return value
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/Sms\b/g, "SMS")
    .replace(/^./, (character) => character.toUpperCase());
}

export function formatRelativeTime(timestamp: string, now = Date.now()): string {
  const elapsedMilliseconds = Math.max(0, now - new Date(timestamp).getTime());
  const minutes = Math.floor(elapsedMilliseconds / 60_000);

  if (minutes < 1) return "Just now";
  if (minutes < 60) return `${minutes}m ago`;

  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;

  const days = Math.floor(hours / 24);
  return days < 7 ? `${days}d ago` : formatDate(timestamp);
}

export function formatDate(timestamp: string): string {
  return new Intl.DateTimeFormat(undefined, {
    month: "short",
    day: "numeric",
    year: new Date(timestamp).getFullYear() === new Date().getFullYear() ? undefined : "numeric",
  }).format(new Date(timestamp));
}

export function formatTimestamp(timestamp: string): string {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(timestamp));
}

export function getInitials(value: string): string {
  const words = value.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return "?";
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return `${words[0][0]}${words.at(-1)?.[0] ?? ""}`.toUpperCase();
}
