# Prose quality loop

This executable runs synthetic career evidence through the application's actual
composition policy and local Ollama writer. It never opens the application store.
Use a fresh output directory for each comparison; its isolated prose cache is
reused only by that run. Raw model exchanges stay in that directory and must not
be enabled for private application data.

```sh
dotnet run --project tools/lucidRESUME.ProseEvaluation -c Release -- \
  /tmp/resume-quality qwen3.5:4b http://127.0.0.1:11434 --quality --focused

dotnet run --project tools/lucidRESUME.ProseEvaluation -c Release -- \
  /tmp/resume-career qwen3.5:4b http://127.0.0.1:11434 --career
```

`--quality` covers internal production agents, offline evaluation, personal and
team delivery, incident logs, directed measurements and cancelled prototypes.
`--focused` selects Direct/Brief and Expressive/Thorough; omit it for all six
tone/detail combinations. Add `--fixture=metric-attribution` to investigate one
family. `--synthesis` runs the earlier complementary-fact fixtures instead.

`results.json` records source, final text, rejected-edit warnings, latency,
unchanged-source status and independently authored distinction checks. A clean
validator result is not evidence of fluent or factually equivalent prose: review
the output against its source, including actor, scope, purpose and outcome.
The small fixture set has already informed fixes and is a regression set, not a
held-out quality benchmark. Add new evidence shapes before claiming broader gains.

`--career` projects one three-role career against contrasting AI leadership and
database jobs using the full compressor, evidence binding and local composition
path. It persists the synthetic career in an isolated SQLite database and records
FTS query/hit counts, exercising TF-IDF and FTS5 rank fusion. It saves readable
resumes and checks source immutability, binding integrity,
role-scoped technologies and differing evidence selection. Its embedder returns
no vectors, deliberately testing lexical fallback rather than semantic matching.

For each iteration, retain the baseline, review factual failures separately from
style preferences, add a targeted regression, fix the shared application path,
then rerun affected cases and native client workflows. Track safe source fallbacks
separately from successful rewrites. These runs do not establish hiring outcomes,
general entailment accuracy or blind human writing preferences.
