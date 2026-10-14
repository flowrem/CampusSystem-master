using LibraryMain.Services;

namespace LibraryMain.Pages;

public class IndexModel(LibraryDesk desk) : LibraryPageModel(desk)
{
    public LibraryDesk.Stats Stats { get; private set; } = new(0, 0, 0, 0, 0, 0);
    public int MyOpenLoans { get; private set; }
    public decimal MyOutstanding { get; private set; }
    public IReadOnlyList<LibraryDesk.BorrowerRow> CurrentBorrowers { get; private set; } = [];
    public IReadOnlyList<LibraryDesk.RecentReturnRow> RecentReturns { get; private set; } = [];

    public void OnGet()
    {
        Stats = Desk.GetStats();
        if (IsStudent)
        {
            MyOpenLoans = Desk.Loans(UserId, "open", null).Count;
            MyOutstanding = Desk.Outstanding(UserId);
        }
        else if (IsStaff)
        {
            CurrentBorrowers = Desk.CurrentBorrowers();
            RecentReturns = Desk.RecentReturns();
        }
    }
}
