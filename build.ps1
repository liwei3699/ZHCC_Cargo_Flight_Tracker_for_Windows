# 货机航班动态 - 发布脚本
# 用法：
#   pwsh -File build.ps1              # 发布文件夹版（推荐，最稳）到 dist\货机航班动态\ 并打包 zip
#   pwsh -File build.ps1 -SingleFile  # 额外发布单文件 exe（首次运行会把原生库解包到 %TEMP%）
#   pwsh -File build.ps1 -SkipClean   # 保留上次的 obj/bin 加快速度
#
# 为什么默认是文件夹版：单文件 exe 启动时要把 wpfgfx/PresentationNative 等原生库解到
# %TEMP%\.net\...，在 TEMP 不可写、漫游配置或安全软件拦截的机器上会“双击没反应”。
# 文件夹版不解包，任何机器直接双击就能跑。
[CmdletBinding()]
param(
    [switch]$SkipClean,
    [switch]$SingleFile,
    [switch]$NoCompress,
    [switch]$SkipZip,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\ZhccCargoBoard.App\ZhccCargoBoard.App.csproj'
$dist = Join-Path $root 'dist'
$appDir = Join-Path $dist '货机航班动态'
$appExeName = '货机航班动态.exe'

Write-Host '== 货机航班动态 发布 ==' -ForegroundColor Cyan

if (-not (Test-Path $project)) {
    throw "找不到项目文件：$project"
}

if (-not $SkipClean) {
    Write-Host '[1/5] 清理旧输出...'
    dotnet clean $project -c $Configuration -v q --nologo | Out-Null
    if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
}
New-Item -ItemType Directory -Force -Path $dist | Out-Null

Write-Host '[2/5] 发布自包含版本（win-x64，文件夹版，免安装 .NET）...'
$publishArgs = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:DebugType=none',
    '-p:SatelliteResourceLanguages=zh-Hans',
    '-o', $appDir,
    '--nologo', '-v', 'q'
)
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（exit $LASTEXITCODE）" }

Write-Host '[3/5] 整理发布目录...'
$built = Join-Path $appDir 'ZhccCargoBoard.App.exe'
if (-not (Test-Path $built)) { throw "没有生成 $built" }
Move-Item $built (Join-Path $appDir $appExeName) -Force

Get-ChildItem $appDir -File | Where-Object { $_.Name -match '\.(pdb|xml)$' } | Remove-Item -Force

Write-Host '[4/5] 复制说明文件...'
$readme = Join-Path $root '使用说明.txt'
if (Test-Path $readme) { Copy-Item $readme $appDir -Force }

if ($SingleFile) {
    Write-Host '[附加] 发布单文件 exe...'
    $oneFileDir = Join-Path $dist '单文件版'
    $oneFileArgs = @(
        'publish', $project,
        '-c', $Configuration,
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=none',
        '-p:SatelliteResourceLanguages=zh-Hans',
        '-o', $oneFileDir,
        '--nologo', '-v', 'q'
    )
    if (-not $NoCompress) { $oneFileArgs += '-p:EnableCompressionInSingleFile=true' }
    dotnet @oneFileArgs
    if ($LASTEXITCODE -ne 0) { throw "单文件发布失败（exit $LASTEXITCODE）" }
    Move-Item (Join-Path $oneFileDir 'ZhccCargoBoard.App.exe') (Join-Path $oneFileDir $appExeName) -Force
    if (Test-Path $readme) { Copy-Item $readme $oneFileDir -Force }
}

Write-Host '[5/5] 打包 zip...'
if (-not $SkipZip) {
    $zip = Join-Path $dist '货机航班动态_免安装版.zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path $appDir -DestinationPath $zip -CompressionLevel Optimal
}

Write-Host ''
$exe = Get-Item (Join-Path $appDir $appExeName)
$dirMb = [math]::Round(((Get-ChildItem $appDir -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host ("输出目录：{0}" -f $appDir) -ForegroundColor Green
Write-Host ("目录大小：{0} MB（解压后）；exe {1} MB" -f $dirMb, [math]::Round($exe.Length / 1MB, 1)) -ForegroundColor Green
if (-not $SkipZip) {
    $zipInfo = Get-Item (Join-Path $dist '货机航班动态_免安装版.zip')
    Write-Host ("压缩包：{0}（{1} MB）" -f $zipInfo.FullName, [math]::Round($zipInfo.Length / 1MB, 1)) -ForegroundColor Green
}
Write-Host '分发方式：把 zip 拷给别人，解压后双击“货机航班动态.exe”。整目录一起用，别只拿走 exe。'
