using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using PocketDesk.Core;
using PocketDesk.Host.Native;

namespace PocketDesk.Host.Services;

/// <summary>系统 Shell 图标加载与缓存。提取在线程池完成，位图在 UI 线程创建。</summary>
public sealed class IconLoader
{
    private const int MaxCacheEntries = 512;
    private readonly ConcurrentDictionary<string, Bitmap> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(4, 4);

    public async Task<Bitmap?> LoadAsync(string path, int size, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var iconSize = Math.Clamp((int)Math.Round(size / 8.0) * 8, 16, 256);
        var key = $"{path.ToLowerInvariant()}|{iconSize}";
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_cache.TryGetValue(key, out cached))
            {
                return cached;
            }

            var pixels = await Task.Run(() => ShellIcons.Extract(path, iconSize)).ConfigureAwait(true);
            if (pixels is null)
            {
                return null;
            }

            var (bytes, width, height) = pixels.Value;
            var bitmap = await Dispatcher.UIThread.InvokeAsync(() => CreateBitmap(bytes, width, height));
            if (bitmap is null)
            {
                return null;
            }

            if (_cache.Count >= MaxCacheEntries)
            {
                _cache.Clear();
            }

            _cache[key] = bitmap;
            return bitmap;
        }
        catch (Exception ex)
        {
            LogService.Warn($"图标加载失败：{Path.GetFileName(path)} :: {ex.GetType().Name}");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Bitmap? CreateBitmap(byte[] bgra, int width, int height)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        var rowBytes = width * 4;
        for (var y = 0; y < height; y++)
        {
            Marshal.Copy(bgra, y * rowBytes, buffer.Address + y * buffer.RowBytes, rowBytes);
        }

        return bitmap;
    }

    public void Invalidate(string path)
    {
        var prefix = path.ToLowerInvariant() + "|";
        foreach (var key in _cache.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            _cache.TryRemove(key, out _);
        }
    }
}
