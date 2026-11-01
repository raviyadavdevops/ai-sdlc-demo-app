# Read Claude Code hook input from stdin
$ErrorActionPreference = "Stop"

try {
    $inputJson = [Console]::In.ReadToEnd()
    $event = $inputJson | ConvertFrom-Json -ErrorAction Stop

    $logDirectory = Join-Path $env:CLAUDE_PROJECT_DIR "logs"
    $logFile = Join-Path $logDirectory "ai-interactions.jsonl"

    if (-not (Test-Path $logDirectory)) {
        New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    }

    # Record metadata only. Never capture prompts, tool arguments or secrets.
    $entry = [ordered]@{
        timestamp = [DateTime]::UtcNow.ToString("o")
        event      = $event.hook_event_name
        session_id = $event.session_id
        agent_id   = $event.agent_id
        agent_type = $event.agent_type
        tool_name  = $event.tool_name
        source     = $event.source
        reason     = $event.reason
    }

    $jsonLine = $entry | ConvertTo-Json -Compress
    Add-Content -Path $logFile -Value $jsonLine -Encoding UTF8
}
catch {
    # Logging failures must not interrupt Claude Code.
    exit 0
}

exit 0