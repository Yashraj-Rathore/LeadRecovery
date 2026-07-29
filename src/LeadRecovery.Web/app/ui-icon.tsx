export type UiIconName =
  | "activity"
  | "alert"
  | "arrow-left"
  | "arrow-right"
  | "chart"
  | "check"
  | "download"
  | "inbox"
  | "message"
  | "refresh"
  | "shield"
  | "sparkles"
  | "user-plus";

export function UiIcon({
  name,
  size = 18,
  className,
}: {
  name: UiIconName;
  size?: number;
  className?: string;
}) {
  return (
    <svg
      aria-hidden="true"
      className={className}
      fill="none"
      height={size}
      viewBox="0 0 24 24"
      width={size}
    >
      {name === "activity" ? (
        <path d="M3 12h4l2.2-6 4.1 12 2.2-6H21" />
      ) : null}
      {name === "alert" ? (
        <>
          <path d="M10.3 3.7 2.8 17a2 2 0 0 0 1.7 3h15a2 2 0 0 0 1.7-3L13.7 3.7a2 2 0 0 0-3.4 0Z" />
          <path d="M12 9v4m0 3.5h.01" />
        </>
      ) : null}
      {name === "arrow-left" ? (
        <path d="m15 18-6-6 6-6" />
      ) : null}
      {name === "arrow-right" ? (
        <path d="M5 12h14m-5-5 5 5-5 5" />
      ) : null}
      {name === "chart" ? (
        <>
          <path d="M4 19V9m6 10V5m6 14v-7m4 9H2" />
          <path d="m4 9 6-4 6 7 4-3" />
        </>
      ) : null}
      {name === "check" ? <path d="m5 12 4 4L19 6" /> : null}
      {name === "download" ? (
        <>
          <path d="M12 3v12m-5-5 5 5 5-5" />
          <path d="M5 21h14" />
        </>
      ) : null}
      {name === "inbox" ? (
        <>
          <path d="M4 4h16v14H4z" />
          <path d="M4 13h4l2 3h4l2-3h4" />
        </>
      ) : null}
      {name === "message" ? (
        <path d="M20 14a3 3 0 0 1-3 3H9l-5 4v-4a3 3 0 0 1-1-2.2V7a3 3 0 0 1 3-3h11a3 3 0 0 1 3 3Z" />
      ) : null}
      {name === "refresh" ? (
        <>
          <path d="M20 7v5h-5" />
          <path d="M18.5 16a8 8 0 1 1 .5-8l1 4" />
        </>
      ) : null}
      {name === "shield" ? (
        <>
          <path d="M12 3 4 6v5c0 5 3.4 8.5 8 10 4.6-1.5 8-5 8-10V6Z" />
          <path d="m8.5 12 2.2 2.2 4.8-5" />
        </>
      ) : null}
      {name === "sparkles" ? (
        <>
          <path d="m12 3 1.2 3.3L16.5 7.5l-3.3 1.2L12 12l-1.2-3.3-3.3-1.2 3.3-1.2Z" />
          <path d="m18 13 .8 2.2L21 16l-2.2.8L18 19l-.8-2.2L15 16l2.2-.8Z" />
          <path d="m5 14 .6 1.4L7 16l-1.4.6L5 18l-.6-1.4L3 16l1.4-.6Z" />
        </>
      ) : null}
      {name === "user-plus" ? (
        <>
          <circle cx="9" cy="8" r="4" />
          <path d="M2.5 21a6.5 6.5 0 0 1 13 0M19 8v6m-3-3h6" />
        </>
      ) : null}
    </svg>
  );
}
