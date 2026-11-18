using Microsoft.JSInterop;
using NoteKeeping.Data;
using NoteKeeping.Models;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components.Authorization;

namespace NoteKeeping.Services;

/// <summary>
/// Abstraction layer that routes notebook persistence to either:
/// - Database (authenticated users)
/// - Browser localStorage (anonymous users)
/// </summary>
public class NotebookStorageService
{
    private readonly IDbContextFactory<NoteKeepingDbContext> _dbContextFactory;
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly IJSRuntime _jsRuntime;
    private readonly NotebookContentSanitizer _contentSanitizer;
    private readonly SemaphoreSlim _anonymousStorageLock = new(1, 1);
    private static readonly JsonSerializerOptions AnonymousNotebookJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private const string AnonymousNotebookKey = "anon-notebook";

    public NotebookStorageService(
        IDbContextFactory<NoteKeepingDbContext> dbContextFactory,
        AuthenticationStateProvider authenticationStateProvider,
        IJSRuntime jsRuntime,
        NotebookContentSanitizer contentSanitizer)
    {
        _dbContextFactory = dbContextFactory;
        _authenticationStateProvider = authenticationStateProvider;
        _jsRuntime = jsRuntime;
        _contentSanitizer = contentSanitizer;
    }

    /// <summary>
    /// Loads the notebook for the current user (auth or anonymous).
    /// For authenticated users: loads from database.
    /// For anonymous users: loads from browser localStorage.
    /// </summary>
    public async Task<Notebook?> LoadNotebookAsync(int? notebookId = null)
    {
        var isAuthenticated = await IsAuthenticatedAsync();

        if (isAuthenticated)
        {
            return await LoadAuthenticatedNotebookAsync(notebookId);
        }
        else
        {
            return notebookId.HasValue ? null : await LoadAnonymousNotebookAsync();
        }
    }

    /// <summary>
    /// Saves a note to the appropriate storage backend.
    /// For authenticated users: saves to database.
    /// For anonymous users: saves to browser localStorage.
    /// </summary>
    public async Task SaveNoteAsync(Notebook? notebook, Note? currentNote, string noteText)
    {
        if (notebook == null || currentNote == null)
            return;

        var isAuthenticated = await IsAuthenticatedAsync();

        if (isAuthenticated)
        {
            await SaveNoteToDatabaseAsync(currentNote, noteText, notebook);
        }
        else
        {
            currentNote.Text = noteText;
            await SaveNotebookToLocalStorageAsync(notebook);
        }
    }

    /// <summary>
    /// Saves the entire notebook to the appropriate storage backend.
    /// </summary>
    public async Task SaveNotebookAsync(Notebook? notebook)
    {
        if (notebook == null)
            return;

        var isAuthenticated = await IsAuthenticatedAsync();

        if (isAuthenticated)
        {
            await SaveNotebookToDatabaseAsync(notebook);
        }
        else
        {
            await SaveNotebookToLocalStorageAsync(notebook);
        }
    }

    public async Task SavePageNavigationAsync(
        Notebook notebook,
        Note currentNote,
        string noteText,
        int targetPage)
    {
        if (targetPage < 1 || targetPage > notebook.TotalPages)
        {
            throw new ArgumentOutOfRangeException(nameof(targetPage));
        }

        var sanitizedText = _contentSanitizer.Sanitize(noteText);
        var isAuthenticated = await IsAuthenticatedAsync();

        if (isAuthenticated)
        {
            await SavePageNavigationToDatabaseAsync(notebook, currentNote, sanitizedText, targetPage);
        }
        else
        {
            currentNote.Text = sanitizedText;
            notebook.CurrentPage = targetPage;
            await SaveNotebookToLocalStorageAsync(notebook);
        }
    }

    /// <summary>
    /// Clears all anonymous notebook data from browser localStorage.
    /// Called on logout or auth state change.
    /// </summary>
    public async Task ClearAnonymousStorageAsync()
    {
        await _anonymousStorageLock.WaitAsync();
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", AnonymousNotebookKey);
        }
        catch (JSDisconnectedException)
        {
            // JS runtime not available; silently continue
        }
        catch (Exception)
        {
            // Graceful fallback if localStorage fails
        }
        finally
        {
            _anonymousStorageLock.Release();
        }
    }

    /// <summary>
    /// Gets the current authentication state.
    /// </summary>
    private async Task<bool> IsAuthenticatedAsync()
    {
        var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
        return authState.User.Identity?.IsAuthenticated ?? false;
    }

    private async Task<int?> GetAuthenticatedUserAccountIdAsync()
    {
        var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var accountId = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(accountId, out var parsedAccountId) ? parsedAccountId : null;
    }

    /// <summary>
    /// Loads notebook for authenticated user from database.
    /// </summary>
    private async Task<Notebook?> LoadAuthenticatedNotebookAsync(int? notebookId)
    {
        var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;
        var accountIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(accountIdValue, out var accountId))
        {
            return null;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        IQueryable<Notebook> notebooks = dbContext.Notebooks
            .Include(n => n.Notes)
            .Where(n => n.UserAccountId == accountId);

        if (notebookId.HasValue)
        {
            notebooks = notebooks.Where(n => n.Id == notebookId.Value);
        }

        var notebook = await notebooks
            .OrderByDescending(n => n.Id)
            .FirstOrDefaultAsync();
        if (notebook is null)
        {
            return null;
        }

        var contentChanged = false;
        foreach (var note in notebook.Notes)
        {
            var sanitizedText = _contentSanitizer.Sanitize(note.Text);
            if (!string.Equals(note.Text, sanitizedText, StringComparison.Ordinal))
            {
                note.Text = sanitizedText;
                contentChanged = true;
            }
        }

        if (contentChanged)
        {
            await dbContext.SaveChangesAsync();
        }

        return notebook;
    }

    /// <summary>
    /// Loads notebook for anonymous user from browser localStorage.
    /// </summary>
    private async Task<Notebook?> LoadAnonymousNotebookAsync()
    {
        try
        {
            var json = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", AnonymousNotebookKey);

            if (string.IsNullOrEmpty(json))
                return null;

            var notebook = JsonSerializer.Deserialize<Notebook>(json, AnonymousNotebookJsonOptions);

            if (notebook == null)
            {
                return null;
            }

            notebook.Notes ??= new List<Note>();
            foreach (var note in notebook.Notes)
            {
                note.Text = _contentSanitizer.Sanitize(note.Text);
            }

            while (notebook.Notes.Count < notebook.TotalPages)
            {
                notebook.Notes.Add(new Note
                {
                    Date = null,
                    NotebookId = notebook.Id,
                    Notebook = notebook
                });
            }

            foreach (var note in notebook.Notes)
            {
                note.Notebook = notebook;
            }

            return notebook;
        }
        catch (JSDisconnectedException)
        {
            // JS runtime not available
            return null;
        }
        catch (Exception ex)
        {
            // Graceful fallback if localStorage fails
            System.Diagnostics.Debug.WriteLine($"Load error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Saves notebook for anonymous user to browser localStorage.
    /// </summary>
    private async Task SaveNotebookToLocalStorageAsync(Notebook notebook)
    {
        await _anonymousStorageLock.WaitAsync();
        try
        {
            // Create a serialization-friendly version without navigation properties
            var json = JsonSerializer.Serialize(new
            {
                notebook.Id,
                notebook.Title,
                notebook.Date,
                notebook.CurrentPage,
                notebook.TotalPages,
                Notes = (notebook.Notes ?? new List<Note>()).Select(n => new
                {
                    n.Id,
                    Text = _contentSanitizer.Sanitize(n.Text),
                    n.Date,
                    n.NotebookId
                }).ToList()
            });

            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", AnonymousNotebookKey, json);
        }
        catch (JSDisconnectedException)
        {
            // JS runtime not available; silently continue
        }
        catch (Exception ex)
        {
            // Graceful fallback if localStorage fails
            System.Diagnostics.Debug.WriteLine($"Storage error: {ex.Message}");
        }
        finally
        {
            _anonymousStorageLock.Release();
        }
    }

    /// <summary>
    /// Saves a single note to the database (authenticated users only).
    /// </summary>
    private async Task SaveNoteToDatabaseAsync(Note currentNote, string noteText, Notebook notebook)
    {
        var accountId = await GetAuthenticatedUserAccountIdAsync();
        if (!accountId.HasValue)
        {
            return;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var sanitizedText = _contentSanitizer.Sanitize(noteText);
        Notebook trackedNotebook;
        Note? trackedNote = null;

        if (currentNote.Id == 0)
        {
            var notebookRecord = await dbContext.Notebooks
                .SingleOrDefaultAsync(n => n.Id == notebook.Id && n.UserAccountId == accountId.Value);
            if (notebookRecord is null)
            {
                return;
            }

            trackedNotebook = notebookRecord;
        }
        else
        {
            trackedNote = await dbContext.Notes
                .Include(n => n.Notebook)
                .SingleOrDefaultAsync(n =>
                    n.Id == currentNote.Id &&
                    n.NotebookId == notebook.Id &&
                    n.Notebook!.UserAccountId == accountId.Value);
            if (trackedNote?.Notebook is null)
            {
                throw new InvalidOperationException("The note does not belong to the current notebook.");
            }

            trackedNotebook = trackedNote.Notebook;
        }

        trackedNotebook.CurrentPage = Math.Clamp(notebook.CurrentPage, 1, Math.Max(1, trackedNotebook.TotalPages));

        if (trackedNote is not null)
        {
            trackedNote.Text = sanitizedText;
            trackedNote.Date = currentNote.Date;
        }
        else
        {
            currentNote.Text = sanitizedText;
            currentNote.NotebookId = trackedNotebook.Id;
            currentNote.Notebook = trackedNotebook;
            dbContext.Notes.Add(currentNote);
        }

        await dbContext.SaveChangesAsync();
        currentNote.Text = sanitizedText;
    }

    private async Task SavePageNavigationToDatabaseAsync(
        Notebook notebook,
        Note currentNote,
        string noteText,
        int targetPage)
    {
        var accountId = await GetAuthenticatedUserAccountIdAsync();
        if (!accountId.HasValue)
        {
            throw new InvalidOperationException("The authenticated account could not be resolved.");
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        Notebook trackedNotebook;
        Note? trackedNote = null;

        if (currentNote.Id == 0)
        {
            var notebookRecord = await dbContext.Notebooks
                .SingleOrDefaultAsync(n => n.Id == notebook.Id && n.UserAccountId == accountId.Value);
            if (notebookRecord is null)
            {
                throw new InvalidOperationException("The current notebook could not be found.");
            }

            trackedNotebook = notebookRecord;
        }
        else
        {
            trackedNote = await dbContext.Notes
                .Include(n => n.Notebook)
                .SingleOrDefaultAsync(n =>
                    n.Id == currentNote.Id &&
                    n.NotebookId == notebook.Id &&
                    n.Notebook!.UserAccountId == accountId.Value);
            if (trackedNote?.Notebook is null)
            {
                throw new InvalidOperationException("The current note could not be found in this notebook.");
            }

            trackedNotebook = trackedNote.Notebook;
        }

        if (targetPage > trackedNotebook.TotalPages)
        {
            throw new InvalidOperationException("The requested page is outside the notebook.");
        }

        trackedNotebook.CurrentPage = targetPage;

        if (trackedNote is null)
        {
            currentNote.Text = noteText;
            currentNote.NotebookId = trackedNotebook.Id;
            currentNote.Notebook = trackedNotebook;
            dbContext.Notes.Add(currentNote);
        }
        else
        {
            trackedNote.Text = noteText;
            trackedNote.Date = currentNote.Date;
            currentNote.Text = noteText;
        }

        foreach (var note in notebook.Notes.Where(note => note.Id == 0 && !ReferenceEquals(note, currentNote)))
        {
            note.Text = _contentSanitizer.Sanitize(note.Text);
            note.NotebookId = trackedNotebook.Id;
            note.Notebook = trackedNotebook;
            dbContext.Notes.Add(note);
        }

        await dbContext.SaveChangesAsync();
        notebook.CurrentPage = targetPage;
    }

    private async Task SaveNotebookToDatabaseAsync(Notebook notebook)
    {
        var accountId = await GetAuthenticatedUserAccountIdAsync();
        if (!accountId.HasValue)
        {
            return;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var trackedNotebook = await dbContext.Notebooks
            .Include(n => n.Notes)
            .SingleOrDefaultAsync(n => n.Id == notebook.Id && n.UserAccountId == accountId.Value);

        if (trackedNotebook is null)
        {
            return;
        }

        trackedNotebook.Title = notebook.Title;
        trackedNotebook.Date = notebook.Date;
        trackedNotebook.CurrentPage = notebook.CurrentPage;
        trackedNotebook.TotalPages = notebook.TotalPages;

        foreach (var note in notebook.Notes)
        {
            var trackedNote = note.Id == 0
                ? null
                : trackedNotebook.Notes.SingleOrDefault(n => n.Id == note.Id);

            if (note.Id != 0 && trackedNote is null)
            {
                throw new InvalidOperationException("The note does not belong to the current notebook.");
            }

            if (trackedNote is null)
            {
                note.Text = _contentSanitizer.Sanitize(note.Text);
                note.NotebookId = trackedNotebook.Id;
                trackedNotebook.Notes.Add(note);
            }
            else
            {
                trackedNote.Date = note.Date;
            }
        }

        await dbContext.SaveChangesAsync();
    }
}
