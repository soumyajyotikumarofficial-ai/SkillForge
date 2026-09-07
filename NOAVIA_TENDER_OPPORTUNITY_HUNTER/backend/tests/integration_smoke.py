"""Database-backed vertical-slice smoke test for a running Compose stack."""
from __future__ import annotations

import json
from datetime import datetime, timedelta, timezone
from uuid import uuid4

from sqlalchemy import delete, func, select

import app.pipeline as pipeline
from app.db import SessionLocal
from app.models import (
    CompanyProfile, MatchAlert, Opportunity, OpportunityEvent, Organization,
    SourceSnapshot, TenderSourceReference,
)
from app.notifications import deliver_pending_alerts
from app.source import TenderCandidate


def main() -> None:
    organization_id = uuid4()
    db = SessionLocal()
    try:
        db.add(Organization(id=organization_id, name="Synthetic smoke organization"))
        db.add(CompanyProfile(
            organization_id=organization_id, company_name="Synthetic Verified Company",
            offerings=["cloud software"], sectors=["public sector"], use_cases=["data automation"],
            keywords=["cloud"], cpv_codes=["72000000"], geographies=["Germany"],
            value_preferences={"min": 10000, "max": 5000000}, certifications=["ISO 27001"],
            exclusions=[], strategic_priorities=["lighthouse"], alert_emails=["smoke@noavia.local"],
        ))
        db.commit()

        deadline = datetime.now(timezone.utc) + timedelta(days=40)

        def candidate(version: int) -> TenderCandidate:
            current_deadline = deadline - timedelta(days=version - 1)
            return TenderCandidate(
                external_id="synthetic-smoke-001",
                title="Cloud software data automation for the German public sector lighthouse programme",
                buyer="Synthetic Public Buyer",
                description="Cloud software and data automation delivery in Germany for the public sector.",
                deadline=current_deadline,
                deadline_original=current_deadline.isoformat(),
                cpv_codes=["72000000"], status="open", place_of_performance=["Germany"],
                mandatory_requirements=[{
                    "text": "ISO 27001 certification is mandatory", "excerpt": "ISO 27001 certification is mandatory",
                    "validation_state": "verified", "validation_score": 1.0, "extraction_method": "source_field",
                }],
                notice_url="https://example.test/synthetic-smoke-001",
                raw_data={"id": "synthetic-smoke-001", "version": version, "deadline": current_deadline.isoformat()},
            )

        pipeline.fetch_candidates = lambda *_args, **_kwargs: [candidate(1)]
        first = pipeline.run_scan(db, organization_id)
        pipeline.fetch_candidates = lambda *_args, **_kwargs: [candidate(2)]
        second = pipeline.run_scan(db, organization_id)
        delivered = deliver_pending_alerts(db)

        opportunities = db.scalar(select(func.count()).select_from(Opportunity).where(Opportunity.organization_id == organization_id))
        opportunity = db.scalar(select(Opportunity).where(Opportunity.organization_id == organization_id))
        snapshots = db.scalar(select(func.count()).select_from(SourceSnapshot).where(SourceSnapshot.organization_id == organization_id))
        references = db.scalar(select(func.count()).select_from(TenderSourceReference).where(TenderSourceReference.organization_id == organization_id))
        events = db.scalar(select(func.count()).select_from(OpportunityEvent).where(OpportunityEvent.organization_id == organization_id))
        alerts = db.scalar(select(func.count()).select_from(MatchAlert).where(MatchAlert.organization_id == organization_id))

        assert first["created"] == 1
        assert second["created"] == 0 and second["updated"] == 1
        assert opportunities == 1 and references == 1 and snapshots == 2
        assert opportunity and opportunity.score >= 80 and opportunity.recommendation == "hot"
        assert events >= 2 and alerts == 2 and delivered == 2
        print(json.dumps({
            "organization_id": str(organization_id), "score": opportunity.score,
            "opportunities": opportunities, "source_references": references, "snapshots": snapshots,
            "events": events, "alerts": alerts, "emails_delivered": delivered,
        }))
    finally:
        db.rollback()
        db.execute(delete(Organization).where(Organization.id == organization_id))
        db.commit()
        db.close()


if __name__ == "__main__":
    main()
