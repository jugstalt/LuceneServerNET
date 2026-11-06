# ChangeLog

All notable changes to LuceneServerNET are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased] – v3

### Security

- **Path traversal protection for index and metadata names.** Index ids from the route
  (e.g. `removeindex/..`) and custom metadata names (e.g. `getmeta/{id}?name=../../x`) were
  passed unchecked into `Path.Combine` and could read, create or delete files and directories
  outside of `RootPath` / `ArchivePath`.
  - Index ids and metadata names must now match `^[a-z0-9_-]{1,64}$` (ids are lower-cased first).
  - Validation happens centrally in the controller and again in the engine
    (`LuceneService`, `LuceneSharedResourcesService`, `ArchiveService`).
  - All file system paths are built via the new `PathGuard.Combine`, which ensures the resolved
    path stays below the configured root directory.
  - `releaseall` no longer uses the `"*"` placeholder id; every route id is validated without exception.

### Breaking Changes

- Index ids and metadata names containing characters other than `a-z`, `0-9`, `_`, `-`
  (e.g. `.`, spaces, umlauts) or longer than 64 characters are now rejected.
  - **REST clients:** such requests return an error result (`success: false`).
  - **NuGet client `LuceneServerNET.Client`:** no API change; affected calls receive the error result.
  - **Existing indices on disk:** files are not touched, but indices whose folder name does not
    match the rule are no longer reachable via the API until renamed.

### Fixed

- The reserved metadata name `mapping` is now actually blocked (the old check compared against
  `"mapping "` with a trailing space).
