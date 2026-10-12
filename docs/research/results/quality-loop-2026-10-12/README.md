# Synthetic quality-loop record

- `baseline-*`: twelve broader cases before the loop's fixes; one automated
  finding (lost unknown production effect).
- `broader-screen-*`: twelve cases with v5 checks. Automated checks were clean,
  but Luna identified three factual/structural weaknesses; see `review.json`.
- `prototype-repair-*` and `metrics-repair-*`: four targeted v6 replays. Final
  prose retains the source; rejected-edit warnings explain the attempted drift.
- `career-baseline-*` and `career-targeted-*`: contrasting job projections of the
  same three-role synthetic career, before/after summary and skill selection fixes.
- `native-client.json`: fifteen native workflow groups using isolated SQLite and
  an unavailable writer. Actual writer behavior is covered by the separate model
  evaluations, not this native fallback run.

The synthetic career IDs vary between runs; compare source statements and job
relevance, not random IDs. Raw exchanges, caches and full projection objects stay
in the temporary evaluation directories. The broader screen was not rerun in full
under v6; the repair records cover only affected families. This is a regression
record, not a held-out benchmark or human preference study.
