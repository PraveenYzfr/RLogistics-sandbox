# RLogistics SOPs — Knowledge base for RLogisticsGENIE RAG

## Completeness before assignment
- Device GUID is required on every asset line.
- Manufacturer and Model are required.
- Pickup address and city must be present.
- Prefer staging date (expected return) before pickup schedule.

## Clarification vs Cancel
- Clarification (On Hold) when missing serials/GUIDs or incomplete contact.
- Cancel only for extreme invalid requests or duplicates after coordinator review.

## Transport vs Processing vendors
- Transport: move assets site → processing facility.
- Processing: Sanitize or Destroy per request disposition.
- Request quotes from both selected vendors before marking Pickup Scheduled.

## Device return reminders
- If expected return date is past and status is not PickedUp/Delivered, send DeviceReturnReminder.
- Contact person and assigned coordinator both notified.

## Status path
Created → Assigned → Pickup Scheduled → Picked Up → Delivered.
Rare: PO Approval, On Hold, Cancelled.
