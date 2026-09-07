"""Backfill source references for opportunities created before snapshot support.

Revision ID: 0004_backfill_source_references
Revises: 0003_complete_mvp
"""
from typing import Sequence, Union

from alembic import op

revision: str = "0004_backfill_source_references"
down_revision: Union[str, None] = "0003_complete_mvp"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.execute("""
        INSERT INTO tender_source_references
            (id, organization_id, tender_id, source_id, source_ref, source_url, first_seen_at, last_seen_at)
        SELECT
            gen_random_uuid(), t.organization_id, t.id, t.source_id, t.external_id,
            t.notice_url, t.first_seen_at, COALESCE(t.updated_at, t.first_seen_at)
        FROM tenders t
        WHERE NOT EXISTS (
            SELECT 1 FROM tender_source_references r
            WHERE r.source_id = t.source_id AND r.source_ref = t.external_id
        )
    """)


def downgrade() -> None:
    # Backfilled rows are valid domain data and are intentionally retained.
    pass
