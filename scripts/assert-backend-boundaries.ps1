#Requires -Version 7.0
param(
    [string] $SolutionPath = (Join-Path $PSScriptRoot '../OpenCRM.sln')
)

$ErrorActionPreference = 'Stop'
$solutionPath = [IO.Path]::GetFullPath($SolutionPath)
$solutionDirectory = [IO.Path]::GetDirectoryName($solutionPath)
$solution = [IO.File]::ReadAllText($solutionPath)
$projects = @{}
$graph = @{}
$referenceCount = 0

foreach ($match in [regex]::Matches($solution, 'Project\("\{[^}]+\}"\) = "([^"]+)", "([^"]+\.csproj)", "(\{[^}]+\})"'))
{
    $path = [IO.Path]::GetFullPath((Join-Path $solutionDirectory $match.Groups[2].Value.Replace('\', '/')))
    if (!(Test-Path -LiteralPath $path -PathType Leaf))
    {
        throw "Проект решения отсутствует: $path."
    }

    [xml] $projectXml = [IO.File]::ReadAllText($path)
    $isTestProject = @($projectXml.Project.ItemGroup.PackageReference | Where-Object { $_.Include -eq 'Microsoft.NET.Test.Sdk' }).Count -gt 0
    $relativePath = [IO.Path]::GetRelativePath($solutionDirectory, $path).Replace('\', '/')
    $projects[$path] = @{
        Name = $match.Groups[1].Value
        Id = $match.Groups[3].Value
        Xml = $projectXml
        IsTest = $isTestProject
        IsTestSupport = $isTestProject -or "/$relativePath" -match '/tests/'
    }
    $graph[$path] = [Collections.Generic.List[string]]::new()
}

if ($projects.Count -eq 0)
{
    throw 'В решении не найдены C#-проекты.'
}

$testsFolders = @([regex]::Matches($solution, 'Project\("\{[^}]+\}"\) = "Tests", "Tests", "(\{[^}]+\})"') |
    ForEach-Object { $_.Groups[1].Value })
foreach ($path in $projects.Keys)
{
    $project = $projects[$path]
    foreach ($reference in @($project.Xml.Project.ItemGroup.ProjectReference | Where-Object { $_ }))
    {
        $target = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($path)) $reference.Include.Replace('\', '/')))
        if (!$projects.ContainsKey($target))
        {
            throw "Ссылка $($project.Name) отсутствует в решении: $target."
        }

        $targetProject = $projects[$target]
        $graph[$path].Add($target)
        $referenceCount++
        if (!$project.IsTestSupport -and $targetProject.IsTestSupport)
        {
            throw "Рабочий проект ссылается на тестовый: $($project.Name) -> $($targetProject.Name)."
        }

        if ($project.IsTestSupport -and $targetProject.IsTest)
        {
            throw "Тестовый проект или фикстура ссылается на тестовый проект: $($project.Name) -> $($targetProject.Name)."
        }

        if (!$project.IsTestSupport -and $targetProject.Name -eq 'OpenCRM.WebApp')
        {
            throw "Библиотека ссылается на HTTP-хост: $($project.Name)."
        }

        if (($project.Name -like '*.Contracts' -or $project.Name -eq 'OpenCRM.Services.Interfaces') -and
            $targetProject.Name -ne 'OpenCRM.Domain' -and
            $targetProject.Name -notlike '*.Contracts')
        {
            throw "Контракт ссылается на реализацию: $($project.Name) -> $($targetProject.Name)."
        }

        if (!$project.IsTest -and $project.Name -like 'OpenCRM.Modules.*' -and $project.Name -notlike '*.Contracts' -and
            ($targetProject.Name -eq 'OpenCRM.Infrastructure.Common' -or
                ($targetProject.Name -like 'OpenCRM.Modules.*' -and $targetProject.Name -notlike '*.Contracts')))
        {
            throw "Модуль зависит от другой реализации: $($project.Name) -> $($targetProject.Name)."
        }
    }

    if ($project.IsTest)
    {
        $isInTestsFolder = @($testsFolders | Where-Object { $solution.Contains("$($project.Id) = $_") }).Count -gt 0
        if (!$isInTestsFolder)
        {
            throw "Тестовый проект вне папки Tests: $($project.Name)."
        }

        foreach ($configuration in @('Debug', 'Release'))
        {
            if (!$solution.Contains("$($project.Id).$configuration|Any CPU.Build.0 = $configuration|Any CPU"))
            {
                throw "Тестовый проект не включён в сборку ${configuration}: $($project.Name)."
            }
        }
    }
}

$visiting = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Visit-Project([string] $path)
{
    if ($visited.Contains($path))
    {
        return
    }

    if (!$visiting.Add($path))
    {
        throw "Цикл зависимостей у проекта $($projects[$path].Name)."
    }

    foreach ($target in $graph[$path])
    {
        Visit-Project $target
    }

    [void] $visiting.Remove($path)
    [void] $visited.Add($path)
}

foreach ($path in $projects.Keys)
{
    Visit-Project $path
}

$repositoryOwners = @{
    IVehicleRepository = 'OpenCRM.Modules.Vehicles'
    IMaintenanceRepository = 'OpenCRM.Modules.Maintenance'
}
foreach ($path in $projects.Keys)
{
    $project = $projects[$path]
    if ($project.IsTestSupport -or $project.Name -notlike 'OpenCRM.Modules.*' -or $project.Name -like '*.Contracts')
    {
        continue
    }

    $sources = Get-ChildItem -LiteralPath ([IO.Path]::GetDirectoryName($path)) -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }
    foreach ($source in $sources)
    {
        $content = [IO.File]::ReadAllText($source.FullName)
        foreach ($repository in $repositoryOwners.Keys)
        {
            if ($project.Name -ne $repositoryOwners[$repository] -and $content -match "\b$repository\b")
            {
                throw "Модуль использует репозиторий чужого владельца: $($project.Name) -> $repository ($($source.FullName))."
            }
        }
    }
}

Write-Output "Границы проверены: $($projects.Count) проектов, $referenceCount ссылок, циклов, запрещённых связей и межмодульных обращений к репозиториям техники/обслуживания нет."
