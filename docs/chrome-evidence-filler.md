# Chrome evidence filler

## Decision

The browser extension is a reviewable JobML projection tool, not an application
agent. It reads a user-supplied full JobML endpoint and the visible empty fields
on the current application page. Chrome's on-device Prompt API may match those
fields to evidence. Deterministic code decides whether the proposed mapping is
allowed and constructs the value that can be inserted.

```text
full JobML endpoint
        |
parse and verify accepted claims, refs, selectors and FNV drift hashes
        |
evidence-backed fact catalogue          visible empty fields
        |                                      |
        +------ Chrome Prompt API mapping -----+
                           |
             evidence IDs and exact substrings
                           |
        deterministic validation and projection
                           |
                  proposal, review, or gap
```

The model does not receive authority to create facts or submit forms.

## Research findings

Chrome's Prompt API is available to extensions from Chrome 138 and uses the
browser's local Gemini Nano model. The model is downloaded separately and is not
available on every device. The API exposes `LanguageModel.availability()` and a
download progress monitor. Structured JSON output uses `responseConstraint`,
which is important here because natural-language requests for JSON are not a
validation boundary.

The Prompt API is not available in web workers. The mapping therefore runs in
the side-panel extension page. The service worker only configures the action
button to open the panel.

Chrome's Side Panel API is intended for a persistent companion interface beside
the current page. `activeTab` gives temporary page access after the user invokes
the extension. The JobML endpoint uses optional host permission requested at
runtime, so installing the extension does not grant access to every site.

Primary references:

- [Chrome Prompt API](https://developer.chrome.com/docs/ai/prompt-api)
- [Prompt API structured output](https://developer.chrome.com/docs/ai/structured-output-for-prompt-api)
- [Built-in AI design guidance](https://developer.chrome.com/docs/ai/built-in-ai-dos-donts)
- [Chrome Side Panel API](https://developer.chrome.com/docs/extensions/reference/api/sidePanel)
- [`activeTab` permission](https://developer.chrome.com/docs/extensions/develop/concepts/activeTab)
- [Runtime and optional permissions](https://developer.chrome.com/docs/extensions/develop/concepts/declare-permissions)
- [Cross-origin requests from extensions](https://developer.chrome.com/docs/extensions/develop/concepts/network-requests)
- [Selenium WebExtension BiDi API](https://www.selenium.dev/selenium/docs/api/py/selenium_webdriver_common_bidi/selenium.webdriver.common.bidi.webextension.html)
- [Chrome Web Store user-data requirements](https://developer.chrome.com/docs/webstore/program-policies/user-data-faq)
- [HTML autocomplete field tokens](https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#autofill)

## Projection rules

1. Standard name, email, telephone, LinkedIn, GitHub and website fields use
   deterministic matching before a model is considered.
2. The model returns only known field IDs, known fact IDs, exact substrings and
   current select-option values under a JSON Schema constraint.
3. A short answer is accepted only when it is an exact contiguous substring of
   one of its cited facts.
4. A long answer is assembled only from complete, fingerprint-valid human prose
   passages. Claim summaries and concept names cannot become human prose.
   An exact text selector can provide the same drift anchor; a bare prose ref
   cannot enter the verified catalogue.
5. Select, radio and checkbox answers always start unchecked for human review.
6. Every model-derived proposal starts unchecked for human review.
7. Existing field values are skipped both during scanning and immediately before
   filling, which prevents overwriting text entered while analysis was running.
8. Salary, sponsorship, work authorisation, availability, demographics, consent
   and motivation become gaps unless the ledger explicitly supports an answer.
9. The extension dispatches normal input and change events but has no form-submit
   operation.

## Threat model

Page labels, option text, JobML prose and external evidence metadata are
untrusted. The prompt tells the model not to follow embedded instructions, but
the real boundary is deterministic validation after the response. Unknown field
IDs, unknown fact IDs, invented substrings and stale evidence are rejected. If a
claim has no current prose or external evidence after those checks, the claim and
its concepts are excluded as well.

The extension does not fetch external evidence URIs from JobML. It accepts HTTPS
ledger endpoints, with HTTP permitted only for `localhost` and `127.0.0.1` during
development. Credentials in endpoint URLs are rejected. The response stream and
parser both enforce a 4 MB limit. The fetched ledger is held in side-panel
memory. Only the endpoint URL is persisted locally.

Chrome Web Store policy treats form contents and locally processed personal data
as user data. Before store publication the project needs a public privacy policy
and the listing must accurately disclose this local processing. The in-product
load screen already presents the processing disclosure before the user loads a
ledger.

## Verification

The TypeScript tests cover parsing, endpoint restrictions, JobML FNV-1a parity,
bounded response streaming, stale selector and fingerprint rejection, removal of
claims whose evidence has drifted, deterministic contact mapping, structured
Prompt API invocation, exact-substring enforcement, human-prose-only long
answers and review defaults.

A real branded-Chrome 153 run installs the unpacked extension through WebDriver
BiDi, loads a local full JobML endpoint, extracts ten evidence-backed facts,
scans a nine-field application and invokes the installed on-device model. It
produces six evidence-backed proposals and three honest gaps. The reviewed human
leadership passage and five deterministic identity/contact values are inserted;
employer, sponsorship and salary remain empty, and the form remains unsubmitted.

The real run caught two issues that controlled tests did not. Chrome's structured
output implementation rejected the `uniqueItems` JSON Schema keyword, so the
schema now uses supported item constraints and requires one mapping per field.
The model also attempted to answer work authorisation from unrelated leadership
evidence. A deterministic sensitive-field gate now rejects that mapping unless a
cited ledger fact explicitly supports the category. Consent and declarations
always require a direct user decision.

Chrome 137 and later ignore `--load-extension` in branded release builds. The
opt-in runner at `extensions/lucidresume-chrome/test/e2e/real_prompt_api.py`
therefore uses WebDriver BiDi's extension-install command. It refuses the live
macOS Chrome profile and enables Chrome's mock keychain for its dedicated test
profile.

A second real-browser matrix uses local fixtures shaped from the documented
Greenhouse, Lever, and Workable form contracts. Across 25 visible empty fields,
it verifies accessible label recovery, a single record per radio group, distinct
radio option labels, non-empty dropdown placeholders, combined and split names,
contact and profile fields, textarea, numeric/date and checkbox controls. File,
hidden, and already populated inputs are excluded. Six harmless values are
inserted across the three forms and all submission counters remain zero.

That matrix found two scanner defects. Radio fields originally inherited the
first option label (`Yes`) rather than their fieldset question. Selects with a
first placeholder such as `<option value="choose">Choose...</option>` were
mistaken for completed fields. Both cases now have explicit browser-level
regressions in `test/e2e/form_matrix.py`.
