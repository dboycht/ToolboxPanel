// MainViewModel.cs —— W3 主界面：读数据、准备图标、暴露给窗口
//
// 数据与系统能力全部来自 ToolboxPanel.Core（DataStore / IconExtractor / WindowsShortcut / Launcher），
// 这里只做「装配 + 翻译成界面模型」。
//
// 图标策略（与原版 v1.11.6 一致）：
//   · tabs.json 里已记 icon_cache_file 且文件在 → 直接用（不重复提取）
//   · 没有 / 文件丢了 → 现场提取一次，把新文件名写回模型（**只在真提取过时才写盘**）

using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.ViewModels;

public sealed class MainViewModel
{
    private readonly DataStore? _store;
    private readonly IconExtractor? _iconExtractor;
    private readonly Launcher? _launcher;

    /// <summary>纯 UI 演示模式：**不读不写任何数据文件**，点击图标也不会真的打开东西。</summary>
    private readonly bool _isDemo;

    /// <summary>本次载入是否动过数据（提取过图标缓存），决定要不要写回 tabs.json。</summary>
    private bool _dataChanged;

    public MainViewModel(string? dataDirectory = null, IShellHost? shellHost = null)
    {
        _store = dataDirectory is null ? DataStore.CreateDefault() : new DataStore(dataDirectory);
        _iconExtractor = new IconExtractor(_store.IconsDirectory);
        _launcher = new Launcher(shellHost);
    }

    /// <summary>演示模式的数据目录（放在临时目录，只用来存演示图标缓存，绝不碰用户数据）。</summary>
    private MainViewModel(bool demo)
    {
        _isDemo = demo;
        var demoDir = Path.Combine(Path.GetTempPath(), "toolboxpanel-ui-demo");
        _iconExtractor = new IconExtractor(Path.Combine(demoDir, "icons"));
        _launcher = null;
    }

    /// <summary>
    /// 纯 UI 演示：用固定的假数据 + 少量**真实系统图标**把界面铺满，
    /// 用于快速迭代视觉（不读 tabs.json、不写 tabs.json、点击不启动任何程序）。
    /// </summary>
    public static MainViewModel CreateDemo()
    {
        var viewModel = new MainViewModel(demo: true);
        viewModel.BuildDemoContent();
        return viewModel;
    }

    public ObservableCollection<TabItemViewModel> Tabs { get; } = new();

    public string DataDirectory => _store?.DataDirectory ?? "(演示模式：未使用数据目录)";

    public bool IsDemo => _isDemo;

    public string StatusText { get; private set; } = "正在载入…";

    /// <summary>读数据并把界面模型建好。</summary>
    public void Load()
    {
        if (_isDemo || _store is null)
        {
            BuildDemoContent();
            return;
        }

        _dataChanged = false;

        var tabs = _store.Load();
        Tabs.Clear();

        int iconCount = 0;
        int listItemCount = 0;
        int extracted = 0;

        foreach (var tab in tabs)
        {
            var tabViewModel = new TabItemViewModel(tab);

            if (tab.IsListTab)
            {
                foreach (var item in tab.ListItems)
                {
                    tabViewModel.ListItems.Add(new ListRowViewModel(item));
                }

                listItemCount += tab.ListItems.Count;
            }
            else
            {
                foreach (var icon in tab.Icons)
                {
                    var (source, wasExtracted) = LoadOrExtractIcon(icon);
                    tabViewModel.Icons.Add(new IconTileViewModel(icon, source));
                    extracted += wasExtracted ? 1 : 0;
                }

                iconCount += tab.Icons.Count;
            }

            Tabs.Add(tabViewModel);
        }

        // 只有真的提取出了新图标才写盘（避免无意义的"每次启动都改文件"）
        if (_dataChanged)
        {
            _store.Save();
        }

        StatusText = $"{tabs.Count} 个标签页 · {iconCount} 个图标 · {listItemCount} 个列表项"
                     + (extracted > 0 ? $"（新提取 {extracted} 个图标）" : string.Empty)
                     + $" · {_store.DataDirectory}";
    }

    /// <summary>打开一个图标（文件/文件夹/网址/快捷方式/命令）。演示模式下不启动任何程序。</summary>
    public LaunchResult Launch(IconModel icon)
        => _isDemo ? LaunchResult.Fail("演示模式：不会真的打开") : _launcher!.Open(icon);

    /// <summary>打开列表页的一行（路径）。演示模式下不启动任何程序。</summary>
    public LaunchResult LaunchListItem(ListItemModel item)
        => _isDemo ? LaunchResult.Fail("演示模式：不会真的打开") : _launcher!.OpenFileOrFolder(item.Path);

    /// <summary>图标缓存目录（界面上要显示"图标从哪来"时用）。</summary>
    public string IconsDirectory => _iconExtractor?.CacheDirectory ?? string.Empty;

    // ────────────────────────────── 纯 UI 演示内容 ──────────────────────────────

    /// <summary>
    /// 造一份"看着像真在用"的假数据：3 个标签页 + 十余个图标 + 若干列表项。
    /// 图标尽量用 <c>C:\Windows</c> 下**真实存在**的可执行文件取真实系统图标（不必用户数据），
    /// 取不到就自动落到字形/标准图标（模板本来就有兜底）。
    /// </summary>
    private void BuildDemoContent()
    {
        Tabs.Clear();

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system32 = Path.Combine(windows, "System32");
        string Sys(string name) => Path.Combine(system32, name);
        var shell32 = Sys("shell32.dll");

        // ① 主页：一屏密集图标（手机桌面观感）
        var home = new TabItemViewModel(new TabModel { Name = "主页", Order = 0, TabType = "grid" });
        var homeIcons = new (string Name, IconType Type, string Source, string Target, string Args, string WorkingDir, string IconFile, int IconIndex)[]
        {
            ("记事本", IconType.File, Sys("notepad.exe"), "", "", "", "", -1),
            ("计算器", IconType.File, Sys("calc.exe"), "", "", "", "", -1),
            ("画图", IconType.File, Sys("mspaint.exe"), "", "", "", "", -1),
            ("命令提示符", IconType.File, Sys("cmd.exe"), "", "", "", "", -1),
            ("PowerShell", IconType.File, Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"), "", "", "", "", -1),
            ("资源管理器", IconType.File, Path.Combine(windows, "explorer.exe"), "", "", "", "", -1),
            ("任务管理器", IconType.File, Sys("taskmgr.exe"), "", "", "", "", -1),
            ("注册表编辑器", IconType.File, Sys("regedt32.exe"), "", "", "", "", -1),
            ("系统信息", IconType.File, Sys("msinfo32.exe"), "", "", "", "", -1),
            ("控制台", IconType.File, Sys("mmc.exe"), "", "", "", "", -1),
            ("文件夹", IconType.Shortcut, Sys("drivers"), "", "", "", shell32, 3),
            ("示例网站", IconType.Url, "https://example.com", "", "", "", "", -1),
            ("一键回显", IconType.Command, "cmd.exe /c echo hi", "cmd.exe", "/c echo hi", system32, "", -1),
            ("纸牌", IconType.File, Sys("shell32.dll"), "", "", "", shell32, 15),
            ("我的电脑", IconType.File, Sys("shell32.dll"), "", "", "", shell32, 16),
            ("网络", IconType.File, Sys("shell32.dll"), "", "", "", shell32, 17),
            ("回收站", IconType.File, Sys("shell32.dll"), "", "", "", shell32, 31),
            ("图片", IconType.Shortcut, Sys("drivers"), "", "", "", shell32, 5),
            ("音乐", IconType.Shortcut, Sys("drivers"), "", "", "", shell32, 6),
            ("视频", IconType.Shortcut, Sys("drivers"), "", "", "", shell32, 7),
        };

        foreach (var spec in homeIcons)
        {
            var icon = new IconModel
            {
                Type = spec.Type,
                DisplayName = spec.Name,
                SourcePath = spec.Source,
                TargetPath = spec.Target,
                Arguments = spec.Args,
                WorkingDir = spec.WorkingDir,
            };

            var shortcut = spec.IconIndex >= 0
                ? new ShortcutInfo(spec.Target, spec.Args, spec.WorkingDir, spec.IconFile, spec.IconIndex,
                                   $"{spec.IconFile},{spec.IconIndex}", "演示用自定义图标")
                : null;

            var (source, _) = _iconExtractor is null
                ? (null, false)
                : LoadOrExtractOrFallback(icon, shortcut);

            home.Icons.Add(new IconTileViewModel(icon, source));
        }

        Tabs.Add(home);

        // ② 常用：列表页（两列）
        var frequent = new TabItemViewModel(new TabModel { Name = "常用", Order = 1, TabType = "list" });
        foreach (var (description, path) in new[]
        {
            ("项目源码", @"D:\code\DeepSeekHarness\ToolboxPanel"),
            ("代码总目录", @"D:\code"),
            ("下载", @"C:\Users\Public\Downloads"),
            ("桌面", @"C:\Users\Public\Desktop"),
        })
        {
            frequent.ListItems.Add(new ListRowViewModel(new ListItemModel { Description = description, Path = path }));
        }

        Tabs.Add(frequent);

        // ③ 工具：少量图标
        var tools = new TabItemViewModel(new TabModel { Name = "工具", Order = 2, TabType = "grid" });
        foreach (var (name, path) in new[]
        {
            ("磁盘清理", Sys("cleanmgr.exe")),
            ("设备管理器", Sys("devmgmt.msc")),
            ("服务", Sys("services.msc")),
            ("事件查看器", Sys("eventvwr.exe")),
            ("截图工具", Sys("SnippingTool.exe")),
        })
        {
            var icon = new IconModel { Type = IconType.File, DisplayName = name, SourcePath = path };
            var (source, _) = _iconExtractor is null ? (null, false) : LoadOrExtractOrFallback(icon, null);
            tools.Icons.Add(new IconTileViewModel(icon, source));
        }

        Tabs.Add(tools);

        var iconTotal = Tabs.Where(t => !t.IsList).Sum(t => t.Icons.Count);
        var itemTotal = Tabs.Where(t => t.IsList).Sum(t => t.ListItems.Count);
        StatusText = $"演示数据（不读写任何文件）· {Tabs.Count} 个标签页 · {iconTotal} 个图标 · {itemTotal} 个列表项";
    }

    /// <summary>演示用：取图标失败也不抛异常，交给模板的字形兜底。</summary>
    private (ImageSource? Source, bool Extracted) LoadOrExtractOrFallback(IconModel icon, ShortcutInfo? shortcut)
    {
        try
        {
            if (_iconExtractor is null)
            {
                return (null, false);
            }

            var cacheName = _iconExtractor.ExtractAndCacheForIcon(icon, shortcut);
            return string.IsNullOrEmpty(cacheName)
                ? (null, false)
                : (CreateImageSource(_iconExtractor.CachePath(cacheName)), true);
        }
        catch (Exception ex)
        {
            App.WriteCrash("MainViewModel.LoadOrExtractOrFallback", ex);
            return (null, false);
        }
    }


    private (ImageSource? Source, bool Extracted) LoadOrExtractIcon(IconModel icon)
    {
        if (_iconExtractor is null)
        {
            return (null, false);
        }

        if (!string.IsNullOrEmpty(icon.IconCacheFile))
        {
            var cached = _iconExtractor.CachePath(icon.IconCacheFile);
            if (File.Exists(cached))
            {
                return (CreateImageSource(cached), false);
            }
        }

        try
        {
            // 快捷方式：先解析（拿自定义图标的「文件 + 索引」），失败也不影响主流程
            var shortcut = icon.Type == IconType.Shortcut
                ? WindowsShortcut.Resolve(icon.SourcePath)
                : null;

            var cacheName = _iconExtractor.ExtractAndCacheForIcon(icon, shortcut);
            if (string.IsNullOrEmpty(cacheName))
            {
                return (null, false);
            }

            icon.IconCacheFile = cacheName;
            _dataChanged = true;
            return (CreateImageSource(_iconExtractor.CachePath(cacheName)), true);
        }
        catch (Exception ex)
        {
            // 提取失败不该让整个界面起不来：退回字形图标
            App.WriteCrash("MainViewModel.LoadOrExtractIcon", ex);
            return (null, false);
        }
    }

    private static ImageSource? CreateImageSource(string filePath)
    {
        try
        {
            return new BitmapImage(new Uri(filePath, UriKind.Absolute));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
