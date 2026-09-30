#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mostlylucid_root="${MOSTLYLUCID_ROOT:-$repo_root/../mostlylucidweb}"

if [[ ! -f "$mostlylucid_root/Mostlylucid/Mostlylucid.csproj" ]]; then
    printf 'Mostlylucid checkout not found: %s\n' "$mostlylucid_root" >&2
    exit 1
fi

export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=http://127.0.0.1:5098
export LucidResumeCompiler__PublicBaseUri=http://127.0.0.1:8080/resume
export Ollama__CompositionEnabled=true
export Ollama__BaseUrl="${Ollama__BaseUrl:-http://127.0.0.1:11435}"
export Ollama__Model="${Ollama__Model:-qwen3.5:latest}"
dotnet run --project "$repo_root/samples/lucidRESUME.Web.Sample" --no-launch-profile --no-restore &
compiler_pid=$!

cleanup() {
    kill "$compiler_pid" 2>/dev/null || true
    wait "$compiler_pid" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

export ASPNETCORE_URLS=http://127.0.0.1:8080
export Analytics__Password=local-dev-placeholder
export Auth__GoogleClientId=local-dev-placeholder
export Auth__GoogleClientSecret=local-dev-placeholder
export Blog__Mode=File
export SemanticSearch__Enabled=false
export TranslateService__Enabled=false
export LucidResumeProxy__UpstreamUri=http://127.0.0.1:5098/
export LucidResumeProxy__AllowLoopbackWriter=true

cd "$mostlylucid_root"
dotnet run --project Mostlylucid/Mostlylucid.csproj --no-launch-profile --no-restore
