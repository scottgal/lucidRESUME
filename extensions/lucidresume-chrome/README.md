# lucidRESUME Evidence Filler for Chrome

This experimental Manifest V3 extension fills job application forms from a
published full JobML endpoint. It is not an application bot and never submits a
form.

The extension follows the same invariant as lucidRESUME:

```text
published human prose + accepted JobML evidence
                         |
visible empty form fields
                         |
Chrome Prompt API selects evidence IDs and exact substrings
                         |
deterministic validation and projection
                         |
reviewed values or explicit gaps
```

The on-device model cannot introduce a value. Short answers must be an exact
substring of cited ledger evidence. Long answers are assembled only from intact,
fingerprint-valid human prose passages. An exact selector can also anchor the
reviewed text, but a bare prose reference is not enough. Select, radio, and checkbox answers
always require manual review. Missing salary, sponsorship, work-authorisation,
availability, demographic, consent, or motivation evidence is shown as a gap.

## Build and load

Requirements: Node.js 22+ and Chrome 138+ on a desktop platform supported by
Chrome built-in AI.

```bash
cd extensions/lucidresume-chrome
npm ci
npm test
npm run check
npm run build
```

Open `chrome://extensions`, enable **Developer mode**, choose **Load unpacked**,
and select `extensions/lucidresume-chrome/dist`.

Open a job application form, click the extension, enter the complete endpoint
such as `https://example.com/lucidresume/api/jobml`, and choose **Load ledger**.
The endpoint must return Markdown containing exactly one fenced `jobml` block.
HTTPS is required except for `localhost` development.

## Permission and privacy model

- `activeTab` gives temporary access only after the user opens the extension.
- Endpoint host access is requested at runtime for the single supplied host.
- The endpoint URL is saved locally; the fetched ledger remains in side-panel memory.
- Prompt API inference runs locally in Chrome. No cloud API key is used.
- Filled fields are never submitted, and existing non-empty fields are not overwritten.
- Only visible empty fields in the main frame are considered in 0.1.

If Prompt API is unavailable, deterministic contact and identity matching still
works and every unresolved field is reported as a gap.

## Real on-device smoke test

The opt-in smoke test uses WebDriver BiDi to install the unpacked extension into
branded Chrome. This is necessary because Chrome 137 and later ignore the
`--load-extension` command-line switch. It requires a dedicated, non-live Chrome
profile in which the on-device model is already available. The runner refuses
to use the normal macOS Chrome data directory and uses a mock keychain.

```bash
npm run build
python3 -m venv /tmp/lucidresume-e2e
/tmp/lucidresume-e2e/bin/pip install -r test/e2e/requirements.txt
/tmp/lucidresume-e2e/bin/python test/e2e/real_prompt_api.py \
  --profile /path/to/dedicated/model-ready-profile
```

The test ingests JobML, scans a nine-field form, runs the real Prompt API,
reviews the human-prose proposal, fills approved values, preserves unsupported
sponsorship and salary gaps, and verifies that the form was not submitted.

## ATS form-shape matrix

The local browser matrix exercises representative Greenhouse, Lever, and
Workable form structures derived from their public API documentation. It covers
separate and combined names, standard contact tokens, custom text areas,
profile URLs, radio groups, checkbox questions, dropdowns whose placeholder has
a non-empty value, numeric/date controls, hidden values, file uploads, and
already populated fields.

The fixtures are local and have no action URL. The runner installs the unpacked
extension with WebDriver BiDi, scans each page in a real branded Chrome process,
fills two harmless identity values, and asserts that nothing was submitted.

```bash
npm run build
python3 -m venv /tmp/lucidresume-e2e
/tmp/lucidresume-e2e/bin/pip install -r test/e2e/requirements.txt
/tmp/lucidresume-e2e/bin/python test/e2e/form_matrix.py
```

Source form contracts:

- [Greenhouse Job Board API](https://docs.greenhouse.io/job-board.html)
- [Lever Postings API](https://github.com/lever/postings-api)
- [Workable application-form endpoint](https://workable.readme.io/reference/jobsshortcodeapplication_form)

These fixtures test documented field families, not private production markup,
and do not claim universal ATS compatibility.

See the extension's [privacy notice](PRIVACY.md) and the full
[design and research note](../../docs/chrome-evidence-filler.md).

## Current limitations

- Cross-origin embedded application frames are not scanned.
- JobML 0.1 needs human prose evidence selectors or resolvable `ref` values for
  long-answer projection.
- A model selection is semantic assistance, not evidence. Every model-derived
  proposal starts unchecked.
- Chrome may need to download its built-in model before the first analysis.
