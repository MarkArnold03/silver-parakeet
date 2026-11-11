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

## Run the customer app locally

Needs Node 22 and pnpm (`npm install -g pnpm`). Start the backend first.

```bash
cd frontend
```

```bash
pnpm install
```

```bash
pnpm dev:customer
```

The app needs a customer link to show anything. Create an order the way a shop would, with the sample
shop's key (exists only in a local development database). In PowerShell, use `curl.exe` instead of `curl`.

```bash
curl -H "X-Api-Key: pk_dev_parakeet_local_only" -H "content-type: application/json" -d @docs/sample-order.json http://localhost:5158/api/merchant/orders
```

Open the `customerLink` from the answer in a browser (http://localhost:5173/b/...). Change `merchantOrderRef`
in `docs/sample-order.json` to create another order; sending the same file twice returns the same order.

## Tests

```bash
dotnet test backend
```

The integration tests start their own SQL Server in Docker, so Docker has to be running.

```bash
cd frontend
```

```bash
pnpm test
```

## When the API changes

`dotnet build` rewrites `frontend/packages/api/openapi.json`. Then run `pnpm gen:api` in `frontend/` to
update the TypeScript types, and commit both files. CI fails if either is out of date.
