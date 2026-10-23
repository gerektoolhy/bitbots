export type StreakSummary = {
  id: string;
  habitName: string;
  targetDays: number;
  joinCode: string;
  currentStreak: number;
  checkedInToday: boolean;
};

export type MessageView = {
  id: string;
  direction: "in" | "out";
  locked?: boolean;
  body?: string;
  createdAt: string;
};

export type StreakDetail = StreakSummary & {
  members: string[];
  messages: MessageView[];
};

export type Me = {
  userId: string;
  timeZone: string;
};

async function request<T>(path: string, token: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      Authorization: `Bearer ${token}`,
      ...(init?.body ? { "Content-Type": "application/json" } : {}),
      ...init?.headers,
    },
  });
  const text = await response.text();
  const payload = text ? JSON.parse(text) : null;
  if (!response.ok) {
    const message = payload?.error ?? payload?.title ?? `Request failed (${response.status})`;
    throw new Error(message);
  }
  return payload as T;
}

export function api(token: string) {
  return {
    me: () => request<Me>("/api/me", token),
    setTimeZone: (timeZone: string) =>
      request<Me>("/api/me", token, { method: "PUT", body: JSON.stringify({ timeZone }) }),
    streaks: () => request<StreakSummary[]>("/api/streaks", token),
    create: (habitName: string, targetDays: number) =>
      request<StreakSummary>("/api/streaks", token, {
        method: "POST",
        body: JSON.stringify({ habitName, targetDays }),
      }),
    join: (code: string) =>
      request<StreakSummary>("/api/streaks/join", token, {
        method: "POST",
        body: JSON.stringify({ code }),
      }),
    get: (id: string) => request<StreakDetail>(`/api/streaks/${id}`, token),
    checkIn: (id: string) =>
      request<{ currentStreak: number; checkedInToday: boolean; counted: boolean }>(
        `/api/streaks/${id}/check-in`,
        token,
        { method: "POST", body: "{}" },
      ),
    leaveMessage: (id: string, toUserId: string, body: string) =>
      request<{ id: string }>(`/api/streaks/${id}/messages`, token, {
        method: "POST",
        body: JSON.stringify({ toUserId, body }),
      }),
  };
}
