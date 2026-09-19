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

Each archive is accompanied by a `.sha256` checksum file.

The release also includes a documentation archive in both `.zip` and `.tar.gz` formats. That archive contains:

- `lucidRESUME-docs-single-page.md` - one-page Markdown archive for offline reading.
- `README.md` - product overview and quickstart.
- `architecture.md` - technical architecture.
- `user-manual.md` - in-app help manual.
- `jobml-0.1-specification.md` - normative JobML document and processor rules.
- `cjobml-0.1-specification.md` - compact publication projection and parser rules.
- `jobml-github-extension-0.1.md` - repository quality, attribution, and skill observation extension.
- `jobml-0.1.schema.json` - deterministic validation schema.

The GitHub release page itself is populated with Markdown release notes that cover basic usage, configuration options, and macOS Gatekeeper guidance.

## macOS Gatekeeper

The macOS app bundle and its native libraries are ad-hoc signed, but the bundle is
not Apple-notarized. Control-click `lucidRESUME.app`, choose **Open**, and confirm
the first launch. If macOS still blocks a quarantined library, use a per-app
exception after extracting the archive:

```bash
xattr -dr com.apple.quarantine ~/Applications/lucidRESUME.app
```

Do not disable Gatekeeper globally. The release workflow verifies the nested code
signatures and runs the packaged app through the Avalonia UI smoke script on a
matching macOS runner before upload.

## Creating A Release

1. Ensure CI is green on `main`.
2. Choose a semantic version, for example `1.0.0`.
3. Create and push the tag:

```bash
git tag v1.0.0
git push origin v1.0.0
```

The `Release - App Archives` workflow will build, archive, checksum, and attach the files to a GitHub release.

## Manual Dry Run

Use the workflow dispatch button in GitHub Actions and provide a version such as `0.1.0-preview`. Manual runs upload artifacts to the workflow run but do not publish a GitHub release unless the run is for a `v*` tag.

## Archive Policy

The project intentionally ships archives rather than native installers for now. Archives are easier to inspect, work across more environments, and avoid unsigned-installer friction. Native packages such as MSI, DMG, AppImage, Flatpak, or Snap can be added later once signing, icons, and installer metadata are in place.
