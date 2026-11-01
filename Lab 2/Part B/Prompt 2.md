# Prompt 2

## Prompt

> Gå igenom kodbasen och leta efter: hårdkodade hemligheter eller uppgifter, saknad inputvalidering, injection-risker (SQL, kommando, path traversal), känsliga data i loggar, och osäkra standardvärden. Lista varje fynd med filnamn och radnummer.

## Answer (English)

### Summary

I found **32 items**: 1 High, 7 Medium, 18 Low and 6 Informational. The code is stronger than its defaults: SQL is parameterized everywhere, uploads are carefully handled, JWT validation is strict, and there are no real secrets in the repository or its git history. The weak spots are a handful of insecure defaults, a few unvalidated admin and anonymous inputs, and logging that is more verbose than the configuration suggests.

| Severity | Count | Items |
|---|---|---|
| High | 1 | D1 |
| Medium | 7 | S1, V2, V3, I4, L1, D2, D3 |
| Low | 18 | S2, V1, V4, V5, V6, V7, V8, I5, I6, L2, L3, L4, L5, D4, D5, D6, D7, D8 |
| Info | 6 | S3, S4, S5, I3, D9, D10 |

**Top five to fix first**
1. **D1:** the JWT signing key silently falls back to a public placeholder if the secret is missing.
2. **S1:** a known demo admin password is hardcoded, shown on the sign-in page, and accepted as a valid default.
3. **L1:** request URLs with query strings (including email-confirmation tokens) are logged to console and file.
4. **V3 and I4:** an admin-entered tracking URL is not validated and ends up in links and emails; the Web client renders toast messages as raw HTML.
5. **D2 and D3:** containers run as root and the public Storefront sends no security headers.

**Scope and limits.** This is a static review of the source at commit `49bcb54`, using targeted searches and reading the relevant code. Paths are relative to `Lab1/NutsnBerries`, with line numbers as of that commit. I rated severity as if the app were deployed publicly as-is. For a course lab, most items are not blockers. I did **not** cover: NuGet dependency vulnerabilities (only `npm audit`, which found 0, was run earlier), dynamic testing against a running instance, a line-by-line read of the shell and PowerShell scripts (I scanned them for dangerous patterns), nginx TLS configuration, or authorization of individual orders (object-level access).

---

### A. Hardcoded secrets and credentials

**Clean:** a scan of all tracked files for well-known key formats (Stripe `sk_live_`/`whsec_`, AWS `AKIA`, PEM private keys, GitHub tokens, JWTs) found only the placeholder in S4. `.env`, `.pem`, `.key` and `.pfx` files were never committed in git history.

| ID | Sev | File:line | Finding |
|---|---|---|---|
| S1 | Medium | `BlazorShop.Application/Options/DemoOptions.cs:23`; `BlazorShop.Presentation/BlazorShop.API/appsettings.json:34-43`; `BlazorShop.Presentation/BlazorShop.Web/Authentication/Components/Login.razor:41-43`; `README.md:442` | A known password (`Demo123!`) and two demo accounts (`demo.user@…`, `demo.admin@…`) are hardcoded as the default, committed in config, displayed on the public sign-in page, and documented. The option validator only requires a non-empty password when demo mode is on (`BlazorShop.Infrastructure/DependencyInjection.cs:50-53`), so the default is accepted. The login page shows the box whether or not demo mode is enabled. Demo mode is off by default (`appsettings.json:35`), which limits the exposure. |
| S2 | Low | `BlazorShop.Infrastructure/Data/AppDbContextFactory.cs:23` | Design-time fallback connection string with `Username=postgres;Password=change-me`. Only used by EF tooling, but it is a hardcoded database credential. |
| S3 | Info | `BlazorShop.Presentation/BlazorShop.API/appsettings.json:45-46` | Sample bank details (`Iban: BG00UNCR70001512345678`, `Beneficiary`) are committed. Not secret, but they would be shown to customers on a bank-transfer order if not replaced. |
| S4 | Info | `.github/workflows/ci.yml:74-84` | CI placeholders (`ci-db-password`, `ci-jwt-signing-key-which-is-long-enough`, `sk_test_placeholder` at `:76`, `ci-email-password`). They only validate the compose file and are not real secrets. |
| S5 | Info | `BlazorShop.Tests/Presentation/Authentication/AuthSmokeTests.cs:92`; `BlazorShop.Tests/Infrastructure/StripeWebhookEventParserTests.cs:17`; many other test files | Test-only credentials and tokens (`Password123!`, `whsec_test`). Expected in tests. |

*(The placeholder JWT key is covered as D1, because the risk is how the app behaves with it, not the string itself.)*

### B. Missing input validation

**Done well:** catalog paging is clamped to 1-100 (`BlazorShop.Domain/Contracts/ProductCatalogQuery.cs`, `GetNormalizedPageSize`), registration enforces an 8-character password with upper, lower, digit and special character (`BlazorShop.Application/Validations/Authentication/CreateUserValidator.cs:16-21`), and uploads are limited by size, content type and signature (`BlazorShop.Presentation/BlazorShop.API/Controllers/FileUploadController.cs:64-76`).

| ID | Sev | File:line | Finding |
|---|---|---|---|
| V1 | Low | `BlazorShop.Application/Services/ProductService.cs:66-79`; `BlazorShop.Application/DTOs/Product/ProductBase.cs:13-24` | Admin create/update of products (and categories and variants) has no validator. `[Required]` on `decimal`/`int`/`Guid` does nothing, so negative prices and unbounded text pass. Admin-only, so mostly a data-integrity issue. |
| V2 | Medium | `BlazorShop.Presentation/BlazorShop.API/Controllers/NewsletterController.cs:22-32`; `BlazorShop.Application/Services/NewsletterService.cs:22,29,44-58` | Anonymous newsletter endpoint: only an empty check on the address (`:22`), no format or length validation. It sends a welcome email to any address submitted (`:44-58`, fire-and-forget), so it can be used to send unsolicited mail, with no double opt-in. Each request also loads every subscriber to check for duplicates (`:29`). The default rate limit is 60 requests per minute per IP (`BlazorShop.Presentation/BlazorShop.API/Options/ApiRuntimeOptions.cs:60`). |
| V3 | Medium | `BlazorShop.Application/DTOs/Payment/UpdateTrackingRequest.cs:5-9`; `BlazorShop.Infrastructure/Services/Admin/AdminOrderService.cs:115-117` | Tracking carrier, number and URL are only trimmed, with no length limit and no `http`/`https` check. The URL is used as an `href` in `BlazorShop.Presentation/BlazorShop.Web/Pages/Account/Dashboard.razor:85`, `Pages/Account/Orders.razor:98,142`, `Pages/Admin/Dashboard.razor:262` and `Pages/Admin/Orders.razor:118`, and inserted into an email (I5). Blazor does not filter URL schemes, so a `javascript:` value can run script on click, depending on the browser. Needs an admin (or a compromised admin account) to plant it, then targets customers. |
| V4 | Low | `BlazorShop.Domain/Contracts/ProductCatalogQuery.cs:25`; `BlazorShop.Infrastructure/Repositories/ProductReadRepository.cs:245-262` | The search term has no maximum length. On an anonymous endpoint it drives `ToLower().Contains()` over product name and description, an easy way to make expensive queries. |
| V5 | Low | `BlazorShop.Presentation/BlazorShop.Storefront/Services/StorefrontPublicRedirectMiddleware.cs:82-100` | Redirect target validation requires a leading `/` and rejects `//`, but not `/\`, which browsers treat like `//`. A malicious SEO redirect row could therefore redirect off-site. The rows are admin-managed, so the risk is low. |
| V6 | Low | `BlazorShop.Presentation/BlazorShop.API/Controllers/StripeWebhookController.cs:14,28-29` | The anonymous, rate-limit-exempt webhook reads the whole body into memory (`ReadToEndAsync`) before the signature is checked. Kestrel's default limit (about 30 MB) bounds it, but a smaller cap would be safer. |
| V7 | Low | `BlazorShop.Application/Validations/Authentication/CreateUserValidator.cs:14` | `EmailAddress()` is permissive (it accepts strings such as `<img …>@x.com`). Combined with I4 this could reflect into the page. I did not demonstrate an end-to-end exploit. |
| V8 | Low | `BlazorShop.Infrastructure/DependencyInjection.cs:171-172,191`; `BlazorShop.Application/DependencyInjection.cs:41`; `BlazorShop.Presentation/BlazorShop.Storefront/Program.cs:23` | `StripeOptions`, `BankTransferSettings`, `RecommendationOptions` and the Storefront's `DemoOptions` are bound without validation, so missing or placeholder values are accepted at startup. |

### C. Injection

| ID | Sev | Area | Result |
|---|---|---|---|
| I1 | none | **SQL injection** | **No findings.** About 30 raw-SQL call sites, all parameterized via `FromSqlInterpolated`/`ExecuteSqlInterpolated` or static strings. Main files: `BlazorShop.Infrastructure/Repositories/ProductInventoryTopologyRepository.cs:54,128,138,192`; `Services/InventoryReservationService.cs:96,116,142,166,318`; `Services/PaymentTransactionStore.cs:48,116`; `Services/StripePaymentReconciliationService.cs:55,75,98,242,250,258`; `Services/StripePaymentStateTransitionService.cs:116,151,212`; `BlazorShop.Presentation/BlazorShop.API/DatabaseMigrationBootstrapper.cs:118,127`. One dynamic identifier, `BlazorShop.Infrastructure/Demo/DemoSessionManager.cs:225-227` (`INSERT INTO pg_temp.{quotedTable} …`), comes from a fixed allowlist (`:24-30`) and goes through `QuoteIdentifier`, so it is safe. |
| I2 | none | **Command injection** | **No findings.** No `Process.Start` or shell calls in application code. `rehearsal/run-staging-rehearsal.sh` and `scripts/*.ps1` contain no `eval`, `bash -c`, `Invoke-Expression` or pipe-to-shell patterns. In CI, the release workflow passes the tag through `env:` rather than inline in `run:` (`.github/workflows/release.yml:48,67`), which is the safe pattern. |
| I3 | Info | **Path traversal** | **No exploitable path.** Upload file names are server-generated GUIDs with an extension from an allowlist (`FileUploadController.cs:69,90`) and a signature check (`:76`). The demo upload folder uses a server-generated 64-character hex session id (`DemoSessionManager.cs:73`), and deletion verifies the resolved path stays under the uploads root (`:276-286`). Defence in depth: `FileUploadController.cs:85` itself does not re-validate that id before `Path.Combine`. Uploads are served with `X-Content-Type-Options: nosniff` (`BlazorShop.Presentation/BlazorShop.API/Program.cs:419`). |

| ID | Sev | File:line | Finding |
|---|---|---|---|
| I4 | Medium | `BlazorShop.Presentation/BlazorShop.Web/Components/Notifications/Toast.razor:18,46,74,102` | Toast messages are rendered with `(MarkupString)toast.Message`, so they are inserted as raw HTML. There is no HTML encoding anywhere in the Web project. Messages come from API responses (for example `Pages/Admin/Categories.razor.cs:215-229`), so any API message that echoes user-supplied text, such as an identity error that includes the email address, would become DOM script. Combined with D6 (access token in session storage) the impact would be significant. |
| I5 | Low | `BlazorShop.Infrastructure/Services/OrderTrackingService.cs:75-82`; `BlazorShop.Application/Services/Payment/CheckoutOrchestrator.cs:1162-1171` | Email HTML is built with string interpolation and no encoding (tracking carrier, number and URL; bank details and reference). The values come from an admin or server configuration, so this is mostly an HTML-injection and phishing concern rather than an outside attack. |
| I6 | Low | `BlazorShop.Presentation/BlazorShop.Web/Pages/Admin/Orders.razor:426-442`; `BlazorShop.Presentation/BlazorShop.Web/Pages/Admin/Dashboard.razor.cs:428-461` | CSV export escapes quotes and commas only. A customer-controlled name or email starting with `=`, `+`, `-` or `@` becomes a spreadsheet formula when an admin opens or pastes the export. |

**Checked and safe:** JSON-LD structured data uses `JavaScriptEncoder.Default` (`BlazorShop.Presentation/BlazorShop.Storefront/Services/StorefrontStructuredDataDocument.cs:11`) before being written into a `<script>` block. Storefront API URLs are built with `Uri.EscapeDataString` (`StorefrontApiClient.cs:58,63,83,154,164,169`), so there is no URL or SSRF injection.

### D. Sensitive data in logs

**Done well:** no password, token, Authorization header or card data is passed to any logger call, EF sensitive-data logging is off (no `EnableSensitiveDataLogging`; SQL parameters appear as `'?'` in the logs), the Stripe webhook logs only a rejection warning without the payload (`BlazorShop.Infrastructure/Services/StripeWebhookService.cs:51`), and `log/` is gitignored (`.gitignore:33`).

| ID | Sev | File:line | Finding |
|---|---|---|---|
| L1 | Medium | `BlazorShop.Presentation/BlazorShop.API/Program.cs:54-58,217`; `appsettings.json:3-6`; `Controllers/AuthenticationController.cs:148` | Serilog is configured in code with no `MinimumLevel` and no `ReadFrom.Configuration`, so the `Microsoft.AspNetCore: Warning` setting in `appsettings.json` is ignored. The framework's `Request starting/finished` lines are therefore logged at Information level with the **full URL including the query string**, to the console and to `log/log.txt` (31 daily files by Serilog's default). I saw these lines in this session's API output. That captures the email-confirmation token in `GET confirm-email?userId=…&token=…` (`AuthenticationController.cs:148`) and users' search terms. |
| L2 | Low | `BlazorShop.Presentation/BlazorShop.Storefront/appsettings.json` and `appsettings.Development.json` (no `Logging` section); `BlazorShop.Presentation/BlazorShop.Storefront/Program.cs` | The Storefront has no logging configuration and no Serilog, so it runs on framework defaults and logs request URLs with query strings (search terms) at Information. |
| L3 | Low | `BlazorShop.Infrastructure/Services/EmailService.cs:53,57`; `BlazorShop.Application/Services/Authentication/AuthenticationService.cs:115,353,375,380` | Personal data (email addresses) written to logs at Information, Warning and Error level. Relevant under GDPR for retention and access. |
| L4 | Low | `BlazorShop.Application/Services/Authentication/AuthenticationService.cs:115,276,353,375,380`; `Services/ProductRecommendationService.cs:55,59,64,99,103,110`; `BlazorShop.Infrastructure/Repositories/ProductRecommendationRepository.cs:31,51,56,65,97,118,123,140,160,165` | 21 logger calls pass an interpolated string instead of a message template with arguments. Values are baked into the text, which defeats structured logging and allows log forging if a value ever contains a newline. |
| L5 | Low | `BlazorShop.Application/Services/NewsletterService.cs:39,53` | Two bare `catch` blocks swallow every exception without logging, including email-send failures (`:53`, `/* ignore */`). Failures here would leave no trace. |

### E. Insecure defaults

| ID | Sev | File:line | Finding |
|---|---|---|---|
| D1 | **High** | `BlazorShop.Presentation/BlazorShop.API/appsettings.json:15`; `BlazorShop.Infrastructure/DependencyInjection.cs:128`; `BlazorShop.Infrastructure/Repositories/Authentication/AppTokenManager.cs:159` | The committed config contains `"Key": "<set-in-user-secrets>"`, and nothing checks it. If `Jwt:Key` is not supplied, the app still starts and signs tokens with that public string, so anyone could forge an admin token. (Concluded from the code; I did not run the app without the secret.) The email options already reject `<…>` placeholders (`BlazorShop.Infrastructure/Configuration/EmailSettingsOptionsValidator.cs:60-61`), so the same check is easy to apply here. |
| D2 | Medium | `BlazorShop.Presentation/BlazorShop.API/Dockerfile:26-34`; `BlazorShop.Presentation/BlazorShop.Storefront/Dockerfile:42-48`; `BlazorShop.Presentation/BlazorShop.Web/Dockerfile:31-36` | No `USER` instruction in any final image, so the containers run as root. The ASP.NET images ship a non-root `app` user and the apps listen on 8080, so switching is straightforward. |
| D3 | Medium | `BlazorShop.Presentation/BlazorShop.Web/nginx.conf:16-19`; `BlazorShop.Presentation/BlazorShop.Storefront/Program.cs`; `BlazorShop.Presentation/BlazorShop.API/Program.cs:419` | Security headers (`X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`, `nosniff`) exist only in the Web client's nginx config. The public Storefront sends none, so it can be framed (clickjacking), and **there is no Content-Security-Policy anywhere**. The API sets only `nosniff` on uploaded files. |
| D4 | Low | `BlazorShop.Presentation/BlazorShop.API/appsettings.json:8` | `"AllowedHosts": "*"` accepts any Host header. A reverse proxy normally covers this. |
| D5 | Low | `BlazorShop.Presentation/BlazorShop.API/appsettings.json:62`; `BlazorShop.Infrastructure/Services/EmailService.cs:45` | `UseSsl` defaults to `false`, and the code calls MailKit's `ConnectAsync(host, port, bool useSsl)`. In MailKit, `false` means "use STARTTLS if the server offers it", so SMTP credentials can travel unencrypted if the server lacks it or STARTTLS is stripped (based on MailKit's documented behavior; not tested). |
| D6 | Low | `BlazorShop.Presentation/BlazorShop.Web.Shared/Helper/TokenService.cs:27-29`; `BlazorShop.Presentation/BlazorShop.Web/wwwroot/js/sessionStorage.js:7` | The JWT access token is kept in `sessionStorage`, readable by any script that runs in the page (see I4). By contrast, the refresh token is handled well: HttpOnly, Secure and SameSite=Strict (`AuthenticationController.cs:190-199`). |
| D7 | Low | `BlazorShop.Presentation/BlazorShop.Storefront/wwwroot/js/storefrontCommerce.js:34-36`; `BlazorShop.Presentation/BlazorShop.Web/wwwroot/js/cookieStorage.js:14` | The cart cookie (`my-cart`) is written from JavaScript without `Secure` or `SameSite`. It is not secret, and checkout re-resolves the cart lines on the server, but the flags are missing. |
| D8 | Low | `.github/workflows/ci.yml`; `.github/workflows/code-analysis.yml` (no `permissions:` block); all four workflows (`uses:` pinned to tags, none to commit SHAs) | CI jobs rely on the repository's default token permissions instead of declaring least privilege, and third-party actions are referenced by mutable tags, which is a supply-chain risk. |
| D9 | Info | `BlazorShop.Presentation/BlazorShop.API/appsettings.Development.json:15-16,23`; `BlazorShop.Presentation/BlazorShop.API/Program.cs:220-223,455` | Development relaxes email confirmation, disables rate limiting and allows any loopback CORS origin; Swagger is enabled only in Development. All fine as long as `ASPNETCORE_ENVIRONMENT` is never `Development` on a public host. |
| D10 | Info | `compose.production.yml:81-82,94-95`; `compose.vps.yml:3-7` | The production compose file publishes ports 8080 and 8081 on all interfaces; the VPS override binds them to `127.0.0.1`. The database port is not published. |

---

### What is in good shape

- **Authentication:** JWT validation checks issuer, audience, lifetime and signing key with zero clock skew (`BlazorShop.Infrastructure/DependencyInjection.cs:118-129`). Access tokens last 2 hours (`AppTokenManager.cs:161`). Login uses account lockout (`BlazorShop.Infrastructure/Repositories/Authentication/AppUserManager.cs:65`), and refresh tokens are stored hashed.
- **Abuse protection:** rate limiting is on by default (60 requests per minute public, 5 per minute for authentication, `ApiRuntimeOptions.cs:58-73`) and applied to every endpoint (`Program.cs:287-302`).
- **CORS** uses an allowlist with credentials (`Program.cs:446-456`), and Swagger is Development-only.
- **Secrets handling:** real secrets live in user-secrets and the gitignored `.env`, never in the repository or its history.
- **Payments:** Stripe webhook signatures are verified (`StripeWebhookEventParser.cs:12`).

### Suggested order of fixes

1. Validate the JWT key at startup and reject placeholder values (D1), and add validation for `StripeOptions` and `BankTransferSettings` (V8).
2. Remove the demo credentials from the sign-in page, show that box only when demo mode is enabled, and require a changed password when it is (S1).
3. Configure Serilog from configuration and set `Microsoft.AspNetCore` to Warning, or strip query strings from request logs (L1, L2); log emails less (L3).
4. Validate tracking URLs as `http(s)` and HTML-encode email bodies (V3, I5); encode toast messages instead of rendering `MarkupString` (I4).
5. Add `USER app` to the Dockerfiles (D2) and security headers plus a basic Content-Security-Policy to the Storefront (D3).
6. Add validators for product, category and newsletter input, cap the search term, and add double opt-in for the newsletter (V1, V2, V4).
