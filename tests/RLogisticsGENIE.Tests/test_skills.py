"""GENIE pure skill unit tests (no Core / Redis / LLM required)."""

from __future__ import annotations

import sys
from pathlib import Path

# Allow `pytest` from repo root or tests folder
ROOT = Path(__file__).resolve().parents[2] / "src" / "RLogisticsGENIE"
sys.path.insert(0, str(ROOT))

from app.skills import (  # noqa: E402
    completeness_score,
    draft_clarification,
    parse_quote_email,
    recommend_vendors,
    status_narrative,
    summarize_request,
)


def test_completeness_perfect_score():
    req = {
        "contactName": "Alex",
        "contactEmail": "a@b.com",
        "site": "HQ",
        "pickupAddressLine1": "1 Main",
        "pickupCity": "CLT",
        "assets": [
            {
                "assetType": "Laptop",
                "manufacturer": "Dell",
                "model": "5540",
                "deviceGuid": "g-1",
                "quantity": 1,
            }
        ],
        "preferredPickupDate": "2026-08-10",
    }
    result = completeness_score(req)
    assert result["score"] >= 0.85
    assert result["readyForCoordinator"] is True
    assert result["gaps"] == []


def test_completeness_detects_gaps():
    result = completeness_score({"assets": [{"assetType": "Laptop", "quantity": 1}]})
    assert result["score"] < 0.85
    assert result["risk"] in ("medium", "high")
    assert any("Contact" in g or "contact" in g.lower() for g in result["gaps"])
    assert any("GUID" in g or "manufacturer" in g for g in result["gaps"])


def test_summarize_request_includes_site():
    summary = summarize_request(
        {
            "requestNumber": "RLogistics-1",
            "requestType": "UsSurplus",
            "dispositionType": "Sanitize",
            "site": "Phoenix",
            "status": "Created",
            "contactName": "Alex",
            "contactEmail": "a@b.com",
            "pickupCity": "PHX",
            "assets": [],
        }
    )
    assert "RLogistics-1" in summary["summary"]
    assert "Phoenix" in summary["summary"]
    assert "Claim" in " ".join(summary["nextBestActions"]) or summary["nextBestActions"]


def test_draft_clarification_lists_gaps():
    text = draft_clarification({"requestNumber": "RLogistics-9", "contactName": "Sam"}, ["Missing GUID", "No phone"])
    assert "RLogistics-9" in text
    assert "Missing GUID" in text
    assert "Sam" in text


def test_recommend_vendors_split_by_type():
    vendors = [
        {"id": 1, "name": "SwiftHaul", "type": "Transport", "serviceArea": "SE"},
        {"id": 2, "name": "IronVault Destruction", "type": "Processing", "serviceArea": "National"},
        {"id": 3, "name": "SecureWipe", "type": "Processing", "serviceArea": "National"},
    ]
    rec = recommend_vendors({"dispositionType": "Destroy"}, vendors)
    assert rec["transport"]
    assert rec["processing"]
    assert rec["processing"][0]["name"] == "IronVault Destruction"


def test_parse_quote_email_amount_and_eta():
    body = "Vendor: FastHaul\nTotal $1,250.00 for 5 business days delivery"
    parsed = parse_quote_email(body, "RLogistics-42")
    assert parsed["requestNumber"] == "RLogistics-42"
    assert parsed["totalAmount"] == 1250.0
    assert parsed["etaDays"] == 5
    assert parsed["confidence"] >= 0.5


def test_parse_quote_email_missing_amount():
    parsed = parse_quote_email("Thanks for the RFQ — we will respond soon.")
    assert parsed["totalAmount"] is None
    assert parsed["exceptions"]


def test_status_narrative():
    text = status_narrative({"requestNumber": "RLogistics-7", "site": "Dallas", "contactName": "Alex"}, "Created", "Assigned")
    assert "RLogistics-7" in text
    assert "Created" in text and "Assigned" in text
