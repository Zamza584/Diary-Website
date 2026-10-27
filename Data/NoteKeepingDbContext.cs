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
        });
    }
}
