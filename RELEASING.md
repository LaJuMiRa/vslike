# Releasing

## 1. Prepare

1. Update `VERSION` (for example `0.2.1`).
2. Add a `## [0.2.1] - YYYY-MM-DD` section to `CHANGELOG.md`. The workflow copies it into the release notes.
3. Check locally: `./release.sh` (engine tests, Swift tests, build, smoke test of the bundle, zip).
4. Commit: `git commit -am "chore: release 0.2.1"`.

## 2. Tag and push

```sh
git tag v0.2.1
git push origin main
git push origin v0.2.1     # tags are not pushed by a plain `git push`
```

The tag starts `.github/workflows/release.yml` on a `macos-26` (Apple Silicon) runner. It checks that the tag matches
`VERSION`, runs `./release.sh` and publishes a GitHub release with `VsLike-<version>-arm64.zip` and its SHA-256.

There is no notarization (no paid Apple Developer account). Users allow the first start once, see the README.

## 3. Homebrew tap

With the repository secret `TAP_GITHUB_TOKEN` (a token with write access to `LaJuMiRa/homebrew-tap`) the workflow
opens a pull request in the tap that changes only `Casks/vslike.rb`. Merge it, then check:

```sh
brew update
brew install --cask LaJuMiRa/tap/vslike
```

Without the secret, render the cask yourself and add it to the tap:

```sh
packaging/homebrew/render-cask.sh 0.2.1 <sha256 from the .sha256 file> Casks/vslike.rb
```
