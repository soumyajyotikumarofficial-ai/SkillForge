from __future__ import annotations

import smtplib
from datetime import datetime, timezone
from email.message import EmailMessage

from sqlalchemy import select
from sqlalchemy.orm import Session

from .config import get_settings
from .models import AlertPreference, AuditEvent, CompanyProfile, DecisionState, MatchAlert, Opportunity, Tender


def create_due_deadline_alerts(db: Session) -> int:
    now = datetime.now(timezone.utc)
    opportunities = db.scalars(
        select(Opportunity).join(Tender).where(
            Opportunity.decision == DecisionState.shortlisted,
            Tender.deadline.is_not(None),
            Tender.status.in_(["open", "updated"]),
        )
    ).all()
    created = 0
    for opportunity in opportunities:
        deadline = opportunity.tender.deadline
        if deadline is None or deadline <= now:
            continue
        preference = db.scalar(select(AlertPreference).where(AlertPreference.organization_id == opportunity.organization_id))
        reminder_days = preference.deadline_days if preference else [30, 14, 7, 3, 1]
        days_left = (deadline.date() - now.date()).days
        if days_left not in reminder_days:
            continue
        key = f"{opportunity.organization_id}:{opportunity.id}:deadline:{deadline.isoformat()}:{days_left}"
        if db.scalar(select(MatchAlert).where(MatchAlert.idempotency_key == key)):
            continue
        db.add(MatchAlert(
            organization_id=opportunity.organization_id, opportunity_id=opportunity.id,
            alert_type="deadline", title=f"Deadline in {days_left} day(s): {opportunity.tender.title}",
            message=f"Submission deadline: {deadline.isoformat()}. Review the opportunity and source evidence before bidding.",
            payload={"deadline": deadline.isoformat(), "days_left": days_left}, idempotency_key=key,
        ))
        db.add(AuditEvent(
            organization_id=opportunity.organization_id, action="deadline_alert.created",
            entity_type="opportunity", entity_id=opportunity.id, metadata_json={"days_left": days_left},
        ))
        created += 1
    db.commit()
    return created


def deliver_pending_alerts(db: Session) -> int:
    """Deliver unsent alerts without logging tender or company document content."""
    settings = get_settings()
    if not settings.smtp_host:
        return 0
    alerts = db.scalars(
        select(MatchAlert)
        .where(MatchAlert.email_status.in_(["not_configured", "retry"]))
        .order_by(MatchAlert.created_at)
        .limit(50)
    ).all()
    sent = 0
    for alert in alerts:
        profile = db.scalar(select(CompanyProfile).where(CompanyProfile.organization_id == alert.organization_id))
        preference = db.scalar(select(AlertPreference).where(AlertPreference.organization_id == alert.organization_id))
        recipients = profile.alert_emails if profile else []
        if not recipients or (preference and not preference.email_enabled):
            alert.email_status = "not_configured"
            continue
        message = EmailMessage()
        message["From"] = settings.smtp_from
        message["To"] = ", ".join(recipients)
        message["Subject"] = alert.title
        message.set_content(alert.message)
        try:
            with smtplib.SMTP(settings.smtp_host, settings.smtp_port, timeout=10) as client:
                client.send_message(message)
            alert.email_status = "sent"
            db.add(AuditEvent(
                organization_id=alert.organization_id, action="alert.email_sent",
                entity_type="match_alert", entity_id=alert.id, metadata_json={"recipient_count": len(recipients)},
            ))
            sent += 1
        except (OSError, smtplib.SMTPException):
            alert.email_status = "retry"
    db.commit()
    return sent
