"""Complete the Tender Opportunity Hunter MVP domain model.

Revision ID: 0003_complete_mvp
Revises: 0002_tender_pipeline
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql

revision: str = "0003_complete_mvp"
down_revision: Union[str, None] = "0002_tender_pipeline"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    uuid = postgresql.UUID(as_uuid=True)
    jsonb = postgresql.JSONB
    postgresql.ENUM("undecided", "shortlisted", "skipped", "review", "closed", name="decision_state").create(op.get_bind(), checkfirst=True)
    postgresql.ENUM("met", "not_met", "unknown", "review", name="eligibility_state").create(op.get_bind(), checkfirst=True)
    decision_state = postgresql.ENUM("undecided", "shortlisted", "skipped", "review", "closed", name="decision_state", create_type=False)
    eligibility_state = postgresql.ENUM("met", "not_met", "unknown", "review", name="eligibility_state", create_type=False)

    op.create_table(
        "company_profiles",
        sa.Column("id", uuid, primary_key=True),
        sa.Column("organization_id", uuid, sa.ForeignKey("organizations.id", ondelete="CASCADE"), nullable=False, unique=True),
        sa.Column("version", sa.Integer(), nullable=False, server_default="1"),
        sa.Column("company_name", sa.String(300), nullable=False),
        sa.Column("offerings", jsonb, nullable=False, server_default="[]"),
        sa.Column("sectors", jsonb, nullable=False, server_default="[]"),
        sa.Column("use_cases", jsonb, nullable=False, server_default="[]"),
        sa.Column("keywords", jsonb, nullable=False, server_default="[]"),
        sa.Column("cpv_codes", jsonb, nullable=False, server_default="[]"),
        sa.Column("geographies", jsonb, nullable=False, server_default="[]"),
        sa.Column("value_preferences", jsonb, nullable=False, server_default="{}"),
        sa.Column("certifications", jsonb, nullable=False, server_default="[]"),
        sa.Column("exclusions", jsonb, nullable=False, server_default="[]"),
        sa.Column("strategic_priorities", jsonb, nullable=False, server_default="[]"),
        sa.Column("alert_emails", jsonb, nullable=False, server_default="[]"),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
    )
    op.create_index("ix_company_profiles_organization_id", "company_profiles", ["organization_id"])
    op.create_table(
        "profile_evidence",
        sa.Column("id", uuid, primary_key=True),
        sa.Column("organization_id", uuid, sa.ForeignKey("organizations.id", ondelete="CASCADE"), nullable=False),
        sa.Column("profile_id", uuid, sa.ForeignKey("company_profiles.id", ondelete="CASCADE"), nullable=False),
        sa.Column("kind", sa.String(50), nullable=False, server_default="capability"),
        sa.Column("filename", sa.String(500), nullable=False),
        sa.Column("storage_key", sa.String(200), nullable=False),
        sa.Column("sha256", sa.String(64), nullable=False),
        sa.Column("parse_status", sa.String(30), nullable=False, server_default="stored"),
        sa.Column("extracted_facts", jsonb, nullable=False, server_default="{}"),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
    )
    op.create_index("ix_profile_evidence_organization_id", "profile_evidence", ["organization_id"])
    op.create_index("ix_profile_evidence_profile_id", "profile_evidence", ["profile_id"])
    op.create_index("ix_profile_evidence_sha256", "profile_evidence", ["sha256"])

    op.add_column("sources", sa.Column("adapter_type", sa.String(50), nullable=False, server_default="feed"))
    op.add_column("sources", sa.Column("access_policy", jsonb, nullable=False, server_default="{}"))
    op.add_column("sources", sa.Column("schedule_minutes", sa.Integer(), nullable=False, server_default="1440"))
    op.add_column("sources", sa.Column("rate_limit_config", jsonb, nullable=False, server_default="{}"))
    op.add_column("sources", sa.Column("last_attempt_at", sa.DateTime(timezone=True)))
    op.add_column("sources", sa.Column("last_success_at", sa.DateTime(timezone=True)))
    op.add_column("sources", sa.Column("last_error", sa.Text()))
    op.create_table(
        "source_snapshots",
        sa.Column("id", uuid, primary_key=True),
        sa.Column("organization_id", uuid, sa.ForeignKey("organizations.id", ondelete="CASCADE"), nullable=False),
        sa.Column("source_id", uuid, sa.ForeignKey("sources.id", ondelete="CASCADE"), nullable=False),
        sa.Column("source_ref", sa.String(500), nullable=False),
        sa.Column("source_url", sa.String(1200)),
        sa.Column("content_hash", sa.String(64), nullable=False),
        sa.Column("raw_payload", jsonb, nullable=False, server_default="{}"),
        sa.Column("prior_snapshot_id", uuid, sa.ForeignKey("source_snapshots.id", ondelete="SET NULL")),
        sa.Column("fetched_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.UniqueConstraint("source_id", "source_ref", "content_hash", name="uq_source_snapshot_version"),
    )
    for column in ("organization_id", "source_id", "source_ref", "content_hash"):
        op.create_index(f"ix_source_snapshots_{column}", "source_snapshots", [column])

    for column in (
        sa.Column("source_snapshot_id", uuid, sa.ForeignKey("source_snapshots.id", ondelete="SET NULL")),
        sa.Column("source_url", sa.String(1200)),
        sa.Column("parse_status", sa.String(30), nullable=False, server_default="pending"),
        sa.Column("page_count", sa.Integer()),
        sa.Column("sheet_count", sa.Integer()),
        sa.Column("ocr_required", sa.Boolean(), nullable=False, server_default=sa.false()),
    ):
        op.add_column("documents", column)
    op.create_index("ix_documents_source_snapshot_id", "documents", ["source_snapshot_id"])

    tender_columns = [
        sa.Column("canonical_key", sa.String(64)),
        sa.Column("deadline_original", sa.String(255)),
        sa.Column("questions_deadline", sa.DateTime(timezone=True)),
        sa.Column("publication_date", sa.DateTime(timezone=True)),
        sa.Column("procedure_type", sa.String(255)),
        sa.Column("cpv_codes", jsonb, nullable=False, server_default="[]"),
        sa.Column("lots", jsonb, nullable=False, server_default="[]"),
        sa.Column("status", sa.String(30), nullable=False, server_default="open"),
        sa.Column("contract_period", jsonb, nullable=False, server_default="{}"),
        sa.Column("place_of_performance", jsonb, nullable=False, server_default="[]"),
        sa.Column("estimated_value", sa.Numeric(18, 2)),
        sa.Column("currency", sa.String(3)),
        sa.Column("mandatory_requirements", jsonb, nullable=False, server_default="[]"),
        sa.Column("award_criteria", jsonb, nullable=False, server_default="[]"),
        sa.Column("contact", jsonb, nullable=False, server_default="{}"),
        sa.Column("language", sa.String(10)),
        sa.Column("normalized_data", jsonb, nullable=False, server_default="{}"),
        sa.Column("latest_snapshot_id", uuid, sa.ForeignKey("source_snapshots.id", ondelete="SET NULL")),
    ]
    for column in tender_columns:
        op.add_column("tenders", column)
    op.create_index("ix_tenders_canonical_key", "tenders", ["canonical_key"])
    op.create_index("ix_tenders_status", "tenders", ["status"])
    op.create_index("ix_tenders_latest_snapshot_id", "tenders", ["latest_snapshot_id"])
    op.create_table(
        "tender_source_references",
        sa.Column("id", uuid, primary_key=True),
        sa.Column("organization_id", uuid, sa.ForeignKey("organizations.id", ondelete="CASCADE"), nullable=False),
        sa.Column("tender_id", uuid, sa.ForeignKey("tenders.id", ondelete="CASCADE"), nullable=False),
        sa.Column("source_id", uuid, sa.ForeignKey("sources.id", ondelete="CASCADE"), nullable=False),
        sa.Column("source_ref", sa.String(500), nullable=False),
        sa.Column("source_url", sa.String(1200)),
        sa.Column("latest_snapshot_id", uuid, sa.ForeignKey("source_snapshots.id", ondelete="SET NULL")),
        sa.Column("first_seen_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.Column("last_seen_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.UniqueConstraint("source_id", "source_ref", name="uq_tender_source_ref"),
    )
    for column in ("organization_id", "tender_id", "source_id"):
        op.create_index(f"ix_tender_source_references_{column}", "tender_source_references", [column])

    opportunity_columns = [
        sa.Column("profile_version", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("component_scores", jsonb, nullable=False, server_default="{}"),
        sa.Column("scoring_version", sa.String(50), nullable=False, server_default="mvp-1"),
        sa.Column("eligibility_state", eligibility_state, nullable=False, server_default="unknown"),
        sa.Column("hard_blockers", jsonb, nullable=False, server_default="[]"),
        sa.Column("missing_information", jsonb, nullable=False, server_default="[]"),
        sa.Column("match_reasons", jsonb, nullable=False, server_default="[]"),
        sa.Column("decision", decision_state, nullable=False, server_default="undecided"),
        sa.Column("owner_id", uuid, sa.ForeignKey("users.id", ondelete="SET NULL")),
        sa.Column("note", sa.Text()),
        sa.Column("reviewed_at", sa.DateTime(timezone=True)),
        sa.Column("is_new", sa.Boolean(), nullable=False, server_default=sa.true()),
        sa.Column("is_updated", sa.Boolean(), nullable=False, server_default=sa.false()),
        sa.Column("last_change_at", sa.DateTime(timezone=True)),
    ]
    for column in opportunity_columns:
        op.add_column("opportunities", column)
    op.create_index("ix_opportunities_decision", "opportunities", ["decision"])
    op.create_index("ix_opportunities_owner_id", "opportunities", ["owner_id"])

    op.create_table(
        "fact_evidence",
        sa.Column("id", uuid, primary_key=True),
        sa.Column("organization_id", uuid, sa.ForeignKey("organizations.id", ondelete="CASCADE"), nullable=False),
        sa.Column("opportunity_id", uuid, sa.ForeignKey("opportunities.id", ondelete="CASCADE"), nullable=False),
        sa.Column("source_snapshot_id", uuid, sa.ForeignKey("source_snapshots.id", ondelete="SET NULL")),
        sa.Column("document_id", uuid, sa.ForeignKey("documents.id", ondelete="SET NULL")),
        sa.Column("fact_key", sa.String(100), nullable=False),
        sa.Column("source_url", sa.String(1200)),
        sa.Column("page", sa.Integer()),
        sa.Column("sheet", sa.String(255)),
        sa.Column("section", sa.String(500)),
        sa.Column("excerpt", sa.Text()),
        sa.Column("extraction_method", sa.String(30), nullable=False, server_default="source_field"),
        sa.Column("validation_state", sa.String(30), nullable=False, server_default="review_required"),
        sa.Column("validation_score", sa.Float(), nullable=False, server_default="0"),
        sa.Column("content_hash", sa.String(64)),
        sa.Column("first_seen_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.Column("last_seen_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.Column("confirmed_by", uuid, sa.ForeignKey("users.id", ondelete="SET NULL")),
    )
    for column in ("organization_id", "opportunity_id", "source_snapshot_id", "document_id", "fact_key"):
        op.create_index(f"ix_fact_evidence_{column}", "fact_evidence", [column])
    op.create_table(
        "opportunity_decisions",
        sa.Column("id", uuid, primary_key=True),
        sa.Column("organization_id", uuid, sa.ForeignKey("organizations.id", ondelete="CASCADE"), nullable=False),
        sa.Column("opportunity_id", uuid, sa.ForeignKey("opportunities.id", ondelete="CASCADE"), nullable=False),
        sa.Column("decision", decision_state, nullable=False),
        sa.Column("owner_id", uuid, sa.ForeignKey("users.id", ondelete="SET NULL")),
        sa.Column("note", sa.Text()),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
    )
    op.create_index("ix_opportunity_decisions_organization_id", "opportunity_decisions", ["organization_id"])
    op.create_index("ix_opportunity_decisions_opportunity_id", "opportunity_decisions", ["opportunity_id"])
    op.create_table(
        "opportunity_events",
        sa.Column("id", uuid, primary_key=True),
        sa.Column("organization_id", uuid, sa.ForeignKey("organizations.id", ondelete="CASCADE"), nullable=False),
        sa.Column("opportunity_id", uuid, sa.ForeignKey("opportunities.id", ondelete="CASCADE"), nullable=False),
        sa.Column("event_type", sa.String(80), nullable=False),
        sa.Column("old_value", jsonb, nullable=False, server_default="{}"),
        sa.Column("new_value", jsonb, nullable=False, server_default="{}"),
        sa.Column("materiality", sa.String(30), nullable=False, server_default="material"),
        sa.Column("source_snapshot_id", uuid, sa.ForeignKey("source_snapshots.id", ondelete="SET NULL")),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
    )
    for column in ("organization_id", "opportunity_id", "event_type", "created_at"):
        op.create_index(f"ix_opportunity_events_{column}", "opportunity_events", [column])
    op.create_table(
        "analysis_runs",
        sa.Column("id", uuid, primary_key=True),
        sa.Column("organization_id", uuid, sa.ForeignKey("organizations.id", ondelete="CASCADE"), nullable=False),
        sa.Column("opportunity_id", uuid, sa.ForeignKey("opportunities.id", ondelete="CASCADE"), nullable=False),
        sa.Column("snapshot_id", uuid, sa.ForeignKey("source_snapshots.id", ondelete="SET NULL")),
        sa.Column("status", sa.String(30), nullable=False, server_default="complete"),
        sa.Column("parser_version", sa.String(50), nullable=False),
        sa.Column("prompt_version", sa.String(50), nullable=False),
        sa.Column("model_version", sa.String(150), nullable=False),
        sa.Column("scoring_version", sa.String(50), nullable=False),
        sa.Column("completed_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
    )
    op.create_index("ix_analysis_runs_organization_id", "analysis_runs", ["organization_id"])
    op.create_index("ix_analysis_runs_opportunity_id", "analysis_runs", ["opportunity_id"])
    op.create_table(
        "opportunity_drafts",
        sa.Column("id", uuid, primary_key=True),
        sa.Column("organization_id", uuid, sa.ForeignKey("organizations.id", ondelete="CASCADE"), nullable=False),
        sa.Column("opportunity_id", uuid, sa.ForeignKey("opportunities.id", ondelete="CASCADE"), nullable=False),
        sa.Column("profile_version", sa.Integer(), nullable=False),
        sa.Column("analysis_run_id", uuid, sa.ForeignKey("analysis_runs.id", ondelete="SET NULL")),
        sa.Column("content", jsonb, nullable=False),
        sa.Column("model_version", sa.String(150), nullable=False, server_default="deterministic-mvp-1"),
        sa.Column("created_by", uuid, sa.ForeignKey("users.id", ondelete="SET NULL")),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
    )
    op.create_index("ix_opportunity_drafts_organization_id", "opportunity_drafts", ["organization_id"])
    op.create_index("ix_opportunity_drafts_opportunity_id", "opportunity_drafts", ["opportunity_id"])
    op.create_table(
        "match_alerts",
        sa.Column("id", uuid, primary_key=True),
        sa.Column("organization_id", uuid, sa.ForeignKey("organizations.id", ondelete="CASCADE"), nullable=False),
        sa.Column("opportunity_id", uuid, sa.ForeignKey("opportunities.id", ondelete="CASCADE")),
        sa.Column("alert_type", sa.String(50), nullable=False),
        sa.Column("title", sa.String(300), nullable=False),
        sa.Column("message", sa.Text(), nullable=False),
        sa.Column("payload", jsonb, nullable=False, server_default="{}"),
        sa.Column("idempotency_key", sa.String(255), nullable=False, unique=True),
        sa.Column("email_status", sa.String(30), nullable=False, server_default="not_configured"),
        sa.Column("read_at", sa.DateTime(timezone=True)),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
    )
    for column in ("organization_id", "opportunity_id", "alert_type", "idempotency_key", "created_at"):
        op.create_index(f"ix_match_alerts_{column}", "match_alerts", [column])
    op.create_table(
        "alert_preferences",
        sa.Column("id", uuid, primary_key=True),
        sa.Column("organization_id", uuid, sa.ForeignKey("organizations.id", ondelete="CASCADE"), nullable=False, unique=True),
        sa.Column("match_threshold", sa.Integer(), nullable=False, server_default="80"),
        sa.Column("deadline_days", jsonb, nullable=False, server_default="[30,14,7,3,1]"),
        sa.Column("email_enabled", sa.Boolean(), nullable=False, server_default=sa.true()),
        sa.Column("material_updates_enabled", sa.Boolean(), nullable=False, server_default=sa.true()),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
    )
    op.create_index("ix_alert_preferences_organization_id", "alert_preferences", ["organization_id"])
    op.create_table(
        "audit_events",
        sa.Column("id", uuid, primary_key=True),
        sa.Column("organization_id", uuid, sa.ForeignKey("organizations.id", ondelete="CASCADE"), nullable=False),
        sa.Column("action", sa.String(100), nullable=False),
        sa.Column("entity_type", sa.String(80), nullable=False),
        sa.Column("entity_id", uuid),
        sa.Column("metadata_json", jsonb, nullable=False, server_default="{}"),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
    )
    for column in ("organization_id", "action", "entity_type", "entity_id", "created_at"):
        op.create_index(f"ix_audit_events_{column}", "audit_events", [column])


def downgrade() -> None:
    for table in (
        "audit_events", "alert_preferences", "match_alerts", "opportunity_drafts", "analysis_runs",
        "opportunity_events", "opportunity_decisions", "fact_evidence", "tender_source_references",
    ):
        op.drop_table(table)
    for column in (
        "last_change_at", "is_updated", "is_new", "reviewed_at", "note", "owner_id", "decision",
        "match_reasons", "missing_information", "hard_blockers", "eligibility_state", "scoring_version",
        "component_scores", "profile_version",
    ):
        op.drop_column("opportunities", column)
    for column in (
        "latest_snapshot_id", "normalized_data", "language", "contact", "award_criteria", "mandatory_requirements",
        "currency", "estimated_value", "place_of_performance", "contract_period", "status", "lots", "cpv_codes",
        "procedure_type", "publication_date", "questions_deadline", "deadline_original", "canonical_key",
    ):
        op.drop_column("tenders", column)
    for column in ("ocr_required", "sheet_count", "page_count", "parse_status", "source_url", "source_snapshot_id"):
        op.drop_column("documents", column)
    op.drop_table("source_snapshots")
    for column in ("last_error", "last_success_at", "last_attempt_at", "rate_limit_config", "schedule_minutes", "access_policy", "adapter_type"):
        op.drop_column("sources", column)
    op.drop_table("profile_evidence")
    op.drop_table("company_profiles")
    op.execute("DROP TYPE IF EXISTS eligibility_state")
    op.execute("DROP TYPE IF EXISTS decision_state")
