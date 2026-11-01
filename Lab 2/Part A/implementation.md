# Implementation summary: Lab 2, Part A

**Project:** NutsnBerries, a natural beauty webshop forked from [unrealbg/BlazorShop](https://github.com/unrealbg/BlazorShop) (Blazor, .NET 10, PostgreSQL in Docker). The shop was built in Lab 1. This part starts from one question: *is any of the project custom code that does not depend on upstream?* It ends with a rule for how future customizations are made, and applies that rule to the first two customizations (the theme CSS and the SEO-settings seed).

## 1. What we analyzed

| Question | How it was checked | Finding |
|---|---|---|
| **Is there custom code independent of upstream?** | Compared every file's content hash against the exact fork commit (`3944038`). A plain file-name comparison was not enough, because the whole tree had been moved into `Lab1/NutsnBerries`. | Mostly no. 971 of 1,059 upstream files (91.7%) are identical, 73 are modified, 15 were deleted (the old fashion product images) and 21 are new (23 after the CSS split described below; course notes are not counted). The 73 modified files are mainly in the two UI projects (27 in the Web client, 22 in the Storefront). The only real new code is `PriceFormatter` (about 45 lines) and its tests; the rest of the new files are sample images, compose, config and docs. |
| **Is there a separate custom project?** | Looked at the solution structure and what calls what | No. Everything we wrote sits inside upstream's solution and is called by upstream's pages. Our customization is a thin layer: branding, theme, SEK and sample data. |
| **Could a later merge from upstream overwrite or break our work?** | Simulated a merge in a throwaway repo shaped like ours (everything moved into a subfolder, then customised) | Git never silently overwrites commits. Untouched files merge cleanly, different-line edits auto-merge, same-line edits become conflicts, and new files in a moved folder are placed correctly but flagged. The real risks are (a) quiet breakage, where upstream adds a page with a hardcoded `€` or "BlazorShop" that git merges without complaint, (b) conflicts in the 73 modified files and (c) new root-level upstream files landing outside the project folder. Upstream has moved on (`9abba4d`), but it was deliberately not fetched. |

## 2. What I suggested

1. **Protect the customization from a future merge:** tag a baseline, merge only on a branch (the 692 unit tests act as a safety net), add two guard tests that fail on hardcoded currency symbols or visible "BlazorShop" text, and move the theme CSS into its own file. A fifth option was to not merge at all.
2. **Shrink the private diff by contributing upstream.** Generic changes, such as configurable currency and brand name, could be offered as pull requests, so they stop being our private edits.
3. **Build customizations as extensions where possible.** What fits as a new file or project: the theme CSS, our own seeder class, `PriceFormatter`, images, compose and docs, and configuration-driven names. What does not: hardcoded text and prices inside Razor pages, the private currency list in `CurrencyMoney`, and page layout, none of which upstream lets you plug into from outside.
4. **Later, a separate repo** holding only our own projects plus a pinned reference to upstream (submodule, subtree or a synced fork), so updates stay cheap to merge.

## 3. What we implemented, and why

| Implemented | Reasoning |
|---|---|
| **Decision: skip upstream merges for now.** | The project is a course lab and 92% of the files are unchanged, so nothing is at risk while the work is in progress. |
| **"Customization policy: extend, do not edit" section in `CLAUDE.md`** (committed and pushed as `5377d88`) | Turns your decision into five working rules: new files over edits, configuration before code, a minimal one-line hook if an upstream file must change, prefer changes upstream could accept, and accept the few modifications that cannot be avoided. The rules live in the repo, so they apply to everyone and every future session. |
| **The rule saved as an AI-assistant memory note** (outside the repo) | Keeps the rule active in later sessions, so new changes follow it without being re-explained. |
| **The audit numbers recorded in the policy** | Makes the goal measurable: the count of modified upstream files should not grow, and should shrink over time. |
| **CSS split: the theme moved out of upstream's `storefront.css` into a new `nutsnberries-theme.css`** | First real use of the policy, and the option I suggested as the easiest win. The theme had been edited into upstream's stylesheet (colors changed in place, plus a block appended), which is exactly the kind of file that conflicts in a merge. |

**How the CSS split was done**

1. Mapped every changed declaration in upstream's `storefront.css` to its selector (9 rules whose colors changed, plus the appended block).
2. Recreated each one as an override in the new `nutsnberries-theme.css` (68 lines). It is linked after `storefront.css`, so at equal specificity it wins the cascade.
3. Restored `storefront.css` to upstream's exact content, confirmed by an identical git blob hash.
4. Added the only seam in upstream code: one `<link>` line in `App.razor`.
5. Put upstream's own header test back to its original color assertion, and added `NutsnBerriesThemeTests` (a new file) that checks the theme is linked after `storefront.css`, defines the palette and respects the 135deg-gradient design guard.

**Result and verification**

- **No visual change.** Computed styles were captured in the browser before the change and compared afterwards across 22 groups (header, brand mark, buttons, badge, hero, the re-themed Tailwind utilities, footer, and the final hover rules): 0 differences.
- **Tests:** 695 passed (692 plus 3 new), 0 failed.
- **Diff against upstream:** `storefront.css` went from 47 added and 11 changed lines to 0. The only remaining upstream edit for the theme is 1 added line in `App.razor`.
- **The modified-file count did not drop** (still 73): `storefront.css` is now identical, but `App.razor` joined the list. The win is the size and location of the diff: a single line instead of 58 changed lines in a file upstream actively maintains.

### SEO settings seeded from our own seeder

| | |
|---|---|
| **Problem** | During the CSS check, page titles lost the "\| NutsnBerries" suffix. The `SeoSettings` table was empty, so once the API was warm it returned empty settings. The suffix only appeared when the API call failed and the Storefront used its config fallback. |
| **Extension, not edit** | A new class, `NutsnBerriesSeeder` (`BlazorShop.Infrastructure/Data/NutsnBerriesSeeder.cs`), instead of adding to upstream's `DevelopmentCatalogSeeder`. It is called from one added line in the API's `Program.cs`, next to the upstream seeder call. |
| **Behavior** | Creates the single SEO row only if none exists (site name, title suffix, a default meta description, company name). It never overwrites an existing row, so edits made in the admin area survive restarts. It runs only in Development, like the upstream seeder. |
| **Why those values** | Only fields we actually know: the brand name and a short description of the shop. Email, phone, address, logo and social URLs are left empty rather than invented. The base canonical URL is also left empty on purpose: the Storefront then builds canonical links from its own `PublicUrl:BaseUrl` setting (checked: `http://localhost:18598/...` locally), which is the right place to set the real domain later. |
| **Tests** | `NutsnBerriesSeederTests` (4 tests): creates the row when none exists, stays within the SEO field length limits, is idempotent, and does not overwrite an admin-edited row. In total 699 tests pass, 0 fail. |
| **Verified live** | After restarting the API, the database held the row, `/api/seo/settings` returned it, and every page title I checked ended with "\| NutsnBerries" (home, new releases, about, a category and a product page). |
| **One thing to remember** | The Storefront caches SEO settings for 5 minutes, so a change in the database or admin area can take that long (or a restart) to show up in titles. |

## 4. Decisions and what was not done

- **The other modified files are not being refactored yet.** When the separate repo is created they get sorted into "can become an extension", "should go upstream" and "must stay a patch". The CSS split was done as a trial of the approach, because it was the cheapest and safest candidate. Doing everything now would add churn without a merge to benefit.
- **Not implemented yet:** the baseline tag, the guard tests for hardcoded currency symbols and brand text (the theme tests are a different, narrower guard), and the dry-run merge.
- **Next extension candidate:** move the sample catalog (categories, products, images) out of upstream's `DevelopmentCatalogSeeder` and into `NutsnBerriesSeeder`, then restore upstream's file. That removes our largest remaining edit to a seeder.
- **No upstream changes were fetched or merged.** The only network access was a read-only listing of upstream's latest commit.
- **Found along the way and fixed:** the empty `SeoSettings` table that removed the site name from page titles, now seeded by `NutsnBerriesSeeder` (see above).
- **Where the numbers stand now:** 969 of 1,059 upstream files identical (91.5%), 75 modified, 15 deleted, 27 new. The new files are the theme stylesheet, `PriceFormatter`, `NutsnBerriesSeeder`, their tests, images, compose, config, docs and two dependency-security `Directory.Build.props` pins. The only upstream hooks we added are one line in `App.razor` and one line in `Program.cs`; the only other upstream edit is the mechanical `package-lock.json` update from `npm audit fix`.
- **Extension policy applied to a CI failure:** when new NuGet advisories turned CI red (see `Lab 2/Part B/Prompt 2.md`, section F), the vulnerable transitive packages were pinned through two **new** `Directory.Build.props` files instead of editing upstream project files.
