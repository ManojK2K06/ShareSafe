$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
& docker --host npipe:////./pipe/docker_engine info --format '{{.OSType}}' | ForEach-Object { if ($_ -ne 'linux') { throw 'Switch Docker Desktop to Linux containers.' } }
if ($LASTEXITCODE -ne 0) { throw 'Start Docker Desktop before building the sandbox.' }
& docker --host npipe:////./pipe/docker_engine build --tag sharesafe-agent:local (Join-Path $repo 'sandbox')
if ($LASTEXITCODE -ne 0) { throw 'Sandbox image build failed.' }
