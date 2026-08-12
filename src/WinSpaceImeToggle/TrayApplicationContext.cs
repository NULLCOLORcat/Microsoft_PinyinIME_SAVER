using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using WinSpaceImeToggle.Contracts;

namespace WinSpaceImeToggle
{
    /// <summary>
    /// 托盘宿主（主程序职责）：渲染托盘图标（仅显示快捷键功能开启/关闭）、
    /// 右键菜单（含模块贡献项）、左键单击动作、设置窗口入口与开机自启动开关。
    /// 不包含任何输入法/代理的具体逻辑。
    /// </summary>
    internal class TrayApplicationContext : ApplicationContext
    {
        private readonly ModuleManager _modules;
        private readonly MainConfig _main;
        private readonly KeyRecorder _recorder = new KeyRecorder();
        private readonly Icon _iconOn;
        private readonly Icon _iconOff;
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _menu;
        private DateTime _lastClick;

        /// <summary>构造托盘上下文：创建托盘图标与右键菜单，启动各模块并显示启动气泡。</summary>
        public TrayApplicationContext(MainConfig main, ModuleManager modules)
        {
            _main = main;
            _modules = modules;

            _iconOn = AssetIcon.Get(false);
            _iconOff = AssetIcon.Get(true);
            if (_iconOn == null) _iconOn = IconFactory.Make("拼", Color.FromArgb(0, 120, 215));
            if (_iconOff == null) _iconOff = IconFactory.Make("拼", Color.FromArgb(150, 150, 150));

            _recorder.CapturingChanged += OnRecorderCapturingChanged;

            BuildMenu();
            _notifyIcon = new NotifyIcon();
            _notifyIcon.ContextMenuStrip = _menu;
            _notifyIcon.Visible = true;
            _notifyIcon.MouseClick += OnNotifyIconClick;
            _notifyIcon.MouseDoubleClick += OnNotifyIconDoubleClick;
            UpdateIconState();

            foreach (IModule module in _modules.Modules)
            {
                try { module.OnAppStarted(); } catch { }
            }

            foreach (IModule module in _modules.Modules)
            {
                if (!string.IsNullOrEmpty(module.StartupBalloonTitle) && !string.IsNullOrEmpty(module.StartupBalloonText))
                {
                    try
                    {
                        _notifyIcon.ShowBalloonTip(3000, module.StartupBalloonTitle, module.StartupBalloonText, ToolTipIcon.Info);
                    }
                    catch { }
                    break;
                }
            }
        }

        private void OnRecorderCapturingChanged(bool capturing)
        {
            foreach (IModule module in _modules.Modules)
            {
                try { module.OnGlobalHotkeyCaptureChanged(capturing); } catch { }
            }
        }

        /// <summary>托盘左键单击：触发第一个模块的主操作（如输入法中英文切换）。</summary>
        private void OnNotifyIconClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if ((DateTime.Now - _lastClick).TotalMilliseconds < SystemInformation.DoubleClickTime) return;
            _lastClick = DateTime.Now;
            foreach (IModule module in _modules.Modules)
            {
                if (module.PrimaryAction != null)
                {
                    try { module.PrimaryAction(); } catch { }
                    return;
                }
            }
        }

        private void OnNotifyIconDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) ShowSettings();
        }

        /// <summary>根据快捷键总开关刷新托盘图标与提示文本。</summary>
        private void UpdateIconState()
        {
            _notifyIcon.Icon = _main.HotkeysEnabled ? _iconOn : _iconOff;
            _notifyIcon.Text = Localization.T("tray.hotkeys") + (_main.HotkeysEnabled ? Localization.T("tray.enabled") : Localization.T("tray.disabled"));
            if (_modules.Modules.Count > 0) _notifyIcon.Text += " · " + _modules.Names();
        }

        /// <summary>构建托盘右键菜单：模块菜单项 + 设置 + 启用快捷键 + 开机自启动 + 退出。</summary>
        private void BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            Font menuFont = AssetFont.Get(9f);
            if (menuFont != null) menu.Font = menuFont;

            bool first = true;
            foreach (IModule module in _modules.Modules)
            {
                IEnumerable<TrayItem> trayItems = null;
                try { trayItems = module.GetTrayItems(); } catch { }
                if (trayItems == null) continue;
                foreach (TrayItem ti in trayItems)
                {
                    if (ti == null) continue;
                    if (ti.SeparatorBefore && !first) menu.Items.Add(new ToolStripSeparator());
                    if (ti.Checkable)
                    {
                        ToolStripMenuItem mi = new ToolStripMenuItem(ti.Text);
                        mi.CheckOnClick = true;
                        mi.Checked = ti.Checked;
                        mi.CheckedChanged += delegate { if (ti.OnToggle != null) { try { ti.OnToggle(mi.Checked); } catch { } } };
                        menu.Items.Add(mi);
                    }
                    else
                    {
                        ToolStripMenuItem mi = new ToolStripMenuItem(ti.Text);
                        mi.Click += delegate { if (ti.OnClick != null) { try { ti.OnClick(); } catch { } } };
                        menu.Items.Add(mi);
                    }
                    first = false;
                }
            }

            if (!first) menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miSettings = new ToolStripMenuItem(Localization.T("tray.settings"));
            miSettings.Click += delegate { ShowSettings(); };
            menu.Items.Add(miSettings);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miEnable = new ToolStripMenuItem(Localization.T("tray.enableHotkeys"));
            miEnable.Checked = _main.HotkeysEnabled;
            miEnable.CheckOnClick = true;
            miEnable.CheckedChanged += delegate
            {
                _main.HotkeysEnabled = miEnable.Checked;
                _main.Save();
                SyncModulesEnabledState();
                UpdateIconState();
            };

            ToolStripMenuItem miAutoStart = new ToolStripMenuItem(Localization.T("tray.autoStart"));
            miAutoStart.Checked = _main.AutoStart;
            miAutoStart.CheckOnClick = true;
            miAutoStart.CheckedChanged += delegate
            {
                _main.AutoStart = miAutoStart.Checked;
                MainConfig.SetAutoStart(_main.AutoStart);
                _main.Save();
            };

            menu.Items.Add(miEnable);
            menu.Items.Add(miAutoStart);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miExit = new ToolStripMenuItem(Localization.T("tray.exit"));
            miExit.Click += delegate { Application.Exit(); };
            menu.Items.Add(miExit);

            _menu = menu;
        }

        private void SyncModulesEnabledState()
        {
            foreach (IModule m in _modules.Modules)
            {
                try { m.Context.HotkeysEnabled = _main.HotkeysEnabled; } catch { }
            }
        }

        /// <summary>以模态方式打开设置窗口，确定后保存配置并刷新菜单与图标。</summary>
        private void ShowSettings()
        {
            using (SettingsForm form = new SettingsForm(_modules, _main, _recorder))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    _main.Save();
                    SyncModulesEnabledState();
                    BuildMenu();
                    _notifyIcon.ContextMenuStrip = _menu;
                    UpdateIconState();
                }
            }
        }

        /// <summary>退出托盘线程：释放托盘图标、快捷键录制器并通知各模块退出。</summary>
        protected override void ExitThreadCore()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
            if (_recorder != null)
            {
                try { _recorder.CancelCapture(); } catch { }
                _recorder.Dispose();
            }
            if (_modules != null) _modules.Shutdown();
            try { if (_iconOn != null) _iconOn.Dispose(); } catch { }
            try { if (_iconOff != null) _iconOff.Dispose(); } catch { }
            base.ExitThreadCore();
        }
    }
}
