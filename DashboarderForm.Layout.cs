using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LomsonLib.UI;

namespace CustomIconDashboarderPlugin
{
    public partial class DashboarderForm
    {
        private readonly Dictionary<ListView, int[]> m_columnMinimums = new Dictionary<ListView, int[]>();
        private Font m_dashboardFont;
        private Font m_dashboardBoldFont;
        private float m_layoutScale = 1f;
        private bool m_fittingColumns;

        private int D(int value) { return (int)Math.Round(value * m_layoutScale); }

        private void ConfigureDashboardLayout()
        {
            using (Graphics graphics = CreateGraphics()) m_layoutScale = graphics.DpiX / 96f;
            AutoScaleMode = AutoScaleMode.None;
            m_dashboardFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            m_dashboardBoldFont = new Font(m_dashboardFont, FontStyle.Bold);
            SetDashboardFont(this);
            Text = "Custom Icon Dashboard";
            ClientSize = new Size(D(1360), D(820));
            tlp_main.Padding = new Padding(D(12));
            tlp_main.RowStyles.Clear();
            tlp_main.RowCount = 1;
            tlp_main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            spc_mainSplitter.BorderStyle = BorderStyle.None;
            spc_mainSplitter.SplitterWidth = D(6);
            spc_mainSplitter.Panel1MinSize = D(340);
            spc_mainSplitter.Panel2MinSize = D(360);
            tlp_topright.Margin = Padding.Empty;
            tlp_right.Margin = new Padding(D(10), 0, 0, 0);
            foreach (TabControl tabs in new[] { tco_lists, tco_right }) { tabs.Margin = Padding.Empty; tabs.Padding = new Point(D(12), D(6)); }
            foreach (TabPage page in new[] { tpa_listAllIcons, tpa_listAllEntries, tpa_IconUsage, tpa_DownloadResult }) page.Padding = new Padding(D(8));
            ConfigureActions(tlp_allIcons, flp_iconActions, cbo_iconActionSelector, btn_performIconAction, btn_stopIconAction, cb_allIconsSelection);
            ConfigureActions(tlp_allEntries, flp_entryAction, cbo_entryActionSelector, btn_performEntryAction, btn_stopEntryAction, cb_allEntriesSelection);

            tlp_right.RowStyles.Clear();
            tlp_right.RowStyles.Add(new RowStyle(SizeType.Absolute, D(210)));
            tlp_right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tlp_right.RowStyles.Add(new RowStyle(SizeType.Absolute, D(44)));
            tlp_upperRight.AutoSize = false;
            tlp_upperRight.Margin = Padding.Empty;
            tlp_upperRight.Padding = new Padding(D(8), 0, D(8), D(8));
            tlp_upperRight.RowStyles.Clear();
            tlp_upperRight.RowStyles.Add(new RowStyle(SizeType.Absolute, D(30)));
            tlp_upperRight.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tlp_upperRight.RowStyles.Add(new RowStyle(SizeType.Absolute, D(26)));
            PreviewColumns(tlp_upperRight);
            PreviewColumns(scltp_icons);
            scltp_icons.Margin = Padding.Empty;
            scltp_icons.SizeChanged += delegate { FitPreviewBoxes(); };
            scltp_downloadResult.SizeChanged += delegate { FitPreviewBoxes(); };
            foreach (Label label in new[] { lbl_128x128, lbl_64x64, lbl_32x32, lbl_16x16 }) { label.Margin = Padding.Empty; label.TextAlign = ContentAlignment.MiddleCenter; }
            lbl_selectedIcon.Text = "Selected icon";
            lbl_originalSize.AutoEllipsis = true;
            spc_right.BorderStyle = BorderStyle.None;
            spc_right.SplitterWidth = D(6);
            spc_right.Panel1MinSize = D(100);
            spc_right.Panel2MinSize = D(100);
            ConfigureUsage(tlp_usedEntries, lbl_usedinentries, m_lvUsedEntries);
            ConfigureUsage(tlp_usedGroups, lbl_usedInGroups, m_lvUsedGroups);
            tlp_footer_right.ColumnStyles.Clear();
            tlp_footer_right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tlp_footer_right.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(96)));
            tlp_footer_right.SetCellPosition(lbl_status, new TableLayoutPanelCellPosition(0, 0));
            tlp_footer_right.SetCellPosition(bto_OK, new TableLayoutPanelCellPosition(1, 0));
            bto_OK.Size = new Size(D(88), D(30));
            bto_OK.DialogResult = DialogResult.Cancel;
            CancelButton = bto_OK;
            btn_stopEntryAction.Enabled = btn_stopIconAction.Enabled = false;
            lbl_status.AutoSize = false;
            lbl_status.AutoEllipsis = true;
            lbl_status.TextAlign = ContentAlignment.MiddleLeft;
            lbl_status.Margin = new Padding(D(4), 0, D(8), 0);
            foreach (ListView list in DashboardLists())
            {
                list.Margin = Padding.Empty;
                list.FullRowSelect = true;
                list.GridLines = false;
                list.BorderStyle = BorderStyle.None;
                list.HideSelection = false;
                list.ShowItemToolTips = true;
                list.Scrollable = true;
                list.OwnerDraw = true;
                list.DrawColumnHeader += DrawReadableHeader;
                list.DrawItem += delegate(object sender, DrawListViewItemEventArgs e) { e.DrawDefault = true; };
                list.DrawSubItem += delegate(object sender, DrawListViewSubItemEventArgs e) { e.DrawDefault = true; };
                list.ColumnWidthChanging += KeepColumnReadable;
                list.ColumnClick += delegate { list.Invalidate(); };
                list.SizeChanged += delegate { if (!m_fittingColumns) FillSpareColumnSpace(list); };
            }
        }

        private void SetDashboardFont(Control control)
        {
            control.Font = control.Font.Bold ? m_dashboardBoldFont : m_dashboardFont;
            foreach (Control child in control.Controls) SetDashboardFont(child);
        }

        private static void PreviewColumns(TableLayoutPanel panel)
        {
            panel.ColumnStyles.Clear();
            foreach (float weight in new[] { 40f, 25f, 20f, 15f }) panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, weight));
        }

        private void ConfigureActions(TableLayoutPanel panel, FlowLayoutPanel actions, ComboBox selector, Button start, Button stop, CheckBox check)
        {
            panel.Margin = Padding.Empty;
            panel.RowStyles.Clear();
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, D(36)));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, D(42)));
            actions.Margin = Padding.Empty;
            actions.WrapContents = false;
            selector.Width = D(244);
            selector.DropDownWidth = D(280);
            selector.Margin = new Padding(0, D(4), D(8), 0);
            foreach (Button button in new[] { start, stop }) { button.Size = new Size(D(72), D(30)); button.Margin = new Padding(0, D(3), D(6), 0); }
            var selection = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, RowCount = 1 };
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(112)));
            check.AutoSize = true;
            check.Text = "Select all";
            check.Margin = new Padding(0, D(7), 0, 0);
            selection.Controls.Add(check, 0, 0);
            var fit = new Button { Text = "Fit columns", Dock = DockStyle.Fill, Margin = new Padding(0, D(3), 0, D(3)), AccessibleName = "Fit table columns to their contents" };
            fit.Click += delegate { FitAllColumns(); };
            selection.Controls.Add(fit, 1, 0);
            panel.Controls.Add(selection, 0, 1);
        }

        private void ConfigureUsage(TableLayoutPanel panel, Label label, ListView list)
        {
            panel.Margin = Padding.Empty;
            panel.ColumnStyles.Clear();
            panel.ColumnCount = 1;
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.SetColumnSpan(label, 1);
            panel.SetCellPosition(label, new TableLayoutPanelCellPosition(0, 0));
            panel.SetCellPosition(list, new TableLayoutPanelCellPosition(0, 1));
            panel.RowStyles.Clear();
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, D(30)));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            label.Margin = Padding.Empty;
            list.Margin = Padding.Empty;
        }

        private ListView[] DashboardLists() { return new[] { m_lvViewIcon, m_lvAllEntries, m_lvUsedEntries, m_lvUsedGroups, m_lvDownloadResult }; }

        private void FitAllColumns() { FitColumns(DashboardLists()); }

        private void FitColumns(params ListView[] lists)
        {
            if (m_fittingColumns) return;
            m_fittingColumns = true;
            try
            {
                foreach (ListView list in lists)
                {
                    int[] minima = new int[list.Columns.Count];
                    for (int column = 0; column < list.Columns.Count; column++)
                    {
                        int width = TextRenderer.MeasureText(list.Columns[column].Text, list.Font).Width + D(30);
                        minima[column] = Math.Max(D(column == 0 ? 76 : 64), width);
                        width = minima[column];
                        for (int row = 0; row < Math.Min(list.Items.Count, 1000); row++)
                        {
                            if (column >= list.Items[row].SubItems.Count) continue;
                            string text = list.Items[row].SubItems[column].Text;
                            if (text.Length > 2048) text = text.Substring(0, 2048);
                            width = Math.Max(width, Math.Min(D(340), TextRenderer.MeasureText(text, list.Font).Width + D(column == 0 ? 52 : 20)));
                        }
                        list.Columns[column].Width = width;
                    }
                    m_columnMinimums[list] = minima;
                    FillSpareColumnSpace(list);
                    foreach (ListViewItem row in list.Items)
                    {
                        var values = new List<string>();
                        foreach (ListViewItem.ListViewSubItem cell in row.SubItems) values.Add(cell.Text);
                        row.ToolTipText = string.Join(" · ", values.ToArray());
                    }
                }
            }
            finally { m_fittingColumns = false; }
        }

        private static void FillSpareColumnSpace(ListView list)
        {
            int used = 0;
            foreach (ColumnHeader header in list.Columns) used += header.Width;
            if (list.Columns.Count > 0 && used < list.ClientSize.Width - 2)
                list.Columns[list.Columns.Count - 1].Width += list.ClientSize.Width - used - 2;
        }

        private void KeepColumnReadable(object sender, ColumnWidthChangingEventArgs e)
        {
            int[] minima;
            if (!m_fittingColumns && m_columnMinimums.TryGetValue((ListView)sender, out minima) && e.ColumnIndex < minima.Length && e.NewWidth < minima[e.ColumnIndex])
            { e.NewWidth = minima[e.ColumnIndex]; e.Cancel = true; }
        }

        private void DrawReadableHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            var list = (ListView)sender;
            Color background = SystemInformation.HighContrast ? SystemColors.Control : list.BackColor;
            Color foreground = SystemInformation.HighContrast ? SystemColors.ControlText : list.ForeColor;
            using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
            Rectangle text = Rectangle.Inflate(e.Bounds, -D(8), 0);
            var sorter = list.ListViewItemSorter as ListViewLayoutManager;
            string caption = e.Header.Text;
            if (sorter != null && sorter.CurrentSortedColumn == e.ColumnIndex && sorter.CurrentSortOrder != SortOrder.None)
                caption += sorter.CurrentSortOrder == SortOrder.Ascending ? " ↑" : " ↓";
            TextRenderer.DrawText(e.Graphics, caption, list.Font, text, foreground, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            using (var pen = new Pen(SystemInformation.HighContrast ? SystemColors.ControlText : SystemColors.GrayText))
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        private void RestoreReadableWindow()
        {
            Rectangle working = Screen.FromControl(m_PluginHost.MainWindow ?? (Control)this).WorkingArea;
            var savedPosition = m_kpcidConfig.DashboardPosition;
            if (!savedPosition.IsNull) working = Screen.FromPoint(new Point(savedPosition.X, savedPosition.Y)).WorkingArea;
            RestoreReadableWindowForArea(working);
        }

        private void RestoreReadableWindowForArea(Rectangle working)
        {
            var savedPosition = m_kpcidConfig.DashboardPosition;
            var savedSize = m_kpcidConfig.DashboardSize;
            Size requested = savedSize.IsNull ? new Size(D(1380), D(860)) : new Size(Math.Max(savedSize.X, D(1280)), Math.Max(savedSize.Y, D(760)));
            Size size = new Size(Math.Min(requested.Width, working.Width), Math.Min(requested.Height, working.Height));
            Point position = savedPosition.IsNull ? new Point(working.Left + (working.Width - size.Width) / 2, working.Top + (working.Height - size.Height) / 2) : new Point(savedPosition.X, savedPosition.Y);
            position.X = Math.Max(working.Left, Math.Min(position.X, working.Right - size.Width));
            position.Y = Math.Max(working.Top, Math.Min(position.Y, working.Bottom - size.Height));
            MinimumSize = new Size(Math.Min(D(900), working.Width), Math.Min(D(640), working.Height));
            StartPosition = FormStartPosition.Manual;
            spc_mainSplitter.Panel1MinSize = spc_mainSplitter.Panel2MinSize = 0;
            spc_right.Panel1MinSize = spc_right.Panel2MinSize = 0;
            Bounds = new Rectangle(position, size);
            int usable = Math.Max(0, spc_mainSplitter.ClientSize.Width - spc_mainSplitter.SplitterWidth);
            spc_mainSplitter.Panel1MinSize = Math.Min(D(340), usable * 2 / 5);
            spc_mainSplitter.Panel2MinSize = Math.Min(D(360), usable * 2 / 5);
            tlp_right.RowStyles[0].Height = Math.Min(D(210), Math.Max(D(120), tlp_right.ClientSize.Height / 3));
            int maximum = spc_mainSplitter.ClientSize.Width - spc_mainSplitter.Panel2MinSize - spc_mainSplitter.SplitterWidth;
            spc_mainSplitter.SplitterDistance = Math.Max(spc_mainSplitter.Panel1MinSize, Math.Min(D(630), maximum));
            int available = spc_right.ClientSize.Height - spc_right.SplitterWidth;
            spc_right.Panel1MinSize = spc_right.Panel2MinSize = Math.Min(D(100), Math.Max(0, available / 3));
            if (available >= spc_right.Panel1MinSize + spc_right.Panel2MinSize)
                spc_right.SplitterDistance = Math.Max(spc_right.Panel1MinSize, Math.Min(available * 3 / 5, available - spc_right.Panel2MinSize));
            FitPreviewBoxes();
            WindowState = m_kpcidConfig.DashboardState == FormWindowState.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
        }
        private void FitPreviewBoxes()
        {
            TableLayoutPanel[] panels = { scltp_icons, scltp_downloadResult };
            PictureBox[][] pictures = {
                new[] { pbo_selectedIcon128, pbo_selectedIcon64, pbo_selectedIcon32, pbo_selectedIcon16 },
                new[] { pbo_downloadedIcon128, pbo_downloadedIcon64, pbo_downloadedIcon32, pbo_downloadedIcon16 }
            };
            int[] sizes = { 132, 68, 36, 20 };
            for (int p = 0; p < panels.Length; p++)
            {
                int[] widths = panels[p].GetColumnWidths();
                for (int i = 0; i < Math.Min(widths.Length, 4); i++)
                {
                    int size = Math.Max(4, Math.Min(D(sizes[i]), Math.Min(Math.Max(4, widths[i] - D(6)), Math.Max(4, panels[p].ClientSize.Height - D(4)))));
                    pictures[p][i].Size = new Size(size, size);
                    pictures[p][i].BackgroundImageLayout = ImageLayout.Zoom;
                }
            }
        }
    }
}
