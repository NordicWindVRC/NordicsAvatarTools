#!/usr/bin/env python3
"""Validates one package and builds its VPM release zip.

Used by .github/workflows/release.yml, and runnable locally to check a package before
releasing:

    python3 Tools/Release/build_package.py --package app.nordicwind.avatar-tools --out dist --local

The zip contains the package folder's contents with package.json at the top, every .meta
file, and no test folders. It is reproducible: the same files always give the same zip and
the same SHA-256, which the VPM listing records.
"""
import argparse
import hashlib
import json
import os
import re
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CONFIG = Path(__file__).resolve().with_name("vpm.json")
SEMVER = re.compile(r"^(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?$")
FIXED_TIME = (1980, 1, 1, 0, 0, 0)  # zip timestamps, for reproducible archives


def fail(message):
    print(f"::error::{message}" if os.environ.get("GITHUB_ACTIONS") else f"error: {message}", file=sys.stderr)
    sys.exit(1)


def ignored(name):
    # Unity ignores hidden files and names ending in "~". Zone.Identifier files are
    # Windows download markers that WSL turns into real files.
    return name.startswith(".") or name.endswith("~") or "Zone.Identifier" in name


def package_files(package_dir, exclude_folders):
    """Every file to ship, relative to the package folder, in a stable order."""
    files = []
    for current, dirs, names in os.walk(package_dir):
        dirs[:] = sorted(d for d in dirs if not ignored(d) and d not in exclude_folders)
        for name in sorted(names):
            if ignored(name):
                continue
            path = Path(current) / name
            if path.suffix == ".meta" and path.with_suffix("").name in exclude_folders:
                continue  # the .meta of an excluded folder
            files.append(path.relative_to(package_dir))
    return files


def check_meta_files(package_dir, files):
    """Inside a package, every file and folder except the package root needs a .meta."""
    shipped = {p.as_posix() for p in files}
    missing = []
    for rel in files:
        if rel.suffix == ".meta":
            continue
        if f"{rel.as_posix()}.meta" not in shipped:
            missing.append(rel.as_posix())
        for parent in rel.parents:
            if parent != Path(".") and f"{parent.as_posix()}.meta" not in shipped and parent.as_posix() not in missing:
                missing.append(parent.as_posix())
    orphans = [p for p in shipped if p.endswith(".meta") and p[:-5] not in shipped
               and not any(s.startswith(p[:-5] + "/") for s in shipped)]
    if missing:
        fail("missing .meta files (open the package in Unity to generate them):\n  " + "\n  ".join(sorted(missing)))
    if orphans:
        fail(".meta files without a file or folder:\n  " + "\n  ".join(sorted(orphans)))


def changelog_section(package_dir, version, release):
    """Returns (heading, body) for the version's CHANGELOG entry."""
    changelog = package_dir / "CHANGELOG.md"
    if not changelog.exists():
        fail(f"{changelog.relative_to(ROOT)} is missing")
    lines = changelog.read_text(encoding="utf-8").splitlines()
    base = version.split("-")[0]
    candidates = [version] if not SEMVER.match(version).group(4) else [version, base]
    for i, line in enumerate(lines):
        match = re.match(r"^## +(\S+)\s*(.*)$", line)
        if match and match.group(1) in candidates:
            end = next((j for j in range(i + 1, len(lines)) if lines[j].startswith("## ")), len(lines))
            heading = line[3:].strip()
            if release and not SEMVER.match(version).group(4):
                if "unreleased" in heading.lower() or not re.search(r"\d{4}-\d{2}-\d{2}", heading):
                    fail(f"CHANGELOG entry '{heading}' must have a release date (## {version} — YYYY-MM-DD) "
                         "instead of 'unreleased' before a full release")
            return heading, "\n".join(lines[i + 1:end]).strip()
    fail(f"{changelog.relative_to(ROOT)} has no '## {version}' entry" +
         (f" (or '## {base}' for a prerelease)" if base != version else ""))


def build_zip(package_dir, files, out_path):
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(out_path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for rel in files:
            info = zipfile.ZipInfo(rel.as_posix(), FIXED_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            archive.writestr(info, (package_dir / rel).read_bytes())
    return hashlib.sha256(out_path.read_bytes()).hexdigest()


def main():
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8")  # changelog headings contain "—"
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--package", required=True, help="package name, for example app.nordicwind.avatar-tools")
    parser.add_argument("--out", default="dist", help="output folder for the zip and release notes")
    parser.add_argument("--local", action="store_true",
                        help="local check: skip the release-date requirement for full versions")
    args = parser.parse_args()

    config = json.loads(CONFIG.read_text(encoding="utf-8"))
    if args.package not in config["packages"]:
        fail(f"unknown package '{args.package}'. Known: {', '.join(config['packages'])}")
    package_dir = ROOT / config["packages"][args.package]
    manifest = json.loads((package_dir / "package.json").read_text(encoding="utf-8-sig"))
    if manifest.get("name") != args.package:
        fail(f"package.json name '{manifest.get('name')}' does not match '{args.package}'")
    version = manifest.get("version", "")
    if not SEMVER.match(version):
        fail(f"package.json version '{version}' is not SemVer (MAJOR.MINOR.PATCH or with -prerelease)")

    files = package_files(package_dir, set(config.get("excludeFolders", [])))
    if Path("package.json") not in files:
        fail("package.json must be at the top of the package")
    check_meta_files(package_dir, files)
    heading, notes = changelog_section(package_dir, version, release=not args.local)

    out = (ROOT / args.out).resolve()
    zip_path = out / f"{args.package}-{version}.zip"
    sha = build_zip(package_dir, files, zip_path)
    notes_path = out / f"{args.package}-{version}.md"
    notes_path.write_text(f"## {manifest.get('displayName', args.package)} {heading}\n\n{notes}\n", encoding="utf-8")

    prerelease = bool(SEMVER.match(version).group(4))
    tag = f"{args.package.split('.')[-1]}-v{version}"
    shown = zip_path.relative_to(ROOT) if zip_path.is_relative_to(ROOT) else zip_path
    print(f"{args.package} {version}: {len(files)} files -> {shown} (sha256 {sha})")

    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
            output.write(f"version={version}\ntag={tag}\nzip={zip_path}\nnotes={notes_path}\n"
                         f"prerelease={'true' if prerelease else 'false'}\n"
                         f"title={manifest.get('displayName', args.package)} {version}\n")


if __name__ == "__main__":
    main()
