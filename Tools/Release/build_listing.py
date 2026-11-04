#!/usr/bin/env python3
"""Builds the VPM repository listing (index.json) and its landing page.

VCC reads index.json to find every package and version. Each version entry is that
version's package.json plus "url" (where to download the zip) and "zipSHA256" (its
checksum). The listing is rebuilt from all GitHub releases of this repository every time,
so it never drifts from what is actually published.

Used by .github/workflows/listing.yml. Locally, build a preview from zips on disk:

    python3 Tools/Release/build_listing.py --out site --local-zips dist
"""
import argparse
import hashlib
import html
import json
import os
import sys
import urllib.request
import zipfile
from io import BytesIO
from pathlib import Path

CONFIG = Path(__file__).resolve().with_name("vpm.json")


def fail(message):
    print(f"::error::{message}" if os.environ.get("GITHUB_ACTIONS") else f"error: {message}", file=sys.stderr)
    sys.exit(1)


def github(url, token, accept="application/vnd.github+json"):
    request = urllib.request.Request(url, headers={"Accept": accept, "User-Agent": "nordicwind-vpm-listing",
                                                   **({"Authorization": f"Bearer {token}"} if token else {})})
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read()


def released_zips(repository, token):
    """Yields (download_url, zip_bytes) for every .zip asset of every published release."""
    page = 1
    while True:
        releases = json.loads(github(f"https://api.github.com/repos/{repository}/releases?per_page=100&page={page}", token))
        if not releases:
            return
        for release in releases:
            if release.get("draft"):
                continue
            for asset in release.get("assets", []):
                if asset["name"].endswith(".zip"):
                    yield asset["browser_download_url"], github(asset["url"], token, "application/octet-stream")
        page += 1


def local_zips(folder):
    for path in sorted(Path(folder).glob("*.zip")):
        yield path.resolve().as_uri(), path.read_bytes()


def add_version(packages, known, url, data):
    with zipfile.ZipFile(BytesIO(data)) as archive:
        if "package.json" not in archive.namelist():
            print(f"skipping {url}: no package.json at the top of the zip", file=sys.stderr)
            return
        manifest = json.loads(archive.read("package.json").decode("utf-8-sig"))
    name, version = manifest.get("name"), manifest.get("version")
    if name not in known:
        print(f"skipping {url}: package '{name}' is not listed in vpm.json", file=sys.stderr)
        return
    versions = packages.setdefault(name, {"versions": {}})["versions"]
    if version in versions:
        fail(f"{name} {version} is published twice ({versions[version]['url']} and {url}). "
             "A version must never be republished; delete one of the releases.")
    versions[version] = {**manifest, "url": url, "zipSHA256": hashlib.sha256(data).hexdigest()}


def semver_key(version):
    """Sort key following SemVer precedence: 1.0.0-beta.2 < 1.0.0-beta.10 < 1.0.0."""
    core, _, pre = version.partition("-")
    parts = [int(p) if p.isdigit() else 0 for p in core.split(".")]
    if not pre:
        return parts, 1, []
    # Numeric identifiers sort before alphanumeric ones, and numerically among themselves.
    return parts, 0, [(0, int(p), "") if p.isdigit() else (1, 0, p) for p in pre.split(".")]


def landing_page(listing):
    add = "vcc://vpm/addRepo?url=" + urllib.request.quote(listing["url"], safe="")
    rows = []
    for name, entry in sorted(listing["packages"].items()):
        versions = sorted(entry["versions"], key=semver_key)
        latest = entry["versions"][versions[-1]]
        rows.append(f"<tr><td><strong>{html.escape(latest.get('displayName', name))}</strong><br><code>{html.escape(name)}</code></td>"
                    f"<td>{html.escape(latest.get('description', ''))}</td><td>{html.escape(', '.join(reversed(versions)))}</td></tr>")
    return f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{html.escape(listing['name'])} VCC Listing</title>
<style>
  :root {{ color-scheme: light dark; font-family: system-ui, sans-serif; }}
  body {{ max-width: 52rem; margin: 2rem auto; padding: 0 1rem; line-height: 1.5; }}
  .button {{ display: inline-block; padding: .6rem 1.2rem; border-radius: .4rem; background: #2f6fde; color: #fff; text-decoration: none; font-weight: 600; }}
  table {{ width: 100%; border-collapse: collapse; margin-top: 1.5rem; }}
  td, th {{ text-align: left; vertical-align: top; padding: .5rem; border-bottom: 1px solid #8884; }}
  code {{ font-size: .9em; }}
</style>
</head>
<body>
<h1>{html.escape(listing['name'])} VCC Listing</h1>
<p>Add this listing to the VRChat Creator Companion (VCC) to install and update {html.escape(listing['name'])} packages.</p>
<p><a class="button" href="{html.escape(add)}">Add to VCC</a></p>
<p>Or in VCC, open <strong>Settings → Packages → Add Repository</strong> and paste:<br><code>{html.escape(listing['url'])}</code></p>
<table>
<tr><th>Package</th><th>Description</th><th>Versions</th></tr>
{''.join(rows)}
</table>
<p><a href="{html.escape(listing.get('infoLink', '#'))}">Source code and documentation</a></p>
</body>
</html>
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", default="site", help="output folder for index.json and index.html")
    parser.add_argument("--local-zips", help="build from zips in this folder instead of GitHub releases (preview only)")
    args = parser.parse_args()

    config = json.loads(CONFIG.read_text(encoding="utf-8"))
    listing = {**config["listing"], "packages": {}}
    known = set(config["packages"])
    if args.local_zips:
        source = local_zips(args.local_zips)
    else:
        repository = os.environ.get("GITHUB_REPOSITORY") or fail("GITHUB_REPOSITORY is not set")
        source = released_zips(repository, os.environ.get("GITHUB_TOKEN"))
    for url, data in source:
        add_version(listing["packages"], known, url, data)

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    (out / "index.json").write_text(json.dumps(listing, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    (out / "index.html").write_text(landing_page(listing), encoding="utf-8")
    count = sum(len(p["versions"]) for p in listing["packages"].values())
    print(f"listing: {len(listing['packages'])} package(s), {count} version(s) -> {out / 'index.json'}")


if __name__ == "__main__":
    main()
