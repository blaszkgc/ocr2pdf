param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\app'))
$ErrorActionPreference = 'Stop'
dotnet publish (Join-Path $PSScriptRoot 'OCR2PDF\OCR2PDF.csproj') -c Release -r win-x64 --self-contained true -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Write-Host "Run $(Join-Path $OutputDirectory 'OCR2PDF.exe')"
