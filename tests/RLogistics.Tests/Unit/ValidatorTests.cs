using FluentAssertions;
using FluentValidation.TestHelper;
using RLogistics.Contracts;
using RLogistics.Validation;

namespace RLogistics.Tests.Unit;

public class ValidatorTests
{
    private readonly CreateRequestDtoValidator _create = new();
    private readonly AssetDtoValidator _asset = new();
    private readonly ClarificationDtoValidator _clarify = new();
    private readonly LoginRequestDtoValidator _login = new();

    [Fact]
    public void CreateRequest_valid_passes()
    {
        var dto = Infrastructure.TestData.ValidCreate();
        _create.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void CreateRequest_empty_assets_fails()
    {
        var dto = Infrastructure.TestData.ValidCreate() with { Assets = [] };
        _create.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Assets);
    }

    [Fact]
    public void CreateRequest_bad_email_fails()
    {
        var dto = Infrastructure.TestData.ValidCreate() with { ContactEmail = "not-an-email" };
        _create.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.ContactEmail);
    }

    [Fact]
    public void Asset_requires_manufacturer_model_quantity()
    {
        var bad = new AssetDto("Laptop", null, 0, "", "", null, null, null);
        var r = _asset.TestValidate(bad);
        r.ShouldHaveValidationErrorFor(x => x.Manufacturer);
        r.ShouldHaveValidationErrorFor(x => x.Model);
        r.ShouldHaveValidationErrorFor(x => x.Quantity);
    }

    [Fact]
    public void Clarification_requires_question()
    {
        _clarify.TestValidate(new ClarificationDto("")).ShouldHaveValidationErrorFor(x => x.Question);
    }

    [Fact]
    public void Login_requires_min_password_length()
    {
        _login.TestValidate(new LoginRequestDto("a@b.com", "short"))
            .ShouldHaveValidationErrorFor(x => x.Password);
    }
}
