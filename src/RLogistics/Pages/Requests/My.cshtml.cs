using RLogistics.Abstractions;
using RLogistics.Contracts;
using RLogistics.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RLogistics.Pages.Requests;

public class MyModel(IRequestService requests, PersonaContext persona) : PageModel
{
    public List<RequestSummaryDto> Items { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (persona.Current is null) return RedirectToPage("/Persona");
        // Force "my" view: for coord/admin, filter to requestor = me by temporarily using user-scoped list
        // RequestService List for User role filters by requestor; for coord it shows all.
        // So query via status null and filter client-side for owned.
        var all = await requests.ListAsync(null, false);
        Items = all.Where(i => i.RequestorEmail == persona.Current.Email).ToList();
        return Page();
    }
}
