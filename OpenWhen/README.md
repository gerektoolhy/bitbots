# OpenWhen

A signed-in person starts a streak (a habit name and a target length in days) and gets a join code. Someone else joins with that code. Each member checks in once per calendar day in their own timezone. A member can leave a text message for one other member. The recipient can see that a message is waiting, and can read it only after their current streak reaches the target. If they were allowed to read it, a later missed day does not hide it again.

The browser app is Clerk (Google sign-in is turned on in the Clerk dashboard). The API checks the Clerk session token on every protected call and uses the token subject as the user id.

ASP.NET Core serves the built React app and the API in one process. `GET /health` returns 200 and does not touch the database. On startup the API applies EF Core migrations to Postgres.

## Environment variables

The app reads these and nothing else for secrets. Do not commit values.

| Variable | Purpose |
| --- | --- |
| `DATABASE_URL` | Postgres connection string. A Render-style `postgres://USER:PASSWORD@HOST:5432/DATABASE` URL is accepted, as is an Npgsql key/value string. |
| `CLERK_PUBLISHABLE_KEY` | Clerk publishable key (`pk_...`). The browser loads it from `GET /api/config`. |
| `CLERK_SECRET_KEY` | Clerk secret key (`sk_...`). The API uses it to fetch the instance JWKS and verify session tokens. |
| `PORT` | Optional. Render sets this. The process listens on it (default `8080`). |

New users default to the `Europe/Vilnius` timezone. A check-in on the next local day continues the streak. A missed local day resets it to 1 on the next check-in. Checking in again the same local day does not increment.

## Render

The service is a Docker web service. Set the root directory to `OpenWhen`, the branch to `main`, and the health check path to `/health`. Provide `DATABASE_URL`, `CLERK_PUBLISHABLE_KEY`, and `CLERK_SECRET_KEY`. No photo, video, or file upload is implemented.

## Local API

```bash
export DATABASE_URL="Host=localhost;Database=openwhen;Username=postgres;Password=postgres"
export CLERK_PUBLISHABLE_KEY="pk_test_..."
export CLERK_SECRET_KEY="sk_test_..."
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
