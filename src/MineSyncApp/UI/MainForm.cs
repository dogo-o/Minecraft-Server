using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Threading.Tasks;
using System.Windows.Forms;
using MineSyncApp.Core;

namespace MineSyncApp.UI
{
    public class MainForm : Form
    {
        private readonly ServerConfig _config;
        private readonly PlayerAndBackupService _playerBackupService;
        private readonly WorkflowEngine _engine;

        private readonly Timer _pollTimer;
        private readonly List<string> _rconHistory = new List<string>();
        private int _rconHistoryIdx = -1;
        private bool _soundEnabled = true;
        private bool _alreadyPlayedReadySound;
        private bool _isRefreshingStatus;
        private bool _allowExit;
        private bool _shownTrayBalloon;
        private string _activeTab = "dashboard";
        private string _currentPlayitAddress = "";

        private DockerStatusInfo _lastDocker = new DockerStatusInfo();
        private CloudStatusInfo _lastCloud = new CloudStatusInfo();
        private List<PlayerInfo> _cachedPlayers = new List<PlayerInfo>();

        // System Tray
        private NotifyIcon _trayIcon;
        private ToolStripMenuItem _trayMenuStart;
        private ToolStripMenuItem _trayMenuStop;

        // Sidebar & Header Controls
        private Panel _sidebarPanel;
        private readonly Dictionary<string, Button> _navButtons = new Dictionary<string, Button>();
        private Label _lblDockerSide;
        private Label _lblDriveSide;
        private Label _lblHeaderStatus;
        private Label _lblHeaderSub;
        private Panel _statusDot;
        private Button _btnPlayitPill;
        private Label _lblResourcesPill;
        private Label _lblLockPill;
        private Button _btnSoundToggle;

        // Content Host & Tabs
        private Panel _contentHost;
        private Panel _tabDashboard;
        private Panel _tabConsole;
        private Panel _tabPlayers;
        private Panel _tabBackups;
        private Panel _tabConfig;

        // Dashboard Controls
        private Button _btnStart;
        private Button _btnStop;
        private Button _btnEmergency;
        private Button _btnKill;
        private ProgressBar _workflowProgress;
        private Label _lblWorkflowStep;
        private Label _lblLocalDate;
        private Label _lblCloudDate;
        private Label _lblLockOwner;
        private Label _lblDriveAuth;
        private Label _lblQuotaText;
        private ProgressBar _quotaBar;
        private RichTextBox _rtbDashLog;
        private TextBox _txtDashRcon;

        // Console Tab Controls
        private ComboBox _cbLogSource;
        private TextBox _txtLogFilter;
        private CheckBox _chkAutoScroll;
        private RichTextBox _rtbFullLog;
        private TextBox _txtFullRcon;

        // Players Tab Controls
        private Label _lblPlayersSummary;
        private DataGridView _gridPlayers;
        private ComboBox _cbGamemodeSelect;

        // Backups Tab Controls
        private DataGridView _gridLocalBackups;
        private DataGridView _gridCloudBackups;
        private Button _btnCreateBackup;
        private CheckBox _chkLocalBackupEnabled;
        private Label _lblLocalBackupsDirInfo;

        // Config Tab Controls
        private TextBox _txtWorkspacePath;
        private TextBox _txtBackupsDirConfig;
        private CheckBox _chkLocalBackupConfig;
        private bool _syncingBackupControls;
        private ComboBox _cbMemory;
        private ComboBox _cbServerType;
        private TextBox _txtServerVersion;
        private ComboBox _cbOnlineMode;
        private TextBox _txtPlayitInfo;
        private TextBox _txtMotd;
        private ComboBox _cbDifficulty;
        private ComboBox _cbGamemode;
        private NumericUpDown _numMaxPlayers;
        private NumericUpDown _numViewDist;
        private NumericUpDown _numSimDist;
        private NumericUpDown _numSpawnProt;
        private ComboBox _cbAllowFlight;
        private ComboBox _cbWhitelist;
        private ComboBox _cbHardcore;
        private NumericUpDown _numRconPort;

        public MainForm()
        {
            _config = new ServerConfig();
            _playerBackupService = new PlayerAndBackupService(_config);
            _engine = new WorkflowEngine(_config, _playerBackupService);

            _engine.OnLog += Engine_OnLog;
            _engine.OnProgress += Engine_OnProgress;

            InitializeWindow();
            InitializeTray();
            BuildLayout();
            LoadConfigFields();
            SwitchTab("dashboard");

            _pollTimer = new Timer();
            _pollTimer.Interval = 3500;
            _pollTimer.Tick += async delegate { await RefreshAllAsync(false); };
            _pollTimer.Start();

            this.Shown += async delegate
            {
                await RefreshAllAsync(true);
                await RefreshLogsTabAsync();
            };
        }

        private void InitializeWindow()
        {
            this.Text = "MineSync Pro — Gerenciador de Servidor Minecraft";
            this.Size = new Size(1220, 800);
            this.MinimumSize = new Size(1020, 680);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Theme.BgBase;
            this.ForeColor = Theme.TextPrimary;
            this.Font = Theme.FontBase;

            try
            {
                string icoPath = Path.Combine(_config.WorkspaceDir, "server.ico");
                if (!File.Exists(icoPath))
                {
                    icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server.ico");
                }
                if (File.Exists(icoPath))
                {
                    this.Icon = new Icon(icoPath);
                }
                else
                {
                    this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
            }
            catch
            {
            }

            Theme.ApplyDarkTitleBar(this);
        }

        private void InitializeTray()
        {
            ContextMenuStrip trayMenu = new ContextMenuStrip();

            ToolStripMenuItem openItem = new ToolStripMenuItem("Abrir MineSync Pro");
            openItem.Font = new Font(trayMenu.Font, FontStyle.Bold);
            openItem.Click += delegate { RestoreFromTray(); };

            _trayMenuStart = new ToolStripMenuItem("Iniciar Servidor");
            _trayMenuStart.Click += async delegate
            {
                RestoreFromTray();
                await RunStartWorkflowAsync();
            };

            _trayMenuStop = new ToolStripMenuItem("Parar e Salvar");
            _trayMenuStop.Enabled = false;
            _trayMenuStop.Click += async delegate
            {
                RestoreFromTray();
                await RunWorkflowWithHandling(_engine.StopAndSaveServerAsync());
            };

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Sair Completamente");
            exitItem.Click += delegate { ExitApplication(); };

            trayMenu.Items.Add(openItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(_trayMenuStart);
            trayMenu.Items.Add(_trayMenuStop);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(exitItem);

            _trayIcon = new NotifyIcon();
            _trayIcon.Icon = this.Icon ?? SystemIcons.Application;
            _trayIcon.Text = "MineSync Pro — Servidor Minecraft";
            _trayIcon.ContextMenuStrip = trayMenu;
            _trayIcon.Visible = true;
            _trayIcon.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    RestoreFromTray();
                }
            };
            _trayIcon.DoubleClick += delegate { RestoreFromTray(); };
        }

        private void RestoreFromTray()
        {
            this.Show();
            this.ShowInTaskbar = true;
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.WindowState = FormWindowState.Normal;
            }
            this.Activate();
            this.BringToFront();
        }

        private void ExitApplication()
        {
            if (_engine.IsBusy)
            {
                DialogResult res = MessageBox.Show(
                    "Existe uma sincronização ou operação em andamento. Deseja realmente fechar o MineSync Pro?",
                    "Operação em Andamento",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );
                if (res != DialogResult.Yes) return;
            }
            _allowExit = true;
            this.Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !_allowExit)
            {
                e.Cancel = true;
                this.Hide();
                this.ShowInTaskbar = false;
                if (!_shownTrayBalloon && _trayIcon != null)
                {
                    _shownTrayBalloon = true;
                    _trayIcon.ShowBalloonTip(
                        3000,
                        "MineSync Pro",
                        "O painel foi minimizado na bandeja do sistema (flechinha ^ perto do relógio).",
                        ToolTipIcon.Info
                    );
                }
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer.Dispose();
            }
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            base.OnFormClosed(e);
        }

        private void BuildLayout()
        {
            // Sidebar
            _sidebarPanel = new Panel();
            _sidebarPanel.Dock = DockStyle.Left;
            _sidebarPanel.Width = 220;
            _sidebarPanel.BackColor = Theme.BgSidebar;
            _sidebarPanel.Padding = new Padding(12);

            Label lblBrand = Theme.CreateLabel("MineSync PRO", new Font("Segoe UI", 15f, FontStyle.Bold), Theme.Emerald);
            lblBrand.Location = new Point(14, 16);

            Label lblBrandSub = Theme.CreateLabel("Servidor Descentralizado", Theme.FontSmall, Theme.TextMuted);
            lblBrandSub.Location = new Point(15, 43);

            _sidebarPanel.Controls.Add(lblBrand);
            _sidebarPanel.Controls.Add(lblBrandSub);

            AddNavButton("dashboard", "Dashboard", 82);
            AddNavButton("console", "Console & RCON", 126);
            AddNavButton("players", "Jogadores", 170);
            AddNavButton("backups", "Backups & Nuvem", 214);
            AddNavButton("config", "Configurações", 258);

            // Sidebar footer
            Panel sideFooter = new Panel();
            sideFooter.Dock = DockStyle.Bottom;
            sideFooter.Height = 132;
            sideFooter.BackColor = Theme.BgSidebar;

            _lblDockerSide = Theme.CreateLabel("Docker: Verificando...", Theme.FontSmall, Theme.TextMuted);
            _lblDockerSide.Location = new Point(6, 6);

            _lblDriveSide = Theme.CreateLabel("Google Drive: Verificando...", Theme.FontSmall, Theme.TextMuted);
            _lblDriveSide.Location = new Point(6, 28);

            Label lblAppVer = Theme.CreateLabel("Aplicativo Nativo v2.0", Theme.FontSmall, Theme.TextDim);
            lblAppVer.Location = new Point(6, 52);

            Button btnExitSide = Theme.CreateButton("Sair do App", Theme.BgElevated, Theme.TextMuted);
            btnExitSide.Size = new Size(194, 32);
            btnExitSide.Location = new Point(2, 82);
            btnExitSide.Font = Theme.FontSmall;
            btnExitSide.Click += delegate { ExitApplication(); };

            sideFooter.Controls.Add(_lblDockerSide);
            sideFooter.Controls.Add(_lblDriveSide);
            sideFooter.Controls.Add(lblAppVer);
            sideFooter.Controls.Add(btnExitSide);
            _sidebarPanel.Controls.Add(sideFooter);

            // Top Header (Simple Panel with explicit Padding keeps pills vertically centered and unclipped)
            Panel topBar = new Panel();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 68;
            topBar.BackColor = Theme.BgSidebar;
            topBar.Padding = new Padding(14, 16, 14, 16);
            topBar.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.DrawLine(new Pen(Theme.Border), 0, topBar.Height - 1, topBar.Width, topBar.Height - 1);
            };

            Panel statusPanel = new Panel();
            statusPanel.Dock = DockStyle.Fill;

            _statusDot = new Panel();
            _statusDot.Size = new Size(12, 12);
            _statusDot.Location = new Point(4, 12);
            _statusDot.BackColor = Theme.Rose;

            _lblHeaderStatus = Theme.CreateLabel("Checando infraestrutura...", Theme.FontBold, Theme.TextPrimary);
            _lblHeaderStatus.Location = new Point(24, 0);

            _lblHeaderSub = Theme.CreateLabel("Aguardando Docker e Google Drive", Theme.FontSmall, Theme.TextMuted);
            _lblHeaderSub.Location = new Point(24, 19);

            statusPanel.Controls.Add(_statusDot);
            statusPanel.Controls.Add(_lblHeaderStatus);
            statusPanel.Controls.Add(_lblHeaderSub);

            FlowLayoutPanel pillsFlow = new FlowLayoutPanel();
            pillsFlow.Dock = DockStyle.Right;
            pillsFlow.AutoSize = true;
            pillsFlow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            pillsFlow.FlowDirection = FlowDirection.LeftToRight;
            pillsFlow.WrapContents = false;
            pillsFlow.Margin = new Padding(0);
            pillsFlow.Padding = new Padding(0);

            _btnPlayitPill = Theme.CreateButton("Playit: Offline", Theme.BgElevated, Theme.Cyan);
            _btnPlayitPill.Size = new Size(148, 32);
            _btnPlayitPill.Font = Theme.FontSmall;
            _btnPlayitPill.Margin = new Padding(4, 0, 4, 0);
            _btnPlayitPill.Click += delegate
            {
                if (!string.IsNullOrEmpty(_currentPlayitAddress))
                {
                    Clipboard.SetText(_currentPlayitAddress);
                    AppendDashLog("Endereço Playit copiado: " + _currentPlayitAddress, "success");
                }
            };

            _lblResourcesPill = CreatePillLabel("CPU/RAM: —", 125);
            _lblLockPill = CreatePillLabel("Trava: —", 120);

            _btnSoundToggle = Theme.CreateButton("Som: ON", Theme.BgElevated, Theme.Emerald);
            _btnSoundToggle.Size = new Size(74, 32);
            _btnSoundToggle.Font = Theme.FontSmall;
            _btnSoundToggle.Margin = new Padding(4, 0, 4, 0);
            _btnSoundToggle.Click += delegate
            {
                _soundEnabled = !_soundEnabled;
                _btnSoundToggle.Text = _soundEnabled ? "Som: ON" : "Som: OFF";
                _btnSoundToggle.ForeColor = _soundEnabled ? Theme.Emerald : Theme.TextMuted;
            };

            Button btnRefreshTop = Theme.CreateButton("Atualizar", Theme.BgElevated, Theme.TextPrimary);
            btnRefreshTop.Size = new Size(80, 32);
            btnRefreshTop.Font = Theme.FontSmall;
            btnRefreshTop.Margin = new Padding(4, 0, 0, 0);
            btnRefreshTop.Click += async delegate { await RefreshAllAsync(true); };

            pillsFlow.Controls.Add(_btnPlayitPill);
            pillsFlow.Controls.Add(_lblResourcesPill);
            pillsFlow.Controls.Add(_lblLockPill);
            pillsFlow.Controls.Add(_btnSoundToggle);
            pillsFlow.Controls.Add(btnRefreshTop);

            topBar.Controls.Add(statusPanel);
            topBar.Controls.Add(pillsFlow);

            // Content host
            _contentHost = new Panel();
            _contentHost.Dock = DockStyle.Fill;
            _contentHost.BackColor = Theme.BgBase;
            _contentHost.Padding = new Padding(14);

            _tabDashboard = BuildDashboardTab();
            _tabConsole = BuildConsoleTab();
            _tabPlayers = BuildPlayersTab();
            _tabBackups = BuildBackupsTab();
            _tabConfig = BuildConfigTab();

            _contentHost.Controls.Add(_tabDashboard);
            _contentHost.Controls.Add(_tabConsole);
            _contentHost.Controls.Add(_tabPlayers);
            _contentHost.Controls.Add(_tabBackups);
            _contentHost.Controls.Add(_tabConfig);

            // Correct docking order: Fill added first, then Top/Left added after
            this.Controls.Add(_contentHost);
            this.Controls.Add(topBar);
            this.Controls.Add(_sidebarPanel);
        }

        private void AddNavButton(string key, string label, int top)
        {
            Button btn = new Button();
            btn.Text = "  " + label;
            btn.UseMnemonic = false;
            btn.TextAlign = ContentAlignment.MiddleLeft;
            btn.Location = new Point(12, top);
            btn.Size = new Size(196, 38);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.Font = Theme.FontBold;
            btn.Cursor = Cursors.Hand;
            btn.Click += delegate { SwitchTab(key); };
            _sidebarPanel.Controls.Add(btn);
            _navButtons[key] = btn;
        }

        private static Label CreatePillLabel(string text, int width)
        {
            Label lbl = new Label();
            lbl.Text = text;
            lbl.UseMnemonic = false;
            lbl.AutoSize = false;
            lbl.Size = new Size(width, 32);
            lbl.Margin = new Padding(4, 0, 4, 0);
            lbl.TextAlign = ContentAlignment.MiddleCenter;
            lbl.BackColor = Theme.BgElevated;
            lbl.ForeColor = Theme.TextMuted;
            lbl.Font = Theme.FontSmall;
            lbl.BorderStyle = BorderStyle.FixedSingle;
            return lbl;
        }

        private void SwitchTab(string key)
        {
            _activeTab = key;
            foreach (KeyValuePair<string, Button> kv in _navButtons)
            {
                bool active = kv.Key == key;
                kv.Value.BackColor = active ? Color.FromArgb(16, 65, 50) : Theme.BgSidebar;
                kv.Value.ForeColor = active ? Theme.Emerald : Theme.TextMuted;
            }

            _tabDashboard.Visible = (key == "dashboard");
            _tabConsole.Visible = (key == "console");
            _tabPlayers.Visible = (key == "players");
            _tabBackups.Visible = (key == "backups");
            _tabConfig.Visible = (key == "config");

            if (key == "console") { var _t = RefreshLogsTabAsync(); }
            if (key == "players") { var _t = RefreshPlayersTabAsync(); }
            if (key == "backups") { var _t = RefreshBackupsTabAsync(); }
            if (key == "config") { LoadConfigFields(); }
        }

        private Panel BuildDashboardTab()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 264f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            TableLayoutPanel topGrid = new TableLayoutPanel();
            topGrid.Dock = DockStyle.Fill;
            topGrid.ColumnCount = 2;
            topGrid.RowCount = 1;
            topGrid.Margin = new Padding(0, 0, 0, 10);
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));

            // Left Card: Controls & Progress
            CardPanel heroCard = new CardPanel();
            heroCard.Dock = DockStyle.Fill;
            heroCard.Margin = new Padding(0, 0, 8, 0);

            TableLayoutPanel heroLayout = new TableLayoutPanel();
            heroLayout.Dock = DockStyle.Fill;
            heroLayout.ColumnCount = 1;
            heroLayout.RowCount = 4;
            heroLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));
            heroLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            heroLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
            heroLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));

            TableLayoutPanel btnGrid = new TableLayoutPanel();
            btnGrid.Dock = DockStyle.Fill;
            btnGrid.ColumnCount = 2;
            btnGrid.RowCount = 2;
            btnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            btnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            btnGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            btnGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            _btnStart = Theme.CreateButton("▶  Iniciar Servidor", Theme.EmeraldDark, Color.White);
            _btnStart.Dock = DockStyle.Fill;
            _btnStart.Margin = new Padding(0, 0, 5, 5);
            _btnStart.Click += async delegate { await RunStartWorkflowAsync(); };

            _btnStop = Theme.CreateButton("⏹  Parar e Salvar", Theme.Rose, Color.White);
            _btnStop.Dock = DockStyle.Fill;
            _btnStop.Margin = new Padding(5, 0, 0, 5);
            _btnStop.Enabled = false;
            _btnStop.Click += async delegate { await RunWorkflowWithHandling(_engine.StopAndSaveServerAsync()); };

            _btnEmergency = Theme.CreateButton("⚠  Salvar Emergência", Theme.Amber, Color.White);
            _btnEmergency.Dock = DockStyle.Fill;
            _btnEmergency.Margin = new Padding(0, 5, 5, 0);
            _btnEmergency.Click += async delegate
            {
                DialogResult res = MessageBox.Show(
                    "Use esta opção apenas se o PC crashou ou o servidor travou antes de salvar.\n\nEle derrubará o Docker, enviará os arquivos locais ao Google Drive e gerará um backup .zip.\n\nDeseja continuar?",
                    "Salvamento de Emergência",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );
                if (res == DialogResult.Yes) await RunWorkflowWithHandling(_engine.EmergencySaveAsync());
            };

            _btnKill = Theme.CreateButton("☠  Fechar Sem Salvar", Theme.Crimson, Color.White);
            _btnKill.Dock = DockStyle.Fill;
            _btnKill.Margin = new Padding(5, 5, 0, 0);
            _btnKill.Enabled = false;
            _btnKill.Click += async delegate
            {
                DialogResult res = MessageBox.Show(
                    "PERIGO!\n\nTodo o progresso desta sessão será perdido e apenas a trava lock.txt será removida da nuvem.\n\nDeseja realmente desligar sem salvar?",
                    "Fechar Sem Salvar",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Error
                );
                if (res == DialogResult.Yes) await RunWorkflowWithHandling(_engine.ForceKillAsync());
            };

            btnGrid.Controls.Add(_btnStart, 0, 0);
            btnGrid.Controls.Add(_btnStop, 1, 0);
            btnGrid.Controls.Add(_btnEmergency, 0, 1);
            btnGrid.Controls.Add(_btnKill, 1, 1);

            _lblWorkflowStep = Theme.CreateLabel("Aguardando comando...", Theme.FontSmall, Theme.TextMuted);
            _lblWorkflowStep.AutoSize = false;
            _lblWorkflowStep.Dock = DockStyle.Fill;
            _lblWorkflowStep.TextAlign = ContentAlignment.BottomLeft;

            _workflowProgress = new ProgressBar();
            _workflowProgress.Dock = DockStyle.Fill;
            _workflowProgress.Maximum = 100;

            heroLayout.Controls.Add(Theme.CreateCardHeader("Controle do Servidor", "Gerencie Docker, Playit.gg e sincronização em um clique", null), 0, 0);
            heroLayout.Controls.Add(btnGrid, 0, 1);
            heroLayout.Controls.Add(_lblWorkflowStep, 0, 2);
            heroLayout.Controls.Add(_workflowProgress, 0, 3);
            heroCard.Controls.Add(heroLayout);

            // Right Card: Cloud Sync
            CardPanel cloudCard = new CardPanel();
            cloudCard.Dock = DockStyle.Fill;
            cloudCard.Margin = new Padding(8, 0, 0, 0);

            TableLayoutPanel cloudLayout = new TableLayoutPanel();
            cloudLayout.Dock = DockStyle.Fill;
            cloudLayout.ColumnCount = 1;
            cloudLayout.RowCount = 3;
            cloudLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));
            cloudLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            cloudLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));

            Button btnCheckDrive = Theme.CreateButton("Checar Agora", Theme.BgElevated, Theme.TextPrimary);
            btnCheckDrive.Size = new Size(108, 30);
            btnCheckDrive.Font = Theme.FontSmall;
            btnCheckDrive.Click += async delegate { await RefreshAllAsync(true); };

            TableLayoutPanel infoGrid = new TableLayoutPanel();
            infoGrid.Dock = DockStyle.Fill;
            infoGrid.ColumnCount = 2;
            infoGrid.RowCount = 5;
            infoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145f));
            infoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int r = 0; r < 5; r++)
            {
                infoGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));
            }

            _lblLocalDate = AddInfoRow(infoGrid, "Save Local:", "—", 0);
            _lblCloudDate = AddInfoRow(infoGrid, "Save na Nuvem:", "—", 1);
            _lblLockOwner = AddInfoRow(infoGrid, "Trava (lock.txt):", "—", 2);
            _lblDriveAuth = AddInfoRow(infoGrid, "Autenticação Drive:", "—", 3);
            _lblQuotaText = AddInfoRow(infoGrid, "Espaço no Drive:", "—", 4);

            _quotaBar = new ProgressBar();
            _quotaBar.Dock = DockStyle.Fill;
            _quotaBar.Maximum = 100;

            cloudLayout.Controls.Add(Theme.CreateCardHeader("Sincronização & Google Drive", "Comparação Local vs Nuvem (world/level.dat)", btnCheckDrive), 0, 0);
            cloudLayout.Controls.Add(infoGrid, 0, 1);
            cloudLayout.Controls.Add(_quotaBar, 0, 2);
            cloudCard.Controls.Add(cloudLayout);

            topGrid.Controls.Add(heroCard, 0, 0);
            topGrid.Controls.Add(cloudCard, 1, 0);

            // Bottom Card: Live Terminal + Command Bar
            CardPanel logCard = new CardPanel();
            logCard.Dock = DockStyle.Fill;

            TableLayoutPanel logLayout = new TableLayoutPanel();
            logLayout.Dock = DockStyle.Fill;
            logLayout.ColumnCount = 1;
            logLayout.RowCount = 3;
            logLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));
            logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            logLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));

            Button btnExpandConsole = Theme.CreateButton("Abrir Console Completo", Theme.BgElevated, Theme.Cyan);
            btnExpandConsole.Size = new Size(168, 30);
            btnExpandConsole.Font = Theme.FontSmall;
            btnExpandConsole.Click += delegate { SwitchTab("console"); };

            _rtbDashLog = new RichTextBox();
            _rtbDashLog.Dock = DockStyle.Fill;
            _rtbDashLog.BackColor = Theme.BgInput;
            _rtbDashLog.ForeColor = Color.FromArgb(203, 213, 225);
            _rtbDashLog.Font = Theme.FontMono;
            _rtbDashLog.BorderStyle = BorderStyle.FixedSingle;
            _rtbDashLog.ReadOnly = true;

            _txtDashRcon = Theme.CreateTextBox(false);
            Panel dashCmdBar = BuildCommandBar(
                _txtDashRcon,
                "Digite um comando do servidor (ex: /op nick, /gamemode creative nick, /time set day, /say ola)...",
                async delegate
                {
                    string cmd = _txtDashRcon.Text.Trim();
                    _txtDashRcon.Text = "";
                    await ExecuteRconFromUiAsync(cmd);
                }
            );

            logLayout.Controls.Add(Theme.CreateCardHeader("Terminal Ao Vivo & Comandos", "Logs em tempo real e envio de comandos como /op nick", btnExpandConsole), 0, 0);
            logLayout.Controls.Add(_rtbDashLog, 0, 1);
            logLayout.Controls.Add(dashCmdBar, 0, 2);
            logCard.Controls.Add(logLayout);

            root.Controls.Add(topGrid, 0, 0);
            root.Controls.Add(logCard, 0, 1);
            return root;
        }

        private Panel BuildCommandBar(TextBox inputBox, string placeholder, Func<Task> onSendAsync)
        {
            TableLayoutPanel bar = new TableLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.ColumnCount = 3;
            bar.RowCount = 1;
            bar.Margin = new Padding(0);
            bar.Padding = new Padding(0, 4, 0, 0);
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146f));
            bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Label lblPrompt = Theme.CreateLabel("Comando /:", Theme.FontBold, Theme.Emerald);
            lblPrompt.Anchor = AnchorStyles.Left;
            lblPrompt.Margin = new Padding(0, 0, 8, 0);

            inputBox.Dock = DockStyle.Fill;
            inputBox.Font = Theme.FontMono;
            inputBox.Margin = new Padding(0, 4, 8, 4);
            Theme.SetPlaceholder(inputBox, placeholder);
            inputBox.KeyDown += async delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    await onSendAsync();
                }
                else if (e.KeyCode == Keys.Up && _rconHistoryIdx + 1 < _rconHistory.Count)
                {
                    _rconHistoryIdx++;
                    inputBox.Text = _rconHistory[_rconHistoryIdx];
                    inputBox.SelectionStart = inputBox.Text.Length;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Down)
                {
                    if (_rconHistoryIdx > 0)
                    {
                        _rconHistoryIdx--;
                        inputBox.Text = _rconHistory[_rconHistoryIdx];
                    }
                    else
                    {
                        _rconHistoryIdx = -1;
                        inputBox.Text = "";
                    }
                    e.SuppressKeyPress = true;
                }
            };

            Button btnSend = Theme.CreateButton("Enviar Comando", Theme.EmeraldDark, Color.White);
            btnSend.Dock = DockStyle.Fill;
            btnSend.Margin = new Padding(0);
            btnSend.Height = 32;
            btnSend.Click += async delegate { await onSendAsync(); };

            bar.Controls.Add(lblPrompt, 0, 0);
            bar.Controls.Add(inputBox, 1, 0);
            bar.Controls.Add(btnSend, 2, 0);
            return bar;
        }

        private static Label AddInfoRow(TableLayoutPanel grid, string keyText, string initialVal, int row)
        {
            Label lblKey = Theme.CreateLabel(keyText, Theme.FontBase, Theme.TextMuted);
            lblKey.Anchor = AnchorStyles.Left;

            Label lblVal = Theme.CreateLabel(initialVal, Theme.FontBold, Theme.TextPrimary);
            lblVal.AutoSize = false;
            lblVal.Dock = DockStyle.Fill;
            lblVal.TextAlign = ContentAlignment.MiddleLeft;

            grid.Controls.Add(lblKey, 0, row);
            grid.Controls.Add(lblVal, 1, row);
            return lblVal;
        }

        private Panel BuildConsoleTab()
        {
            CardPanel card = new CardPanel();
            card.Dock = DockStyle.Fill;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 4;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));

            FlowLayoutPanel filterRow = new FlowLayoutPanel();
            filterRow.Dock = DockStyle.Top;
            filterRow.Height = 40;
            filterRow.WrapContents = false;

            _cbLogSource = Theme.CreateComboBox();
            _cbLogSource.Items.AddRange(new object[] { "Servidor Minecraft (mc_server)", "Túnel Playit.gg (mc_playit)" });
            _cbLogSource.SelectedIndex = 0;
            _cbLogSource.Width = 225;
            _cbLogSource.Margin = new Padding(0, 4, 8, 0);
            _cbLogSource.SelectedIndexChanged += async delegate { await RefreshLogsTabAsync(); };

            _txtLogFilter = Theme.CreateTextBox(false);
            _txtLogFilter.Width = 210;
            _txtLogFilter.Margin = new Padding(0, 4, 8, 0);
            Theme.SetPlaceholder(_txtLogFilter, "Filtrar logs...");
            _txtLogFilter.TextChanged += async delegate { await RefreshLogsTabAsync(); };

            _chkAutoScroll = new CheckBox();
            _chkAutoScroll.Text = "Auto-scroll";
            _chkAutoScroll.Checked = true;
            _chkAutoScroll.ForeColor = Theme.TextMuted;
            _chkAutoScroll.AutoSize = true;
            _chkAutoScroll.Margin = new Padding(4, 8, 10, 0);

            Button btnRefreshLogs = Theme.CreateButton("Atualizar Logs", Theme.BgElevated, Theme.TextPrimary);
            btnRefreshLogs.Size = new Size(120, 32);
            btnRefreshLogs.Font = Theme.FontSmall;
            btnRefreshLogs.Margin = new Padding(0, 2, 0, 0);
            btnRefreshLogs.Click += async delegate { await RefreshLogsTabAsync(); };

            filterRow.Controls.Add(_cbLogSource);
            filterRow.Controls.Add(_txtLogFilter);
            filterRow.Controls.Add(_chkAutoScroll);
            filterRow.Controls.Add(btnRefreshLogs);

            FlowLayoutPanel quickRow = new FlowLayoutPanel();
            quickRow.Dock = DockStyle.Bottom;
            quickRow.Height = 40;
            quickRow.WrapContents = true;

            string[][] quickCmds = new string[][]
            {
                new string[] { "Salvar Mundo", "save-all" },
                new string[] { "Dia", "time set day" },
                new string[] { "Noite", "time set night" },
                new string[] { "Clima Limpo", "weather clear" },
                new string[] { "Chuva", "weather rain" },
                new string[] { "Listar Jogadores", "list" }
            };
            foreach (string[] pair in quickCmds)
            {
                string title = pair[0];
                string cmd = pair[1];
                Button qBtn = Theme.CreateButton(title, Theme.BgElevated, Theme.Emerald);
                qBtn.Size = new Size(116, 30);
                qBtn.Font = Theme.FontSmall;
                qBtn.Margin = new Padding(0, 4, 6, 0);
                qBtn.Click += async delegate { await ExecuteRconFromUiAsync(cmd); };
                quickRow.Controls.Add(qBtn);
            }

            Panel toolbarWrap = new Panel();
            toolbarWrap.Dock = DockStyle.Fill;
            toolbarWrap.Margin = new Padding(0);
            toolbarWrap.Controls.Add(quickRow);
            toolbarWrap.Controls.Add(filterRow);

            _rtbFullLog = new RichTextBox();
            _rtbFullLog.Dock = DockStyle.Fill;
            _rtbFullLog.BackColor = Theme.BgInput;
            _rtbFullLog.ForeColor = Color.FromArgb(203, 213, 225);
            _rtbFullLog.Font = Theme.FontMono;
            _rtbFullLog.BorderStyle = BorderStyle.FixedSingle;
            _rtbFullLog.ReadOnly = true;

            _txtFullRcon = Theme.CreateTextBox(false);
            Panel fullCmdBar = BuildCommandBar(
                _txtFullRcon,
                "Digite qualquer comando (ex: /op nick, /gamemode creative nick, /tp a b, /say ola)...",
                async delegate
                {
                    string cmd = _txtFullRcon.Text.Trim();
                    _txtFullRcon.Text = "";
                    await ExecuteRconFromUiAsync(cmd);
                }
            );

            layout.Controls.Add(Theme.CreateCardHeader("Console Completo & RCON", "Visualize logs do Minecraft ou Playit.gg e envie comandos (com ou sem /)", null), 0, 0);
            layout.Controls.Add(toolbarWrap, 0, 1);
            layout.Controls.Add(_rtbFullLog, 0, 2);
            layout.Controls.Add(fullCmdBar, 0, 3);
            card.Controls.Add(layout);
            return card;
        }

        private Panel BuildPlayersTab()
        {
            CardPanel card = new CardPanel();
            card.Dock = DockStyle.Fill;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 4;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));

            _lblPlayersSummary = Theme.CreateLabel("Carregando...", Theme.FontBase, Theme.TextMuted);
            _lblPlayersSummary.Dock = DockStyle.Fill;

            _gridPlayers = new DataGridView();
            Theme.StyleDataGrid(_gridPlayers);
            _gridPlayers.Dock = DockStyle.Fill;
            _gridPlayers.Margin = new Padding(0, 2, 0, 6);
            _gridPlayers.Columns.Add("Name", "Jogador");
            _gridPlayers.Columns.Add("Status", "Status");
            _gridPlayers.Columns.Add("Role", "OP");
            _gridPlayers.Columns.Add("Whitelist", "Whitelist");
            _gridPlayers.Columns.Add("PlayTime", "Tempo Jogado");
            _gridPlayers.Columns.Add("MobKills", "Mobs Mortos");
            _gridPlayers.Columns.Add("Deaths", "Mortes");
            _gridPlayers.Columns.Add("BlocksMined", "Blocos Minerados");
            _gridPlayers.Columns.Add("DistanceKm", "Distância");

            FlowLayoutPanel actionBar = new FlowLayoutPanel();
            actionBar.Dock = DockStyle.Fill;
            actionBar.Margin = new Padding(0);
            actionBar.Padding = new Padding(0, 6, 0, 0);
            actionBar.WrapContents = false;

            Button btnOp = Theme.CreateButton("Alternar OP", Theme.BgElevated, Theme.Emerald);
            btnOp.Size = new Size(118, 34);
            btnOp.Margin = new Padding(0, 0, 6, 0);
            btnOp.Click += async delegate
            {
                PlayerInfo p = GetSelectedPlayer();
                if (p == null) return;
                await RunPlayerActionAsync(p.IsOp ? "deop" : "op", p, null);
            };

            Button btnWl = Theme.CreateButton("Alternar Whitelist", Theme.BgElevated, Theme.Cyan);
            btnWl.Size = new Size(142, 34);
            btnWl.Margin = new Padding(0, 0, 8, 0);
            btnWl.Click += async delegate
            {
                PlayerInfo p = GetSelectedPlayer();
                if (p == null) return;
                await RunPlayerActionAsync(p.Whitelisted ? "whitelist_remove" : "whitelist_add", p, null);
            };

            _cbGamemodeSelect = Theme.CreateComboBox();
            _cbGamemodeSelect.Items.AddRange(new object[] { "survival", "creative", "spectator", "adventure" });
            _cbGamemodeSelect.SelectedIndex = 0;
            _cbGamemodeSelect.Width = 112;
            _cbGamemodeSelect.Margin = new Padding(0, 4, 6, 0);

            Button btnGm = Theme.CreateButton("Aplicar Gamemode", Theme.BgElevated, Theme.TextPrimary);
            btnGm.Size = new Size(138, 34);
            btnGm.Margin = new Padding(0, 0, 6, 0);
            btnGm.Click += async delegate
            {
                PlayerInfo p = GetSelectedPlayer();
                if (p == null) return;
                await RunPlayerActionAsync("gamemode", p, Convert.ToString(_cbGamemodeSelect.SelectedItem));
            };

            Button btnKick = Theme.CreateButton("Expulsar (Kick)", Theme.Rose, Color.White);
            btnKick.Size = new Size(125, 34);
            btnKick.Margin = new Padding(0, 0, 6, 0);
            btnKick.Click += async delegate
            {
                PlayerInfo p = GetSelectedPlayer();
                if (p == null) return;
                await RunPlayerActionAsync("kick", p, "Expulso pelo painel administrativo");
            };

            Button btnRefreshP = Theme.CreateButton("Atualizar", Theme.BgElevated, Theme.TextPrimary);
            btnRefreshP.Size = new Size(96, 34);
            btnRefreshP.Click += async delegate { await RefreshPlayersTabAsync(); };

            actionBar.Controls.Add(btnOp);
            actionBar.Controls.Add(btnWl);
            actionBar.Controls.Add(_cbGamemodeSelect);
            actionBar.Controls.Add(btnGm);
            actionBar.Controls.Add(btnKick);
            actionBar.Controls.Add(btnRefreshP);

            layout.Controls.Add(Theme.CreateCardHeader("Jogadores & Estatísticas do Mundo", "Dados reais lidos de usercache, ops, whitelist e world/players/stats", null), 0, 0);
            layout.Controls.Add(_lblPlayersSummary, 0, 1);
            layout.Controls.Add(_gridPlayers, 0, 2);
            layout.Controls.Add(actionBar, 0, 3);
            card.Controls.Add(layout);
            return card;
        }

        private Panel BuildBackupsTab()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 176f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            CardPanel toolsCard = new CardPanel();
            toolsCard.Dock = DockStyle.Fill;
            toolsCard.Margin = new Padding(0, 0, 0, 10);

            TableLayoutPanel toolsLayout = new TableLayoutPanel();
            toolsLayout.Dock = DockStyle.Fill;
            toolsLayout.ColumnCount = 1;
            toolsLayout.RowCount = 3;
            toolsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));
            toolsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
            toolsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            FlowLayoutPanel toolsFlow = new FlowLayoutPanel();
            toolsFlow.Dock = DockStyle.Fill;
            toolsFlow.Margin = new Padding(0);
            toolsFlow.Padding = new Padding(0, 4, 0, 0);
            toolsFlow.WrapContents = false;

            _btnCreateBackup = Theme.CreateButton("Criar Backup Agora (.zip)", Theme.EmeraldDark, Color.White);
            _btnCreateBackup.Size = new Size(186, 36);
            _btnCreateBackup.Margin = new Padding(0, 0, 8, 0);
            _btnCreateBackup.Click += async delegate { await CreateManualBackupAsync(); };

            Button btnReconnect = Theme.CreateButton("Reconectar Google Drive", Theme.BgElevated, Theme.Cyan);
            btnReconnect.Size = new Size(176, 36);
            btnReconnect.Margin = new Padding(0, 0, 8, 0);
            btnReconnect.Click += delegate { _engine.OpenDriveReconnectWindow(); };

            Button btnUnlock = Theme.CreateButton("Forçar Remoção de Trava", Theme.Amber, Color.White);
            btnUnlock.Size = new Size(186, 36);
            btnUnlock.Margin = new Padding(0, 0, 8, 0);
            btnUnlock.Click += async delegate
            {
                DialogResult res = MessageBox.Show(
                    "Remova a trava lock.txt somente se você tiver certeza de que nenhum amigo está com o servidor aberto!\n\nDeseja remover a trava agora?",
                    "Remover Trava na Nuvem",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );
                if (res == DialogResult.Yes)
                {
                    try
                    {
                        await _engine.ForceUnlockCloudAsync();
                        await RefreshAllAsync(true);
                        MessageBox.Show("Trava removida com sucesso!", "MineSync Pro", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            };

            Button btnOpenFolder = Theme.CreateButton("Abrir Pasta Local", Theme.BgElevated, Theme.TextPrimary);
            btnOpenFolder.Size = new Size(140, 36);
            btnOpenFolder.Margin = new Padding(0, 0, 8, 0);
            btnOpenFolder.Click += delegate
            {
                try
                {
                    Directory.CreateDirectory(_config.BackupsDir);
                    Process.Start("explorer.exe", _config.BackupsDir);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Não foi possível abrir a pasta: " + ex.Message, "Pasta de Backups", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            Button btnChangeBackupFolder = Theme.CreateButton("Mudar Pasta Local...", Theme.BgElevated, Theme.Emerald);
            btnChangeBackupFolder.Size = new Size(154, 36);
            btnChangeBackupFolder.Margin = new Padding(0);
            btnChangeBackupFolder.Click += delegate { BrowseAndSetBackupsDir(); };

            toolsFlow.Controls.Add(_btnCreateBackup);
            toolsFlow.Controls.Add(btnReconnect);
            toolsFlow.Controls.Add(btnUnlock);
            toolsFlow.Controls.Add(btnOpenFolder);
            toolsFlow.Controls.Add(btnChangeBackupFolder);

            Panel toggleRow = new Panel();
            toggleRow.Dock = DockStyle.Fill;
            toggleRow.Margin = new Padding(0);

            _chkLocalBackupEnabled = new CheckBox();
            _chkLocalBackupEnabled.Text = "Salvar backup local (.zip) no HD ao parar servidor (o backup no Google Drive continua sempre ativo)";
            _chkLocalBackupEnabled.UseMnemonic = false;
            _chkLocalBackupEnabled.Checked = _config.LocalBackupEnabled;
            _chkLocalBackupEnabled.Font = Theme.FontBold;
            _chkLocalBackupEnabled.ForeColor = _config.LocalBackupEnabled ? Theme.Emerald : Theme.TextMuted;
            _chkLocalBackupEnabled.AutoSize = true;
            _chkLocalBackupEnabled.Location = new Point(2, 8);
            _chkLocalBackupEnabled.CheckedChanged += delegate
            {
                if (_syncingBackupControls) return;
                _config.SetBackupPreferences(_config.BackupsDir, _chkLocalBackupEnabled.Checked);
                SyncBackupControlsFromConfig();
            };
            toggleRow.Controls.Add(_chkLocalBackupEnabled);

            toolsLayout.Controls.Add(Theme.CreateCardHeader("Ferramentas da Nuvem & Snapshots", "Manutenção de trava lock.txt, reconexão do Google Drive e backups .zip", null), 0, 0);
            toolsLayout.Controls.Add(toolsFlow, 0, 1);
            toolsLayout.Controls.Add(toggleRow, 0, 2);
            toolsCard.Controls.Add(toolsLayout);

            TableLayoutPanel split = new TableLayoutPanel();
            split.Dock = DockStyle.Fill;
            split.ColumnCount = 2;
            split.RowCount = 1;
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));

            // Local backups
            CardPanel localCard = new CardPanel();
            localCard.Dock = DockStyle.Fill;
            localCard.Margin = new Padding(0, 0, 8, 0);

            TableLayoutPanel localLayout = new TableLayoutPanel();
            localLayout.Dock = DockStyle.Fill;
            localLayout.ColumnCount = 1;
            localLayout.RowCount = 3;
            localLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));
            localLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            localLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));

            _gridLocalBackups = new DataGridView();
            Theme.StyleDataGrid(_gridLocalBackups);
            _gridLocalBackups.Dock = DockStyle.Fill;
            _gridLocalBackups.Margin = new Padding(0, 2, 0, 6);
            _gridLocalBackups.Columns.Add("FileName", "Arquivo");
            _gridLocalBackups.Columns.Add("Size", "Tamanho");
            _gridLocalBackups.Columns.Add("Date", "Data");

            FlowLayoutPanel localBtns = new FlowLayoutPanel();
            localBtns.Dock = DockStyle.Fill;
            localBtns.Margin = new Padding(0);
            localBtns.Padding = new Padding(0, 6, 0, 0);

            Button btnRestore = Theme.CreateButton("Restaurar Selecionado", Theme.Amber, Color.White);
            btnRestore.Size = new Size(162, 34);
            btnRestore.Margin = new Padding(0, 0, 6, 0);
            btnRestore.Click += async delegate { await RestoreSelectedBackupAsync(); };

            Button btnDelBackup = Theme.CreateButton("Excluir", Theme.Rose, Color.White);
            btnDelBackup.Size = new Size(92, 34);
            btnDelBackup.Margin = new Padding(0, 0, 6, 0);
            btnDelBackup.Click += async delegate { await DeleteSelectedBackupAsync(); };

            Button btnRefreshBk = Theme.CreateButton("Atualizar", Theme.BgElevated, Theme.TextPrimary);
            btnRefreshBk.Size = new Size(92, 34);
            btnRefreshBk.Click += async delegate { await RefreshBackupsTabAsync(); };

            localBtns.Controls.Add(btnRestore);
            localBtns.Controls.Add(btnDelBackup);
            localBtns.Controls.Add(btnRefreshBk);

            Panel localHeader = Theme.CreateCardHeader("Backups Locais (HD)", "Pasta: " + _config.BackupsDir, null);
            foreach (Control c in localHeader.Controls)
            {
                Panel stack = c as Panel;
                if (stack != null && stack.Controls.Count > 1)
                {
                    _lblLocalBackupsDirInfo = stack.Controls[1] as Label;
                }
            }

            localLayout.Controls.Add(localHeader, 0, 0);
            localLayout.Controls.Add(_gridLocalBackups, 0, 1);
            localLayout.Controls.Add(localBtns, 0, 2);
            localCard.Controls.Add(localLayout);

            // Cloud backups
            CardPanel cloudCard = new CardPanel();
            cloudCard.Dock = DockStyle.Fill;
            cloudCard.Margin = new Padding(8, 0, 0, 0);

            TableLayoutPanel cloudLayout = new TableLayoutPanel();
            cloudLayout.Dock = DockStyle.Fill;
            cloudLayout.ColumnCount = 1;
            cloudLayout.RowCount = 2;
            cloudLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));
            cloudLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _gridCloudBackups = new DataGridView();
            Theme.StyleDataGrid(_gridCloudBackups);
            _gridCloudBackups.Dock = DockStyle.Fill;
            _gridCloudBackups.Margin = new Padding(0, 2, 0, 0);
            _gridCloudBackups.Columns.Add("FileName", "Arquivo");
            _gridCloudBackups.Columns.Add("Size", "Tamanho");
            _gridCloudBackups.Columns.Add("Date", "Data");

            cloudLayout.Controls.Add(Theme.CreateCardHeader("Backups na Nuvem (Drive)", "Arquivos em MinecraftSync/Backups", null), 0, 0);
            cloudLayout.Controls.Add(_gridCloudBackups, 0, 1);
            cloudCard.Controls.Add(cloudLayout);

            split.Controls.Add(localCard, 0, 0);
            split.Controls.Add(cloudCard, 1, 0);

            root.Controls.Add(toolsCard, 0, 0);
            root.Controls.Add(split, 0, 1);
            return root;
        }

        private Panel BuildConfigTab()
        {
            CardPanel card = new CardPanel();
            card.Dock = DockStyle.Fill;

            TableLayoutPanel outer = new TableLayoutPanel();
            outer.Dock = DockStyle.Fill;
            outer.ColumnCount = 1;
            outer.RowCount = 2;
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Button btnSaveCfg = Theme.CreateButton("Salvar Configurações", Theme.EmeraldDark, Color.White);
            btnSaveCfg.Size = new Size(175, 34);
            btnSaveCfg.Click += delegate { SaveConfigFields(); };

            Panel scrollHost = new Panel();
            scrollHost.Dock = DockStyle.Fill;
            scrollHost.AutoScroll = true;

            TableLayoutPanel formGrid = new TableLayoutPanel();
            formGrid.Dock = DockStyle.Top;
            formGrid.AutoSize = true;
            formGrid.ColumnCount = 4;
            formGrid.Padding = new Padding(0, 4, 14, 12);
            formGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185f));
            formGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            formGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185f));
            formGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            int row = 0;

            // Workspace row
            _txtWorkspacePath = Theme.CreateTextBox(true);
            _txtWorkspacePath.Dock = DockStyle.Fill;

            Button btnBrowseWs = Theme.CreateButton("Alterar Pasta...", Theme.BgElevated, Theme.TextPrimary);
            btnBrowseWs.Size = new Size(120, 28);
            btnBrowseWs.Font = Theme.FontSmall;
            btnBrowseWs.Click += delegate
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.SelectedPath = _config.WorkspaceDir;
                    if (fbd.ShowDialog(this) == DialogResult.OK)
                    {
                        if (!ServerConfig.IsValidWorkspace(fbd.SelectedPath))
                        {
                            MessageBox.Show("A pasta selecionada não contém docker-compose.yml ou dados_mine.", "Pasta Inválida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        _config.SetWorkspaceDir(fbd.SelectedPath);
                        LoadConfigFields();
                        var _t = RefreshAllAsync(true);
                    }
                }
            };

            TableLayoutPanel wsPanel = new TableLayoutPanel();
            wsPanel.Dock = DockStyle.Fill;
            wsPanel.Margin = new Padding(0);
            wsPanel.Height = 32;
            wsPanel.ColumnCount = 2;
            wsPanel.RowCount = 1;
            wsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            wsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128f));
            wsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            btnBrowseWs.Dock = DockStyle.Fill;
            btnBrowseWs.Margin = new Padding(6, 0, 0, 0);
            _txtWorkspacePath.Margin = new Padding(0, 2, 0, 2);
            wsPanel.Controls.Add(_txtWorkspacePath, 0, 0);
            wsPanel.Controls.Add(btnBrowseWs, 1, 0);

            AddFullWidthField(formGrid, "Pasta do Servidor:", wsPanel, ref row);

            // Backups Folder row
            _txtBackupsDirConfig = Theme.CreateTextBox(true);
            _txtBackupsDirConfig.Dock = DockStyle.Fill;

            Button btnBrowseBk = Theme.CreateButton("Alterar Pasta...", Theme.BgElevated, Theme.TextPrimary);
            btnBrowseBk.Size = new Size(120, 28);
            btnBrowseBk.Font = Theme.FontSmall;
            btnBrowseBk.Click += delegate { BrowseAndSetBackupsDir(); };

            TableLayoutPanel bkPanel = new TableLayoutPanel();
            bkPanel.Dock = DockStyle.Fill;
            bkPanel.Margin = new Padding(0);
            bkPanel.Height = 32;
            bkPanel.ColumnCount = 2;
            bkPanel.RowCount = 1;
            bkPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bkPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128f));
            bkPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            btnBrowseBk.Dock = DockStyle.Fill;
            btnBrowseBk.Margin = new Padding(6, 0, 0, 0);
            _txtBackupsDirConfig.Margin = new Padding(0, 2, 0, 2);
            bkPanel.Controls.Add(_txtBackupsDirConfig, 0, 0);
            bkPanel.Controls.Add(btnBrowseBk, 1, 0);

            AddFullWidthField(formGrid, "Pasta Backups Locais:", bkPanel, ref row);

            _chkLocalBackupConfig = new CheckBox();
            _chkLocalBackupConfig.Text = "Manter cópia .zip no HD local ao desligar (backup no Google Drive continua normal)";
            _chkLocalBackupConfig.UseMnemonic = false;
            _chkLocalBackupConfig.Checked = _config.LocalBackupEnabled;
            _chkLocalBackupConfig.ForeColor = Theme.TextPrimary;
            _chkLocalBackupConfig.AutoSize = true;
            _chkLocalBackupConfig.CheckedChanged += delegate
            {
                if (_syncingBackupControls) return;
                _config.SetBackupPreferences(_config.BackupsDir, _chkLocalBackupConfig.Checked);
                SyncBackupControlsFromConfig();
            };
            AddFullWidthField(formGrid, "Backup Local (.zip):", _chkLocalBackupConfig, ref row);

            AddSectionRow(formGrid, "Recursos Docker & Túnel Playit.gg", ref row);

            _cbMemory = Theme.CreateComboBox();
            _cbMemory.Items.AddRange(new object[] { "2G", "4G", "6G", "8G", "10G", "12G", "16G" });
            _cbServerType = Theme.CreateComboBox();
            _cbServerType.Items.AddRange(new object[] { "VANILLA", "PAPER", "FABRIC", "FORGE" });
            AddTwoFields(formGrid, "Memória RAM:", _cbMemory, "Tipo de Servidor:", _cbServerType, ref row);

            _txtServerVersion = Theme.CreateTextBox(false);
            _cbOnlineMode = Theme.CreateComboBox();
            _cbOnlineMode.Items.AddRange(new object[] { "TRUE", "FALSE" });
            AddTwoFields(formGrid, "Versão do Minecraft:", _txtServerVersion, "Online Mode (Original):", _cbOnlineMode, ref row);

            _txtPlayitInfo = Theme.CreateTextBox(true);
            AddFullWidthField(formGrid, "Chave Playit.toml:", _txtPlayitInfo, ref row);

            AddSectionRow(formGrid, "Propriedades do Mundo (dados_mine/server.properties)", ref row);

            _txtMotd = Theme.CreateTextBox(false);
            AddFullWidthField(formGrid, "MOTD (Descrição):", _txtMotd, ref row);

            _cbDifficulty = Theme.CreateComboBox();
            _cbDifficulty.Items.AddRange(new object[] { "peaceful", "easy", "normal", "hard" });
            _cbGamemode = Theme.CreateComboBox();
            _cbGamemode.Items.AddRange(new object[] { "survival", "creative", "adventure", "spectator" });
            AddTwoFields(formGrid, "Dificuldade:", _cbDifficulty, "Modo de Jogo:", _cbGamemode, ref row);

            _numMaxPlayers = CreateNumeric(1, 200, 2);
            _numViewDist = CreateNumeric(2, 32, 16);
            AddTwoFields(formGrid, "Máximo de Jogadores:", _numMaxPlayers, "View Distance (Chunks):", _numViewDist, ref row);

            _numSimDist = CreateNumeric(2, 32, 10);
            _numSpawnProt = CreateNumeric(0, 128, 16);
            AddTwoFields(formGrid, "Simulation Distance:", _numSimDist, "Proteção do Spawn:", _numSpawnProt, ref row);

            _cbAllowFlight = Theme.CreateComboBox();
            _cbAllowFlight.Items.AddRange(new object[] { "false", "true" });
            _cbWhitelist = Theme.CreateComboBox();
            _cbWhitelist.Items.AddRange(new object[] { "false", "true" });
            AddTwoFields(formGrid, "Permitir Voo (allow-flight):", _cbAllowFlight, "Whitelist Ativa:", _cbWhitelist, ref row);

            _cbHardcore = Theme.CreateComboBox();
            _cbHardcore.Items.AddRange(new object[] { "false", "true" });
            _numRconPort = CreateNumeric(1024, 65535, 25575);
            AddTwoFields(formGrid, "Modo Hardcore:", _cbHardcore, "Porta RCON:", _numRconPort, ref row);

            scrollHost.Controls.Add(formGrid);
            outer.Controls.Add(Theme.CreateCardHeader("Configurações do Servidor", "Edite pasta do servidor, parâmetros do Docker e propriedades de server.properties", btnSaveCfg), 0, 0);
            outer.Controls.Add(scrollHost, 0, 1);
            card.Controls.Add(outer);
            return card;
        }

        private static void AddSectionRow(TableLayoutPanel grid, string title, ref int row)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            Label lbl = Theme.CreateLabel(title, Theme.FontBold, Theme.Emerald);
            lbl.Margin = new Padding(0, 10, 0, 4);
            grid.Controls.Add(lbl, 0, row);
            grid.SetColumnSpan(lbl, 4);
            row++;
        }

        private static void AddFullWidthField(TableLayoutPanel grid, string label, Control ctrl, ref int row)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            Label lbl = Theme.CreateLabel(label, Theme.FontBase, Theme.TextMuted);
            lbl.Anchor = AnchorStyles.Left;
            ctrl.Dock = DockStyle.Fill;
            ctrl.Margin = new Padding(0, 4, 0, 4);

            grid.Controls.Add(lbl, 0, row);
            grid.Controls.Add(ctrl, 1, row);
            grid.SetColumnSpan(ctrl, 3);
            row++;
        }

        private static void AddTwoFields(TableLayoutPanel grid, string l1, Control c1, string l2, Control c2, ref int row)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            Label lbl1 = Theme.CreateLabel(l1, Theme.FontBase, Theme.TextMuted);
            lbl1.Anchor = AnchorStyles.Left;
            c1.Dock = DockStyle.Fill;
            c1.Margin = new Padding(0, 4, 16, 4);

            Label lbl2 = Theme.CreateLabel(l2, Theme.FontBase, Theme.TextMuted);
            lbl2.Anchor = AnchorStyles.Left;
            c2.Dock = DockStyle.Fill;
            c2.Margin = new Padding(0, 4, 0, 4);

            grid.Controls.Add(lbl1, 0, row);
            grid.Controls.Add(c1, 1, row);
            grid.Controls.Add(lbl2, 2, row);
            grid.Controls.Add(c2, 3, row);
            row++;
        }

        private static NumericUpDown CreateNumeric(int min, int max, int val)
        {
            NumericUpDown num = new NumericUpDown();
            num.Minimum = min;
            num.Maximum = max;
            num.Value = val;
            num.BackColor = Theme.BgInput;
            num.ForeColor = Theme.TextPrimary;
            num.BorderStyle = BorderStyle.FixedSingle;
            return num;
        }

        private void Engine_OnLog(string msg, string level)
        {
            AppendDashLog(msg, level);
        }

        private void Engine_OnProgress(int current, int total, string title)
        {
            Action update = delegate
            {
                _lblWorkflowStep.Text = string.Format("Etapa {0}/{1}: {2}", current, total, title);
                _workflowProgress.Value = Math.Min(100, (int)Math.Round((current * 100.0) / total));
            };
            if (this.InvokeRequired) this.BeginInvoke(update);
            else update();
        }

        private void AppendDashLog(string text, string level)
        {
            Action append = delegate
            {
                Color color = Color.FromArgb(203, 213, 225);
                if (level == "success") color = Theme.Emerald;
                else if (level == "step") color = Theme.Cyan;
                else if (level == "warn") color = Color.FromArgb(251, 191, 36);
                else if (level == "error") color = Color.FromArgb(251, 113, 133);

                _rtbDashLog.SelectionStart = _rtbDashLog.TextLength;
                _rtbDashLog.SelectionLength = 0;
                _rtbDashLog.SelectionColor = color;
                _rtbDashLog.AppendText(string.Format("[{0}] {1}{2}", DateTime.Now.ToString("HH:mm:ss"), text, Environment.NewLine));
                _rtbDashLog.ScrollToCaret();
            };
            if (this.InvokeRequired) this.BeginInvoke(append);
            else append();
        }

        private async Task RunStartWorkflowAsync()
        {
            Func<CloudStatusInfo, MapConflictResolution> prompt = delegate(CloudStatusInfo cloudInfo)
            {
                MapConflictResolution choice = MapConflictResolution.Cancel;
                this.Invoke((MethodInvoker)delegate
                {
                    using (ConflictForm modal = new ConflictForm(cloudInfo))
                    {
                        modal.ShowDialog(this);
                        choice = modal.Resolution;
                    }
                });
                return choice;
            };

            await RunWorkflowWithHandling(_engine.StartServerAsync(prompt, false));
        }

        private async Task RunWorkflowWithHandling(Task workflowTask)
        {
            UpdateButtonsForBusyState(true);
            try
            {
                await workflowTask;
                _workflowProgress.Value = 100;
            }
            catch (Exception ex)
            {
                AppendDashLog("[ERRO FATAL] " + ex.Message, "error");
                MessageBox.Show(ex.Message, "MineSync Pro — Atenção", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                UpdateButtonsForBusyState(false);
            }
            await RefreshAllAsync(true);
        }

        private void UpdateButtonsForBusyState(bool busy)
        {
            _btnStart.Enabled = !busy && !_lastDocker.McRunning;
            _btnStop.Enabled = !busy && _lastDocker.McRunning;
            _btnEmergency.Enabled = !busy;
            _btnKill.Enabled = !busy && (_lastDocker.McRunning || _lastDocker.PlayitRunning);

            if (_trayMenuStart != null) _trayMenuStart.Enabled = _btnStart.Enabled;
            if (_trayMenuStop != null) _trayMenuStop.Enabled = _btnStop.Enabled;
        }

        private async Task RefreshAllAsync(bool includeCloud)
        {
            if (_isRefreshingStatus) return;
            _isRefreshingStatus = true;

            try
            {
                _lastDocker = await _engine.GetDockerStatusAsync();
                if (includeCloud)
                {
                    _lastCloud = await _engine.GetCloudStatusAsync();
                }

                ApplyStatusToUi();

                if (_activeTab == "console")
                {
                    await RefreshLogsTabAsync();
                }
            }
            catch
            {
            }
            finally
            {
                _isRefreshingStatus = false;
            }
        }

        private void ApplyStatusToUi()
        {
            if (_engine.IsBusy)
            {
                _statusDot.BackColor = Theme.Cyan;
                _lblHeaderStatus.Text = "Operação em andamento...";
                _lblHeaderSub.Text = _lblWorkflowStep.Text;
            }
            else if (_lastDocker.McRunning && _lastDocker.McReady)
            {
                _statusDot.BackColor = Theme.Emerald;
                _lblHeaderStatus.Text = "SERVIDOR PRONTO PARA JOGAR!";
                _lblHeaderSub.Text = _lastDocker.McStatusText;

                if (!_alreadyPlayedReadySound)
                {
                    _alreadyPlayedReadySound = true;
                    if (_soundEnabled)
                    {
                        try
                        {
                            string wav = @"C:\Windows\Media\chimes.wav";
                            if (File.Exists(wav)) new SoundPlayer(wav).Play();
                            else SystemSounds.Asterisk.Play();
                        }
                        catch { }
                    }
                }
            }
            else if (_lastDocker.McRunning)
            {
                _statusDot.BackColor = Theme.Amber;
                _lblHeaderStatus.Text = "GERANDO MUNDO E CARREGANDO...";
                _lblHeaderSub.Text = _lastDocker.McStatusText;
                _alreadyPlayedReadySound = false;
            }
            else if (!_lastDocker.DockerRunning)
            {
                _statusDot.BackColor = Theme.Amber;
                _lblHeaderStatus.Text = "DOCKER FECHADO — Pronto para iniciar";
                _lblHeaderSub.Text = "O Docker Desktop será aberto automaticamente ao iniciar";
                _alreadyPlayedReadySound = false;
            }
            else
            {
                _statusDot.BackColor = Theme.Rose;
                _lblHeaderStatus.Text = "SERVIDOR OFFLINE";
                _lblHeaderSub.Text = "Containers encerrados com segurança";
                _alreadyPlayedReadySound = false;
            }

            _lblDockerSide.Text = _lastDocker.DockerRunning ? "Docker: Ativo" : "Docker: Fechado";
            _lblDockerSide.ForeColor = _lastDocker.DockerRunning ? Theme.Emerald : Color.FromArgb(251, 191, 36);

            _lblDriveSide.Text = _lastCloud.Authenticated ? "Google Drive: Conectado" : "Google Drive: Desconectado";
            _lblDriveSide.ForeColor = _lastCloud.Authenticated ? Theme.Emerald : Theme.Rose;

            _currentPlayitAddress = _lastDocker.PlayitAddress ?? "";
            if (_lastDocker.PlayitRunning && !string.IsNullOrEmpty(_currentPlayitAddress))
            {
                _btnPlayitPill.Text = _currentPlayitAddress + " (Copiar)";
            }
            else if (_lastDocker.PlayitRunning)
            {
                _btnPlayitPill.Text = "Playit: Túnel Ativo";
            }
            else
            {
                _btnPlayitPill.Text = "Playit: Offline";
            }

            _lblResourcesPill.Text = (_lastDocker.McRunning && !string.IsNullOrEmpty(_lastDocker.McCpu))
                ? string.Format("{0} | {1}", _lastDocker.McCpu, _lastDocker.McMemory)
                : "CPU/RAM: —";

            _lblLockPill.Text = _lastCloud.Locked
                ? (!string.IsNullOrEmpty(_lastCloud.LockHost) ? "Trava: " + _lastCloud.LockHost : "Trava: Em uso")
                : "Trava: Livre";

            UpdateButtonsForBusyState(_engine.IsBusy);

            _lblLocalDate.Text = _lastCloud.LocalTime.HasValue ? _lastCloud.LocalTime.Value.ToString("dd/MM/yyyy HH:mm") : "Sem save local";
            _lblCloudDate.Text = _lastCloud.CloudTime.HasValue ? _lastCloud.CloudTime.Value.ToString("dd/MM/yyyy HH:mm") : "Sem save na nuvem";

            if (_lastCloud.Locked)
            {
                _lblLockOwner.Text = !string.IsNullOrEmpty(_lastCloud.LockHost)
                    ? string.Format("{0} ({1})", _lastCloud.LockHost, _lastCloud.LockUser)
                    : "Trancado";
                _lblLockOwner.ForeColor = Color.FromArgb(251, 191, 36);
            }
            else
            {
                _lblLockOwner.Text = "Livre (sem trava)";
                _lblLockOwner.ForeColor = Theme.Emerald;
            }

            _lblDriveAuth.Text = _lastCloud.Authenticated ? string.Format("Autenticado ({0}:)", _lastCloud.Remote) : "Token expirado / Erro";
            _lblDriveAuth.ForeColor = _lastCloud.Authenticated ? Theme.Emerald : Theme.Rose;

            if (_lastCloud.QuotaTotal > 0)
            {
                _lblQuotaText.Text = string.Format("{0} / {1} ({2}%)", _lastCloud.QuotaUsedText, _lastCloud.QuotaTotalText, _lastCloud.QuotaPercent);
                _quotaBar.Value = _lastCloud.QuotaPercent;
            }
            else
            {
                _lblQuotaText.Text = _lastCloud.Authenticated ? "Disponível" : "Indisponível";
                _quotaBar.Value = 0;
            }
        }

        private async Task ExecuteRconFromUiAsync(string rawInput)
        {
            string cmd = (rawInput ?? "").Trim();
            if (string.IsNullOrEmpty(cmd)) return;

            _rconHistory.Insert(0, cmd);
            if (_rconHistory.Count > 30) _rconHistory.RemoveAt(30);
            _rconHistoryIdx = -1;

            string normalized = cmd;
            while (normalized.StartsWith("/"))
            {
                normalized = normalized.Substring(1).TrimStart();
            }
            if (string.IsNullOrEmpty(normalized)) return;

            bool serverReady = _lastDocker.McRunning && _lastDocker.McReady;
            try
            {
                if (!serverReady)
                {
                    string offlineResult = await TryRunOfflineCommandAsync(normalized);
                    if (offlineResult != null)
                    {
                        AppendDashLog(string.Format("> /{0} => {1}", normalized, offlineResult), "success");
                        return;
                    }
                    throw new InvalidOperationException("O servidor Minecraft está offline. Ligue o servidor para enviar comandos ao vivo (comandos offline suportados: /op, /deop, /whitelist add, /whitelist remove).");
                }

                string res = await RconClient.SendCommandAsync(_config, normalized, 4500);
                string output = string.IsNullOrWhiteSpace(res) ? "(Comando executado com sucesso)" : res;
                AppendDashLog(string.Format("> /{0} => {1}", normalized, output), "success");
                if (_activeTab == "console") await RefreshLogsTabAsync();
            }
            catch (Exception ex)
            {
                AppendDashLog(string.Format("> /{0} [ERRO] {1}", normalized, ex.Message), "error");
                MessageBox.Show(ex.Message, "Comando do Servidor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task<string> TryRunOfflineCommandAsync(string normalizedCmd)
        {
            string[] parts = normalizedCmd.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;

            string verb = parts[0].ToLowerInvariant();
            string action = null;
            string targetNick = null;

            if ((verb == "op" || verb == "deop") && parts.Length == 2)
            {
                action = verb;
                targetNick = parts[1];
            }
            else if (verb == "whitelist" && parts.Length >= 3)
            {
                string sub = parts[1].ToLowerInvariant();
                if (sub == "add") action = "whitelist_add";
                else if (sub == "remove") action = "whitelist_remove";
                targetNick = parts[2];
            }

            if (action == null || string.IsNullOrWhiteSpace(targetNick)) return null;

            List<PlayerInfo> known = await _playerBackupService.GetPlayersAsync(false);
            PlayerInfo match = known.Find(delegate(PlayerInfo p)
            {
                return string.Equals(p.Name, targetNick, StringComparison.OrdinalIgnoreCase);
            });

            if (match == null)
            {
                throw new InvalidOperationException(string.Format(
                    "Jogador '{0}' ainda não está em usercache.json. Ligue o servidor para executar '/{1}' online.",
                    targetNick,
                    normalizedCmd
                ));
            }

            return await _playerBackupService.PerformPlayerActionAsync(action, match, null, false);
        }

        private async Task RefreshLogsTabAsync()
        {
            string source = _cbLogSource.SelectedIndex == 1 ? "playit" : "minecraft";
            string raw = await _engine.GetContainerLogsAsync(source, 200);
            string filter = _txtLogFilter.Text.Trim();

            if (!string.IsNullOrEmpty(filter))
            {
                List<string> filtered = new List<string>();
                foreach (string line in raw.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    if (line.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        filtered.Add(line);
                    }
                }
                raw = string.Join(Environment.NewLine, filtered.ToArray());
            }

            _rtbFullLog.Text = raw;
            if (_chkAutoScroll.Checked)
            {
                _rtbFullLog.SelectionStart = _rtbFullLog.TextLength;
                _rtbFullLog.ScrollToCaret();
            }
        }

        private async Task RefreshPlayersTabAsync()
        {
            bool serverReady = _lastDocker.McRunning && _lastDocker.McReady;
            _cachedPlayers = await _playerBackupService.GetPlayersAsync(serverReady);

            int onlineCount = 0;
            foreach (PlayerInfo p in _cachedPlayers)
            {
                if (p.Online) onlineCount++;
            }

            _lblPlayersSummary.Text = serverReady
                ? string.Format("{0} online agora • {1} jogadores registrados no mundo", onlineCount, _cachedPlayers.Count)
                : string.Format("Servidor offline • {0} jogadores registrados no mundo", _cachedPlayers.Count);

            _gridPlayers.Rows.Clear();
            foreach (PlayerInfo p in _cachedPlayers)
            {
                _gridPlayers.Rows.Add(
                    p.Name,
                    p.Online ? "Online" : "Offline",
                    p.IsOp ? ("OP Lv." + p.OpLevel) : "Não",
                    p.Whitelisted ? "Sim" : "Não",
                    p.PlayTimeFormatted,
                    p.MobKills.ToString("N0"),
                    p.Deaths.ToString("N0"),
                    p.BlocksMined.ToString("N0"),
                    p.DistanceKm + " km"
                );
            }
        }

        private PlayerInfo GetSelectedPlayer()
        {
            if (_gridPlayers.SelectedRows.Count == 0)
            {
                MessageBox.Show("Selecione um jogador na tabela primeiro.", "Jogadores", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }
            int idx = _gridPlayers.SelectedRows[0].Index;
            if (idx < 0 || idx >= _cachedPlayers.Count) return null;
            return _cachedPlayers[idx];
        }

        private async Task RunPlayerActionAsync(string action, PlayerInfo player, string val)
        {
            try
            {
                bool serverReady = _lastDocker.McRunning && _lastDocker.McReady;
                string msg = await _playerBackupService.PerformPlayerActionAsync(action, player, val, serverReady);
                AppendDashLog(msg, "success");
                MessageBox.Show(msg, "MineSync Pro", MessageBoxButtons.OK, MessageBoxIcon.Information);
                await RefreshPlayersTabAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erro na Ação", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task RefreshBackupsTabAsync()
        {
            List<BackupEntry> local = _playerBackupService.ListLocalBackups();
            _gridLocalBackups.Rows.Clear();
            foreach (BackupEntry b in local)
            {
                int rowIdx = _gridLocalBackups.Rows.Add(b.FileName, b.SizeFormatted, b.ModifiedAt.ToString("dd/MM/yyyy HH:mm"));
                _gridLocalBackups.Rows[rowIdx].Tag = b;
            }

            List<BackupEntry> cloud = await _engine.ListCloudBackupsAsync();
            _gridCloudBackups.Rows.Clear();
            foreach (BackupEntry b in cloud)
            {
                _gridCloudBackups.Rows.Add(b.FileName, b.SizeFormatted, b.ModifiedAt.ToString("dd/MM/yyyy HH:mm"));
            }
        }

        private async Task CreateManualBackupAsync()
        {
            _btnCreateBackup.Enabled = false;
            _btnCreateBackup.Text = "Compactando...";
            try
            {
                BackupEntry b = await _playerBackupService.CreateLocalBackupAsync("Backup_Manual");
                AppendDashLog(string.Format("Backup criado: {0} ({1})", b.FileName, b.SizeFormatted), "success");
                await RefreshBackupsTabAsync();
                MessageBox.Show("Backup criado: " + b.FileName, "MineSync Pro", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erro no Backup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnCreateBackup.Enabled = true;
                _btnCreateBackup.Text = "Criar Backup Agora (.zip)";
            }
        }

        private async Task RestoreSelectedBackupAsync()
        {
            if (_gridLocalBackups.SelectedRows.Count == 0) return;
            BackupEntry entry = _gridLocalBackups.SelectedRows[0].Tag as BackupEntry;
            if (entry == null) return;

            DialogResult res = MessageBox.Show(
                string.Format("Deseja restaurar \"{0}\" para dados_mine/?\n\nO servidor deve estar desligado e arquivos atuais serão substituídos.", entry.FileName),
                "Restaurar Backup",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );
            if (res != DialogResult.Yes) return;

            try
            {
                await _playerBackupService.RestoreLocalBackupAsync(entry.FullPath, _lastDocker.McRunning);
                MessageBox.Show("Backup restaurado com sucesso!", "MineSync Pro", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erro ao Restaurar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task DeleteSelectedBackupAsync()
        {
            if (_gridLocalBackups.SelectedRows.Count == 0) return;
            BackupEntry entry = _gridLocalBackups.SelectedRows[0].Tag as BackupEntry;
            if (entry == null) return;

            DialogResult res = MessageBox.Show(
                string.Format("Excluir permanentemente o arquivo \"{0}\"?", entry.FileName),
                "Excluir Backup",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );
            if (res != DialogResult.Yes) return;

            _playerBackupService.DeleteLocalBackup(entry.FullPath);
            await RefreshBackupsTabAsync();
        }

        private void BrowseAndSetBackupsDir()
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Selecione a pasta para salvar os backups locais (.zip)";
                fbd.SelectedPath = _config.BackupsDir;
                if (fbd.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    _config.SetBackupPreferences(fbd.SelectedPath, _config.LocalBackupEnabled);
                    SyncBackupControlsFromConfig();
                    var _t = RefreshBackupsTabAsync();
                }
            }
        }

        private void SyncBackupControlsFromConfig()
        {
            _syncingBackupControls = true;
            try
            {
                if (_chkLocalBackupEnabled != null)
                {
                    _chkLocalBackupEnabled.Checked = _config.LocalBackupEnabled;
                    _chkLocalBackupEnabled.ForeColor = _config.LocalBackupEnabled ? Theme.Emerald : Theme.TextMuted;
                }
                if (_chkLocalBackupConfig != null)
                {
                    _chkLocalBackupConfig.Checked = _config.LocalBackupEnabled;
                }
                if (_txtBackupsDirConfig != null)
                {
                    _txtBackupsDirConfig.Text = _config.BackupsDir;
                }
                if (_lblLocalBackupsDirInfo != null)
                {
                    _lblLocalBackupsDirInfo.Text = string.Format(
                        "Pasta: {0} • {1}",
                        _config.BackupsDir,
                        _config.LocalBackupEnabled ? "Backup local ATIVADO" : "Backup local DESATIVADO (apenas Drive)"
                    );
                }
            }
            finally
            {
                _syncingBackupControls = false;
            }
        }

        private void LoadConfigFields()
        {
            _txtWorkspacePath.Text = _config.WorkspaceDir;
            SyncBackupControlsFromConfig();
            DockerSettings docker = _config.ReadDockerConfig();
            SelectOrAddCombo(_cbMemory, docker.Memory);
            SelectOrAddCombo(_cbServerType, docker.Type);
            _txtServerVersion.Text = docker.Version;
            SelectOrAddCombo(_cbOnlineMode, docker.OnlineMode.ToUpperInvariant());
            _txtPlayitInfo.Text = docker.PlayitConfigured
                ? string.Format("{0} (porta local {1})", docker.PlayitKeyMasked, docker.PlayitPort)
                : "Não configurado";

            Dictionary<string, string> props = _config.ReadServerProperties();
            _txtMotd.Text = GetProp(props, "motd", "A Minecraft Server");
            SelectOrAddCombo(_cbDifficulty, GetProp(props, "difficulty", "normal"));
            SelectOrAddCombo(_cbGamemode, GetProp(props, "gamemode", "survival"));
            SetNumeric(_numMaxPlayers, GetProp(props, "max-players", "20"));
            SetNumeric(_numViewDist, GetProp(props, "view-distance", "10"));
            SetNumeric(_numSimDist, GetProp(props, "simulation-distance", "10"));
            SetNumeric(_numSpawnProt, GetProp(props, "spawn-protection", "16"));
            SelectOrAddCombo(_cbAllowFlight, GetProp(props, "allow-flight", "false"));
            SelectOrAddCombo(_cbWhitelist, GetProp(props, "white-list", "false"));
            SelectOrAddCombo(_cbHardcore, GetProp(props, "hardcore", "false"));
            SetNumeric(_numRconPort, GetProp(props, "rcon.port", "25575"));
        }

        private void SaveConfigFields()
        {
            try
            {
                _config.SetBackupPreferences(_txtBackupsDirConfig.Text, _chkLocalBackupConfig.Checked);
                SyncBackupControlsFromConfig();

                DockerSettings docker = new DockerSettings();
                docker.Memory = Convert.ToString(_cbMemory.SelectedItem);
                docker.Type = Convert.ToString(_cbServerType.SelectedItem);
                docker.Version = string.IsNullOrWhiteSpace(_txtServerVersion.Text) ? "latest" : _txtServerVersion.Text.Trim();
                docker.OnlineMode = Convert.ToString(_cbOnlineMode.SelectedItem);
                _config.WriteDockerConfig(docker);

                Dictionary<string, string> props = new Dictionary<string, string>();
                props["motd"] = _txtMotd.Text;
                props["difficulty"] = Convert.ToString(_cbDifficulty.SelectedItem);
                props["gamemode"] = Convert.ToString(_cbGamemode.SelectedItem);
                props["max-players"] = _numMaxPlayers.Value.ToString();
                props["view-distance"] = _numViewDist.Value.ToString();
                props["simulation-distance"] = _numSimDist.Value.ToString();
                props["spawn-protection"] = _numSpawnProt.Value.ToString();
                props["allow-flight"] = Convert.ToString(_cbAllowFlight.SelectedItem);
                props["white-list"] = Convert.ToString(_cbWhitelist.SelectedItem);
                props["hardcore"] = Convert.ToString(_cbHardcore.SelectedItem);
                props["rcon.port"] = _numRconPort.Value.ToString();
                _config.WriteServerProperties(props);

                MessageBox.Show("Configurações salvas! Reinicie o servidor para aplicar as mudanças.", "MineSync Pro", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao salvar configurações: " + ex.Message, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string GetProp(Dictionary<string, string> props, string key, string fallback)
        {
            string val;
            return props.TryGetValue(key, out val) ? val : fallback;
        }

        private static void SelectOrAddCombo(ComboBox cb, string value)
        {
            int idx = cb.FindStringExact(value);
            if (idx >= 0) cb.SelectedIndex = idx;
            else if (!string.IsNullOrEmpty(value))
            {
                cb.Items.Add(value);
                cb.SelectedIndex = cb.Items.Count - 1;
            }
        }

        private static void SetNumeric(NumericUpDown num, string value)
        {
            int parsed;
            if (int.TryParse(value, out parsed))
            {
                num.Value = Math.Max(num.Minimum, Math.Min(num.Maximum, parsed));
            }
        }
    }
}
