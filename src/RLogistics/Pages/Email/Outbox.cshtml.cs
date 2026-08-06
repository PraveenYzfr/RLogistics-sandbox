using RLogistics.Abstractions;
using RLogistics.Data;
using RLogistics.Domain;
using RLogistics.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace RLogistics.Pages.Email;

public class OutboxModel(RLogisticsDbContext db, PersonaContext persona, IRequestService requests) : PageModel
{
    public List<EmailOutbox> Items { get; private set; } = [];
    public bool CanRunReminders { get; private set; }
    public string? Flash { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? flash = null)
    {
        if (persona.Current is null) return RedirectToPage("/Persona");
        Flash = flash;
        var me = persona.Current;
        CanRunReminders = me.Role is UserRole.Coordinator or UserRole.Admin;
        var q = db.EmailOutbox.AsNoTracking().AsQueryable();
        if (me.Role == UserRole.User)
            q = q.Where(e => e.ToAddress == me.Email);
        Items = await q.OrderByDescending(e => e.CreatedAt).Take(150).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRunRemindersAsync()
    {
        if (persona.Current is null) return RedirectToPage("/Persona");
        try
        {
            var n = await requests.RunOverdueDeviceReturnRemindersAsync();
            return RedirectToPage(new { flash = $"Sent return reminders for {n} overdue request(s)." });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return await OnGetAsync();
        }
    }
}
