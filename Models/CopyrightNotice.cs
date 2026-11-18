namespace NoteKeeping.Models;

public class CopyrightNotice
{
    public int Id { get; set; }
    public string ReporterName { get; set; } = string.Empty;
    public string ReporterEmail { get; set; } = string.Empty;
    public string CopyrightWork { get; set; } = string.Empty;
    public string InfringingUrl { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public bool AccuracyDeclarationAccepted { get; set; }
    public bool AuthorityDeclarationAccepted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Received";
}
