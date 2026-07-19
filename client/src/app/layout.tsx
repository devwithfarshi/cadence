import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import { Tooltip } from "radix-ui";
import { AuthProvider } from "@/components/providers/auth-provider";
import { PreferencesProvider } from "@/components/providers/preferences-provider";
import { ToastProvider } from "@/components/ui/toast";
import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: {
    default: "Cadence — AI Meeting Assistant",
    template: "%s · Cadence",
  },
  description:
    "Record, transcribe and summarise every meeting. Turn discussion into decisions and action items automatically.",
};

/**
 * Applies the persisted theme synchronously, while the browser is still parsing
 * the document. Without this the page paints in light mode and then snaps to
 * dark once React hydrates.
 *
 * Deliberately dependency-free and defensive: a corrupt preferences entry must
 * never stop the app from rendering.
 */
const THEME_SCRIPT = `
(function () {
  try {
    var raw = localStorage.getItem("ama:preferences");
    var theme = raw ? (JSON.parse(raw) || {}).theme : "system";
    if (theme !== "light" && theme !== "dark") {
      theme = window.matchMedia("(prefers-color-scheme: dark)").matches
        ? "dark"
        : "light";
    }
    if (theme === "dark") document.documentElement.classList.add("dark");
  } catch (e) {}
})();
`;

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="en"
      // The inline script below mutates this element's class list before React
      // hydrates, which would otherwise be reported as a mismatch.
      suppressHydrationWarning
      className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}
    >
      <head>
        {/* biome-ignore lint/security/noDangerouslySetInnerHtml: static, self-authored theme bootstrap */}
        <script dangerouslySetInnerHTML={{ __html: THEME_SCRIPT }} />
      </head>
      <body className="min-h-full bg-background text-foreground">
        <PreferencesProvider>
          <AuthProvider>
            <ToastProvider>
              <Tooltip.Provider delayDuration={300}>
                {children}
              </Tooltip.Provider>
            </ToastProvider>
          </AuthProvider>
        </PreferencesProvider>
      </body>
    </html>
  );
}
