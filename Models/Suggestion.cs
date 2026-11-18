namespace NoteKeeping.Models;

public class Suggestion
{
    public const int MaxMessageLength = 5000;

    public int Id { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
