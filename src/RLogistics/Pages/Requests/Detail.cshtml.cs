using RLogistics.Abstractions;
using RLogistics.Contracts;
using RLogistics.Domain;
using RLogistics.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RLogistics.Pages.Requests;

public class DetailModel(IRequestService requests, PersonaContext persona) : PageModel
{
    public RequestDetailDto? Item { get; private set; }
    public string? Error { get; private set; }
    public string? Flash { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (persona.Current is null) return RedirectToPage("/Persona");
        if (persona.Current.Role is UserRole.Coordinator or UserRole.Admin)
            return RedirectToPage("/Coordinator/Process", new { id });
        try
        {
            Item = await requests.GetAsync(id);
            return Item is null ? NotFound() : Page();
        }
        catch (UnauthorizedAccessException ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostReplyAsync(int id, int clarificationId, string response)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(response))
                throw new InvalidOperationException("Response required.");
            await requests.ReplyClarificationAsync(id, clarificationId, response);
            Flash = "Reply sent. Request moved back to Assigned if it was On Hold.";
        }
        catch (Exception ex) { Error = ex.Message; }
        return await OnGetAsync(id);
    }
}
