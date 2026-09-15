# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

A Hacker News reader in two independently deployable halves: a .NET 10 CQRS API (`backend/`) and an
Angular 20 SPA (`frontend/`). They share no build, no tooling and no CI workflow — keep it that way.

## Commands

All backend commands run from `backend/`, all frontend commands from `frontend/`.

```bash
# Backend
dotnet build                                  # TreatWarningsAsErrors is on
dotnet test                                   # 170 tests
./scripts/Test-Coverage.ps1                   # tests + merged coverage, fails under 80% line
dotnet run --project src/HackerNews.Api        # http://localhost:5070

# Frontend
npm start                                     # http://localhost:4200
npm run test:ci                               # 86 unit tests, headless
npm run build:prod
npm run e2e                                   # 12 Playwright tests — needs the API running
```

### Running a single test

```bash
dotnet test tests/HackerNews.UnitTests -- --filter-method "*Rejects_a_page_below_one*"
npx ng test --watch=false --browsers=ChromeHeadless --include='**/highlight.spec.ts'
npx playwright test -g "linkless"
```

`dotnet test` runs through **Microsoft.Testing.Platform**, opted into by `backend/global.json`.
That is why the filter flag is `--filter-method` after a `--`, not `--filter`. `coverlet` does not
hook MTP; coverage comes from `Microsoft.Testing.Extensions.CodeCoverage` merged by
`dotnet-coverage`, which `Test-Coverage.ps1` wraps.

### Two things that waste time if you don't know them

**A Release build fails while the API is running** — the process holds a lock on
`bin/Release/net10.0/HackerNews.Api.exe`. Stop it first. This looks like a build error and isn't.

**The Angular dev server can wedge and silently serve a stale bundle.** If the UI ignores your
changes, read `npm start`'s output before suspecting the code — a transient missing file (e.g. mid
file-rewrite) kills the watcher and it keeps serving the last good build.

## Architecture

### The snapshot cache is the central idea

Search spans the whole feed and each story is a separate upstream fetch, so per-request fetching is
impossible. The feed is materialised in memory and every read is served from it:

- **`StorySnapshotRefresher`** performs one pass — fetch the ≤500 newest ids, fetch only ids it has
  not already seen (throttled), drop dead/deleted/untitled, order newest first, swap atomically.
- **`StorySnapshotBackgroundService`** owns *only* the schedule.
- **`StorySnapshotCache`** holds an immutable snapshot swapped with `Volatile.Write`; reads are
  lock-free.

That split exists so a refresh pass can be unit-tested directly with no host, timers or sleeps.
Preserve it — merging them back would make 11 tests untestable.

### Failure philosophy

A failed pass **keeps the previous snapshot** rather than publishing a degraded one. That covers an
empty id list, individual item failures, a mostly-failed full refresh (see
`Cache:MinimumYieldPercent`), and a total outage. If the cache has *never* loaded, requests return
**503** — deliberately never an empty `200`, which a client cannot distinguish from "no stories".
Several tests exist solely to pin this; don't "simplify" them away.

### Request flow

```
Controller → ISender → LoggingBehavior → ValidationBehavior → Handler → IStorySnapshotCache
                                              ↓ on failure
                                       GlobalExceptionHandler → ProblemDetails
```

Behaviour registration order in `Program.cs` is execution order. Controllers contain no logic.

### Conventions to follow

- Handlers own their nested `Query`, `Response` and `Validator` as nested types.
- Every service has an interface in `Services/Interfaces/` and is injected through it. The sole
  exception is `StorySnapshotBackgroundService`, which already implements the framework's
  `IHostedService`.
- `Shared/` holds only feature-agnostic things — behaviours, `PagedResult<T>`, cross-cutting
  options. Never a feature's service.

### Bind configuration through options, not `builder.Configuration`

Reading a value off `builder.Configuration` **before `Build()`** makes it unreachable from
`WebApplicationFactory` tests, because test overrides are applied during host build. Rate limiting
and forwarded headers were both fixed this way; follow the same pattern for anything new.

## Frontend specifics

- State is signals; templates use `@if` / `@for`. `switchMap` on the request stream means a slow
  response for an old page can never overwrite a newer one.
- **A story with no `url` renders as plain text with a "no link" badge**, never a dead anchor. This
  is the app's signature edge case and is covered at every layer.
- **Search highlighting returns text segments, never markup.** Titles are user-submitted, so
  building `<mark>` strings and assigning `innerHTML` would hand over script execution. It matches
  with `indexOf`, not `RegExp`, so a term of metacharacters is inert.
- `@for` over page numbers tracks by value, not `$index`. Index tracking relabels DOM nodes under a
  focused button and navigates somewhere the user didn't aim. This causes a dev-only `NG0956`
  warning that is a false positive — leave it.

## Deployment

Azure App Service (API) + Static Web Apps (UI), deployed by `deploy-api.yml` / `deploy-ui.yml` on
push. Both skip with a notice when their secret is absent rather than failing. Secrets live in the
GitHub **`DEV` environment**, so each job declares `environment: DEV` — without it `secrets.*` are
empty strings and the deploy silently skips.

`deploy/provision.sh` provisions the resources and is written for Azure Cloud Shell.

### Three deployment traps already hit

**`environment.ts` and the CSP must agree.** The UI's `apiBaseUrl` is the API's absolute origin, and
`connect-src` in `staticwebapp.config.json` must name that same origin. Change one without the other
and every API call is blocked or misrouted. An empty `apiBaseUrl` makes the browser request `/api`
from the Static Web App, where `navigationFallback` answers with `index.html` — a 200 carrying HTML.

**`inlineCritical` is off in the production build.** Angular's critical-CSS inliner defers the
stylesheet with `<link media="print" onload="this.media='all'">`; `script-src 'self'` blocks that
inline handler, so the stylesheet never applies. `frontend-ci` fails the build if an inline event
handler or print-deferred stylesheet reappears.

**`Network__TrustForwardedHeaders=true` is required in production.** Without it every caller shares
one rate-limit bucket and HSTS silently emits nothing. It defaults to `false` because enabling it
without a real proxy in front makes `X-Forwarded-For` spoofable.

## MediatR licensing

MediatR 13+ is dual-licensed. The Community tier is free under $5M revenue and $10M outside capital,
but the library logs a warning on every start saying production use requires a licence. A key can be
supplied via `MediatR:LicenseKey` with no code change. `MediatR` 12.5.0 is the last Apache-2.0
release and is API-compatible with this codebase.
