# Implementation summary: Lab 2, Part A

**Project:** NutsnBerries, a natural beauty webshop forked from [unrealbg/BlazorShop](https://github.com/unrealbg/BlazorShop) (Blazor, .NET 10, PostgreSQL in Docker). The shop was built in Lab 1. This part starts from one question: *is any of the project custom code that does not depend on upstream?* It ends with a rule for how future customizations are made.

## 1. What we analyzed

| Question | How it was checked | Finding |
|---|---|---|
| **Is there custom code independent of upstream?** | Compared every file's content hash against the exact fork commit (`3944038`). A plain file-name comparison was not enough, because the whole tree had been moved into `Lab1/NutsnBerries`. | Mostly no. 971 of 1,059 upstream files (91.7%) are identical, 73 are modified, 15 were deleted (the old fashion product images) and 23 are new. The 73 modified files are mainly in the two UI projects (27 in the Web client, 22 in the Storefront). The only real new code is `PriceFormatter` (about 45 lines) and its tests; the rest of the new files are sample images, compose, config and docs. |
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

## 4. Decisions and what was not done

- **Existing 73 modified files are not being refactored yet.** When the separate repo is created they get sorted into "can become an extension", "should go upstream" and "must stay a patch". Doing it now would add churn without a merge to benefit.
- **Not implemented yet:** the baseline tag, the guard tests, the CSS split and the dry-run merge. They were offered, and we agreed to apply the extension rule to new work only.
- **No upstream changes were fetched or merged.** The only network access was a read-only listing of upstream's latest commit.
