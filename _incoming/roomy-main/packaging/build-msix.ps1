<#
  Builds dist\Roomy.msix for upload to the Microsoft Store.
  The Store signs the package itself, so no certificate is needed.

  Get the three identity values from Partner Center:
    Apps and games → Roomy → Product management → Product identity

  Example:
    ./packaging/build-msix.ps1 -IdentityName "12345krezio.Roomy" `
                               -Publisher "CN=ABCDEF12-3456-7890-ABCD-EF1234567890" `
                               -PublisherDisplayName "krezio" -Version 0.1.0
  Pass -DisplayName with the exact product name you reserved (it must match).
#>
param(
  [Parameter(Mandatory)] [string] $IdentityName,
  [Parameter(Mandatory)] [string] $Publisher,
  [Parameter(Mandatory)] [string] $PublisherDisplayName,
  [string] $DisplayName = "Roomy",
  [string] $Version = "0.1.5"
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root "dist"
$stage = Join-Path $dist "msix"
$tools = Join-Path $dist "tools"

# 1. Publish the app (self-contained single exe)
dotnet publish "$root\src\Roomy" -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "$dist\publish" | Out-Null

# 2. Stage package contents
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$stage\Assets" | Out-Null
Copy-Item "$dist\publish\Roomy.exe" $stage
$v = ([version]$Version)
$manifest = (Get-Content "$PSScriptRoot\AppxManifest.xml" -Raw) `
  -replace "{{IdentityName}}", $IdentityName `
  -replace "{{Publisher}}", $Publisher `
  -replace "{{PublisherDisplayName}}", $PublisherDisplayName `
  -replace "{{DisplayName}}", $DisplayName `
  -replace "{{Version}}", "$($v.Major).$($v.Minor).$([Math]::Max($v.Build,0)).0"
Set-Content -Path "$stage\AppxManifest.xml" -Value $manifest -Encoding UTF8
Copy-Item "$PSScriptRoot\Assets\*" "$stage\Assets"

# 3. Get makeappx from Microsoft's official SDK build-tools package (cached in dist\tools)
$makeappx = Get-ChildItem $tools -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -match "\\x64\\" } | Select-Object -First 1
if (-not $makeappx) {
  New-Item -ItemType Directory -Force $tools | Out-Null
  $zip = Join-Path $tools "buildtools.zip"
  Invoke-WebRequest "https://www.nuget.org/api/v2/package/Microsoft.Windows.SDK.BuildTools" -OutFile $zip
  Expand-Archive $zip (Join-Path $tools "buildtools") -Force
  $makeappx = Get-ChildItem $tools -Recurse -Filter makeappx.exe | Where-Object { $_.FullName -match "\\x64\\" } | Select-Object -First 1
}

# 4. Pack
& $makeappx.FullName pack /d $stage /p "$dist\Roomy.msix" /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }
Write-Host "Built $dist\Roomy.msix  ->  upload it in Partner Center under Packages."
