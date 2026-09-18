# JobML GitHub Repository Extension 0.1

Status: Experimental Draft

Namespace: `lucidresume.github`
Last updated: 2026-09-18

This extension records repository observations, quality assessments, contribution
attribution, and skill candidates without pretending that any one of them proves
a person's competence.

It extends [JobML 0.1](jobml-0.1-specification.md). The deterministic shape is
also present in [`jobml-0.1.schema.json`](jobml-0.1.schema.json).

## 1. Design rule

Repository analysis has four separate lanes:

```text
repository facts -> quality assessments -> skill observations -> reviewed claims
```

A processor MUST keep these lanes separate.

- A repository fact says what the repository contained at a revision.
- A quality assessment applies a named method to that revision.
- A skill observation proposes what the repository may demonstrate.
- A core JobML claim states what a person claims and cites evidence.

Stars, forks, repository size, and recent pushes MUST NOT be used as direct proof
of code quality or personal competence.

## 2. Research basis

The extension reuses established signals instead of inventing one opaque score:

- GitHub language totals are produced from Linguist's byte counts. Linguist
  excludes common binary, vendored, generated, documentation, data, and prose
  content from its normal repository statistics. This makes the totals a useful
  repository observation, but not proof of authorship. See
  [How Linguist works](https://github.com/github-linguist/linguist/blob/main/docs/how-linguist-works.md)
  and [Linguist overrides](https://github.com/github-linguist/linguist/blob/main/docs/overrides.md).
- GitHub exposes community-profile facts such as README, licence, contributing
  guide, code of conduct, issue template, pull request template, and its own
  `health_percentage`. That percentage measures presence of community files, not
  implementation quality. See the
  [community metrics API](https://docs.github.com/en/rest/metrics/community).
- GitHub Actions exposes workflow definitions and run conclusions. A configured
  workflow is evidence of automation; a successful run at the analysed revision
  is stronger evidence than file presence alone. See the
  [workflows API](https://docs.github.com/en/rest/actions/workflows) and
  [workflow runs API](https://docs.github.com/en/rest/actions/workflow-runs).
- GitHub can export dependency data as SPDX SBOM. Direct and transitive
  dependencies must remain distinguishable. The older synchronous SBOM endpoint
  is scheduled to close after 2026-11-13, so implementations should use the
  asynchronous generation and fetch flow. See the
  [SBOM API](https://docs.github.com/en/rest/dependency-graph/sboms).
- OpenSSF Scorecard supplies versioned, explainable security checks including
  maintained status, code review, dependency update tooling, pinned dependencies,
  security policy, token permissions, dangerous workflow patterns, and known
  vulnerabilities. Scorecard results are security posture assessments, not a
  general code-quality or developer-skill score. See the
  [Scorecard checks](https://github.com/ossf/scorecard/blob/main/docs/checks.md).
- GitHub code scanning can report tool, rule, severity, state, revision, and code
  location. Availability depends on repository settings and permissions, and the
  absence of alerts does not prove that scanning ran. See the
  [code scanning API](https://docs.github.com/en/rest/code-scanning).
- Git tree responses allow revision-pinned path discovery, but recursive results
  can be truncated above GitHub's documented limits. See the
  [Git trees API](https://docs.github.com/en/rest/git/trees).

## 3. Repository record

Each repository record MUST identify an immutable revision and observation time.

```yaml
extensions:
  lucidresume.github:
    version: "0.1"
    repositories:
      - id: github-example-platform
        provider: github
        uri: https://github.com/example/platform
        revision: 0123456789abcdef0123456789abcdef01234567
        observed_at: 2026-09-18T12:00:00Z
        visibility: public
        fork: false
        archived: false
```

`revision` MUST be a commit SHA or another immutable provider revision. Moving
branches MAY be shown in the UI but MUST NOT replace it.

## 4. Attribution

Repository ownership and code authorship are different facts.

```yaml
attribution:
  login: jane-smith
  relationship: contributor
  confidence: 0.93
  commit_count: 47
  first_commit: 2022-03-10T09:12:00Z
  last_commit: 2026-08-21T16:40:00Z
```

Allowed relationships are `owner`, `member`, `contributor`, `fork-only`, and
`unknown`.

Attribution SHOULD use provider-linked commit identities where possible. Email or
name-only matching SHOULD have lower confidence and MUST account for shared,
rewritten, bot, and unlinked commits. A repository owned by a user MUST NOT imply
that the user authored every file.

## 5. Observations

Observations report source facts without interpreting competence.

```yaml
observations:
  languages:
    - name: C#
      bytes: 842113
      fraction: 0.71
      source: github-linguist
  topics: [avalonia, dotnet, resume]
  manifests: [lucidRESUME.sln, Directory.Packages.props]
  direct_dependencies: [Avalonia, YamlDotNet]
  workflows: [build, test, release]
  community_health_percentage: 87
  license_spdx: Unlicense
  stars: 42
```

Processors SHOULD record:

- the source of each measurement;
- whether dependency evidence is direct or transitive;
- whether a workflow merely exists or passed at the analysed revision;
- whether a tree or history query was truncated;
- failures caused by permissions or rate limits as `unknown`, not `false`.

README text and topics are self-description. They MAY nominate candidate skills,
but SHOULD receive less weight than authored source usage, direct manifests, or a
successful workflow that exercises the technology.

## 6. Quality assessments

Assessments MUST name their method and method version.

```yaml
quality_assessments:
  - method: openssf-scorecard
    method_version: 5.3.0
    observed_at: 2026-09-18T12:05:00Z
    score: 7.4
    checks:
      - name: Maintained
        score: 10
        reason: Recent repository activity detected.
        documentation_uri: https://github.com/ossf/scorecard/blob/main/docs/checks.md
      - name: Code-Review
        score: 6
        reason: Some recent changes showed review evidence.
```

An overall score MUST retain its component checks and method version. Consumers
SHOULD present the components because methods and weights change over time.

Recommended dimensions are:

| Dimension | Example signals | What it does not prove |
|---|---|---|
| Maintainability | activity, releases, issue response | personal skill |
| Engineering process | CI, tests, review, branch rules | defect-free code |
| Security posture | Scorecard, CodeQL, dependency alerts | general quality |
| Documentation | README, examples, API docs | implementation depth |
| Community readiness | licence, contributing guide, templates | popularity |
| Reproducibility | lock files, pinned actions, release provenance | authorship |

## 7. Skill observations

Skill observations are candidates, not claims.

```yaml
skill_observations:
  - concept: csharp
    basis: [language, source-usage, commit-attribution]
    confidence: 0.91
    state: inferred
    paths:
      - src/Example/Service.cs
    notes: Dominant Linguist language with attributed changes in source paths.

  - concept: github-actions
    basis: [workflow, commit-attribution]
    confidence: 0.78
    state: inferred
    paths:
      - .github/workflows/ci.yml
```

Allowed basis values are `language`, `topic`, `readme`, `manifest`,
`direct-dependency`, `workflow`, `source-usage`, and `commit-attribution`.

Confidence MUST describe confidence in the observation-to-skill inference. It
MUST NOT be presented as proficiency. Confidence SHOULD rise when independent
signals agree, and SHOULD fall when the repository is a fork, attribution is
unknown, evidence is generated or vendored, or only README text supports it.

Suggested evidence ordering, strongest first:

1. attributed source changes that use the technology;
2. attributed manifest or workflow changes plus repository usage;
3. direct dependency or build manifest presence;
4. Linguist language contribution with attribution;
5. repository topic;
6. README mention;
7. transitive dependency alone.

A transitive dependency alone SHOULD NOT create a skill observation.

## 8. Promotion to a JobML claim

A processor MAY suggest a core claim from one or more skill observations:

```yaml
claims:
  - id: github-platform-csharp
    subject: github-example-platform
    statement: Contributed C# implementation work to the Example Platform repository.
    concepts:
      skills: [csharp]
    supported_by:
      - type: repository
        uri: https://github.com/example/platform/tree/0123456789abcdef0123456789abcdef01234567
    origin: derived
    review: required
```

The statement MUST match the strength of attribution. `Repository contains C#`
does not entail `Architected a production C# platform`. A human must review the
claim before it becomes accepted evidence.

## 9. Drift and refresh

Repository records are snapshots. A refresh SHOULD create a new observation for
a new revision rather than overwrite the revision that supports an accepted
claim. Processors SHOULD report:

- revision changed;
- source path removed or changed;
- attribution changed;
- assessment method version changed;
- quality checks improved or regressed;
- access became unavailable;
- a query was incomplete or rate limited.

An accepted claim remains linked to its original revision until a person accepts
new evidence.

## 10. lucidRESUME extraction profile

The current importer implements:

- repository metadata and filtering;
- GitHub Linguist language byte fractions;
- repository topics;
- README summarisation and taxonomy matching;
- project dates and URLs;
- a heuristic evidence-strength value.

The current heuristic combines size, skill count, stars, and recency. It is a
ranking aid, not a standards-based repository quality score. It MUST NOT be
serialised as an OpenSSF or JobML quality assessment.

The next implementation stage SHOULD add immutable revision capture, commit
attribution, manifests and direct dependencies, workflow status, community
metrics, and versioned OpenSSF Scorecard results before promoting repository
observations into reviewable JobML claims.
