using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using WinSpaceImeToggle.Contracts;

namespace WinSpaceImeToggle
{
    /// <summary>
    /// 模块管理器：扫描程序目录 modules 子目录中的 DLL，加载其中实现了 IModule 契约的类型。
    /// 主程序不直接引用任何具体模块类型，删除模块 DLL 即可在运行时移除对应功能与设置分页。
    /// </summary>
    internal sealed class ModuleManager
    {
        private readonly List<IModule> _modules = new List<IModule>();

        public IList<IModule> Modules { get { return _modules; } }
        public MainConfig Main { get; private set; }

        public ModuleManager(MainConfig main)
        {
            Main = main;
        }

        /// <summary>扫描程序目录 modules\ 下的 DLL，加载实现 IModule 契约的模块。</summary>
        public void LoadModules(string[] args)
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "modules");
            if (!Directory.Exists(dir)) return;
            string[] files;
            try { files = Directory.GetFiles(dir, "*.dll"); }
            catch { return; }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string dll in files)
            {
                LoadModuleFile(dll, args);
            }
        }

        /// <summary>加载单个模块 DLL：以字节流方式载入程序集，实例化 IModule 类型并初始化。</summary>
        private void LoadModuleFile(string dll, string[] args)
        {
            try
            {
                // 以字节加载，模块对契约程序集的引用会回退到主程序目录解析，保证类型统一。
                Assembly asm = Assembly.Load(File.ReadAllBytes(dll));
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                if (types == null) return;
                foreach (Type t in types)
                {
                    if (t == null || t.IsAbstract || t.IsInterface || !typeof(IModule).IsAssignableFrom(t)) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                    IModule mod = (IModule)Activator.CreateInstance(t);
                    ModuleContext ctx = new ModuleContext(mod.Id, args);
                    ctx.Language = Localization.Current;
ctx.TextResolver = Localization.T;
                    ctx.HotkeysEnabled = Main.HotkeysEnabled;
                    mod.Context = ctx;
                    mod.Initialize();
                    _modules.Add(mod);
                }
            }
            catch { }
        }

        /// <summary>把命令行参数分发给各模块；返回 true 表示已处理。</summary>
        public bool TryDispatchCommandLine(string[] args)
        {
            foreach (IModule m in _modules)
            {
                try
                {
                    if (m.TryHandleCommandLine(args)) return true;
                }
                catch { }
            }
            return false;
        }

        public string Names()
        {
            List<string> names = new List<string>();
            foreach (IModule m in _modules) names.Add(m.DisplayName);
            return string.Join(Localization.IsEnglish ? ", " : "、", names.ToArray());
        }

        public void Shutdown()
        {
            foreach (IModule m in _modules)
            {
                try { m.OnAppExit(); } catch { }
            }
        }
    }
}
