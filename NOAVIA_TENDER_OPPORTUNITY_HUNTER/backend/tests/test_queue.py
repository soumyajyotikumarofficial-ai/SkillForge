from types import SimpleNamespace
from uuid import uuid4

from app.models import JobStatus
from app.queue import claim_next_job, fail_job


class _Tx:
    def __enter__(self): return self
    def __exit__(self, *_): return False


class _Result:
    def __init__(self, job): self.job = job
    def scalars(self): return self
    def first(self): return self.job


class _DB:
    def __init__(self, job): self.job = job
    def begin(self): return _Tx()
    def execute(self, _query): return _Result(self.job)
    def flush(self): pass
    def get(self, _model, _id): return self.job


def test_claim_marks_job_processing_and_fail_requeues():
    job = SimpleNamespace(status=JobStatus.queued, run_at=None, attempts=0, max_attempts=2,
                          locked_at=None, locked_by=None, id=uuid4(), last_error=None)
    db = _DB(job)
    assert claim_next_job(db, "test-worker") is job
    assert job.status == JobStatus.processing
    assert job.attempts == 1
    fail_job(db, job.id, "temporary")
    assert job.status == JobStatus.queued
    assert job.last_error == "temporary"
