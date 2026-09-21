# Jev-assisted parsing and benchmarking

## Decision

Jev is an optional decision layer inside ingestion. It is not a resume parser,
an evidence generator, or part of rendering.

The ingestion order is:

```text
document layout and text
    -> deterministic parsing and NER candidates
    -> bounded Jev decision for unresolved ambiguity
    -> probability and margin policy
    -> accepted value or human review
    -> evidence ledger
    -> deterministic projections
```

This preserves the central invariant: output is projected from the ledger. It is
never re-inferred while a resume is being rendered.

Jev is suited to this narrow role because it accepts shared state and typed
questions, then returns a choice and a probability distribution. It does not
write resume prose. The caller owns every possible answer. See the
[Jev model card](https://vercel.com/ai-gateway/models/jev).

## Implemented decisions

The first experiment supports three bounded decisions:

1. Classify a top-level section when deterministic heading rules abstain.
2. Select the resume owner's name from candidates already found by positional
   rules and NER.
3. Select an employer from `Organization` NER spans when an experience entry has
   no deterministic company value.

Existing deterministic names and employer values are not overwritten. Jev
cannot return an arbitrary string. A `none` or `other` candidate gives it a real
abstention path.

An accepted decision must clear both configured gates:

```text
selected probability >= 0.80
winner minus runner-up >= 0.20
```

These are policy defaults, not universal truth. The benchmark exists to
calibrate them.

## Audit and drift

Every completed decision is recorded in `ResumeDocument.IngestionDecisions`.
The record contains:

- decision contract version;
- stable source reference;
- FNV-1a 64 source hash;
- candidate-set hash;
- selected candidate and original selected value;
- full probability distribution, confidence, and margin;
- acceptance result;
- provider and exact model;
- token counts and provider request ID;
- evaluation time.

The source passage itself is not copied into the audit record. If the source hash
changes, the old result is stale and must not be reused. Rendering reads the
accepted ledger state and does not call Jev.

## Privacy and configuration

Jev is disabled by default. Enabling it sends bounded resume passages and
candidate values to TypeSafe's hosted API. Email addresses, phone numbers, and
URLs are redacted from the state, but candidate names and organisations cannot
be redacted without destroying the decision. Review the
[TypeSafe privacy policy](https://typesafe.ai/legal/privacy-policy) before opting
in.

The desktop Profile page stores API keys in macOS Keychain, Windows Credential
Manager, or the Linux Secret Service. Only non-secret settings are written to
`ai-settings.json`. Existing plaintext keys are migrated and removed. Linux
requires `secret-tool` and a working Secret Service provider.

For development and automation, user secrets or environment variables remain
available. Do not put a real key in `appsettings.json`.

```bash
dotnet user-secrets set "Jev:Enabled" true --project src/lucidRESUME
dotnet user-secrets set "Jev:ApiKey" "$TYPESAFE_API_KEY" --project src/lucidRESUME
```

For environment configuration, the desktop application's prefix is required:

```bash
export LUCIDRESUME_Jev__Enabled=true
export LUCIDRESUME_Jev__ApiKey="$TYPESAFE_API_KEY"
```

Configuration:

```json
{
  "Jev": {
    "Enabled": false,
    "BaseUrl": "https://api.typesafe.ai",
    "ApiKey": "",
    "Model": "jev-1.13.0",
    "AcceptanceProbability": 0.80,
    "MinimumMargin": 0.20,
    "MaxStateCharacters": 4000,
    "RedactContactDetails": true
  }
}
```

The model is pinned so a benchmark remains meaningful. A model update should be
treated as an experiment change and measured before becoming the default.

## Benchmarks

`lucidRESUME.ParsingBenchmarks` produces machine-readable JSON and an
article-ready Markdown report. Its fixtures are synthetic and safe to publish.
No personal resume data is checked in.

Run the current deterministic baseline:

```bash
dotnet run --project tools/lucidRESUME.ParsingBenchmarks -- \
  --suite section --provider deterministic

dotnet run --project tools/lucidRESUME.ParsingBenchmarks -- \
  --suite entity --provider deterministic
```

Run the same corpus against Jev and the baseline:

```bash
export TYPESAFE_API_KEY="..."

dotnet run --project tools/lucidRESUME.ParsingBenchmarks -- \
  --suite section --provider jev --repetitions 5 \
  --input-price-per-million 0.04

dotnet run --project tools/lucidRESUME.ParsingBenchmarks -- \
  --suite entity --provider jev --repetitions 5 \
  --input-price-per-million 0.04
```

Compare the primary full-strength OpenAI path on the same frozen fixtures:

```bash
export OPENAI_API_KEY="..."

dotnet run --project tools/lucidRESUME.ParsingBenchmarks -- \
  --suite section --provider openai --repetitions 5 \
  --openai-model gpt-5.6-luna

dotnet run --project tools/lucidRESUME.ParsingBenchmarks -- \
  --suite entity --provider openai --repetitions 5 \
  --openai-model gpt-5.6-luna
```

The local LLamaSharp/grug lane is explicitly experimental:

```bash
dotnet run --project tools/lucidRESUME.ParsingBenchmarks -- \
  --suite section --provider llamasharp --repetitions 5 \
  --llama-model-path "/path/to/grug-9b-Q4_K_M.gguf"
```

`--provider all` requires the OpenAI and TypeSafe keys plus the local GGUF and
runs every lane. Prefer individual provider runs while developing so a missing
credential cannot invalidate unrelated results.

Set the price explicitly from the provider used for that run. The benchmark does
not bake a changeable commercial price into the code.

The report includes:

- ordinary accuracy and macro F1, where abstention counts as wrong;
- coverage and selective accuracy, which show the cost and quality of gating;
- multiclass Brier score for probability-bearing providers;
- repeatability across identical calls;
- mean and p95 latency;
- input and output token totals;
- estimated input cost;
- a case-level error list grouped by provider.

The seed corpus deliberately contains ordinary, ambiguous, hard, and prompt
injection cases. It proves the harness, not Jev's production quality. Before
enabling automatic acceptance, expand it with consented, anonymised examples,
lock a held-out test set, and report confidence intervals. Do not tune thresholds
on the same cases used for the published result.

## Article experiment design

For a defensible article, compare these conditions on the same frozen cases:

| Condition | Purpose |
|---|---|
| Deterministic only | Current local baseline and natural abstention rate |
| Deterministic plus Jev | Whether bounded judgement resolves useful ambiguity |
| Deterministic plus general LLM | Whether Jev improves schema reliability, latency, calibration, or cost |
| Human adjudication | Gold labels and disagreement analysis |

Run providers at least five times to expose instability. Publish the fixture
revision, exact model names, thresholds, raw JSON reports, failure cases, and the
number of cases excluded. A typed response guarantees shape, not correctness.

On the small synthetic v1 fixtures, one-shot OpenAI and local grug runs both
classified all 20 sections and all 15 entity cases correctly. The local runs
averaged roughly 3.8 seconds per section and 3.1 seconds per entity decision;
OpenAI averaged roughly 1.4 seconds per section decision. These are smoke-test
results, not production-quality estimates: the corpora are small, synthetic,
and have not been used as held-out evidence. Raw reports should remain build
artifacts rather than being presented as a model leaderboard.

The next benchmark extension should add full experience-boundary detection. It
needs span-level gold labels and boundary F1, not only section labels. That work
should be completed before Jev is allowed to influence experience segmentation.
