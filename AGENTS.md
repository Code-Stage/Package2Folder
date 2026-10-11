# AGENTS.md

Package2Folder (`net.codestage.package2folder`) is a Unity Editor package. The package source is this repository; the development Unity project references it through a symlink. Read `README.md` for the public API and `Tests/Editor/` for integration tests.

## Branches and releases

Before creating a branch, opening a PR or preparing a release, read the [shared branch policy](https://github.com/Code-Stage/UAS-Common/blob/main/ai-agent-guides/product-branching.md) and `.github/product-release.json`.

Create feature and fix branches from `develop` and target their PRs there. Cut `release/<version>` from `develop`; merge its validated PR into `master` with a merge commit. Use merge commits for synchronization too; squash and rebase break release ancestry. After a stable merge, synchronize `master` back into `develop`.

Keep the product version and dated changelog entry in sync. After a stable merge changes the release version, the Action creates the bare version tag and GitHub Release at that merge commit. Installing this workflow does not publish the existing version. Store uploads and submissions follow their own checks.
