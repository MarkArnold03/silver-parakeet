# CLAUDE.md

Project plan: see [PLAN.md](PLAN.md). No code before Mark has approved the plan.

## Working rules: Mark is learning while we build

Mark wants to understand every step. In every session:

- Before you run a command or create a file, tell me in one or two plain sentences what it does and why we need it.
- When you use a pattern or tool I might not know (applock, Testcontainers, owned types, minimal APIs, TanStack Query and so on), explain it briefly the first time it shows up, with why you picked it over the obvious alternative.
- After each slice, give me a short "what you should understand now" list and 2 or 3 questions I can try to answer to check that I got it.
- Keep it short and plain. No lectures. If I want more depth I'll ask.
- If I ask "why", explain before continuing, don't just carry on.

## Project layout

- `backend/`: .NET 10 solution `Parakeet.sln`. Dependencies point inward: Api → Infrastructure → Application → Domain.
  - `Parakeet.Domain`: entities and rules. No EF, no ASP.NET. Rules are tested here without a database.
  - `Parakeet.Application`: one service per area (`BookingService`, `SlotService`, `ZoneService`, `TruckService`, `AuthService`), `IAppDbContext`.
  - `Parakeet.Infrastructure`: EF Core `AppDbContext`, table mapping in `Persistence/ModelConfiguration.cs`, migrations, `DevSeeder`.
  - `Parakeet.Api`: `Program.cs`, `Endpoints/` (minimal APIs), `ErrorHandling.cs`.
- `frontend/`: not started yet (slice 3).

## Commands

Run from `backend/`:

- Build: `dotnet build`
- Unit tests (fast, no Docker): `dotnet test tests/Parakeet.UnitTests`
- Integration tests (need Docker, start their own SQL Server): `dotnet test tests/Parakeet.IntegrationTests`
- New migration after changing an entity or its mapping:
  `dotnet ef migrations add <Name> -p src/Parakeet.Infrastructure -o Persistence/Migrations`
- Run locally: `docker compose up -d` in the repo root, then `dotnet run --project src/Parakeet.Api`.
  In Development the API migrates the database and seeds sample data on start.
  Dev login: `dispatcher@parakeet.local` / `parakeet-dev` (created by `DevSeeder`, Development only).

In a Claude Code cloud session the Docker daemon is not started automatically. Start it with `dockerd` as a
background process before running integration tests. The .NET SDK comes from `apt-get install dotnet-sdk-10.0`.

## Conventions

- Every status change on a delivery goes through a method on `Delivery`, which also writes the `DeliveryEvent`.
- Broken business rules throw `DomainException` with a stable `Code`.
- Entity ids are Guid v7 created in constructors. Enums are stored as strings. Money is `long` öre.
- "Now" always comes from `TimeProvider`, never `DateTime.Now`.
- Foreign keys never cascade deletes, except owned data (a zone's postcode ranges).
- Exceptions decide the HTTP status in one place (`Parakeet.Api/ErrorHandling.cs`): `NotFoundException` 404,
  `ConflictException` 409, `UnauthorizedException` 401, any other `DomainException` 400.
- Anything that checks zone-day capacity and then writes (booking, slot changes, zone-day trucks) runs inside
  `BeginLockedTransactionAsync([LockKeys.ZoneDay(zoneId, date)])`. Read the data you check after taking the lock.
- Settings live in `appsettings.json` sections `Booking` and `Auth`, read through `IOptions`. Secrets
  (`Auth:JwtSigningKey`, connection string) come from environment variables outside Development.
- Integration tests: `Scenario` builds data for service-level tests, `ApiTestBase` starts the real API in memory.
  A new race test must be shown to fail with the lock switched off before it is trusted.
- A slice is not done until CI is green on the pushed commit. Report the CI result to Mark.
