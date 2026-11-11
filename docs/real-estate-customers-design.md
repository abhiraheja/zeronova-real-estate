# Real Estate — enquiries, customers, unmet demand, video and agreement fixes (section 8)

**Status:** design, 2026-11-10. Continues `real-estate-design.md` (D1–D26) with D27–D40. C1, C2, C3, C4 and C5 are built (backend), each with its notes below. **C1 built 2026-11-10
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

**C4 built 2026-11-10 (backend; the app's upload card and player, and the share page's and website's players, are not built):**
property and project videos (D36) through zeronova-media (its `docs/media-service.md` 13.2). Real Estate is media
client `real-estate`, with the company as the account. GraphQL: `ZeroNova_startEstateVideoUpload(listingKind, listingId,
fileName, sizeBytes, contentType, title)` → `EstateVideoUploadDto {videoId, partSize, partCount, parts {number, url},
expiresAt, video}`; `ZeroNova_moreEstateVideoParts(videoId, numbers)`; `ZeroNova_completeEstateVideoUpload(videoId, parts
[{number, etag}])`; `ZeroNova_cancelEstateVideoUpload(videoId)`; `ZeroNova_deleteEstateVideo(videoId)`;
`ZeroNova_renameEstateVideo(videoId, title)`; `ZeroNova_reorderEstateVideos(listingKind, listingId, videoIds)`. All of these
need PPRM for a property's video and PPJM for a project's, checked per video. `ZeroNova_estateVideoStatus(videoIds)` (PPRV /
PPRM / PPJM) is the app's poll. The playback grants are `ZeroNova_estateVideoPlayback(videoId)` (signed in, PPRV / PPRM / PPJM),
and `ZeroNova_estateShareVideoPlayback(token, videoId)` and `ZeroNova_estateWebsiteVideoPlayback(code, slug, videoId)`
(both anonymous), each returning `EstateVideoGrantDto {videoId, playbackUrl, posterUrl, durationSeconds, expiresAt}`.
`PropertyDto` and `ProjectDto` get `videos: [EstateVideoDto]` (videoId, listingKind, listingId, title, status UPLOADING /
PROCESSING / READY / FAILED, failureReason, durationSeconds, posterUrl, sortOrder, addedAt). `SharedPropertyDto`,
`SharedProjectDto`, `EstateWebsitePropertyDto` and `EstateWebsiteProjectDto` get `videos: [EstatePublicVideoDto {id, title,
posterUrl, durationSeconds}]`, ready videos only. How it works:

- **Videos are kept in their own collection, `re_videos`**, one row per video: company, listing kind and id, media's video id,
  the external ref (`property:<id>` / `project:<id>`), title, status, failure reason, duration, poster, order, when it was
  added, file name and size, and the time of the last event applied. The rows are not inside the listing, so an upload or an
  event never clashes with an edit of the property or project. **A unit has no videos of its own:** an upload to a unit is
  refused, and a unit's `PropertyDto` and share page show its project's videos.
- **Rules:**
  - **At most 3 videos per listing.** A failed video counts until it is deleted. An upload abandoned in media shows as failed
    when the listing's next upload starts or when the app polls.
  - **Files:** the content type must be `video/*` (media itself takes MP4, MOV, MKV, WEBM and AVI), and a file may be at most
    **1 GB** (a 2-minute 1080p phone clip is 100–300 MB).
  - **Length:** every create asks media for `maxDurationSeconds: 120`.
  - **Title:** the one given, else the file name; at most 200 characters.
- **The app does the upload.** It `PUT`s each 16 MB part straight to R2 using the URLs and keeps each part's `ETag`. It can ask
  for more (or renewed) URLs, then completes the upload, and the video goes to PROCESSING. Cancelling aborts the upload in
  media and drops the row.
- **Status arrives two ways.** `ZeroNova_estateVideoStatus` asks media (`GET /videos?ids=`) about rows still uploading or
  processing. The consumers of media's `video-ready` and `video-failed` events on `media-events` (subscriptions
  `zeronova-real-estate.video-ready-sub` and `zeronova-real-estate.video-failed-sub`, filtered on `event_type`) apply those
  events. A consumer:
  - keeps only events with `ClientId == Media:ClientId` (`real-estate`);
  - finds the row by media's video id **in the event's account** and checks its external ref;
  - does nothing when the row already has that state (a redelivery);
  - drops an event older than the last one applied.

  **Too long:** media marks the video failed when it probes the original, seconds to minutes after the upload completes,
  with "The video is longer than 2 minutes.". A `video-ready` saying the video is over 120 seconds also fails it here
  (D36's second check). If media no longer has the video, or dropped the upload, the poll finds nothing and the row goes to
  FAILED "The upload did not finish…".
- **Playback is always a grant asked for on the server**, never a stored URL. It carries no watermark, lasts
  `Media:GrantSeconds` (600, clamped to 60–900), and is given only for a READY video. The page asks for the grant when the
  visitor presses play.
  - **Viewer refs:** `app:<userId>`; `share:<shareId>:<visit>` (the share's id, not its token, so the link itself never
    appears in media's logs); `web:<code>:<visit>`. `<visit>` is a new random id for each grant, because media limits key
    requests per viewer.
  - **Share links:** only the link's own property (for a unit, its project) or project, and only if it has not been removed.
  - **Website:** the website must be on and the video's listing listed there: a property that passes `IsListed` (shown,
    available or on hold, not stale, not a unit) or a project among the website's projects. The slug must be that
    listing's own.
- **Deletes:** deleting a video, a property or a project removes the rows and asks media to delete each video (an upload in
  progress is aborted instead). This is best effort: a media failure is logged and never fails the delete, and media's
  sweeps drop abandoned uploads. Renaming a video also tells media, best effort.
- **Without a media key** (or with one shorter than 24 characters, which is a placeholder), the service still runs, like
  the `Encryption:Key` rule. Listings show their stored videos, every video operation and grant answers "Video is not set up
  yet.", and deletes skip media with a log line. `zero-wealthgrow-prod` runs no media, so videos stay off there and the
  YouTube `videoUrl` field is the only video.
- The existing `videoUrl` field (YouTube) is unchanged.

**C4: the owner's setup** (the build applies none of this):

1. **Secret**, in `wealthgrow-qa` (dev) and `wealthgrow-prod`: make one random value (`openssl rand -hex 32`) and store it
   under two names: `zeronovamedia--Clients--real-estate--Key` (media's) and `zeronovarealestate--Media--ClientKey` (ours,
   read as `Media:ClientKey`; our Secret Manager loader takes every `zeronovarealestate--…` secret). For prod, also add both
   names to `zeronova-k8s/infra/secrets.txt` and the line `put "$(hex_32)" zeronovamedia--Clients--real-estate--Key
   zeronovarealestate--Media--ClientKey` to `seed-random-secrets.sh`, then run `provision.sh secrets` and the seed script.
2. **Media's address** for Real Estate: dev `Media:BaseUrl = https://media-dev.zrnova.com` is already set in
   `appsettings.Development.json`. Prod still needs `Media__BaseUrl: "http://zeronova-media:8080"` in
   `zeronova-k8s/config/zeronova-real-estate.yaml`. Optional, on media's side (prod `config/zeronova-media.yaml`):
   `Clients__real-estate__Name` and `Clients__real-estate__MaxDurationSeconds: "120"`.
3. **Restart zeronova-media** after adding its key (the dev container; the prod api, worker and encoder pods), because it
   reads secrets only at start-up. Then restart Real Estate.
4. **R2 CORS:**
   - The private (upload) buckets already allow `PUT` and expose `ETag` for `app[-dev].zrnova.com`. Add any other origin Real
     Estate uploads from (the phone apps need none).
   - The **delivery buckets** (`video[-dev].zrnova.com`) must allow `GET` and `HEAD` from every origin that plays video: the
     share pages on `app[-dev].zrnova.com` and the dealers' own website domains, so use `AllowedOrigins: ["*"]` (the segments
     are encrypted). Also add the Cloudflare response-header Transform Rule that sets `Access-Control-Allow-Origin: *` on
     that host (media doc 7.3).
5. **Subscriptions** on `media-events`: `zeronova-real-estate.video-ready-sub` (already added in zeronova-k8s 5337773) and
   `zeronova-real-estate.video-failed-sub` (added to `infra/subscriptions.tsv`, not applied). Dev creates them itself
   (`Messaging:EnsureResources`).

**C5 built 2026-11-10 (backend; the app's screens are not):** the compliance fixes (D38) and the two lease templates
(D37). Every rule is a **warning**, never a block. Nothing stored is migrated: deals keep the checklist they were made with,
brokerages saved before C5 keep their TDS % (their payer kind is null), and new fields are optional.

GraphQL:

- **Parties:** `PartyInput` / `PartyDto` `residency: PartyResidency` (RESIDENT, NRI, OCI, FOREIGN_NATIONAL) and
  `gender: PartyGender` (MALE, FEMALE, OTHER); null keeps the stored value. `DealPartyDto` carries both.
- **State:** `state` on `PropertyInput` / `PropertyDto` and `ProjectInput` / `ProjectDto` (null keeps, empty clears; a unit
  takes its project's), `rural: Boolean` on properties (stamp duty), `defaultState` in settings. Known names are spelled one
  way ("haryana", "UT Chandigarh" → "Haryana", "Chandigarh"); others are kept as typed. `ZeroNova_estateStates` lists
  India's 28 states and 8 UTs for the pickers.
- **Deals:** `DealDto.state`, `DealDto.tdsDetail: DealTdsDto {applies, threshold, section, ratePercent, onFullPrice,
  nonResidentSeller, note, sellers [{partyId, name, sharePercent, nonResident, noPan, ratePercent}]}`, `DealDto.stampDuty:
  StampDutyEstimateDto {label, state, rural, category, dutyPercent, duty, registrationPercent, registration, total, notes}`;
  `tds` and `tdsSection` on every `DealPaymentDto` and `DealInstalmentDto`; `step`, `typicalDays`, `hint` on
  `ChecklistItemDto`. `TransferKind` adds GMADA_LOI, CHANDIGARH_LEASEHOLD, CHANDIGARH_FREEHOLD.
- **Brokerage:** `BrokerageInput.payerKind: PayerKind` (INDIVIDUAL_OR_HUF, COMPANY_OR_FIRM; null keeps, a new one is
  INDIVIDUAL_OR_HUF); `tdsPercent` null now means "by the rule" (up to 20 allowed). `BrokerageDto` adds `payerKind`,
  `tdsSection`, `tdsNote`, `warnings`. Settings add `dealerPan`.
- **Rentals:** `TenancyInput` adds `agreementDate`, `rentFreeDays`, `permittedUse` (null keeps). `TenancyDto` adds them and
  `compliance: TenancyComplianceDto {policeVerified, policeVerifiedOn, policeStation, policeReference, policeDocument,
  policeVerificationDue, rentAuthorityRequired, rentAuthorityDueDate, rentAuthorityDoneOn, rentAuthorityUin,
  rentAuthorityOverdue, warnings}`. New `ZeroNova_updateEstateTenancyCompliance(tenancyId, input: TenancyComplianceInput)`
  (PRNM) saves the paperwork as a whole, on an ended tenancy too.
- **Stamp duty rates:** `EstateSettingsDto.stampDutyRates: [StampDutyRateDto {state, area (URBAN / RURAL / null = whole
  state), menPercent, womenPercent, jointPercent, registrationPercent, note}]`; `EstateSettingsInput.stampDutyRates`
  replaces the list (null keeps, an empty list goes back to the defaults).
- **Agreements:** `AgreementKind` adds COMMERCIAL_LEASE and REGISTERED_LEASE; the ready templates `builtin:commercial-lease`
  and `builtin:registered-lease`. New placeholders, usable in the dealer's own Word files too: `tds.percent` (the blended rate over
  the whole price), `tds.sellers` (each resident seller's rate and amount: "1% of Harpreet Singh's share (Rs. 42,500/-)"),
  `tds.section`, `tds.nri`, `.tds` inside `payments` and `schedule`, `rent.figures` / `rent.words`, `rent.dueday`, `lease.months` /
  `lease.months.words`, `lease.start`, `lease.end`, `lease.lockin`, `lease.escalation`, `lease.rentfree`, `lease.notice`,
  `lease.use`, `lease.registration`, `deposit.figures` / `deposit.words`, `maintenance.figures`.

The rules:

1. **Property TDS** (`PropertyTds`). Due when the deal price — the whole deal, every buyer and seller together — is
   **₹50 lakh or more** (the code always used `>=`; only the design text said "above"). It is deducted from **every payment
   and instalment, the token included**, shown per line. The section is **s.393(1) of the Income-tax Act 2025** for a
   payment dated on or after 2026-04-01 and **s.194-IA** before. A line with no date counts as today; the deal-level label
   and the agreement's clause use the agreement's date. The rate is per seller's share (their owner share when every seller
   is a current owner, else equal): **1%, or 20% when the seller has no PAN on record**. A **non-resident seller** (NRI, OCI
   or foreign national — all treated as non-resident for tax) gets no rate. Instead the warning reads "TDS under s.195 —
   buyer needs a TAN and files Form 27Q; rate depends on capital gains — consult a CA", whatever the price (s.195 has no
   threshold). The ready Agreements to Sell print the section, the rate and an s.195 clause naming the non-resident sellers.
2. **Brokerage TDS** (`BrokerageTds`): **s.393(1) (Table Sl. 1(ii))** from 2026-04-01, **s.194H** before, by the invoice
   date. An individual or HUF payer: **no TDS by default**, with a note. A company, firm or other payer: **2%**, or **20%
   when the dealer has no PAN on record** (settings `dealerPan`, or a GSTIN, which contains it). It applies only when that
   payer's brokerage to the dealer in the financial year, this invoice included, is **over ₹20,000**. A TDS % given by hand
   wins. GST 18% is unchanged. The threshold is checked when an invoice is saved; earlier invoices of the year are not
   re-taxed when a later one crosses it.
3. **Haryana cap** (`HaryanaBrokerageCap`): on a sale whose property is in Haryana, warn when the sides together exceed
   **1%** of the deal price, or one side exceeds **0.5%**. The warning shows on the brokerage (the deal's total and that side),
   and in the deal's and agreement's checks only for callers with PBRV or PBRM. Punjab and Chandigarh have no cap.
4. **FEMA** (`FemaCheck`): a sale of AGRICULTURAL_LAND or FARMHOUSE with an NRI, OCI or foreign national buyer. There is no
   "agricultural tenure" on plots, so plots are not checked.
5. **GPA** (`GpaCheck`): an owner holding POWER_OF_ATTORNEY (or a property whose tenure is POWER_OF_ATTORNEY) warns "GPA
   gives no title (Supreme Court, Suraj Lamp 2011) — check the chain" on `PropertyDto.warnings`, the deal and the agreement.
   This replaces the old "get a lawyer to check it" line, and covers any owner, not only the sellers.
6. **Transfer checklists:**
   - **AUTHORITY_PLOT** (GMADA / authority, Punjab) is now ordered: NDC (about 15 working days) → sale permission (10) →
     registry at the Sub-Registrar (deed / vasika number) → re-transfer (5) → mutation (about 30). TDS and possession can be
     done at any step. A step ticked before an earlier one warns.
   - **GMADA_LOI:** NDC → LOI transfer (21 working days; the hint gives the fee, about 2.5% of the allotment price) → papers in
     the buyer's name.
   - **CHANDIGARH_LEASEHOLD:** Estate Office permission → unearned increase (hint: about one-third of the rise since
     allotment) → registry → mutation.
   - **CHANDIGARH_FREEHOLD:** the seller's conveyance deed → registry → mutation.
   - The TDS step now reads "26QB and 16B; 27Q and 16A for a non-resident seller". On completion, the buyers of a GMADA_LOI or
     CHANDIGARH_LEASEHOLD deal hold an authority allotment.
7. **Rentals:**
   - **Police verification** is due once a tenancy has started. It shows as a warning and rings the agent once
     (`ESTATE_POLICE_VERIFICATION`, from the rent reminder job).
   - **Chandigarh:** the **Rent Authority intimation** is due 2 months after the agreement (`agreementDate`, else the rent
     deal's date, else the start). It has a due date, a done date and the UIN, and is overdue after the due date. The bell
     `ESTATE_RENT_AUTHORITY_DUE` rings once, from 15 days before the due date.
8. **Stamp duty estimate** (`StampDuty`), labelled "Estimate only". It takes the rate row for the property's state, the
   rural row when the property is marked rural, and the buyers' genders: every buyer a woman → the women's rate; women and
   men → joint; otherwise men. A buyer with no gender (or OTHER) counts as a man, with a note. The estimate is on the deal
   price; the collector rate is not modelled, and the label says duty is on the higher of the two. The defaults are:

   | State | Men | Women | Joint | Registration |
   |---|---|---|---|---|
   | Punjab | 7% | 5% | 6% | 1% |
   | Haryana, urban | 7% | 5% | 6% | ~1% |
   | Haryana, rural | 5% | 3% | 4% | ~1% |
   | Chandigarh | blank | blank | blank | blank |

   The dealer edits the rates in the settings.
9. **Leases:**
   - The **commercial lease** has the lessor and lessee, premises, term, lock-in, rent with GST, yearly escalation, fit-out /
     rent-free days, security deposit, maintenance, the use clause, termination and notice, and a registration clause (must
     register when the term is over 11 months).
   - The **registered lease (over 11 months)** is residential. It opens with the note that a lease over 11 months must be
     registered and stamp duty paid, to delete before printing.
   - Both are filled from the rent deal and **its tenancy** (term, lock-in, escalation, due day, deposit, rent-free days,
     use), else the property's rent terms and the deal's price. Missing values print as blanks.
   - Template warnings: a lease on a sale deal, a lease with no tenancy, a registered lease for 11 months or less, and a
     commercial lease with no use clause.

**Law, practice, and what is not verified** (from the 2026-11-10 workflow research):

| Rule | Basis | Confidence |
|---|---|---|
| Property TDS s.393(1) / s.194-IA, ≥ ₹50 lakh over the whole deal, every instalment, 20% without PAN | Law (Income-tax Act 2025; Finance Act 2024 aggregation) | Secondary source (TDSMan); "or more" is the safe reading |
| Non-resident seller under s.195, TAN, Form 27Q / 16A | Law | **The Income-tax Act 2025 section that replaces s.195 is not verified.** The app says "s.195" to the user, as the research does, and gives no rate |
| Brokerage TDS 2% / 20%, ₹20,000 a year, s.393(1) Sl. 1(ii) | Law | Secondary source. The exception for individuals with business turnover, and s.194M, are not researched: the individual default is "generally does not apply" |
| Haryana 1% brokerage cap | Law (Haryana property dealers rules 2009, rule 10) | Press and law-firm reports |
| FEMA: no farm land or farmhouse for NRI / OCI | Law | Secondary sources |
| GPA gives no title | Law (Suraj Lamp 2011, Supreme Court) | Primary (judgment) |
| GMADA order and days, LOI fee 2.5% | Practice and authority service limits | A local guide and the GMADA checklist; the days are hints, and the fees change yearly |
| Chandigarh leasehold unearned increase about one-third | Practice | A law-firm article; the Estate Office fee schedule is not read |
| Police verification | Law (DM / Commissioner orders under s.163 BNSS, offence under s.223 BNS) | Secondary sources and news |
| Chandigarh Rent Authority intimation within 2 months, UIN | Law (UT of Chandigarh Tenancy Rules 2026) | Press reports. The Assam Tenancy Act text, and whether older tenancies must report, are not verified |
| Stamp duty Punjab and Haryana | Law | **Secondary sources only** — estimates |
| Stamp duty Chandigarh (portals say 6 / 4 / 5 + 1) | — | **Not verified**, so it ships blank |
| Lease registration over 11 months | Law (Registration Act 1908: leases over a year), practice (11-month agreements) | The registered-lease note uses the owner's "over 11 months" wording |
| GST on commercial under-construction (12% or 18%) | — | **Not verified**; not used by C5 (brokerage GST stays 18%) |

Not done in C5: 26QB / 16B and 27Q / 16A tracking per payment; the collector (circle) rate as the duty base; a deposit
cap warning (Model Tenancy Act); a separate Chandigarh tenancy template; the double-sale lock (built 2026-11-11, section 8.5); colony status; the "EC"
step in Punjab registered transfers (fard / jamabandi); the allotment letter and builder-buyer agreement (P2).

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

## 8.3 Review fixes 2026-11-11

After C1–C5 went to dev, a review found these; each has a test in `ReviewFixTests`:

- **Own-capture metadata.** `estate`, `estateSource`, `agentId`, `agentName` are trusted only on our own captures (ref `estate:`).
  The AI office's (ref `office:`) keeps `estate` and `estateSource = AiOffice`, never an agent. Anyone else's (a website form's
  extra fields) loses all four, and they are not shown as extras.
- **Demand signals are bounded.** An empty website search keeps a city or locality only when it is in the company's locality
  list (as the list spells it, else `*`), names up to 60 characters, and at most 500 criteria rows a company a day. Past that, new
  criteria count on the day's `other` row. The report reads only the fields it needs, up to 5,000 rows.
- **Budgets out of free text.** These are never read as amounts: a number before an area unit (sq ft, sq yd, gaj, marla, kanal,
  acre, bigha), a year from 1990 to 2100, or a phone-like number (10 digits or more). In free text a bare number counts only from
  ₹1,00,000. Below that it needs ₹ / Rs / INR or lakh / cr / k. A budget field still takes a bare number from ₹1,000. The locality
  key `area` matches only as the whole key, so `plotArea` is a size, not a place.
- **C5 bells cut-off.** Police verification and Chandigarh Rent Authority bells ring only for tenancies that start on or after
  `Rentals:ComplianceBellsFrom` (default **2026-11-11 IST**, the C5 release) or were created on or after it. Older tenancies show
  the warnings on screen but never all ring at once. A row already rung on dev keeps its flag. When the intimation's window
  opens for a tenancy outside Chandigarh, the row is marked `rentAuthorityNotApplicable` and not read for it again (cleared on its
  next save).
- **One-click message.** A WhatsApp send of a match is refused again within 10 minutes. The idempotency key is
  `re:{company}:m:{match}:{10-minute slot}`, so a double click sends once. A stale property and a stale (or never confirmed)
  partner project are refused (D6). Whoever may send it (PRQM or PRQV) may share the listing, so the share fields take PRQV too.
- **Customer dashboard without PRQV.** The name, phone and numbers come from the caller's own rows. A caller with no row they may
  see gets a blank answer: no name, phone or leads, and empty lists.
- **Scheduled price lists.** The current version is the newest whose `effectiveFrom` has come (India day). A later-dated version
  is stored as `pending`, shown as `scheduled` (`PriceListDto.scheduled`, `ProjectDto.scheduledPriceList`), and prices the units
  when its day comes. That step runs with the hold sweep, every 10 minutes.
- **Holds.** Whom a unit is held for is shown only with PRQV or to the member who held it (`holdBy`). The website shows an on-hold
  property as available.
- **Re-matching.** A project save (and a price list) re-matches only the unit types whose units changed in what matching reads:
  price, status, size, place, rooms, facing, the project name, and so on. Not photos or notes.
- **Races.** Each video takes one of its listing's 3 places (`slot`, unique on the live rows). A parallel upload that loses is
  refused and its media upload aborted. An enquiry's `E-` number is taken only after its ref's insert wins, so a duplicate never
  uses one up.
- **Watch sweep.** One failing row is logged and the sweep goes on.
- **CP terms** are read with PBRV **or** PBRM, so a PBRM member's save keeps them. **PPJM** reads `estateSettings` and
  `estateStates`.
- **Brokerage TDS.** `tdsManual` is true when the saved TDS % was typed in, and false when the rule set it. A save with no TDS %
  applies the rule again.
- **Party residency and gender.** `null` keeps the saved value. `clearResidency: true` / `clearGender: true` removes it, back to
  "not set".

## 8.4 End-to-end fixes on dev

- **A staff enquiry's form.** `ZeroNova_addEstateEnquiry` always sends `form` in the metadata: `staff-call`, `staff-walk-in`,
  `staff-whatsapp` or `staff-other` (the share page sends `share-page`). zeronova-lead's payload `Form` falls back to the
  LEAD's first form when a capture has none, so the consumer never reads it for our own (`estate:`) or the AI office's
  (`office:`) captures. A website capture takes the metadata's `form` too, and the payload's only when it has none.
- **Demand groups without a purpose.** A website search that did not say sale or rent joins the same locality / type / BHK /
  band group of SALE, else RENT, counted under `WEBSITE_SEARCH`. It keeps a group of its own (purpose null) only when no
  purpose has that group.
- **Property TDS per seller.** The warning says the rate per seller, never one rate "on the full price": "TDS under s.393(1)
  of the Income-tax Act 2025: 1% of each resident seller's share (Rs. 42,500/- in all) — …" ("1% of the price" when every
  seller is resident at one rate). A non-resident seller's s.195 line stays separate. `DealTdsDto.ratePercent` is unchanged:
  the blended rate over the whole price (a resident 50% and an NRI 50% is 0.5%). The TDS on an amount is each resident
  seller's part rounded and summed. The ready agreements use `tds.sellers` ("1% of Harpreet Singh's share (Rs. 42,500/-)").

## 8.5 QA fixes 2026-11-11

From the team's test report (bugs.pdf).

- **Double-sale lock** (`DealLock` in `DealFeatures.cs`):
  - Several deals may sit at Offer on one property. Only one may be at Token, Agreement, Transfer or Registry; moving a
    second one there is refused ("D-0005 already has the token for P-0025").
  - A deal never starts, or moves to Token or Completed, on a sold, rented or withdrawn property.
  - Reaching Token sets the property to Token received and drops its new matches; cancelling the token's deal puts it
    back to Available and matches it again.
  - Completing a deal cancels the other open deals on the property ("P-0025 went to D-0006").
- **Website searches in the demand report:** the words typed in the search box are read the way an enquiry's "wanted"
  is (BHK, type, a sector the company lists, an amount) and only those are kept, never the text. A search that says only
  sale or rent is not counted (it made a "Sale · anything" row that grew with every typed search).
- **App:** the deal page never shows one blended TDS rate "on the full price": with mixed sellers it lists each resident
  seller's rate and amount and a s.195 line for a non-resident. The requirement, Add enquiry and Start deal dialogs say
  what is missing instead of a silently grey button (a walk-in typed in full is added on Save). The rentals chip says
  "Starts …" or "Due on day …" until this month's rent is recorded in full. The shared date picker zooms out to months
  and years from its heading.
- **Core:** each Real Estate (and Tutor and Dispatch) feature has its own permission module, so the role editor shows a
  row per feature instead of one row that kept only two codes; the editor also gives any code that does not fit a free
  View or Manage box (host, reply, take) its own row.
