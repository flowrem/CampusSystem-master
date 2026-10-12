using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TestingMain.Services;

namespace TestingMain.Pages;

public class IndexModel(JsonCollectionBrowser browser) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Collection { get; set; }

    [BindProperty]
    public string NewRowJson { get; set; } = "";

    public IReadOnlyList<CollectionInfo> Collections { get; private set; } = [];
    public CollectionView? View { get; private set; }
    public string FilePath => browser.FilePath;
    public int SchemaVersion => browser.SchemaVersion;

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public bool IsSuccess { get; set; }

    public void OnGet() => Load();

    public IActionResult OnPostAdd()
    {
        return Run(() => $"Added row {browser.Add(Collection ?? "", NewRowJson)} to {Collection}.");
    }

    public IActionResult OnPostUpdateRow(int rowId)
    {
        // Form keys are the row's column names. Everything else on the form is routing data.
        var reserved = new HashSet<string>(["rowId", "Collection", "__RequestVerificationToken"], StringComparer.OrdinalIgnoreCase);
        var values = Request.Form.Where(pair => !reserved.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
        return Run(() => { browser.Update(Collection ?? "", rowId, values); return $"Row {rowId} updated in {Collection}."; });
    }

    public IActionResult OnPostDeleteRow(int rowId)
    {
        return Run(() => { browser.Delete(Collection ?? "", rowId); return $"Row {rowId} deleted from {Collection}."; });
    }

    private IActionResult Run(Func<string> action)
    {
        try
        {
            Message = action();
            IsSuccess = true;
        }
        catch (BrowserException ex)
        {
            Message = ex.Message;
            IsSuccess = false;
        }
        return RedirectToPage(new { Collection });
    }

    private void Load()
    {
        Collections = browser.ListCollections();
        if (string.IsNullOrWhiteSpace(Collection)) Collection = Collections.FirstOrDefault()?.Name;
        if (Collection is null) return;

        try { View = browser.Read(Collection); }
        catch (BrowserException ex) { Message = ex.Message; IsSuccess = false; }
    }
}
