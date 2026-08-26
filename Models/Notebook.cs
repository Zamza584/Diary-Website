namespace NoteKeeping.Models;

public class Notebook
{
    public int Id { get; set; }
    public int UserAccountId { get; set; }
    public UserAccount? UserAccount { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public List<Note> Notes { get; set; } = new List<Note>();
}

