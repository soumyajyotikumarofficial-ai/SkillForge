from datetime import datetime, timedelta, timezone
from uuid import UUID

from sqlalchemy import select, text
from sqlalchemy.orm import Session

from .models import Job, JobStatus


def claim_next_job(db: Session, worker_id: str) -> Job | None:
    """Atomically claim one queued job so multiple workers do not duplicate work."""
    with db.begin():
        job = db.execute(
            select(Job)
            .where(
                Job.status == JobStatus.queued,
                Job.run_at <= text("CURRENT_TIMESTAMP"),
                Job.attempts < Job.max_attempts,
            )
            .order_by(Job.run_at, Job.created_at)
            .with_for_update(skip_locked=True)
        ).scalars().first()
        if job is None:
            return None
        job.status = JobStatus.processing
        job.locked_at = datetime.now(timezone.utc)
        job.locked_by = worker_id
        job.attempts += 1
        db.flush()
        return job


def finish_job(db: Session, job_id: UUID, result: dict) -> None:
    with db.begin():
        job = db.get(Job, job_id)
        if job:
            job.status = JobStatus.complete
            job.result = result
            job.last_error = None
            job.locked_at = None
            job.locked_by = None


def fail_job(db: Session, job_id: UUID, error: str) -> None:
    with db.begin():
        job = db.get(Job, job_id)
        if job:
            job.status = JobStatus.failed if job.attempts >= job.max_attempts else JobStatus.queued
            job.last_error = error[:4000]
            if job.status == JobStatus.queued:
                job.run_at = datetime.now(timezone.utc) + timedelta(seconds=min(900, 15 * (2 ** max(0, job.attempts - 1))))
            job.locked_at = None
            job.locked_by = None
