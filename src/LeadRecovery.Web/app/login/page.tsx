"use client";

import { FormEvent, useState } from "react";
import { useRouter } from "next/navigation";

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
    <main className="login-shell">
      <section className="login-intro" aria-labelledby="login-heading">
        <p className="eyebrow">LeadRecovery</p>
        <h1 id="login-heading">Turn missed calls into a clear next action.</h1>
        <p>
          A focused tenant inbox for home-service teams—built to show what needs
          attention without adding CRM noise.
        </p>
        <div className="signal-card" aria-hidden="true">
          <span className="signal-dot" />
          <span>Recovery workflow ready</span>
          <strong>Under 1 min</strong>
        </div>
      </section>

      <section className="login-panel" aria-label="Account login">
        <div>
          <p className="eyebrow">Secure workspace</p>
          <h2>Welcome back</h2>
          <p className="muted">Sign in with your staff account.</p>
        </div>

        <form onSubmit={handleSubmit} className="login-form">
          <label htmlFor="email">Email address</label>
          <input
            id="email"
            name="email"
            type="email"
            autoComplete="username"
            required
          />

          <label htmlFor="password">Password</label>
          <input
            id="password"
            name="password"
            type="password"
            autoComplete="current-password"
            required
          />

          {error ? (
            <p className="form-error" role="alert">
              {error}
            </p>
          ) : null}

          <button type="submit" disabled={isSubmitting}>
            {isSubmitting ? "Signing in…" : "Sign in"}
          </button>
        </form>

        <p className="security-note">
          Your session is protected with an HttpOnly same-origin cookie.
        </p>
      </section>
    </main>
  );
}
