# Prompt 3

## Prompt

> Identifiera: funktioner som gör mer än en sak, otydlig namngivning (variabler, funktioner, klasser), saknad felhantering för externa anrop, och kod som är svår att läsa utan kommentarer. Lista de fem allvarligaste problemen.

## Answer (English)

### Summary

The code is not sloppy. Most method and class names are descriptive, payment calls to Stripe are handled very carefully, and every external call I looked at has at least a timeout or a catch. The serious problems come from **size and hidden reasoning**, not from carelessness: a few very large classes, critical concurrency code with no explanation, and two naming problems that can mislead a future reader. **All five problems below are inherited from upstream; none was written by us.**

I picked the five using measurements first (a scan of 541 production C# files and 1,063 methods), then read each candidate before ranking it. "Serious" here means: likely to cause bugs, or likely to make a future change risky.

### The five most serious problems

#### 1. `CheckoutOrchestrator` does everything on the payment path (functions that do more than one thing)

`BlazorShop.Application/Services/Payment/CheckoutOrchestrator.cs` is **1,280 lines** with **15 constructor dependencies** (`:38-53`) and only **3 comment lines**. It handles at least eight separate jobs in one class:

| Job | Where |
|---|---|
| Idempotent replay of earlier results | `ReplayPersistedOutcomeAsync` `:1222` |
| Cart pricing and product lookup | `ResolveCartLinesAsync` `:939` |
| Building the order | `CreateOrder` `:1019` (7 parameters) |
| Starting a Stripe checkout | `InitializeStripeAsync` `:309-564` |
| Recovering from uncertain Stripe state | `RecoverStripeProviderStateAsync` `:640-758` |
| Finalizing paid or failed payments | `FinalizePaidStripeCheckoutAsync` `:858`, `FinalizeTerminalStripeFailureAsync` `:879` |
| Building the Stripe request | `CreateStripeInitialization` `:1080` |
| Sending the bank-transfer email and building URLs | `TrySendBankTransferEmailAsync` `:1153`, `BuildClientUrl` `:1137` |

`InitializeStripeAsync` alone is **255 lines with 35 branches**, and it is a state machine written as nested `if`s: seven separate exits call `MarkStripeReconciliationRequiredAsync` (`:368, 386, 407, 478, 491, 513, 538`). The handling of failures is careful, which is why this is not a bug today. The problem is that nobody can change one part without re-reading all of it, and a mistake here means money or orders going wrong.

**Fix:** split by responsibility (cart pricing, order factory, Stripe flow, notifications), model the Stripe states explicitly instead of nested ifs, and add a short "why" comment per phase.

#### 2. Misleading names: a plaintext password is stored in `PasswordHash`, and 66 types share names across layers (unclear naming)

**a) `PasswordHash` holds the raw password.** `AuthenticationService.cs:78` and `:139` do `mappedUser.PasswordHash = user.Password;`. The value is then used as the *plaintext* in `AppUserManager.cs:44` (`CreateAsync(user!, user!.PasswordHash!)`) and `:65` (`CheckPasswordSignInAsync(currentUser, user.PasswordHash!, …)`). A property called "hash" that contains the actual password is the kind of name that causes a leak later: anyone who logs, serializes or saves that entity in good faith writes the password out. The API shape is also wrong: `LoginUserAsync(AppUser user)` (`AppUserManager.cs:49`) takes a whole entity but uses only its email and that borrowed field. It should take an email and a password.

**b) 66 type names are defined twice**, once on the server side (`Application`, `Domain` or `Infrastructure`) and once in `BlazorShop.Web.Shared`, with a *different meaning*. Example: `ProductService` is the real business logic in `BlazorShop.Application/Services/ProductService.cs` and an HTTP wrapper in `BlazorShop.Presentation/BlazorShop.Web.Shared/Services/ProductService.cs`; `AuthenticationService`, `CategoryService`, `NewsletterService` and the matching `I…Service` interfaces follow the same pattern. The same goes for DTOs and models (`GetProduct`, `CreateProduct`, `ProductBase`, `LoginUser`, `PagedResult`, `ServiceResponse` and more), which can drift apart. A stack trace, a search result or an IDE "go to type" can land on the wrong one.

**c) Smaller naming issues.** `GetReFreshToken` is a typo (`AppTokenManager.cs:30`, called at `AuthenticationService.cs:179,222`). Cryptic locals such as `tApi` and `sApi` (`Web/Pages/Admin/Dashboard.razor.cs:207-208`) and a handful of one- or two-letter variables. There are only about nine of those, so they are minor.

**Fix:** pass credentials as explicit parameters instead of through the entity, rename `GetReFreshToken`, and give the client-side classes distinct names (for example `ProductApiClient`) or share one set of DTOs.

#### 3. Locking and concurrency code has almost no comments (code that is hard to read without comments)

These files hold the most delicate logic in the application, and they have **0, 0 and 2 comment lines**:

| File | Lines | Comment lines |
|---|---|---|
| `BlazorShop.Infrastructure/Services/InventoryReservationService.cs` | 518 | 0 |
| `BlazorShop.Infrastructure/Services/InventoryReservationTransitionOperation.cs` | 159 | 0 |
| `BlazorShop.Infrastructure/Services/StripePaymentReconciliationService.cs` | 536 | 2 |

What a reader cannot see from the code:
- `InventoryReservationService.cs:139` loops over `Distinct().Order()`. The ordering is what prevents two checkouts from deadlocking on each other's product rows, but nothing says so, and a well-meaning cleanup would remove it.
- Raw `SELECT … FOR UPDATE` row locks (`:116, 142, 166, 318`) and `xmin` concurrency tokens in the same queries have no explanation.
- `StripePaymentReconciliationService.cs:75-77` takes a Postgres advisory lock with `pg_advisory_xact_lock(hashtextextended(sessionId, 0))`. Why that lock, why that hash and what it protects against are not stated.
- `CreateOrderWithInventoryAsync` (`InventoryReservationService.cs:39-275`) is **236 lines, nesting depth 5, 26 branches, 13 return statements (8 of them early exits through `RollBackAsync`)**.

The rules are written down elsewhere (`docs/order-lifecycle.md` is good), but the reasoning is not next to the code that depends on it.

**Fix:** add "why" comments at each lock and ordering decision, extract named steps (for example `LockProductsInOrder`), and link the doc.

#### 4. The admin pages are oversized components doing UI, data and logic together (functions that do more than one thing)

| File | Lines | Comment lines | Methods |
|---|---|---|---|
| `Web/Pages/Admin/Products.razor.cs` | 727 | 0 | 34 |
| `Web/Pages/Admin/Dashboard.razor.cs` | 463 | 0 | 25 |
| `Web/Pages/Payments/Cart.razor.cs` | 396 | 0 | 20 |

(Under `BlazorShop.Presentation/BlazorShop.Web/`.) `Products.razor.cs` mixes loading the catalog, category filtering (`ToggleCat`, `ToggleAllCats`), product create/edit/delete, variant create/edit/delete, SEO editing, and **image upload with validation** inside the component. It also has twin methods for "add" and "edit" modes that repeat the same work: `OnFileSelected` (68 lines, `:454`) and `OnEditFileSelected` (61 lines, `:523`), `OnScaleChanged` and `OnScaleChangedAdd`, `AddVariantAsync` and `AddVariantForNewAsync`, `LoadVariantsAsync` and `LoadNewVariantsAsync`. `Dashboard.razor.cs` aggregates revenue and top products on the client (`:100, 105, 162`) and builds CSV exports (`:428-461`), although a `MetricsController` and `MetricsService` exist on the server.

**Fix:** split into child components (variant editor, SEO editor, uploader), move upload validation, aggregation and CSV export into services, and merge each add/edit pair into one parameterized method.

#### 5. Failures on side effects vanish without a trace (missing error handling for external calls)

Error handling around **Stripe, SMTP and HTTP calls is mostly good**. The weak spots are the side effects around them, where a failure leaves no log line:

- `BlazorShop.Application/Services/NewsletterService.cs:44-58`: the welcome email is sent in a fire-and-forget `Task.Run` whose `catch` is `/* ignore */` (`:53`), so an SMTP failure is invisible and the caller is told "Subscribed successfully". The earlier `catch` at `:39` turns every database error into the same generic "Subscription failed." with no logging of the cause.
- `BlazorShop.Infrastructure/Services/OrderTrackingService.cs:203`: the tracking email is best-effort (the comment explains why), but a failed send is not logged either.
- `BlazorShop.Presentation/BlazorShop.Web/Components/Header/HeaderBoxComponent.razor.cs:74,134`: empty `catch { }` around JavaScript interop. A real fault in the cart badge would never surface.
- `BlazorShop.Presentation/BlazorShop.Web/Components/Notifications/Toast.razor.cs:15` is an `async void` event handler. An exception inside it cannot be caught by the caller and can take down the page or circuit.

**Fix:** log inside each of these catches, send mail through a background queue or hosted service, replace `async void` with a method that wraps its body in try/catch, and catch the specific interop exception instead of everything.

---

### Considered but not in the top five

| Item | Why it ranked lower |
|---|---|
| `API/Program.cs:40` `Main`, 234 lines and 35 branches | Does about ten jobs (CLI modes, logging, options, DI, CORS, rate limits, pipeline, migrate, admin bootstrap, seed), but startup methods like this are common and it is well structured top to bottom. |
| `Infrastructure/Data/DevelopmentCatalogSeeder.cs:11` `SeedAsync`, 299 lines | Mostly data written as code, not logic. |
| `ResolvedCartLine`, `CheckoutCartLine` and the Storefront `CartLine` with 10-11 positional parameters | Records with many fields; awkward but safe. |
| `AppDbContext.OnModelCreating` (128 lines) and `AddInfrastructure` (154 lines) | Long, but linear configuration. |
| `PriceFormatter` (ours) keeps its currency in static mutable state (`CurrencyCode`, `Configure`) | The one item in this review that **we** wrote. Fine for a single-store app, but global state is harder to test. |

### What is done well

- **Stripe error handling is thorough**: `StripePaymentService.Pay` (`BlazorShop.Infrastructure/Services/StripePaymentService.cs:38-146`) has specific catches for Stripe errors, HTTP failures, timeouts and cancellation, each mapped to a safe "retry with the same key" outcome.
- Timeouts and cancellation exist on the other external calls: SMTP (`EmailService.cs:45`) and the Storefront's API client (`StorefrontApiClient.cs:18-20`).
- A global exception middleware turns unhandled errors into a safe JSON response with a trace id (`BlazorShop.Infrastructure/ExceptionsMiddleware/ExceptionHandlingMiddleware.cs`).
- Names are generally descriptive (for example `MarkStripeReconciliationRequiredAsync`), so the code reads reasonably despite the size.

### Method and limits

- **Scan:** 541 production C# files and 1,063 methods, excluding tests, migrations and build output. It measured method length, a branching count and nesting as complexity proxies, comment density, and error-handling patterns. 37 methods exceed 60 lines and 12 exceed 100 lines. The parser is approximate (brace counting, not a compiler), so numbers can be off by a few lines.
- **Corrections after reading:** the scan flagged 24 bare `catch` blocks and 8 uses of `.Result`. Reading them showed most bare catches rethrow (`ApplicationTransactionManager.cs:31`, `DemoSessionManager.cs:96,105`) or return a safe default (`StorefrontSessionResolver.cs:120`), and the `.Result` calls I checked (`StorefrontSitemapService.cs:35,41`, `Dashboard.razor.cs:245-246`) come right after `await Task.WhenAll`, which is safe. Those were dropped.
- **Not covered:** the Razor markup itself (only the code-behind), JavaScript, and a full read of every large method. Comment density counts `//` lines only. Self-documenting names reduce the need for comments, so a low count is a signal for the reasoning behind the code, not for what it does.
- **Relation to our policy:** because every item is upstream code, fixing them would mean editing upstream files, which conflicts with "extend, do not edit". They are better handled as upstream contributions, or accepted as known debt in this lab.

---

## Addendum: proposal for the top-priority fix (problem 1, `CheckoutOrchestrator`)

**Status: proposal, since implemented as steps 0 to 4 (see the implementation addendum at the end of this file).** The text below is the proposal as it was written, before any code changed.

### Why this is the one to fix first

Of the five problems, this is the only one on the money path. `CheckoutOrchestrator` decides whether an order is created, whether stock is reserved or released, and whether a Stripe payment is considered done. It is also the hardest file in the repository to change safely: 1,280 lines, 15 constructor dependencies, 3 comment lines, and one method (`InitializeStripeAsync`, 255 lines) with seven separate exits into `MarkStripeReconciliationRequiredAsync`. The other four problems (names, missing comments, long admin pages, silent catches) cost reading time. This one costs correctness every time someone has to change checkout.

### What the code looks like when mapped (not guessed)

I mapped every method to the injected services it uses and to the other methods it calls. The file is really five groups of code that barely touch each other:

| Group | Methods | Lines | Injected services it needs |
|---|---|---|---|
| **Entry and local-payment flow** (what should remain) | `CheckoutAsync`, `ExecuteOwnedCheckoutAsync`, `ResumeCommittedOrderAsync`, `CompleteLocalPaymentAsync`, `ReplayPersistedOutcomeAsync`, `FinishFailureAsync` | about 300 | idempotency store, user manager, order repository, inventory reservation, commerce options, payment transaction store |
| **Stripe flow** | `InitializeStripeAsync` (255), `RecoverStripeProviderStateAsync` (119), `MarkStripeReconciliationRequiredAsync`, `FailDefinitiveInitialStripeAttemptAsync`, `Terminalize*` (2), `Finalize*` (3) | about 590 | payment service, payment transaction store, state transition service, idempotency store/options, logger |
| **Cart and pricing** | `ResolveCartLinesAsync` (79), `ResolvePaymentKindAsync` (16) | about 95 | product read repository, payment method service |
| **Bank-transfer notice** | `BuildBankTransferInfo`, `TrySendBankTransferEmailAsync`, `BuildClientUrl` | about 45 | bank transfer settings, email service, client app options |
| **Pure builders** (no injected services at all) | `CreateOrder`, `CreateStripeInitialization`, `BuildStripeDescription`, `CreateSuccessOutcome`, `CreateFailureOutcome`, `FromPersistedOutcome` | about 150 | none |

The Stripe cluster is the only one with real internal complexity. It calls only itself plus the pure builders, which means it can be lifted out whole without untangling it from the rest.

### Proposed solution

Keep `ICheckoutOrchestrator` (one method, `CheckoutAsync`) and its DI registration (`DependencyInjection.cs:65`) exactly as they are. Callers do not change. Inside the Application project, add new `internal` classes in new files next to the orchestrator and move code into them **without rewriting it**:

| New file | Receives | Result |
|---|---|---|
| `CheckoutOutcomes.cs` (static) | `CreateSuccessOutcome`, `CreateFailureOutcome`, `FromPersistedOutcome` | pure, trivially testable |
| `CheckoutOrderFactory.cs` (static) | `CreateOrder`, `CreateStripeInitialization`, `BuildStripeDescription` | pure |
| `CheckoutCartResolver.cs` | `ResolveCartLinesAsync`, `ResolvePaymentKindAsync` | 2 dependencies |
| `BankTransferNotifier.cs` | `BuildBankTransferInfo`, `TrySendBankTransferEmailAsync`, `BuildClientUrl` | 3 dependencies |
| `StripeCheckoutFlow.cs` | the whole Stripe cluster | 6 dependencies, about 590 lines, still too large but now isolated |
| `CheckoutOrchestrator.cs` | what is left | about 300 lines and about 6 dependencies instead of 15 |

Then, as a **second, separate stage**, work on the one method that is hard to read even after the move: `InitializeStripeAsync`. Give the seven nested `MarkStripeReconciliationRequiredAsync` exits one named helper that takes a reason (an enum), flatten the nesting with early returns, and add the missing comments that say *why* each outcome is non-terminal (the "ambiguous failure keeps the reservation" rule that the tests already name). This is the only stage that changes control flow, so it is kept apart and optional.

**Constructor decision.** To keep the diff in the test projects at zero, the orchestrator's public 15-parameter constructor stays during stage 1 and builds the new collaborators internally. Four call sites construct it today (1 in the unit tests, 1 in the checkout idempotency tests, 2 in the inventory tests). Moving the collaborators into DI properly, and updating those four sites, is a follow-up once the structure has settled.

### The safety net, and its gaps

| Existing protection | What it covers |
|---|---|
| `CheckoutOrchestratorTests` (802 lines, 19 tests, mock-based) | COD, variants, bank transfer incl. email failure, Stripe happy path, Stripe definitive and ambiguous failure, retry deadlines, validation, inventory and variant rejection |
| `CheckoutIdempotencyPostgreSqlTests` (36 tests, 2,042 lines) | Retries and concurrency against a real PostgreSQL |
| `InventoryReservationPostgreSqlTests` (25 tests) | Reservation, release and locking against a real PostgreSQL |

Two gaps to be honest about:

1. **The 61 PostgreSQL tests need Docker** (Testcontainers). Without Docker they cannot run, and they are the tests that matter most for this refactor. They must be run with Docker up before and after every step. CI runs the whole test project on a runner with Docker (`ci.yml`, no filter on the main test step).
2. **Coverage is not measured.** The test project has no coverage tooling, so "well covered" is an assessment from test names, not a number. Before moving a method, a quick check is needed that a test actually fails when that method is broken on purpose.

### Staged plan (each step is its own commit and must leave all tests green)

| Step | Change | Risk |
|---|---|---|
| 0 | Run the full suite with Docker up and record the baseline (789 passed, 10 skipped today). Break each method that is about to move on purpose and confirm a test fails; add a new test file for any that does not. | None to production code |
| 1 | Extract the pure builders and outcome mapping (`CheckoutOutcomes`, `CheckoutOrderFactory`) | Very low: no dependencies, no behavior |
| 2 | Extract `CheckoutCartResolver` | Low |
| 3 | Extract `BankTransferNotifier` | Low (email failure is already tested) |
| 4 | Extract `StripeCheckoutFlow` | Medium: largest move, but a cut-and-paste with the same method bodies |
| 5 (optional) | Flatten `InitializeStripeAsync`, add the *why* comments | Medium: the only step that changes control flow |
| 6 (optional) | Move the collaborators to DI and update the four constructor call sites | Low |

Steps 1 to 4 should leave the orchestrator at about a quarter of its size with no change in behavior. Rollback is `git revert` of a single commit at any point.

### Conflict with the "extend, do not edit" policy

This is a real conflict, not a technicality. `CheckoutOrchestrator.cs` is an upstream file, and this change shrinks it by about 75%. Upstream has changed the file five times (2026-08-20 to 2026-11-01), once since our fork point (`57dbd29`, "Fail closed on invalid bank transfer configuration"), so it is not a dormant file. Once we have moved its code into other files, every future upstream change to it would arrive as a conflict against code that no longer exists there.

Options, in the order I would weigh them:

1. **Do it in the future separate repo instead, and offer it upstream as a pull request.** Cleanest for merges, and upstream benefits. It means waiting.
2. **Do it now in this fork** and accept that this one file will need a manual merge. The additions are new files, so only the one shrunken file is at risk.
3. **Leave it** and record it as accepted debt, as the "Relation to our policy" note above suggests for the whole review.

My recommendation is option 1 for production use of the code, and option 2 if the goal of this lab is to practice the refactor itself. This is a lab, so option 2 is reasonable, provided the file is added to the list of "must stay a patch" files when the separate repo is created.

### What I need from you

Approval of the approach, and a decision on two points: option 1, 2 or 3 above, and whether stage 5 (changing the control flow of `InitializeStripeAsync`) is wanted or only the move in steps 1 to 4. I will not implement or commit anything until you say so.

---

## Addendum: implementation of the proposal (option 2, steps 0 to 4)

You chose **option 2** (do it now, in this fork). Steps 0 to 4 are done and committed locally. **Not done:** step 5 (flattening `InitializeStripeAsync`, which you did not decide on) and step 6 (moving the collaborators into DI). Nothing has been pushed.

### Step 0 found real gaps in the safety net

Before moving anything I broke ten pieces of the code that was about to move, one at a time, and ran the 87 checkout tests (19 unit tests plus the 61 PostgreSQL tests, with Docker). A test that does not fail when the code is broken is not protecting it.

| Deliberate break | Existing tests | After adding 11 tests |
|---|---|---|
| Stripe orders start `Confirmed` instead of `Pending` | caught | caught |
| Email failure escapes the bank-transfer notice | caught | caught |
| Ambiguous Stripe failure treated as definitive | caught (25 tests) | caught |
| Existing paid transaction not finalized | caught (3 tests) | caught |
| Unpublished products can be bought | **missed** | caught |
| Variant stock is not checked | **missed** | caught |
| Failure outcome marked as a success type | **missed** | caught |
| Bank-transfer beneficiary and bank name swapped | **missed** | caught |
| Reconciliation marker does not release the lease | **missed** | caught |
| Stripe line-total check removed | **missed** | caught |

Six of ten breaks went unnoticed, which is why the "well covered" assessment in the proposal was too optimistic. The 11 new tests are in a new file, `BlazorShop.Tests/Application/Services/Payment/CheckoutOrchestratorCharacterizationTests.cs`. They passed against the unsplit code first, then stayed unchanged through every extraction.

### What was done

| Commit | Step | Moved to |
|---|---|---|
| `520d135` | 0 | Characterization tests (above) |
| `7522810` | 1 | `CheckoutOutcomes` (static) and `CheckoutOrderFactory` (static) |
| `da02eff` | 2 | `CheckoutCartResolver` |
| `6bc3142` | 3 | `BankTransferNotifier` |
| `5fe3a01` | 4 | `StripeCheckoutFlow` |

Each step was built with warnings as errors and followed by the whole test suite (Docker up) before its commit.

| | Before | After |
|---|---|---|
| `CheckoutOrchestrator.cs` | 1,280 lines | **387 lines** |
| Injected fields in the orchestrator | 15 | **10** (the public constructor still takes 15 parameters, so no caller or test changed) |
| New files | | `StripeCheckoutFlow` 708, `CheckoutCartResolver` 110, `BankTransferNotifier` 75, `CheckoutOrderFactory` 73, `CheckoutOutcomes` 52 |
| Tests | 789 passed, 10 skipped | **800 passed, 0 failed, 10 skipped** (the same 10 smoke tests that need a running site) |
| Build | | 0 warnings, 0 errors with `-warnaserror` |

The five files add up to more lines than the original because each repeats a `namespace`, `using` and class wrapper.

### How I checked that the logic did not change

- Method bodies were cut out of the original file by line range with a script and pasted unchanged, not retyped.
- A line-by-line comparison of the original against the union of the six files shows that the only lines that differ are declarations, field and constructor wiring, call sites and `using` lines. The 18 original lines that no longer exist are all renamed declarations or the calls to them (for example `ResolveCartLinesAsync(...)` became `_cartResolver.ResolveCartLinesAsync(...)`).
- The collaborators receive the orchestrator's own `ILogger<CheckoutOrchestrator>`, so log categories are identical.

### Where the result differs from the proposal

1. **`ResolvePaymentKindAsync` stayed in the orchestrator.** It maps a payment method id to a kind and has nothing to do with carts, so putting it in `CheckoutCartResolver` would have made that class worse.
2. **The Stripe initialization builder moved into `StripeCheckoutFlow`, not into the static factory.** `CreateStripeInitialization` needs the client URL setting, and that is used by nothing else. So the flow takes seven dependencies, not six.
3. **The orchestrator is 387 lines with 10 fields, not about 300 and 6.** `ReplayPersistedOutcomeAsync` (45 lines, uses the payment transaction store), `FinishFailureAsync`, `ResolvePaymentKindAsync` and the order-creation logic in `ExecuteOwnedCheckoutAsync` (112 lines) stayed. Moving them would have meant rewriting rather than moving.

### What this did not fix

The most serious part of problem 1 is **moved, not solved**. `InitializeStripeAsync` is still 255 lines with seven exits into `MarkStripeReconciliationRequiredAsync`, and it still has almost no comments. It now lives in a file called `StripeCheckoutFlow.cs` where it is the file's whole subject, which makes it easier to find and to test, but it is not easier to read. That is step 5, still waiting for your decision. The same goes for the `CheckoutOrchestrator` constructor, which still takes 15 parameters until step 6.

### Not verified

- **GitHub CI** has not run on these commits because nothing is pushed.
- **No live checkout was done.** A real checkout needs a signed-in customer, and I do not handle your password. The unit tests and the PostgreSQL tests exercise the same code paths.

### Cost against the extension policy

`CheckoutOrchestrator.cs` is now an upstream file with 915 lines deleted and 22 added against the fork point. Counted against the fork point: **965 identical (was 966), 79 modified (was 78), 15 deleted, 46 new (was 40)**. Upstream will keep changing this file: its most recent change, `57dbd29` ("Fail closed on invalid bank transfer configuration", not merged here), edits the constructor, the bank-transfer settings field and `ExecuteOwnedCheckoutAsync`, so merging it onto the split file would conflict in several places and have to be redone by hand. It belongs on the "must stay a patch" list when the separate repo is created, unless the same split is accepted upstream as a pull request.
