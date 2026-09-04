<#
.SYNOPSIS
    发布 ControllerFlow self-contained win-x64 安装目录（须在 Windows 或启用 EnableWindowsTargeting 的主机上执行）。
.DESCRIPTION
    产出：artifacts/publish/ControllerFlow.exe 及其运行所需的全部文件。
    分发包：artifacts/ControllerFlow-win-x64.zip（可选）。
#>
param(
    [string]$Configuration = "Release",
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "artifacts\publish"

Write-Host "==> Publishing ControllerFlow ($Configuration, self-contained win-x64)"
dotnet publish (Join-Path $root "src\ControllerFlow.App\ControllerFlow.App.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -o $publishDir `
    -p:PublishSingleFile=false `
    -p:EnableWindowsTargeting=true

if (-not $SkipZip) {
    $zip = Join-Path $root "artifacts\ControllerFlow-win-x64.zip"
    Write-Host "==> Packaging $zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zip
}

Write-Host "==> Done. 发布目录：$publishDir"
Write-Host "    安装：将发布目录整体复制到目标机器，运行 ControllerFlow.exe。"