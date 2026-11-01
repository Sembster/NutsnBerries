# Prompt 2

## Prompt

> Gå igenom kodbasen och leta efter: hårdkodade hemligheter eller uppgifter, saknad inputvalidering, injection-risker (SQL, kommando, path traversal), känsliga data i loggar, och osäkra standardvärden. Lista varje fynd med filnamn och radnummer.

## Answer (English)

### Summary

I found **32 items**: 1 High, 7 Medium, 18 Low and 6 Informational. The code is stronger than its defaults: SQL is parameterized everywhere, uploads are carefully handled, JWT validation is strict, and there are no real secrets in the repository or its git history. The weak spots are a handful of insecure defaults, a few unvalidated admin and anonymous inputs, and logging that is more verbose than the configuration suggests. **Addendum F (added afterwards) covers the failing GitHub CI:** three vulnerable NuGet advisories (1 High, 2 Medium) in two packages, plus a separate npm audit failure, with a fix that was tested in a scratch copy first and then applied (two new props files and an npm lockfile update), followed by a base-image bump for the runtime exposure (F10).

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

**Scope and limits.** This is a static review of the source at commit `49bcb54`, using targeted searches and reading the relevant code. Paths are relative to `Lab1/NutsnBerries`, with line numbers as of that commit. I rated severity as if the app were deployed publicly as-is. For a course lab, most items are not blockers. I did **not** cover in sections A-E: dependency vulnerabilities (the CI failure is analysed separately in addendum F; the runtime-only `npm audit` run earlier found 0, but the full dev-tree audit that CI runs finds 6), dynamic testing against a running instance, a line-by-line read of the shell and PowerShell scripts (I scanned them for dangerous patterns), nginx TLS configuration, or authorization of individual orders (object-level access).

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

---

## F. Addendum: GitHub CI fails on vulnerable packages (analysis and applied fix)

**Request:** check the severity of the NuGet vulnerabilities behind the failing CI, propose a solution, and let you verify it before any change. After your approval ("Option A, include the npm fix, update lab documentation and push") the fix was applied; sections F6 to F10 record what changed and what is still open.

**Method:** I read the failing runs through `gh`, reproduced the failure locally, read the three GitHub security advisories, traced where each package comes from, checked whether our code can reach the vulnerable functions, and tested the candidate fixes in a scratch copy outside the repository before touching the repository itself.

### F1. What exactly fails in CI

Failing runs on commit `354acc4` (2026-11-01), repository `Sembster/NutsnBerries`:

| Workflow / job | Failing step | Cause |
|---|---|---|
| `Code Analysis` / analyze | Analyze Code with StyleCop (`dotnet build -warnaserror`) | NuGet audit errors NU1902 and NU1903, in 5 projects |
| `ci` / browser-e2e | Build browser E2E project (`-warnaserror`); the upload step fails afterwards only because nothing was produced | The same NuGet audit errors |
| `ci` / build-test | Audit full npm build dependency tree | **A separate npm cause, not NuGet** (F5) |
| `ci` / postgres16-archive | none | passes |

How a NuGet advisory becomes a CI failure: since .NET 9 the SDK audits packages during restore and reports known vulnerabilities as warnings NU1901-NU1904. Both workflows that build with `-warnaserror` turn those warnings into errors.

**Why it started failing without any code change:** the last green `Code Analysis` run was 2026-10-10 (`ef1a107`). The advisories were published on **2026-10-13** (Pkcs) and **2026-10-15** (MessagePack), and the first red run was **2026-10-15** (`6b3e104`). Our code did not change; the advisory database did.

### F2. The vulnerabilities and their severity

| ID | Package and version | Advisory | Advisory severity | Problem | Fixed in |
|---|---|---|---|---|---|
| N1 | `System.Security.Cryptography.Pkcs` 10.0.0 | GHSA-846f-f764-76mx (CVE-2026-84008) | **High**, CVSS 7.5 (network, no authentication, availability only) | `SignedCms.CheckSignature` re-parses attacker-controlled X.509 data repeatedly, so CPU use grows quadratically (denial of service) | 10.0.13 |
| N2 | `System.Security.Cryptography.Pkcs` 10.0.0 | GHSA-2fmv-rv2c-59x7 (CVE-2026-70162) | **Medium**, CVSS 5.9 | On Windows, `EnvelopedCms.Decode` does quadratic work on attacker-supplied CMS/PKCS#7 attributes (denial of service) | 10.0.13 |
| N3 | `MessagePack` 2.5.302 | GHSA-qhrr-8q5h-9q3h (CVE-2026-92707) | **Medium**, CVSS 5.3 | Deeply nested arrays make `PrimitiveObjectFormatter` allocate far too much memory before the depth limit applies (denial of service) | 2.5.303 (or 3.1.9) |

All three are **denial-of-service only**: no data disclosure and no code execution.

### F3. Where they come from, and what they mean for this project

| Package | Dependency path | Projects affected | Reachable from our code? | Shipped to production? |
|---|---|---|---|---|
| `System.Security.Cryptography.Pkcs` | `BlazorShop.Infrastructure` > `MailKit 4.16.0` > `MimeKit 4.16.0` > Pkcs 10.0.0 | Infrastructure, API, Tests, E2E | **No evidence.** Our code never uses `SignedCms`, `EnvelopedCms` or S/MIME (0 matches in production code); `BlazorShop.Infrastructure/Services/EmailService.cs:6,11` only *sends* plain email. | **See F8:** this assembly is also part of the ASP.NET Core runtime, so what actually runs is decided by the runtime image, not only by the NuGet package. |
| `MessagePack` | `BlazorShop.AppHost` > `Aspire.Hosting 13.4.6` > `StreamJsonRpc 2.25.29` > MessagePack 2.5.302 | AppHost, Tests, E2E | No: 0 uses in our code | **No**, development and test tooling only |

**Practical risk for this lab: low.** The advisories are real, and the High one blocks CI, but exploiting them would need the app to parse attacker-supplied CMS/PKCS#7 data (it does not) or deserialize attacker-supplied MessagePack (it does not). The severity labels describe the library in general, not our usage.

### F4. Options considered

| Option | What changes | Edits upstream files? | Tested | Verdict |
|---|---|---|---|---|
| **A. Pin the two packages in new `Directory.Build.props` files** | Pkcs `10.0.13` pinned for Infrastructure, MessagePack `2.5.307` pinned for AppHost; dependent projects inherit the fixed versions | **No** (new files only), so it fits the "extend, do not edit" policy | **Yes** | **Chosen and applied** |
| B. Upgrade `MailKit` and `MimeKit` to 4.18.2, and pin MessagePack | Edits two version numbers in `BlazorShop.Infrastructure.csproj`; MimeKit 4.18.2 itself depends on Pkcs 10.0.13 | Yes, 2 lines | Yes (build clean, 699 tests pass) | Good and "proper", but edits an upstream file; keep as the alternative |
| C. Upgrade `Aspire.Hosting` 13.4.6 to 13.6.x | Pulls a newer StreamJsonRpc that requires the fixed MessagePack | Yes (several packages) | No | Larger change and more risk to the AppHost and E2E tests, for a dev-only package |
| D. Suppress the warnings | Hides NU1902/NU1903 | Yes | not needed | **Not recommended**: it hides a High advisory instead of fixing it |
| E. Update the runtime base images (`aspnet:10.0.6` and the SDK tag) to 10.0.13 or later | Changes the image tags in the three Dockerfiles | Yes (5 lines) | Tags checked against registry metadata and BuildKit lint; images built by CI | **Applied as a follow-up, see F8 and F10** |

**Adjustment made while applying Option A:** my first proposal put one `Directory.Build.props` at the solution root. Reading the Dockerfiles showed they copy **only specific project folders** and `.csproj` files, so a root-level file would never reach the container builds. The two props files therefore live **inside the project folders** that the Dockerfiles do copy (`BlazorShop.Infrastructure/` and `BlazorShop.AppHost/`). This is still new files only, and it behaves the same for solution builds, CI and Docker.

### F5. A second, independent CI failure (npm, not NuGet)

`ci` / build-test fails at **Audit full npm build dependency tree** (`npm audit --include=dev --audit-level=high`). It was already red on **2026-10-10**, before the NuGet advisories existed, so fixing NuGet alone would not have turned `ci` green. Locally reproduced: **6 advisories, 2 high and 4 moderate**:

| Package (all transitive, from `tailwindcss`) | Severity | Issue |
|---|---|---|
| `brace-expansion` 2.0.0-2.1.6 | **High** | Quadratic-time expansion and uncontrolled recursion (3 advisories, DoS) |
| `source-map-js` 1.0.0-1.2.1 | **High** | Event-loop denial of service via crafted source-map offsets |
| `glob` 3.0.0-14.0.1 | Moderate | Cancellation bypass allows file-system traversal after a timeout (pulled in by `sucrase`) |
| `minimatch` <11.0.1 | Moderate | Denial of service via glob-part amplification |
| `postcss-selector-parser` <6.1.5 | Moderate | Quadratic complexity in selector parsing |
| `sucrase` 3.35.0 | Moderate | Depends on the vulnerable `glob` |

Practical risk: **low**. These are build-time tools (Tailwind compiling CSS) and are never shipped; the runtime-only audit reported 0 vulnerabilities throughout.

### F6. What was changed

1. **New file** `Lab1/NutsnBerries/BlazorShop.Infrastructure/Directory.Build.props`: pins `System.Security.Cryptography.Pkcs` to `10.0.13` (fixes N1 and N2).
2. **New file** `Lab1/NutsnBerries/BlazorShop.AppHost/Directory.Build.props`: pins `MessagePack` to `2.5.307` (fixes N3).
   Both files carry a comment saying why they exist and when to remove them. 10.0.13 is the first fixed Pkcs release (and exactly what MimeKit 4.18.2 itself requires); 2.5.307 is the newest 2.5.x and what the latest StreamJsonRpc declares, a patch-level change on the same minor version.
3. **Modified** `Lab1/NutsnBerries/BlazorShop.Presentation/BlazorShop.Web/package-lock.json` through `npm audit fix`: three transitive packages updated (`postcss-selector-parser` 6.1.4 to 6.1.5, `source-map-js` 1.2.1 to 1.2.2, `sucrase` 3.35.0 to 3.35.1). Sucrase 3.35.1 no longer needs `glob`, so 37 old packages drop out (123 lockfile entries become 85). `package.json` is unchanged and no direct dependency changed. In this first step it was the only upstream file edited, a mechanical lockfile update.

**Rollback:** delete the two props files and restore `package-lock.json` (or revert the commit).

### F7. Verification

| Check | Result |
|---|---|
| **Control:** scratch copy without pins, `dotnet build -warnaserror` | **Fails with the same NU1902/NU1903 errors as GitHub** (Infrastructure, API, AppHost, E2E) |
| Real repo after the fix: `dotnet list package --vulnerable --include-transitive` | **No vulnerable packages**; resolved versions Pkcs 10.0.13 and MessagePack 2.5.307 |
| Real repo: whole solution `dotnet build --no-restore -warnaserror` (what `Code Analysis` runs) | **0 warnings, 0 errors** |
| Real repo: E2E project, Release, `-warnaserror` (what `ci` / browser-e2e builds) | **0 warnings, 0 errors** |
| Real repo: unit tests | **699 passed, 0 failed, 10 skipped**, identical to the baseline |
| Option B in the scratch copy | Also clean (vulnerability list, build, 699 tests) |
| npm after `npm audit fix`: full audit (what CI runs) | **0 vulnerabilities**; runtime-only audit 0; generated `site.css` byte-identical to the committed file |

**Not verified locally:** the GitHub run, the Playwright browser tests and the Docker image builds (all three now confirmed green on GitHub, see F9), Option C, and which Pkcs version a container image actually ships (see F8).

### F8. Correction found while applying the fix: where Pkcs really comes from at runtime

When I published the API locally to inspect what it ships, **no `System.Security.Cryptography.Pkcs.dll` was in the output at all**, with or without the pin. Reason: this assembly is part of the **ASP.NET Core shared runtime** (`Microsoft.AspNetCore.App`, present in the installed 10.0.14 runtime). At publish, a NuGet package copy is dropped when the runtime already provides an equal or newer version. So my earlier statement in F3 of the first draft, that the package "ends up in the API" from MailKit, was incomplete.

What follows for production:
- What runs in a container is decided by the **runtime image**. The Dockerfiles use `aspnet:10.0.6-noble`, older than the fixed 10.0.13, so the runtime's own Pkcs is very likely still in the vulnerable range. I have **not** verified this against the image (it needs about 1 GB of base images that are not cached here).
- The pin does clear CI, which only audits NuGet packages. In the Docker build, whose older SDK has an older runtime than 10.0.13, the pinned 10.0.13 package should be newer than the runtime's copy and therefore be published and win at run time. That is a reasoned expectation, not a proven fact.
- The robust production fix is **Option E**: move both image tags to a .NET 10.0.13 or later patch. It was applied as a follow-up, see F10.
- A `docker build` of the API image followed by reading its `*.deps.json` would prove which Pkcs version an image ships. It needs a ~1 GB download and has not been run.

### F9. Result on GitHub

Fix commit `274564d`, pushed 2026-11-01. **Both workflows are green**, the first green `Code Analysis` run since 2026-10-10 and the first green `ci` run since the same day.

| Workflow / job | Before (`354acc4`) | After (`274564d`) |
|---|---|---|
| `Code Analysis` / analyze | failure (NU1902/NU1903) | **success** |
| `ci` / build-test (npm audits, restore, build, unit tests, three Docker image builds) | failure (npm full-tree audit) | **success** |
| `ci` / browser-e2e (E2E build, Playwright suite run twice, scenario verification, upload) | failure (NU1902/NU1903) | **success** |
| `ci` / postgres16-archive | success | success |

This also settles two items listed as not verified in F7: the Playwright browser tests pass (two consecutive runs), and the three container images (API, Storefront, Web) build with the props files present.

**Follow-up:** the container build passing does not show *which* Pkcs version ends up inside the API image. As explained in F8, the runtime image tag was the real production exposure; it was addressed afterwards by Option E, see F10.

### F10. Follow-up: Option E applied (Docker base-image tags)

**Request:** "Bump the Dockerfile image tags", the open production question from F8.

**What changed (5 lines in 3 Dockerfiles, plus the runbook that documents them):**

| File | Before | After |
|---|---|---|
| `BlazorShop.Presentation/BlazorShop.API/Dockerfile` | `sdk:10.0.202-noble`, `aspnet:10.0.6-noble` | `sdk:10.0.403-noble`, `aspnet:10.0.14-noble` |
| `BlazorShop.Presentation/BlazorShop.Storefront/Dockerfile` | `sdk:10.0.202-noble`, `aspnet:10.0.6-noble` | `sdk:10.0.403-noble`, `aspnet:10.0.14-noble` |
| `BlazorShop.Presentation/BlazorShop.Web/Dockerfile` | `sdk:10.0.202-noble` | `sdk:10.0.403-noble` |
| `docs/production-runbook.md` | listed the old tags and left out the Storefront | lists the new tags, Storefront added |

**Why these two tags.** The SDK and runtime images were chosen as a matching pair, as upstream had them. I read the image metadata straight from the Microsoft registry (no layers downloaded), which shows what each tag really contains:

| Image tag | Runtime inside | Note |
|---|---|---|
| `aspnet:10.0.6-noble` (old) | 10.0.6 | below the fixed 10.0.13 |
| `aspnet:10.0.14-noble` (new) | 10.0.14 | past the fix; the same runtime I used for all local tests |
| `sdk:10.0.202-noble` (old) | 10.0.6 | |
| `sdk:10.0.403-noble` (new) | 10.0.14 | the same SDK used for the local builds and test runs |

The advisory's own remediation text is to "install the latest version of .NET" and restart the apps, which is what a newer base image does.

**Left unchanged on purpose:**
- `rehearsal/run-staging-rehearsal.sh` also names the old tags, but only to pull and log them as evidence; the images it builds come from the Dockerfiles of two fixed historical commits, so the rehearsal reproduces a historical environment by design.
- The `nginx:1.27.5` image for the Web client is not part of this advisory and was not reviewed here.

**Verification:** BuildKit's lint mode (`docker buildx build --check`) passes for all three Dockerfiles: the new tags resolve in the registry and there are no warnings. No test references the tags. A full local image build was not run, because it needs about 1 GB of base images; the GitHub run below builds all three images.

**Still unproven:** reading the Pkcs assembly inside a built image. The conclusion rests on the tag-to-runtime mapping above, which comes from the registry metadata, not on opening a container.

**Cost and rollback:** this edits three upstream files (5 lines), so the modified-file count is now **78** (966 of 1,059 files identical, 91.2%). Upstream will probably bump these tags itself, so expect a trivial merge conflict later. Rollback: restore the three Dockerfiles and the runbook lines.

**Result on GitHub** (commit `fcec08b`, 2026-11-01): **both workflows are green.** `Code Analysis` succeeded. In `ci`, `build-test` succeeded, including the unit tests, both npm audits and the three container image builds (API, Storefront, Web) with the new `sdk:10.0.403` and `aspnet:10.0.14` bases; `browser-e2e` succeeded with the Playwright suite passing in both consecutive runs; and `postgres16-archive` succeeded. So the image bump did not break any build or test. What this does not show is the Pkcs version inside the built images (see "Still unproven" above).

---

## G. Status update: findings closed by the validation work

The first priority item of `Prompt 1.md` (input validation and startup validation, see the addendum there) closes or reduces these findings from this review. The original tables above stay unchanged as the record.

| ID | Finding | Status |
|---|---|---|
| **D1** (High) | JWT signing key silently falls back to a public placeholder | **Closed.** `JwtKeyOptionsValidator` makes startup fail if the key is missing, a `<placeholder>`, or shorter than 32 bytes. Confirmed live: the API refuses to start with the placeholder. |
| V1 | No validation on product, category and variant input | **Closed.** New validators run through one global filter; limits are in `CatalogConstraints`. |
| V2 (Medium) | Newsletter endpoint accepts any text | **Reduced.** The address is now required, bounded and checked (confirmed live: 4 bad inputs return 400). Still open: no double opt-in, the duplicate check still loads every subscriber, and the endpoint still sends mail to whatever valid address it receives (rate-limited). |
| V7 | Permissive email check | **Closed for the newsletter** (HTML and quoted forms are rejected). The registration validator is upstream code and is unchanged. |
| V8 | Stripe, bank transfer, recommendation and Storefront demo options unvalidated | **Closed.** Each has a startup validator. |

**New totals for sections A to E:** 4 of the 32 findings are closed (D1, V1, V7, V8), leaving 28: 0 High, 6 Medium (S1, V3, I4, L1, D2, D3), 16 Low (counting V2 at its reduced severity) and 6 Informational.
