$ErrorActionPreference = 'Stop'

$packageName = 'pisum-whisper'
$softwareName = 'Pisum Whisper*'

# Uninstalling removes the application and its Start-menu shortcut. It deliberately leaves
# %USERPROFILE%\.pisum-whisper.json and %USERPROFILE%\.pisum-whisper\logs\ alone: they hold the API
# keys and presets the user entered, and a reinstall is not a request to discard them.
[array]$key = Get-UninstallRegistryKey -SoftwareName $softwareName

if ($key.Count -eq 1) {
  $key | ForEach-Object {
    $packageArgs = @{
      packageName    = $packageName
      fileType       = 'msi'
      silentArgs     = "$($_.PSChildName) /qn /norestart"
      validExitCodes = @(0, 3010, 1605, 1614, 1641)
      file           = ''
    }

    if ($_.UninstallString) {
      $packageArgs['file'] = "$($_.UninstallString)"
    }

    Uninstall-ChocolateyPackage @packageArgs
  }
} elseif ($key.Count -eq 0) {
  Write-Warning "$packageName has already been uninstalled by other means."
} elseif ($key.Count -gt 1) {
  Write-Warning "$($key.Count) matches found!"
  Write-Warning "To prevent accidental data loss, no programs will be uninstalled."
  Write-Warning "Please alert the package maintainer that the following keys were found:"
  $key | ForEach-Object { Write-Warning "- $($_.DisplayName)" }
}
