#Requires -Version 7.0
param(
    [Parameter(Mandatory = $true)]
    [string] $ReportPath
)

& "$PSScriptRoot/assert-module-coverage.ps1" -ReportPath $ReportPath -ModuleName Documents
