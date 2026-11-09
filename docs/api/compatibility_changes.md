# Compatibility change ledger

## Unreleased preparation

- Added a proposed API support/review policy. No package identity/version or platform-support commitment selected.
- Existing plugin API remains0.1.0. `ApiVersion.Satisfies` and public `PlanRevision` remain unchanged.
- Complete reviewed assembly/public-package baseline and binary/consumer proof are pending. Compilation and metadata loading were not performed for this preparation.
- Accepted preparatory scope: AuthoringInfo.cs plus the leading ApiVersion declaration/preamble only. Added a conservative source-snapshot detector and six stdlib tests; full API/enum/binary coverage remains pending. No held security work is brought into scope.

## Required entry for an intentional API change

Record affected package/project and exact declarations; old/new behavior and signatures; source/binary/persisted-data impact; baseline additions/removals; approved API-line and package-version decisions (when made); deprecation/replacement/removal plan; consumer migration example; focused test results and unrun gates; reviewer and source reference. Do not record a planned version as released or remove a failure by silently accepting new expected output.

This is a compatibility-review ledger, not a claim of an existing release history or an automatically approved breaking change.
