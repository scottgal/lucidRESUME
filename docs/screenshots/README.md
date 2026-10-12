# Desktop documentation screenshots

The synthetic [projection-options.png](projection-options.png) capture was added on
2026-10-12 after exercising Add Job → Project Resume in the native client. It shows
the saved-job selector and length, detail and tone controls. Its fixture contains
no private career information.

The following screenshots were refreshed on 2026-10-12 using the native Avalonia
UI test bridge with a private copy of the imported career. They are real client
captures, without compositing or generated UI imagery.

| Image | View |
|---|---|
| [complete-career-editor.png](complete-career-editor.png) | Complete career Markdown, evidence links, machine JobML and review diagnostics |
| [generated-resume-pdf.png](generated-resume-pdf.png) | Archived Morena PDF, two pages, with page/zoom controls |
| [generated-resume-word.png](generated-resume-word.png) | Actual archived Morena DOCX rendered in the client, four pages |
| [generated-resume-evidence.png](generated-resume-evidence.png) | Selected role citation, generated claim and original saved source excerpt |

Window size was requested at 1600×1000 logical pixels and constrained by the
available display. Document preview width was 560 pixels. The editor contains
pending-review observations as well as accepted claims; warnings are expected and
remain visible. Word and PDF pagination differ. Existing screenshots of other
pages have not been recaptured by this refresh.

To refresh, start the desktop with an isolated data directory and `--ux-mcp`,
navigate using `ui_navigate`, select the saved version, choose the desired tab and
capture using `ui_screenshot`. Inspect each image before replacing it. Use real
saved Word/PDF renders and keep the evidence panel visible. Do not generate a new
resume merely to reopen an existing archive, or publish test interviews to live
career data. See [complete career workflow](../complete-career-workflow.md).
