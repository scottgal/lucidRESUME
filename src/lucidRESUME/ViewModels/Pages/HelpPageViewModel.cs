using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class HelpPageViewModel : ViewModelBase
{
    [ObservableProperty] private string _manualMarkdown = "";
    [ObservableProperty] private string? _scrollToAnchor;
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string _currentSection = "Getting Started";

    // Table of contents entries for sidebar nav
    public IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("Getting Started", "getting-started"),
        new("Importing Your Resume", "importing-your-resume"),
        new("Understanding the Skill Ledger", "understanding-the-skill-ledger"),
        new("Browsing & Managing Jobs", "browsing--managing-jobs"),
        new("Adding Job Descriptions", "adding-job-descriptions"),
        new("Matching & Gap Analysis", "matching--gap-analysis"),
        new("Tailoring Your Resume", "tailoring-your-resume"),
        new("Application Pipeline", "application-pipeline"),
        new("Email Integration", "email-integration"),
        new("AI Provider Setup", "ai-provider-setup"),
        new("Profile & Preferences", "profile--preferences"),
        new("Keyboard Shortcuts", "keyboard-shortcuts"),
        new("Troubleshooting", "troubleshooting"),
    ];

    public HelpPageViewModel()
    {
        LoadManual();
    }

    private void LoadManual()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("user-manual.md", StringComparison.OrdinalIgnoreCase));

        if (resourceName != null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                ManualMarkdown = reader.ReadToEnd();
                return;
            }
        }

        // Fallback: try file next to assembly
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "user-manual.md");
        if (File.Exists(path))
        {
            ManualMarkdown = File.ReadAllText(path);
            return;
        }

        ManualMarkdown = "# Help\n\nUser manual not found. Please reinstall the application.";
    }

    [RelayCommand]
    private void NavigateToSection(string anchor)
    {
        // Clear then set to trigger change notification even if same anchor
        ScrollToAnchor = null;
        ScrollToAnchor = anchor;
        CurrentSection = Sections.FirstOrDefault(s => s.Anchor == anchor)?.Title ?? anchor;
    }

    /// <summary>
    /// Navigate directly to a help anchor. Called from other pages via ? button.
    /// </summary>
    public void ShowHelp(string anchor)
    {
        NavigateToSection(anchor);
    }
}

public record HelpSection(string Title, string Anchor);
