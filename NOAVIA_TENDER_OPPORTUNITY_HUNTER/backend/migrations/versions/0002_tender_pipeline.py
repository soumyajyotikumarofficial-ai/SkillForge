"""Add public source, tender, run and opportunity tables."""
from typing import Sequence, Union
from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql

revision: str = "0002_tender_pipeline"
down_revision: Union[str, None] = "0001_initial"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    uuid = postgresql.UUID(as_uuid=True)
    jsonb = postgresql.JSONB
    op.create_table("sources",
        sa.Column("id", uuid, primary_key=True), sa.Column("organization_id", uuid, sa.ForeignKey("organizations.id", ondelete="CASCADE"), nullable=False),
        sa.Column("name", sa.String(200), nullable=False), sa.Column("url", sa.String(1000), nullable=False),
        sa.Column("enabled", sa.Boolean, nullable=False, server_default=sa.true()), sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.UniqueConstraint("organization_id", "name", name="uq_sources_org_name"))
    op.create_index("ix_sources_organization_id", "sources", ["organization_id"])
    op.create_table("source_runs",
        sa.Column("id", uuid, primary_key=True), sa.Column("organization_id", uuid, sa.ForeignKey("organizations.id", ondelete="CASCADE"), nullable=False),
        sa.Column("source_id", uuid, sa.ForeignKey("sources.id", ondelete="CASCADE"), nullable=False), sa.Column("status", sa.String(30), nullable=False),
        sa.Column("fetched_count", sa.Integer, nullable=False, server_default="0"), sa.Column("created_count", sa.Integer, nullable=False, server_default="0"),
        sa.Column("error", sa.Text), sa.Column("started_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False), sa.Column("finished_at", sa.DateTime(timezone=True)))
    op.create_index("ix_source_runs_organization_id", "source_runs", ["organization_id"])
    op.create_index("ix_source_runs_source_id", "source_runs", ["source_id"])
    op.create_index("ix_source_runs_status", "source_runs", ["status"])
    op.create_table("tenders",
        sa.Column("id", uuid, primary_key=True), sa.Column("organization_id", uuid, sa.ForeignKey("organizations.id", ondelete="CASCADE"), nullable=False),
        sa.Column("source_id", uuid, sa.ForeignKey("sources.id", ondelete="CASCADE"), nullable=False), sa.Column("external_id", sa.String(255), nullable=False),
        sa.Column("title", sa.String(1000), nullable=False), sa.Column("buyer", sa.String(500)), sa.Column("description", sa.Text),
        sa.Column("deadline", sa.DateTime(timezone=True)), sa.Column("notice_url", sa.String(1200)), sa.Column("document_urls", jsonb, nullable=False, server_default="[]"),
        sa.Column("raw_data", jsonb, nullable=False, server_default="{}"), sa.Column("first_seen_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.UniqueConstraint("organization_id", "external_id", name="uq_tenders_org_external_id"))
    op.create_index("ix_tenders_organization_id", "tenders", ["organization_id"])
    op.create_index("ix_tenders_source_id", "tenders", ["source_id"])
    op.add_column("documents", sa.Column("tender_id", uuid, sa.ForeignKey("tenders.id", ondelete="SET NULL")))
    op.add_column("documents", sa.Column("scan_status", sa.String(30), nullable=False, server_default="stored"))
    op.add_column("documents", sa.Column("extracted_text", sa.Text))
    op.create_index("ix_documents_tender_id", "documents", ["tender_id"])
    op.create_table("opportunities",
        sa.Column("id", uuid, primary_key=True), sa.Column("organization_id", uuid, sa.ForeignKey("organizations.id", ondelete="CASCADE"), nullable=False),
        sa.Column("tender_id", uuid, sa.ForeignKey("tenders.id", ondelete="CASCADE"), nullable=False, unique=True), sa.Column("score", sa.Float, nullable=False, server_default="0"),
        sa.Column("recommendation", sa.String(30), nullable=False, server_default="review"), sa.Column("rationale", sa.Text), sa.Column("extracted", jsonb, nullable=False, server_default="{}"),
        sa.Column("processed_at", sa.DateTime(timezone=True)))
    op.create_index("ix_opportunities_organization_id", "opportunities", ["organization_id"])
    op.create_index("ix_opportunities_tender_id", "opportunities", ["tender_id"])


def downgrade() -> None:
    op.drop_table("opportunities")
    op.drop_index("ix_documents_tender_id", table_name="documents")
    op.drop_column("documents", "extracted_text")
    op.drop_column("documents", "scan_status")
    op.drop_column("documents", "tender_id")
    op.drop_table("tenders")
    op.drop_table("source_runs")
    op.drop_table("sources")
