# ZeroNova Real Estate — test guide for the team

**What this is:** a step-by-step check of every Real Estate screen. Each module gives the values to type and what you
should see. Work top to bottom: later modules use the data made in earlier ones.

**Where:** `https://app-dev.zrnova.com` (dev). Never test on `app.zrnova.com` (production, real customers).

**Written:** 2026-11-11, for what is on dev that day.

---

## 0. Before you start

### 0.1 Your login

- Use your own dev login, in the test company your lead gives you. The company must have the **Real Estate** product.
  When it has, the side menu shows a **Real Estate** group.
- **Never** use a real customer's phone number or email. Use the test numbers in 0.3.

### 0.2 Permissions

The admin gives them under the company's roles. Test with **all of them** first, then do module 17 with fewer.

| Code | What it allows |
|---|---|
| PPRV / PPRM | See / add and change properties |
| POWV | See owners, their phones, unit numbers and the owner's expectation |
| PPJM | Add and change projects, unit types, units, price lists, plans, milestones and construction updates |
| PRQV / PRQM | See everyone's / work on own requirements, enquiries, customers and visits |
| PDLV / PDLM | See / work on deals |
| PBRV / PBRM | See / work on brokerage (also the channel-partner terms on a project) |
| PRNV / PRNM | See / work on rentals |
| PSEM | Real Estate settings |

To see the customer's lead details (contact, lead journey, activity) on the customer dashboard you also need lead
access (LEDV).

### 0.3 Test values

| What | Use |
|---|---|
| Phone numbers | `9000000001`, `9000000002`, … (10 digits, start with 9). One number per test person. |
| Names | Start with `Test`: `Test Ramesh Kumar`, `Test Sunita Sharma` |
| Aadhaar | `999999990019` (any 12 digits; never a real one) |
| PAN | `ABCPT1234F` (format: 5 letters, 4 digits, 1 letter) |
| City / locality | Mohali · `Sector 70`, `Sector 71`; Sunam · `Patiala Road` |
| Photos | Any JPG/PNG under 10 MB from the internet or your phone |
| Video | A phone video under 2 minutes; another one over 2 minutes (to see it refused) |

Codes are made by the system and keep counting: P-0001 (property), PJ-0001 (project), E-0001 (enquiry), D-0001
(deal). The company may already have test data, so **your codes may be higher** than the ones in this guide. That is
fine.

### 0.4 How to report a problem

For every failure, send one message with:

```
Module + step:   e.g. 4.12
What I did:      the values typed, the button pressed
What I expected: copy the "Expect" text from this guide
What happened:   what the screen showed (error text exactly)
Screenshot:      yes
Time:            e.g. 11 Nov, 3:40 pm
```

Things that are **not bugs** are listed in section 19. Check there first.

---

## 1. Real Estate settings (PSEM)

Menu: **Real Estate → Real Estate settings**. Press **Save** at the top after each block.

| # | Do this | Expect |
|---|---|---|
| 1.1 | **Your firm:** Firm name `Test Realty`, Phone `9000000001`, RERA agent number `PBRERA-TEST-A001`, Address `SCO 10, Sector 70, Mohali`, Usual city `Mohali`. Tick **GST registered**, GSTIN `03ABCPT1234F1Z5`, Your PAN `ABCPT1234F`, Default state `Punjab`. Save. | "Settings saved". Reload the page: every value is still there. |
| 1.2 | **Matching and brokerage:** check the defaults. | Sale listing old after **30** days, rent **15**, match score at least **50**, budget stretch **10%**. |
| 1.3 | Set Usual sale brokerage `1` (%) and rent brokerage `1` (month). Save. | Saved. |
| 1.4 | **Land units:** Marla `272.25`, Kanal `5445` (sq ft). Save. | Saved. These are used when a size is typed in marla/kanal. |
| 1.5 | **Your property fields → Add field:** Label `Plot shape`, Kind `Choice`, Choices `Square`, `Rectangle`, `Irregular`, For types `Plot`, tick **Search by it**. Save. | The field is listed. It shows under **Your fields** when you add a Plot (2.4). |
| 1.6 | **Stamp duty rates:** look at the table. | Rows for Punjab, Haryana urban, Haryana rural, and Chandigarh blank with "Not verified". Do not change them. |
| 1.7 | **Watched demand:** Keep watching for `90` days. Type `400` and save. | `400` is refused (1 to 365 allowed). Put back `90`. |
| 1.8 | **WhatsApp template name:** type `Bad Name!` and save. | Refused: "A template name is lower-case letters, digits and underscores." Clear the field and save (no template is used on dev). |
| 1.9 | **Agreement templates:** look at the list. | Ready templates are listed. Optional: upload your own `.docx` with `{{sellers}}`, `{{buyers}}`, `{{price}}`. Expect "added — N placeholders found". |

---

## 2. Properties (PPRV / PPRM)

Menu: **Real Estate → Properties → Add property**.

### 2.1 Sale flat — "Flat A"

| # | Do this | Expect |
|---|---|---|
| 2.1 | For `Sale`, Type `Flat`, City `Mohali`, Locality `Sector 70`, Society `Test Heights`, Block `B`, Flat number `802`. Size: Super area `1650` sq ft, Carpet area `1200` sq ft. Asking price `8500000`, Owner's expectation `8200000`, tick Negotiable. BHK `3`, Bathrooms `3`, Floor `8`, Total floors `14`, Facing `East`, Covered parking `1`, Lift ✓, Furnishing `Semi-furnished`, Construction `Ready to move`. Amenities: `Club` Enter, `Gym` Enter. Description `Bright 3 BHK, park view`. Notes `Owner prefers evening calls`. Add 3 photos. Source `From the owner`, Keys with `Owner`, Visit timings `after 5 pm`. Papers: Registry ✓, NOC ✓. Save. | "P-00xx saved". The property page opens with the cover photo, ₹85,00,000 and the rate per sq ft. |
| 2.2 | Open **Owners** and add 2 owners: **Owner 1** `Test Ramesh Kumar`, S/O `Test Hari Kumar`, phone `9000000002`, Aadhaar `999999990019`, PAN `ABCPT1234F`, Residency `Resident`, share `50`. **Owner 2** `Test Vikram Singh`, phone `9000000003`, Residency `NRI`, PAN `ABCPV5678K`, share `50`. Holds by `Registered sale deed`. Save. | Both owners listed with 50%. Aadhaar and PAN show only the last 4 digits. |
| 2.3 | Edit owners: make the shares `60` and `30`. Save. | Refused: "Shares add up to 90%, not 100%". Put back 50/50. |

### 2.2 More properties

| # | Do this | Expect |
|---|---|---|
| 2.4 | **Plot:** Sale, Type `Plot`, City `Mohali`, Locality `Sector 70`, Plot area `10` Marla, Front `30`, Depth `75` feet, Corner ✓, Park facing ✓, Road width `40`, Tenure `Freehold`, price `9000000`. Under **Your fields** pick Plot shape `Rectangle`. Save. | Saved. The size shows 10 marla (≈ 2,722 sq ft). Plot shape is shown. |
| 2.5 | **Duplicate:** add again exactly as 2.1 (same society, block, flat number). Save. | A warning: "This may already be listed", with the matching property. Press Cancel (do not save). |
| 2.6 | **Rent flat — "Flat R":** Rent, `Flat`, `Mohali`, `Sector 70`, 2 BHK, Monthly rent `25000`, Deposit `50000`, Maintenance `2000`, Lock-in `6`, Notice `1`, Available from (next week), Who may rent `Family`. Save. | Saved; the list shows "₹25,000/month". |
| 2.7 | **Commercial in Haryana:** Sale, `Shop`, City `Gurugram`, Locality `Sohna Road`, State `Haryana`, price `6000000`. Save. | Saved (used for the Haryana brokerage warning, 11.6). |

### 2.3 On the property page

| # | Do this | Expect |
|---|---|---|
| 2.8 | On Flat A press **Still available?** → Status `Available`, Price `8400000`, Checked by `Phone call` → Confirm. | Price becomes ₹84,00,000; "confirmed today". Change it back to `8500000` the same way. |
| 2.9 | **Share** → copy the link and open it in a **private/incognito window**. | Photos, price, description, size. **No** owner name, **no** phone, **no** block or flat number, **no** owner's expectation. |
| 2.10 | Back in the app: **Shared links** tab. | The link shows with "1 opens". |
| 2.11 | **Owners history → Add earlier owners:** From `Test Old Owner`, To `Test Ramesh Kumar`, `Test Vikram Singh`, How `Registered sale deed`, Date `2015-04-01`, Note `Sale deed no. 1234, Mohali`. | Step 1 shown in the chain. |
| 2.12 | Properties list: search `Sector 70`, then `Test Heights`, then the code. | Each finds the flat. Switch to **Not confirmed**: only properties not confirmed within 30 / 15 days. |

---

## 3. Owners and parties (POWV)

Menu: **Real Estate → Owners and parties**.

| # | Do this | Expect |
|---|---|---|
| 3.1 | Search `Test Ramesh`. | The person, with Flat A under Properties. |
| 3.2 | Edit him; leave Aadhaar blank; Save. | "On file: ending 0019" stays; blank does not wipe it. |
| 3.3 | Open Test Vikram Singh. | Residency NRI and the note about s.195 TDS and FEMA. |

---

## 4. Projects (PPJM)

Menu: **Real Estate → Projects → New project**.

### 4.1 The project

| # | Do this | Expect |
|---|---|---|
| 4.1 | Name `Test Sapphire Plaza`, Own or partner `Own project`, Stage `Under construction`, Category `Commercial`, City `Sunam`, Locality `Patiala Road`, State `Punjab`. **Leave RERA empty.** | A warning: an own project can be shared only with a RERA registration (or marked not needed). Saving still works. |
| 4.2 | Towers: Name `A`, Floors `2`. Key figures: `Showrooms` = `6`. Highlights: `Main road` Enter, `Parking` Enter. GST `12`. Add 2 photos and a brochure PDF. Save. | "Project PJ-00xx saved". The project page opens on **Overview**. |
| 4.3 | Edit → **Add RERA registration:** number `PBRERA-SGR78-TEST01`, State `Punjab`, Registered on (any past date), Valid until (a future date). Save. | Overview shows RERA, "registered …, valid till …". The warning is gone. |

### 4.2 Unit types and units

| # | Do this | Expect |
|---|---|---|
| 4.4 | **Unit types → Add unit type:** Name `Showroom 500`, Type `Showroom`, Super area `500` sq ft, Carpet area `300` sq ft. Leave GST blank. Save. | Listed; "GST 12%" (taken from the project). |
| 4.5 | **Units → Make units:** Unit type `Showroom 500`, Tower `A`, Make `On each floor`, Floor from `0`, Floor to `1`, Units per floor `3`, Numbering `{tower}-{floor}{nn}`. | Preview: **6 units**, A-G01, A-G02, A-G03, A-101, A-102, A-103. Press **Make 6 units** → "6 units made"; the board shows them all Available. |
| 4.6 | Make units again with the same values. | Refused: a unit number is never made twice. |
| 4.7 | Numbering `{tower}-showroom` (no number). | Refused: "The pattern needs {n} or {nn}". |
| 4.8 | Click unit **A-101** → edit → tick **Corner**. Save. | Saved. |

### 4.3 Price list and cost sheet (check the sums by hand)

| # | Do this | Expect |
|---|---|---|
| 4.9 | **Price list:** Showroom 500 → Rate `8000`, Per `per sq ft`, On `Super area`. Charges: **(a)** `Corner PLC`, `% of base price`, `5`, Applies `Corner units`, Mandatory ✓, GST applies ✓. **(b)** `Floor rise`, `Per sq ft`, `100`, Applies `Per floor above a floor`, Above floor `0`, Mandatory ✓, GST ✓. **(c)** `EDC / IDC`, `Per sq ft`, `300`, `Always`, Mandatory ✓, GST **off**. **(d)** `Club membership`, `Fixed amount`, `100000`, `Always`, Mandatory **off**. Effective from today. **Save as new version**. | "Price list version 1 saved". |
| 4.10 | Open unit **A-G01** → Cost sheet. | Basic ₹40,00,000 · EDC/IDC ₹1,50,000 (no GST) · Club ₹1,00,000 (optional, not included) · **Subtotal ₹41,50,000** · **GST (12%) ₹4,80,000** · **Total ₹46,30,000**. |
| 4.11 | Open unit **A-101** (corner, 1st floor) → Cost sheet. | Basic ₹40,00,000 · Corner PLC ₹2,00,000 · Floor rise ₹50,000 · EDC/IDC ₹1,50,000 · **Subtotal ₹44,00,000** · **GST ₹5,10,000** (12% of ₹42,50,000) · **Total ₹49,10,000**. |
| 4.12 | Price list: change the rate to `8500`, Effective from **a date next month**, Save. | Version 2 shows as "Scheduled" and read-only. The cost sheets still use version 1 (₹46,30,000). |

### 4.4 Milestones and payment plans

| # | Do this | Expect |
|---|---|---|
| 4.13 | **Milestones:** add `Slab 1` and `Possession`. Save milestones. | "Milestones saved". |
| 4.14 | **Payment plans → New:** Name `CLP`, Kind `Construction-linked`. Steps: `Booking` 10% On booking · `Agreement` 30% On agreement for sale · `Slab 1` 30% At milestone Slab 1 · `Possession` 30% On possession. Save. | "Percent steps total 100%". Saved without a warning. |
| 4.15 | Unit A-G01 → Cost sheet → Payment plan `CLP`. | Booking ₹4,63,000 · Agreement ₹13,89,000 · Slab 1 ₹13,89,000 · Possession ₹13,89,000 (adds up to ₹46,30,000). |
| 4.16 | Edit CLP: Booking `15`%, Agreement `25`%. Save. | A warning that 15% falls due before the agreement for sale, and RERA allows at most 10%. It still saves. Put back 10/30. |

### 4.5 Unit board, updates, sharing

| # | Do this | Expect |
|---|---|---|
| 4.17 | Unit A-G02 → **Block** for a customer (pick a lead or the test customer), Blocked until (3 days ahead). | The board shows A-G02 Blocked, with the name and date. |
| 4.18 | A-G03 → **Book**; A-103 → **Mark sold**; A-102 → **Withdraw**. | The board colours change. On the Projects list the line "N of M available · 1 blocked · 1 booked · 1 sold" follows (2 still available: A-G01 and A-101). |
| 4.19 | A-G02 → **Make available**. | Back to Available. |
| 4.20 | **Construction updates:** `Slab 1 cast on tower A` + 1 photo. | Listed with today's date. |
| 4.21 | Overview → **Share / Create link**; open it in incognito. | Project name, photos, RERA number, unit types with "from" price and availability, brochure download, the construction update. **No** unit numbers, **no** channel-partner terms. |
| 4.22 | Make a **partner** project: `Test Partner Towers`, Partner, Builder `Test Builders Pvt Ltd`, Stage `New launch`, Category `Residential`, Locality `Sector 70`. Save. Then **Confirm inventory**. | "Inventory confirmed". (A partner project not confirmed for 30 days is not matched.) |
| 4.23 | Try **Delete project** on Test Sapphire Plaza. | Refused: it has booked/sold units. |

---

## 5. Requirements and matching (PRQV / PRQM)

Menu: **Real Estate → Requirements → Add requirement**.

| # | Do this | Expect |
|---|---|---|
| 5.1 | Customer: **New customer (walk-in)** `Test Anil Mehta`, phone `9000000004` → Add customer. For `Sale`, Types `Flat`, Localities `Sector 70`, Budget from `7000000` to `9000000`, BHK `3`, Needs a lift ✓. **Save and match**. | "Saved — matching properties now: N" — at least 1 (Flat A; older test data may add more). Open it: Flat A with a score and reasons (e.g. "lift ✓"). |
| 5.2 | Add a sale flat `Sector 70`, 3 BHK at `9500000`. | It matches 5.1 too, with a lower score and the reason "6% over budget" (within the 10% budget stretch: up to ₹99 L). |
| 5.3 | Add a sale flat `Sector 70`, 3 BHK at `10500000`. | It does **not** match (over ₹99 L). |
| 5.4 | On 5.1's matches: the ₹95 L flat → **Not interested**, Why `price too high`. | It leaves the list and is never offered again. |
| 5.5 | Flat A → **Send**. | A share link + WhatsApp message to the customer. The match turns "Shown". |
| 5.6 | Flat A → **Fix visit** → tomorrow 11:00, Meeting point `Gate 2`. | "Visit fixed"; it is under Site visits → Coming. |
| 5.7 | **Unmet demand:** Customer `Test Pooja Rani`, phone `9000000005`. For `Rent`, `Flat`, Localities `Sector 71`, Rent `20000` to `30000`, BHK `2`. Save and match. | "matching properties now: 0". |
| 5.8 | Open it → **Change status** → `Closed`, Reason `Not available — nothing fitted`, Note `wanted 2 BHK in 71`. | Status **Watching**, "Watching until" (today + 90 days). |
| 5.9 | Add a property: Rent, `Flat`, `Mohali`, `Sector 71`, 2 BHK, rent `24000`. Save. | Within a minute the requirement shows a match marked **"Asked earlier — now available"**. |
| 5.10 | On that match press **Tell customer**. | WhatsApp opens in a new tab with the message and link ready (no template on dev). The match says "WhatsApp message opened". |
| 5.11 | Close another requirement with reason `Bought elsewhere`. | Status Closed, **not** Watching. |
| 5.12 | **Project match:** customer `Test Kiran`, `9000000006`. Sale, Type `Showroom`, Locality `Patiala Road`, Budget `4000000` to `5000000`. | Matched with **Test Sapphire Plaza · Showroom 500 · N available**, best unit + price. |

---

## 6. Enquiries (PRQV / PRQM)

Menu: **Real Estate → Enquiries**.

| # | Do this | Expect |
|---|---|---|
| 6.1 | **Add enquiry:** Or a new person `Test Neha`, phone `9000000007`. About `A property` → Flat A. Came by `Call`. What they asked `wants visit Sunday`. Add enquiry. | "Enquiry E-00xx added", the dialog closes. Row: Source **Call**, Status **New**. A lead for Test Neha appears in Leads (product Real Estate). |
| 6.2 | Phone `12345` in the new-person field. | Refused: "Enter a 10-digit Indian mobile number." |
| 6.3 | **From a shared link:** open Flat A's share link in incognito → **Enquire**: Your name `Test Walk Web`, Mobile `9000000008`, message `Is it still available?` → Send. | "Thank you. The dealer will call you soon." In the app the enquiry shows within a minute: Source **Shared link**, About Flat A. |
| 6.4 | Same from the project's share link (4.21). | Source Shared link, About the project. |
| 6.5 | Open the enquiry from 6.1 → **Mark contacted**. | Status Contacted. |
| 6.6 | → **Add requirement**. | The requirement form opens for Test Neha; after saving, the enquiry's status becomes "Requirement added". |
| 6.7 | → **Book a visit**. | After fixing it the status is "Visit booked". |
| 6.8 | Enquiry from 6.3 → **Close enquiry**, Why `Not available`, What did they want `2 BHK Sector 70 under 30k rent`. | Closed. A message says a watched requirement was made for this customer (watching 90 days). |
| 6.9 | Another enquiry → Close, Why `Spam`. | Closed, nothing watched. |
| 6.10 | **Give to agent** → pick another team member. | The agent changes. |
| 6.11 | Top of the page: **Show by** `Day`, `Week`, `Month`; change From / To. | The counts (Enquiries, Still open, Closed) and the "Enquiries by source" chart follow the dates. |
| 6.12 | Flat A page → **Enquiries: N → See the enquiries**. | The list is filtered to "Property Flat A". The Properties list shows "N this week" in the Enquiries column. |

---

## 7. Customers (PRQV / PRQM)

Menu: **Real Estate → Customers**.

| # | Do this | Expect |
|---|---|---|
| 7.1 | Open the list. | Everyone from modules 5–6, newest activity first, with counts: Enquiries, Open requirements, Visits, Deals, Not found. |
| 7.2 | View **New match waiting**. | Test Pooja Rani (5.9) is highlighted. |
| 7.3 | Open Test Pooja Rani's **Customer dashboard**. | Tiles: Enquiries, Active requirements, Matches, Shared, Visits, Deals, Asked for not found. A banner "Asked earlier — now available" with **Tell customer**. |
| 7.4 | Tabs: Overview, Enquiries, Requirements, Matches, Shared, Visits, Deals, Rentals, Owned properties, **Unmet demand**, Activity. | Unmet demand lists the Sector 71 requirement. Activity lists calls/WhatsApp/enquiries (needs LEDV). |
| 7.5 | **Lead journey** on Overview. | The lead(s) of this person, newest first, with how far each went. |
| 7.6 | **Schedule visit** from the dashboard. | First pick where (matched / enquired / shared properties), then date and time. |
| 7.7 | Test Ramesh Kumar's dashboard → **Owned properties**. | Flat A, 50% share. |

---

## 8. Site visits

Menu: **Real Estate → Site visits**.

| # | Do this | Expect |
|---|---|---|
| 8.1 | Coming: the visit from 5.6. | "Meet at Gate 2", "keys with Owner". |
| 8.2 | **Tell customer** / **Tell owner**. | WhatsApp opens with a ready message (date, time, place). |
| 8.3 | **Move** to the day after, 16:00. | The new time shows. |
| 8.4 | After the time (or move it to the past): **Record** → What happened `Done`, Customer is `Negotiating`, Feedback `wants 5% less`. | It moves to Past with the outcome. |
| 8.5 | Record another as `No-show`. | Shown as No-show. |

---

## 9. Demand report (PRQV)

Menu: **Real Estate → Demand**.

| # | Do this | Expect |
|---|---|---|
| 9.1 | Dates: last 30 days. Group by `Locality`. | Groups such as Sector 71 · Rent · Flat · 2 BHK, with counts by source (Watching, No match, Closed, Enquiries). Each locality only **once** per purpose. |
| 9.2 | Group by `Type`, `BHK`, `Budget band`. | Regrouped. |
| 9.3 | From date after To date. | "The first date is after the second." |
| 9.4 | A range over 400 days. | "Pick at most 400 days." |

---

## 10. Deals and agreements (PDLV / PDLM)

| # | Do this | Expect |
|---|---|---|
| 10.1 | Flat A page → **Start deal**. For customer `Test Anil Mehta`. Sellers: the two owners (filled in). Buyers → **New person** `Test Sunita Sharma`, phone `9000000009`, Gender `Woman`, Residency `Resident`, PAN `ABCPS4321Q`. Price `8500000`. How it passes `Registered sale deed`. Start deal. | Deal D-00xx at stage **Offer**. |
| 10.2 | Look at the **TDS on the property** block. | Deal over ₹50 lakh: **Test Ramesh Kumar 1% of his 50% share = ₹42,500** (s.393(1)); **Test Vikram Singh (NRI) — s.195**, buyer needs a TAN and Form 27Q, consult a CA. |
| 10.3 | Look at **Stamp duty**. | Punjab, urban, women's rate (only a woman buys): **5% = ₹4,25,000 + registration 1% = ₹85,000 → about ₹5,10,000**, marked "Estimate only". |
| 10.4 | **Edit terms:** Earnest money `10`% → shows ₹8,50,000. Last date for balance: 60 days ahead. **Add payment:** Mode `UPI`, Amount `100000`, UTR `TESTUTR0001`, Date today, Paid by Test Sunita Sharma. Place `Mohali`, Witness 1 `Test Witness One`. Save. | Money: Paid ₹1,00,000, Balance ₹84,00,000. |
| 10.5 | Add a second payment with the **same** UTR `TESTUTR0001`. | A check warns about the duplicate UTR. Remove it. |
| 10.6 | Add a payment Mode `Cash`, `50000`. | A check warns about cash. (Remove it afterwards.) |
| 10.7 | **Move stage** → `Token`, then `Agreement`. | Stage changes. At Agreement a check says the earnest money is 10% but only ₹1,00,000 is paid. |
| 10.8 | **Agreement** → pick a ready template (Agreement to sell). | The text shows with the parties, price in figures and words, TDS line. Change a line, then **Download Word**. The `.docx` opens in Word with your change. |
| 10.9 | **GPA warning:** on Flat A's owners set Holds by `Power of attorney (GPA)`. Back on the deal. | A check: a GPA gives no title. Put it back to Registered sale deed. |
| 10.10 | **Transfer checklist:** tick step 3 before step 1. | A note: ticked before step 1 — these steps go in order. |
| 10.11 | Move stage → **Completed**. | Test Sunita Sharma becomes the owner, the two sellers move into Owners history, Flat A is **Sold**, Test Anil Mehta's requirement closes. |
| 10.12 | Another deal → Move stage → `Cancelled`, Why `buyer's loan refused`. | "Fell through: buyer's loan refused". |
| 10.13 | **Rent deal** on Flat R: Landlords = its owners (add one if none), Tenants new person `Test Rahul`, `9000000010`, Monthly rent `25000`, How it passes `Rent`. Agreement → template **Rent agreement**, also try **Commercial lease** and **Registered lease (over 11 months)**. | Each downloads a Word file. Completing the deal marks Flat R **Rented**. |

---

## 11. Brokerage (PBRV / PBRM)

On the completed sale deal (10.11) → **Add brokerage**.

| # | Do this | Expect |
|---|---|---|
| 11.1 | Side `Buyer`, Who pays Test Sunita Sharma, The payer is `Individual / HUF`, Reckoned as `Percent of price`, Rate blank. | Rate 1% (from settings) = ₹85,000 + GST 18% ₹15,300 = **₹1,00,300**; **no TDS** (an individual does not deduct). |
| 11.2 | Side `Seller`, payer `Company / firm / other`, Rate `1`. | ₹85,000 + GST ₹15,300 − TDS 2% ₹1,700 = **₹98,600** receivable. |
| 11.3 | Settings: clear **Your PAN** and GSTIN, save; open 11.2 again and save it. | TDS becomes **20%** (₹17,000). Put the PAN and GSTIN back. |
| 11.4 | Type TDS `5` by hand. | "TDS 5% as typed — it overrides the rule". Clear it to follow the rule. |
| 11.5 | Menu **Brokerage** → **Receive** ₹50,000 on 11.1 (UPI, UTR `TESTUTR0002`). | Status Part received; Due ₹50,300. Receiving more than due is refused. |
| 11.6 | Brokerage on a deal of the Haryana shop (2.7), 1.5% from one side. | A warning: Haryana caps brokerage (1% in all, 0.5% a side). |
| 11.7 | **Invoice (Word)**. | A Word invoice with firm name, GSTIN, GST and amounts. |
| 11.8 | **Write off** the rest, then **Bring back**. | Due goes out of the totals, then back. |
| 11.9 | Top tiles. | Earned (this year), Received, Due now, Open invoices add up. |

---

## 12. Rentals (PRNV / PRNM)

Menu: **Real Estate → Rentals → Add tenancy**.

| # | Do this | Expect |
|---|---|---|
| 12.1 | Property: search `Sector 70` → Flat R. Tenants `Test Rahul`. Rent `25000`, Deposit `50000`, Starts the 1st of this month, Term `11` months, Rent due day `5`, Yearly increase `5`%. Save. | Listed under Active: "due on day 5". |
| 12.2 | Rent due day `30`. | Refused (1–28). |
| 12.3 | **Rent received** → For the month (this month), ₹25,000, UPI. | "Paid" for this month; a receipt line. |
| 12.4 | **Remind tenant** / **Ask about renewal**. | WhatsApp opens with the message. |
| 12.5 | **Compliance** → Police verification: tick done, Done on today, Police station `Phase 8 Mohali`, Reference `TEST-PV-01`, attach a PDF/photo. Save. | "Police verification due" goes away. |
| 12.6 | Add a tenancy for a property with City `Chandigarh`. Open **Compliance**. | A **Chandigarh Rent Authority** block: due 2 months after the agreement date. Fill Intimated on + UIN `TESTUIN01` → Done. Leave one empty with an old agreement date → "Overdue". |
| 12.7 | **Renew**: Term 11, New rent (suggested +5% = ₹26,250). | New term and rent shown. |
| 12.8 | **End tenancy**. | Moves to Ended; Flat R becomes available to let again. |

---

## 13. Videos (on a property or a project)

| # | Do this | Expect |
|---|---|---|
| 13.1 | Flat A → Videos → **Add video** → a phone video **under 2 minutes**. | "Uploading N%", then "Processing" ("Getting it ready to play — this takes a minute or two"), then **Ready**. |
| 13.2 | Press play. | It plays in the app. Open the share link: it plays there too. |
| 13.3 | Add a video **over 2 minutes**. | Refused before upload: "This video is m:ss long — longer than 2 minutes." |
| 13.4 | Add a PDF or photo as video. | "Choose a video file." |
| 13.5 | Add 3 videos, then a 4th. | "This listing already has 3 videos. Delete one to add another." |
| 13.6 | Rename, Move up / down, Delete one. | Works; delete asks first. |
| 13.7 | Add a video on the **project**; open one of its units' pages. | The unit shows the project's videos with "Add or change them on the project." |

---

## 14. Website listings (PSEM)

| # | Do this | Expect |
|---|---|---|
| 14.1 | Settings → **Website** card → turn **Website on**. | A website code (6 letters/digits) shows. "On the website now: 0 properties, 0 projects." |
| 14.2 | **Change the code** to `AB` | Refused (4 to 20 letters and digits). Try `TESTRE1` → saved. |
| 14.3 | The 2.4 plot (Flat A is sold by now) → Website card → **Show on website** ✓, **Featured** ✓. Save. | The Properties list shows "On website" / "Featured". The settings card count goes up by 1. |
| 14.4 | A new own project without RERA → Show on website → Save. | Refused: an own project needs RERA or "not required" with a reason. |
| 14.5 | Mark the listed property **Sold**. | It no longer counts as listed. |
| 14.6 | On the Real Estate website (when one runs), open a listed property that has a Ready video. | A **Video tour** section with the poster and length; pressing play plays it in a pop-up. |

The website itself (the customer-facing site) is not run on dev for the team; a separate test covers it.

---

## 15. Property dashboard

Menu: **Real Estate → Property dashboard**.

| # | Check | Expect |
|---|---|---|
| 15.1 | Tiles | For sale, For rent, To re-check, Searching, New matches, Enquiries today / this week / open — match what you made. |
| 15.2 | Latest matches, Visits today and tomorrow, Check these again, Projects (units still free). | Rows open the right page. |

---

## 16. Hindi

| # | Do this | Expect |
|---|---|---|
| 16.1 | Switch the app language to Hindi; open each Real Estate page. | All labels in Hindi; no raw keys like `estate.form.city`. Report any English left or broken text. |

---

## 17. Permissions (do this last, with a second login)

| # | Give the second login only | Expect |
|---|---|---|
| 17.1 | PPRV | Sees properties; **no** Add/Edit; **no** owner phones, unit number, owner's expectation or source dealer's phone. |
| 17.2 | PPRV + POWV | Now sees owners and unit numbers. |
| 17.3 | PRQM (no PRQV) | Sees only their own enquiries, requirements and customers. |
| 17.4 | No PBRV | Brokerage menu hidden; cannot change a project's channel-partner terms. |
| 17.5 | No PSEM | Settings menu hidden; opening `/real-estate/settings` directly is refused. |
| 17.6 | PPRM without PPJM | Sees projects, but cannot add or edit them. |

---

## 18. Phone view

Repeat 2.1, 5.1, 6.1 and 10.4 on a phone browser (or a narrow window). Expect: nothing cut off, no sideways
scrolling, dialogs fit the screen.

---

## 19. Not bugs (known, by design or not built yet)

- **"Tell customer" opens WhatsApp instead of sending:** no approved WhatsApp template on dev. With a template set in
  settings it sends by itself.
- **Matches take up to a minute** after a save: matching runs in the background.
- **Stamp duty is an estimate;** Chandigarh has no rates until someone enters them.
- **NRI seller TDS shows no amount:** s.195 depends on capital gains; a CA decides.
- **Not built yet:** booking → allotment → builder-buyer agreement → demands → possession (P2), the channel-partner
  portal (P3), the AI office's property search.
- **AI office WhatsApp enquiries** (source "AI office") need the office tool turned on for an Executive; not part of
  this round.
