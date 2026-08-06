using FluentAssertions;
using RLogistics.Domain;
using RLogistics.Security;

namespace RLogistics.Tests.Unit;

public class PermissionCatalogTests
{
    [Fact]
    public void Admin_has_all_permissions()
    {
        PermissionCatalog.ForRole(UserRole.Admin).Should().BeEquivalentTo(RLogisticsPermissions.All);
    }

    [Fact]
    public void User_cannot_assign_or_admin()
    {
        var perms = PermissionCatalog.ForRole(UserRole.User);
        perms.Should().Contain(RLogisticsPermissions.RequestsRead);
        perms.Should().Contain(RLogisticsPermissions.RequestsWrite);
        perms.Should().NotContain(RLogisticsPermissions.RequestsAssign);
        perms.Should().NotContain(RLogisticsPermissions.AdminConfig);
    }

    [Fact]
    public void Coordinator_can_plan_and_quote_but_not_admin_templates()
    {
        var perms = PermissionCatalog.ForRole(UserRole.Coordinator);
        perms.Should().Contain(RLogisticsPermissions.RequestsPlan);
        perms.Should().Contain(RLogisticsPermissions.RequestsQuotes);
        perms.Should().NotContain(RLogisticsPermissions.AdminTemplates);
    }
}
