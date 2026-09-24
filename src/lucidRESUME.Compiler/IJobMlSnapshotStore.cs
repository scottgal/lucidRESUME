namespace lucidRESUME.Compiler;

public interface IJobMlSnapshotStore
{
    Task<JobMlSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task<JobMlSnapshot?> GetAsync(string revision, CancellationToken cancellationToken = default);
    Task<JobMlSnapshot> PublishAsync(string source, CancellationToken cancellationToken = default);
}

public sealed class JobMlCompilerOptions
{
    public const string SectionName = "LucidResumeCompiler";
    public string SnapshotDirectory { get; set; } = Path.Combine("App_Data", "jobml");
    public long MaximumUploadBytes { get; set; } = 16 * 1024 * 1024;
    public int MaximumJobDescriptionBytes { get; set; } = 256 * 1024;
    public int CompilationCacheMinutes { get; set; } = 30;
    public string EmbeddingProviderName { get; set; } = "local-onnx";
    public bool RequireAuthenticatedWriter { get; set; } = true;
}
