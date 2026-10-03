# 一键打包：生成可直接运行的程序压缩包（自带 .NET 运行时，单个 exe，目标电脑无需安装任何东西）
# 用法：在本目录右键“使用 PowerShell 运行”，或在终端执行  powershell -ExecutionPolicy Bypass -File 打包.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publish = Join-Path $root 'publish'
$version = ([xml](Get-Content (Join-Path $root 'tingshu\tingshu.csproj') -Encoding UTF8)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$out = Join-Path $publish "随身听书-$version"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory $out | Out-Null

# 1. 听书源插件
Write-Host '编译听书源插件…' -ForegroundColor Cyan
dotnet build (Join-Path $root 'sources\ShunSources.Plugin') -c Release -v q -nologo
if ($LASTEXITCODE -ne 0) { throw '插件编译失败' }
$pluginDll = Join-Path $root 'sources\ShunSources.Plugin\bin\Release\net8.0\ShunSources.Plugin.dll'
Compress-Archive -Path $pluginDll -DestinationPath (Join-Path $out '听书源插件-ShunSources.Plugin.zip') -Force

# 2. 程序：x64（绝大多数电脑）、x86（32 位旧电脑）、arm64（ARM 笔记本）
foreach ($arch in 'win-x64', 'win-x86', 'win-arm64') {
    Write-Host "发布 $arch …" -ForegroundColor Cyan
    dotnet publish (Join-Path $root 'tingshu') "-p:PublishProfile=$arch" -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw "$arch 发布失败" }

    # 给用户的包只放程序本身
    $stage = Join-Path $env:TEMP "suishentingshu-$arch"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory $stage | Out-Null
    Copy-Item (Join-Path $publish "$arch\TingShu.exe") (Join-Path $stage '随身听书.exe')
    Compress-Archive -Path "$stage\*" -DestinationPath (Join-Path $out "随身听书-$version-$arch.zip") -Force
    Remove-Item $stage -Recurse -Force
}

# 3. 插件开发包：SDK 和开发文档
Compress-Archive -Path (Join-Path $publish 'win-x64\sdk'), (Join-Path $publish 'win-x64\docs') -DestinationPath (Join-Path $out "插件开发SDK-$version.zip") -Force

Write-Host "`n完成，输出目录：$out" -ForegroundColor Green
Get-ChildItem $out | ForEach-Object { '{0,-40} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
