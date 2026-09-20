using lucidRESUME.Compiler;
using Microsoft.Extensions.Caching.Memory;

namespace lucidRESUME.Web;

internal sealed class CompilationSessionStore(IMemoryCache cache)
{
    public void Put(CompilationResult result, TimeSpan lifetime) => cache.Set(result.CompilationId, result, lifetime);
    public bool TryGet(string id, out CompilationResult result) => cache.TryGetValue(id, out result!);
}
