# PostToolUse hook: logs every file Claude edits/writes to .claude/hooks/edit-log.txt
# and surfaces a visible systemMessage in the Claude Code UI.
# Input: hook JSON on stdin ({ tool_name, tool_input: { file_path }, tool_response })

$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json

$file = $payload.tool_input.file_path
if (-not $file) { $file = $payload.tool_response.filePath }
if (-not $file) { exit 0 }

$logPath = Join-Path $PSScriptRoot 'edit-log.txt'
$line = '{0}  {1}  {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $payload.tool_name, $file
Add-Content -Path $logPath -Value $line

# JSON on stdout controls hook behavior; systemMessage is shown to the user.
@{ systemMessage = "PostToolUse hook fired: $($payload.tool_name) -> $file (logged to .claude/hooks/edit-log.txt)" } | ConvertTo-Json -Compress
