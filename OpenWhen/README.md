# OpenWhen

Hobby hello-world: a React (Vite) page and an ASP.NET Core minimal API in one process. The API serves the built page, `GET /api/hello`, and `GET /health`. There is no database.

`GET /api/hello` returns `{"message":"Hello from OpenWhen"}`. `GET /health` returns 200 and does not touch a database.

Requires the .NET 10 SDK and Node.js 22.

## Run locally

From `OpenWhen/`:

```bash
cd client
npm install
npm run build
cd ../src/OpenWhen.Api
dotnet run
```

Open http://localhost:5080. The page loads the hello message from the API.

For frontend-only reload while editing, start the API, then in another terminal:

```bash
cd client
npm run dev
```

Vite proxies `/api` and `/health` to http://localhost:5080.

Run the API test:

```bash
dotnet test
```

## Deploy on Render (free web service)

Do this in the Render dashboard. A free web service sleeps after idle; the first request after sleep can take a minute.

1. Push this repo to GitHub.
2. In Render, create a **Web Service** and connect the repository.
3. Set the root directory to `OpenWhen` so the build context is this folder.
4. Choose **Docker**. Render uses `OpenWhen/Dockerfile`.
5. Leave the instance type on the free web service. No environment variables or secrets are required.
6. Set the health check path to `/health`.
7. Deploy. Render sets `PORT`; the app listens on that port when it is present. Locally and in the image default, it listens on 8080 unless `launchSettings.json` or `PORT` says otherwise.

The service is one container. It does not use a database or a paid add-on.
