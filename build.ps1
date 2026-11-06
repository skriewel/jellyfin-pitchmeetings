# Builds the plugin and packs a ready-to-install folder + zip into .\dist
param(
    [string]$Version = "1.0.0.0"
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$proj = Join-Path $root "Jellyfin.Plugin.PitchMeetings\Jellyfin.Plugin.PitchMeetings.csproj"

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
$userDotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
if (Test-Path $userDotnet) { $dotnet = $userDotnet }

& $dotnet build $proj -c Release -nologo -p:AssemblyVersion=$Version -p:FileVersion=$Version
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$dist = Join-Path $root "dist"
$folder = Join-Path $dist "PitchMeetings_$Version"
if (Test-Path $folder) { Remove-Item $folder -Recurse -Force -Confirm:$false }
New-Item -ItemType Directory -Force $folder | Out-Null

Copy-Item (Join-Path $root "Jellyfin.Plugin.PitchMeetings\bin\Release\net10.0\Jellyfin.Plugin.PitchMeetings.dll") $folder

$meta = [ordered]@{
    category    = "General"
    changelog   = "First release: automatic links between Pitch Meetings and movies/series"
    description = "Shows the matching Pitch Meeting video on movie and series pages, and links each Pitch Meeting back to its movie."
    guid        = "263fd402-979d-4384-8f9c-889c416552bf"
    name        = "Pitch Meetings"
    overview    = "Links Pitch Meeting videos to their movies and shows"
    owner       = "local"
    targetAbi   = "12.0.0.0"
    timestamp   = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    version     = $Version
    status      = "Active"
    autoUpdate  = $false
    assemblies  = @("Jellyfin.Plugin.PitchMeetings.dll")
}
[IO.File]::WriteAllText((Join-Path $folder "meta.json"), ($meta | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))

$zip = Join-Path $dist "PitchMeetings_$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force -Confirm:$false }
Compress-Archive -Path (Join-Path $folder "*") -DestinationPath $zip
$md5 = (Get-FileHash $zip -Algorithm MD5).Hash
Write-Host "Done: $zip (MD5 $md5)"
