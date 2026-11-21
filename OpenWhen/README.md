# OpenWhen

A signed-in person starts a streak (a habit name and a target length in days) and gets a join code. Someone else joins with that code. Each member checks in once per calendar day in their own timezone. A member can leave a text message for one other member. The recipient can see that a message is waiting, and can read it only after their current streak reaches the target. If they were allowed to read it, a later missed day does not hide it again.

The browser app is Clerk. Signed-out people only see Continue with Google, which starts Clerk OAuth with strategy `oauth_google` for both sign-in and sign-up. Turn off email and password under Clerk → User & authentication if that toggle exists. The API checks the Clerk session token on every protected call and uses the token subject as the user id. Session tokens are signed by the Frontend API, so the API verifies them with the public keys at `https://<frontend-api>/.well-known/jwks.json`, derived from `CLERK_PUBLISHABLE_KEY`. Those tokens have no `aud` claim.

Production keys only accept the Clerk domain (`streak.me.uk` and its subdomains). `https://bitbots.onrender.com` is the same service on Render's host, so Clerk rejects it with `origin_invalid`. On any other host the browser talks to `/__clerk` on that host, and the API forwards those calls to Clerk with `CLERK_SECRET_KEY`.

ASP.NET Core serves the built React app and the API in one process. `GET /health` returns 200 and does not touch the database. On startup the API applies EF Core migrations to Postgres.

## Environment variables

The app reads these and nothing else for secrets. Do not commit values.

| Variable | Purpose |
| --- | --- |
| `DATABASE_URL` | Postgres connection string. A Render-style `postgres://USER:PASSWORD@HOST:5432/DATABASE` URL is accepted, as is an Npgsql key/value string. |
| `CLERK_PUBLISHABLE_KEY` | Clerk publishable key (`pk_...`). The browser loads it from `GET /api/config`. The API uses it to find the Frontend API and verify session tokens. |
| `CLERK_SECRET_KEY` | Clerk secret key (`sk_...`). Required on hosts other than the production domain, including `https://bitbots.onrender.com`, so the API can proxy Clerk. The production domain does not send it to the browser. |
| `PORT` | Optional. Render sets this. The process listens on it (default `8080`). |

New users default to the `Europe/Vilnius` timezone. A check-in on the next local day continues the streak. A missed local day resets it to 1 on the next check-in. Checking in again the same local day does not increment.

## Render

The service is a Docker web service. Set the root directory to `OpenWhen`, the branch to `main`, and the health check path to `/health`. Provide `DATABASE_URL`, `CLERK_PUBLISHABLE_KEY`, and `CLERK_SECRET_KEY`. The secret lets `https://bitbots.onrender.com` sign in. `https://streak.me.uk` talks to Clerk directly. No photo, video, or file upload is implemented.

## Local API

```bash
export DATABASE_URL="Host=localhost;Database=openwhen;Username=postgres;Password=postgres"
export CLERK_PUBLISHABLE_KEY="pk_test_..."
dotnet run --project src/OpenWhen.Api
```

The UI dev server proxies `/api` to that process:

```bash
cd web
npm install
npm run dev
```

## Tests

```bash
dotnet test
```

Tests use SQLite and signed test tokens. They do not need Postgres or Clerk.
