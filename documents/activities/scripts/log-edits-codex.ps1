# PostToolUse hook: logs files edited or written by Codex to edit-log.txt.
# Input: hook JSON on stdin ({ tool_name, tool_input, tool_response }).

$rawInput = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($rawInput)) { exit 0 }

try {
    $payload = $rawInput | ConvertFrom-Json
}
catch {
    exit 0
}

$files = [System.Collections.Generic.List[string]]::new()

if ($payload.tool_input.file_path) {
    $files.Add([string]$payload.tool_input.file_path)
}
if ($payload.tool_response.filePath) {
    $files.Add([string]$payload.tool_response.filePath)
}

# Codex supplies apply_patch input in "command". Extract every affected path.
$patch = $payload.tool_input.command
if (-not $patch) {
    $patch = $payload.tool_input.patch
}
if ($patch) {
    $pattern = '(?m)^\*\*\* (?:Add|Update|Delete) File: (.+?)\r?$|^\*\*\* Move to: (.+?)\r?$'
    foreach ($match in [regex]::Matches([string]$patch, $pattern)) {
        $file = if ($match.Groups[1].Success) {
            $match.Groups[1].Value.Trim()
        }
        else {
            $match.Groups[2].Value.Trim()
        }

        if ($file) {
            $files.Add($file)
        }
    }
}

$files = @($files | Select-Object -Unique)
if ($files.Count -eq 0) { exit 0 }

$logPath = Join-Path $PSScriptRoot 'edit-log.txt'
$timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
foreach ($file in $files) {
    $line = '{0}  {1}  {2}' -f $timestamp, $payload.tool_name, $file
    Add-Content -LiteralPath $logPath -Value $line
}

$fileList = $files -join ', '
@{
    systemMessage = "PostToolUse hook fired: $($payload.tool_name) -> $fileList (logged to .codex/hooks/edit-log.txt)"
} | ConvertTo-Json -Compress
