// 线稿生成器 LineArtTool
// 10 种风格: XDoG线稿/铅笔素描/钢笔线稿/白描/蓝图晒图/粉笔黑板/漫画网点/木刻版画/铅笔排线/淡彩上色稿
// 附加: 墨色选择、透明底输出(垫色上色用)
// 运行环境: .NET Framework 4.x (Windows 自带), 无其他依赖
// 用法:
//   双击运行            图形界面
//   拖图片到 exe        按已存参数生成 <原名>_线稿.png
//   LineArtTool.exe -i 输入 -o 输出 [--style 0-9] [--ink 0-3] [--alpha] [--invert] [--nosp]
//                    [--sigma 1.0] [--k 1.6] [--p 20] [--eps 0.02] [--phi 25]
//                    [--shadow 0.85] [--thr 0.42]
//   LineArtTool.exe -i 输入 --video 输出.avi [--fps 24] [--vsize 1080] [--vspeed 1.0]
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LineArtTool
{
    public class PParams
    {
        public int Style = 0;           // 0 XDoG线稿 1 铅笔素描 2 钢笔线稿 3 白描 4 蓝图晒图
                                        // 5 粉笔黑板 6 漫画网点 7 木刻版画 8 铅笔排线 9 淡彩上色稿
        public float Sigma = 1.0f;
        public float K = 1.6f;
        public float P = 20f;
        public float Eps = 0.02f;
        public float Phi = 25f;
        public float Shadow = 0.85f;
        public float ShadowThr = 0.42f;
        public bool Despeckle = true;
        public bool Invert = false;     // 黑底白线 (仅线稿类风格)
        public int Ink = 0;             // 0 灰黑 1 钢笔蓝 2 复古棕 3 淡紫
        public bool Transparent = false; // 透明底输出 (仅线稿类风格)

        public PParams Clone()
        {
            PParams c = new PParams();
            c.Style = Style; c.Sigma = Sigma; c.K = K; c.P = P; c.Eps = Eps;
            c.Phi = Phi; c.Shadow = Shadow; c.ShadowThr = ShadowThr;
            c.Despeckle = Despeckle; c.Invert = Invert;
            c.Ink = Ink; c.Transparent = Transparent;
            return c;
        }
    }

    public static class Core
    {
        // 原图数据: 亮度 L(0..1, 1=白), 颜色(仅淡彩需要)
        public class SrcData
        {
            public int W, H;
            public float[] L;
            public float[] Cr, Cg, Cb;
        }

        public static Color InkColorOf(int k)
        {
            if (k == 1) return Color.FromArgb(0x1F, 0x3A, 0x93);   // 钢笔蓝
            if (k == 2) return Color.FromArgb(0x5B, 0x42, 0x32);   // 复古棕
            if (k == 3) return Color.FromArgb(0x6B, 0x4E, 0x8E);   // 淡紫
            return Color.FromArgb(0x22, 0x22, 0x22);               // 灰黑
        }

        public static SrcData Extract(Bitmap src, bool needColor)
        {
            SrcData s = new SrcData();
            s.W = src.Width; s.H = src.Height;
            int w = s.W, h = s.H;
            Rectangle rect = new Rectangle(0, 0, w, h);
            BitmapData bd = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int stride = bd.Stride;
            byte[] px = new byte[stride * h];
            Marshal.Copy(bd.Scan0, px, 0, stride * h);
            src.UnlockBits(bd);

            s.L = new float[w * h];
            if (needColor) { s.Cr = new float[w * h]; s.Cg = new float[w * h]; s.Cb = new float[w * h]; }
            for (int y = 0; y < h; y++)
            {
                int row = y * stride, idx = y * w;
                for (int x = 0; x < w; x++)
                {
                    int o = row + x * 4;
                    float r = px[o + 2], g = px[o + 1], b = px[o];
                    float al = px[o + 3] / 255f;
                    r = r * al + 255f * (1f - al);
                    g = g * al + 255f * (1f - al);
                    b = b * al + 255f * (1f - al);
                    s.L[idx + x] = (0.299f * r + 0.587f * g + 0.114f * b) / 255f;
                    if (needColor) { s.Cr[idx + x] = r / 255f; s.Cg[idx + x] = g / 255f; s.Cb[idx + x] = b / 255f; }
                }
            }
            return s;
        }

        // 线条暗度图 d: 0=无线 1=全黑线
        public static float[] LineMap(SrcData s, PParams p)
        {
            int w = s.W, h = s.H, n = w * h;
            float[] d = new float[n];

            if (p.Style == 7)
            {
                // 木刻版画: 大块填黑(阈值用 ShadowThr) + 轮廓线
                float[] lb = GaussBlur(s.L, w, h, 1.5f);
                float thr = p.ShadowThr;
                for (int i = 0; i < n; i++)
                {
                    float fill = SmoothStep(thr + 0.06f, thr - 0.06f, lb[i]);
                    d[i] = fill;
                }
                float[] e = XDoGMap(s, p);
                for (int i = 0; i < n; i++)
                {
                    float v = e[i] * 1.25f;
                    if (v > d[i]) d[i] = v;
                    if (d[i] > 1f) d[i] = 1f;
                }
                if (p.Despeckle) CleanBinary(d, w, h, 12);
                return d;
            }

            if (p.Style == 1 || p.Style == 8)
            {
                // 铅笔(反色-模糊-减淡)
                float[] inv = new float[n];
                for (int i = 0; i < n; i++) inv[i] = 1f - s.L[i];
                float[] b = GaussBlur(inv, w, h, Math.Max(0.6f, p.Sigma * 2.5f));
                for (int i = 0; i < n; i++)
                {
                    float denom = Math.Max(0.02f, 1f - b[i]);
                    float dd = 1.05f * s.L[i] / denom - 0.03f;
                    dd = Clamp01(1f - Clamp01(dd));
                    d[i] = dd;
                }
            }
            else
            {
                // XDoG 系
                float[] v = XDoGMap(s, p);
                for (int i = 0; i < n; i++) d[i] = Clamp01(1f - v[i]);
            }

            int spk = 6;
            if (p.Style == 3) spk = 14;
            if (p.Style == 4 || p.Style == 5) spk = 8;
            if (p.Despeckle && p.Style != 9) DespeckleDark(d, w, h, spk);
            return d;
        }

        // XDoG 白度图 (1=白)
        static float[] XDoGMap(SrcData s, PParams p)
        {
            int w = s.W, h = s.H, n = w * h;
            float[] g1 = GaussBlur(s.L, w, h, p.Sigma);
            float[] g2 = GaussBlur(s.L, w, h, p.Sigma * p.K);
            float a = 1f + p.P;
            float[] v = new float[n];
            for (int i = 0; i < n; i++)
            {
                float dd = a * g1[i] - p.P * g2[i];
                float o;
                if (dd >= p.Eps) o = 1f;
                else o = 1f + (float)Math.Tanh(p.Phi * (dd - p.Eps));
                v[i] = Clamp01(o);
            }
            // 暗部调子
            if (p.Shadow > 0.001f && p.Style != 2 && p.Style != 3)
            {
                float[] lb = GaussBlur(s.L, w, h, 2.5f);
                float t1 = p.ShadowThr, t0 = p.ShadowThr * 0.35f;
                for (int i = 0; i < n; i++)
                {
                    float t = SmoothStep(t0, t1, lb[i]);
                    float tone = 1f - p.Shadow * (1f - t);
                    if (v[i] > tone) v[i] = tone;
                }
            }
            if (p.Despeckle) DespeckleWhite(v, w, h, 6);
            return v;
        }

        public static Bitmap Process(Bitmap src, PParams p)
        {
            SrcData s = Extract(src, p.Style == 9);
            float[] d = LineMap(s, p);
            return Compose(s, d, p);
        }

        // 按风格合成最终图
        public static Bitmap Compose(SrcData s, float[] d, PParams p)
        {
            int w = s.W, h = s.H, n = w * h;
            bool alphaOut = p.Transparent && (p.Style == 0 || p.Style == 2 || p.Style == 3 || p.Style == 6);
            Bitmap bmp = new Bitmap(w, h, alphaOut ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb);
            Rectangle rect = new Rectangle(0, 0, w, h);
            BitmapData bd = bmp.LockBits(rect, ImageLockMode.WriteOnly,
                alphaOut ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb);
            int stride = bd.Stride;
            byte[] px = new byte[stride * h];

            Color inkC = InkColorOf(p.Ink);
            float ir = inkC.R / 255f, ig = inkC.G / 255f, ib = inkC.B / 255f;
            bool isInvert = p.Invert && !alphaOut &&
                (p.Style == 0 || p.Style == 2 || p.Style == 3 || p.Style == 6);

            float[] dots = null;
            if (p.Style == 6) dots = HalftoneDots(s, 2.0f);

            float[] wr = null, wg = null, wb = null;
            if (p.Style == 9) WatercolorWash(s, out wr, out wg, out wb);

            Random rnd = new Random(12345);
            float[] noise = new float[n];
            for (int i = 0; i < n; i++) noise[i] = (float)rnd.NextDouble();

            for (int y = 0; y < h; y++)
            {
                int row = y * stride, idx = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = idx + x;
                    float dd = d[i];
                    float r, g, b;

                    switch (p.Style)
                    {
                        case 4: // 蓝图晒图: 蓝底白线
                            {
                                float nz = 0.94f + 0.06f * noise[i];
                                float br = 0.15f * nz, bg = 0.30f * nz, bb = 0.62f * nz;
                                r = br + (0.97f - br) * dd;
                                g = bg + (0.97f - bg) * dd;
                                b = bb + (0.99f - bb) * dd;
                            }
                            break;
                        case 5: // 粉笔黑板
                            {
                                float nz = 0.75f + 0.25f * noise[i];
                                float br = 0.10f, bg = 0.13f, bb = 0.11f;
                                float cr = 0.93f * nz, cg = 0.95f * nz, cb = 0.93f * nz;
                                r = br + (cr - br) * dd;
                                g = bg + (cg - bg) * dd;
                                b = bb + (cb - bb) * dd;
                            }
                            break;
                        case 6: // 漫画网点
                            {
                                float v = dd;
                                if (dots != null && dots[i] > v) v = dots[i];
                                if (isInvert) { r = g = b = v; }
                                else { r = 1f + (ir - 1f) * v; g = 1f + (ig - 1f) * v; b = 1f + (ib - 1f) * v; }
                            }
                            break;
                        case 7: // 木刻版画
                            {
                                if (dd > 0.5f)
                                {
                                    float e = (dd - 0.5f) / 0.06f;
                                    if (e > 1f) e = 1f;
                                    r = 0.08f + (ir - 0.08f) * e;
                                    g = 0.08f + (ig - 0.08f) * e;
                                    b = 0.08f + (ib - 0.08f) * e;
                                }
                                else { r = g = b = 0.97f; }
                            }
                            break;
                        case 8: // 铅笔排线
                            {
                                if (isInvert) { r = g = b = dd; }
                                else { r = 1f + (ir - 1f) * dd; g = 1f + (ig - 1f) * dd; b = 1f + (ib - 1f) * dd; }
                            }
                            break;
                        case 9: // 淡彩上色稿
                            {
                                r = wr[i] + (ir - wr[i]) * dd;
                                g = wg[i] + (ig - wg[i]) * dd;
                                b = wb[i] + (ib - wb[i]) * dd;
                            }
                            break;
                        default: // 0/2/3 线稿
                            {
                                if (isInvert) { r = g = b = dd; }
                                else { r = 1f + (ir - 1f) * dd; g = 1f + (ig - 1f) * dd; b = 1f + (ib - 1f) * dd; }
                            }
                            break;
                    }

                    int o = row + x * (alphaOut ? 4 : 3);
                    if (alphaOut)
                    {
                        float a = dd;
                        if (a > 1f) a = 1f;
                        px[o] = (byte)(ir * 255f + 0.5f);
                        px[o + 1] = (byte)(ig * 255f + 0.5f);
                        px[o + 2] = (byte)(ib * 255f + 0.5f);
                        px[o + 3] = (byte)(a * 255f + 0.5f);
                    }
                    else
                    {
                        px[o] = (byte)(Clamp01(b) * 255f + 0.5f);
                        px[o + 1] = (byte)(Clamp01(g) * 255f + 0.5f);
                        px[o + 2] = (byte)(Clamp01(r) * 255f + 0.5f);
                    }
                }
            }
            Marshal.Copy(px, 0, bd.Scan0, px.Length);
            bmp.UnlockBits(bd);
            return bmp;
        }

        // 漫画网点: 亮度→Bayer 有序抖动点
        static float[] HalftoneDots(SrcData s, float sigma)
        {
            int w = s.W, h = s.H, n = w * h;
            float[] lb = GaussBlur(s.L, w, h, sigma);
            int cell = Math.Max(2, Math.Max(w, h) / 150);
            float[] res = new float[n];
            int[] bay = Bayer8();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float tone = lb[i];
                    if (tone > 0.965f) { res[i] = 0f; continue; }
                    float mask = SmoothStep(0.99f, 0.93f, tone);
                    int bx = (x / cell) & 7, by = (y / cell) & 7;
                    float th = bay[by * 8 + bx] / 64f;
                    float dot = th > tone ? 1f : 0f;
                    res[i] = dot * mask * 0.92f;
                }
            }
            return res;
        }

        static int[] Bayer8()
        {
            int[] b = new int[64];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    int v = 0;
                    for (int bit = 0; bit < 3; bit++)
                    {
                        int xb = (x >> (2 - bit)) & 1;
                        int yb = (y >> (2 - bit)) & 1;
                        v = (v << 2) | ((xb ^ yb) << 1) | yb;
                    }
                    b[y * 8 + x] = v;
                }
            return b;
        }

        // 淡彩: 色块量化 + 模糊 + 提白
        static void WatercolorWash(SrcData s, out float[] wr, out float[] wg, out float[] wb)
        {
            int w = s.W, h = s.H, n = w * h;
            const float Q = 4f;
            float[] qr = new float[n], qg = new float[n], qb = new float[n];
            for (int i = 0; i < n; i++)
            {
                qr[i] = (float)Math.Round(s.Cr[i] * (Q - 1f)) / (Q - 1f);
                qg[i] = (float)Math.Round(s.Cg[i] * (Q - 1f)) / (Q - 1f);
                qb[i] = (float)Math.Round(s.Cb[i] * (Q - 1f)) / (Q - 1f);
            }
            wr = GaussBlur(qr, w, h, 2.5f);
            wg = GaussBlur(qg, w, h, 2.5f);
            wb = GaussBlur(qb, w, h, 2.5f);
            Random rnd = new Random(777);
            for (int i = 0; i < n; i++)
            {
                float nz = (float)rnd.NextDouble() - 0.5f;
                float lift = 0.22f + nz * 0.05f;
                wr[i] = wr[i] + (1f - wr[i]) * lift;
                wg[i] = wg[i] + (1f - wg[i]) * lift;
                wb[i] = wb[i] + (1f - wb[i]) * lift;
            }
        }

        // ==================== 基础工具 ====================

        public static float[] GaussBlur(float[] src, int w, int h, float sigma)
        {
            if (sigma < 0.35f) return (float[])src.Clone();
            int r = (int)Math.Ceiling(sigma * 3f);
            if (r > 300) r = 300;
            int len = 2 * r + 1;
            float[] kern = new float[len];
            float s2 = 2f * sigma * sigma;
            float sum = 0f;
            for (int i = 0; i < len; i++)
            {
                float x = i - r;
                kern[i] = (float)Math.Exp(-(x * x) / s2);
                sum += kern[i];
            }
            for (int i = 0; i < len; i++) kern[i] /= sum;

            float[] tmp = new float[src.Length];
            float[] dst = new float[src.Length];
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    float acc = 0f;
                    for (int k = -r; k <= r; k++)
                    {
                        int xx = x + k;
                        if (xx < 0) xx = 0; else if (xx >= w) xx = w - 1;
                        acc += src[row + xx] * kern[k + r];
                    }
                    tmp[row + x] = acc;
                }
            }
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    float acc = 0f;
                    for (int k = -r; k <= r; k++)
                    {
                        int yy = y + k;
                        if (yy < 0) yy = 0; else if (yy >= h) yy = h - 1;
                        acc += tmp[yy * w + x] * kern[k + r];
                    }
                    dst[row + x] = acc;
                }
            }
            return dst;
        }

        static float SmoothStep(float a, float b, float x)
        {
            if (b <= a) return (x < a) ? 0f : 1f;
            float t = (x - a) / (b - a);
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            return t * t * (3f - 2f * t);
        }

        static float Clamp01(float x)
        {
            if (x < 0f) return 0f;
            if (x > 1f) return 1f;
            return x;
        }

        // 漂白小暗斑 (在白度图 v 上: v<0.9 视为暗)
        static void DespeckleWhite(float[] v, int w, int h, int minSize)
        {
            int n = w * h;
            bool[] mask = new bool[n];
            for (int i = 0; i < n; i++) mask[i] = v[i] < 0.90f;
            BleachComponents(mask, v, w, h, minSize);
        }

        // 清除二值图小暗块 (在暗度图 d 上)
        static void DespeckleDark(float[] d, int w, int h, int minSize)
        {
            int n = w * h;
            bool[] mask = new bool[n];
            for (int i = 0; i < n; i++) mask[i] = d[i] > 0.1f;
            BleachComponents(mask, d, w, h, minSize, true);
        }

        static void CleanBinary(float[] d, int w, int h, int minSize)
        {
            int n = w * h;
            bool[] mask = new bool[n];
            for (int i = 0; i < n; i++) mask[i] = d[i] > 0.5f;
            BleachComponents(mask, d, w, h, minSize, true);
        }

        static void BleachComponents(bool[] mask, float[] vals, int w, int h, int minSize, bool zeroOut = false)
        {
            int n = w * h;
            bool[] visited = new bool[n];
            int[] stack = new int[n];
            for (int i = 0; i < n; i++)
            {
                if (!mask[i] || visited[i]) continue;
                int sp = 0;
                stack[sp++] = i;
                visited[i] = true;
                int head = 0;
                while (head < sp)
                {
                    int cur = stack[head++];
                    int cx = cur % w, cy = cur / w;
                    if (cx > 0 && mask[cur - 1] && !visited[cur - 1]) { visited[cur - 1] = true; stack[sp++] = cur - 1; }
                    if (cx < w - 1 && mask[cur + 1] && !visited[cur + 1]) { visited[cur + 1] = true; stack[sp++] = cur + 1; }
                    if (cy > 0 && mask[cur - w] && !visited[cur - w]) { visited[cur - w] = true; stack[sp++] = cur - w; }
                    if (cy < h - 1 && mask[cur + w] && !visited[cur + w]) { visited[cur + w] = true; stack[sp++] = cur + w; }
                }
                if (sp < minSize)
                {
                    for (int q = 0; q < sp; q++)
                        vals[stack[q]] = zeroOut ? 0f : 1f;
                }
            }
        }

        public static Bitmap LoadImage(string path)
        {
            using (Bitmap raw = new Bitmap(path))
            {
                if (raw.PixelFormat == PixelFormat.Format32bppArgb)
                    return new Bitmap(path);
                return raw.Clone(new Rectangle(0, 0, raw.Width, raw.Height),
                    PixelFormat.Format32bppArgb);
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                return;
            }
            RunCli(args);
        }

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int dwProcessId);

        static void RunCli(string[] args)
        {
            PParams p = Settings.Load();
            string inPath = null, outPath = null;
            bool silent = false;
            try
            {
                int i = 0;
                while (i < args.Length)
                {
                    string a = args[i].ToLowerInvariant();
                    if (a == "-i" || a == "--input") { inPath = args[++i]; silent = true; }
                    else if (a == "-o" || a == "--output") { outPath = args[++i]; silent = true; }
                    else if (a == "--style" || a == "--mode") p.Style = (int)ParseF(args[++i]);
                    else if (a == "--ink") p.Ink = (int)ParseF(args[++i]);
                    else if (a == "--alpha") p.Transparent = true;
                    else if (a == "--sigma") p.Sigma = ParseF(args[++i]);
                    else if (a == "--k") p.K = ParseF(args[++i]);
                    else if (a == "--p") p.P = ParseF(args[++i]);
                    else if (a == "--eps") p.Eps = ParseF(args[++i]);
                    else if (a == "--phi") p.Phi = ParseF(args[++i]);
                    else if (a == "--shadow") p.Shadow = ParseF(args[++i]);
                    else if (a == "--thr") p.ShadowThr = ParseF(args[++i]);
                    else if (a == "--invert") p.Invert = true;
                    else if (a == "--nosp") p.Despeckle = false;
                    else if (a.StartsWith("-") || a.StartsWith("/")) { }
                    else if (inPath == null) inPath = args[i];
                    i++;
                }
                if (inPath == null) throw new Exception("未指定输入图片。");

                if (outPath == null)
                    {
                        string dir = Path.GetDirectoryName(Path.GetFullPath(inPath));
                        string name = Path.GetFileNameWithoutExtension(inPath);
                        outPath = Path.Combine(dir, name + "_线稿.png");
                    }
                    using (Bitmap src = Core.LoadImage(inPath))
                    using (Bitmap result = Core.Process(src, p))
                    {
                        SaveBitmap(result, outPath);
                    }
                    if (!silent)
                    {
                        MessageBox.Show("已生成线稿:\r\n" + outPath, "线稿生成器",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        AttachConsole(-1);
                        Console.WriteLine("OK " + outPath);
                    }
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                if (silent)
                {
                    AttachConsole(-1);
                    Console.WriteLine("ERROR " + ex.Message);
                }
                MessageBox.Show("处理失败:\r\n" + ex.Message, "线稿生成器",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.ExitCode = 1;
            }
        }

        static float ParseF(string s)
        {
            return float.Parse(s, CultureInfo.InvariantCulture);
        }

        public static void SaveBitmap(Bitmap bmp, string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".jpg" || ext == ".jpeg")
                bmp.Save(path, ImageFormat.Jpeg);
            else if (ext == ".bmp")
                bmp.Save(path, ImageFormat.Bmp);
            else
                bmp.Save(path, ImageFormat.Png);
        }
    }

    // 参数持久化: %APPDATA%\LineArtTool\settings.txt (12 字段)
    public static class Settings
    {
        static string FilePath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LineArtTool");
            return Path.Combine(dir, "settings.txt");
        }

        public static PParams Load()
        {
            PParams p = new PParams();
            try
            {
                string f = FilePath();
                if (!File.Exists(f)) return p;
                string[] parts = File.ReadAllText(f).Trim().Split(',');
                if (parts.Length >= 10)
                {
                    p.Style = int.Parse(parts[0]);
                    if (p.Style < 0 || p.Style > 9) p.Style = 0;
                    p.Sigma = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    p.K = float.Parse(parts[2], CultureInfo.InvariantCulture);
                    p.P = float.Parse(parts[3], CultureInfo.InvariantCulture);
                    p.Eps = float.Parse(parts[4], CultureInfo.InvariantCulture);
                    p.Phi = float.Parse(parts[5], CultureInfo.InvariantCulture);
                    p.Shadow = float.Parse(parts[6], CultureInfo.InvariantCulture);
                    p.ShadowThr = float.Parse(parts[7], CultureInfo.InvariantCulture);
                    p.Despeckle = parts[8].Trim() == "1";
                    p.Invert = parts[9].Trim() == "1";
                }
                if (parts.Length >= 12)
                {
                    p.Ink = int.Parse(parts[10]);
                    if (p.Ink < 0 || p.Ink > 3) p.Ink = 0;
                    p.Transparent = parts[11].Trim() == "1";
                }
            }
            catch { }
            return p;
        }

        public static void Save(PParams p)
        {
            try
            {
                string f = FilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f, string.Format(CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}",
                    p.Style, p.Sigma, p.K, p.P, p.Eps, p.Phi,
                    p.Shadow, p.ShadowThr, p.Despeckle ? 1 : 0, p.Invert ? 1 : 0,
                    p.Ink, p.Transparent ? 1 : 0));
            }
            catch { }
        }
    }

    public class MainForm : Form
    {
        PParams prms;
        Bitmap original;
        Bitmap previewSrc;
        PictureBox pic;
        Label status;
        ComboBox styleBox, inkBox;
        TrackBar tbSigma, tbK, tbP, tbEps, tbPhi, tbShadow, tbThr;
        Label vSigma, vK, vP, vEps, vPhi, vShadow, vThr;
        CheckBox cbSpk, cbInv, cbAlpha;
        Button btnSave;
        System.Windows.Forms.Timer debounce;
        object busyLock = new object();
        bool workerBusy = false, rerunNeeded = false;
        int previewBudget = 1400;

        static readonly string[] StyleNames = new string[] {
            "XDoG 线稿", "铅笔素描", "钢笔线稿", "白描", "蓝图晒图",
            "粉笔黑板", "漫画网点", "木刻版画", "铅笔排线", "淡彩上色稿" };
        static readonly string[] InkNames = new string[] {
            "灰黑", "钢笔蓝", "复古棕", "淡紫" };

        public MainForm()
        {
            prms = Settings.Load();
            InitUi();
            this.Closed += delegate { Settings.Save(prms); };
        }

        void InitUi()
        {
            this.Text = "线稿生成器 LineArtTool";
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }
            this.ClientSize = new Size(1080, 700);
            this.MinimumSize = new Size(880, 560);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.AllowDrop = true;
            this.DragEnter += delegate(object s, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            this.DragDrop += delegate(object s, DragEventArgs e)
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0) OpenFile(files[0]);
            };

            Panel left = new Panel();
            left.Dock = DockStyle.Left;
            left.Width = 296;
            left.AutoScroll = true;
            left.Padding = new Padding(10, 10, 4, 10);

            int y = 4;
            Label title = new Label();
            title.Text = "线稿生成器";
            title.Font = new Font("微软雅黑", 14, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(10, y);
            left.Controls.Add(title);
            y += 34;

            Button open = new Button();
            open.Text = "打开图片…";
            open.Width = 270;
            open.Height = 32;
            open.Location = new Point(10, y);
            open.Click += delegate { DoOpen(); };
            left.Controls.Add(open);
            y += 42;

            Label lm = new Label();
            lm.Text = "风格:";
            lm.AutoSize = true;
            lm.Location = new Point(10, y + 6);
            left.Controls.Add(lm);
            styleBox = new ComboBox();
            styleBox.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (string sn in StyleNames) styleBox.Items.Add(sn);
            styleBox.SelectedIndex = Math.Max(0, Math.Min(StyleNames.Length - 1, prms.Style));
            styleBox.Location = new Point(62, y + 2);
            styleBox.Width = 218;
            styleBox.SelectedIndexChanged += delegate
            {
                prms.Style = styleBox.SelectedIndex;
                ApplyPreset(prms.Style);
                SyncFromParams();
                UpdateStyleEnables();
                Schedule();
            };
            left.Controls.Add(styleBox);
            y += 36;

            Label li = new Label();
            li.Text = "墨色:";
            li.AutoSize = true;
            li.Location = new Point(10, y + 6);
            left.Controls.Add(li);
            inkBox = new ComboBox();
            inkBox.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (string iN in InkNames) inkBox.Items.Add(iN);
            inkBox.SelectedIndex = Math.Max(0, Math.Min(InkNames.Length - 1, prms.Ink));
            inkBox.Location = new Point(62, y + 2);
            inkBox.Width = 218;
            inkBox.SelectedIndexChanged += delegate { prms.Ink = inkBox.SelectedIndex; Schedule(); };
            left.Controls.Add(inkBox);
            y += 36;

            tbSigma = AddSlider(left, ref y, "细节 (线条精细度)", 3, 60, (int)(prms.Sigma * 20), 20, "0.00", out vSigma, delegate { prms.Sigma = tbSigma.Value / 20f; });
            tbK = AddSlider(left, ref y, "线宽", 12, 26, (int)(prms.K * 10), 10, "0.0", out vK, delegate { prms.K = tbK.Value / 10f; });
            tbP = AddSlider(left, ref y, "锐度", 5, 60, (int)prms.P, 1, "0", out vP, delegate { prms.P = tbP.Value; });
            tbEps = AddSlider(left, ref y, "阈值 (越大线越多)", -20, 30, (int)(prms.Eps * 100), 100, "0.00", out vEps, delegate { prms.Eps = tbEps.Value / 100f; });
            tbPhi = AddSlider(left, ref y, "硬度 (小=柔和 大=干脆)", 1, 80, (int)prms.Phi, 1, "0", out vPhi, delegate { prms.Phi = tbPhi.Value; });
            tbShadow = AddSlider(left, ref y, "暗部调子 (保留瞳孔/阴影)", 0, 100, (int)(prms.Shadow * 100), 100, "0%", out vShadow, delegate { prms.Shadow = tbShadow.Value / 100f; });
            tbThr = AddSlider(left, ref y, "暗部阈值", 5, 60, (int)(prms.ShadowThr * 100), 100, "0%", out vThr, delegate { prms.ShadowThr = tbThr.Value / 100f; });
            y += 4;

            cbSpk = new CheckBox();
            cbSpk.Text = "去杂点";
            cbSpk.AutoSize = true;
            cbSpk.Location = new Point(10, y);
            cbSpk.Checked = prms.Despeckle;
            cbSpk.CheckedChanged += delegate { prms.Despeckle = cbSpk.Checked; Schedule(); };
            left.Controls.Add(cbSpk);

            cbInv = new CheckBox();
            cbInv.Text = "反相 (黑底白线)";
            cbInv.AutoSize = true;
            cbInv.Location = new Point(120, y);
            cbInv.Checked = prms.Invert;
            cbInv.CheckedChanged += delegate { prms.Invert = cbInv.Checked; Schedule(); };
            left.Controls.Add(cbInv);
            y += Math.Max(cbSpk.Height, cbInv.Height) + 6;

            cbAlpha = new CheckBox();
            cbAlpha.Text = "透明底 (线稿垫色用, PNG)";
            cbAlpha.AutoSize = true;
            cbAlpha.Location = new Point(10, y);
            cbAlpha.Checked = prms.Transparent;
            cbAlpha.CheckedChanged += delegate { prms.Transparent = cbAlpha.Checked; Schedule(); };
            left.Controls.Add(cbAlpha);
            y += Math.Max(cbAlpha.Height, 20) + 6;

            LinkLabel reset = new LinkLabel();
            reset.Text = "恢复默认参数";
            reset.AutoSize = true;
            reset.Location = new Point(10, y);
            reset.Click += delegate
            {
                PParams def = new PParams();
                prms.Sigma = def.Sigma; prms.K = def.K; prms.P = def.P; prms.Eps = def.Eps;
                prms.Phi = def.Phi; prms.Shadow = def.Shadow; prms.ShadowThr = def.ShadowThr;
                prms.Despeckle = def.Despeckle; prms.Invert = def.Invert;
                SyncFromParams();
                Schedule();
            };
            left.Controls.Add(reset);
            y += 26;

            btnSave = new Button();
            btnSave.Text = "保存图片… (原图分辨率)";
            btnSave.Width = 270;
            btnSave.Height = 36;
            btnSave.Location = new Point(10, y);
            btnSave.Enabled = false;
            btnSave.Click += delegate { DoSave(); };
            left.Controls.Add(btnSave);
            y += 44;

            Label tip = new Label();
            tip.Text = "提示: 可把图片直接拖进本窗口。\r\n风格下拉含 10 种预设, 选后可微调。";
            tip.ForeColor = Color.DimGray;
            tip.AutoSize = true;
            tip.Location = new Point(10, y);
            left.Controls.Add(tip);

            Controls.Add(left);

            pic = new PictureBox();
            pic.Dock = DockStyle.Fill;
            pic.BackColor = Color.White;
            pic.SizeMode = PictureBoxSizeMode.Zoom;
            pic.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(pic);
            pic.BringToFront();

            status = new Label();
            status.Dock = DockStyle.Bottom;
            status.Height = 26;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.Padding = new Padding(6, 0, 0, 0);
            status.Text = "请打开一张图片开始。";
            status.BackColor = Color.FromArgb(240, 240, 240);
            Controls.Add(status);
            status.BringToFront();

            debounce = new System.Windows.Forms.Timer();
            debounce.Interval = 140;
            debounce.Tick += delegate
            {
                debounce.Stop();
                StartWorker(false);
            };

            UpdateStyleEnables();
        }

        const int PanelRight = 264;

        TrackBar AddSlider(Panel parent, ref int y, string name, int min, int max, int val,
            int divisor, string fmt, out Label valLabel, Action onChanged)
        {
            Label lbl = new Label();
            lbl.Text = name;
            lbl.AutoSize = true;
            lbl.Location = new Point(10, y);
            parent.Controls.Add(lbl);

            Label vl = new Label();
            vl.Text = FormatVal(val, divisor, fmt);
            vl.AutoSize = true;
            vl.ForeColor = Color.DimGray;
            parent.Controls.Add(vl);
            vl.Location = new Point(PanelRight - vl.PreferredWidth, y);
            y += Math.Max(lbl.PreferredHeight, vl.PreferredHeight) + 1;

            TrackBar tb = new TrackBar();
            tb.Minimum = min;
            tb.Maximum = max;
            tb.Value = Math.Max(min, Math.Min(max, val));
            tb.TickStyle = TickStyle.None;
            tb.SmallChange = 1;
            tb.LargeChange = Math.Max(1, (max - min) / 12);
            tb.Width = 256;
            tb.Height = 30;
            tb.Location = new Point(10, y);
            tb.ValueChanged += delegate
            {
                vl.Text = FormatVal(tb.Value, divisor, fmt);
                vl.Left = PanelRight - vl.PreferredWidth;
                onChanged();
                Schedule();
            };
            parent.Controls.Add(tb);
            y += Math.Max(30, tb.Height) + 2;
            valLabel = vl;
            return tb;
        }

        string FormatVal(int v, int divisor, string fmt)
        {
            float f = v / (float)divisor;
            return f.ToString(fmt, CultureInfo.InvariantCulture);
        }

        // 各风格可调参数矩阵: sigma k p eps phi shadow thr
        static readonly bool[][] StyleEnables = new bool[][] {
            new bool[] { true, true, true, true, true, true, true },   // 0 XDoG
            new bool[] { true, false, false, false, false, false, false }, // 1 铅笔
            new bool[] { true, true, true, true, true, false, false }, // 2 钢笔
            new bool[] { true, true, true, true, true, false, false }, // 3 白描
            new bool[] { true, true, true, true, true, true, true },   // 4 蓝图
            new bool[] { true, true, true, true, true, true, true },   // 5 粉笔
            new bool[] { true, true, true, true, true, true, true },   // 6 网点
            new bool[] { true, false, false, false, false, false, true }, // 7 木刻
            new bool[] { true, false, false, false, false, false, false }, // 8 排线
            new bool[] { true, true, true, true, true, false, false }, // 9 淡彩
        };
        // 墨色可用: 线稿类
        static readonly bool[] InkEnabled = new bool[] {
            true, false, true, true, false, false, true, true, true, false };
        static readonly bool[] AlphaEnabled = new bool[] {
            true, false, true, true, false, false, true, false, false, false };

        void UpdateStyleEnables()
        {
            bool[] en = StyleEnables[Math.Max(0, Math.Min(9, prms.Style))];
            tbSigma.Enabled = en[0]; tbK.Enabled = en[1]; tbP.Enabled = en[2];
            tbEps.Enabled = en[3]; tbPhi.Enabled = en[4]; tbShadow.Enabled = en[5]; tbThr.Enabled = en[6];
            vSigma.Enabled = en[0]; vK.Enabled = en[1]; vP.Enabled = en[2];
            vEps.Enabled = en[3]; vPhi.Enabled = en[4]; vShadow.Enabled = en[5]; vThr.Enabled = en[6];
            inkBox.Enabled = InkEnabled[Math.Max(0, Math.Min(9, prms.Style))];
            cbAlpha.Enabled = AlphaEnabled[Math.Max(0, Math.Min(9, prms.Style))];
        }

        // 风格预设值
        void ApplyPreset(int style)
        {
            switch (style)
            {
                case 2: prms.Sigma = 1.0f; prms.K = 1.7f; prms.P = 25f; prms.Eps = 0.015f; prms.Phi = 45f; prms.Shadow = 0f; break;
                case 3: prms.Sigma = 1.1f; prms.K = 1.7f; prms.P = 25f; prms.Eps = 0.06f; prms.Phi = 35f; prms.Shadow = 0f; break;
                case 4: prms.Sigma = 1.0f; prms.K = 1.6f; prms.P = 20f; prms.Eps = 0.02f; prms.Phi = 25f; prms.Shadow = 0.35f; prms.ShadowThr = 0.40f; break;
                case 5: prms.Sigma = 1.0f; prms.K = 1.6f; prms.P = 20f; prms.Eps = 0.02f; prms.Phi = 30f; prms.Shadow = 0.45f; prms.ShadowThr = 0.40f; break;
                case 7: prms.Sigma = 1.5f; prms.ShadowThr = 0.50f; break;
                default: break; // 0/1/6/8/9 沿用当前参数
            }
        }

        void SyncFromParams()
        {
            if (styleBox.SelectedIndex != prms.Style) styleBox.SelectedIndex = prms.Style;
            if (inkBox.SelectedIndex != prms.Ink) inkBox.SelectedIndex = prms.Ink;
            tbSigma.Value = ClampTb(tbSigma, (int)(prms.Sigma * 20));
            tbK.Value = ClampTb(tbK, (int)(prms.K * 10));
            tbP.Value = ClampTb(tbP, (int)prms.P);
            tbEps.Value = ClampTb(tbEps, (int)(prms.Eps * 100));
            tbPhi.Value = ClampTb(tbPhi, (int)prms.Phi);
            tbShadow.Value = ClampTb(tbShadow, (int)(prms.Shadow * 100));
            tbThr.Value = ClampTb(tbThr, (int)(prms.ShadowThr * 100));
            cbSpk.Checked = prms.Despeckle;
            cbInv.Checked = prms.Invert;
            cbAlpha.Checked = prms.Transparent;
            UpdateStyleEnables();
        }

        static int ClampTb(TrackBar tb, int v)
        {
            if (v < tb.Minimum) return tb.Minimum;
            if (v > tb.Maximum) return tb.Maximum;
            return v;
        }

        void DoOpen()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "打开图片";
                dlg.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    OpenFile(dlg.FileName);
            }
        }

        void OpenFile(string path)
        {
            try
            {
                Bitmap loaded = Core.LoadImage(path);
                if (loaded == null) return;
                if (original != null) original.Dispose();
                original = loaded;

                long pixels = (long)original.Width * original.Height;
                if (pixels > 40000000L)
                {
                    float sc = (float)Math.Sqrt(40000000f / pixels);
                    int nw = Math.Max(1, (int)(original.Width * sc));
                    int nh = Math.Max(1, (int)(original.Height * sc));
                    Bitmap scaled = new Bitmap(original, nw, nh);
                    original.Dispose();
                    original = scaled;
                    MessageBox.Show("图片过大, 已自动缩到 " + nw + "x" + nh + " 处理。", "线稿生成器");
                }

                if (previewSrc != null) previewSrc.Dispose();
                int ow = original.Width, oh = original.Height;
                int longSide = Math.Max(ow, oh);
                if (longSide > previewBudget)
                {
                    float sc = previewBudget / (float)longSide;
                    previewSrc = new Bitmap(original, Math.Max(1, (int)(ow * sc)), Math.Max(1, (int)(oh * sc)));
                }
                else previewSrc = new Bitmap(original);

                btnSave.Enabled = true;
                status.Text = string.Format("原图 {0}x{1}    预览 {2}x{3}    处理中…",
                    ow, oh, previewSrc.Width, previewSrc.Height);
                StartWorker(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开失败: " + ex.Message, "线稿生成器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void Schedule()
        {
            if (original == null) return;
            debounce.Stop();
            debounce.Start();
        }

        void StartWorker(bool fresh)
        {
            if (previewSrc == null) return;
            lock (busyLock)
            {
                if (workerBusy) { rerunNeeded = true; return; }
                workerBusy = true;
            }
            if (fresh) status.Text = "处理中…";
            else status.Text = ReplaceTail(status.Text, "处理中…");

            PParams snapshot = prms.Clone();
            Bitmap srcCopy = previewSrc;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Bitmap result = null;
                try { result = Core.Process(srcCopy, snapshot); }
                catch { result = null; }
                Bitmap finalResult = result;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (finalResult != null)
                        {
                            if (pic.Image != null) pic.Image.Dispose();
                            pic.Image = finalResult;
                            status.Text = string.Format("预览 {0}x{1}    保存时按原图分辨率输出",
                                finalResult.Width, finalResult.Height);
                        }
                        else status.Text = "处理失败。";
                        lock (busyLock)
                        {
                            workerBusy = false;
                            bool again = rerunNeeded;
                            rerunNeeded = false;
                            if (again) StartWorker(false);
                        }
                    });
                }
                catch { lock (busyLock) { workerBusy = false; } }
            });
        }

        static string ReplaceTail(string s, string tail)
        {
            int i = s.LastIndexOf("    ");
            if (i > 0) return s.Substring(0, i) + "    " + tail;
            return tail;
        }

        void DoSave()
        {
            if (original == null) return;
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "保存线稿";
                dlg.Filter = "PNG 图像|*.png|JPEG 图像|*.jpg|位图|*.bmp";
                dlg.AddExtension = true;
                dlg.FileName = "线稿.png";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string path = dlg.FileName;
                btnSave.Enabled = false;
                status.Text = "正在按原图分辨率全尺寸处理…";
                UseWaitCursor = true;
                PParams snapshot = prms.Clone();
                Bitmap srcCopy = original;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string msg;
                    try
                    {
                        using (Bitmap result = Core.Process(srcCopy, snapshot))
                        {
                            Program.SaveBitmap(result, path);
                        }
                        msg = "已保存: " + path;
                        try { BeginInvoke((MethodInvoker)delegate { Settings.Save(prms); }); } catch { }
                    }
                    catch (Exception ex) { msg = "保存失败: " + ex.Message; }
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            UseWaitCursor = false;
                            btnSave.Enabled = true;
                            status.Text = msg;
                            MessageBox.Show(this, msg, "线稿生成器",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                        });
                    }
                    catch { }
                });
            }
        }
    }
}
