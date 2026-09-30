# Local Nimble decisions during import

lucidRESUME can use the [Ollama Nimble model](https://ollama.com/library/nimble) for bounded choices after deterministic parsing has found ambiguous resume sections, names, employers, or skill categories. The model selects from candidates supplied by the parser. It cannot add prose, skills, claims, or evidence. Each decision records its source reference and hash, candidates hash, probability distribution, model, and acceptance result. Rendering does not call Nimble.

Install Ollama 0.35 or newer, then pull the model:

```sh
ollama pull nimble
```

In the desktop Profile settings, enable **Use local Nimble for ambiguous ingestion decisions**, set the Ollama URL (normally `http://localhost:11434`), save, and restart. The setting is off by default. For automation, use `LUCIDRESUME_Nimble__Enabled=true` and, if needed, `LUCIDRESUME_Nimble__BaseUrl=http://127.0.0.1:11435`.

The adapter accepts only a local HTTP loopback URL and calls Ollama's `/v1/systemone` endpoint. It redacts email addresses, phone numbers, and URLs from the decision state. Extracted name and employer candidates remain in the choice labels because Nimble needs them to decide. The default acceptance gates are a selected probability of at least 0.80 and a 0.20 gap over the runner-up. A failed request or uncertain decision leaves deterministic parsing intact.

Known section headings and skill categories are classified by rules first. Nimble sees only unresolved headings and skills. Name and employer decisions use already extracted spans or `none`. For an ambiguous skill category, the skill must occur in the source Markdown transcript. Nimble sees a bounded excerpt from that paragraph and chooses from fixed category labels or `other`. The audit record points back to the paragraph reference and its fingerprint. The model never adds a new skill.

The earlier [Jev experiment](jev-parsing-experiment.md) is retained as historical benchmark documentation. Hosted Jev is no longer registered in the import pipeline.

To compare local Nimble with deterministic parsing on the synthetic section and
entity fixtures, run:

```sh
dotnet run --project tools/lucidRESUME.ParsingBenchmarks -- \
  --suite section --provider nimble \
  --nimble-base-url http://127.0.0.1:11435 \
  --output /tmp/lucidresume-nimble-benchmarks
```

The benchmark needs no hosted API key. On a standard Ollama setup, omit
`--nimble-base-url` to use port 11434.
