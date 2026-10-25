# zeronova-real-estate — orientation (read first)

The **Real Estate industry** of ZeroNova (Core T18, product `REAL_ESTATE`): the property dealer's stock, owners,
customers' requirements and the matching between them — later site visits, deals, owner transfers, agreements in Word,
brokerage and rentals. Built fresh for ZeroNova (not ported from Saarvix). Product overview: `../zeronova-core/CLAUDE.md`.

Design, decisions (D1–D16) and build order (M1–M5): [docs/real-estate-design.md](docs/real-estate-design.md). What is
built and its API: [docs/real-estate-service.md](docs/real-estate-service.md). Research behind it:
`../zeronova-app/reports/Property dealer apps research.md`.

## Stack

.NET 10 · HotChocolate 16 · MongoDB · the ZeroNova packages (MediatR, messaging/outbox, security). Same skeleton as
zeronova-study.

## Layout

```
ZeroNovaRealEstate.Api/            Program, Supabase auth, [RequirePermission], GraphQL (Graphql/EstateResolvers.cs)
ZeroNovaRealEstate.Application/    Features/{Properties,Parties,Requirements,Matching,Settings,Common}, interfaces
ZeroNovaRealEstate.Domain/         Documents (re_* collections)
ZeroNovaRealEstate.Infrastructure/ repositories, indexes, the hold sweep, installers
ZeroNovaRealEstate.Models/         DTOs, inputs, enums, permissions (EstatePermissions), options, events
ZeroNovaRealEstate.PubSub/         producers (agents' bells via zeronova-utility's dispatch)
ZeroNovaRealEstate.Tests/          xUnit + Mongo2Go, the real handlers
```

## Rules

- **Every field needs the Real Estate industry** (`products` claim has `REAL_ESTATE`) and a `P…` permission
  (`EstatePermissions`). Owners' details only with `POWV`; requirements and matches are an agent's own unless `PRQV`.
- **Customers stay in zeronova-lead.** A requirement points at the lead; never copy the CRM here.
- **Company-scoped repositories.** The `companyId` overloads only for jobs with no caller; a cross-tenant read only in a
  named repository method (`HoldsEndedAsync`).
- **Matching lives in `MatchEngine`** and runs on every property or requirement save, confirm and hold release. A
  match shown, interested or rejected is history and is never offered again.
- **Aadhaar and PAN are sealed** with the shared `Encryption:Key`; only the last four are kept in clear. A missing or
  placeholder key keeps the service up and refuses only the identity numbers.
- Money and sizes are `Decimal128` in Mongo (range queries). Sizes keep their unit and a sq ft value.
- Logging/OTel, rate limiting, Docker/CI deploy are the owner's (T00b). Don't add them ad hoc.
- Reply to the owner in Hinglish; write code, docs and commit messages in English.

## Run locally

```bash
dotnet test ZeroNovaRealEstate.slnx
dotnet run --project ZeroNovaRealEstate.Api
```
