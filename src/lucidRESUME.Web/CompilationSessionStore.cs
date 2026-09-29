using lucidRESUME.Compiler;
using Microsoft.Extensions.Caching.Memory;

namespace lucidRESUME.Web;

public sealed class CompilationSessionStore(IMemoryCache cache)
{
    public void Put(CompilationResult result, TimeSpan lifetime, bool includeCitations = true, int minimumPages = 2)
    {
        ArgumentNullException.ThrowIfNull(result);
        cache.Set(result.CompilationId,
            new CompilationSession(result, includeCitations, Math.Clamp(minimumPages, 1, 2)), lifetime);
    }

    public bool TryGet(string id, out CompilationSession session) => cache.TryGetValue(id, out session!);
}

public sealed record CompilationSession(CompilationResult Result, bool IncludeCitations, int MinimumPages);
