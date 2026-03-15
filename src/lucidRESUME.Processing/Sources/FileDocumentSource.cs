using lucidRESUME.Processing.Pipeline;

namespace lucidRESUME.Processing.Sources;

public sealed class FileDocumentSource : IDocumentSource
{
    private readonly string _filePath;

    public FileDocumentSource(string filePath)
    {
        _filePath = filePath;
        SourceId = Path.GetFileName(filePath);
        ContentType = GuessContentType(filePath);
    }

    public string SourceId { get; }
    public string ContentType { get; }
    public Task<Stream> OpenAsync(CancellationToken ct = default)
        => Task.FromResult<Stream>(File.OpenRead(_filePath));

    private static string GuessContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf"  => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc"  => "application/msword",
            ".txt"  => "text/plain",
            ".html" => "text/html",
            ".htm"  => "text/html",
            _       => "application/octet-stream"
        };
}
