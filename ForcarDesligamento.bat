@echo off
set REMOTE=gdrive0
set PASTA_NUVEM=%REMOTE%:MinecraftSync

echo =========================================
echo   INICIANDO DESLIGAMENTO FORCADO
echo =========================================

echo [1/3] Verificando conexao com o Drive...
rclone about %REMOTE%: >nul 2>&1
if %errorlevel% neq 0 (
    echo [AVISO] Drive desconectado! A trava na nuvem tera que ser removida manualmente depois.
)

echo [2/3] Destruindo containers sem salvar...
docker compose down

echo [3/3] Removendo travas de seguranca locais e na nuvem...
rclone delete %PASTA_NUVEM%/lock.txt >nul 2>&1
if exist lock.txt del lock.txt

echo =========================================
echo   SERVIDOR FECHADO! (Nenhum dado salvo)
echo =========================================