namespace RLogistics.Integrations.Notifications;

/// <summary>
/// How outbound Mail/Teams is delivered.
/// Mock — SQL outbox only (always audited).
/// PersonalMicrosoft — real Graph with YOUR signed-in Microsoft account (see Outlook/web live).
/// EnterpriseGraph — app-only Graph (corp Entra app registration).
/// </summary>
public enum NotificationChannelMode
{
    Mock = 0,
    PersonalMicrosoft = 1,
    EnterpriseGraph = 2
}

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Mock | PersonalMicrosoft | EnterpriseGraph</summary>
    public string Mode { get; set; } = "Mock";

    /// <summary>Always write Mock outbox rows for audit (recommended true).</summary>
    public bool AlwaysAuditToOutbox { get; set; } = true;

    /// <summary>Also fan-out key events to Teams (when Teams provider is configured).</summary>
    public bool NotifyTeamsOnEmail { get; set; } = true;

    public GraphOptions Graph { get; set; } = new();
    public TeamsOptions Teams { get; set; } = new();

    public NotificationChannelMode ResolvedMode =>
        Enum.TryParse<NotificationChannelMode>(Mode, ignoreCase: true, out var m)
            ? m
            : NotificationChannelMode.Mock;
}

public sealed class GraphOptions
{
    /// <summary>Azure AD application (client) id — public client for Personal, confidential for Enterprise.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Tenant: "common" for personal/multi, or your tenant GUID for enterprise.</summary>
    public string TenantId { get; set; } = "common";

    /// <summary>Client secret — EnterpriseGraph app-only only. Never commit real secrets.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>Mailbox UPN/user id for app-only send (Enterprise). For personal, uses signed-in user.</summary>
    public string SenderUserId { get; set; } = "me";

    /// <summary>Token cache file for personal device-code login (local). </summary>
    public string PersonalTokenCachePath { get; set; } = ".rlogistics-graph-token-cache.bin";

    public string[] MailScopes { get; set; } =
    [
        "User.Read",
        "Mail.Send"
    ];

    public string[] TeamsScopes { get; set; } =
    [
        "User.Read",
        "Chat.ReadWrite",
        "ChannelMessage.Send"
    ];
}

public sealed class TeamsOptions
{
    /// <summary>MockOutbox | IncomingWebhook | Graph</summary>
    public string Provider { get; set; } = "MockOutbox";

    /// <summary>Teams incoming webhook URL (easiest real-time local viz in a channel you own).</summary>
    public string IncomingWebhookUrl { get; set; } = "";

    /// <summary>Team + channel ids for Enterprise Graph channel posts (optional).</summary>
    public string? TeamId { get; set; }
    public string? ChannelId { get; set; }

    /// <summary>Chat id for 1:1 / group chat posts via Graph (optional).</summary>
    public string? ChatId { get; set; }
}
