using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MineSyncApp.Core;

namespace MineSyncApp.UI
{
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
        public static readonly Font FontTitle = new Font("Segoe UI", 12.5f, FontStyle.Bold);
        public static readonly Font FontHero = new Font("Segoe UI", 14f, FontStyle.Bold);
        public static readonly Font FontSmall = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        public static readonly Font FontMono = new Font("Consolas", 9.5f, FontStyle.Regular);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

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

        public static Panel CreateCard(string title, string subtitle)
        {
            Panel card = new Panel();
            card.BackColor = BgCard;
            card.Padding = new Padding(14);
            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle, Border, ButtonBorderStyle.Solid);
            };

            if (!string.IsNullOrEmpty(title))
            {
                Label lblTitle = new Label();
                lblTitle.Text = title;
                lblTitle.Font = FontTitle;
                lblTitle.ForeColor = TextPrimary;
                lblTitle.AutoSize = true;
                lblTitle.Location = new Point(14, 12);
                card.Controls.Add(lblTitle);

                if (!string.IsNullOrEmpty(subtitle))
                {
                    Label lblSub = new Label();
                    lblSub.Text = subtitle;
                    lblSub.Font = FontSmall;
                    lblSub.ForeColor = TextMuted;
                    lblSub.AutoSize = true;
                    lblSub.Location = new Point(14, 36);
                    card.Controls.Add(lblSub);
                }
            }
            return card;
        }

        public static Button CreateButton(string text, Color backColor, Color foreColor)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.BackColor = backColor;
            btn.ForeColor = foreColor;
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

            Label lblTitle = new Label();
            lblTitle.Text = "Os saves Local e da Nuvem estão diferentes!";
            lblTitle.Font = Theme.FontTitle;
            lblTitle.ForeColor = Color.FromArgb(251, 191, 36);
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(20, 18);
            this.Controls.Add(lblTitle);

            Label lblDesc = new Label();
            lblDesc.Text = string.Format(
                "Diferença detectada de {0} minutos em world/level.dat. Escolha qual save você deseja carregar:",
                cloud.DiffMinutes
            );
            lblDesc.Font = Theme.FontBase;
            lblDesc.ForeColor = Theme.TextMuted;
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
            Panel box = new Panel();
            box.BackColor = Theme.BgCard;
            box.Size = new Size(243, 96);
            box.Location = loc;
            box.Paint += delegate(object s, PaintEventArgs e)
            {
                ControlPaint.DrawBorder(e.Graphics, box.ClientRectangle, Theme.Border, ButtonBorderStyle.Solid);
            };

            Label t = new Label();
            t.Text = title;
            t.Font = Theme.FontBold;
            t.ForeColor = Theme.TextPrimary;
            t.AutoSize = true;
            t.Location = new Point(12, 12);

            Label d = new Label();
            d.Text = dateStr;
            d.Font = Theme.FontBase;
            d.ForeColor = Theme.TextMuted;
            d.AutoSize = true;
            d.Location = new Point(12, 36);

            Label b = new Label();
            b.Text = tag;
            b.Font = Theme.FontBold;
            b.ForeColor = tagColor;
            b.AutoSize = true;
            b.Location = new Point(12, 64);

            box.Controls.Add(t);
            box.Controls.Add(d);
            box.Controls.Add(b);
            return box;
        }
    }
}
