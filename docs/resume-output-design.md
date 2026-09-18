# Resume output and template design

## End-to-end output

The supported role-specific document path is:

1. Import one or more resume documents and LinkedIn data.
2. Import GitHub repositories and their public evidence.
3. Review the merge so uncertain facts do not enter the profile silently.
4. Paste a job advert into Project.
5. Select an output template and project the relevant accepted ledger records.
6. Review the human-owned prose and its JobML evidence links.
7. Export the same artifact as Markdown, DOCX, or PDF.

Ingestion may use deterministic parsers, NER, or an optional LLM to propose ledger
records. Inferred records remain review-required. An optional AI authoring step may
also offer a clearly labelled prose draft or sample. It does not publish prose or
create accepted evidence. A person must review and edit it.

Rendering does not call a model, reparse its own output, rewrite human prose, or
infer provenance. Markdown and JobML are emitted together from one revisioned
projection whose blocks already carry ledger claim IDs. JobML may retain more
detail than the role-specific prose, but every claim must still trace to reviewed
human or external evidence.

This is not an automated application path and it is not detector evasion. The
goal is a clean separation: natural human writing for people, and explicit,
verifiable JobML for ATS and AI systems.

If the source ledger changes after projection, export fails until the role-specific
projection is rebuilt. This prevents a visually plausible document from carrying
stale evidence links.

The portable artifact ends with:

```text
MACHINE AREA
  link to the JobML article
  fenced JobML YAML
```

The DOCX and PDF exports put that machine area on a new page. The visible resume
remains an ordinary single-column document.

## Template research

The templates are original layouts based on the following findings, not copies of
third-party designs:

- Arnulf, Tegner and Larssen found formal layouts were preferred to creative layouts
  for otherwise equivalent candidates: https://doi.org/10.1080/13594320902903613
- Wingate, Robie, Powell and Bourdage found clearer, more detailed, better-structured
  applications were associated with better job-search outcomes in their sample:
  https://doi.org/10.1111/ijsa.70022
- MIT Career Advising recommends concise, easy-to-scan, reverse-chronological resumes
  tailored to the role: https://capd.mit.edu/resources/resumes/

Those sources do not establish one perfect template. They support the conservative
choices shared by all three templates:

- one reading order and no sidebars;
- conventional section headings;
- restrained typography and colour;
- text contacts instead of icons;
- selectable density without changing content semantics;
- the same content model for PDF and Word.

## Included templates

| Template | Intended use |
|---|---|
| ATS Classic | Conservative default with generous spacing and minimal colour. |
| Modern Professional | Restrained blue accent and balanced density. |
| Compact Technical | Smaller margins and type for evidence-heavy technical careers. |

The templates are presentation choices. They never change claims, evidence, JobML,
or requirement coverage.

## OpenAI configuration

Set the provider and API key through Profile, or with environment variables:

```bash
export LUCIDRESUME_Tailoring__Provider=openai
export LUCIDRESUME_OpenAi__ApiKey=sk-...
export LUCIDRESUME_OpenAi__Model=gpt-5.6-luna
```

OpenAI is optional for ingestion-time extraction and authoring drafts. AI output
is displayed as a suggestion. It cannot publish prose, create accepted claims, or
run during projection and export. The implementation follows the OpenAI Responses API
structured-output guidance:
https://developers.openai.com/api/docs/guides/structured-outputs

API keys are never written into an exported resume or JobML artifact.
