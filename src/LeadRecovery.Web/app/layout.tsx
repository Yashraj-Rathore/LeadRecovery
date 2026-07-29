import type { Metadata, Viewport } from "next";

import "./styles.css";
import "./dark-theme.css";

export const metadata: Metadata = {
  title: {
    default: "LeadRecovery",
    template: "%s | LeadRecovery",
  },
  description: "A focused inbox for missed-call recovery.",
};

export const viewport: Viewport = {
  colorScheme: "dark",
  themeColor: "#07090d",
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
