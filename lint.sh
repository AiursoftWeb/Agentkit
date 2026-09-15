#!/bin/bash
set -euo pipefail

# Security baseline checks.
security_failed=0

check_security_baseline() {
    local description="$1"
    local pattern="$2"
    local matches
    matches=$(grep -RInE --include='*.cs' "$pattern" src || true)
    if [ -n "$matches" ]; then
        echo "$description"
        echo "$matches"
        security_failed=1
    fi
}

check_security_baseline "Unbounded request size is forbidden." 'DisableRequestSizeLimit'
check_security_baseline "Unbounded multipart bodies are forbidden." 'MultipartBodyLengthLimit[[:space:]]*=[[:space:]]*long\.MaxValue'
check_security_baseline "Generic file responses must not hard-code inline delivery." 'Content-Disposition.*inline'
check_security_baseline "Generic WebFile must remain attachment-only." 'bool[[:space:]]+inline'
check_security_baseline "Physical path boundaries must not use string prefix checks." 'physicalPath\.StartsWith\(root'
check_security_baseline "OIDC subjects must not bind through preferred_username." 'FindByNameAsync\(username\)'

if [ "$security_failed" -ne 0 ]; then
    exit 1
fi

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export PATH="$PATH:$HOME/.dotnet/tools"

RED='\033[0;31m'
GREEN='\033[0;32m'
NC='\033[0m'

# Keep the existing formatting gate and remove generated inspection output when
# running locally. GitLab retains the XML report as a lint artifact.
if [ -z "${CI:-}" ]; then
    trap 'rm -f analyze_output.xml' EXIT
fi

dotnet format Aiursoft.AgentKit.sln --verify-no-changes

echo "Checking for JetBrains ReSharper Global Tools..."
if ! command -v jb &> /dev/null; then
    echo "jb not found, installing..."
    dotnet tool install JetBrains.ReSharper.GlobalTools --global --add-source https://nuget.aiursoft.com/v3/index.json --configfile ./nuget.config -v d
else
    echo "jb is already installed."
fi

echo "Restoring dependencies..."
dotnet restore --no-cache --configfile nuget.config Aiursoft.AgentKit.sln || \
    (echo "Restore failed. Retrying in 10 seconds..." && sleep 10 && dotnet restore --no-cache --configfile nuget.config Aiursoft.AgentKit.sln)

echo "Running ReSharper Code Inspection..."
if ! jb inspectcode Aiursoft.AgentKit.sln --output=analyze_output.xml --build -f=xml &> /dev/null; then
    echo "First attempt failed, retrying..."
    if ! jb inspectcode Aiursoft.AgentKit.sln --output=analyze_output.xml --build -f=xml &> /dev/null; then
        echo "Second attempt failed, retrying..."
        jb inspectcode Aiursoft.AgentKit.sln --output=analyze_output.xml --build -f=xml &> /dev/null
    fi
fi

echo "Filtering known false positives..."
sed -i '/InconsistentNaming/d' analyze_output.xml
sed -i '/AssignNullToNotNullAttribute/d' analyze_output.xml
sed -i '/UnusedAutoPropertyAccessor/d' analyze_output.xml
sed -i '/DuplicateResource/d' analyze_output.xml
sed -i '/NotOverriddenInSpecificCulture/d' analyze_output.xml

if grep -q 'WARNING' analyze_output.xml; then
    echo -e "${RED}Linting FAILED!${NC}"
    echo "Issues found:"

    warning_ids=$(grep -E 'Severity="(WARNING|ERROR)"' analyze_output.xml | grep -o 'Id="[^"]*"' | cut -d'"' -f2 || true)
    for id in $warning_ids; do
        grep "TypeId=\"$id\"" analyze_output.xml | while read -r line; do
            file=$(echo "$line" | grep -o 'File="[^"]*"' | cut -d'"' -f2 | sed 's/\\\\/\//g')
            line_number=$(echo "$line" | grep -o 'Line="[^"]*"' | cut -d'"' -f2)
            message=$(echo "$line" | grep -o 'Message="[^"]*"' | cut -d'"' -f2)
            echo "File: $file | Line: ${line_number:-Unknown} | Reason: $message"
        done
    done

    exit 1
fi

echo -e "${GREEN}Linting PASSED! No warnings found.${NC}"
