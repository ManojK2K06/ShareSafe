param([switch]$Restricted, [switch]$Docker)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$python = Join-Path $repo '.venv\Scripts\python.exe'
Push-Location $repo
try {
    $testTemporary = Join-Path $repo 'tests\tmp'
    New-Item -ItemType Directory -Path $testTemporary -Force | Out-Null
    $env:TEMP = $testTemporary
    $env:TMP = $testTemporary
    if ($Restricted) {
        & $python tests\windows_restricted.py
        if ($LASTEXITCODE -ne 0) { throw 'Restricted engine checks failed.' }
        Write-Host 'In-memory transport checks completed. Real TCP, native UI, browser and Docker acceptance remain separate.'
    } else {
        & $python tests\run_backend.py --output tests\results\windows-backend.json
        if ($LASTEXITCODE -ne 0) { throw 'Real loopback engine checks failed.' }
        $report = Join-Path $repo 'tests\results\windows-native.json'
        $native = Start-Process -FilePath (Join-Path $repo 'WindowsApp\bin\ShareSafe.Windows.exe') -ArgumentList @('--self-test', ('"' + $report + '"')) -WindowStyle Hidden -PassThru
        if (-not $native.WaitForExit(60000)) { throw 'Native self-test exceeded its deadline.' }
        if ($native.ExitCode -ne 0) { throw 'Native self-test failed; inspect its measured report.' }
    }
    if ($Docker) {
        $env:PYTHONPATH = Join-Path $repo 'backend'
        $env:SHARESAFE_DOCKER_TEST = '1'
        & $python tests\docker_isolation.py
        if ($LASTEXITCODE -ne 0) { throw 'Actual container isolation acceptance did not pass.' }
    }
} finally { Pop-Location }
