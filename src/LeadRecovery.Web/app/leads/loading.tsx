export default function LeadsLoading() {
  return (
    <main id="main-content" className="dashboard-shell">
      <section className="loading-page" role="status" aria-live="polite">
        <div className="loading-brand-mark" aria-hidden="true"><span /></div>
        <p className="eyebrow">LeadRecovery</p>
        <h1>Preparing your lead inbox…</h1>
        <span className="loading-bar" aria-hidden="true" />
      </section>
    </main>
  );
}
