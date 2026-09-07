from types import SimpleNamespace

from app.main import _grounded_draft


def test_response_starter_uses_todos_for_unconfirmed_company_claims():
    tender = SimpleNamespace(
        title="Synthetic AI tender",
        mandatory_requirements=[{"text": "ISO 27001 certification is mandatory"}],
        source_references=[SimpleNamespace(source_url="https://example.test/tender")],
    )
    opportunity = SimpleNamespace(tender=tender)
    profile = SimpleNamespace(
        company_name="Noavia", certifications=[], offerings=["AI automation"],
    )
    draft = _grounded_draft(opportunity, profile)
    assert draft["compliance_matrix"][0]["status"] == "TODO"
    assert draft["compliance_matrix"][0]["company_evidence"] is None
    assert draft["clarification_questions"][0].startswith("TODO:")


def test_response_starter_can_reference_exact_confirmed_certification():
    tender = SimpleNamespace(
        title="Synthetic AI tender",
        mandatory_requirements=[{"text": "ISO 27001 certification is mandatory"}],
        source_references=[],
    )
    opportunity = SimpleNamespace(tender=tender)
    profile = SimpleNamespace(company_name="Noavia", certifications=["ISO 27001"], offerings=[])
    draft = _grounded_draft(opportunity, profile)
    assert draft["compliance_matrix"][0]["status"] == "CONFIRMED"
    assert draft["compliance_matrix"][0]["company_evidence"] == "ISO 27001"
