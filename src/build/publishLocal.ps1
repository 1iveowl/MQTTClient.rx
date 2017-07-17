param([string]$betaver)

if ([string]::IsNullOrEmpty($betaver)) {
	$version = [Reflection.AssemblyName]::GetAssemblyName((resolve-path '..\interface\IMQTTClient.rx\bin\Release\netstandard1.3\IMQTTClientRx.dll')).Version.ToString(3)
	}
else {
	$version = [Reflection.AssemblyName]::GetAssemblyName((resolve-path '..\interface\IMQTTClient.rx\bin\Release\netstandard1.3\IMQTTClientRx.dll')).Version.ToString(3) + "-" + $betaver
}

.\build.ps1 $version

nuget.exe push -Source "1iveowlNuGetRepo" -ApiKey key ".\NuGet\MQTTClientRx.$version.symbols.nupkg"
