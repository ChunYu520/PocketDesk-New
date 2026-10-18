# 架构设计

## 1. 目标与约束

PocketDesk-New 的核心命题是：**在 Windows 上复现手机桌面小组件的“常驻可见、视觉稳定、按需交互”体验，同时把收纳盒做成第一个可替换的 Widget。**

硬约束：

- 主界面必须透明、无边框、停靠桌面层，且默认不抢前台应用的键盘焦点。
- 卡片之间不能因为系统焦点变化而互相灰化/闪烁。
- 只有输入控件（搜索框、重命名框）需要真实键盘焦点。
- 设置界面继续使用 WinUI 3，主界面不引用 WinUI 3 程序集。
- 新增组件（天气等）不能修改宿主主界面代码。

## 2. 进程与程序集边界

```text
PocketDesk.Host (Avalonia, WinExe)
  ├─ PocketDesk.Core
  ├─ PocketDesk.Widgets.Abstractions
  ├─ PocketDesk.Shared
  └─ 动态加载 Widgets/*.dll

PocketDesk.Settings (WinUI 3, WinExe)
  └─ PocketDesk.Shared

PocketDesk.Widgets.Weather (类库)
  ├─ PocketDesk.Widgets.Abstractions
  └─ PocketDesk.Shared
```

- `Shared` 不引用 Avalonia / WinUI，因此设置界面可以只依赖契约。
- `Abstractions` 引用 Avalonia，因为它返回 `Control`；它不被设置界面引用。
- 宿主不引用具体组件程序集；天气插件通过项目引用仅用于**构建与复制**（`ReferenceOutputAssembly=false` + `OutputItemType`）。
- 设置界面与宿主之间只通过 `IpcRequest/IpcResponse`（单行 JSON）通信。

## 3. 桌面组件层窗口

### 3.1 单窗口多卡片

首期采用一个覆盖主屏工作区的 Avalonia `Window`，卡片是窗口内 `Canvas` 的子控件。这样：

- 卡片之间共享同一坐标系，跨卡片拖拽不需要系统 OLE，不会引起窗口间焦点切换。
- 卡片视觉状态由 `CardViewModel` 驱动，和窗口是否激活无关。
- 层级顺序是窗口内 `ZIndex`，不受系统窗口 Z-order 干扰。

### 3.2 Win32 行为

`WindowController` 在窗口句柄创建后：

| 行为 | 实现 |
| :--- | :--- |
| 不进入任务栏/Alt+Tab | `WS_EX_TOOLWINDOW` + `ShowInTaskbar=false` |
| 默认不抢焦点 | `WS_EX_NOACTIVATE` + `WM_MOUSEACTIVATE → MA_NOACTIVATE` |
| 透明 | Avalonia `TransparencyLevelHint=Transparent`，窗口背景全透明 |
| 点击穿透 | `WM_NCHITTEST`：命中卡片 → `HTCLIENT`；否则 → `HTTRANSPARENT` |
| 桌面层停靠 | 把 Progman 设为窗口 owner，再 `HWND_BOTTOM`；owner 关系保证在壁纸之上，底部 z 序保证普通应用可覆盖 |
| Win+D 可见 | 450ms 前台轮询；桌面成为前台时临时 `HWND_TOP`，普通应用回来后再停靠 |
| 工作区尺寸 | `Screens.Primary.WorkingArea / Scaling` 换算成 DIP |

`WM_NCHITTEST` 使用 Avalonia 的 `PointToClient` 把屏幕坐标换算成窗口 DIP，再检查每张卡片视图的 `Bounds`。卡片之间的透明像素不会被命中，因此鼠标事件穿透到桌面/下层应用。\n\n桌面层的关键是 owner 关系：把窗口 owner 设为 Progman 后，它始终位于壁纸/桌面图标之上；再压到 z 序底部，普通应用窗口会完整盖住它。这样同时满足“壁纸之上”和“不遮挡工作流”。

### 3.3 焦点状态机

```text
默认：NOACTIVATE，卡片全部“视觉活跃”
   │ 点击搜索框 / 重命名框（GotFocus）
   ▼
输入态：移除 NOACTIVATE → SetForegroundWindow(host)
        → 卡片顶部显示强调色指示线
   │ Esc / Enter / 失焦 / WM_ACTIVATEAPP 失去激活
   ▼
恢复：重新加上 NOACTIVATE → SetForegroundWindow(之前的前台窗口)
```

关键点：

- 视觉高亮**只**取决于 `CardViewModel.IsInputActive`，不是 `Window.IsActive`。
- 其他卡片在 A 获得输入焦点时完全不变。
- 交还前台用 `AttachThreadInput` 附加到当前/目标线程，必要时回退 `SwitchToThisWindow`，解决 Windows 前台锁。
- `WM_MOUSEACTIVATE` 在非输入态返回 `MA_NOACTIVATE`，点击卡片背景不会激活窗口。

## 4. Widget 契约

```csharp
public interface IWidget
{
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }
    string Glyph { get; }
    bool IsBuiltIn { get; }
    bool SupportsMultipleInstances { get; }
    WidgetSettingsSchema GetSettingsSchema();
    Control CreateView(IWidgetContext context);
}
```

`IWidgetContext` 提供：

- 当前 `WidgetInstance` 与全局 `AppSettings`
- `IServiceProvider`（宿主服务、Core 服务）
- `GetSetting/SetSetting`
- `SettingsChanged` 事件（设置界面修改后即时刷新内容）
- `RequestKeyboardFocus()`（组件内含输入控件时请求临时激活）

宿主负责卡片外框、标题、拖拽、缩放、焦点指示线、右键菜单入口；Widget 只提供内容视图。这样天气等组件不需要重复实现卡片 chrome。

### 4.1 注册与插件加载

1. `HostServices.Initialize()` 注册内置 `BoxWidget`。
2. `WidgetRegistry.LoadPlugins(AppContext.BaseDirectory/Widgets)` 扫描 DLL。
3. 每个插件用独立 `AssemblyLoadContext` 加载；`PocketDesk.Shared`、`PocketDesk.Widgets.Abstractions`、Avalonia 等程序集返回 `null`，回退到默认上下文，避免类型重复。
4. 反射查找 `IWidget` 的无参实现，按 `Id` 去重注册。
5. 也支持 `%LOCALAPPDATA%\PocketDesk-New\Widgets` 作为用户插件目录。

### 4.2 收纳盒也是 Widget

`BoxWidget` 是内置 `IWidget`，返回 `BoxCardContent`。收纳盒实例与 `boxes` 表共用 Id，位置仍保存在 `boxes.window_json`，所以旧版数据层可以继续使用。

### 4.3 Schema 驱动设置

`WidgetSettingsSchema` 是纯数据传输对象：

```text
WidgetSettingsSchema
  ├─ WidgetTypeId / Title / Description
  └─ Fields[]
       ├─ Key / Label / Description
       ├─ Type: text | number | toggle | choice | color
       ├─ DefaultValue / Min / Max / Step / Placeholder
       └─ Choices[]
```

- 宿主的 `CreateSnapshot()` 把已加载组件的 Schema 通过 IPC 发给设置界面。
- 设置界面按 `Type` 生成 `TextBox / NumberBox / ToggleSwitch / ComboBox / 颜色输入`，不包含任何组件专用代码。
- 修改写回 `widgets.updateSettings`；收纳盒的 `view / showItemCount / sortField` 由宿主映射到 `BoxAppearance` / `boxes.sort_field`。

## 5. IPC

- 通道：`\\.\pipe\PocketDeskNew.Host.v1`，`PipeOptions.CurrentUserOnly`。
- 协议：UTF-8 单行 JSON；请求 `{ id, command, payload }`，响应 `{ id, ok, error, payload }`。
- 设置界面每次请求使用独立连接，6 秒超时；宿主按连接串行处理并在 UI 线程执行命令。
- 命令分组：
  - `settings.get / settings.update`
  - `widgets.add / remove / setVisible / reorder / updateSettings`
  - `boxes.create / rename / delete / setVisible / reorder / tile`
  - `host.showAll / hideAll / openDataDir / openLogDir / exportDiagnostics / shutdown`
- 设置界面不直接打开 SQLite，也不操作宿主 UI 对象；双方只交换 Shared 契约。

## 6. 数据层

`PocketDesk.Core` 从旧版迁移了：

- `Database`：SQLite WAL、schema 版本、迁移前 `VACUUM INTO` 备份（保留 3 份）。
- `BoxRepository / ItemRepository / JournalRepository / SettingsRepository`。
- `WidgetRepository`：新增 `widgets` 表（schema v2）。
- `FileSystemService`：journal 移动、跨卷分阶段复制校验、启动恢复、回收站删除。
- `ImportService`：普通/映射/目标的导入语义、冲突策略、大文件确认、云占位符/UNC/回收站保护。
- `PathUtil`、`ShortcutService`、`ShellService`、`LogService`。

新数据目录为 `%LOCALAPPDATA%\PocketDesk-New`，避免与旧版 schema v1 冲突。旧版逻辑保留，但 UI 层完全重写。

### 6.1 Schema v2

```sql
CREATE TABLE widgets (
  id TEXT PRIMARY KEY,
  widget_type_id TEXT NOT NULL,
  display_name TEXT NOT NULL,
  is_visible INTEGER NOT NULL DEFAULT 1,
  z_order INTEGER NOT NULL DEFAULT 0,
  x REAL NOT NULL DEFAULT 80,
  y REAL NOT NULL DEFAULT 80,
  width REAL NOT NULL DEFAULT 340,
  height REAL NOT NULL DEFAULT 280,
  settings_json TEXT NOT NULL DEFAULT '{}',
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
```

迁移是幂等的：v1 → v2 只 `CREATE TABLE IF NOT EXISTS widgets`，可在中断后重试。v1 数据会先备份再迁移。

## 7. 视觉系统

- `ThemeService` 解析 System/Light/Dark 与系统强调色（`DwmGetColorizationColor`），生成 `ThemePalette`。
- 所有卡片使用自定义画刷：背景 alpha 来自每盒 `BoxAppearance.BackgroundOpacity`，边框 alpha 来自 `BorderOpacity`，不依赖系统 `ThemeResource` 的焦点状态。
- 卡片圆角、阴影、强调色输入线都是自定义渲染，窗口失焦不改变。
- 输入指示线只在 `IsInputActive` 时出现；其他卡片的边框/背景/阴影不变。

## 8. 失败处理

- 数据库版本高于应用：拒绝启动并提示升级。
- 迁移失败：备份保留，日志记录；不静默丢数据。
- 文件移动失败：journal 记录 + 依据磁盘真实状态收敛；启动时 `RecoverPending`。
- 设置界面连不上宿主：显示离线页与重试，不阻塞宿主。
- 插件加载失败：记录日志，跳过该 DLL，不影响内置组件。
- 未知组件实例：快照中保留数据，宿主跳过渲染并记录警告。

## 9. 已知取舍

- 单窗口覆盖主屏工作区，因此全屏应用会完整盖住组件层（符合 AC-04），但组件层不能跨多个显示器。
- `RenderTargetBitmap` 的截图模式用于文档，不代表真实 DWM 合成；真实桌面层行为由 Win32 检查验证。
- `--demo` 会写入隔离的临时数据目录；正式数据目录不会被演示脚本污染。
