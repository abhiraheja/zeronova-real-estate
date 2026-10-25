# zeronova-real-estate — orientation (read first)

ZeroNova's **Tutor** industry (Core topic T17): coaching institutes run courses, students, a question bank, tests,
test series, attempts and live quizzes. Ported from Saarvix **Saar-Study-Plus**
(`D:\AVDesignWorks\SaarvixV2\Saar-Study-Plus`, read-only) and its web screens
(`D:\AVDesignWorks\SaarvixV3\Saarvix-Aura-Frontend-V3\projects\study-plus`). Plan, decisions and slices: Core
`../zeronova-core/docs/migration/topics/T17-tutor.md`. Design here: [docs/study-service.md](docs/study-service.md).

Stack: .NET 10 · HotChocolate 16 GraphQL (`ZeroNova_tutor…` fields) · MongoDB (own database, Core's cluster) ·
Supabase JWT (same token and claims as Core) · Pub/Sub via ZeroNova_Packages.Messaging · Google Secret Manager. The
skeleton is zeronova-email's; Core's `zeronova-backend` skill applies where it fits.

## Layout

```
ZeroNovaRealEstate.Models          leaf: options, claim + permission + product names, DTOs/inputs, enums
ZeroNovaRealEstate.Domain          documents ([DocumentName], `tutor_` prefix), BSON conventions
ZeroNovaRealEstate.Application     features (MediatR), repository interfaces
ZeroNovaRealEstate.Infrastructure  repositories, Mongo, indexes, request context
ZeroNovaRealEstate.PubSub          producers + consumers (none yet; Core's company-events next)
ZeroNovaRealEstate.Api             Program, auth, Secret Manager, GraphQL resolvers
ZeroNovaRealEstate.Tests           xUnit; Mongo2Go (a real local mongod), the schema, the authorization gate
```

## Working agreements

- **The whole service is the Tutor industry.** Every staff operation is `[RequirePermission(TutorPermissions.…)]`, and
  that policy also requires `TUTOR` in the token's `products` claim — the admin's `*` grants codes, never an industry.
  Never add a staff field without it.
- **Permission codes live in Core's catalog** (`ZeroNovaCore.Models/Permissions/TutorPermissions.cs`, `Product = TUTOR`)
  and are mirrored in `TutorPermissions` here. Add a code in both, in the same change.
- **Students are not ZeroNova users** (T17 P3): they get their own token from this service (slice 3). Never make a
  student a company member or a Supabase user.
- Field names carry `ZeroNova_` and say Tutor (`ZeroNova_tutorCourses`), so the graph gateway stitches without clashes.
- Collections carry the `tutor_` prefix. Counters (`studentCount`, `testCount`) move with the write that changes them,
  never by a recount.
- Logging/OTel, rate limiting and CI/CD are topic **T00b, owned by the user**. Secrets only in Google Secret Manager.
- Reply to the owner in Hinglish; code, docs and commit messages in English.

## Run locally

```bash
gcloud auth application-default login          # once per machine
dotnet run --project ZeroNovaRealEstate.Api           # http://localhost:5130/graphql
dotnet test ZeroNovaRealEstate.Tests                  # starts its own throwaway mongod, no secrets needed
docker compose up --build -d                     # same, in Docker
```
