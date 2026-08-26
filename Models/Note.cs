namespace NoteKeeping.Models;

public class Note
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
    public int NotebookId { get; set; }
    public Notebook? Notebook { get; set; }
}
