#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
  cat <<'USAGE'
Usage:
  scripts/validate-markdown-links.sh [FILE ...]

Validates relative Markdown links in tracked Markdown files, or in the supplied files.
External URLs, anchors, app links, and mailto links are ignored.
USAGE
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

cd "$ROOT_DIR"

files=()
if [[ $# -gt 0 ]]; then
  files=("$@")
else
  while IFS= read -r file; do
    files+=("$file")
  done < <(git ls-files '*.md')
  while IFS= read -r file; do
    files+=("$file")
  done < <(git ls-files --others --exclude-standard '*.md')
fi

missing=0

for file in "${files[@]}"; do
  [[ -f "$file" ]] || continue
  base_dir="$(dirname "$file")"
  while IFS= read -r link; do
    link="${link%%#*}"
    [[ -n "$link" ]] || continue
    case "$link" in
      http:*|https:*|mailto:*|app:*|plugin:*|mcp:*|\#* )
        continue
        ;;
    esac

    if [[ "$link" = /* ]]; then
      target="$link"
    else
      target="$base_dir/$link"
    fi

    if [[ ! -e "$target" ]]; then
      printf '%s: missing link target: %s\n' "$file" "$link" >&2
      missing=1
    fi
  done < <(perl -0777 -ne 'while (/\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g) { print "$1\n" }' "$file")
done

if [[ "$missing" -ne 0 ]]; then
  exit 1
fi

echo "Markdown links validated."
