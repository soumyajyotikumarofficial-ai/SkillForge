from __future__ import annotations

import re
from datetime import datetime, timezone
from uuid import UUID

from fastapi import Depends, FastAPI, File, Form, Header, HTTPException, UploadFile
from fastapi.responses import PlainTextResponse
from prometheus_fastapi_instrumentator import Instrumentator
from pydantic import BaseModel, Field
from sqlalchemy import case, select, text
from sqlalchemy.orm import Session

from .config import get_settings
from .db import get_db
from .models import (
    AlertPreference, AnalysisRun, AuditEvent, CompanyProfile, DecisionState,
    Job, MatchAlert, Opportunity, OpportunityDecision, OpportunityDraft,
    OpportunityEvent, Organization, ProfileEvidence, Source, Tender, User,
)
from .pipeline import run_scan
from .processing import clamav_scan, parse_document_with_metadata, validate_document_type
from .storage import ContentAddressedStorage

app = FastAPI(title="Noavia Tender Opportunity Hunter", version="0.2.0")
Instrumentator().instrument(app).expose(app)


@app.get("/health/live")
@app.get("/api/health/live")
def live() -> dict[str, str]:
    return {"status": "ok"}


@app.get("/health/ready")
@app.get("/api/health/ready")
def ready(db: Session = Depends(get_db)) -> dict[str, str]:
    db.execute(text("SELECT 1"))
    return {"status": "ready"}


@app.get("/api/status")
def status() -> dict[str, str]:
    return {"service": "noavia-api", "environment": get_settings().app_env, "version": app.version}


@app.get("/health", response_class=PlainTextResponse, include_in_schema=False)
def health_legacy() -> str:
    return "ok"


class ScanRequest(BaseModel):
    run_now: bool = False


class CompanyProfileRequest(BaseModel):
    company_name: str = Field(min_length=1, max_length=300)
    offerings: list[str] = Field(default_factory=list)
    sectors: list[str] = Field(default_factory=list)
    use_cases: list[str] = Field(default_factory=list)
    keywords: list[str] = Field(default_factory=list)
    cpv_codes: list[str] = Field(default_factory=list)
    geographies: list[str] = Field(default_factory=list)
    value_preferences: dict = Field(default_factory=dict)
    certifications: list[str] = Field(default_factory=list)
    exclusions: list[str] = Field(default_factory=list)
    strategic_priorities: list[str] = Field(default_factory=list)
    alert_emails: list[str] = Field(default_factory=list)


class DecisionRequest(BaseModel):
    decision: DecisionState
    owner_id: UUID | None = None
    owner_email: str | None = Field(default=None, max_length=320)
    note: str | None = Field(default=None, max_length=4000)
    mark_reviewed: bool = False


class AlertPreferenceRequest(BaseModel):
    match_threshold: int = Field(default=80, ge=0, le=100)
    deadline_days: list[int] = Field(default_factory=lambda: [30, 14, 7, 3, 1])
    email_enabled: bool = True
    material_updates_enabled: bool = True


def organization(db: Session, organization_id: str | None) -> Organization:
    try:
        oid = UUID(organization_id) if organization_id else UUID("00000000-0000-0000-0000-000000000001")
    except ValueError as exc:
        raise HTTPException(400, "X-Organization-Id must be a UUID") from exc
    result = db.get(Organization, oid)
    if result is None:
        result = Organization(id=oid, name="Local demo organisation")
        db.add(result)
        db.commit()
    return result


def _audit(db: Session, org_id: UUID, action: str, entity_type: str, entity_id: UUID | None, metadata: dict | None = None) -> None:
    db.add(AuditEvent(organization_id=org_id, action=action, entity_type=entity_type, entity_id=entity_id, metadata_json=metadata or {}))


def _profile_json(profile: CompanyProfile) -> dict:
    return {
        "id": str(profile.id), "organization_id": str(profile.organization_id), "version": profile.version,
        "company_name": profile.company_name, "offerings": profile.offerings, "sectors": profile.sectors,
        "use_cases": profile.use_cases, "keywords": profile.keywords, "cpv_codes": profile.cpv_codes,
        "geographies": profile.geographies, "value_preferences": profile.value_preferences,
        "certifications": profile.certifications, "exclusions": profile.exclusions,
        "strategic_priorities": profile.strategic_priorities, "alert_emails": profile.alert_emails,
        "updated_at": profile.updated_at.isoformat() if profile.updated_at else None,
        "evidence": [{
            "id": str(item.id), "kind": item.kind, "filename": item.filename, "sha256": item.sha256,
            "parse_status": item.parse_status, "created_at": item.created_at.isoformat() if item.created_at else None,
        } for item in profile.evidence],
    }


def _evidence_json(row) -> dict:
    return {
        "id": str(row.id), "fact_key": row.fact_key, "source_url": row.source_url,
        "source_snapshot_id": str(row.source_snapshot_id) if row.source_snapshot_id else None,
        "document_id": str(row.document_id) if row.document_id else None,
        "page": row.page, "sheet": row.sheet, "section": row.section, "excerpt": row.excerpt,
        "extraction_method": row.extraction_method, "validation_state": row.validation_state,
        "validation_score": row.validation_score, "content_hash": row.content_hash,
    }


def _opportunity_json(opportunity: Opportunity, include_detail: bool = False) -> dict:
    tender = opportunity.tender
    result = {
        "id": str(opportunity.id), "tender_id": str(tender.id), "title": tender.title, "buyer": tender.buyer,
        "description": tender.description, "publication_date": tender.publication_date.isoformat() if tender.publication_date else None,
        "deadline": tender.deadline.isoformat() if tender.deadline else None,
        "deadline_original": tender.deadline_original,
        "questions_deadline": tender.questions_deadline.isoformat() if tender.questions_deadline else None,
        "procedure_type": tender.procedure_type, "cpv_codes": tender.cpv_codes or [], "lots": tender.lots or [],
        "status": tender.status, "place_of_performance": tender.place_of_performance or [],
        "estimated_value": float(tender.estimated_value) if tender.estimated_value is not None else None,
        "currency": tender.currency, "notice_url": tender.notice_url, "document_urls": tender.document_urls or [],
        "score": opportunity.score, "component_scores": opportunity.component_scores or {},
        "scoring_version": opportunity.scoring_version, "recommendation": opportunity.recommendation,
        "rationale": opportunity.rationale, "eligibility_state": opportunity.eligibility_state.value,
        "hard_blockers": opportunity.hard_blockers or [], "missing_information": opportunity.missing_information or [],
        "match_reasons": opportunity.match_reasons or [], "decision": opportunity.decision.value,
        "owner_id": str(opportunity.owner_id) if opportunity.owner_id else None, "note": opportunity.note,
        "is_new": opportunity.is_new, "is_updated": opportunity.is_updated,
        "processed_at": opportunity.processed_at.isoformat() if opportunity.processed_at else None,
    }
    if include_detail:
        result.update({
            "mandatory_requirements": tender.mandatory_requirements or [], "award_criteria": tender.award_criteria or [],
            "contract_period": tender.contract_period or {}, "contact": tender.contact or {}, "language": tender.language,
            "extracted": opportunity.extracted or {},
            "evidence": [_evidence_json(item) for item in opportunity.evidence],
            "source_references": [{
                "source_id": str(item.source_id), "source_ref": item.source_ref, "source_url": item.source_url,
                "snapshot_id": str(item.latest_snapshot_id) if item.latest_snapshot_id else None,
                "first_seen_at": item.first_seen_at.isoformat() if item.first_seen_at else None,
                "last_seen_at": item.last_seen_at.isoformat() if item.last_seen_at else None,
            } for item in tender.source_references],
            "decision_history": [{
                "decision": item.decision.value, "owner_id": str(item.owner_id) if item.owner_id else None,
                "note": item.note, "created_at": item.created_at.isoformat() if item.created_at else None,
            } for item in opportunity.decisions],
        })
    return result


def _get_opportunity(db: Session, opportunity_id: UUID, org_id: UUID) -> Opportunity:
    row = db.scalar(select(Opportunity).where(Opportunity.id == opportunity_id, Opportunity.organization_id == org_id))
    if row is None:
        raise HTTPException(404, "Opportunity not found")
    return row


@app.get("/api/company-profile")
def get_company_profile(db: Session = Depends(get_db), x_organization_id: str | None = Header(default=None)) -> dict:
    org = organization(db, x_organization_id)
    profile = db.scalar(select(CompanyProfile).where(CompanyProfile.organization_id == org.id))
    return {"profile": _profile_json(profile) if profile else None}


@app.put("/api/company-profile")
def put_company_profile(request: CompanyProfileRequest, db: Session = Depends(get_db), x_organization_id: str | None = Header(default=None)) -> dict:
    org = organization(db, x_organization_id)
    invalid_emails = [email for email in request.alert_emails if not re.fullmatch(r"[^@\s]+@[^@\s]+\.[^@\s]+", email)]
    if invalid_emails:
        raise HTTPException(422, f"Invalid alert email: {invalid_emails[0]}")
    profile = db.scalar(select(CompanyProfile).where(CompanyProfile.organization_id == org.id))
    if profile is None:
        profile = CompanyProfile(organization_id=org.id, company_name=request.company_name)
        db.add(profile)
        action = "profile.created"
    else:
        profile.version += 1
        action = "profile.updated"
    for field, value in request.model_dump().items():
        setattr(profile, field, value)
    db.flush()
    _audit(db, org.id, action, "company_profile", profile.id, {"version": profile.version})
    db.commit()
    return {"profile": _profile_json(profile)}


@app.post("/api/company-profile/documents")
async def upload_profile_document(
    document: UploadFile = File(...), kind: str = Form(default="capability"),
    db: Session = Depends(get_db), x_organization_id: str | None = Header(default=None),
) -> dict:
    org = organization(db, x_organization_id)
    profile = db.scalar(select(CompanyProfile).where(CompanyProfile.organization_id == org.id))
    if profile is None:
        raise HTTPException(409, "Create the company profile before uploading evidence")
    content = await document.read(15 * 1024 * 1024 + 1)
    if len(content) > 15 * 1024 * 1024:
        raise HTTPException(413, "Document exceeds 15 MiB")
    try:
        validate_document_type(document.filename or "document", document.content_type)
        if not clamav_scan(content):
            raise HTTPException(422, "Malware detected")
    except HTTPException:
        raise
    except Exception as exc:
        raise HTTPException(503, str(exc)) from exc
    stored = ContentAddressedStorage(get_settings().storage_root).put(content)
    parsed = parse_document_with_metadata(content, document.filename or "document", document.content_type)
    evidence = ProfileEvidence(
        organization_id=org.id, profile_id=profile.id, kind=kind[:50], filename=(document.filename or "document")[:500],
        storage_key=stored.key, sha256=stored.sha256,
        parse_status="ocr_required" if parsed.ocr_required else "review_required",
        extracted_facts={"summary": " ".join(parsed.text.split())[:1000], "confirmed": False},
    )
    db.add(evidence)
    db.flush()
    _audit(db, org.id, "profile.evidence_uploaded", "profile_evidence", evidence.id, {"sha256": stored.sha256})
    db.commit()
    return {"id": str(evidence.id), "sha256": evidence.sha256, "parse_status": evidence.parse_status}


@app.post("/api/scans")
def trigger_scan(request: ScanRequest | None = None, db: Session = Depends(get_db), x_organization_id: str | None = Header(default=None)) -> dict:
    org = organization(db, x_organization_id)
    request = request or ScanRequest()
    if request.run_now:
        return run_scan(db, org.id)
    job = Job(
        organization_id=org.id, kind="scan", payload={"organization_id": str(org.id)},
        idempotency_key=f"scan:{org.id}:{datetime.now(timezone.utc).strftime('%Y%m%d%H%M')}",
    )
    existing = db.scalar(select(Job).where(Job.idempotency_key == job.idempotency_key))
    if existing:
        return {"job_id": str(existing.id), "status": existing.status.value}
    db.add(job)
    db.commit()
    return {"job_id": str(job.id), "status": job.status.value}


@app.get("/api/opportunities")
def list_opportunities(
    db: Session = Depends(get_db), x_organization_id: str | None = Header(default=None),
    limit: int = 50, state: str | None = None,
) -> dict:
    org = organization(db, x_organization_id)
    query = select(Opportunity).join(Tender).where(Opportunity.organization_id == org.id)
    if state:
        if state in {item.value for item in DecisionState}:
            query = query.where(Opportunity.decision == DecisionState(state))
        elif state == "new":
            query = query.where(Opportunity.is_new.is_(True))
        elif state == "updated":
            query = query.where(Opportunity.is_updated.is_(True))
    actionability = case((Tender.status.in_(["closed", "cancelled", "awarded"]), 1), else_=0)
    rows = db.scalars(query.order_by(actionability, Tender.deadline.asc().nullslast(), Opportunity.score.desc()).limit(min(max(limit, 1), 100))).all()
    return {"items": [_opportunity_json(row) for row in rows], "count": len(rows)}


@app.get("/api/opportunities/{opportunity_id}")
def get_opportunity(opportunity_id: UUID, db: Session = Depends(get_db), x_organization_id: str | None = Header(default=None)) -> dict:
    org = organization(db, x_organization_id)
    return _opportunity_json(_get_opportunity(db, opportunity_id, org.id), include_detail=True)


@app.patch("/api/opportunities/{opportunity_id}/decision")
def update_decision(
    opportunity_id: UUID, request: DecisionRequest, db: Session = Depends(get_db),
    x_organization_id: str | None = Header(default=None),
) -> dict:
    org = organization(db, x_organization_id)
    opportunity = _get_opportunity(db, opportunity_id, org.id)
    owner_id = request.owner_id
    if request.owner_email:
        if not re.fullmatch(r"[^@\s]+@[^@\s]+\.[^@\s]+", request.owner_email):
            raise HTTPException(422, "Owner email is invalid")
        owner = db.scalar(select(User).where(User.email == request.owner_email))
        if owner and owner.organization_id != org.id:
            raise HTTPException(422, "Owner email belongs to another organization")
        if owner is None:
            owner = User(
                organization_id=org.id, email=request.owner_email,
                display_name=request.owner_email.split("@", 1)[0].replace(".", " ").title(),
            )
            db.add(owner)
            db.flush()
        owner_id = owner.id
    if owner_id and not db.scalar(select(User).where(User.id == owner_id, User.organization_id == org.id)):
        raise HTTPException(422, "Owner must belong to this organization")
    opportunity.decision, opportunity.owner_id, opportunity.note = request.decision, owner_id, request.note
    opportunity.is_new = False
    opportunity.is_updated = False
    if request.mark_reviewed or request.decision == DecisionState.review:
        opportunity.reviewed_at = datetime.now(timezone.utc)
    history = OpportunityDecision(
        organization_id=org.id, opportunity_id=opportunity.id, decision=request.decision,
        owner_id=owner_id, note=request.note,
    )
    db.add(history)
    _audit(db, org.id, "opportunity.decision_changed", "opportunity", opportunity.id, request.model_dump(mode="json"))
    db.commit()
    return _opportunity_json(opportunity, include_detail=True)


def _grounded_draft(opportunity: Opportunity, profile: CompanyProfile) -> dict:
    requirements = opportunity.tender.mandatory_requirements or []
    confirmed_certifications = [str(item) for item in profile.certifications]
    confirmed_text = " ".join(confirmed_certifications).casefold()
    checklist = []
    matrix = []
    questions = []
    for requirement in requirements:
        text_value = str(requirement.get("text") if isinstance(requirement, dict) else requirement)
        matched = [cert for cert in confirmed_certifications if cert.casefold() in text_value.casefold()]
        state = "confirmed" if matched else "todo"
        profile_evidence = matched[0] if matched else None
        checklist.append({"requirement": text_value, "state": state, "profile_fact": profile_evidence})
        matrix.append({"tender_requirement": text_value, "company_evidence": profile_evidence, "status": state.upper()})
        if not matched:
            questions.append(f"TODO: Confirm whether {profile.company_name} can satisfy: {text_value}")
    if not requirements:
        questions.append("TODO: Review source documents and confirm all mandatory requirements; none were verified automatically.")
    return {
        "grounding_policy": "Tender claims use stored tender evidence; company claims use confirmed profile fields only. Unknowns are TODOs.",
        "requirements_checklist": checklist,
        "compliance_matrix": matrix,
        "clarification_questions": questions,
        "proposal_outline": [
            {"section": "Understanding of the requirement", "source": opportunity.tender.title},
            {"section": "Proposed approach", "todo": "Draft using confirmed offerings only", "confirmed_offerings": profile.offerings},
            {"section": "Relevant experience", "todo": "Select a confirmed reference project from profile evidence"},
            {"section": "Compliance and certifications", "confirmed_certifications": profile.certifications},
            {"section": "Delivery plan and commercial response", "todo": "Complete after human review of deadlines, lots, and award criteria"},
        ],
        "source_links": [reference.source_url for reference in opportunity.tender.source_references if reference.source_url],
    }


@app.post("/api/opportunities/{opportunity_id}/draft")
def generate_draft(opportunity_id: UUID, db: Session = Depends(get_db), x_organization_id: str | None = Header(default=None)) -> dict:
    org = organization(db, x_organization_id)
    opportunity = _get_opportunity(db, opportunity_id, org.id)
    profile = db.scalar(select(CompanyProfile).where(CompanyProfile.organization_id == org.id))
    if profile is None:
        raise HTTPException(409, "Create and confirm a company profile before generating a response starter")
    latest_analysis = db.scalar(select(AnalysisRun).where(AnalysisRun.opportunity_id == opportunity.id).order_by(AnalysisRun.completed_at.desc()))
    draft = OpportunityDraft(
        organization_id=org.id, opportunity_id=opportunity.id, profile_version=profile.version,
        analysis_run_id=latest_analysis.id if latest_analysis else None, content=_grounded_draft(opportunity, profile),
    )
    db.add(draft)
    db.flush()
    _audit(db, org.id, "draft.generated", "opportunity_draft", draft.id, {"profile_version": profile.version})
    db.commit()
    return {"id": str(draft.id), "content": draft.content, "created_at": draft.created_at.isoformat() if draft.created_at else None}


@app.get("/api/opportunity-events")
def list_events(db: Session = Depends(get_db), x_organization_id: str | None = Header(default=None), limit: int = 100) -> dict:
    org = organization(db, x_organization_id)
    rows = db.scalars(select(OpportunityEvent).where(OpportunityEvent.organization_id == org.id).order_by(OpportunityEvent.created_at.desc()).limit(min(limit, 200))).all()
    return {"items": [{
        "id": str(row.id), "opportunity_id": str(row.opportunity_id), "event_type": row.event_type,
        "old_value": row.old_value, "new_value": row.new_value, "materiality": row.materiality,
        "source_snapshot_id": str(row.source_snapshot_id) if row.source_snapshot_id else None,
        "created_at": row.created_at.isoformat() if row.created_at else None,
    } for row in rows]}


@app.get("/api/alerts")
def list_alerts(db: Session = Depends(get_db), x_organization_id: str | None = Header(default=None), limit: int = 50) -> dict:
    org = organization(db, x_organization_id)
    rows = db.scalars(select(MatchAlert).where(MatchAlert.organization_id == org.id).order_by(MatchAlert.created_at.desc()).limit(min(limit, 100))).all()
    return {"items": [{
        "id": str(row.id), "opportunity_id": str(row.opportunity_id) if row.opportunity_id else None,
        "type": row.alert_type, "title": row.title, "message": row.message, "payload": row.payload,
        "email_status": row.email_status, "read_at": row.read_at.isoformat() if row.read_at else None,
        "created_at": row.created_at.isoformat() if row.created_at else None,
    } for row in rows]}


@app.patch("/api/alert-preferences")
def update_alert_preferences(
    request: AlertPreferenceRequest, db: Session = Depends(get_db), x_organization_id: str | None = Header(default=None),
) -> dict:
    org = organization(db, x_organization_id)
    preference = db.scalar(select(AlertPreference).where(AlertPreference.organization_id == org.id))
    if preference is None:
        preference = AlertPreference(organization_id=org.id)
        db.add(preference)
    for key, value in request.model_dump().items():
        setattr(preference, key, value)
    _audit(db, org.id, "alert_preferences.updated", "alert_preference", preference.id)
    db.commit()
    return request.model_dump()


@app.get("/api/sources/status")
def source_status(db: Session = Depends(get_db), x_organization_id: str | None = Header(default=None)) -> dict:
    org = organization(db, x_organization_id)
    now = datetime.now(timezone.utc)
    settings = get_settings()
    sources = db.scalars(select(Source).where(Source.organization_id == org.id)).all()
    return {"items": [{
        "id": str(source.id), "name": source.name, "url": source.url, "adapter_type": source.adapter_type,
        "enabled": source.enabled, "schedule_minutes": source.schedule_minutes,
        "last_attempt_at": source.last_attempt_at.isoformat() if source.last_attempt_at else None,
        "last_success_at": source.last_success_at.isoformat() if source.last_success_at else None,
        "last_error": source.last_error,
        "stale": source.last_success_at is None or (now - source.last_success_at).total_seconds() > settings.source_stale_hours * 3600,
    } for source in sources]}


@app.get("/api/jobs/{job_id}")
def get_job(job_id: UUID, db: Session = Depends(get_db), x_organization_id: str | None = Header(default=None)) -> dict:
    org = organization(db, x_organization_id)
    job = db.scalar(select(Job).where(Job.id == job_id, Job.organization_id == org.id))
    if job is None:
        raise HTTPException(404, "Job not found")
    return {"id": str(job.id), "status": job.status.value, "result": job.result, "error": job.last_error}


@app.get("/api/audit-events")
def audit_events(db: Session = Depends(get_db), x_organization_id: str | None = Header(default=None), limit: int = 100) -> dict:
    org = organization(db, x_organization_id)
    rows = db.scalars(select(AuditEvent).where(AuditEvent.organization_id == org.id).order_by(AuditEvent.created_at.desc()).limit(min(limit, 200))).all()
    return {"items": [{
        "id": str(row.id), "action": row.action, "entity_type": row.entity_type,
        "entity_id": str(row.entity_id) if row.entity_id else None, "metadata": row.metadata_json,
        "created_at": row.created_at.isoformat() if row.created_at else None,
    } for row in rows]}
