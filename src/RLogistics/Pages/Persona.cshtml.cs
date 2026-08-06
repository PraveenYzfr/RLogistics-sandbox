using RLogistics.Data;
using RLogistics.Domain;
using RLogistics.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace RLogistics.Pages;

public class PersonaModel(RLogisticsDbContext db) : PageModel
{
    public List<AppUser> Users { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Users = await db.Users.AsNoTracking().OrderBy(u => u.Role).ThenBy(u => u.DisplayName).ToListAsync();
    }

    public async Task<IActionResult> OnPostAsync(int userId)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return RedirectToPage();

        Response.Cookies.Append(PersonaMiddleware.CookieName, user.Id.ToString(), new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        });
        return RedirectToPage("/Index");
    }
}
