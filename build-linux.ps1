<#
  构建板端（Orange Pi 5 / linux-arm64）发布包
  用法：pwsh -File build-linux.ps1 [-SkipPublish]
  产物：dist-linux\zhcc-board\（目录） + dist-linux\zhcc-board-linux-arm64.tar.gz
#>
[CmdletBinding()]
param(
    [switch]$SkipPublish,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\ZhccCargoBoard.Desktop\ZhccCargoBoard.Desktop.csproj'
$outDir = Join-Path $root 'dist-linux\zhcc-board'
$tar = Join-Path $root 'dist-linux\zhcc-board-linux-arm64.tar.gz'
$installer = Join-Path $root 'tools\linux\install-on-board.sh'

if (-not $SkipPublish) {
    Write-Host '[1/3] 发布 linux-arm64 自包含版本...'
    dotnet publish $project -c $Configuration -r linux-arm64 --self-contained true `
        -p:DebugType=none -p:SatelliteResourceLanguages=zh-Hans -o $outDir --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败" }
}

Write-Host '[2/3] 附带安装脚本与图标...'
if (Test-Path $installer) {
    Copy-Item $installer (Join-Path $outDir 'install-on-board.sh') -Force
}
$icon = Join-Path $root 'src\ZhccCargoBoard.Desktop\Assets\app.png'
if (Test-Path $icon) { Copy-Item $icon (Join-Path $outDir 'app.png') -Force }

Write-Host '[3/3] 打包 tar.gz...'
if (Test-Path $tar) { Remove-Item $tar -Force }
tar -czf $tar -C (Split-Path $outDir -Parent) 'zhcc-board'
if ($LASTEXITCODE -ne 0) { throw "tar 打包失败" }

$sizeMb = [math]::Round((Get-Item $tar).Length / 1MB, 1)
Write-Host ''
Write-Host ("包：{0}（{1} MB）" -f $tar, $sizeMb) -ForegroundColor Green
Write-Host '上板：scp dist-linux\zhcc-board-linux-arm64.tar.gz orangepi@<板子IP>:~/'
Write-Host '安装：ssh orangepi@<板子IP> "mkdir -p ~/apps && tar -xzf ~/zhcc-board-linux-arm64.tar.gz -C ~/apps && bash ~/apps/zhcc-board/install-on-board.sh"'
