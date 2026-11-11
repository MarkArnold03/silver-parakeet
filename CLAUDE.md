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
  - `Parakeet.Application`: one service per area (`BookingService`, `SlotService`, `ZoneService`, `TruckService`, `AuthService`,
    `OrderService`, `MerchantService`, `CustomerDeliveryService`, `GeocodingService`), `IAppDbContext`, and the interfaces
    Infrastructure implements (`IGeocoder`, `ICustomerLinks`, `IAccessTokenIssuer`).
  - `Parakeet.Infrastructure`: EF Core `AppDbContext`, table mapping in `Persistence/ModelConfiguration.cs`, migrations, `DevSeeder`, `NominatimGeocoder`, JWT issuers.
  - `Parakeet.Api`: `Program.cs`, `Endpoints/` (minimal APIs), `ErrorHandling.cs`.
- `frontend/`: pnpm workspace. TypeScript 5 (openapi-typescript does not support 7 yet).
  - `packages/api`: `openapi.json` (written by `dotnet build`), `src/schema.d.ts` (written by `pnpm gen:api`), and the typed client. Never edit the two generated files by hand.
  - `packages/ui`: `tokens.css` (teal theme), Button, Card, EmptyState, BottomNav, `createI18n` (typed sv/en dictionary).
  - `apps/customer`: Vite + React, mobile first. `/b/:token` Home, `/b/:token/plan` Plan. Texts in `src/texts.ts`.
  - `apps/dispatch`: not started yet (slice 4).

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

Run from `frontend/`:

- Install: `pnpm install`
- Customer app: `pnpm dev:customer` (http://localhost:5173, passes `/api` on to the backend on 5158)
- Checks: `pnpm typecheck`, `pnpm test`, `pnpm build`
- After any API change: `dotnet build` in `backend/`, then `pnpm gen:api`, and commit `openapi.json` and `schema.d.ts`.
- A customer link for local testing: see "Run the customer app locally" in README.md.

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
- Settings live in `appsettings.json` sections `Booking`, `Auth`, `CustomerLinks` and `Geocoding`, read through
  `IOptions`. Secrets (`Auth:JwtSigningKey`, `CustomerLinks:SigningKey`, connection string) come from environment
  variables outside Development.
- Integration tests: `Scenario` builds data for service-level tests, `ApiTestBase` starts the real API in memory.
  A new race test must be shown to fail with the lock switched off before it is trusted.
- A slice is not done until CI is green on the pushed commit. Report the CI result to Mark.
- Three kinds of caller, three kinds of proof: dispatchers send a JWT (default scheme), customers send the token from
  their link (scheme and policy `Customer`, own signing key), merchants send `X-Api-Key`. Customer endpoints take
  nothing about the delivery from the client; `CustomerDeliveryService` checks link version and expiry on every call.
- Anything that calls an outside service sits behind an interface in Application and is replaced by a fake in tests
  (`FakeGeocoder` in `ApiTestBase`). Tests never use the internet.
- Program.cs skips the database parts when `writingApiDescription` is true (the build step that writes openapi.json).
- Every text the customer sees goes in `apps/customer/src/texts.ts`, in both Swedish and English.
