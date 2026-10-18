using System.Runtime.InteropServices;

namespace PocketDesk.Host.Native;

/// <summary>Shell 图标提取（IShellItemImageFactory），返回 32 位 BGRA 像素。</summary>
internal static class ShellIcons
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE
    {
        public int CX;
        public int CY;

        public SIZE(int cx, int cy)
        {
            CX = cx;
            CY = cy;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [Flags]
    internal enum SIIGBF
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        MemoryOnly = 0x02,
        IconOnly = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly = 0x10,
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    internal static extern void SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern int GetObject(IntPtr hgdiobj, int cbBuffer, ref BITMAP lpvObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern int GetDIBits(
        IntPtr hdc,
        IntPtr hbm,
        uint startScan,
        uint lines,
        [Out] byte[]? bits,
        ref BITMAPINFO bmi,
        uint usage);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr hObject);

    internal const uint BI_RGB = 0;
    internal const uint DIB_RGB_COLORS = 0;

    internal static (byte[] Bytes, int Width, int Height)? Extract(string path, int size)
    {
        if (!PocketDesk.Core.PathUtil.Exists(path))
        {
            return null;
        }

        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
            var hr = factory.GetImage(
                new SIZE(size, size),
                SIIGBF.IconOnly | SIIGBF.BiggerSizeOk,
                out var hBitmap);
            if (hr != 0 || hBitmap == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return BitmapToPixels(hBitmap);
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        catch
        {
            return null;
        }
    }

    private static (byte[] Bytes, int Width, int Height)? BitmapToPixels(IntPtr hBitmap)
    {
        var bitmap = new BITMAP();
        if (GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), ref bitmap) == 0)
        {
            return null;
        }

        var width = bitmap.bmWidth;
        var height = bitmap.bmHeight;
        if (width <= 0 || height <= 0 || width > 512 || height > 512)
        {
            return null;
        }

        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
            },
        };

        var bytes = new byte[width * height * 4];
        var hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var lines = GetDIBits(hdc, hBitmap, 0, (uint)height, bytes, ref info, DIB_RGB_COLORS);
            if (lines == 0)
            {
                return null;
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }

        EnsureAlpha(bytes);
        return (bytes, width, height);
    }

    private static void EnsureAlpha(byte[] bgra)
    {
        for (var i = 3; i < bgra.Length; i += 4)
        {
            if (bgra[i] != 0)
            {
                return;
            }
        }

        for (var i = 3; i < bgra.Length; i += 4)
        {
            bgra[i] = 255;
        }
    }
}
