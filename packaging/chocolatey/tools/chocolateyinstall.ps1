$ErrorActionPreference = 'Stop'

# The URL and the checksum are both rewritten by release.yml, from the asset it has just published
# and the SHA-256 it computed over exactly those bytes. A download that does not match is refused by
# Install-ChocolateyPackage rather than installed.
$packageArgs = @{
  packageName    = 'pisum-whisper'
  fileType       = 'msi'
  url64bit       = 'https://github.com/mschnecke/pisum-whisper/releases/download/v0.1.0/Pisum.Whisper_0.1.0_win-x64.msi'
  softwareName   = 'Pisum Whisper*'
  checksum64     = 'REPLACE_WITH_ACTUAL_CHECKSUM'
  checksumType64 = 'sha256'
  silentArgs     = '/qn /norestart'
  validExitCodes = @(0, 3010, 1641)
}

Install-ChocolateyPackage @packageArgs
