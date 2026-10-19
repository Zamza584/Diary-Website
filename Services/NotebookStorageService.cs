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
    private readonly NoteKeepingDbContext _dbContext;
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly IJSRuntime _jsRuntime;
    private readonly SemaphoreSlim _anonymousStorageLock = new(1, 1);
    private const string AnonymousNotebookKey = "anon-notebook";

    public NotebookStorageService(
        NoteKeepingDbContext dbContext,
        AuthenticationStateProvider authenticationStateProvider,
        IJSRuntime jsRuntime)
    {
        _dbContext = dbContext;
        _authenticationStateProvider = authenticationStateProvider;
        _jsRuntime = jsRuntime;
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
            await _dbContext.SaveChangesAsync();
        }
        else
        {
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

    /// <summary>
    /// Loads notebook for authenticated user from database.
    /// </summary>
    private async Task<Notebook?> LoadAuthenticatedNotebookAsync(int? notebookId)
    {
        var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;
        var email = user.FindFirstValue(ClaimTypes.Email) ?? user.Identity?.Name ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email))
            return null;

        var userAccount = await _dbContext.UserAccounts
            .SingleOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

        if (userAccount == null)
            return null;

        IQueryable<Notebook> notebooks = _dbContext.Notebooks
            .Include(n => n.Notes)
            .Where(n => n.UserAccountId == userAccount.Id);

        if (notebookId.HasValue)
        {
            notebooks = notebooks.Where(n => n.Id == notebookId.Value);
        }

        return await notebooks
            .OrderByDescending(n => n.Id)
            .FirstOrDefaultAsync();
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

            var notebook = JsonSerializer.Deserialize<Notebook>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (notebook == null)
            {
                return null;
            }

            notebook.Notes ??= new List<Note>();
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
                    n.Text,
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
        if (_dbContext.Entry(currentNote).State == EntityState.Detached)
        {
            var trackedNote = await _dbContext.Notes.FindAsync(currentNote.Id);
            if (trackedNote != null)
            {
                trackedNote.Text = noteText;
                trackedNote.Date = currentNote.Date;
            }
            else
            {
                currentNote.Text = noteText;
                currentNote.Notebook = notebook;
                currentNote.NotebookId = notebook.Id;
                _dbContext.Notes.Attach(currentNote);
            }
        }
        else
        {
            currentNote.Text = noteText;
        }

        notebook.CurrentPage = Math.Clamp(notebook.CurrentPage, 1, notebook.TotalPages);
        await _dbContext.SaveChangesAsync();
    }
}
