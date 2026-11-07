# Nyhems Trafikskola – Backend

REST API for the Nyhems Trafikskola (driving school) website: accounts and sign-in, Riskettan bookings for students, and course session administration for staff.

The frontend lives in a separate repository: [NyhemsTrafikskola-Frontend](https://github.com/KristinaK993/NyhemsTrafikskola-Frontend).

> **Swedish terms:** *Riskettan* and *Risktvåan* are the mandatory risk education courses (part 1 and part 2) for a Swedish driving licence, offered for car (*Bil*) and motorcycle (*MC*). User-facing messages returned by the API are in Swedish.

## Tech stack

- .NET 8 / ASP.NET Core Web API
- Entity Framework Core 8 with SQL Server
- ASP.NET Core Identity with cookie authentication and antiforgery (CSRF) protection
- NUnit tests (integration tests against SQLite via `WebApplicationFactory`)
- Swagger in development

## Project structure

```
NyhemsTrafikskola/
├── NyhemsTrafikskola.sln
├── NyhemsTrafikskola.API/            Controllers, Program.cs, configuration
├── NyhemsTrafikskola.Application/    Interfaces, DTOs, roles, validation (e.g. Swedish phone numbers)
├── NyhemsTrafikskola.Domain/         Entities: Course, CourseSession, CourseBooking, StudentProfile …
├── NyhemsTrafikskola.Infrastructure/ EF Core (ApplicationDbContext, migrations), Identity, services
└── NyhemsTrafikskola.Tests/          NUnit tests
```

## Getting started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A local SQL Server instance (e.g. SQL Server Express)
- The EF Core tool: `dotnet tool install --global dotnet-ef`
- A trusted development certificate: `dotnet dev-certs https --trust`

### 1. Connection string

The API reads `ConnectionStrings:DefaultConnection`. Point it at your own local server, preferably with user secrets so nothing machine-specific gets committed:

```bash
cd NyhemsTrafikskola/NyhemsTrafikskola.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=.\\SQLEXPRESS;Database=NyhemsTrafikskolaDb;Trusted_Connection=True;TrustServerCertificate=True;"
```

Alternatively use `appsettings.Development.json` (ignored by git) or the environment variable `ConnectionStrings__DefaultConnection`.

### 2. Create the database

```bash
cd NyhemsTrafikskola
dotnet ef database update --project NyhemsTrafikskola.Infrastructure --startup-project NyhemsTrafikskola.API
```

> Take a backup before running migrations against a database that contains real data.

### 3. Create the first administrator

The roles (`Student`, `Instructor`, `Admin`) are created automatically when the API starts. The first admin user is created interactively from a local terminal:

```bash
dotnet run --project NyhemsTrafikskola.API -- --create-first-admin
```

The command does nothing if an administrator already exists.

### 4. Run the API

```bash
dotnet run --project NyhemsTrafikskola.API --launch-profile https
```

- API: `https://localhost:7181` (http: `http://localhost:5055`)
- Swagger: `https://localhost:7181/swagger`

## Configuration

| Key | Description |
| --- | --- |
| `ConnectionStrings:DefaultConnection` | SQL Server connection (required; the API will not start without it) |
| `Cors:AllowedOrigins` | Allowed frontend origins. Default: `localhost:3000` and `localhost:5173` (http/https) |

Cookies are issued with `SameSite=None` and `Secure`, so both the frontend and the API must run over **https** for sign-in to work.

## Security overview

- Sign-in uses the HttpOnly cookie `.NyhemsTrafikskola.Auth`. Unauthenticated requests get `401`, missing permissions get `403`.
- All write requests (POST/PUT/PATCH/DELETE) from signed-in users require an antiforgery token in the `X-XSRF-TOKEN` header. The token is returned in the same header by the auth endpoints.
- Passwords: at least 8 characters with an uppercase letter, a lowercase letter and a digit. Accounts are locked for 5 minutes after 5 failed attempts.
- Validation errors are returned as `{ "message": "...", "field": "..." }` with Swedish messages.

## API overview

| Method | Path | Access | Description |
| --- | --- | --- | --- |
| POST | `/api/auth/register` | Anyone | Register a student account |
| POST | `/api/auth/login` | Anyone | Sign in |
| POST | `/api/auth/logout` | Signed in | Sign out |
| GET | `/api/auth/me` | Signed in | Current user and roles |
| GET | `/api/course-sessions?courseType=RiskettanBil\|RiskettanMC` | Anyone | Published course sessions with available seats |
| POST | `/api/course-sessions/{id}/bookings` | Student | Book a seat (`{ "termsAccepted": true }`) |
| GET | `/api/student/bookings` | Student | My bookings |
| POST | `/api/student/bookings/{id}/cancel` | Student | Cancel a booking (no later than 48 h before start) |
| GET | `/api/admin/courses` | Instructor/Admin | Courses |
| GET/POST | `/api/admin/course-sessions` | Instructor/Admin | List/create course sessions |
| GET/PUT/DELETE | `/api/admin/course-sessions/{id}` | Instructor/Admin | View/update/delete a session |
| POST | `/api/admin/course-sessions/{id}/publish` · `/unpublish` · `/cancel` · `/archive` | Instructor/Admin | Change session status |
| POST | `/api/admin/course-sessions/{id}/bookings` | Instructor/Admin | Manually book a student |
| POST | `/api/admin/course-sessions/{id}/bookings/{bookingId}/cancel` | Instructor/Admin | Cancel a student's booking |

See Swagger for the full request/response models.

### Booking terms

`BookingTerms.CurrentVersion` in `NyhemsTrafikskola.Application/Bookings/BookingTerms.cs` must match `BOOKING_TERMS_VERSION` in the frontend's `src/api/courseSessionsApi.ts`. Update both whenever the terms text changes.

## Migrations

Add a new migration:

```bash
cd NyhemsTrafikskola
dotnet ef migrations add <Name> --project NyhemsTrafikskola.Infrastructure --startup-project NyhemsTrafikskola.API
```

## Tests

```bash
dotnet test NyhemsTrafikskola/NyhemsTrafikskola.sln
```

GitHub Actions (`.github/workflows/backend-ci.yml`) builds the solution and runs the tests on pushes and pull requests to `main`.
