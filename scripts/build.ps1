param(
    [string]$Version,
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $Version) {
    [xml]$versionProps = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw
    $Version = [string]$versionProps.Project.PropertyGroup.Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be in major.minor.patch format, for example 1.0.1.' }
if ($Runtime -ne 'win-x64') { throw 'Only the verified win-x64 package is currently supported.' }
$outputDirectory = Join-Path $projectRoot "dist\v$Version\$Runtime"
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
& dotnet publish (Join-Path $projectRoot 'EndfieldQteHelper.csproj') -c Release -r $Runtime --self-contained true "-p:Version=$Version" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $outputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Build failed. Close a running copy of this version before overwriting its executable.' }
$executableName = "EndfieldQteHelper-v$Version.exe"
$executablePath = Join-Path $outputDirectory $executableName
$reportPath = Join-Path $outputDirectory 'verification.json'
$testProcess = Start-Process -FilePath $executablePath -ArgumentList @('--self-test', ('"' + $reportPath + '"')) -WindowStyle Hidden -PassThru -Wait
if ($testProcess.ExitCode -ne 0) { throw "Verification failed. Inspect $reportPath" }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $outputDirectory 'README.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Destination (Join-Path $outputDirectory 'CHANGELOG.md')
$packageAssets = Join-Path $outputDirectory 'assets'
New-Item -ItemType Directory -Force -Path $packageAssets | Out-Null
foreach ($asset in @('NOTICE.md', 'icon-prompt.txt', 'laevatain-pixel.png')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "assets\$asset") -Destination (Join-Path $packageAssets $asset)
}
$zipPath = Join-Path (Split-Path -Parent $outputDirectory) "EndfieldQteHelper-v$Version-$Runtime.zip"
$packageFiles = @($executablePath, (Join-Path $outputDirectory 'README.md'), (Join-Path $outputDirectory 'CHANGELOG.md'), $packageAssets)
Compress-Archive -LiteralPath $packageFiles -DestinationPath $zipPath -Force
$hashes = @($executablePath, $zipPath) | ForEach-Object { $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256; "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))" }
$hashes | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $outputDirectory) 'SHA256SUMS.txt') -Encoding ascii
Write-Output "Built and verified: $executablePath"
Write-Output "Release package: $zipPath"
