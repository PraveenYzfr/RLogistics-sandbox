using RLogistics.Data;
using RLogistics.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace RLogistics.Pages;

public class IndexModel(RLogisticsDbContext db, PersonaContext persona) : PageModel
{
    public int RequestCount { get; private set; }
    public int OutboxCount { get; private set; }
    public int TeamsOutboxCount { get; private set; }
    public string? PersonaName => persona.Current?.DisplayName;

    public async Task OnGetAsync()
    {
        RequestCount = await db.Requests.CountAsync();
        OutboxCount = await db.EmailOutbox.CountAsync();
        TeamsOutboxCount = await db.TeamsOutbox.CountAsync();
    }
}
