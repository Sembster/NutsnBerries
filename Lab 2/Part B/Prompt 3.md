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
