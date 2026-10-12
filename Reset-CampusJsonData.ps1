<#
.SYNOPSIS
    Restores campus_system_unified_data_schema.json from campus_system_seed_data.json.

.DESCRIPTION
    Backs up the live datastore to BackupFolder\json, then overwrites it with the seed.
    Takes the same named mutex as CanonicalJsonStore, so it is safe to run while
    department apps are running.

.PARAMETER BackupOnly
    Only take a timestamped backup of the live datastore; do not reset it.
#>
param(
    [switch]$BackupOnly
)

$ErrorActionPreference = 'Stop'

$livePath = Join-Path $PSScriptRoot 'campus_system_unified_data_schema.json'
$seedPath = Join-Path $PSScriptRoot 'campus_system_seed_data.json'
$backupDirectory = Join-Path $PSScriptRoot 'BackupFolder\json'

if (-not (Test-Path $livePath)) { throw "Live datastore not found: $livePath" }
if (-not $BackupOnly -and -not (Test-Path $seedPath)) { throw "Seed datastore not found: $seedPath" }

# Must match CanonicalJsonStore.MutexName.
$fullPath = [System.IO.Path]::GetFullPath($livePath).ToUpperInvariant()
$sha = [System.Security.Cryptography.SHA256]::Create()
$hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($fullPath))
$hex = -join ($hash[0..7] | ForEach-Object { $_.ToString('X2') })
$mutex = New-Object System.Threading.Mutex($false, "Local\CampusSystemJsonStore_$hex")

$acquired = $false
try {
    try { $acquired = $mutex.WaitOne([TimeSpan]::FromSeconds(15)) }
    catch [System.Threading.AbandonedMutexException] { $acquired = $true }
    if (-not $acquired) { throw 'Timed out waiting for the datastore lock.' }

    New-Item -ItemType Directory -Force $backupDirectory | Out-Null
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd_HHmmss_fff')
    $backupPath = Join-Path $backupDirectory "campus_system_unified_data_schema_$stamp.json"
    Copy-Item $livePath $backupPath
    Write-Host "Backed up live datastore to $backupPath"

    if (-not $BackupOnly) {
        $null = Get-Content $seedPath -Raw | ConvertFrom-Json
        Copy-Item $seedPath $livePath -Force
        Write-Host 'Live datastore reset to seed.'
    }
}
finally {
    if ($acquired) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
