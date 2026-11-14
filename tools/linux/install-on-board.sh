#!/usr/bin/env bash
# 在板子上安装/更新「货机航班动态」查看器
# 用法：把 zhcc-board-linux-arm64.tar.gz 传到板子并解压到 ~/apps 后，执行：
#   bash ~/apps/zhcc-board/install-on-board.sh
set -euo pipefail

APP_DIR="${APP_DIR:-$HOME/apps/zhcc-board}"
SELF_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ "$SELF_DIR" != "$APP_DIR" ]; then
  echo "== 复制到 $APP_DIR =="
  mkdir -p "$(dirname "$APP_DIR")"
  rm -rf "$APP_DIR"
  cp -a "$SELF_DIR" "$APP_DIR"
fi

chmod +x "$APP_DIR/ZhccCargoBoard.Desktop"

DESKTOP_DIR="$HOME/.local/share/applications"
mkdir -p "$DESKTOP_DIR"

cat > "$DESKTOP_DIR/zhcc-board.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=货机航班动态
Comment=郑州新郑国际机场货运航班动态（只读查看）
Exec=$APP_DIR/ZhccCargoBoard.Desktop
Icon=$APP_DIR/app.png
Terminal=false
Categories=Utility;
StartupNotify=true
EOF
chmod +x "$DESKTOP_DIR/zhcc-board.desktop"

mkdir -p "$HOME/Desktop"
cp "$DESKTOP_DIR/zhcc-board.desktop" "$HOME/Desktop/货机航班动态.desktop"
chmod +x "$HOME/Desktop/货机航班动态.desktop"

echo "installed : $APP_DIR"
echo "launcher  : $DESKTOP_DIR/zhcc-board.desktop"
echo "desktop   : $HOME/Desktop/货机航班动态.desktop"
