#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the backend test suites with code coverage and fails if line coverage is below the
    threshold. Used both locally and by backend-ci.yml so the gate behaves identically in each.

.EXAMPLE
    ./scripts/Test-Coverage.ps1
    ./scripts/Test-Coverage.ps1 -Threshold 90 -Configuration Debug
#>
[CmdletBinding()]
param(
    [double]$Threshold = 80,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$backendRoot = Split-Path -Parent $PSScriptRoot
Push-Location $backendRoot
try {
    $resultsDir = Join-Path $backendRoot 'TestResults'
    $mergedReport = Join-Path $backendRoot 'coverage.cobertura.xml'

    if (Test-Path $resultsDir) { Remove-Item $resultsDir -Recurse -Force }
    if (Test-Path $mergedReport) { Remove-Item $mergedReport -Force }

    Write-Host 'Running tests with coverage...' -ForegroundColor Cyan
    dotnet test --configuration $Configuration -- `
        --coverage `
        --coverage-settings coverage.settings.xml `
        --coverage-output-format cobertura
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

    # Each test project emits its own report; they overlap on HackerNews.Api, so a plain sum
    # would undercount. Merging gives the real combined figure.
    dotnet tool restore | Out-Null
    $reports = @(Get-ChildItem -Path $resultsDir -Filter '*.cobertura.xml' -Recurse |
        Select-Object -ExpandProperty FullName)
    if ($reports.Count -eq 0) { throw "No coverage reports were produced under $resultsDir." }

    Write-Host "Merging $($reports.Count) coverage report(s)..." -ForegroundColor Cyan
    dotnet dotnet-coverage merge -o $mergedReport -f cobertura @reports | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Merging coverage reports failed.' }

    [xml]$coverage = Get-Content -Path $mergedReport
    $linePercent = [math]::Round([double]$coverage.coverage.'line-rate' * 100, 2)
    $branchPercent = [math]::Round([double]$coverage.coverage.'branch-rate' * 100, 2)
    $covered = $coverage.coverage.'lines-covered'
    $valid = $coverage.coverage.'lines-valid'

    Write-Host ''
    Write-Host "Line coverage:   $linePercent% ($covered/$valid)" -ForegroundColor Cyan
    Write-Host "Branch coverage: $branchPercent%" -ForegroundColor Cyan
    Write-Host "Report:          $mergedReport"

    if ($linePercent -lt $Threshold) {
        throw "Line coverage $linePercent% is below the required $Threshold%."
    }

    Write-Host "Coverage gate passed (threshold $Threshold%)." -ForegroundColor Green
}
finally {
    Pop-Location
}
