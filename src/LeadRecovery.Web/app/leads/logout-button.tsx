"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

export function LogoutButton() {
  const router = useRouter();
  const [isPending, setIsPending] = useState(false);

  async function logout() {
    setIsPending(true);
    try {
      const tokenResponse = await fetch("/api/v1/auth/csrf", {
        cache: "no-store",
        credentials: "same-origin",
      });
      if (!tokenResponse.ok) {
        return;
      }

      const { token } = (await tokenResponse.json()) as { token: string };
      const response = await fetch("/api/v1/auth/logout", {
        method: "POST",
        credentials: "same-origin",
        headers: { "X-CSRF-TOKEN": token },
      });
      if (response.ok) {
        router.replace("/login");
        router.refresh();
      }
    } finally {
      setIsPending(false);
    }
  }

  return (
    <button className="sign-out-button" type="button" onClick={logout} disabled={isPending}>
      {isPending ? "Signing out…" : "Sign out"}
    </button>
  );
}
