using System;
using System.Runtime.InteropServices;
using WinSpaceImeToggle.Contracts;

namespace WinSpaceImeToggle
{
    /// <summary>
    /// 主程序拥有的“键位录制”能力：录制期间安装一个临时低级键盘钩子，
    /// 捕获用户按下的组合键（修饰键 + 普通键）并回调。与模块自身的快捷键钩子相互独立。
    /// </summary>
    internal class KeyRecorder : IDisposable
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
        private const uint VK_ESCAPE = 0x1B;
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

        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookId;
        private bool _installed;
        private Action<Hotkey> _callback;

        private bool _winDown;
        private bool _ctrlDown;
        private bool _altDown;
        private bool _shiftDown;
        private uint _swallowVk;
        private bool _swallowArmed;

        /// <summary>捕获状态变化：true=开始录制，false=结束录制（Esc 取消或 CancelCapture）。</summary>
        public event Action<bool> CapturingChanged;

        /// <summary>被用户主动取消（按 Esc）时触发。</summary>
        public event Action Cancelled;

        public bool IsCapturing { get { return _callback != null; } }

        public KeyRecorder()
        {
            _proc = HookCallback;
        }

        public void BeginCapture(Action<Hotkey> onCaptured)
        {
            CancelCapture();
            _callback = onCaptured;
            _swallowArmed = false;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            _installed = _hookId != IntPtr.Zero;
            RaiseCapturingChanged(true);
        }

        public void CancelCapture()
        {
            if (_callback == null && !_installed) return;
            _callback = null;
            Uninstall();
            RaiseCapturingChanged(false);
            if (Cancelled != null) Cancelled();
        }

        private void RaiseCapturingChanged(bool capturing)
        {
            if (CapturingChanged != null)
            {
                try { CapturingChanged(capturing); } catch { }
            }
        }

        private void Uninstall()
        {
            if (_installed)
            {
                UnhookWindowsHookEx(_hookId);
                _installed = false;
            }
            _hookId = IntPtr.Zero;
        }

        private bool AnyModifierDown()
        {
            return _winDown || _ctrlDown || _altDown || _shiftDown;
        }

        private void SuppressStartMenu()
        {
            if (!_winDown) return;
            // 录制期间注入 F24，防止 Win 抬起时弹出开始菜单（与模块的防开始菜单注入一致）。
            keybd_event((byte)0x87, 0, 0, UIntPtr.Zero);
            keybd_event((byte)0x87, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
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

                    // 忽略注入键（如模块的防开始菜单注入）
                    if ((kbd.flags & LLKHF_INJECTED) != 0)
                    {
                        return CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }

                    if (down)
                    {
                        if (isWin) _winDown = true;
                        else if (isCtrl) _ctrlDown = true;
                        else if (isAlt) _altDown = true;
                        else if (isShift) _shiftDown = true;
                        else if (_callback != null)
                        {
                            if (vk == VK_ESCAPE && !AnyModifierDown())
                            {
                                CancelCapture();
                                return (IntPtr)1;
                            }
                            if (AnyModifierDown())
                            {
                                Hotkey hk = new Hotkey();
                                hk.Win = _winDown;
                                hk.Ctrl = _ctrlDown;
                                hk.Alt = _altDown;
                                hk.Shift = _shiftDown;
                                hk.Key = vk;
                                _swallowVk = vk;
                                _swallowArmed = true;
                                if (_winDown) SuppressStartMenu();
                                Action<Hotkey> cb = _callback;
                                _callback = null;
                                Uninstall();
                                RaiseCapturingChanged(false);
                                if (cb != null) cb(hk);
                                return (IntPtr)1;
                            }
                        }
                    }
                    else if (up)
                    {
                        if (isWin) _winDown = false;
                        else if (isCtrl) _ctrlDown = false;
                        else if (isAlt) _altDown = false;
                        else if (isShift) _shiftDown = false;
                        else if (_swallowArmed && vk == _swallowVk)
                        {
                            _swallowArmed = false;
                            return (IntPtr)1;
                        }
                    }
                }
            }
            catch { }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            CancelCapture();
            _callback = null;
        }
    }
}
