using RLogistics.Abstractions;
using RLogistics.Contracts;
using RLogistics.Domain;
using RLogistics.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RLogistics.Pages.Coordinator;

public class DashboardModel(IRequestService requests, PersonaContext persona) : PageModel
{
    public string CoordinatorName { get; private set; } = "";
    public Dictionary<string, int> StatusCounts { get; private set; } = new();
    public int AssignedToMeCount { get; private set; }
    public int UnassignedCount { get; private set; }
    public int OpenCount { get; private set; }
    public List<RequestSummaryDto> MyGrid { get; private set; } = [];
    public List<RequestSummaryDto> UnassignedQueue { get; private set; } = [];
    public List<RequestSummaryDto> AllActive { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (persona.Current is null) return RedirectToPage("/Persona");
        if (persona.Current.Role is UserRole.User) return RedirectToPage("/Requests/My");

        CoordinatorName = persona.Current.DisplayName;
        var all = await requests.ListAsync(null, false);
        var closed = new[] { RequestStatus.Delivered, RequestStatus.Cancelled };

        AllActive = all.Where(r => !closed.Contains(r.Status)).ToList();
        MyGrid = AllActive
            .Where(r => r.AssignedCoordinatorEmail == persona.Current.Email)
            .OrderByDescending(r => r.CreatedAt)
            .ToList();
        UnassignedQueue = AllActive
            .Where(r => r.AssignedCoordinatorEmail is null)
            .OrderBy(r => r.CreatedAt)
            .ToList();

        AssignedToMeCount = MyGrid.Count;
        UnassignedCount = UnassignedQueue.Count;
        OpenCount = AllActive.Count;

        StatusCounts = Enum.GetValues<RequestStatus>()
            .ToDictionary(
                s => s.ToString(),
                s => all.Count(r => r.Status == s));

        return Page();
    }
}
