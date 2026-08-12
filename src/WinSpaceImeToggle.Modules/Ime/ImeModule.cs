using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WinSpaceImeToggle.Contracts;

namespace WinSpaceImeToggle.Modules
{
    /// <summary>
    /// 输入法实现模块：全局按键捕获（WH_KEYBOARD_LL）、微软拼音中英文切换（IMM32）、
    /// 防开始菜单注入、防误触、切换策略，以及对应的“输入法工具”设置分页。
    /// 删除本 DLL 后主程序仍可运行，该分页与快捷键功能一并移除。
    /// </summary>
    public class ImeModule : IModule
    {
        internal sealed class State
        {
            public Hotkey Hotkey;
            public bool AutoSwitch;
            public uint SuppressKey;
            public double DebounceSeconds;
        }

        private State _state;
        private HotkeyHook _hook;
        private volatile bool _recordingPaused;

        public string Id { get { return "ime"; } }
        public string DisplayName { get { return Context.T("ime.name"); } }
        public ModuleContext Context { get; set; }
        public Action PrimaryAction { get { return ToggleNow; } }
        public string StartupBalloonTitle { get { return Context.T("ime.name"); } }
        public string StartupBalloonText
        {
            get { return Context.TF("ime.startupBalloon", _state.Hotkey.Display); }
        }

        public void Initialize()
        {
            _state = LoadState();
        }

        private State LoadState()
        {
            State s = new State();
            s.Hotkey = Context.GetHotkey("hotkey", Hotkey.Default);
            s.AutoSwitch = Context.GetBool("autoSwitch", true);
            s.SuppressKey = Context.GetUIntHex("suppressKey", 0x87);
            s.DebounceSeconds = Context.GetDouble("debounceSeconds", 0.3);
            return s;
        }

        public IEnumerable<SettingsPage> GetSettingsPages()
        {
            SettingsPage page = new SettingsPage();
            page.Title = Context.T("ime.pageTitle");

            HotkeyItem hk = new HotkeyItem();
            hk.Key = "hotkey";
            hk.Label = Context.T("ime.hotkeyLabel");
            hk.DefaultValue = Hotkey.Default.Display;
            hk.Note = Context.T("ime.hotkeyNote");
            page.Items.Add(hk);

            ChoiceItem strategy = new ChoiceItem();
            strategy.Key = "autoSwitch";
            strategy.Label = Context.T("ime.strategyLabel");
            strategy.Options = new ChoiceOption[] {
                new ChoiceOption { Name = Context.T("ime.strategyAuto"), Value = "auto" },
                new ChoiceOption { Name = Context.T("ime.strategyPinyinOnly"), Value = "pinyinOnly" }
            };
            strategy.DefaultValue = "auto";
            page.Items.Add(strategy);

            NumericItem debounce = new NumericItem();
            debounce.Key = "debounceSeconds";
            debounce.Label = Context.T("ime.debounceLabel");
            debounce.DefaultValue = 0.3;
            debounce.Min = 0;
            debounce.Max = 5;
            debounce.DecimalPlaces = 2;
            debounce.Increment = 0.1;
            debounce.Note = Context.T("ime.debounceNote");
            debounce.Tooltip = Context.T("ime.debounceTooltip");
            page.Items.Add(debounce);

            ChoiceItem suppress = new ChoiceItem();
            suppress.Key = "suppressKey";
            suppress.Label = Context.T("ime.suppressLabel");
            List<ChoiceOption> opts = new List<ChoiceOption>();
            for (int i = 0; i < 12; i++) opts.Add(new ChoiceOption { Name = "F" + (24 - i), Value = "F" + (24 - i) });
            opts.Add(new ChoiceOption { Name = "Ctrl", Value = "Ctrl" });
            opts.Add(new ChoiceOption { Name = "Alt", Value = "Alt" });
            opts.Add(new ChoiceOption { Name = "Shift", Value = "Shift" });
            suppress.Options = opts.ToArray();
            suppress.DefaultValue = "F24";
            suppress.Note = Context.T("ime.suppressNote");
            suppress.Tooltip = Context.T("ime.suppressTooltip");
            page.Items.Add(suppress);

            List<SettingsPage> pages = new List<SettingsPage>();
            pages.Add(page);
            return pages;
        }

        public void OnSettingsApplied()
        {
            _state = LoadState();
        }

        public void OnGlobalHotkeyCaptureChanged(bool capturing)
        {
            _recordingPaused = capturing;
        }

        public void OnAppStarted()
        {
            try
            {
                string logPath = null;
                if (Context.Args != null)
                {
                    foreach (string a in Context.Args)
                    {
                        if (a == "--hooklog")
                        {
                            logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinSpaceImeToggle", "hook.log");
                            try { File.Delete(logPath); } catch { }
                            break;
                        }
                    }
                }
                _hook = new HotkeyHook(this, logPath);
            }
            catch { }
        }

        public void OnAppExit()
        {
            if (_hook != null)
            {
                try { _hook.Dispose(); } catch { }
                _hook = null;
            }
        }

        public bool TryHandleCommandLine(string[] args)
        {
            if (args == null || args.Length == 0) return false;
            string a = args[0];
            if (a == "--toggle")
            {
                ImeApi.Toggle(_state);
                return true;
            }
            if (a == "--state")
            {
                IntPtr fg = ImeApi.GetForegroundWindow();
                uint pid = 0;
                uint tid = (fg != IntPtr.Zero) ? ImeApi.GetWindowThreadProcessId(fg, out pid) : 0;
                Console.WriteLine("pinyinCurrent=" + ImeApi.IsPinyinCurrent());
                if (fg != IntPtr.Zero)
                {
                    Console.WriteLine("fg=0x" + fg.ToInt64().ToString("X") + " tid=" + tid + " hkl=0x" + ImeApi.GetKeyboardLayout(tid).ToInt32().ToString("X8"));
                    uint conv;
                    bool ok = ImeApi.GetConversionMode(fg, out conv);
                    Console.WriteLine("getConv=" + ok + " conversion=0x" + conv.ToString("X8"));
                }
                return true;
            }
            if (a == "--list")
            {
                IntPtr[] list = new IntPtr[64];
                int n = ImeApi.GetKeyboardLayoutList(list.Length, list);
                for (int i = 0; i < n; i++)
                {
                    IntPtr hkl = list[i];
                    StringBuilder sb = new StringBuilder(256);
                    ImeApi.ImmGetDescription(hkl, sb, sb.Capacity);
                    Console.WriteLine("HKL=0x" + hkl.ToInt32().ToString("X8") + " desc=" + sb.ToString() + " isPinyin=" + ImeApi.IsPinyinHkl(hkl));
                }
                return true;
            }
            return false;
        }

        public IEnumerable<TrayItem> GetTrayItems()
        {
            List<TrayItem> items = new List<TrayItem>();
            TrayItem toggle = new TrayItem();
            toggle.Text = Context.TF("ime.trayToggle", _state.Hotkey.Display);
            toggle.OnClick = ToggleNow;
            items.Add(toggle);
            return items;
        }

        private void ToggleNow()
        {
            try
            {
                State s = _state;
                if (ImeApi.IsPinyinCurrent()) ImeApi.Toggle(s);
                else ThreadPool.QueueUserWorkItem(delegate { ImeApi.Toggle(s); });
            }
            catch { }
        }

        /// <summary>全局低级键盘钩子：匹配组合键、防误触、吞键、注入防开始菜单键。</summary>
        private sealed class HotkeyHook : IDisposable
        {
            private const int WH_KEYBOARD_LL = 13;
            private const int WM_KEYDOWN = 0x0100;
            private const int WM_KEYUP = 0x0101;
            private const int WM_SYSKEYDOWN = 0x0104;
            private const int WM_SYSKEYUP = 0x0105;
            private const uint VK_LWIN = 0x5B;
            private const uint VK_RWIN = 0x5C;
            private const uint VK_CONTROL = 0x11;
            private const uint VK_MENU = 0x12;
            private const uint VK_SHIFT = 0x10;
            private const uint VK_LCONTROL = 0xA2;
            private const uint VK_RCONTROL = 0xA3;
            private const uint VK_LSHIFT = 0xA0;
            private const uint VK_RSHIFT = 0xA1;
            private const uint VK_LMENU = 0xA4;
            private const uint VK_RMENU = 0xA5;
            private const uint LLKHF_INJECTED = 0x00000010;
            private const uint KEYEVENTF_KEYUP = 0x0002;

            private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

            [StructLayout(LayoutKind.Sequential)]
            private struct KBDLLHOOKSTRUCT
            {
                public uint vkCode;
                public uint scanCode;
                public uint flags;
                public uint time;
                public IntPtr dwExtraInfo;
            }

            [DllImport("user32.dll", SetLastError = true)]
            private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("user32.dll")]
            private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern IntPtr GetModuleHandle(string lpModuleName);

            [DllImport("user32.dll")]
            private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

            private readonly ImeModule _owner;
            private readonly string _logPath;
            private readonly LowLevelKeyboardProc _proc;
            private IntPtr _hookId;
            private bool _installed;

            private bool _winDown;
            private bool _ctrlDown;
            private bool _altDown;
            private bool _shiftDown;
            private bool _comboArmed;
            private bool _comboTriggeredOnce;
            private uint _swallowedVk;
            private DateTime _lastToggleAt = DateTime.MinValue;

            public HotkeyHook(ImeModule owner, string logPath)
            {
                _owner = owner;
                _logPath = logPath;
                _proc = HookCallback;
                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
                _installed = _hookId != IntPtr.Zero;
                if (!_installed)
                {
                    int err = Marshal.GetLastWin32Error();
                    MessageBox.Show(_owner.Context.TF("ime.hookFailText", err),
                        _owner.Context.T("ime.name"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            private void Log(string msg)
            {
                if (_logPath == null) return;
                try { File.AppendAllText(_logPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\r\n"); }
                catch { }
            }

            private int DebounceMs
            {
                get
                {
                    double s = _owner._state.DebounceSeconds;
                    if (s < 0) s = 0;
                    if (s > 10) s = 10;
                    return (int)(s * 1000);
                }
            }

            private bool AnyModifierDown()
            {
                return _winDown || _ctrlDown || _altDown || _shiftDown;
            }

            private void SuppressStartMenu(uint vk)
            {
                if (!_winDown) return;
                // 注入一个无害键（默认 F24）的按下+抬起，让系统认为 Win 被用于组合键，
                // 从而在 Win 抬起时不会弹出开始菜单（Win 抬起会正常放行，避免按键卡住）。
                keybd_event((byte)vk, 0, 0, UIntPtr.Zero);
                keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }

            private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
            {
                try
                {
                    if (nCode >= 0)
                    {
                        KBDLLHOOKSTRUCT kbd = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                        uint vk = kbd.vkCode;
                        int msg = wParam.ToInt32();
                        bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                        bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
                        bool isWin = vk == VK_LWIN || vk == VK_RWIN;
                        bool isCtrl = vk == VK_CONTROL || vk == VK_LCONTROL || vk == VK_RCONTROL;
                        bool isAlt = vk == VK_MENU || vk == VK_LMENU || vk == VK_RMENU;
                        bool isShift = vk == VK_SHIFT || vk == VK_LSHIFT || vk == VK_RSHIFT;

                        State s = _owner._state;
                        if (vk == s.SuppressKey && (kbd.flags & LLKHF_INJECTED) != 0)
                        {
                            return CallNextHookEx(_hookId, nCode, wParam, lParam);
                        }

                        if (down)
                        {
                            if (isWin) _winDown = true;
                            else if (isCtrl) _ctrlDown = true;
                            else if (isAlt) _altDown = true;
                            else if (isShift) _shiftDown = true;
                            else
                            {
                                Log("down vk=0x" + vk.ToString("X") + " win=" + _winDown + " ctrl=" + _ctrlDown + " alt=" + _altDown + " shift=" + _shiftDown
                                    + " enabled=" + _owner.Context.HotkeysEnabled + " paused=" + _owner._recordingPaused
                                    + " cfgKey=0x" + s.Hotkey.Key.ToString("X")
                                    + " match=" + (vk == s.Hotkey.Key && s.Hotkey.Matches(_winDown, _ctrlDown, _altDown, _shiftDown)));
                                if (!_owner._recordingPaused
                                    && _owner.Context.HotkeysEnabled
                                    && vk == s.Hotkey.Key
                                    && s.Hotkey.Matches(_winDown, _ctrlDown, _altDown, _shiftDown))
                                {
                                    if (!_comboTriggeredOnce)
                                    {
                                        _comboTriggeredOnce = true;
                                        bool act = s.AutoSwitch || ImeApi.IsPinyinCurrent();
                                        bool debounced = false;
                                        int dbMs = DebounceMs;
                                        if (dbMs > 0)
                                        {
                                            DateTime now = DateTime.UtcNow;
                                            debounced = (now - _lastToggleAt).TotalMilliseconds < dbMs;
                                        }
                                        Log("combo matched, act=" + act + " debounced=" + debounced);
                                        if (act)
                                        {
                                            _comboArmed = true;
                                            _swallowedVk = vk;
                                            if (s.Hotkey.Win) SuppressStartMenu(s.SuppressKey);
                                            if (!debounced)
                                            {
                                                _lastToggleAt = DateTime.UtcNow;
                                                _owner.ToggleNow();
                                            }
                                        }
                                    }
                                    if (_comboArmed) return (IntPtr)1;
                                }
                            }
                        }
                        else if (up)
                        {
                            if (isWin) _winDown = false;
                            else if (isCtrl) _ctrlDown = false;
                            else if (isAlt) _altDown = false;
                            else if (isShift) _shiftDown = false;
                            else if (_comboArmed && vk == _swallowedVk)
                            {
                                _comboArmed = false;
                                _comboTriggeredOnce = false;
                                return (IntPtr)1;
                            }
                            else if (_comboTriggeredOnce && vk == s.Hotkey.Key)
                            {
                                _comboTriggeredOnce = false;
                            }
                        }
                    }
                }
                catch { }
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            public void Dispose()
            {
                if (_installed)
                {
                    UnhookWindowsHookEx(_hookId);
                    _installed = false;
                }
            }
        }
    }

    /// <summary>IMM32 输入法 API：读取/设置微软拼音 conversion 模式并切换输入法布局。</summary>
    internal static class ImeApi
    {
        public const uint IME_CMODE_NATIVE = 0x0001;
        public const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
        private const uint WM_IME_CONTROL = 0x0283;
        private const uint IMC_GETCONVERSIONMODE = 0x0001;
        private const uint IMC_SETCONVERSIONMODE = 0x0002;

        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private const uint SMTO_BLOCK = 0x0001;
        private const uint KLF_SUBSTITUTE_OK = 0x0002;

        private static IntPtr _cachedPinyin;

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        public static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] lpList);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("imm32.dll")]
        public static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        public static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll")]
        public static extern bool ImmGetConversionStatus(IntPtr hIMC, out uint fdwConversion, out uint fdwSentence);

        [DllImport("imm32.dll")]
        public static extern bool ImmSetConversionStatus(IntPtr hIMC, uint fdwConversion, uint fdwSentence);

        [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
        public static extern int ImmGetDescription(IntPtr hKL, StringBuilder lpszDescription, int uBufLen);

        [DllImport("imm32.dll")]
        public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        public static bool IsPinyinHkl(IntPtr hkl)
        {
            if (hkl == IntPtr.Zero) return false;
            StringBuilder sb = new StringBuilder(256);
            int len = ImmGetDescription(hkl, sb, sb.Capacity);
            if (len > 0)
            {
                string d = sb.ToString();
                if (d.IndexOf("微软拼音", StringComparison.Ordinal) >= 0) return true;
                if (d.IndexOf("Microsoft Pinyin", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (d.IndexOf("Pinyin", StringComparison.OrdinalIgnoreCase) >= 0
                    && d.IndexOf("Sogou", StringComparison.OrdinalIgnoreCase) < 0
                    && d.IndexOf("QQ", StringComparison.OrdinalIgnoreCase) < 0
                    && d.IndexOf("搜狗", StringComparison.Ordinal) < 0)
                {
                    return true;
                }
            }
            int lo = hkl.ToInt32() & 0xFFFF;
            int hi = (hkl.ToInt32() >> 16) & 0xFFFF;
            // KLID 00000804（中文简体布局，承载微软拼音 TSF）与 E0200804（微软拼音）
            return lo == 0x0804 && (hi == 0x0804 || hi == 0xE020);
        }

        public static IntPtr FindPinyinHkl()
        {
            if (_cachedPinyin != IntPtr.Zero && IsPinyinHkl(_cachedPinyin)) return _cachedPinyin;

            IntPtr[] list = new IntPtr[64];
            int n = GetKeyboardLayoutList(list.Length, list);
            for (int i = 0; i < n; i++)
            {
                if (IsPinyinHkl(list[i]))
                {
                    _cachedPinyin = list[i];
                    return _cachedPinyin;
                }
            }

            string[] ids = { "00000804", "E0200804", "0804:00000804" };
            foreach (string id in ids)
            {
                IntPtr h = LoadKeyboardLayout(id, KLF_SUBSTITUTE_OK);
                if (h != IntPtr.Zero && IsPinyinHkl(h))
                {
                    _cachedPinyin = h;
                    return h;
                }
            }
            return IntPtr.Zero;
        }

        public static bool IsPinyinCurrent()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            uint pid;
            uint tid = GetWindowThreadProcessId(fg, out pid);
            return IsPinyinHkl(GetKeyboardLayout(tid));
        }

        public static void Toggle(ImeModule.State state)
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return;
            uint pid;
            uint tid = GetWindowThreadProcessId(fg, out pid);
            IntPtr pinyin = FindPinyinHkl();
            if (pinyin == IntPtr.Zero) return;
            IntPtr cur = GetKeyboardLayout(tid);
            if (cur != IntPtr.Zero && cur != pinyin)
            {
                if (state == null || !state.AutoSwitch) return;
                if (!SwitchLayout(fg, pinyin, tid)) return;
            }
            uint conv;
            if (!GetConversionMode(fg, out conv)) return;
            // 微软拼音：中文模式 = IME_CMODE_NATIVE(0x1)，英文模式 = IME_CMODE_ALPHANUMERIC(0x0)
            uint nc = (conv & IME_CMODE_NATIVE) != 0 ? 0 : IME_CMODE_NATIVE;
            SetConversionMode(fg, nc);
        }

        public static bool GetConversionMode(IntPtr fg, out uint conv)
        {
            conv = 0;
            IntPtr himc = ImmGetContext(fg);
            if (himc != IntPtr.Zero)
            {
                try
                {
                    uint sent;
                    if (ImmGetConversionStatus(himc, out conv, out sent)) return true;
                }
                finally
                {
                    ImmReleaseContext(fg, himc);
                }
            }
            IntPtr imeWnd = ImmGetDefaultIMEWnd(fg);
            if (imeWnd == IntPtr.Zero) return false;
            IntPtr r = SendMessage(imeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETCONVERSIONMODE, IntPtr.Zero);
            conv = (uint)r.ToInt64();
            return true;
        }

        private static bool SetConversionMode(IntPtr fg, uint conv)
        {
            IntPtr himc = ImmGetContext(fg);
            if (himc != IntPtr.Zero)
            {
                try
                {
                    return ImmSetConversionStatus(himc, conv, 0);
                }
                finally
                {
                    ImmReleaseContext(fg, himc);
                }
            }
            IntPtr imeWnd = ImmGetDefaultIMEWnd(fg);
            if (imeWnd == IntPtr.Zero) return false;
            SendMessage(imeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETCONVERSIONMODE, (IntPtr)conv);
            return true;
        }

        private static bool SwitchLayout(IntPtr fg, IntPtr target, uint tid)
        {
            IntPtr result;
            SendMessageTimeout(fg, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, target,
                SMTO_ABORTIFHUNG | SMTO_BLOCK, 800, out result);
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(10);
                if (GetKeyboardLayout(tid) == target) return true;
            }
            return false;
        }
    }
}
