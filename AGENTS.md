# AGENTS

## Release

This repo is a library and follows Tupic's [Release Guideline](https://tupic.atlassian.net/wiki/spaces/TPRD/pages/1245412)
([Releasing Libraries](https://tupic.atlassian.net/wiki/spaces/TPRD/pages/123994142)):

- Trunk-based: branch from `main` and merge back by pull request. No `dev`, `develop` or `release/*` branches.
- A SemVer tag `vX.Y.Z` on a green `main` commit is the release: major for a breaking change, minor for something
  new, patch for a fix.
- Fixes go to `main`, then the next patch. Only when `main` holds an unreleased breaking change: branch
  `hotfix/<name>` from the last tag, fix it, tag the patch there, then merge its pull request into `main` with a
  merge commit. No cherry-picks.
- Never move or delete a tag.
