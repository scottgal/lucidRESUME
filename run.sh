#!/bin/sh
set -eu

repo_dir="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
cd "$repo_dir"

configuration=Release
case "${1:-}" in
    Debug|Release)
        configuration="$1"
        shift
        ;;
    ""|-*) ;;
    *)
        echo "Usage: $0 [Release|Debug] [--] [app arguments...]" >&2
        exit 2
        ;;
esac
if [ "${1:-}" = "--" ]; then shift; fi

exec dotnet run --project "$repo_dir/src/lucidRESUME/lucidRESUME.csproj" \
    --configuration "$configuration" -- "$@"
