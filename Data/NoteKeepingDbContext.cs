using Microsoft.EntityFrameworkCore;
using NoteKeeping.Models;

namespace NoteKeeping.Data;

public class NoteKeepingDbContext : DbContext
{
    public NoteKeepingDbContext(DbContextOptions<NoteKeepingDbContext> options)
        : base(options)
    {
    }

    public DbSet<Notebook> Notebooks => Set<Notebook>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Notebook>(entity =>
        {
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Title).IsRequired();
            entity.Property(n => n.Date).IsRequired();
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
            entity.Property(n => n.Text).IsRequired();
            entity.Property(n => n.Date).IsRequired();
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
            entity.Property(u => u.CreatedAt).IsRequired();
        });
    }
}
