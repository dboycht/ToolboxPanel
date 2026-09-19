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
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Core.Storage;

/// <summary>tabs.json 的读写与增删改查。</summary>
public sealed class DataStore
{
    /// <summary>数据文件版本（写回时固定写 1）。</summary>
    public const int TabsFileVersion = TabsDocument.CurrentVersion;

    /// <summary>兜底默认页名 —— 与原版一致是英文的 "Home"（不是 <see cref="TabModel.DefaultName"/>）。</summary>
    public const string FallbackTabName = "Home";

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

    /// <summary>
    /// 把当前状态原子写回 tabs.json。
    ///
    /// <para>串行化与"失败不留临时文件"都交给 <see cref="AtomicFile"/>（同一目标的并发保存会排队，
    /// 否则后一次会因为"临时文件已被前一次改名搬走"抛 FileNotFoundException）。</para>
    /// </summary>
    public void Save()
    {
        var document = new TabsDocument
        {
            Version = TabsDocument.CurrentVersion,
            Tabs = Tabs,
            ExtraFields = _documentExtraFields,   // 顶层未知字段原样带回
        };

        var json = TabsJson.Serialize(document);

        AtomicFile.WriteAllText(TabsFile, TabsTempFile, json);
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

    /// <summary>
    /// 删除标签页（连同它的图标缓存文件），并重排 order。
    ///
    /// <para>⚠️ **至少保留一个标签页**：只剩一页时这里什么都不做（判据与文案见
    /// <see cref="TabEditor.CannotRemoveReason"/>，界面上会先弹「无法删除」）。
    /// 需要知道"到底删没删、为什么没删"就调 <see cref="TryRemoveTab"/>。</para>
    /// </summary>
    public void RemoveTab(string tabId) => TryRemoveTab(tabId, out _);

    /// <summary>
    /// 删除标签页，并回答"删没删成、没删成是因为什么"。
    ///
    /// <para>这是**防御式**的：即便界面忘了先问 <see cref="TabEditor.CanRemove"/>，
    /// 数据层也不会把最后一页删掉 —— "一页都不剩"的 tabs.json 下次启动会被当成损坏文件
    /// 而走"备份 + 重建默认页"的容错路径，用户会莫名看到一个 `.bak`。</para>
    /// </summary>
    /// <param name="blockedReason">没删成的原因（走 i18n，可直接显示）；删成了为 null。</param>
    /// <returns>真的删掉了返回 true。</returns>
    public bool TryRemoveTab(string tabId, out string? blockedReason)
    {
        blockedReason = null;

        if (Tabs.Count <= 1)
        {
            blockedReason = TabEditor.CannotRemoveReason(Tabs.Count);
            return false;
        }

        var tab = FindTab(tabId);
        if (tab is null)
        {
            blockedReason = null;   // 找不到就当"已经没了"，不算用户可见的失败
            return false;
        }

        foreach (var icon in tab.Icons)
        {
            DeleteCacheFile(icon.IconCacheFile);
        }

        Tabs.Remove(tab);
        RenumberTabOrder();
        Save();
        return true;
    }

    /// <summary>
    /// **重置数据**（原版 文件菜单 →「重置数据」`Ctrl+Shift+R`）：清空所有标签页与图标缓存，
    /// 重建一个默认页。
    ///
    /// <para>⚠️ 顺序是刻意的（见工作区 `memory/22` §1 的判据："删除/覆盖类操作要问
    /// '这一步失败之后用户手上还剩什么'"）：</para>
    /// <list type="number">
    /// <item><b>先</b>把内存数据换成"一个默认页"并落盘；</item>
    /// <item><b>再</b>删 <c>icons/</c> 里的缓存文件。</item>
    /// </list>
    /// <para>反过来（原版 Python 就是先删目录）一旦在中间中断，磁盘上会留下
    /// "tabs.json 里还列着一堆图标、文件却已经没了" 的**坏图标**；
    /// 按现在这个顺序，最坏也只是剩下一堆**没人引用的孤儿缓存**——
    /// 而 `MainViewModel.Load()` 每次载入都会 `CleanOrphanCache()` 把它们收掉（上一轮已接上）。</para>
    ///
    /// <para>默认页名用 <see cref="TabModel.DefaultName"/>（原版重置时用的就是 <c>tab.default_name</c>，
    /// 跟随当前语言）；注意这与"tabs.json 读不出来时的兜底页名"不同 —— 那个是英文 "Home"，
    /// 属于**损坏容错**，不是用户主动重置。</para>
    /// </summary>
    /// <returns>实际删掉的缓存文件个数（给状态栏用）。</returns>
    public int ResetAll()
    {
        // ① 先换内存数据 + 落盘（此刻起 tabs.json 已经是干净的默认页）
        Tabs = new List<TabModel> { new() { Name = TabModel.DefaultName, Order = 0 } };
        _documentExtraFields = null;   // 重置就是回到初始状态，不再保留旧的顶层未知字段
        Save();

        // ② 再删图标缓存（删不掉的跳过，与 CleanOrphanCache 同策略）
        var deleted = 0;
        if (Directory.Exists(IconsDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(IconsDirectory))
            {
                var name = Path.GetFileName(file);
                if (!CacheFileName.IsPlain(name))
                {
                    continue;
                }

                if (TryDeleteFileCounting(Path.Combine(IconsDirectory, name)))
                {
                    deleted++;
                }
            }
        }

        return deleted;
    }

    /// <summary>删一个文件，成功返回 true（失败跳过，不抛）。</summary>
    private static bool TryDeleteFileCounting(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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

    /// <summary>
    /// 一次性给某页放一整批图标（**只落盘一次**）。
    ///
    /// <para>为什么需要它：`AddIcon` 每加一个就整份重写 tabs.json，批量创建 N 个图标
    /// 就是 N 次全量落盘（示例图标那条路是 20 个 ⇒ 20 次写整个文件）。批量入口把
    /// "N 次"压成"1 次"，慢盘上差别很明显。</para>
    ///
    /// <para>语义与逐个 `AddIcon` 完全一致：<c>sort_order</c> 从当前末尾开始连续编号；
    /// 页不存在时什么都不做（不落盘）。</para>
    /// </summary>
    /// <returns>实际加入的个数。</returns>
    public int AddIcons(string tabId, IEnumerable<IconModel> icons)
    {
        ArgumentNullException.ThrowIfNull(icons);

        var tab = FindTab(tabId);
        if (tab is null)
        {
            return 0;
        }

        var added = 0;
        foreach (var icon in icons)
        {
            icon.SortOrder = tab.Icons.Count;
            tab.Icons.Add(icon);
            added++;
        }

        if (added > 0)
        {
            Save();   // ★ 只写一次盘
        }

        return added;
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

    /// <summary>
    /// 把图标移动到另一个标签页的指定位置。
    ///
    /// <para>⚠️ **目标页不存在时一个字节都不改**（直接返回）。原来的写法是"先把图标从源页摘掉、
    /// 再发现目标页不存在、于是用**调用方给的索引**重新插回源页、然后 return（没有 <c>Save()</c>）" ——
    /// 结果是内存里的顺序变了、磁盘没变，等下一次任何不相关操作触发 <c>Save()</c> 时，
    /// 这个没人要求过的重排就被持久化了。判据前置到最前面，"半途而废的修改"就不可能出现。</para>
    /// </summary>
    public void MoveIcon(string iconId, string targetTabId, int newSortOrder)
    {
        var targetTab = FindTab(targetTabId);
        if (targetTab is null)
        {
            return;   // ★ 目标页不存在 ⇒ 什么都不动（不摘、不插、不落盘）
        }

        var found = FindIcon(iconId);
        if (found is null)
        {
            return;
        }

        var (sourceTab, icon) = found.Value;
        sourceTab.Icons.Remove(icon);
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

        tab.Icons = ReorderById(tab.Icons, orderedIconIds, icon => icon.Id);
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

        tab.ListItems = ReorderById(tab.ListItems, orderedItemIds, item => item.Id);
        for (int i = 0; i < tab.ListItems.Count; i++)
        {
            tab.ListItems[i].SortOrder = i;
        }

        Save();
    }

    /// <summary>
    /// 「按给定 id 顺序重排一个列表，没提到的项一律追加到末尾」—— 图标与列表项共用这一份实现。
    ///
    /// <para>⚠️ 这里曾经有一个真 bug（图标/列表项两处各写了一遍同样的错）：判据用的是**循环之前**
    /// 一次性构造的 <c>HashSet(orderedIds)</c>，而收项是按**位置**收的 —— 于是入参里同一个 id
    /// 出现两次时，对应的模型实例会被 <c>Add</c> 两次（列表长度虚增、<c>sort_order</c> 出现重复值，
    /// 后续按 id 删除只摘掉一格，界面与数据从此分叉）。</para>
    ///
    /// <para>现在改为「收下即标记」：<c>taken.Add(id)</c> 返回 false 就是重复，直接跳过；
    /// 末尾补齐的判据也用同一个 <c>taken</c> —— 两个集合的口径不可能再分叉。</para>
    /// </summary>
    private static List<T> ReorderById<T>(List<T> current, IReadOnlyList<string> orderedIds, Func<T, string> idOf)
    {
        var byId = current.ToDictionary(idOf, item => item);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var reordered = new List<T>(current.Count);

        foreach (var id in orderedIds)
        {
            // taken.Add 返回 false = 这个 id 已经收过了（重复入参）⇒ 跳过，绝不重复收同一个实例
            if (taken.Add(id) && byId.TryGetValue(id, out var item))
            {
                reordered.Add(item);
            }
        }

        // 清单里没提到的项一律追加到末尾（防御：绝不静默丢项）
        reordered.AddRange(current.Where(item => !taken.Contains(idOf(item))));

        return reordered;
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

        // 摘掉自己之后再夹取：此刻合法的下标区间是 [0, Count]
        var insertedAt = Math.Clamp(adjusted, 0, items.Count);
        items.Insert(insertedAt, moved);

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

        // ⚠️ 回传的是**实际插入下标**（夹取 + 往后移 -1 之后的），不是入参：
        //    `DragDropResult.Request.TargetIndex` 的契约就是"落库后的实际索引"，
        //    原来这里透传入参，于是"拖到末尾"会回一个比列表长度还大的值（夹取也没做）。
        return DragDropResult.Ok(new DragDropRequest(request.Payload, request.TargetTabId, insertedAt));
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
            if (CacheFileName.Combine(IconsDirectory, orphan) is { } path)
            {
                DeleteFileQuietly(path);
            }
        }
    }

    /// <summary>
    /// 删掉某个图标的缓存文件。
    ///
    /// <para>⚠️ <paramref name="cacheFileName"/> 是**磁盘上 JSON 里的字符串**，必须当不可信输入：
    /// 只有裸文件名才拼路径（判据见 <see cref="CacheFileName"/>）。
    /// 否则 `"..\config.json"` 这种值会让"清理图标缓存"删到数据目录里的设置文件，
    /// `"..\..\..\Windows\..."` 更是完全离开数据目录。</para>
    /// </summary>
    private void DeleteCacheFile(string cacheFileName)
    {
        if (CacheFileName.Combine(IconsDirectory, cacheFileName) is { } path)
        {
            DeleteFileQuietly(path);
        }
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
