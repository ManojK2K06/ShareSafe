param(
    [string]$Python = 'python',
    [switch]$SkipDependencies,
    [switch]$UseRuntimePackages,
    [string]$OfflineDependencyPath,
    [switch]$Package
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$venvPython = Join-Path $repo '.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $venvPython)) {
    $venvArgs = @('-m', 'venv')
    if ($UseRuntimePackages) { $venvArgs += '--system-site-packages' }
    if ($SkipDependencies) { $venvArgs += '--without-pip' }
    & $Python @venvArgs (Join-Path $repo '.venv')
    if ($LASTEXITCODE -ne 0) { throw 'Python 3.11+ is required to create the engine environment.' }
}
if ($OfflineDependencyPath) {
    $offline = (Resolve-Path -LiteralPath $OfflineDependencyPath).Path
    $packages = Join-Path $repo '.venv\Lib\site-packages'
    Get-ChildItem -LiteralPath $offline | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $packages -Recurse -Force }
}
if (-not $SkipDependencies) {
    & $venvPython -m pip install -r (Join-Path $repo 'backend\requirements.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed. Supply offline dependencies or restore package access.' }
}
& $venvPython -c 'import sys,PIL,docx,pymupdf; assert sys.version_info >= (3,11); print("Engine dependencies available")'
if ($LASTEXITCODE -ne 0) { throw 'Engine dependencies are incomplete.' }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 and the Windows C# compiler are required.' }
$destination = Join-Path $repo 'WindowsApp\bin'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $repo 'WindowsApp') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.Net.Http.dll','System.Security.dll','WindowsBase.dll')
$refs = @($references | ForEach-Object {
    $assembly = Join-Path $framework $_
    if (-not (Test-Path -LiteralPath $assembly)) { $assembly = Join-Path $framework ('WPF\' + $_) }
    '/reference:' + $assembly
})
$refs += '/reference:' + (Join-Path $framework 'WPF\UIAutomationClient.dll')
$refs += '/reference:' + (Join-Path $framework 'WPF\UIAutomationTypes.dll')
& $compiler /nologo /target:winexe /platform:x64 /optimize+ ('/out:' + (Join-Path $destination 'ShareSafe.Windows.exe')) @refs @sources
if ($LASTEXITCODE -ne 0) { throw 'The Windows application did not compile.' }
Copy-Item -LiteralPath (Join-Path $repo 'WindowsApp\ShareSafe.exe.config') -Destination (Join-Path $destination 'ShareSafe.Windows.exe.config') -Force
if ($Package) {
    & $venvPython -m pip install 'pyinstaller>=6,<7'
    if ($LASTEXITCODE -ne 0) { throw 'PyInstaller installation failed.' }
    & $venvPython -m PyInstaller --noconfirm --clean --onedir --name sharesafe-engine --paths (Join-Path $repo 'backend') --collect-all pymupdf --collect-all PIL --collect-all docx --distpath (Join-Path $repo 'build\windows\engine-dist') --workpath (Join-Path $repo 'build\windows\engine-work') --specpath (Join-Path $repo 'build\windows') (Join-Path $repo 'backend\bridge_entry.py')
    if ($LASTEXITCODE -ne 0) { throw 'Engine packaging failed.' }
    $bundle = Join-Path $repo 'build\windows\ShareSafe-Windows'
    New-Item -ItemType Directory -Path (Join-Path $bundle 'WindowsApp\bin') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $bundle 'build\windows') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo 'build\windows\engine-dist\sharesafe-engine') -Destination (Join-Path $bundle 'build\windows\engine') -Recurse -Force
    Copy-Item -Path (Join-Path $destination '*') -Destination (Join-Path $bundle 'WindowsApp\bin') -Force
    foreach ($item in @('BrowserExtension','sandbox','scripts')) { Copy-Item -LiteralPath (Join-Path $repo $item) -Destination $bundle -Recurse -Force }
    Copy-Item -LiteralPath (Join-Path $repo 'Start-ShareSafe.cmd') -Destination $bundle -Force
    Compress-Archive -Path (Join-Path $bundle '*') -DestinationPath (Join-Path $repo 'build\windows\ShareSafe-Windows-x64.zip') -Force
}
Write-Host ('Windows app built: ' + (Join-Path $destination 'ShareSafe.Windows.exe'))
