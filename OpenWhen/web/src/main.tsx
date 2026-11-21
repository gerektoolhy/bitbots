import { ClerkProvider } from "@clerk/react";
import { StrictMode, useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import "./styles.css";

function Root() {
  const [publishableKey, setPublishableKey] = useState<string | null>(null);
  const [proxyUrl, setProxyUrl] = useState<string | undefined>(undefined);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetch("/api/config")
      .then(async (response) => {
        const body = (await response.json()) as { clerkPublishableKey?: string; clerkProxyPath?: string; detail?: string };
        if (!response.ok || !body.clerkPublishableKey) throw new Error(body.detail ?? "OpenWhen could not read CLERK_PUBLISHABLE_KEY.");
        setPublishableKey(body.clerkPublishableKey);
        setProxyUrl(body.clerkProxyPath ? new URL(body.clerkProxyPath, window.location.origin).toString() : undefined);
      })
      .catch((reason: unknown) => {
        setError(reason instanceof Error ? reason.message : "Could not load sign-in.");
      });
  }, []);

  if (error) {
    return <p className="boot-error">{error}</p>;
  }
  if (!publishableKey) {
    return <p className="boot-error">Loading…</p>;
  }

  return (
    <ClerkProvider publishableKey={publishableKey} proxyUrl={proxyUrl} signInUrl="/" signUpUrl="/" afterSignOutUrl="/">
      <App />
    </ClerkProvider>
  );
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <Root />
  </StrictMode>,
);
