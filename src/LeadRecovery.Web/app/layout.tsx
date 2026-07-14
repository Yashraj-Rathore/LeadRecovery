import type { Metadata } from "next";

import "./styles.css";

export const metadata: Metadata = {
  title: "LeadRecovery",
  description: "A focused inbox for missed-call recovery.",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
