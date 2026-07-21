import type { Metadata, Viewport } from "next";

import "./styles.css";

export const metadata: Metadata = {
  title: {
    default: "LeadRecovery",
    template: "%s | LeadRecovery",
  },
  description: "A focused inbox for missed-call recovery.",
};

export const viewport: Viewport = {
  colorScheme: "light",
  themeColor: "#123f34",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>
        <a className="skip-link" href="#main-content">
          Skip to main content
        </a>
        {children}
      </body>
    </html>
  );
}
