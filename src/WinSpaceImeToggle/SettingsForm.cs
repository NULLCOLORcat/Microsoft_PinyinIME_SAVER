using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using WinSpaceImeToggle.Contracts;

namespace WinSpaceImeToggle
{
    /// <summary>
    /// 通用设置窗口（主程序职责）：按“主程序选项”分页 + 各模块声明的模板分页动态构建。
    /// 保存/取消固定在底部，不受分页影响。
    /// 模板类型：ToggleItem（选项开关）、NumericItem（数字输入）、ChoiceItem（下拉页）、
    /// HotkeyItem（键位录制）、NoteItem（说明文字）。
    /// </summary>
    internal class SettingsForm : Form
    {
        private sealed class ItemBinding
        {
            public IModule Module;
            public ModuleContext Context;
            public SettingItem Item;
            public Func<string> GetValue;
        }

        private readonly ModuleManager _modules;
        private readonly MainConfig _main;
        private readonly KeyRecorder _recorder;
        private readonly List<ItemBinding> _bindings = new List<ItemBinding>();
        private readonly CheckBox _autoStartChk = new CheckBox();
        private readonly CheckBox _hotkeysEnabledChk = new CheckBox();
        private readonly ToolTip _tooltip = new ToolTip();
        private readonly ComboBox _langCb = new ComboBox();
        private HotkeyItem _capturingItem;
        private TextBox _capturingBox;
        private string _captureOriginalText;

        /// <summary>构造设置窗口：按模板构建主程序分页与各模块分页。</summary>
        public SettingsForm(ModuleManager modules, MainConfig main, KeyRecorder recorder)
        {
            _modules = modules;
            _main = main;
            _recorder = recorder;
            _recorder.Cancelled += OnRecorderCancelled;
            _recorder.CapturingChanged += OnRecorderCapturingChanged;

            Text = Localization.T("app.title");
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            MinimumSize = new Size(720, 480);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            ClientSize = new Size(780, 520);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font uiFont = AssetFont.Get(9f);
            if (uiFont != null) Font = uiFont;
            else { try { Font = new Font("Microsoft YaHei UI", 9f); } catch { } }
            try { Icon = AssetIcon.Get(false); } catch { }

            TabControl tabs = new TabControl();
            tabs.Location = new Point(10, 10);
            tabs.Size = new Size(ClientSize.Width - 20, ClientSize.Height - 78);
            tabs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            tabs.TabPages.Add(BuildMainPage());
            foreach (IModule module in _modules.Modules)
            {
                IEnumerable<SettingsPage> pages = null;
                try { pages = module.GetSettingsPages(); } catch { }
                if (pages == null) continue;
                foreach (SettingsPage page in pages)
                {
                    if (page == null || string.IsNullOrEmpty(page.Title)) continue;
                    try
                    {
                        tabs.TabPages.Add(BuildModulePage(module, page));
                    }
                    catch { }
                }
            }

            Button btnOk = new Button();
            btnOk.Text = Localization.T("app.save");
            btnOk.Width = 140;
            btnOk.Height = 38;
            btnOk.DialogResult = DialogResult.OK;
            btnOk.Click += delegate { Apply(); };

            Button btnCancel = new Button();
            btnCancel.Text = Localization.T("app.cancel");
            btnCancel.Width = 140;
            btnCancel.Height = 38;
            btnCancel.DialogResult = DialogResult.Cancel;

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            TableLayoutPanel btnPanel = new TableLayoutPanel();
            btnPanel.ColumnCount = 3;
            btnPanel.RowCount = 1;
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnPanel.Location = new Point(10, ClientSize.Height - 62);
            btnPanel.Size = new Size(ClientSize.Width - 20, 50);
            btnPanel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            btnCancel.Margin = new Padding(0, 6, 8, 6);
            btnOk.Margin = new Padding(0, 6, 0, 6);
            btnPanel.Controls.Add(new Label(), 0, 0);
            btnPanel.Controls.Add(btnCancel, 1, 0);
            btnPanel.Controls.Add(btnOk, 2, 0);

            Controls.Add(tabs);
            Controls.Add(btnPanel);
        }

        private TabPage BuildMainPage()
        {
            TabPage tp = new TabPage(Localization.T("main.pageTitle"));
            TableLayoutPanel tbl = new TableLayoutPanel();
            tbl.Dock = DockStyle.Fill;
            tbl.Padding = new Padding(18, 16, 18, 16);
            tbl.ColumnCount = 1;
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _autoStartChk.Text = Localization.T("main.autoStart");
            _autoStartChk.AutoSize = true;
            _autoStartChk.Checked = _main.AutoStart;
            _autoStartChk.Margin = new Padding(0, 0, 0, 14);

            _hotkeysEnabledChk.Text = Localization.T("main.hotkeysEnabled");
            _hotkeysEnabledChk.AutoSize = true;
            _hotkeysEnabledChk.Checked = _main.HotkeysEnabled;
            _hotkeysEnabledChk.Margin = new Padding(0, 0, 0, 14);

            FlowLayoutPanel langPanel = new FlowLayoutPanel();
            langPanel.AutoSize = true;
            langPanel.Margin = new Padding(0, 0, 0, 14);
            Label lblLang = new Label();
            lblLang.AutoSize = true;
            lblLang.Text = Localization.T("main.languageLabel");
            lblLang.Margin = new Padding(0, 4, 8, 0);
            _langCb.DropDownStyle = ComboBoxStyle.DropDownList;
            _langCb.Width = 150;
            for (int i = 0; i < Localization.LanguageCodes.Length; i++)
            {
                _langCb.Items.Add(new ChoiceOption { Name = Localization.LanguageNames[i], Value = Localization.LanguageCodes[i] });
            }
            int langIdx = 0;
            for (int i = 0; i < Localization.LanguageCodes.Length; i++)
            {
                if (string.Equals(_main.Language, Localization.LanguageCodes[i], StringComparison.OrdinalIgnoreCase)) { langIdx = i; break; }
            }
            _langCb.SelectedIndex = langIdx;
            langPanel.Controls.Add(lblLang);
            langPanel.Controls.Add(_langCb);
            Label lblNote = new Label();
            lblNote.AutoSize = true;
            lblNote.ForeColor = Color.Gray;
            string loadedNames = _modules.Modules.Count == 0 ? Localization.T("main.none") : _modules.Names();
            string loadedLine = Localization.TF("main.loadedModules", loadedNames);
            lblNote.Text = loadedLine + Environment.NewLine + Localization.T("main.moduleNote");

            tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tbl.Controls.Add(_autoStartChk, 0, 0);
            tbl.Controls.Add(_hotkeysEnabledChk, 0, 1);
            tbl.Controls.Add(langPanel, 0, 2);
            tbl.Controls.Add(lblNote, 0, 3);
            tp.Controls.Add(tbl);
            return tp;
        }

        private TabPage BuildModulePage(IModule module, SettingsPage page)
        {
            TabPage tp = new TabPage(page.Title);
            TableLayoutPanel tbl = new TableLayoutPanel();
            tbl.Dock = DockStyle.Fill;
            tbl.Padding = new Padding(18, 16, 18, 16);
            tbl.ColumnCount = 4;
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            int row = 0;
            foreach (SettingItem item in page.Items)
            {
                if (item == null) continue;

                NoteItem noteOnly = item as NoteItem;
                if (noteOnly != null)
                {
                    Label note = new Label();
                    note.Text = noteOnly.Text;
                    note.AutoSize = true;
                    note.ForeColor = Color.Gray;
                    note.Margin = new Padding(0, 2, 0, 8);
                    tbl.Controls.Add(note, 0, row);
                    tbl.SetColumnSpan(note, 4);
                    row++;
                    continue;
                }

                ToggleItem toggle = item as ToggleItem;
                if (toggle != null)
                {
                    CheckBox chk = new CheckBox();
                    chk.Text = toggle.Label;
                    chk.AutoSize = true;
                    chk.Margin = new Padding(0, 4, 0, 4);
                    chk.Checked = module.Context.GetBool(toggle.Key, toggle.DefaultValue);
                    if (!string.IsNullOrEmpty(toggle.Tooltip)) _tooltip.SetToolTip(chk, toggle.Tooltip);
                    tbl.Controls.Add(chk, 0, row);
                    tbl.SetColumnSpan(chk, 4);
                    _bindings.Add(new ItemBinding
                    {
                        Module = module,
                        Context = module.Context,
                        Item = item,
                        GetValue = delegate { return chk.Checked.ToString(); }
                    });
                    row++;
                }
                else if (item is HotkeyItem)
                {
                    HotkeyItem hk = (HotkeyItem)item;
                    Label lbl = new Label();
                    lbl.Text = hk.Label;
                    lbl.AutoSize = true;
                    lbl.Margin = new Padding(0, 9, 0, 0);

                    TextBox box = new TextBox();
                    box.ReadOnly = true;
                    box.Width = 280;
                    box.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                    box.Margin = new Padding(8, 4, 8, 4);
                    box.Text = module.Context.Get(hk.Key, hk.DefaultValue);
                    if (!string.IsNullOrEmpty(hk.Tooltip)) _tooltip.SetToolTip(box, hk.Tooltip);

                    Button btnRecord = new Button();
                    btnRecord.Text = Localization.T("app.record");
                    btnRecord.Width = 150;
                    btnRecord.Height = 38;
                    btnRecord.Click += delegate { BeginCapture(module, hk, box); };

                    Button btnReset = new Button();
                    btnReset.Text = Localization.T("app.restoreDefault");
                    btnReset.Width = 150;
                    btnReset.Height = 38;
                    btnReset.Click += delegate { if (_capturingItem == hk) CancelCapture(); box.Text = hk.DefaultValue; };

                    tbl.Controls.Add(lbl, 0, row);
                    tbl.Controls.Add(box, 1, row);
                    tbl.Controls.Add(btnRecord, 2, row);
                    tbl.Controls.Add(btnReset, 3, row);
                    _bindings.Add(new ItemBinding
                    {
                        Module = module,
                        Context = module.Context,
                        Item = item,
                        GetValue = delegate { return box.Text; }
                    });
                    row++;
                }
                else if (item is NumericItem)
                {
                    NumericItem ni = (NumericItem)item;
                    Label lbl = new Label();
                    lbl.Text = ni.Label;
                    lbl.AutoSize = true;
                    lbl.Margin = new Padding(0, 9, 0, 0);

                    NumericUpDown nud = new NumericUpDown();
                    nud.Minimum = (decimal)ni.Min;
                    nud.Maximum = (decimal)ni.Max;
                    nud.DecimalPlaces = ni.DecimalPlaces;
                    nud.Increment = (decimal)ni.Increment;
                    nud.Width = 110;
                    nud.Margin = new Padding(8, 4, 8, 4);
                    double d = module.Context.GetDouble(ni.Key, ni.DefaultValue);
                    decimal dv = (decimal)d;
                    if (dv < nud.Minimum) dv = nud.Minimum;
                    if (dv > nud.Maximum) dv = nud.Maximum;
                    nud.Value = dv;
                    if (!string.IsNullOrEmpty(ni.Tooltip)) _tooltip.SetToolTip(nud, ni.Tooltip);

                    tbl.Controls.Add(lbl, 0, row);
                    tbl.Controls.Add(nud, 1, row);
                    _bindings.Add(new ItemBinding
                    {
                        Module = module,
                        Context = module.Context,
                        Item = item,
                        GetValue = delegate { return nud.Value.ToString(CultureInfo.InvariantCulture); }
                    });
                    row++;
                }
                else if (item is ChoiceItem)
                {
                    ChoiceItem ci = (ChoiceItem)item;
                    Label lbl = new Label();
                    lbl.Text = ci.Label;
                    lbl.AutoSize = true;
                    lbl.Margin = new Padding(0, 9, 0, 0);

                    ComboBox cb = new ComboBox();
                    cb.DropDownStyle = ComboBoxStyle.DropDownList;
                    cb.Width = 340;
                    cb.Margin = new Padding(8, 4, 8, 4);
                    if (ci.Options != null)
                    {
                        foreach (ChoiceOption o in ci.Options) cb.Items.Add(o);
                        int dropWidth = 0;
                        using (Graphics g = CreateGraphics())
                        {
                            foreach (ChoiceOption o in ci.Options)
                            {
                                int w = (int)Math.Ceiling(g.MeasureString(o.Name, Font).Width) + 32;
                                if (w > dropWidth) dropWidth = w;
                            }
                        }
                        if (dropWidth > cb.Width) cb.DropDownWidth = dropWidth;
                        string cur = module.Context.Get(ci.Key, ci.DefaultValue);
                        int idx = -1;
                        for (int i = 0; i < cb.Items.Count; i++)
                        {
                            if (((ChoiceOption)cb.Items[i]).Value == cur) { idx = i; break; }
                        }
                        cb.SelectedIndex = idx >= 0 ? idx : 0;
                    }
                    if (!string.IsNullOrEmpty(ci.Tooltip)) _tooltip.SetToolTip(cb, ci.Tooltip);

                    tbl.Controls.Add(lbl, 0, row);
                    tbl.Controls.Add(cb, 1, row);
                    _bindings.Add(new ItemBinding
                    {
                        Module = module,
                        Context = module.Context,
                        Item = item,
                        GetValue = delegate { return ((ChoiceOption)cb.SelectedItem).Value; }
                    });
                    row++;
                }

                if (!string.IsNullOrEmpty(item.Note))
                {
                    Label note = new Label();
                    note.Text = item.Note;
                    note.AutoSize = true;
                    note.ForeColor = Color.Gray;
                    note.Margin = new Padding(0, 0, 0, 10);
                    tbl.Controls.Add(note, 0, row);
                    tbl.SetColumnSpan(note, 4);
                    row++;
                }
            }
            tbl.RowCount = row;
            tp.Controls.Add(tbl);
            return tp;
        }

        private void BeginCapture(IModule module, HotkeyItem item, TextBox box)
        {
            if (_capturingItem != null) CancelCapture();
            _capturingItem = item;
            _capturingBox = box;
            _captureOriginalText = box.Text;
            box.Text = Localization.T("app.capturePrompt");
            _recorder.BeginCapture(delegate(Hotkey hk)
            {
                if (box.IsDisposed) return;
                _capturingItem = null;
                _capturingBox = null;
                box.Text = hk.Display;
            });
        }

        private void CancelCapture()
        {
            _recorder.CancelCapture();
            OnRecorderCancelled();
        }

        private void OnRecorderCancelled()
        {
            if (_capturingBox == null) return;
            try
            {
                if (!_capturingBox.IsDisposed) _capturingBox.Text = _captureOriginalText;
            }
            catch { }
            _capturingBox = null;
            _capturingItem = null;
        }

        private void OnRecorderCapturingChanged(bool capturing)
        {
            foreach (IModule module in _modules.Modules)
            {
                try { module.OnGlobalHotkeyCaptureChanged(capturing); } catch { }
            }
        }

        /// <summary>保存全部设置：写回主程序配置并通知各模块 OnSettingsApplied。</summary>
        private void Apply()
        {
            if (_capturingItem != null) CancelCapture();

            // 语言切换：写回主配置、更新当前语言并同步到各模块，托盘菜单等立即按新语言刷新
            if (_langCb.SelectedItem != null)
            {
                ChoiceOption langOpt = (ChoiceOption)_langCb.SelectedItem;
                _main.Language = langOpt.Value;
                Localization.Load(_main.Language);
                KeyNames.Language = Localization.Current;
                foreach (IModule m in _modules.Modules)
                {
                    if (m.Context != null) m.Context.Language = Localization.Current;
                }
            }

            HashSet<IModule> touched = new HashSet<IModule>();
            foreach (ItemBinding b in _bindings)
            {
                if (b.Context == null || b.Item == null || string.IsNullOrEmpty(b.Item.Key)) continue;
                try { b.Context.Set(b.Item.Key, b.GetValue()); } catch { }
                if (b.Module != null) touched.Add(b.Module);
            }
            foreach (IModule module in touched)
            {
                try { module.Context.Save(); } catch { }
                try { module.OnSettingsApplied(); } catch { }
            }

            _main.HotkeysEnabled = _hotkeysEnabledChk.Checked;
            _main.AutoStart = _autoStartChk.Checked;
            _main.Save();
            MainConfig.SetAutoStart(_main.AutoStart);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_capturingItem != null) CancelCapture();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _recorder.Cancelled -= OnRecorderCancelled;
            _recorder.CapturingChanged -= OnRecorderCapturingChanged;
            _tooltip.Dispose();
            base.OnFormClosed(e);
        }
    }
}


