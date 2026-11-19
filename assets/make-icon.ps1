param([Parameter(Mandatory = $true)][string]$Portrait)
$ErrorActionPreference = 'Stop'
$Portrait = (Resolve-Path -LiteralPath $Portrait).Path
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class RezeIconMaker
{
    public static void Save(string source, string output)
    {
        using (var original = new Bitmap(source))
        using (var file = File.Create(Path.Combine(output, "reze-pixel-icon.ico")))
        using (var writer = new BinaryWriter(file))
        {
            int[] dimensions = {16, 32, 48, 256};
            byte[][] frames = new byte[dimensions.Length][];
            for (int i = 0; i < dimensions.Length; i++)
            {
                int size = dimensions[i];
                using (var pixels = Sample(original, Math.Min(size, 32)))
                using (var frame = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                {
                    using (var graphics = Graphics.FromImage(frame))
                    {
                        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                        graphics.PixelOffsetMode = PixelOffsetMode.Half;
                        graphics.DrawImage(pixels, new Rectangle(0, 0, size, size));
                    }
                    if (size == 256) frame.Save(Path.Combine(output, "reze-pixel-icon.png"), ImageFormat.Png);
                    frames[i] = Dib(frame);
                }
            }
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)frames.Length);
            int offset = 6 + 16 * frames.Length;
            for (int i = 0; i < frames.Length; i++)
            {
                writer.Write(unchecked((byte)dimensions[i]));
                writer.Write(unchecked((byte)dimensions[i]));
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(frames[i].Length);
                writer.Write(offset);
                offset += frames[i].Length;
            }
            foreach (byte[] frame in frames) writer.Write(frame);
        }
    }

    private static Bitmap Sample(Bitmap source, int size)
    {
        var sample = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(sample))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, new Rectangle(0, 0, size, size), 60, 115, 595, 595, GraphicsUnit.Pixel);
        }
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Color c = sample.GetPixel(x, y);
                sample.SetPixel(x, y, Color.FromArgb(255, Quantize(c.R), Quantize(c.G), Quantize(c.B)));
            }
        return sample;
    }

    private static int Quantize(int channel)
    {
        return Math.Min(255, ((channel + 8) / 16) * 16);
    }

    private static byte[] Dib(Bitmap icon)
    {
        int size = icon.Width;
        int maskSize = ((size + 31) / 32) * 4 * size;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(40);
            writer.Write(size);
            writer.Write(size * 2);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(0);
            writer.Write(size * size * 4 + maskSize);
            for (int i = 0; i < 4; i++) writer.Write(0);
            for (int y = size - 1; y >= 0; y--)
                for (int x = 0; x < size; x++) writer.Write(icon.GetPixel(x, y).ToArgb());
            writer.Write(new byte[maskSize]);
            return stream.ToArray();
        }
    }
}
'@
[RezeIconMaker]::Save($Portrait, $PSScriptRoot)
