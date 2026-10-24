[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [switch]$SkipInstallerBuild
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$projectFile = Join-Path $root 'SynToolkit\SynToolkit.csproj'
[xml]$projectXml = Get-Content -Raw -LiteralPath $projectFile
$applicationVersion = @($projectXml.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if ([string]::IsNullOrWhiteSpace($applicationVersion)) {
    throw 'The SynToolkit version could not be read from the project file.'
}

$releaseDirectory = Join-Path $root 'Release'
$sourceArchive = Join-Path $releaseDirectory "SynToolkit-Source-$applicationVersion.zip"
$setupSource = Join-Path $root "Installer\Output\SynToolkit-Setup-$applicationVersion-Beta.exe"
$setupDestination = Join-Path $releaseDirectory "SynToolkit-Setup-$applicationVersion.exe"

if (-not $SkipInstallerBuild) {
    & (Join-Path $root 'build-installer.ps1') -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Installer build failed with exit code $LASTEXITCODE."
    }
}
elseif (-not (Test-Path -LiteralPath $setupSource -PathType Leaf)) {
    throw "The existing installer was not found: $setupSource"
}

$rootPath = [System.IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
$releasePath = [System.IO.Path]::GetFullPath($releaseDirectory)
if (-not $releasePath.StartsWith($rootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean a release directory outside the project: $releasePath"
}
if (Test-Path -LiteralPath $releasePath) {
    Remove-Item -LiteralPath $releasePath -Recurse -Force
}
New-Item -ItemType Directory -Path $releasePath -Force | Out-Null

Push-Location $root
try {
    & tar.exe -a -c -f $sourceArchive `
        --exclude=.git `
        --exclude=.vs `
        --exclude=.dotnet-cli `
        --exclude=artifacts `
        --exclude=Release `
        --exclude=SynToolkit/bin `
        --exclude=SynToolkit/obj `
        --exclude=SynToolkit.SystemInformationTests/bin `
        --exclude=SynToolkit.SystemInformationTests/obj `
        --exclude=Installer/Output `
        .github SynToolkit SynToolkit.SystemInformationTests Installer tools build-installer.ps1 build-release.ps1 SynToolkit.sln LICENSE README.md .gitattributes .gitignore
    if ($LASTEXITCODE -ne 0) {
        throw "Source archive creation failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

Copy-Item -LiteralPath $setupSource -Destination $setupDestination -Force

Write-Host "Source:    $sourceArchive"
Write-Host "Installer: $setupDestination"
