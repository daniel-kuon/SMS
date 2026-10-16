# 9. Costs and Settlement ("Bordkasse")

Every trip has a shared cost account. Crew members record what they paid; the
portal computes each person's balance and a short list of transfers that makes
everybody even. Entities: `Expense`, `ExpenseShare`, `Settlement` (`08` §8.1).

## 9.1 Rules

- **Trip currency** (`Trip.Currency`, default EUR). Expenses may be in another
  currency; the user enters (or accepts a prefilled) exchange rate. The amount
  is converted once when saved and stored as `AmountInTripCurrency`.
- **Participants** are `CrewMember`s (including guest crew). Default split =
  all accepted crew aboard on the expense date (respecting `FromDate`/`ToDate`).
- **Split modes**
  - `Equal`: everybody selected pays the same.
  - `Weights`: shares proportional to weights (e.g. 2 for a couple, 0.5 for a child, number of nights).
  - `Exact`: amounts per person; must sum exactly to the expense amount (validation error otherwise).
- **Money arithmetic** in minor units (cents) as `long`/`decimal`; never `double`.
- **Rounding** (Equal/Weights): compute each raw share, floor to cents,
  then distribute the remaining cents one by one to participants with the
  largest fractional remainder (ties: by `CrewMemberId`). Sum of shares always
  equals the amount.
- **Lock**: after the skipper closes the account (`Trip.CostsLockedAt`), no
  expenses can be added/edited; settlements can still be reported/confirmed.
- **Audit**: keep who created/changed each expense; deleted expenses are
  soft-deleted and listed as "gelöscht" in the history.

## 9.2 Balance calculation

For each crew member *i* (all amounts in trip currency):

```
paid_i      = Σ AmountInTripCurrency of expenses with PaidBy = i
share_i     = Σ ComputedShare of ExpenseShares of i
sent_i      = Σ confirmed (or reported) settlements From = i
received_i  = Σ confirmed (or reported) settlements To = i

balance_i   = paid_i − share_i + sent_i − received_i
```
`balance > 0` → the person gets money back; `balance < 0` → the person owes.
Σ balance_i = 0 (assert in code). Show `Reported` settlements as pending;
decide whether they already count (default: count only `Confirmed`, show
pending separately).

## 9.3 Suggested transfers ("Wer zahlt wem?")

Greedy algorithm (at most *n − 1* transfers, usually optimal enough; finding
the true minimum number of transfers is NP-hard):

```
creditors = members with balance > 0, sorted desc
debtors   = members with balance < 0, sorted by |balance| desc
while both non-empty:
    d = debtors[0]; c = creditors[0]
    x = min(|d.balance|, c.balance)
    emit transfer d → c of x
    d.balance += x; c.balance -= x
    remove whoever reached 0 (re-sort if needed)
```
Ignore residual amounts below 1 cent. Transfers are **suggestions**,
recomputed on every change; recording one creates a `Settlement`.

### Worked example (EUR)

| Expense | Paid by | Amount | Split |
|---------|---------|--------|-------|
| Hafengeld Kiel | Anna | 36.00 | Anna, Ben, Carla (equal) |
| Diesel | Ben | 90.00 | Anna, Ben, Carla (equal) |
| Abendessen | Carla | 50.00 | Anna, Ben (equal) |

| Member | paid | share | balance |
|--------|------|-------|---------|
| Anna | 36.00 | 12 + 30 + 25 = 67.00 | −31.00 |
| Ben | 90.00 | 12 + 30 + 25 = 67.00 | +23.00 |
| Carla | 50.00 | 12 + 30 = 42.00 | +8.00 |

Suggested transfers: Anna → Ben 23.00, Anna → Carla 8.00.

Rounding example: 10.00 split equally by 3 → 3.34, 3.33, 3.33 (the extra cent
goes to the member with the lowest id among equal remainders).

## 9.4 UI ("Kosten" tab of a trip, crew only)

1. Summary cards: total trip costs, my paid, my share, my balance
   ("Du bekommst 23,00 €" / "Du schuldest 31,00 €").
2. Balance table for all members (paid / share / balance).
3. "Ausgleich" list of suggested transfers with buttons "Als bezahlt
   markieren" (payer) and "Erhalt bestätigen" (receiver).
4. Expense list (filter by category, member, day) with receipt thumbnails;
   "Neue Ausgabe" dialog: Betrag, Währung (+ Kurs), Datum, Kategorie,
   Beschreibung, Bezahlt von, Aufteilen auf (checkbox list + mode), Beleg (photo).
5. Statistics: costs per category, per day, per person (simple charts).
6. Export CSV / PDF.

## 9.5 API (suggested)

| Method & route | Purpose |
|----------------|---------|
| `GET /api/trips/{id}/expenses?category&member&page` | list |
| `POST /api/trips/{id}/expenses` | create (shares computed server-side) |
| `PUT /api/expenses/{id}`, `DELETE /api/expenses/{id}` | edit / delete (author or skipper, not locked) |
| `GET /api/trips/{id}/balances` | balances + suggested transfers |
| `POST /api/trips/{id}/settlements` | report a payment |
| `POST /api/settlements/{id}/confirm` | receiver confirms |
| `POST /api/trips/{id}/costs/lock` / `unlock` | skipper |
| `GET /api/trips/{id}/costs/export?format=csv|pdf` | export |

## 9.6 Tests to write
- Rounding: shares always sum to the amount for random amounts/participant counts/weights.
- Balances always sum to zero.
- Greedy transfers: after applying them every balance is 0; ≤ n − 1 transfers.
- Exact split validation; currency conversion rounding; lock prevents changes;
  guest crew included; member leaving the trip keeps historic shares.
