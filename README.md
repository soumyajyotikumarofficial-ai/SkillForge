# ⚡ SkillForge — Next-Gen AI Talent Matchmaking & Recruitment Engine

[![.NET 10 Web API](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![EF Core](https://img.shields.io/badge/Entity%20Framework-Core-blue)](https://docs.microsoft.com/en-us/ef/core/)
[![LLM Powered](https://img.shields.io/badge/AI-Ollama%20%7C%20Groq%20%7C%20Llama3-orange?logo=openai)](https://ollama.ai/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**SkillForge** is an enterprise-grade AI recruiting platform that bridges the gap between job seekers and hiring managers. Moving beyond keyword matching, SkillForge uses Large Language Models (LLMs) to parse resume context, calculate candidate-to-job skill gaps, route applicants directly to company career portals (Greenhouse, Workday, Lever), and automate AI-assisted team sizing for complex projects.

---

## ✨ Key Architectural Highlights

### 🎯 Candidate Intelligence & Job Ingestion
* **On-the-Fly JSON Resume Parsing:** Extracts skills, experience, and domain knowledge from uploaded PDFs into a structured `ParsedResumeJson` payload, eliminating heavy file storage and enabling instantaneous AI query evaluations.
* **Dual-Profile Resume Management:** Candidates can maintain up to **2 active resume profiles** (with strict file-name duplicate validation) and toggle between them at login to tailor their active job feed.
* **Direct ATS & Career Portal Routing:** Integrates Apify and ATS web scrapers to deliver **direct company apply URLs** (Greenhouse, Lever, Workday) alongside standard aggregator links (Indeed, LinkedIn).
* **Work Mode & Location Filtering:** Native multi-select support for **WFH (Remote), Hybrid, and WFO (On-Site)** work environments.

### 🤖 AI Recruiter Suite & Workforce Planning
* **Workflow A: Company Hiring:**
  * **Automated Company Briefs:** Auto-generates crisp, professional company descriptions (< 200 words) using LLMs.
  * **Candidate Favorability Scoring:** Ranks applicants with natural-language AI reasoning explaining *why* a candidate is shortlisted.
  * **Branded Candidate Outreach:** Automatically triggers HTML emails to candidates upon selection.
* **Workflow B: Project-Based Team Sizing & Capacity Planning:**
  * **AI Project Breakdown:** Analyzes raw project scope and deadlines to recommend exact workforce composition (e.g., *"Recommends 2x Senior .NET Engineers, 1x React Dev"*).
  * **Automated Skill Allocation:** Matches the recommended team structure against active database profiles before triggering recruitment pipelines.

---

## 🛠️ System Architecture & Tech Stack

| Layer | Technology | Description |
| :--- | :--- | :--- |
| **Backend API** | C# / .NET 10 Web API | High-performance asynchronous REST endpoints |
| **ORM & Database** | EF Core + SQLite | JSON column mappings for lightweight resume storage |
| **AI Orchestration** | Ollama (Local) / Groq API | Llama 3 / Phi models for parsing & recommendation reasoning |
| **Data Scraping** | Apify ATS Actors | Fetches live postings with direct company portal resolution |
| **Email Gateway** | MailKit / SMTP | Sends branded shortlist notification emails |
| **Frontend UI** | HTML5, Modern CSS3, JS | Responsive candidate and recruiter dashboards |

---

## 📋 Data Pipeline Overview

```text
[ Candidate Resume ] ──> [ On-the-Fly AI Parser ] ──> [ SQLite: ParsedResumeJson ]
                                                             │
[ Apify Scrapers ]   ──> [ Direct ATS & Job Pipeline ] ──────┼──> [ Skill-Gap Engine ]
                                                             │
[ Recruiter Input ]  ──> [ AI Team Sizing & Matcher ] ───────┘──> [ Branded Email Trigger ]
