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

Contents: 1 components · 2 decisions · 3 build order · 4 M1 in detail · 5 later phases · 6 open items.

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
| D1 | Who it is for | **Property dealers**: resale and rental, residential and commercial, plots and built property. Builders, project inventory and channel partners are out |
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
  fee, affidavits, fresh allotment/BBA, conveyance; authority plot: NDC, NOC, re-transfer, conveyance; registered:
  registry, mutation, EC); on completion the buyers become the owners and the sellers move into the history.
  **Agreement builder:** the owner's Format A and Format B as ready templates, the dealer's own Word templates with
  placeholders, filled from the deal, checks (every current owner signs, sums add up, dates agree, no duplicate
  UTR, empty fields, 1% TDS on the full price above ₹50 lakh, cash lines warned), output Word on legal paper, sent on
  WhatsApp.
- **M4 Brokerage and rentals:** brokerage per side with GST and TDS, invoice, received and due, staff without `PBRV`
  never see it; rentals with tenant, rent, deposit, start, term, renewal, rent reminders and receipts on WhatsApp, rent
  agreement from a template.
- **M5:** listing website, AI office, copilot search, portal leads; co-broking between ZeroNova dealers.

## 6. Open items

1. The repo's remote is `abhiraheja/zeronova-real-estate`; the other services live in `WealthgrowTech` with a mirror to
   `abhiraheja`. Which one is the main repo (CI secrets and the dev deploy live with the main one)?
2. Owner history (D8): optional chain as proposed — confirm.
3. Rentals depth (M4): rent reminders and receipts as proposed, or rent collection online too?
4. Portal leads (M5): reading the portals' lead emails, or waiting for a paid connector?
