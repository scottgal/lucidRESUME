#!/bin/bash
# AI Visual Test Review
# Runs the UX test, then checks each screenshot against its expected content.
# Uses screenshot filenames as the assertion (EXPECT-xxx describes what should be visible).
#
# Usage: ./tools/ai-visual-review.sh
#
# The screenshot names encode what the AI reviewer should verify:
#   ai-test-01-EXPECT-docx-preview-with-page-image-and-quality-score.png
#   → verify: page image visible, quality score visible
#
# This script outputs a checklist that can be fed to an LLM for automated review.

set -e

echo "🔍 Running AI Visual Test Suite..."
dotnet run --project src/lucidRESUME/lucidRESUME.csproj -- \
  --ux-test --script ux-scripts/ai-visual-test.yaml --output ux-screenshots 2>/dev/null

echo ""
echo "📸 Screenshots generated. Review checklist:"
echo "============================================"
echo ""

for f in ux-screenshots/ai-test-*.png; do
  name=$(basename "$f" .png)
  # Extract the EXPECT part from the filename
  expect=$(echo "$name" | sed 's/ai-test-[0-9]*-EXPECT-//' | tr '-' ' ')
  num=$(echo "$name" | grep -o 'test-[0-9]*' | tr -d 'test-')
  echo "  [$num] $expect"
  echo "       → $f"
  echo ""
done

echo "============================================"
echo "Feed these screenshots + descriptions to an LLM to verify each assertion."
echo "Or review manually: open ux-screenshots/ai-test-*.png"
