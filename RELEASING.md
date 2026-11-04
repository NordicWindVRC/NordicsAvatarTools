# Releasing

How packages in this repository are released to the NordicWind VCC listing.

## How it works

```
package.json + CHANGELOG.md on master
        │
        ▼  Actions → "Release package" (started by hand)
build_package.py checks the package and builds the zip
        │
        ▼
GitHub release  avatar-tools-v0.1.0  with  app.nordicwind.avatar-tools-0.1.0.zip
        │
        ▼  "Build VCC listing" (starts automatically)
build_listing.py reads every release and writes index.json
        │
        ▼
GitHub Pages  https://vpm.nordicwind.app/index.json   ← VCC reads this
```

- **The GitHub releases are the source of truth.** The listing is rebuilt from all releases
  every time, so it always matches what's published. Nothing is committed back to the repo.
- **One zip per release.** Each release holds one package version. The tag is
  `<package>-v<version>`, for example `avatar-tools-v0.1.0` or `texture-wrapper-v0.9.0`.
- **The version comes from `package.json`.** The workflow never changes files; you bump the
  version yourself and commit it before releasing.

| File | Purpose |
|---|---|
| `Tools/Release/vpm.json` | Listing name, id and URL, and which packages are released from where |
| `Tools/Release/build_package.py` | Checks one package and builds its zip and release notes |
| `Tools/Release/build_listing.py` | Builds `index.json` and the landing page from all releases |
| `.github/workflows/release.yml` | **Release package**: builds and publishes one package |
| `.github/workflows/listing.yml` | **Build VCC listing**: rebuilds and deploys the listing |

## One-time setup

1. **GitHub Pages.** In the repository, open **Settings → Pages** and set **Source** to
   **GitHub Actions**.
2. **DNS.** At your DNS provider for `nordicwind.app`, add a record:

   | Type | Name | Value |
   |---|---|---|
   | CNAME | `vpm` | `nordicwindvrc.github.io` |

3. **Custom domain.** Back in **Settings → Pages**, enter `vpm.nordicwind.app` as the custom
   domain and save. When the DNS check passes, tick **Enforce HTTPS**. The certificate can take
   a while to be issued.
   - With GitHub Actions as the source, you don't need a `CNAME` file in the repo; GitHub
     ignores it. The domain is stored in the repository settings.
   - Recommended: verify the domain under your **account** settings (**Settings → Pages →
     Add a domain**). This stops anyone else from using `nordicwind.app` subdomains on
     GitHub Pages.
4. **First deploy.** Open **Actions → Build VCC listing → Run workflow**. With no releases yet,
   it publishes an empty listing. Check that `https://vpm.nordicwind.app/` shows the landing
   page and `https://vpm.nordicwind.app/index.json` shows the JSON.
5. **Check that VCC can find the dependencies.** The packages depend on NDMF
   (`nadena.dev.ndmf`) and the VRChat Avatars SDK. VCC can only install them automatically if
   it knows a listing that has them. The VRChat SDK is always available. Make sure that NDMF
   is too: in a fresh VCC install with only the NordicWind listing added, try adding the
   bundle to a new avatar project. If VCC can't resolve NDMF, tell users in the README to also
   add the NDMF listing (`https://vpm.nadena.dev/vpm.json`).

The listing id `app.nordicwind.vpm` and the URL in `vpm.json` must never change once people
have added the listing. VCC uses them to recognise it.

## Releasing a version

1. **Bump the version** in the package's `package.json`.
   - Bundle (`Packages/app.nordicwind.avatar-tools`): bump the bundle version whenever an
     included tool changes. Bump each changed tool's own version in its `CHANGELOG.md`.
   - Texture Wrapper (`Packages/app.nordicwind.texture-wrapper`): its version is its API
     version. Any breaking API change is a major bump (a minor bump while below 1.0.0).
2. **Update the changelogs.**
   - Change the package's entry from `## 0.1.0 — unreleased` to `## 0.1.0 — 2026-11-04`
     (the release date). The release workflow refuses a full release without a date.
   - For the bundle, also update the tool version table and each changed tool's
     `CHANGELOG.md` entry, with the same date.
   - The text under the package's entry becomes the GitHub release notes.
3. **Run the tests** in Unity (Test Runner → EditMode).
4. **Check the package locally** from the repository root:

   ```bash
   python3 Tools/Release/build_package.py --package app.nordicwind.avatar-tools
   ```

   This runs the same checks as the workflow and writes the zip to `dist/` (ignored by git).
   Add `--local` to skip the release-date check while you're still working on a version.
5. **Commit and push** to `master`.
6. **Run the release.** Open **Actions → Release package → Run workflow**, keep the branch on
   `master`, choose the package, and run it.
7. **Check the result.** When **Build VCC listing** finishes (it starts by itself), open
   `https://vpm.nordicwind.app/` and check that the new version is listed. In VCC, the new
   version shows up after VCC refreshes its listings (restart VCC to force it).

### Dependencies between packages

If a package needs a newer version of another NordicWind package, release that package first.
For example, a future Advanced Atlaser release that needs Texture Wrapper 0.10.0 must wait
until Texture Wrapper 0.10.0 is in the listing.

## Prereleases (beta testing)

Use a SemVer prerelease version, such as `0.2.0-beta.1`.

- The changelog entry can be `## 0.2.0-beta.1` or the base version, `## 0.2.0 — unreleased`.
  A prerelease doesn't need a date.
- The GitHub release is marked as a pre-release.
- VCC only shows prerelease versions to users who turn on **Settings → Packages → Show
  Pre-release Packages**, so normal users aren't affected.
- Each beta needs its own number (`-beta.1`, `-beta.2`, …). Finish with the full version,
  `0.2.0`.

## Rules

- **Never change a released version.** VCC and users' projects remember each version's zip
  checksum. If a release is wrong, release a new patch version. The workflow refuses to
  publish a version whose tag already exists.
- **Deleting a release removes it from the listing.** The listing rebuilds automatically when
  a release is deleted or edited on GitHub. Projects that already installed that version keep
  it, but can't restore it from VCC anymore. Keep the tag, so the version can't be reused by
  mistake.
- **Don't edit release zips on GitHub.** Replacing an asset changes its checksum. Users who
  installed the old one get a checksum error.
- **Every file needs its `.meta`.** Unity generates them; commit them. The build fails if one
  is missing or if a `.meta` has no file.

## What goes into a zip

The contents of the package folder, with `package.json` at the top. Excluded:

- Folders named `Tests` (set in `vpm.json`, under `excludeFolders`) and their `.meta` files.
- Hidden files and folders (starting with `.`), names ending in `~`, and `Zone.Identifier`
  files.

The zip is reproducible: the same files always produce the same zip and checksum.

## Troubleshooting

| Problem | Fix |
|---|---|
| `CHANGELOG entry … must have a release date` | Change `— unreleased` to `— YYYY-MM-DD` in the package's `CHANGELOG.md`. |
| `has no '## x.y.z' entry` | Add a changelog entry for the version in `package.json`. |
| `missing .meta files` | Open the dev project in Unity so it generates them, then commit them. |
| `… already exists. Bump the version` | That version was released before. Bump the version. |
| `Releases are made from master` | Run the workflow with the branch set to `master`. |
| `… is published twice` in the listing build | Two releases contain the same package version. Delete the wrong release. |
| Listing deploy fails with a branch protection error | **Settings → Environments → github-pages**: allow deployments from `master`. |
| Release succeeded, listing not updated | Run **Actions → Build VCC listing → Run workflow** by hand. |
| VCC doesn't show the new version | Restart VCC. For prereleases, turn on **Show Pre-release Packages**. |
| `https://vpm.nordicwind.app` doesn't load | Check the DNS record and the custom domain under **Settings → Pages**. |

## Previewing the listing locally

```bash
python3 Tools/Release/build_package.py --package app.nordicwind.avatar-tools --local
python3 Tools/Release/build_package.py --package app.nordicwind.texture-wrapper --local
python3 Tools/Release/build_listing.py --out site --local-zips dist
```

This writes `site/index.json` and `site/index.html` from the zips in `dist/`. The download URLs
in that preview point at local files. The real listing uses the GitHub release URLs.
