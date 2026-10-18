using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using PocketDesk.Core;
using PocketDesk.Shared;

namespace PocketDesk.Host.Services;

/// <summary>解析后的视觉调色板。所有卡片使用自定义画刷，不依赖控件的焦点/非激活状态。</summary>
public sealed class ThemePalette
{
    public bool IsDark { get; init; }
    public Color CardBackground { get; init; }
    public Color CardBorder { get; init; }
    public Color TextPrimary { get; init; }
    public Color TextSecondary { get; init; }
    public Color TextMuted { get; init; }
    public Color Accent { get; init; }
    public Color Shadow { get; init; }
    public Color InputBackground { get; init; }
    public Color Hover { get; init; }
    public Color MenuBackground { get; init; }
    public Color MenuBorder { get; init; }
    public Color Danger { get; init; }
    public Color DropOverlay { get; init; }
}

/// <summary>主题、强调色与材质参数。</summary>
public sealed class ThemeService
{
    public ThemePalette Palette { get; private set; } = Build(new AppSettings(), false);

    public event Action? Changed;

    public void Apply(AppSettings settings)
    {
        var requested = settings.Theme switch
        {
            "Dark" => ThemeVariant.Dark,
            "Light" => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };

        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = requested;
        }

        var platformVariant = Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant;
        var isDark = requested == ThemeVariant.Dark ||
                     (requested == ThemeVariant.Default && platformVariant == PlatformThemeVariant.Dark);

        var accent = settings.FollowSystemAccent
            ? GetSystemAccent() ?? ParseColor(settings.AccentColor, Color.Parse("#4CC2FF"))
            : ParseColor(settings.AccentColor, Color.Parse("#4CC2FF"));

        Palette = Build(settings, isDark, accent);
        Changed?.Invoke();
    }

    public static Color? GetSystemAccent()
    {
        try
        {
            var hr = DwmGetColorizationColor(out var argb, out _);
            if (hr != 0)
            {
                return null;
            }

            return Color.FromArgb(
                255,
                (byte)((argb >> 16) & 0xFF),
                (byte)((argb >> 8) & 0xFF),
                (byte)(argb & 0xFF));
        }
        catch
        {
            return null;
        }
    }

    private static ThemePalette Build(AppSettings settings, bool isDark, Color? accentOverride = null)
    {
        var accent = accentOverride ?? Color.Parse("#4CC2FF");
        var cardAlpha = (byte)Math.Clamp((int)Math.Round(settings.CardOpacity * 255), 40, 255);
        var borderAlpha = (byte)Math.Clamp((int)Math.Round(settings.CardBorderOpacity * 255), 0, 255);
        var shadowAlpha = (byte)Math.Clamp((int)Math.Round(settings.ShadowStrength / 100 * 140), 0, 160);

        return isDark
            ? new ThemePalette
            {
                IsDark = true,
                CardBackground = Color.FromArgb(cardAlpha, 22, 24, 31),
                CardBorder = Color.FromArgb(borderAlpha, 255, 255, 255),
                TextPrimary = Color.Parse("#FFFFFFFF"),
                TextSecondary = Color.Parse("#B8FFFFFF"),
                TextMuted = Color.Parse("#9EFFFFFF"),
                Accent = accent,
                Shadow = Color.FromArgb(shadowAlpha, 0, 0, 0),
                InputBackground = Color.Parse("#2EFFFFFF"),
                Hover = Color.Parse("#16FFFFFF"),
                MenuBackground = Color.Parse("#F21A1D24"),
                MenuBorder = Color.Parse("#2EFFFFFF"),
                Danger = Color.Parse("#FF7A7A"),
                DropOverlay = Color.FromArgb(70, accent.R, accent.G, accent.B),
            }
            : new ThemePalette
            {
                IsDark = false,
                CardBackground = Color.FromArgb(cardAlpha, 250, 250, 252),
                CardBorder = Color.FromArgb(borderAlpha, 0, 0, 0),
                TextPrimary = Color.Parse("#1B1C20"),
                TextSecondary = Color.Parse("#99000000"),
                TextMuted = Color.Parse("#7A000000"),
                Accent = accent,
                Shadow = Color.FromArgb(shadowAlpha, 0, 0, 0),
                InputBackground = Color.Parse("#0F000000"),
                Hover = Color.Parse("#0A000000"),
                MenuBackground = Color.Parse("#F7FFFFFF"),
                MenuBorder = Color.Parse("#1F000000"),
                Danger = Color.Parse("#C42B1C"),
                DropOverlay = Color.FromArgb(48, accent.R, accent.G, accent.B),
            };
    }

    private static Color ParseColor(string? text, Color fallback)
    {
        try
        {
            return string.IsNullOrWhiteSpace(text) ? fallback : Color.Parse(text);
        }
        catch
        {
            return fallback;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(out uint colorizationColor, out bool colorizationOpaqueBlend);
}
