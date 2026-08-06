"""LangChain-style skill functions + offline LLM path for GENIE P0."""

from __future__ import annotations

from typing import Any


REQUIRED_FIELDS = [
    ("contactName", "Contact name"),
    ("contactEmail", "Contact email"),
    ("site", "Facility / site"),
    ("pickupAddressLine1", "Pickup address"),
    ("pickupCity", "Pickup city"),
    ("assets", "Equipment lines"),
]


def completeness_score(request: dict[str, Any]) -> dict[str, Any]:
    gaps: list[str] = []
    for key, label in REQUIRED_FIELDS:
        val = request.get(key)
        if val is None or val == "" or val == []:
            gaps.append(label)

    assets = request.get("assets") or []
    missing_guid = 0
    missing_mfr = 0
    for a in assets:
        if not a.get("deviceGuid"):
            missing_guid += 1
        if not a.get("manufacturer") or not a.get("model"):
            missing_mfr += 1
    if missing_guid:
        gaps.append(f"{missing_guid} asset(s) missing Device GUID")
    if missing_mfr:
        gaps.append(f"{missing_mfr} asset(s) missing manufacturer/model")

    if not request.get("expectedDeviceReturnDate") and not request.get("preferredPickupDate"):
        gaps.append("Expected return / preferred pickup date")

    total_checks = len(REQUIRED_FIELDS) + 3
    score = max(0.0, 1.0 - (len(gaps) / total_checks))
    risk = "low" if score >= 0.85 else "medium" if score >= 0.6 else "high"
    return {
        "score": round(score, 2),
        "risk": risk,
        "gaps": gaps,
        "assetCount": len(assets),
        "readyForCoordinator": score >= 0.85 and not missing_guid,
    }


def summarize_request(request: dict[str, Any], sop_hits: list[dict[str, Any]] | None = None) -> dict[str, Any]:
    assets = request.get("assets") or []
    lines = [
        f"{a.get('quantity', 1)}× {a.get('assetType')} ({a.get('manufacturer')} {a.get('model')})"
        for a in assets[:12]
    ]
    summary = (
        f"{request.get('requestNumber')} — {request.get('requestType')} / {request.get('dispositionType')} "
        f"at {request.get('site')}. Status: {request.get('status')}. "
        f"Contact: {request.get('contactName')} <{request.get('contactEmail')}>. "
        f"Pickup: {request.get('pickupCity')}, preferred {request.get('preferredPickupDate') or 'TBD'}. "
        f"Assets ({len(assets)} lines): " + ("; ".join(lines) if lines else "none") + "."
    )
    next_actions = []
    if request.get("status") in ("Created", 0, "0"):
        next_actions.append("Claim / assign coordinator")
    if not request.get("transportVendorId") or not request.get("processingVendorId"):
        next_actions.append("Select transport + processing vendors and request quotes")
    if request.get("status") in ("Assigned", "Created"):
        next_actions.append("Schedule pickup date/slot when ready")
    policy = [h.get("title") for h in (sop_hits or [])[:3]]
    return {
        "requestNumber": request.get("requestNumber"),
        "status": request.get("status"),
        "summary": summary,
        "nextBestActions": next_actions,
        "policyHints": policy,
        "sources": [{"title": h.get("title"), "score": h.get("score")} for h in (sop_hits or [])],
    }


def draft_clarification(request: dict[str, Any], gaps: list[str]) -> str:
    num = request.get("requestNumber", "this request")
    if not gaps:
        return f"Regarding {num}: please confirm devices are staged and Device GUIDs are complete before pickup."
    bullets = "\n".join(f"- {g}" for g in gaps)
    return (
        f"Hi {request.get('contactName') or 'team'},\n\n"
        f"We need a few details on {num} before we can proceed:\n{bullets}\n\n"
        f"Please update RLogistics or reply to this query.\n\n— RLogistics Coordinator (GENIE draft)"
    )


def recommend_vendors(request: dict[str, Any], vendors: list[dict[str, Any]]) -> dict[str, Any]:
    transport = [v for v in vendors if str(v.get("type")) in ("0", "Transport", "transport")]
    processing = [v for v in vendors if str(v.get("type")) in ("1", "Processing", "processing")]
    disp = str(request.get("dispositionType", "")).lower()
    # Prefer destroy vendor name if destroy
    proc_sorted = sorted(
        processing,
        key=lambda v: (0 if "destroy" in (v.get("name") or "").lower() and "destroy" in disp else 1,
                       v.get("name") or ""),
    )
    return {
        "transport": [
            {"vendorId": v.get("id"), "name": v.get("name"), "score": 0.9 - i * 0.05, "reasons": ["Active transport vendor", v.get("serviceArea") or ""]}
            for i, v in enumerate(transport[:3])
        ],
        "processing": [
            {"vendorId": v.get("id"), "name": v.get("name"), "score": 0.9 - i * 0.05,
             "reasons": [f"Matches disposition {request.get('dispositionType')}", v.get("serviceArea") or ""]}
            for i, v in enumerate(proc_sorted[:3])
        ],
    }


def parse_quote_email(body: str, request_number: str | None = None) -> dict[str, Any]:
    import re

    amount = None
    m = re.search(r"\$?\s*([0-9]{1,3}(?:,[0-9]{3})*(?:\.[0-9]{2})?)", body)
    if m:
        amount = float(m.group(1).replace(",", ""))
    eta = None
    m2 = re.search(r"(\d+)\s*(?:business\s*)?days?", body, re.I)
    if m2:
        eta = int(m2.group(1))
    vendor = None
    for line in body.splitlines()[:8]:
        if "from" in line.lower() or "vendor" in line.lower():
            vendor = line.strip()[:120]
            break
    return {
        "requestNumber": request_number,
        "vendorName": vendor,
        "totalAmount": amount,
        "currency": "USD",
        "etaDays": eta,
        "confidence": 0.55 if amount else 0.35,
        "rawExcerpt": body[:500],
        "exceptions": [] if amount else ["Amount not detected — review manually"],
    }


def status_narrative(request: dict[str, Any], from_status: str, to_status: str) -> str:
    return (
        f"Request {request.get('requestNumber')} moved from {from_status} to {to_status} "
        f"for site {request.get('site')}. Contact {request.get('contactName')} will be notified via email template."
    )
