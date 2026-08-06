using RLogistics.Contracts;
using RLogistics.Domain;

namespace RLogistics.Tests.Infrastructure;

public static class TestData
{
    public static CreateRequestDto ValidCreate(
        string contactEmail = "user@demo.local",
        string site = "Test Site Alpha") =>
        new(
            RequestorEmail: "user@demo.local",
            ContactName: "Alex Requestor",
            ContactEmail: contactEmail,
            ContactPhone: "555-0100",
            ContactDepartment: "IT",
            Site: site,
            FacilityCode: "FAC-1",
            Building: "B1",
            Floor: "3",
            Room: "301",
            PickupAddressLine1: "100 Main St",
            PickupAddressLine2: null,
            PickupCity: "Charlotte",
            PickupState: "NC",
            PickupPostalCode: "28202",
            PickupCountry: "USA",
            PreferredPickupDate: DateTime.UtcNow.Date.AddDays(7),
            PickupInstructions: "Dock B",
            DispositionType: DispositionType.Sanitize,
            RequestType: RequestType.UsSurplus,
            Notes: "Integration test",
            Assets:
            [
                new AssetDto("Laptop", "SN-T-1", 2, "Dell", "5540", "TAG-1", "Good",
                    "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")
            ]);
}
