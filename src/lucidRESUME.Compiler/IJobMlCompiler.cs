namespace lucidRESUME.Compiler;

public interface IJobMlCompiler
{
    Task<CompilationResult> CompileAsync(
        JobMlSnapshot completeResume,
        string jobDescription,
        CompilationOptions? options = null,
        CancellationToken cancellationToken = default);
}
