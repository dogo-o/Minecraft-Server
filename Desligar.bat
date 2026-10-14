@echo off
set REMOTE=gdrive0
set PASTA_NUVEM=%REMOTE%:MinecraftSync

echo =========================================
echo   INICIANDO ROTINA DE DESLIGAMENTO
echo =========================================

echo [0/6] Verificando autenticacao do Google Drive...
rclone about %REMOTE%: >nul 2>&1
if %errorlevel% neq 0 (
    echo =======================================================
    echo [ERRO FATAL] O token do Google Drive expirou!
    echo O servidor CONTINUA LIGADO para voce nao perder dados.
    echo.
    echo PARA RESOLVER AGORA:
    echo 1. Abra o menu Iniciar do Windows e digite CMD
    echo 2. Cole este comando e aperte Enter:
    echo    rclone config reconnect %REMOTE%:
    echo 3. Apos o login, clique em "Parar e Salvar" novamente.
    echo =======================================================
    exit /b
)

echo Pausando e destruindo os containers do Docker para liberar arquivos...
docker compose down

echo [5/6] Enviando o mapa atualizado para o Google Drive...
rclone sync ./dados_mine %PASTA_NUVEM%/dados_mine -v

echo [EXTRA] Criando backup historico (.zip)...
if not exist "Backups" mkdir Backups
set DATETIME=%date:/=-%_%time:~0,2%-%time:~3,2%
set DATETIME=%DATETIME: =0%
powershell -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; [System.IO.Compression.ZipFile]::CreateFromDirectory('dados_mine', 'Backups\Backup_Mapa_%DATETIME%.zip')"

echo Limpando backups antigos do Drive (Lixeira de 30 dias)...
rclone delete %PASTA_NUVEM%/Backups/ -v

echo Movendo arquivo compactado para a nuvem...
rclone copy "Backups\Backup_Mapa_%DATETIME%.zip" %PASTA_NUVEM%/Backups/ -v

echo [6/6] Removendo a trava de acesso...
rclone delete %PASTA_NUVEM%/lock.txt
if exist lock.txt del lock.txt

echo =========================================
echo   SINCRONIZACAO E BACKUP CONCLUIDOS!
echo =========================================