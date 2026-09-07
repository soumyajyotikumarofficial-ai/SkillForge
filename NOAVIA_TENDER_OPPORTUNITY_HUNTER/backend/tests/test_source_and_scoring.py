from app.processing import score_opportunity, score_profile_match, validate_document_type
from app.source import TenderCandidate, filter_candidates, parse_feed


def test_parse_and_dedupe_ocds():
    payload = b'{"releases":[{"ocid":"ocds-1","tender":{"title":"Cloud software support","description":"Digital service"}},{"ocid":"ocds-1","tender":{"title":"duplicate"}}]}'
    candidates = filter_candidates(parse_feed(payload, "application/json"), ["cloud"])
    assert len(candidates) == 1
    assert candidates[0].external_id == "ocds-1"


def test_score_is_deterministic_and_bounded():
    tender = {"title": "Cloud software platform", "description": "Digital data support", "document_urls": ["https://example.test/a"]}
    first = score_opportunity(tender)
    assert first == score_opportunity(tender)
    assert 0 <= first[0] <= 100
    assert first[1] == "strong_match"


def test_rss_preserves_explicit_deadline_and_attachment():
    payload = b'''<rss><channel><item><guid>notice-1</guid><title>Cloud platform</title>
        <description>Submission deadline: 2026-10-15T12:00:00+02:00</description>
        <link>https://example.test/notice</link>
        <enclosure url="https://example.test/spec.pdf" type="application/pdf" />
    </item></channel></rss>'''
    candidate = parse_feed(payload, "application/rss+xml")[0]
    assert candidate.deadline.isoformat() == "2026-10-15T12:00:00+02:00"
    assert candidate.deadline_original == "2026-10-15T12:00:00+02:00"
    assert candidate.document_urls == ["https://example.test/spec.pdf"]


def test_profile_scoring_keeps_unconfirmed_requirement_in_review():
    result = score_profile_match(
        {
            "title": "Cloud software platform",
            "description": "Data automation for a municipal utility",
            "status": "open",
            "mandatory_requirements": [{"text": "ISO 27001 certification is mandatory"}],
        },
        {
            "offerings": ["cloud software", "data automation"],
            "sectors": ["municipal utility"],
            "certifications": [],
            "geographies": ["Germany"],
            "strategic_priorities": [],
        },
    )
    assert result["eligibility_state"] == "review"
    assert result["missing_information"] == ["ISO 27001 certification is mandatory"]
    assert result["recommendation"] != "closed_skip"


def test_explicit_exclusion_is_a_hard_blocker():
    result = score_profile_match(
        {"title": "Hardware-only supply", "description": "Servers", "status": "open"},
        {"offerings": ["servers"], "exclusions": ["hardware-only"]},
    )
    assert result["eligibility_state"] == "not_met"
    assert result["score"] <= 39
    assert result["hard_blockers"]


def test_remote_document_type_restrictions():
    validate_document_type("requirements.pdf", "application/pdf")
    try:
        validate_document_type("payload.exe", "application/x-msdownload")
    except ValueError as exc:
        assert "unsupported" in str(exc)
    else:
        raise AssertionError("Executable attachment should be rejected")


def test_short_keyword_does_not_match_inside_unrelated_word():
    candidates = [
        TenderCandidate(external_id="1", title="Gehweg am Mühlenberg in Zielitz"),
        TenderCandidate(external_id="2", title="IT consulting services"),
    ]
    result = filter_candidates(candidates, ["IT"])
    assert [item.external_id for item in result] == ["2"]
