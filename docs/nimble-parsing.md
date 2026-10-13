# Nimble decisions for resume import and job requirements

lucidRESUME can use the [Ollama Nimble model](https://ollama.com/library/nimble) for bounded choices after deterministic parsing has found ambiguous resume sections, names, employers, or skill categories. The model selects from candidates supplied by the parser. It cannot add prose, skills, claims, or evidence. Each decision records its source reference and hash, candidates hash, probability distribution, model, and acceptance result. Rendering does not call Nimble.

Install Ollama 0.35 or newer, then pull the model:

```sh
ollama pull nimble
```

In the desktop Profile settings, enable **Use Nimble for resume parsing and job skill classification**, set the Ollama URL (normally `http://localhost:11434`), save, and restart. The setting is off by default. For automation, use `LUCIDRESUME_Nimble__Enabled=true` and, if needed, `LUCIDRESUME_Nimble__BaseUrl=http://127.0.0.1:11435`.

The adapter accepts a configured HTTP or HTTPS server URL (including a LAN Ollama host) and calls Ollama's `/v1/systemone` endpoint. It redacts email addresses, phone numbers, and URLs from the decision state. Extracted name and employer candidates remain in the choice labels because Nimble needs them to decide. The default acceptance gates are a selected probability of at least 0.80 and a 0.20 gap over the runner-up. A failed request or uncertain decision leaves deterministic parsing intact.

Known section headings and skill categories are classified by rules first. Nimble sees only unresolved headings and skills. Name and employer decisions use already extracted spans or `none`. For an ambiguous skill category, the skill must occur in the source Markdown transcript. Nimble sees a bounded excerpt from that paragraph and chooses from fixed category labels or `other`. The audit record points back to the paragraph reference and its fingerprint. The model never adds a new skill.

## Job requirements

When Nimble is enabled, job parsing uses structural extraction, NER and taxonomy candidates followed by bounded decisions, instead of also running full LLM job extraction. Known noise and logistics are removed by cheap rules first; Nimble classifies the remaining phrases as a specific skill, a professional capability, logistics or noise. It cannot propose extra skills. Ordinary fragments such as “Business”, “Systems”, “Support” and “Connect” never become summary themes. Named skills are retained separately from the advert’s intact competency descriptions.

The adapter sends at most 16 questions per HTTP request, with shared source context. Job analysis has a 30-second deadline, uses the configured probability and margin gates, and stores label decisions, probabilities, model, source fingerprint and policy version with the saved job. Repeated generation reuses these decisions. Older jobs are refreshed once; editing the advert invalidates them. Unavailable decision servers produce a visible warning and conservative extraction, with a five-minute retry window rather than repeated inference on every action. Verify the running server version with `GET /api/version`: a newer installed CLI does not upgrade an already-running server. Ollama versions without `/v1/systemone` return 404.

Sentence-based adverts retain complete requirement and responsibility sentences. Role-heading adverts retain their existing competency groups. Neither salary, location nor application instructions should turn into evidence gaps. Relevant author-written product accounts and concise career context can stay in the summary; named tools supported by selected role evidence can stay in Skills even when a broad engineering advert does not name a vendor. Selecting an AI vacancy no longer adds every declared AI skill automatically.

Saved jobs appear before any matching work. Opening the Jobs list no longer rebuilds every job ledger or rescans imported résumés. Matching, career planning and personal data use the complete career transcript, and job-specific analysis runs for the selected advert. Unassessed saved matches are labelled accordingly; they are not shown as 0% fits.

Prose validation permits singular/plural requirements and a single adjacent-letter spelling correction in a long source word. It still rejects new numbers, named technologies, strengthened ownership and lost qualifiers. Previously accepted cached prose is revalidated after this policy change; rejected edits get a fresh attempt. Summary prompts receive only the selected authored sentences, avoiding unrelated career metrics leaking into recent AI work.

Word exports inherit consistent bullet spacing, use paragraph spacing between roles, and stop compact headings from chaining into a mostly empty page. Education uses years; an unresolved subject label is retained in the full transcript rather than presented as an awarded qualification. Current concurrent roles appear before historical roles.

Generated Word and PDF files omit citations and the reference appendix by default. JobML, claim identities and source evidence remain in the archive; citations can still be explicitly enabled.

In the October 2026 desktop check against the MBN Applied AI Engineer advert, Nimble retained six intact requirements and filtered 68 extracted labels without classification warnings. Repeating generation with unchanged evidence and the same Ollama model took 5.15 seconds using stored analysis and prose; the first run with the larger 27B writer took about seven minutes. These are local measurements, not latency guarantees. Both Word and PDF previews contained two pages. Source wording remained where a proposed edit failed grounding checks.

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
