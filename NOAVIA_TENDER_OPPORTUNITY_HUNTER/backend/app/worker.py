import logging
import os
import time

from .db import SessionLocal
from .queue import claim_next_job, fail_job, finish_job
from .pipeline import run_scan
from .notifications import deliver_pending_alerts

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("noavia.worker")


def process_job(job) -> None:
    db = SessionLocal()
    try:
        if job.kind == "scan":
            result = run_scan(db, job.organization_id)
            deliver_pending_alerts(db)
        else:
            result = {"kind": job.kind, "message": "Unknown job kind ignored"}
        finish_job(db, job.id, result)
    finally:
        db.close()


def run() -> None:
    logger.info("worker started")
    while True:
        db = SessionLocal()
        try:
            job = claim_next_job(db, os.getenv("HOSTNAME", "worker"))
            if job is None:
                time.sleep(2)
                continue
            logger.info("processing job %s", job.id)
            try:
                process_job(job)
            except Exception as exc:
                logger.exception("job %s failed", job.id)
                failure_db = SessionLocal()
                try:
                    fail_job(failure_db, job.id, str(exc))
                finally:
                    failure_db.close()
        finally:
            db.close()


if __name__ == "__main__":
    run()
