"use client";

import { FormEvent, useEffect, useMemo, useState } from "react";

type Evidence = {
  id: string; fact_key: string; source_url?: string; page?: number; sheet?: string;
  section?: string; excerpt?: string; validation_state: string; extraction_method: string;
};

type Opportunity = {
  id: string; title: string; buyer?: string; description?: string; deadline?: string;
  questions_deadline?: string; publication_date?: string; procedure_type?: string;
  score: number; recommendation: string; rationale?: string; status: string;
  eligibility_state: string; decision: string; is_new: boolean; is_updated: boolean;
  notice_url?: string; match_reasons: string[]; hard_blockers: string[];
  missing_information: string[]; component_scores: Record<string, number>;
  mandatory_requirements?: Array<{ text?: string; validation_state?: string } | string>;
  award_criteria?: Array<Record<string, unknown> | string>; evidence?: Evidence[];
  source_references?: Array<{ source_url?: string; source_ref: string; last_seen_at?: string }>;
  decision_history?: Array<{ decision: string; note?: string; created_at?: string }>;
};

type CompanyProfile = {
  id: string; version: number; company_name: string; offerings: string[]; sectors: string[];
  use_cases: string[]; keywords: string[]; cpv_codes: string[]; geographies: string[];
  value_preferences: Record<string, number>; certifications: string[]; exclusions: string[];
  strategic_priorities: string[]; alert_emails: string[];
};

type SourceStatus = {
  id: string; name: string; url: string; enabled: boolean; last_success_at?: string;
  last_error?: string; stale: boolean; schedule_minutes: number;
};

type Alert = { id: string; opportunity_id?: string; type: string; title: string; message: string; created_at?: string };
type Draft = { requirements_checklist: Array<{ requirement: string; state: string; profile_fact?: string }>;
  compliance_matrix: Array<{ tender_requirement: string; company_evidence?: string; status: string }>;
  clarification_questions: string[]; proposal_outline: Array<Record<string, unknown>>; grounding_policy: string };

const api = process.env.NEXT_PUBLIC_API_BASE_URL || "/api";
const emptyProfile = {
  company_name: "", offerings: "", sectors: "", use_cases: "", keywords: "", cpv_codes: "",
  geographies: "Germany", certifications: "", exclusions: "", strategic_priorities: "", alert_emails: "",
  value_min: "", value_max: "",
};

function splitList(value: string) {
  return value.split(",").map((item) => item.trim()).filter(Boolean);
}

function formatDate(value?: string) {
  if (!value) return "Needs review";
  return new Intl.DateTimeFormat("en-GB", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
}

function statusLabel(item: Opportunity) {
  if (["closed", "cancelled", "awarded"].includes(item.status) || item.decision === "skipped") return "CLOSED / SKIP";
  if (item.recommendation === "hot") return "HOT · ACT NOW";
  if (item.recommendation === "review") return "REVIEW";
  return "WATCH";
}

function nextAction(item: Opportunity) {
  if (item.hard_blockers.length) return "Review blocker";
  if (item.missing_information.length) return "Confirm missing facts";
  if (item.decision === "shortlisted") return "Prepare response";
  return "Make bid decision";
}

export default function Dashboard() {
  const [view, setView] = useState<"radar" | "profile" | "alerts" | "sources">("radar");
  const [items, setItems] = useState<Opportunity[]>([]);
  const [selected, setSelected] = useState<Opportunity | null>(null);
  const [profile, setProfile] = useState<CompanyProfile | null>(null);
  const [profileForm, setProfileForm] = useState(emptyProfile);
  const [sources, setSources] = useState<SourceStatus[]>([]);
  const [alerts, setAlerts] = useState<Alert[]>([]);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [ownerEmail, setOwnerEmail] = useState("");
  const [decisionNote, setDecisionNote] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("Loading opportunity intelligence…");
  const [query, setQuery] = useState("");
  const [filter, setFilter] = useState("all");

  async function loadAll() {
    setBusy(true);
    try {
      const [opportunitiesResponse, profileResponse, sourceResponse, alertResponse] = await Promise.all([
        fetch(`${api}/opportunities`, { cache: "no-store" }),
        fetch(`${api}/company-profile`, { cache: "no-store" }),
        fetch(`${api}/sources/status`, { cache: "no-store" }),
        fetch(`${api}/alerts`, { cache: "no-store" }),
      ]);
      if (!opportunitiesResponse.ok) throw new Error(`API ${opportunitiesResponse.status}`);
      const [opportunityData, profileData, sourceData, alertData] = await Promise.all([
        opportunitiesResponse.json(), profileResponse.json(), sourceResponse.json(), alertResponse.json(),
      ]);
      setItems(opportunityData.items || []);
      setProfile(profileData.profile || null);
      if (profileData.profile) {
        const saved = profileData.profile as CompanyProfile;
        setProfileForm({
          company_name: saved.company_name, offerings: saved.offerings.join(", "), sectors: saved.sectors.join(", "),
          use_cases: saved.use_cases.join(", "), keywords: saved.keywords.join(", "), cpv_codes: saved.cpv_codes.join(", "),
          geographies: saved.geographies.join(", "), certifications: saved.certifications.join(", "),
          exclusions: saved.exclusions.join(", "), strategic_priorities: saved.strategic_priorities.join(", "),
          alert_emails: saved.alert_emails.join(", "), value_min: String(saved.value_preferences?.min || ""),
          value_max: String(saved.value_preferences?.max || ""),
        });
      }
      setSources(sourceData.items || []);
      setAlerts(alertData.items || []);
      setMessage(opportunityData.items?.length ? `${opportunityData.items.length} opportunities ranked against your profile` : "Create a profile, then run a scan to discover matches.");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Unable to reach the API");
    } finally {
      setBusy(false);
    }
  }

  async function runScan() {
    setBusy(true);
    setMessage("Scanning and qualifying public tenders…");
    try {
      const response = await fetch(`${api}/scans`, { method: "POST", headers: { "Content-Type": "application/json" }, body: "{}" });
      if (!response.ok) throw new Error(`API ${response.status}`);
      const job = await response.json();
      if (job.job_id) {
        for (let attempt = 0; attempt < 30; attempt += 1) {
          await new Promise((resolve) => window.setTimeout(resolve, 2000));
          const statusResponse = await fetch(`${api}/jobs/${job.job_id}`, { cache: "no-store" });
          const status = await statusResponse.json();
          if (status.status === "complete") break;
          if (status.status === "failed") throw new Error(status.error || "Scan failed");
        }
      }
      await loadAll();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Scan failed");
      setBusy(false);
    }
  }

  async function openDetail(id: string) {
    setDraft(null);
    const response = await fetch(`${api}/opportunities/${id}`, { cache: "no-store" });
    if (!response.ok) return setMessage(`Unable to load opportunity (${response.status})`);
    setSelected(await response.json());
  }

  async function decide(decision: "shortlisted" | "skipped" | "review") {
    if (!selected) return;
    const response = await fetch(`${api}/opportunities/${selected.id}/decision`, {
      method: "PATCH", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ decision, owner_email: ownerEmail || null, note: decisionNote || null, mark_reviewed: decision === "review" }),
    });
    if (!response.ok) return setMessage(`Decision failed (${response.status})`);
    const updated = await response.json();
    setSelected(updated);
    setItems((current) => current.map((item) => item.id === updated.id ? { ...item, ...updated } : item));
  }

  async function generateDraft() {
    if (!selected) return;
    setBusy(true);
    const response = await fetch(`${api}/opportunities/${selected.id}/draft`, { method: "POST" });
    const result = await response.json();
    if (response.ok) setDraft(result.content);
    else setMessage(result.detail || "Could not generate response starter");
    setBusy(false);
  }

  async function saveProfile(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    const payload = {
      company_name: profileForm.company_name, offerings: splitList(profileForm.offerings), sectors: splitList(profileForm.sectors),
      use_cases: splitList(profileForm.use_cases), keywords: splitList(profileForm.keywords), cpv_codes: splitList(profileForm.cpv_codes),
      geographies: splitList(profileForm.geographies), certifications: splitList(profileForm.certifications),
      exclusions: splitList(profileForm.exclusions), strategic_priorities: splitList(profileForm.strategic_priorities),
      alert_emails: splitList(profileForm.alert_emails),
      value_preferences: {
        ...(profileForm.value_min ? { min: Number(profileForm.value_min) } : {}),
        ...(profileForm.value_max ? { max: Number(profileForm.value_max) } : {}),
      },
    };
    const response = await fetch(`${api}/company-profile`, {
      method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload),
    });
    const result = await response.json();
    if (response.ok) {
      setProfile(result.profile);
      setMessage("Company profile saved. Run a scan to refresh qualification scores.");
      setView("radar");
    } else setMessage(result.detail || "Profile could not be saved");
    setBusy(false);
  }

  const filteredItems = useMemo(() => {
    const term = query.trim().toLowerCase();
    return items.filter((item) => {
      const matchesQuery = !term || `${item.title} ${item.buyer || ""} ${item.match_reasons.join(" ")}`.toLowerCase().includes(term);
      const matchesFilter = filter === "all" || (filter === "new" && item.is_new) || (filter === "updated" && item.is_updated)
        || (filter === "shortlisted" && item.decision === "shortlisted") || item.recommendation === filter;
      return matchesQuery && matchesFilter;
    });
  }, [items, query, filter]);

  useEffect(() => { loadAll(); }, []);

  return (
    <main className="app-shell">
      <aside className="sidebar">
        <div className="brand"><span className="brand-mark">N</span><span>noavia</span></div>
        <div className="workspace"><span className="workspace-dot" /> {profile?.company_name || "Tender workspace"}</div>
        <nav className="nav" aria-label="Workspace navigation">
          <button className={`nav-link ${view === "radar" ? "active" : ""}`} onClick={() => setView("radar")}><span className="nav-icon">⌕</span> Opportunity radar <span className="nav-count">{items.length}</span></button>
          <button className={`nav-link ${view === "profile" ? "active" : ""}`} onClick={() => setView("profile")}><span className="nav-icon">◎</span> Company profile</button>
          <button className={`nav-link ${view === "alerts" ? "active" : ""}`} onClick={() => setView("alerts")}><span className="nav-icon">◉</span> Alerts <span className="nav-count">{alerts.length}</span></button>
          <button className={`nav-link ${view === "sources" ? "active" : ""}`} onClick={() => setView("sources")}><span className="nav-icon">◫</span> Source health</button>
        </nav>
        <div className="sidebar-bottom"><div className="trust-card"><strong>Evidence-first</strong><span>Unknown facts stay visible as review items.</span></div></div>
      </aside>

      <section className="content">
        <header className="topbar">
          <div><p className="eyebrow">NOAVIA PROCUREMENT INTELLIGENCE</p><strong>{view === "radar" ? "Opportunity radar" : view === "profile" ? "Company profile" : view === "alerts" ? "Alerts" : "Source health"}</strong></div>
          <div className="top-actions"><span className={`health-dot ${sources.some((source) => source.stale) ? "warning" : ""}`} />
            <span className="last-sync">{sources.some((source) => source.stale) ? "Source needs attention" : "Sources current"}</span>
            <button className="secondary-button" onClick={loadAll} disabled={busy}>Refresh</button>
            <button className="primary-button" onClick={runScan} disabled={busy || !profile}>{busy ? "Working…" : "Run scan"}</button>
          </div>
        </header>

        <div className="content-inner">
          {view === "radar" && <>
            {!profile && <section className="onboarding-callout"><div><span className="badge">FIRST STEP</span><h1>Tell Noavia what your company can actually deliver.</h1><p>A confirmed profile keeps fit scores explainable and prevents invented capability claims.</p></div><button className="primary-button" onClick={() => setView("profile")}>Create company profile</button></section>}
            <section className="page-heading"><div><h1>What should we act on next?</h1><p>{message}</p></div><div className="summary-strip"><span><strong>{items.filter((item) => item.recommendation === "hot").length}</strong> Hot</span><span><strong>{items.filter((item) => item.recommendation === "review").length}</strong> Review</span><span><strong>{items.filter((item) => item.decision === "shortlisted").length}</strong> Shortlisted</span></div></section>
            <section className="toolbar"><label className="search-box"><span>⌕</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search titles, buyers, and match reasons" /></label><div className="filters">
              {["all", "hot", "review", "watch", "new", "updated", "shortlisted"].map((value) => <button key={value} className={filter === value ? "active" : ""} onClick={() => setFilter(value)}>{value}</button>)}
            </div></section>
            <section className="opportunity-panel">
              <div className="table-head"><span>OPPORTUNITY</span><span>NEXT DEADLINE</span><span>QUALIFICATION</span><span>NEXT ACTION</span></div>
              <div className="opportunity-list">
                {filteredItems.map((item) => <article className="opportunity-row" key={item.id} onClick={() => openDetail(item.id)}>
                  <div className="opportunity-main"><div className={`status-rail ${item.recommendation}`} /><div><div className="title-line"><h2>{item.title}</h2>{item.is_new && <span className="badge new">NEW</span>}{item.is_updated && <span className="badge updated">UPDATED</span>}</div><p>{item.buyer || "Buyer not stated"}</p><div className="reason-line">{item.match_reasons[0] || "Awaiting confirmed profile evidence"}</div></div></div>
                  <div className="deadline"><strong>{formatDate(item.deadline)}</strong><span>{item.questions_deadline ? `Questions: ${formatDate(item.questions_deadline)}` : "Question deadline unknown"}</span></div>
                  <div><span className={`qualification ${item.recommendation}`}>{statusLabel(item)}</span><div className="score-wrap"><strong>{Math.round(item.score)}</strong><span>/100 · {item.eligibility_state}</span></div></div>
                  <div className="next-action"><strong>{nextAction(item)}</strong><span>{item.hard_blockers.length ? `${item.hard_blockers.length} blocker(s)` : item.missing_information.length ? `${item.missing_information.length} unknown(s)` : "Evidence ready"}</span></div>
                </article>)}
                {!filteredItems.length && <div className="empty-state"><span className="empty-icon">⌕</span><strong>No matching opportunities</strong><p>{profile ? "Run a scan or change the current filter." : "Create a company profile to begin."}</p></div>}
              </div>
            </section>
          </>}

          {view === "profile" && <section className="settings-panel"><div className="section-heading"><span className="badge">CONFIRMED FACTS</span><h1>{profile ? "Edit company profile" : "Create company profile"}</h1><p>Only facts saved here or confirmed in profile evidence may be used in qualification and response starters.</p></div><form className="profile-form" onSubmit={saveProfile}>
            <label className="full">Company name<input required value={profileForm.company_name} onChange={(event) => setProfileForm({ ...profileForm, company_name: event.target.value })} /></label>
            {([[
              "offerings", "Offerings", "AI automation, document processing, data platforms"], ["sectors", "Target sectors", "Public utilities, healthcare"],
              ["use_cases", "Use cases", "Process automation, knowledge management"], ["keywords", "Keywords", "software, cloud, digitalisation"],
              ["cpv_codes", "CPV hints", "72000000, 72200000"], ["geographies", "Service geographies", "Germany, DACH"],
              ["certifications", "Confirmed certifications", "ISO 27001"], ["exclusions", "Hard exclusions", "construction, hardware-only"],
              ["strategic_priorities", "Strategic priorities", "municipal utilities"], ["alert_emails", "Alert email recipients", "tenders@example.com"],
            ] as const).map(([key, label, placeholder]) => <label key={key}>{label}<input value={profileForm[key]} placeholder={placeholder} onChange={(event) => setProfileForm({ ...profileForm, [key]: event.target.value })} /><small>Comma-separated confirmed values</small></label>)}
            <label>Preferred contract value – minimum (€)<input type="number" min="0" value={profileForm.value_min} onChange={(event) => setProfileForm({ ...profileForm, value_min: event.target.value })} /></label>
            <label>Preferred contract value – maximum (€)<input type="number" min="0" value={profileForm.value_max} onChange={(event) => setProfileForm({ ...profileForm, value_max: event.target.value })} /></label>
            <div className="form-actions full"><button type="button" className="secondary-button" onClick={() => setView("radar")}>Cancel</button><button className="primary-button" disabled={busy}>{profile ? "Save new profile version" : "Create profile"}</button></div>
          </form></section>}

          {view === "alerts" && <section><div className="section-heading"><span className="badge">ACTION FEED</span><h1>Alerts</h1><p>Idempotent notifications for high-fit matches, material changes, and shortlisted deadlines.</p></div><div className="card-list">{alerts.map((alert) => <article className="info-card" key={alert.id}><span className="qualification review">{alert.type.replaceAll("_", " ")}</span><h2>{alert.title}</h2><p>{alert.message}</p><small>{formatDate(alert.created_at)}</small>{alert.opportunity_id && <button className="text-button" onClick={() => openDetail(alert.opportunity_id!)}>Open opportunity →</button>}</article>)}{!alerts.length && <div className="empty-state">No alerts yet.</div>}</div></section>}

          {view === "sources" && <section><div className="section-heading"><span className="badge">MONITORING</span><h1>Source health</h1><p>Failed scans preserve the last known opportunity state and are retried with backoff.</p></div><div className="card-list">{sources.map((source) => <article className="info-card" key={source.id}><div className="source-title"><span className={`health-dot ${source.stale ? "warning" : ""}`} /><h2>{source.name}</h2><span className={`badge ${source.stale ? "updated" : "new"}`}>{source.stale ? "STALE" : "CURRENT"}</span></div><p>{source.url}</p><dl><div><dt>Schedule</dt><dd>Every {Math.round(source.schedule_minutes / 60)} hour(s)</dd></div><div><dt>Last success</dt><dd>{formatDate(source.last_success_at)}</dd></div></dl>{source.last_error && <div className="warning-box">{source.last_error}</div>}</article>)}{!sources.length && <div className="empty-state">A source is initialized with the first scan.</div>}</div></section>}
        </div>
      </section>

      {selected && <div className="modal-backdrop" onClick={() => setSelected(null)}><div className="detail-modal" onClick={(event) => event.stopPropagation()}>
        <div className="modal-top"><div><span className={`qualification ${selected.recommendation}`}>{statusLabel(selected)}</span><h1>{selected.title}</h1><p>{selected.buyer || "Buyer not stated"}</p></div><button className="close-button" onClick={() => setSelected(null)} aria-label="Close">×</button></div>
        <div className="detail-grid"><section><h3>Why it fits</h3>{selected.match_reasons.map((reason) => <div className="fact good" key={reason}>✓ {reason}</div>)}<h3>Eligibility and blockers</h3>{selected.hard_blockers.map((blocker) => <div className="fact bad" key={blocker}>! {blocker}</div>)}{selected.missing_information.map((missing) => <div className="fact unknown" key={missing}>? {missing}</div>)}{!selected.hard_blockers.length && !selected.missing_information.length && <div className="fact good">No known blocker; human verification still required.</div>}</section>
          <aside className="decision-card"><div className="large-score">{Math.round(selected.score)}<span>/100</span></div><p>{selected.rationale}</p><dl><div><dt>Submission</dt><dd>{formatDate(selected.deadline)}</dd></div><div><dt>Questions</dt><dd>{formatDate(selected.questions_deadline)}</dd></div><div><dt>Decision</dt><dd>{selected.decision}</dd></div></dl><label className="decision-field">Owner email<input type="email" value={ownerEmail} onChange={(event) => setOwnerEmail(event.target.value)} placeholder="proposal.owner@example.com" /></label><label className="decision-field">Decision note<input value={decisionNote} onChange={(event) => setDecisionNote(event.target.value)} placeholder="Optional reason or next step" /></label><div className="decision-actions"><button className="primary-button" onClick={() => decide("shortlisted")}>Shortlist</button><button className="secondary-button" onClick={() => decide("review")}>Mark reviewed</button><button className="secondary-button danger" onClick={() => decide("skipped")}>Skip</button></div></aside></div>
        <section className="detail-section"><h3>Mandatory requirements</h3>{selected.mandatory_requirements?.length ? selected.mandatory_requirements.map((requirement, index) => <div className="requirement" key={index}><span>REVIEW</span><p>{typeof requirement === "string" ? requirement : requirement.text}</p></div>) : <div className="warning-box">No mandatory requirement has been verified. Review source documents before deciding.</div>}</section>
        <section className="detail-section"><h3>Source evidence</h3>{selected.evidence?.length ? selected.evidence.map((evidence) => <article className="evidence-card" key={evidence.id}><div><strong>{evidence.fact_key.replaceAll("_", " ")}</strong><span>{evidence.validation_state} · {evidence.extraction_method}{evidence.page ? ` · page ${evidence.page}` : ""}</span></div><p>{evidence.excerpt || "Excerpt unavailable"}</p>{evidence.source_url && <a href={evidence.source_url} target="_blank" rel="noreferrer">Open source ↗</a>}</article>) : <div className="warning-box">Critical evidence is not yet available. Treat this item as REVIEW.</div>}</section>
        <section className="draft-section"><div><h3>Response starter</h3><p>Checklist, compliance matrix, clarification questions, and outline grounded in source and confirmed profile facts.</p></div><button className="primary-button" onClick={generateDraft} disabled={busy || !profile}>Generate starter</button></section>
        {draft && <section className="draft-output"><div className="grounding-note">{draft.grounding_policy}</div><h3>Compliance matrix</h3>{draft.compliance_matrix.map((row, index) => <div className="matrix-row" key={index}><span className={row.status === "CONFIRMED" ? "confirmed" : "todo"}>{row.status}</span><p>{row.tender_requirement}</p><strong>{row.company_evidence || "TODO: confirm company evidence"}</strong></div>)}<h3>Clarification / internal questions</h3><ul>{draft.clarification_questions.map((question) => <li key={question}>{question}</li>)}</ul></section>}
        <div className="modal-footer">{selected.notice_url && <a className="text-button" href={selected.notice_url} target="_blank" rel="noreferrer">Open original notice ↗</a>}<span>Unknown facts remain REVIEW/TODO by design.</span></div>
      </div></div>}
    </main>
  );
}
