using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NoteKeeping.Models;
using NoteKeeping.Services;

namespace NoteKeeping.Data;

public class NoteKeepingDbContext : DbContext
{
    private readonly NoteTextEncryption _noteTextEncryption;
    private readonly NotebookTitleEncryption _notebookTitleEncryption;

    public NoteKeepingDbContext(
        DbContextOptions<NoteKeepingDbContext> options,
        NoteTextEncryption noteTextEncryption,
        NotebookTitleEncryption notebookTitleEncryption)
        : base(options)
    {
        _noteTextEncryption = noteTextEncryption;
        _notebookTitleEncryption = notebookTitleEncryption;
    }

    public DbSet<Notebook> Notebooks => Set<Notebook>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<CopyrightNotice> CopyrightNotices => Set<CopyrightNotice>();
    public DbSet<Suggestion> Suggestions => Set<Suggestion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var noteTextEncryption = _noteTextEncryption;
        var notebookTitleEncryption = _notebookTitleEncryption;

        modelBuilder.Entity<Notebook>(entity =>
        {
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Title)
                .HasConversion(new ValueConverter<string, string>(
                    title => notebookTitleEncryption.Encrypt(title),
                    title => notebookTitleEncryption.Decrypt(title)))
                .IsRequired();
            entity.Property(n => n.Date)
                .HasColumnType("timestamp without time zone")
                .IsRequired();
            entity.Property(n => n.CurrentPage).IsRequired();
            entity.Property(n => n.TotalPages).IsRequired();

            entity.HasMany(n => n.Notes)
                .WithOne(n => n.Notebook)
                .HasForeignKey(n => n.NotebookId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Note>(entity =>
        {
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Text)
                .HasConversion(new ValueConverter<string, string>(
                    text => noteTextEncryption.Encrypt(text),
                    text => noteTextEncryption.Decrypt(text)))
                .IsRequired();
            entity.Property(n => n.Date)
                .HasColumnType("timestamp without time zone");
            entity.HasOne(n => n.Notebook)
                .WithMany(n => n.Notes)
                .HasForeignKey(n => n.NotebookId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Name).IsRequired();
            entity.Property(u => u.Email).IsRequired();
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.PasswordHash).IsRequired();
            entity.Property(u => u.CreatedAt)
                .HasColumnType("timestamp with time zone")
                .IsRequired();
            entity.Property(u => u.AgeConfirmed).IsRequired();
            entity.Property(u => u.PrivacyPolicyAccepted).IsRequired();
            entity.Property(u => u.TermsAccepted).IsRequired();
            entity.Property(u => u.InternationalTransferAcknowledged).IsRequired();
        });

        modelBuilder.Entity<CopyrightNotice>(entity =>
        {
            entity.HasKey(n => n.Id);
            entity.Property(n => n.ReporterName).IsRequired();
            entity.Property(n => n.ReporterEmail).IsRequired();
            entity.Property(n => n.CopyrightWork).IsRequired();
            entity.Property(n => n.InfringingUrl).IsRequired();
            entity.Property(n => n.Explanation).IsRequired();
            entity.Property(n => n.Signature).IsRequired();
            entity.Property(n => n.Status).IsRequired();
            entity.Property(n => n.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        });

        modelBuilder.Entity<Suggestion>(entity =>
        {
            entity.HasKey(suggestion => suggestion.Id);
            entity.Property(suggestion => suggestion.Message)
                .HasMaxLength(Suggestion.MaxMessageLength)
                .IsRequired();
            entity.Property(suggestion => suggestion.CreatedAt)
                .HasColumnType("timestamp with time zone")
                .IsRequired();
        });
    }
}
