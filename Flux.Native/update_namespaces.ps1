# Script to update namespaces in XAML and code-behind files

# Update XAML files
$directories = @("Config", "Job", "Data", "Tools", "Settings", "About")

foreach ($dir in $directories) {
    $path = "Views/Pages/$dir"
    if (Test-Path $path) {
        # Update XAML files
        Get-ChildItem -Path $path -Filter '*.xaml' | ForEach-Object {
            $fileName = [System.IO.Path]::GetFileNameWithoutExtension($_.Name)
            $newClass = "Flux.Native.Views.Pages.$dir.$fileName"
            $content = Get-Content $_.FullName -Raw
            $content = $content -replace 'x:Class="Flux\.Native\.Views\.Pages\.[^"]+"', "x:Class=""$newClass"""
            Set-Content $_.FullName $content
        }

        # Update XAML.CS files
        Get-ChildItem -Path $path -Filter '*.xaml.cs' | ForEach-Object {
            $newNamespace = "Flux.Native.Views.Pages.$dir"
            $content = Get-Content $_.FullName -Raw
            $content = $content -replace 'namespace Flux\.Native\.Views\.Pages', "namespace $newNamespace"
            Set-Content $_.FullName $content
        }
    }
}

# Update ViewModels
$viewModelDirs = @("Config", "Job", "Data", "Tools", "Settings", "Shared")

foreach ($dir in $viewModelDirs) {
    $path = "ViewModels/$dir"
    if (Test-Path $path) {
        Get-ChildItem -Path $path -Filter '*.cs' | ForEach-Object {
            $newNamespace = "Flux.Native.ViewModels.$dir"
            $content = Get-Content $_.FullName -Raw
            $content = $content -replace 'namespace Flux\.Native\.ViewModels', "namespace $newNamespace"
            Set-Content $_.FullName $content
        }
    }
}

Write-Host "Namespace updates completed!"
