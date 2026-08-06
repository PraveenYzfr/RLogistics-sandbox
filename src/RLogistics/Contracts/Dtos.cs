using RLogistics.Domain;

namespace RLogistics.Contracts;

public record AssetDto(
    string AssetType,
    string? SerialNumber,
    int Quantity,
    string? Manufacturer = null,
    string? Model = null,
    string? AssetTag = null,
    string? Condition = null,
    string? DeviceGuid = null);

public record CreateRequestDto(
    string? RequestorEmail,
    // Contact
    string ContactName,
    string ContactEmail,
    string? ContactPhone,
    string? ContactDepartment,
    // Facility
    string Site,
    string? FacilityCode,
    string? Building,
    string? Floor,
    string? Room,
    // Pickup
    string PickupAddressLine1,
    string? PickupAddressLine2,
    string PickupCity,
    string? PickupState,
    string? PickupPostalCode,
    string? PickupCountry,
    DateTime? PreferredPickupDate,
    string? PickupInstructions,
    DispositionType DispositionType,
    RequestType RequestType,
    string? Notes,
    List<AssetDto> Assets);

public record UpdateStatusDto(RequestStatus Status, string? Notes);

public record AssignRequestDto(int? CoordinatorUserId);

public record ClarificationDto(string Question);

public record ClarificationReplyDto(string Response);

public record LoginRequestDto(string Email, string Password);

public record TokenResponseDto(
    string AccessToken,
    string TokenType,
    int ExpiresInMinutes,
    string Email,
    string Role,
    string[] Permissions,
    string AuthMode);

public record EmailTemplateDto(string Code, string SubjectTemplate, string BodyTemplate, bool IsActive);

public record AppConfigDto(string Key, string Value, string? Description);

public record RequestSummaryDto(
    int Id,
    string RequestNumber,
    string Site,
    string PickupCity,
    DispositionType DispositionType,
    RequestType RequestType,
    RequestStatus Status,
    string RequestorEmail,
    string ContactName,
    string? AssignedCoordinatorEmail,
    DateTime CreatedAt,
    int AssetCount,
    DateTime? PreferredPickupDate,
    DateTime? ScheduledPickupDate,
    string? TransportVendorName,
    string? ProcessingVendorName);

public record RequestDetailDto(
    int Id,
    string RequestNumber,
    string Site,
    string? FacilityCode,
    string? Building,
    string? Floor,
    string? Room,
    string ContactName,
    string ContactEmail,
    string? ContactPhone,
    string? ContactDepartment,
    string PickupAddressLine1,
    string? PickupAddressLine2,
    string PickupCity,
    string? PickupState,
    string? PickupPostalCode,
    string PickupCountry,
    DateTime? PreferredPickupDate,
    string? PickupInstructions,
    DispositionType DispositionType,
    RequestType RequestType,
    RequestStatus Status,
    string RequestorEmail,
    int RequestorUserId,
    string? AssignedCoordinatorEmail,
    int? AssignedCoordinatorUserId,
    string? Notes,
    string? CoordinatorNotes,
    int? TransportVendorId,
    string? TransportVendorName,
    int? ProcessingVendorId,
    string? ProcessingVendorName,
    DateTime? ScheduledPickupDate,
    string? ScheduledPickupSlot,
    DateTime? ExpectedDeviceReturnDate,
    DateTime? LastReturnReminderAt,
    DateTime CreatedAt,
    List<AssetDto> Assets,
    List<AuditItemDto> Audit,
    List<ClarificationItemDto> Clarifications);

public record PlanRequestDto(
    int? TransportVendorId,
    int? ProcessingVendorId,
    DateTime? ScheduledPickupDate,
    string? ScheduledPickupSlot,
    bool MarkPickupScheduled = true,
    DateTime? ExpectedDeviceReturnDate = null);

public record UpdateRequestFieldsDto(string? Notes, string? CoordinatorNotes, string? PickupInstructions);

public record VendorDto(int Id, string Name, VendorType Type, string? ServiceArea, string? Email = null);

public record AuditItemDto(string Action, string Detail, DateTime At, int? ActorUserId);
public record ClarificationItemDto(int Id, string Question, string? Response, DateTime CreatedAt);
public record UserDto(int Id, string Email, string DisplayName, UserRole Role);
public record EmailOutboxDto(int Id, string ToAddress, string Subject, string Body, int? RequestId, string? TemplateCode, string? StatusFrom, string? StatusTo, DateTime CreatedAt);
