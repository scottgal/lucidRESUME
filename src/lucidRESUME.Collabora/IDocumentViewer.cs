namespace lucidRESUME.Collabora;

public interface IDocumentViewer
{
    Task LoadDocumentAsync(string filePath);
    Task CloseDocumentAsync();
    bool IsDocumentLoaded { get; }
}
