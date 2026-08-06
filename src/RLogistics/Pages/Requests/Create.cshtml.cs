using System.Text.Json;
using RLogistics.Abstractions;
using RLogistics.Contracts;
using RLogistics.Domain;
using RLogistics.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RLogistics.Pages.Requests;

public class CreateModel(IRequestService requests, PersonaContext persona) : PageModel
{
    private const string DraftKey = "mdt_create_draft";

    public int Step { get; private set; } = 1;
    public string? Error { get; private set; }
    public CreateDraft Draft { get; private set; } = new();

    // Step 1 — contact
    [BindProperty] public string ContactName { get; set; } = string.Empty;
    [BindProperty] public string ContactEmail { get; set; } = string.Empty;
    [BindProperty] public string? ContactPhone { get; set; }
    [BindProperty] public string? ContactDepartment { get; set; }

    // Step 2 — pickup
    [BindProperty] public string PickupAddressLine1 { get; set; } = string.Empty;
    [BindProperty] public string? PickupAddressLine2 { get; set; }
    [BindProperty] public string PickupCity { get; set; } = string.Empty;
    [BindProperty] public string? PickupState { get; set; }
    [BindProperty] public string? PickupPostalCode { get; set; }
    [BindProperty] public string PickupCountry { get; set; } = "USA";
    [BindProperty] public DateTime? PreferredPickupDate { get; set; }
    [BindProperty] public string? PickupInstructions { get; set; }

    // Step 3 — facility
    [BindProperty] public string Site { get; set; } = string.Empty;
    [BindProperty] public string? FacilityCode { get; set; }
    [BindProperty] public string? Building { get; set; }
    [BindProperty] public string? Floor { get; set; }
    [BindProperty] public string? Room { get; set; }
    [BindProperty] public DispositionType DispositionType { get; set; } = DispositionType.Sanitize;
    [BindProperty] public RequestType RequestType { get; set; } = RequestType.UsSurplus;
    [BindProperty] public string? Notes { get; set; }

    // Step 4 — equipment line (add one at a time)
    [BindProperty] public string AssetType { get; set; } = "Laptop";
    [BindProperty] public string? Manufacturer { get; set; }
    [BindProperty] public string? Model { get; set; }
    [BindProperty] public string? SerialNumber { get; set; }
    [BindProperty] public string? DeviceGuid { get; set; }
    [BindProperty] public string? AssetTag { get; set; }
    [BindProperty] public int Quantity { get; set; } = 1;
    [BindProperty] public string? Condition { get; set; }
    [BindProperty] public int? RemoveIndex { get; set; }

    public IActionResult OnGet(int step = 1)
    {
        if (persona.Current is null) return RedirectToPage("/Persona");
        Draft = LoadDraft();
        if (Draft.ContactEmail == string.Empty && persona.Current is not null)
        {
            Draft.ContactName = persona.Current.DisplayName;
            Draft.ContactEmail = persona.Current.Email;
            SaveDraft(Draft);
        }
        ApplyDraftToBindings(Draft);
        Step = Math.Clamp(step, 1, 5);
        return Page();
    }

    public IActionResult OnPostContact(int step)
    {
        if (persona.Current is null) return RedirectToPage("/Persona");
        Draft = LoadDraft();
        if (string.IsNullOrWhiteSpace(ContactName) || string.IsNullOrWhiteSpace(ContactEmail))
        {
            Error = "Name and email are required.";
            Step = 1;
            return Page();
        }
        Draft.ContactName = ContactName.Trim();
        Draft.ContactEmail = ContactEmail.Trim();
        Draft.ContactPhone = ContactPhone;
        Draft.ContactDepartment = ContactDepartment;
        SaveDraft(Draft);
        return RedirectToPage(new { step = 2 });
    }

    public IActionResult OnPostPickup(int step)
    {
        if (persona.Current is null) return RedirectToPage("/Persona");
        Draft = LoadDraft();
        if (string.IsNullOrWhiteSpace(PickupAddressLine1) || string.IsNullOrWhiteSpace(PickupCity))
        {
            Error = "Pickup address line 1 and city are required.";
            ApplyDraftToBindings(Draft);
            ContactName = Draft.ContactName; ContactEmail = Draft.ContactEmail;
            Step = 2;
            return Page();
        }
        Draft.PickupAddressLine1 = PickupAddressLine1.Trim();
        Draft.PickupAddressLine2 = PickupAddressLine2;
        Draft.PickupCity = PickupCity.Trim();
        Draft.PickupState = PickupState;
        Draft.PickupPostalCode = PickupPostalCode;
        Draft.PickupCountry = string.IsNullOrWhiteSpace(PickupCountry) ? "USA" : PickupCountry.Trim();
        Draft.PreferredPickupDate = PreferredPickupDate;
        Draft.PickupInstructions = PickupInstructions;
        SaveDraft(Draft);
        return RedirectToPage(new { step = 3 });
    }

    public IActionResult OnPostFacility(int step)
    {
        if (persona.Current is null) return RedirectToPage("/Persona");
        Draft = LoadDraft();
        if (string.IsNullOrWhiteSpace(Site))
        {
            Error = "Facility name is required.";
            ApplyDraftToBindings(Draft);
            Step = 3;
            return Page();
        }
        Draft.Site = Site.Trim();
        Draft.FacilityCode = FacilityCode;
        Draft.Building = Building;
        Draft.Floor = Floor;
        Draft.Room = Room;
        Draft.DispositionType = DispositionType;
        Draft.RequestType = RequestType;
        Draft.Notes = Notes;
        SaveDraft(Draft);
        return RedirectToPage(new { step = 4 });
    }

    public IActionResult OnPostAddEquipment()
    {
        if (persona.Current is null) return RedirectToPage("/Persona");
        Draft = LoadDraft();
        if (string.IsNullOrWhiteSpace(AssetType))
        {
            Error = "Equipment type is required.";
            ApplyDraftToBindings(Draft);
            Step = 4;
            return Page();
        }
        if (string.IsNullOrWhiteSpace(DeviceGuid))
        {
            Error = "Device GUID is required for each equipment line.";
            ApplyDraftToBindings(Draft);
            Step = 4;
            return Page();
        }
        if (string.IsNullOrWhiteSpace(Manufacturer) || string.IsNullOrWhiteSpace(Model))
        {
            Error = "Manufacturer and Model are required for each equipment line.";
            ApplyDraftToBindings(Draft);
            Step = 4;
            return Page();
        }
        Draft.Assets.Add(new CreateDraftAsset
        {
            AssetType = AssetType.Trim(),
            Manufacturer = Manufacturer.Trim(),
            Model = Model.Trim(),
            SerialNumber = SerialNumber,
            DeviceGuid = DeviceGuid.Trim(),
            AssetTag = AssetTag,
            Quantity = Quantity <= 0 ? 1 : Quantity,
            Condition = Condition
        });
        SaveDraft(Draft);
        return RedirectToPage(new { step = 4 });
    }

    public IActionResult OnPostRemoveEquipment()
    {
        Draft = LoadDraft();
        if (RemoveIndex is int i && i >= 0 && i < Draft.Assets.Count)
            Draft.Assets.RemoveAt(i);
        SaveDraft(Draft);
        return RedirectToPage(new { step = 4 });
    }

    public IActionResult OnPostToReview()
    {
        Draft = LoadDraft();
        if (Draft.Assets.Count == 0)
        {
            Error = "Add at least one equipment line before continuing.";
            ApplyDraftToBindings(Draft);
            Step = 4;
            return Page();
        }
        return RedirectToPage(new { step = 5 });
    }

    public async Task<IActionResult> OnPostSubmitAsync()
    {
        if (persona.Current is null) return RedirectToPage("/Persona");
        Draft = LoadDraft();
        if (Draft.Assets.Count == 0)
        {
            Error = "Add at least one equipment line.";
            Step = 4;
            ApplyDraftToBindings(Draft);
            return Page();
        }

        try
        {
            var created = await requests.CreateAsync(new CreateRequestDto(
                null,
                Draft.ContactName,
                Draft.ContactEmail,
                Draft.ContactPhone,
                Draft.ContactDepartment,
                Draft.Site,
                Draft.FacilityCode,
                Draft.Building,
                Draft.Floor,
                Draft.Room,
                Draft.PickupAddressLine1,
                Draft.PickupAddressLine2,
                Draft.PickupCity,
                Draft.PickupState,
                Draft.PickupPostalCode,
                Draft.PickupCountry,
                Draft.PreferredPickupDate,
                Draft.PickupInstructions,
                Draft.DispositionType,
                Draft.RequestType,
                Draft.Notes,
                Draft.Assets.Select(a => new AssetDto(a.AssetType, a.SerialNumber, a.Quantity, a.Manufacturer, a.Model, a.AssetTag, a.Condition, a.DeviceGuid)).ToList()));
            HttpContext.Session.Remove(DraftKey);
            return RedirectToPage("Detail", new { id = created.Id });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            ApplyDraftToBindings(Draft);
            Step = 5;
            return Page();
        }
    }

    private CreateDraft LoadDraft()
    {
        var json = HttpContext.Session.GetString(DraftKey);
        if (string.IsNullOrEmpty(json)) return new CreateDraft();
        return JsonSerializer.Deserialize<CreateDraft>(json) ?? new CreateDraft();
    }

    private void SaveDraft(CreateDraft draft) =>
        HttpContext.Session.SetString(DraftKey, JsonSerializer.Serialize(draft));

    private void ApplyDraftToBindings(CreateDraft d)
    {
        ContactName = d.ContactName;
        ContactEmail = d.ContactEmail;
        ContactPhone = d.ContactPhone;
        ContactDepartment = d.ContactDepartment;
        PickupAddressLine1 = d.PickupAddressLine1;
        PickupAddressLine2 = d.PickupAddressLine2;
        PickupCity = d.PickupCity;
        PickupState = d.PickupState;
        PickupPostalCode = d.PickupPostalCode;
        PickupCountry = d.PickupCountry;
        PreferredPickupDate = d.PreferredPickupDate;
        PickupInstructions = d.PickupInstructions;
        Site = d.Site;
        FacilityCode = d.FacilityCode;
        Building = d.Building;
        Floor = d.Floor;
        Room = d.Room;
        DispositionType = d.DispositionType;
        RequestType = d.RequestType;
        Notes = d.Notes;
    }
}

public class CreateDraft
{
    public string ContactName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string? ContactDepartment { get; set; }
    public string PickupAddressLine1 { get; set; } = string.Empty;
    public string? PickupAddressLine2 { get; set; }
    public string PickupCity { get; set; } = string.Empty;
    public string? PickupState { get; set; }
    public string? PickupPostalCode { get; set; }
    public string PickupCountry { get; set; } = "USA";
    public DateTime? PreferredPickupDate { get; set; }
    public string? PickupInstructions { get; set; }
    public string Site { get; set; } = string.Empty;
    public string? FacilityCode { get; set; }
    public string? Building { get; set; }
    public string? Floor { get; set; }
    public string? Room { get; set; }
    public DispositionType DispositionType { get; set; } = DispositionType.Sanitize;
    public RequestType RequestType { get; set; } = RequestType.UsSurplus;
    public string? Notes { get; set; }
    public List<CreateDraftAsset> Assets { get; set; } = [];
}

public class CreateDraftAsset
{
    public string AssetType { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? DeviceGuid { get; set; }
    public string? AssetTag { get; set; }
    public int Quantity { get; set; } = 1;
    public string? Condition { get; set; }
}
