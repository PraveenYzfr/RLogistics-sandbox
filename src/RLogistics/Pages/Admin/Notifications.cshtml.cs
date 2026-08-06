using RLogistics.Abstractions;
using RLogistics.Integrations.Notifications;
using RLogistics.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace RLogistics.Pages.Admin;

public class NotificationsModel(
    PersonaContext persona,
    IOptions<NotificationOptions> options,
    PersonalGraphTokenStore tokens,
    IEmailTransport email,
    ITeamsNotifier teams) : PageModel
{
    public string Mode { get; private set; } = "Mock";
    public string ResolvedMode { get; private set; } = "Mock";
    public bool AlwaysAudit { get; private set; }
    public bool NotifyTeams { get; private set; }
    public bool ClientIdConfigured { get; private set; }
    public string? SignedInUpn { get; private set; }
    public string TeamsProvider { get; private set; } = "MockOutbox";
    public bool WebhookConfigured { get; private set; }
    public string? DeviceCode { get; private set; }
    public string? VerificationUrl { get; private set; }
    public string? DeviceCodeMessage { get; private set; }
    public bool LoginInProgress { get; private set; }
    public string? LoginError { get; private set; }
    public string? TestEmailTo { get; set; }
    public string? Flash { get; private set; }
    public string? Error { get; private set; }

    public IActionResult OnGet(string? flash = null)
    {
        if (persona.Current is null) return RedirectToPage("/Persona");
        if (persona.Current.Role != Domain.UserRole.Admin)
            return RedirectToPage("/Index");

        Flash = flash;
        BindStatus();
        return Page();
    }

    public IActionResult OnPostStartLogin()
    {
        if (persona.Current?.Role != Domain.UserRole.Admin) return RedirectToPage("/Index");
        if (string.IsNullOrWhiteSpace(options.Value.Graph.ClientId))
            return RedirectToPage(new { flash = "ClientId empty — set Notifications:Graph:ClientId in appsettings." });

        tokens.StartInteractiveLogin();
        // Brief wait so device code is often available on first paint
        Thread.Sleep(800);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestEmailAsync(string? to)
    {
        if (persona.Current?.Role != Domain.UserRole.Admin) return RedirectToPage("/Index");
        try
        {
            var dest = string.IsNullOrWhiteSpace(to) ? "test@demo.local" : to.Trim();
            await email.SendAsync(new EmailMessage(
                dest,
                "[RLogistics] Test notification",
                $"Hello from RLogistics Core.\nMode: {options.Value.Mode}\nTime: {DateTime.UtcNow:u}",
                null, "Test", null, "Test"));
            return RedirectToPage(new { flash = $"Test email queued/sent to {dest} (mode {options.Value.Mode}). Check Email Outbox / Outlook." });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            BindStatus();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostTestTeamsAsync()
    {
        if (persona.Current?.Role != Domain.UserRole.Admin) return RedirectToPage("/Index");
        try
        {
            await teams.NotifyAsync(new TeamsMessage(
                "RLogistics test message",
                $"Hello from RLogistics Core at {DateTime.UtcNow:u}\nProvider: {options.Value.Teams.Provider}"));
            return RedirectToPage(new { flash = "Test Teams message written (and delivered if webhook/Graph is configured)." });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            BindStatus();
            return Page();
        }
    }

    private void BindStatus()
    {
        var o = options.Value;
        Mode = o.Mode;
        ResolvedMode = o.ResolvedMode.ToString();
        AlwaysAudit = o.AlwaysAuditToOutbox;
        NotifyTeams = o.NotifyTeamsOnEmail;
        ClientIdConfigured = !string.IsNullOrWhiteSpace(o.Graph.ClientId);
        SignedInUpn = tokens.LastSignedInUpn;
        TeamsProvider = o.Teams.Provider;
        WebhookConfigured = !string.IsNullOrWhiteSpace(o.Teams.IncomingWebhookUrl);
        DeviceCode = tokens.LastDeviceCode;
        VerificationUrl = tokens.LastVerificationUrl;
        DeviceCodeMessage = tokens.LastDeviceCodeMessage;
        LoginInProgress = tokens.LoginInProgress;
        LoginError = tokens.LastLoginError;
        TestEmailTo = "you@outlook.com";
    }
}
