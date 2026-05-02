$csprojFile = Get-ChildItem -Path $PSScriptRoot -Filter "*.csproj" | Select-Object -First 1

if ($null -eq $csprojFile) {
    Write-Host "[Sync] ❌ The .csproj file was not found!" -ForegroundColor Red
    exit 1
}

$csprojPath = $csprojFile.FullName
Write-Host "[Sync] 🔍 Reading Metadata from: $($csprojFile.Name)" -ForegroundColor Cyan

[xml]$xml = Get-Content $csprojPath

$version = $xml.Project.PropertyGroup.Version
$description = $xml.Project.PropertyGroup.Description
$author = $xml.Project.PropertyGroup.Authors
$repoUrl = $xml.Project.PropertyGroup.PackageProjectUrl
$originalAuthor = $xml.Project.PropertyGroup.OriginalAuthor

if ([string]::IsNullOrWhiteSpace($version)) {
    Write-Host "[Sync] ❌ <Version> tag is missing in .csproj" -ForegroundColor Red
    exit 1
}

$targetFile = Get-ChildItem -Path $PSScriptRoot -Include "MyPluginInfo.cs", "PluginInfo.cs", "Plugin.cs" -Recurse | Select-Object -First 1

if ($null -eq $targetFile) {
    Write-Host "[Sync] ❌ Target C# file not found" -ForegroundColor Red
    exit 1
}

$targetPath = $targetFile.FullName
$content = Get-Content $targetPath -Raw
$newContent = $content

function Update-Metadata($regexName, $newValue, $currentContent) {
    if ([string]::IsNullOrWhiteSpace($newValue)) { return $currentContent }
    $pattern = "(public const string $regexName\s*=\s*`")(.+?)(`")"
    return [regex]::Replace($currentContent, $pattern, "${1}$newValue${3}")
}

$newContent = Update-Metadata "Version" $version $newContent
$newContent = Update-Metadata "Name" $description $newContent
$newContent = Update-Metadata "Author" $author $newContent
$newContent = Update-Metadata "RepoUrl" $repoUrl $newContent
$newContent = Update-Metadata "OriginalAuthor" $originalAuthor $newContent

if ($content -ne $newContent) {
    $newContent.TrimEnd() + [System.Environment]::NewLine | Set-Content $targetPath -NoNewline
    Write-Host "[Sync] ✅ Metadata updated successfully in $($targetFile.Name)!" -ForegroundColor Green
    Write-Host "[Sync] 🚀 New Version: $version | Author: $author" -ForegroundColor Cyan
} else {
    Write-Host "[Sync] ℹ️ Metadata is already up-to-date, no changes made." -ForegroundColor Gray
}