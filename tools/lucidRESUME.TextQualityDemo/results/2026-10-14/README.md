# Synthetic CLI probe results

These are all results from the first support run, its repeat, and the coverage run
used in the accompanying article. No private career data is included.

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
