# MineSync Pro — Aplicativo Desktop Nativo (.exe)

Gerenciador profissional nativo para Windows (**C# / .NET Framework 4.8**) feito para hospedar um servidor compartilhado de Minecraft em **Docker** com túnel **Playit.gg**, cliente **RCON** integrado e sincronização automática com o **Google Drive (`rclone`)** — sem interface web, sem scripts `.bat` e sem dependências externas.

---

## Como Instalar e Usar

### 1. Instalação via `MineSync-Setup.exe` (Recomendado)
1. Dê duplo clique em **`MineSync-Setup.exe`**.
2. Confirme a pasta de instalação (padrão: `%LOCALAPPDATA%\Programs\MineSyncPro`) e a pasta onde está o servidor (`docker-compose.yml` e `dados_mine`).
3. O instalador extrai o aplicativo, cria atalhos na **Área de Trabalho** e no **Menu Iniciar**, e registra o **MineSync Pro** nos Programas Instalados do Windows (com desinstalador em `--uninstall` que preserva seus mundos e backups).

### 2. Uso Portátil Direto
Você também pode abrir **`MineSync.exe`** diretamente de dentro da pasta do servidor.

### Pré-requisitos no PC Host
- **Windows 10 / 11** (.NET Framework 4.8 já incluso no Windows)
- **Docker Desktop** instalado
- **`rclone.exe`** e **`rclone.conf`** configurados na pasta do servidor

---

## Funcionalidades do MineSync Pro

- **Dashboard de Controle**:
  - **Iniciar Servidor**: valida o token do Google Drive, abre o Docker Desktop automaticamente se estiver fechado, verifica trava (`lock.txt`), compara timestamps de `dados_mine/world/level.dat` (abrindo diálogo nativo em caso de conflito), cria `lock.txt`, baixa o mapa atualizado e sobe `mc_server` + `mc_playit`.
  - **Parar e Salvar**: envia `save-all` via RCON, derruba os containers, sincroniza `dados_mine/` para o Google Drive, cria backup histórico `.zip` em `Backups/`, envia o backup à nuvem e remove `lock.txt`.
  - **Salvar Emergência** e **Fechar Sem Salvar**: recuperação rápida em caso de travamento ou descarte da sessão.
  - Monitoramento em tempo real de CPU, RAM, endereço do túnel Playit.gg (clique para copiar) e alerta sonoro quando o mundo fica pronto.
- **Console & RCON**:
  - Logs ao vivo do `mc_server` (com fallback para `dados_mine/logs/latest.log` quando offline) e `mc_playit`, filtro de texto, atalhos rápidos (`save-all`, `time set day/night`, `weather clear/rain`, `list`) e terminal RCON com histórico.
- **Jogadores**:
  - Lista de jogadores com status Online/Offline, OP, Whitelist, Tempo Jogado, Mobs Mortos, Mortes, Blocos Minerados e Distância percorrida, além de botões de OP, Whitelist, Gamemode e Kick.
- **Backups & Nuvem**:
  - Criação de snapshots `.zip` manuais, restauração segura (com servidor desligado), exclusão, visualização de backups no Drive, reconexão OAuth do `rclone` e remoção forçada da trava `lock.txt`.
- **Configurações**:
  - Editor visual para a pasta do servidor, parâmetros do Docker (`MEMORY`, `TYPE`, `VERSION`, `ONLINE_MODE`) em `docker-compose.yml` e propriedades de `dados_mine/server.properties`.

---

## Compilação a partir do Código-Fonte

O projeto compila nativamente no Windows usando o compilador `csc.exe` do .NET Framework:

```powershell
# Compilar o aplicativo
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -optimize -target:winexe -win32icon:server.ico -out:MineSync.exe -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll -r:System.Web.Extensions.dll src\MineSyncApp\Program.cs src\MineSyncApp\Core\ServerConfig.cs src\MineSyncApp\Core\RconClient.cs src\MineSyncApp\Core\PlayerAndBackupService.cs src\MineSyncApp\Core\WorkflowEngine.cs src\MineSyncApp\UI\Theme.cs src\MineSyncApp\UI\MainForm.cs

# Compilar o instalador com recursos embutidos
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -optimize -target:winexe -win32icon:server.ico -resource:MineSync.exe,MineSync.exe -resource:server.ico,server.ico -out:MineSync-Setup.exe -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll src\MineSyncInstaller\InstallerProgram.cs
```
