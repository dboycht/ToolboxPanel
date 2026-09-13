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

    /// <summary>同页内重排图标（索引越界/原地移动时什么都不做）。</summary>
    public void ReorderIcon(string tabId, int fromIndex, int toIndex)
    {
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
            return DragDropResult.Fail("来源标签页已不存在", request);
        }

        var targetTab = FindTab(request.TargetTabId);
        if (targetTab is null)
        {
            return DragDropResult.Fail("目标标签页已不存在", request);
        }

        if (payload.Kind == DragItemKind.Icon && targetTab.IsListTab)
        {
            return DragDropResult.Fail("图标不能放到列表页", request);
        }

        if (payload.Kind == DragItemKind.ListItem && !targetTab.IsListTab)
        {
            return DragDropResult.Fail("列表项不能放到网格页", request);
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
            return DragDropResult.Fail("被拖动的图标已不存在", request);
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
            return DragDropResult.Fail("被拖动的列表项已不存在", request);
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

    /// <summary>同页内重排：移动 → 重排序号 → 落盘。**索引越界已由调用方夹取**。</summary>
    private DragDropResult ApplySameTabDrop<T>(List<T> items, int fromIndex, int toIndex, DragDropRequest request)
    {
        if (fromIndex < 0)
        {
            return DragDropResult.Fail("被拖动的项已不存在", request);
        }

        var moved = items[fromIndex];
        items.RemoveAt(fromIndex);
        items.Insert(Math.Clamp(toIndex, 0, items.Count), moved);

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
