param([switch]$RemoveShortcut)
$ErrorActionPreference = 'Stop'
$desktop = [Environment]::GetFolderPath('Desktop')
$link = Join-Path $desktop 'Thesis 论文工作台.lnk'
if ($RemoveShortcut) {
    if (Test-Path -LiteralPath $link) { Remove-Item -LiteralPath $link }
    Write-Output 'Desktop shortcut removed. Project files are retained.'
    return
}
$executable = Join-Path $PSScriptRoot 'Thesis.Gui.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Run this script from the extracted desktop package.' }
$shell = New-Object -ComObject WScript.Shell
try {
    $shortcut = $shell.CreateShortcut($link)
    $shortcut.TargetPath = $executable
    $shortcut.WorkingDirectory = $PSScriptRoot
    $shortcut.Description = 'Thesis 论文工作台'
    $shortcut.Save()
} finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
Write-Output "Shortcut created: $link"
