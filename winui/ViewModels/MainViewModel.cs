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

    // ────────────────────────────── 右键菜单的四个动作（W5）──────────────────────────────

    /// <summary>「用其他应用打开…」（原版 <c>_open_with</c>：rundll32 的「打开方式」对话框）。</summary>
    public LaunchResult OpenWith(IconModel icon)
        => _isDemo ? LaunchResult.Fail("演示模式：不会真的打开") : _launcher!.OpenWith(icon);

    /// <summary>「打开文件位置」（原版 <c>_open_file_location</c>：文件 → explorer /select；文件夹 → 打开它）。</summary>
    public LaunchResult OpenFileLocation(IconModel icon)
        => _isDemo ? LaunchResult.Fail("演示模式：不会真的打开") : _launcher!.OpenFileLocation(icon);

    /// <summary>
    /// 「重命名」：Core 校验（名称必填）→ 落库 → 刷新图块上的名字。
    /// 只改名字，**不重取图标**（与原版一致）。
    /// </summary>
    public IconEditResult RenameIcon(IconModel icon, string? newName)
    {
        if (_store is null)
        {
            return IconEditResult.Fail(DemoNoSave);
        }

        var result = IconEditor.Rename(icon, newName);
        if (!result.Success)
        {
            return result;
        }

        _store.RenameIcon(icon.Id, icon.DisplayName);
        FindTile(icon.Id)?.RefreshName();
        return result;
    }

    /// <summary>
    /// 「批量删除」：把勾选的图标一次删掉（Core 侧**一次落盘** + 连带删缓存文件），
    /// 再把它们从界面集合移除并刷新标签栏数量。
    /// 返回**实际删除的数量**（0 = 演示模式 / 页不存在 / 一个都没匹配上，此时什么都不做）。
    /// </summary>
    public int RemoveIcons(string tabId, IReadOnlyList<string> iconIds)
    {
        if (_store is null)
        {
            return 0;
        }

        var removed = _store.RemoveIcons(tabId, iconIds);
        if (removed.Count == 0)
        {
            return 0;
        }

        var tab = FindTab(tabId);
        if (tab is not null)
        {
            var removedIds = removed.Select(icon => icon.Id).ToHashSet(StringComparer.Ordinal);

            foreach (var tile in tab.Icons.Where(tile => removedIds.Contains(tile.Model.Id)).ToList())
            {
                tab.Icons.Remove(tile);
            }

            tab.NotifyCountLabel();
        }

        return removed.Count;
    }

    /// <summary>
    /// 「列表项」的新建 / 编辑属性 / 重命名 / 删除（列表页行右键菜单）。
    /// 规则与文案全在 Core 的 `ListItemEditor`（有单测）；这里只负责落库 + 界面集合跟随。
    /// </summary>
    public bool AddListItem(string tabId, ListItemModel item)
    {
        if (_store is null || FindTab(tabId) is not { IsList: true } tab)
        {
            return false;
        }

        _store.AddListItem(tabId, item);
        tab.ListItems.Add(new ListRowViewModel(item));
        tab.NotifyCountLabel();
        return true;
    }

    /// <summary>编辑属性：改说明与路径（两个字段都写，与原版一致）。</summary>
    public bool UpdateListItem(ListItemModel item)
    {
        if (_store is null)
        {
            return false;
        }

        _store.UpdateListItem(item.Id, item.Description, item.Path);
        RefreshRowOf(item.Id);
        return true;
    }

    /// <summary>重命名：只改说明（路径不动）—— 调用方先用 Core 的 TryRename 校验并写入模型。</summary>
    public bool RenameListItem(ListItemModel item)
    {
        if (_store is null)
        {
            return false;
        }

        _store.UpdateListItem(item.Id, item.Description, null);
        RefreshRowOf(item.Id);
        return true;
    }

    public bool RemoveListItem(ListItemModel item)
    {
        if (_store is null || FindListItemOwner(item.Id) is not { } owner)
        {
            return false;
        }

        _store.RemoveListItem(item.Id);

        var row = owner.ListItems.FirstOrDefault(r => r.Model.Id == item.Id);
        if (row is not null)
        {
            owner.ListItems.Remove(row);
        }

        owner.NotifyCountLabel();
        return true;
    }

    /// <summary>哪一页持有这个列表项（找不到返回 null）。</summary>
    private TabItemViewModel? FindListItemOwner(string itemId)
        => Tabs.FirstOrDefault(tab => tab.ListItems.Any(row => row.Model.Id == itemId));

    /// <summary>模型字段改过之后让那一行就地刷新（不重建整行，保住滚动位置）。</summary>
    private void RefreshRowOf(string itemId)
        => FindListItemOwner(itemId)?.ListItems.FirstOrDefault(row => row.Model.Id == itemId)?.Refresh();

    /// <summary>
    /// 「删除」：Core 落库（连带删掉图标缓存文件）→ 从界面集合里移除 → 刷新标签栏数量。
    /// 返回 false 表示演示模式或找不到该图标（此时**什么都不做**）。
    /// </summary>
    public bool RemoveIcon(IconModel icon)
    {
        if (_store is null)
        {
            return false;
        }

        var tab = Tabs.FirstOrDefault(t => t.Icons.Any(tile => tile.Model.Id == icon.Id));
        if (tab is null)
        {
            return false;
        }

        _store.RemoveIcon(icon.Id);

        var tile = tab.Icons.FirstOrDefault(t => t.Model.Id == icon.Id);
        if (tile is not null)
        {
            tab.Icons.Remove(tile);
        }

        tab.NotifyCountLabel();
        return true;
    }

    /// <summary>
    /// 从资源管理器**拖入**的路径 → 建图标（对应原版 <c>tab_widget._add_dropped_paths</c>）。
    ///
    /// <para>判定（分类 / 命名 / 去重 / 文案）全在 Core 的 <see cref="DropImporter"/>（有单测）；
    /// 这里按判定结果执行：**按路径提取图标 → 落库 → 进界面集合**，并收集每条状态文案
    /// （原版是每处理一个路径就发一条状态消息）。</para>
    ///
    /// <para>⚠️ 顺序与原版一致：先提取缓存再落库，且**重复/不存在的路径一个字节都不改**。</para>
    /// </summary>
    public DropImportResult AddDroppedPaths(string tabId, IReadOnlyList<string> paths)
    {
        if (_store is null)
        {
            return DropImportResult.Fail(DemoNoSave);
        }

        var tab = FindTab(tabId);
        if (tab is null || tab.IsList)
        {
            return DropImportResult.Fail("目标标签页不存在或不是网格页");
        }

        var decisions = DropImporter.Plan(paths, tab.Icons.Select(tile => tile.Model.SourcePath));
        var messages = new List<string>(decisions.Count);
        int added = 0;

        foreach (var decision in decisions)
        {
            if (decision.Kind != DropDecisionKind.Add)
            {
                messages.Add(decision.Message);
                continue;
            }

            var icon = DropImporter.CreateIcon(decision);
            if (icon is null)
            {
                continue;
            }

            var source = RefreshIconCache(icon, IconRefreshPlan.FromSource(icon.SourcePath));

            _store.AddIcon(tabId, icon);
            tab.Icons.Add(new IconTileViewModel(icon, source));
            messages.Add(decision.Message);
            added++;
        }

        if (added > 0)
        {
            tab.NotifyCountLabel();
        }

        return new DropImportResult(added, messages, null);
    }

    /// <summary>图标缓存目录（界面上要显示"图标从哪来"时用）。</summary>
    public string IconsDirectory => _iconExtractor?.CacheDirectory ?? string.Empty;

    /// <summary>
    /// 底层的 <see cref="DataStore"/>（演示模式下为 null）。
    /// 给"补充示例图标"这类**数据层操作**用；界面仍然只通过本 ViewModel 的方法改数据。
    /// </summary>
    internal DataStore? Store => _store;

    /// <summary>数据里有没有可显示的内容（所有内容页加起来至少一个图标）。</summary>
    public bool HasAnyIcon => Tabs.Any(tab => tab.Icons.Count > 0);

    // ────────────────────────────── 拖拽排序（W3）──────────────────────────────

    /// <summary>
    /// 把界面上的拖放结果落库，并让**两张标签页的界面集合跟着 Core 走**。
    ///
    /// <para>顺序很关键（也是这个类唯一有点绕的地方）：
    /// ① 先让 <see cref="DataStore.ApplyDragDrop"/> 改 Core（它是唯一事实源，失败就一个字节都没动）；
    /// ② 再 <see cref="TabItemViewModel.SyncIconsFromModel"/> 把界面集合重排成 Core 的顺序
    ///    —— 源页与目标页都要同步（跨页移动时两张页的集合都变了）。
    /// ③ 最后刷新标签栏上的数量文字。</para>
    ///
    /// <para>演示模式下 <c>_store</c> 为空：**不做任何事**并返回失败（演示模式不产生持久化副作用）。</para>
    /// </summary>
    public DragDropResult ApplyDrop(DragDropRequest request)
    {
        if (_store is null)
        {
            return DragDropResult.Fail("演示模式：不会真的保存", request);
        }

        var result = _store.ApplyDragDrop(request);
        if (!result.Success)
        {
            return result;
        }

        if (request.Payload.Kind == DragItemKind.Icon)
        {
            FindTab(request.Payload.SourceTabId)?.SyncIconsFromModel();
            FindTab(request.TargetTabId)?.SyncIconsFromModel();
        }
        else
        {
            FindTab(request.Payload.SourceTabId)?.SyncListItemsFromModel();
            FindTab(request.TargetTabId)?.SyncListItemsFromModel();
        }

        return result;
    }

    private TabItemViewModel? FindTab(string tabId)
        => Tabs.FirstOrDefault(t => t.Id == tabId);

    // ────────────────────────────── 新建 / 编辑图标（W5）──────────────────────────────
    //
    // 四条纪律（与拖拽排序同一套思路）：
    //   ① **校验与字段语义全在 Core**（IconEditor）—— 这里只把草稿递过去、把结果搬进界面；
    //   ② **先落库、再改界面集合**（Core 是唯一事实源，界面的顺序/内容跟着它走）；
    //   ③ 图标缓存按 Core 给的「刷新计划」提取（不要再在 UI 里写 if 判断类型）；
    //   ④ 演示模式下什么都不写（返回失败），绝不产生持久化副作用。

    private const string DemoNoSave = "演示模式：不会真的保存";

    /// <summary>
    /// 新建一个图标：校验 → 提取图标 → Core 落库 → 加进界面集合。
    /// </summary>
    public IconEditResult CreateIcon(string tabId, IconEditDraft draft)
    {
        if (_store is null)
        {
            return IconEditResult.Fail(DemoNoSave);
        }

        var tab = FindTab(tabId);
        if (tab is null || tab.IsList)
        {
            return IconEditResult.Fail("目标标签页不存在或不是网格页");
        }

        var result = IconEditor.Create(draft);
        if (!result.Success || result.Icon is null)
        {
            return result;
        }

        var icon = result.Icon;
        var source = RefreshIconCache(icon, result.Refresh);

        _store.AddIcon(tabId, icon);        // 顺序号由 Core 给（= 当前数量），界面直接追加即可
        tab.Icons.Add(new IconTileViewModel(icon, source));
        tab.NotifyCountLabel();
        return result;
    }

    /// <summary>
    /// 把编辑结果写回：校验 → 就地改模型 → 按需重取图标 → Core 落库 → 刷新那个图块。
    /// </summary>
    public IconEditResult UpdateIcon(IconModel icon, IconEditDraft draft)
    {
        if (_store is null)
        {
            return IconEditResult.Fail(DemoNoSave);
        }

        var result = IconEditor.Edit(icon, draft);   // 成功时**就地**改的就是界面上那个模型
        if (!result.Success)
        {
            return result;
        }

        var source = RefreshIconCache(icon, result.Refresh);
        _store.UpdateIcon(icon);
        FindTile(icon.Id)?.Refresh(source);
        return result;
    }

    private IconTileViewModel? FindTile(string iconId)
        => Tabs.SelectMany(tab => tab.Icons).FirstOrDefault(tile => tile.Model.Id == iconId);

    /// <summary>
    /// 按 Core 给的刷新计划重取图标缓存，写回 <see cref="IconModel.IconCacheFile"/>，返回可显示的图片源。
    ///
    /// <para>⚠️ 与原版一致：**提取成功之后才删旧缓存**（原版是 extract → <c>_delete_cache</c> → 换名）；
    /// 删文件失败只当没删掉 —— 绝不因为一个缓存文件让"改属性"整件事失败。</para>
    /// </summary>
    private ImageSource? RefreshIconCache(IconModel icon, IconRefreshPlan plan)
    {
        if (_iconExtractor is null)
        {
            return null;
        }

        if (plan.Kind == IconRefreshKind.None)
        {
            // 图标不用重取：沿用已缓存的那张（文件没了就返回 null，模板走字形兜底）
            return CachedImageSource(icon);
        }

        try
        {
            var cacheName = plan.Kind switch
            {
                IconRefreshKind.ReextractFromSource =>
                    _iconExtractor.ExtractAndCache(plan.SourcePath, icon.Type),
                IconRefreshKind.CustomIndex =>
                    _iconExtractor.ExtractAndCacheFromIconFile(plan.IconFile, plan.IconIndex, icon.Type),
                _ => _iconExtractor.CreateFallbackCache(icon.Type),
            };

            if (string.IsNullOrEmpty(cacheName))
            {
                return null;
            }

            var oldCacheFile = icon.IconCacheFile;
            icon.IconCacheFile = cacheName;

            if (!string.IsNullOrEmpty(oldCacheFile)
                && !string.Equals(oldCacheFile, cacheName, StringComparison.OrdinalIgnoreCase))
            {
                DeleteCacheFileQuietly(_iconExtractor.CachePath(oldCacheFile));
            }

            return CreateImageSource(_iconExtractor.CachePath(cacheName));
        }
        catch (Exception ex)
        {
            // 提取失败不该把"改属性"整件事搞失败：退回字形兜底，数据照常落库
            App.WriteCrash("MainViewModel.RefreshIconCache", ex);
            return null;
        }
    }

    private ImageSource? CachedImageSource(IconModel icon)
    {
        if (_iconExtractor is null || string.IsNullOrEmpty(icon.IconCacheFile))
        {
            return null;
        }

        var path = _iconExtractor.CachePath(icon.IconCacheFile);
        return File.Exists(path) ? CreateImageSource(path) : null;
    }

    private static void DeleteCacheFileQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 旧缓存删不掉不影响结果（下次清孤儿缓存会处理）
        }
    }

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

/// <summary>一次"拖入添加"的结果：成功几个 + 每个路径的状态文案（原版是逐条发状态消息）。</summary>
public sealed record DropImportResult(int Added, IReadOnlyList<string> Messages, string? Error)
{
    public static DropImportResult Fail(string error) => new(0, Array.Empty<string>(), error);
}
