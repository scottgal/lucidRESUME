# ***lucid*RESUME** User Manual

Welcome to ***lucid*RESUME** - a local-first editor that keeps human resume prose
and an evidence-linked machine representation together. You own the prose. JobML
gives ATS and AI systems a more explicit view without forcing the human document
to read like machine input.

Core processing and storage run on your machine. No account is required. Data only
leaves the device when you explicitly select a cloud AI provider or import from an
external service such as GitHub.

---

<!-- help:getting-started -->
## Getting Started

### First Launch

When you first open ***lucid*RESUME**, the app prepares its extraction models. The
optional grug 9B local language model is a separate download of about 5.6 GB. You'll
see progress in the sidebar status panel:

- **Embeddings** - the semantic matching engine (all-MiniLM-L6-v2)
- **NER** - named entity recognition for extracting skills, names, and organisations (2 models)
- **AI** - optional grug 9B, Ollama, Anthropic, or OpenAI assistance during ingestion

All three status indicators should turn green. If NER or Embeddings show red, the app will still work but with reduced extraction accuracy.

### Quick Workflow

1. **Import** your resume (PDF or DOCX) on the **My CV** page
2. **Add a job** you're interested in via the **Add Job** page
3. **View the match** on the **Jobs** page - see per-skill scoring
4. **Project** your resume on the **Project** page
5. **Track** your application in the **Pipeline**

### Installing From A Release Archive

GitHub releases provide self-contained archives for Windows, macOS, and Linux on x64 and ARM64. Download the archive matching your platform, extract it, and run the `lucidRESUME` executable inside.

Each app archive has a matching `.sha256` checksum file. The GitHub release page includes quick usage and configuration guidance. Releases also include a documentation archive with `lucidRESUME-docs-single-page.md` for offline reference.

On macOS, the app bundle is ad-hoc signed but not Apple-notarized. If macOS blocks first launch, do not disable Gatekeeper globally. After extracting the archive, Control-click `lucidRESUME.app` and choose **Open**. If that still fails, run this only against the extracted app:

```bash
xattr -dr com.apple.quarantine ./lucidRESUME.app
```

---

<!-- help:importing-your-resume -->
## Importing Your Resume

### Supported Formats

- **PDF** - including two-column layouts, LaTeX-generated, and scanned documents (with Docling)
- **DOCX** - Microsoft Word and compatible editors. Preview powered by [Morph](https://github.com/SimonCropp/Morph) — no LibreOffice needed.
- **TXT** - plain text resumes
- **LinkedIn ZIP** - drop your LinkedIn data export archive and it auto-detects and imports positions, skills, education, projects, and contact info
- **GitHub** - import skills from your GitHub repos via the Profile page

### How to Import

**Drag and drop** any file onto the app window to import. Or:

1. Go to the **My CV** page
2. **Choose your import mode** from the dropdown next to the Import button:
   - **Fast** - structural parsing + NER only. Sub-second. No external services needed. Best for well-formatted DOCX resumes and clean PDFs.
   - **AI** - adds LLM fallback for missing fields. Takes 3-10 seconds. Requires Ollama or a cloud API key. Best for messy PDFs, non-standard formats, or resumes where Fast mode missed experience/skills.
3. Click **Import Resume** and select your file

### Unified Candidate Document

All imports merge into a **single candidate document**. Whether you import a DOCX resume, a LinkedIn archive, or GitHub repos, the data is merged using semantic matching (embedding cosine similarity):

- **Experience**: matched by company name + date overlap
- **Skills**: matched by name (handles aliases like "K8s" ≈ "Kubernetes")
- **Education**: matched by institution
- **Projects**: matched by name

Each element tracks which imports contributed to it (e.g. "LinkedIn + Scott_Galloway_CTO.docx").

### Import Review

When importing into an existing document, you'll see a **review page** showing what would change. You can accept or reject individual items before they enter your data.

### Document Layout Detection

The app uses a DocLayNet YOLO model to understand your resume's visual structure — detecting titles, section headers, tables, columns, and list items. This produces a structural hash that correctly identifies different template layouts even when they use identical fonts.

The extraction pipeline runs automatically using **5-layer RRF fusion** — all signals run in parallel and vote on the result:

1. **Structural** - text extraction, section classification, pattern matching
2. **NER** - two BERT models find skills, job titles, companies, dates, locations
3. **Skill taxonomy** - 19,983 known skills (from 1.3M real job postings) matched against your text. Cross-industry — not just tech.
4. **LLM backstop** - when other signals are weak, the LLM extracts what they missed. Never fails.
5. **Entity lookup** - 11K known companies + 7K locations validate and boost NER candidates

Multi-source agreement boosts confidence. The same 5-layer pattern is used for resume parsing, JD extraction, and person name resolution.

<!-- help:resume-multiple -->
### Multiple Resume Variants

You can import multiple versions of your resume. Each import adds to your **compound skill ledger** - skills and evidence accumulate across variants:

- A general resume might list broad skills
- A targeted variant adds specific technologies
- An older resume captures historical experience

The ledger merges everything with deduplication. More variants = richer skill profile.

<!-- help:resume-quality -->
### Quality Analysis

After import, the quality panel shows findings grouped by category:

- **Bullet Quality** - are your achievement bullets strong? Do they start with action verbs? Do they include metrics?
- **Completeness** - is your name, email, phone, summary present?
- **Format** - is the document length appropriate for your experience level?
- **Presentation** - reverse-chronological order, location info, graduation dates
- **JD Alignment** - (when viewing against a job) which required skills are missing?

> **Tip:** Bullet findings are the most actionable. Replace weak verbs like "helped" or "worked" with strong verbs like "implemented", "reduced", or "architected".

---

<!-- help:understanding-the-skill-ledger -->
## Understanding the Skill Ledger

The skill ledger is the core of ***lucid*RESUME**. It's not a keyword list - it's a structured, evidence-backed model of your capabilities.

### What's in an Entry

Each skill in the ledger tracks:

| Field | What it means |
|-------|--------------|
| **Skill Name** | The normalised skill term (e.g., "Kubernetes", "Python") |
| **Category** | Technical, Soft, Domain, Tool, etc. |
| **Evidence** | List of jobs + bullets where this skill appears |
| **Calculated Years** | Sum of non-overlapping date ranges |
| **Strength Score** | Weighted combination: years (30%) + roles (25%) + recency (25%) + confidence (20%) |

### Skill Classifications

- **Strong Skills** - multiple evidence points, recent usage, high strength
- **Weak Skills** - mentioned but sparse evidence (e.g., listed in Skills section but never in a bullet)
- **Unsubstantiated** - claimed but zero supporting evidence

<!-- help:skill-consistency -->
### Consistency Checks

The ledger flags issues:

- **Years mismatch** - you claim "10 years Python" but evidence shows 3 years of roles using it
- **Unsubstantiated** - skill listed but never demonstrated in any achievement
- **Stale** - last evidence is 5+ years old

These aren't errors - they're signals that your resume could be stronger. A presentation gap (the evidence exists but isn't highlighted) is different from a true gap (you don't have the skill).

---

<!-- help:jobml-editor -->
## JobML Evidence Editor

The **JobML Editor** keeps the human resume and its machine-readable evidence map
in one reversible Markdown file.

The editor has three linked areas:

1. **Human** on the left contains three views of the same revision. **Document**
   is the live Word-layout projection, **Write** edits the authoritative Markdown,
   and **Markdown** provides a fast structural preview.
2. **Live Links** in the centre shows every connection from a prose passage or
   external source to a JobML claim.
3. **Machine** on the right contains editable JobML YAML.

Select a link card to highlight both the supporting prose and its JobML reference.
Moving the caret through linked prose or a JobML reference selects the other side.

After an imported resume is opened in JobML, **Document** is selected by default.
The template selector changes typography, spacing, and pagination. Editing prose
waits briefly, exports the current Markdown to DOCX, and renders its pages with
Morph. This preview follows the current editor text; it is not a frozen image of
the originally imported file. Template rendering never reruns extraction or asks
an LLM to reinterpret the resume.

### Extracting and reviewing links

Use **Generate links** to deterministically extract draft claim links from the current prose. Draft
claims are marked `derived` and `review: required`. They do not count as accepted
evidence until you review them and choose **Accept claims**.

The evidence graph updates while you type:

| State | Meaning | What to do |
|---|---|---|
| `Valid` | The referenced text still matches | Nothing |
| `Changed` | The passage moved or its meaning may have changed | Inspect it, then use **Accept drift** only if it still supports the claim |
| `Missing` | Supporting prose can no longer be found | Restore evidence or remove/change the claim |
| `Ambiguous` | More than one passage may match | Select the correct evidence manually |

The fast `fnv1a64` fingerprint detects ordinary editing drift. It is not a
cryptographic signature. Accepted prose claims without a fingerprint cannot be
published.

### Human prose and machine detail

Markdown is human-owned and authoritative, but it does not have to contain every
detail. Write it for a human reader. A resume for one role may summarise or omit
less relevant material. JobML can retain a higher-resolution evidence ledger and
can link to repositories, articles, qualifications, and other external sources.

JobML does not give the AI permission to strengthen the prose. A repository link,
skill alias, or machine inference remains a suggestion until its claim and
attribution are reviewed.

An AI provider may offer a clearly labelled draft or sample during an explicit
authoring step. That output is not accepted prose and does not enter the ledger
as fact. You must review, edit, and accept it. Projection and export never ask a
model to make the text look human or to invent missing coverage.

The saved source artifact contains normal Markdown followed by one fenced `jobml`
block. Published Markdown, Word, and PDF
use inline numbered citations and a compact cJobML **References** section instead.
The compact section can link to a published full JobML endpoint and remains
readable without JobML-aware software.

For the full format, see the [JobML 0.1 specification](../../../docs/jobml-0.1-specification.md)
and [cJobML 0.1 publication specification](../../../docs/cjobml-0.1-specification.md)
and [GitHub repository extension](../../../docs/jobml-github-extension-0.1.md).

---

<!-- help:my-data -->
## My Data Dashboard

The **My Data** page shows everything extracted about you in one place:

### Skills Dashboard
- **Category pie chart** - visual breakdown of your skill categories (Languages, Frameworks, Cloud, etc.)
- **Career Timeline** - Gantt chart showing your roles positioned by date
- **Skill Communities** - UMAP projection of your skills colored by Leiden community clusters

### Skill Ledger
- Filter by category or search by name
- Click any skill to expand and see every piece of evidence (which job, which bullet, what date range, what confidence)
- Dismiss skills that were incorrectly detected
- Add skills manually
- Tier badges: Strong (green), Moderate (yellow), Weak (red)

### Corrections
- **Dismiss** skills or individual evidence entries — stored separately so re-imports don't lose your corrections
- **Add manual skills** — creates evidence with Manual source
- Personal info fields are editable — changes stored as overrides

---

<!-- help:career-planner -->
## Career Planner

The **Career** page analyses the gap between your skills and a target job:

1. Select a job from the dropdown
2. The planner classifies each gap:
   - **Quick Wins** (low effort) — presentation gaps, just reword your resume
   - **Side Projects** (medium effort) — adjacent skills in the same Leiden community
   - **New Learning** (high effort) — true gaps requiring dedicated study
3. Each recommendation shows the skill, gap type, and specific actionable advice

---

<!-- help:hiring-mode -->
## Hiring Mode

The Jobs page has a **Looking / Hiring** toggle:

- **Looking** - the candidate view you're used to (browsing jobs, matching)
- **Hiring** - for posting roles you're hiring for

In Hiring mode:
- **Salary and location are mandatory** — no "competitive salary"
- Enter title, company, location, remote/hybrid, salary range, and description
- The JD parser auto-extracts skills from the description

---

<!-- help:browsing--managing-jobs -->
## Browsing & Managing Jobs

### The Jobs Page

The Jobs page shows all saved job descriptions with match scores. Each job card displays:

- **Company** and **title**
- **Match score** - overall fit percentage
- **Required coverage** - what percentage of required skills you match
- **Preferred coverage** - what percentage of nice-to-haves you match
- **Salary** (if detected)
- **Location** and **remote/hybrid** status

### Match Scoring

Matching uses a 3-layer approach:

1. **Substring match** - exact or partial text match (fast, high precision)
2. **Embedding similarity** - semantic matching via cosine similarity (catches "NoSQL" matching "MongoDB")
3. **Achievement-text search** - keyword search in your actual bullet points (catches "payment systems" when you have Stripe experience)

<!-- help:jobs-career-plan -->
### Career Plan Panel

When you select a job, the career plan shows:

- **Presentation Gaps** - you have the skill but need to highlight it better
- **Weak Evidence** - you have related experience but it's thin
- **Adjacent Skills** - skills near yours in the skill graph (short bridge)
- **True Gaps** - skills you genuinely don't have yet

Each gap includes an **effort level** (Low/Medium/High) and recommended action.

---

<!-- help:adding-job-descriptions -->
## Adding Job Descriptions

### From URL

1. Go to **Add Job**
2. Paste a job listing URL
3. Click **Fetch** - the app scrapes and parses the listing

### From Text

1. Go to **Add Job**
2. Paste the full job description text into the text box
3. Click **Add from Text**

### What Gets Extracted

The JD parser extracts:

- **Required skills** - tagged as must-have
- **Preferred skills** - tagged as nice-to-have
- **Salary range** (if mentioned)
- **Remote/hybrid/onsite** status
- **Years of experience** requirements
- **Company name** and **job title**

Extraction uses the same RRF fusion as resume parsing - structural patterns, NER models, and (optionally) LLM all vote, and results are fused.

<!-- help:search-job-boards -->
### Searching Job Boards

***lucid*RESUME** searches 7 job boards in parallel:

| Source | Needs API Key? | Coverage |
|--------|---------------|----------|
| Adzuna | Yes | UK, US, DE, FR, AU, etc. |
| Reed | Yes | UK |
| Findwork | Yes | Global (tech) |
| Arbeitnow | No | EU focus |
| JoinRise | No | Global |
| Jobicy | No (RSS) | Remote |
| Remotive | No (RSS) | Remote |

Configure API keys on the **Profile** page. Jobs without keys use the free sources.

### Search Watches

Set up a **Search Watch** to poll automatically:

- Name your search
- Set query terms
- Set hard filters (salary, remote, contract type, excluded companies)
- Choose polling interval

New jobs appear as notifications in the sidebar.

---

<!-- help:matching--gap-analysis -->
## Matching & Gap Analysis

### How Match Scores Work

The overall fit score combines:

- **Required skill coverage** - heavily weighted
- **Preferred skill coverage** - moderate weight
- **Evidence strength** - are your matches backed by strong evidence?
- **Years alignment** - do your calculated years meet the JD requirements?

### The Skill Graph

Your skills form a graph where edges connect skills that appear together in the same role or JD. **Louvain community detection** finds clusters:

- Your strongest cluster is your core competency
- Adjacent clusters show growth paths
- JD skills that fall in your clusters are natural fits

### Search Query Suggestions

The career planner generates job search queries from your skill communities:

- **Strong Fit** - queries targeting your core cluster
- **Growth Target** - queries that stretch into adjacent clusters
- **Stretch Goal** - further afield but reachable
- **Bridge Role** - roles that connect two of your clusters

---

<!-- help:tailoring-your-resume -->
## Projecting Your Resume

### How Projection Works

1. Select a job on the **Jobs** page
2. Navigate to **Project**
3. The system queries your skill ledger for evidence matching the JD
4. **Semantic compression** selects only relevant roles and bullets
5. Markdown and JobML are rendered together from those selected ledger claim IDs

> **Important:** Project is a projection, not a generation step. Evidence extraction
> happens when sources are ingested or explicitly reviewed. Rendering never asks a
> model to infer claims or decide what supports them.

<!-- help:authoring-drafts -->
If you ask an AI provider for a draft or sample, the result remains an authoring
suggestion. Review and edit it as human prose, then reconcile its evidence before
accepting it. Project never rewrites prose while it renders a role-specific
document.

---

<!-- help:application-pipeline -->
## Application Pipeline

### Tracking Applications

The Pipeline page tracks your job hunt end-to-end:

**Stages:**
- **Saved** - bookmarked, not yet applied
- **Applied** - application submitted
- **Screening** - initial phone/email screen
- **Interview** - formal interview process
- **Offer** - offer received
- **Accepted** / **Rejected** / **Withdrawn** / **Ghosted** - terminal states

### Timeline

Each application has a chronological timeline of events:

- Stage changes (with timestamps)
- Emails received (if email integration is configured)
- Notes you add manually
- Interview scheduling

### Funnel Visualization

The horizontal funnel at the top shows your pipeline flow:

- How many applications at each stage
- Response rate, interview rate, offer rate
- Visual proportions by stage

<!-- help:pipeline-stale -->
### Stale Detection

- **14+ days** with no activity - yellow warning indicator
- **30+ days** in Applied with no response - auto-suggested as Ghosted

---

<!-- help:email-integration -->
## Email Integration

### Setup

1. Go to **Profile** page
2. Enter your IMAP settings:
   - **Host** (e.g., `imap.gmail.com`)
   - **Port** (usually `993` for SSL)
   - **Username** (your email address)
   - **Password** (app-specific password - see below)
3. Click **Test Connection**

> **Gmail users:** You need an App Password. Go to Google Account > Security > 2-Step Verification > App Passwords. Generate one for "Mail".

> **Outlook users:** Use your regular password with `outlook.office365.com` port 993.

### How Scanning Works

The email scanner:

1. Connects to your inbox via IMAP (read-only)
2. Scans recent emails for job-related content
3. **Classifies** emails: confirmation, interview invite, rejection, offer
4. **Matches** emails to existing applications (by company domain, recruiter email, subject)
5. Creates timeline events and advances stages automatically

Auto-detected events are flagged so you can verify them.

### Rules

- Only advances forward (never backwards)
- Never auto-advances past Offer (no auto-Accept)
- Rejections are auto-applied (terminal, clearly detectable)
- Unmatched emails surface for manual assignment

---

<!-- help:ai-provider-setup -->
## AI Provider Setup

### LLamaSharp with grug 9B (Default - Local)

Download the optional GGUF model from Profile. It runs in-process and is used for
ingestion assistance and optional authoring drafts, not projection or export.

### Ollama (Alternative Local Provider)

Ollama runs AI models on your own hardware. No API key, no cost, no data leaving your machine.

```
ollama pull qwen3.5:4b
```

The app connects to `http://localhost:11434` by default. Change this on the Profile page if needed.

### Anthropic (Cloud)

1. Get an API key from [console.anthropic.com](https://console.anthropic.com)
2. On the Profile page, enter your API key in the Anthropic field
3. Select a model (Claude Haiku is cheapest, Sonnet is best value)

### OpenAI (Cloud)

1. Get an API key from [platform.openai.com](https://platform.openai.com)
2. On the Profile page, enter your API key in the OpenAI field
3. Select a model (GPT-4o-mini is cheapest)

### Which to Choose?

| Provider | Cost | Speed | Quality | Privacy |
|----------|------|-------|---------|---------|
| Ollama (local) | Free | Depends on hardware | Good with 4B+ models | Full - nothing leaves your machine |
| Anthropic Haiku | ~$0.001/resume | Fast | Very good | Data sent to Anthropic |
| Anthropic Sonnet | ~$0.01/resume | Medium | Excellent | Data sent to Anthropic |
| OpenAI GPT-4o-mini | ~$0.002/resume | Fast | Very good | Data sent to OpenAI |

> **Recommendation:** Use deterministic extraction and NER first. Enable a language model only when source documents need additional ingestion help.

---

<!-- help:profile--preferences -->
## Profile & Preferences

### Identity

- **Your Name** - used in projected resumes
- **Current Title** - provides context for extraction and optional authoring drafts
- **Years of Experience** - calibrates resume length recommendations
- **Career Goals** - guides role-specific projection emphasis

### Theme

Choose **System**, **Light**, or **Dark** from the dropdown at the top of the Profile page.

### GitHub Import

Enter your GitHub username and click **Import** to extract skills from your public repos:
- Languages reported by GitHub Linguist, weighted by code bytes
- Topics from repo metadata
- README analysis via lucidRAG (BERT mode, no LLM needed)
- Per-project profiles with technologies, skills, and time ranges

Supports personal access tokens for private repos and higher rate limits.

GitHub evidence is interpreted conservatively:

- A language total says the repository contains that language. It does not prove
  that you authored all of it or establish a proficiency level.
- Topics and README text are project self-description. They nominate possible
  skills but are weaker than attributed source changes or direct build manifests.
- A direct dependency can support a technology observation. A transitive
  dependency alone should not create a skill claim.
- Stars and repository size are popularity and scale signals. They are not code
  quality or competence scores.
- Forks, archived repositories, generated code, and missing attribution reduce
  the strength of an inference.

The current importer analyses languages, topics, README content, dates, and basic
repository metadata. Repository quality and authorship analysis are being added
through the JobML GitHub extension. That extension keeps raw observations,
OpenSSF Scorecard checks, workflow results, attribution, inferred skills, and
human-accepted claims separate.

### Work Preferences

- **Remote / Hybrid / Onsite** - filters job search results
- **Minimum Salary** - hard filter for job searches
- **Preferred Currency** - for salary display
- **Max Commute** - for onsite roles

### Blocked Lists

- **Blocked Industries** - gambling, defence, etc.
- **Blocked Companies** - specific employers to exclude from results

### Aspect Voting

The Profile page includes an aspect voting system for fine-tuning:

- Vote skills up/down to influence match scoring
- Upvoted skills get priority in role-specific projection
- Downvoted skills are de-emphasised

---

<!-- help:keyboard-shortcuts -->
## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `Ctrl+1` through `Ctrl+7` | Navigate to page (My CV, Jobs, Add Job, Project, Pipeline, Profile, Help) |
| `Ctrl+I` | Import resume |
| `Ctrl+S` | Save current state |
| `Ctrl+E` | Export (context-dependent) |

---

<!-- help:troubleshooting -->
## Troubleshooting

### Models won't download

- Check your internet connection
- The app needs ~600MB for initial model download
- Models are cached in your app data directory - subsequent launches are instant
- If a download was interrupted, delete the `models/` folder in app data and restart

### Ollama not connecting

- Ensure Ollama is running: `ollama serve`
- Check the URL on the Profile page (default: `http://localhost:11434`)
- Test with: `curl http://localhost:11434/api/tags`

### PDF not parsing correctly

- Try enabling **Docling** for ML-based layout detection:
  ```
  docker run -d --name docling-serve -p 5001:5001 quay.io/docling-project/docling-serve:latest
  ```
  Then set `Docling.Enabled = true` in `appsettings.json`
- Two-column PDFs are detected automatically but complex layouts may need Docling
- LaTeX-generated PDFs usually parse well with the default parser

### Match scores seem low

- Check that skills are being extracted correctly on the My CV page
- Import additional resume variants to enrich the skill ledger
- The matcher uses a 0.58 similarity threshold - some legitimate matches may be below this
- Achievement-text fallback catches skills mentioned in bullets but not in the Skills section

### Email scanning not finding matches

- Ensure the application's company name matches the sender domain
- Check that the recruiter email is recorded in the application's contact info
- The matcher needs confidence >= 0.7 to auto-associate - below that, emails appear as unmatched

### Data Location

All data is stored locally:

- **macOS:** `~/Library/Application Support/lucidRESUME/`
- **Windows:** `%APPDATA%\lucidRESUME\`
- **Linux:** `~/.config/lucidRESUME/`

The main database is `data.db` (SQLite). You can back it up by copying this file.

### Export & Backup

- **JSON export**: File > Export JSON - full backup of all data
- **JSON import**: File > Import JSON - restore from backup
- The JSON file includes resumes, jobs, applications, profile, and settings

---

## Getting Help

- **GitHub Issues:** [github.com/scottgal/lucidRESUME/issues](https://github.com/scottgal/lucidRESUME/issues)
- **Source Code:** [github.com/scottgal/lucidRESUME](https://github.com/scottgal/lucidRESUME)

***lucid*RESUME** is free, open-source software released into the public domain under the Unlicense.
