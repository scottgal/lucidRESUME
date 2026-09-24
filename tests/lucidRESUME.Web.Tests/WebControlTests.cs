using System.Net;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using lucidRESUME.JobML;
using Microsoft.AspNetCore.Mvc.Testing;

namespace lucidRESUME.Web.Tests;

public sealed class WebControlTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public WebControlTests(WebApplicationFactory<Program> factory) =>
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });

    [Fact]
    public async Task Page_explains_career_record_to_resume_projection_flow()
    {
        var html = await _client.GetStringAsync("/lucidresume/");
        Assert.Contains("Career record", html);
        Assert.Contains("complete human career transcript", html);
        Assert.Contains("JobML career_record projection", html);
        Assert.Contains("Paste the job. Get the right version of you.", html);
    }

    [Fact]
    public async Task Mutation_without_antiforgery_token_is_rejected()
    {
        var response = await _client.PostAsync("/lucidresume/api/ledger",
            new StringContent("not a ledger", Encoding.UTF8, "text/markdown"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Compile_rejects_oversized_job_description_before_running_compiler()
    {
        var html = await _client.GetStringAsync("/lucidresume/");
        var token = Regex.Match(html, "const token='(?<token>[^']+)'", RegexOptions.CultureInvariant)
            .Groups["token"].Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/lucidresume/api/compile");
        request.Headers.Add("X-CSRF-TOKEN", token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { jobDescription = new string('x', 262145) }),
            Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Career_record_endpoint_rejects_a_role_specific_profile()
    {
        var html = await _client.GetStringAsync("/lucidresume/");
        var token = Regex.Match(html, "const token='(?<token>[^']+)'", RegexOptions.CultureInvariant)
            .Groups["token"].Value;
        const string source = """
            # Jane Smith

            ```jobml
            jobml:
              version: "0.1"
              profile: resume
              purpose: A role-specific resume.
              semantics:
                - Do not infer unsupported claims.
            document:
              id: jane-resume
              language: en-GB
            entities: []
            claims: []
            concepts: []
            ```
            """;
        using var publish = new HttpRequestMessage(HttpMethod.Post, "/lucidresume/api/career-record");
        publish.Headers.Add("X-CSRF-TOKEN", token);
        publish.Content = new StringContent(source, Encoding.UTF8, "text/markdown");

        var response = await _client.SendAsync(publish);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("jobml.profile: career_record", payload);
    }

    [Fact]
    public async Task Career_record_is_published_compiled_and_exported_end_to_end()
    {
        var html = await _client.GetStringAsync("/lucidresume/");
        var token = Regex.Match(html, "const token='(?<token>[^']+)'", RegexOptions.CultureInvariant)
            .Groups["token"].Value;
        const string prose = "Led a TypeScript engineering team through platform change on AWS.";
        var fingerprint = MarkdownEvidenceIndex.Fingerprint(prose);
        var source = $$"""
            # Jane Smith

            ## Example Ltd {#example-role}

            <p id="leadership">
            {{prose}}
            </p>

            ---

            ```jobml
            jobml:
              version: "0.1"
              profile: career_record
              purpose: Complete portable career record.
              semantics:
                - Claims describe experience, skills, capabilities, responsibilities, or domain knowledge.
                - Every substantive claim should be supported by evidence.
                - Do not infer unsupported claims.
            document:
              id: jane-career-record
              language: en-GB
            entities:
              - id: example-role
                type: experience
                name: Example Ltd
                source: "#example-role"
            claims:
              - id: leadership
                subject: example-role
                statement: Led a TypeScript engineering team through platform change on AWS.
                origin: declared
                review: accepted
                concepts:
                  skills: [typescript, aws]
                  capabilities: [engineering-leadership]
                supported_by:
                  - type: prose
                    ref: "#leadership"
                    fingerprint:
                      text: "{{fingerprint}}"
            concepts:
              - id: typescript
                type: skill
                name: TypeScript
              - id: aws
                type: skill
                name: AWS
              - id: engineering-leadership
                type: capability
                name: Engineering Leadership
            ```
            """;
        using var publish = new HttpRequestMessage(HttpMethod.Post, "/lucidresume/api/career-record");
        publish.Headers.Add("X-CSRF-TOKEN", token);
        publish.Content = new StringContent(source, Encoding.UTF8, "text/markdown");

        var published = await _client.SendAsync(publish);
        var publicationPayload = await published.Content.ReadAsStringAsync();
        Assert.True(published.StatusCode == HttpStatusCode.OK,
            $"Expected career record publication to succeed, got {(int)published.StatusCode}: {publicationPayload}");

        using var compile = new HttpRequestMessage(HttpMethod.Post, "/lucidresume/api/compile");
        compile.Headers.Add("X-CSRF-TOKEN", token);
        compile.Content = new StringContent(JsonSerializer.Serialize(new
        {
            jobDescription = "Head of Engineering. TypeScript and AWS experience required. Lead engineering change.",
            polish = false
        }), Encoding.UTF8, "application/json");
        var compiled = await _client.SendAsync(compile);
        var payload = await compiled.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, compiled.StatusCode);
        Assert.Contains(prose, payload);
        Assert.Contains("cJobML 0.1", payload);
        Assert.Contains("/api/export/", payload);

        using var json = JsonDocument.Parse(payload);
        var docxUrl = json.RootElement.GetProperty("downloads").GetProperty("docx").GetString()!;
        var docx = await _client.GetByteArrayAsync(docxUrl);
        using var archive = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var documentReader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        var documentXml = await documentReader.ReadToEndAsync();
        Assert.Contains(prose, documentXml);

        var fullJobMl = await _client.GetStringAsync("/lucidresume/api/jobml");
        Assert.Contains("profile: career_record", fullJobMl);
    }
}
