using System;
using System.Collections.Generic;
using System.IO;

namespace WinSpaceImeToggle
{
    /// <summary>
    /// 界面本地化（主程序职责）：从程序目录 Languages 文件夹加载语言文件（INI 键值对）。
    /// 每个语言文件以语言代码命名（如 zh-CN.ini、en.ini），新增语言只需添加一个文件，
    /// 程序会自动识别并出现在“界面语言”下拉框中。
    /// 键未命中时回退英文（en.ini），再未命中则返回键名本身。
    /// </summary>
    internal static class Localization
    {
        /// <summary>当前语言代码（如 zh-CN / en），与 Languages 目录中的文件名对应。</summary>
        public static string Current = "en";

        /// <summary>语言文件目录（默认程序目录下的 Languages，可在测试时覆盖）。</summary>
        public static string LanguagesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages");

        private static Dictionary<string, string> _current = new Dictionary<string, string>();
        private static Dictionary<string, string> _fallback = new Dictionary<string, string>();

        /// <summary>当前是否为英文界面（用于模块名连接符等少量语言中性判断）。</summary>
        public static bool IsEnglish
        {
            get { return Current != null && Current.StartsWith("en", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>加载指定语言：写入 Current 并缓存键值表；语言文件缺失时该语言回退到英文键值。</summary>
        public static void Load(string lang)
        {
            if (string.IsNullOrEmpty(lang)) lang = "en";
            Current = lang;
            _current = ReadFile(lang);
            _fallback = ReadFile("en");
        }

        /// <summary>读取一个语言文件（INI 键值对；空行与 ; / # 注释行忽略）。</summary>
        private static Dictionary<string, string> ReadFile(string code)
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            try
            {
                string path = Path.Combine(LanguagesDir, code + ".ini");
                if (!File.Exists(path)) return map;
                string[] lines = File.ReadAllLines(path);
                foreach (string line in lines)
                {
                    string t = line.Trim();
                    if (t.Length == 0 || t[0] == ';' || t[0] == '#') continue;
                    int idx = t.IndexOf('=');
                    if (idx <= 0) continue;
                    string k = t.Substring(0, idx).Trim();
                    string v = t.Substring(idx + 1).Trim();
                    if (k.Length > 0) map[k] = v;
                }
            }
            catch { }
            return map;
        }

        /// <summary>按当前语言返回键对应的文本；未命中回退英文，仍缺失则返回键名。</summary>
        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            string v;
            if (_current.TryGetValue(key, out v)) return v;
            if (_fallback.TryGetValue(key, out v)) return v;
            return key;
        }

        /// <summary>T + string.Format：用于包含占位符（{0} 等）的文本。</summary>
        public static string TF(string key, params object[] args)
        {
            try { return string.Format(T(key), args); }
            catch { return T(key); }
        }

        /// <summary>扫描 Languages 目录得到的可用语言代码（文件名），新增语言文件后自动出现在下拉框。</summary>
        public static string[] LanguageCodes
        {
            get
            {
                string[] codes;
                string[] names;
                ScanLanguages(out codes, out names);
                return codes;
            }
        }

        /// <summary>与 LanguageCodes 一一对应的语言显示名（取各语言文件 meta.nativeName，缺失时用代码）。</summary>
        public static string[] LanguageNames
        {
            get
            {
                string[] codes;
                string[] names;
                ScanLanguages(out codes, out names);
                return names;
            }
        }

        /// <summary>扫描 Languages 目录：文件名为语言代码，显示名取文件内 meta.nativeName 键。</summary>
        private static void ScanLanguages(out string[] codes, out string[] names)
        {
            List<string> listCodes = new List<string>();
            List<string> listNames = new List<string>();
            try
            {
                if (Directory.Exists(LanguagesDir))
                {
                    string[] files = Directory.GetFiles(LanguagesDir, "*.ini");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    foreach (string f in files)
                    {
                        string code = Path.GetFileNameWithoutExtension(f);
                        if (code.Length == 0) continue;
                        listCodes.Add(code);
                        listNames.Add(ReadNativeName(code));
                    }
                }
            }
            catch { }
            if (listCodes.Count == 0)
            {
                // 兜底：语言目录缺失时至少提供中英文选项（默认英文）
                listCodes.Add("en");
                listNames.Add("English");
                listCodes.Add("zh-CN");
                listNames.Add("中文");
            }
            codes = listCodes.ToArray();
            names = listNames.ToArray();
        }

        /// <summary>读取语言文件的显示名键（meta.nativeName）。</summary>
        private static string ReadNativeName(string code)
        {
            Dictionary<string, string> map = ReadFile(code);
            string v;
            return map.TryGetValue("meta.nativeName", out v) && v.Length > 0 ? v : code;
        }
    }
}
