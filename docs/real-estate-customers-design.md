# Real Estate — enquiries, customers, unmet demand, video and agreement fixes (section 8)

**Status:** design, 2026-11-10. Continues `real-estate-design.md` (D1–D26) with D27–D40. C1, C2 and C3 are built (backend), each with its notes below. **C1 built 2026-11-10
(backend; the app's Enquiries screen is not):** `re_enquiries` from zeronova-lead's `lead-enquiry-received`
(subscription `zeronova-real-estate.lead-enquiry-sub` on `lead-lifecycle`), the share page's `ZeroNova_enquireEstateShare`,
staff `ZeroNova_addEstateEnquiry` (+ `ZeroNova_estateEnquiryByRef` to poll), `ZeroNova_updateEstateEnquiry`,
`ZeroNova_estateEnquiries` / `ZeroNova_estateEnquiry` / `ZeroNova_estateEnquiryStats`, `enquiries {total, last7Days,
last30Days, bySource}` on properties, projects and unit types, the dashboard's enquiry counts, `enquiryId` on requirement
and visit inputs; and (E2 tweak, owner 2026-11-10) stale properties are off the website. What C1 does differently from
the text below:

- **One capture event.** The share page and "Add enquiry" both send zeronova-lead's generic `lead-captured-enquiry`
  (topic `lead-captured`; `LeadSource` Website / ManualCall / Manual / WhatsApp), not `lead-captured-website` (D29).
  `ExternalRef` = `estate:{id}`, the dedup key both ways. The share page sends no lead id: the person is found by phone,
  so a forwarded link reaches whoever enquires.
- **Which events are Real Estate's** (conservative): metadata `product = REAL_ESTATE`, or `estate = "1"` (our own
  captures), or a `propertyId` / `projectId` / `unitTypeId`, or a form only the Real Estate website has (`site-visit`,
  `post-property`, `requirement`, `project-enquiry`) while the company's Real Estate website is on. A generic contact or
  call-back form counts only with the product or a listing id — zeronova-cms still sends the listing title as
  `product` (its D29 fix is pending). Idempotent on the external ref, else the lead's activity id.
- **Listing by id, slug or code**: the website does not see our ids, so `propertyId` / `projectId` may be a slug or a
  code; one not found is kept as an extra field and the title falls back to the form's `product`. A unit brings its
  project and unit type.
- **Our captures carry more metadata**: `estateSource` (Call, WalkIn, WhatsApp, Other — "Manual" alone cannot tell walk-in
  from other), `agentId` / `agentName` (the agent who took it, or the shared requirement's agent; wins over the lead's
  owner, so the agent sees the enquiry they added even on another agent's lead), `requirementId` (linked only when the
  enquiry reached that requirement's lead; the status stays New).
- **Agent scope** (D39): an enquiry is the agent's whose id it carries; an unassigned one (lead with no owner) is seen
  only with PRQV, and a manager can give it to an agent (`agentId` on the update).
- **Counts** on listings are the whole company's (every member sees the whole stock, D15), spam left out; a project
  counts its unit types' and units' enquiries, a unit type its units'. One grouped query per kind per mapping. The
  dashboard's (today, this week, open) follow the agent scope.
- **Stats** take India dates (`from`, `to`, yyyy-MM-dd, both included) widened to whole buckets (a week from Monday, a
  month from the 1st), at most 400 buckets; open / closed is the enquiries' status now; spam is counted (it is in the list).
- **Share-page guard**: 5 enquiries per link per rolling hour (`Share:EnquiriesPerHour`), one atomic update on the
  share, which also counts them (`enquiries` on the share DTOs). The count is written before the capture is queued (no
  unit of work), so a failed queue may over-count by one. Rate limiting proper stays the owner's T00b.
- **Closing** keeps `wanted` for not available (C3 makes it a watched requirement) and a note; reopening clears them.
  Requirement added / visit booked need the link first; a closed enquiry only takes a link.

**C2 built 2026-11-10 (backend; the app's Customers screen and dashboard are not):** `ZeroNova_estateCustomers(filter
{text, agentId, hasOpenRequirement, highlighted, lastActivityFrom, lastActivityTo}, skip, take)` and
`ZeroNova_estateCustomer(contactId, leadIds)` (both PRQV / PRQM), `contactId` on requirements, visits and parties (inputs
and DTOs). The lead side of the dashboard (contact, current lead, lead history, activities) the app reads from
zeronova-lead itself (`ZeroNova_contactById`, `ZeroNova_contactLeads`, `ZeroNova_contactActivities`, LEDV there); Real
Estate never calls zeronova-lead for it and copies nothing of the CRM. How C2 works, and what differs from D32:

- **The contact link.** Enquiries already carry `contactId`. Requirements take it from the input (the lead picker's
  `contactId`), else the enquiry they are made from, else the one kept for the same lead, else the lead's contact as our
  rows know it; visits from the input, their requirement or enquiry, else so; parties from the input, else so. Deals and
  tenancies have no contact of their own: a deal is the customer's by its lead, its requirement or a seller / buyer party;
  a tenancy by a landlord / tenant party. Every query also takes the contact's leads (`leadIds`, from
  `ZeroNova_contactLeads`) and matches `leadId ∈ leadIds`; our own rows add the leads they know for the contact.
- **Backfill.** Each Real Estate `lead-enquiry-received` with a lead and a contact sets the contact on that lead's
  requirements, visits and parties that have none (one `updateMany` each; only rows without one change, so it is
  idempotent and cheap).
- **The list is a summary collection, `re_customers`** (chosen over aggregating six collections per request): one row per
  customer key — the contact id, or `lead:{leadId}` while no row names the contact (merged into the contact's row when
  an enquiry names it). The row holds the leads, requirement and party ids, name and phone (as the latest enquiry,
  requirement or visit has them; a party's phone apart, shown only with POWV), the latest row's agent, every agent of any
  row, the counts (enquiries, open requirements = active or on hold, visits, deals, tenancies, unmet) and the last
  activity (a row's last change) **in all and per agent**, and a search text. `CustomerBook` rebuilds a customer's row
  from their rows after every write of an enquiry (the consumer and updates), requirement (save, close, a completed
  deal's close), visit (schedule, reschedule, result), deal (save, stage), tenancy (save, rent, renew, end) or party
  (save) — a whole replace, so a missed or doubled refresh is put right by the next; a failure is logged and never fails
  the write. The list is one indexed, paged query (latest activity first). A company's **first list builds every row
  from its rows** once (counter `customers-built-v1`; bump the name to build again).
- **Scoping (D15, D39).** PRQM: the customers the caller is the agent of any row of, with only their own share of the
  numbers (an agent never sees another agent's counts); PRQV: every customer with all of it, and the `agentId` filter.
  `deals` in the counts is null without PDLV / PDLM (all with PDLV, own with PDLM only).
- **Highlighted** is not stored: at query time, the customer has a still-new match on an active requirement made in the
  last 7 days (the caller's own matches unless PRQV). C3 adds revived matches on watched requirements to the same rule.
- **The dashboard** is read live from the rows (not the summary): numbers (enquiries and open ones; requirements active /
  closed; matches by status; shares sent / opened; visits done / upcoming; deals and deals by stage; tenancies;
  properties owned; unmet) and the lists — enquiries (newest first), requirements (with all their matches counted and
  the criteria in words), matches (new first, at most 200), shares (the requirement's links, property or project, opens,
  last opened), visits (outcome, feedback; the owner's phone only with POWV as today), deals (stage, price, paid,
  balance), tenancies, owned properties. Enquiries, requirements, matches, shares and visits follow PRQM / PRQV (an
  unassigned enquiry only with PRQV); **deals** need PDLV / PDLM (own with PDLM only), **tenancies** PRNV / PRNM (own with
  PRNM only), **owned properties** POWV — a list (and its number) the caller may not see is null, never filtered silently
  to look empty. A customer another agent works shows an agent only what is theirs (no refusal: nothing of the other
  agent's leaks).
- **Unmet demand** (on the dashboard, `unmet` and its count in the list): requirements **lost**, or **closed with a reason
  saying not available** ("not available", "nahi mila") — requirements have no close-reason list yet, so the free text is
  read; one closed by a deal is met — with their criteria in words, and enquiries **closed as not available** with
  `wanted`. C3 adds the watched status and revival.

**C3 built 2026-11-10 (backend; the app's screens are not):** close reason codes, watched requirements, revived matches,
the one-click message and the unmet-demand report. GraphQL: `RequirementStatus.WATCHING`; `RequirementCloseReason`
(BoughtThroughUs, RentedThroughUs, BoughtElsewhere, RentedElsewhere, NotAvailable, NotInterested, Unreachable,
BudgetMismatch, Other); `CloseRequirementInput.reasonCode` (the old `reason` is the note); on `RequirementDto`
`closeReasonCode`, `closedAt`, `watchUntil`, `enquiryId`; on `MatchDto` `revived`, `messageVia`, `messagedAt`,
`messageFailed`; `ZeroNova_sendEstateMatchMessage(matchId)` (PRQM own / PRQV) → `MatchMessageDto {sent, via, draftUrl,
shareUrl, text, phone, match}`; `ZeroNova_estateDemandReport(from, to, groupBy, unmatchedDays)` (PRQV); settings
`watchDays` (default 90), `matchTemplateName`, `matchTemplateLanguage` (null keeps on save); `CustomerUnmetDto`
`requirementCloseReason`, `watchUntil`; `enquiries` on `ProjectSummaryDto`. How it works, and what differs:

- **Close reasons.** A close carries a code beside the free-text note. A close **without a code is `Other`** (a lost one
  keeps none): the C2 reading of the note ("not available", "nahi mila") is kept **only for rows closed before C3**
  (no code), in the customer's unmet demand and the report. A completed deal closes its requirement BoughtThroughUs /
  RentedThroughUs.
- **Watching (D33).** Closing or losing as NotAvailable — or asking for `WATCHING` — sets status `Watching` until now +
  `watchDays` (1–365). Watching is matched both ways, unit types too (D24); it is not "open" in the customer counts but
  is unmet demand. Reopening to Active clears code, note and dates. The **watch sweep** (`WatchExpiryJob`, at start and
  then daily, across companies through `WatchesEndedAsync`) closes ended watches: status Closed, code and note kept,
  still-new (revived) matches dropped.
- **An enquiry closed NotAvailable** with a lead and no requirement of its own makes a watched requirement of that lead
  (contact, name, phone, the enquiry's agent, `enquiryId`), linked as the enquiry's `requirementId` (the enquiry stays
  closed). Criteria: the listing (property: purpose, type, locality, BHK, its price as the budget top; project: locality,
  the unit type's type and BHK), then the form's extra fields (budget / price, bhk / bedrooms, purpose / looking, *type*,
  locality / location / sector / area, city), then the words of `wanted` ("3 BHK", "50-80 lakh", "1.2 cr", "rent",
  "kothi", "sector 70"); default purpose Sale and the dealer's city. Closing it again re-watches the same requirement. An
  enquiry already linked to a requirement made while working it leaves that requirement to the agent. **Counted once:**
  an enquiry that made a watched requirement is unmet only as that requirement (dashboard, list count, report).
- **Revived.** A new match on a Watching requirement is `revived`; the agent gets a bell of its own
  (`ESTATE_REVIVED_MATCH`: "<customer> asked for this earlier — now available", body the property or "<type> in
  <project>", one per requirement, apart from the ordinary new-match bells). **Highlighted** = a new match on an active
  requirement in the last 7 days, **or** a revived new match on a watching requirement however old — until it is shown or
  rejected, or the watch ends. Shown / interested / rejected stay history and are never offered again (unchanged).
- **One-click message.** A fresh share link for the match's requirement (the property's `/p/`, or for a unit-type match
  the project's `/pj/` — an own project still needs its RERA number), which also marks the match shown (the existing share
  handlers). Then, when the dealer set `matchTemplateName` and the customer has a phone, **zeronova-social's existing
  `whatsapp-first-touch-requested`** on topic `whatsapp-sends` (the server-to-server send of a company's own approved
  template that zeronova-office's first touch uses), through the outbox: `To` = the requirement's phone, `TemplateName` /
  `Language` from settings, `Parameters` = [customer name, property title, price, share link], `ContactName`,
  `RequestedBy` = the caller, `LeadId`, no line (social uses the company's default WhatsApp line), `IdempotencyKey`
  `re:{companyId}:m:{matchId}:{shareId}`. Else the M2 wa.me draft (`Drafts.WaLink`). **Deviation:** the service cannot
  ask social whether WhatsApp is connected or the template approved (social's GraphQL needs a signed-in WAPV user; there is
  no machine identity), so "template available" is the dealer's setting — the app checks social
  (`ZeroNova_whatsAppTemplates`, status APPROVED) before saving it — and social checks the line and the template at send
  time. A refusal comes back on `whatsapp-failed` (new subscription `zeronova-real-estate.whatsapp-failed-sub` on
  `whatsapp-lifecycle`, filter `event_type = "whatsapp-failed"`; only `re:` keys are read): the reason goes on the match
  (`messageFailed`: not_connected, template_not_approved, blocked, rate_limited, …) and the agent gets one
  `ESTATE_MESSAGE_FAILED` bell to send it from their phone; the match stays shown. `sent = true` means handed to social,
  not delivered. Social's per-company first-touch budget (60 an hour, 300 a day, shared with the office) applies.
- **Demand report (D34).** As they stand now: (a) watching requirements, (b) active requirements with **no match at
  all** made `unmatchedDays` (default 14) or more ago. In the window (India days, default the last 90, at most 400): (c)
  requirements closed not available (code, or an old row's note; by `closedAt`, else the last change), (d) enquiries closed
  not available that made no watched requirement, (e) website searches with no result. Grouped within each purpose (sale
  and rent never mix; a search without one is its own) by any of Locality, Type, Bhk, BudgetBand (default all four); a
  row counts in every group its criteria fall in (two localities = two groups), "Any" where it did not say. Bands: sale
  Up to 25L, 25L-50L, 50L-1Cr, 1Cr-2Cr, 2Cr-5Cr, Above 5Cr; rent Up to 10k, 10k-25k, 25k-50k, 50k-1L, Above 1L (by the
  budget's top, else its bottom). Each group: total, counts per source, up to 5 sample customers (a–d only: id, code,
  name, lead, contact, agent, what). PRQV at the API; the handler still scopes requirements and enquiries to the caller's
  own without PRQV. At most 300 groups, biggest first.
- **E2's empty-search log.** `ZeroNova_estateWebsiteProperties` returning nothing on page 1 adds one to
  `re_demand_signals` (company, India day, criteria: purpose, types, city, locality — a slug's dashes read as spaces —,
  budget band, BHK; unique per company + day + criteria key, `$inc`). No IP, user agent, free text (it may hold a name or
  a number) or anything else; a search with none of those criteria is not kept; a failure never fails the search.
- **Website ids and a public description (asked by the CMS session):** `id` on `EstateWebsitePropertyDto`,
  `EstateWebsiteProjectSummaryDto` and `EstateWebsiteProjectDto`; a public `description` on properties (`PropertyInput` /
  `PropertyDto`, up to 2,000 characters, null keeps, empty clears) shown on `ZeroNova_estateSharedProperty` and the
  website; the notes stay private. Units take the project's overview (no description).

**The WhatsApp template the dealer creates** (in the app's WhatsApp templates — zeronova-social
`ZeroNova_createWhatsAppTemplate`, WAPM; Meta, or MSG91, reviews it; nothing in Real Estate calls Meta). Name
`zeronova_estate_now_available`, category UTILITY (MARKETING if Meta re-categorises it), one language per template —
create the English one (`en`) and/or the Hindi one (`hi`) and put the approved name and language in Real Estate
settings. Four body variables in this order; Meta does not allow a variable at the very start or end of the body, hence
the last sentence. Example values for review: Rahul / 3 BHK Flat in Sector 70 / ₹95,00,000 /
https://app.zeronova.in/p/abc123.

- English (`en`): "Namaste {{1}}, you had asked us for a property like this, and it is available now: {{2}} at {{3}}.
  See the photos and details here: {{4}} . Reply to this message to book a visit."
- Hindi (`hi`): "नमस्ते {{1}}, आपने हमसे ऐसी प्रॉपर्टी के बारे में पूछा था, वह अब उपलब्ध है: {{2}}, कीमत {{3}}।
  फ़ोटो और पूरी जानकारी यहाँ देखें: {{4}} । विज़िट बुक करने के लिए इस मैसेज का जवाब दें।"

The dealer approves it by submitting it from the app (social sends it to Meta / MSG91), waits for status APPROVED in the
templates list (webhooks, or the 10-minute MSG91 sync), then sets `matchTemplateName` / `matchTemplateLanguage` in Real
Estate settings. Until then the one-click message is the wa.me draft.

**Why:** the owner (2026-11-10): every listing is managed in the Real Estate app; each property and project shows how
many enquiries it got; an Enquiries screen shows them day, week and month wise; a **customer** has a dashboard like a
Tutor student (every enquiry, how far the lead went, what they asked for and did not get); when a customer's request
could not be met and was closed, a matching property arriving later **highlights the customer** and one click tells
them it is available; property videos up to **2 minutes**; agreements for every final sale or rent.

The owner chose (2026-11-10): the customer stays the contact / lead in zeronova-lead (D3), with the dashboard in Real
Estate; enquiries count from the website forms, the share pages, WhatsApp / the AI office and staff (call, walk-in);
unmet demand is caught from requirements with no match, requirements closed as "not available", enquiries closed as
"not available", and website searches with no result.

## 8.1 What exists today (zeronova-lead, read 2026-11-10)

- A website form reaches zeronova-lead as `lead-captured-website` (topic `lead-captured`). The lead is found by phone
  or email; an open lead is reused and the enquiry becomes one `WebsiteForm` activity (dedup key = the CMS enquiry id);
  a Won/Lost last lead gets a new lead. **The form's `propertyId`, `projectId`, extra fields and page URL are dropped.**
- `lead-lifecycle` events (`lead-created`, `lead-status-changed`, …) carry lead id, contact id, status, channel,
  product and form — **no property or project, and nothing at all for a repeat enquiry on an open lead.**
- Lead statuses New, Contacted, Interested, FollowUp, Won, Lost; working status; lost reasons from settings; no won
  reason (a note). One open lead per contact. No query lists a contact's past leads or a contact's activities.
- Website-created contacts are tagged "Manual"; a customer-tagged Buyer/Owner filling a form still opens a lead.
- Email and call channels create no leads today.

## 8.2 Decisions

| # | Topic | Decision |
|---|---|---|
| D27 | Enquiry | A Real Estate **enquiry** is one person asking about something: a property, a project (and unit type), or a general need. Kept in **`re_enquiries`**: code `E-0001`, when, **source** (website form — which form; share page; WhatsApp; AI office; call; walk-in; portal later), lead id, contact id, name and phone (as the lead has them), property / project / unit type, message, the form's extra fields (budget, BHK, slot), agent (the lead's owner), **status** (new → contacted → requirement added / visit booked → closed), close reason |
| D28 | One road in | Every enquiry reaches Real Estate **through zeronova-lead**, so the lead and the enquiry always agree: zeronova-lead keeps the capture's metadata on the activity and publishes a new **`lead-enquiry-received`** on `lead-lifecycle` for **every** captured enquiry — new lead or repeat — with lead id, contact id, channel, form, `product`, the metadata (propertyId, projectId, unitTypeId, enquiry ref, page URL, extra fields) and whether the lead is new. Real Estate consumes the events whose metadata says `product: REAL_ESTATE` (or carries a property / project id) into `re_enquiries` (idempotent on the enquiry ref) |
| D29 | Sources into the road | **Website:** zeronova-cms already sends; it sends `product: REAL_ESTATE` and the listing title separately (fix in cms). **Share page:** a public "Enquire" form on `/p/:token` and `/pj/:token` (name, phone, message) — Real Estate publishes `lead-captured-website` with the share's property/project and requirement. **Staff:** "Add enquiry" in the app (lead picker or new name + phone; property/project; source call / walk-in / WhatsApp) creates or finds the lead through zeronova-lead and records the enquiry. **AI office:** an office tool "record enquiry" (property code or project) for WhatsApp conversations — the same staff mutation, called by the office. **Portals:** later (M5) on the same road |
| D30 | Counts | Each property, project and unit type shows **enquiries: total, last 7 / 30 days, by source**, and its list. The **Enquiries** screen: list with filters (source, status, property, project, agent, date) and **day / week / month** views (counts per bucket, by source, open vs closed); agents see their own, `PRQV` everyone's (D15) |
| D31 | Working an enquiry | From an enquiry: call / WhatsApp, **add requirement** (prefilled from the enquiry), book a visit, share matching properties, or **close with a reason**: not available (with what was wanted — becomes a watched requirement, D33), not interested, unreachable, duplicate, spam, converted. Closing never closes the lead; the lead keeps its own status |
| D32 | Customer | A **customer** is a zeronova-lead contact with anything in Real Estate (an enquiry, a requirement, a party record, a visit, a deal or a tenancy). **Customers** list (search, filters: has open requirement, highlighted, agent, last activity) and a **customer dashboard**: the contact and current lead (status, working status, owner, classification), **lead history** (every lead with its status and won/lost reason), **enquiries** timeline, **requirements** (active, closed — with reasons, watched), **matches** (new, shown, interested, rejected), shares sent and opened, site visits and feedback, deals and payments, tenancies and rent, owned properties (as owner), and **unmet demand** (what they asked for and did not get). Numbers on top: enquiries, requirements, visits, deals, days since last contact |
| D33 | Watched demand ("mila nahi") | Closing a requirement or enquiry as **not available** keeps the need as a **watched requirement** (status `Watching`, until a date — default 90 days, the dealer's setting). Matching includes watched requirements. A new fit marks the match **revived**: the customer is **highlighted** in Customers and on the dashboard, the agent gets a bell, and **one click sends** the customer "this property is now available" — a WhatsApp template through zeronova-social when the company's WhatsApp is connected, else the ready wa.me message (M2). Sending marks the match shown; the customer's reply comes back as a new enquiry |
| D34 | Demand report | **Unmet demand** report: watched and unmatched requirements, enquiries closed "not available", and **website searches with no result** (E2 logs the filters and the day — never who searched), grouped by locality, type, BHK and budget band, so the dealer knows what to source |
| D35 | zeronova-lead changes | Keep the capture metadata on the activity; publish `lead-enquiry-received`; GraphQL **leads of a contact** (all, with status and reasons) and **activities of a contact**; tag website-created contacts `Website`; a won reason field (Real Estate already sends one). No Real Estate concepts inside zeronova-lead — the metadata is opaque to it |
| D36 | Property video | A property and a project each take **videos up to 2 minutes** (a few per listing), uploaded from the app to **zeronova-media** (Real Estate is a new media client): encoded to HLS with a poster; over 120 seconds is refused at upload and checked again on `video-ready`. The app, the share page and the website play it through a short playback grant asked by Real Estate (never a raw URL). The existing video link field stays for YouTube |
| D37 | Agreements: what is added | Built today: Agreement to Sell (Format A, Format B), biana receipt, 11-month rent agreement, the dealer's own Word templates. **Add:** commercial lease (lock-in, yearly escalation, fit-out period, security deposit), registered lease (over 11 months), and for project units (P2) the allotment letter and builder-buyer agreement (RERA Form Q style) |
| D38 | Compliance fixes (built features) | From the 2026-11-10 workflow research: property TDS is **s.393(1) of the Income-tax Act 2025** from 1 Apr 2026 (194-IA before), at ₹50 lakh **or more** counted over the whole deal, on every instalment, 20% without PAN; an **NRI seller** is s.195 with TAN and Form 27Q, not 1%; brokerage TDS (old 194H, now s.393(1)) generally does not apply when the payer is an individual; **Haryana caps brokerage at 1%** (½ + ½); GMADA transfers follow **NDC → sale permission → registry → re-transfer → mutation**, plus LOI transfer (2.5% fee), Chandigarh leasehold (Estate Office permission + unearned increase) and freehold kinds; **GPA** holding carries a "no title" warning; rentals get **police verification** and, in Chandigarh, **Rent Authority intimation within 2 months** with the UIN; parties get **resident / NRI / OCI**, and an NRI or OCI buyer on agricultural land or a farmhouse is warned (FEMA) |
| D39 | Permissions | Enquiries follow requirements: `PRQM` works one's own, `PRQV` sees everyone's. Customers: `PRQM`/`PRQV` the same way, plus zeronova-lead's own `LEDV` for lead history. Video upload: `PPRM` (property) / `PPJM` (project). No new codes |
| D40 | Order | **C1** zeronova-lead changes (D35) + Real Estate enquiries from the event, the share-page form, staff "Add enquiry", counts on property/project, the Enquiries screen. **C2** customers list and dashboard. **C3** watched demand, revived matches, one-click message, the unmet-demand report (with E2's empty-search log). **C4** video (zeronova-media client). **C5** compliance fixes and the new agreement templates. **C6** the AI office "record enquiry" tool. Each slice: backend + app + tests |
