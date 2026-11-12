# 运行 Core 自检（不依赖测试框架，跑完看退出码）
#   pwsh -File tests\run-tests.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

# 受限环境（如受限用户/沙箱）下 TEMP 可能不可写，统一指到工作区
$tmp = Join-Path $root '.localtmp'
New-Item -ItemType Directory -Force -Path (Join-Path $tmp 'tests'), (Join-Path $tmp 'temp') | Out-Null
$env:ZHCC_TEST_DIR = Join-Path $tmp 'tests'
$env:TEMP = Join-Path $tmp 'temp'
$env:TMP = Join-Path $tmp 'temp'

Write-Host '== 运行自检 ==' -ForegroundColor Cyan
dotnet run --project (Join-Path $root 'tests\ZhccCargoBoard.Tests\ZhccCargoBoard.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "自检失败（exit $LASTEXITCODE）" -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host '自检全部通过。' -ForegroundColor Green
