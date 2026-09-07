from __future__ import annotations

import logging
import time
from datetime import datetime, timedelta, timezone

from sqlalchemy import select

from .db import SessionLocal
from .models import CompanyProfile, Job, JobStatus, Organization, Source
from .pipeline import ensure_source
from .notifications import create_due_deadline_alerts, deliver_pending_alerts

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("noavia.scheduler")


def enqueue_due_scans() -> int:
    now = datetime.now(timezone.utc)
    db = SessionLocal()
    queued = 0
    try:
        organization_ids = db.scalars(select(CompanyProfile.organization_id)).all()
        if not organization_ids:
            organization_ids = db.scalars(select(Organization.id)).all()
        for organization_id in organization_ids:
            ensure_source(db, organization_id)
        db.flush()
        sources = db.scalars(select(Source).where(Source.enabled.is_(True))).all()
        for source in sources:
            due = source.last_attempt_at is None or source.last_attempt_at <= now - timedelta(minutes=source.schedule_minutes)
            if not due:
                continue
            bucket_seconds = max(60, source.schedule_minutes * 60)
            bucket = int(now.timestamp()) // bucket_seconds
            key = f"scheduled-scan:{source.organization_id}:{source.id}:{bucket}"
            if db.scalar(select(Job).where(Job.idempotency_key == key)):
                continue
            db.add(Job(
                organization_id=source.organization_id, kind="scan",
                status=JobStatus.queued, payload={"organization_id": str(source.organization_id), "source_id": str(source.id)},
                idempotency_key=key,
            ))
            source.last_attempt_at = now
            queued += 1
        db.commit()
        return queued
    finally:
        db.close()


def run() -> None:
    logger.info("scheduler started")
    while True:
        try:
            count = enqueue_due_scans()
            if count:
                logger.info("queued %s scheduled scan(s)", count)
            db = SessionLocal()
            try:
                create_due_deadline_alerts(db)
                deliver_pending_alerts(db)
            finally:
                db.close()
        except Exception:
            logger.exception("scheduler tick failed")
        time.sleep(60)


if __name__ == "__main__":
    run()
