using RLogistics.Data;
using RLogistics.Domain;
using RLogistics.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace RLogistics.Pages.Teams;

public class OutboxModel(RLogisticsDbContext db, PersonaContext persona) : PageModel
{
    public List<TeamsOutbox> Items { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (persona.Current is null) return RedirectToPage("/Persona");
        Items = await db.TeamsOutbox.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Take(150)
            .ToListAsync();
        return Page();
    }
}
