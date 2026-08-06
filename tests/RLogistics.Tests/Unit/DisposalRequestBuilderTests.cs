using FluentAssertions;
using RLogistics.Contracts;
using RLogistics.Domain;
using RLogistics.Patterns.Builder;

namespace RLogistics.Tests.Unit;

public class DisposalRequestBuilderTests
{
    [Fact]
    public void Builder_sets_fields_and_assets()
    {
        var factory = new DisposalRequestBuilderFactory();
        var user = new AppUser { Id = 5, Email = "user@demo.local", DisplayName = "Alex", Role = UserRole.User };
        var dto = Infrastructure.TestData.ValidCreate();

        var entity = factory.Create()
            .WithRequestor(user)
            .WithContact(dto)
            .WithFacility(dto)
            .WithPickup(dto)
            .WithAssets(dto.Assets)
            .WithDefaults(dto.PreferredPickupDate, 7)
            .Build("RLogistics-9001");

        entity.RequestNumber.Should().Be("RLogistics-9001");
        entity.RequestorUserId.Should().Be(5);
        entity.ContactEmail.Should().Be(dto.ContactEmail);
        entity.Site.Should().Be(dto.Site);
        entity.PickupCity.Should().Be("Charlotte");
        entity.Assets.Should().HaveCount(1);
        var asset = entity.Assets.First();
        asset.Manufacturer.Should().Be("Dell");
        asset.DeviceGuid.Should().NotBeNullOrWhiteSpace();
        entity.Status.Should().Be(RequestStatus.Created);
        entity.ExpectedDeviceReturnDate.Should().NotBeNull();
    }
}
