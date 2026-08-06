using FluentValidation;
using RLogistics.Contracts;

namespace RLogistics.Validation;

public sealed class CreateRequestDtoValidator : AbstractValidator<CreateRequestDto>
{
    public CreateRequestDtoValidator()
    {
        RuleFor(x => x.ContactName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Site).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PickupAddressLine1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PickupCity).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Assets).NotEmpty().WithMessage("At least one asset is required.");
        RuleForEach(x => x.Assets).SetValidator(new AssetDtoValidator());
    }
}

public sealed class AssetDtoValidator : AbstractValidator<AssetDto>
{
    public AssetDtoValidator()
    {
        RuleFor(x => x.AssetType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Manufacturer).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Model).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(10_000);
        RuleFor(x => x.DeviceGuid).MaximumLength(100);
    }
}

public sealed class UpdateStatusDtoValidator : AbstractValidator<UpdateStatusDto>
{
    public UpdateStatusDtoValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(4000).When(x => x.Notes is not null);
    }
}

public sealed class PlanRequestDtoValidator : AbstractValidator<PlanRequestDto>
{
    public PlanRequestDtoValidator()
    {
        RuleFor(x => x.ScheduledPickupSlot).MaximumLength(80).When(x => x.ScheduledPickupSlot is not null);
        RuleFor(x => x.TransportVendorId).GreaterThan(0).When(x => x.TransportVendorId.HasValue);
        RuleFor(x => x.ProcessingVendorId).GreaterThan(0).When(x => x.ProcessingVendorId.HasValue);
    }
}

public sealed class ClarificationDtoValidator : AbstractValidator<ClarificationDto>
{
    public ClarificationDtoValidator()
    {
        RuleFor(x => x.Question).NotEmpty().MaximumLength(2000);
    }
}

public sealed class ClarificationReplyDtoValidator : AbstractValidator<ClarificationReplyDto>
{
    public ClarificationReplyDtoValidator()
    {
        RuleFor(x => x.Response).NotEmpty().MaximumLength(4000);
    }
}

public sealed class LoginRequestDtoValidator : AbstractValidator<LoginRequestDto>
{
    public LoginRequestDtoValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}
