[CmdletBinding()]
param(
    [int] $Port = 5080,
    [switch] $AutoStartDepartments
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$checker = Join-Path $root 'Check-Departments.ps1'
$selectedPort = $Port
$listener = $null

function Get-FirstAvailablePort {
    param([int] $StartPort, [int] $MaxAttempts = 20)
    for ($offset = 0; $offset -lt $MaxAttempts; $offset++) {
        $candidate = $StartPort + $offset
        $inUse = $false
        try {
            $connections = Get-NetTCPConnection -LocalPort $candidate -ErrorAction Stop
            if ($null -ne $connections -and $connections.Count -gt 0) { $inUse = $true }
        }
        catch {
            $inUse = $false
        }
        if (-not $inUse) { return $candidate }
    }
    return $StartPort
}

$departmentProjects = [ordered]@{
    FacultyPortalMain = Join-Path $root 'Departments\FacultyPortal\FacultyPortalMain\FacultyPortalMain.csproj'
    FinanceMain = Join-Path $root 'Departments\Finance\FinanceMain\FinanceMain.csproj'
    GuidanceDepartmentMain = Join-Path $root 'Departments\GuidanceDepartment\GuidanceDepartmentMain\GuidanceDepartmentMain.csproj'
    LibraryMain = Join-Path $root 'Departments\Library\LibraryMain\LibraryMain.csproj'
    RegistrarMain = Join-Path $root 'Departments\Registrar\RegistrarMain\RegistrarMain.csproj'
    StudentPortalMain = Join-Path $root 'Departments\StudentPortal\StudentPortalMain\StudentPortalMain.csproj'
}
$departmentPorts = [ordered]@{
    FacultyPortalMain = 52141
    FinanceMain = 52142
    GuidanceDepartmentMain = 52143
    LibraryMain = 52144
    RegistrarMain = 52145
    StudentPortalMain = 52146
}
$departmentProcesses = @{}
$selectedPort = Get-FirstAvailablePort -StartPort $Port
$listener = [Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$selectedPort/")
try {
    $listener.Start()
}
catch [Net.HttpListenerException] {
    $listener.Close()
    $selectedPort = Get-FirstAvailablePort -StartPort ($Port + 1)
    $listener = [Net.HttpListener]::new()
    $listener.Prefixes.Add("http://localhost:$selectedPort/")
    try {
        $listener.Start()
    }
    catch {
        throw "No available dashboard port found between $Port and $($Port + 20)."
    }
}
Write-Host "Maintenance dashboard: http://localhost:$selectedPort/"
Write-Host 'Press Ctrl+C to stop.'

function Send-Response {
    param([Net.HttpListenerContext] $Context, [int] $StatusCode, [string] $ContentType, [string] $Body)
    $bytes = [Text.Encoding]::UTF8.GetBytes($Body)
    $Context.Response.StatusCode = $StatusCode
    $Context.Response.ContentType = $ContentType
    $Context.Response.ContentLength64 = $bytes.Length
    $Context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
    $Context.Response.Close()
}

function Get-DepartmentStatus {
    Sync-DepartmentProcesses
    $departmentProjects.Keys | ForEach-Object {
        $process = $departmentProcesses[$_]
        $managed = $null -ne $process
        if ($null -eq $process) {
            $process = Get-ExternalDepartmentProcess $_
        }
        $running = $null -ne $process -and -not $process.HasExited
        $port = $departmentPorts[$_]
        if (-not $managed -and $running) {
            $externalPort = Get-DepartmentListeningPort $process
            if ($null -ne $externalPort) { $port = $externalPort }
        }
        [pscustomobject]@{ Name = $_; Port = $port; Url = if ($running) { "http://localhost:$port/" } else { $null }; Running = $running; Managed = $managed; CanStop = $managed; Error = $null -ne $process -and -not $running }
    }
}

function Get-ExternalDepartmentProcess {
    param([string] $Name)
    $executable = Join-Path (Split-Path $departmentProjects[$Name]) "bin\Debug\net10.0\$Name.exe"
    Get-Process -Name $Name -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -eq $executable } catch { $false }
    } | Select-Object -First 1
}

function Get-DepartmentListeningPort {
    param([Diagnostics.Process] $Process)
    try {
        Get-NetTCPConnection -State Listen -OwningProcess $Process.Id -ErrorAction Stop |
            Where-Object { $_.LocalAddress -in @('0.0.0.0', '127.0.0.1', '::', '::1') } |
            Select-Object -First 1 -ExpandProperty LocalPort
    }
    catch {
        $null
    }
}

function Sync-DepartmentProcesses {
    foreach ($name in @($departmentProcesses.Keys)) {
        $process = $departmentProcesses[$name]
        try { $process.Refresh() } catch { }
        if ($process.HasExited) {
            Write-Host "$name stopped (exit code $($process.ExitCode))."
            $departmentProcesses.Remove($name)
        }
    }
}

function Stop-Department {
    param([string] $Name)
    $process = $departmentProcesses[$Name]
    if ($null -ne $process -and -not $process.HasExited) {
        & taskkill.exe /PID $process.Id /T /F | Out-Null
    }
    $departmentProcesses.Remove($Name)
}

function Stop-AllDotnetRuntimeProcesses {
    $stopped = @()
    $candidateProcesses = @()

    $dotnetProcesses = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue
    if ($null -ne $dotnetProcesses) {
        foreach ($proc in @($dotnetProcesses)) {
            $commandLine = [string]$proc.CommandLine
            if ($commandLine -match 'Departments\\.*Main.*\\.*\.csproj|--project.*Departments\\.*Main.*\.csproj' -or $commandLine -match 'Departments\\.*Main.*\\.*\.csproj' -or $commandLine -match '\\Departments\\.*Main.*\\.*\.csproj') {
                $candidateProcesses += $proc
            }
        }
    }

    $departmentPids = @($candidateProcesses | Select-Object -ExpandProperty ProcessId)
    if ($departmentPids.Count -gt 0) {
        $consoleProcesses = Get-CimInstance Win32_Process -Filter "Name = 'conhost.exe'" -ErrorAction SilentlyContinue
        if ($null -ne $consoleProcesses) {
            foreach ($proc in @($consoleProcesses)) {
                if ($departmentPids -contains $proc.ParentProcessId) {
                    $candidateProcesses += $proc
                }
            }
        }
    }

    foreach ($process in @($candidateProcesses | Sort-Object ProcessId -Unique)) {
        try {
            if ($process.ProcessId -gt 0) {
                $null = taskkill.exe /F /T /PID $process.ProcessId 2>$null
                $stopped += [pscustomobject]@{ ProcessId = $process.ProcessId; Name = $process.Name }
            }
        }
        catch {
            Write-Host "Failed to stop department runtime process $($process.ProcessId): $($_.Exception.Message)"
        }
    }

    $departmentProcesses.Clear()
    return @($stopped)
}

function Start-Department {
    param([string] $Name)
    $externalProcess = Get-ExternalDepartmentProcess $Name
    if ($null -ne $externalProcess -and -not $externalProcess.HasExited) {
        Write-Host "$Name is already running outside the maintenance dashboard (PID $($externalProcess.Id))."
        return
    }
    Stop-Department $Name
    $project = $departmentProjects[$Name]
    if (-not (Test-Path -LiteralPath $project)) { throw "Project not found: $project" }
    $logDirectory = Join-Path $PSScriptRoot 'logs'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $stdout = Join-Path $logDirectory "$Name.out.log"
    $stderr = Join-Path $logDirectory "$Name.error.log"
    $departmentProcesses[$Name] = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--project', $project, '--no-launch-profile', '--urls', "http://localhost:$($departmentPorts[$Name])") -WorkingDirectory (Split-Path $project) -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -WindowStyle Hidden
    $departmentProcesses[$Name].EnableRaisingEvents = $true
}

function Get-DepartmentLog {
    param([string] $Name)
    $logDirectory = Join-Path $PSScriptRoot 'logs'
    $files = @((Join-Path $logDirectory "$Name.out.log"), (Join-Path $logDirectory "$Name.error.log"))
    $lines = foreach ($file in $files) {
        if (Test-Path -LiteralPath $file) { Get-Content -LiteralPath $file -Tail 120 }
    }
    return ($lines -join "`n")
}

function Get-AIContextFiles {
    $fileSpecs = @(
        @{ Name = 'Maintenance'; RelativePath = 'Maintenance\AI-CONTEXT_DepartmentMaintainance .md' },
        @{ Name = 'FacultyPortal'; RelativePath = 'Departments\FacultyPortal\FacultyPortalMain\Ai-context_(FacultyPortal).md' },
        @{ Name = 'Finance'; RelativePath = 'Departments\Finance\FinanceMain\Ai-context_(Finance).md' },
        @{ Name = 'GuidanceDepartment'; RelativePath = 'Departments\GuidanceDepartment\GuidanceDepartmentMain\Ai-context_(GuidanceDepartment).md' },
        @{ Name = 'Library'; RelativePath = 'Departments\Library\LibraryMain\Ai-context_(Library).md' },
        @{ Name = 'Registrar'; RelativePath = 'Departments\Registrar\RegistrarMain\Ai-context_(Registrar).md' },
        @{ Name = 'StudentPortal'; RelativePath = 'Departments\StudentPortal\StudentPortalMain\Ai-context_(StudentPortal).md' }
    )

    $entries = @()
    foreach ($spec in $fileSpecs) {
        $fullPath = Join-Path $root $spec.RelativePath
        $entry = [pscustomobject]@{
            name = $spec.Name
            path = $fullPath
            content = $null
            error = $null
            truncated = $false
        }

        if (-not (Test-Path -LiteralPath $fullPath)) {
            $entry.error = 'File not found.'
            $entries += $entry
            continue
        }

        try {
            $rawText = Get-Content -LiteralPath $fullPath -Raw -ErrorAction Stop
            if ($rawText.Length -gt 12000) {
                $entry.truncated = $true
                $entry.content = ($rawText.Substring(0, 12000) + "`n... [trimmed for dashboard display]")
            }
            else {
                $entry.content = $rawText
            }
        }
        catch {
            $entry.error = $_.Exception.Message
        }

        $entries += $entry
    }

    return @($entries)
}

$script:AiContextCache = @()
try {
    $script:AiContextCache = @(Get-AIContextFiles)
}
catch {
    $script:AiContextCache = @([pscustomobject]@{ name = 'maintenance'; path = $root; content = $null; error = $_.Exception.Message })
}

if ($AutoStartDepartments) {
    Write-Host 'Auto-starting department runtimes because -AutoStartDepartments was supplied.'
    foreach ($name in $departmentProjects.Keys) {
        Start-Department $name
    }
}
else {
    Write-Host 'Department services are not auto-started. Use the dashboard controls or /api/departments/start-all to start them.'
    Stop-AllDotnetRuntimeProcesses
}

try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        try {
            $path = $context.Request.Url.AbsolutePath
            if ($path -eq '/api/departments') {
                $payload = [pscustomobject]@{ Departments = @(Get-DepartmentStatus) } | ConvertTo-Json -Depth 5
                Send-Response $context 200 'application/json; charset=utf-8' $payload
            }
            elseif ($path -eq '/api/departments/start-all' -or $path -eq '/api/departments/stop-all') {
                $startAll = $path -like '*start-all'
                foreach ($name in $departmentProjects.Keys) {
                    if ($startAll) { Start-Department $name } else { Stop-Department $name }
                }
                $payload = [pscustomobject]@{ Action = if ($startAll) { 'start-all' } else { 'stop-all' }; Departments = @(Get-DepartmentStatus) } | ConvertTo-Json -Depth 5
                Send-Response $context 200 'application/json; charset=utf-8' $payload
            }
            elseif ($path -eq '/api/netruntime/stop-all') {
                $stoppedProcesses = @(Stop-AllDotnetRuntimeProcesses)
                $payload = [pscustomobject]@{ Stopped = $stoppedProcesses; Departments = @(Get-DepartmentStatus) } | ConvertTo-Json -Depth 5
                Send-Response $context 200 'application/json; charset=utf-8' $payload
            }
            elseif ($path -match '^/api/departments/([^/]+)/log$') {
                $name = [Uri]::UnescapeDataString($Matches[1])
                if (-not $departmentProjects.Contains($name)) { Send-Response $context 404 'application/json; charset=utf-8' '{"error":"Unknown department"}'; continue }
                Send-Response $context 200 'text/plain; charset=utf-8' (Get-DepartmentLog $name)
            }
            elseif ($path -match '^/api/departments/([^/]+)/(start|stop)$') {
                $name = [Uri]::UnescapeDataString($Matches[1])
                $action = $Matches[2]
                if (-not $departmentProjects.Contains($name)) { Send-Response $context 404 'application/json; charset=utf-8' '{"error":"Unknown department"}'; continue }
                if ($action -eq 'start') { Start-Department $name } else { Stop-Department $name }
                $status = @(Get-DepartmentStatus | Where-Object Name -eq $name)[0]
                $payload = [pscustomobject]@{ Name = $name; Running = $status.Running; Error = $status.Error } | ConvertTo-Json
                Send-Response $context 200 'application/json; charset=utf-8' $payload
            }
            elseif ($path -eq '/api/ai-context') {
                $payload = @($script:AiContextCache) | ConvertTo-Json -Depth 5 -Compress
                Send-Response $context 200 'application/json; charset=utf-8' $payload
            }
            elseif ($path -eq '/api/swarm-config') {
                $configPath = Join-Path $PSScriptRoot 'swarm\campus-review-swarm.json'
                if (Test-Path -LiteralPath $configPath) {
                    $body = Get-Content -LiteralPath $configPath -Raw
                    Send-Response $context 200 'application/json; charset=utf-8' $body
                }
                else {
                    Send-Response $context 404 'application/json; charset=utf-8' '{"error":"Swarm config not found"}'
                }
            }
            elseif ($path -eq '/swarm/campus-review-swarm.json') {
                $configPath = Join-Path $PSScriptRoot 'swarm\campus-review-swarm.json'
                if (Test-Path -LiteralPath $configPath) {
                    $body = Get-Content -LiteralPath $configPath -Raw
                    Send-Response $context 200 'application/json; charset=utf-8' $body
                }
                else {
                    Send-Response $context 404 'application/json; charset=utf-8' '{"error":"Swarm config not found"}'
                }
            }
            elseif ($path -eq '/api/health') {
                $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $checker -ProjectPath $root -AllDepartments -Build 2>&1 | Out-String
                $reports = @()
                foreach ($line in ($output -split "`r?`n")) {
                    if ($line -match '^\s*(\w+Main): (HEALTHY|MISSING ITEMS)\s*$') {
                        $reports += [pscustomobject]@{ Name = $Matches[1]; Healthy = $Matches[2] -eq 'HEALTHY'; Errors = @() }
                    }
                    elseif ($line -match '^\s*(FAIL\s+.+?)\s{2,}(.+)$' -and $reports.Count -gt 0) {
                        $reports[-1].Errors += ($Matches[1].Trim() + ': ' + $Matches[2].Trim())
                    }
                    elseif ($line -match '^\s*Missing: (.+)$' -and $reports.Count -gt 0) {
                        $reports[-1].Errors += $Matches[1].Trim()
                    }
                    elseif ($line -match '\berror\s+[A-Z]{2}\d+\b' -and $reports.Count -gt 0) {
                        $reports[-1].Errors += $line.Trim()
                    }
                    elseif ($line -match '^\s*Build failed with .+$' -and $reports.Count -gt 0) {
                        $reports[-1].Errors += $line.Trim()
                    }
                }
                $failed = @($reports | Where-Object { -not $_.Healthy }).Count
                $payload = [pscustomobject]@{ Reports = $reports; Failed = $failed; Summary = "Checked $($reports.Count) department(s): $(@($reports | Where-Object Healthy).Count) healthy, $failed with errors." } | ConvertTo-Json -Depth 5
                Send-Response $context 200 'application/json; charset=utf-8' $payload
            }
            elseif ($path -eq '/' -or $path -eq '/index.html') {
                Send-Response $context 200 'text/html; charset=utf-8' (Get-Content (Join-Path $PSScriptRoot 'index.html') -Raw)
            }
            else { Send-Response $context 404 'text/plain; charset=utf-8' 'Not found' }
        }
        catch { Send-Response $context 500 'text/plain; charset=utf-8' $_.Exception.Message }
    }
}
finally {
    foreach ($name in @($departmentProcesses.Keys)) { Stop-Department $name }
    $listener.Stop(); $listener.Close()
}
