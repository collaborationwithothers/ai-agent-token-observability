#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
  cat <<'USAGE'
Usage:
  scripts/session-digest.sh ISSUE_NUMBER
  scripts/session-digest.sh --no-issue

Prints the compact ready-for-agent issue packet for a new Codex session.
USAGE
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" || -z "${1:-}" ]]; then
  usage
  if [[ -z "${1:-}" ]]; then
    exit 2
  fi
  exit 0
fi

cd "$ROOT_DIR"
scripts/issue-start.sh --compact "$1"
