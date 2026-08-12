using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WinSpaceImeToggle.Contracts
{
    /// <summary>组合键描述（修饰键 + 普通键）。</summary>
    public class Hotkey
    {
        public bool Win;
        public bool Ctrl;
        public bool Alt;
        public bool Shift;
        public uint Key = 0x20;

        public static Hotkey Default
        {
            get { return new Hotkey { Win = true, Key = 0x20 }; }
        }

        public Hotkey Clone()
        {
            return new Hotkey { Win = Win, Ctrl = Ctrl, Alt = Alt, Shift = Shift, Key = Key };
        }

        public bool Matches(bool w, bool c, bool a, bool s)
        {
            return Win == w && Ctrl == c && Alt == a && Shift == s;
        }

        public string Display
        {
            get
            {
                List<string> parts = new List<string>();
                if (Win) parts.Add("Win");
                if (Ctrl) parts.Add("Ctrl");
                if (Alt) parts.Add("Alt");
                if (Shift) parts.Add("Shift");
                parts.Add(KeyNames.Display(Key));
                return string.Join(" + ", parts.ToArray());
            }
        }

        public static bool TryParse(string s, out Hotkey hk)
        {
            hk = null;
            if (string.IsNullOrWhiteSpace(s)) return false;
            string[] tokens = s.Split('+');
            Hotkey h = new Hotkey();
            foreach (string raw in tokens)
            {
                string t = raw.Trim();
                if (t == "Win") h.Win = true;
                else if (t == "Ctrl") h.Ctrl = true;
                else if (t == "Alt") h.Alt = true;
                else if (t == "Shift") h.Shift = true;
                else h.Key = KeyNames.Parse(t);
            }
            if (h.Key == 0) return false;
            if (!(h.Win || h.Ctrl || h.Alt || h.Shift)) return false;
            hk = h;
            return true;
        }
    }

    /// <summary>虚拟键码 <-> 显示名。</summary>
    public static class KeyNames
    {
        /// <summary>键名显示语言（zh-CN 中文 / en 英文），由主程序在启动与语言切换时同步。</summary>
        public static string Language = "en";

        /// <summary>语言文本解析器（由主程序注入 Localization.T）：键名文本从语言文件读取；为空时回退内置中英文。</summary>
        public static Func<string, string> Resolve;

        /// <summary>取本地化文本：优先使用语言文件（Resolve），否则按 Language 回退内置中英文。</summary>
        private static string ResolveText(string key, string zh, string en)
        {
            if (Resolve != null)
            {
                try
                {
                    string s = Resolve(key);
                    if (!string.IsNullOrEmpty(s)) return s;
                }
                catch { }
            }
            return Language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? en : zh;
        }

        public static string Display(uint vk)
        {
            switch (vk)
            {
                case 0x20: return ResolveText("key.space", "空格", "Space");
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                case 0x08: return "Backspace";
                case 0x1B: return "Esc";
                case 0x2E: return "Del";
                case 0x2D: return "Ins";
                case 0x24: return "Home";
                case 0x23: return "End";
                case 0x21: return "PgUp";
                case 0x22: return "PgDn";
                case 0x25: return "←";
                case 0x26: return "↑";
                case 0x27: return "→";
                case 0x28: return "↓";
                case 0x5B: return "Win";
                case 0x5C: return "Win";
                case 0x10: return "Shift";
                case 0x11: return "Ctrl";
                case 0x12: return "Alt";
                case 0xBD: return "-";
                case 0xBB: return "=";
                case 0xDB: return "[";
                case 0xDD: return "]";
                case 0xBA: return ";";
                case 0xDE: return "'";
                case 0xDC: return "\\";
                case 0xBC: return ",";
                case 0xBE: return ".";
                case 0xBF: return "/";
                case 0xC0: return "`";
            }
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1).ToString();
            return string.Format(ResolveText("key.unknown", "键({0})", "Key({0})"), vk);
        }

        public static uint Parse(string s)
        {
            switch (s)
            {
                case "空格": return 0x20;
                case "Space": return 0x20;
                case "Tab": return 0x09;
                case "Enter": return 0x0D;
                case "Backspace": return 0x08;
                case "Esc": return 0x1B;
                case "Del": return 0x2E;
                case "Ins": return 0x2D;
                case "Home": return 0x24;
                case "End": return 0x23;
                case "PgUp": return 0x21;
                case "PgDn": return 0x22;
                case "←": return 0x25;
                case "↑": return 0x26;
                case "→": return 0x27;
                case "↓": return 0x28;
                case "-": return 0xBD;
                case "=": return 0xBB;
                case "[": return 0xDB;
                case "]": return 0xDD;
                case ";": return 0xBA;
                case "'": return 0xDE;
                case "\\": return 0xDC;
                case ",": return 0xBC;
                case ".": return 0xBE;
                case "/": return 0xBF;
                case "`": return 0xC0;
                case "Win": return 0x5B;
            }
            if (s.Length == 1)
            {
                char c = s[0];
                if (c >= 'A' && c <= 'Z') return (uint)c;
                if (c >= '0' && c <= '9') return (uint)c;
            }
            if (s.Length > 1 && (s[0] == 'F' || s[0] == 'f'))
            {
                int n;
                if (int.TryParse(s.Substring(1), out n) && n >= 1 && n <= 24) return (uint)(0x6F + n);
            }
            return 0;
        }
    }

    /// <summary>
    /// 模块上下文：主程序为每个模块提供独立的配置存储（%APPDATA%\WinSpaceImeToggle\modules\&lt;Id&gt;.ini）、
    /// 命令行参数与全局快捷键总开关状态。模块通过它读写自己的设置。
    /// </summary>
    public sealed class ModuleContext
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();
        private readonly string _configPath;

        public string Id { get; private set; }
        public string[] Args { get; private set; }
        /// <summary>主程序的全局快捷键总开关（托盘图标灰色/彩色由此驱动）。模块在处理快捷键时应当尊重它。</summary>
        public bool HotkeysEnabled { get; set; }
        /// <summary>主程序当前 UI 语言（zh-CN / en），模块据此本地化自身文本。</summary>
        public string Language { get; set; }
        /// <summary>语言文本解析器（由主程序注入 Localization.T），模块经 T()/TF() 从语言文件读取自身文本。</summary>
        public Func<string, string> TextResolver;

        /// <summary>读取当前语言的文本（键定义在 Languages 目录的语言文件中；未命中返回键名）。</summary>
        public string T(string key)
        {
            if (TextResolver != null)
            {
                try { return TextResolver(key); } catch { }
            }
            return key;
        }

        /// <summary>T + string.Format：用于包含占位符（{0} 等）的文本。</summary>
        public string TF(string key, params object[] args)
        {
            try { return string.Format(T(key), args); }
            catch { return T(key); }
        }

        public ModuleContext(string id, string[] args)
        {
            Id = id;
            Language = "zh-CN";
            Args = args;
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinSpaceImeToggle", "modules");
            _configPath = Path.Combine(dir, id + ".ini");
            try
            {
                if (File.Exists(_configPath))
                {
                    string[] lines = File.ReadAllLines(_configPath);
                    foreach (string line in lines)
                    {
                        int idx = line.IndexOf('=');
                        if (idx <= 0) continue;
                        string k = line.Substring(0, idx).Trim();
                        string v = line.Substring(idx + 1).Trim();
                        if (k.Length > 0) _values[k] = v;
                    }
                }
            }
            catch { }
        }

        public string Get(string key, string def)
        {
            string v;
            return _values.TryGetValue(key, out v) ? v : def;
        }

        public void Set(string key, string value)
        {
            _values[key] = value;
        }

        public bool GetBool(string key, bool def)
        {
            string v = Get(key, null);
            bool b;
            return bool.TryParse(v, out b) ? b : def;
        }

        public void SetBool(string key, bool value)
        {
            Set(key, value.ToString());
        }

        public double GetDouble(string key, double def)
        {
            string v = Get(key, null);
            double d;
            return double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) ? d : def;
        }

        public void SetDouble(string key, double value)
        {
            Set(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public uint GetUIntHex(string key, uint def)
        {
            string v = Get(key, null);
            if (v == null) return def;
            string hv = v;
            if (hv.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hv = hv.Substring(2);
            uint u;
            return uint.TryParse(hv, System.Globalization.NumberStyles.HexNumber, null, out u) ? u : def;
        }

        public void SetUIntHex(string key, uint value)
        {
            Set(key, "0x" + value.ToString("X"));
        }

        public Hotkey GetHotkey(string key, Hotkey def)
        {
            string v = Get(key, null);
            Hotkey h;
            return Hotkey.TryParse(v, out h) ? h : def;
        }

        public void SetHotkey(string key, Hotkey value)
        {
            Set(key, value.Display);
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, string> kv in _values)
                {
                    sb.AppendLine(kv.Key + "=" + kv.Value);
                }
                File.WriteAllText(_configPath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>导出一行 key=value 摘要，供 --config 调试输出。</summary>
        public string Dump()
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in _values)
            {
                if (sb.Length > 0) sb.Append(" ");
                sb.Append(kv.Key).Append("=").Append(kv.Value);
            }
            return sb.Length == 0 ? "(empty)" : sb.ToString();
        }
    }

    /// <summary>托盘菜单项（模块贡献，由主程序负责渲染）。</summary>
    public sealed class TrayItem
    {
        public string Text;
        public bool Checkable;
        public bool Checked;
        public bool SeparatorBefore;
        public Action OnClick;
        public Action<bool> OnToggle;
    }

    /// <summary>设置项基类（模板）。Key 为模块配置存储键。</summary>
    public abstract class SettingItem
    {
        public string Key;
        public string Label;
        public string Note;
        public string Tooltip;
    }

    /// <summary>选项开关（复选框）。</summary>
    public sealed class ToggleItem : SettingItem
    {
        public bool DefaultValue;
    }

    /// <summary>数字输入（NumericUpDown）。</summary>
    public sealed class NumericItem : SettingItem
    {
        public double DefaultValue;
        public double Min;
        public double Max;
        public int DecimalPlaces;
        public double Increment;
    }

    /// <summary>下拉选项。</summary>
    public sealed class ChoiceOption
    {
        public string Name;
        public string Value;
        public override string ToString() { return Name; }
    }

    /// <summary>下拉页（ComboBox）。</summary>
    public sealed class ChoiceItem : SettingItem
    {
        public ChoiceOption[] Options;
        public string DefaultValue;
    }

    /// <summary>键位录制（只读文本框 + 录制按钮 + 恢复默认按钮）。</summary>
    public sealed class HotkeyItem : SettingItem
    {
        public string DefaultValue;
    }

    /// <summary>纯说明文字（灰色）。</summary>
    public sealed class NoteItem : SettingItem
    {
        public string Text;
    }

    /// <summary>一个设置分页（对应主程序设置窗口中的一个 Tab）。</summary>
    public sealed class SettingsPage
    {
        public string Title;
        public List<SettingItem> Items = new List<SettingItem>();
    }

    /// <summary>
    /// 模块契约。按此接口实现的类（放在 modules 目录的 DLL 中）会被主程序自动发现：
    /// 设置分页自动出现在设置窗口、托盘项自动出现在右键菜单、生命周期回调自动被调用。
    /// </summary>
    public interface IModule
    {
        /// <summary>模块唯一 ID（同时用于模块配置文件名）。</summary>
        string Id { get; }

        /// <summary>展示名（托盘提示、--config 输出等）。</summary>
        string DisplayName { get; }

        /// <summary>由主程序注入的模块上下文（Context 属性已赋值后才调用 Initialize）。</summary>
        ModuleContext Context { get; set; }

        /// <summary>模块初始化（读取配置、准备状态），此时不可操作 UI。</summary>
        void Initialize();

        /// <summary>声明式设置分页；主程序据此构建设置窗口的 Tab。</summary>
        IEnumerable<SettingsPage> GetSettingsPages();

        /// <summary>用户点击“保存”后回调（模块可在此刷新内部状态）。</summary>
        void OnSettingsApplied();

        /// <summary>托盘主程序启动完成、图标已显示后回调。</summary>
        void OnAppStarted();

        /// <summary>程序退出前回调（释放钩子/资源）。</summary>
        void OnAppExit();

        /// <summary>主程序正在录制新组合键时通知模块（模块应暂停自身快捷键处理）。</summary>
        void OnGlobalHotkeyCaptureChanged(bool capturing);

        /// <summary>命令行参数分发；返回 true 表示已处理（主程序随后退出，不启动托盘）。</summary>
        bool TryHandleCommandLine(string[] args);

        /// <summary>贡献到托盘右键菜单的菜单项（可返回 null/空）。</summary>
        IEnumerable<TrayItem> GetTrayItems();

        /// <summary>左键单击托盘图标时执行的动作（可返回 null）。</summary>
        Action PrimaryAction { get; }

        /// <summary>启动气泡提示标题（返回 null 则不显示）。</summary>
        string StartupBalloonTitle { get; }

        /// <summary>启动气泡提示正文（返回 null 则不显示）。</summary>
        string StartupBalloonText { get; }
    }
}
