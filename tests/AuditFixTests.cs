// AuditFixTests.cs —— 代码体检轮的回归测试
//
// 每一条都对应一个**真缺陷**（不是"补覆盖率"），注释里写清"改之前会怎样"，
// 免得后人"顺手简化"又把它们放回去。

using System.Text;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class AuditFixTests
{
    /// <summary>建一个"数据目录 + 一个图标页 + N 个图标"的环境。</summary>
    private static (DataStore Store, TabModel Tab, List<string> Ids) StoreWithIcons(
        TempDataDirectory temp, params string[] names)
    {
        var store = temp.NewStore();
        store.Load();
        var tab = store.AddTab("图标页");

        foreach (var name in names)
        {
            store.AddIcon(tab.Id, new IconModel { DisplayName = name, SourcePath = @"C:\a.exe" });
        }

        return (store, tab, tab.Icons.Select(i => i.Id).ToList());
    }

    // ────────────────────────────── ApplyIconOrder / ReorderListItems 的重复 id ──────────────────────────────

    [Fact]
    public void ApplyIconOrder_入参含重复id时不重复收同一个实例()
    {
        // ⚠️ 改之前：`seen` 是循环**之前**一次性构造的 HashSet，收项却是按**位置**收的。
        //    入参 `{A,A,B,C}` ⇒ 列表变成 [A,A,B,C]（长度 4、实际只有 3 个图标），
        //    同一个 IconModel 实例占两格、sort_order 出现重复值 ⇒
        //    之后按 id 删除只摘掉一格，界面与数据从此分叉。
        using var temp = new TempDataDirectory();
        var (store, tab, ids) = StoreWithIcons(temp, "A", "B", "C");

        var ok = store.ApplyIconOrder(tab.Id, new[] { ids[0], ids[0], ids[1], ids[2] });

        Assert.True(ok);
        Assert.Equal(3, tab.Icons.Count);                                   // 长度不虚增
        Assert.Equal(3, tab.Icons.Select(i => i.Id).Distinct().Count());    // 没有重复实例
        Assert.Equal(new[] { 0, 1, 2 }, tab.Icons.Select(i => i.SortOrder)); // 序号连续
        Assert.Equal(new[] { "A", "B", "C" }, tab.Icons.Select(i => i.DisplayName));
    }

    [Fact]
    public void ApplyIconOrder_清单里没提到的项追加到末尾()
    {
        using var temp = new TempDataDirectory();
        var (store, tab, ids) = StoreWithIcons(temp, "A", "B", "C");

        store.ApplyIconOrder(tab.Id, new[] { ids[2] });

        Assert.Equal(new[] { "C", "A", "B" }, tab.Icons.Select(i => i.DisplayName));
    }

    [Fact]
    public void ReorderListItems_入参含重复id时不重复收同一个实例()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var tab = store.AddTab("列表页", TabModel.TypeList);

        foreach (var description in new[] { "一", "二" })
        {
            store.AddListItem(tab.Id, new ListItemModel { Description = description });
        }

        var ids = tab.ListItems.Select(i => i.Id).ToList();
        store.ReorderListItems(tab.Id, new[] { ids[1], ids[1], ids[0] });

        Assert.Equal(2, tab.ListItems.Count);
        Assert.Equal(2, tab.ListItems.Select(i => i.Id).Distinct().Count());
        Assert.Equal(new[] { "二", "一" }, tab.ListItems.Select(i => i.Description));
        Assert.Equal(new[] { 0, 1 }, tab.ListItems.Select(i => i.SortOrder));
    }

    // ────────────────────────────── MoveIcon 的失败补偿 ──────────────────────────────

    [Fact]
    public void MoveIcon_目标页不存在时一个字节都不改()
    {
        // ⚠️ 改之前：先把图标从源页摘掉，发现目标页不存在，再用**调用方给的索引**插回源页，
        //    然后直接 return（没有 Save()）⇒ 内存里的顺序变了、磁盘没变；
        //    下一次任何不相关操作触发 Save() 就把这个没人要求过的重排持久化了。
        using var temp = new TempDataDirectory();
        var (store, tab, ids) = StoreWithIcons(temp, "A", "B", "C");
        var beforeOrder = tab.Icons.Select(i => i.Id).ToArray();
        var beforeBytes = File.ReadAllBytes(temp.TabsFile);

        store.MoveIcon(ids[0], "根本不存在的一页", newSortOrder: 2);

        Assert.Equal(beforeOrder, tab.Icons.Select(i => i.Id));      // 内存顺序不变
        Assert.Equal(beforeBytes, File.ReadAllBytes(temp.TabsFile));  // 磁盘字节不变
    }

    // ────────────────────────────── DragDropResult.Request.TargetIndex ──────────────────────────────

    [Fact]
    public void 同页拖放_成功结果回传的是实际落库下标()
    {
        // ⚠️ 改之前：同页分支把**入参原样**回传（连夹取都没做）——
        //    拖到末尾会回一个比列表长度还大的值，而注释把该字段的契约写成"实际落库后的索引"。
        using var temp = new TempDataDirectory();
        var (store, tab, ids) = StoreWithIcons(temp, "A", "B", "C");

        var result = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.Icon, tab.Id, ids[0]), tab.Id, targetIndex: 999));

        Assert.True(result.Success);
        Assert.Equal(new[] { "B", "C", "A" }, tab.Icons.Select(i => i.DisplayName));
        Assert.Equal(tab.Icons.Count - 1, result.Request.TargetIndex);   // 末尾 = 2，不是 999
    }

    // ────────────────────────────── 缓存文件名的路径越界 ──────────────────────────────

    [Theory]
    [InlineData(@"..\config.json")]
    [InlineData("../../outside.png")]
    [InlineData(@"sub\x.png")]
    [InlineData("")]
    [InlineData("..")]
    public void 删除图标时_越界的缓存文件名不会删到数据目录之外(string cacheFileName)
    {
        // ⚠️ 改之前：`Path.Combine(IconsDirectory, icon_cache_file)` 对 `..\config.json`
        //    会老老实实拼出 <data>\config.json，于是"清理图标缓存"顺手删掉了用户的设置文件。
        //    icon_cache_file 是**磁盘上 JSON 里的字符串**，必须当不可信输入。
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var tab = store.AddTab("图标页");

        // 造一个"数据目录里、icons 之外"的靶子文件
        var victim = Path.Combine(temp.Path, "config.json");
        File.WriteAllText(victim, "{}", new UTF8Encoding(false));

        store.AddIcon(tab.Id, new IconModel { DisplayName = "x", IconCacheFile = cacheFileName });
        var iconId = tab.Icons[0].Id;

        store.RemoveIcon(iconId);

        Assert.True(File.Exists(victim), "数据目录里的文件绝不能被图标缓存清理删掉");
        Assert.Equal("{}", File.ReadAllText(victim));
    }

    [Fact]
    public void 删除图标时_裸文件名照常被删掉()
    {
        // 反过来兜一条：正常路径不能因为上面那条校验而失效
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var tab = store.AddTab("图标页");

        temp.TouchIconCache("ok.png");
        store.AddIcon(tab.Id, new IconModel { DisplayName = "x", IconCacheFile = "ok.png" });
        var iconId = tab.Icons[0].Id;

        store.RemoveIcon(iconId);

        Assert.False(File.Exists(Path.Combine(temp.IconsDirectory, "ok.png")));
    }

    // ────────────────────────────── 原子写盘的失败语义 ──────────────────────────────

    [Fact]
    public void Save_临时文件写不进去时_原tabs文件保持原样()
    {
        // ⚠️ 原子写的承诺是"要么全写、要么原样"。用**同名目录**占住 `tabs.tmp`
        //    逼出写临时文件这一步失败，验证原 tabs.json 一个字节都没动。
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        store.AddTab("第二页");

        var before = File.ReadAllBytes(temp.TabsFile);

        // 把 tabs.tmp 的位置占成一个目录 ⇒ File.WriteAllText 必失败
        Directory.CreateDirectory(temp.TabsTempFile);

        Assert.ThrowsAny<Exception>(() => store.AddTab("第三页"));
        Assert.Equal(before, File.ReadAllBytes(temp.TabsFile));   // 目标文件没被写坏

        Directory.Delete(temp.TabsTempFile);
    }

    // ────────────────────────────── 孤儿缓存回收 ──────────────────────────────

    [Fact]
    public void 孤儿缓存会被回收_还在用的缓存不受影响()
    {
        // ⚠️ 改之前：`DataStore.CleanOrphanCache()` 只有单测在调 —— 生产路径（MainViewModel.Load）
        //    从来没调过。于是"改图标属性 → 提取新缓存"在中断/异常路径留下的旧 PNG、
        //    以及导入备份替换 tabs.json 之后对不上的旧缓存，会一直堆在 data/icons/ 里。
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var tab = store.AddTab("图标页");

        temp.TouchIconCache("keep.png");
        temp.TouchIconCache("orphan.png");
        store.AddIcon(tab.Id, new IconModel { DisplayName = "在用的", IconCacheFile = "keep.png" });
        store.Save();

        Assert.Contains("orphan.png", store.OrphanCacheFiles());

        store.CleanOrphanCache();

        Assert.True(File.Exists(Path.Combine(temp.IconsDirectory, "keep.png")));
        Assert.False(File.Exists(Path.Combine(temp.IconsDirectory, "orphan.png")));
        Assert.Empty(store.OrphanCacheFiles());
    }

    // ────────────────────────────── 批量加图标（只落一次盘）──────────────────────────────

    [Fact]
    public void AddIcons_批量加入并只落一次盘_序号连续()
    {
        // 逐个 `AddIcon` 是"每加一个就整份重写 tabs.json"；示例图标那条路有 20 个 ⇒ 20 次全量写。
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var tab = store.AddTab("示例");

        var batch = Enumerable.Range(0, 5)
            .Select(i => new IconModel { DisplayName = $"第{i}个", SourcePath = @"C:\a.exe" })
            .ToList();

        var added = store.AddIcons(tab.Id, batch);

        Assert.Equal(5, added);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, tab.Icons.Select(i => i.SortOrder));
        Assert.Equal(5, temp.NewStore().Load().Single(t => t.Id == tab.Id).Icons.Count);
    }

    [Fact]
    public void AddIcons_空批次不落盘()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var tab = store.AddTab("示例");
        var before = File.ReadAllBytes(temp.TabsFile);

        Assert.Equal(0, store.AddIcons(tab.Id, Array.Empty<IconModel>()));
        Assert.Equal(before, File.ReadAllBytes(temp.TabsFile));
    }

    [Fact]
    public void AddIcons_页不存在时什么都不做()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var before = File.ReadAllBytes(temp.TabsFile);

        var added = store.AddIcons("不存在的页", new[] { new IconModel { DisplayName = "x" } });

        Assert.Equal(0, added);
        Assert.Equal(before, File.ReadAllBytes(temp.TabsFile));
    }

    // ────────────────────────────── 示例图标的字段口径 ──────────────────────────────

    [Fact]
    public void 示例图标的文件类图标_两条路径字段口径一致()
    {
        // 与 IconEditor.Create / DropImporter 对齐：FILE/FOLDER 的 source 与 target 都填同一个值。
        // 只填一个虽然靠"读取侧优先 target"侥幸能用，但那是两套口径躺着等下次改读取侧。
        var icons = SampleIcons.BuildForExisting().ToList();
        var fileLike = icons.Where(i => i.Type is IconType.File or IconType.Folder).ToList();

        Assert.NotEmpty(fileLike);
        Assert.All(fileLike, icon => Assert.Equal(icon.SourcePath, icon.TargetPath));
    }
}
