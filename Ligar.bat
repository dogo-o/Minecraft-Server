@echo off
set REMOTE=gdrive0
set PASTA_NUVEM=%REMOTE%:MinecraftSync

echo =========================================
echo   INICIANDO ROTINA DE LIGAMENTO
echo =========================================

echo [0/6] Verificando autenticacao do Google Drive...
rclone about %REMOTE%: >nul 2>&1
if %errorlevel% neq 0 (
    echo =======================================================
    echo [ERRO FATAL] O token do Google Drive expirou!
    echo O script foi bloqueado para evitar perda de dados.
    echo.
    echo PARA RESOLVER AGORA:
    echo 1. Abra o menu Iniciar do Windows e digite CMD
    echo 2. Cole este comando e aperte Enter:
    echo    rclone config reconnect %REMOTE%:
    echo 3. Faca o login no navegador e feche a janela preta.
    echo 4. Tente ligar o servidor novamente pelo painel.
    echo =======================================================
    exit /b
)

echo [1/6] Verificando Docker...
docker info >nul 2>&1
if %errorlevel% neq 0 (
    echo O Docker esta fechado. Abrindo o Docker Desktop...
    powershell -WindowStyle Hidden -Command "Start-Process 'C:\Users\Usuario\AppData\Local\Programs\DockerDesktop\Docker Desktop.exe'"
    :aguarda_docker
    timeout /t 5 /nobreak >nul
    docker info >nul 2>&1
    if %errorlevel% neq 0 (
        echo Aguardando o motor do Docker carregar...
        goto aguarda_docker
    )
    echo Docker carregado com sucesso!
) else (
    echo Docker ja esta rodando!
)

echo [2/6] Verificando se o servidor ja esta em uso...
rclone ls %PASTA_NUVEM% | findstr "lock.txt" >nul
if %errorlevel%==0 (
    echo [ALERTA] O servidor ja esta rodando no PC de outra pessoa!
    echo PROCESSO ABORTADO.
    exit /b
)

echo [3/6] Verificando datas (Local vs Nuvem)...
set PULAR_DOWNLOAD=0
if exist ".\dados_mine\level.dat" (
    powershell -Command "$c_time = (rclone lsf -F t %PASTA_NUVEM%/dados_mine/level.dat); if ([string]::IsNullOrWhiteSpace($c_time)) { exit 0 };$cloud = [datetime]::ParseExact($c_time.Substring(0,19), 'yyyy-MM-dd HH:mm:ss',$null); $local = (Get-Item '.\dados_mine\level.dat').LastWriteTime; if ([math]::Abs(($local - $cloud).TotalMinutes) -lt 2) { exit 0 };$msgNuvem = 'Nuvem (' + $cloud.ToString('dd/MM/yyyy HH:mm') + ')';$msgLocal = 'Local (' + $local.ToString('dd/MM/yyyy HH:mm') + ')'; if ($local -gt $cloud) {$msgLocal += ' - Mais recente'; $msgNuvem += ' - Mais antigo'; } else {$msgNuvem += ' - Mais recente'; $msgLocal += ' - Mais antigo'; };$msg = 'Os saves estao diferentes!`n`n' + $msgNuvem + '`n' + $msgLocal + '`n`nVoce quer BAIXAR O DA NUVEM?`n`n[SIM] = Baixar da Nuvem (Apaga o local)`n[NAO] = Jogar Local (Ignora a nuvem)'; $wshell = New-Object -ComObject Wscript.Shell; $res =$wshell.Popup($msg, 0, 'Conflito de Mapa', 4 + 32 + 4096); if ($res -eq 7) { exit 2 } else { exit 0 }"
    
    if %errorlevel% equ 2 (
        echo [AVISO] Voce escolheu manter o mapa LOCAL.
        set PULAR_DOWNLOAD=1
    )
)

echo Derrubando containers fantasmas para destravar arquivos locais...
docker compose down >nul 2>&1

echo [4/6] Trancando o acesso na nuvem...
echo locked > lock.txt
rclone copy lock.txt %PASTA_NUVEM%/

if "%PULAR_DOWNLOAD%"=="0" (
    echo [5/6] Baixando a versao mais recente do mapa...
    rclone sync %PASTA_NUVEM%/dados_mine ./dados_mine -v
) else (
    echo [5/6] Download cancelado. Carregando os dados Locais!
)

echo [6/6] Subindo a infraestrutura do Docker...
docker compose up -d

echo.
echo ========================================================
echo   CONTAINERS INICIADOS!
echo   Aguarde o status do painel indicar que esta pronto...
echo ========================================================