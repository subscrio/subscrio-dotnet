# Run tests sequentially for each framework version to avoid connection pool exhaustion
# This script runs tests one framework at a time instead of in parallel
# Usage: .\run-tests-sequential.ps1

param(
    [switch]$Build = $true
)

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptPath

Write-Host "Running tests sequentially for each framework version..." -ForegroundColor Cyan
Write-Host "This prevents 'too many clients' errors by running one framework at a time" -ForegroundColor Gray
Write-Host ""

if ($Build) {
    Write-Host "Building test project..." -ForegroundColor Yellow
    dotnet build --no-restore | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed!" -ForegroundColor Red
        exit 1
    }
    Write-Host "Build succeeded" -ForegroundColor Green
    Write-Host ""
}

$frameworks = @("net8.0", "net9.0", "net10.0")
$totalFailed = 0
$totalPassed = 0
$results = @()

foreach ($framework in $frameworks) {
    Write-Host "========================================" -ForegroundColor Yellow
    Write-Host "Testing framework: $framework" -ForegroundColor Yellow
    Write-Host "========================================" -ForegroundColor Yellow
    Write-Host ""
    
    dotnet test -f $framework --no-build
    
    if ($LASTEXITCODE -ne 0) {
        $totalFailed++
        $results += "❌ $framework - FAILED"
        Write-Host "Tests failed for $framework" -ForegroundColor Red
    } else {
        $totalPassed++
        $results += "✅ $framework - PASSED"
        Write-Host "Tests passed for $framework" -ForegroundColor Green
    }
    
    Write-Host ""
    if ($framework -ne $frameworks[-1]) {
        Write-Host "Waiting 2 seconds before next framework..." -ForegroundColor Gray
        Start-Sleep -Seconds 2
        Write-Host ""
    }
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Test Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
foreach ($result in $results) {
    Write-Host $result
}
Write-Host ""
Write-Host "Frameworks passed: $totalPassed" -ForegroundColor Green
Write-Host "Frameworks failed: $totalFailed" -ForegroundColor $(if ($totalFailed -eq 0) { "Green" } else { "Red" })
Write-Host ""

if ($totalFailed -eq 0) {
    Write-Host "✅ All tests passed for all frameworks!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "❌ Some tests failed!" -ForegroundColor Red
    exit 1
}

