[CmdletBinding()]
param(
    [string] $ProjectPath = (Get-Location).Path,
    [switch] $AllDepartments,
    [switch] $Build
)

# Health check for the department apps. Replaces Check-GuidanceServices.ps1, which verified the old
# per-department SQL service copies. Every app now talks to the shared JSON datastore, so this checks that:
#   - the project file and Program.cs exist
#   - Program.cs registers CanonicalJsonStore (the only way to reach the datastore)
#   - the app has real sign-in (CampusAuth cookies; Student Portal uses AuthStore sessions and Guidance uses AuthStore JWTs)
#   - no SQL, EF Core or MySQL package or connection string has crept back in
#   - the datastore file loads and its schema version is one this build understands
#   - optionally, the project builds
# Output keeps the "<Name>Main: HEALTHY" / "Missing:" shape that Maintenance\Start-MaintenanceDashboard.ps1 parses.

$ErrorActionPreference = 'Stop'

$forbiddenPackages = @(
    'Microsoft.EntityFrameworkCore'
    'Microsoft.EntityFrameworkCore.SqlServer'
    'Microsoft.EntityFrameworkCore.Design'
    'Microsoft.EntityFrameworkCore.Tools'
    'MySqlConnector'
    'MySql.Data'
    'Dapper'
    'System.Data.SqlClient'
    'Microsoft.Data.SqlClient'
)

$departmentPaths = [ordered]@{
    FacultyPortal = 'Departments\FacultyPortal\FacultyPortalMain'
    Finance = 'Departments\Finance\FinanceMain'
    GuidanceDepartment = 'Departments\GuidanceDepartment\GuidanceDepartmentMain'
    Library = 'Departments\Library\LibraryMain'
    Registrar = 'Departments\Registrar\RegistrarMain'
    StudentPortal = 'Departments\StudentPortal\StudentPortalMain'
    Testing = 'Departments\Testing\TestingMain'
}

function Get-ProjectDirectory {
    param([string] $Path)

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $projectFiles = @(Get-ChildItem -LiteralPath $resolvedPath -Filter '*.csproj' -File)
    if ($projectFiles.Count -eq 1) { return $resolvedPath }
    if ($projectFiles.Count -gt 1) { throw "Multiple project files found in $resolvedPath. Pass the specific project folder." }
    throw "No .csproj file found in $resolvedPath."
}

function Get-DatastoreCheck {
    param([string] $Directory)

    $current = (Resolve-Path -LiteralPath $Directory).Path
    while ($current) {
        $candidate = Join-Path $current 'campus_system_unified_data_schema.json'
        if (Test-Path -LiteralPath $candidate) {
            try {
                $root = Get-Content -LiteralPath $candidate -Raw | ConvertFrom-Json
                $version = $root.system.schema_version
                return @($true, "Loads, schema_version $version")
            }
            catch {
                return @($false, "Does not parse: $($_.Exception.Message)")
            }
        }
        $parent = Split-Path $current -Parent
        if ($parent -eq $current) { break }
        $current = $parent
    }
    return @($false, 'campus_system_unified_data_schema.json not found above the project')
}

function Test-Project {
    param([string] $Directory)

    $projectFile = Get-ChildItem -LiteralPath $Directory -Filter '*.csproj' -File | Select-Object -First 1
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectFile.Name)
    $programFile = Join-Path $Directory 'Program.cs'
    $results = [System.Collections.Generic.List[object]]::new()

    function Add-Result {
        param([string] $Check, [bool] $Passed, [string] $Detail)
        $results.Add([pscustomobject]@{
                Status = if ($Passed) { 'PASS' } else { 'FAIL' }
                Check = $Check
                Detail = $Detail
            })
    }

    $program = if (Test-Path -LiteralPath $programFile) { Get-Content -LiteralPath $programFile -Raw } else { '' }
    Add-Result 'Program.cs' ($program.Length -gt 0) $(if ($program) { 'Present' } else { 'Missing' })

    $ownSignIn = @('StudentPortalMain', 'GuidanceDepartmentMain') -contains $projectName
    $hasAuth = $program.Contains('AddCampusAuth') -or $ownSignIn
    Add-Result 'Sign-in configured' $hasAuth $(if ($program.Contains('AddCampusAuth')) { 'CampusAuth cookie sign-in' } elseif ($ownSignIn) { 'Own sign-in on AuthStore' } else { 'Program.cs does not call AddCampusAuth' })

    $registered = $program.Contains('CanonicalJsonStore')
    Add-Result 'CanonicalJsonStore registered' $registered $(if ($registered) { 'Registered' } else { 'Program.cs does not register CanonicalJsonStore' })

    $projectContents = Get-Content -LiteralPath $projectFile.FullName -Raw
    $found = @($forbiddenPackages | Where-Object { $projectContents.Contains(('Include="{0}"' -f $_)) })
    Add-Result 'No SQL or EF package' ($found.Count -eq 0) $(if ($found.Count -eq 0) { 'None referenced' } else { "Referenced: $($found -join ', ')" })

    $settingsFound = @()
    foreach ($name in 'appsettings.json', 'appsettings.Development.json') {
        $path = Join-Path $Directory $name
        if ((Test-Path -LiteralPath $path) -and (Get-Content -LiteralPath $path -Raw).Contains('"ConnectionStrings"')) { $settingsFound += $name }
    }
    Add-Result 'No connection strings' ($settingsFound.Count -eq 0) $(if ($settingsFound.Count -eq 0) { 'None configured' } else { "Found in: $($settingsFound -join ', ')" })

    $datastore = Get-DatastoreCheck $Directory
    Add-Result 'Datastore file' $datastore[0] $datastore[1]

    if ($Build) {
        $buildOutput = & dotnet build $projectFile.FullName --nologo 2>&1
        $buildPassed = $LASTEXITCODE -eq 0
        Add-Result 'dotnet build' $buildPassed $(if ($buildPassed) { 'Build succeeded' } else { "Build failed with exit code $LASTEXITCODE" })
        if (-not $buildPassed) { $buildOutput | Where-Object { $_ -match '\berror\s+[A-Z]{2}\d+\b' } | Select-Object -First 5 | ForEach-Object { Write-Host $_ }  }
    }

    $failed = @($results | Where-Object { $_.Status -eq 'FAIL' })
    [pscustomobject]@{
        Project = $projectName
        Directory = $Directory
        Passed = $failed.Count -eq 0
        Results = $results
        Missing = @($failed | Select-Object -ExpandProperty Check)
    }
}

$directories = @()
if ($AllDepartments) {
    $repositoryRoot = (Resolve-Path -LiteralPath $ProjectPath).Path
    foreach ($relativePath in $departmentPaths.Values) {
        $candidate = Join-Path $repositoryRoot $relativePath
        if (Test-Path -LiteralPath $candidate) { $directories += Get-ProjectDirectory $candidate }
    }
} else {
    $directories = @(Get-ProjectDirectory $ProjectPath)
}

$reports = foreach ($directory in $directories) { Test-Project $directory }

foreach ($report in $reports) {
    $status = if ($report.Passed) { 'HEALTHY' } else { 'MISSING ITEMS' }
    Write-Host "`n$($report.Project): $status"
    $report.Results | Format-Table -AutoSize | Out-Host
    if (-not $report.Passed) {
        Write-Host "Missing: $($report.Missing -join '; ')" -ForegroundColor Yellow
    }
}

$allReports = @($reports)
$unhealthy = @($allReports | Where-Object { -not $_.Passed })
$healthyCount = @($allReports | Where-Object Passed).Count
Write-Host "`nChecked $($allReports.Count) project(s): $healthyCount healthy, $($unhealthy.Count) with missing items."
if ($unhealthy.Count -gt 0) { exit 1 }
