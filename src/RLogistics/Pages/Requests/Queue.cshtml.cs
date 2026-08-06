using RLogistics.Abstractions;
using RLogistics.Contracts;
using RLogistics.Domain;
using RLogistics.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RLogistics.Pages.Requests;

public class QueueModel(IRequestService requests, PersonaContext persona) : PageModel
{
    public List<RequestSummaryDto> Items { get; private set; } = [];
    public bool AssignedToMeOnly { get; private set; }

    public async Task<IActionResult> OnGetAsync(bool assignedToMe = false)
    {
        if (persona.Current is null) return RedirectToPage("/Persona");
        if (persona.Current.Role is UserRole.User) return RedirectToPage("My");
        AssignedToMeOnly = assignedToMe;
        Items = await requests.ListAsync(null, assignedToMe);
        return Page();
    }
}
