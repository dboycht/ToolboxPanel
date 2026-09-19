// TabManagementTests.cs —— 批次 1：标签页管理的**数据层**行为
//
// 三条不变量（都是本次新加/收紧的，注释里写清"改之前会怎样"）：
//   ① 至少保留一个标签页 —— 数据层自己也拦（不只是界面弹窗）
//   ② 删除连带清掉那一页的图标缓存，并重排 order
//   ③ 重置数据：先换内存数据落盘、再删缓存（最坏只留孤儿缓存，绝不出现"坏图标"）

using System.Text;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class TabManagementTests
{
    // ────────────────────────────── 删除 ──────────────────────────────

    [Fact]
    public void 只剩一页时_拒绝删除且不落盘()
    {
        // ⚠️ 改之前：`RemoveTab` 会把最后一页删掉 ⇒ tabs.json 变成 `"tabs": []`，
        //    下次启动被当成**损坏文件**走"备份 + 重建默认页"的容错路径，用户会莫名看到一个 .bak。
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();   // 只有兜底的一页
        var before = File.ReadAllBytes(temp.TabsFile);

        var removed = store.TryRemoveTab(store.Tabs[0].Id, out var blockedReason);

        Assert.False(removed);
        Assert.False(string.IsNullOrEmpty(blockedReason));       // 有可显示的拒绝原因
        Assert.Single(store.Tabs);                                // 内存没动
        Assert.Equal(before, File.ReadAllBytes(temp.TabsFile));   // 磁盘一个字节没动
    }

    [Fact]
    public void 只剩一页时_RemoveTab_这个旧入口也不会删()
    {
        // `RemoveTab` 是既有签名（保留兼容），但它必须与 TryRemoveTab 同一条判据
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();

        store.RemoveTab(store.Tabs[0].Id);

        Assert.Single(store.Tabs);
        Assert.Single(temp.NewStore().Load());
    }

    [Fact]
    public void 删除未知id_什么都不做也不抛()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        store.AddTab("A");
        var before = File.ReadAllBytes(temp.TabsFile);

        Assert.False(store.TryRemoveTab("根本没这个页", out _));
        Assert.Equal(before, File.ReadAllBytes(temp.TabsFile));
    }

    [Fact]
    public void 删除正常页_连带清缓存并重排order()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var a = store.AddTab("A");
        var b = store.AddTab("B");

        temp.TouchIconCache("a-icon.png");
        store.AddIcon(a.Id, new IconModel { DisplayName = "x", IconCacheFile = "a-icon.png" });
        Assert.True(File.Exists(Path.Combine(temp.IconsDirectory, "a-icon.png")));

        Assert.True(store.TryRemoveTab(a.Id, out var blockedReason));

        Assert.Null(blockedReason);
        Assert.Null(store.FindTab(a.Id));
        Assert.False(File.Exists(Path.Combine(temp.IconsDirectory, "a-icon.png")));   // 缓存跟着走
        Assert.Equal(new[] { 0, 1 }, store.Tabs.Select(t => t.Order));                // order 重排
        Assert.NotNull(store.FindTab(b.Id));
        Assert.Equal(2, temp.NewStore().Load().Count);                                // 已落盘
    }

    // ────────────────────────────── 新建 ──────────────────────────────

    [Fact]
    public void 新建列表页_类型与默认名都对()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();

        var edit = Services.TabEditor.Create(null, isList: true);
        var tab = store.AddTab(edit.Name, edit.TabType);

        Assert.True(tab.IsListTab);
        Assert.Equal("list", tab.TabType);
        Assert.Equal(I18n.T("list.default_name"), tab.Name);

        // 重启后仍然是列表页（tab_type 落盘了）
        Assert.True(temp.NewStore().Load().Single(t => t.Id == tab.Id).IsListTab);
    }

    // ────────────────────────────── 重置数据 ──────────────────────────────

    [Fact]
    public void 重置数据_只剩一个默认页且缓存目录被清空()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var a = store.AddTab("A");
        var b = store.AddTab("B", TabModel.TypeList);

        temp.TouchIconCache("a1.png");
        temp.TouchIconCache("b1.png");
        store.AddIcon(a.Id, new IconModel { DisplayName = "x", IconCacheFile = "a1.png" });
        store.AddIcon(b.Id, new IconModel { DisplayName = "y", IconCacheFile = "b1.png" });

        var deleted = store.ResetAll();

        Assert.Equal(2, deleted);                       // 返回删掉的缓存个数（给状态栏用）
        Assert.Single(store.Tabs);
        Assert.False(store.Tabs[0].IsListTab);          // 重建出来的是网格页
        Assert.Empty(Directory.GetFiles(temp.IconsDirectory));

        var reloaded = temp.NewStore().Load();
        Assert.Single(reloaded);
        Assert.Empty(reloaded[0].Icons);
    }

    [Fact]
    public void 重置数据_落盘结果是合法文件_不是损坏文件()
    {
        // ⚠️ 这条钉住"重置后不能变成需要容错的文件"：Load() 一旦把文件判成损坏，
        //    就会复制一份 tabs.json.bak 出来 —— 用户会莫名多一个 .bak。
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        store.AddTab("A");
        store.ResetAll();

        temp.NewStore().Load();

        Assert.False(File.Exists(temp.TabsBackupFile), "重置不该产生 .bak（那不是损坏文件）");
    }

    [Fact]
    public void 重置数据_默认页名跟随语言()
    {
        var original = I18n.Current;
        try
        {
            using var temp = new TempDataDirectory();
            var store = temp.NewStore();
            store.Load();

            I18n.SetLanguage("en");
            store.ResetAll();

            Assert.Equal(I18n.T("tab.default_name"), store.Tabs[0].Name);
        }
        finally
        {
            I18n.SetLanguage(original);
        }
    }

    [Fact]
    public void 重置数据_顶层未知字段不再保留()
    {
        // 重置 = 回到初始状态，旧文档的顶层未知字段没有理由继续带着
        using var temp = new TempDataDirectory();
        temp.WriteRawTabsJson("""{"version":1,"tabs":[{"id":"t","name":"页"}],"future_field":123}""");
        var store = temp.NewStore();
        store.Load();
        Assert.Contains("future_field", File.ReadAllText(temp.TabsFile, Encoding.UTF8));

        store.ResetAll();

        Assert.DoesNotContain("future_field", File.ReadAllText(temp.TabsFile, Encoding.UTF8));
    }
}
