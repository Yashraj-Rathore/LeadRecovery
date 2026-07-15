export default function LeadsLoading() {
  return (
    <main className="dashboard-shell">
      <section className="loading-page" role="status" aria-live="polite">
        <p className="eyebrow">LeadRecovery</p>
        <h1>Loading your lead inbox…</h1>
        <span className="loading-bar" aria-hidden="true" />
      </section>
    </main>
  );
}
