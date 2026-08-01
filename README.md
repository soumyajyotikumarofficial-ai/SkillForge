# 🛠️ SkillForge

**SkillForge** is an AI-powered job matching platform that connects candidates and recruiters through intelligent resume analysis, live job aggregation, and preference-driven matching. Upload a resume, let Gemini AI score and summarize it, then get matched against a continuously updated feed of live job postings — filtered by work mode, country, location, and career aspirations.

---

## ✨ Features

### 👤 For Candidates
- **JWT-based authentication** — secure register/login flow with hashed credentials (BCrypt)
- **AI resume analysis** — upload a PDF/DOCX/TXT resume and get an instant AI-generated score, summary, extracted skills, and role detection powered by **Google Gemini**
- **Multi-resume management** — keep up to 2 resumes on file, switch the active one at any time, and pick up right where you left off across logins
- **Smart job matching** — matches are ranked by skill overlap against your active resume
- **Live search preferences** — filter matches by Work Mode (WFH / Hybrid / WFO), Country, up to 3 preferred Locations/States, and an optional Target Role — re-applied fresh on every search, never stale
- **Direct-to-company apply links** — jump straight to the original job posting

### 🧑‍💼 For Recruiters
- Dedicated recruiter authentication and dashboard
- Post new job openings
- Review matched candidates for posted roles

### ⚙️ Under the Hood
- **Live job ingestion engine** — a background worker that syncs fresh listings daily via the Apify job-scraper API, with configurable search queries, target countries/cities, de-duplication, and daily insert caps
- **Resume parsing pipeline** — extracts raw text from PDF/DOCX/TXT and sends it to Gemini for structured analysis (name, contact info, experience, qualifications, skills, and a 1–100 fit score)
- **Fuzzy, resilient matching** — location/country/skill matching tolerates missing or partial job metadata instead of silently returning empty result sets

---

## 🏗️ Tech Stack

| Layer            | Technology |
|------------------|------------|
| Backend API      | ASP.NET Core (.NET 10 preview), C# |
| Database         | SQLite via Entity Framework Core |
| Authentication   | JWT Bearer + BCrypt password hashing |
| AI Analysis      | Google Gemini API |
| Live Job Sourcing| Apify job-scraper actor |
| Frontend         | Vanilla HTML/CSS/JS dashboards (candidate + recruiter) |
| API Docs         | Swagger / Swashbuckle |

---

## 📂 Project Structure

```
SkillForge/
├── skillforge_phase1/
│   ├── ARCHITECTURE_AND_FLOW_GUIDE.md   # Deep-dive architecture & data-flow reference
│   ├── backend/                         # ASP.NET Core Web API
│   │   ├── Controllers/                 # Auth, Candidate, Recruiter, Job, Matching, AI
│   │   ├── Services/                    # AIService, ApifyJobService, LiveJobFetcherService, MatchingService
│   │   ├── Models/                      # EF Core entities (User, CandidateProfile, Job, Match, ...)
│   │   ├── Data/                        # SkillForgeDbContext
│   │   ├── Migrations/                  # EF Core migrations
│   │   └── appsettings.json             # DB, JWT, Gemini, and Apify configuration
│   └── frontend/
│       ├── frontend-app/                # Candidate & recruiter dashboards (vanilla JS)
│       └── angular-app/                 # Supplementary Angular frontend assets
└── README.md
```

For a detailed breakdown of the backend architecture, request flows, and data model relationships, see [ARCHITECTURE_AND_FLOW_GUIDE.md](skillforge_phase1/ARCHITECTURE_AND_FLOW_GUIDE.md).

---

## 🚀 Getting Started

### Prerequisites
- [.NET 10 SDK (preview)](https://dotnet.microsoft.com/download)
- Node.js (for the frontend dev server, if used)
- A [Google Gemini API key](https://ai.google.dev/) for resume analysis
- An [Apify API token](https://apify.com/) for live job ingestion (optional but recommended)

### 1. Configure the backend

Update `skillforge_phase1/backend/appsettings.json` with your own values:

```json
{
  "ConnectionStrings": { "DefaultConnection": "Data Source=skillforge.db" },
  "Jwt": { "Key": "...", "Issuer": "...", "Audience": "..." },
  "Gemini": { "ApiKey": "your-gemini-api-key" },
  "Apify": {
    "ApiToken": "your-apify-api-token",
    "ActorId": "misceres~indeed-scraper",
    "DailySearchQueries": "Software Engineer,.NET Developer,...",
    "DailyLocations": "Kolkata,Bengaluru,Hyderabad,...",
    "DailyCountries": "IN,US,GB,CA,DE",
    "DailyFetchLimit": 10
  }
}
```

### 2. Run the backend

```powershell
cd skillforge_phase1/backend
dotnet build
dotnet run
```

The API listens on `http://localhost:5123` (falls back to `7123`/`5000`), applies EF Core migrations automatically, and exposes Swagger docs in development.

### 3. Run the frontend

```powershell
cd skillforge_phase1/frontend/frontend-app
npm install
npm run dev
```

Open the candidate dashboard (`candidate-dashboard.html`) or recruiter dashboard in your browser and start uploading resumes or posting jobs.

---

## 🔑 Key API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/Auth/register` / `/api/Auth/login` | Candidate/recruiter authentication |
| `POST` | `/api/Candidate/resumes` | Upload a new resume (max 2 per candidate) |
| `GET`  | `/api/Candidate/resumes` | List saved resumes |
| `POST` | `/api/Candidate/resumes/{id}/activate` | Set the active resume |
| `DELETE` | `/api/Candidate/resumes/{id}` | Delete a saved resume |
| `GET`  | `/api/Candidate/job-matches` | Get ranked job matches for the active resume, filterable by `workMode`, `country`, `location1-3`, `roleAspiration` |
| `GET`  | `/api/Job` / `/api/Job/{id}` | Browse live job listings |
| `GET`  | `/api/Job/live-sync-status` | View the latest live-fetch metrics |
| `POST` | `/api/Job/run-daily-sync?force=true` | Manually trigger a live job sync |

---

## 🗺️ Roadmap

- [ ] Full recruiter dashboard (post jobs, review candidates, manage listings)
- [ ] Broader direct-to-company apply-link support beyond Indeed
- [ ] Richer skill-weighting and experience-aware match scoring

---

## 📄 License

This project is currently unlicensed for public distribution. Contact the repository owner for usage terms.
