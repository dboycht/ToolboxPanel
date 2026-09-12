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
    private readonly DataStore _store;
    private readonly IconExtractor _iconExtractor;
    private readonly Launcher _launcher;

    /// <summary>本次载入是否动过数据（提取过图标缓存），决定要不要写回 tabs.json。</summary>
    private bool _dataChanged;

    public MainViewModel(string? dataDirectory = null, IShellHost? shellHost = null)
    {
        _store = dataDirectory is null ? DataStore.CreateDefault() : new DataStore(dataDirectory);
        _iconExtractor = new IconExtractor(_store.IconsDirectory);
        _launcher = new Launcher(shellHost);
    }

    public ObservableCollection<TabItemViewModel> Tabs { get; } = new();

    public string DataDirectory => _store.DataDirectory;

    public string StatusText { get; private set; } = "正在载入…";

    /// <summary>读数据并把界面模型建好。</summary>
    public void Load()
    {
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

    /// <summary>打开一个图标（文件/文件夹/网址/快捷方式/命令）。</summary>
    public LaunchResult Launch(IconModel icon) => _launcher.Open(icon);

    /// <summary>打开列表页的一行（路径）。</summary>
    public LaunchResult LaunchListItem(ListItemModel item) => _launcher.OpenFileOrFolder(item.Path);

    /// <summary>图标缓存目录（界面上要显示"图标从哪来"时用）。</summary>
    public string IconsDirectory => _store.IconsDirectory;

    private (ImageSource? Source, bool Extracted) LoadOrExtractIcon(IconModel icon)
    {
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
