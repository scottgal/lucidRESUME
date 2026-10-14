# Synthetic CLI probe results

These reports cover the original support/coverage runs and the larger-model rerun
used in the accompanying article. No private career data is included.

## Original runs

* NLI: Python 3.9.6, PyTorch 2.8.0, Transformers 4.57.6, CPU on Apple M5.
  The resolved Hugging Face revision is stored in each report. A fresh process loads
  the model for each run; `loadMs` includes initial download on the first run.
* Nimble: `nimble:latest` returned by a LAN Ollama 0.35.0 server; server hardware was
  not recorded. Each support request asks two questions, coverage requests ask one.
  The mutable model tag limits reproducibility; do not infer throughput superiority.
* General LLM: local Ollama 0.40.3, Qwen3.5 4B, structured output, temperature zero,
  thinking disabled. This baseline generates relation labels only.

Support expected labels match 8/12 for NLI and 9/12 for Nimble in both runs;
Nimble's response actions match 10/12. Coverage relation labels match 5/6, 5/6 and
3/6 for NLI, Nimble and the general LLM respectively. These are small hand-authored
fixtures, not a benchmark sample or threshold calibration set. Unknown evidence
must not be treated as disproving an assertion; several models make that mistake.

Use `compare.py` to inspect every mismatch. The named expected labels live in the
fixture files and are independent of the models. Changing them changes this probe.

## Larger LLM rerun

`llm-qwen27b-*` and `llm-gemma31b-*` rerun both fixture sets with Qwen3.8 27.3B and
Gemma4 31.3B on the same LAN Ollama 0.35.0 server. Both use Q4_K_M weights. The
reports record digests, model details, token counts, load time and generation
settings. Prompts, fixtures and classification settings match the original 4B
baseline: temperature zero, thinking disabled, 128 output tokens and 4096 context.

Qwen matches 10/12 support labels; Gemma matches 9/12. Neither produces a false
entailment. Both match 5/6 exact coverage labels and 6/6 supported-versus-unsupported
coverage findings. Each calls the polished non-answer contradiction rather than
neutral, which does not change the finding that its coverage hypothesis is unsupported.
The unknown-status fixture tests explicit acknowledgement separately from supplying
the status. Corpus labels are unchanged.

Median request times are about 1.66s/1.80s for Qwen support/coverage and 2.39s/2.47s
for Gemma. The first support calls take about 16s and 31s respectively; their load
times are retained. These runs do not compare reasoning-enabled configurations or
evaluate communication quality.
