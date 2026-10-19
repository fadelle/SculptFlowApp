<#
.SYNOPSIS
  Tests copy-from-old.sql end to end on a throwaway local PostgreSQL cluster (nothing touches Supabase).

.DESCRIPTION
  1. Creates a fresh cluster (own port) in a temp folder.
  2. Database "old": the OLD project's schema + seeds taken from git (-OldRef, default main) + the old demo seed +
     the old admin-schema.sql from the portal repo + test-old-extras.sql (the cases the demo data lacks).
  3. Database "new": this branch's Database/schema.sql + seed-config.sql.
  4. Runs copy-from-old.sql (postgres_fdw to "old"), then test-copy-assert.sql: row counts, every mapping, users,
     admins, settings, sequences.
  5. Refusal tests: a second run without -v force=1 must refuse and change nothing; a run into a database without
     seed-config must fail and leave no trace; with -v force=1 it must run again and pass the same assertions.
  6. Publishes Omni.App, runs it in Staging against "new" and checks real sign-ins: a migrated client user, a member,
     the platform-admin login of a migrated admin (and that locked / inactive / wrong password / client users are refused).
  7. Stops and deletes everything.

  Run it twice in a row to see it pass from scratch each time. Exit code 0 = pass.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Database\migrations\2026-10-generic\test-copy.ps1
#>
[CmdletBinding()]
param(
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin',
    [int]$Port = 55444,
    [int]$AppPort = 5189,
    [string]$WorkDir = (Join-Path ([IO.Path]::GetTempPath()) 'omni-copy-test'),
    [string]$OldRef = 'main',
    [string]$AdminRepo = '',
    [string]$AdminRef = 'main',
    [switch]$SkipAppLogin,
    [switch]$Keep
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = (Resolve-Path (Join-Path $here '..\..\..')).Path          # SculptFlowApp
if (-not $AdminRepo) { $AdminRepo = (Resolve-Path (Join-Path $repo '..\SculptFlowAdmin')).Path }
$placeholderPassword = 'Demo-ChangeMe-2026'                      # the demo seed's documented placeholder
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Fail([string]$message) { throw "FAILED: $message" }
function Step([string]$message) { Write-Host "== $message" -ForegroundColor Cyan }

function Quote([string]$s) { if ($s -match '[\s"]') { '"' + ($s -replace '"', '\"') + '"' } else { $s } }

# Runs a native program, captures stdout/stderr/exit code (PowerShell 5.1 turns native stderr into errors otherwise).
function Run([string]$exe, [string[]]$arguments, [hashtable]$environment = @{}, [string]$workingDirectory = $repo) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $exe
    $info.Arguments = ($arguments | ForEach-Object { Quote $_ }) -join ' '
    $info.WorkingDirectory = $workingDirectory
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = $utf8
    $info.StandardErrorEncoding = $utf8
    foreach ($key in $environment.Keys) { $info.EnvironmentVariables[$key] = [string]$environment[$key] }
    $process = [System.Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    [pscustomobject]@{ ExitCode = $process.ExitCode; Out = $stdout.Result; Err = $stderr.Result }
}

$psql = Join-Path $PgBin 'psql.exe'
function Sql([string]$database, [string[]]$extra, [switch]$AllowFail) {
    $result = Run $psql (@('-h', 'localhost', '-p', $Port, '-U', 'postgres', '-d', $database, '-X', '-v', 'ON_ERROR_STOP=1') + $extra)
    if ($result.ExitCode -ne 0 -and -not $AllowFail) { Fail ("psql on '$database' " + ($extra -join ' ') + "`n" + $result.Err + $result.Out) }
    $result
}
function SqlValue([string]$database, [string]$query) {
    (Sql $database @('-At', '-c', $query)).Out.Trim()
}

function Git-Show([string]$gitRepo, [string]$ref, [string]$path, [string]$target) {
    $result = Run 'git' @('-C', $gitRepo, 'show', "${ref}:${path}")
    if ($result.ExitCode -ne 0) { Fail "git show ${ref}:${path} in $gitRepo ($($result.Err))" }
    [IO.File]::WriteAllText($target, $result.Out, $utf8)
}

# The test cluster has no pgvector: the vector column becomes real[] on both sides (as the unit tests do).
function Swap-Vector([string]$sourceFile, [string]$targetFile) {
    $text = [IO.File]::ReadAllText($sourceFile, $utf8)
    $text = $text.Replace('create extension if not exists vector;', '').Replace('vector(1536)', 'real[]')
    $text = $text.Replace("pg_temp.dummy_embedding()::vector", "translate(pg_temp.dummy_embedding(), '[]', '{}')::real[]")
    [IO.File]::WriteAllText($targetFile, $text, $utf8)
}

$pgData = Join-Path $WorkDir 'pgdata'
$publishDir = Join-Path $WorkDir 'publish'
$appProcess = $null
$postgres = $null
$passed = $false

try {
    Step 'Preparing a fresh throwaway cluster'
    if (-not (Test-Path $psql)) { Fail "psql not found in '$PgBin' (use -PgBin)." }
    if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
    New-Item -ItemType Directory -Path $WorkDir | Out-Null
    $init = Run (Join-Path $PgBin 'initdb.exe') @('-D', $pgData, '-U', 'postgres', '-A', 'trust', '-E', 'UTF8', '--no-locale')
    if ($init.ExitCode -ne 0) { Fail "initdb: $($init.Err)" }
    $postgres = Start-Process -FilePath (Join-Path $PgBin 'postgres.exe') -ArgumentList @('-D', ('"' + $pgData + '"'), '-p', $Port) `
        -RedirectStandardError (Join-Path $WorkDir 'pg.log') -WindowStyle Hidden -PassThru
    $ready = $false
    for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
        Start-Sleep -Milliseconds 500
        $ready = (Run (Join-Path $PgBin 'pg_isready.exe') @('-h', 'localhost', '-p', $Port)).ExitCode -eq 0
    }
    if (-not $ready) { Fail 'the test cluster did not start (see pg.log in the work folder).' }

    Step "Old project: $OldRef schema + seeds + old demo seed + old admin schema + extras"
    $old = Join-Path $WorkDir 'old'
    New-Item -ItemType Directory -Path $old | Out-Null
    foreach ($name in 'schema', 'seed-config', 'seed', 'seed-demo') {
        Git-Show $repo $OldRef "Database/$name.sql" (Join-Path $old "$name.raw.sql")
        Swap-Vector (Join-Path $old "$name.raw.sql") (Join-Path $old "$name.sql")
    }
    Git-Show $AdminRepo $AdminRef 'Database/admin-schema.sql' (Join-Path $old 'admin-schema.sql')
    Sql 'postgres' @('-q', '-c', 'create database old', '-c', 'create database new', '-c', 'create database new_without_config') | Out-Null
    foreach ($file in 'schema', 'seed-config', 'seed', 'seed-demo', 'admin-schema') {
        Sql 'old' @('-q', '-f', (Join-Path $old "$file.sql")) | Out-Null
    }
    Sql 'old' @('-q', '-f', (Join-Path $here 'test-old-extras.sql')) | Out-Null
    Write-Host ("   old: {0} clinics, {1} leads, {2} users, {3} admins" -f (SqlValue 'old' 'select count(*) from core.clinics'),
        (SqlValue 'old' 'select count(*) from crm.leads'), (SqlValue 'old' 'select count(*) from identity.identity_users'),
        (SqlValue 'old' 'select count(*) from admin.admin_users'))

    Step 'New project: this branch schema.sql + seed-config.sql'
    $new = Join-Path $WorkDir 'new'
    New-Item -ItemType Directory -Path $new | Out-Null
    Swap-Vector (Join-Path $repo 'Database\schema.sql') (Join-Path $new 'schema.sql')
    foreach ($database in 'new', 'new_without_config') { Sql $database @('-q', '-f', (Join-Path $new 'schema.sql')) | Out-Null }
    Sql 'new' @('-q', '-f', (Join-Path $repo 'Database\seed-config.sql')) | Out-Null

    $copy = Join-Path $here 'copy-from-old.sql'
    $assert = Join-Path $here 'test-copy-assert.sql'
    $oldVars = @('-v', 'old_host=localhost', '-v', "old_port=$Port", '-v', 'old_dbname=old', '-v', 'old_user=postgres',
                 '-v', 'old_password=not-used-with-trust', '-v', 'old_sslmode=disable')

    Step 'Missing variables are refused'
    $missing = Sql 'new' @('-q', '-f', $copy) -AllowFail
    if ($missing.Out -notmatch 'Missing -v old_host' ) { Fail "no clear message without old_host. Output: $($missing.Out) $($missing.Err)" }
    if ((SqlValue 'new' 'select count(*) from core.clients') -ne '0') { Fail 'the script changed data without variables.' }

    Step 'A failed run leaves no trace (target without seed-config.sql)'
    $broken = Sql 'new_without_config' (@('-q', '-f', $copy) + $oldVars) -AllowFail
    if ($broken.ExitCode -eq 0 -or $broken.Err -notmatch 'config.settings is empty') { Fail "expected the seed-config check to stop the run. Output: $($broken.Err)" }
    if ((SqlValue 'new_without_config' "select count(*) from pg_foreign_server") -ne '0' -or
        (SqlValue 'new_without_config' "select count(*) from pg_namespace where nspname like 'old\_%'") -ne '0' -or
        (SqlValue 'new_without_config' 'select count(*) from core.clients') -ne '0') { Fail 'a failed run left the foreign server, schemas or rows behind.' }

    Step 'Run 1: copy into the empty project'
    $first = Sql 'new' (@('-q', '-f', $copy) + $oldVars)
    if ($first.Out -notmatch 'Result: ok') { Fail "the report did not end in ok.`n$($first.Out)" }
    if ($first.Out -match 'MISMATCH') { Fail "row count mismatch.`n$($first.Out)" }
    $rows = [regex]::Matches($first.Out, '\|\s+ok\s*\r?\n').Count
    Write-Host "   $rows tables compared, all ok; owners listed in the report:"
    ($first.Out -split "`n" | Where-Object { $_ -match '@' }) | ForEach-Object { Write-Host "   $_" }
    $assertRun = Sql 'new' @('-q', '-o', 'NUL', '-f', $assert)
    $okLines = ([regex]::Matches($assertRun.Err, 'NOTICE:\s+ok:')).Count
    Write-Host "   $okLines assertions ok"

    Step 'Run 2 without force: refuses and changes nothing'
    $before = SqlValue 'new' 'select (select count(*) from identity.users) || ''/'' || (select count(*) from crm.contacts) || ''/'' || (select count(*) from activity.events)'
    $second = Sql 'new' (@('-q', '-f', $copy) + $oldVars) -AllowFail
    if ($second.ExitCode -eq 0 -or $second.Err -notmatch 'already have rows') { Fail "the second run was not refused. Output: $($second.Err)" }
    $after = SqlValue 'new' 'select (select count(*) from identity.users) || ''/'' || (select count(*) from crm.contacts) || ''/'' || (select count(*) from activity.events)'
    if ($before -ne $after) { Fail "the refused run changed data ($before -> $after)." }
    if ((SqlValue 'new' 'select count(*) from pg_foreign_server') -ne '0') { Fail 'the refused run left the foreign server behind.' }

    Step 'Run 3 with force=1: empties the targets and copies again'
    $third = Sql 'new' (@('-q', '-v', 'force=1', '-f', $copy) + $oldVars)
    if ($third.Out -notmatch 'Result: ok') { Fail "the forced run did not end in ok.`n$($third.Out)" }
    Sql 'new' @('-q', '-o', 'NUL', '-f', $assert) | Out-Null
    Write-Host '   same assertions pass after the forced run'

    if (-not $SkipAppLogin) {
        Step 'Publishing Omni.App and signing in against the migrated database'
        $publish = Run 'dotnet' @('publish', (Join-Path $repo 'Omni.App\Omni.App.csproj'), '-c', 'Release', '-o', $publishDir, '--nologo', '-v', 'q')
        if ($publish.ExitCode -ne 0) { Fail "dotnet publish failed.`n$($publish.Out)" }
        $apiKey = 'copy-test-' + [guid]::NewGuid().ToString('N')
        $startInfo = New-Object System.Diagnostics.ProcessStartInfo
        $startInfo.FileName = 'dotnet'
        $startInfo.Arguments = '"' + (Join-Path $publishDir 'Omni.App.dll') + '" --urls http://localhost:' + $AppPort
        $startInfo.WorkingDirectory = $publishDir
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Staging'
        $startInfo.EnvironmentVariables['ConnectionStrings__Postgres'] = "Host=localhost;Port=$Port;Username=postgres;Database=new;Pooling=true"
        $startInfo.EnvironmentVariables['PlatformAdmin__ApiKey'] = $apiKey
        $startInfo.EnvironmentVariables['N8n__AiWebhookUrl'] = ''
        $startInfo.EnvironmentVariables['N8n__CalendarSyncWebhookUrl'] = ''
        $startInfo.EnvironmentVariables['N8n__KnowledgeBenchmarkWebhookUrl'] = ''
        $appProcess = [System.Diagnostics.Process]::Start($startInfo)
        $appOut = $appProcess.StandardOutput.ReadToEndAsync()
        $appErr = $appProcess.StandardError.ReadToEndAsync()
        $base = "http://localhost:$AppPort"
        $up = $false
        for ($i = 0; $i -lt 60 -and -not $up; $i++) {
            Start-Sleep -Milliseconds 500
            if ($appProcess.HasExited) { Fail "the app exited at start-up.`n$($appOut.Result)`n$($appErr.Result)" }
            try { $null = Invoke-WebRequest "$base/Account/Login" -UseBasicParsing -TimeoutSec 5; $up = $true } catch { }
        }
        if (-not $up) { Fail 'the app did not answer on the login page.' }

        Add-Type -AssemblyName System.Net.Http
        function New-Browser {
            $handler = New-Object System.Net.Http.HttpClientHandler
            $handler.AllowAutoRedirect = $false
            $handler.UseCookies = $true
            $client = New-Object System.Net.Http.HttpClient($handler)
            $client.Timeout = [TimeSpan]::FromSeconds(60)
            $client
        }
        function Send($client, [System.Net.Http.HttpRequestMessage]$request) {
            $response = $client.SendAsync($request).GetAwaiter().GetResult()
            $location = ''
            if ($null -ne $response.Headers.Location) { $location = $response.Headers.Location.ToString() }
            [pscustomobject]@{ Status = [int]$response.StatusCode; Location = $location; Body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() }
        }
        function Sign-In([string]$email, [string]$password) {
            $browser = New-Browser
            $page = Send $browser (New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, "$base/Account/Login"))
            $token = [regex]::Match($page.Body, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
            if (-not $token) { Fail 'no antiforgery token on the login page.' }
            $fields = New-Object 'System.Collections.Generic.List[System.Collections.Generic.KeyValuePair[string,string]]'
            foreach ($pair in @(@('Email', $email), @('Password', $password), @('__RequestVerificationToken', $token))) {
                $fields.Add((New-Object 'System.Collections.Generic.KeyValuePair[string,string]'($pair[0], $pair[1])))
            }
            $post = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, "$base/Account/Login")
            $post.Content = New-Object System.Net.Http.FormUrlEncodedContent -ArgumentList (,$fields)
            $result = Send $browser $post
            $result | Add-Member -NotePropertyName Browser -NotePropertyValue $browser
            $result
        }
        function Signed-In($attempt) { $attempt.Status -eq 302 -and $attempt.Location -notmatch 'Account/Login' }

        $owner = Sign-In 'demo@aurora-demo.test' $placeholderPassword
        if (-not (Signed-In $owner)) { Fail "the migrated client user could not sign in (status $($owner.Status), $($owner.Location))." }
        $target = if ($owner.Location -match '^https?://') { $owner.Location } else { $base + $owner.Location }
        $dashboard = Send $owner.Browser (New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, $target))
        if ($dashboard.Status -ne 200) { Fail "the page after sign-in ($target) answered $($dashboard.Status) for the migrated client user." }
        Write-Host '   client user (owner) signs in and opens the dashboard'

        $member = Sign-In 'nurse@aurora-demo.test' $placeholderPassword
        if (-not (Signed-In $member)) { Fail 'the migrated member could not sign in.' }
        Write-Host '   member signs in'

        if (Signed-In (Sign-In 'demo@aurora-demo.test' 'wrong-password-1')) { Fail 'a wrong password signed in.' }
        if (Signed-In (Sign-In 'admin@omni-test.test' $placeholderPassword)) { Fail 'an admin signed in to the client app.' }
        Write-Host '   wrong password and admin account are refused on the client app'

        $adminBrowser = New-Browser
        function Admin-Login([string]$email, [string]$password) {
            $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, "$base/api/platform-admin/auth/login")
            $request.Headers.Add('X-Platform-Admin-Key', $apiKey)
            $request.Content = New-Object System.Net.Http.StringContent((@{ email = $email; password = $password } | ConvertTo-Json), $utf8, 'application/json')
            Send $adminBrowser $request
        }
        $admin = Admin-Login 'admin@omni-test.test' $placeholderPassword
        if ($admin.Status -ne 200 -or $admin.Body -notmatch 'ad000000-0000-4000-8000-000000000001') { Fail "the migrated admin could not log in (status $($admin.Status)): $($admin.Body)" }
        Write-Host '   migrated admin logs in through the platform-admin API'
        foreach ($case in @(@('locked@omni-test.test', $placeholderPassword, 'locked admin'), @('inactive@omni-test.test', $placeholderPassword, 'inactive admin'),
                            @('admin@omni-test.test', 'wrong-password-1', 'wrong password'), @('demo@aurora-demo.test', $placeholderPassword, 'client user'))) {
            $attempt = Admin-Login $case[0] $case[1]
            if ($attempt.Status -ne 401) { Fail "$($case[2]) was not refused by the admin login (status $($attempt.Status))." }
        }
        Write-Host '   locked, inactive, wrong password and client users are refused (401)'
    }

    $passed = $true
}
finally {
    if ($appProcess -and -not $appProcess.HasExited) { try { $appProcess.Kill() } catch { } }
    if ($postgres -and -not $postgres.HasExited) {
        $null = Run (Join-Path $PgBin 'pg_ctl.exe') @('stop', '-D', $pgData, '-m', 'fast')
        Start-Sleep -Milliseconds 500
        if (-not $postgres.HasExited) { try { $postgres.Kill() } catch { } }
    }
    if (-not $Keep -and (Test-Path $WorkDir)) { Start-Sleep -Milliseconds 500; Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue }
}

if ($passed) { Write-Host 'copy-from-old test: PASS' -ForegroundColor Green; exit 0 }
Write-Host 'copy-from-old test: FAIL' -ForegroundColor Red; exit 1
