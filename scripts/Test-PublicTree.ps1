[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$failures = [System.Collections.Generic.List[string]]::new()
$secretPatterns = [ordered]@{
    "OpenAI API key" = "sk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{20,}"
    "GitHub token" = "gh[pousr]_[A-Za-z0-9]{20,}"
    "AWS access key" = "AKIA[0-9A-Z]{16}"
    "Private key" = "-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"
}
$retiredIdentityPatterns = [ordered]@{
    "Retired product identity" = [Regex]::Escape(("je" + "berlo"))
    "Retired organization acronym" = "(?<![A-Za-z])" + [Regex]::Escape(("I" + "ID")) + "(?![A-Za-z])"
    "Personal author email" = [Regex]::Escape(("charis.chaliotis" + "@" + "gmail.com"))
}
$textExtensions = @(
    ".cs", ".json", ".md", ".ps1", ".py", ".sln", ".txt", ".xaml", ".xml", ".yml", ".yaml"
)

$publicFiles = @(
    git ls-files --cached --others --exclude-standard
) | Sort-Object -Unique

foreach ($path in $publicFiles) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        continue
    }

    foreach ($pattern in $retiredIdentityPatterns.GetEnumerator()) {
        if ($path -match $pattern.Value) {
            $failures.Add("$($pattern.Key) found in public path $path")
        }
    }

    if ([IO.Path]::GetExtension($path) -notin $textExtensions) {
        continue
    }

    $content = Get-Content -Raw -LiteralPath $path
    foreach ($pattern in $secretPatterns.GetEnumerator()) {
        if ($content -match $pattern.Value) {
            $failures.Add("$($pattern.Key) pattern found in $path")
        }
    }

    foreach ($pattern in $retiredIdentityPatterns.GetEnumerator()) {
        if ($content -match $pattern.Value) {
            $failures.Add("$($pattern.Key) found in $path")
        }
    }
}

$publicProfiles = @(Get-ChildItem -LiteralPath "Profiles" -Filter "*.json" -File)
if ($publicProfiles.Count -ne 1 -or $publicProfiles[0].Name -cne "Default.json") {
    $failures.Add("Profiles/ must contain only Default.json")
}

$publicProfiles | ForEach-Object {
    try {
        $profile = Get-Content -Raw -LiteralPath $_.FullName | ConvertFrom-Json
    }
    catch {
        $failures.Add("Invalid JSON profile: $($_.Name)")
        return
    }

    foreach ($propertyName in @("ImageFolderPath", "ExportFilePath")) {
        $value = [string]$profile.$propertyName
        if (-not [string]::IsNullOrWhiteSpace($value) -and [IO.Path]::IsPathFullyQualified($value)) {
            $failures.Add("Public profile $($_.Name) contains an absolute $propertyName")
        }
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output "Public-tree checks passed."
