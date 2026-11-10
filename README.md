# Parakeet

Delivery booking platform: a customer portal for booking a delivery slot and a dispatch portal for
planning trucks. The design is in [PLAN.md](PLAN.md).

## Run the backend locally

Needs the .NET 10 SDK and Docker.

```bash
docker compose up -d
```

```bash
dotnet run --project backend/src/Parakeet.Api
```

The API creates the database, adds sample data and listens on http://localhost:5158. Check it with
http://localhost:5158/health.

## Tests

```bash
dotnet test backend
```

The integration tests start their own SQL Server in Docker, so Docker has to be running.
