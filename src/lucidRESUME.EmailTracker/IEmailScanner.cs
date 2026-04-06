namespace lucidRESUME.EmailTracker;

public interface IEmailScanner
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<ScannedEmail>> ScanAsync(DateTimeOffset since, CancellationToken ct = default);
}
