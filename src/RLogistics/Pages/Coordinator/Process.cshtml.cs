using System.Text.Json;
using RLogistics.Abstractions;
using RLogistics.Contracts;
using RLogistics.Domain;
using RLogistics.Genie;
using RLogistics.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RLogistics.Pages.Coordinator;

public class ProcessModel(IRequestService requests, PersonaContext persona, IGenieClient genie) : PageModel
{
    public RequestDetailDto? Item { get; private set; }
    public List<VendorDto> TransportVendors { get; private set; } = [];
    public List<VendorDto> ProcessingVendors { get; private set; } = [];
    public string[] Slots { get; } = PickupSlots.All;
    public string? Error { get; private set; }
    public string? Flash { get; private set; }
    public string? GenieJson { get; private set; }
    public string? GenieError { get; private set; }

    [BindProperty] public RequestStatus NewStatus { get; set; }
    [BindProperty] public string? StatusNotes { get; set; }
    [BindProperty] public string? ClarificationQuestion { get; set; }
    [BindProperty] public string? Notes { get; set; }
    [BindProperty] public string? CoordinatorNotes { get; set; }
    [BindProperty] public string? PickupInstructions { get; set; }
    [BindProperty] public int? TransportVendorId { get; set; }
    [BindProperty] public int? ProcessingVendorId { get; set; }
    [BindProperty] public DateTime? ScheduledPickupDate { get; set; }
    [BindProperty] public string? ScheduledPickupSlot { get; set; }
    [BindProperty] public DateTime? ExpectedDeviceReturnDate { get; set; }
    [BindProperty] public bool ConfirmPickupScheduled { get; set; } = true;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (persona.Current is null) return RedirectToPage("/Persona");
        if (persona.Current.Role is UserRole.User) return RedirectToPage("/Requests/Detail", new { id });

        try
        {
            await LoadAsync(id);
            if (Item is null) return NotFound();
            return Page();
        }
        catch (UnauthorizedAccessException ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostClaimAsync(int id)
    {
        try
        {
            await requests.AssignAsync(id, new AssignRequestDto(null));
            Flash = "Claimed — assigned to you.";
        }
        catch (Exception ex) { Error = ex.Message; }
        return await OnGetAsync(id);
    }

    public async Task<IActionResult> OnPostStatusAsync(int id)
    {
        try
        {
            await requests.UpdateStatusAsync(id, new UpdateStatusDto(NewStatus, StatusNotes));
            Flash = "Status updated — notification emailed (see Outbox).";
        }
        catch (Exception ex) { Error = ex.Message; }
        return await OnGetAsync(id);
    }

    public async Task<IActionResult> OnPostClarifyAsync(int id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ClarificationQuestion))
                throw new InvalidOperationException("Enter a question for the requestor.");
            await requests.AddClarificationAsync(id, new ClarificationDto(ClarificationQuestion));
            Flash = "Query sent to requestor (mock email).";
        }
        catch (Exception ex) { Error = ex.Message; }
        return await OnGetAsync(id);
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id)
    {
        try
        {
            await requests.UpdateFieldsAsync(id, new UpdateRequestFieldsDto(Notes, CoordinatorNotes, PickupInstructions));
            Flash = "Request fields updated.";
        }
        catch (Exception ex) { Error = ex.Message; }
        return await OnGetAsync(id);
    }

    public async Task<IActionResult> OnPostPlanAsync(int id)
    {
        try
        {
            await requests.PlanAsync(id, new PlanRequestDto(
                TransportVendorId,
                ProcessingVendorId,
                ScheduledPickupDate,
                ScheduledPickupSlot,
                ConfirmPickupScheduled,
                ExpectedDeviceReturnDate));
            Flash = "Vendor / pickup plan saved.";
        }
        catch (Exception ex) { Error = ex.Message; }
        return await OnGetAsync(id);
    }

    public async Task<IActionResult> OnPostQuotesAsync(int id)
    {
        try
        {
            await requests.PlanAsync(id, new PlanRequestDto(
                TransportVendorId,
                ProcessingVendorId,
                ScheduledPickupDate,
                ScheduledPickupSlot,
                false,
                ExpectedDeviceReturnDate));
            var result = await requests.RequestVendorQuotesAsync(id);
            Flash = $"Quote emails: {result.Sent} sent. {result.Detail}";
        }
        catch (Exception ex) { Error = ex.Message; }
        return await OnGetAsync(id);
    }

    public async Task<IActionResult> OnPostReturnReminderAsync(int id)
    {
        try
        {
            await requests.PlanAsync(id, new PlanRequestDto(
                TransportVendorId,
                ProcessingVendorId,
                ScheduledPickupDate,
                ScheduledPickupSlot,
                false,
                ExpectedDeviceReturnDate));
            await requests.SendDeviceReturnReminderAsync(id);
            Flash = "Device return reminder emailed to contact / requestor / coordinator.";
        }
        catch (Exception ex) { Error = ex.Message; }
        return await OnGetAsync(id);
    }

    public async Task<IActionResult> OnPostGenieRefreshAsync(int id)
    {
        await LoadAsync(id);
        await LoadGenieAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostGenieUseDraftAsync(int id)
    {
        try
        {
            var intake = await genie.GetIntakeAsync(id);
            if (intake is JsonElement je &&
                je.TryGetProperty("clarification_draft", out var draft))
            {
                ClarificationQuestion = draft.GetString();
                Flash = "GENIE clarification draft loaded into Query form — review then Send query.";
            }
            else
            {
                Flash = "GENIE draft unavailable. Is GENIE running on :8090?";
            }
        }
        catch (Exception ex) { Error = ex.Message; }
        return await OnGetAsync(id);
    }

    private async Task LoadAsync(int id)
    {
        Item = await requests.GetAsync(id);
        if (Item is null) return;

        NewStatus = Item.Status;
        Notes = Item.Notes;
        CoordinatorNotes = Item.CoordinatorNotes;
        PickupInstructions = Item.PickupInstructions;
        TransportVendorId = Item.TransportVendorId;
        ProcessingVendorId = Item.ProcessingVendorId;
        ScheduledPickupDate = Item.ScheduledPickupDate;
        ScheduledPickupSlot = Item.ScheduledPickupSlot;
        ExpectedDeviceReturnDate = Item.ExpectedDeviceReturnDate;

        TransportVendors = await requests.ListVendorsAsync(VendorType.Transport);
        ProcessingVendors = await requests.ListVendorsAsync(VendorType.Processing);
        await LoadGenieAsync(id);
    }

    private async Task LoadGenieAsync(int id)
    {
        try
        {
            var intake = await genie.GetIntakeAsync(id);
            GenieJson = JsonSerializer.Serialize(intake, new JsonSerializerOptions { WriteIndented = true });
            if (GenieJson.Contains("\"error\"", StringComparison.OrdinalIgnoreCase) &&
                GenieJson.Length < 400)
                GenieError = "GENIE may be offline — start with infra/docker-compose or uvicorn.";
        }
        catch (Exception ex)
        {
            GenieError = ex.Message;
            GenieJson = null;
        }
    }
}
