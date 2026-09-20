@echo off
setlocal

cd /d "%~dp0"
set "PYTHON=%~dp0..\..\.venv\Scripts\python.exe"
set "PYTHONPATH=%~dp0src"
set "CATALOG=%~dp0catalog\tool-catalog.mdaiAgent-1.0.json"
set "TASKS=%~dp0input\tasks.example.jsonl"
set "CONFIG=%~dp0config.json"
set "OUTPUT=%~dp0output"
set "RAW=%~dp0raw"
set "CHECKPOINT=%~dp0checkpoints\state.json"
set "CONSENSUS=%OUTPUT%\consensus.jsonl"
set "REPORT=%~dp0reports\quality.json"
set "TRAINING=%OUTPUT%\training.jsonl"

if not exist "%PYTHON%" (
    echo [HATA] Python sanal ortami bulunamadi: %PYTHON%
    pause
    exit /b 1
)
if not exist "%CONFIG%" (
    echo [HATA] config.json bulunamadi.
    pause
    exit /b 1
)
if not exist "%TASKS%" (
    echo [HATA] tasks.example.jsonl bulunamadi.
    pause
    exit /b 1
)
if not exist "%CATALOG%" (
    echo [HATA] Tool catalog bulunamadi.
    pause
    exit /b 1
)

if not exist "%OUTPUT%" mkdir "%OUTPUT%"
if not exist "%RAW%" mkdir "%RAW%"
if not exist "%~dp0checkpoints" mkdir "%~dp0checkpoints"
if not exist "%~dp0reports" mkdir "%~dp0reports"

echo.
echo [1/2] Teacher modellerine gorevler gonderiliyor...
"%PYTHON%" -m mdai_router_trainer collect --config "%CONFIG%" --catalog "%CATALOG%" --tasks "%TASKS%" --output "%OUTPUT%" --checkpoint "%CHECKPOINT%" --providers gemini cerebras
if errorlevel 1 (
    echo [HATA] Teacher veri toplama adimi basarisiz oldu.
    pause
    exit /b 1
)

echo.
echo [2/2] Consensus, kalite raporu ve training.jsonl olusturuluyor...
"%PYTHON%" -m mdai_router_trainer aggregate --catalog "%CATALOG%" --input "%OUTPUT%" --output "%CONSENSUS%" --report "%REPORT%" --training "%TRAINING%"
if errorlevel 1 (
    echo [HATA] Consensus/rapor adimi basarisiz oldu.
    pause
    exit /b 1
)

echo.
echo ========================================
echo TAMAMLANDI
echo Gorevler: %TASKS%
echo Consensus: %CONSENSUS%
echo Quality report: %REPORT%
echo Training dataset: %TRAINING%
echo ========================================
echo.
pause
exit /b 0
