using RLogistics.Data;
using RLogistics.Domain;
using RLogistics.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace RLogistics.Pages.Admin;

public class ConfigModel(RLogisticsDbContext db, PersonaContext persona) : PageModel
{
    public List<AppConfig> Items { get; private set; } = [];
    public string? EditingKey { get; private set; }
    public string? Flash { get; private set; }
    public string? Error { get; private set; }

    [BindProperty] public string Key { get; set; } = string.Empty;
    [BindProperty] public string Value { get; set; } = string.Empty;
    [BindProperty] public string? Description { get; set; }

    public async Task<IActionResult> OnGetAsync(string? edit = null, string? flash = null)
    {
        if (persona.Current?.Role != UserRole.Admin) return RedirectToPage("/Persona");
        Flash = flash;
        Items = await db.AppConfigs.AsNoTracking().OrderBy(c => c.Key).ToListAsync();
        if (!string.IsNullOrWhiteSpace(edit))
        {
            var c = Items.FirstOrDefault(x => x.Key.Equals(edit, StringComparison.OrdinalIgnoreCase));
            if (c is not null)
            {
                EditingKey = c.Key;
                Key = c.Key;
                Value = c.Value;
                Description = c.Description;
            }
        }
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (persona.Current?.Role != UserRole.Admin) return RedirectToPage("/Persona");
        Key = (Key ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(Key) || Value is null)
        {
            Error = "Key and value are required.";
            Items = await db.AppConfigs.AsNoTracking().OrderBy(c => c.Key).ToListAsync();
            return Page();
        }

        var c = await db.AppConfigs.FirstOrDefaultAsync(x => x.Key == Key);
        var isNew = c is null;
        if (c is null)
        {
            c = new AppConfig { Key = Key };
            db.AppConfigs.Add(c);
        }
        c.Value = Value;
        c.Description = Description;
        await db.SaveChangesAsync();
        var msg = isNew ? $"Created config '{Key}'." : $"Updated config '{Key}'.";
        return RedirectToPage(new { flash = msg });
    }

    public async Task<IActionResult> OnPostDeleteAsync(string key)
    {
        if (persona.Current?.Role != UserRole.Admin) return RedirectToPage("/Persona");
        if (string.Equals(key, "WorkflowStatusV2", StringComparison.OrdinalIgnoreCase))
            return RedirectToPage(new { flash = "System key WorkflowStatusV2 cannot be deleted." });

        var c = await db.AppConfigs.FirstOrDefaultAsync(x => x.Key == key);
        if (c is not null)
        {
            db.AppConfigs.Remove(c);
            await db.SaveChangesAsync();
            return RedirectToPage(new { flash = $"Deleted '{key}'." });
        }
        return RedirectToPage();
    }
}
