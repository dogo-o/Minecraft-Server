@echo off
set REMOTE=gdrive0
set PASTA_NUVEM=%REMOTE%:MinecraftSync

echo =========================================
echo   INICIANDO UPLOAD DE EMERGENCIA
echo =========================================

echo [0/3] Verificando autenticacao do Google Drive...
rclone about %REMOTE%: >nul 2>&1
if %errorlevel% neq 0 (
    echo =======================================================
    echo [ERRO FATAL] O token do Google Drive expirou!
    echo Para conseguir forcar o upload, voce precisa reconectar.
    echo.
    echo 1. Abra o menu Iniciar do Windows e digite CMD
    echo 2. Cole este comando e aperte Enter:
    echo    rclone config reconnect %REMOTE%:
    echo 3. Apos o login, clique no botao de Emergencia de novo.
    echo =======================================================
    exit /b
)

echo [1/3] Verificando status do Docker...
docker info >nul 2>&1
if %errorlevel% equ 0 (
    docker ps | findstr "mc_server" >nul
    if %errorlevel% equ 0 (
        echo Imagens ativas detectadas! Derrubando servidor com seguranca...
        docker compose down
    )
)

echo [2/3] Forcando upload do mapa para o Drive...
rclone sync ./dados_mine %PASTA_NUVEM%/dados_mine -v

echo [EXTRA] Criando backup de emergencia (.zip)...
if not exist "Backups" mkdir Backups
set DATETIME=%date:/=-%_%time:~0,2%-%time:~3,2%
set DATETIME=%DATETIME: =0%
powershell -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; [System.IO.Compression.ZipFile]::CreateFromDirectory('dados_mine', 'Backups\Backup_Emergencia_%DATETIME%.zip')"

echo Limpando backups antigos do Drive (Lixeira de 30 dias)...
rclone delete %PASTA_NUVEM%/Backups/ -v

echo Movendo arquivo compactado para a nuvem...
rclone copy "Backups\Backup_Emergencia_%DATETIME%.zip" %PASTA_NUVEM%/Backups/ -v

echo [3/3] Removendo travas de seguranca...
rclone delete %PASTA_NUVEM%/lock.txt
if exist lock.txt del lock.txt

echo =========================================
echo   SALVAMENTO FORCADO CONCLUIDO!
echo =========================================