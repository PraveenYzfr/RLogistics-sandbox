using RLogistics.Domain;
using Microsoft.EntityFrameworkCore;

namespace RLogistics.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(RLogisticsDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        await SchemaPatcher.ApplyAsync(db);

        await EnsureVendorsAsync(db);
        await EnsureTemplatesAsync(db);
        await EnsureConfigsAsync(db);

        if (await db.Users.AnyAsync())
            return;

        var user = new AppUser { Email = "user@demo.local", DisplayName = "Alex Requestor", Role = UserRole.User };
        var coord1 = new AppUser { Email = "coord1@demo.local", DisplayName = "Casey Coordinator", Role = UserRole.Coordinator };
        var coord2 = new AppUser { Email = "coord2@demo.local", DisplayName = "Jordan Coordinator", Role = UserRole.Coordinator };
        var admin = new AppUser { Email = "admin@demo.local", DisplayName = "Sam Admin", Role = UserRole.Admin };
        db.Users.AddRange(user, coord1, coord2, admin);
        await db.SaveChangesAsync();

        var samples = new List<DisposalRequest>
        {
            Make(user, "RLogistics-1001", "Charlotte HQ Floor 3", DispositionType.Sanitize, RequestType.UsSurplus, RequestStatus.Created, null,
                ("Laptop", "SN-A1001", 5), ("Laptop", null, 2)),
            Make(user, "RLogistics-1002", "Des Moines Warehouse", DispositionType.Destroy, RequestType.PointToPoint, RequestStatus.Assigned, coord1.Id,
                ("Server", "SRV-9001", 1)),
            Make(user, "RLogistics-1003", "Phoenix Branch", DispositionType.Sanitize, RequestType.UsSurplus, RequestStatus.Assigned, coord1.Id,
                ("Monitor", "MON-22", 10)),
            Make(user, "RLogistics-1004", "Chicago DC", DispositionType.Sanitize, RequestType.RequestABox, RequestStatus.OnHold, coord2.Id,
                ("Laptop", null, 12)),
            Make(user, "RLogistics-1005", "Atlanta Hub", DispositionType.Destroy, RequestType.UsSurplus, RequestStatus.Assigned, coord1.Id,
                ("Desktop", "DT-771", 4)),
            Make(user, "RLogistics-1006", "Seattle Office", DispositionType.Sanitize, RequestType.International, RequestStatus.PickupScheduled, coord2.Id,
                ("Laptop", "SN-SEA-1", 3)),
            Make(user, "RLogistics-1007", "Dallas Site B", DispositionType.Destroy, RequestType.PointToPoint, RequestStatus.Created, null,
                ("Tablet", "TB-01", 8)),
            Make(user, "RLogistics-1008", "Remote locker 4", DispositionType.Sanitize, RequestType.UsSurplus, RequestStatus.Cancelled, coord1.Id,
                ("Laptop", "SN-X", 1)),
        };

        db.Requests.AddRange(samples);
        foreach (var r in samples)
        {
            db.AuditLogs.Add(new AuditLog
            {
                Request = r,
                ActorUserId = user.Id,
                Action = "Seeded",
                Detail = $"Seeded as {r.Status} / {r.RequestType}"
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureVendorsAsync(RLogisticsDbContext db)
    {
        if (await db.Vendors.AnyAsync())
        {
            // Patch emails on existing vendors
            foreach (var v in await db.Vendors.Where(x => x.Email == null || x.Email == "").ToListAsync())
                v.Email = $"quotes+{v.Name.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant()}@vendor.demo.local";
            await db.SaveChangesAsync();
            return;
        }
        db.Vendors.AddRange(
            new Vendor { Name = "SwiftHaul Logistics", Type = VendorType.Transport, ServiceArea = "Southeast / National", Email = "quotes@swifthaul.demo.local" },
            new Vendor { Name = "RoadLink Transport", Type = VendorType.Transport, ServiceArea = "Midwest / National", Email = "rfq@roadlink.demo.local" },
            new Vendor { Name = "SecureWipe Processing", Type = VendorType.Processing, ServiceArea = "National sanitize", Email = "quotes@securewipe.demo.local" },
            new Vendor { Name = "IronVault Destruction", Type = VendorType.Processing, ServiceArea = "National destroy", Email = "rfq@ironvault.demo.local" });
        await db.SaveChangesAsync();
    }

    private static async Task EnsureTemplatesAsync(RLogisticsDbContext db)
    {
        async Task Ensure(string code, string subject, string body)
        {
            if (await db.EmailTemplates.AnyAsync(t => t.Code == code)) return;
            db.EmailTemplates.Add(new EmailTemplate
            {
                Code = code,
                SubjectTemplate = subject,
                BodyTemplate = body,
                IsActive = true
            });
        }

        await Ensure("StatusChanged",
            "RLogistics {{RequestNumber}} status: {{StatusTo}}",
            "Hello {{RequestorName}},\n\nYour disposal request {{RequestNumber}} at {{Site}} changed from {{StatusFrom}} to {{StatusTo}}.\n\nContact: {{ContactName}} ({{ContactEmail}})\nDisposition: {{Disposition}} · Type: {{RequestType}}\nAssets ({{AssetCount}}):\n{{AssetList}}\n\nNotes: {{Notes}}\n\n— RLogistics Simulation (mock email)");

        await Ensure("Status_Created",
            "RLogistics {{RequestNumber}} received",
            "Hello {{RequestorName}},\n\nWe received disposal request {{RequestNumber}} for {{Site}}.\n\nPlease ensure devices listed below are ready by {{ExpectedReturnDate}}.\n\n{{AssetList}}\n\n— RLogistics Simulation");

        await Ensure("Status_Assigned",
            "RLogistics {{RequestNumber}} assigned to coordinator",
            "Hello {{RequestorName}},\n\nRequest {{RequestNumber}} is now Assigned. Coordinator: {{CoordinatorEmail}}.\n\nSite: {{Site}}\nExpected device return / ready-by: {{ExpectedReturnDate}}\n\n— RLogistics Simulation");

        await Ensure("Status_PickupScheduled",
            "RLogistics {{RequestNumber}} pickup scheduled",
            "Hello {{RequestorName}},\n\nPickup for {{RequestNumber}} at {{Site}} is scheduled for {{ScheduledPickupDate}} {{ScheduledPickupSlot}}.\n\nAddress: {{PickupAddress}}\nTransport: {{TransportVendor}}\nProcessing: {{ProcessingVendor}}\n\n— RLogistics Simulation");

        await Ensure("Status_PickedUp",
            "RLogistics {{RequestNumber}} devices picked up",
            "Hello {{RequestorName}},\n\nAssets for {{RequestNumber}} have been marked Picked Up from {{Site}}.\n\n{{AssetList}}\n\n— RLogistics Simulation");

        await Ensure("Status_Delivered",
            "RLogistics {{RequestNumber}} delivered to processing",
            "Hello {{RequestorName}},\n\nRequest {{RequestNumber}} is Delivered. Disposition: {{Disposition}}.\nProcessing vendor: {{ProcessingVendor}}\n\n— RLogistics Simulation");

        await Ensure("Status_PoApproval",
            "RLogistics {{RequestNumber}} awaiting PO approval",
            "Hello {{RequestorName}},\n\nRequest {{RequestNumber}} is on PO Approval. Site: {{Site}}.\nNotes: {{Notes}}\n\n— RLogistics Simulation");

        await Ensure("Status_OnHold",
            "RLogistics {{RequestNumber}} on hold",
            "Hello {{RequestorName}},\n\nRequest {{RequestNumber}} is On Hold. Please check RLogistics for open questions.\nNotes: {{Notes}}\n\n— RLogistics Simulation");

        await Ensure("Status_Cancelled",
            "RLogistics {{RequestNumber}} cancelled",
            "Hello {{RequestorName}},\n\nRequest {{RequestNumber}} has been Cancelled.\nNotes: {{Notes}}\n\n— RLogistics Simulation");

        await Ensure("ClarificationSent",
            "RLogistics {{RequestNumber}} needs your response",
            "Hello {{RequestorName}},\n\nA coordinator has a question about request {{RequestNumber}} at {{Site}}.\n\nQuestion: {{ClarificationQuestion}}\n\nPlease review RLogistics and respond.\n\n— RLogistics Simulation");

        await Ensure("PickupScheduled",
            "RLogistics {{RequestNumber}} pickup scheduled",
            "Hello {{RequestorName}},\n\nPickup for request {{RequestNumber}} at {{Site}} has been scheduled for {{ScheduledPickupDate}} {{ScheduledPickupSlot}}.\n\n— RLogistics Simulation");

        await Ensure("VendorQuote",
            "RLogistics quote request {{RequestNumber}} — {{VendorType}}",
            "Hello {{VendorName}},\n\nPlease provide a quote for RLogistics request {{RequestNumber}}.\n\nRequest type: {{RequestType}}\nDisposition: {{Disposition}}\nSite: {{Site}}\nPickup: {{PickupAddress}}\nPreferred date: {{PreferredPickupDate}}\nScheduled: {{ScheduledPickupDate}} {{ScheduledPickupSlot}}\n\nAssets ({{AssetCount}}):\n{{AssetList}}\n\nReply with pricing and capacity. Contact: {{ContactName}} {{ContactEmail}}\n\n— RLogistics Simulation");

        await Ensure("VendorQuote_Transport",
            "RLogistics TRANSPORT quote request {{RequestNumber}}",
            "Hello {{VendorName}},\n\nTransport quote request for RLogistics {{RequestNumber}}.\n\nPickup: {{PickupAddress}}\nPreferred: {{PreferredPickupDate}} | Scheduled: {{ScheduledPickupDate}} {{ScheduledPickupSlot}}\nSite: {{Site}}\nAssets ({{AssetCount}}):\n{{AssetList}}\n\nDisposition after transport: {{Disposition}}\nContact: {{ContactName}} · {{ContactEmail}} · {{ContactPhone}}\n\n— RLogistics Simulation");

        await Ensure("VendorQuote_Processing",
            "RLogistics PROCESSING quote request {{RequestNumber}}",
            "Hello {{VendorName}},\n\nProcessing quote request for RLogistics {{RequestNumber}}.\n\nDisposition required: {{Disposition}}\nRequest type: {{RequestType}}\nOrigin site: {{Site}}\nAssets ({{AssetCount}}):\n{{AssetList}}\n\nTransport partner: {{TransportVendor}}\nContact: {{ContactName}} · {{ContactEmail}}\n\n— RLogistics Simulation");

        await Ensure("DeviceReturnReminder",
            "REMINDER: Return / stage devices for RLogistics {{RequestNumber}}",
            "Hello {{ContactName}},\n\nThis is a reminder that devices on RLogistics request {{RequestNumber}} were expected by {{ExpectedReturnDate}} and are still not marked picked up (current status: {{StatusTo}}).\n\nDays overdue: {{DaysOverdue}}\nSite: {{Site}}\nAssets still outstanding ({{AssetCount}}):\n{{AssetList}}\n\nPlease return or stage these devices for pickup and update RLogistics / your coordinator ({{CoordinatorEmail}}).\n\n— RLogistics Simulation");

        await db.SaveChangesAsync();
    }

    private static async Task EnsureConfigsAsync(RLogisticsDbContext db)
    {
        async Task Ensure(string key, string value, string description)
        {
            if (await db.AppConfigs.AnyAsync(c => c.Key == key)) return;
            db.AppConfigs.Add(new AppConfig { Key = key, Value = value, Description = description });
        }

        await Ensure("ClarificationSlaHours", "48", "Hours before clarification reminder");
        await Ensure("DeviceReturnReminderCooldownHours", "24", "Min hours between automatic device-return reminder emails for the same request");
        await Ensure("DefaultDeviceReturnDays", "7", "Default days from create until ExpectedDeviceReturnDate when preferred pickup not set");
        await Ensure("AllowPartnerCreate", "true", "Allow POST /api/requests from partners");
        await Ensure("DefaultDisposition", "Sanitize", "Default disposition on create form");
        await Ensure("DefaultRequestType", "UsSurplus", "Default request type (UsSurplus, PointToPoint, International, RequestABox)");
        await db.SaveChangesAsync();
    }

    private static DisposalRequest Make(
        AppUser requestor,
        string number,
        string site,
        DispositionType disposition,
        RequestType requestType,
        RequestStatus status,
        int? coordinatorId,
        params (string Type, string? Serial, int Qty)[] assets)
    {
        var req = new DisposalRequest
        {
            RequestNumber = number,
            Requestor = requestor,
            ContactName = requestor.DisplayName,
            ContactEmail = requestor.Email,
            ContactPhone = "555-0100",
            ContactDepartment = "Technology",
            Site = site,
            FacilityCode = "FAC-" + number[^3..],
            Building = "Main",
            Floor = "1",
            Room = "Dock",
            PickupAddressLine1 = "100 " + site,
            PickupCity = site.Split(' ')[0],
            PickupState = "NC",
            PickupPostalCode = "28202",
            PickupCountry = "USA",
            PreferredPickupDate = DateTime.UtcNow.Date.AddDays(7),
            ExpectedDeviceReturnDate = status is RequestStatus.Created or RequestStatus.Assigned or RequestStatus.OnHold
                ? DateTime.UtcNow.Date.AddDays(-2) // overdue sample for reminder demo
                : DateTime.UtcNow.Date.AddDays(7),
            DispositionType = disposition,
            RequestType = requestType,
            Status = status,
            AssignedCoordinatorUserId = coordinatorId,
            Notes = status == RequestStatus.OnHold ? "Missing serials / awaiting requestor info" : null,
            CreatedAt = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 14))
        };
        if (status == RequestStatus.PickupScheduled)
        {
            req.ScheduledPickupDate = DateTime.UtcNow.Date.AddDays(3);
            req.ScheduledPickupSlot = PickupSlots.All[0];
        }
        foreach (var a in assets)
        {
            req.Assets.Add(new AssetLine
            {
                AssetType = a.Type,
                SerialNumber = a.Serial,
                DeviceGuid = Guid.NewGuid().ToString(),
                Quantity = a.Qty,
                Manufacturer = a.Type switch
                {
                    "Laptop" => "Dell",
                    "Desktop" => "Dell",
                    "Server" => "HP",
                    "Monitor" => "Dell",
                    "Tablet" => "Apple",
                    _ => "Generic"
                },
                Model = a.Type switch
                {
                    "Laptop" => "Latitude 5540",
                    "Desktop" => "OptiPlex 7090",
                    "Server" => "ProLiant DL380",
                    "Monitor" => "P2422H",
                    "Tablet" => "iPad 10th Gen",
                    _ => a.Type + " Unit"
                }
            });
        }
        return req;
    }
}
