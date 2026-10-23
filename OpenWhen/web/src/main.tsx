import { ClerkProvider } from "@clerk/react";
import { StrictMode, useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import "./styles.css";

function Root() {
  const [publishableKey, setPublishableKey] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetch("/api/config")
      .then(async (response) => {
        if (!response.ok) throw new Error("OpenWhen could not read CLERK_PUBLISHABLE_KEY.");
        const body = (await response.json()) as { clerkPublishableKey: string };
        setPublishableKey(body.clerkPublishableKey);
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
    <ClerkProvider publishableKey={publishableKey}>
      <App />
    </ClerkProvider>
  );
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <Root />
  </StrictMode>,
);
