#!/bin/bash
# run-demo.sh — Start the Ellucian ERP Integration Engine demo

set -e

echo ""
echo "Starting Ellucian ERP Integration Engine demo..."
echo ""

# Check dotnet is installed
if ! command -v dotnet &> /dev/null; then
  echo "❌ .NET SDK not found."
  echo "   Install from: https://dotnet.microsoft.com/download"
  echo "   macOS:  brew install --cask dotnet-sdk"
  exit 1
fi

echo "✅ .NET SDK: $(dotnet --version)"

# Navigate to demo folder
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Restore packages
echo "📦 Restoring packages..."
dotnet restore --nologo -q

# Run
echo "🚀 Starting server at http://localhost:5000/swagger"
echo ""
dotnet run --no-restore
