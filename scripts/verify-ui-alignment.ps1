param([string]$Root = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$layouts = @(
    'Departments/Testing/TestingMain/Pages/Shared/_Layout.cshtml',
    'Departments/Library/LibraryMain/Pages/Shared/_Layout.cshtml',
    'Departments/StudentPortal/StudentPortalMain/Pages/Shared/_Layout.cshtml',
    'Departments/FacultyPortal/FacultyPortalMain/Pages/Shared/_Layout.cshtml',
    'Departments/Registrar/RegistrarMain/Pages/Shared/_Layout.cshtml',
    'Departments/Finance/FinanceMain/Pages/Shared/_FinanceAdminLayout.cshtml',
    'Departments/Finance/FinanceMain/Pages/Shared/_FinanceFacultyLayout.cshtml',
    'Departments/Finance/FinanceMain/Pages/Shared/_FinanceStudentLayout.cshtml'
)
$failures = [System.Collections.Generic.List[string]]::new()
foreach ($relative in $layouts) {
    $path = Join-Path $Root $relative
    if (-not (Test-Path -LiteralPath $path)) { $failures.Add("Missing layout: $relative"); continue }
    if ((Get-Content -LiteralPath $path -Raw) -notmatch '_content/CampusSystem\.Ui/css/campus-(base|tokens)\.css') { $failures.Add("Shared UI stylesheet missing from $relative") }
}
$guidance = Join-Path $Root 'Departments/GuidanceDepartment/GuidanceDepartmentMain/wwwroot/styles.css'
if ((Get-Content -LiteralPath $guidance -Raw) -notmatch '_content/CampusSystem\.Ui/css/campus-base\.css') { $failures.Add('Guidance does not import the shared UI stylesheet.') }
Get-ChildItem -LiteralPath (Join-Path $Root 'Departments') -Recurse -Filter '*.css' | ForEach-Object {
    $badToken = Select-String -LiteralPath $_.FullName -Pattern '--(?:primary|nu-blue|fin-navy)[\w-]*\s*:\s*#[0-9a-fA-F]{3,8}'
    if ($badToken) { $failures.Add("Hard-coded brand token in $($_.FullName):$($badToken[0].LineNumber)") }
}
if ($failures.Count) { $failures | ForEach-Object { Write-Error $_ }; exit 1 }
Write-Output 'UI alignment check passed.'
