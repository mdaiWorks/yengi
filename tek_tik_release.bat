@echo off
chcp 65001 > nul
echo ============================================================
echo 🚀 YENGI - TEK TIKLA OTOMATİK RELEASE SİSTEMİ
echo ============================================================
python "%~dp0github_push.py"
pause
