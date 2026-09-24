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

If polishing is disabled, the projection uses the selected passages exactly. If
enabled, the orchestrator runs two edits:

1. `Tighten` removes irrelevant wording and foregrounds already-supported detail.
2. `HumanVoice` removes generic model phrasing while preserving the author's stance and vocabulary.

Both passes receive the immutable source blocks as well as the current draft.
They receive only the requirements already matched to each selected section, not
the full untrusted vacancy as a source of candidate facts. The local grug provider
edits one section at a time to keep its context small and its JSON contract reliable.
After every pass, validation rejects unknown sections, claims, evidence IDs,
numbers, and over-budget text. A rejected or failed pass leaves the last valid
human-based version in place.

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
    "RequireAuthenticatedWriter": true
  },
  "Tailoring": { "Provider": "llamasharp" },
  "LlamaSharp": {
    "ModelPath": "models/grug-9b-Q4_K_M.gguf"
  }
}
```

OpenAI is optional. Configure `OpenAi:ApiKey` through server-side configuration
or secrets, never browser JavaScript or a checked-in settings file. The OpenAI
provider uses the Responses API with strict structured output and `store: false`.

## Endpoints

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/lucidresume/` | Upload, paste, preview and download control |
| `GET` | `/lucidresume/api/status` | Current master revision |
| `POST` | `/lucidresume/api/career-record` | Validate and publish complete Markdown + JobML |
| `POST` | `/lucidresume/api/ledger` | Compatibility alias for career-record publication |
| `POST` | `/lucidresume/api/compile` | Compile a job-specific projection |
| `GET/HEAD` | `/lucidresume/api/jobml` | Current full JobML career record |
| `GET` | `/lucidresume/api/jobml/{revision}` | Immutable career-record revision |
| `GET` | `/lucidresume/api/export/{id}/{format}` | `markdown`, `docx`, or `pdf` |

Mutation endpoints require an authenticated identity by default and always require
antiforgery validation. The local sample explicitly disables the authentication
gate; do not copy that setting to a public host. Upload size is bounded and the
control never fetches URLs contained in uploaded JobML.

## Verification

The compiler test suite covers immutable snapshots, drift rejection, source-prose
selection, honest gaps, rejection of invented numeric facts, live bounded edits
with OpenAI and the installed grug 9B model, and endpoint-only cJobML. The sample host
is also exercised in a real browser. The checked smoke flow publishes the example
master, compiles a VP Engineering projection, renders the document, and produces
valid Word and PDF files.
