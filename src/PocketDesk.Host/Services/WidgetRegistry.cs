using System.Reflection;
using System.Runtime.Loader;
using PocketDesk.Core;
using PocketDesk.Shared.Widgets;
using PocketDesk.Widgets;

namespace PocketDesk.Host.Services;

/// <summary>
/// 组件注册表。内置组件由宿主注册；外部组件从 Widgets 目录动态发现。
/// 宿主只依赖 IWidget 契约，新增组件不需要修改宿主主界面代码。
/// </summary>
public sealed class WidgetRegistry
{
    private readonly Dictionary<string, IWidget> _widgets = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<IWidget> All => _widgets.Values;

    public void Register(IWidget widget)
    {
        if (string.IsNullOrWhiteSpace(widget.Id))
        {
            throw new ArgumentException("组件 Id 不能为空。", nameof(widget));
        }

        _widgets[widget.Id] = widget;
        LogService.Info($"已注册组件：{widget.Id} ({widget.DisplayName}){(widget.IsBuiltIn ? " [内置]" : " [插件]")}");
    }

    public IWidget? Get(string typeId) =>
        _widgets.TryGetValue(typeId, out var widget) ? widget : null;

    public IReadOnlyList<WidgetTypeInfo> GetTypeInfos() =>
        _widgets.Values
            .OrderBy(w => w.IsBuiltIn ? 0 : 1)
            .ThenBy(w => w.DisplayName, StringComparer.CurrentCulture)
            .Select(w => new WidgetTypeInfo
            {
                TypeId = w.Id,
                DisplayName = w.DisplayName,
                Description = w.Description,
                Glyph = w.Glyph,
                IsBuiltIn = w.IsBuiltIn,
                SupportsMultipleInstances = w.SupportsMultipleInstances,
                Schema = w.GetSettingsSchema(),
            })
            .ToList();

    /// <summary>从目录加载插件；返回成功加载的组件数。</summary>
    public int LoadPlugins(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var loaded = 0;
        foreach (var dll in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (IsHostAssembly(dll))
            {
                continue;
            }

            try
            {
                var context = new WidgetLoadContext(dll);
                var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(dll));
                var types = GetLoadableTypes(assembly)
                    .Where(t => typeof(IWidget).IsAssignableFrom(t) &&
                                t is { IsAbstract: false, IsInterface: false } &&
                                t.GetConstructor(Type.EmptyTypes) is not null);

                foreach (var type in types)
                {
                    if (Activator.CreateInstance(type) is not IWidget widget)
                    {
                        continue;
                    }

                    if (_widgets.ContainsKey(widget.Id))
                    {
                        LogService.Warn($"组件 {widget.Id} 已存在，忽略重复插件：{Path.GetFileName(dll)}");
                        continue;
                    }

                    Register(widget);
                    loaded++;
                }
            }
            catch (Exception ex)
            {
                LogService.Warn($"加载组件插件失败：{Path.GetFileName(dll)} :: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return loaded;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    private static bool IsHostAssembly(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith("PocketDesk.Shared", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("PocketDesk.Widgets.Abstractions", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("PocketDesk.Core", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("PocketDesk.Host", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>插件的加载上下文：契约与 UI 框架复用宿主已加载程序集，避免类型重复。</summary>
    private sealed class WidgetLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver? _resolver;

        public WidgetLoadContext(string pluginPath)
            : base($"PocketDesk.Widget.{Path.GetFileNameWithoutExtension(pluginPath)}", isCollectible: false)
        {
            try
            {
                _resolver = new AssemblyDependencyResolver(pluginPath);
            }
            catch
            {
                _resolver = null;
            }
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name ?? string.Empty;
            if (name.StartsWith("PocketDesk.Shared", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("PocketDesk.Widgets.Abstractions", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("PocketDesk.Core", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("SkiaSharp", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("HarfBuzzSharp", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("MicroCom", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("CommunityToolkit", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var path = _resolver?.ResolveAssemblyToPath(assemblyName);
            return path is not null ? LoadFromAssemblyPath(path) : null;
        }
    }
}
