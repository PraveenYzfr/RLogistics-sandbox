using System.Text;
using Azure.Identity;
using RLogistics.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace RLogistics.Integrations.Notifications;

/// <summary>
/// Real Microsoft Graph mail.
/// PersonalMicrosoft — delegated MSAL (device code / cached token) → your Outlook.
/// EnterpriseGraph — client credentials app-only → service mailbox.
/// </summary>
public sealed class GraphMailTransport(
    IOptions<NotificationOptions> options,
    ILogger<GraphMailTransport> log,
    PersonalGraphTokenStore tokens) : IEmailTransport
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var opts = options.Value;
        var graph = opts.Graph;
        if (string.IsNullOrWhiteSpace(graph.ClientId))
            throw new InvalidOperationException(
                "Notifications:Graph:ClientId is empty. Register an Entra app (public client for PersonalMicrosoft) and set ClientId.");

        var client = await CreateGraphClientAsync(opts, ct);
        var messageBody = new Message
        {
            Subject = message.Subject,
            Body = new ItemBody
            {
                ContentType = BodyType.Text,
                Content = message.Body
            },
            ToRecipients =
            [
                new Recipient
                {
                    EmailAddress = new EmailAddress { Address = message.ToAddress }
                }
            ]
        };

        if (opts.ResolvedMode == NotificationChannelMode.EnterpriseGraph &&
            !string.IsNullOrWhiteSpace(graph.SenderUserId) &&
            !string.Equals(graph.SenderUserId, "me", StringComparison.OrdinalIgnoreCase))
        {
            var body = new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
            {
                Message = messageBody,
                SaveToSentItems = true
            };
            await client.Users[graph.SenderUserId].SendMail.PostAsync(body, cancellationToken: ct);
        }
        else
        {
            var body = new Microsoft.Graph.Me.SendMail.SendMailPostRequestBody
            {
                Message = messageBody,
                SaveToSentItems = true
            };
            await client.Me.SendMail.PostAsync(body, cancellationToken: ct);
        }

        log.LogInformation("Sent Graph mail Subject={Subject} To={To}", message.Subject, message.ToAddress);
    }

    private async Task<GraphServiceClient> CreateGraphClientAsync(NotificationOptions opts, CancellationToken ct)
    {
        if (opts.ResolvedMode == NotificationChannelMode.EnterpriseGraph)
        {
            if (string.IsNullOrWhiteSpace(opts.Graph.ClientSecret))
                throw new InvalidOperationException("EnterpriseGraph requires Notifications:Graph:ClientSecret.");
            var cred = new ClientSecretCredential(opts.Graph.TenantId, opts.Graph.ClientId, opts.Graph.ClientSecret);
            return new GraphServiceClient(cred, ["https://graph.microsoft.com/.default"]);
        }

        // PersonalMicrosoft — interactive/device-code token via MSAL
        var accessToken = await tokens.GetAccessTokenAsync(opts.Graph, ct);
        return new GraphServiceClient(new StaticTokenProvider(accessToken), ["https://graph.microsoft.com/.default"]);
    }
}

/// <summary>Cached personal login for Graph (device code + file cache).</summary>
public sealed class PersonalGraphTokenStore(IOptions<NotificationOptions> options, ILogger<PersonalGraphTokenStore> log)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPublicClientApplication? _app;
    private Task? _loginTask;

    public string? LastDeviceCodeMessage { get; private set; }
    public string? LastDeviceCode { get; private set; }
    public string? LastVerificationUrl { get; private set; }
    public string? LastSignedInUpn { get; private set; }
    public string? LastLoginError { get; private set; }
    public bool LoginInProgress => _loginTask is { IsCompleted: false };

    /// <summary>
    /// Mail-only by default (best success for personal MSA).
    /// Pass teams:true or combine when posting Graph Teams chat/channel.
    /// </summary>
    private static string[] ScopesFor(GraphOptions graph, bool includeTeams) =>
        includeTeams
            ? graph.MailScopes.Concat(graph.TeamsScopes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : graph.MailScopes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public Task<string> GetAccessTokenAsync(GraphOptions graph, CancellationToken ct)
        => GetAccessTokenAsync(graph, includeTeams: false, ct);

    public async Task<string> GetAccessTokenAsync(GraphOptions graph, bool includeTeams, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var scopes = ScopesFor(graph, includeTeams);
            var app = await EnsureAppAsync(graph);
            var accounts = await app.GetAccountsAsync();
            AuthenticationResult result;
            try
            {
                result = await app.AcquireTokenSilent(scopes, accounts.FirstOrDefault())
                    .ExecuteAsync(ct);
            }
            catch (MsalUiRequiredException)
            {
                log.LogWarning("Interactive Graph login required — starting device code flow.");
                result = await app.AcquireTokenWithDeviceCode(scopes, d =>
                {
                    log.LogWarning("GRAPH LOGIN: {Message}", d.Message);
                    LastDeviceCodeMessage = d.Message;
                    LastDeviceCode = d.UserCode;
                    LastVerificationUrl = d.VerificationUrl;
                    return Task.CompletedTask;
                }).ExecuteAsync(ct);
            }

            LastSignedInUpn = result.Account?.Username;
            LastLoginError = null;
            return result.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Kick off device-code login in the background (API returns immediately; poll status).</summary>
    public void StartInteractiveLogin()
    {
        if (LoginInProgress) return;
        _loginTask = Task.Run(async () =>
        {
            try
            {
                await InteractiveLoginAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                LastLoginError = ex.Message;
                log.LogError(ex, "Graph device-code login failed");
            }
        });
    }

    /// <summary>Force device-code login (for setup endpoint).</summary>
    public async Task<string> InteractiveLoginAsync(CancellationToken ct = default)
    {
        var graph = options.Value.Graph;
        if (string.IsNullOrWhiteSpace(graph.ClientId))
            throw new InvalidOperationException("Notifications:Graph:ClientId is empty.");

        await _gate.WaitAsync(ct);
        try
        {
            // Interactive setup path: mail-only first try (personal Outlook).
            var scopes = ScopesFor(graph, includeTeams: false);
            var app = await EnsureAppAsync(graph);
            var result = await app.AcquireTokenWithDeviceCode(scopes, d =>
            {
                LastDeviceCodeMessage = d.Message;
                LastDeviceCode = d.UserCode;
                LastVerificationUrl = d.VerificationUrl;
                log.LogWarning("GRAPH LOGIN: {Message}", d.Message);
                return Task.CompletedTask;
            }).ExecuteAsync(ct);
            LastSignedInUpn = result.Account?.Username;
            LastLoginError = null;
            return result.Account?.Username ?? "(signed in)";
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IPublicClientApplication> EnsureAppAsync(GraphOptions graph)
    {
        if (_app is not null) return _app;

        _app = PublicClientApplicationBuilder
            .Create(graph.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, graph.TenantId)
            .WithDefaultRedirectUri()
            .Build();

        var cachePath = Path.GetFullPath(graph.PersonalTokenCachePath);
        var dir = Path.GetDirectoryName(cachePath)!;
        Directory.CreateDirectory(dir);
        var storage = new StorageCreationPropertiesBuilder(Path.GetFileName(cachePath), dir).Build();
        var cacheHelper = await MsalCacheHelper.CreateAsync(storage);
        cacheHelper.RegisterCache(_app.UserTokenCache);
        return _app;
    }
}

/// <summary>Minimal Graph auth provider from raw token.</summary>
file sealed class StaticTokenProvider(string accessToken) : Azure.Core.TokenCredential
{
    public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
        => new(accessToken, DateTimeOffset.UtcNow.AddMinutes(50));

    public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
        => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}
