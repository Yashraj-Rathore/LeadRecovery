"use client";

import { FormEvent, useState } from "react";
import { useRouter } from "next/navigation";

import { UiIcon } from "../ui-icon";

type CsrfResponse = {
  token: string;
};

export default function LoginPage() {
  const router = useRouter();
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setIsSubmitting(true);

    const form = new FormData(event.currentTarget);
    try {
      const csrfResponse = await fetch("/api/v1/auth/csrf", {
        cache: "no-store",
        credentials: "same-origin",
      });
      if (!csrfResponse.ok) {
        throw new Error("Unable to initialize a secure login.");
      }

      const csrf = (await csrfResponse.json()) as CsrfResponse;
      const loginResponse = await fetch("/api/v1/auth/login", {
        method: "POST",
        credentials: "same-origin",
        headers: {
          "Content-Type": "application/json",
          "X-CSRF-TOKEN": csrf.token,
        },
        body: JSON.stringify({
          email: form.get("email"),
          password: form.get("password"),
        }),
      });
      if (!loginResponse.ok) {
        setError(
          loginResponse.status === 429
            ? "Too many login attempts. Wait a moment and try again."
            : "The email or password is invalid.",
        );
        return;
      }

      router.replace("/leads");
      router.refresh();
    } catch {
      setError("Login is temporarily unavailable. Please try again.");
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <main id="main-content" className="login-shell">
      <section className="login-intro" aria-labelledby="login-heading">
        <div className="brand-lockup brand-lockup-inverse">
          <span className="brand-mark" aria-hidden="true">
            <span />
          </span>
          <span>
            <strong>LeadRecovery</strong>
            <small>Operations workspace</small>
          </span>
        </div>

        <div className="login-copy">
          <p className="eyebrow">Every missed call, accounted for</p>
          <h1 id="login-heading">Move every lead forward.</h1>
          <p>
            A focused workspace for home-service teams to recover missed calls,
            respond sooner, and keep every next action clear.
          </p>
        </div>

        <ul className="login-benefits" aria-label="Workspace benefits">
          <li>
            <span aria-hidden="true">01</span>
            <div>
              <strong>See what needs attention</strong>
              <small>Urgent and unread leads rise above the noise.</small>
            </div>
          </li>
          <li>
            <span aria-hidden="true">02</span>
            <div>
              <strong>Keep the full context</strong>
              <small>Calls, messages, notes, and scheduled work in one view.</small>
            </div>
          </li>
          <li>
            <span aria-hidden="true">03</span>
            <div>
              <strong>Stay in control</strong>
              <small>Automation is visible, reversible, and staff-led.</small>
            </div>
          </li>
        </ul>

        <div className="signal-card" aria-label="Recovery workflow status">
          <span className="signal-dot" aria-hidden="true" />
          <span>
            <small>Recovery workflow</small>
            <strong>Ready for the next call</strong>
          </span>
          <span className="signal-status">Ready</span>
        </div>
      </section>

      <section className="login-panel" aria-label="Account login">
        <div className="login-panel-inner">
          <header>
            <p className="eyebrow">Secure workspace</p>
            <h2>Welcome back</h2>
            <p className="muted">Sign in with your staff account to open the lead inbox.</p>
          </header>

          <form onSubmit={handleSubmit} className="login-form" noValidate={false}>
            <label htmlFor="email">Email address</label>
            <input
              id="email"
              name="email"
              type="email"
              autoComplete="username"
              inputMode="email"
              aria-invalid={Boolean(error)}
              aria-describedby={error ? "login-error" : undefined}
              required
            />

            <label htmlFor="password">Password</label>
            <input
              id="password"
              name="password"
              type="password"
              autoComplete="current-password"
              aria-invalid={Boolean(error)}
              aria-describedby={error ? "login-error" : undefined}
              required
            />

            {error ? (
              <p id="login-error" className="form-error" role="alert">
                {error}
              </p>
            ) : null}

            <button className="primary-button login-submit" type="submit" disabled={isSubmitting}>
              {isSubmitting ? "Signing in…" : "Sign in"}
            </button>
          </form>

          <p className="security-note">
            <span><UiIcon name="shield" size={14} /></span>
            Protected by a secure, same-origin staff session.
          </p>
        </div>
      </section>
    </main>
  );
}
