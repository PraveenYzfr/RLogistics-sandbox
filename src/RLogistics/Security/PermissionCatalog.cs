using RLogistics.Domain;

namespace RLogistics.Security;

/// <summary>Maps roles to permissions (RBAC → claims). Strategy input for token builders.</summary>
public static class PermissionCatalog
{
    public static IReadOnlyList<string> ForRole(UserRole role) => role switch
    {
        UserRole.Admin => RLogisticsPermissions.All,
        UserRole.Coordinator =>
        [
            RLogisticsPermissions.UsersRead,
            RLogisticsPermissions.RequestsRead,
            RLogisticsPermissions.RequestsWrite,
            RLogisticsPermissions.RequestsAssign,
            RLogisticsPermissions.RequestsStatus,
            RLogisticsPermissions.RequestsPlan,
            RLogisticsPermissions.RequestsQuotes,
            RLogisticsPermissions.RequestsReminders,
            RLogisticsPermissions.RequestsClarify,
            RLogisticsPermissions.VendorsRead,
            RLogisticsPermissions.EmailOutboxRead,
            RLogisticsPermissions.EmailRemindersRun
        ],
        UserRole.User =>
        [
            RLogisticsPermissions.RequestsRead,
            RLogisticsPermissions.RequestsWrite,
            RLogisticsPermissions.EmailOutboxRead
        ],
        _ => []
    };
}
