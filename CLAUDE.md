# VaultBudget AI

Privacy-first personal finance app. Bank transactions and PDF statements sync
into one ledger; identifying data is stripped before any AI call.

Rules here apply to any AI assistant — Claude Code, Cursor, anything else.
Read before acting. Backend specifics live in `backend/CLAUDE.md`.

This repository is **public**. Issues, commit messages and comments are
world-readable.

## Tech stack

- **Backend** — .NET 10 (`net10.0`, SDK 10.0.300), ASP.NET Core **controllers**,
  EF Core 10 with Npgsql 10.0.3, JWT bearer 10.0.10
- **Frontend** — React 19.2, TypeScript 6.0, Vite 8.0, react-router-dom 7.18,
  Bootstrap 5.3, `@react-oauth/google` 0.13
- **Tests** — xUnit 2.9.3, `Microsoft.AspNetCore.Mvc.Testing` 10.0.10,
  EF Core InMemory 10.0.10
- **Database** — PostgreSQL 18 locally; Azure Postgres Flexible Server in prod
- **Tooling** — `dotnet-ef` 10.0.11 as a global tool

## Structure

```
backend/            API. Controllers, Services, DTOs, Models, Data, Migrations
backend.Tests/      xUnit integration tests against the real API
frontend/src/       api/ (fetch client) components/ pages/ styles/
VaultBudget.slnx    Solution — backend + backend.Tests
```

## Commands

All verified working as of 2026-09-26.

```bash
dotnet build VaultBudget.slnx          # build; currently 0 warnings
dotnet test VaultBudget.slnx           # 18 tests, ~1s
dotnet run --project backend           # API on :5211 (https :7054)
dotnet ef migrations add <Name> --project backend
dotnet ef database update --project backend
cd frontend && npm run dev             # Vite on :5173
cd frontend && npm run build           # tsc -b && vite build
cd frontend && npm run lint
```

## Architecture

**What this codebase does:**

- Controllers under `[Route("api/...")]`, not minimal-API modules
  (`/health` is the one exception)
- EF Core accessed directly through `AppDbContext` — no repository layer
- Options pattern with `.Validate(...).ValidateOnStart()`, so bad config fails
  at boot rather than at first request
- `sealed record` DTOs, positional, in `backend/DTOs/`
- Auth tokens travel **only** in HttpOnly cookies, never in a response body

**What it deliberately does not use** — do not introduce these without asking:

- No MediatR or CQRS
- No repository or unit-of-work abstraction over EF
- No FluentValidation — validation lives in `backend/Services/AuthValidation.cs`
- No `Result<T>` — endpoints return `ActionResult<T>` with status codes
- No AutoMapper — DTOs are constructed explicitly

## Domain terms

- **Slice** — one vertical unit of work, one GitHub issue, one branch
- **Ledger** — the shared transactions table. Everything reads from it
- **Producer / consumer** — Plaid and PDF upload *produce* rows; dashboard,
  chart, AI tips, health score and recurring detection *consume* them
- **Descope trigger** — a dated decision point to cut agreed scope
- **Sprint N** — a GitHub milestone with a real due date

## Source of truth

- **How we work** → this file
- **What to build** → GitHub issues, which carry the acceptance criteria
- **What is shipped** → `main`
- **Schedule** → GitHub milestones `Sprint 0`–`Sprint 9`; issue #51 tracks
  deliverable coverage
- **Not** a source of truth: chat plans, status files in the repo. Both go
  stale and neither binds.

## Plan before code

**No source, test, config or migration changes until a plan is discussed and
explicitly approved.** A question is not approval. Silence is not approval.
Expect one or more rounds of discussion; the plan will usually change.

Keep it under ~20 lines:

> **Goal** — one sentence
> **Files** — each path, and what changes in it
> **Approach** — 3–5 bullets on how
> **Decisions** — anything with a real alternative, stated so it can be argued with

Then stop and wait.

No plan needed for: a command the user asked for directly, a typo, or a change
the user already specified line by line.

The point is that the user reviews the **design, not the diff**. Name the
trade-offs and say what an interviewer would push on — this project is
deliberate practice, not only delivery.

## Every task

1. Plan first, per the section above. Acceptance criteria come from the issue
2. One vertical slice — one issue or one deploy step. Not "build everything"
3. Match existing patterns. No drive-by refactors
4. Verify before reporting. Run the tests or the build and quote real output.
   Never "this should work". The user does auth and UI click-throughs
5. Keep the diff scoped to what was asked

## Before claiming something is missing

Check the GitHub issues — **open and closed** — the code, and the git
history before stating that a feature, requirement or file is absent.
Something can be tracked but unbuilt, or built under a name you did not
search for.

This rule exists because an unverified "requirements gap" claim about
password reset, email verification and MFA reached a client-facing document.
All three had been open issues since July.

## Ask first

Anything that writes **outside the working tree** needs explicit approval:
GitHub issues, PRs or comments; `git push`, force-push, branch deletion;
creating or deleting cloud resources; sending email.

Reading and editing local files does not. Neither does running tests or builds.

## Dependencies

**Project dependencies** — anything landing in `backend/backend.csproj` or
`frontend/package.json` — need explicit approval, every time.

**Local tooling** — a throwaway venv, a CLI installed to generate an
artifact — does not, but say what you installed. Keep it out of the repo.

## Quality gates

- Automated checks green for the area touched, with output shown
- A new test must **fail before it passes**. A test that never failed proves
  nothing
- Flag secrets, CORS, cookie `SameSite` and deploy-environment risks explicitly
- Diff still scoped

## Secrets

Never put a real credential in chat, in a file you write, or in a commit. Ask
the user to enter passwords and tokens themselves. Connection strings and keys
belong in App Service configuration or a git-ignored `.env`, never in
`appsettings.json` — which ships an empty `DefaultConnection` deliberately.

A Postgres credential was committed here once and had to be purged from all
history. Do not repeat that.

## When you are unsure

Do everything that does not depend on the unknown first, then state your
assumption or ask. Do not stall a whole task on one open question.

If the user hears a concern and reaffirms their decision, that is the answer.
Proceed with the full request.

## Who owns what

The user owns product judgment, design taste, merge and deploy clicks, and
what counts as "good enough". The assistant proposes; the user decides.

## Deploy work

Hosting is its own milestone with ordered slices: code prep → database → API
host → frontend host → smoke test. Never mixed with product stories.

## Commits

- One branch per slice, named for its issue
- Prefix `feat:`, `fix:`, `chore:` or `docs:` — lowercase, colon
- Reference the issue: `Closes #21`
- No AI attribution trailers. Do not add `Co-authored-by:` for an assistant,
  or "Generated with" lines, to commits or pull request descriptions
- Do not commit or push unless asked
