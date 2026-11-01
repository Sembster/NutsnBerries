# NutsnBerries

## What this project is

NutsnBerries is a webshop for natural beauty products (skincare, hair care, body care, bath and soap, lips and face, gift sets), with a nature-inspired look: forest green, sage, cream and berry. Customers browse the catalog, add products to a cart and check out. An admin area manages products, categories, orders, inventory, users, SEO and settings. Prices are in SEK.

It is a fork of [unrealbg/BlazorShop](https://github.com/unrealbg/BlazorShop) (MIT), rebranded and adapted. **This is a lab for an online course, not a real shop.** Placeholder copy, sample products and demo-grade configuration are fine. Do not add production-hardening work (real SMTP/Stripe/bank details, deployment hardening, legal or marketing claims) unless asked.

## Repository layout

The git root is `Labs`; the project lives one level down:

```
Labs/                      <- git root, .github/ (CI workflows), this file
└── Lab1/
    └── NutsnBerries/      <- the solution (BlazorShop.sln, compose files, docs)
```

Inside `Lab1/NutsnBerries`:

| Folder | Role |
|---|---|
| `BlazorShop.Domain` | Entities and contracts |
| `BlazorShop.Application` | DTOs, services, FluentValidation validators, currency/money rules |
| `BlazorShop.Infrastructure` | EF Core, repositories, Identity, email, Stripe, sample-catalog seeder |
| `BlazorShop.Presentation/BlazorShop.API` | ASP.NET Core Web API (applies migrations on startup) |
| `BlazorShop.Presentation/BlazorShop.Storefront` | Public shop: Blazor Web App, server-rendered (SEO-friendly routes) |
| `BlazorShop.Presentation/BlazorShop.Web` | Blazor WebAssembly client: sign-in, account, admin |
| `BlazorShop.Presentation/BlazorShop.Web.Shared` | Models, services and `PriceFormatter` shared by Storefront and Web |
| `BlazorShop.AppHost`, `ServiceDefaults` | .NET Aspire orchestration and shared telemetry (upstream) |
| `BlazorShop.Tests`, `BlazorShop.E2E`, `BlazorShop.StagingRehearsal` | Unit tests, Playwright browser tests, upstream staging rehearsal |

Namespaces and project names deliberately stay `BlazorShop.*` so upstream changes remain easy to merge. Only user-visible text and styling are rebranded. The `upstream` git remote points at unrealbg/BlazorShop with pushing disabled.

## Technology stack

| Area | Choice |
|---|---|
| Language / runtime | C# on .NET 10 (SDK 10.0.x, see `global.json`) |
| UI | Blazor: Blazor Web App (Storefront, server-rendered) and Blazor WebAssembly (Web client) |
| Styling | Tailwind CSS 3.4 (built by npm, needs Node 22+/24) plus hand-written `storefront.css` |
| API | ASP.NET Core Web API, Swagger/OpenAPI |
| Data access | Entity Framework Core 10 with `Npgsql.EntityFrameworkCore.PostgreSQL` |
| Database | PostgreSQL 16, run in Docker |
| Auth | ASP.NET Core Identity, JWT access tokens plus refresh-token cookie, roles `Admin` and `User` |
| Mapping / validation | AutoMapper, FluentValidation |
| Payments | Stripe (card), cash on delivery, bank transfer |
| Email | MailKit (SMTP) |
| Logging | Serilog |
| Orchestration | .NET Aspire AppHost (optional), Docker Compose |
| Tests | xUnit, Moq, Testcontainers (PostgreSQL), Playwright |
| CI | GitHub Actions in `Labs/.github/workflows` |

## Why this stack

These choices come from the requirements for the lab and from constraints found while building it.

- **Blazor on .NET 10.** Required by the lab brief. Blazor lets the whole shop be written in C#, and .NET 10 is the current release the upstream project targets.
- **Forking BlazorShop.** Also part of the brief. It already provides a layered solution with catalog, cart, checkout, Identity and an admin area, so the lab can focus on adapting a real codebase rather than writing one from scratch.
- **PostgreSQL instead of MySQL.** The original plan was MySQL in Docker. BlazorShop is built and tested against PostgreSQL (EF Core migrations, Npgsql, Testcontainers tests), and the usual MySQL provider, Pomelo, had no stable EF Core 10 release (latest stable was 9.0.0). Oracle's `MySql.EntityFrameworkCore` 10.x existed, but porting meant regenerating migrations and re-verifying queries. The decision was to keep PostgreSQL, which still satisfies the "database in a Docker container" requirement.
- **Docker for the database.** `compose.dev.yml` runs Postgres with a named volume and localhost-only port, so the lab needs no local database install and data survives restarts. Password lives in a gitignored `.env`.
- **Server-rendered Storefront plus WASM client.** This is upstream's architecture: the public shop is server-rendered so product and category pages are crawlable and fast; sign-in, account and admin run as an interactive WASM app.
- **Trimmed rebrand, not a rewrite.** Namespaces stay `BlazorShop.*` to keep merges with upstream cheap; the nature theme is layered on top in `storefront.css`.
- **SEK via a central `PriceFormatter`.** Upstream only supported EUR/GBP/USD and hardcoded currency symbols. SEK was added to `CurrencyMoney`, and all display goes through `PriceFormatter` (configured from `Commerce:Currency`) so the symbol and number format live in one place.
- **Plain-HTTP local setup.** The ASP.NET dev HTTPS certificate is not trusted on this machine, and trusting it is a security-store change for the user to make. Local runs therefore use HTTP with environment-variable overrides.

## Architectural principles

These describe how the code is structured today and the rules to follow when changing it.

1. **Dependencies point inward.** `Domain` references nothing; `Application` references `Domain`; `Infrastructure` references `Application`; the API references `Infrastructure`. The Storefront (references `Application` and `Web.Shared`) and the Web client (references `Domain` and `Web.Shared` only) never reference `Infrastructure` and reach data only through the API over HTTP. `Application` contains no EF Core or Npgsql code; persistence lives in `Infrastructure` behind contracts. Do not add a reference that points outward.
2. **Keep controllers and pages thin.** Controllers delegate to Application services and return the result; business rules live in `Application`. Razor pages and code-behind map input to a service call and render the response.
3. **Validate at the boundary and fail fast.** Incoming requests go through FluentValidation. Configuration uses the options pattern with `ValidateOnStart()` (for example `Commerce:Currency`), so a bad setting stops startup instead of failing at checkout.
4. **One place for money rules.** Amounts are `decimal`. Rounding and minor units go through `CurrencyMoney`; display goes only through `PriceFormatter`. Never hardcode a currency symbol or format a price inline.
5. **Orders keep their own currency.** The purchase currency is copied onto each order and payment transaction when it is created, so changing the store currency never reinterprets old orders.
6. **Idempotent checkout, reconciled payment state.** Checkout uses idempotency keys, so a retry must never create a second order. For Stripe, `PaymentTransaction.Status` is authoritative and `Order.PaymentStatus` changes only through Stripe webhook reconciliation; there is deliberately no generic payment-status setter (see `docs/order-lifecycle.md`).
7. **Separate the public read path from admin writes, and authorize explicitly.**
   - Public catalog endpoints (the slug endpoints in `PublicCatalogController`, and the anonymous `GET`s on `ProductController` and `CategoryController`) all go through `IPublicCatalogService`, which returns only published items; the `IsPublished` filtering is enforced in the repositories.
   - Every admin read and every state-changing admin action requires `[Authorize(Roles = "Admin")]`, either on the class (all `Admin*` controllers and `MetricsController`) or on each action (`Product`, `ProductVariant`, `Category`, `FileUpload`, and the admin actions in `Cart`). Checkout requires the `User` role.
   - **There is no global fallback authorization policy, so an action without an attribute is anonymous.** Every new action must carry an explicit attribute, or live in a controller that has one at class level. Anonymous writes are limited to register, login, refresh-token and logout, newsletter subscribe, demo sessions, the Stripe webhook and the legacy PayPal capture redirect.
8. **Server-render what search engines need.** Public catalog pages stay in the server-rendered Storefront with stable, slug-based URLs. Only sign-in, account and admin belong in the interactive WASM client.
9. **The schema changes only through EF migrations.** No hand-edited tables. The API applies migrations on startup. Seed data is idempotent: `DevelopmentCatalogSeeder` upserts by fixed ID, so running it twice is harmless.
10. **Layered configuration, secrets out of git, resilient and observable services.** Settings come from `appsettings`, then environment variables, then user-secrets, and no secret is committed. Service-to-service HTTP uses the resilience handler (timeouts and retries), and the API exposes health endpoints and structured Serilog logging.

## Customization policy: extend, do not edit

NutsnBerries is a fork of upstream BlazorShop, and the plan is to move it into a separate repo later where upstream updates can be merged with little manual work. We are skipping upstream merges for now. To keep that future merge cheap, **customizations should be extensions on the base code, not modifications of upstream files.**

At the fork point upstream had 1,059 files. Today 971 are identical to upstream, 73 are modified, 15 were deleted (the old fashion product images) and 23 are new. Every modified file is a possible merge conflict, so the goal is to stop that number growing and to shrink it over time.

When making a change:

1. **Prefer new files over edits.** Put our own code in new, clearly named files or projects (as with `PriceFormatter.cs`), for example a separate CSS file for the theme or our own seeder class, instead of editing upstream's `storefront.css` or `DevelopmentCatalogSeeder`.
2. **Use configuration before code.** If a setting already exists (such as `StorefrontSeo:SiteName` or `Commerce:Currency`), change the setting rather than the code that reads it.
3. **If an upstream file must change, keep the edit minimal.** A one-line hook that calls into our own code is better than rewriting the file. Keep the diff small and localized.
4. **Prefer changes upstream could accept.** Generic improvements (for example making currency or the brand name configurable) are good candidates to offer upstream as pull requests. Once merged upstream they stop being our private diff.
5. **Know the limits.** Upstream hardcodes some things in Razor pages (prices, brand text, hero copy), and a page cannot be extended from outside, so those stay as small modifications.

Existing modifications are not being refactored yet. When the separate repo is created, sort the modified files into "can become an extension", "should go upstream" and "must stay a patch".

## Running locally

Everything runs over plain HTTP. Run from `Lab1/NutsnBerries` (bash syntax; on Windows Docker Desktop must be running first):

```bash
# 1. Database (needs .env with NUTSNBERRIES_DB_PASSWORD; copy .env.example)
docker compose -f compose.dev.yml up -d

# 2. API -> http://localhost:5282   (start first, then request the catalog once to warm it)
ClientApp__BaseUrl=http://localhost:5209 \
  dotnet run --project BlazorShop.Presentation/BlazorShop.API --launch-profile http --no-restore

# 3. Storefront -> http://localhost:18598
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:18598 \
Api__BaseUrl=http://localhost:5282/api/ PublicUrl__BaseUrl=http://localhost:18598 \
ClientApp__BaseUrl=http://localhost:5209/ \
  dotnet run --project BlazorShop.Presentation/BlazorShop.Storefront --no-launch-profile --no-restore

# 4. Web client (sign-in, account, admin) -> http://localhost:5209
ASPNETCORE_URLS=http://localhost:5209 \
  dotnet run --project BlazorShop.Presentation/BlazorShop.Web --no-launch-profile \
  -p:SkipNpm=true -p:WasmApplicationEnvironmentName=Local
```

If the API has not been warmed, the Storefront can time out on its first request and show "Catalog temporarily unavailable"; a refresh fixes it. First-time setup also needs the API's user-secrets (`ConnectionStrings:DefaultConnection`, `Jwt:Key`) and the one-off admin bootstrap (`AdminBootstrap:*` secrets, then `dotnet run --project BlazorShop.Presentation/BlazorShop.API -- --bootstrap-admin`). More detail is in `Lab1/NutsnBerries/NUTSNBERRIES.md`.

## Build and test

Run from `Lab1/NutsnBerries`:

```bash
dotnet build BlazorShop.sln -c Release
dotnet test BlazorShop.Tests/BlazorShop.Tests.csproj -c Release
```

The Web project runs `npm ci` and the Tailwind build as part of `dotnet build`. Pass `-p:SkipNpm=true` to skip that when Node is not available. The unit tests currently pass (692 passed, 10 skipped smoke tests that need a running site).

## Conventions and gotchas

- **Secrets stay out of git.** DB password is in `.env` (gitignored). The API's connection string, `Jwt:Key` and admin bootstrap values are in `dotnet user-secrets` for the API project. Do not read credentials into the session; the user signs in themselves.
- **Local-only Web config.** `BlazorShop.Web/wwwroot/appsettings.Local.json` is gitignored. Launch the Web client with `-p:WasmApplicationEnvironmentName=Local` to use it.
- **NuGet.** `nuget.config` restricts restore to nuget.org. The machine's global config includes a private Azure feed that hangs non-interactive restores.
- **After a .NET SDK update**, run `dotnet restore` on the Web project before `--no-restore` builds, or the WASM runtime pack is missing (NETSDK1112).
- **Seed data.** `DevelopmentCatalogSeeder` upserts 6 categories and 15 sample products by fixed GUIDs on API startup in Development. Product images are placeholder SVGs in `BlazorShop.Web/wwwroot/images/products`.
- **Tests pin visual details.** Some tests assert header text, a header colour and CSS patterns (for example, no `linear-gradient(135deg`). Run the unit tests after styling changes.
- **Git.** Commit messages end with the Co-Authored-By line. Push to `origin` (Sembster/NutsnBerries); never push to `upstream`.
