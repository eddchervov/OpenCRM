#Requires -Version 7.0
param(
    [Parameter(Mandatory = $true)]
    [string] $ReportPath,

    [Parameter(Mandatory = $true)]
    [ValidateSet('Documents', 'Vehicles', 'Inventory', 'Maintenance', 'Clients', 'Organizations', 'Catalog', 'Planning', 'Billing', 'Workspace', 'Accounts', 'Notifications', 'Support', 'Promotions')]
    [string] $ModuleName
)

$ErrorActionPreference = 'Stop'
[xml] $report = Get-Content -LiteralPath $ReportPath -Raw -Encoding utf8
$packages = @($report.coverage.packages.package)
if ($packages.Count -ne 1 -or $packages[0].name -ne "OpenCRM.Modules.$ModuleName")
{
    throw "Отчёт должен содержать только OpenCRM.Modules.$ModuleName."
}

$linesCovered = [long] $report.coverage.'lines-covered'
$linesValid = [long] $report.coverage.'lines-valid'
$branchesCovered = [long] $report.coverage.'branches-covered'
$branchesValid = [long] $report.coverage.'branches-valid'
if ($linesValid -le 0 -or $branchesValid -le 0)
{
    throw 'В отчёте отсутствуют измеренные строки или ветвления.'
}

Write-Output "${ModuleName}: строки $linesCovered/$linesValid, ветвления $branchesCovered/$branchesValid."
