# Script to fix double namespaces created by the previous script

# Fix XAML.CS files
$directories = @("Config", "Job", "Data", "Tools", "Settings", "About")

foreach ($dir in $directories) {
    $path = "Views/Pages/$dir"
    if (Test-Path $path) {
        # Fix XAML.CS files - remove double namespace
        Get-ChildItem -Path $path -Filter '*.xaml.cs' | ForEach-Object {
            $content = Get-Content $_.FullName -Raw
            # Fix double namespace like OpenBullet2.Native.Views.Pages.Config.Config
            $content = $content -replace 'namespace OpenBullet2\.Native\.Views\.Pages\.([A-Z][a-z]+)\.\1', 'namespace OpenBullet2.Native.Views.Pages.$1'
            Set-Content $_.FullName $content
        }
    }
}

# Fix ViewModels
$viewModelDirs = @("Config", "Job", "Data", "Tools", "Settings", "Shared")

foreach ($dir in $viewModelDirs) {
    $path = "ViewModels/$dir"
    if (Test-Path $path) {
        Get-ChildItem -Path $path -Filter '*.cs' | ForEach-Object {
            $content = Get-Content $_.FullName -Raw
            # Fix double namespace like OpenBullet2.Native.ViewModels.Config.Config
            $content = $content -replace 'namespace OpenBullet2\.Native\.ViewModels\.([A-Z][a-z]+)\.\1', 'namespace OpenBullet2.Native.ViewModels.$1'
            Set-Content $_.FullName $content
        }
    }
}

Write-Host "Namespace fixes completed!"
