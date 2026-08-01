# SkillForge Architecture & Flow Guide

## 1. Overview
SkillForge is a hybrid job-matching application that combines:
- ASP.NET Core backend with Entity Framework Core and SQLite
- Live job fetching via Apify
- Resume parsing and scoring via a Gemini AI integration
- Candidate matching between parsed candidate profiles and scraped jobs
- A lightweight frontend dashboard for candidates

This guide explains the architecture, main components, request flow, and configuration.

---

## 2. Project Structure

### Top-level folders
- `skillforge_phase1/backend`: ASP.NET Core API and services
- `skillforge_phase1/frontend/frontend-app`: Candidate-facing static dashboard
- `skillforge_phase1/frontend/angular-app`: additional Angular-based frontend assets
- `skillforge_phase1/src`: supporting app code and module definitions

### Important backend files
- `Program.cs`: application startup and service registration
- `appsettings.json`: configuration for JWT, database, Gemini, Apify, and logging
- `Data/SkillForgeDbContext.cs`: EF Core database model and seed data
- `Models/Models.cs`: entity classes for users, candidates, jobs, matches, and fetch history
- `Services/AIService.cs`: resume parsing and AI-based candidate analysis
- `Services/ApifyJobService.cs`: live job scraping client
- `Services/LiveJobFetcherService.cs`: scheduled/manual live job sync engine
- `Controllers/CandidateController.cs`: resume upload, candidate profile, and match endpoint
- `Controllers/JobController.cs`: job query, manual sync trigger, and sync status endpoints

---

## 3. Backend Architecture

### 3.1. Startup and registration (`Program.cs`)
- Adds controllers, Swagger, CORS, authentication, and EF Core SQLite support
- Registers `AIService` and `ApifyJobService` as scoped services
- Registers `LiveJobFetcherService` as a singleton hosted service
- Ensures the SQLite database is created on startup

### 3.2. Database model and persistence
- `SkillForgeDbContext` defines DbSets:
  - `Users`
  - `Candidates`
  - `CandidateSkills`
  - `Jobs`
  - `JobSkills`
  - `JobMatches`
  - `JobFetchHistories`
- Model relationships:
  - A `Candidate` owns many `CandidateSkills` and `JobMatches`
  - A `Job` owns many `JobSkills` and `JobMatches`
  - `JobFetchHistory` stores live fetch audit data
- Seed data includes a default recruiter user and sample jobs

### 3.3. Live job sync engine
File: `Services/LiveJobFetcherService.cs`

Purpose:
- Automatically fetch live jobs using Apify
- Run daily at 9 AM local time
- Allow manual triggering by API
- Enforce daily insert limits and country-specific geo filtering

Key behaviors:
- Uses `BackgroundService` with a loop that waits until the next 9 AM execution
- Reads configuration keys from `appsettings.json`:
  - `Apify:ApiToken`
  - `Apify:ActorId`
  - `Apify:DailySearchQueries`
  - `Apify:DailyLocations`
  - `Apify:DailyCountries`
  - `Apify:DailyFetchLimit`
- Maintains target mapping by country and city/state via `CountryTargets`
  - e.g. `IN` maps to Bengaluru, Chennai, Gurugram, etc.
  - `US` maps to Austin, New York, San Francisco, Seattle
- Stops inserting after `DailyFetchLimit` jobs, preventing runaway ingestion
- Skips jobs if:
  - they are not IT-related
  - geo mismatch occurs
  - they are duplicates
  - the source created date is older than last successful fetch

Sync result metrics include:
- `InsertedCount`
- `SkippedCount`
- `FilteredByDateCount`
- `DuplicateCount`
- `NonItFilteredCount`
- `GeoMismatchCount`
- `ApiFetchedCount`
- `TargetCombinationCount`

### 3.4. Apify job scraper client
File: `Services/ApifyJobService.cs`

Purpose:
- Call an Apify actor to scrape live job data
- Normalize varied Apify dataset field names into a common job shape

Flow:
- Use the configured Apify actor endpoint and token
- Send a JSON payload containing search query, location, country, and maximum item count
- Map each returned item to `ApifyJobResult`
- Normalize salary, apply link, job title, location, and benefit fields
- Derive currency from country when available

### 3.5. Resume parsing and analysis
File: `Services/AIService.cs`

Purpose:
- Extract structured candidate data from resumes
- Compute a dynamic resume score using the Gemini AI API
- Identify candidate role, skills, summary, and experience

Flow:
1. Accept resume file upload and optional `JobHuntPreferences`
2. Extract text from PDF, DOCX, or TXT
3. Call `AnalyzeResumeAsync` to send prompt + extracted text to Gemini
4. Parse structured response into a `ResumeAnalysisResult`
5. Use candidate-supplied role aspiration when present
6. Formulate a target job query based on extracted role or aspiration

Important notes:
- The AI prompt explicitly requests a score from 1–100 and a deduced role
- It also requires the engine to infer a role if not explicitly present in the resume
- The service logs failures when the resume cannot be parsed or the score is missing

### 3.6. Candidate resume endpoint and matching
File: `Controllers/CandidateController.cs`

Endpoint: `POST /api/candidate/upload-resume`

Responsibilities:
- Accept resume upload plus optional country, location preferences, and role aspiration
- Call `AIService.ProcessAndAnalyzeResumeAsync`
- Persist or update candidate profile in SQLite
- Store extracted skills as `CandidateSkill` records
- Build a target search rule using explicit role or derived skills
- Match existing jobs in the database by:
  - job title and description keywords
  - normalized candidate skills
  - candidate-preferred locations and country
- Create or update `JobMatch` records
- Return structured payload for frontend consumption

Matching strategy:
- If the candidate supplies a target role, match jobs by title keywords
- Otherwise, match jobs by skill occurrence in job title/description
- If a job has required skills, compute a percentage match
- If a job looks like a developer/engineer/analyst posting but no skills overlap, default to a low baseline score

### 3.7. Job management controller
File: `Controllers/JobController.cs`

Endpoints:
- `GET /api/job`: get all jobs
- `GET /api/job/{id}`: get job details
- `GET /api/job/live-sync-status`: latest sync metrics and job counts
- `POST /api/job/run-daily-sync?force=true`: manually trigger a live fetch

Purpose:
- Provide visibility into live fetch health
- Enable manual job scraping and inspection
- Return totals and recent fetch history for the UI or administrative use

---

## 4. Frontend Architecture

File: `frontend/frontend-app/candidate-dashboard.html`

### Key UI pieces
- A resume upload zone with drag/drop and file selector
- Country dropdown and preferred location fields
- Optional target role aspiration input
- Analysis result panel showing:
  - resume score
  - candidate summary
  - extracted skills
  - matched jobs

### Interaction flow
1. Candidate chooses a resume file
2. Candidate optionally chooses country and preferred locations
3. Candidate optionally enters a target role
4. Candidate clicks `Analyze Profile Metrics`
5. Frontend posts data to the backend resume endpoint
6. Backend returns parsed candidate profile, skill list, and matching jobs
7. UI renders score, summary, skills, and job cards

### Frontend notes
- Uses vanilla HTML/CSS/JS, so it is easy to host as static content
- The dashboard includes full country support and a state/location autocomplete list
- The UI is designed for candidate resume processing rather than recruiter job posting

---

## 5. Data Flow Summary

### Resume ingestion and matching
1. Candidate uploads resume via the frontend
2. Backend extracts the text and parses it using Gemini AI
3. AI returns:
   - candidate name, email, phone, location
   - experience, highest qualification, role
   - skills, summary, score
4. Candidate data is saved or updated in SQLite
5. Candidate skills are stored in `CandidateSkill`
6. Job matching runs against current in-database jobs
7. Job matches are stored in `JobMatch`
8. Response includes matched jobs and candidate analytics

### Live job sync
1. `LiveJobFetcherService` runs automatically at 9 AM local time
2. It uses configured queries, countries, and locations
3. `ApifyJobService` runs the Apify actor using that search data
4. Returned jobs are normalized and filtered:
   - remove duplicates
   - enforce IT-related keywords
   - ensure country/city geo relevance
   - respect date-based filtering unless forced
5. New jobs are saved as `Job` records
6. Fetch metrics are stored in `JobFetchHistory`

---

## 6. Key API Endpoints

### Candidate endpoints
- `POST /api/candidate/upload-resume`
  - Form fields: `file`, `country`, `location1`, `location2`, `location3`, `roleAspiration`
  - Returns candidate profile, score, skills, and job matches

### Job endpoints
- `GET /api/job`
- `GET /api/job/{id}`
- `GET /api/job/live-sync-status`
- `POST /api/job/run-daily-sync?force=true`

### Observability
- `live-sync-status` returns the latest fetch run metadata and counts
- Manual sync returns a full set of sync counters for debugging

---

## 7. Configuration

### `appsettings.json`
Important sections:
- `ConnectionStrings:DefaultConnection`: SQLite file path
- `Jwt`: token key, issuer, audience
- `Gemini`: `ApiKey`, `Endpoint`
- `Apify`: `ApiToken`, `ActorId`, `BaseUrl`, query/location/country settings, and `DailyFetchLimit`

Example:
```json
"Apify": {
  "ApiToken": "your-apify-api",
  "ActorId": "misceres~indeed-scraper",
  "BaseUrl": "https://api.apify.com/v2",
  "DailySearchQueries": "Software Engineer,.NET Developer,Java Developer,...",
  "DailyLocations": "Kolkata,Bengaluru,Hyderabad,...",
  "DailyCountries": "IN,US,GB,CA,DE,...",
  "DailyFetchLimit": 10
}
```

### Important behavior
- Daily fetch limit is enforced to avoid too many inserts
- Country-to-location mapping is used in `LiveJobFetcherService`
- `Gemini` must be configured with a valid API key

---

## 8. Running the application

### Backend
From `skillforge_phase1/backend`:
```powershell
dotnet build
dotnet run
```
- The backend listens on `https://localhost:5123` and `http://localhost:5000`
- Swagger is enabled in development mode for testing APIs

### Frontend
From `skillforge_phase1/frontend/frontend-app`:
```powershell
npm install
npm run dev
```
- Static dashboard can be served by Vite or any static web host
- The frontend posts data to the backend API endpoints

---

## 9. Customization and extension

### Add new job sources
- Update `Apify:ActorId` or replace `ApifyJobService` logic to call a different scraping actor or API
- Add improved normalization rules in `MapDatasetItem`

### Improve candidate match quality
- Extend `ParseJobSkillsText` with more domain-specific keywords
- Add weighted role matching or skill proficiency scoring
- Use resume summary, experience, and job salary to refine `JobMatch` scores

### Add recruiter features
- Add new controllers for job posting, candidate review, and admin dashboards
- Add authentication role enforcement to protect recruiter endpoints

---

## 10. Architecture at a glance

### Core components
- `Program.cs` → bootstraps services and database
- `LiveJobFetcherService` → scheduled and manual live job ingestion
- `ApifyJobService` → job scraper integration and normalization
- `AIService` → resume parsing, role extraction, scoring
- `CandidateController` → resume upload, profile persistence, matching
- `JobController` → job browsing, sync control, status metrics
- `SkillForgeDbContext` → entity model, relationships, persistence

### Data model relationships
- Candidate ↔ CandidateSkill
- Candidate ↔ JobMatch ↔ Job
- Job ↔ JobSkill
- JobFetchHistory records sync metadata

### Flow summary
1. Candidate uploads resume
2. Resume text is parsed and scored
3. Candidate profile saved
4. Skills extracted
5. Job search logic filters and scores jobs
6. Matches are returned to the UI
7. Background sync keeps job inventory fresh

---

## 11. Downloading and sharing
This markdown file can be downloaded directly from the repository.
If you need a PDF version, convert `ARCHITECTURE_AND_FLOW_GUIDE.md` using an external Markdown-to-PDF tool.
