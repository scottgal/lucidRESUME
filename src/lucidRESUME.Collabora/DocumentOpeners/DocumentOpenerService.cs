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
        var openers = new List<DocumentOpener?>(4)
        {
            DocumentOpener.TryCreateLibreOffice(),
            DocumentOpener.TryCreateMicrosoftWord(),
            DocumentOpener.TryCreateWpsOffice(),
            DocumentOpener.TryCreateOnlyOffice(),
        };

        return openers.Where(o => o != null).Cast<DocumentOpener>().ToList();
    }
}
