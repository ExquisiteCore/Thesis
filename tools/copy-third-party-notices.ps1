param(
    [Parameter(Mandatory)][string]$AssetsFile,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$RuntimeIdentifier = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$assets = Get-Content -LiteralPath $AssetsFile -Raw | ConvertFrom-Json -AsHashtable
$destination = Join-Path $OutputDirectory 'licenses'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -Path (Join-Path $root 'licenses/*.txt') -Destination $destination
$index = [Collections.Generic.List[string]]::new()
$index.Add('Third-party dependencies and licenses')
$index.Add('Original license and notice files are included in the licenses directory.')
$index.Add('This inventory also includes build-time packages; their presence does not imply that every component is shipped or used.')
$index.Add('Markdig: licenses/Markdig.txt; PdfPig: licenses/PdfPig.txt; Open XML SDK: licenses/OpenXml.txt.')
function Copy-PackageNotices([string]$Package, [string]$Directory) {
    $target = Join-Path $destination ($Package -replace '[/\\]', '-')
    $files = @(Get-ChildItem -LiteralPath $Directory -File | Where-Object Name -Match 'license|notice')
    if ($files.Count -gt 0) {
        New-Item -ItemType Directory -Force -Path $target | Out-Null
        $files | Copy-Item -Destination $target
    }
}
foreach ($key in ($assets.libraries.Keys | Sort-Object)) {
    $library = $assets.libraries[$key]
    if ($library.type -ne 'package') { continue }
    $package = $null
    foreach ($folder in $assets.packageFolders.Keys) {
        $candidate = Join-Path $folder $library.path
        if (Test-Path -LiteralPath $candidate) { $package = $candidate; break }
    }
    if (-not $package) { throw "Missing restored package: $key" }
    [xml]$nuspec = Get-Content -LiteralPath (Get-ChildItem -LiteralPath $package -Filter '*.nuspec' | Select-Object -First 1).FullName -Raw
    $metadata = $nuspec.package.metadata
    $index.Add("$key | $($metadata.license.InnerText) | $($metadata.licenseUrl)")
    Copy-PackageNotices $key $package
}
foreach ($framework in $assets.project.frameworks.Values) {
    foreach ($dependency in $framework.downloadDependencies) {
        if ($dependency.name -ne "Microsoft.NETCore.App.Runtime.$RuntimeIdentifier") { continue }
        $version = ($dependency.version.Trim('[', ']') -split ',')[0].Trim()
        foreach ($folder in $assets.packageFolders.Keys) {
            $runtime = Join-Path $folder ($dependency.name.ToLowerInvariant() + '/' + $version)
            if (Test-Path -LiteralPath $runtime) {
                Copy-PackageNotices ('Microsoft.NETCore.App/' + $version) $runtime
                $index.Add("Microsoft.NETCore.App/$version | See bundled runtime license and third-party notices.")
            }
        }
    }
}
$index | Set-Content -LiteralPath (Join-Path $OutputDirectory 'THIRD-PARTY-NOTICES.txt') -Encoding utf8
