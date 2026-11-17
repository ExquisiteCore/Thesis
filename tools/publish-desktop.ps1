param(
    [string]$OutputDirectory = "artifacts/thesis-desktop-win-x64",
    [switch]$Archive
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$destination = [IO.Path]::GetFullPath($OutputDirectory, $root)
dotnet publish (Join-Path $root 'src/Thesis.Gui/Thesis.Gui.csproj') -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $destination
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $destination
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $destination
Copy-Item -LiteralPath (Join-Path $root 'tools/install-desktop.ps1') -Destination $destination
& (Join-Path $PSScriptRoot 'copy-third-party-notices.ps1') -AssetsFile (Join-Path $root 'src/Thesis.Gui/obj/project.assets.json') -OutputDirectory $destination
$manifest = Get-ChildItem -LiteralPath $destination -File -Recurse | Where-Object Name -ne 'checksums.json' | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($destination, $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destination 'checksums.json') -Encoding utf8
if ($Archive) { Compress-Archive -Path (Join-Path $destination '*') -DestinationPath ($destination + '.zip') -Force }
Write-Output "Desktop package: $destination"
