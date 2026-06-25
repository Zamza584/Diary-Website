
public class Notebook
{
    public string Title { get; set; } = string.Empty;
    public DateTime Date = DateTime.Today;
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public List<Note> Notes { get; set; } = new List<Note>();
}


