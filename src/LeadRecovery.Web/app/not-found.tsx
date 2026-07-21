import Link from "next/link";

export default function NotFound() {
  return (
    <main id="main-content" className="dashboard-shell">
      <section className="error-state">
        <span className="empty-state-icon" aria-hidden="true">?</span>
        <p className="eyebrow">Page not found</p>
        <h1>There’s nothing here</h1>
        <p>The page may have moved, or the address may be incomplete.</p>
        <Link className="primary-button button-link" href="/leads">Return to the inbox</Link>
      </section>
    </main>
  );
}
