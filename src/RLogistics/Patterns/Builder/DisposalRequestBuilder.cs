using RLogistics.Abstractions;
using RLogistics.Contracts;
using RLogistics.Domain;

namespace RLogistics.Patterns.Builder;

/// <summary>
/// Builder pattern — fluent construction of complex DisposalRequest aggregates.
/// </summary>
public sealed class DisposalRequestBuilder : IDisposalRequestBuilder
{
    private AppUser? _requestor;
    private CreateRequestDto? _dto;
    private readonly List<AssetDto> _assets = [];
    private DateTime? _preferredPickup;
    private int _defaultReturnDays = 7;

    public IDisposalRequestBuilder WithRequestor(AppUser requestor)
    {
        _requestor = requestor;
        return this;
    }

    public IDisposalRequestBuilder WithContact(CreateRequestDto dto)
    {
        _dto = dto;
        return this;
    }

    public IDisposalRequestBuilder WithFacility(CreateRequestDto dto)
    {
        _dto = dto;
        return this;
    }

    public IDisposalRequestBuilder WithPickup(CreateRequestDto dto)
    {
        _dto = dto;
        return this;
    }

    public IDisposalRequestBuilder WithAssets(IEnumerable<AssetDto> assets)
    {
        _assets.Clear();
        _assets.AddRange(assets);
        return this;
    }

    public IDisposalRequestBuilder WithDefaults(DateTime? preferredPickup, int defaultReturnDays)
    {
        _preferredPickup = preferredPickup;
        _defaultReturnDays = defaultReturnDays;
        return this;
    }

    public DisposalRequest Build(string requestNumber)
    {
        if (_requestor is null || _dto is null)
            throw new InvalidOperationException("Requestor and DTO required before Build.");

        var dto = _dto;
        var entity = new DisposalRequest
        {
            RequestNumber = requestNumber,
            RequestorUserId = _requestor.Id,
            ContactName = dto.ContactName.Trim(),
            ContactEmail = dto.ContactEmail.Trim(),
            ContactPhone = string.IsNullOrWhiteSpace(dto.ContactPhone) ? null : dto.ContactPhone.Trim(),
            ContactDepartment = string.IsNullOrWhiteSpace(dto.ContactDepartment) ? null : dto.ContactDepartment.Trim(),
            Site = dto.Site.Trim(),
            FacilityCode = string.IsNullOrWhiteSpace(dto.FacilityCode) ? null : dto.FacilityCode.Trim(),
            Building = string.IsNullOrWhiteSpace(dto.Building) ? null : dto.Building.Trim(),
            Floor = string.IsNullOrWhiteSpace(dto.Floor) ? null : dto.Floor.Trim(),
            Room = string.IsNullOrWhiteSpace(dto.Room) ? null : dto.Room.Trim(),
            PickupAddressLine1 = dto.PickupAddressLine1.Trim(),
            PickupAddressLine2 = string.IsNullOrWhiteSpace(dto.PickupAddressLine2) ? null : dto.PickupAddressLine2.Trim(),
            PickupCity = dto.PickupCity.Trim(),
            PickupState = string.IsNullOrWhiteSpace(dto.PickupState) ? null : dto.PickupState.Trim(),
            PickupPostalCode = string.IsNullOrWhiteSpace(dto.PickupPostalCode) ? null : dto.PickupPostalCode.Trim(),
            PickupCountry = string.IsNullOrWhiteSpace(dto.PickupCountry) ? "USA" : dto.PickupCountry.Trim(),
            PreferredPickupDate = dto.PreferredPickupDate ?? _preferredPickup,
            ExpectedDeviceReturnDate = (dto.PreferredPickupDate ?? _preferredPickup)?.Date
                ?? DateTime.UtcNow.Date.AddDays(_defaultReturnDays),
            PickupInstructions = string.IsNullOrWhiteSpace(dto.PickupInstructions) ? null : dto.PickupInstructions.Trim(),
            DispositionType = dto.DispositionType,
            RequestType = dto.RequestType,
            Status = RequestStatus.Created,
            Notes = dto.Notes,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var a in _assets)
        {
            if (string.IsNullOrWhiteSpace(a.Manufacturer) || string.IsNullOrWhiteSpace(a.Model))
                throw new InvalidOperationException($"Manufacturer and Model are required for asset type '{a.AssetType}'.");

            entity.Assets.Add(new AssetLine
            {
                AssetType = a.AssetType,
                Manufacturer = a.Manufacturer.Trim(),
                Model = a.Model.Trim(),
                SerialNumber = string.IsNullOrWhiteSpace(a.SerialNumber) ? null : a.SerialNumber,
                DeviceGuid = string.IsNullOrWhiteSpace(a.DeviceGuid) ? Guid.NewGuid().ToString() : a.DeviceGuid.Trim(),
                AssetTag = string.IsNullOrWhiteSpace(a.AssetTag) ? null : a.AssetTag,
                Quantity = a.Quantity <= 0 ? 1 : a.Quantity,
                Condition = string.IsNullOrWhiteSpace(a.Condition) ? null : a.Condition
            });
        }

        return entity;
    }
}

/// <summary>Factory for scoped builders (new instance per build).</summary>
public interface IDisposalRequestBuilderFactory
{
    IDisposalRequestBuilder Create();
}

public sealed class DisposalRequestBuilderFactory : IDisposalRequestBuilderFactory
{
    public IDisposalRequestBuilder Create() => new DisposalRequestBuilder();
}
