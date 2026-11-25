# Changelog

## [0.8.0] - 2026-11-25

### Changed

- Rebuilt the application identity and release package naming for GhostStrap.
- Kept the Avalonia/.NET 10 architecture and the inherited feature set.
- Added GhostStrap's parallel range downloader to macOS bundle package downloads, with range validation, fallback, temporary-file cleanup, and final package hash verification.
- Reworked the release workflow to collect platform artifacts from this repository, validate the version tag, create SHA256 checksums, and publish the changelog section without a separate packages-repository secret.

### Licensing

- Preserved Froststrap's MPL-2.0 source notices and added a separate notice for the MIT-licensed GhostStrap downloader integration.
