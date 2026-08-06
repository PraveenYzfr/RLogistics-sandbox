namespace RLogistics.Security;

/// <summary>Fine-grained permission claims embedded in JWT / API-key principals.</summary>
public static class RLogisticsPermissions
{
    public const string ClaimType = "permission";

    public const string UsersRead = "rlogistics.users.read";
    public const string RequestsRead = "rlogistics.requests.read";
    public const string RequestsWrite = "rlogistics.requests.write";
    public const string RequestsAssign = "rlogistics.requests.assign";
    public const string RequestsStatus = "rlogistics.requests.status";
    public const string RequestsPlan = "rlogistics.requests.plan";
    public const string RequestsQuotes = "rlogistics.requests.quotes";
    public const string RequestsReminders = "rlogistics.requests.reminders";
    public const string RequestsClarify = "rlogistics.requests.clarify";
    public const string VendorsRead = "rlogistics.vendors.read";
    public const string EmailOutboxRead = "rlogistics.email.outbox.read";
    public const string EmailRemindersRun = "rlogistics.email.reminders.run";
    public const string AdminTemplates = "rlogistics.admin.templates";
    public const string AdminConfig = "rlogistics.admin.config";

    public static readonly string[] All =
    [
        UsersRead, RequestsRead, RequestsWrite, RequestsAssign, RequestsStatus,
        RequestsPlan, RequestsQuotes, RequestsReminders, RequestsClarify,
        VendorsRead, EmailOutboxRead, EmailRemindersRun, AdminTemplates, AdminConfig
    ];
}
