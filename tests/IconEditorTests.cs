// IconEditorTests.cs —— 新建/编辑图标的字段语义（W5）
//
// 行为基准 = 原 Python 的 icon_edit_dialog.apply() + tab_widget._create_*_icon / icon_grid._edit_icon。
// 三处有意差异（URL 忽略大小写 / 错误消息为中文常量 / 刷新计划显式返回）见 IconEditor.cs 顶部注释。

using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class IconEditorTests
{
    // ────────────────────────────── 新建：文件 / 文件夹 ──────────────────────────────

    [Fact]
    public void Create_文件_路径写入source与target_且要按路径提取()
    {
        var result = IconEditor.Create(new IconEditDraft
        {
            Type = IconType.File,
            DisplayName = "  记事本  ",
            Path = @"C:\Windows\notepad.exe",
        });

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        var icon = result.Icon!;
        Assert.Equal("记事本", icon.DisplayName);          // 名称去空白
        Assert.Equal(@"C:\Windows\notepad.exe", icon.SourcePath);
        Assert.Equal(@"C:\Windows\notepad.exe", icon.TargetPath);
        Assert.Equal(IconRefreshKind.ReextractFromSource, result.Refresh.Kind);
        Assert.Equal(@"C:\Windows\notepad.exe", result.Refresh.SourcePath);
    }

    [Fact]
    public void Create_文件夹_与文件同一套字段语义()
    {
        var result = IconEditor.Create(new IconEditDraft
        {
            Type = IconType.Folder,
            DisplayName = "我的资料",
            Path = @"D:\Docs",
        });

        Assert.True(result.Success);
        Assert.Equal(@"D:\Docs", result.Icon!.TargetPath);
        Assert.Equal(IconRefreshKind.ReextractFromSource, result.Refresh.Kind);
    }

    [Fact]
    public void Create_名称空_失败且不产出图标()
    {
        var result = IconEditor.Create(new IconEditDraft { Type = IconType.File, DisplayName = "  ", Path = "x" });

        Assert.False(result.Success);
        Assert.Equal(IconEditor.ErrorNameRequired, result.ErrorMessage);
        Assert.Null(result.Icon);
    }

    [Fact]
    public void Create_文件路径空_失败()
    {
        var result = IconEditor.Create(new IconEditDraft { Type = IconType.File, DisplayName = "x", Path = "" });

        Assert.False(result.Success);
        Assert.Equal(IconEditor.ErrorPathRequired, result.ErrorMessage);
    }

    [Fact]
    public void Create_文件夹路径空_失败()
    {
        var result = IconEditor.Create(new IconEditDraft { Type = IconType.Folder, DisplayName = "x" });

        Assert.False(result.Success);
        Assert.Equal(IconEditor.ErrorPathRequired, result.ErrorMessage);
    }

    // ────────────────────────────── 新建：快捷方式 ──────────────────────────────

    [Fact]
    public void Create_快捷方式_目标入target_lnk来源入source()
    {
        var result = IconEditor.Create(new IconEditDraft
        {
            Type = IconType.Shortcut,
            DisplayName = "VS Code",
            Path = @"C:\Program Files\Microsoft VS Code\Code.exe",
            ShortcutSourcePath = @"C:\Users\me\Desktop\Code.lnk",
            Arguments = "--new-window",
            WorkingDir = @"C:\Projects",
            Description = "编辑器",
        });

        Assert.True(result.Success);
        var icon = result.Icon!;
        Assert.Equal(@"C:\Program Files\Microsoft VS Code\Code.exe", icon.TargetPath);
        Assert.Equal(@"C:\Users\me\Desktop\Code.lnk", icon.SourcePath);
        Assert.Equal("--new-window", icon.Arguments);
        Assert.Equal(@"C:\Projects", icon.WorkingDir);
        Assert.Equal("编辑器", icon.Description);
        // 没给自定义图标 → 按 .lnk 路径提取（shell 自己解析）
        Assert.Equal(IconRefreshKind.ReextractFromSource, result.Refresh.Kind);
        Assert.Equal(@"C:\Users\me\Desktop\Code.lnk", result.Refresh.SourcePath);
    }

    [Fact]
    public void Create_快捷方式_给了自定义图标_用文件加索引提取()
    {
        var result = IconEditor.Create(new IconEditDraft
        {
            Type = IconType.Shortcut,
            DisplayName = "终端",
            Path = @"C:\Windows\System32\WindowsTerminal.exe",
            ShortcutSourcePath = @"C:\tmp\Terminal.lnk",
            CustomIconPath = @"C:\Windows\System32\shell32.dll",
            CustomIconIndex = 13,
        });

        Assert.True(result.Success);
        Assert.Equal(IconRefreshKind.CustomIndex, result.Refresh.Kind);
        Assert.Equal(@"C:\Windows\System32\shell32.dll", result.Refresh.IconFile);
        Assert.Equal(13, result.Refresh.IconIndex);
    }

    [Fact]
    public void Create_快捷方式_路径空_失败()
    {
        var result = IconEditor.Create(new IconEditDraft { Type = IconType.Shortcut, DisplayName = "x" });

        Assert.False(result.Success);
        Assert.Equal(IconEditor.ErrorPathRequired, result.ErrorMessage);
    }

    // ────────────────────────────── 新建：网址 / 命令 ──────────────────────────────

    [Fact]
    public void Create_网址_没写协议自动补https_且用标准图标()
    {
        var result = IconEditor.Create(new IconEditDraft { Type = IconType.Url, DisplayName = "官网", Path = "example.com" });

        Assert.True(result.Success);
        Assert.Equal("https://example.com", result.Icon!.SourcePath);
        Assert.Equal("https://example.com", result.Icon!.TargetPath);
        Assert.Equal(IconRefreshKind.StandardFallback, result.Refresh.Kind);
    }

    [Fact]
    public void Create_网址_已有协议不重复补_大小写不敏感()
    {
        // ⚠️ 有意差异：Python 的 startswith 区分大小写（"HTTPS://x" 会被补成 https://HTTPS://x），
        //    这里忽略大小写 —— 与原版行为不同但更宽容，见 IconEditor.cs 顶部注释
        foreach (var input in new[] { "http://a.com", "https://a.com", "ftp://a.com", "HTTPS://A.COM", "Ftp://a.com" })
        {
            var result = IconEditor.Create(new IconEditDraft { Type = IconType.Url, DisplayName = "x", Path = input });
            Assert.True(result.Success, input);
            Assert.Equal(input, result.Icon!.SourcePath);
        }
    }

    [Fact]
    public void Create_网址空_失败()
    {
        var result = IconEditor.Create(new IconEditDraft { Type = IconType.Url, DisplayName = "x", Path = "  " });

        Assert.False(result.Success);
        Assert.Equal(IconEditor.ErrorUrlRequired, result.ErrorMessage);
    }

    [Fact]
    public void Create_命令_目标入target_source为命令加参数整串()
    {
        var result = IconEditor.Create(new IconEditDraft
        {
            Type = IconType.Command,
            DisplayName = "备份脚本",
            Path = "python",
            Arguments = "--verbose backup.py",
            WorkingDir = @"C:\Scripts",
        });

        Assert.True(result.Success);
        var icon = result.Icon!;
        Assert.Equal("python", icon.TargetPath);
        Assert.Equal("python --verbose backup.py", icon.SourcePath);
        Assert.Equal("--verbose backup.py", icon.Arguments);
        Assert.Equal(@"C:\Scripts", icon.WorkingDir);
        Assert.Equal(IconRefreshKind.StandardFallback, result.Refresh.Kind);
    }

    [Fact]
    public void Create_命令空_失败()
    {
        var result = IconEditor.Create(new IconEditDraft { Type = IconType.Command, DisplayName = "x" });

        Assert.False(result.Success);
        Assert.Equal(IconEditor.ErrorCommandRequired, result.ErrorMessage);
    }

    // ────────────────────────────── 编辑：文件 / 文件夹 ──────────────────────────────

    [Fact]
    public void Edit_文件_只改名_字段不变_不用刷新图标()
    {
        var icon = new IconModel { Type = IconType.File, DisplayName = "旧名", SourcePath = @"C:\a.exe", TargetPath = @"C:\a.exe" };

        var result = IconEditor.Edit(icon, new IconEditDraft { DisplayName = "新名" });

        Assert.True(result.Success);
        Assert.Equal("新名", icon.DisplayName);
        Assert.Equal(@"C:\a.exe", icon.SourcePath);
        Assert.Equal(@"C:\a.exe", icon.TargetPath);
        Assert.Equal(IconRefreshKind.None, result.Refresh.Kind);
    }

    [Fact]
    public void Edit_文件_改了路径_重写两字段并重新提取()
    {
        var icon = new IconModel { Type = IconType.File, DisplayName = "x", SourcePath = @"C:\old.exe", TargetPath = @"C:\old.exe" };

        var result = IconEditor.Edit(icon, new IconEditDraft { DisplayName = "x", Path = @"D:\new.exe" });

        Assert.True(result.Success);
        Assert.Equal(@"D:\new.exe", icon.SourcePath);
        Assert.Equal(@"D:\new.exe", icon.TargetPath);
        Assert.Equal(IconRefreshKind.ReextractFromSource, result.Refresh.Kind);
        Assert.Equal(@"D:\new.exe", result.Refresh.SourcePath);
    }

    [Fact]
    public void Edit_文件_路径框清空_保持原样_不刷新()
    {
        var icon = new IconModel { Type = IconType.File, DisplayName = "x", SourcePath = @"C:\a.exe", TargetPath = @"C:\a.exe" };

        var result = IconEditor.Edit(icon, new IconEditDraft { DisplayName = "x", Path = "  " });

        Assert.True(result.Success);
        Assert.Equal(@"C:\a.exe", icon.TargetPath);
        Assert.Equal(IconRefreshKind.None, result.Refresh.Kind);
    }

    // ────────────────────────────── 编辑：快捷方式 ──────────────────────────────

    [Fact]
    public void Edit_快捷方式_改目标参数工作目录描述()
    {
        var icon = new IconModel
        {
            Type = IconType.Shortcut,
            DisplayName = "x",
            SourcePath = @"C:\a.lnk",
            TargetPath = @"C:\a.exe",
            Arguments = "-old",
            WorkingDir = @"C:\Old",
            Description = "旧描述",
        };

        var result = IconEditor.Edit(icon, new IconEditDraft
        {
            DisplayName = "新名",
            Path = @"D:\b.exe",
            Arguments = "-new",
            WorkingDir = @"D:\Wd",
            Description = "新描述",
        });

        Assert.True(result.Success);
        Assert.Equal(@"D:\b.exe", icon.TargetPath);
        Assert.Equal(@"C:\a.lnk", icon.SourcePath);          // .lnk 来源不因编辑而变（与原版一致）
        Assert.Equal("-new", icon.Arguments);
        Assert.Equal(@"D:\Wd", icon.WorkingDir);
        Assert.Equal("新描述", icon.Description);
        // 没给自定义图标 → 编辑快捷方式总是重新提取（按目标路径）
        Assert.Equal(IconRefreshKind.ReextractFromSource, result.Refresh.Kind);
        Assert.Equal(@"D:\b.exe", result.Refresh.SourcePath);
    }

    [Fact]
    public void Edit_快捷方式_自定义图标_用文件加索引()
    {
        var icon = new IconModel { Type = IconType.Shortcut, DisplayName = "x", TargetPath = @"C:\a.exe" };

        var result = IconEditor.Edit(icon, new IconEditDraft
        {
            DisplayName = "x",
            CustomIconPath = @"C:\Windows\System32\shell32.dll",
            CustomIconIndex = 3,
        });

        Assert.True(result.Success);
        Assert.Equal(IconRefreshKind.CustomIndex, result.Refresh.Kind);
        Assert.Equal(3, result.Refresh.IconIndex);
    }

    [Fact]
    public void Edit_快捷方式_目标留空_保持原值()
    {
        var icon = new IconModel { Type = IconType.Shortcut, DisplayName = "x", SourcePath = @"C:\a.lnk", TargetPath = @"C:\a.exe" };

        var result = IconEditor.Edit(icon, new IconEditDraft { DisplayName = "x" });

        Assert.True(result.Success);
        Assert.Equal(@"C:\a.exe", icon.TargetPath);
        Assert.Equal(@"C:\a.lnk", icon.SourcePath);
        Assert.Equal(IconRefreshKind.ReextractFromSource, result.Refresh.Kind);
        Assert.Equal(@"C:\a.exe", result.Refresh.SourcePath);   // target 优先
    }

    // ────────────────────────────── 编辑：网址 / 命令 ──────────────────────────────

    [Fact]
    public void Edit_网址_留空保持原值_非空则归一化_永不刷新()
    {
        var icon = new IconModel { Type = IconType.Url, DisplayName = "x", SourcePath = "https://old.com", TargetPath = "https://old.com" };

        var keep = IconEditor.Edit(icon, new IconEditDraft { DisplayName = "x" });
        Assert.True(keep.Success);
        Assert.Equal("https://old.com", icon.SourcePath);
        Assert.Equal(IconRefreshKind.None, keep.Refresh.Kind);

        var change = IconEditor.Edit(icon, new IconEditDraft { DisplayName = "x", Path = "new-site.cn" });
        Assert.True(change.Success);
        Assert.Equal("https://new-site.cn", icon.SourcePath);
        Assert.Equal("https://new-site.cn", icon.TargetPath);
        Assert.Equal(IconRefreshKind.None, change.Refresh.Kind);
    }

    [Fact]
    public void Edit_命令_命令留空_保持target与source_但参数照常更新()
    {
        var icon = new IconModel { Type = IconType.Command, DisplayName = "x", SourcePath = "python a.py", TargetPath = "python", Arguments = "a.py" };

        var result = IconEditor.Edit(icon, new IconEditDraft { DisplayName = "x", Arguments = "b.py --fast" });

        Assert.True(result.Success);
        Assert.Equal("python", icon.TargetPath);
        Assert.Equal("python a.py", icon.SourcePath);            // 命令没改 → 不重建 source
        Assert.Equal("b.py --fast", icon.Arguments);
        Assert.Equal(IconRefreshKind.None, result.Refresh.Kind);
    }

    [Fact]
    public void Edit_命令_改了命令_重建source串()
    {
        var icon = new IconModel { Type = IconType.Command, DisplayName = "x", SourcePath = "python a.py", TargetPath = "python", Arguments = "a.py" };

        var result = IconEditor.Edit(icon, new IconEditDraft { DisplayName = "x", Path = "pwsh", Arguments = "-NoProfile" });

        Assert.True(result.Success);
        Assert.Equal("pwsh", icon.TargetPath);
        Assert.Equal("pwsh -NoProfile", icon.SourcePath);
        Assert.Equal(IconRefreshKind.None, result.Refresh.Kind);
    }

    [Fact]
    public void Edit_名称空_失败_模型不被改动()
    {
        var icon = new IconModel { Type = IconType.File, DisplayName = "旧名", SourcePath = @"C:\a.exe", TargetPath = @"C:\a.exe" };

        var result = IconEditor.Edit(icon, new IconEditDraft { DisplayName = "   " });

        Assert.False(result.Success);
        Assert.Equal(IconEditor.ErrorNameRequired, result.ErrorMessage);
        Assert.Equal("旧名", icon.DisplayName);
        Assert.Equal(@"C:\a.exe", icon.TargetPath);
    }

    [Fact]
    public void Edit_类型以模型为准_草稿类型被忽略()
    {
        // 编辑对话框不给改类型：即使草稿里写 File，也必须按模型上的 Url 走 URL 分支
        var icon = new IconModel { Type = IconType.Url, DisplayName = "x", SourcePath = "https://a.com", TargetPath = "https://a.com" };

        var result = IconEditor.Edit(icon, new IconEditDraft { Type = IconType.File, DisplayName = "x", Path = "site.example" });

        Assert.True(result.Success);
        Assert.Equal("https://site.example", icon.SourcePath);   // 走的是 URL 归一化
        Assert.Equal(IconRefreshKind.None, result.Refresh.Kind);
    }

    // ────────────────────────────── NormalizeUrl ──────────────────────────────

    [Fact]
    public void NormalizeUrl_三种协议保留_大小写不敏感()
    {
        foreach (var input in new[] { "http://a", "https://a", "ftp://a", "HTTP://a", "HTTPS://A" })
        {
            Assert.Equal(input, IconEditor.NormalizeUrl(input));
        }
    }

    [Fact]
    public void NormalizeUrl_没写协议补https_空串原样返回()
    {
        Assert.Equal("https://example.com", IconEditor.NormalizeUrl("example.com"));
        Assert.Equal("https://example.com", IconEditor.NormalizeUrl("  example.com  "));
        Assert.Equal(string.Empty, IconEditor.NormalizeUrl("   "));
        Assert.Equal(string.Empty, IconEditor.NormalizeUrl(null));
    }

    // ────────────────────────────── 落库：DataStore.UpdateIcon ──────────────────────────────

    [Fact]
    public void UpdateIcon_覆盖字段_保留id与sort_order_并落盘()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var tab = store.AddTab("A");

        var icon = new IconModel { Type = IconType.Url, DisplayName = "旧", SourcePath = "https://a.com", TargetPath = "https://a.com" };
        store.AddIcon(tab.Id, icon);
        var id = icon.Id;
        var sortOrder = icon.SortOrder;

        var updated = new IconModel
        {
            Id = id,
            Type = IconType.Url,
            DisplayName = "新",
            SourcePath = "https://b.com",
            TargetPath = "https://b.com",
            IconCacheFile = "abc.png",
        };

        Assert.True(store.UpdateIcon(updated));

        var reloaded = temp.NewStore().Load().Single(t => t.Id == tab.Id).Icons.Single();
        Assert.Equal("新", reloaded.DisplayName);
        Assert.Equal("https://b.com", reloaded.SourcePath);
        Assert.Equal("abc.png", reloaded.IconCacheFile);
        Assert.Equal(id, reloaded.Id);
        Assert.Equal(sortOrder, reloaded.SortOrder);
    }

    [Fact]
    public void UpdateIcon_找不到_返回false且不落盘()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        store.AddTab("A");

        Assert.False(store.UpdateIcon(new IconModel { Id = "no-such-id", DisplayName = "x" }));
    }

    // ────────────────────────────── 非变异校验（对话框点「确定」时用）──────────────────────────────

    [Fact]
    public void ValidateCreate_只回答能不能存_不动草稿()
    {
        var draft = new IconEditDraft { Type = IconType.Url, DisplayName = "x", Path = "" };

        Assert.Equal(IconEditor.ErrorUrlRequired, IconEditor.ValidateCreate(draft));
        Assert.Equal("x", draft.DisplayName);   // 草稿一个字段都没被改
        Assert.Equal(string.Empty, draft.Path);

        draft.Path = "a.com";
        Assert.Null(IconEditor.ValidateCreate(draft));
    }

    [Fact]
    public void ValidateCreate_五类型的主字段消息各不相同()
    {
        Assert.Equal(IconEditor.ErrorNameRequired,
            IconEditor.ValidateCreate(new IconEditDraft { Type = IconType.File }));
        Assert.Equal(IconEditor.ErrorPathRequired,
            IconEditor.ValidateCreate(new IconEditDraft { Type = IconType.File, DisplayName = "x" }));
        Assert.Equal(IconEditor.ErrorPathRequired,
            IconEditor.ValidateCreate(new IconEditDraft { Type = IconType.Folder, DisplayName = "x" }));
        Assert.Equal(IconEditor.ErrorPathRequired,
            IconEditor.ValidateCreate(new IconEditDraft { Type = IconType.Shortcut, DisplayName = "x" }));
        Assert.Equal(IconEditor.ErrorUrlRequired,
            IconEditor.ValidateCreate(new IconEditDraft { Type = IconType.Url, DisplayName = "x" }));
        Assert.Equal(IconEditor.ErrorCommandRequired,
            IconEditor.ValidateCreate(new IconEditDraft { Type = IconType.Command, DisplayName = "x" }));
    }

    [Fact]
    public void ValidateEdit_只要求名称_主字段可空()
    {
        var icon = new IconModel { Type = IconType.File, DisplayName = "x", TargetPath = @"C:\a.exe" };

        Assert.Equal(IconEditor.ErrorNameRequired,
            IconEditor.ValidateEdit(icon, new IconEditDraft { DisplayName = "   " }));
        Assert.Null(IconEditor.ValidateEdit(icon, new IconEditDraft { DisplayName = "新名" }));
        Assert.Null(IconEditor.ValidateEdit(icon, new IconEditDraft { DisplayName = "新名", Path = string.Empty }));
    }

    [Fact]
    public void Create_经AddIcon落库_重载后字段完整()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var tab = store.AddTab("A");

        var result = IconEditor.Create(new IconEditDraft
        {
            Type = IconType.Command,
            DisplayName = "ping",
            Path = "ping.exe",
            Arguments = "-t 8.8.8.8",
            WorkingDir = @"C:\Windows\System32",
        });
        Assert.True(result.Success);
        store.AddIcon(tab.Id, result.Icon!);

        var reloaded = temp.NewStore().Load().Single(t => t.Id == tab.Id).Icons.Single();
        Assert.Equal("ping", reloaded.DisplayName);
        Assert.Equal("ping.exe", reloaded.TargetPath);
        Assert.Equal("ping.exe -t 8.8.8.8", reloaded.SourcePath);
        Assert.Equal("-t 8.8.8.8", reloaded.Arguments);
    }
}
