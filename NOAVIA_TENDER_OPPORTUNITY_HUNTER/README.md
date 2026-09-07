# Noavia Tender Opportunity Hunter

Self-hosted Docker Compose MVP for collecting tender documents, queueing extraction work, and reviewing ranked opportunities.

## Setup

1. Install Docker Engine/Desktop with Compose v2.
2. Copy `.env.example` to `.env`. Leave `OPENROUTER_API_KEY` empty for the
   deterministic local fallback (or provide your own key; never commit it).
3. Start the core stack:

   ```sh
   docker compose up --build -d
   ```

4. Open <http://localhost/>. API probes are `/api/health/live` and `/api/health/ready`.
5. Local alert email UI: Mailpit at <http://localhost:8025>. Set `SMTP_HOST`, `SMTP_PORT`, and `SMTP_FROM` for a real outbound provider.
6. Optional metrics: `docker compose --profile observability up -d` (Prometheus at `:9090`, Grafana at `:3001`).

The `migrate` Compose service applies Alembic migrations before the API and worker start. To apply a later revision manually:

```sh
docker compose exec api alembic upgrade head
```

Migrations are intentionally kept explicit; do not use `Base.metadata.create_all` in production.

## Real prospect smoke test

The default source is the public, no-credential German service.bund.de RSS
feed, which aggregates notices from federal, state, and municipal authorities.
It is filtered for software/digital/cloud/data keywords. After the stack is healthy:

```sh
curl -X POST http://localhost/api/scans -H "Content-Type: application/json" -d "{}"
# the worker claims the job; wait a few seconds, then:
curl http://localhost/api/opportunities
```

The dashboard's **Run scan**, **Refresh**, and opportunity detail dialog use
the same endpoints. To run synchronously while debugging (without the worker):
`curl -X POST http://localhost/api/scans -H "Content-Type: application/json" -d "{\"run_now\":true}"`.
Set `SOURCE_URL` to a German RSS/Atom/JSON/OCDS procurement feed and
`SOURCE_KEYWORDS` to a comma-separated list to search German opportunities.
For example:

```env
SOURCE_URL=https://your-german-public-feed.example/opportunities.json
SOURCE_KEYWORDS=software,digitalisierung,cloud,daten,beratung,IT
```

The source URL is synchronized to the configured value on the next scan, so
changing `.env` is enough; no database reset is required. The current parser
supports RSS, Atom, generic JSON arrays, and OCDS JSON. It does not scrape
HTML-only portals.

## Security

- Do not commit `.env`, API keys, database dumps, or uploaded documents.
- Put TLS and an authentication layer in front of the stack before exposing it beyond localhost. Caddy is HTTP-only in this starter.
- Keep Postgres, ClamAV, Mailpit, and observability ports private in production.
- Uploaded blobs are content-addressed locally and should be scanned by ClamAV before downstream processing.
- Model calls use OpenRouter's OpenAI-compatible Responses API with Structured Outputs and `store=false`; review your organization's data processing policy before enabling them.
- Rotate the secret, database password, and Grafana password before deployment.

## Backup and restore

Back up both the Postgres volume and the content-addressed storage volume. A simple database dump is:

```sh
docker compose exec -T postgres pg_dump -U app noavia > noavia.sql
```

Restore with `docker compose exec -T postgres psql -U app noavia < noavia.sql`, then restore `/data/storage` from your encrypted volume backup. Test restores regularly.

## MVP scope

The implementation follows the developer specification's first vertical slice:

- versioned company profiles with confirmed offerings, sectors, geographies,
  value preferences, certifications, exclusions, CPV hints, and alert recipients;
- scheduled ingestion from an approved public feed, immutable content-hashed
  source snapshots, conservative canonicalization, and material-change events;
- fail-closed ClamAV scanning, attachment type/size restrictions, SHA-256 object
  storage, PDF/DOCX/XLSX/text parsing, page/sheet markers, and OCR-needed state;
- deterministic hard filters plus stored 35/20/20/15/10 profile score components,
  explicit eligibility states, blockers, missing information, and match reasons;
- critical-fact provenance with source/snapshot, excerpt, location, extraction
  method, validation state, and content hash;
- deadline-first HOT/REVIEW/WATCH dashboard states, NEW/UPDATED/stale indicators,
  shortlist/review/skip history, source health, in-app alerts, Mailpit email alerts,
  and job-progress polling;
- an on-demand response starter containing a requirements checklist, compliance
  matrix, clarification questions, and proposal outline. Unknown company facts
  remain explicit TODOs.

Authentication remains delegated to the trusted reverse proxy for this local
deployment. `X-Organization-Id` is treated as a trusted tenant header and every
customer-owned query is organization-scoped. Do not expose this starter directly
to the public internet without an authenticating proxy that sets and strips that
header.

## Primary APIs

- `GET|PUT /api/company-profile`
- `POST /api/company-profile/documents`
- `POST /api/scans` and `GET /api/jobs/{id}`
- `GET /api/opportunities` and `GET /api/opportunities/{id}`
- `PATCH /api/opportunities/{id}/decision`
- `POST /api/opportunities/{id}/draft`
- `GET /api/opportunity-events`, `GET /api/alerts`
- `PATCH /api/alert-preferences`, `GET /api/sources/status`
- `GET /api/audit-events`

## Pilot gate

The product code implements the MVP Definition of Done, but the quantitative
pilot gates still require the specification's external, manually labelled data:
at least 50 representative tenders and five synthetic company profiles. Run that
golden-set evaluation before claiming the documented deadline, requirement,
deduplication, relevance, update-detection, and draft-factuality percentages.
