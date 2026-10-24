import { SignIn, UserButton, useAuth } from "@clerk/react";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { api, type StreakDetail, type StreakSummary } from "./api";

const zones = ["Europe/Vilnius", "Europe/London", "America/New_York", "America/Los_Angeles", "Asia/Tokyo", "UTC"];

export function App() {
  const { isLoaded, isSignedIn, getToken } = useAuth();
  if (!isLoaded) return <p className="boot-error">Loading…</p>;
  if (!isSignedIn) return <Welcome />;
  return <Home getToken={getToken} />;
}

function Welcome() {
  return (
    <main className="shell">
      <header className="mast">
        <p className="eyebrow">OpenWhen</p>
        <h1>A note that opens when the streak does.</h1>
        <p className="lede">Keep a daily habit with someone. Leave them a message. They can read it after they reach the goal.</p>
      </header>
      <SignIn routing="hash" withSignUp />
    </main>
  );
}

function Home({ getToken }: { getToken: () => Promise<string | null> }) {
  const [streaks, setStreaks] = useState<StreakSummary[]>([]);
  const [openId, setOpenId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [timeZone, setTimeZone] = useState("Europe/Vilnius");
  const [habitName, setHabitName] = useState("");
  const [targetDays, setTargetDays] = useState(7);
  const [code, setCode] = useState("");

  const call = useCallback(async () => {
    const token = await getToken();
    if (!token) throw new Error("Sign in again.");
    return api(token);
  }, [getToken]);

  const refresh = useCallback(async () => {
    const client = await call();
    const [me, list] = await Promise.all([client.me(), client.streaks()]);
    setTimeZone(me.timeZone);
    setStreaks(list);
  }, [call]);

  useEffect(() => {
    refresh().catch((reason: unknown) => setError(reason instanceof Error ? reason.message : "Could not load streaks."));
  }, [refresh]);

  async function onCreate(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      const client = await call();
      const created = await client.create(habitName.trim(), targetDays);
      setHabitName("");
      setOpenId(created.id);
      await refresh();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Could not create the streak.");
    }
  }

  async function onJoin(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      const client = await call();
      const joined = await client.join(code.trim());
      setCode("");
      setOpenId(joined.id);
      await refresh();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Could not join.");
    }
  }

  async function onZone(next: string) {
    setTimeZone(next);
    try {
      const client = await call();
      await client.setTimeZone(next);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Could not save timezone.");
    }
  }

  if (openId) {
    return (
      <StreakScreen
        id={openId}
        call={call}
        onBack={() => {
          setOpenId(null);
          refresh().catch(() => undefined);
        }}
      />
    );
  }

  return (
    <main className="shell">
      <header className="topbar">
        <div>
          <p className="eyebrow">OpenWhen</p>
          <h1>Your streaks</h1>
        </div>
        <UserButton />
      </header>
      {error && <p className="banner">{error}</p>}
      <label className="field">
        <span>Your day resets in</span>
        <select value={timeZone} onChange={(event) => onZone(event.target.value)}>
          {(zones.includes(timeZone) ? zones : [timeZone, ...zones]).map((zone) => (
            <option key={zone} value={zone}>
              {zone}
            </option>
          ))}
        </select>
      </label>
      <ul className="cards">
        {streaks.map((streak) => (
          <li key={streak.id}>
            <button className="card" type="button" onClick={() => setOpenId(streak.id)}>
              <strong>{streak.habitName}</strong>
              <span>
                {streak.currentStreak} / {streak.targetDays} days
                {streak.checkedInToday ? " · checked in" : ""}
              </span>
            </button>
          </li>
        ))}
        {streaks.length === 0 && <li className="empty">No streaks yet. Start one, or join with a code.</li>}
      </ul>
      <form className="panel" onSubmit={onCreate}>
        <h2>Start a streak</h2>
        <label className="field">
          <span>Habit</span>
          <input value={habitName} onChange={(event) => setHabitName(event.target.value)} maxLength={80} required placeholder="Morning stretch" />
        </label>
        <label className="field">
          <span>Opens after (days)</span>
          <input
            type="number"
            min={1}
            max={365}
            value={targetDays}
            onChange={(event) => setTargetDays(Number(event.target.value))}
            required
          />
        </label>
        <button className="primary" type="submit">
          Create
        </button>
      </form>
      <form className="panel" onSubmit={onJoin}>
        <h2>Join with a code</h2>
        <label className="field">
          <span>Join code</span>
          <input value={code} onChange={(event) => setCode(event.target.value)} required placeholder="Paste the code" autoCapitalize="none" />
        </label>
        <button className="primary" type="submit">
          Join
        </button>
      </form>
    </main>
  );
}

function StreakScreen({
  id,
  call,
  onBack,
}: {
  id: string;
  call: () => Promise<ReturnType<typeof api>>;
  onBack: () => void;
}) {
  const [streak, setStreak] = useState<StreakDetail | null>(null);
  const [me, setMe] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [recipient, setRecipient] = useState("");
  const [body, setBody] = useState("");
  const [copied, setCopied] = useState(false);

  const load = useCallback(async () => {
    const client = await call();
    const [profile, detail] = await Promise.all([client.me(), client.get(id)]);
    setMe(profile.userId);
    setStreak(detail);
    const others = detail.members.filter((member) => member !== profile.userId);
    setRecipient((current) => (current && others.includes(current) ? current : others[0] ?? ""));
  }, [call, id]);

  useEffect(() => {
    load().catch((reason: unknown) => setError(reason instanceof Error ? reason.message : "Could not open streak."));
  }, [load]);

  async function checkIn() {
    setError(null);
    try {
      const client = await call();
      await client.checkIn(id);
      await load();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Check-in failed.");
    }
  }

  async function send(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      const client = await call();
      await client.leaveMessage(id, recipient, body.trim());
      setBody("");
      await load();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Could not leave the message.");
    }
  }

  async function copyCode() {
    if (!streak) return;
    await navigator.clipboard.writeText(streak.joinCode);
    setCopied(true);
  }

  if (!streak) {
    return (
      <main className="shell">
        <p className="boot-error">{error ?? "Loading…"}</p>
      </main>
    );
  }

  const others = streak.members.filter((member) => member !== me);

  return (
    <main className="shell">
      <header className="topbar">
        <button className="text" type="button" onClick={onBack}>
          Back
        </button>
        <UserButton />
      </header>
      <p className="eyebrow">Goal {streak.targetDays} days</p>
      <h1>{streak.habitName}</h1>
      <p className="count" aria-live="polite">
        {streak.currentStreak}
        <span>day streak</span>
      </p>
      <button className="primary" type="button" onClick={checkIn} disabled={streak.checkedInToday}>
        {streak.checkedInToday ? "Checked in today" : "Check in today"}
      </button>
      {error && <p className="banner">{error}</p>}
      <section className="panel">
        <h2>Invite</h2>
        <p className="code">{streak.joinCode}</p>
        <button className="secondary" type="button" onClick={copyCode}>
          {copied ? "Copied" : "Copy join code"}
        </button>
      </section>
      <section className="panel">
        <h2>Leave a message</h2>
        {others.length === 0 ? (
          <p className="muted">Waiting for someone to join.</p>
        ) : (
          <form onSubmit={send}>
            <label className="field">
              <span>For</span>
              <select value={recipient} onChange={(event) => setRecipient(event.target.value)}>
                {others.map((member) => (
                  <option key={member} value={member}>
                    {member}
                  </option>
                ))}
              </select>
            </label>
            <label className="field">
              <span>Message</span>
              <textarea value={body} onChange={(event) => setBody(event.target.value)} maxLength={2000} required rows={4} placeholder="Read this when you get there." />
            </label>
            <button className="primary" type="submit">
              Leave message
            </button>
          </form>
        )}
      </section>
      <section className="panel">
        <h2>Messages</h2>
        <ul className="messages">
          {streak.messages.map((message) => (
            <li key={message.id} className={message.direction === "in" && message.locked ? "locked" : "open"}>
              {message.direction === "out" && <p>You left a message. Only they can read it.</p>}
              {message.direction === "in" && message.locked && <p>A message is waiting until your streak reaches {streak.targetDays}.</p>}
              {message.direction === "in" && !message.locked && <p>{message.body}</p>}
            </li>
          ))}
          {streak.messages.length === 0 && <li className="empty">Nothing here yet.</li>}
        </ul>
      </section>
    </main>
  );
}
