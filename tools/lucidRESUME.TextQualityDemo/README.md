# Structured response quality CLI

A small synthetic probe comparing three-way NLI with a bounded decision model.
It does not change the application, execute agent actions or use a personal career store.
Fixtures cover tool status, ownership, invented impact, prototype status, metrics,
publication evidence and retained qualifications. Expected labels are human-written;
they are not predictions from another model.

## Nimble from C#

Requires .NET 10, Ollama 0.35+ and `ollama pull nimble` on the selected server.
This reuses lucidRESUME's typed, validated decision adapter. Each request asks two
questions over the same state: textual relation and next response action.
The actions are suggestions; the CLI never publishes or calls tools.

```sh
dotnet run --project tools/lucidRESUME.TextQualityDemo -c Release -- \
  --url http://localhost:11434 \
  --output /tmp/nimble-quality.json
```

Use `--url` for a LAN server, `--model` to change its decision model, or
`--fixtures` to supply another corpus in the same JSON format. An unavailable or
invalid server response fails the run rather than pretending to be a decision.
Console output shows relation, action and per-request elapsed time. JSON preserves
probability distributions and returned model name.

## Three-way NLI

Requires Python 3.10+ and a suitable PyTorch installation. First use downloads the
model/tokenizer from Hugging Face. The default model is the three-way
`MoritzLaurer/DeBERTa-v3-base-mnli-fever-anli`. Binary zero-shot models are rejected
because they cannot distinguish neutral from contradiction.

```sh
python3 -m venv /tmp/text-quality-venv
/tmp/text-quality-venv/bin/pip install -r tools/lucidRESUME.TextQualityDemo/requirements.txt
/tmp/text-quality-venv/bin/python tools/lucidRESUME.TextQualityDemo/nli.py \
  --output /tmp/nli-quality.json
python3 tools/lucidRESUME.TextQualityDemo/compare.py \
  /tmp/nli-quality.json /tmp/nimble-quality.json
```

`--revision` pins a Hugging Face commit; JSON records the resolved revision. NLI
runs on CPU and passes the attention mask along with token IDs. Oversized passages
fail rather than silently dropping qualifications. This intentionally simple probe
uses one pair per forward pass; it is not an optimized inference service.

## Interpret the comparison

Both models classify the same premise/hypothesis pairs with the same three labels.
Nimble additionally sees the user's question and chooses an application action.
That extra context and second decision mean this is not a pure model throughput
comparison. NLI records model-load time separately; Nimble's first HTTP call can
include a cold load. Run twice to examine warm latency. Keep hardware, server load,
model revisions and prompt/fixture changes alongside results.

The comparison reports relation agreement, false entailment and action agreement.
Review errors individually. Twelve examples cannot establish which model is better
in production or calibrate an acceptance threshold. No model confidence here is a
probability that a response is factually correct. Add independent paraphrases,
adversarial examples and held-out real conversations before adopting a policy.

The research rationale is in
[Evidence-preserving prose](../../docs/research/2026-10-12-evidence-preserving-prose.md).
NLI is an experiment here, not a new application validation dependency.

## Answer presence is a different question

`coverage-fixtures.json` treats the premise as an output to inspect. A response that
says a refund is complete contains a status answer even if an external tool says
pending. A polite response that never gives a status does not. Explicit inability
to determine a status is tracked separately from giving a status. Multi-part
questions have separate coverage hypotheses for contribution and measured result.
Neither support nor coverage establishes that the answer is good.

```sh
dotnet run --project tools/lucidRESUME.TextQualityDemo -c Release -- \
  --task coverage --url http://localhost:11434 --output /tmp/nimble-coverage.json
/tmp/text-quality-venv/bin/python tools/lucidRESUME.TextQualityDemo/nli.py \
  --task coverage \
  --output /tmp/nli-coverage.json
```

## Optional general LLM baseline

The standard-library Python CLI calls Ollama `/api/chat` with a three-label JSON
schema. Choose an installed model supporting structured output. It generates a
classification and does not invent confidence probabilities. This makes its
latency and errors inspectable alongside the specialist classifiers. It does not
judge style, usefulness or overall response quality.

```sh
python3 tools/lucidRESUME.TextQualityDemo/llm.py --model qwen3.5:4b \
  --task coverage --output /tmp/llm-coverage.json
python3 tools/lucidRESUME.TextQualityDemo/compare.py \
  /tmp/nli-coverage.json /tmp/nimble-coverage.json /tmp/llm-coverage.json
```

General LLMs can also propose question decomposition or claim extraction upstream,
then help revise wording downstream. Review those proposed obligations and preserve
source spans. Never let the writer define the expected answer for its own check.

## C# article samples

`ArticleSamples.cs` contains small examples using only the .NET standard libraries.
They demonstrate exact structured fact checks, template rendering, embedding
similarity, reading the NLI worker's JSON, and direct HTTP calls for a decision model
and a general LLM. These return diagnostics, not an overall answer-quality score.

```sh
dotnet run --project tools/lucidRESUME.TextQualityDemo -c Release -- --sample code
dotnet run --project tools/lucidRESUME.TextQualityDemo -c Release -- \
  --sample nli --report /tmp/nli-quality.json
dotnet run --project tools/lucidRESUME.TextQualityDemo -c Release -- --sample decision
dotnet run --project tools/lucidRESUME.TextQualityDemo -c Release -- --sample llm
ollama pull embeddinggemma
dotnet run --project tools/lucidRESUME.TextQualityDemo -c Release -- --sample embedding
```

Use `--url` for another Ollama server and `--model` for an installed compatible model.
The NLI example consumes the support report produced by the Python worker earlier.
It does not imply that Ollama runs the DeBERTa NLI model. The embedding example
compares pending with complete: their common topic can produce similarity without
agreement about the status. All snippets intentionally use synthetic data.
