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
        group.MapPost("/api/career-record", Publish);
        group.MapPost("/api/ledger", Publish); // JobML 0.1 draft compatibility route.
        group.MapPost("/api/compile", Compile);
        group.MapGet("/api/jobml", CurrentJobMl);
        group.MapMethods("/api/jobml", ["HEAD"], CurrentJobMl);
        group.MapGet("/api/jobml/{revision}", VersionedJobMl);
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
            if (context.Request.Path.Value?.EndsWith("/api/career-record", StringComparison.OrdinalIgnoreCase) == true)
            {
                var parsed = new lucidRESUME.JobML.JobMlParser().Parse(source);
                if (!string.Equals(parsed.Data.Header.Profile, "career_record", StringComparison.Ordinal))
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["careerRecord"] = ["The career-record endpoint requires jobml.profile: career_record."]
                    });
            }
            var snapshot = await store.PublishAsync(source, ct);
            return Results.Ok(new { snapshot.Revision, snapshot.PublishedAt, warnings = snapshot.Diagnostics });
        }
        catch (Exception ex) when (ex is JobMlPublicationException or lucidRESUME.JobML.JobMlParseException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["careerRecord"] = [ex.Message] });
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
        if (snapshot is null) return Results.NotFound(new { error = "Publish a JobML career record first." });
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
                FullJobMlUri = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}{routeBase}/api/jobml/{snapshot.Revision}"
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

    private static async Task<IResult> CurrentJobMl(HttpContext context, IJobMlSnapshotStore store, CancellationToken ct)
    {
        var snapshot = await store.GetCurrentAsync(ct);
        return snapshot is null ? Results.NotFound() : JobMl(context, snapshot, false);
    }

    private static async Task<IResult> VersionedJobMl(string revision, HttpContext context,
        IJobMlSnapshotStore store, CancellationToken ct)
    {
        var snapshot = await store.GetAsync(revision, ct);
        return snapshot is null ? Results.NotFound() : JobMl(context, snapshot, true);
    }

    private static IResult JobMl(HttpContext context, JobMlSnapshot snapshot, bool immutable)
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
        PopulateProjectionSections(resume, result.ProjectedJobMl);
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

    private static void PopulateProjectionSections(ResumeDocument resume, lucidRESUME.JobML.JobMlFile projection)
    {
        // The generic Markdown parser recognises conventional "Experience" sections,
        // not the compiler's evidence-packet headings. Rebuild the export model from
        // the already-projected JobML bindings; never infer it again from rendered text.
        resume.Personal.Summary = null;
        resume.Experience.Clear();
        resume.Projects.Clear();
        resume.Education.Clear();

        var index = lucidRESUME.JobML.MarkdownEvidenceIndex.Create(projection.Markdown);
        var claimsBySubject = projection.Data.Claims
            .GroupBy(claim => claim.Subject, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var ordered = projection.Data.Entities.Select(entity =>
        {
            var claims = claimsBySubject.GetValueOrDefault(entity.Id) ?? [];
            var passages = claims.SelectMany(claim => claim.Evidence)
                .Where(evidence => evidence.Type == "prose" && !string.IsNullOrWhiteSpace(evidence.Ref))
                .Select(evidence => index.TryGet(evidence.Ref!, out var passage) ? passage : null)
                .Where(passage => passage is not null)
                .Cast<lucidRESUME.JobML.ProsePassage>()
                .DistinctBy(passage => (passage.SourceStart, passage.SourceLength))
                .OrderBy(passage => passage.SourceStart)
                .ToList();
            return new { Entity = entity, Claims = claims, Passages = passages,
                Start = passages.FirstOrDefault()?.SourceStart ?? int.MaxValue };
        }).OrderBy(item => item.Start);

        foreach (var item in ordered)
        {
            var prose = string.Join(" ", item.Passages.Select(passage => passage.Text));
            if (string.IsNullOrWhiteSpace(prose)) continue;
            if (item.Claims.Any(claim => claim.Type == "summary"))
            {
                resume.Personal.Summary = prose;
                continue;
            }
            if (item.Entity.Type == "project")
            {
                resume.Projects.Add(new Project { Name = item.Entity.Name, Description = prose });
                continue;
            }
            if (item.Entity.Type == "education")
            {
                resume.Education.Add(new Education { Institution = item.Entity.Name, Highlights = [prose] });
                continue;
            }
            if (item.Entity.Type != "experience") continue;
            var role = item.Entity.Name.Split(" · ", 2, StringSplitOptions.TrimEntries);
            resume.Experience.Add(new WorkExperience
            {
                Title = role[0],
                Company = role.Length > 1 ? role[1] : null,
                Achievements = [prose]
            });
        }
    }

    public sealed record CompileRequest(string JobDescription, string? SourceRevision = null,
        bool Polish = false, string? Provider = null);

    private const string Html = """
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><link rel="icon" href="data:,">
<title>lucidRESUME compiler</title><style>
:root{font-family:Inter,system-ui,sans-serif;color:#172026;background:#f5f7f7}body{margin:0}.shell{max-width:1180px;margin:auto;padding:32px}.grid{display:grid;grid-template-columns:1fr 1fr;gap:20px}.card{background:#fff;border:1px solid #ccd6d5;border-radius:12px;padding:20px}textarea{box-sizing:border-box;width:100%;min-height:330px;border:1px solid #9dafac;border-radius:8px;padding:12px;font:14px ui-monospace,monospace}button,a.button{border:1px solid #176b61;background:#176b61;color:#fff;padding:10px 14px;border-radius:7px;text-decoration:none;cursor:pointer}.muted{color:#596b69}.actions{display:flex;gap:10px;align-items:center;flex-wrap:wrap;margin-top:12px}.preview{margin-top:18px;padding:30px 34px;min-height:500px;background:#fff;border:1px solid #d9dfde;box-shadow:0 8px 22px #243b3720;font-family:Georgia,serif;line-height:1.48}.preview h1{font:700 30px Inter,system-ui;border-bottom:2px solid #176b61;padding-bottom:10px}.preview h2{font:700 19px Inter,system-ui;margin-top:28px}.preview a{color:#176b61}.hidden{display:none}@media(max-width:800px){.grid{grid-template-columns:1fr}.shell{padding:16px}.preview{padding:22px}}
</style></head><body><main class="shell"><h1>Paste the job. Get the right version of you.</h1><p class="muted">Compile a short, evidence-linked résumé from a published JobML career record.</p><div class="grid"><section class="card"><h2>1. Career record</h2><p class="muted">Upload the complete human career transcript and its JobML career_record projection. The source may include every role, project, repository and linked article.</p><input id="careerRecord" type="file" accept=".md,text/markdown,text/plain"><div class="actions"><button id="publish">Publish career record</button><span id="status"></span></div><h2>2. Target role</h2><textarea id="job" placeholder="Paste the complete job description"></textarea><div class="actions"><label><input id="polish" type="checkbox"> Tighten selected human prose</label><select id="provider"><option value="llamasharp">Local LLamaSharp</option><option value="openai">OpenAI</option></select><button id="compile">Compile résumé</button></div></section><section class="card"><h2>Résumé projection</h2><div id="summary" class="muted">Nothing compiled yet.</div><article id="output" class="preview"></article><div id="downloads" class="actions hidden"><a class="button" id="md">Markdown</a><a class="button" id="docx">Word</a><a class="button" id="pdf">PDF</a></div></section></div></main><script>
const token='__TOKEN__';const headers={'X-CSRF-TOKEN':token};
async function json(r){const t=await r.text();let x;try{x=JSON.parse(t)}catch{}if(!r.ok)throw new Error(x?.error??Object.values(x?.errors??{}).flat()[0]??t);return x}
document.querySelector('#publish').onclick=async()=>{try{const f=document.querySelector('#careerRecord').files[0];if(!f)throw new Error('Choose the JobML career record first.');const x=await json(await fetch(location.pathname+'api/career-record',{method:'POST',headers:{...headers,'Content-Type':'text/markdown'},body:await f.text()}));document.querySelector('#status').textContent='Published '+x.revision.slice(0,12)}catch(e){document.querySelector('#status').textContent=e.message}};
document.querySelector('#compile').onclick=async()=>{const s=document.querySelector('#summary');try{s.textContent='Compiling from the published career record…';const x=await json(await fetch(location.pathname+'api/compile',{method:'POST',headers:{...headers,'Content-Type':'application/json'},body:JSON.stringify({jobDescription:document.querySelector('#job').value,polish:document.querySelector('#polish').checked,provider:document.querySelector('#provider').value})}));s.textContent=`${x.manifest.sections.length} source section${x.manifest.sections.length===1?'':'s'}, ${x.manifest.gaps.length} honest gap${x.manifest.gaps.length===1?'':'s'}. ${x.usedCompositionProvider?'Polished with '+x.compositionProvider:'Exact selected prose.'}`;document.querySelector('#output').innerHTML=x.publishedHtml;for(const k of ['md','docx','pdf'])document.querySelector('#'+k).href=x.downloads[k==='md'?'markdown':k];document.querySelector('#downloads').classList.remove('hidden')}catch(e){s.textContent=e.message}};
</script></body></html>
""";
}
