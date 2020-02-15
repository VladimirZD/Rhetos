﻿# Documentation:
# https://docs.microsoft.com/en-us/nuget/reference/ps-reference/ps-ref-get-project
# https://docs.microsoft.com/en-us/dotnet/api/envdte.dte
# https://docs.microsoft.com/en-us/dotnet/api/envdte.projectitems.addfromfilecopy?view=visualstudiosdk-2017

$sourceFolder = "$PSScriptRoot\projectFiles"
$project = (Get-Project)
$projectFolder = (Get-Item $project.FullName).DirectoryName

Copy-Item -Path "$sourceFolder\Web.config" -Destination $projectFolder -Force
Copy-Item -Path "$sourceFolder\Rhetos Server DOM.linq" -Destination $projectFolder -Force
Copy-Item -Path "$sourceFolder\Rhetos Server SOAP.linq" -Destination $projectFolder -Force
Copy-Item -Path "$sourceFolder\Template.ConnectionStrings.config" -Destination $projectFolder -Force

$project.ProjectItems.AddFromFileCopy("$sourceFolder\RhetosService.svc");
$project.ProjectItems.AddFromFileCopy("$sourceFolder\Global.asax");
$project.ProjectItems.AddFromFileCopy("$sourceFolder\Default.aspx");
