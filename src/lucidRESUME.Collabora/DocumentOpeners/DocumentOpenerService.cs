namespace lucidRESUME.Collabora.DocumentOpeners;

/// <summary>
/// Discovers locally installed document editors at startup.
/// Apps are detected once and cached — no runtime polling.
/// </summary>
public sealed class DocumentOpenerService
{
    public IReadOnlyList<DocumentOpener> Available { get; }

    public bool HasAny => Available.Count > 0;

    public DocumentOpener? Primary => Available.Count > 0 ? Available[0] : null;

    public DocumentOpenerService()
    {
        Available = Discover();
    }

    private static IReadOnlyList<DocumentOpener> Discover()
    {
        var openers = new List<DocumentOpener?>
        {
            // Cross-platform editors (priority: most capable first)
            DocumentOpener.TryCreateLibreOffice(),
            DocumentOpener.TryCreateMicrosoftWord(),
            DocumentOpener.TryCreateWpsOffice(),
            DocumentOpener.TryCreateOnlyOffice(),

            // macOS native apps
            DocumentOpener.TryCreateMacApp("Preview", "Preview"),
            DocumentOpener.TryCreateMacApp("Pages", "Pages"),
            DocumentOpener.TryCreateMacApp("TextEdit", "TextEdit"),

            // macOS system default (always available on macOS, last resort)
            DocumentOpener.TryCreateMacDefault(),
        };

        return openers.Where(o => o != null).Cast<DocumentOpener>().ToList();
    }
}
