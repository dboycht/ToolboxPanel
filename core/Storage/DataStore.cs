// DataStore.cs —— ToolboxPanel v2 数据层（C# 重写，W1）
//
// 对应原 Python 的 src/toolbox/models/data_store.py（JSON 持久化层）。
// 行为逐条对齐，差异处均在注释里标注「⚠️ 与原版不同」。
//
// 关键行为（**别改**，改了旧数据就读不了 / 新数据旧版读不了）：
//   · 文件：<data>/tabs.json，图标缓存：<data>/icons/<uuid>.png
//   · 原子写：先写 tabs.tmp（注意不是 tabs.json.tmp），再 rename 覆盖 tabs.json
//   · 损坏容错：解析失败 → 复制一份 tabs.json.bak → 重建默认页并落盘
//   · 默认页：文件不存在 / tabs 为空 / 解析失败 → 一个名为 "Home" 的 grid 页
//   · 载入后按 order 升序排列（Python 用稳定排序，这里用 LINQ OrderBy 同样是稳定排序）

using System.Text;
using System.Text.Json;
using ToolboxPanel.Core.Models;

namespace ToolboxPanel.Core.Storage;

/// <summary>tabs.json 的读写与增删改查。</summary>
public sealed class DataStore
{
    /// <summary>数据文件版本（写回时固定写 1）。</summary>
    public const int TabsFileVersion = TabsDocument.CurrentVersion;

    /// <summary>兜底默认页名 —— 与原版一致是英文的 "Home"（不是 <see cref="TabModel.DefaultName"/>）。</summary>
    public const string FallbackTabName = "Home";

    /// <summary>UTF-8 且**不带 BOM**（Python 侧写出的文件就是无 BOM 的）。</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public DataStore(string dataDirectory)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        IconsDirectory = Path.Combine(DataDirectory, "icons");
        TabsFile = Path.Combine(DataDirectory, "tabs.json");
        TabsTempFile = Path.Combine(DataDirectory, "tabs.tmp");
        TabsBackupFile = Path.Combine(DataDirectory, "tabs.json.bak");

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(IconsDirectory);
    }

    /// <summary>用 <see cref="AppPaths.ResolveDataDirectory"/> 定位数据目录。</summary>
    public static DataStore CreateDefault(string? startDirectory = null)
        => new(AppPaths.ResolveDataDirectory(startDirectory));

    public string DataDirectory { get; }

    public string IconsDirectory { get; }

    public string TabsFile { get; }

    /// <summary>原子写的临时文件：与原版一样是 <c>tabs.tmp</c>。</summary>
    public string TabsTempFile { get; }

    public string TabsBackupFile { get; }

    /// <summary>当前内存中的标签页（已按 order 升序）。</summary>
    public List<TabModel> Tabs { get; private set; } = new();

    /// <summary>载入时读到的**顶层**未知字段，写回时原样带上（否则 Save 会用新文档把它们冲掉）。</summary>
    private Dictionary<string, JsonElement>? _documentExtraFields;

    // ────────────────────────────── 载入 / 保存 ──────────────────────────────

    /// <summary>
    /// 读取 tabs.json。任何失败都回落到「一个 Home 页」并立刻落盘。
    ///
    /// ⚠️ 与原版不同：原版只捕获 <c>json.JSONDecodeError / OSError</c>，**结构不合法**
    /// （例如 tabs 不是数组）会直接抛异常炸到 UI；这里把结构错误也当作「损坏」处理
    /// （备份 + 重建），因为对用户来说两者是同一件事：文件读不了，别丢数据即可。
    /// </summary>
    public List<TabModel> Load()
    {
        if (!File.Exists(TabsFile))
        {
            Tabs = CreateFallbackTabs();
            Save();
            return Tabs;
        }

        TabsDocument? document;
        try
        {
            var text = File.ReadAllText(TabsFile, Encoding.UTF8);
            document = TabsJson.Deserialize(text);
            if (document is null)
            {
                throw new JsonException("tabs.json 反序列化结果为 null");
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            BackupCorruptedFile();
            _documentExtraFields = null;
            Tabs = CreateFallbackTabs();
            Save();
            return Tabs;
        }

        var loaded = document.Tabs ?? new List<TabModel>();
        if (loaded.Count == 0)
        {
            _documentExtraFields = document.ExtraFields;
            Tabs = CreateFallbackTabs();
            Save();
            return Tabs;
        }

        _documentExtraFields = document.ExtraFields;

        // 与原版一致：OrderBy 是稳定排序（List.Sort 不是，别换成 Sort）
        Tabs = loaded.OrderBy(t => t.Order).ToList();
        return Tabs;
    }

    /// <summary>把当前状态原子写回 tabs.json。</summary>
    public void Save()
    {
        var document = new TabsDocument
        {
            Version = TabsDocument.CurrentVersion,
            Tabs = Tabs,
            ExtraFields = _documentExtraFields,   // 顶层未知字段原样带回
        };

        var json = TabsJson.Serialize(document);

        // 原子写：先写临时文件，再整体 rename 覆盖（Python 是 tmp.replace(file)）
        File.WriteAllText(TabsTempFile, json, Utf8NoBom);
        File.Move(TabsTempFile, TabsFile, overwrite: true);
    }

    /// <summary>整体替换内存中的标签页（<paramref name="save"/> 为 true 时立刻落盘）。</summary>
    public void SetTabs(IEnumerable<TabModel> tabs, bool save = true)
    {
        Tabs = tabs.ToList();
        if (save)
        {
            Save();
        }
    }

    private static List<TabModel> CreateFallbackTabs() => new()
    {
        new TabModel { Name = FallbackTabName, Order = 0 },
    };

    private void BackupCorruptedFile()
    {
        try
        {
            File.Copy(TabsFile, TabsBackupFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 备份失败不阻断「重建默认数据」，与原版的容错意图一致
        }
    }

    // ────────────────────────────── 查询 ──────────────────────────────

    public TabModel? FindTab(string tabId) => Tabs.FirstOrDefault(t => t.Id == tabId);

    /// <summary>按图标 id 找到「所属标签页 + 图标」；找不到返回 null。</summary>
    public (TabModel Tab, IconModel Icon)? FindIcon(string iconId)
    {
        foreach (var tab in Tabs)
        {
            foreach (var icon in tab.Icons)
            {
                if (icon.Id == iconId)
                {
                    return (tab, icon);
                }
            }
        }

        return null;
    }

    /// <summary>按列表项 id 找到「所属标签页 + 列表项」；找不到返回 null。</summary>
    public (TabModel Tab, ListItemModel Item)? FindListItem(string itemId)
    {
        foreach (var tab in Tabs)
        {
            foreach (var item in tab.ListItems)
            {
                if (item.Id == itemId)
                {
                    return (tab, item);
                }
            }
        }

        return null;
    }

    // ────────────────────────────── 标签页 ──────────────────────────────

    public TabModel AddTab(string name = FallbackTabName, string tabType = TabModel.TypeGrid)
    {
        var tab = new TabModel { Name = name, Order = Tabs.Count, TabType = tabType };
        Tabs.Add(tab);
        Save();
        return tab;
    }

    /// <summary>删除标签页（连同它的图标缓存文件），并重排 order。</summary>
    public void RemoveTab(string tabId)
    {
        var tab = FindTab(tabId);
        if (tab is null)
        {
            return;
        }

        foreach (var icon in tab.Icons)
        {
            DeleteCacheFile(icon.IconCacheFile);
        }

        Tabs.Remove(tab);
        RenumberTabOrder();
        Save();
    }

    public void RenameTab(string tabId, string newName)
    {
        var tab = FindTab(tabId);
        if (tab is null)
        {
            return;
        }

        tab.Name = newName;
        Save();
    }

    /// <summary>拖拽重排标签页（索引越界时什么都不做，与原版一致）。</summary>
    public void ReorderTabs(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Tabs.Count || toIndex < 0 || toIndex >= Tabs.Count)
        {
            return;
        }

        var tab = Tabs[fromIndex];
        Tabs.RemoveAt(fromIndex);
        Tabs.Insert(toIndex, tab);
        RenumberTabOrder();
        Save();
    }

    private void RenumberTabOrder()
    {
        for (int i = 0; i < Tabs.Count; i++)
        {
            Tabs[i].Order = i;
        }
    }

    // ────────────────────────────── 图标 ──────────────────────────────

    public void AddIcon(string tabId, IconModel icon)
    {
        var tab = FindTab(tabId);
        if (tab is null)
        {
            return;
        }

        icon.SortOrder = tab.Icons.Count;
        tab.Icons.Add(icon);
        Save();
    }

    public void RemoveIcon(string iconId)
    {
        var found = FindIcon(iconId);
        if (found is null)
        {
            return;
        }

        var (tab, icon) = found.Value;
        tab.Icons.Remove(icon);
        DeleteCacheFile(icon.IconCacheFile);
        Save();
    }

    /// <summary>
    /// **批量删除**一页里的多个图标（批量管理用）。
    ///
    /// <para>与 <see cref="RemoveIcon"/> 同语义：连带删掉图标缓存文件、重排 <c>sort_order</c>；
    /// 差别只有两点：① **只落盘一次**（而不是逐个 <c>Save()</c>）；② 未知 id 直接忽略。</para>
    ///
    /// <para>页不存在 / 不是网格页 / 一个都没匹配上 ⇒ **什么都不做**（不落盘、返回空清单）。</para>
    /// </summary>
    /// <returns>**实际删除**的图标模型（按原页面顺序），供界面移除与状态栏计数。</returns>
    public IReadOnlyList<IconModel> RemoveIcons(string tabId, IEnumerable<string>? iconIds)
    {
        if (iconIds is null)
        {
            return Array.Empty<IconModel>();
        }

        var wanted = new HashSet<string>(iconIds, StringComparer.Ordinal);
        if (wanted.Count == 0)
        {
            return Array.Empty<IconModel>();
        }

        var tab = FindTab(tabId);
        if (tab is null || tab.IsListTab)
        {
            return Array.Empty<IconModel>();
        }

        var removed = new List<IconModel>(wanted.Count);

        // 按页面顺序挑出来再删（顺序确定 ⇒ 日志/测试都稳定）
        foreach (var icon in tab.Icons.ToList())
        {
            if (wanted.Contains(icon.Id))
            {
                tab.Icons.Remove(icon);
                removed.Add(icon);
            }
        }

        if (removed.Count == 0)
        {
            return Array.Empty<IconModel>();
        }

        foreach (var icon in removed)
        {
            DeleteCacheFile(icon.IconCacheFile);
        }

        RenumberIcons(tab);
        Save();          // ★ 只写一次盘

        return removed;
    }

    /// <summary>同页内重排图标（索引越界/原地移动时什么都不做）。</summary>
    public void ReorderIcon(string tabId, int fromIndex, int toIndex)    {
        var tab = FindTab(tabId);
        if (tab is null || fromIndex < 0 || fromIndex >= tab.Icons.Count || fromIndex == toIndex)
        {
            return;
        }

        var icon = tab.Icons[fromIndex];
        tab.Icons.RemoveAt(fromIndex);
        tab.Icons.Insert(Math.Clamp(toIndex, 0, tab.Icons.Count), icon);
        RenumberIcons(tab);
        Save();
    }

    /// <summary>把图标移动到另一个标签页的指定位置（目标页不存在时放回原处，与原版一致）。</summary>
    public void MoveIcon(string iconId, string targetTabId, int newSortOrder)
    {
        var found = FindIcon(iconId);
        if (found is null)
        {
            return;
        }

        var (sourceTab, icon) = found.Value;
        sourceTab.Icons.Remove(icon);

        var targetTab = FindTab(targetTabId);
        if (targetTab is null)
        {
            sourceTab.Icons.Insert(Math.Clamp(newSortOrder, 0, sourceTab.Icons.Count), icon);
            return;
        }

        targetTab.Icons.Insert(Math.Clamp(newSortOrder, 0, targetTab.Icons.Count), icon);

        RenumberIcons(sourceTab);
        if (!ReferenceEquals(sourceTab, targetTab))
        {
            RenumberIcons(targetTab);
        }

        Save();
    }

    public void RenameIcon(string iconId, string newName)
    {
        var found = FindIcon(iconId);
        if (found is null)
        {
            return;
        }

        found.Value.Icon.DisplayName = newName;
        Save();
    }

    /// <summary>
    /// 按给定 id 顺序重排某个网格页的图标（**实时让位预览之后落库用**）。
    ///
    /// <para>为什么需要它而不是继续用 <see cref="ApplyDragDrop"/>：实时让位时界面集合已经被
    /// 拖到最终位置了，落库只是"把这个顺序写下来"；而 <see cref="ApplyDragDrop"/> 是拿
    /// 落点索引重新算一次 —— 界面已经变了，索引对不上，会出现闪一下又弹回去。
    /// 语义与列表项的 <see cref="ReorderListItems"/> 完全一致：没出现的 id **一律追加到末尾**
    /// （防御：绝不静默丢项）。</para>
    /// </summary>
    /// <returns>标签页不存在或不是网格页时返回 false（什么都不改）。</returns>
    public bool ApplyIconOrder(string tabId, IReadOnlyList<string> orderedIconIds)
    {
        var tab = FindTab(tabId);
        if (tab is null || tab.IsListTab)
        {
            return false;
        }

        var byId = tab.Icons.ToDictionary(icon => icon.Id, icon => icon);
        var seen = new HashSet<string>(orderedIconIds);
        var reordered = new List<IconModel>(tab.Icons.Count);

        foreach (var id in orderedIconIds)
        {
            if (byId.TryGetValue(id, out var icon))
            {
                reordered.Add(icon);
            }
        }

        reordered.AddRange(tab.Icons.Where(icon => !seen.Contains(icon.Id)));

        tab.Icons = reordered;
        RenumberIcons(tab);
        Save();
        return true;
    }

    /// <summary>
    /// 用编辑后的字段覆盖某个已有图标（按 id 找）。
    ///
    /// <para>为什么需要它而不是"直接改模型引用 + Save"：界面上的图标集合持有的是模型实例，
    /// 正常情况下是同一个对象，但这里**不依赖引用相等** —— 按 id 找到库里那份，逐字段复制，
    /// 保证"库里那份"永远是唯一事实源；id / sort_order / 未知扩展字段保持不变。</para>
    /// </summary>
    /// <returns>找不到该图标时返回 false（不落盘、什么都不改）。</returns>
    public bool UpdateIcon(IconModel updated)
    {
        var found = FindIcon(updated.Id);
        if (found is null)
        {
            return false;
        }

        var target = found.Value.Icon;
        target.Type = updated.Type;
        target.DisplayName = updated.DisplayName;
        target.SourcePath = updated.SourcePath;
        target.TargetPath = updated.TargetPath;
        target.Arguments = updated.Arguments;
        target.WorkingDir = updated.WorkingDir;
        target.Description = updated.Description;
        target.IconCacheFile = updated.IconCacheFile;
        Save();
        return true;
    }

    private static void RenumberIcons(TabModel tab)
    {
        for (int i = 0; i < tab.Icons.Count; i++)
        {
            tab.Icons[i].SortOrder = i;
        }
    }

    // ────────────────────────────── 列表项 ──────────────────────────────

    public void AddListItem(string tabId, ListItemModel item)
    {
        var tab = FindTab(tabId);
        if (tab is null)
        {
            return;
        }

        item.SortOrder = tab.ListItems.Count;
        tab.ListItems.Add(item);
        Save();
    }

    public void RemoveListItem(string itemId)
    {
        var found = FindListItem(itemId);
        if (found is null)
        {
            return;
        }

        found.Value.Tab.ListItems.Remove(found.Value.Item);
        Save();
    }

    /// <summary>只更新传入的非 null 字段（与原版一致的「部分更新」语义）。</summary>
    public void UpdateListItem(string itemId, string? description = null, string? path = null)
    {
        var found = FindListItem(itemId);
        if (found is null)
        {
            return;
        }

        var item = found.Value.Item;
        if (description is not null)
        {
            item.Description = description;
        }

        if (path is not null)
        {
            item.Path = path;
        }

        Save();
    }

    /// <summary>
    /// 按给定 id 顺序重排列表项（UI 拖拽后调用）。
    /// <paramref name="orderedItemIds"/> 里没出现的项**一律追加到末尾**（防御：绝不静默丢项）。
    /// </summary>
    public void ReorderListItems(string tabId, IReadOnlyList<string> orderedItemIds)
    {
        var tab = FindTab(tabId);
        if (tab is null)
        {
            return;
        }

        var byId = tab.ListItems.ToDictionary(it => it.Id, it => it);
        var seen = new HashSet<string>(orderedItemIds);
        var reordered = new List<ListItemModel>(tab.ListItems.Count);

        foreach (var id in orderedItemIds)
        {
            if (byId.TryGetValue(id, out var item))
            {
                reordered.Add(item);
            }
        }

        reordered.AddRange(tab.ListItems.Where(it => !seen.Contains(it.Id)));

        tab.ListItems = reordered;
        for (int i = 0; i < tab.ListItems.Count; i++)
        {
            tab.ListItems[i].SortOrder = i;
        }

        Save();
    }

    // ────────────────────────────── 拖拽排序（W3）──────────────────────────────

    /// <summary>
    /// 把界面上的拖放结果落库 —— **UI 侧唯一应该调的拖放入口**。
    ///
    /// <para>为什么不让界面直接调 <see cref="ReorderIcon"/> / <see cref="MoveIcon"/>：
    /// 那样"同页排序"与"跨页移动"要各写一套，而且很容易出现
    /// 「界面集合改了、Core 没改」这种下次启动才暴露的不一致。这里统一收口，
    /// 并且**失败时保证一个字节都不改**（越界的索引一律夹取，不做半途而废的部分修改）。</para>
    ///
    /// <para>⚠️ 与原 Python 版的一致性：原版是在 UI 里就地改集合再 save()，没有这一层；
    /// 结果数据格式完全相同（都是把顺序写进 <c>sort_order</c> 并重排），只是入口更收敛。</para>
    /// </summary>
    public DragDropResult ApplyDragDrop(DragDropRequest request)
    {
        var payload = request.Payload;

        var sourceTab = FindTab(payload.SourceTabId);
        if (sourceTab is null)
        {
            return DragDropResult.Fail(I18n.T("drag.error.source_missing"), request);
        }

        var targetTab = FindTab(request.TargetTabId);
        if (targetTab is null)
        {
            return DragDropResult.Fail(I18n.T("drag.error.target_missing"), request);
        }

        if (payload.Kind == DragItemKind.Icon && targetTab.IsListTab)
        {
            return DragDropResult.Fail(I18n.T("drag.error.icon_to_list"), request);
        }

        if (payload.Kind == DragItemKind.ListItem && !targetTab.IsListTab)
        {
            return DragDropResult.Fail(I18n.T("drag.error.list_to_grid"), request);
        }

        return payload.Kind == DragItemKind.Icon
            ? ApplyIconDrop(payload, sourceTab, targetTab, request)
            : ApplyListItemDrop(payload, sourceTab, targetTab, request);
    }

    private DragDropResult ApplyIconDrop(
        DragPayload payload, TabModel sourceTab, TabModel targetTab, DragDropRequest request)
    {
        if (!sourceTab.Icons.Any(i => i.Id == payload.ItemId))
        {
            return DragDropResult.Fail(I18n.T("drag.error.icon_gone"), request);
        }

        if (ReferenceEquals(sourceTab, targetTab))
        {
            int fromIndex = sourceTab.Icons.FindIndex(i => i.Id == payload.ItemId);
            int toIndex = Math.Clamp(request.TargetIndex, 0, sourceTab.Icons.Count);
            return ApplySameTabDrop(sourceTab.Icons, fromIndex, toIndex, request);
        }

        // 跨页移动：**先把图标从源页摘下来**，目标索引才是"去掉自己之后"的坐标系
        // （否则往后面的位置拖会整体差一位）
        var icon = FindIcon(payload.ItemId)!.Value.Icon;
        sourceTab.Icons.Remove(icon);
        int insertAt = Math.Clamp(request.TargetIndex, 0, targetTab.Icons.Count);
        targetTab.Icons.Insert(insertAt, icon);

        RenumberIcons(sourceTab);
        RenumberIcons(targetTab);
        Save();

        return DragDropResult.Ok(new DragDropRequest(payload, request.TargetTabId, insertAt));
    }

    private DragDropResult ApplyListItemDrop(
        DragPayload payload, TabModel sourceTab, TabModel targetTab, DragDropRequest request)
    {
        if (!sourceTab.ListItems.Any(it => it.Id == payload.ItemId))
        {
            return DragDropResult.Fail(I18n.T("drag.error.list_item_gone"), request);
        }

        if (ReferenceEquals(sourceTab, targetTab))
        {
            int fromIndex = sourceTab.ListItems.FindIndex(it => it.Id == payload.ItemId);
            int toIndex = Math.Clamp(request.TargetIndex, 0, sourceTab.ListItems.Count);
            return ApplySameTabDrop(sourceTab.ListItems, fromIndex, toIndex, request);
        }

        var item = FindListItem(payload.ItemId)!.Value.Item;
        sourceTab.ListItems.Remove(item);
        int insertAtListItem = Math.Clamp(request.TargetIndex, 0, targetTab.ListItems.Count);
        targetTab.ListItems.Insert(insertAtListItem, item);

        RenumberListItems(sourceTab);
        RenumberListItems(targetTab);
        Save();

        return DragDropResult.Ok(new DragDropRequest(payload, request.TargetTabId, insertAtListItem));
    }

    /// <summary>
    /// 同页内重排：移动 → 重排序号 → 落盘。
    ///
    /// <para>⚠️ <c>TargetIndex</c> 的口径是「插到**当前**第 N 项之前」（与界面上的落点指示线、
    /// 以及 <see cref="DropIndexCalculator.Compute"/> 的返回值同一个口径）。
    /// **往后移时被拖项自己还占着一格**，去掉自己之后索引要 -1，否则会落到目标项的**后面**（差一格）。
    /// 例：<c>[A,B,C,D]</c> 把 B 拖到 C 与 D 之间（TargetIndex=3）⇒ 期望 <c>[A,C,B,D]</c>，
    /// 不修正的话会变成 <c>[A,C,D,B]</c>（本轮补的单测抓出来的）。</para>
    /// </summary>
    private DragDropResult ApplySameTabDrop<T>(List<T> items, int fromIndex, int toIndex, DragDropRequest request)
    {
        if (fromIndex < 0)
        {
            return DragDropResult.Fail(I18n.T("drag.error.item_gone"), request);
        }

        var adjusted = toIndex > fromIndex ? toIndex - 1 : toIndex;

        var moved = items[fromIndex];
        items.RemoveAt(fromIndex);
        items.Insert(Math.Clamp(adjusted, 0, items.Count), moved);

        // 即使"原地落下"（顺序没变）也照样重排序号：序号必须是 0..N-1 的连续值
        for (int i = 0; i < items.Count; i++)
        {
            switch (items[i])
            {
                case IconModel icon:
                    icon.SortOrder = i;
                    break;
                case ListItemModel listItem:
                    listItem.SortOrder = i;
                    break;
            }
        }

        Save();
        return DragDropResult.Ok(new DragDropRequest(request.Payload, request.TargetTabId, toIndex));
    }

    private static void RenumberListItems(TabModel tab)
    {
        for (int i = 0; i < tab.ListItems.Count; i++)
        {
            tab.ListItems[i].SortOrder = i;
        }
    }

    // ────────────────────────────── 图标缓存清理 ──────────────────────────────

    /// <summary>icons/ 目录里没有被任何图标引用的缓存文件名。</summary>
    public HashSet<string> OrphanCacheFiles()
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tab in Tabs)
        {
            foreach (var icon in tab.Icons)
            {
                if (!string.IsNullOrEmpty(icon.IconCacheFile))
                {
                    referenced.Add(icon.IconCacheFile);
                }
            }
        }

        if (!Directory.Exists(IconsDirectory))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var onDisk = Directory.EnumerateFiles(IconsDirectory).Select(Path.GetFileName).OfType<string>();
        return onDisk.Where(name => !referenced.Contains(name))
                     .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>删除所有孤儿缓存文件（删不掉的跳过，与原版一致）。</summary>
    public void CleanOrphanCache()
    {
        foreach (var orphan in OrphanCacheFiles())
        {
            DeleteFileQuietly(Path.Combine(IconsDirectory, orphan));
        }
    }

    private void DeleteCacheFile(string cacheFileName)
    {
        if (string.IsNullOrEmpty(cacheFileName))
        {
            return;
        }

        DeleteFileQuietly(Path.Combine(IconsDirectory, cacheFileName));
    }

    private static void DeleteFileQuietly(string path)
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
            // 文件被占用/无权限：跳过，不影响数据一致性
        }
    }
}
