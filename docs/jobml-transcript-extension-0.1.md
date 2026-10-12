# JobML complete transcript extension 0.1

Status: Experimental, implemented by lucidRESUME

Namespace: `lucidresume.transcript`

Last updated: 2026-10-12

This optional extension accompanies a full JobML `career_record`. It preserves
source narrative and complete reviewed role interviews which would otherwise be
reduced to achievement claims. Core JobML claims, sources and review decisions
remain the basis for evidence coverage; this extension does not accept its contents
as facts automatically.

```yaml
extensions:
  lucidresume.transcript:
    version: "0.1"
    retained_source_narrative: |-
      Original career narrative retained for review.
    narrative_policy: Retained source material is not automatically an accepted claim or skill.
    role_interviews:
      - role_id: 0123456789abcdef0123456789abcdef
        entries:
          - id: fedcba9876543210fedcba9876543210
            topic: role:0123456789abcdef0123456789abcdef:delivery
            requirement: Production reliability
            context: During recurring service incidents
            action: Automated recovery for the affected service
            outcome: Reduced manual recovery work
            reviewed_prose: Automated service recovery to reduce manual incident work.
            skills: [Incident response]
            accepted_at: "2026-10-12T10:00:00.0000000+00:00"
```

`role_id` is the canonical role GUID in compact `N` form. `id` identifies the
accepted interview entry. `topic` identifies the interview dimension; `requirement`
retains its target. `context`, `action` and `outcome` preserve the raw author answers.
`reviewed_prose` and `skills` preserve the author's reviewed account, and
`accepted_at` records its acceptance timestamp. These fields are candidate-declared,
not an independent employer verification.

Full career generation writes the extension even when no interviews exist; in
that case `role_interviews` is empty. When an interview is merged into existing
JobML, lucidRESUME refreshes its career extension and preserves unrelated extension
namespaces and existing authored narrative. The generated resume archive retains
the full career snapshot separately from the shorter employer-facing projection.

See [JobML 0.1](jobml-0.1-specification.md) and
[the complete career workflow](complete-career-workflow.md).


`role_interviews.entries[].contribution_account` optionally retains ownership,
action, object, context, decision, outcome, delivery status, qualifiers, declared
skills, reviewed prose and role/interview IDs. Unknown fields stay empty.

`prose_index` contains reusable derived passage analysis: source and accepted-claim
revisions, policy and embedding-space identity, original scoped prose, claim/evidence
links, skills, reviewed accounts, structural diagnostics and optional vectors. The
index is invalidated by review-only and skill-link changes as well as source edits.
Its structural completeness cue is heuristic; neither that cue nor vector similarity
establishes competence. Vectors may be absent and lexical retrieval remains available.

Full career records also expose `lucidresume.publications` with cached article
observations and retained passages. Generated projections expose
`lucidresume.related_work`, containing source ID, URL, kind, relevance, supporting
passage, revision, requirements, claim/evidence IDs and observed attribution. These
extensions complement accepted core claims; they never accept unreviewed content.


`prose_bindings` maps each stable segment ID and its internal ledger claim IDs to
actual claim, subject and skill-concept IDs in this JobML file. This mapping uses
retained evidence identities and accommodates custom JobML IDs. Internal ledger
IDs in the cached analysis are not assumed to be portable JobML claim IDs.
