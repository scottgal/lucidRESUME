# Desktop UX review: career writing and target jobs

Reviewed all existing destinations with native screenshots, then exercised the new
workflows against an isolated SQLite backup. Personal source data and screenshots
stay in the ignored `artifacts/ux-redesign/` directory.

## The two primary tasks

**My career:** import a CV or LinkedIn export, or start by adding a role. Select a
role, write in an ordinary text editor, and explicitly save the career. The
complete-prose tab edits the summary and role accounts together without showing
JobML. Role identity, dates, named skills and the always-include preference sit in
role details. Evidence prompts show the saved account, the missing detail and a
specific question, either for the career generally or for a selected saved job.
Prompts describe documentation gaps; they do not diagnose lack of experience.

**Jobs:** paste the advert, extract details, correct or supply the role title and
company, optionally attach an advert/LinkedIn URL, then save. The same workspace
owns the saved list, editing, application tracking and resume creation. The
Resume tab contains the result, optional length/style controls and exports.
Saving corrections updates the original job identity rather than making another
record. Extraction is separate from saving and never substitutes for author
confirmation of the visible metadata.

Saved resumes and Applications remain secondary destinations for outputs and
tracking. Diagnostics, discovery/hiring, career analysis, source inspection and
the full evidence editor live under More tools or Service status.

## Review findings and changes

| Previous surface | Problem | Result |
| --- | --- | --- |
| Sidebar | Eleven peer destinations mixed tasks, storage formats and technical machinery | Four destinations with task descriptions; two primary workflows; advanced tools disclosed separately |
| My CV | Imported files, complete career, quality, exports and editing competed | Imports open directly from My career; source inspection remains an advanced destination; import review returns to My career |
| JobML editor | Writing required navigating machine syntax, link counts and acceptance controls | Plain role and complete-prose writing are the default; the full evidence editor remains available for specialist review |
| My Data | Graphs, skill corrections and role identity editing competed with authoring | Primary role metadata edits are beside prose; graphs and detailed ledger inspection are secondary |
| Jobs | Job discovery, employer mode, coverage, preferences and projection occupied one dense screen | Discovery/hiring remain separate under More tools; the primary Jobs destination is intake and saved-job work |
| Add Job | A second saved-job list and separate URL/paste entry flow | Removed from the primary navigation; old Search navigation resolves to the canonical Jobs workspace |
| Project / Apply | Repeated job inputs, transcript interviewing, style configuration and results in two crowded columns | One job context, a dedicated Resume tab, optional style controls and a direct route back to My career |
| Generated resumes | Three fixed columns left little room for a document | List and document layout; evidence is a tab rather than a permanent third pane; useful empty state |
| Pipeline | Specialist tracking information presented as a primary ingestion path | Applications is explicitly secondary; Track application adds a saved-stage record without claiming an application was sent |
| Profile | Service configuration obscured the everyday task | Settings remains a secondary destination; no provider configuration is required to enter prose or save a job |
| Career planner / Help | Useful support but competing with daily work | Career planning is under More tools; Help remains available at the foot of the sidebar |

## Flashing and unnecessary work

- Reuse page controls by view-model identity rather than recreating them on every
  navigation. Re-selecting the current page is a no-op.
- Balance chart property subscriptions on load/unload instead of accumulating
  another set on every visit.
- Ignore caret events from unfocused editors; a hidden text box must not switch
  the visible document tab back to Write.
- Do not reload an unchanged complete-career document into the advanced editor.
- Cache document previews by human-prose revision and template.
- Preserve archive entry objects, selection and the rendered image when archive
  identities have not changed. Clicking the active preview format is a no-op.
- Avoid rebuilding the career role list when the displayed source revision is
  unchanged. Preserve local drafts and their selected role across navigation.

## Integrity and limits

Saving career prose is an explicit author action. Imports remain unchanged;
original source passages, unrelated custom narrative, interview entries, external
links for unchanged claims and extensions owned by other tools are retained.
Saving checks the source snapshot and refuses to overwrite concurrent author edits, while ignoring derived search-index refreshes. Reviewed identity corrections and role anchors are saved as import overrides so they survive subsequent imports.
Complete-prose editing preserves role headings and identity; structural changes
are made in Role details. New roles open those details automatically.

Drafts are retained in memory while navigating, not across application restarts.
The UI labels them unsaved. Save career / Save job is required for durable use.
A source-version conflict retains the draft and reports the conflict; the user
must reconcile the draft with the latest saved career before retrying.

The existing requirement extraction and gap assessment still depend on the
configured parser and reviewed evidence. Their output is not an eligibility or
hiring score. The redesign does not add unsupported technologies or silently
accept model-generated prose.

## Verification

`WorkspaceTests` exercises prose saving and source preservation, complete-prose
editing and malformed headings, source-version conflicts, manual career entry
without imports, extract/correct/save job intake, URL validation, stable job IDs,
draft retention, deduplicated tracking, concrete evidence prompts and page-instance reuse. A source-catalogue regression covers multiple display labels for one source identity; evidence records retain their individual provenance.

`ux-scripts/workspaces-review.yaml` exercises native controls, retained writing,
job extraction and saving, complete-prose switching, 900×600 layouts, archive
re-entry, and the advanced Document/Write tabs. Screenshots must be inspected in
addition to the script result: a successful action does not establish layout
quality or prove every transient frame is flicker-free.

The full-size native save/reopen check also runs against the isolated ledger.
It caught and now covers a catalogue lookup failure when an author edit gave
some passages a different source label without changing their source identity.
The source catalogue now groups by identity, retaining the labels and all
individual evidence records. The corrected native save/reopen scenario passes.

In the final native pass, the settled Document tab screenshots one second apart
are byte-identical, as are the archive previews before and after navigation.
These are checks of the tested settled states, not a claim about every possible
transient frame or operating-system rendering configuration.
