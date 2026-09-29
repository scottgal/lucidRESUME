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
        var html = await _client.GetStringAsync("/resume/");
        Assert.Contains("Career record", html);
        Assert.Contains("complete human career transcript", html);
        Assert.Contains("JobML career_record projection", html);
        Assert.Contains("Paste the job. Get the right version of you.", html);
    }

    [Fact]
    public async Task Mutation_without_antiforgery_token_is_rejected()
    {
        var response = await _client.PostAsync("/resume/api/ledger",
            new StringContent("not a ledger", Encoding.UTF8, "text/markdown"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Compile_rejects_oversized_job_description_before_running_compiler()
    {
        var html = await _client.GetStringAsync("/resume/");
        var token = Regex.Match(html, "const token='(?<token>[^']+)'", RegexOptions.CultureInvariant)
            .Groups["token"].Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/resume/api/compile");
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
        var html = await _client.GetStringAsync("/resume/");
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
        using var publish = new HttpRequestMessage(HttpMethod.Post, "/resume/api/career-record");
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
        var html = await _client.GetStringAsync("/resume/");
        var token = Regex.Match(html, "const token='(?<token>[^']+)'", RegexOptions.CultureInvariant)
            .Groups["token"].Value;
        const string prose = "Led a TypeScript engineering team through platform change on AWS.";
        const string employmentDates = "Engineering Lead · Example Ltd | 2022-01-01 | Present";
        const string additionalDates = "Technical Consultant · Earlier Ltd | 2018-01-01 | 2018-05-01";
        const string exactQuarterDates = "Short Consultant · Brief Ltd | 2017-01-01 | 2017-04-01";
        const string education = "BSc (Hons) Computer Science | Example University";
        var fingerprint = MarkdownEvidenceIndex.Fingerprint(prose);
        var dateFingerprint = MarkdownEvidenceIndex.Fingerprint(employmentDates);
        var additionalDateFingerprint = MarkdownEvidenceIndex.Fingerprint(additionalDates);
        var exactQuarterDateFingerprint = MarkdownEvidenceIndex.Fingerprint(exactQuarterDates);
        var educationFingerprint = MarkdownEvidenceIndex.Fingerprint(education);
        var source = $$"""
            # Jane Smith

            ## Example Ltd {#example-role}

            <p id="leadership">
            {{prose}}
            </p>

            <p id="employment-dates">
            {{employmentDates}}
            </p>

            ## Earlier Ltd {#earlier-role}

            <p id="earlier-dates">
            {{additionalDates}}
            </p>

            ## Brief Ltd {#brief-role}

            <p id="brief-dates">
            {{exactQuarterDates}}
            </p>

            ## Education {#example-education}

            <p id="degree">
            {{education}}
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
              - id: example-education
                type: education
                name: {{education}}
                source: "#example-education"
              - id: earlier-role
                type: experience
                name: Technical Consultant · Earlier Ltd
                source: "#earlier-role"
              - id: brief-role
                type: experience
                name: Short Consultant · Brief Ltd
                source: "#brief-role"
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
                  - id: imported-example-role
                    type: source_ledger
                    ref: ledger://experience/example-role
                    title: Imported career record
              - id: example-role-dates
                subject: example-role
                type: experience
                statement: {{employmentDates}}
                origin: declared
                review: accepted
                concepts: {}
                supported_by:
                  - type: prose
                    ref: "#employment-dates"
                    fingerprint:
                      text: "{{dateFingerprint}}"
              - id: degree
                subject: example-education
                type: education
                statement: Holds a BSc (Hons) in Computer Science from Example University.
                origin: declared
                review: accepted
                concepts: {}
                supported_by:
                  - type: prose
                    ref: "#degree"
                    fingerprint:
                      text: "{{educationFingerprint}}"
              - id: earlier-role-dates
                subject: earlier-role
                type: experience
                statement: {{additionalDates}}
                origin: declared
                review: accepted
                concepts: {}
                supported_by:
                  - type: prose
                    ref: "#earlier-dates"
                    fingerprint:
                      text: "{{additionalDateFingerprint}}"
                  - id: imported-earlier-role
                    type: source_ledger
                    ref: ledger://experience/earlier-role
                    title: Imported career record
              - id: brief-role-dates
                subject: brief-role
                type: experience
                statement: {{exactQuarterDates}}
                origin: declared
                review: accepted
                concepts: {}
                supported_by:
                  - type: prose
                    ref: "#brief-dates"
                    fingerprint:
                      text: "{{exactQuarterDateFingerprint}}"
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
        using var publish = new HttpRequestMessage(HttpMethod.Post, "/resume/api/career-record");
        publish.Headers.Add("X-CSRF-TOKEN", token);
        publish.Content = new StringContent(source, Encoding.UTF8, "text/markdown");

        var published = await _client.SendAsync(publish);
        var publicationPayload = await published.Content.ReadAsStringAsync();
        Assert.True(published.StatusCode == HttpStatusCode.OK,
            $"Expected career record publication to succeed, got {(int)published.StatusCode}: {publicationPayload}");
        using var compile = new HttpRequestMessage(HttpMethod.Post, "/resume/api/compile");
        compile.Headers.Add("X-CSRF-TOKEN", token);
        compile.Content = new StringContent(JsonSerializer.Serialize(new
        {
            jobDescription = "Head of Engineering. TypeScript and AWS experience required. Lead engineering change.",
            polish = false,
            applicationReference = "Example Ltd, Head of Engineering"
        }), Encoding.UTF8, "application/json");
        var compiled = await _client.SendAsync(compile);
        var payload = await compiled.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, compiled.StatusCode);
        Assert.Contains(prose, payload);
        Assert.Contains("cJobML 0.1", payload);

        using var json = JsonDocument.Parse(payload);
        Assert.Equal("Head of Engineering",
            json.RootElement.GetProperty("manifest").GetProperty("targetTitle").GetString());
        var publicationUrl = json.RootElement.GetProperty("publication").GetProperty("url").GetString()!;
        Assert.Matches(@"^/resume/[A-Za-z0-9_-]{32}$", publicationUrl);
        Assert.Equal("Example Ltd, Head of Engineering",
            json.RootElement.GetProperty("publication").GetProperty("applicationReference").GetString());
        var publishedMarkdown = json.RootElement.GetProperty("publishedMarkdown").GetString()!;
        var fullRecordUri = $"http://localhost{publicationUrl}/jobml";
        Assert.Contains($"Full JobML: <{fullRecordUri}>", publishedMarkdown);
        Assert.Contains($"<{fullRecordUri}#example-role>", publishedMarkdown);
        Assert.Contains("[Career Transcript]", publishedMarkdown);
        var docxUrl = json.RootElement.GetProperty("downloads").GetProperty("docx").GetString()!;
        var docx = await _client.GetByteArrayAsync(docxUrl);
        using var archive = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var documentReader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        var documentXml = await documentReader.ReadToEndAsync();
        Assert.Contains(prose, documentXml);
        Assert.Contains("w:type=\"page\"", documentXml);
        Assert.Contains("Target role: Head of Engineering", documentXml);
        Assert.Contains("Jan 2022 – Present", documentXml);
        Assert.Contains("Additional consulting, contract and earlier experience", documentXml);
        Assert.Contains("Technical Consultant", documentXml);
        Assert.Contains("Earlier Ltd", documentXml);
        Assert.Contains("Jan 2018 – May 2018", documentXml);
        Assert.DoesNotContain("Brief Ltd", documentXml);
        Assert.Contains("BSc (Hons) Computer Science — Example University", documentXml);
        Assert.Matches(@"BSc \(Hons\) Computer Science — Example University.*?\[\d+\]", documentXml);
        using var relationshipsReader = new StreamReader(
            archive.GetEntry("word/_rels/document.xml.rels")!.Open());
        var relationshipsXml = await relationshipsReader.ReadToEndAsync();
        Assert.Contains($"{fullRecordUri}#example-role", relationshipsXml);

        var evidencePage = await _client.GetStringAsync(publicationUrl);
        Assert.Contains("Evidence behind this résumé", evidencePage);
        Assert.Contains("Read this section in the complete transcript", evidencePage);
        Assert.Contains("Reviewed source passage:", evidencePage);
        Assert.Contains("Example Ltd, Head of Engineering", evidencePage);
        Assert.Contains(prose, evidencePage);

        var roleJobMl = await _client.GetStringAsync(publicationUrl + "/jobml");
        Assert.Contains("profile: resume", roleJobMl);
        Assert.Contains("full_jobml:", roleJobMl);

        using var browserJobMlRequest = new HttpRequestMessage(HttpMethod.Get, publicationUrl + "/jobml");
        browserJobMlRequest.Headers.Accept.ParseAdd("text/html");
        using var browserJobMl = await _client.SendAsync(browserJobMlRequest);
        var browserEvidencePage = await browserJobMl.Content.ReadAsStringAsync();
        Assert.Equal("text/html", browserJobMl.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Evidence behind this résumé", browserEvidencePage);
        Assert.Contains("id=\"example-role\"", browserEvidencePage);

        using var browserTranscriptRequest = new HttpRequestMessage(HttpMethod.Get, publicationUrl + "/transcript");
        browserTranscriptRequest.Headers.Accept.ParseAdd("text/html");
        using var browserTranscript = await _client.SendAsync(browserTranscriptRequest);
        var transcriptPage = await browserTranscript.Content.ReadAsStringAsync();
        Assert.Contains("Complete career transcript", transcriptPage);
        Assert.Contains(prose, transcriptPage);
        Assert.Contains("id=\"example-role\"", transcriptPage);

        var fullJobMl = await _client.GetStringAsync("/resume/api/jobml");
        Assert.Contains("profile: career_record", fullJobMl);

        using var citationFreeCompile = new HttpRequestMessage(HttpMethod.Post, "/resume/api/compile");
        citationFreeCompile.Headers.Add("X-CSRF-TOKEN", token);
        citationFreeCompile.Content = new StringContent(JsonSerializer.Serialize(new
        {
            jobDescription = "Head of Engineering. TypeScript and AWS experience required.",
            polish = false,
            publish = true,
            includeCitations = false,
            minimumPages = 1
        }), Encoding.UTF8, "application/json");
        using var citationFreeResponse = await _client.SendAsync(citationFreeCompile);
        var citationFreePayload = await citationFreeResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, citationFreeResponse.StatusCode);
        using var citationFreeJson = JsonDocument.Parse(citationFreePayload);
        Assert.Equal(JsonValueKind.Null, citationFreeJson.RootElement.GetProperty("publication").ValueKind);
        var citationFreeMarkdown = citationFreeJson.RootElement.GetProperty("publishedMarkdown").GetString()!;
        Assert.Contains(prose, citationFreeMarkdown);
        Assert.DoesNotContain("cJobML 0.1", citationFreeMarkdown);
        Assert.DoesNotContain("## References", citationFreeMarkdown);
        Assert.DoesNotContain("MACHINE AREA", citationFreeMarkdown);

        var citationFreeDocxUrl = citationFreeJson.RootElement.GetProperty("downloads")
            .GetProperty("docx").GetString()!;
        var citationFreeDocx = await _client.GetByteArrayAsync(citationFreeDocxUrl);
        using var citationFreeArchive = new ZipArchive(new MemoryStream(citationFreeDocx), ZipArchiveMode.Read);
        using var citationFreeDocumentReader = new StreamReader(
            citationFreeArchive.GetEntry("word/document.xml")!.Open());
        var citationFreeDocumentXml = await citationFreeDocumentReader.ReadToEndAsync();
        Assert.Contains(prose, citationFreeDocumentXml);
        Assert.DoesNotContain("w:type=\"page\"", citationFreeDocumentXml);
        Assert.DoesNotContain("References", citationFreeDocumentXml);
        Assert.DoesNotContain("cJobML", citationFreeDocumentXml);
    }
}
