using System.Net.Http.Json;
using System.Text;
using RLogistics.Abstractions;
using RLogistics.Data;
using RLogistics.Domain;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace RLogistics.Integrations.Notifications;

/// <summary>
/// Teams delivery:
/// MockOutbox — SQL + UI always available;
/// IncomingWebhook — post to your personal/work Teams channel (best real-time local viz);
/// Graph — channel/chat message via Graph (work accounts; needs TeamId/ChannelId or ChatId).
/// </summary>
public sealed class CompositeTeamsNotifier(
    RLogisticsDbContext db,
    IHttpClientFactory httpFactory,
    IOptions<NotificationOptions> options,
    PersonalGraphTokenStore tokens,
    ILogger<CompositeTeamsNotifier> log) : ITeamsNotifier
{
    public async Task NotifyAsync(TeamsMessage message, CancellationToken ct = default)
    {
        var opts = options.Value;
        var provider = opts.Teams.Provider;
        string result = "mock";

        // Always audit
        var row = new TeamsOutbox
        {
            Channel = provider,
            ToHint = opts.Teams.ChatId ?? opts.Teams.ChannelId ?? "outbox",
            Title = message.Title,
            Body = message.Body,
            RequestId = message.RequestId,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            if (string.Equals(provider, "IncomingWebhook", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(opts.Teams.IncomingWebhookUrl))
            {
                var client = httpFactory.CreateClient(nameof(CompositeTeamsNotifier));
                var payload = new
                {
                    text = $"**{message.Title}**\n\n{message.Body}" +
                           (message.DeepLink is null ? "" : $"\n\n[Open]({message.DeepLink})")
                };
                var resp = await client.PostAsJsonAsync(opts.Teams.IncomingWebhookUrl, payload, ct);
                result = resp.IsSuccessStatusCode ? "webhook-ok" : $"webhook-{(int)resp.StatusCode}";
                row.SentAt = DateTime.UtcNow;
            }
            else if (string.Equals(provider, "Graph", StringComparison.OrdinalIgnoreCase))
            {
                await SendGraphAsync(opts, message, ct);
                result = "graph-ok";
                row.SentAt = DateTime.UtcNow;
            }
            else
            {
                result = "mock-outbox";
                row.SentAt = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            result = "error:" + ex.Message;
            log.LogError(ex, "Teams notify failed ({Provider})", provider);
        }

        row.ProviderResult = result;
        db.Set<TeamsOutbox>().Add(row);
        await db.SaveChangesAsync(ct);
    }

    private async Task SendGraphAsync(NotificationOptions opts, TeamsMessage message, CancellationToken ct)
    {
        GraphServiceClient client;
        if (opts.ResolvedMode == NotificationChannelMode.EnterpriseGraph &&
            !string.IsNullOrWhiteSpace(opts.Graph.ClientSecret))
        {
            var cred = new Azure.Identity.ClientSecretCredential(
                opts.Graph.TenantId, opts.Graph.ClientId, opts.Graph.ClientSecret);
            client = new GraphServiceClient(cred, ["https://graph.microsoft.com/.default"]);
        }
        else
        {
            var token = await tokens.GetAccessTokenAsync(opts.Graph, includeTeams: true, ct);
            client = new GraphServiceClient(
                new GraphTokenCredential(token),
                ["https://graph.microsoft.com/.default"]);
        }

        var chatBody = new ChatMessage
        {
            Body = new ItemBody
            {
                ContentType = BodyType.Html,
                Content = $"<b>{System.Net.WebUtility.HtmlEncode(message.Title)}</b><br/>{System.Net.WebUtility.HtmlEncode(message.Body)}"
            }
        };

        if (!string.IsNullOrWhiteSpace(opts.Teams.ChatId))
        {
            await client.Chats[opts.Teams.ChatId].Messages.PostAsync(chatBody, cancellationToken: ct);
            return;
        }

        if (!string.IsNullOrWhiteSpace(opts.Teams.TeamId) && !string.IsNullOrWhiteSpace(opts.Teams.ChannelId))
        {
            await client.Teams[opts.Teams.TeamId].Channels[opts.Teams.ChannelId].Messages
                .PostAsync(chatBody, cancellationToken: ct);
            return;
        }

        throw new InvalidOperationException(
            "Graph Teams needs Notifications:Teams:ChatId or TeamId+ChannelId. Or use IncomingWebhook for easy local viz.");
    }
}

file sealed class GraphTokenCredential(string accessToken) : Azure.Core.TokenCredential
{
    public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
        => new(accessToken, DateTimeOffset.UtcNow.AddMinutes(50));

    public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
        => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}
