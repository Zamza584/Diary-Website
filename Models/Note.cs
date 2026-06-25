
public class Note
{
    public string Text { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
    public int NotebookId { get; set; }
    public Notebook? Notebook { get; set; }

}
