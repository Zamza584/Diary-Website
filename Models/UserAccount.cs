namespace NoteKeeping.Models;

public class UserAccount
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool AgeConfirmed { get; set; }
    public DateTime? AgeConfirmedAt { get; set; }
    public bool PrivacyPolicyAccepted { get; set; }
    public DateTime? PrivacyPolicyAcceptedAt { get; set; }
    public bool TermsAccepted { get; set; }
    public DateTime? TermsAcceptedAt { get; set; }
    public bool InternationalTransferAcknowledged { get; set; }
    public DateTime? InternationalTransferAcknowledgedAt { get; set; }
    public List<Notebook> Notebooks { get; set; } = new();
}
