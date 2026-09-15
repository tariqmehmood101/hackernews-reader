# Hacker News Reader

A reader for the newest Hacker News stories, built as two independently deployable halves in one
repository.

| | Stack | Location |
|---|---|---|
| **API** | .NET 10 · MediatR (CQRS) · FluentValidation | `backend/` |
| **UI** | Angular 20 · standalone components · signals | `frontend/` |

**Features:** newest-stories list (title + link, links are optional), search across the whole feed,
paging, an in-memory cache of the feed, and automated unit + integration tests on both halves.

---

## Quick start

**Prerequisites:** .NET SDK 10.0.3xx or later, Node.js 20.19+/22.12+/24.x.

```bash
# API  →  http://localhost:5070
cd backend
dotnet run --project src/HackerNews.Api

# UI   →  http://localhost:4200   (in a second terminal)
cd frontend
npm install
npm start
```

The API warms its cache at startup; the first request may wait a few seconds for the initial
fetch, after which responses are served from memory.

```bash
# Backend: 170 tests + an enforced 80% line-coverage gate
cd backend && ./scripts/Test-Coverage.ps1

# Frontend: 86 unit tests (end-to-end needs the API running — see Testing below)
cd frontend && npm run test:ci
```

---

## API

Base path `/api/stories`. All responses are camelCase JSON; all failures are RFC 7807
`application/problem+json`.

| Method | Route | Notes |
|---|---|---|
| `GET` | `/api/stories/newest?page=1&pageSize=20&search=` | `page` ≥ 1, `pageSize` 1–100, `search` ≤ 100 chars |
| `GET` | `/api/stories/{id}` | Served from cache, falling back to upstream |
| `GET` | `/health` | Azure App Service probe path |
| `GET` | `/openapi/v1.json` | OpenAPI document (Development only) |

```jsonc
// GET /api/stories/newest?page=1&pageSize=2
{
  "items": [
    { "id": 49667734, "title": "Why Your Memory API Benchmarks Are Inflated",
      "url": "https://chatsorter.com", "by": "JacobH1234",
      "time": "2026-09-12T01:33:35+00:00", "score": 1, "descendants": 0 },
    { "id": 49667700, "title": "Ask HN: What are you working on?",
      "url": null,                              // text posts have no link — render as plain text
      "by": "gmays", "time": "2026-09-12T01:28:17+00:00", "score": 1, "descendants": 0 }
  ],
  "totalCount": 500, "page": 1, "pageSize": 2, "totalPages": 250,
  "hasPreviousPage": false, "hasNextPage": true
}
```

**Status codes:** `400` validation failure (with a per-field `errors` object) · `404` unknown story
· `429` rate limit · `503` upstream unreachable and no cached data · `500` anything else, with no
internal detail leaked.

---

## Backend architecture

```
backend/src/HackerNews.Api/
├─ Program.cs                          # composition root
├─ Controllers/
│  └─ StoriesController.cs             # binds → sends to mediator → unwraps. No logic.
├─ Features/Story/
│  ├─ GetNewestStoriesHandler.cs       # Query + Response + Validator nested in the handler
│  ├─ GetStoryByIdHandler.cs           #   "
│  ├─ Dto/
│  │  ├─ StoryDto.cs                   # shared across handlers
│  │  └─ StorySnapshot.cs
│  ├─ Options/StoryCacheOptions.cs
│  └─ Services/
│     ├─ HackerNewsClient.cs               : IHackerNewsClient
│     ├─ StorySnapshotCache.cs             : IStorySnapshotCache
│     ├─ StorySnapshotRefresher.cs         : IStorySnapshotRefresher
│     ├─ StorySnapshotBackgroundService.cs → framework IHostedService
│     └─ Interfaces/                       # one interface per service
├─ Exceptions/
│  ├─ GlobalExceptionHandler.cs        # IExceptionHandler → ProblemDetails
│  ├─ NotFoundException.cs
│  └─ UpstreamUnavailableException.cs
└─ Shared/
   ├─ Behaviors/                       # ValidationBehavior, LoggingBehavior
   ├─ Options/RateLimitingOptions.cs   # cross-cutting, not feature-specific
   └─ Models/PagedResult.cs            # feature-agnostic only
```

Every service is registered and injected through its interface. The one exception is
`StorySnapshotBackgroundService`, which already implements the framework's `IHostedService` via
`BackgroundService` and is never resolved by a caller — a hand-rolled interface on top would be
duplicate ceremony.

### Request flow

```
Controller → ISender → LoggingBehavior → ValidationBehavior → Handler → IStorySnapshotCache
                                              ↓ (on failure)
                                       GlobalExceptionHandler → ProblemDetails
```

### Caching

Fetching 500 stories per request would be unusable, and search has to span the whole feed — so the
feed is materialised in memory and every read is served from it.

- **`StorySnapshotRefresher`** does one pass: fetch the ≤500 newest ids, fetch only the ids it has
  not already seen (throttled to 15 concurrent), drop dead/deleted/untitled items, order newest
  first, and swap the snapshot atomically. Steady state is a few dozen upstream calls per pass,
  not 500.
- **`StorySnapshotBackgroundService`** owns only the schedule. Splitting the two is what lets a
  refresh pass be unit-tested directly, with no host, timers or sleeps.
- **`StorySnapshotCache`** holds an immutable snapshot swapped with `Volatile.Write`, so reads are
  lock-free.

**Failure behaviour.** A failed pass keeps the previous snapshot rather than replacing it — that
covers an empty id list, individual item failures, and a total upstream outage. If the cache has
*never* loaded, requests wait up to `Cache:InitialLoadTimeoutSeconds` and then return **503**.
Deliberately not an empty `200`, which a client cannot tell apart from "no stories exist".

**Known trade-off.** Reusing cached items means `score` and `descendants` can drift between full
refreshes. `Cache:FullRefreshEveryNCycles` (default 12, ≈hourly) forces a complete re-fetch.

---

## Frontend architecture

```
frontend/src/app/
├─ app.config.ts                       # provideHttpClient(withFetch()), provideRouter
├─ core/
│  ├─ models/story.ts                  # Story, PagedResult<T>, StoryQuery
│  └─ services/story-api.ts            # the only place that knows the URL shape
│  ├─ pipes/                           # highlight (search matches), relative-time
│  └─ services/theme.ts                # light/dark, remembered per browser
└─ features/stories/
   ├─ story-list/                      # list + search + states
   └─ pagination/                      # presentational pager
```

- State is signals; templates use `@if` / `@for`.
- Search is debounced 300 ms and resets to page 1, since a filtered list is a different list.
- Requests go through `switchMap`, so a slow response for an old page can never overwrite a newer
  one.
- Search matches are highlighted in the title and author. The highlight pipe returns *text
  segments*, never markup — see Security below.
- **A story with no `url` renders as plain text with a "no link" badge, never a dead anchor.**
- Loading, empty-result and error states are all rendered; the error state offers a retry.

---

## Configuration

`backend/src/HackerNews.Api/appsettings.json` — every value is overridable by an environment
variable or an Azure App Service application setting.

| Key | Default | Purpose |
|---|---|---|
| `HackerNews:BaseUrl` | `https://hacker-news.firebaseio.com/v0/` | Upstream API |
| `Cache:RefreshIntervalSeconds` | `300` | Time between refresh passes |
| `Cache:RetryIntervalSeconds` | `30` | Shorter delay after a failed pass |
| `Cache:InitialLoadTimeoutSeconds` | `10` | How long a request waits on a cold cache before 503 |
| `Cache:MaxConcurrentItemFetches` | `15` | Upstream fetch concurrency |
| `Cache:FullRefreshEveryNCycles` | `12` | Force a complete re-fetch every Nth pass |
| `Cache:MinimumYieldPercent` | `50` | Share of listed ids a pass must resolve to replace a loaded snapshot |
| `RateLimiting:PermitLimitPerMinute` | `100` | Fixed window per client IP; `/health` is exempt |
| `Network:TrustForwardedHeaders` | `false` | **Set to `true` on App Service** — see below |
| `Network:TrustedProxyHops` | `1` | Proxy hops in front; only this many header entries are read, from the right |
| `Cors:AllowedOrigins` | `[]` (dev: `http://localhost:4200`) | Allowed UI origins |
| `MediatR:LicenseKey` | `""` | See licensing below |

**`Network:TrustForwardedHeaders` must be `true` wherever a proxy terminates the connection**
(Azure App Service does). Without it, `RemoteIpAddress` is the front end's, so the rate limiter
puts every caller in the world into one bucket and throttles them collectively. It defaults to
`false` because with nothing in front, `X-Forwarded-For` is merely client-supplied — honouring it
would let anyone rotate a fake address per request and walk past the limiter. Only the rightmost
`TrustedProxyHops` entries are read, which is the end a proxy appends to, so a value planted at the
left of the header is never mistaken for the caller.

The frontend's API base URL lives in `src/environments/`: `environment.development.ts` points at
`http://localhost:5070`; `environment.ts` uses an empty base (same origin), which suits Azure
Static Web Apps with a linked backend.

### ⚠️ MediatR licensing

MediatR 13+ is dual-licensed (RPL-1.5 + a Lucky Penny commercial licence). The free **Community**
tier covers organisations under **$5M revenue** that have taken under **$10M outside capital**.

With no key configured the library runs unrestricted but logs this on every start:

> *You do not have a valid license key for the Lucky Penny software MediatR. This is allowed for
> development and testing scenarios. If you are running in production you are required to have a
> licensed version.*

Note this contradicts the vendor FAQ, which states no key is needed for the Community tier —
**verify your position before deploying to production.** A key can be supplied via the
`MediatR:LicenseKey` setting (or an App Service setting) with no code change and nothing committed
to git. If the licence is unacceptable, `MediatR` **12.5.0** is the last Apache-2.0 release and is
API-compatible with this codebase.

---

## Testing

| Suite | Count | What it covers |
|---|---|---|
| `backend/tests/HackerNews.UnitTests` | 127 | Paging arithmetic, boundaries and offset overflow, search semantics, validators, both pipeline behaviours, snapshot cache concurrency and timeout, refresher diffing/eviction/failure isolation/concurrency cap, partial-refresh yield floor, the hosted service's loop and shutdown, options binding, HN client parsing, exception mapping, by-id upstream failure vs. caller cancellation |
| `backend/tests/HackerNews.IntegrationTests` | 43 | Real HTTP stack via `WebApplicationFactory`, with only the outbound handler faked — routing, model binding, the mediator pipeline, ProblemDetails, caching behaviour, 503 on upstream failure, CORS allow **and** deny, rate limiting (429) including per-caller buckets behind a proxy, header spoofing, and the health probe's exemption, OpenAPI document |
| `frontend/src/**/*.spec.ts` | 58 | `StoryApi` params and error paths, search debounce, paging, page size, clear/`/`/`Esc`, linkless rendering, error/retry, pager windowing and steps, pager node identity and focus across a sliding window, relative time, theme store and toggle |
| `frontend/e2e/full-stack.spec.ts` | 12 | Playwright against the **real API and live feed** — stories render through the whole stack, search narrows, paging doesn't repeat, Last reaches the end, 400/404 contracts, cache latency |

Backend stack: **xunit.v3** · **Shouldly** · **NSubstitute** — all permissively licensed. No test
touches the network; the upstream API is faked in both suites.

**Coverage gate.** `./scripts/Test-Coverage.ps1` runs both suites, merges the reports, and fails
below **80% line coverage**. Current: **100% lines, 100% branches**. Generated code (the OpenAPI
source generator's output) is excluded via `coverage.settings.xml`.

### End-to-end (Playwright)

The suite runs against the whole stack — real browser, real Angular, the real API, the live Hacker
News feed. Nothing is stubbed.

```bash
# one terminal
cd backend && dotnet run --project src/HackerNews.Api

# another
cd frontend
npm run e2e         # the suite
npm run e2e:ui      # the Playwright UI runner
npm run e2e:report  # open the last HTML report
```

Playwright starts the Angular dev server itself, but **the API must already be running**. Specs
skip themselves with a clear message when it is unreachable, so a forgotten backend reads as
"skipped" rather than as a false pass.

Because the data is live, assertions are on invariants rather than fixed numbers — `Last reaches
the end of the real feed` matches `page (\d+) of \1` instead of hardcoding 25, and every assertion
uses Playwright's auto-retrying `expect` so none of them race the UI.

**What this does not cover.** These tests depend on a third party, so they cannot run on every
push, and they cannot pin exact counts. A UI regression that only shows up for a specific dataset
will not be caught here — the unit and integration suites are what hold that line.

---

## Security

The app renders user-submitted Hacker News content verbatim, so escaping is the property that
matters most. Angular neutralises both vectors — HTML in a title renders as text, and a
`javascript:` url is rewritten to `unsafe:javascript:` — and three tests in `story-list.spec.ts`
pin that rather than trusting it, so swapping an interpolation for `innerHTML` fails the build.

Outbound story links carry `rel="noopener noreferrer"`, so clicking one leaks neither the opener
nor the referrer.

**Search highlighting is the obvious place to undo all that**, since the natural implementation is
to build a string of `<mark>` tags and assign it to `innerHTML` — handing script execution to
anyone who submits a story. `core/pipes/highlight.ts` instead returns *text segments*, and the
template wraps the matched ones in `<mark>` elements, so ordinary interpolation keeps escaping
every character. It also matches with `indexOf` rather than a `RegExp`, so a search term full of
regex metacharacters needs no escaping and cannot become a backtracking input.

### Transport

`app.UseHsts()` runs outside Development. Two things about it are easy to get wrong:

- **There is deliberately no `UseHttpsRedirection()`.** Behind App Service, Kestrel sees plain HTTP.
  With forwarded headers untrusted, an in-app redirect to HTTPS would be forwarded back as HTTP and
  loop forever. The redirect belongs to the platform — turn on **HTTPS Only** in App Service.
- **HSTS is silent unless the forwarded scheme is trusted.** The middleware only emits on a response
  the app believes is HTTPS, so without `Network:TrustForwardedHeaders=true` the header is never
  sent and the hardening looks configured while doing nothing. `Hsts_is_silently_lost_unless_the_
  forwarded_scheme_is_trusted` exists to make that failure visible.

HSTS also skips `localhost` by design, which is why the tests present a real `Host` header.

### Frontend headers

`frontend/staticwebapp.config.json` applies a CSP, `nosniff`, `X-Frame-Options: DENY`,
`Referrer-Policy` and `Permissions-Policy` to every response, and adds the `navigationFallback`
that makes a hard refresh on a deep link work.

The CSP is strict where it can be — `script-src 'self'`, since the production build emits no inline
scripts — and permissive only where Angular requires it: `style-src` needs `'unsafe-inline'`
because the build inlines critical CSS into a `<style>` block.

> **If you host the API on a different origin** (a separate App Service rather than a Static Web
> Apps linked backend), add that origin to `connect-src`. Otherwise the browser blocks every API
> call and the UI shows nothing but "Could not load stories".

### Deployment checklist

| Setting | Where | Why |
|---|---|---|
| `Network__TrustForwardedHeaders=true` | App Service | Without it every user shares one rate-limit bucket, and HSTS never emits |
| **HTTPS Only** = On | App Service | The HTTPS redirect, which the app deliberately does not do itself |
| `connect-src` updated | `staticwebapp.config.json` | Only if the API is on a different origin than the UI |

⚠️ Set `TrustForwardedHeaders` **only** where a proxy really sits in front. On a directly exposed
host it lets anyone forge `X-Forwarded-For` and walk past the rate limiter, which is why it is off
by default.

## CI

Three GitHub Actions workflows. The first two are path-filtered so the halves stay independently
buildable:

| Workflow | Trigger | Does |
|---|---|---|
| `backend-ci.yml` | `backend/**` | restore → build (warnings-as-errors) → test + coverage gate → upload report |
| `frontend-ci.yml` | `frontend/**` | `npm ci` → production build → unit tests |
| `e2e-live.yml` | nightly + manual | starts the real API, runs the end-to-end suite against the live feed |

End-to-end tests are deliberately kept off the per-push path: they depend on a third-party service,
so a Hacker News outage should not redden an unrelated pull request.

---

## Key prompts used

The project was built with Claude Code. The prompts that actually shaped it:

1. **The brief** — "Create a Hacker News API using .NET Core 10 using the Mediator pattern via
   `MediatR.Extension.Microsoft.DependencyInjection` to achieve CQRS with fluent validation;
   frontend using Angular v18; keep both in a single repo with separate `backend` and `frontend`
   directories", followed by the prescribed folder layout (controllers at root, `Features/<Name>/`
   with `Services/` and a nested interfaces folder, `Exceptions/` and `Shared/` at root, queries
   and responses nested inside handler classes), the feature list, and "Ask before planning
   anything if there is any confusion."

2. **Verify before planning** — a standing instruction from earlier sessions to critique the plan
   and check package existence, versions and licence terms against nuget.org and vendor docs
   rather than asserting them from memory. This is what surfaced every issue in the next section.

3. **Four clarifying decisions** — MediatR version vs. licence; Angular version given v18's EOL;
   caching strategy (background snapshot vs. lazy per-request); delivery scope (CI only vs. CI +
   Azure deploy).

4. **Two structural corrections during planning** — "move StoryDto to a Dto directory under the
   Story directory", and "create an interface folder under service which will have an interface of
   each service."

---

## How AI suggestions were accepted, modified, or rejected

### Rejected — and why

| Suggestion | Outcome |
|---|---|
| `MediatR.Extensions.Microsoft.DependencyInjection` (from the brief) | **Rejected.** Deprecated since Feb 2023 (last v11.1.0); nuget.org states its functionality is "folded into the main MediatR package". `AddMediatR` now ships in `MediatR` itself. |
| **Angular 18** (from the brief) | **Rejected.** End-of-life — Angular's docs state "v2 to v19 are no longer supported" — and incompatible with the installed Node v24.16.0. Replaced with Angular 20 (LTS to Nov 2026) after checking with the author. |
| `FluentValidation.AspNetCore` | **Rejected.** Deprecated. Validation runs through a MediatR pipeline behaviour instead, which also keeps it framework-agnostic. |
| **FluentAssertions** (the reflexive default) | **Rejected.** v8 requires a paid licence for commercial use. Replaced with Shouldly (BSD-3) and NSubstitute (BSD-3). |
| `coverlet.collector` / `coverlet.msbuild` | **Rejected after testing.** Neither hooks .NET 10's Microsoft.Testing.Platform mode — the run reported zero tests. Replaced with `Microsoft.Testing.Extensions.CodeCoverage` plus `dotnet-coverage` for merging. |
| A partial `karma.conf.js` with a no-sandbox launcher | **Rejected after testing.** The Angular builder replaces rather than merges that config, which clobbered the jasmine framework ("describe is not defined"). Reverted to the builder's default. |

### Modified

- **Interface convention.** The first draft put an interface only on the services that obviously
  needed one. Corrected to one interface per service — which in turn exposed that
  `IStorySnapshotRefresher` would be a hollow abstraction over a `BackgroundService`. The refresher
  was therefore **split in two**: `StorySnapshotRefresher` (the logic, behind the interface) and
  `StorySnapshotBackgroundService` (the schedule). This earned the interface and made the refresh
  logic directly unit-testable — 11 of the backend tests exist only because of that split.
- **`StoryDto` location.** Moved from the feature root into `Features/Story/Dto/` on review, as a
  sibling of `Services/`. Per-handler `Query`/`Response` types stayed nested in their handlers.
- **Caching.** The initial instinct — cache the id list and fetch pages on demand — cannot support
  server-side search without fetching everything anyway. Replaced with the background snapshot,
  then refined with per-item reuse so a steady-state pass costs a few dozen calls instead of 500.
- **Upstream failure handling.** An early version would have published an empty snapshot when every
  fetch failed, silently wiping good data. Changed to keep the previous snapshot and return 503
  when nothing has ever loaded. Later widened: guarding only the *empty* case still let a
  half-failed pass collapse 500 stories into a handful, so a full cycle now re-fetches **over the
  top of** the cached items instead of clearing them first — a failed re-fetch leaves a merely
  stale story in place — and `Cache:MinimumYieldPercent` backstops the rest.
- **No upper bound on `Page`.** `PageSize` is capped, `Page` deliberately is not: a page past the
  end is a valid request that answers with an empty page and the real total, so a client's pager
  stays correct instead of meeting a 400. That only holds because `PagedResult.From` computes its
  offset in `long` — in `int` the product overflows and wraps negative, and `Skip` reads a negative
  count as zero, which would serve page 1's rows under a far-off page number.
- **Coverage measurement.** The first run reported 34.9% — the OpenAPI source generator's emitted
  file was being counted. Excluded generated code rather than lowering the threshold.

### A real bug the tests caught

`ValidationBehavior` originally shared one `ValidationContext` across all validators for a request.
FluentValidation accumulates failures into a list that every returned `ValidationResult` wraps, so
each message came back **duplicated** — and mutating one context from concurrently-running
validators is a race besides. A test asserting the exact set of messages failed, which is how it was
found; each validator now gets its own context. It was invisible in manual testing because each
request currently has exactly one validator.

### Accepted as proposed

Vertical-slice feature folders; the MediatR pipeline for validation and logging;
`IExceptionHandler` + ProblemDetails; `Microsoft.Extensions.Http.Resilience` for retry/timeout/
circuit-breaking; `PagedResult<T>` in `Shared`; config-driven CORS; `/health` for the App Service
probe; per-IP rate limiting; signals + `switchMap` on the frontend; path-filtered CI workflows.

---

## Not included

Azure deployment workflows and infrastructure-as-code were deliberately deferred — the current
scope is CI only. The API is App Service-ready (config-driven CORS, `/health` probe) and the
frontend build output suits Static Web Apps.
