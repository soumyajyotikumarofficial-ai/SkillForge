# MVP engineering decisions

## Initial source

- Adapter: public RSS/Atom/JSON feed; default `service.bund.de` public tenders.
- Access: public, unauthenticated machine-readable feed only. The MVP does not
  bypass logins or automate authenticated procurement portals.
- Rate/cadence: one adapter execution at a time, scheduled every 1,440 minutes
  by default. Failed queue jobs retry with exponential backoff.
- Retention: source records are stored as content-hashed snapshots for audit and
  change detection. Review source licensing before changing the default source
  or retention policy.
- Owner: deployment operator.

## Parsing and file safety

- Allowed attachment types: PDF, DOCX, XLSX/XLS, TXT, and CSV.
- Limit: 15 MiB per remote file and at most five attachments per opportunity in
  the MVP pipeline.
- ClamAV is fail-closed by default. Development-only fail-open behavior requires
  both `APP_ENV=development` and the explicit `CLAMAV_FAIL_OPEN=true` override.
- Image-only PDFs are marked `ocr_required`; they are not presented as verified.

## Models and scoring

- OpenRouter remains behind an adapter and uses schema-constrained output with
  storage disabled. If it is unavailable, deterministic extraction remains active.
- Scoring version `mvp-profile-1` stores scope (35%), sector (20%), eligibility
  (20%), geography/value (15%), and strategic fit (10%) components.
- Passed deadlines, cancelled/awarded/closed status, and explicit profile
  exclusions are deterministic gates. Unknown facts produce REVIEW, not a pass.

## Response starter

- Scope: requirements checklist, compliance matrix, clarification/internal
  questions, and proposal outline only.
- Company claims may come only from confirmed profile fields. Missing capability
  evidence is emitted as an explicit TODO.

## Tenant boundary

- The local deployment trusts an authentication proxy to set
  `X-Organization-Id`. Customer-owned reads and writes are organization-scoped.
- The proxy must remove any client-supplied tenant header before setting its own.
