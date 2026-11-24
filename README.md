# BookManager

Monorepo with:

- backend API: `BookManager/` (`BookmanagerAPI`, ASP.NET Core + EF Core + PostgreSQL)
- frontend app: `Bookmanager-frontend/` (Vue 3 + Vite + PWA)

## Requirements

- .NET SDK 10
- Node.js `^20.19.0` or `>=22.12.0`
- PostgreSQL (default local connection in `BookManager/appsettings.json`)

## Run Backend

```powershell
cd BookManager
dotnet restore
dotnet run --launch-profile https
```

Default URLs:

- `https://localhost:7026`
- `http://localhost:5127`

Swagger (Development): `https://localhost:7026/swagger`

## Run Frontend

```powershell
cd Bookmanager-frontend
npm install
npm run dev
```

Set API base in `Bookmanager-frontend/.env`:

```env
VITE_API_BASE_URL=https://localhost:7026
```

## Build

Backend:

```powershell
cd BookManager
dotnet build
```

Frontend:

```powershell
cd Bookmanager-frontend
npm run build
```

## Notes

- Frontend docs: `Bookmanager-frontend/README.md`
- Backend auto-runs some schema safety SQL in `BookManager/Program.cs` (e.g. `IF NOT EXISTS` changes).
- Current `EmailService` uses SMTP credentials in code; move them to environment variables before production use.

## Troubleshooting

- Port already in use (`5127` or `7026`): stop the existing API process or run with a different launch profile/URL.
- `ERR_BLOCKED_BY_CLIENT` on login/API calls: disable ad/privacy extension for localhost or whitelist the API origin.
