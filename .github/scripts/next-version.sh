#!/usr/bin/env bash
# Prints the next release version (without the v) from the commits since the last v* tag, or nothing when there is
# none to release. Conventional commit subjects decide the bump: a breaking change (`feat!:`, `fix(x)!:` or a
# BREAKING CHANGE footer) bumps the major, `feat:` the minor, anything else the patch. Before 1.0 a breaking change
# bumps the minor instead, as semver allows for 0.x.
set -euo pipefail

last=$(git describe --tags --abbrev=0 --match 'v[0-9]*' 2>/dev/null || true)
if [[ -z "$last" ]]; then
  range=HEAD
  last=v0.0.0
else
  range="$last..HEAD"
fi

subjects=$(git log --format=%s "$range")
[[ -z "$subjects" ]] && exit 0

IFS=. read -r major minor patch <<<"${last#v}"
if grep -qE '^[a-z]+(\([^)]*\))?!:' <<<"$subjects" || git log --format=%b "$range" | grep -q '^BREAKING CHANGE'; then
  if ((major == 0)); then minor=$((minor + 1)) patch=0; else major=$((major + 1)) minor=0 patch=0; fi
elif grep -qE '^feat(\([^)]*\))?:' <<<"$subjects"; then
  minor=$((minor + 1)) patch=0
else
  patch=$((patch + 1))
fi
echo "$major.$minor.$patch"
