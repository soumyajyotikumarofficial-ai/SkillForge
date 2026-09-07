"""No-credential procurement feed ingestion.

The default is the German service.bund.de RSS endpoint.  The parser also accepts
RSS/Atom and a small generic JSON shape, making demos easy to point at a
fixture or another public feed with ``SOURCE_URL``.
"""
from __future__ import annotations

import json
import re
from email.utils import parsedate_to_datetime
from html import unescape
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from typing import Any

DEFAULT_SOURCE_URL = "https://www.service.bund.de/Content/Globals/Functions/RSSFeed/RSSGenerator_Ausschreibungen.xml"


@dataclass
class TenderCandidate:
    external_id: str
    title: str
    buyer: str | None = None
    description: str | None = None
    deadline: datetime | None = None
    deadline_original: str | None = None
    questions_deadline: datetime | None = None
    publication_date: datetime | None = None
    procedure_type: str | None = None
    cpv_codes: list[str] = field(default_factory=list)
    lots: list[dict[str, Any]] = field(default_factory=list)
    status: str = "open"
    contract_period: dict[str, Any] = field(default_factory=dict)
    place_of_performance: list[str] = field(default_factory=list)
    estimated_value: float | None = None
    currency: str | None = None
    mandatory_requirements: list[dict[str, Any]] = field(default_factory=list)
    award_criteria: list[dict[str, Any]] = field(default_factory=list)
    contact: dict[str, Any] = field(default_factory=dict)
    language: str | None = None
    notice_url: str | None = None
    document_urls: list[str] = field(default_factory=list)
    raw_data: dict[str, Any] = field(default_factory=dict)


def _date(value: Any) -> datetime | None:
    if not value:
        return None
    try:
        text = str(value).replace("Z", "+00:00")
        result = datetime.fromisoformat(text)
        return result if result.tzinfo else result.replace(tzinfo=timezone.utc)
    except (TypeError, ValueError):
        try:
            parsed = parsedate_to_datetime(str(value))
            return parsed if parsed.tzinfo else parsed.replace(tzinfo=timezone.utc)
        except (TypeError, ValueError, OverflowError):
            return None


def _text(value: Any) -> str | None:
    if isinstance(value, dict):
        value = value.get("en") or next(iter(value.values()), None)
    if value is None:
        return None
    return re.sub(r"\s+", " ", unescape(re.sub(r"<[^>]+>", " ", str(value)))).strip() or None


def _deadline_from_text(value: str | None) -> tuple[datetime | None, str | None]:
    """Extract only a date explicitly labelled as a submission deadline."""
    if not value:
        return None, None
    pattern = re.compile(
        r"(?:angebotsfrist|submission\s+deadline|teilnahmefrist|schluss(?:termin)?|frist)\s*[:\-]?\s*"
        r"(\d{1,2}[./-]\d{1,2}[./-]\d{2,4}(?:\s+\d{1,2}:\d{2})?|\d{4}-\d{2}-\d{2}(?:[T ]\d{1,2}:\d{2}(?::\d{2})?(?:Z|[+-]\d{2}:?\d{2})?)?)",
        re.IGNORECASE,
    )
    match = pattern.search(value)
    if not match:
        return None, None
    original = match.group(1)
    parsed = _date(original)
    if parsed:
        return parsed, original
    for fmt in ("%d.%m.%Y %H:%M", "%d.%m.%Y", "%d/%m/%Y", "%d-%m-%Y"):
        try:
            return datetime.strptime(original, fmt).replace(tzinfo=timezone.utc), original
        except ValueError:
            continue
    return None, original


def _status(value: Any) -> str:
    text = str(value or "open").casefold()
    if any(word in text for word in ("cancel", "aufgehoben", "zurückgezogen")):
        return "cancelled"
    if any(word in text for word in ("award", "vergeben", "bezuschlagt")):
        return "awarded"
    if any(word in text for word in ("closed", "geschlossen", "expired")):
        return "closed"
    if any(word in text for word in ("update", "amend", "korrig")):
        return "updated"
    return "open"


def _list_text(value: Any) -> list[str]:
    if value is None:
        return []
    if isinstance(value, list):
        result = []
        for item in value:
            if isinstance(item, dict):
                item = item.get("code") or item.get("description") or item.get("name") or item.get("id")
            text = _text(item)
            if text:
                result.append(text)
        return result
    text = _text(value)
    return [text] if text else []


def parse_feed(payload: bytes | str, content_type: str = "") -> list[TenderCandidate]:
    """Parse OCDS JSON, generic JSON arrays, RSS, or Atom into candidates."""
    text = payload.decode("utf-8", "replace") if isinstance(payload, bytes) else payload
    if "json" in content_type.lower() or text.lstrip().startswith(("{", "[")):
        return _parse_json(json.loads(text))
    root = ET.fromstring(text)
    return _parse_xml(root)


def _parse_json(data: Any) -> list[TenderCandidate]:
    if isinstance(data, dict) and "releases" in data:
        records = data["releases"]
    elif isinstance(data, dict) and "results" in data:
        records = data["results"]
    elif isinstance(data, dict) and "packages" in data:
        records = [r for p in data["packages"] for r in p.get("releases", [])]
    elif isinstance(data, list):
        records = data
    else:
        records = [data] if isinstance(data, dict) else []
    candidates: list[TenderCandidate] = []
    for item in records:
        if not isinstance(item, dict):
            continue
        # OCDS puts procurement facts under tender and buyer under parties.
        tender = item.get("tender") or item
        buyer = item.get("buyer") or {}
        buyer_name = _text(buyer.get("name")) if isinstance(buyer, dict) else _text(buyer)
        if not buyer_name:
            for party in item.get("parties", []):
                if "buyer" in party.get("roles", []):
                    buyer_name = _text(party.get("name"))
                    break
        ext = item.get("ocid") or item.get("id") or tender.get("id") or tender.get("noticeId")
        title = _text(tender.get("title") or item.get("name"))
        if not ext or not title:
            continue
        docs = tender.get("documents") or item.get("documents") or []
        urls = [str(d.get("url")) for d in docs if isinstance(d, dict) and d.get("url")]
        period = tender.get("tenderPeriod") if isinstance(tender.get("tenderPeriod"), dict) else {}
        deadline_raw = period.get("end") or tender.get("deadline") or item.get("submission_deadline")
        value = tender.get("value") if isinstance(tender.get("value"), dict) else {}
        items = tender.get("items") if isinstance(tender.get("items"), list) else []
        classifications = [entry.get("classification", {}) for entry in items if isinstance(entry, dict)]
        cpv_codes = _list_text(tender.get("cpv_codes") or [entry.get("id") for entry in classifications if entry.get("id")])
        lots = tender.get("lots") if isinstance(tender.get("lots"), list) else []
        criteria = tender.get("awardCriteriaDetails") or tender.get("awardCriteria") or item.get("award_criteria") or []
        if not isinstance(criteria, list):
            criteria = [{"text": _text(criteria)}] if _text(criteria) else []
        requirements = tender.get("mandatoryRequirements") or item.get("mandatory_requirements") or []
        if not isinstance(requirements, list):
            requirements = [{"text": _text(requirements)}] if _text(requirements) else []
        candidates.append(TenderCandidate(
            external_id=str(ext), title=title, buyer=buyer_name,
            description=_text(tender.get("description") or item.get("description")),
            deadline=_date(deadline_raw), deadline_original=str(deadline_raw) if deadline_raw else None,
            questions_deadline=_date(tender.get("questionsDeadline") or item.get("questions_deadline")),
            publication_date=_date(item.get("date") or item.get("published") or item.get("publication_date")),
            procedure_type=_text(tender.get("procurementMethodDetails") or tender.get("procurementMethod")),
            cpv_codes=cpv_codes, lots=lots, status=_status(tender.get("status") or item.get("status")),
            contract_period=tender.get("contractPeriod") if isinstance(tender.get("contractPeriod"), dict) else {},
            place_of_performance=_list_text(tender.get("deliveryLocations") or item.get("place_of_performance")),
            estimated_value=value.get("amount") if isinstance(value.get("amount"), (int, float)) else None,
            currency=_text(value.get("currency")), mandatory_requirements=requirements, award_criteria=criteria,
            language=_text(item.get("language") or tender.get("language")),
            notice_url=_text(item.get("url") or tender.get("url")),
            document_urls=urls, raw_data=item,
        ))
    return candidates


def _parse_xml(root: ET.Element) -> list[TenderCandidate]:
    candidates = []
    for node in root.iter():
        if node.tag.rsplit("}", 1)[-1].lower() not in {"item", "entry"}:
            continue
        values: dict[str, str] = {}
        document_urls: list[str] = []
        for child in node:
            key = child.tag.rsplit("}", 1)[-1].lower()
            values[key] = (child.text or "").strip()
            if key == "link" and child.attrib.get("href"):
                values[key] = child.attrib["href"]
                if child.attrib.get("rel") == "enclosure":
                    document_urls.append(child.attrib["href"])
            if key == "enclosure" and child.attrib.get("url"):
                document_urls.append(child.attrib["url"])
        ext = values.get("guid") or values.get("id") or values.get("link")
        title = _text(values.get("title"))
        if ext and title:
            description = _text(values.get("description") or values.get("summary"))
            deadline, deadline_original = _deadline_from_text(description)
            candidates.append(TenderCandidate(
                external_id=ext, title=title, buyer=_text(values.get("author") or values.get("creator")),
                description=description,
                deadline=_date(values.get("deadline") or values.get("closingdate")) or deadline,
                deadline_original=values.get("deadline") or values.get("closingdate") or deadline_original,
                publication_date=_date(values.get("pubdate") or values.get("published") or values.get("updated")),
                status=_status(values.get("status") or title),
                notice_url=values.get("link"), document_urls=document_urls, raw_data=values,
            ))
    return candidates


def fetch_candidates(url: str = DEFAULT_SOURCE_URL, *, timeout: int = 30, days: int = 30) -> list[TenderCandidate]:
    """Fetch a feed. FTS requires a bounded date window; custom URLs are untouched."""
    parsed = urllib.parse.urlsplit(url)
    if parsed.netloc.endswith("find-tender.service.gov.uk") and "updatedFrom" not in url:
        now = datetime.now(timezone.utc)
        query = dict(urllib.parse.parse_qsl(parsed.query))
        query.update(updatedFrom=(now - timedelta(days=days)).strftime("%Y-%m-%dT%H:%M:%SZ"),
                     updatedTo=now.strftime("%Y-%m-%dT%H:%M:%SZ"))
        url = urllib.parse.urlunsplit(parsed._replace(query=urllib.parse.urlencode(query)))
    request = urllib.request.Request(url, headers={"User-Agent": "Noavia-Tender-Hunter/1.0", "Accept": "application/json, application/rss+xml, application/atom+xml"})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        candidates = parse_feed(response.read(), response.headers.get("content-type", ""))
        if not candidates:
            raise ValueError("source returned no recognizable tender records; adapter or source schema may have changed")
        return candidates


def filter_candidates(candidates: list[TenderCandidate], keywords: list[str] | None = None) -> list[TenderCandidate]:
    """Deterministically filter and deduplicate by normalized external id."""
    terms = [t.casefold() for t in (keywords or []) if t.strip()]
    seen: set[str] = set()
    result = []
    for candidate in candidates:
        key = re.sub(r"\s+", " ", candidate.external_id).strip().casefold()
        haystack = f"{candidate.title} {candidate.description or ''}".casefold()
        def matches(term: str) -> bool:
            if len(term) <= 3 or term.isdigit():
                return re.search(rf"(?<!\w){re.escape(term)}(?!\w)", haystack) is not None
            return term in haystack
        if terms and not any(matches(term) for term in terms):
            continue
        if key in seen:
            continue
        seen.add(key)
        result.append(candidate)
    return result
