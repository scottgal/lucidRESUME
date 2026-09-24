# Product data and clean-install contract

lucidRESUME ships the shared data needed to import and match a resume. It does not ship a developer's or user's resume corpus.

## Shipped product data

- `skill-taxonomy.txt`: 19,983 preprocessed role/skill rows, containing 16,169 distinct terms across 17 source groups.
- `skill-priorities.txt`: 1,542 priority-labelled role/skill rows.
- `role-archetypes.txt`: compact, reviewable seed profiles for Lead Developer, Head of Engineering, CTO and VP of Engineering.
- Cross-industry taxonomy files, entity lookup lists, GitHub language mappings, writing-quality word lists and Hunspell dictionaries.
- Model configuration and public acquisition URLs.

The large ONNX and GGUF weights are immutable public model dependencies, not user data. They are acquired into the application data directory on first use because bundling the optional grug model would add about 5.6 GB to every platform archive. Downloads are written to a temporary file and atomically installed. Invalid or interrupted ONNX downloads are detected and repaired.

Role centroids are materialised locally from the shipped role seeds with the configured embedding service. This keeps the role definitions inspectable and makes centroid generation reproducible without embedding one model vendor's floating-point output into the file format.

## Never shipped

- `data.db` or any other user store
- imported resumes, LinkedIn exports or GitHub account data
- full JobML career-record exports or generated role projections
- API keys, user secrets or provider credentials

`lucidRESUME --asset-audit` verifies the immutable product assets in an installed build. The release workflow runs it for every target and fails if a user database, JobML career-record snapshot or credential-like file appears in the publish directory.

## Dataset provenance

The broad taxonomy was processed with DuckDB from the datasets recorded in [v1.5.0 release notes](release-notes/v1.5.0.md): Kaggle role/skill and priority spreadsheets plus the frequency-filtered LinkedIn job-skills dataset. The source datasets are not redistributed. Their processed, non-personal lookup output is versioned with the application.
