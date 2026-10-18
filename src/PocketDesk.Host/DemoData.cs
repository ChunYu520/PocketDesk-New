using PocketDesk.Core;
using PocketDesk.Host.Services;
using PocketDesk.Shared;
using PocketDesk.Shared.Widgets;

namespace PocketDesk.Host;

/// <summary>演示数据：用于首次验收、截图与手动体验。只在数据库为空时创建。</summary>
public static class DemoData
{
    public static void EnsureSeeded(HostServices host, bool includeWeather = true)
    {
        if (BoxService.GetBoxes().Count > 0)
        {
            return;
        }

        var demoRoot = Path.Combine(Path.GetTempPath(), "PocketDesk-New-Demo");
        var targetRoot = Path.Combine(demoRoot, "目标文件夹");
        var workRoot = Path.Combine(demoRoot, "待收纳");
        Directory.CreateDirectory(demoRoot);
        Directory.CreateDirectory(targetRoot);
        Directory.CreateDirectory(workRoot);

        WriteFile(Path.Combine(workRoot, "产品需求文档.md"), "# PocketDesk-New\n\n桌面组件层演示文档。\n");
        WriteFile(Path.Combine(workRoot, "项目计划.txt"), "1. 组件层\n2. 收纳盒\n3. 设置界面\n");
        WriteFile(Path.Combine(workRoot, "设计说明.txt"), "半透明卡片、无焦点交互、按需输入。\n");
        WriteFile(Path.Combine(workRoot, "预算表.csv"), "项目,预算\n组件层,3\n收纳盒,4\n设置界面,2\n");
        WriteFile(Path.Combine(targetRoot, "季度目标.md"), "- 完成 M1 组件层\n- 完成 M2 收纳盒\n");
        WriteFile(Path.Combine(targetRoot, "团队分工.txt"), "A：组件层\nB：收纳盒\nC：设置界面\n");
        var mappedFile = Path.Combine(demoRoot, "参考文档.md");
        WriteFile(mappedFile, "# 参考文档\n\n映射收纳盒只保存路径引用，不改动源文件。\n");

        var work = BoxService.CreateBox("工作资料", BoxType.Normal, null, new WindowPlacement
        {
            X = 70,
            Y = 60,
            Width = 430,
            Height = 470,
        });
        var files = Directory.EnumerateFiles(workRoot).ToList();
        ImportService.ImportPathsAsync(work, files, SilentImportInteraction.Instance).GetAwaiter().GetResult();

        var mapped = BoxService.CreateBox("灵感收藏", BoxType.Mapped, null, new WindowPlacement
        {
            X = 525,
            Y = 60,
            Width = 400,
            Height = 300,
        });
        ImportService.ImportUrlAsync(mapped, "https://github.com/avaloniaui", SilentImportInteraction.Instance).GetAwaiter().GetResult();
        ImportService.ImportPathsAsync(mapped, new[] { mappedFile }, SilentImportInteraction.Instance).GetAwaiter().GetResult();

        var target = BoxService.CreateBox("项目目标", BoxType.Target, targetRoot, new WindowPlacement
        {
            X = 950,
            Y = 60,
            Width = 300,
            Height = 300,
        });
        BoxService.GetItems(target, refreshMetadata: true);

        if (includeWeather)
        {
            var widget = host.Widgets.Get(AppInfo.WeatherWidgetId);
            if (widget is not null)
            {
                var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["city"] = "上海",
                    ["refreshMinutes"] = "30",
                    ["units"] = "c",
                    ["showFeelsLike"] = "true",
                    ["accent"] = "#4CC2FF",
                };

                WidgetRepository.Insert(new WidgetInstance
                {
                    WidgetTypeId = AppInfo.WeatherWidgetId,
                    DisplayName = "天气",
                    ZOrder = 10,
                    X = 950,
                    Y = 380,
                    Width = 300,
                    Height = 230,
                    Settings = settings,
                });
            }
        }

        LogService.Info("已创建演示数据");
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
