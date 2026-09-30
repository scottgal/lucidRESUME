# JobML web compiler

The web compiler turns one exhaustive, human-authored career record into shorter
role-specific résumés. It is deliberately not a general résumé ingestion service
and it is not an automated application agent.

```text
LinkedIn + repositories + historical CVs
                    |
          upstream ingestion and review
                    |
      canonical career ledger
                    |
          JobML career record
                    |
            immutable publication
                    |
 job description -> requirements -> evidence selection
                    |
      bounded tightening and voice passes (optional)
                    |
       Markdown + cJobML + Word + PDF
                    |
       opaque application evidence URL
```

## Source contract

The input is a complete Markdown career transcript with an embedded
`career_record` JobML block. In practice it may be ten or more pages because it
records all useful role detail,
responsibilities, projects, linked posts and external sources. That completeness
is a feature. It is a portable projection of the canonical application ledger,
which may additionally retain private inputs, review decisions, and indexes.

Publishing validates JobML and reconciles every accepted claim. Changed,
missing, or ambiguous accepted evidence blocks publication. A successful publish
creates a content-addressed immutable revision and atomically advances the
`current` pointer.

## Compilation invariant

> Every output passage starts as human prose selected from the complete career transcript.

The target job controls selection, order and emphasis. It cannot supply candidate
facts. The deterministic planner parses required skills, preferred skills and
responsibilities, matches these to accepted claims and aliases, rejects stale or
unreviewed evidence, and selects a diverse set with per-role limits. Genuine
requirements with no accepted evidence are reported as gaps.

Before optional polishing, the deterministic planner selects complete sentences
from the reviewed passages and fits them to compact per-section budgets. It drops
a lower-ranked claim rather than chopping prose or dumping every matching duty.
If polishing is disabled, this human-authored selection is the final prose. If
enabled, the orchestrator runs two edits:

1. `Tighten` removes irrelevant wording and foregrounds already-supported detail.
2. `HumanVoice` removes generic model phrasing while preserving the author's stance and vocabulary.

Both passes receive the immutable source blocks as well as the current draft.
The vacancy guides emphasis but never supplies candidate facts. Ollama edits the
summary alone without the vacancy text, then edits experience in ordered batches
of three with the vacancy as context. Its summary prompt asks for a shorter
version of the reviewed facts. The hard word limit remains the section budget in
the projection manifest. Other providers receive the selected source and draft
through the same evidence-bounded composition contract.
The LLamaSharp adapter can recover independently complete section objects from
truncated JSON. After every pass, validation accepts or rejects each
section independently. Unknown sections, changed claim or evidence identities,
invented numbers, unsupported vacancy terminology, and over-budget text are
discarded. Each rejected or incomplete section retains its last valid
human-based version.

The human projection remains conventional as well as evidence-aware. It prints
the detected target title, a compact `Core Skills` index containing only
canonical concepts attached to claims that survive into the rendered projection,
reverse-chronological experience, role-relevant projects, and reviewed education.
The vacancy may rank the skills index but cannot contribute its vocabulary.
Senior role titles give a bounded preference to accepted productisation evidence,
while hands-on roles continue to favour implementation evidence.

Detailed roles contain only the claims selected for this target. A final
`Additional consulting, contract and earlier experience` subsection preserves
the broader chronology without expanding every engagement into bullets. It is
built directly from accepted `experience` claims which were not selected for
detail and whose date range is strictly longer than three months. The default
threshold is configurable through `CompilationOptions`, and deliberately excludes
an engagement lasting exactly three months. Each compact line retains its
projected JobML claim, drift-checked evidence, full-transcript link, and cJobML
citation. These lines bypass every model editing pass.

This is a projection with optional bounded editing, not generation from a blank
prompt.

## ASP.NET Core setup

Reference `src/lucidRESUME.Web`, then register and map it:

```csharp
using lucidRESUME.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLucidResumeCompiler(builder.Configuration);

var app = builder.Build();
app.UseAntiforgery();
app.MapLucidResumeCompiler();
app.Run();
```

Minimal configuration:

```json
{
  "LucidResumeCompiler": {
    "SnapshotDirectory": "App_Data/jobml",
    "MaximumUploadBytes": 16777216,
    "MaximumJobDescriptionBytes": 262144,
    "CompilationCacheMinutes": 30,
    "PublicBaseUri": "https://careers.example.com/resume",
    "RequireAuthenticatedWriter": true
  },
  "LucidResumeWeb": {
    "PublicationDirectory": "App_Data/resume-publications",
    "PublishCompiledResumes": true
  },
  "Tailoring": { "Provider": "llamasharp" },
  "LlamaSharp": {
    "ModelPath": "models/grug-9b-Q4_K_M.gguf"
  }
}
```

Set `PublicBaseUri` when the control is compiled behind a reverse proxy or on a
developer machine. cJobML citations then point at the externally reachable,
application-specific evidence URL instead of `localhost`. If it is omitted, the
request scheme, host and mapped route are used.

The default renderer is Markdig with raw HTML disabled. A host with an established
Markdown pipeline can register its own `IResumeMarkdownRenderer` before calling
`AddLucidResumeCompiler`. A direct in-process host can mount the control like this:

```csharp
builder.Services.AddScoped<IResumeMarkdownRenderer, MostlylucidResumeRenderer>();
builder.Services.AddLucidResumeCompiler(builder.Configuration);

// after UseAuthentication, UseAuthorization, and UseAntiforgery
app.MapLucidResumeCompiler("/resume");
```

The current Mostlylucid integration instead uses a same-origin gateway at
`/resume`, because `lucidRESUME.Web` is not yet published as a NuGet package.
The site forwards GET, HEAD and POST to a separately running compiler host.
Public evidence pages and downloads remain readable; POST requires the Google
account email configured as `LucidResumeProxy__WriterEmail`. The gateway forwards
only the compiler's dedicated `lucidresume.csrf` cookie and never forwards the
site's login cookie.

For a local run, start the sample host on loopback with
`ASPNETCORE_URLS=http://127.0.0.1:5098` and set
`LucidResumeCompiler__PublicBaseUri=https://mostlylucid.net/resume` in its
environment. Set `LucidResumeProxy__UpstreamUri=http://127.0.0.1:5098/` and
`LucidResumeProxy__WriterEmail` in the site environment. The compiler's
`SnapshotDirectory` and `PublicationDirectory` must be persistent across restarts,
or old verification links will stop resolving. Keep the compiler host reachable
only by the site gateway when using the sample's disabled writer-authentication
setting. On another public hostname, set `PublicBaseUri` to that actual hostname.

OpenAI is optional. Configure `OpenAi:ApiKey` through server-side configuration
or secrets, never browser JavaScript or a checked-in settings file. The OpenAI
provider uses the Responses API with strict structured output and `store: false`.

## Endpoints

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/resume/` | Upload, paste, preview, publish and download control |
| `GET` | `/resume/api/status` | Current master revision |
| `POST` | `/resume/api/career-record` | Validate and publish complete Markdown + JobML |
| `POST` | `/resume/api/ledger` | Compatibility alias for career-record publication |
| `POST` | `/resume/api/compile` | Compile a job-specific projection and optionally publish it |
| `GET/HEAD` | `/resume/api/jobml` | Current full JobML career record |
| `GET` | `/resume/api/jobml/{revision}` | Immutable career-record revision |
| `GET` | `/resume/api/export/{id}/{format}` | Short-lived preview export compatibility route |
| `GET` | `/resume/{publicId}` | Human application résumé and evidence view |
| `GET/HEAD` | `/resume/{publicId}/jobml` | Full role-specific JobML; renders the evidence view for browser clients |
| `GET` | `/resume/{publicId}/cjobml` | Compact résumé and cJobML Markdown |
| `GET` | `/resume/{publicId}/transcript` | Complete source transcript, rendered for browsers or Markdown for machines |
| `GET` | `/resume/{publicId}/transcript/chunks` | Ordered source chunk manifest with offsets, SHA-256 hashes and links |
| `GET` | `/resume/{publicId}/transcript/chunks/{index}` | Exact Markdown source chunk for deterministic reassembly |
| `GET` | `/resume/{publicId}/download/{format}` | Durable `markdown`, `docx`, or `pdf` application export |

For the adjacent Mostlylucid checkout, run `scripts/run-local-resume-integration.sh`
from this repository. It starts the compiler on `127.0.0.1:5098` and the site on
`127.0.0.1:8080/resume`, with file-mode and local analytics placeholders. The
loopback writer setting is explicit and accepts uploads only when the request
host, client, and compiler upstream are all loopback addresses. It also sets
`LucidResumeCompiler__PublicBaseUri` so citations inside Word, Markdown and
JobML point to the site's `/resume` route.

The local script enables Ollama prose editing with `qwen3.5:latest` on
`127.0.0.1:11435`. Start an Ollama server on that port and install the model
there before selecting **Ollama** in the compile form:

```sh
OLLAMA_HOST=127.0.0.1:11435 ollama serve
OLLAMA_HOST=127.0.0.1:11435 ollama pull qwen3.5:latest
```

Run the server and pull commands in separate terminals. To use a different
local server or model, set `Ollama__BaseUrl` and `Ollama__Model` before starting
the integration script. The editor works on selected source passages in
small batches and runs a tightening pass followed by a natural-language pass;
each returned section is checked against its source IDs, numeric facts and word
budget. A failed or rejected edit leaves the selected source prose in place.

The public ID is generated from 192 bits of cryptographic randomness. It identifies
the exact application projection and source revision; it does not record who opens
the URL. Publications are marked `noindex`, but the opaque URL is a bearer link,
not an authorization boundary. The complete transcript may contain more detail
than the short résumé, so a public host should add its own authorization policy if
unguessable links are not sufficient for that deployment.

Mutation endpoints require an authenticated identity by default and always require
antiforgery validation. The local sample explicitly disables the authentication
gate; do not copy that setting to a public host. Upload size is bounded and the
control never fetches URLs contained in uploaded JobML.

## Verification

The compiler test suite covers immutable snapshots, drift rejection, compact
source-sentence selection, honest gaps, partial-pass recovery, rejection of
invented numeric facts, strict additional-experience duration boundaries, live
bounded edits with OpenAI and the installed grug 9B model, and endpoint-only
cJobML. The sample host
is also exercised in a real browser. The checked smoke flow publishes the example
master, compiles a VP Engineering projection, renders the document, and produces
valid Word and PDF files.
