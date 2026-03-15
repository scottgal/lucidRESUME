using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class ResumePageViewModel : ViewModelBase
{
    private readonly IResumeParser _parser;
    private readonly IAppStore _store;

    // Set by ResumePage.axaml.cs once the control is attached to the visual tree
    internal TopLevel? TopLevel { get; set; }

    [ObservableProperty] private ResumeDocument? _resume;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    // Flattened display properties (updated when Resume changes)
    [ObservableProperty] private string _fullName = string.Empty;
    [ObservableProperty] private string _contactLine = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private IReadOnlyList<WorkExperience> _experience = [];
    [ObservableProperty] private IReadOnlyList<Education> _education = [];
    [ObservableProperty] private IReadOnlyList<SkillGroup> _skillGroups = [];
    [ObservableProperty] private bool _hasResume;

    public ResumePageViewModel(IResumeParser parser, IAppStore store)
    {
        _parser = parser;
        _store = store;
    }

    [RelayCommand]
    private async Task ImportResumeAsync()
    {
        if (TopLevel is null) return;

        var files = await TopLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Resume",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Resume Files") { Patterns = ["*.pdf", "*.docx"] },
                new FilePickerFileType("PDF") { Patterns = ["*.pdf"] },
                new FilePickerFileType("Word Document") { Patterns = ["*.docx"] },
            ]
        });

        if (files.Count == 0) return;

        var path = files[0].Path.LocalPath;
        ErrorMessage = null;
        StatusMessage = $"Parsing {Path.GetFileName(path)}…";
        IsLoading = true;

        try
        {
            Resume = await _parser.ParseAsync(path);
            PopulateDisplayProperties();
            StatusMessage = $"Imported {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Import failed: {ex.Message}";
            StatusMessage = null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void PopulateDisplayProperties()
    {
        if (Resume is null) return;

        var p = Resume.Personal;
        FullName = p.FullName ?? Path.GetFileNameWithoutExtension(Resume.FileName);

        var contacts = new List<string>();
        if (p.Email != null) contacts.Add(p.Email);
        if (p.Phone != null) contacts.Add(p.Phone);
        if (p.Location != null) contacts.Add(p.Location);
        if (p.LinkedInUrl != null) contacts.Add(p.LinkedInUrl);
        ContactLine = string.Join("  ·  ", contacts);

        Summary = p.Summary ?? string.Empty;
        Experience = Resume.Experience;
        Education = Resume.Education;

        SkillGroups = Resume.Skills
            .GroupBy(s => s.Category ?? "General")
            .Select(g => new SkillGroup(g.Key, string.Join(", ", g.Select(s => s.Name))))
            .ToList();

        HasResume = true;
    }
}

/// <summary>Simple flat record for binding skill groups to the UI.</summary>
public record SkillGroup(string Category, string Skills);
