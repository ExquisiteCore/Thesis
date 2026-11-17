param([string]$ArtifactsDirectory = '.analysis/desktop-smoke')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = [IO.Path]::GetFullPath($ArtifactsDirectory, $root)
$app = Join-Path $artifacts 'app'
$evidence = Join-Path $artifacts ('run-' + [Guid]::NewGuid().ToString('N'))
dotnet publish (Join-Path $root 'src/Thesis.Gui/Thesis.Gui.csproj') -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:PublishTrimmed=false -p:PublishSingleFile=false -p:EnableUiSmokeTests=true -o $app -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Desktop test build failed.' }
$process = Start-Process -FilePath (Join-Path $app 'Thesis.Gui.exe') -ArgumentList @('--ui-smoke', ('"' + $evidence + '"')) -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(60000)) {
    [void]$process.CloseMainWindow()
    throw "Desktop smoke timed out. Evidence: $evidence"
}
$resultPath = Join-Path $evidence 'result.json'
if (-not (Test-Path -LiteralPath $resultPath)) { throw "Desktop exited before reporting a result. Evidence: $evidence" }
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
if (-not $result.passed) { throw $result.error }
Write-Output "Desktop smoke passed: $($result.steps.Count) checks. Evidence: $evidence"
