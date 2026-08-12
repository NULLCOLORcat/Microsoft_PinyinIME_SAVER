using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinSpaceImeToggle
{
    /// <summary>界面字体：优先加载 assets 目录中的 HarmonyOS Sans SC Medium（高 DPI 更清晰）。</summary>
    internal static class AssetFont
    {
        private static System.Drawing.Text.PrivateFontCollection _pfc;
        private static Font _font;

        public static Font Get(float size)
        {
            if (_font != null) return _font;
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "HarmonyOS_Sans_SC_Medium.ttf");
                if (File.Exists(path))
                {
                    _pfc = new System.Drawing.Text.PrivateFontCollection();
                    _pfc.AddFontFile(path);
                    if (_pfc.Families.Length > 0)
                    {
                        _font = new Font(_pfc.Families[0], size, FontStyle.Regular, GraphicsUnit.Point);
                    }
                }
            }
            catch { }
            return _font;
        }
    }

    /// <summary>托盘/应用图标：assets/icon.ico；gray=true 输出灰度版（表示快捷键功能关闭）。</summary>
    internal static class AssetIcon
    {
        private static string IconPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "icon.ico"); }
        }

        private static Icon _original;
        private static Icon _gray;

        public static Icon Get(bool gray)
        {
            try
            {
                if (!File.Exists(IconPath)) return null;
                if (gray)
                {
                    if (_gray == null) _gray = MakeGray(new Icon(IconPath, 32, 32));
                    return _gray;
                }
                if (_original == null) _original = new Icon(IconPath, 32, 32);
                return _original;
            }
            catch { return null; }
        }

        private static Icon MakeGray(Icon src)
        {
            using (Bitmap bmp = src.ToBitmap())
            {
                Bitmap g = new Bitmap(bmp.Width, bmp.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (Graphics gr = Graphics.FromImage(g))
                {
                    System.Drawing.Imaging.ColorMatrix cm = new System.Drawing.Imaging.ColorMatrix(new float[][] {
                        new float[] { 0.33f, 0.33f, 0.33f, 0f, 0f },
                        new float[] { 0.33f, 0.33f, 0.33f, 0f, 0f },
                        new float[] { 0.33f, 0.33f, 0.33f, 0f, 0f },
                        new float[] { 0f, 0f, 0f, 1f, 0f },
                        new float[] { 0f, 0f, 0f, 0f, 1f }
                    });
                    using (System.Drawing.Imaging.ImageAttributes ia = new System.Drawing.Imaging.ImageAttributes())
                    {
                        ia.SetColorMatrix(cm);
                        gr.DrawImage(bmp, new Rectangle(0, 0, g.Width, g.Height), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
                    }
                }
                IntPtr h = g.GetHicon();
                Icon ic = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                g.Dispose();
                return ic;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }

    /// <summary>图标缺失时的回退：绘制“拼”字图标。</summary>
    internal static class IconFactory
    {
        public static Icon Make(string text, Color bg)
        {
            Bitmap bmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (GraphicsPath path = RoundedRect(1, 1, 14, 14, 3))
                using (SolidBrush brush = new SolidBrush(bg))
                {
                    g.FillPath(brush, path);
                }
                using (Font font = MakeFont())
                using (SolidBrush brush = new SolidBrush(Color.White))
                {
                    SizeF sz = g.MeasureString(text, font);
                    g.DrawString(text, font, brush, (16 - sz.Width) / 2f, (16 - sz.Height) / 2f + 0.5f);
                }
            }
            IntPtr hicon = bmp.GetHicon();
            bmp.Dispose();
            return Icon.FromHandle(hicon);
        }

        private static Font MakeFont()
        {
            try { return new Font("Microsoft YaHei", 9f, FontStyle.Bold, GraphicsUnit.Point); }
            catch { return new Font(FontFamily.GenericSansSerif, 9f, FontStyle.Bold, GraphicsUnit.Point); }
        }

        private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddArc(x, y, r * 2, r * 2, 180, 90);
            p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
