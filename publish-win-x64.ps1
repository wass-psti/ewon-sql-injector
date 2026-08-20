$ErrorActionPreference = "Stop"

Write-Host "Restoring packages..."
dotnet restore

Write-Host "Publishing self-contained Windows x64 single-file executable..."
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

$exe = Join-Path $PSScriptRoot "bin\Release\net10.0-windows\win-x64\publish\EwonSqlInjector.exe"

if (Test-Path $exe) {
    Write-Host ""
    Write-Host "Publish successful:"
    Write-Host $exe
    Start-Process explorer.exe -ArgumentList "/select,`"$exe`""
} else {
    throw "Publish finished but the expected EXE was not found at $exe"
}
