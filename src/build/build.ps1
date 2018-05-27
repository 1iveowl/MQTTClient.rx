#################################################
## IMPORTANT: Using MS Build Tool for VS 2017! ##
## This project include C# 7.0 features        ##
#################################################

param([string]$version)

if ([string]::IsNullOrEmpty($version)) {$version = "0.0.1"}

$msbuild = "C:\Program Files (x86)\Microsoft Visual Studio\2017\Enterprise\MSBuild\15.0\Bin\MSBuild.exe"
&$msbuild ..\interface\IMQTTClient.rx\IMQTTClientRx.csproj /t:Build /p:Configuration="Release"
#&$msbuild ..\main\MQTTClient.rx\MQTTClientRx.csproj /t:Build /p:Configuration="Release"
&$msbuild ..\main\MQTTClientRx.netstandard20\MQTTClientRx.netstandard20.csproj /t:Build /p:Configuration="Release"
#&$msbuild ..\main\MQTTClientRx.UWP\MQTTClientRx.UWP.csproj /t:Build /p:Configuration="Release"


Remove-Item .\NuGet -Force -Recurse
New-Item -ItemType Directory -Force -Path .\NuGet
c:\tools\nuget\NuGet.exe pack MQTTClientRx.nuspec -Verbosity detailed -Symbols -OutputDir "NuGet" -Version $version