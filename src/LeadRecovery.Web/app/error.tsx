"use client";

export default function GlobalError({
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  return (
    <main id="main-content" className="dashboard-shell">
      <section className="error-state" role="alert">
        <span className="empty-state-icon" aria-hidden="true">!</span>
        <p className="eyebrow">Something interrupted the workspace</p>
        <h1>This page couldn’t be loaded</h1>
        <p>Your work is safe. Check your connection, then try loading the page again.</p>
        <button className="primary-button" type="button" onClick={reset}>Try again</button>
      </section>
    </main>
  );
}
