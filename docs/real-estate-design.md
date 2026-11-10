# Real Estate — decisions, design and build order

**Status:** M1–M4 built and deployed to dev (2026-10-26): this service at `property-dev.zrnova.com`, stitched as the graph
gateway's `RealEstate` subgraph, the P permission codes in Core, and the app's `@zeronova/real-estate` screens (committed,
not yet pushed). Changes from the draft while building: matching runs inside the save (no queue); site visits use the
requirement codes (PRQV / PRQM) rather than codes of their own; the owner's availability check and the customer's
messages are ready wa.me drafts — automatic WhatsApp template replies are later; a requirement belongs to a lead, picked
from the leads or opened from the lead page. M5 is not started. Designed fresh for ZeroNova from the
dealer's problem and ZeroNova's own services (not from Saarvix).

Input: the property dealer apps research (`zeronova-app/reports/Property dealer apps research.md` — Sell.Do,
LeadSquared, Privyr, FPD, Propertybook, Lofty, Propertybase, Reapit and others; the owner's three sample agreements;
transfer and title rules). The owner's direction: **property dealers first** (local resale and rental dealers), not
builders; the dealer's daily problem is managing properties, finding them, keeping them true, and never losing a
customer who came when the right property was not there yet; agreements must be made ready, **in Word, always
editable**; owners and buyers can each be several people.

Contents: 1 components · 2 decisions · 3 build order · 4 M1 in detail · 5 later phases · 6 open items · 7 projects.

## 1. Components

| Component | Needed for | In ZeroNova today | Decision |
|---|---|---|---|
| The service | Properties, owners, requirements, matching, deals, agreements, rentals | — | **New: `zeronova-real-estate`** (this repo), the same stack as zeronova-study: .NET 10, HotChocolate 16, MongoDB, the ZeroNova packages (MediatR, messaging), Pub/Sub |
| Customers | Buyers and tenants | Leads and contacts (zeronova-lead) | **Use them**: a requirement points at the lead; the customer is never copied into a second CRM |
| Product and permissions | A Real Estate company and its roles | Core T16: product code `REAL_ESTATE` already in the catalog | **Use it**; permission codes join Core's catalog with `Product = REAL_ESTATE` (D14) |
| Photos and papers | Property photos, owner documents, agreements | zeronova-document-helper (GCS, public and private buckets) | **Use it**: photos in the public bucket (share pages), papers in the private one |
| WhatsApp | Sharing properties, availability checks, match alerts to customers, rent reminders | zeronova-social (templates, inbox) | **Use it** (templates like Tutor's alerts) |
| Calling | Owners and buyers | Core's calling device | **Use it** |
| AI | Answering enquiries, qualifying, booking visits, natural-language search | zeronova-office, zeronova-ai | **Use them** (M5) |
| Website | Public listings | zeronova-cms | M5 |
| Word documents | Agreements | — | **New, in this service** (M3) |
| Secrets | Aadhaar and PAN on parties | `ISecretProtector` with the shared `Encryption:Key` | **Use it** (D11) |

## 2. Decisions

| # | Topic | Decision |
|---|---|---|
| D1 | Who it is for | **Property dealers**: resale and rental, residential and commercial, plots and built property. *Changed 2026-11-10 (owner):* **builders and projects are in** — a builder's own projects and a dealer's channel-partner projects (section 7, D17–D26) |
| D2 | Where | **A new service, `zeronova-real-estate`**; GraphQL through the graph gateway as subgraph `RealEstate`; the app's own library `@zeronova/real-estate` and a **Real Estate** sidebar section (like Tutor) |
| D3 | Customers | **Leads stay in zeronova-lead.** A requirement belongs to a lead; "Add requirement" sits on the lead page. A walk-in typed into Real Estate creates the lead first |
| D4 | Property fields | **Three layers:** fields every property has; fields by type (plot, flat, builder floor, house/kothi, villa, shop, office, showroom, warehouse, agricultural land, PG); the **dealer's own fields** (text, number, yes/no, choice) added in settings (section 4.2) |
| D5 | Area units | Size kept **as entered with its unit** (gaj/sq yd, sq ft, sq m, marla, kanal, bigha, biswa, katha, acre, guntha, cent) and converted to sq ft for search. Fixed units are fixed; **marla, kanal, bigha, biswa and katha take the dealer's local value** from settings |
| D6 | Keeping inventory true | Every property has a **status** (available, on hold until a date, under negotiation, token received, sold, rented, withdrawn) and a **last confirmed** date; past the dealer's limit (default 15 days for rent, 30 for sale) it is **stale**: marked in search, never sent to a customer, and put on a "to check" list |
| D7 | Owners | A property has **one or more current owners**, each a **party** with a **share %** (equal by default, total 100%) and how they hold it. Owner phone numbers are seen only with permission (D14) |
| D8 | Owner history | **A chain, optional to fill:** every transfer recorded (from first owners → to second owners, kind, dates, papers); earlier owners entered as far back as the papers go. Built with deals (M3) |
| D9 | Requirements | Saved **for every buyer or tenant, even when nothing matches**, and active until closed, lost or past its date |
| D10 | Matching | **Both ways and automatic:** a new or changed property (price down, back to available) is scored against active requirements; a new or changed requirement against the inventory. Hard filters + score + threshold; the requirement's agent is told; matches already shown or rejected are not offered again (section 4.5) |
| D11 | Identity papers | Aadhaar and PAN are stored **encrypted**, shown **masked** (last four digits), opened only to print an agreement, and only for those allowed to see owners |
| D12 | Agreements | **Word (.docx), always editable** before printing — in the app and by download. Templates in the dealer's own wording (the owner's two formats ship ready); the dealer can upload their own Word template with placeholders. Filled from the deal; amounts in words (lakh/crore), totals, he/she/they, one execution date, checks before printing (M3, section 5) |
| D13 | Brokerage | Per side (buyer, seller/owner, tenant, landlord), with GST and TDS settings (M4) |
| D14 | Permissions | Core codes with `Product = REAL_ESTATE`, prefix **P** (no Core code starts with P): `PPRV`/`PPRM` properties, `POWV` see owners' contacts and papers, `PRQV`/`PRQM` requirements, `PSEM` settings; later `PSVV`/`PSVM` site visits, `PDLV`/`PDLM` deals, `PBRV` brokerage, `PRNV`/`PRNM` rentals |
| D15 | Agent scope | **Every member sees all properties** (the dealer's shared stock); requirements and matches are **the agent's own** unless the member has `PRQV` for all (manager) — the same idea as Tutor's teacher scope |
| D16 | Order | Inventory and matching first (the daily problem); then sharing and site visits; then deals, transfers and agreements; then brokerage and rentals; then website, AI office and portals |

## 3. Build order

| Phase | What | Lives in |
|---|---|---|
| **M1** | **Properties** (all fields, units, photos, custom fields), **owners and co-owners** (parties), **requirements**, **automatic matching** both ways with alerts, **keeping inventory true** (status, stale list, duplicates), search, settings, dashboard | real-estate · Core (permissions) · document-helper · app · graph gateway |
| M2 | **Sharing**: property page link without owner details, who opened it; one-tap WhatsApp to matched customers; **owner availability check on WhatsApp** (reply updates the status); **site visits** (schedule, remind client and owner, feedback, outcome) | real-estate · social · app |
| M3 | **Deals** (offer → token → agreement → transfer → registry), payments received and the balance schedule, **owner transfers and history**, transfer checklist by kind, **agreement builder in Word** | real-estate · app |
| M4 | **Brokerage** (both sides, GST, TDS, received and due, invoice) and **rentals** (tenant, rent, deposit, renewal, rent reminders and receipts, rent agreement) | real-estate · social · app |
| M5 | Public **listing website** from the inventory (zeronova-cms), the **AI office** answering enquiries and booking visits, natural-language search (copilot), **portal leads** (99acres, MagicBricks, Housing) | real-estate · cms · office · ai · lead |

## 4. M1 in detail

### 4.1 Property

| Group | Fields |
|---|---|
| What | Purpose (sale, rent), type (D4 list), title (auto: "3 BHK builder floor, Sector 70") |
| Where | City, locality (sector/colony — from the dealer's locality list, created as typed), society/project, block/pocket/phase, unit or plot number (hidden from those without `POWV`), map pin (optional) |
| Size | Plot size (value + unit), built-up / carpet / super area (value + unit), dimensions (front × depth); sq ft computed (D5) |
| Price | Sale: demand, negotiable, owner's expectation (dealer-only), rate per unit computed. Rent: rent, deposit, maintenance, lock-in, notice |
| Built property | BHK, bathrooms, balconies, floor and total floors (e.g. stilt + 4), lift, parking (covered/open, count), furnishing, age, ready or under construction, possession date, servant/pooja/store/study room, power backup, water, gated, amenities |
| Plot | Facing (N, E, S, W, NE, NW, SE, SW), corner, park facing, facing road width, ownership (freehold, leasehold, GPA), khasra number (land) |
| Commercial | Frontage, washroom, power load, pantry, leased (tenant, rent) or vacant |
| Rent | Available from, preferred tenants (family, bachelors, company), pets |
| Papers | Registry, mutation, loan approved, NOC, OC/CC, property tax paid; documents attached |
| Media | Photos (ordered, a cover), video link, floor plan |
| Source and keys | Source (owner direct, other dealer — name and phone, own), keys with (owner, dealer, guard), visit timings, mandate (open/exclusive, until) |
| Status | D6 statuses, hold-until, last confirmed, who confirmed and how (call, WhatsApp, visit) |
| Dealer's own | Custom fields (D4) |
| Notes | Free text, visible to the team |

### 4.2 Dealer's own fields and settings

- **Custom fields:** name, kind (text, number, yes/no, choice with options), which property types, shown in search
  filters (yes/no and choice).
- **Settings (`PSEM`):** local values of marla, kanal, bigha, biswa, katha; budget tolerance (default 10%); freshness
  days for sale and rent; match threshold; locality list; RERA registration number (printed on shares and agreements
  later).

### 4.3 Owners (parties)

- A **party**: name, S/O, W/O or D/O and the relative's name, phone, WhatsApp, email, address, Aadhaar and PAN
  (encrypted, masked — D11), and the lead/contact it belongs to when there is one.
- A property's **current owners**: party, share % (equal by default; total 100%), holding (builder allotment,
  authority allotment, registered sale deed, GPA, inheritance), title papers.
- The same party can own several properties and later be a buyer; parties are reused in deals and agreements (M3).
- **Contact book** (2026-10-26): `PartyContacts` publishes `contact-upsert-requested` on zeronova-lead's
  `contact-requests` after a party is saved (tag `Property party`), made an owner (`Property owner`, also buyers when a
  sale completes), or put on a deal (`Seller` / `Buyer`, `Landlord` / `Tenant` for rent) or a tenancy (`Landlord` /
  `Tenant`). Only name, phone (or WhatsApp) and email are sent — never Aadhaar, PAN or address. zeronova-lead finds the
  contact by phone or email, else creates it; never a lead. A failure is logged, never fails the save.
- **Won enquiries:** completing a deal sends the buyers (`Buyer` + `Property owner`; `Tenant` for rent) with a
  `WonReason` (`Bought P-0001 (D-0003)`), the deal's lead (or its requirement's) with the first buyer: zeronova-lead
  closes that lead, and each buyer's own open lead, as won. Customers tagged so open no new lead from their messages.

### 4.4 Requirement

- Lead (customer), agent (default: the lead's owner), purpose, property types, localities (several; none = anywhere
  in the city), budget min–max, size min–max (any unit; compared in sq ft), BHK (several), must-haves (corner, park
  facing, facing list, floor ground/top/not top, lift, parking, furnishing, family/bachelor for rent), needed by,
  notes, status (active, on hold, closed — bought/rented through us or elsewhere, lost — with a reason).

### 4.5 Matching

- **Hard filters:** same purpose; type in the requirement's types; locality in its localities (when set); price within
  budget + tolerance; BHK in its list (built property); not stale; status available.
- **Score (0–100):** price position in the budget, size fit, must-haves met, locality preference order, freshness.
  Stored with its reasons ("East facing ✓, corner ✗").
- **When:** on saving a property (new, price changed, back to available, confirmed again) and on saving a requirement;
  worked off the request by a job so saves stay fast.
- **A match** has a status: new, shown (sent or told), interested, rejected (reason), and is never offered again once
  shown or rejected.
- **Told:** the agent sees "new matches" on the dashboard and the property/requirement pages, and gets a bell
  notification (zeronova-utility's dispatch) for matches above the threshold. Sending to the customer on WhatsApp is
  M2.

### 4.6 Keeping inventory true

- **Stale list:** properties past their freshness days, oldest first, with call and WhatsApp buttons and one-tap
  "still available / sold / price changed" (which also re-scores matches).
- **Hold** falls back to available at its date.
- **Duplicates:** a new property is checked against the same society + unit, or the same owner phone + type + size;
  the dealer sees "looks like P-0123" before saving.

### 4.7 Search

Filters on every structured field (purpose, type, locality, price, size, BHK, facing, corner, park facing, floor,
furnishing, status, freshness, custom yes/no and choice fields), text over title, locality, society and notes, sort by
newest, price, size or last confirmed. Natural-language search ("3 bhk sector 70 under 1.2 cr") is M5.

### 4.8 API, data and app

- **Collections:** `re_properties`, `re_parties`, `re_requirements`, `re_matches`, `re_localities`, `re_custom_fields`,
  `re_settings`; company-scoped like study's.
- **GraphQL** (prefix `ZeroNova_estate…`): properties (list, search, get, save, set status, confirm, photos), parties,
  owners, requirements (by lead, list, save, close), matches (for a property, for a requirement, mark shown/rejected),
  stale list, settings, custom fields, dashboard.
- **App:** a Real Estate section — Dashboard, Properties (list with filters; detail with owners, photos, matches,
  history of status), Requirements (list; detail with matches), Settings; on the lead page, "Requirements" and "Add
  requirement".
- **Tests** like study's: Mongo2Go, the real handlers, fakes for outside services.

## 5. Later phases (outline)

- **M2 Sharing and site visits:** a share link per property (photos and details, never the owner or unit number),
  opens tracked; WhatsApp template to send a match; the owner availability check as a WhatsApp template with quick
  replies (1 still available / 2 sold / 3 price changed) whose reply updates the property; site visits with time,
  client, owner, keys, reminders to both, feedback and outcome.
- **M3 Deals, transfers, agreements:** a deal joins a property, its sellers (current owners) and buyers (parties,
  several each side); stages offer → token → agreement → transfer → registry; payments received (mode, UTR/cheque,
  bank, date, payer) and the balance schedule; the transfer checklist by kind (builder flat: no-dues, NOC, transfer
  fee, affidavits, fresh allotment/BBA, conveyance; authority plot (GMADA), in order since C5: NDC (~15 working days) →
  sale permission (~10) → registry at the Sub-Registrar → re-transfer (~5) → mutation (~30); GMADA LOI transfer (NDC →
  LOI transfer, ~21 working days, fee ~2.5% of the allotment price); Chandigarh leasehold (Estate Office permission →
  unearned increase → registry → mutation); Chandigarh freehold (the seller's conveyance deed → registry → mutation);
  registered: registry, mutation, EC); on completion the buyers become the owners and the sellers move into the history.
  **Agreement builder:** the owner's Format A and Format B as ready templates, the dealer's own Word templates with
  placeholders, filled from the deal, checks (every current owner signs, sums add up, dates agree, no duplicate
  UTR, empty fields, cash lines warned; C5: property TDS when the whole deal is **₹50 lakh or more**, on **every
  payment** including the token, under **s.393(1) of the Income-tax Act 2025** from 1 Apr 2026 (s.194-IA before), 1% per
  seller's share or 20% without a PAN, a non-resident seller under s.195 (TAN, Form 27Q) with no rate; FEMA for an NRI /
  OCI buyer of farm land or a farmhouse; GPA gives no title (Suraj Lamp); GMADA steps out of order; Haryana's brokerage
  cap), output Word on legal paper, sent on WhatsApp. C5 adds a stamp duty estimate (state, urban/rural, the buyers'
  genders; rates in the settings). Details: `real-estate-customers-design.md`, "C5 built".
- **M4 Brokerage and rentals:** brokerage per side with GST and TDS, invoice, received and due, staff without `PBRV`
  never see it; rentals with tenant, rent, deposit, start, term, renewal, rent reminders and receipts on WhatsApp, rent
  agreement from a template. C5: brokerage TDS by **who pays** — none from an individual or HUF by default; 2% (20% when
  the dealer has no PAN on record) from a company or firm over ₹20,000 a year, under s.393(1) (Table Sl. 1(ii)) from
  1 Apr 2026 (s.194H before); a property in **Haryana** warns over 1% in all or 0.5% a side. Rentals get **police
  verification** (date, station, reference, the form; a bell once the tenancy starts) and, in **Chandigarh**, the **Rent
  Authority intimation** due 2 months after the agreement (done date, UIN, a bell 15 days before). Leases: a **commercial
  lease** and a **registered lease (over 11 months)** besides the 11-month rent agreement.
- **M5:** listing website, AI office, copilot search, portal leads; co-broking between ZeroNova dealers.

## 6. Open items

1. The repo's remote is `abhiraheja/zeronova-real-estate`; the other services live in `WealthgrowTech` with a mirror to
   `abhiraheja`. Which one is the main repo (CI secrets and the dev deploy live with the main one)?
2. Owner history (D8): optional chain as proposed — confirm.
3. Rentals depth (M4): rent reminders and receipts as proposed, or rent collection online too?
4. Portal leads (M5): reading the portals' lead emails, or waiting for a paid connector?
5. Projects (section 7): check the price list, payment plan and booking form against a real builder's papers
   (WorldPro's Aetro Sapphire) before P2.

## 7. Projects — builders and channel partners (P1–P3)

**Status:** P1 built in this service (2026-11-10; not yet deployed): projects, unit types, generated units, the board
and statuses, versioned price lists and the cost sheet, payment plans and milestones, construction updates, the
project share page, matching by unit type, project site visits and projects on the dashboard, with `PPJM` in Core. The
app's Projects screens are next (the SDL is regenerated). Changes from the draft while building:
- **Legal rules (2026-11-10):** GST % on the unit type, over the project's; an "OC received" date on the project takes
  GST off unsold units; every cost sheet gives the carpet area and the basic and total price per sq ft of carpet
  (RERA prices on carpet area); a payment-plan step can fall due "on agreement for sale", and more than 10% before it
  comes back as a warning on the plan and the schedule (RERA s.13), never a refusal; an own project is shared only with
  a RERA registration or marked "RERA not required" with the reason (RERA s.3). `Farmhouse` joins the property types.
- **Matching:** the match's available count is the units of the type that *fit* the requirement; a stale partner
  project (past the sale freshness days since it was confirmed) is not matched until it is confirmed again.
- **Prices:** the newest price-list version prices the units at once (its effective-from date is recorded, not waited
  for); booked and sold units keep the price they were taken at. A plan's percents are of what is left after its
  fixed amounts and total 100%; the last percent step takes the rounding.
- **Units:** changed only through the project (`saveEstateUnit`, `setEstateUnitStatus`); `saveEstateProperty` and
  `confirmEstateProperty` refuse a unit, and removing one needs `PPJM` and no booking. A unit shows the project's photos
  and its type's floor plan; the dealer's own stock counts on the dashboard leave units out.
- **Media:** like property photos — the app uploads to document-helper's public bucket (PDFs are allowed, up to
  50 MB) and saves the key and address.
- **Added:** `estateProjectShares`, `deleteEstateConstructionUpdate`, `estateVisits(projectId)`; a project link is
  `{BaseUrl}/pj/{token}`.

**Why (2026-11-10):** the first Real Estate website client, WorldPro Township (Chandigarh), is a **builder**: Aetro
Sapphire at Sunam (commercial, RERA PBRERA-SGR78-PC0365, 50 built-up showrooms, 12,487 sq yd), farmhouses, and group
housing / plotted / industrial projects planned. The market research of 2026-11-10 found builder CRMs (Sell.Do,
Farvision, Propacity) built around project → tower → unit inventory, price lists, payment plans and channel partners
(CPs), with about a third of new-home bookings coming through CPs. The owner chose (2026-11-10): **both** a builder's
own projects and a dealer's CP projects in one record; **every unit tracked on its own**; **inventory first**, booking
later.

### 7.1 Decisions

| # | Topic | Decision |
|---|---|---|
| D17 | One record for both | A **project** is either **own** (the company is the builder) or **partner** (another builder's project the company sells as a channel partner: builder name and contact, the CP terms). Everything else is the same |
| D18 | Units are properties | **Every unit is a property** (`re_properties`) with `projectId`, `unitTypeId`, tower, floor and unit number. Share links, site visits, deals, agreements, brokerage and rentals work on a unit unchanged. A unit takes its type's fields (type, BHK, areas, bathrooms, balconies) and the project's (city, locality, society = project name, amenities, construction status, possession date) when made, and keeps them in step when the type or project changes; facing, corner, park facing, road width, floor and notes are its own |
| D19 | Making units | Units are **generated** from a unit type: a tower (or phase/block), floors from–to, units per floor, and a numbering pattern (`{tower}-{floor}{nn}`, `SCO-{n}`, `Plot {n}` from a start number); or added one at a time. A unit number is never made twice in a project |
| D20 | Unit status | The property statuses in builder words: **Available**, **Blocked** (= on hold, for a lead, until a date; the hold sweep releases it), **Booked** (= token received), **Sold**, **Withdrawn**. The **unit board** shows a project's units by tower and floor, coloured by status, with counts |
| D21 | Freshness | A unit is never stale on its own. An **own** project's units are always fresh; a **partner** project is confirmed as a whole ("inventory checked with the builder"), which stamps all its units |
| D22 | Price list | One **price list per project**, in versions (effective from; old versions kept). Per unit type: a **base rate** (per sq ft, per sq yd, or a lump sum) on a chosen area (super, built-up, carpet or plot). **Charges** (PLC, floor rise, EDC/IDC, parking, club, power backup …): fixed, per area, or % of base; mandatory or optional; when it applies (always, corner, park facing, floors from–to, or per floor above N); taxable or not. **GST %** per project, typed in (never assumed). A unit's **cost sheet** = base + its charges + GST; the unit's price (search, matching, shares) is the cost-sheet total unless the unit has its own price. "Price on request" per unit type hides prices on shares |
| D23 | Payment plans | Several **payment plans** per project (construction-linked, down payment, possession-linked, flexi): steps with a label, % of the total or a fixed amount, and when it falls due (on booking, N days after booking, at a construction milestone, on possession). The project's **construction milestones** carry a status and a date. P1 shows a plan's schedule on a unit's cost sheet; P2 raises demands from it |
| D24 | Matching | A requirement is matched against a project's **unit types**, not each unit: one match per requirement per (project, unit type), with the available count, pointing at the best available unit of the type. When that unit is blocked or sold the match moves to the next available one; when none is left it drops. A match shown or rejected is never offered again (as before) |
| D25 | Permissions | Seeing projects, unit types, the board, price lists and payment plans: `PPRV` / `PPRM`. **New `PPJM` Manage projects**: add and edit projects, towers, unit types, generate units, price lists, payment plans, milestones and construction updates. Blocking or booking a unit: `PPRM`. A partner project's CP terms: seen with `PBRV`, edited with `PBRM`. Unit numbers of project units are not owner secrets (seen with `PPRV`); share pages still never show them |
| D26 | What stays out | Website-only copy (FAQ, SEO text, layout) stays in zeronova-cms. The project holds the facts and media the app, the share page and (later) the website read |

### 7.2 The project record

| Group | Fields |
|---|---|
| Identity | Code (`PJ-0001`), name, own / partner. Partner: builder name, builder contact (name, phone, email), CP terms (commission % of agreement value, or an amount per unit type; when it is paid — e.g. on registry, or after 10–20% collected; lead registration validity in days; valid until; notes) |
| Kind and stage | Categories (residential, commercial, mixed use, plotted, farmhouse, industrial, group housing — several), stage (upcoming / pre-launch, new launch, under construction, ready to move, completed), launch date, possession date |
| RERA and approvals | RERA registrations (state, number, registered on, valid until, link — several, for phases); approvals (name, number, date: licence, CLU, environment, fire, OC/CC) |
| Location | City, locality (the dealer's locality list), address, map pin, landmarks with distances (name, km or minutes) |
| Size and figures | Land area (value + unit), towers / blocks / phases (name, floors), key figures (label + value — "201 ft wide front", "50 showrooms") |
| Description | Overview, highlights, amenities, specifications (section + text: structure, flooring, doors, electrical …) |
| Media | Photos (ordered, a cover), video and drone links, **brochure (PDF)**, master / site plan, a floor plan per unit type, **construction updates** (date, text, photos) |
| Sales | Price list (D22), payment plans and milestones (D23), the project's GST % |

A **unit type**: name ("3 BHK Type A", "Ground floor showroom", "250 sq yd plot"), property type, BHK, bathrooms,
balconies, areas (carpet, built-up, super, plot; value + unit), dimensions, floor plan, price on request. Its units'
counts by status are computed.

### 7.3 API (GraphQL, prefix `ZeroNova_estate…`)

- Queries: `estateProjects(search)`, `estateProject(projectId)` (towers, unit types with their counts, milestones,
  updates, current price list, payment plans), `estateProjectUnits(projectId, unitTypeId?, tower?, statuses?)` (the
  board), `estateUnitCostSheet(propertyId, paymentPlanId?)`, `estatePriceListVersions(projectId)`,
  `estateSharedProject(token)` (public).
- Mutations: `saveEstateProject`, `deleteEstateProject` (only with no booked or sold units), `confirmEstateProject`
  (partner inventory checked), `saveEstateUnitType`, `deleteEstateUnitType` (only with no units), `generateEstateUnits`,
  `saveEstateUnit` (number, tower, floor, its own fields, own price), `setEstateUnitStatus` (available / blocked for a
  lead until a date / booked / sold / withdrawn), `saveEstatePriceList` (a new version), `saveEstatePaymentPlan`,
  `deleteEstatePaymentPlan`, `saveEstateMilestones`, `addEstateConstructionUpdate`, `shareEstateProject(projectId,
  requirementId?)`.
- The property list hides project units unless asked (`PropertySearchInput.projectId` or `includeProjectUnits`); a
  site visit can be to a project (`projectId`, unit optional).
- Collections: `re_projects` (towers, milestones, updates inside), `re_unit_types`, `re_price_lists` (versions),
  `re_payment_plans`; units stay in `re_properties`; shares gain `projectId`.

### 7.4 Build order

| Phase | What |
|---|---|
| **P1** | Projects (own and partner), towers, unit types, generated units, the unit board and statuses, the price list with versions and the cost sheet, payment plans and milestones, construction updates, brochure, the project share page, matching by unit type, project site visits, projects on the dashboard; the app's Projects screens; `PPJM` in Core |
| P2 | Primary sale: block → booking (booking amount, applicants, plan chosen) → allotment letter → builder-buyer agreement (Word, like D12) → demands raised from the plan as milestones are reached, receipts, interest on late payment → possession → conveyance / registry; the customer's statement |
| P3 | Channel partners: CP registration (firm, RERA agent number, PAN, GST, KYC), lead registration with validity and first-tag ownership, a CP's own view of inventory and their leads, commission slabs and payout runs with TDS (the M4 engine) |
| Later | Projects on the website (the cms real-estate template), RERA QR on shares where a state requires it, AI answers from the brochure |

### 7.5 Website listings (E2, cms doc 06 §12)

**Status:** built in this service (2026-11-10; not yet deployed). The app's Website card and toggles are E3.
- **Settings (`PSEM`):** `re_websites`, one per company: on/off and a code of letters and digits, unique across companies,
  made on first ask like Tutor's coaching code (six characters without look-alikes; PSEM may choose another, 4–20,
  kept in upper case). `ZeroNova_estateWebsiteSettings` (PSEM, PPRV, PPRM or PPJM; with the counts listed) and
  `ZeroNova_saveEstateWebsiteSettings` (PSEM; null keeps). The admin copies the code into the cms `estateWebsiteCode`.
- **Flags:** `showOnWebsite` and `featured` on `PropertyInput` and `ProjectInput` (null keeps; off by default), and on
  `PropertyDto`, `ProjectDto`, `ProjectSummaryDto` with `websiteSlug`. A project unit is never listed on its own (units
  have no flag; `saveEstateProperty` still refuses them). An own project goes on the website only with its RERA
  registration or marked as needing none — the share rule (RERA s.3).
- **Slugs:** made on the first save and kept when the title or name changes; unique per company (partial unique index).
  A property: the title without its locality, sale or rent, the locality, the city, the code
  (`3-bhk-flat-for-sale-sector-70-mohali-p0001`). A project: its name; with the code when another project has the name
  (`aetro-sapphire-pj0002`). Properties saved before E2 get theirs at their next save (they cannot be flagged without one).
- **Public queries** (anonymous, by code, through the graph gateway like Tutor's shop; a wrong code and a website turned
  off both answer "This website is not available"): `ZeroNova_estateWebsite`, `ZeroNova_estateWebsiteProperties(code,
  filter, sort, page, pageSize)`, `ZeroNova_estateWebsiteProperty(code, slug)`, `ZeroNova_estateWebsiteProjects(code)`,
  `ZeroNova_estateWebsiteProject(code, slug)`. Listed: the flag on, the website on, available or on hold. The design's
  "status" filter is the construction status (ready / under construction); `locality` takes a name or a slug; the text
  search looks only in the title, locality, society, city and code (never notes, block or unit number).
- **A property page** is `Listed`, or `NoLongerAvailable` (sold, rented, withdrawn, taken off the website, removed — the
  title kept only when it had been on the website) with up to four similar listings, or `NotFound`. A project no longer
  listed is null.
- **Unit-type availability:** sold out with none available; **few left** with at most max(3, 10% of the type's units)
  available; available otherwise, also for a type with no units yet. Withdrawn units do not count. The "from" price is
  the lowest price of an available unit (its cost sheet, D22), null when the type's price is on request.
- **Never shown:** owners and their contacts, unit numbers, block, a property's map pin, the owner's expectation, the
  source dealer, keys, mandate, papers, notes, the dealer's own fields, holds, CP terms, the builder's contact, plan
  notes. A project's address and map pin are shown, as on its share page.
