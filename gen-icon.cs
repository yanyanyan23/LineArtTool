// gen-icon.cs - 从示例线稿生成 app.ico
// 设计: 白色圆角方形画布 + 猫耳少女线稿 (体现本工具输出的线稿风格)
// 尺寸: 16/24/32/48/64 (DIB) + 256 (PNG)
// 用法: gen-icon.exe <线稿png> <输出.ico>
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

class GenIcon
{
    static int[] Sizes = new int[] { 16, 24, 32, 48, 64, 256 };

    static void Main(string[] args)
    {
        string src = args[0];
        string outIco = args[1];

        using (Bitmap full = new Bitmap(src))
        {
            // 方形裁切: 大尺寸取头部主体, 小尺寸单独更紧裁切(见 MakeFrame)
            int W = full.Width, H = full.Height;
            int cx0 = (int)(W * 0.14), cy0 = (int)(H * 0.04);
            int side = Math.Min((int)(W * 0.72), (int)(H * 0.72));
            Rectangle crop = new Rectangle(cx0, cy0, side, side);
            if (crop.Right > W) crop.X = W - crop.Width;
            if (crop.Bottom > H) crop.Y = H - crop.Height;
            using (Bitmap head = full.Clone(crop, PixelFormat.Format32bppArgb))
            {
                byte[][] blobs = new byte[Sizes.Length][];
                for (int i = 0; i < Sizes.Length; i++)
                {
                    using (Bitmap frame = MakeFrame(head, Sizes[i]))
                        blobs[i] = (Sizes[i] >= 256) ? PngBytes(frame) : DibBytes(frame);
                }

                using (FileStream fs = new FileStream(outIco, FileMode.Create))
                {
                    BinaryWriter w = new BinaryWriter(fs);
                    w.Write((ushort)0);
                    w.Write((ushort)1);
                    w.Write((ushort)Sizes.Length);
                    int offset = 6 + 16 * Sizes.Length;
                    for (int i = 0; i < Sizes.Length; i++)
                    {
                        int s = Sizes[i];
                        w.Write((byte)(s >= 256 ? 0 : s));
                        w.Write((byte)(s >= 256 ? 0 : s));
                        w.Write((byte)0);
                        w.Write((byte)0);
                        w.Write((ushort)1);
                        w.Write((ushort)32);
                        w.Write((uint)blobs[i].Length);
                        w.Write((uint)offset);
                        offset += blobs[i].Length;
                    }
                    for (int i = 0; i < Sizes.Length; i++)
                        w.Write(blobs[i]);
                }
            }
        }
        Console.WriteLine("ICON OK " + outIco);
    }

    static Bitmap MakeFrame(Bitmap head, int size)
    {
        Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);

            float r = size * 0.22f;
            using (GraphicsPath path = RoundedRect(
                new RectangleF(0.5f, 0.5f, size - 1f, size - 1f), r))
            {
                using (SolidBrush wb = new SolidBrush(Color.White))
                    g.FillPath(wb, path);

                // 线稿增强对比: 小尺寸下线条更清晰
                float k = size <= 32 ? 2.4f : 1.5f;
                float t = 0.5f - 0.5f * k;
                ColorMatrix cm = new ColorMatrix(new float[][] {
                    new float[] { k, 0, 0, 0, 0 },
                    new float[] { 0, k, 0, 0, 0 },
                    new float[] { 0, 0, k, 0, 0 },
                    new float[] { 0, 0, 0, 1, 0 },
                    new float[] { t, t, t, 0, 1 }
                });
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(cm);
                    float padF = (size <= 32) ? 1f : size * 0.085f;
                    int pad = Math.Max(1, (int)Math.Round(padF));
                    Rectangle dest = new Rectangle(pad, pad,
                        size - 2 * pad, size - 2 * pad);
                    // 小尺寸取脸部区域放大(中心略下移对准五官), 保证 16/32px 下可辨
                    float zoom = (size <= 32) ? 0.70f : 1f;
                    float cyBias = (size <= 32) ? 0.56f : 0.5f;
                    float sw = head.Width * zoom, sh = head.Height * zoom;
                    float sx = head.Width * (1f - zoom) / 2f;
                    float sy = head.Height * cyBias - sh / 2f;
                    if (sy < 0) sy = 0;
                    if (sy > head.Height - sh) sy = head.Height - sh;
                    g.DrawImage(head, dest, sx, sy, sw, sh,
                        GraphicsUnit.Pixel, ia);
                }

                using (Pen pen = new Pen(Color.FromArgb(190, 190, 190),
                    Math.Max(1f, size / 48f)))
                    g.DrawPath(pen, path);
            }
        }
        return bmp;
    }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        GraphicsPath p = new GraphicsPath();
        float d = radius * 2f;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static byte[] PngBytes(Bitmap bmp)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }

    // 32bpp DIB: BITMAPINFOHEADER + XOR(BGRA 自底向上) + AND 掩码(全0)
    static byte[] DibBytes(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int xorSize = w * h * 4;
        int andRow = ((w + 31) / 32) * 4;
        int andSize = andRow * h;
        byte[] buf = new byte[40 + xorSize + andSize];
        WriteInt(buf, 0, 40);
        WriteInt(buf, 4, w);
        WriteInt(buf, 8, h * 2);
        WriteShort(buf, 12, 1);
        WriteShort(buf, 14, 32);
        WriteInt(buf, 16, 0);
        WriteInt(buf, 20, xorSize + andSize);

        byte[] row = new byte[w * 4];
        for (int y = 0; y < h; y++)
        {
            Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + y * bd.Stride), row, 0, w * 4);
            int dst = 40 + (h - 1 - y) * w * 4;
            for (int x = 0; x < w; x++)
            {
                buf[dst] = row[x * 4];         // B
                buf[dst + 1] = row[x * 4 + 1]; // G
                buf[dst + 2] = row[x * 4 + 2]; // R
                buf[dst + 3] = row[x * 4 + 3]; // A
                dst += 4;
            }
        }
        bmp.UnlockBits(bd);
        return buf;
    }

    static void WriteInt(byte[] b, int o, int v)
    {
        b[o] = (byte)v; b[o + 1] = (byte)(v >> 8);
        b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24);
    }

    static void WriteShort(byte[] b, int o, int v)
    {
        b[o] = (byte)v; b[o + 1] = (byte)(v >> 8);
    }
}
