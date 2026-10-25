#!/usr/bin/env bash
# Stop hook: keeps Claude from finishing while uncommitted src/ changes break two CLAUDE.md rules.
# Exit 2 sends the stderr text back to Claude, which then continues the turn.
input=$(cat)
# Already sent back once this turn: let it stop rather than loop.
echo "$input" | grep -q '"stop_hook_active" *: *true' && exit 0

cd "${CLAUDE_PROJECT_DIR:-.}" || exit 0
changed=$(git status --porcelain 2>/dev/null | cut -c4-)
echo "$changed" | grep -q '^src/' || exit 0

if ! echo "$changed" | grep -qx 'CHANGELOG.md'; then
  echo "src/ has uncommitted changes but CHANGELOG.md has no new entry: add one under ## [Unreleased] (Thai, เพิ่ม / เปลี่ยน / แก้ไข)." >&2
  exit 2
fi

if echo "$changed" | grep -q '\.vsct$' &&
   ! git diff HEAD -U0 -- src/SqlAssist.Vsix/SqlAssistPackage.cs | grep -q '^+.*ProvideMenuResource'; then
  echo "SqlAssistPackage.vsct changed but ProvideMenuResource in SqlAssistPackage.cs was not bumped." >&2
  exit 2
fi
exit 0
