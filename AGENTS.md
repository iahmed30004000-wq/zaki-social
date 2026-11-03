# Zaki Social — rules for coding agents

Read `docs/SPEC_platform.docx` and `docs/BUILD_PROMPTS_GLM.docx` first. Build ONE step at a time, in order, and verify before the next.

## Talking to the owner
- The owner (Ahmad) is a business owner, not a programmer, and speaks Levantine Arabic. Reply in Arabic only, short and plain, one concrete example per explanation.
- Finish tasks without small questions. Ask only for real decisions or things only he can do (secrets, payments, logins).
- He types keys, tokens and passwords himself. Never ask for them in chat and never write them anywhere.
- Report honestly what was verified (a real run) and what was not.

## Stack (fixed)
Next.js + TypeScript + Tailwind + shadcn/ui (RTL) in `frontend/`; ASP.NET Core 8 (Api, Core, Data, Integrations, Infrastructure, Workers) in `backend/`; PostgreSQL with Row-Level Security; Redis; Hangfire; MinIO; FFmpeg; Docker; Caddy.

## Non-negotiable
- Every table and query is scoped by `workspace_id` (multi-tenant from day one).
- No secret in code, logs, tests or commits. Environment variables only; `.env.example` lists names with no values.
- Official platform APIs only. No scraping and no browser automation to bypass protections.
- OAuth tokens and secrets are encrypted at rest (AES-256).
- All publishing goes through the Redis queue and a worker, never directly from an API request.
- AI-generated content needs a human approval step before it posts.
- All AI calls go through `AiRouter`. All prompts live in the database (Prompt Hub), never in code.
- UI text in Arabic, layout RTL.

## Git and deploy
- `main` is protected: never push to it. Push a branch, open a pull request, and turn on auto-merge (`gh pr merge --auto --merge`). It merges when the `image` check passes.
- Every merge to `main` builds and publishes the two images and tells Coolify to deploy.
- Do not build or install heavy tooling on the owner's PC. Let GitHub Actions build and test.
