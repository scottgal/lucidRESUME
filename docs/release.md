# Release & Archive Guide

lucidRESUME releases are built by `.github/workflows/release.yml` when a tag matching `v*` is pushed, or manually from the GitHub Actions UI.

## Release Outputs

The workflow publishes self-contained application archives for:

| Runtime | Host runner | Archives |
|---------|-------------|----------|
| `win-x64` | Windows | `.zip`, `.tar.gz` |
| `win-arm64` | Windows | `.zip`, `.tar.gz` |
| `osx-x64` | macOS | `.zip`, `.tar.gz` |
| `osx-arm64` | macOS | `.zip`, `.tar.gz` |
| `linux-x64` | Linux | `.zip`, `.tar.gz` |
| `linux-arm64` | Linux | `.zip`, `.tar.gz` |

Each archive is accompanied by a `.sha256` checksum file.

The release also includes a documentation archive in both `.zip` and `.tar.gz` formats. That archive contains:

- `lucidRESUME-docs-single-page.md` - one-page Markdown archive for offline reading.
- `README.md` - product overview and quickstart.
- `architecture.md` - technical architecture.
- `user-manual.md` - in-app help manual.

The GitHub release page itself is populated with Markdown release notes that cover basic usage, configuration options, and macOS Gatekeeper guidance.

## macOS Gatekeeper

Current app archives are unsigned. macOS may quarantine the extracted app and block the first launch. Do not disable Gatekeeper globally. Use a per-folder exception after extracting the archive:

```bash
xattr -dr com.apple.quarantine ~/Applications/lucidRESUME
```

Alternatively, Control-click or right-click the `lucidRESUME` executable, choose Open, and confirm once.

## Creating A Release

1. Ensure CI is green on `master`.
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
