# Check for REAL Antivirus Installation Locations
Write-Host "Checking for actual antivirus installations..." -ForegroundColor Cyan

# Check COMODO
$comodoLocations = @(
    "C:\Program Files\COMODO",
    "C:\Program Files (x86)\COMODO",
    "${env:ProgramFiles}\COMODO\COMODO Internet Security",
    "${env:ProgramFiles(x86)}\COMODO\COMODO Internet Security"
)

Write-Host "`nCOMODO Antivirus:" -ForegroundColor Yellow
$comodoFound = $false
foreach ($path in $comodoLocations) {
    if (Test-Path $path) {
        Write-Host "  FOUND: $path" -ForegroundColor Red
        Get-ChildItem $path -Recurse -Filter "*.exe" | Select-Object -First 5 | ForEach-Object {
            Write-Host "    $($_.FullName)"
        }
        $comodoFound = $true
    }
}
if (-not $comodoFound) {
    Write-Host "  NOT INSTALLED (phantom detection)" -ForegroundColor Green
}

# Check Kaspersky
$kasperskyLocations = @(
    "C:\Program Files\Kaspersky Lab",
    "C:\Program Files (x86)\Kaspersky Lab",
    "C:\ProgramData\Kaspersky Lab"
)

Write-Host "`nKaspersky Antivirus:" -ForegroundColor Yellow
$kasperskyFound = $false
foreach ($path in $kasperskyLocations) {
    if (Test-Path $path) {
        Write-Host "  FOUND: $path" -ForegroundColor Red
        Get-ChildItem $path -Recurse -Filter "*.exe" | Select-Object -First 5 | ForEach-Object {
            Write-Host "    $($_.FullName)"
        }
        $kasperskyFound = $true
    }
}
if (-not $kasperskyFound) {
    Write-Host "  NOT INSTALLED (phantom detection)" -ForegroundColor Green
}

# Check all running antivirus processes
Write-Host "`nRunning Antivirus Processes:" -ForegroundColor Yellow
$avProcesses = Get-Process | Where-Object {
    $_.ProcessName -match "comodo|kaspersky|avast|avg|norton|mcafee|bitdefender|avira|eset|sophos|trend"
}
if ($avProcesses) {
    $avProcesses | ForEach-Object {
        Write-Host "  $($_.ProcessName) - PID: $($_.Id) - Path: $($_.Path)"
    }
} else {
    Write-Host "  No third-party antivirus processes running" -ForegroundColor Green
}

Write-Host "`nConclusion:" -ForegroundColor Cyan
if (-not $comodoFound -and -not $kasperskyFound -and -not $avProcesses) {
    Write-Host "  Antivirus detection was FALSE POSITIVE from SecurityCenter2" -ForegroundColor Green
    Write-Host "  The issue is NOT antivirus!" -ForegroundColor Green
} else {
    Write-Host "  Real antivirus software is installed" -ForegroundColor Red
}
