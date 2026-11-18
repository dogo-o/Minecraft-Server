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
        private string _activeTab = "dashboard";
        private string _currentPlayitAddress = "";

        private DockerStatusInfo _lastDocker = new DockerStatusInfo();
        private CloudStatusInfo _lastCloud = new CloudStatusInfo();
        private List<PlayerInfo> _cachedPlayers = new List<PlayerInfo>();

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

        // Config Tab Controls
        private TextBox _txtWorkspacePath;
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
            this.MinimumSize = new Size(1040, 700);
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

        private void BuildLayout()
        {
            // Sidebar
            _sidebarPanel = new Panel();
            _sidebarPanel.Dock = DockStyle.Left;
            _sidebarPanel.Width = 225;
            _sidebarPanel.BackColor = Theme.BgSidebar;
            _sidebarPanel.Padding = new Padding(12);

            Label lblBrand = new Label();
            lblBrand.Text = "MineSync PRO";
            lblBrand.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            lblBrand.ForeColor = Theme.Emerald;
            lblBrand.Location = new Point(14, 16);
            lblBrand.AutoSize = true;

            Label lblBrandSub = new Label();
            lblBrandSub.Text = "Servidor Descentralizado";
            lblBrandSub.Font = Theme.FontSmall;
            lblBrandSub.ForeColor = Theme.TextMuted;
            lblBrandSub.Location = new Point(15, 43);
            lblBrandSub.AutoSize = true;

            _sidebarPanel.Controls.Add(lblBrand);
            _sidebarPanel.Controls.Add(lblBrandSub);

            AddNavButton("dashboard", "Dashboard", 82);
            AddNavButton("console", "Console & RCON", 126);
            AddNavButton("players", "Jogadores", 170);
            AddNavButton("backups", "Backups & Nuvem", 214);
            AddNavButton("config", "Configurações", 258);

            // Sidebar footer info
            Panel sideFooter = new Panel();
            sideFooter.Dock = DockStyle.Bottom;
            sideFooter.Height = 96;
            sideFooter.BackColor = Theme.BgSidebar;

            _lblDockerSide = new Label();
            _lblDockerSide.Text = "Docker: Verificando...";
            _lblDockerSide.Font = Theme.FontSmall;
            _lblDockerSide.ForeColor = Theme.TextMuted;
            _lblDockerSide.Location = new Point(6, 12);
            _lblDockerSide.AutoSize = true;

            _lblDriveSide = new Label();
            _lblDriveSide.Text = "Google Drive: Verificando...";
            _lblDriveSide.Font = Theme.FontSmall;
            _lblDriveSide.ForeColor = Theme.TextMuted;
            _lblDriveSide.Location = new Point(6, 36);
            _lblDriveSide.AutoSize = true;

            Label lblAppVer = new Label();
            lblAppVer.Text = "Aplicativo Nativo v2.0";
            lblAppVer.Font = Theme.FontSmall;
            lblAppVer.ForeColor = Theme.TextDim;
            lblAppVer.Location = new Point(6, 62);
            lblAppVer.AutoSize = true;

            sideFooter.Controls.Add(_lblDockerSide);
            sideFooter.Controls.Add(_lblDriveSide);
            sideFooter.Controls.Add(lblAppVer);
            _sidebarPanel.Controls.Add(sideFooter);

            // Top Header
            Panel topBar = new Panel();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 64;
            topBar.BackColor = Theme.BgSidebar;
            topBar.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.DrawLine(new Pen(Theme.Border), 0, topBar.Height - 1, topBar.Width, topBar.Height - 1);
            };

            _statusDot = new Panel();
            _statusDot.Size = new Size(12, 12);
            _statusDot.Location = new Point(20, 26);
            _statusDot.BackColor = Theme.Rose;

            _lblHeaderStatus = new Label();
            _lblHeaderStatus.Text = "Checando infraestrutura...";
            _lblHeaderStatus.Font = Theme.FontBold;
            _lblHeaderStatus.ForeColor = Theme.TextPrimary;
            _lblHeaderStatus.Location = new Point(40, 14);
            _lblHeaderStatus.AutoSize = true;

            _lblHeaderSub = new Label();
            _lblHeaderSub.Text = "Aguardando Docker e Google Drive";
            _lblHeaderSub.Font = Theme.FontSmall;
            _lblHeaderSub.ForeColor = Theme.TextMuted;
            _lblHeaderSub.Location = new Point(40, 34);
            _lblHeaderSub.AutoSize = true;

            FlowLayoutPanel pillsFlow = new FlowLayoutPanel();
            pillsFlow.Dock = DockStyle.Right;
            pillsFlow.Width = 560;
            pillsFlow.FlowDirection = FlowDirection.RightToLeft;
            pillsFlow.Padding = new Padding(0, 14, 14, 0);
            pillsFlow.WrapContents = false;

            Button btnRefreshTop = Theme.CreateButton("Atualizar", Theme.BgElevated, Theme.TextPrimary);
            btnRefreshTop.Size = new Size(86, 32);
            btnRefreshTop.Click += async delegate { await RefreshAllAsync(true); };

            _btnSoundToggle = Theme.CreateButton("Som: ON", Theme.BgElevated, Theme.Emerald);
            _btnSoundToggle.Size = new Size(80, 32);
            _btnSoundToggle.Click += delegate
            {
                _soundEnabled = !_soundEnabled;
                _btnSoundToggle.Text = _soundEnabled ? "Som: ON" : "Som: OFF";
                _btnSoundToggle.ForeColor = _soundEnabled ? Theme.Emerald : Theme.TextMuted;
            };

            _lblLockPill = CreatePillLabel("Trava: —", 130);
            _lblResourcesPill = CreatePillLabel("CPU/RAM: —", 135);

            _btnPlayitPill = Theme.CreateButton("Playit: Offline", Theme.BgElevated, Theme.Cyan);
            _btnPlayitPill.Size = new Size(165, 32);
            _btnPlayitPill.Font = Theme.FontSmall;
            _btnPlayitPill.Click += delegate
            {
                if (!string.IsNullOrEmpty(_currentPlayitAddress))
                {
                    Clipboard.SetText(_currentPlayitAddress);
                    AppendDashLog("Endereço Playit copiado: " + _currentPlayitAddress, "success");
                }
            };

            pillsFlow.Controls.Add(btnRefreshTop);
            pillsFlow.Controls.Add(_btnSoundToggle);
            pillsFlow.Controls.Add(_lblLockPill);
            pillsFlow.Controls.Add(_lblResourcesPill);
            pillsFlow.Controls.Add(_btnPlayitPill);

            topBar.Controls.Add(_statusDot);
            topBar.Controls.Add(_lblHeaderStatus);
            topBar.Controls.Add(_lblHeaderSub);
            topBar.Controls.Add(pillsFlow);

            // Content host
            _contentHost = new Panel();
            _contentHost.Dock = DockStyle.Fill;
            _contentHost.BackColor = Theme.BgBase;
            _contentHost.Padding = new Padding(16);

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

            this.Controls.Add(_contentHost);
            this.Controls.Add(topBar);
            this.Controls.Add(_sidebarPanel);
        }

        private void AddNavButton(string key, string label, int top)
        {
            Button btn = new Button();
            btn.Text = "  " + label;
            btn.TextAlign = ContentAlignment.MiddleLeft;
            btn.Location = new Point(12, top);
            btn.Size = new Size(200, 38);
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
            lbl.AutoSize = false;
            lbl.Size = new Size(width, 32);
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
            Panel root = new Panel();
            root.Dock = DockStyle.Fill;

            TableLayoutPanel topGrid = new TableLayoutPanel();
            topGrid.Dock = DockStyle.Top;
            topGrid.Height = 270;
            topGrid.ColumnCount = 2;
            topGrid.RowCount = 1;
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54f));
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));

            // Left Card: Controls & Progress
            Panel heroCard = Theme.CreateCard("Controle do Servidor", "Gerencie Docker, Playit.gg e sincronização em um clique");
            heroCard.Dock = DockStyle.Fill;
            heroCard.Margin = new Padding(0, 0, 10, 10);

            _btnStart = Theme.CreateButton("▶  Iniciar Servidor", Theme.EmeraldDark, Color.White);
            _btnStart.Size = new Size(215, 44);
            _btnStart.Location = new Point(14, 66);
            _btnStart.Click += async delegate { await RunStartWorkflowAsync(); };

            _btnStop = Theme.CreateButton("⏹  Parar e Salvar", Theme.Rose, Color.White);
            _btnStop.Size = new Size(215, 44);
            _btnStop.Location = new Point(240, 66);
            _btnStop.Enabled = false;
            _btnStop.Click += async delegate { await RunWorkflowWithHandling(_engine.StopAndSaveServerAsync()); };

            _btnEmergency = Theme.CreateButton("⚠  Salvar Emergência", Theme.Amber, Color.White);
            _btnEmergency.Size = new Size(215, 44);
            _btnEmergency.Location = new Point(14, 120);
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
            _btnKill.Size = new Size(215, 44);
            _btnKill.Location = new Point(240, 120);
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

            _lblWorkflowStep = new Label();
            _lblWorkflowStep.Text = "Aguardando comando...";
            _lblWorkflowStep.Font = Theme.FontSmall;
            _lblWorkflowStep.ForeColor = Theme.TextMuted;
            _lblWorkflowStep.Location = new Point(14, 184);
            _lblWorkflowStep.Size = new Size(445, 20);

            _workflowProgress = new ProgressBar();
            _workflowProgress.Location = new Point(14, 208);
            _workflowProgress.Size = new Size(445, 16);
            _workflowProgress.Maximum = 100;

            heroCard.Controls.Add(_btnStart);
            heroCard.Controls.Add(_btnStop);
            heroCard.Controls.Add(_btnEmergency);
            heroCard.Controls.Add(_btnKill);
            heroCard.Controls.Add(_lblWorkflowStep);
            heroCard.Controls.Add(_workflowProgress);

            // Right Card: Cloud Sync
            Panel cloudCard = Theme.CreateCard("Sincronização & Google Drive", "Comparação Local vs Nuvem (world/level.dat)");
            cloudCard.Dock = DockStyle.Fill;
            cloudCard.Margin = new Padding(10, 0, 0, 10);

            Button btnCheckDrive = Theme.CreateButton("Checar Agora", Theme.BgElevated, Theme.TextPrimary);
            btnCheckDrive.Size = new Size(105, 30);
            btnCheckDrive.Location = new Point(290, 14);
            btnCheckDrive.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnCheckDrive.Click += async delegate { await RefreshAllAsync(true); };

            _lblLocalDate = AddInfoPair(cloudCard, "Save Local:", "—", 68);
            _lblCloudDate = AddInfoPair(cloudCard, "Save na Nuvem:", "—", 96);
            _lblLockOwner = AddInfoPair(cloudCard, "Trava (lock.txt):", "—", 124);
            _lblDriveAuth = AddInfoPair(cloudCard, "Autenticação Drive:", "—", 152);
            _lblQuotaText = AddInfoPair(cloudCard, "Espaço no Drive:", "—", 180);

            _quotaBar = new ProgressBar();
            _quotaBar.Location = new Point(14, 210);
            _quotaBar.Size = new Size(375, 14);
            _quotaBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _quotaBar.Maximum = 100;

            cloudCard.Controls.Add(btnCheckDrive);
            cloudCard.Controls.Add(_quotaBar);

            topGrid.Controls.Add(heroCard, 0, 0);
            topGrid.Controls.Add(cloudCard, 1, 0);

            // Bottom Card: Live Terminal + Quick RCON
            Panel logCard = Theme.CreateCard("Terminal Ao Vivo", "Logs de execução e envio rápido de comandos RCON");
            logCard.Dock = DockStyle.Fill;

            _rtbDashLog = new RichTextBox();
            _rtbDashLog.BackColor = Theme.BgInput;
            _rtbDashLog.ForeColor = Color.FromArgb(203, 213, 225);
            _rtbDashLog.Font = Theme.FontMono;
            _rtbDashLog.BorderStyle = BorderStyle.FixedSingle;
            _rtbDashLog.ReadOnly = true;
            _rtbDashLog.Location = new Point(14, 62);
            _rtbDashLog.Size = new Size(920, 270);
            _rtbDashLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            Panel rconBar = new Panel();
            rconBar.Height = 38;
            rconBar.Dock = DockStyle.Bottom;

            _txtDashRcon = Theme.CreateTextBox(false);
            _txtDashRcon.Font = Theme.FontMono;
            _txtDashRcon.Location = new Point(0, 4);
            _txtDashRcon.Size = new Size(800, 30);
            _txtDashRcon.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _txtDashRcon.KeyDown += async delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    string cmd = _txtDashRcon.Text.Trim();
                    _txtDashRcon.Text = "";
                    await ExecuteRconFromUiAsync(cmd);
                }
            };

            Button btnSendDashRcon = Theme.CreateButton("Enviar RCON", Theme.EmeraldDark, Color.White);
            btnSendDashRcon.Size = new Size(115, 30);
            btnSendDashRcon.Location = new Point(808, 3);
            btnSendDashRcon.Anchor = AnchorStyles.Right;
            btnSendDashRcon.Click += async delegate
            {
                string cmd = _txtDashRcon.Text.Trim();
                _txtDashRcon.Text = "";
                await ExecuteRconFromUiAsync(cmd);
            };

            rconBar.Controls.Add(_txtDashRcon);
            rconBar.Controls.Add(btnSendDashRcon);

            logCard.Controls.Add(_rtbDashLog);
            logCard.Controls.Add(rconBar);

            root.Controls.Add(logCard);
            root.Controls.Add(topGrid);
            return root;
        }

        private static Label AddInfoPair(Panel parent, string labelText, string initialVal, int y)
        {
            Label lblKey = new Label();
            lblKey.Text = labelText;
            lblKey.ForeColor = Theme.TextMuted;
            lblKey.Font = Theme.FontBase;
            lblKey.Location = new Point(14, y);
            lblKey.AutoSize = true;

            Label lblVal = new Label();
            lblVal.Text = initialVal;
            lblVal.ForeColor = Theme.TextPrimary;
            lblVal.Font = Theme.FontBold;
            lblVal.Location = new Point(155, y);
            lblVal.Size = new Size(240, 20);
            lblVal.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            parent.Controls.Add(lblKey);
            parent.Controls.Add(lblVal);
            return lblVal;
        }

        private Panel BuildConsoleTab()
        {
            Panel card = Theme.CreateCard("Console Completo & RCON", "Visualize logs do Minecraft ou Playit.gg e execute comandos administrativos");
            card.Dock = DockStyle.Fill;

            Panel toolbar = new Panel();
            toolbar.Location = new Point(14, 60);
            toolbar.Size = new Size(920, 74);
            toolbar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            _cbLogSource = Theme.CreateComboBox();
            _cbLogSource.Items.AddRange(new object[] { "Servidor Minecraft (mc_server)", "Túnel Playit.gg (mc_playit)" });
            _cbLogSource.SelectedIndex = 0;
            _cbLogSource.Location = new Point(0, 4);
            _cbLogSource.Size = new Size(230, 28);
            _cbLogSource.SelectedIndexChanged += async delegate { await RefreshLogsTabAsync(); };

            _txtLogFilter = Theme.CreateTextBox(false);
            _txtLogFilter.Location = new Point(242, 4);
            _txtLogFilter.Size = new Size(220, 28);
            _txtLogFilter.TextChanged += async delegate { await RefreshLogsTabAsync(); };

            _chkAutoScroll = new CheckBox();
            _chkAutoScroll.Text = "Auto-scroll";
            _chkAutoScroll.Checked = true;
            _chkAutoScroll.ForeColor = Theme.TextMuted;
            _chkAutoScroll.Location = new Point(474, 7);
            _chkAutoScroll.AutoSize = true;

            Button btnRefreshLogs = Theme.CreateButton("Atualizar Logs", Theme.BgElevated, Theme.TextPrimary);
            btnRefreshLogs.Size = new Size(120, 30);
            btnRefreshLogs.Location = new Point(575, 2);
            btnRefreshLogs.Click += async delegate { await RefreshLogsTabAsync(); };

            toolbar.Controls.Add(_cbLogSource);
            toolbar.Controls.Add(_txtLogFilter);
            toolbar.Controls.Add(_chkAutoScroll);
            toolbar.Controls.Add(btnRefreshLogs);

            // Quick command buttons
            string[][] quickCmds = new string[][]
            {
                new string[] { "Salvar Mundo", "save-all" },
                new string[] { "Dia", "time set day" },
                new string[] { "Noite", "time set night" },
                new string[] { "Clima Limpo", "weather clear" },
                new string[] { "Chuva", "weather rain" },
                new string[] { "Listar Jogadores", "list" }
            };
            int qx = 0;
            foreach (string[] pair in quickCmds)
            {
                string title = pair[0];
                string cmd = pair[1];
                Button qBtn = Theme.CreateButton(title, Theme.BgElevated, Theme.Emerald);
                qBtn.Size = new Size(118, 28);
                qBtn.Font = Theme.FontSmall;
                qBtn.Location = new Point(qx, 42);
                qBtn.Click += async delegate { await ExecuteRconFromUiAsync(cmd); };
                toolbar.Controls.Add(qBtn);
                qx += 124;
            }

            _rtbFullLog = new RichTextBox();
            _rtbFullLog.BackColor = Theme.BgInput;
            _rtbFullLog.ForeColor = Color.FromArgb(203, 213, 225);
            _rtbFullLog.Font = Theme.FontMono;
            _rtbFullLog.BorderStyle = BorderStyle.FixedSingle;
            _rtbFullLog.ReadOnly = true;
            _rtbFullLog.Location = new Point(14, 140);
            _rtbFullLog.Size = new Size(920, 440);
            _rtbFullLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            Panel bottomBar = new Panel();
            bottomBar.Dock = DockStyle.Bottom;
            bottomBar.Height = 38;

            _txtFullRcon = Theme.CreateTextBox(false);
            _txtFullRcon.Font = Theme.FontMono;
            _txtFullRcon.Location = new Point(0, 4);
            _txtFullRcon.Size = new Size(790, 30);
            _txtFullRcon.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _txtFullRcon.KeyDown += async delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    string cmd = _txtFullRcon.Text.Trim();
                    _txtFullRcon.Text = "";
                    await ExecuteRconFromUiAsync(cmd);
                }
                else if (e.KeyCode == Keys.Up && _rconHistoryIdx + 1 < _rconHistory.Count)
                {
                    _rconHistoryIdx++;
                    _txtFullRcon.Text = _rconHistory[_rconHistoryIdx];
                    _txtFullRcon.SelectionStart = _txtFullRcon.Text.Length;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Down)
                {
                    if (_rconHistoryIdx > 0)
                    {
                        _rconHistoryIdx--;
                        _txtFullRcon.Text = _rconHistory[_rconHistoryIdx];
                    }
                    else
                    {
                        _rconHistoryIdx = -1;
                        _txtFullRcon.Text = "";
                    }
                    e.SuppressKeyPress = true;
                }
            };

            Button btnExecRcon = Theme.CreateButton("Executar RCON", Theme.EmeraldDark, Color.White);
            btnExecRcon.Size = new Size(125, 30);
            btnExecRcon.Location = new Point(798, 3);
            btnExecRcon.Anchor = AnchorStyles.Right;
            btnExecRcon.Click += async delegate
            {
                string cmd = _txtFullRcon.Text.Trim();
                _txtFullRcon.Text = "";
                await ExecuteRconFromUiAsync(cmd);
            };

            bottomBar.Controls.Add(_txtFullRcon);
            bottomBar.Controls.Add(btnExecRcon);

            card.Controls.Add(toolbar);
            card.Controls.Add(_rtbFullLog);
            card.Controls.Add(bottomBar);
            return card;
        }

        private Panel BuildPlayersTab()
        {
            Panel card = Theme.CreateCard("Jogadores & Estatísticas do Mundo", "Dados reais lidos de usercache, ops, whitelist e world/players/stats");
            card.Dock = DockStyle.Fill;

            _lblPlayersSummary = new Label();
            _lblPlayersSummary.Text = "Carregando...";
            _lblPlayersSummary.ForeColor = Theme.TextMuted;
            _lblPlayersSummary.Location = new Point(14, 62);
            _lblPlayersSummary.AutoSize = true;

            _gridPlayers = new DataGridView();
            Theme.StyleDataGrid(_gridPlayers);
            _gridPlayers.Location = new Point(14, 90);
            _gridPlayers.Size = new Size(920, 480);
            _gridPlayers.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            _gridPlayers.Columns.Add("Name", "Jogador");
            _gridPlayers.Columns.Add("Status", "Status");
            _gridPlayers.Columns.Add("Role", "OP");
            _gridPlayers.Columns.Add("Whitelist", "Whitelist");
            _gridPlayers.Columns.Add("PlayTime", "Tempo Jogado");
            _gridPlayers.Columns.Add("MobKills", "Mobs Mortos");
            _gridPlayers.Columns.Add("Deaths", "Mortes");
            _gridPlayers.Columns.Add("BlocksMined", "Blocos Minerados");
            _gridPlayers.Columns.Add("DistanceKm", "Distância");

            Panel actionBar = new Panel();
            actionBar.Dock = DockStyle.Bottom;
            actionBar.Height = 44;

            Button btnOp = Theme.CreateButton("Alternar OP", Theme.BgElevated, Theme.Emerald);
            btnOp.Size = new Size(120, 34);
            btnOp.Location = new Point(0, 4);
            btnOp.Click += async delegate
            {
                PlayerInfo p = GetSelectedPlayer();
                if (p == null) return;
                await RunPlayerActionAsync(p.IsOp ? "deop" : "op", p, null);
            };

            Button btnWl = Theme.CreateButton("Alternar Whitelist", Theme.BgElevated, Theme.Cyan);
            btnWl.Size = new Size(145, 34);
            btnWl.Location = new Point(128, 4);
            btnWl.Click += async delegate
            {
                PlayerInfo p = GetSelectedPlayer();
                if (p == null) return;
                await RunPlayerActionAsync(p.Whitelisted ? "whitelist_remove" : "whitelist_add", p, null);
            };

            _cbGamemodeSelect = Theme.CreateComboBox();
            _cbGamemodeSelect.Items.AddRange(new object[] { "survival", "creative", "spectator", "adventure" });
            _cbGamemodeSelect.SelectedIndex = 0;
            _cbGamemodeSelect.Location = new Point(282, 7);
            _cbGamemodeSelect.Size = new Size(115, 28);

            Button btnGm = Theme.CreateButton("Aplicar Gamemode", Theme.BgElevated, Theme.TextPrimary);
            btnGm.Size = new Size(140, 34);
            btnGm.Location = new Point(404, 4);
            btnGm.Click += async delegate
            {
                PlayerInfo p = GetSelectedPlayer();
                if (p == null) return;
                await RunPlayerActionAsync("gamemode", p, Convert.ToString(_cbGamemodeSelect.SelectedItem));
            };

            Button btnKick = Theme.CreateButton("Expulsar (Kick)", Theme.Rose, Color.White);
            btnKick.Size = new Size(130, 34);
            btnKick.Location = new Point(552, 4);
            btnKick.Click += async delegate
            {
                PlayerInfo p = GetSelectedPlayer();
                if (p == null) return;
                await RunPlayerActionAsync("kick", p, "Expulso pelo painel administrativo");
            };

            Button btnRefreshP = Theme.CreateButton("Atualizar", Theme.BgElevated, Theme.TextPrimary);
            btnRefreshP.Size = new Size(105, 34);
            btnRefreshP.Location = new Point(692, 4);
            btnRefreshP.Click += async delegate { await RefreshPlayersTabAsync(); };

            actionBar.Controls.Add(btnOp);
            actionBar.Controls.Add(btnWl);
            actionBar.Controls.Add(_cbGamemodeSelect);
            actionBar.Controls.Add(btnGm);
            actionBar.Controls.Add(btnKick);
            actionBar.Controls.Add(btnRefreshP);

            card.Controls.Add(_lblPlayersSummary);
            card.Controls.Add(_gridPlayers);
            card.Controls.Add(actionBar);
            return card;
        }

        private Panel BuildBackupsTab()
        {
            Panel root = new Panel();
            root.Dock = DockStyle.Fill;

            Panel toolsCard = Theme.CreateCard("Ferramentas da Nuvem & Snapshots", "Manutenção de trava lock.txt, reconexão do Google Drive e backups .zip");
            toolsCard.Dock = DockStyle.Top;
            toolsCard.Height = 128;

            _btnCreateBackup = Theme.CreateButton("Criar Backup Agora (.zip)", Theme.EmeraldDark, Color.White);
            _btnCreateBackup.Size = new Size(195, 38);
            _btnCreateBackup.Location = new Point(14, 70);
            _btnCreateBackup.Click += async delegate { await CreateManualBackupAsync(); };

            Button btnReconnect = Theme.CreateButton("Reconectar Google Drive", Theme.BgElevated, Theme.Cyan);
            btnReconnect.Size = new Size(185, 38);
            btnReconnect.Location = new Point(218, 70);
            btnReconnect.Click += delegate { _engine.OpenDriveReconnectWindow(); };

            Button btnUnlock = Theme.CreateButton("Forçar Remoção de Trava", Theme.Amber, Color.White);
            btnUnlock.Size = new Size(195, 38);
            btnUnlock.Location = new Point(412, 70);
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

            Button btnOpenFolder = Theme.CreateButton("Abrir Pasta Backups", Theme.BgElevated, Theme.TextPrimary);
            btnOpenFolder.Size = new Size(155, 38);
            btnOpenFolder.Location = new Point(616, 70);
            btnOpenFolder.Click += delegate
            {
                Directory.CreateDirectory(_config.BackupsDir);
                Process.Start("explorer.exe", _config.BackupsDir);
            };

            toolsCard.Controls.Add(_btnCreateBackup);
            toolsCard.Controls.Add(btnReconnect);
            toolsCard.Controls.Add(btnUnlock);
            toolsCard.Controls.Add(btnOpenFolder);

            TableLayoutPanel split = new TableLayoutPanel();
            split.Dock = DockStyle.Fill;
            split.ColumnCount = 2;
            split.RowCount = 1;
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            split.Padding = new Padding(0, 12, 0, 0);

            Panel localCard = Theme.CreateCard("Backups Locais (Pasta Backups/)", "Restaure ou gerencie snapshots salvos neste PC");
            localCard.Dock = DockStyle.Fill;
            localCard.Margin = new Padding(0, 0, 8, 0);

            _gridLocalBackups = new DataGridView();
            Theme.StyleDataGrid(_gridLocalBackups);
            _gridLocalBackups.Location = new Point(14, 66);
            _gridLocalBackups.Size = new Size(470, 330);
            _gridLocalBackups.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _gridLocalBackups.Columns.Add("FileName", "Arquivo");
            _gridLocalBackups.Columns.Add("Size", "Tamanho");
            _gridLocalBackups.Columns.Add("Date", "Data");

            Panel localBtns = new Panel();
            localBtns.Dock = DockStyle.Bottom;
            localBtns.Height = 40;

            Button btnRestore = Theme.CreateButton("Restaurar Selecionado", Theme.Amber, Color.White);
            btnRestore.Size = new Size(165, 34);
            btnRestore.Location = new Point(0, 4);
            btnRestore.Click += async delegate { await RestoreSelectedBackupAsync(); };

            Button btnDelBackup = Theme.CreateButton("Excluir", Theme.Rose, Color.White);
            btnDelBackup.Size = new Size(95, 34);
            btnDelBackup.Location = new Point(174, 4);
            btnDelBackup.Click += async delegate { await DeleteSelectedBackupAsync(); };

            Button btnRefreshBk = Theme.CreateButton("Atualizar", Theme.BgElevated, Theme.TextPrimary);
            btnRefreshBk.Size = new Size(95, 34);
            btnRefreshBk.Location = new Point(278, 4);
            btnRefreshBk.Click += async delegate { await RefreshBackupsTabAsync(); };

            localBtns.Controls.Add(btnRestore);
            localBtns.Controls.Add(btnDelBackup);
            localBtns.Controls.Add(btnRefreshBk);

            localCard.Controls.Add(_gridLocalBackups);
            localCard.Controls.Add(localBtns);

            Panel cloudCard = Theme.CreateCard("Backups na Nuvem (Drive)", "Arquivos em MinecraftSync/Backups");
            cloudCard.Dock = DockStyle.Fill;
            cloudCard.Margin = new Padding(8, 0, 0, 0);

            _gridCloudBackups = new DataGridView();
            Theme.StyleDataGrid(_gridCloudBackups);
            _gridCloudBackups.Location = new Point(14, 66);
            _gridCloudBackups.Size = new Size(380, 370);
            _gridCloudBackups.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _gridCloudBackups.Columns.Add("FileName", "Arquivo");
            _gridCloudBackups.Columns.Add("Size", "Tamanho");
            _gridCloudBackups.Columns.Add("Date", "Data");

            cloudCard.Controls.Add(_gridCloudBackups);

            split.Controls.Add(localCard, 0, 0);
            split.Controls.Add(cloudCard, 1, 0);

            root.Controls.Add(split);
            root.Controls.Add(toolsCard);
            return root;
        }

        private Panel BuildConfigTab()
        {
            Panel card = Theme.CreateCard("Configurações do Servidor", "Edite pasta do servidor, parâmetros do Docker e propriedades de server.properties");
            card.Dock = DockStyle.Fill;
            card.AutoScroll = true;

            Button btnSaveCfg = Theme.CreateButton("Salvar Configurações", Theme.EmeraldDark, Color.White);
            btnSaveCfg.Size = new Size(175, 36);
            btnSaveCfg.Location = new Point(730, 14);
            btnSaveCfg.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSaveCfg.Click += delegate { SaveConfigFields(); };
            card.Controls.Add(btnSaveCfg);

            int y = 72;
            AddFieldLabel(card, "Pasta do Servidor (Workspace):", 14, y);
            _txtWorkspacePath = Theme.CreateTextBox(true);
            _txtWorkspacePath.Location = new Point(220, y - 3);
            _txtWorkspacePath.Size = new Size(560, 26);

            Button btnBrowseWs = Theme.CreateButton("Alterar...", Theme.BgElevated, Theme.TextPrimary);
            btnBrowseWs.Size = new Size(95, 28);
            btnBrowseWs.Location = new Point(790, y - 4);
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
            card.Controls.Add(_txtWorkspacePath);
            card.Controls.Add(btnBrowseWs);
            y += 42;

            // Docker Section
            AddSectionHeader(card, "Recursos Docker & Túnel Playit.gg", 14, y);
            y += 30;

            _cbMemory = Theme.CreateComboBox();
            _cbMemory.Items.AddRange(new object[] { "2G", "4G", "6G", "8G", "10G", "12G", "16G" });
            AddFieldControl(card, "Memória RAM:", _cbMemory, 14, y, 220);

            _cbServerType = Theme.CreateComboBox();
            _cbServerType.Items.AddRange(new object[] { "VANILLA", "PAPER", "FABRIC", "FORGE" });
            AddFieldControl(card, "Tipo de Servidor:", _cbServerType, 470, y, 220);
            y += 38;

            _txtServerVersion = Theme.CreateTextBox(false);
            AddFieldControl(card, "Versão do Minecraft:", _txtServerVersion, 14, y, 220);

            _cbOnlineMode = Theme.CreateComboBox();
            _cbOnlineMode.Items.AddRange(new object[] { "TRUE", "FALSE" });
            AddFieldControl(card, "Online Mode (Original):", _cbOnlineMode, 470, y, 220);
            y += 38;

            _txtPlayitInfo = Theme.CreateTextBox(true);
            AddFieldControl(card, "Chave Playit.toml:", _txtPlayitInfo, 14, y, 676);
            y += 46;

            // server.properties Section
            AddSectionHeader(card, "Propriedades do Mundo (dados_mine/server.properties)", 14, y);
            y += 30;

            _txtMotd = Theme.CreateTextBox(false);
            AddFieldControl(card, "MOTD (Descrição):", _txtMotd, 14, y, 676);
            y += 38;

            _cbDifficulty = Theme.CreateComboBox();
            _cbDifficulty.Items.AddRange(new object[] { "peaceful", "easy", "normal", "hard" });
            AddFieldControl(card, "Dificuldade:", _cbDifficulty, 14, y, 220);

            _cbGamemode = Theme.CreateComboBox();
            _cbGamemode.Items.AddRange(new object[] { "survival", "creative", "adventure", "spectator" });
            AddFieldControl(card, "Modo de Jogo:", _cbGamemode, 470, y, 220);
            y += 38;

            _numMaxPlayers = CreateNumeric(1, 200, 2);
            AddFieldControl(card, "Máximo de Jogadores:", _numMaxPlayers, 14, y, 220);

            _numViewDist = CreateNumeric(2, 32, 16);
            AddFieldControl(card, "View Distance (Chunks):", _numViewDist, 470, y, 220);
            y += 38;

            _numSimDist = CreateNumeric(2, 32, 10);
            AddFieldControl(card, "Simulation Distance:", _numSimDist, 14, y, 220);

            _numSpawnProt = CreateNumeric(0, 128, 16);
            AddFieldControl(card, "Proteção do Spawn:", _numSpawnProt, 470, y, 220);
            y += 38;

            _cbAllowFlight = Theme.CreateComboBox();
            _cbAllowFlight.Items.AddRange(new object[] { "false", "true" });
            AddFieldControl(card, "Permitir Voo (allow-flight):", _cbAllowFlight, 14, y, 220);

            _cbWhitelist = Theme.CreateComboBox();
            _cbWhitelist.Items.AddRange(new object[] { "false", "true" });
            AddFieldControl(card, "Whitelist Ativa:", _cbWhitelist, 470, y, 220);
            y += 38;

            _cbHardcore = Theme.CreateComboBox();
            _cbHardcore.Items.AddRange(new object[] { "false", "true" });
            AddFieldControl(card, "Modo Hardcore:", _cbHardcore, 14, y, 220);

            _numRconPort = CreateNumeric(1024, 65535, 25575);
            AddFieldControl(card, "Porta RCON:", _numRconPort, 470, y, 220);

            return card;
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

        private static void AddSectionHeader(Panel parent, string text, int x, int y)
        {
            Label lbl = new Label();
            lbl.Text = text;
            lbl.Font = Theme.FontBold;
            lbl.ForeColor = Theme.Emerald;
            lbl.Location = new Point(x, y);
            lbl.AutoSize = true;
            parent.Controls.Add(lbl);
        }

        private static void AddFieldLabel(Panel parent, string text, int x, int y)
        {
            Label lbl = new Label();
            lbl.Text = text;
            lbl.Font = Theme.FontBase;
            lbl.ForeColor = Theme.TextMuted;
            lbl.Location = new Point(x, y);
            lbl.AutoSize = true;
            parent.Controls.Add(lbl);
        }

        private static void AddFieldControl(Panel parent, string label, Control ctrl, int x, int y, int width)
        {
            AddFieldLabel(parent, label, x, y);
            ctrl.Location = new Point(x + 206, y - 3);
            ctrl.Size = new Size(width, 26);
            parent.Controls.Add(ctrl);
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

            // Cloud summary labels
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

        private async Task ExecuteRconFromUiAsync(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return;
            _rconHistory.Insert(0, cmd);
            if (_rconHistory.Count > 30) _rconHistory.RemoveAt(30);
            _rconHistoryIdx = -1;

            try
            {
                string res = await RconClient.SendCommandAsync(_config, cmd, 4500);
                string output = string.IsNullOrWhiteSpace(res) ? "(Comando executado sem resposta)" : res;
                AppendDashLog("RCON > " + cmd + " => " + output, "success");
                if (_activeTab == "console") await RefreshLogsTabAsync();
            }
            catch (Exception ex)
            {
                AppendDashLog("Erro RCON: " + ex.Message, "error");
                MessageBox.Show(ex.Message, "Erro RCON", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
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

        private void LoadConfigFields()
        {
            _txtWorkspacePath.Text = _config.WorkspaceDir;
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
