using System.Text;
using System.Text.Encodings.Web;
using lucidRESUME.Compiler;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Ingestion.Parsing;
using Markdig;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace lucidRESUME.Web;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapLucidResumeCompiler(this IEndpointRouteBuilder endpoints, string prefix = "/lucidresume")
    {
        var group = endpoints.MapGroup(prefix);
        group.MapGet("/", Page);
        group.MapGet("/api/status", Status);
        group.MapPost("/api/ledger", Publish);
        group.MapPost("/api/compile", Compile);
        group.MapGet("/api/jobml", CurrentLedger);
        group.MapMethods("/api/jobml", ["HEAD"], CurrentLedger);
        group.MapGet("/api/jobml/{revision}", VersionedLedger);
        group.MapGet("/api/export/{id}/{format}", Export);
        return endpoints;
    }

    private static IResult Page(HttpContext context, IAntiforgery antiforgery)
    {
        var token = antiforgery.GetAndStoreTokens(context).RequestToken ?? "";
        return Results.Content(Html.Replace("__TOKEN__", HtmlEncoder.Default.Encode(token)), "text/html; charset=utf-8");
    }

    private static async Task<IResult> Status(IJobMlSnapshotStore store, CancellationToken ct)
    {
        var snapshot = await store.GetCurrentAsync(ct);
        return Results.Ok(new { ready = snapshot is not null, revision = snapshot?.Revision, publishedAt = snapshot?.PublishedAt });
    }

    private static async Task<IResult> Publish(HttpContext context, IAntiforgery antiforgery,
        IJobMlSnapshotStore store, IOptions<JobMlCompilerOptions> configured, CancellationToken ct)
    {
        if (configured.Value.RequireAuthenticatedWriter && context.User.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();
        try { await antiforgery.ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { return Results.BadRequest(new { error = "Invalid antiforgery token." }); }
        if (context.Request.ContentLength > configured.Value.MaximumUploadBytes)
            return Results.Problem("The complete resume exceeds the configured upload limit.", statusCode: 413);
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, true, leaveOpen: true);
        var source = await reader.ReadToEndAsync(ct);
        if (Encoding.UTF8.GetByteCount(source) > configured.Value.MaximumUploadBytes)
            return Results.Problem("The complete resume exceeds the configured upload limit.", statusCode: 413);
        try
        {
            var snapshot = await store.PublishAsync(source, ct);
            return Results.Ok(new { snapshot.Revision, snapshot.PublishedAt, warnings = snapshot.Diagnostics });
        }
        catch (Exception ex) when (ex is JobMlPublicationException or lucidRESUME.JobML.JobMlParseException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["ledger"] = [ex.Message] });
        }
    }

    private static async Task<IResult> Compile(CompileRequest request, HttpContext context, IAntiforgery antiforgery,
        IJobMlSnapshotStore store, IJobMlCompiler compiler, CompilationSessionStore sessions,
        IOptions<JobMlCompilerOptions> configured, CancellationToken ct)
    {
        if (configured.Value.RequireAuthenticatedWriter && context.User.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();
        try { await antiforgery.ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { return Results.BadRequest(new { error = "Invalid antiforgery token." }); }
        if (string.IsNullOrWhiteSpace(request.JobDescription))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["jobDescription"] = ["Paste a job description."] });
        if (Encoding.UTF8.GetByteCount(request.JobDescription) > configured.Value.MaximumJobDescriptionBytes)
            return Results.Problem("The job description exceeds the configured input limit.", statusCode: 413);
        var snapshot = string.IsNullOrWhiteSpace(request.SourceRevision)
            ? await store.GetCurrentAsync(ct)
            : await store.GetAsync(request.SourceRevision, ct);
        if (snapshot is null) return Results.NotFound(new { error = "Publish a complete Markdown + JobML resume first." });
        const string compileSuffix = "/api/compile";
        var requestPath = context.Request.Path.Value ?? "/lucidresume/api/compile";
        var routeBase = requestPath.EndsWith(compileSuffix, StringComparison.OrdinalIgnoreCase)
            ? requestPath[..^compileSuffix.Length]
            : "/lucidresume";
        var result = await compiler.CompileAsync(snapshot, request.JobDescription,
            new CompilationOptions
            {
                ComposeProse = request.Polish,
                CompositionProvider = request.Provider,
                CompleteLedgerUri = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}{routeBase}/api/jobml/{snapshot.Revision}"
            }, ct);
        sessions.Put(result, TimeSpan.FromMinutes(configured.Value.CompilationCacheMinutes));
        var markdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
        var previewMarkdown = System.Text.RegularExpressions.Regex.Replace(
            result.PublishedMarkdown, "<a id=\"ref-\\d+\"></a>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return Results.Ok(new
        {
            result.CompilationId,
            result.HumanMarkdown,
            result.PublishedMarkdown,
            publishedHtml = Markdown.ToHtml(previewMarkdown, markdownPipeline),
            result.Manifest,
            result.UsedCompositionProvider,
            result.CompositionProvider,
            result.Warnings,
            downloads = new
            {
                markdown = $"{context.Request.PathBase}{routeBase}/api/export/{result.CompilationId}/markdown",
                docx = $"{context.Request.PathBase}{routeBase}/api/export/{result.CompilationId}/docx",
                pdf = $"{context.Request.PathBase}{routeBase}/api/export/{result.CompilationId}/pdf"
            }
        });
    }

    private static async Task<IResult> CurrentLedger(HttpContext context, IJobMlSnapshotStore store, CancellationToken ct)
    {
        var snapshot = await store.GetCurrentAsync(ct);
        return snapshot is null ? Results.NotFound() : Ledger(context, snapshot, false);
    }

    private static async Task<IResult> VersionedLedger(string revision, HttpContext context,
        IJobMlSnapshotStore store, CancellationToken ct)
    {
        var snapshot = await store.GetAsync(revision, ct);
        return snapshot is null ? Results.NotFound() : Ledger(context, snapshot, true);
    }

    private static IResult Ledger(HttpContext context, JobMlSnapshot snapshot, bool immutable)
    {
        context.Response.Headers.ETag = $"\"{snapshot.Revision}\"";
        context.Response.Headers.LastModified = snapshot.PublishedAt.ToString("R");
        context.Response.Headers.CacheControl = immutable ? "public,max-age=31536000,immutable" : "no-cache";
        return context.Request.Method == "HEAD" ? Results.Empty : Results.Text(snapshot.Source, "text/markdown", Encoding.UTF8);
    }

    private static async Task<IResult> Export(string id, string format, CompilationSessionStore sessions,
        IEnumerable<IResumeExporter> exporters, CancellationToken ct)
    {
        if (!sessions.TryGet(id, out var result)) return Results.NotFound();
        var parsedFormat = format.ToLowerInvariant() switch
        {
            "markdown" or "md" => ExportFormat.Markdown,
            "docx" => ExportFormat.Docx,
            "pdf" => ExportFormat.Pdf,
            _ => (ExportFormat?)null
        };
        if (parsedFormat is null) return Results.BadRequest(new { error = "Use markdown, docx or pdf." });
        var resume = ResumeDocument.Create("tailored.md", "text/markdown", Encoding.UTF8.GetByteCount(result.HumanMarkdown));
        resume.SetDoclingOutput(result.HumanMarkdown, null, null);
        resume.CanonicalMarkdown = result.HumanMarkdown;
        resume.JobMlSource = result.FullJobMlMarkdown;
        resume.JobMlRevision = result.Manifest.SourceRevision;
        MarkdownSectionParser.PopulateSections(resume, result.HumanMarkdown);
        var exporter = exporters.Single(x => x.Format == parsedFormat);
        var bytes = await exporter.ExportAsync(resume, ct);
        var metadata = parsedFormat switch
        {
            ExportFormat.Docx => ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "resume.docx"),
            ExportFormat.Pdf => ("application/pdf", "resume.pdf"),
            _ => ("text/markdown; charset=utf-8", "resume.md")
        };
        return Results.File(bytes, metadata.Item1, metadata.Item2);
    }

    public sealed record CompileRequest(string JobDescription, string? SourceRevision = null,
        bool Polish = false, string? Provider = null);

    private const string Html = """
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><link rel="icon" href="data:,">
<title>lucidRESUME compiler</title><style>
:root{font-family:Inter,system-ui,sans-serif;color:#172026;background:#f5f7f7}body{margin:0}.shell{max-width:1180px;margin:auto;padding:32px}.grid{display:grid;grid-template-columns:1fr 1fr;gap:20px}.card{background:#fff;border:1px solid #ccd6d5;border-radius:12px;padding:20px}textarea{box-sizing:border-box;width:100%;min-height:330px;border:1px solid #9dafac;border-radius:8px;padding:12px;font:14px ui-monospace,monospace}button,a.button{border:1px solid #176b61;background:#176b61;color:#fff;padding:10px 14px;border-radius:7px;text-decoration:none;cursor:pointer}.muted{color:#596b69}.actions{display:flex;gap:10px;align-items:center;flex-wrap:wrap;margin-top:12px}.preview{margin-top:18px;padding:30px 34px;min-height:500px;background:#fff;border:1px solid #d9dfde;box-shadow:0 8px 22px #243b3720;font-family:Georgia,serif;line-height:1.48}.preview h1{font:700 30px Inter,system-ui;border-bottom:2px solid #176b61;padding-bottom:10px}.preview h2{font:700 19px Inter,system-ui;margin-top:28px}.preview a{color:#176b61}.hidden{display:none}@media(max-width:800px){.grid{grid-template-columns:1fr}.shell{padding:16px}.preview{padding:22px}}
</style></head><body><main class="shell"><h1>Paste the job. Get the right version of you.</h1><p class="muted">Compile a short, evidence-linked résumé from your complete human-written résumé and JobML ledger.</p><div class="grid"><section class="card"><h2>1. Complete résumé</h2><p class="muted">Upload the long-form Markdown document containing all roles, responsibilities and its full JobML block.</p><input id="ledger" type="file" accept=".md,text/markdown,text/plain"><div class="actions"><button id="publish">Publish master snapshot</button><span id="status"></span></div><h2>2. Target role</h2><textarea id="job" placeholder="Paste the complete job description"></textarea><div class="actions"><label><input id="polish" type="checkbox"> Tighten selected human prose</label><select id="provider"><option value="llamasharp">Local LLamaSharp</option><option value="openai">OpenAI</option></select><button id="compile">Compile résumé</button></div></section><section class="card"><h2>Projection</h2><div id="summary" class="muted">Nothing compiled yet.</div><article id="output" class="preview"></article><div id="downloads" class="actions hidden"><a class="button" id="md">Markdown</a><a class="button" id="docx">Word</a><a class="button" id="pdf">PDF</a></div></section></div></main><script>
const token='__TOKEN__';const headers={'X-CSRF-TOKEN':token};
async function json(r){const t=await r.text();if(!r.ok)throw new Error(t);return JSON.parse(t)}
document.querySelector('#publish').onclick=async()=>{try{const f=document.querySelector('#ledger').files[0];if(!f)throw new Error('Choose the complete resume first.');const x=await json(await fetch(location.pathname+'api/ledger',{method:'POST',headers:{...headers,'Content-Type':'text/markdown'},body:await f.text()}));document.querySelector('#status').textContent='Published '+x.revision.slice(0,12)}catch(e){document.querySelector('#status').textContent=e.message}};
document.querySelector('#compile').onclick=async()=>{const s=document.querySelector('#summary');try{s.textContent='Compiling from the master ledger…';const x=await json(await fetch(location.pathname+'api/compile',{method:'POST',headers:{...headers,'Content-Type':'application/json'},body:JSON.stringify({jobDescription:document.querySelector('#job').value,polish:document.querySelector('#polish').checked,provider:document.querySelector('#provider').value})}));s.textContent=`${x.manifest.sections.length} source section${x.manifest.sections.length===1?'':'s'}, ${x.manifest.gaps.length} honest gap${x.manifest.gaps.length===1?'':'s'}. ${x.usedCompositionProvider?'Polished with '+x.compositionProvider:'Exact selected prose.'}`;document.querySelector('#output').innerHTML=x.publishedHtml;for(const k of ['md','docx','pdf'])document.querySelector('#'+k).href=x.downloads[k==='md'?'markdown':k];document.querySelector('#downloads').classList.remove('hidden')}catch(e){s.textContent=e.message}};
</script></body></html>
""";
}
