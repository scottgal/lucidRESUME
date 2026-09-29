using System.Text;
using System.Text.Encodings.Web;
using lucidRESUME.Compiler;
using lucidRESUME.Core.Interfaces;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace lucidRESUME.Web;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapLucidResumeCompiler(this IEndpointRouteBuilder endpoints, string prefix = "/resume")
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
        group.MapGet("/{publicId}", PublishedResume);
        group.MapGet("/{publicId}/jobml", PublishedJobMl);
        group.MapMethods("/{publicId}/jobml", ["HEAD"], PublishedJobMl);
        group.MapGet("/{publicId}/cjobml", PublishedCompactJobMl);
        group.MapGet("/{publicId}/transcript", PublishedTranscript);
        group.MapGet("/{publicId}/download/{format}", PublishedExport);
        return endpoints;
    }

    private static IResult Page(HttpContext context, IAntiforgery antiforgery)
    {
        var token = antiforgery.GetAndStoreTokens(context).RequestToken ?? "";
        return Results.Content(Html.Replace("__TOKEN__", HtmlEncoder.Default.Encode(token),
            StringComparison.Ordinal), "text/html; charset=utf-8");
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
        IResumePublicationStore publications, IResumeMarkdownRenderer renderer,
        IOptions<JobMlCompilerOptions> configured, IOptions<LucidResumeWebOptions> webConfigured,
        CancellationToken ct)
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
        var requestPath = context.Request.Path.Value ?? "/resume/api/compile";
        var routeBase = requestPath.EndsWith(compileSuffix, StringComparison.OrdinalIgnoreCase)
            ? requestPath[..^compileSuffix.Length]
            : "/resume";
        var requestBaseUri = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}{routeBase}";
        var publicBaseUri = Uri.TryCreate(configured.Value.PublicBaseUri, UriKind.Absolute, out var configuredUri) &&
                            configuredUri.Scheme is "http" or "https"
            ? configuredUri.ToString().TrimEnd('/')
            : requestBaseUri;
        var minimumPages = Math.Clamp(request.MinimumPages, 1, 2);
        var publish = request.IncludeCitations && request.Publish && webConfigured.Value.PublishCompiledResumes;
        var publicId = publish ? publications.CreatePublicId() : null;
        var fullJobMlUri = publicId is null
            ? $"{publicBaseUri}/api/jobml/{snapshot.Revision}"
            : $"{publicBaseUri}/{publicId}/jobml";
        var compilationOptions = new CompilationOptions
        {
            ComposeProse = request.Polish,
            CompositionProvider = request.Provider,
            FullJobMlUri = fullJobMlUri
        };
        if (minimumPages == 1)
        {
            compilationOptions.MaximumClaims = 12;
            compilationOptions.MaximumClaimsPerSubject = 2;
            compilationOptions.MaximumSections = 6;
            compilationOptions.MinimumExperienceSections = 5;
            compilationOptions.MinimumProjectSections = 2;
        }
        var result = await compiler.CompileAsync(snapshot, request.JobDescription, compilationOptions, ct);
        sessions.Put(result, TimeSpan.FromMinutes(configured.Value.CompilationCacheMinutes),
            request.IncludeCitations, minimumPages);
        var outputMarkdown = request.IncludeCitations ? result.PublishedMarkdown : result.HumanMarkdown;
        var previewMarkdown = System.Text.RegularExpressions.Regex.Replace(
            outputMarkdown, "<a id=\"ref-\\d+\"></a>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        ResumePublication? publication = null;
        if (publicId is not null)
        {
            publication = new ResumePublication(publicId, DateTimeOffset.UtcNow,
                NormalizeApplicationReference(request.ApplicationReference), result, minimumPages);
            await publications.PublishAsync(publication, ct);
        }
        var publicationPath = publication is null
            ? null
            : $"{context.Request.PathBase}{routeBase}/{publication.PublicId}";
        var downloadBase = publicationPath is null
            ? $"{context.Request.PathBase}{routeBase}/api/export/{result.CompilationId}"
            : publicationPath + "/download";
        return Results.Ok(new
        {
            result.CompilationId,
            result.HumanMarkdown,
            PublishedMarkdown = outputMarkdown,
            publishedHtml = renderer.ToHtml(previewMarkdown),
            result.Manifest,
            result.UsedCompositionProvider,
            result.CompositionProvider,
            result.Warnings,
            publication = publication is null ? null : new
            {
                publication.PublicId,
                publication.PublishedAt,
                publication.ApplicationReference,
                url = publicationPath,
                jobml = publicationPath + "/jobml",
                cjobml = publicationPath + "/cjobml"
            },
            downloads = new
            {
                markdown = downloadBase + "/markdown",
                docx = downloadBase + "/docx",
                pdf = downloadBase + "/pdf"
            }
        });
    }

    private static string? NormalizeApplicationReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= 160 ? value : value[..160];
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
        if (!sessions.TryGet(id, out var session)) return Results.NotFound();
        return await ExportResult(session.Result, format, exporters, session.IncludeCitations,
            session.MinimumPages, ct);
    }

    private static async Task<IResult> PublishedResume(string publicId, HttpContext context,
        IResumePublicationStore publications, IJobMlSnapshotStore snapshots,
        IResumeMarkdownRenderer renderer, CancellationToken ct)
    {
        var publication = await publications.GetAsync(publicId, ct);
        if (publication is null) return Results.NotFound();
        SetPublicationCacheHeaders(context, publication);
        return await RenderPublicationPage(publication, context, snapshots, renderer, ct);
    }

    private static async Task<IResult> PublishedJobMl(string publicId, HttpContext context,
        IResumePublicationStore publications, IJobMlSnapshotStore snapshots,
        IResumeMarkdownRenderer renderer, CancellationToken ct)
    {
        var publication = await publications.GetAsync(publicId, ct);
        if (publication is null) return Results.NotFound();
        SetPublicationCacheHeaders(context, publication);
        context.Response.Headers.Vary = "Accept";
        if (WantsHtml(context.Request))
            return await RenderPublicationPage(publication, context, snapshots, renderer, ct);
        return context.Request.Method == "HEAD"
            ? Results.Empty
            : Results.Text(publication.Compilation.FullJobMlMarkdown, "text/markdown", Encoding.UTF8);
    }

    private static async Task<IResult> PublishedCompactJobMl(string publicId, HttpContext context,
        IResumePublicationStore publications, CancellationToken ct)
    {
        var publication = await publications.GetAsync(publicId, ct);
        if (publication is null) return Results.NotFound();
        SetPublicationCacheHeaders(context, publication);
        return Results.Text(publication.Compilation.PublishedMarkdown, "text/markdown", Encoding.UTF8);
    }

    private static async Task<IResult> PublishedTranscript(string publicId, HttpContext context,
        IResumePublicationStore publications, IJobMlSnapshotStore snapshots,
        IResumeMarkdownRenderer renderer, CancellationToken ct)
    {
        var publication = await publications.GetAsync(publicId, ct);
        if (publication is null) return Results.NotFound();
        var snapshot = await snapshots.GetAsync(publication.Compilation.Manifest.SourceRevision, ct);
        if (snapshot is null) return Results.NotFound();
        SetPublicationCacheHeaders(context, publication);
        context.Response.Headers.Vary = "Accept";
        if (!WantsHtml(context.Request))
            return Results.Text(snapshot.Source, "text/markdown", Encoding.UTF8);

        var publicationPath = PublicationPath(context.Request.PathBase, context.Request.Path, publicId);
        var body = renderer.ToHtml(snapshot.File.Markdown);
        return Results.Content(RenderTranscriptPage(body, publicationPath), "text/html; charset=utf-8");
    }

    private static async Task<IResult> RenderPublicationPage(ResumePublication publication,
        HttpContext context, IJobMlSnapshotStore snapshots, IResumeMarkdownRenderer renderer,
        CancellationToken ct)
    {
        var publicationPath = PublicationPath(context.Request.PathBase, context.Request.Path,
            publication.PublicId);
        var machineUri = publicationPath + "/jobml";
        var compactUri = publicationPath + "/cjobml";
        var transcriptUri = publicationPath + "/transcript";
        var snapshot = await snapshots.GetAsync(publication.Compilation.Manifest.SourceRevision, ct);
        var title = publication.Compilation.Manifest.TargetTitle ?? "Published resume";
        var body = renderer.ToHtml(publication.Compilation.PublishedMarkdown);
        var evidence = BuildEvidenceHtml(publication, snapshot, transcriptUri);
        return Results.Content(RenderPublishedPage(title, publication.ApplicationReference, body,
            evidence, machineUri, compactUri, transcriptUri), "text/html; charset=utf-8");
    }

    private static string PublicationPath(PathString pathBase, PathString requestPath, string publicId)
    {
        var value = requestPath.Value ?? string.Empty;
        var marker = "/" + publicId;
        var markerIndex = value.IndexOf(marker, StringComparison.Ordinal);
        var prefix = markerIndex < 0 ? value : value[..markerIndex];
        return pathBase + prefix + marker;
    }

    private static bool WantsHtml(HttpRequest request) =>
        request.GetTypedHeaders().Accept?.Any(value =>
            value.MediaType.Value?.Equals("text/html", StringComparison.OrdinalIgnoreCase) == true) == true;

    private static string BuildEvidenceHtml(ResumePublication publication, JobMlSnapshot? snapshot,
        string transcriptUri)
    {
        var encoder = HtmlEncoder.Default;
        var claims = publication.Compilation.ProjectedJobMl.Data.Claims
            .GroupBy(claim => claim.Subject, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var selectedClaims = publication.Compilation.Manifest.Sections
            .SelectMany(section => section.Claims)
            .GroupBy(selected => selected.Claim.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var items = publication.Compilation.ProjectedJobMl.Data.Entities
            .Where(entity => claims.ContainsKey(entity.Id))
            .Select(entity =>
            {
                var statements = string.Join(" ", claims[entity.Id]
                    .Select(claim => BuildClaimEvidenceHtml(claim, selectedClaims, transcriptUri, encoder)));
                var id = encoder.Encode(entity.Id);
                var name = encoder.Encode(entity.Name);
                var sourceAvailable = snapshot?.File.Data.Entities.Any(source =>
                    source.Id.Equals(entity.Id, StringComparison.OrdinalIgnoreCase)) == true;
                var source = sourceAvailable
                    ? $"<a href=\"{encoder.Encode(transcriptUri)}#{id}\">Read this section in the complete transcript</a>"
                    : "The private source transcript is not available from this host.";
                return $"<section class=\"evidence-card\" id=\"{id}\"><h3>{name}</h3><ul>{statements}</ul><p>{source}</p></section>";
            });
        return string.Join("", items);
    }

    private static string BuildClaimEvidenceHtml(lucidRESUME.JobML.JobMlClaim claim,
        Dictionary<string, SelectedClaim> selectedClaims, string transcriptUri,
        HtmlEncoder encoder)
    {
        var passage = selectedClaims.TryGetValue(claim.Id, out var selected) &&
                      !string.IsNullOrWhiteSpace(selected.Prose)
            ? $"<blockquote><strong>Reviewed source passage:</strong> {encoder.Encode(selected.Prose)}</blockquote>"
            : string.Empty;
        var evidence = claim.Evidence.Select(item =>
        {
            var label = item.Title ?? item.Qualification ?? item.Type;
            if (Uri.TryCreate(item.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                return $"<a href=\"{encoder.Encode(uri.AbsoluteUri)}\" rel=\"noopener noreferrer\">{encoder.Encode(label)}</a>";
            if (item.Type is "prose" or "source_ledger")
                return $"<a href=\"{encoder.Encode(transcriptUri)}\">{encoder.Encode(label)}</a>";
            return encoder.Encode(label);
        }).Distinct(StringComparer.Ordinal).ToList();
        var sources = evidence.Count == 0
            ? string.Empty
            : $"<p class=\"claim-sources\"><strong>Evidence:</strong> {string.Join(", ", evidence)}</p>";
        return $"<li><p>{encoder.Encode(claim.Statement)}</p>{passage}{sources}</li>";
    }

    private static async Task<IResult> PublishedExport(string publicId, string format,
        IResumePublicationStore publications, IEnumerable<IResumeExporter> exporters, CancellationToken ct)
    {
        var publication = await publications.GetAsync(publicId, ct);
        return publication is null
            ? Results.NotFound()
            : await ExportResult(publication.Compilation, format, exporters, true,
                publication.MinimumPages, ct);
    }

    private static void SetPublicationCacheHeaders(HttpContext context, ResumePublication publication)
    {
        context.Response.Headers.ETag = $"\"{publication.Compilation.CompilationId}\"";
        context.Response.Headers.LastModified = publication.PublishedAt.ToString("R");
        context.Response.Headers.CacheControl = "public,max-age=86400,immutable";
        context.Response.Headers.Append("X-Robots-Tag", "noindex, nofollow");
    }

    private static string RenderPublishedPage(string title, string? applicationReference, string body,
        string evidence, string machineUri, string compactUri, string transcriptUri)
    {
        var encodedTitle = HtmlEncoder.Default.Encode(title);
        var encodedReference = HtmlEncoder.Default.Encode(applicationReference ?? "Role-specific evidence projection");
        var encodedMachineUri = HtmlEncoder.Default.Encode(machineUri);
        var encodedCompactUri = HtmlEncoder.Default.Encode(compactUri);
        var encodedTranscriptUri = HtmlEncoder.Default.Encode(transcriptUri);
        return $$$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
            <meta name="robots" content="noindex,nofollow"><title>{{{encodedTitle}}}</title>
            <link rel="alternate" type="text/markdown" href="{{{encodedMachineUri}}}" title="Full JobML">
            <style>:root{font-family:Inter,system-ui,sans-serif;color:#172026;background:#eef2f1}body{margin:0}.page{max-width:860px;margin:28px auto;padding:18px}.meta{display:flex;justify-content:space-between;gap:16px;align-items:center;margin-bottom:14px;color:#526461;font-size:14px}.meta a,.resume a,.evidence a{color:#176b61}.resume,.evidence{background:#fff;border:1px solid #ccd6d5;box-shadow:0 8px 28px #243b3718;padding:42px 52px}.resume{font-family:Georgia,serif;line-height:1.5}.resume h1,.resume h2,.resume h3,.evidence h2,.evidence h3{font-family:Inter,system-ui,sans-serif}.resume h1{border-bottom:2px solid #176b61;padding-bottom:10px}.evidence{margin-top:22px}.evidence-card{border-top:1px solid #d7dfdd;padding:16px 0}.evidence-card:first-of-type{border-top:0}.evidence-card li{margin:12px 0}.evidence-card blockquote{margin:8px 0;padding:10px 14px;border-left:3px solid #9dafac;background:#f5f7f7}.claim-sources{font-size:14px;color:#526461}@media(max-width:700px){.page{margin:0;padding:0}.meta{padding:14px;flex-direction:column;align-items:flex-start}.resume,.evidence{border:0;padding:26px 22px}}</style></head>
            <body><main class="page"><nav class="meta"><span>{{{encodedReference}}}</span><span><a href="{{{encodedMachineUri}}}">Full JobML</a> · <a href="{{{encodedCompactUri}}}">cJobML</a> · <a href="{{{encodedTranscriptUri}}}">Complete transcript</a></span></nav><article class="resume">{{{body}}}</article><aside class="evidence"><h2>Evidence behind this résumé</h2><p>These are the accepted claims selected for this application. Follow each link to the fuller human account they came from.</p>{{{evidence}}}</aside></main></body></html>
            """;
    }

    private static string RenderTranscriptPage(string body, string publicationPath)
    {
        var encodedPath = HtmlEncoder.Default.Encode(publicationPath);
        return $$$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><meta name="robots" content="noindex,nofollow"><title>Complete career transcript</title>
            <style>:root{font-family:Inter,system-ui,sans-serif;color:#172026;background:#eef2f1}body{margin:0}.page{max-width:920px;margin:28px auto;padding:18px}.back{display:inline-block;margin-bottom:16px;color:#176b61}.transcript{background:#fff;border:1px solid #ccd6d5;padding:42px 52px;line-height:1.5}.transcript a{color:#176b61}@media(max-width:700px){.page{margin:0;padding:18px}.transcript{padding:26px 22px}}</style></head>
            <body><main class="page"><a class="back" href="{{{encodedPath}}}">Back to the tailored résumé</a><article class="transcript">{{{body}}}</article></main></body></html>
            """;
    }

    private static async Task<IResult> ExportResult(CompilationResult result, string format,
        IEnumerable<IResumeExporter> exporters, bool includeCitations, int minimumPages, CancellationToken ct)
    {
        var parsedFormat = format.ToUpperInvariant() switch
        {
            "MARKDOWN" or "MD" => ExportFormat.Markdown,
            "DOCX" => ExportFormat.Docx,
            "PDF" => ExportFormat.Pdf,
            _ => (ExportFormat?)null
        };
        if (parsedFormat is null) return Results.BadRequest(new { error = "Use markdown, docx or pdf." });
        var resume = ResumeProjectionMapper.Build(result, includeCitations, minimumPages);
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

    private const string Html = """
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><link rel="icon" href="data:,">
<title>lucidRESUME compiler</title><style>
:root{font-family:Inter,system-ui,sans-serif;color:#172026;background:#f5f7f7}body{margin:0}.shell{max-width:1180px;margin:auto;padding:32px}.grid{display:grid;grid-template-columns:1fr 1fr;gap:20px}.card{background:#fff;border:1px solid #ccd6d5;border-radius:12px;padding:20px}textarea,input[type=text],select{box-sizing:border-box;border:1px solid #9dafac;border-radius:8px;padding:12px}textarea,input[type=text]{width:100%}textarea{min-height:330px;font:14px ui-monospace,monospace}button,a.button{border:1px solid #176b61;background:#176b61;color:#fff;padding:10px 14px;border-radius:7px;text-decoration:none;cursor:pointer}.muted{color:#596b69}.actions{display:flex;gap:10px;align-items:center;flex-wrap:wrap;margin-top:12px}.preview{margin-top:18px;padding:30px 34px;min-height:500px;background:#fff;border:1px solid #d9dfde;box-shadow:0 8px 22px #243b3720;font-family:Georgia,serif;line-height:1.48}.preview h1{font:700 30px Inter,system-ui;border-bottom:2px solid #176b61;padding-bottom:10px}.preview h2{font:700 19px Inter,system-ui;margin-top:28px}.preview a{color:#176b61}.hidden{display:none}@media(max-width:800px){.grid{grid-template-columns:1fr}.shell{padding:16px}.preview{padding:22px}}
</style></head><body><main class="shell"><h1>Paste the job. Get the right version of you.</h1><p class="muted">Compile a focused résumé from a published JobML career record.</p><div class="grid"><section class="card"><h2>1. Career record</h2><p class="muted">Upload the complete human career transcript and its JobML career_record projection. The source may include every role, project, repository and linked article.</p><input id="careerRecord" type="file" accept=".md,text/markdown,text/plain"><div class="actions"><button id="publish">Publish career record</button><span id="status"></span></div><h2>2. Target role</h2><textarea id="job" placeholder="Paste the complete job description"></textarea><label for="applicationReference">Application label</label><input id="applicationReference" type="text" maxlength="160" placeholder="Planet DDS, Engineering Technical Lead"><div class="actions"><label><input id="polish" type="checkbox" checked> Select, tighten and redraft the reviewed prose</label><label><input id="includeCitations" type="checkbox" checked> Include cJobML references and evidence link</label><label><input id="publishProjection" type="checkbox" checked> Create a shareable evidence link</label><label for="minimumPages">Length</label><select id="minimumPages"><option value="2" selected>Minimum 2 pages</option><option value="1">Allow 1 page</option></select><select id="provider"><option value="openai">OpenAI</option><option value="llamasharp">Local LLamaSharp</option></select><button id="compile">Compile résumé</button></div></section><section class="card"><h2>Résumé projection</h2><div id="summary" class="muted">Nothing compiled yet.</div><div id="publication" class="actions hidden"><a id="publicationLink">Open the evidence-linked résumé</a></div><article id="output" class="preview"></article><div id="downloads" class="actions hidden"><a class="button" id="md">Markdown</a><a class="button" id="docx">Word</a><a class="button" id="pdf">PDF</a></div></section></div></main><script>
const token='__TOKEN__';const headers={'X-CSRF-TOKEN':token};
async function json(r){const t=await r.text();let x;try{x=JSON.parse(t)}catch{}if(!r.ok)throw new Error(x?.error??Object.values(x?.errors??{}).flat()[0]??t);return x}
document.querySelector('#publish').onclick=async()=>{try{const f=document.querySelector('#careerRecord').files[0];if(!f)throw new Error('Choose the JobML career record first.');const x=await json(await fetch(location.pathname+'api/career-record',{method:'POST',headers:{...headers,'Content-Type':'text/markdown'},body:await f.text()}));document.querySelector('#status').textContent='Published '+x.revision.slice(0,12)}catch(e){document.querySelector('#status').textContent=e.message}};
document.querySelector('#compile').onclick=async()=>{const s=document.querySelector('#summary');try{s.textContent='Compiling from the published career record...';const x=await json(await fetch(location.pathname+'api/compile',{method:'POST',headers:{...headers,'Content-Type':'application/json'},body:JSON.stringify({jobDescription:document.querySelector('#job').value,polish:document.querySelector('#polish').checked,provider:document.querySelector('#provider').value,includeCitations:document.querySelector('#includeCitations').checked,publish:document.querySelector('#publishProjection').checked,minimumPages:Number(document.querySelector('#minimumPages').value),applicationReference:document.querySelector('#applicationReference').value})}));s.textContent=`${x.manifest.sections.length} source section${x.manifest.sections.length===1?'':'s'}, ${x.manifest.gaps.length} honest gap${x.manifest.gaps.length===1?'':'s'}. ${x.usedCompositionProvider?'Polished with '+x.compositionProvider:'Exact selected prose.'}`;document.querySelector('#output').innerHTML=x.publishedHtml;for(const k of ['md','docx','pdf'])document.querySelector('#'+k).href=x.downloads[k==='md'?'markdown':k];document.querySelector('#downloads').classList.remove('hidden');const p=document.querySelector('#publication');if(x.publication){const a=document.querySelector('#publicationLink');a.href=x.publication.url;a.textContent='Open the evidence-linked résumé';p.classList.remove('hidden')}else p.classList.add('hidden')}catch(e){s.textContent=e.message}};
</script></body></html>
""";
}
