namespace PocketDesk.Shared;

/// <summary>进程无关的产品元信息与 IPC 标识。</summary>
public static class AppInfo
{
    public const string ProductName = "PocketDesk-New";
    public const string Version = "1.0.1";
    public const string HostPipeName = "PocketDeskNew.Host.v1";
    public const string HostMutexName = @"Local\PocketDesk-New.Host";

    /// <summary>内置收纳盒 Widget 的类型 Id。</summary>
    public const string BoxWidgetId = "pocketdesk.box";

    /// <summary>占位天气 Widget 的类型 Id（用于验证扩展路径）。</summary>
    public const string WeatherWidgetId = "pocketdesk.weather";
}
