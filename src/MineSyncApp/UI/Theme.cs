using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MineSyncApp.Core;

namespace MineSyncApp.UI
{
    public class CardPanel : Panel
    {
        public CardPanel()
        {
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
            this.BackColor = Theme.BgCard;
            this.Padding = new Padding(14);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle rect = new Rectangle(0, 0, Math.Max(1, this.Width - 1), Math.Max(1, this.Height - 1));
            using (Pen p = new Pen(Theme.Border, 1))
            {
                e.Graphics.DrawRectangle(p, rect);
            }
        }
    }

    public class StyledButton : Button
    {
        public Color ActiveBackColor { get; set; }
        public Color ActiveForeColor { get; set; }

        private static readonly Color DisabledBackColor = Color.FromArgb(22, 29, 41);
        private static readonly Color DisabledBorderColor = Color.FromArgb(35, 46, 62);
        private static readonly Color DisabledForeColor = Color.FromArgb(78, 92, 112);

        public StyledButton(Color activeBackColor, Color activeForeColor)
        {
            ActiveBackColor = activeBackColor;
            ActiveForeColor = activeForeColor;
            ApplyStateColors();
        }

        private void ApplyStateColors()
        {
            if (this.Enabled)
            {
                this.BackColor = ActiveBackColor;
                this.ForeColor = ActiveForeColor;
                this.FlatAppearance.BorderColor = ControlPaint.Light(ActiveBackColor, 0.25f);
            }
            else
            {
                this.BackColor = DisabledBackColor;
                this.ForeColor = DisabledForeColor;
                this.FlatAppearance.BorderColor = DisabledBorderColor;
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            ApplyStateColors();
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            if (!this.Enabled)
            {
                using (SolidBrush bg = new SolidBrush(DisabledBackColor))
                {
                    pevent.Graphics.FillRectangle(bg, this.ClientRectangle);
                }
                Rectangle borderRect = new Rectangle(0, 0, Math.Max(1, this.Width - 1), Math.Max(1, this.Height - 1));
                using (Pen borderPen = new Pen(DisabledBorderColor, 1))
                {
                    pevent.Graphics.DrawRectangle(borderPen, borderRect);
                }
                TextRenderer.DrawText(
                    pevent.Graphics,
                    this.Text,
                    this.Font,
                    this.ClientRectangle,
                    DisabledForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis
                );
                return;
            }
            base.OnPaint(pevent);
        }
    }

    public static class Theme
    {
        public static readonly Color BgBase = Color.FromArgb(11, 15, 23);
        public static readonly Color BgSidebar = Color.FromArgb(14, 20, 31);
        public static readonly Color BgCard = Color.FromArgb(19, 27, 40);
        public static readonly Color BgElevated = Color.FromArgb(26, 36, 52);
        public static readonly Color BgInput = Color.FromArgb(7, 10, 16);
        public static readonly Color Border = Color.FromArgb(40, 54, 74);

        public static readonly Color TextPrimary = Color.FromArgb(241, 245, 249);
        public static readonly Color TextMuted = Color.FromArgb(148, 163, 184);
        public static readonly Color TextDim = Color.FromArgb(100, 116, 139);

        public static readonly Color Emerald = Color.FromArgb(16, 185, 129);
        public static readonly Color EmeraldDark = Color.FromArgb(5, 150, 105);
        public static readonly Color Cyan = Color.FromArgb(6, 182, 212);
        public static readonly Color Amber = Color.FromArgb(217, 119, 6);
        public static readonly Color Rose = Color.FromArgb(225, 29, 72);
        public static readonly Color Crimson = Color.FromArgb(153, 27, 27);

        public static readonly Font FontBase = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        public static readonly Font FontBold = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        public static readonly Font FontTitle = new Font("Segoe UI", 12f, FontStyle.Bold);
        public static readonly Font FontSmall = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        public static readonly Font FontMono = new Font("Consolas", 9.5f, FontStyle.Regular);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private const int EmSetCueBanner = 0x1501;

        public static void ApplyDarkTitleBar(Form form)
        {
            try
            {
                int useDark = 1;
                if (DwmSetWindowAttribute(form.Handle, 20, ref useDark, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(form.Handle, 19, ref useDark, sizeof(int));
                }
            }
            catch
            {
            }
        }

        public static void SetPlaceholder(TextBox tb, string placeholder)
        {
            try
            {
                if (tb.IsHandleCreated)
                {
                    SendMessage(tb.Handle, EmSetCueBanner, (IntPtr)1, placeholder ?? "");
                }
                else
                {
                    tb.HandleCreated += delegate
                    {
                        SendMessage(tb.Handle, EmSetCueBanner, (IntPtr)1, placeholder ?? "");
                    };
                }
            }
            catch
            {
            }
        }

        public static Panel CreateCardHeader(string title, string subtitle, Control rightControl)
        {
            TableLayoutPanel header = new TableLayoutPanel();
            header.Dock = DockStyle.Top;
            header.Height = string.IsNullOrEmpty(subtitle) ? 34 : 50;
            header.ColumnCount = 2;
            header.RowCount = 1;
            header.Margin = new Padding(0);
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Panel textStack = new Panel();
            textStack.Size = new Size(100, 44);
            textStack.Margin = new Padding(0);
            textStack.Dock = DockStyle.Fill;

            Label lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.UseMnemonic = false;
            lblTitle.Font = FontTitle;
            lblTitle.ForeColor = TextPrimary;
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(0, 0);
            textStack.Controls.Add(lblTitle);

            if (!string.IsNullOrEmpty(subtitle))
            {
                Label lblSub = new Label();
                lblSub.Text = subtitle;
                lblSub.UseMnemonic = false;
                lblSub.Font = FontSmall;
                lblSub.ForeColor = TextMuted;
                lblSub.AutoSize = true;
                lblSub.Location = new Point(0, 23);
                textStack.Controls.Add(lblSub);
            }

            header.Controls.Add(textStack, 0, 0);
            if (rightControl != null)
            {
                rightControl.Margin = new Padding(0);
                rightControl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                header.Controls.Add(rightControl, 1, 0);
            }

            return header;
        }

        public static Button CreateButton(string text, Color backColor, Color foreColor)
        {
            StyledButton btn = new StyledButton(backColor, foreColor);
            btn.Text = text;
            btn.UseMnemonic = false;
            btn.Font = FontBold;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = ControlPaint.Light(backColor, 0.25f);
            btn.Cursor = Cursors.Hand;
            btn.Height = 36;
            return btn;
        }

        public static TextBox CreateTextBox(bool readOnly)
        {
            TextBox tb = new TextBox();
            tb.BackColor = BgInput;
            tb.ForeColor = TextPrimary;
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.Font = FontBase;
            tb.ReadOnly = readOnly;
            return tb;
        }

        public static ComboBox CreateComboBox()
        {
            ComboBox cb = new ComboBox();
            cb.BackColor = BgInput;
            cb.ForeColor = TextPrimary;
            cb.FlatStyle = FlatStyle.Flat;
            cb.DropDownStyle = ComboBoxStyle.DropDownList;
            cb.Font = FontBase;
            return cb;
        }

        public static Label CreateLabel(string text, Font font, Color color)
        {
            Label lbl = new Label();
            lbl.Text = text;
            lbl.UseMnemonic = false;
            lbl.Font = font ?? FontBase;
            lbl.ForeColor = color;
            lbl.AutoSize = true;
            return lbl;
        }

        public static void StyleDataGrid(DataGridView grid)
        {
            grid.BackgroundColor = BgInput;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.GridColor = Border;
            grid.EnableHeadersVisualStyles = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.ReadOnly = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            grid.ColumnHeadersDefaultCellStyle.BackColor = BgElevated;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextPrimary;
            grid.ColumnHeadersDefaultCellStyle.Font = FontBold;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = BgElevated;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersHeight = 34;

            grid.DefaultCellStyle.BackColor = BgCard;
            grid.DefaultCellStyle.ForeColor = TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(20, 83, 62);
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.DefaultCellStyle.Font = FontBase;
            grid.RowTemplate.Height = 32;
        }
    }

    public class ConflictForm : Form
    {
        public MapConflictResolution Resolution { get; private set; }

        public ConflictForm(CloudStatusInfo cloud)
        {
            Resolution = MapConflictResolution.Cancel;

            this.Text = "Conflito de Versão do Mapa — MineSync Pro";
            this.Size = new Size(560, 310);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Theme.BgBase;
            this.ForeColor = Theme.TextPrimary;

            Theme.ApplyDarkTitleBar(this);

            Label lblTitle = Theme.CreateLabel("Os saves Local e da Nuvem estão diferentes!", Theme.FontTitle, Color.FromArgb(251, 191, 36));
            lblTitle.Location = new Point(20, 18);
            this.Controls.Add(lblTitle);

            Label lblDesc = Theme.CreateLabel(
                string.Format("Diferença detectada de {0} minutos em world/level.dat. Escolha qual save você deseja carregar:", cloud.DiffMinutes),
                Theme.FontBase,
                Theme.TextMuted
            );
            lblDesc.AutoSize = false;
            lblDesc.Size = new Size(505, 40);
            lblDesc.Location = new Point(20, 48);
            this.Controls.Add(lblDesc);

            Panel cloudBox = BuildBox(
                "NUVEM (Google Drive)",
                cloud.CloudTime.HasValue ? cloud.CloudTime.Value.ToString("dd/MM/yyyy HH:mm") : "Não encontrado",
                cloud.NewerSource == "cloud" ? "MAIS RECENTE" : "Mais antigo",
                cloud.NewerSource == "cloud" ? Theme.Emerald : Theme.Amber,
                new Point(20, 98)
            );
            this.Controls.Add(cloudBox);

            Panel localBox = BuildBox(
                "LOCAL (Este Computador)",
                cloud.LocalTime.HasValue ? cloud.LocalTime.Value.ToString("dd/MM/yyyy HH:mm") : "Não encontrado",
                cloud.NewerSource == "local" ? "MAIS RECENTE" : "Mais antigo",
                cloud.NewerSource == "local" ? Theme.Emerald : Theme.Amber,
                new Point(282, 98)
            );
            this.Controls.Add(localBox);

            Button btnCloud = Theme.CreateButton("Baixar da Nuvem", Theme.EmeraldDark, Color.White);
            btnCloud.Size = new Size(165, 38);
            btnCloud.Location = new Point(20, 212);
            btnCloud.Click += delegate
            {
                Resolution = MapConflictResolution.DownloadCloud;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            Button btnLocal = Theme.CreateButton("Manter Save Local", Theme.Amber, Color.White);
            btnLocal.Size = new Size(165, 38);
            btnLocal.Location = new Point(195, 212);
            btnLocal.Click += delegate
            {
                Resolution = MapConflictResolution.KeepLocal;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            Button btnCancel = Theme.CreateButton("Cancelar", Theme.BgElevated, Theme.TextPrimary);
            btnCancel.Size = new Size(155, 38);
            btnCancel.Location = new Point(370, 212);
            btnCancel.Click += delegate
            {
                Resolution = MapConflictResolution.Cancel;
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };

            this.Controls.Add(btnCloud);
            this.Controls.Add(btnLocal);
            this.Controls.Add(btnCancel);
        }

        private static Panel BuildBox(string title, string dateStr, string tag, Color tagColor, Point loc)
        {
            CardPanel box = new CardPanel();
            box.Padding = new Padding(10);
            box.Size = new Size(243, 96);
            box.Location = loc;

            Label t = Theme.CreateLabel(title, Theme.FontBold, Theme.TextPrimary);
            t.Location = new Point(12, 12);

            Label d = Theme.CreateLabel(dateStr, Theme.FontBase, Theme.TextMuted);
            d.Location = new Point(12, 36);

            Label b = Theme.CreateLabel(tag, Theme.FontBold, tagColor);
            b.Location = new Point(12, 64);

            box.Controls.Add(t);
            box.Controls.Add(d);
            box.Controls.Add(b);
            return box;
        }
    }
}
