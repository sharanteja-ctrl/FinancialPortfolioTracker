#!/bin/bash
# =============================================================
#  scripts/setup.sh
#  One-shot setup: install .NET SDK (macOS/Linux) and build project
# =============================================================
set -e

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC_DIR="$PROJECT_DIR/src"

echo ""
echo "╔══════════════════════════════════════════════╗"
echo "║   Financial Portfolio Tracker — Setup         ║"
echo "╚══════════════════════════════════════════════╝"
echo ""

# ── 1. Check/Install .NET 8 ──────────────────────────────────
if ! command -v dotnet &>/dev/null; then
  echo "▶ .NET SDK not found. Installing..."
  if [[ "$OSTYPE" == "darwin"* ]]; then
    # macOS – try Homebrew first
    if command -v brew &>/dev/null; then
      brew install --cask dotnet-sdk
    else
      echo "⚠  Homebrew not found. Downloading .NET installer..."
      curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 8.0
      export PATH="$HOME/.dotnet:$PATH"
    fi
  else
    # Linux
    curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 8.0
    export PATH="$HOME/.dotnet:$PATH"
  fi
else
  DOTNET_VER=$(dotnet --version)
  echo "✔ .NET SDK found: $DOTNET_VER"
fi

# ── 2. Restore NuGet packages ─────────────────────────────────
echo ""
echo "▶ Restoring NuGet packages..."
dotnet restore "$PROJECT_DIR/FinancialPortfolioTracker.sln"

# ── 3. Build ──────────────────────────────────────────────────
echo ""
echo "▶ Building solution..."
dotnet build "$PROJECT_DIR/FinancialPortfolioTracker.sln" --configuration Release --no-restore

# ── 4. Run Tests ─────────────────────────────────────────────
echo ""
echo "▶ Running unit tests..."
dotnet test "$PROJECT_DIR/tests/FinancialPortfolioTracker.Tests.csproj" \
  --configuration Release --no-build \
  --logger "console;verbosity=minimal"

echo ""
echo "✔ Setup complete!"
echo ""
echo "  To run the application:"
echo "    cd $SRC_DIR"
echo "    dotnet run"
echo ""
