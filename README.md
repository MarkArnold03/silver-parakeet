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

Log in as the sample dispatcher (exists only in a local development database):

```bash
curl -H "content-type: application/json" -d '{"email":"dispatcher@parakeet.local","password":"parakeet-dev"}' http://localhost:5158/api/auth/login
```

The list of endpoints is at http://localhost:5158/openapi/v1.json.

If the database was created by an older version and the API fails on start, reset it with
`docker compose down -v` and start again.

## Tests

```bash
dotnet test backend
```

The integration tests start their own SQL Server in Docker, so Docker has to be running.
