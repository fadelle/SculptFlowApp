<#
.SYNOPSIS
  Writes the AI prompts and tool descriptions of the n8n workflows into Omni.App/Resources/ai, so the C# bot uses the
  exact text n8n used (never retyped).

.DESCRIPTION
  Reads docs/n8n/omni-ai-assistant.json and docs/n8n/omni-retrieval-benchmark.json and writes:
    agent-system.txt                 AI Agent node, options.systemMessage
    formatter-system.txt             result_formatter node, the system message
    formatter-user.txt               result_formatter node, the prompt text
    benchmark-system.txt             benchmark AI Agent node, options.systemMessage
    tool.<name>.txt                  each AI tool node's toolDescription
    tool.<name>.parameters.json      the JSON schema of the arguments the AI fills ($fromAI calls), in order
  Every n8n expression in a prompt becomes a named placeholder {{name}} (table below); an expression not in the table
  stops the script, so a workflow change can't slip through unnoticed. AiPromptTemplate renders the placeholders with
  the values AiPromptValues builds the way n8n evaluated them (AiPromptTemplateTests checks both against the JSON).

  Run from the repository root:  powershell -File tools/extract-n8n-prompts.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'Omni.App/Resources/ai'
$utf8 = New-Object System.Text.UTF8Encoding($false)

# n8n expression (inside {{ }}, spaces trimmed) -> placeholder name.
$body = "`$('user_message').item.json.body"
$placeholders = [ordered]@{
    "$body.clientName"                                                      = 'clientName'
    "$body.bot.instructions"                                                = 'botInstructions'
    "$body.aiProfile"                                                       = 'aiProfile'
    "JSON.stringify($body.terms)"                                           = 'termsJson'
    "$body.language"                                                        = 'language'
    "$body.contactLanguage || 'not known yet'"                              = 'contactLanguage'
    "$body.timezone || 'UTC'"                                               = 'timezone'
    "`$now.setZone($body.timezone || 'UTC').toFormat('cccc, yyyy-MM-dd HH:mm')" = 'now'
    "$body.conversationId"                                                  = 'conversationId'
    "$body.messageText"                                                     = 'messageText'
    'JSON.stringify($json)'                                                 = 'resultJson'
}

function Read-Workflow([string]$file) {
    $text = [System.IO.File]::ReadAllText((Join-Path $root "docs/n8n/$file"), $utf8)
    return $text | ConvertFrom-Json
}

function Get-Node($workflow, [string]$name) {
    $node = @($workflow.nodes | Where-Object { $_.name -eq $name })
    if ($node.Count -ne 1) { throw "Node '$name' not found once in the workflow." }
    return $node[0]
}

# "=..." n8n text -> template with {{name}} placeholders.
function ConvertTo-Template([string]$text, [string]$what) {
    if ($text.StartsWith('=')) { $text = $text.Substring(1) }
    return [regex]::Replace($text, '\{\{(.*?)\}\}', {
        param($m)
        $expression = $m.Groups[1].Value.Trim()
        if (-not $placeholders.Contains($expression)) { throw "Unknown n8n expression in ${what}: {{ $expression }}" }
        return '{{' + $placeholders[$expression] + '}}'
    }, [System.Text.RegularExpressions.RegexOptions]::Singleline)
}

function Write-Resource([string]$name, [string]$text) {
    [System.IO.File]::WriteAllText((Join-Path $out $name), $text, $utf8)
    Write-Host "  $name ($($text.Length) chars)"
}

# Every string value of a node's parameters (URL, query values, JSON body), in document order.
function Get-Strings($value) {
    if ($null -eq $value) { return }
    if ($value -is [string]) { return $value }
    if ($value -is [System.Collections.IEnumerable]) { foreach ($item in $value) { Get-Strings $item }; return }
    foreach ($property in $value.PSObject.Properties) { Get-Strings $property.Value }
}

function ConvertTo-JsonString([string]$text) {
    $escaped = $text.Replace('\', '\\').Replace('"', '\"').Replace("`n", '\n').Replace("`r", '\r').Replace("`t", '\t')
    return '"' + $escaped + '"'
}

# $fromAI('name', 'description', 'type'[, default]) calls -> JSON schema text (a default makes the argument optional).
function Get-ToolParameters($node) {
    $pattern = "\`$fromAI\(\s*'([^']*)'\s*,\s*'([^']*)'\s*,\s*'(\w+)'\s*(?:,\s*'([^']*)'\s*)?\)"
    $properties = @()
    $required = @()
    foreach ($text in @(Get-Strings $node.parameters)) {
        foreach ($m in [regex]::Matches($text, $pattern)) {
            $name = $m.Groups[1].Value
            $property = "    $(ConvertTo-JsonString $name): { ""type"": $(ConvertTo-JsonString $m.Groups[3].Value), ""description"": $(ConvertTo-JsonString $m.Groups[2].Value)"
            if ($m.Groups[4].Success) { $property += ", ""default"": $(ConvertTo-JsonString $m.Groups[4].Value)" } else { $required += ConvertTo-JsonString $name }
            $properties += $property + ' }'
        }
    }
    $propertyText = if ($properties.Count -eq 0) { '{}' } else { "{`n" + ($properties -join ",`n") + "`n  }" }
    return "{`n  ""type"": ""object"",`n  ""properties"": $propertyText,`n  ""required"": [$($required -join ', ')]`n}`n"
}

New-Item -ItemType Directory -Force $out | Out-Null
Write-Host "Writing $out"

$assistant = Read-Workflow 'omni-ai-assistant.json'
Write-Resource 'agent-system.txt' (ConvertTo-Template (Get-Node $assistant 'AI Agent').parameters.options.systemMessage 'AI Agent')
$formatter = (Get-Node $assistant 'result_formatter').parameters
Write-Resource 'formatter-system.txt' (ConvertTo-Template $formatter.messages.messageValues[0].message 'result_formatter system')
Write-Resource 'formatter-user.txt' (ConvertTo-Template $formatter.text 'result_formatter text')

foreach ($tool in @($assistant.nodes | Where-Object { $_.type -eq 'n8n-nodes-base.httpRequestTool' } | Sort-Object name)) {
    Write-Resource "tool.$($tool.name).txt" $tool.parameters.toolDescription
    Write-Resource "tool.$($tool.name).parameters.json" (Get-ToolParameters $tool)
}

$benchmark = Read-Workflow 'omni-retrieval-benchmark.json'
Write-Resource 'benchmark-system.txt' (ConvertTo-Template (Get-Node $benchmark 'AI Agent').parameters.options.systemMessage 'benchmark AI Agent')
