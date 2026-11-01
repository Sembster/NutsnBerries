# Prompt 1

## Prompt

> Granska kodbasen mot de arkitekturprinciper jag definierat i CLAUDE.md. Var bryter koden mot principerna? Var är den i linje med dem? Ge konkreta filreferenser.

## Answer (English)

### Summary

I reviewed the code against the ten architectural principles and the "extend, do not edit" policy in `CLAUDE.md`. Overall the code follows the principles well where they describe structure (layering, payments, authorization, migrations). It breaks them mostly where `CLAUDE.md` states a rule more strongly than the code delivers it, and in leftovers from upstream.

| # | Principle | Verdict | Headline |
|---|---|---|---|
| 1 | Dependencies point inward | Mostly aligned | Project references and `using`s are clean. The Domain project still depends on the ASP.NET Identity package. |
| 2 | Thin controllers and pages | Partly aligned | Most controllers delegate. `FileUploadController` does file handling itself, and three admin pages are very large. |
| 3 | Validate at the boundary, fail fast | Partly aligned | Only auth and SEO have validators. Product, category and newsletter input is barely validated, and the JWT key is unchecked. |
| 4 | One place for money rules | Mostly aligned | Stripe and checkout use `CurrencyMoney`. About 25 inline price formats remain in the Web client, and one in a server email. |
| 5 | Orders keep their own currency | Mostly aligned | Done correctly for orders. A second, unconnected `DefaultCurrency` setting is misleading. |
| 6 | Idempotent checkout, reconciled payment state | Aligned | Idempotency key, webhook signature and a single payment-status writer are all in place. A dead PayPal stub remains. |
| 7 | Public read path vs admin writes | Aligned | All 81 actions checked. The known pitfall is that nothing enforces an attribute on new actions. |
| 8 | Server-render what search engines need | Aligned, with dead code | Public routes live in the Storefront. 25 unrouted legacy files remain in the WASM client. |
| 9 | Schema changes only through migrations | Aligned | No violations found. |
| 10 | Layered config, secrets out of git, resilience, observability | Mostly aligned | Resilience and validation exist, but with gaps. The Storefront has no Serilog, and its 2 s timeout caused the cold-start errors we saw. |

**Method and limits.** This is a static review of the source at commit `e0bfb0f`. I used targeted searches and read the relevant code. I did not run the whole application for this review, though I did run the stack earlier in the session. Paths are relative to `Lab1/NutsnBerries`, and line numbers are as of that commit.

---

### 1. Dependencies point inward: mostly aligned

**In line**
- Project references follow the rule: `BlazorShop.Domain` references nothing, `Application` references `Domain`, `Infrastructure` references `Application`, `API` references `Infrastructure` (`*.csproj` files).
- No layer-violating `using`s: `Application` has no `Microsoft.AspNetCore.Http/Mvc`, Stripe, MailKit or EF Core usings, and `Infrastructure` has no UI usings.
- `Storefront` and `Web` never reference `Infrastructure`. They reach data only through the API.

**Breaks**
- `BlazorShop.Domain` carries the `Microsoft.Extensions.Identity.Stores` package (`BlazorShop.Domain/BlazorShop.Domain.csproj`), and `Entities/Identity/AppUser.cs:3` derives from ASP.NET Identity. The innermost layer therefore depends on a framework. "Domain references nothing" is true only for project references.
- Three controllers use concrete `Infrastructure` types rather than Application abstractions: `DemoController.cs:7`, `FileUploadController.cs:6`, `StripeWebhookController.cs:5`. This is allowed (the API references Infrastructure) but bypasses the Application layer.
- `BlazorShop.Infrastructure.csproj` carries web-host packages (`Microsoft.AspNetCore.Identity.UI`, `JwtBearer`, `Serilog.AspNetCore`), blurring the line between infrastructure and the host.

### 2. Thin controllers and pages: partly aligned

**In line**
- `ProductController.cs:28-60` and `CategoryController.cs:28-110` only call a service and return the result.
- No page or component injects `HttpClient` directly; all calls go through service classes.
- `Storefront/Pages/CartPage.razor.cs:26-28,172-176` sums line totals only for display. The authoritative total is calculated server-side in `CheckoutOrchestrator`.

**Breaks**
- `FileUploadController.cs:20-95` (117 lines) contains real logic: size limit, content-type to extension map, image signature check, path building and writing the file to disk. This belongs in a service.
- Very large admin pages hold a lot of logic: `Web/Pages/Admin/Products.razor.cs` (727 lines), `Admin/Dashboard.razor.cs` (463 lines) and `Payments/Cart.razor.cs` (396 lines).
- The admin dashboard computes revenue and top products on the client by summing orders (`Admin/Dashboard.razor.cs:100,105,162`), even though a `MetricsController` and `MetricsService` exist.

### 3. Validate at the boundary and fail fast: partly aligned

**In line**
- FluentValidation validators exist for authentication (`Application/Validations/Authentication/`: create user, login, change password) and SEO (`Validations/Seo/`: five validators plus a shared rules helper). These services use them through `IValidationService`.
- Startup options validation (`ValidateOnStart`) covers `CommerceOptions` and `ClientAppOptions` (`Application/DependencyInjection.cs:42-59`), `DemoOptions`, `CheckoutIdempotencyOptions` and `EmailSettings` (`Infrastructure/DependencyInjection.cs:44-55,174-190`), `ApiRuntimeOptions` (`API/Program.cs:64-98`) and the Storefront URL options (`Storefront/Program.cs:28-36`).

**Breaks**
- **`CLAUDE.md` overstates this.** It says "Incoming requests go through FluentValidation". There are no validators for products, categories, variants, cart or newsletter. `ProductService.AddAsync` (`Application/Services/ProductService.cs:66-79`) maps the DTO and saves it.
- `[Required]` on value types is a no-op: `Application/DTOs/Product/ProductBase.cs:13-24` marks `Price`, `Quantity` and `CategoryId` required, but a `decimal`, `int` or `Guid` is never null. There is no range check (negative prices pass) and no length limit on name or description (the database columns are unbounded `text`).
- Newsletter: `NewsletterService.cs:24` only checks for an empty string, and `SubscribeEmailDto` (`NewsletterController.cs:32`) has no attributes, so any text is accepted on an anonymous endpoint.
- Options bound without validation: `StripeOptions` (`Infrastructure/DependencyInjection.cs:171-172`), `BankTransferSettings` (`:191`), `RecommendationOptions` (`Application/DependencyInjection.cs:41`) and the Storefront's `DemoOptions` (`Storefront/Program.cs:23`).
- **The JWT signing key is never validated.** It is read with `config["JWT:Key"]` (`Infrastructure/DependencyInjection.cs:128`, `AppTokenManager.cs:159`), and `appsettings.json:15` ships the placeholder `<set-in-user-secrets>`. If the secret is missing, the app starts and signs tokens with that publicly known string. This is low risk for a lab and high risk anywhere real.

### 4. One place for money rules: mostly aligned

**In line**
- Minor-unit conversion for Stripe goes through `CurrencyMoney`: `CheckoutOrchestrator.cs:319,1093`, `StripePaymentReconciliationService.cs:307`.
- No `float` or `double` is used for money (the only `double` uses are paging ceilings, `Domain/Contracts/PagedResult.cs:15`).
- The Storefront formats all visible prices through `PriceFormatter` (`ProductCard.razor`, `ProductPage.razor`, `CartPage.razor.cs`). Its `ToString("0.00", Invariant)` calls at `ProductCard.razor:123` and `ProductPage.razor:210,333` are deliberate machine-readable data attributes for JavaScript.

**Breaks**
- About 25 inline `.ToString("F2")` price displays remain in the Web client: `Account/Orders.razor:79-92,137,158`, `Account/Dashboard.razor:76`, `Admin/Orders.razor:111-112,139,229-241,422`, `Admin/Dashboard.razor:258,325`, `Admin/Dashboard.razor.cs:437` and `Payments/Cart.razor:187`. They print the order's currency code in front of the number ("SEK 12.30"), which differs from `PriceFormatter`'s "12,30 kr".
- `MetricsService.cs:76-77,102,121` hardcodes `decimal.Round(x, 2)` instead of using `CurrencyMoney`.
- `CheckoutOrchestrator.cs:1168` formats the amount in the bank-transfer email with `{info.Amount:F2} {info.Currency}`. **Structural gap:** `PriceFormatter` lives in `Web.Shared`, a UI project, so server code cannot use it and has to format prices itself.
- The list of supported currencies is a private static dictionary (`CurrencyMoney.cs:7-12`), so it cannot be extended without editing upstream.

### 5. Orders keep their own currency: mostly aligned

**In line**
- `Order.Currency` (`Domain/Entities/Payment/Order.cs:33`) and `PaymentTransaction.Currency` (`PaymentTransaction.cs:17`) are filled once from `CommerceOptions` at order creation (`CheckoutOrchestrator.cs:184`).
- Later steps read the order's own currency, not the store setting: `StripePaymentReconciliationService.cs:299,307`, `CheckoutOrchestrator.cs:319,1093-1094`. The column is fixed at 3 characters (`OrderPaymentConfiguration.cs:37`).

**Breaks**
- A second currency setting, `AdminSettings.DefaultCurrency` (`Infrastructure/Services/Admin/AdminSettingsService.cs:75,178,219`), is validated only by a regex (`^[A-Z]{3}$`, `:24,248`), not by `CurrencyMoney.IsSupportedCurrency`. Nothing in checkout reads it. An admin can set it to JPY in Settings and nothing changes, although it looks authoritative. (Our edit changed its entity default to SEK at `Domain/Entities/AdminSettings.cs:13`.)

### 6. Idempotent checkout, reconciled payment state: aligned

**In line**
- `CartController.cs:36-56` requires a GUID `Idempotency-Key` header. `CheckoutOrchestrator.cs:75,102,110` rejects a key reused for a different checkout.
- Stripe webhook signatures are verified (`StripeWebhookEventParser.cs:12`, `EventUtility.ConstructEvent`) and a webhook secret is required (`StripeWebhookService.cs:38`).
- Payment status has one writer: `InventoryReservationTransitionOperation.cs:34,62,151`, plus the initial `Pending` at `CheckoutOrchestrator.cs:1056`. No stray setters exist, as `docs/order-lifecycle.md` describes.

**Breaks and leftovers**
- Dead PayPal path: `PaymentController.cs:43-51` exposes an anonymous `GET api/payment/paypal/capture`, but `Infrastructure/Services/PayPalPaymentService.cs:13-16` always returns `false`, so it always redirects to the cancel page. Harmless, but it is misleading dead code, and `CLAUDE.md` lists it among the anonymous endpoints.
- Two admin write paths for the same order transitions: `CartController.cs:106-160` (list orders, tracking, shipping status, via `IOrderTrackingService`) duplicate `AdminOrdersController` (via `IAdminOrderService`).

### 7. Public read path vs admin writes: aligned

**In line (all 81 actions checked)**
- Every admin read and every state-changing admin action requires the `Admin` role, either on the class (all nine `Admin*` controllers and `MetricsController`) or per action (`ProductController`, `CategoryController`, `ProductVariantController`, `FileUploadController`, and the admin actions in `CartController`).
- All anonymous catalog `GET`s go through `IPublicCatalogService`, and `IsPublished` is enforced in the repositories (`CategoryRepository.cs:35,46`, `ProductReadRepository.cs:79,107`). Recommendations are also published-only (`ProductRecommendationRepository.cs:37-46,88-93,108-115`). Checkout rejects unpublished products (`CheckoutOrchestrator.cs:957`).
- Anonymous writes are limited to register, login, refresh-token, logout, newsletter subscribe, demo sessions and the Stripe webhook.

**Risk, not a violation**
- There is no fallback authorization policy (nothing like `FallbackPolicy` in `API/Program.cs`), so a new action with no attribute is public. Nothing enforces the rule in `CLAUDE.md`. A small reflection test over the controllers would.

### 8. Server-render what search engines need: aligned, with dead code

**In line**
- All public catalog routes live in the server-rendered Storefront. The WASM client routes only to account, admin, auth, checkout continuation and payment results: `Pages/Payments/Cart.razor:1` (`/account/checkout`), `SuccessPayment.razor:1-2`, `CancelPayment.razor:1`, `WorkspaceEntry.razor` (`/`).

**Breaks**
- 25 unrouted legacy files (1,864 lines, none with an `@page`) remain in the WASM project under `Web/Pages/Public/*` and `Web/Pages/Products/*` (`MainPage`, `LatestProducts`, `NewReleases`, `TodaysDeals`, `ProductDetails`, About, FAQ, Terms, Privacy and others). They are referenced only by each other. They ship in the bundle but nothing can navigate to them.
- We edited 11 of these dead files (price formatting, branding text), which cost modifications for no visible effect. See the policy section below.

### 9. Schema changes only through migrations: aligned

- Startup runs `DatabaseMigrationBootstrapper.MigrateAsync` (`API/Program.cs:177`, which calls `Database.MigrateAsync` at `DatabaseMigrationBootstrapper.cs:84`). There is no `EnsureCreated`.
- 11 migrations exist in `Infrastructure/Migrations` (latest `20260909075202_ArchiveLegacyCheckoutOrderItems`), with a guard test that the model matches them (`BlazorShop.Tests/Infrastructure/MigrationModelConsistencyTests.cs`).
- Raw SQL is limited to the legacy-database baseline (`DatabaseMigrationBootstrapper.cs:118,127`) and row-claiming DML in the payment code (`PaymentTransactionStore.cs:48`, `StripePaymentReconciliationService.cs:55,75`). None of it changes the schema.
- Seeding is idempotent: `DevelopmentCatalogSeeder.cs` upserts by fixed ID, and `NutsnBerriesSeeder.cs` guards with `AnyAsync` and never overwrites an edited row.
- No violations found.

### 10. Config layering, secrets, resilience, observability: mostly aligned

**In line**
- Secrets are not committed: appsettings hold placeholders (`appsettings.json:11,15,28-29,64`), and the real values live in user-secrets and the gitignored `.env`.
- Resilience is configured once for all HTTP clients (`ServiceDefaults/Extensions.cs:26-29`, `AddStandardResilienceHandler`).
- Health endpoints are mapped on the API and Storefront. The API logs with Serilog.

**Breaks**
- **The Storefront does not use Serilog** (no references in `Storefront/Program.cs`), so `CLAUDE.md` claiming structured Serilog logging is true only for the API.
- **The Storefront overrides the resilience defaults with a 2 s catalog timeout** (`StorefrontApiClient.cs:18`, plus 500 ms timeouts for SEO settings and redirect lookups at `:19-20`). This is what produced the "Catalog temporarily unavailable" page on a cold API, which we hit repeatedly.
- A known demo password is committed (`appsettings.json:42`, `"Password": "Demo123!"`). It matters only if demo mode is enabled, but it is a credential in source.
- The unvalidated options and the unchecked JWT key from principle 3 also belong here.

---

### Policy check: "extend, do not edit"

Measured against the fork commit `3944038`: about 75 upstream-owned files differ from upstream, with roughly 650 lines added and 609 removed.

| Area | Files | Lines |
|---|---|---|
| Web client | 28 | +53 / -43 |
| Storefront | 22 | +69 / -62 |
| Application, Domain, Infrastructure, docs, config | 13 | +451 / -442 |
| CI workflows | 5 | +48 / -39 |
| Tests | 5 | +24 / -19 |
| API | 3 | +5 / -4 |

**In line**
- New files rather than edits: `PriceFormatter.cs`, `nutsnberries-theme.css` (with a one-line hook in `App.razor`), `NutsnBerriesSeeder.cs` (with a one-line hook in `Program.cs`), `compose.dev.yml`, `nuget.config` and their tests. `storefront.css` is byte-identical to upstream again.

**Conflicts with the policy**
- About two thirds of all churn is one file: `DevelopmentCatalogSeeder.cs` was fully rewritten (about 430 lines added and 430 removed). This is the biggest merge conflict waiting to happen and the next extension candidate.
- Around 50 small text and price edits spread over about 50 Razor and C# files in the Web client and Storefront. Some are generic and could go upstream (currency symbols via a formatter), and some are unavoidable hardcoded text.
- 11 of those edits are in the dead legacy files from principle 8, and should be reverted or the files removed rather than maintained.
- CI workflow and test edits (7 files) follow from the repo move and the SEK change.

---

### Where `CLAUDE.md` should be corrected

1. **Principle 1:** say that `Domain` has no *project* references, and mention the Identity package.
2. **Principle 3:** "Incoming requests go through FluentValidation" is currently true only for authentication and SEO.
3. **Principle 10:** structured Serilog logging applies to the API only, and the Storefront uses its own short timeouts.
4. **Principle 7:** the PayPal endpoint is a stub that always cancels.

### Suggested priority

1. Add validation for product, category, variant and newsletter input, and validate the remaining options and the JWT key at startup.
2. Remove dead code: the PayPal endpoint, the duplicate admin order endpoints in `CartController`, and the 25 unrouted WASM files (reverting our edits there).
3. Add a reflection test that every controller action has an explicit authorization attribute.
4. Move file handling out of `FileUploadController`, and decide whether `AdminSettings.DefaultCurrency` should exist.
5. Move money formatting rules into a layer the server can use, so emails and Razor share them.
6. Move the sample catalog out of `DevelopmentCatalogSeeder` into `NutsnBerriesSeeder`.
