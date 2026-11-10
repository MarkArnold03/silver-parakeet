# PLAN: Delivery booking platform

Status: **draft, waiting for Mark's ok.** No code until approved.

Two portals on one backend:

- **Customer portal**: mobile first, public, opened from a signed link. View, book or rebook, extras with payment, alternative recipient, sign, track. Swedish and English.
- **Dispatch portal**: desktop first, login. Unplanned list, trucks, drag and drop to routes with live fill, stop order with a map, zones, slots, statuses. Live updates.

Working name in code: `Parakeet` (namespaces, solution). Easy to rename now, painful later, so say if you want something else.

---

## 1. Decisions locked in (from the Q&A)

| # | Decision |
|---|----------|
| 1 | A slot is bookable when it has a free delivery count **and** the zone-day has m3 and kg left. Zone-day capacity is the sum of the trucks assigned to that zone on that date. Only `SellableVolumeRatio` (default 0.85) of m3 is sold. Kg is sold to 100%. |
| 2 | Truck fill: warn above 90%. Kg above 100% is a hard block. M3 above 100% is blocked unless the dispatcher gives an override reason, which is logged. |
| 3 | `Merchant` is an entity with its own API key, stored hashed. `(MerchantId, MerchantOrderRef)` is unique, so a retried POST returns the existing order. |
| 4 | Single company, no multi-tenancy. |
| 5 | Customer link expires 7 days after the delivery date. Rebook cutoff is a setting (default 18:00 the day before). Rebooking a planned delivery unassigns it and notifies dispatch live. |
| 6 | One status endpoint for every change. It records actor type (Dispatcher, Driver, System, Customer) and actor id. Failed needs a reason and goes back to unplanned. |
| 7 | Leaflet + OpenStreetMap. `IGeocoder` with Nominatim behind it, a per-address cache and 1 req/s. If geocoding fails, the order is still created and flagged. |
| 8 | `Route` = truck + date + run number, unique on all three. Run number is always 1 for now. |
| 9 | Money is `long` öre. The price is copied onto the delivery extra when it is added. Stripe Checkout plus an idempotent webhook. |

## 2. Assumptions I made (shout if any are wrong)

- **There is one more status before Booked.** A merchant order arrives without a slot, so it starts as `AwaitingBooking`. The full list is AwaitingBooking, Booked, Planned, Loaded, OutForDelivery, Delivered, Failed.
- **One order means one delivery.** `Order` is the merchant's data (lines, reference, recipient). `Delivery` is the logistics record (slot, route, status, extras, signatures). They are split so a second delivery per order (returns, split shipments) can be added later without a migration.
- **There is no Article catalogue.** Order lines arrive with unit volume and unit weight from the merchant. Delivery totals are calculated from the lines and never typed in. A catalogue can come later if merchants want to send only article numbers.
- **The customer can't cancel** in the first version. The old portal could. Easy to add.
- **Settings live in `appsettings` and environment variables**, through `IOptions`: sellable ratio, cutoff, link validity, warn threshold. There's no settings table or settings UI until someone needs to change them without a deploy.
- **Times:** slots are stored as local `DateOnly` + `TimeOnly` in Europe/Stockholm. Timestamps are UTC `DateTimeOffset`. All "now" logic goes through `TimeProvider` so tests can control the clock.
- **Each truck serves one zone per day.** It can still carry deliveries from other zones if a dispatcher drops them on it (the board warns). Zone capacity only counts trucks assigned to that zone.
- **Removing a truck from a zone-day is allowed even if it drops capacity below what is already sold.** The zone-day then shows as oversold and dispatch sees a warning. Blocking it would leave dispatch stuck when a truck breaks down.

## 3. Domain model

```
Merchant ──< Order ──< OrderLine
               │ 1
               │
               │ 1
            Delivery >── Slot >── Zone ──< PostcodeRange
               │  │                 │
               │  └──< DeliveryExtra >── ExtraService (price list)
               │  └──< Signature
               │  └──< DeliveryEvent  (status timeline + audit)
               │  └──< Payment
               │
               └── Route (truck + date + run) >── Truck
                                                   │
                         ZoneDayTruck (zone, date, truck) ┘
```

### Entities (main fields only)

**Merchant**: `Id`, `Name`, `ApiKeyHash`, `ApiKeyPrefix` (shown in UI to tell keys apart), `IsActive`.

**Order**: `Id`, `MerchantId`, `MerchantOrderRef` (unique with MerchantId), `OrderNumber` (ours, shown to customer), `Recipient` (owned: Name, Phone, Email), `Address` (owned: Street, Postcode, City, Lat?, Lng?, `GeocodeStatus` Pending/Ok/Failed), `DeliveryType` (Curbside, Doorstep), `Lines`, `CreatedAt`.

**OrderLine**: `Id`, `OrderId`, `ArticleNumber`, `Description`, `Quantity`, `UnitVolumeM3` (decimal 9,3), `UnitWeightKg` (decimal 9,2). The order's totals are domain properties summed from the lines.

**Delivery**: `Id`, `OrderId`, `Status`, `SlotId?`, `RouteId?`, `StopSequence?`, `VolumeM3` and `WeightKg` (copied from the order totals when the delivery is created, since lines don't change after that), `AlternativeRecipient?` (owned: Name, Phone), `FailureReason?`, `LinkVersion` (bumped to revoke old links), `RowVersion`.

**DeliveryEvent**: `Id`, `DeliveryId`, `Type` (StatusChanged, Rebooked, Assigned, Unassigned, CapacityOverride, AddressFixed), `FromStatus?`, `ToStatus?`, `ActorType`, `ActorId?`, `Reason?`, `At`. This one table is the customer's tracking timeline (filtered to customer-facing types) and the audit log, including m3 override reasons.

**Zone**: `Id`, `Name`, `PostcodeRanges` (`From`, `To` as 5-digit ints, inclusive). Overlapping ranges across zones are rejected on save.

**Slot**: `Id`, `ZoneId`, `Date`, `Start`, `End`, `MaxDeliveries`, `IsClosed`. The booked count is counted, not stored (see 5.1).

**Truck**: `Id`, `Name`, `Plate`, `CapacityM3`, `CapacityKg`, `IsActive`.

**ZoneDayTruck**: `ZoneId`, `Date`, `TruckId`. Unique on `(TruckId, Date)`, so a truck is in one zone per day.

**Route**: `Id`, `TruckId`, `Date`, `RunNumber` (=1). Unique on `(TruckId, Date, RunNumber)`. Its stops are the deliveries with that `RouteId`, ordered by `StopSequence`.

**ExtraService**: `Id`, `Code` (CarryIn, Assembly, PackagingRemoval), `NameSv`, `NameEn`, `PriceOre`, `IsActive`.

**DeliveryExtra**: `Id`, `DeliveryId`, `ExtraServiceId`, `PriceOre` (copied when added), `Status` (Unpaid, Paid), `PaymentId?`. Unique on `(DeliveryId, ExtraServiceId)`.

**Payment**: `Id`, `DeliveryId`, `AmountOre`, `StripeCheckoutSessionId` (unique), `Status` (Pending, Paid, Expired).

**ProcessedStripeEvent**: `EventId` (PK). The webhook inserts the event id first; a duplicate key means the event was already handled, so it is skipped.

**Signature**: `Id`, `DeliveryId`, `Kind` (Delivery, Waiver), `SignerName`, `Svg`, `SignedAt`. Unique on `(DeliveryId, Kind)`.

**GeocodeCacheEntry**: `AddressKey` (normalized "street|postcode|city", PK), `Lat?`, `Lng?`, `Found`, `At`.

**User** (dispatchers): `Id`, `Email`, `Name`, `PasswordHash` (ASP.NET Core `PasswordHasher<T>` only, not full Identity), `Role` (Dispatcher, Admin).

**RefreshToken**: `Id`, `UserId`, `TokenHash`, `ExpiresAt`, `RevokedAt?`, `ReplacedById?`. Each refresh rotates the token. Reusing a revoked token revokes the whole chain.

### Status machine (domain, unit tested)

```
AwaitingBooking ──book──► Booked ──assign──► Planned ──► Loaded ──► OutForDelivery ──► Delivered
                            ▲  ◄──unassign/rebook──┘                       │
                            │                                              └──► Failed (reason required)
                            └────────────── rebook ◄─────────────────────────────┘
```

- Assign and unassign go through planning, not the status endpoint.
- `Failed` clears `RouteId`, so the delivery shows in the unplanned list (filter Booked + Failed).
- Rebook is allowed from AwaitingBooking, Booked, Planned and Failed, before the cutoff of the **current** slot. The new slot must also be before its own cutoff. Rebooking from Planned unassigns first.
- Signing is allowed from OutForDelivery (delivery signature). The waiver can be signed any time before Delivered.

## 4. Customer links

- The token is a JWT with its own signing key, separate from the dispatch key. Audience `customer`. Claims: `delivery_id`, `lv` (link version). Hard `exp` at 120 days as a backstop.
- On every request the server also checks `now <= (slot date, or created date if no slot) + LinkValidityDaysAfterDelivery`, and that `lv` matches `Delivery.LinkVersion`. So rebooking moves the expiry, and dispatch can revoke a link by regenerating it.
- The link is `https://<customer-app>/b/<token>`. The app keeps the token in memory and sends it as `Authorization: Bearer`, so it isn't sent in query strings to the API.
- Nothing about the delivery comes from the client. Zone, postcode, volume and price are all looked up from the delivery id in the token.

## 5. Core rules

### 5.1 Slot availability and booking (the race-safe part)

For a zone-day:
```
sellableM3 = sum(trucks.CapacityM3) * SellableVolumeRatio
sellableKg = sum(trucks.CapacityKg)
reservedM3/Kg = sum over deliveries with a slot in that zone-day and status not Failed
slot is offered to delivery D if:
  !slot.IsClosed && now < cutoff(slot.Date)
  && countBooked(slot) < slot.MaxDeliveries
  && reservedM3 + D.VolumeM3 <= sellableM3
  && reservedKg + D.WeightKg <= sellableKg
```
The availability calculation is a pure function in Domain, with unit tests for every rule.

**Booking** runs in one transaction. It takes `sp_getapplock` on `zoneday:{zoneId}:{date}` (exclusive, transaction-owned), re-runs the same check against fresh sums, then writes. A rebook locks both zone-days in sorted key order so two rebooks can't deadlock. Two customers fighting over the last slot are serialized: one wins and the other gets `409 SlotFull` and a refreshed slot list. Changing zone-day trucks and slot capacity takes the same lock.

`ponytail:` one lock per zone-day is coarse but correct. Bookings for the same zone and day are rare enough that contention won't matter. If it ever does, switch to conditional UPDATE counters.

### 5.2 Truck fill on the board

`fill(route) = sum of its deliveries' m3 and kg / truck capacity`. Assigning:
- kg after > 100%: `409 OverweightBlocked`, no override.
- m3 after > 100%: `409 OverVolume`, unless `overrideReason` is given, which logs a `CapacityOverride` event.
- either > 90%: allowed, response says `warning: true`.

The delivery's slot date must equal the route date. A delivery from another zone is allowed with a warning. The check runs inside a transaction with an applock on `route:{truckId}:{date}`, so two dispatchers can't overfill the same truck at once.

### 5.3 Live updates (SignalR)

- `/hubs/dispatch` (dispatcher JWT): `DeliveryChanged(deliveryId)`, `RouteChanged(truckId, date)`, `DeliveryUnplanned(deliveryId, reason)`. The client refetches what changed. Messages never carry full objects, so there is one source of truth.
- `/hubs/tracking` (customer token): joins the group `delivery:{id}` and gets `StatusChanged`.
- Events are published after the transaction commits, from the one place that writes `DeliveryEvent`.

## 6. API endpoints

All under `/api`. OpenAPI from .NET 9's built-in `Microsoft.AspNetCore.OpenApi`. Errors are `ProblemDetails` with a stable `code` (e.g. `SlotFull`, `OverweightBlocked`, `CutoffPassed`).

**Auth (dispatch)**
| Method | Path | |
|---|---|---|
| POST | `/auth/login` | email + password → access JWT (15 min) + refresh token (httpOnly cookie, 14 days) |
| POST | `/auth/refresh` | rotates the refresh token |
| POST | `/auth/logout` | revokes it |

**Merchant** (header `X-Api-Key`)
| POST | `/merchant/orders` | create order + delivery, idempotent on `merchantOrderRef`. Returns `orderNumber` + `customerLink`. Same ref with a different payload → 409. |
|---|---|---|
| GET | `/merchant/orders/{merchantOrderRef}` | status + link |

**Customer** (customer token)
| GET | `/customer/delivery` | order number, lines, address, slot, delivery type, extras, alt recipient, status |
|---|---|---|
| GET | `/customer/slots?from&to` | bookable slots for this delivery (empty list = empty state) |
| PUT | `/customer/delivery/slot` | `{ slotId }` book or rebook |
| GET | `/customer/extras` | active price list |
| POST | `/customer/delivery/extras` | `{ extraServiceId }` adds an unpaid extra with the price copied |
| DELETE | `/customer/delivery/extras/{id}` | only if unpaid |
| POST | `/customer/checkout` | Stripe Checkout session for all unpaid extras → `{ url }` |
| PUT / DELETE | `/customer/delivery/alternative-recipient` | `{ name, phone }` |
| POST | `/customer/delivery/signatures` | `{ kind, signerName, svg }` (SVG size-limited and sanitized) |
| GET | `/customer/delivery/tracking` | status timeline |

**Dispatch** (dispatcher JWT)
| GET | `/deliveries?date&zoneId&status&unplanned&needsAddressFix` | list |
|---|---|---|
| GET | `/deliveries/{id}` | detail incl. events |
| POST | `/orders` | manual create (same command as the merchant API) |
| POST | `/deliveries/{id}/status` | `{ status, reason? }`, the single status endpoint. Actor comes from the auth context. |
| PUT | `/deliveries/{id}/address` | fix address, re-geocode |
| POST | `/deliveries/{id}/link` | new customer link (bumps LinkVersion) |
| GET POST PUT | `/trucks`, `/trucks/{id}` | deactivate instead of delete |
| GET POST PUT DELETE | `/zones`, `/zones/{id}` | including postcode ranges |
| GET | `/zone-days?from&to&zoneId` | sellable vs reserved m3/kg per zone-day |
| PUT | `/zone-days/{zoneId}/{date}/trucks` | `{ truckIds }` |
| GET | `/slots?zoneId&from&to` | with booked counts |
| POST | `/slots/generate` | `{ zoneId, from, to, weekdays, windows[], maxDeliveries }`; skips slots that already exist |
| PUT / DELETE | `/slots/{id}` | change max or close; delete only if nothing is booked |
| GET | `/routes?date` | every active truck with its route, stops and fill |
| POST | `/routes/assign` | `{ deliveryId, truckId, date, overrideReason? }` |
| POST | `/routes/unassign` | `{ deliveryId }` |
| PUT | `/routes/{id}/stops` | `{ deliveryIds[] }` new order |
| GET POST PUT | `/extra-services` | price list |
| GET POST | `/merchants`, `/merchants/{id}/api-key` | the key is shown once |

**Webhooks**
| POST | `/webhooks/stripe` | signature verified, idempotent via `ProcessedStripeEvent` |
|---|---|---|

**Hubs**: `/hubs/dispatch`, `/hubs/tracking`.

## 7. Folder structure

```
silver-parakeet/
├── PLAN.md  CLAUDE.md  README.md
├── docker-compose.yml               # SQL Server 2022 for local dev + integration tests
├── backend/
│   ├── Parakeet.sln
│   ├── src/
│   │   ├── Parakeet.Domain/         # entities, value objects, status machine, capacity + fill rules. No EF, no ASP.NET.
│   │   │   ├── Orders/  Deliveries/  Slots/  Zones/  Trucks/  Routes/  Customers/  Payments/
│   │   ├── Parakeet.Application/    # one service per use case area, IAppDbContext, IGeocoder, IPaymentGateway, ICustomerLinks, INotifier
│   │   │   ├── Orders/  Booking/  Planning/  Slots/  Zones/  Trucks/  Tracking/  Payments/  Auth/
│   │   ├── Parakeet.Infrastructure/ # EF Core DbContext + configurations + migrations, Nominatim, Stripe, JWT, applock helper
│   │   └── Parakeet.Api/            # Program.cs, minimal API endpoint groups (one file per area), hubs, auth setup, ProblemDetails mapping
│   └── tests/
│       ├── Parakeet.UnitTests/         # Domain + Application rules, fast, no DB
│       └── Parakeet.IntegrationTests/  # WebApplicationFactory + Testcontainers SQL Server: endpoints, concurrency, migrations
└── frontend/                        # pnpm workspace
    ├── package.json  pnpm-workspace.yaml  tsconfig.base.json
    ├── packages/
    │   ├── api/                     # openapi-typescript types generated from the API's OpenAPI + openapi-fetch client + SignalR helpers
    │   └── ui/                      # tokens (teal theme), Card, Button, BottomNav, FillBar, StatusTimeline, SignaturePad, EmptyState, i18n (sv/en)
    └── apps/
        ├── customer/                # Vite + React, mobile first: /b/:token → Home, Plan, Extras, Sign, Track
        └── dispatch/                # Vite + React, desktop first: Login, Board, Deliveries, Trucks, Zones & Slots, Extras, Merchants
```

**Stack choices** (the ponytail ladder, each one picked for a reason):
- **Minimal APIs grouped per area instead of controllers.** About 10 small files instead of 90 controllers, and the OpenAPI output is just as good.
- **No MediatR, AutoMapper or repositories.** Application services use `IAppDbContext` directly, and DTO mapping is hand-written records.
- **No FluentValidation.** Request records use data annotations, and domain invariants throw `DomainException`, which is mapped to ProblemDetails in one place.
- **Frontend:** React Router, TanStack Query (caching plus refetch when a SignalR event comes in), `openapi-fetch` (about 6 kB typed client), Leaflet / react-leaflet, `@microsoft/signalr`, `@stripe/stripe-js` only for the redirect. Drag and drop is native HTML5 plus an "Assign to…" menu for keyboard users. The signature pad is our own canvas component (about 60 lines, exports SVG). i18n is a typed sv/en dictionary in `ui`, no library.
- **Tests:** xUnit + Testcontainers on the backend, Vitest + Testing Library on the frontend for components with logic (fill bar thresholds, slot picker empty state, signature pad).
- **CI:** one GitHub Actions workflow for backend build + tests (Testcontainers works there) and frontend typecheck + tests + build. It also checks that the generated API client matches the OpenAPI spec.

## 8. Vertical slices

Each slice ends with: all tests green, a short report to Mark (what works, what's left), small commits on `claude/project-thread-kmkrdn`.

1. **Domain and database.** Solution skeleton, Domain entities + status machine, EF configurations, first migration, docker-compose SQL Server, dev seeder (dev only, behind an environment check), CLAUDE.md.
   *Tests:* status transitions, order totals from lines, postcode range overlap and lookup, migration applies to an empty SQL Server.
2. **Slot availability with capacity.** Zones, postcode ranges, trucks, zone-day trucks, slot generation, availability calculation, the booking transaction with applock, settings for ratio and cutoff. Dispatch endpoints for those resources, dispatch auth (login, refresh, rotation).
   *Tests:* each availability rule (count, m3 at 85%, kg at 100%, closed, cutoff, wrong zone), **parallel booking of the last slot: exactly one wins**, rebook releases the old reservation, refresh token reuse revokes the chain.
3. **Customer booking flow.** Merchant API with idempotency, customer links, geocoding behind `IGeocoder` with cache and rate limit, customer endpoints, OpenAPI → `packages/api`, `packages/ui` basics, customer app Home + Plan in sv/en with an empty state.
   *Tests:* idempotent POST (same ref twice gives one order; different payload gives 409), expired or revoked link gives 401, geocode failure still creates the order and flags it, slot picker empty state.
4. **Dispatch planning board with truck fill.** Unplanned list with filters, routes, assign/unassign/reorder with fill rules and override logging, map, SignalR dispatch hub, rebook-from-planned unassigns + live notify.
   *Tests:* kg block, m3 block + override logged, 90% warning, date mismatch rejected, two dispatchers filling the same truck concurrently, rebook of a planned delivery lands in unplanned with an event.
5. **Signing and tracking.** Single status endpoint with actor recording, Failed back to unplanned, signatures (delivery + waiver), tracking timeline, tracking hub, customer Sign + Track pages, alternative recipient.
   *Tests:* illegal transitions rejected, Failed without a reason rejected, signature only in the right status, SVG sanitizing, timeline shows only customer-facing events.
6. **Payments.** Extra services price list, add/remove extras with a price snapshot, Stripe Checkout, webhook signature + idempotency, paid extras visible on the board.
   *Tests:* price change after adding doesn't affect the extra, the same webhook event twice is handled once, the amount is computed server side only, a paid extra can't be removed.

## 9. Out of scope for now (named so they aren't forgotten)

SMS/email sending of links and notifications, driver page (the status endpoint is ready for it), automatic route optimisation, several runs per truck, customer cancellation, Article catalogue, refunds, multi-tenancy, a settings UI.
