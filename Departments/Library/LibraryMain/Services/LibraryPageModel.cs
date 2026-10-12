using CampusSystem.Data.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryMain.Services;

/// <summary>Who is signed in, and a way to run an action and show its outcome on the next page load.</summary>
public abstract class LibraryPageModel(LibraryDesk desk) : PageModel
{
    protected LibraryDesk Desk { get; } = desk;

    public int UserId => User.GetUserId() ?? 0;
    public bool IsStaff => User.GetRole() is "librarian" or "admin";
    public bool IsStudent => User.GetRole() == "student";

    /// <summary>Students see only their own records. Staff see everyone's (null).</summary>
    protected int? OwnerFilter => IsStudent ? UserId : null;

    [TempData] public string? Message { get; set; }
    [TempData] public bool IsError { get; set; }

    protected void Run(Action action, string success)
    {
        try
        {
            action();
            Message = success;
            IsError = false;
        }
        catch (CampusSystem.Data.Json.LibraryException ex)
        {
            Message = ex.Message;
            IsError = true;
        }
    }

    protected void Fail(string message)
    {
        Message = message;
        IsError = true;
    }
}
