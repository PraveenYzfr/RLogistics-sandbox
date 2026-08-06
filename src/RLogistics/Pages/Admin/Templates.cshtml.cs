using RLogistics.Data;
using RLogistics.Domain;
using RLogistics.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace RLogistics.Pages.Admin;

public class TemplatesModel(RLogisticsDbContext db, PersonaContext persona) : PageModel
{
    public List<EmailTemplate> Templates { get; private set; } = [];
    public string? EditingCode { get; private set; }
    public string? Flash { get; private set; }
    public string? Error { get; private set; }

    [BindProperty] public string Code { get; set; } = string.Empty;
    [BindProperty] public string SubjectTemplate { get; set; } = string.Empty;
    [BindProperty] public string BodyTemplate { get; set; } = string.Empty;
    [BindProperty] public bool IsActive { get; set; } = true;

    public async Task<IActionResult> OnGetAsync(string? edit = null, string? flash = null)
    {
        if (persona.Current?.Role != UserRole.Admin) return RedirectToPage("/Persona");
        Flash = flash;
        Templates = await db.EmailTemplates.AsNoTracking().OrderBy(t => t.Code).ToListAsync();
        if (!string.IsNullOrWhiteSpace(edit))
        {
            var t = Templates.FirstOrDefault(x => x.Code.Equals(edit, StringComparison.OrdinalIgnoreCase));
            if (t is not null)
            {
                EditingCode = t.Code;
                Code = t.Code;
                SubjectTemplate = t.SubjectTemplate;
                BodyTemplate = t.BodyTemplate;
                IsActive = t.IsActive;
            }
        }
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (persona.Current?.Role != UserRole.Admin) return RedirectToPage("/Persona");
        Code = (Code ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(SubjectTemplate) || string.IsNullOrWhiteSpace(BodyTemplate))
        {
            Error = "Code, subject, and body are required.";
            Templates = await db.EmailTemplates.AsNoTracking().OrderBy(t => t.Code).ToListAsync();
            return Page();
        }

        Code = new string(Code.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray());
        if (string.IsNullOrWhiteSpace(Code))
        {
            Error = "Template code must contain letters or numbers.";
            Templates = await db.EmailTemplates.AsNoTracking().OrderBy(t => t.Code).ToListAsync();
            return Page();
        }

        var t = await db.EmailTemplates.FirstOrDefaultAsync(x => x.Code == Code);
        var isNew = t is null;
        if (t is null)
        {
            t = new EmailTemplate { Code = Code };
            db.EmailTemplates.Add(t);
        }
        t.SubjectTemplate = SubjectTemplate.Trim();
        t.BodyTemplate = BodyTemplate;
        t.IsActive = IsActive;
        await db.SaveChangesAsync();
        var msg = isNew ? $"Created template '{Code}'." : $"Updated template '{Code}'.";
        return RedirectToPage(new { flash = msg });
    }

    public async Task<IActionResult> OnPostToggleAsync(string code)
    {
        if (persona.Current?.Role != UserRole.Admin) return RedirectToPage("/Persona");
        var t = await db.EmailTemplates.FirstOrDefaultAsync(x => x.Code == code);
        if (t is not null)
        {
            t.IsActive = !t.IsActive;
            await db.SaveChangesAsync();
            return RedirectToPage(new { flash = $"Template '{code}' is now {(t.IsActive ? "active" : "inactive")}." });
        }
        return RedirectToPage();
    }
}
