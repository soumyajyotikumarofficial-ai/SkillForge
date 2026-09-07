from __future__ import annotations

import hashlib
import json
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from decimal import Decimal, InvalidOperation
from uuid import UUID

from sqlalchemy import select
from sqlalchemy.orm import Session

from .config import get_settings
from .models import (
    AlertPreference, AnalysisRun, AuditEvent, CompanyProfile, Document,
    EligibilityState, FactEvidence, MatchAlert, Opportunity, OpportunityEvent,
    Source, SourceRun, SourceSnapshot, Tender, TenderSourceReference,
)
from .processing import (
    PARSER_VERSION, analyze_text, clamav_scan, parse_document_with_metadata,
    score_profile_match, validate_document_type,
)
from .source import DEFAULT_SOURCE_URL, TenderCandidate, fetch_candidates, filter_candidates
from .storage import ContentAddressedStorage

PROMPT_VERSION = "tender-extraction-2"


def ensure_source(db: Session, organization_id: UUID) -> Source:
    settings = get_settings()
    configured_url = settings.source_url or DEFAULT_SOURCE_URL
    source = db.scalar(select(Source).where(Source.organization_id == organization_id, Source.url == configured_url).limit(1))
    if source:
        return source
    source = Source(
        organization_id=organization_id,
        name="service.bund.de public tenders" if "service.bund.de" in configured_url else "Configured procurement feed",
        url=configured_url,
        adapter_type="feed",
        schedule_minutes=settings.scan_interval_minutes,
        access_policy={"public": True, "authenticated_scraping": False},
        rate_limit_config={"max_concurrent": 1},
    )
    db.add(source)
    db.flush()
    return source


def _download(url: str, timeout: int) -> tuple[bytes, str]:
    if not url.lower().startswith(("http://", "https://")):
        raise ValueError("document URL must be http(s)")
    request = urllib.request.Request(url, headers={"User-Agent": "Noavia-Tender-Hunter/1.0"})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        content = response.read(15 * 1024 * 1024 + 1)
        if len(content) > 15 * 1024 * 1024:
            raise ValueError("document exceeds 15 MiB safety limit")
        return content, response.headers.get("content-type", "application/octet-stream").split(";", 1)[0]


def _profile_dict(profile: CompanyProfile | None, fallback_keywords: list[str]) -> dict:
    if profile is None:
        return {"offerings": fallback_keywords, "keywords": fallback_keywords}
    return {key: getattr(profile, key) for key in (
        "offerings", "sectors", "use_cases", "keywords", "cpv_codes", "geographies",
        "value_preferences", "certifications", "exclusions", "strategic_priorities",
    )}


def _candidate_payload(candidate: TenderCandidate) -> dict:
    return {
        "external_id": candidate.external_id, "title": candidate.title, "buyer": candidate.buyer,
        "description": candidate.description,
        "deadline": candidate.deadline.isoformat() if candidate.deadline else None,
        "deadline_original": candidate.deadline_original,
        "questions_deadline": candidate.questions_deadline.isoformat() if candidate.questions_deadline else None,
        "publication_date": candidate.publication_date.isoformat() if candidate.publication_date else None,
        "procedure_type": candidate.procedure_type, "cpv_codes": candidate.cpv_codes, "lots": candidate.lots,
        "status": candidate.status, "contract_period": candidate.contract_period,
        "place_of_performance": candidate.place_of_performance, "estimated_value": candidate.estimated_value,
        "currency": candidate.currency, "mandatory_requirements": candidate.mandatory_requirements,
        "award_criteria": candidate.award_criteria, "contact": candidate.contact, "language": candidate.language,
        "notice_url": candidate.notice_url, "document_urls": candidate.document_urls, "raw": candidate.raw_data,
    }


def _hash_payload(payload: dict) -> str:
    return hashlib.sha256(json.dumps(payload, sort_keys=True, ensure_ascii=False, default=str).encode("utf-8")).hexdigest()


def _canonical_key(candidate: TenderCandidate) -> str:
    normalized = "|".join((
        " ".join(candidate.title.casefold().split()),
        " ".join((candidate.buyer or "").casefold().split()),
        candidate.deadline.date().isoformat() if candidate.deadline else "",
    ))
    return hashlib.sha256(normalized.encode("utf-8")).hexdigest()


def _safe_decimal(value: float | None) -> Decimal | None:
    try:
        return Decimal(str(value)) if value is not None else None
    except InvalidOperation:
        return None


def _material_state(tender: Tender) -> dict:
    return {
        "deadline": tender.deadline.isoformat() if tender.deadline else None,
        "questions_deadline": tender.questions_deadline.isoformat() if tender.questions_deadline else None,
        "status": tender.status, "document_urls": tender.document_urls or [],
        "mandatory_requirements": tender.mandatory_requirements or [], "award_criteria": tender.award_criteria or [],
        "estimated_value": str(tender.estimated_value) if tender.estimated_value is not None else None,
    }


def _audit(db: Session, organization_id: UUID, action: str, entity_type: str, entity_id: UUID | None, metadata: dict | None = None) -> None:
    db.add(AuditEvent(organization_id=organization_id, action=action, entity_type=entity_type, entity_id=entity_id, metadata_json=metadata or {}))


def _alert(db: Session, organization_id: UUID, opportunity: Opportunity, alert_type: str, suffix: str, title: str, message: str) -> None:
    key = f"{organization_id}:{opportunity.id}:{alert_type}:{suffix}"
    if db.scalar(select(MatchAlert).where(MatchAlert.idempotency_key == key)):
        return
    db.add(MatchAlert(
        organization_id=organization_id, opportunity_id=opportunity.id, alert_type=alert_type,
        title=title[:300], message=message,
        payload={"score": opportunity.score, "recommendation": opportunity.recommendation}, idempotency_key=key,
    ))
    _audit(db, organization_id, "alert.created", "opportunity", opportunity.id, {"type": alert_type})


def _replace_evidence(db: Session, opportunity: Opportunity, tender: Tender, snapshot: SourceSnapshot, extracted: dict) -> None:
    for row in list(opportunity.evidence):
        db.delete(row)
    if tender.deadline:
        db.add(FactEvidence(
            organization_id=opportunity.organization_id, opportunity_id=opportunity.id,
            source_snapshot_id=snapshot.id, fact_key="submission_deadline", source_url=tender.notice_url,
            excerpt=tender.deadline_original or tender.deadline.isoformat(), extraction_method="source_field",
            validation_state="verified" if tender.deadline_original else "review_required",
            validation_score=1.0 if tender.deadline_original else 0.7, content_hash=snapshot.content_hash,
        ))
    for index, requirement in enumerate(tender.mandatory_requirements or []):
        item = requirement if isinstance(requirement, dict) else {"text": str(requirement)}
        db.add(FactEvidence(
            organization_id=opportunity.organization_id, opportunity_id=opportunity.id,
            source_snapshot_id=snapshot.id, fact_key=f"mandatory_requirement:{index}", source_url=tender.notice_url,
            page=item.get("page"), sheet=item.get("sheet"), section=item.get("section"),
            excerpt=(item.get("excerpt") or item.get("text") or "")[:1000],
            extraction_method=item.get("extraction_method", "model" if extracted.get("model_used") else "rule"),
            validation_state=item.get("validation_state", "review_required"),
            validation_score=float(item.get("validation_score", 0.6)), content_hash=snapshot.content_hash,
        ))


def _store_documents(db: Session, tender: Tender, snapshot: SourceSnapshot, settings, storage: ContentAddressedStorage) -> tuple[str, int]:
    text = tender.description or tender.title
    failures = 0
    for url in (tender.document_urls or [])[:5]:
        filename = urllib.parse.unquote(url.split("?", 1)[0].rsplit("/", 1)[-1])[:500] or "document"
        try:
            content, content_type = _download(url, settings.source_timeout_seconds)
            validate_document_type(filename, content_type)
            if not clamav_scan(content):
                failures += 1
                _audit(db, tender.organization_id, "document.malware_blocked", "tender", tender.id, {"url": url})
                continue
            stored = storage.put(content)
            document = db.scalar(select(Document).where(Document.organization_id == tender.organization_id, Document.sha256 == stored.sha256))
            parsed = parse_document_with_metadata(content, filename, content_type)
            if document is None:
                document = Document(
                    organization_id=tender.organization_id, tender_id=tender.id, source_snapshot_id=snapshot.id,
                    source_url=url, filename=filename, content_type=content_type, sha256=stored.sha256,
                    size_bytes=stored.size_bytes, storage_key=stored.key,
                )
                db.add(document)
            document.scan_status = "clean"
            document.parse_status = "ocr_required" if parsed.ocr_required else "parsed"
            document.extracted_text = parsed.text[:20000]
            document.page_count = parsed.page_count
            document.sheet_count = parsed.sheet_count
            document.ocr_required = parsed.ocr_required
            text += "\n" + parsed.text
        except Exception as exc:
            failures += 1
            _audit(db, tender.organization_id, "document.processing_failed", "tender", tender.id, {"url": url, "error": str(exc)[:500]})
    return text, failures


def run_scan(db: Session, organization_id: UUID) -> dict:
    settings = get_settings()
    source = ensure_source(db, organization_id)
    now = datetime.now(timezone.utc)
    source.last_attempt_at = now
    run = SourceRun(organization_id=organization_id, source_id=source.id, status="running")
    db.add(run)
    db.flush()
    source_id, run_id = source.id, run.id
    _audit(db, organization_id, "source.scan_started", "source", source.id, {"url": source.url})
    try:
        profile = db.scalar(select(CompanyProfile).where(CompanyProfile.organization_id == organization_id))
        fallback_keywords = [item.strip() for item in settings.source_keywords.split(",") if item.strip()]
        profile_values = _profile_dict(profile, fallback_keywords)
        prefilter_terms = list(dict.fromkeys(fallback_keywords + list(profile_values.get("keywords", [])) + list(profile_values.get("offerings", []))))
        fetched = fetch_candidates(source.url, timeout=settings.source_timeout_seconds)
        if not isinstance(fetched, list):
            raise ValueError("source adapter returned an invalid result")
        candidates = filter_candidates(fetched, prefilter_terms)
        run.fetched_count = len(candidates)
        storage = ContentAddressedStorage(settings.storage_root)
        preferences = db.scalar(select(AlertPreference).where(AlertPreference.organization_id == organization_id))
        threshold = preferences.match_threshold if preferences else 80
        created = updated = unchanged = document_failures = 0

        for candidate in candidates:
            payload = _candidate_payload(candidate)
            content_hash = _hash_payload(payload)
            reference = db.scalar(select(TenderSourceReference).where(
                TenderSourceReference.organization_id == organization_id,
                TenderSourceReference.source_id == source.id,
                TenderSourceReference.source_ref == candidate.external_id,
            ))
            prior_snapshot = db.get(SourceSnapshot, reference.latest_snapshot_id) if reference and reference.latest_snapshot_id else None
            snapshot = db.scalar(select(SourceSnapshot).where(
                SourceSnapshot.source_id == source.id, SourceSnapshot.source_ref == candidate.external_id,
                SourceSnapshot.content_hash == content_hash,
            ))
            changed = snapshot is None
            if snapshot is None:
                snapshot = SourceSnapshot(
                    organization_id=organization_id, source_id=source.id, source_ref=candidate.external_id,
                    source_url=candidate.notice_url, content_hash=content_hash, raw_payload=payload,
                    prior_snapshot_id=prior_snapshot.id if prior_snapshot else None,
                )
                db.add(snapshot)
                db.flush()

            tender = reference.tender if reference else None
            if tender is None:
                tender = db.scalar(select(Tender).where(
                    Tender.organization_id == organization_id,
                    Tender.external_id == candidate.external_id,
                ))
            if tender is None:
                canonical_key = _canonical_key(candidate)
                tender = db.scalar(select(Tender).where(Tender.organization_id == organization_id, Tender.canonical_key == canonical_key))
                if tender is None:
                    tender = Tender(
                        organization_id=organization_id, source_id=source.id, external_id=candidate.external_id,
                        canonical_key=canonical_key, title=candidate.title,
                    )
                    db.add(tender)
                    db.flush()
                    created += 1
            if reference is None:
                if not tender.canonical_key:
                    tender.canonical_key = _canonical_key(candidate)
                reference = TenderSourceReference(
                    organization_id=organization_id, tender_id=tender.id, source_id=source.id,
                    source_ref=candidate.external_id, source_url=candidate.notice_url, latest_snapshot_id=snapshot.id,
                )
                db.add(reference)
            old_state = _material_state(tender)
            reference.latest_snapshot_id = snapshot.id
            reference.source_url = candidate.notice_url
            reference.last_seen_at = now

            tender.title, tender.buyer, tender.description = candidate.title, candidate.buyer, candidate.description
            tender.deadline, tender.deadline_original = candidate.deadline, candidate.deadline_original
            tender.questions_deadline, tender.publication_date = candidate.questions_deadline, candidate.publication_date
            tender.procedure_type, tender.cpv_codes, tender.lots = candidate.procedure_type, candidate.cpv_codes, candidate.lots
            tender.status, tender.contract_period = candidate.status, candidate.contract_period
            if tender.deadline and tender.deadline < now:
                tender.status = "closed"
            tender.place_of_performance = candidate.place_of_performance
            tender.estimated_value, tender.currency = _safe_decimal(candidate.estimated_value), (candidate.currency or "")[:3] or None
            tender.award_criteria, tender.contact, tender.language = candidate.award_criteria, candidate.contact, candidate.language
            tender.notice_url, tender.document_urls = candidate.notice_url, candidate.document_urls
            tender.raw_data, tender.normalized_data, tender.latest_snapshot_id = candidate.raw_data, payload, snapshot.id
            new_state = _material_state(tender)
            material_changes = {key: {"old": old_state.get(key), "new": value} for key, value in new_state.items() if old_state.get(key) != value}

            text, failed_documents = _store_documents(db, tender, snapshot, settings, storage)
            document_failures += failed_documents
            extracted = analyze_text(text, {
                "title": tender.title, "buyer": tender.buyer,
                "deadline": tender.deadline.isoformat() if tender.deadline else None,
                "description": tender.description, "award_criteria": tender.award_criteria,
            })
            tender.mandatory_requirements = candidate.mandatory_requirements or extracted.get("mandatory_requirements", [])
            new_state = _material_state(tender)
            if old_state.get("mandatory_requirements") != new_state.get("mandatory_requirements"):
                material_changes["mandatory_requirements"] = {"old": old_state.get("mandatory_requirements"), "new": new_state.get("mandatory_requirements")}

            opportunity = tender.opportunity
            was_existing = opportunity is not None
            if opportunity is None:
                opportunity = Opportunity(organization_id=organization_id, tender_id=tender.id)
                db.add(opportunity)
                db.flush()
            scoring = score_profile_match({
                "title": tender.title, "buyer": tender.buyer, "description": tender.description,
                "deadline": tender.deadline, "status": tender.status, "procedure_type": tender.procedure_type,
                "cpv_codes": tender.cpv_codes, "place_of_performance": tender.place_of_performance,
                "estimated_value": tender.estimated_value, "mandatory_requirements": tender.mandatory_requirements,
            }, profile_values, extracted)
            opportunity.score, opportunity.profile_version = scoring["score"], profile.version if profile else 0
            opportunity.component_scores, opportunity.scoring_version = scoring["component_scores"], scoring["scoring_version"]
            opportunity.eligibility_state = EligibilityState(scoring["eligibility_state"])
            opportunity.hard_blockers, opportunity.missing_information = scoring["hard_blockers"], scoring["missing_information"]
            opportunity.match_reasons, opportunity.recommendation = scoring["match_reasons"], scoring["recommendation"]
            opportunity.rationale, opportunity.extracted, opportunity.processed_at = scoring["rationale"], extracted, now
            opportunity.is_new = not was_existing
            opportunity.is_updated = bool(was_existing and changed and material_changes)
            if opportunity.is_updated:
                opportunity.last_change_at = now
                updated += 1
            elif was_existing:
                unchanged += 1
            db.flush()
            _replace_evidence(db, opportunity, tender, snapshot, extracted)
            db.add(AnalysisRun(
                organization_id=organization_id, opportunity_id=opportunity.id, snapshot_id=snapshot.id,
                status="complete", parser_version=PARSER_VERSION, prompt_version=PROMPT_VERSION,
                model_version=settings.openrouter_model if settings.openrouter_api_key else "deterministic-local",
                scoring_version=opportunity.scoring_version,
            ))
            if opportunity.is_new:
                db.add(OpportunityEvent(
                    organization_id=organization_id, opportunity_id=opportunity.id, event_type="created",
                    new_value=new_state, source_snapshot_id=snapshot.id,
                ))
            elif opportunity.is_updated:
                db.add(OpportunityEvent(
                    organization_id=organization_id, opportunity_id=opportunity.id, event_type="material_update",
                    old_value={key: value["old"] for key, value in material_changes.items()},
                    new_value={key: value["new"] for key, value in material_changes.items()},
                    source_snapshot_id=snapshot.id,
                ))
            if (opportunity.is_new or opportunity.is_updated) and opportunity.score >= threshold and opportunity.recommendation == "hot":
                _alert(
                    db, organization_id, opportunity, "new_match" if opportunity.is_new else "material_update",
                    snapshot.content_hash, f"{opportunity.recommendation.upper()}: {tender.title}",
                    f"Fit {opportunity.score:.0f}/100. Deadline: {tender.deadline.isoformat() if tender.deadline else 'review required'}. "
                    + "; ".join(opportunity.match_reasons[:3]),
                )
            elif opportunity.is_updated and opportunity.decision.value == "shortlisted":
                _alert(
                    db, organization_id, opportunity, "material_update", snapshot.content_hash,
                    f"UPDATED: {tender.title}",
                    "A shortlisted opportunity changed materially. Review the before/after event and source evidence.",
                )

        source.last_success_at, source.last_error = now, None
        run.created_count, run.status, run.finished_at = created, "complete", now
        _audit(db, organization_id, "source.scan_completed", "source", source.id, {
            "fetched": len(candidates), "created": created, "updated": updated,
            "unchanged": unchanged, "document_failures": document_failures,
        })
        db.commit()
        return {
            "run_id": str(run.id), "fetched": len(candidates), "created": created,
            "updated": updated, "unchanged": unchanged, "document_failures": document_failures,
        }
    except Exception as exc:
        db.rollback()
        source = db.get(Source, source_id)
        if source:
            source.last_error = str(exc)[:4000]
            source.last_attempt_at = datetime.now(timezone.utc)
        failed_run = db.get(SourceRun, run_id)
        if failed_run is None:
            failed_run = SourceRun(id=run_id, organization_id=organization_id, source_id=source_id, status="failed")
            db.add(failed_run)
        failed_run.status, failed_run.error, failed_run.finished_at = "failed", str(exc)[:4000], datetime.now(timezone.utc)
        _audit(db, organization_id, "source.scan_failed", "source", source_id, {"error": str(exc)[:500]})
        db.commit()
        raise
