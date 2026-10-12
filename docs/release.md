# Release & Archive Guide

lucidRESUME releases are built by `.github/workflows/release.yml` when a tag matching `v*` is pushed, or manually from the GitHub Actions UI.

## Release Outputs

The workflow publishes self-contained application archives for:

| Runtime | Host runner | Archives |
|---------|-------------|----------|
| `win-x64` | Windows | `.zip` |
| `win-arm64` | Windows | `.zip` |
| `osx-x64` | macOS | `.tar.gz` containing `lucidRESUME.app` |
| `osx-arm64` | macOS | `.tar.gz` containing `lucidRESUME.app` |
| `linux-x64` | Linux | `.tar.gz` |
| `linux-arm64` | Linux | `.tar.gz` |

The release also publishes an experimental Chrome extension archive. It is an
unpacked Manifest V3 build for developer-mode installation, not a Chrome Web
Store package.

Each archive is accompanied by a `.sha256` checksum file.

The release also includes a documentation archive in both `.zip` and `.tar.gz` formats. That archive contains:

- `lucidRESUME-docs-single-page.md` - one-page Markdown archive for offline reading.
- `README.md` - product overview and quickstart.
- `architecture.md` - technical architecture.
- `user-manual.md` - in-app help manual.
- `jobml-0.1-specification.md` - normative JobML document and processor rules.
- `cjobml-0.1-specification.md` - compact publication projection and parser rules.
- `jobml-github-extension-0.1.md` - repository quality, attribution, and skill observation extension.
- `complete-career-workflow.md` - canonical transcript, interviews, archive and performance.
- `jobml-transcript-extension-0.1.md` - retained narrative and raw role interview fields.
- `screenshots/` - native desktop captures used by the guides.
- `jobml-package-extension-0.1.md` - package-family and registry-observation extension.
- `jobml-web-compiler.md` - ASP.NET Core publication and deterministic compilation guide.
- `chrome-evidence-filler.md` - on-device form-mapping experiment, privacy model, and limitations.
- `nimble-parsing.md` - active local Ollama decision model setup and audit behavior.
- `jev-parsing-experiment.md` - bounded parsing decisions and benchmark procedure.
- `resume-output-design.md` - Markdown, DOCX, PDF, and cJobML output contract.
- `2026-09-26-resume-cv-ats-review.md` - sourced format and ATS compatibility research.
- `jobml-0.1.schema.json` - deterministic validation schema.

The GitHub release page itself is populated with Markdown release notes that cover basic usage, configuration options, and macOS Gatekeeper guidance.

## macOS Gatekeeper

The macOS app bundle and each native executable are ad-hoc signed, but the bundle
is not Apple-notarized. The bundle requires macOS 14 or newer: the shipped
Apple Silicon sqlite-vec library targets 14.0 (the previous 12.0 bundle declaration
was incorrect). Release launch checks run on macOS 15 for both architectures.
The application payload and native libraries remain in `Contents/Resources/app`
rather than being extracted on first launch. Control-click
`lucidRESUME.app`, choose **Open**, and confirm the first launch. If macOS still
blocks the downloaded app, use a per-app exception after extracting the archive:

```bash
xattr -dr com.apple.quarantine ~/Applications/lucidRESUME.app
```

Do not disable Gatekeeper globally. The release workflow verifies the nested code
signatures after extracting the final archive and runs all native client workflows
on matching Intel and Apple Silicon runners before upload. Neither Mac architecture
may skip its launch check.

The extracted launcher also runs `--storage-audit` from an unrelated working
directory without developer library search paths. This uses the shipped SQLite
provider and sqlite-vec extension to migrate synthetic JSON, save and reopen a
database, search both vector dimensions, check integrity and release file handles.
It only creates temporary synthetic data; it never opens the user's career database.
For support diagnostics, run:

```bash
/path/to/lucidRESUME.app/Contents/MacOS/lucidRESUME --storage-audit
```

A successful result contains `"complete":true`; failure returns exit code 2 and
the exception details. The workflow retains this JSON with the client screenshots.

## Creating A Release

1. Ensure CI is green on `main`.
2. Increment the minor version for each release in the current `2.x` line
   (for example, `2.5.0` to `2.6.0`). Check existing tags and releases first;
   never reuse a release version. Update the application version, workflow default
   and matching release notes together.
3. Create and push the tag:

```bash
git tag v2.6.0
git push origin v2.6.0
```

The `Release - App Archives` workflow will build, archive, checksum, and attach the files to a GitHub release.

## Manual Dry Run

Use the workflow dispatch button in GitHub Actions and provide a version such as
`2.6.0-rc1`. Manual runs build the same application, documentation, and Chrome
extension archives and retain them as workflow artifacts, but do not publish a
GitHub release. Only a pushed `v*` tag can publish one.

## Archive Policy

The project intentionally ships archives rather than native installers for now. Archives are easier to inspect, work across more environments, and avoid unsigned-installer friction. Native packages such as MSI, DMG, AppImage, Flatpak, or Snap can be added later once signing, icons, and installer metadata are in place.


## 2.6.0 preparation and end-to-end checks

See [2.6.0 release notes](release-notes/v2.6.0.md) and the
[validation report](reviews/2026-10-12-v2.5-validation.md). The native validator
covers import/review, deduplication, interviews, JobML editing, generation,
archives, job/pipeline flows, hiring, profile saving, restart and PDF re-import.
It requires a fresh isolated output directory and uses synthetic career data.

```bash
python3 scripts/validate-release-cli.py --output artifacts/cli-e2e
python3 scripts/validate-release-client.py --output artifacts/native-e2e
```

CI runs the offline CLI and native checks across its OS matrix, uploads evidence
on failure, and checks the local web publish/compile/download round trip on Linux.
Release source validation also runs the CLI and native checks before building
archives. Both macOS archive runners extract the final archive, run the SQLite
audit and test the signed `.app` launcher using the same native workflows. Other
cross-architecture builds retain asset checks without claiming they were launched
on incompatible hosts.

Close the application before backing up the complete app data directory. Include
`data.db`, `generated-resumes/` and `prose-library/`; a database-only backup cannot
restore archived document files. Preparing version metadata does not publish a
release. Push `v2.6.0` only after final-source CI and archive validation pass.
