$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
dotnet run --project tests/Scribe.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw "Core tests failed." }
dotnet publish src/Scribe.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist/windows
if ($LASTEXITCODE -ne 0) { throw "Windows publish failed." }
Write-Host "Ready: $PSScriptRoot/dist/windows/RenpyScribe.exe"
