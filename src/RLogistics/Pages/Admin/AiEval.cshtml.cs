using System.Text.Json;
using RLogistics.Genie;
using RLogistics.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RLogistics.Pages.Admin;

public class AiEvalModel(PersonaContext persona, IGenieClient genie) : PageModel
{
    public object? Metrics { get; private set; }
    public object? Cases { get; private set; }
    public object? Usage { get; private set; }
    public string? Flash { get; private set; }
    public string? Error { get; private set; }
    public string CasesJson { get; private set; } = "[]";
    public string MetricsJson { get; private set; } = "{}";
    public string UsageJson { get; private set; } = "{}";

    public async Task<IActionResult> OnGetAsync(string? flash = null)
    {
        if (persona.Current is null) return RedirectToPage("/Persona");
        if (persona.Current.Role is not (Domain.UserRole.Admin or Domain.UserRole.Coordinator))
            return RedirectToPage("/Index");

        Flash = flash;
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSmeAsync(
        string caseId,
        double score,
        bool passed,
        string? notes)
    {
        if (persona.Current is null) return RedirectToPage("/Persona");
        if (persona.Current.Role is not (Domain.UserRole.Admin or Domain.UserRole.Coordinator))
            return RedirectToPage("/Index");

        if (string.IsNullOrWhiteSpace(caseId))
            return RedirectToPage(new { flash = "Missing case id." });

        var reviewer = persona.Current.Email ?? persona.Current.DisplayName ?? "sme";
        var result = await genie.SubmitSmeScoreAsync(caseId.Trim(), score, passed, notes ?? "", reviewer);
        if (result is null)
            return RedirectToPage(new { flash = "GENIE returned empty response." });

        var json = JsonSerializer.Serialize(result);
        if (json.Contains("\"error\"", StringComparison.OrdinalIgnoreCase))
        {
            Error = json;
            await LoadAsync();
            return Page();
        }

        return RedirectToPage(new { flash = $"SME score saved for {caseId}." });
    }

    private async Task LoadAsync()
    {
        Metrics = await genie.GetEvalMetricsAsync();
        Cases = await genie.GetEvalCasesAsync(pendingSme: true);
        Usage = await genie.GetObservabilitySummaryAsync();
        MetricsJson = JsonSerializer.Serialize(Metrics, new JsonSerializerOptions { WriteIndented = true });
        CasesJson = JsonSerializer.Serialize(Cases, new JsonSerializerOptions { WriteIndented = true });
        UsageJson = JsonSerializer.Serialize(Usage, new JsonSerializerOptions { WriteIndented = true });
    }
}
