using Microsoft.EntityFrameworkCore;

namespace RLogistics.Data;

/// <summary>
/// EnsureCreated does not alter existing tables — patch new columns for local sandbox upgrades.
/// </summary>
public static class SchemaPatcher
{
    public static async Task ApplyAsync(RLogisticsDbContext db)
    {
        // SQL Server-only patches (Integration tests use EF InMemory).
        if (!db.Database.IsSqlServer())
            return;

        var sql = """
            IF COL_LENGTH('Requests','ContactName') IS NULL ALTER TABLE Requests ADD ContactName nvarchar(200) NOT NULL CONSTRAINT DF_Requests_ContactName DEFAULT('');
            IF COL_LENGTH('Requests','ContactEmail') IS NULL ALTER TABLE Requests ADD ContactEmail nvarchar(256) NOT NULL CONSTRAINT DF_Requests_ContactEmail DEFAULT('');
            IF COL_LENGTH('Requests','ContactPhone') IS NULL ALTER TABLE Requests ADD ContactPhone nvarchar(40) NULL;
            IF COL_LENGTH('Requests','ContactDepartment') IS NULL ALTER TABLE Requests ADD ContactDepartment nvarchar(120) NULL;
            IF COL_LENGTH('Requests','FacilityCode') IS NULL ALTER TABLE Requests ADD FacilityCode nvarchar(64) NULL;
            IF COL_LENGTH('Requests','Building') IS NULL ALTER TABLE Requests ADD Building nvarchar(100) NULL;
            IF COL_LENGTH('Requests','Floor') IS NULL ALTER TABLE Requests ADD Floor nvarchar(40) NULL;
            IF COL_LENGTH('Requests','Room') IS NULL ALTER TABLE Requests ADD Room nvarchar(40) NULL;
            IF COL_LENGTH('Requests','PickupAddressLine1') IS NULL ALTER TABLE Requests ADD PickupAddressLine1 nvarchar(200) NOT NULL CONSTRAINT DF_Requests_Pickup1 DEFAULT('');
            IF COL_LENGTH('Requests','PickupAddressLine2') IS NULL ALTER TABLE Requests ADD PickupAddressLine2 nvarchar(200) NULL;
            IF COL_LENGTH('Requests','PickupCity') IS NULL ALTER TABLE Requests ADD PickupCity nvarchar(100) NOT NULL CONSTRAINT DF_Requests_PickupCity DEFAULT('');
            IF COL_LENGTH('Requests','PickupState') IS NULL ALTER TABLE Requests ADD PickupState nvarchar(50) NULL;
            IF COL_LENGTH('Requests','PickupPostalCode') IS NULL ALTER TABLE Requests ADD PickupPostalCode nvarchar(20) NULL;
            IF COL_LENGTH('Requests','PickupCountry') IS NULL ALTER TABLE Requests ADD PickupCountry nvarchar(60) NOT NULL CONSTRAINT DF_Requests_PickupCountry DEFAULT('USA');
            IF COL_LENGTH('Requests','PreferredPickupDate') IS NULL ALTER TABLE Requests ADD PreferredPickupDate datetime2 NULL;
            IF COL_LENGTH('Requests','PickupInstructions') IS NULL ALTER TABLE Requests ADD PickupInstructions nvarchar(max) NULL;
            IF COL_LENGTH('Assets','Manufacturer') IS NULL ALTER TABLE Assets ADD Manufacturer nvarchar(100) NULL;
            IF COL_LENGTH('Assets','Model') IS NULL ALTER TABLE Assets ADD Model nvarchar(100) NULL;
            IF COL_LENGTH('Assets','AssetTag') IS NULL ALTER TABLE Assets ADD AssetTag nvarchar(100) NULL;
            IF COL_LENGTH('Assets','Condition') IS NULL ALTER TABLE Assets ADD Condition nvarchar(80) NULL;
            IF COL_LENGTH('Assets','DeviceGuid') IS NULL ALTER TABLE Assets ADD DeviceGuid nvarchar(100) NULL;
            IF COL_LENGTH('Requests','CoordinatorNotes') IS NULL ALTER TABLE Requests ADD CoordinatorNotes nvarchar(max) NULL;
            IF COL_LENGTH('Requests','TransportVendorId') IS NULL ALTER TABLE Requests ADD TransportVendorId int NULL;
            IF COL_LENGTH('Requests','ProcessingVendorId') IS NULL ALTER TABLE Requests ADD ProcessingVendorId int NULL;
            IF COL_LENGTH('Requests','ScheduledPickupDate') IS NULL ALTER TABLE Requests ADD ScheduledPickupDate datetime2 NULL;
            IF COL_LENGTH('Requests','ScheduledPickupSlot') IS NULL ALTER TABLE Requests ADD ScheduledPickupSlot nvarchar(80) NULL;
            IF COL_LENGTH('Requests','RequestType') IS NULL ALTER TABLE Requests ADD RequestType int NOT NULL CONSTRAINT DF_Requests_RequestType DEFAULT(0);
            IF COL_LENGTH('Requests','ExpectedDeviceReturnDate') IS NULL ALTER TABLE Requests ADD ExpectedDeviceReturnDate datetime2 NULL;
            IF COL_LENGTH('Requests','LastReturnReminderAt') IS NULL ALTER TABLE Requests ADD LastReturnReminderAt datetime2 NULL;
            IF COL_LENGTH('EmailOutbox','TemplateCode') IS NULL ALTER TABLE EmailOutbox ADD TemplateCode nvarchar(64) NULL;
            IF OBJECT_ID(N'dbo.TeamsOutbox', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.TeamsOutbox (
                    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    Channel nvarchar(40) NOT NULL,
                    ToHint nvarchar(256) NULL,
                    Title nvarchar(500) NOT NULL,
                    Body nvarchar(max) NOT NULL,
                    RequestId int NULL,
                    ProviderResult nvarchar(500) NULL,
                    CreatedAt datetime2 NOT NULL,
                    SentAt datetime2 NULL
                );
            END
            IF OBJECT_ID(N'dbo.Vendors', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Vendors (
                    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    Name nvarchar(200) NOT NULL,
                    Type int NOT NULL,
                    ServiceArea nvarchar(200) NULL,
                    Email nvarchar(256) NULL,
                    IsActive bit NOT NULL CONSTRAINT DF_Vendors_IsActive DEFAULT(1)
                );
            END
            ELSE
            BEGIN
                IF COL_LENGTH('Vendors','Email') IS NULL ALTER TABLE Vendors ADD Email nvarchar(256) NULL;
            END
            """;
        await db.Database.ExecuteSqlRawAsync(sql);

        // Remap legacy statuses once (old 0-7 scale -> new workflow).
        var migrated = await db.AppConfigs.AsNoTracking()
            .AnyAsync(c => c.Key == "WorkflowStatusV2");
        if (!migrated)
        {
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE Requests SET Status = CASE Status
                    WHEN 0 THEN 0  -- Created / was Submitted
                    WHEN 1 THEN 1  -- Assigned
                    WHEN 2 THEN 1  -- UnderReview -> Assigned
                    WHEN 3 THEN 6  -- AwaitingClarification -> OnHold
                    WHEN 4 THEN 1  -- ReadyForPlanning -> Assigned
                    WHEN 5 THEN 2  -- PickupScheduled
                    WHEN 6 THEN 7  -- Cancelled
                    WHEN 7 THEN 4  -- Completed -> Delivered
                    ELSE 0
                END;
                """);
            db.AppConfigs.Add(new Domain.AppConfig
            {
                Key = "WorkflowStatusV2",
                Value = "true",
                Description = "Statuses remapped to Created/Assigned/PickupScheduled/PickedUp/Delivered/PoApproval/OnHold"
            });
            await db.SaveChangesAsync();
        }

        // Backfill contact/pickup for seed rows that predate those columns.
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE r
            SET
                ContactName = CASE WHEN r.ContactName IS NULL OR LTRIM(RTRIM(r.ContactName)) = '' THEN u.DisplayName ELSE r.ContactName END,
                ContactEmail = CASE WHEN r.ContactEmail IS NULL OR LTRIM(RTRIM(r.ContactEmail)) = '' THEN u.Email ELSE r.ContactEmail END,
                ContactPhone = CASE WHEN r.ContactPhone IS NULL OR LTRIM(RTRIM(r.ContactPhone)) = '' THEN '555-0100' ELSE r.ContactPhone END,
                ContactDepartment = CASE WHEN r.ContactDepartment IS NULL OR LTRIM(RTRIM(r.ContactDepartment)) = '' THEN 'Technology' ELSE r.ContactDepartment END,
                FacilityCode = CASE WHEN r.FacilityCode IS NULL OR LTRIM(RTRIM(r.FacilityCode)) = '' THEN 'FAC-' + RIGHT(r.RequestNumber, 3) ELSE r.FacilityCode END,
                Building = CASE WHEN r.Building IS NULL OR LTRIM(RTRIM(r.Building)) = '' THEN 'Main' ELSE r.Building END,
                Floor = CASE WHEN r.Floor IS NULL OR LTRIM(RTRIM(r.Floor)) = '' THEN '1' ELSE r.Floor END,
                Room = CASE WHEN r.Room IS NULL OR LTRIM(RTRIM(r.Room)) = '' THEN 'Dock' ELSE r.Room END,
                PickupAddressLine1 = CASE WHEN r.PickupAddressLine1 IS NULL OR LTRIM(RTRIM(r.PickupAddressLine1)) = '' THEN '100 ' + r.Site ELSE r.PickupAddressLine1 END,
                PickupCity = CASE WHEN r.PickupCity IS NULL OR LTRIM(RTRIM(r.PickupCity)) = '' THEN
                    CASE WHEN CHARINDEX(' ', r.Site) > 0 THEN LEFT(r.Site, CHARINDEX(' ', r.Site) - 1) ELSE r.Site END
                    ELSE r.PickupCity END,
                PickupState = CASE WHEN r.PickupState IS NULL OR LTRIM(RTRIM(r.PickupState)) = '' THEN 'NC' ELSE r.PickupState END,
                PickupPostalCode = CASE WHEN r.PickupPostalCode IS NULL OR LTRIM(RTRIM(r.PickupPostalCode)) = '' THEN '28202' ELSE r.PickupPostalCode END,
                PickupCountry = CASE WHEN r.PickupCountry IS NULL OR LTRIM(RTRIM(r.PickupCountry)) = '' THEN 'USA' ELSE r.PickupCountry END
            FROM Requests r
            INNER JOIN Users u ON u.Id = r.RequestorUserId;
            """);

        // Device GUID for assets missing it
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE Assets
            SET DeviceGuid = LOWER(CONVERT(nvarchar(36), NEWID()))
            WHERE DeviceGuid IS NULL OR LTRIM(RTRIM(DeviceGuid)) = '';
            """);

        // Manufacturer / Model for all devices
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE Assets SET Manufacturer = 'Dell', Model = 'Latitude 5540'
            WHERE (Manufacturer IS NULL OR LTRIM(RTRIM(Manufacturer)) = '') AND AssetType LIKE '%Laptop%';
            UPDATE Assets SET Manufacturer = 'Dell', Model = 'OptiPlex 7090'
            WHERE (Manufacturer IS NULL OR LTRIM(RTRIM(Manufacturer)) = '') AND AssetType LIKE '%Desktop%';
            UPDATE Assets SET Manufacturer = 'HP', Model = 'ProLiant DL380'
            WHERE (Manufacturer IS NULL OR LTRIM(RTRIM(Manufacturer)) = '') AND AssetType LIKE '%Server%';
            UPDATE Assets SET Manufacturer = 'Dell', Model = 'P2422H'
            WHERE (Manufacturer IS NULL OR LTRIM(RTRIM(Manufacturer)) = '') AND AssetType LIKE '%Monitor%';
            UPDATE Assets SET Manufacturer = 'Apple', Model = 'iPad 10th Gen'
            WHERE (Manufacturer IS NULL OR LTRIM(RTRIM(Manufacturer)) = '') AND AssetType LIKE '%Tablet%';
            UPDATE Assets SET Manufacturer = 'Generic'
            WHERE Manufacturer IS NULL OR LTRIM(RTRIM(Manufacturer)) = '';
            UPDATE Assets SET Model = AssetType + ' Unit'
            WHERE Model IS NULL OR LTRIM(RTRIM(Model)) = '';
            """);

        // Expected return date default: preferred pickup or created+7d
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE Requests
            SET ExpectedDeviceReturnDate = COALESCE(PreferredPickupDate, DATEADD(day, 7, CreatedAt))
            WHERE ExpectedDeviceReturnDate IS NULL
              AND Status NOT IN (3, 4, 7); -- PickedUp, Delivered, Cancelled
            """);

        // Vendor mock emails
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE Vendors SET Email = 'quotes+' + REPLACE(LOWER(Name), ' ', '') + '@vendor.demo.local'
            WHERE Email IS NULL OR LTRIM(RTRIM(Email)) = '';
            """);
    }
}
