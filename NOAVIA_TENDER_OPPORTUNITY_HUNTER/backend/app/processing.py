"""Safe document processing and deterministic opportunity scoring."""
from __future__ import annotations

import re
import socket
from dataclasses import dataclass
from datetime import datetime, timezone
from io import BytesIO
from typing import Any

from .config import get_settings
from .openai_client import OpenAIResponsesClient


PARSER_VERSION = "mvp-2"
SCORING_VERSION = "mvp-profile-1"


class MalwareScannerUnavailable(RuntimeError):
    pass


@dataclass(frozen=True)
class ParsedDocument:
    text: str
    page_count: int | None = None
    sheet_count: int | None = None
    ocr_required: bool = False


def clamav_scan(content: bytes, host: str | None = None, port: int | None = None) -> bool:
    """Return False for malware and fail closed when the scanner is unavailable."""
    settings = get_settings()
    try:
        with socket.create_connection((host or settings.clamav_host, port or settings.clamav_port), timeout=3) as sock:
            sock.sendall(b"zINSTREAM\0")
            for offset in range(0, len(content), 8192):
                chunk = content[offset:offset + 8192]
                sock.sendall(len(chunk).to_bytes(4, "big") + chunk)
            sock.sendall((0).to_bytes(4, "big"))
            response = sock.recv(4096).decode("utf-8", "replace")
            return "FOUND" not in response.upper()
    except (OSError, socket.timeout):
        if settings.clamav_fail_open and settings.app_env == "development":
            return True
        raise MalwareScannerUnavailable("ClamAV is unavailable; attachment was not trusted")


def validate_document_type(filename: str, content_type: str | None) -> None:
    allowed_extensions = (".pdf", ".docx", ".xlsx", ".xls", ".txt", ".csv")
    allowed_types = {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-excel",
        "text/plain",
        "text/csv",
        "application/octet-stream",
    }
    lowered = filename.casefold().split("?", 1)[0]
    if not lowered.endswith(allowed_extensions) and (content_type or "").split(";", 1)[0].casefold() not in allowed_types:
        raise ValueError("unsupported attachment type")


def parse_document_with_metadata(content: bytes, filename: str, content_type: str | None = None) -> ParsedDocument:
    name = filename.casefold()
    try:
        if name.endswith(".pdf") or content_type == "application/pdf":
            from pypdf import PdfReader
            reader = PdfReader(BytesIO(content))
            pages = [page.extract_text() or "" for page in reader.pages]
            text = "\n".join(f"[page {index}]\n{page}" for index, page in enumerate(pages, start=1))
            return ParsedDocument(text=text, page_count=len(pages), ocr_required=bool(pages) and not any(page.strip() for page in pages))
        if name.endswith(".docx") or "wordprocessingml" in (content_type or ""):
            from docx import Document
            doc = Document(BytesIO(content))
            return ParsedDocument(text="\n".join(p.text for p in doc.paragraphs), page_count=None)
        if name.endswith((".xlsx", ".xls")) or "spreadsheet" in (content_type or ""):
            from openpyxl import load_workbook
            book = load_workbook(BytesIO(content), read_only=True, data_only=True)
            sections = []
            for sheet in book:
                rows = [" ".join(str(cell.value or "") for cell in row) for row in sheet.iter_rows()]
                sections.append(f"[sheet {sheet.title}]\n" + "\n".join(rows))
            return ParsedDocument(text="\n".join(sections), sheet_count=len(book.sheetnames))
    except (ImportError, ValueError, OSError):
        return ParsedDocument(text="", ocr_required=name.endswith(".pdf"))
    return ParsedDocument(text=content.decode("utf-8", "replace"))


def parse_document(content: bytes, filename: str, content_type: str | None = None) -> str:
    return parse_document_with_metadata(content, filename, content_type).text


def deterministic_extract(text: str, fallback: dict[str, Any] | None = None) -> dict[str, Any]:
    fallback = fallback or {}
    deadline = fallback.get("deadline")
    if not deadline:
        match = re.search(
            r"(?:submission\s+deadline|angebotsfrist|teilnahmefrist|schlusstermin)\s*[:\-]?\s*"
            r"(\d{1,2}[./-]\d{1,2}[./-]\d{2,4}|\d{4}-\d{2}-\d{2})",
            text,
            re.IGNORECASE,
        )
        deadline = match.group(1) if match else None
    requirement_pattern = re.compile(
        r"[^\n.!?]*(?:\bmuss\b|\bzwingend\b|\bmandatory\b|\brequired\b|\bNachweis\b)[^\n.!?]*[.!?]?",
        re.IGNORECASE,
    )
    requirements = []
    for match in requirement_pattern.finditer(text):
        requirement = re.sub(r"\s+", " ", match.group(0)).strip()
        if len(requirement) >= 12:
            prefix = text[:match.start()]
            page_matches = list(re.finditer(r"\[page\s+(\d+)\]", prefix, re.IGNORECASE))
            sheet_matches = list(re.finditer(r"\[sheet\s+([^\]]+)\]", prefix, re.IGNORECASE))
            requirements.append({
                "text": requirement[:1000],
                "category": "mandatory",
                "criticality": "review",
                "validation_state": "review_required",
                "excerpt": requirement[:500],
                "page": int(page_matches[-1].group(1)) if page_matches else None,
                "sheet": sheet_matches[-1].group(1) if sheet_matches else None,
            })
        if len(requirements) >= 25:
            break
    return {
        "title": fallback.get("title"),
        "buyer": fallback.get("buyer"),
        "deadline": deadline,
        "summary": re.sub(r"\s+", " ", text).strip()[:500] or fallback.get("description"),
        "mandatory_requirements": requirements,
        "award_criteria": fallback.get("award_criteria") or [],
    }


def analyze_text(text: str, fallback: dict[str, Any] | None = None) -> dict[str, Any]:
    """Use strict OpenRouter extraction when configured, otherwise local extraction."""
    local = deterministic_extract(text, fallback)
    try:
        result = OpenAIResponsesClient().extract_tender(text[:12000])
        return {**local, **{k: v for k, v in result.items() if v is not None}}
    except Exception:
        return local


def score_opportunity(tender: dict[str, Any], extracted: dict[str, Any] | None = None) -> tuple[float, str, str]:
    """Stable 0-100 score; no model output can influence the numeric ranking."""
    extracted = extracted or {}
    text = " ".join(str(tender.get(k) or "") for k in ("title", "description", "buyer")) + " " + str(extracted.get("summary") or "")
    text = text.casefold()
    score = 35.0
    weights = {"software": 18, "digital": 15, "cloud": 12, "data": 10, "consult": 8, "technology": 12, "support": 5}
    score += sum(weight for term, weight in weights.items() if term in text)
    deadline = tender.get("deadline") or extracted.get("deadline")
    if isinstance(deadline, datetime):
        remaining = (deadline - datetime.now(timezone.utc)).days
        if 0 <= remaining <= 30:
            score += 8
        elif remaining < 0:
            score -= 25
    if tender.get("document_urls"):
        score += 4
    score = max(0.0, min(100.0, score))
    recommendation = "strong_match" if score >= 75 else "review" if score >= 50 else "low_match"
    rationale = f"Deterministic score from procurement relevance ({score:.0f}/100); review source facts before bidding."
    return score, recommendation, rationale


def score_profile_match(tender: dict[str, Any], profile: dict[str, Any] | None, extracted: dict[str, Any] | None = None) -> dict[str, Any]:
    """Transparent, reproducible profile score using the weights in the MVP spec."""
    profile = profile or {}
    extracted = extracted or {}
    tender_text = " ".join(str(tender.get(key) or "") for key in ("title", "description", "buyer", "procedure_type")).casefold()
    tender_text += " " + " ".join(str(value) for value in tender.get("cpv_codes", []))

    def overlap(values: list[Any]) -> tuple[float, list[str]]:
        confirmed = [str(value).strip() for value in values if str(value).strip()]
        def present(value: str) -> bool:
            normalized = value.casefold()
            if len(normalized) <= 3 or normalized.isdigit():
                return re.search(rf"(?<!\w){re.escape(normalized)}(?!\w)", tender_text) is not None
            return normalized in tender_text
        matches = [value for value in confirmed if present(value)]
        return (min(1.0, len(matches) / max(1, min(3, len(confirmed)))) if confirmed else 0.5), matches

    offerings = list(profile.get("offerings", [])) + list(profile.get("keywords", [])) + list(profile.get("cpv_codes", []))
    scope_fit, scope_matches = overlap(offerings)
    sector_fit, sector_matches = overlap(list(profile.get("sectors", [])) + list(profile.get("use_cases", [])))
    geo_values = list(profile.get("geographies", []))
    geography_fit, geography_matches = overlap(geo_values)
    if not tender.get("place_of_performance"):
        geography_fit = 0.5

    requirements = tender.get("mandatory_requirements") or extracted.get("mandatory_requirements") or []
    confirmed_facts = " ".join(str(value) for value in profile.get("certifications", []) + profile.get("offerings", [])).casefold()
    missing_information = []
    explicit_matches = []
    for requirement in requirements:
        text = str(requirement.get("text") if isinstance(requirement, dict) else requirement)
        tokens = [token for token in re.findall(r"[a-zA-ZÄÖÜäöüß0-9-]{4,}", text.casefold()) if token not in {"muss", "sind", "werden", "required", "mandatory"}]
        if tokens and any(token in confirmed_facts for token in tokens):
            explicit_matches.append(text)
        else:
            missing_information.append(text)
    eligibility_fit = 1.0 if requirements and len(explicit_matches) == len(requirements) else 0.5 if requirements else 0.5

    exclusions = [str(value).strip() for value in profile.get("exclusions", []) if str(value).strip()]
    blockers = [f"Explicit profile exclusion matched: {value}" for value in exclusions if value.casefold() in tender_text]
    strategic_fit, strategic_matches = overlap(list(profile.get("strategic_priorities", [])))

    value_pref = profile.get("value_preferences") or {}
    estimated = tender.get("estimated_value")
    value_fit = 0.5
    if estimated is not None:
        minimum, maximum = value_pref.get("min"), value_pref.get("max")
        if minimum is not None and float(estimated) < float(minimum):
            value_fit = 0.0
        elif maximum is not None and float(estimated) > float(maximum):
            value_fit = 0.0
        else:
            value_fit = 1.0
    geo_value_fit = (geography_fit + value_fit) / 2
    components = {
        "scope_fit": round(scope_fit, 4),
        "sector_fit": round(sector_fit, 4),
        "eligibility_fit": round(eligibility_fit, 4),
        "geo_value_fit": round(geo_value_fit, 4),
        "strategic_fit": round(strategic_fit, 4),
    }
    score = 100 * (scope_fit * 0.35 + sector_fit * 0.20 + eligibility_fit * 0.20 + geo_value_fit * 0.15 + strategic_fit * 0.10)
    deadline = tender.get("deadline")
    status = str(tender.get("status") or "open")
    if isinstance(deadline, datetime) and deadline < datetime.now(timezone.utc):
        status = "closed"
        blockers.append("Submission deadline has passed")
    if status in {"cancelled", "awarded", "closed"}:
        blockers.append(f"Tender status is {status}")
    if blockers:
        score = min(score, 39)

    eligibility_state = "review" if missing_information else "met"
    if blockers:
        eligibility_state = "not_met"
    recommendation = "hot" if score >= 80 and not blockers else "review" if score >= 60 or missing_information else "watch" if score >= 40 else "closed_skip"
    reasons = [f"Confirmed profile match: {value}" for value in (scope_matches + sector_matches + geography_matches + strategic_matches)[:4]]
    if not reasons:
        reasons = ["Insufficient confirmed profile overlap; human review required"]
    return {
        "score": round(max(0.0, min(100.0, score)), 2),
        "recommendation": recommendation,
        "component_scores": components,
        "scoring_version": SCORING_VERSION,
        "eligibility_state": eligibility_state,
        "hard_blockers": blockers,
        "missing_information": missing_information[:25],
        "match_reasons": reasons,
        "rationale": f"Profile-weighted deterministic score ({score:.0f}/100) using {SCORING_VERSION}.",
    }
