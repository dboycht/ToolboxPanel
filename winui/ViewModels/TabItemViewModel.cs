// TabItemViewModel.cs —— 标签栏一项的展示模型
//
// 只负责「把 Core 的模型翻译成界面要显示的东西」：
//   · TabModel  → TabItemViewModel（标签栏标签 + 该页的图标/列表项集合）
//   · IconModel → IconTileViewModel（网格图块：图标图 + 名称）
//   · ListItemModel → ListRowViewModel（列表行：说明 + 路径）

using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.ViewModels;

/// <summary>一个标签页（标签栏一项 + 它自己的内容集合）。</summary>
public sealed class TabItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _showCount = true;

    public TabItemViewModel(TabModel model)
    {
        Model = model;
        Id = model.Id;
        Name = string.IsNullOrEmpty(model.Name) ? "(未命名标签页)" : model.Name;
        IsList = model.IsListTab;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TabModel Model { get; }

    public string Id { get; }

    public string Name { get; }

    public bool IsList { get; }

    /// <summary>
    /// 拖拽用：这一页收哪一类东西。
    /// 界面靠它判断"这次拖放我接不接受"（网格页只收图标、列表页只收列表项）。
    /// </summary>
    public DragItemKind DraggableKind => IsList ? DragItemKind.ListItem : DragItemKind.Icon;

    /// <summary>是否选中（标签栏用它显示底部强调条）。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value, nameof(IsSelected));
    }

    /// <summary>是否显示数量文字（设置项；由标签栏统一设置）。</summary>
    public bool ShowCount
    {
        get => _showCount;
        set => SetField(ref _showCount, value, nameof(ShowCount));
    }

    /// <summary>
    /// 标签类型字形。
    /// ⚠️ 选字形**必须实际渲染出来看**：`\uE71D`（名字叫 AllApps）实际画出来是「缩略图列表」，
    /// 跟 `\uE8FD`(List) 几乎分不出来（用户实测反馈"两种标签图标一样"）。
    /// 现在改用 `\uE80A`（密集方格 = 网格页）与 `\uE8FD`（项目符号列表 = 列表页），区分明显。
    /// </summary>
    public string Glyph => IsList ? "\uE8FD" : "\uE80A";

    public ObservableCollection<IconTileViewModel> Icons { get; } = new();

    public ObservableCollection<ListRowViewModel> ListItems { get; } = new();

    /// <summary>标签栏上的数量文字（如「20 个图标」）。</summary>
    public string CountLabel => IsList ? $"{ListItems.Count} 项" : $"{Icons.Count} 个图标";

    /// <summary>数量变了（拖拽搬走/搬来图标）之后刷新标签栏上的数量文字。</summary>
    public void NotifyCountLabel() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountLabel)));

    // ────────────────────────────── 视图 ↔ Core 顺序同步（拖拽排序用）──────────────────────────────

    /// <summary>
    /// 按 Core 里图标的顺序重排界面的 <see cref="Icons"/>。
    ///
    /// <para>为什么要"照 Core 重排"而不是"界面自己挪一格"：
    /// **界面集合的顺序必须与 Core 完全一致** —— 下次启动时界面是照 Core 的顺序重建的，
    /// 只要有一次不一致，用户就会看到"重启后顺序又变了"。
    /// 把 Core 当唯一事实源、界面跟着它走，这个类问题就不存在了。</para>
    ///
    /// <para>⚠️ 复用已有的 <see cref="IconTileViewModel"/> 实例（连同已经提取好的图标位图），
    /// 而不是重新构造 —— 重新构造会丢掉图片源、并触发重复的图标提取。</para>
    /// </summary>
    public void SyncIconsFromModel()
    {
        var existing = Icons.ToDictionary(tile => tile.Model.Id, tile => tile);
        var ordered = new List<IconTileViewModel>(Model.Icons.Count);

        foreach (var icon in Model.Icons)
        {
            if (existing.TryGetValue(icon.Id, out var tile))
            {
                ordered.Add(tile);
            }
        }

        // Core 里新出现、界面还没建的图标（理论上不会有）补建，绝不静默丢项
        foreach (var icon in Model.Icons)
        {
            if (!existing.ContainsKey(icon.Id))
            {
                ordered.Add(new IconTileViewModel(icon, null));
            }
        }

        ReorderObservable(Icons, ordered);
        NotifyCountLabel();
    }

    /// <summary>同上，列表页版本。</summary>
    public void SyncListItemsFromModel()
    {
        var existing = ListItems.ToDictionary(row => row.Model.Id, row => row);
        var ordered = new List<ListRowViewModel>(Model.ListItems.Count);

        foreach (var item in Model.ListItems)
        {
            if (existing.TryGetValue(item.Id, out var row))
            {
                ordered.Add(row);
            }
        }

        ReorderObservable(ListItems, ordered);
        NotifyCountLabel();
    }

    /// <summary>
    /// 用最小改动把 <paramref name="target"/> 调成 <paramref name="ordered"/> 的顺序。
    /// 走 <see cref="ObservableCollection{T}.Move"/> 而不是 Clear+Add：
    /// 后者会让 GridView 丢掉容器、把入场动画/滚动位置一起重置（观感上是"整页闪一下"）。
    /// </summary>
    private static void ReorderObservable<T>(ObservableCollection<T> target, IReadOnlyList<T> ordered)
    {
        for (int i = 0; i < ordered.Count; i++)
        {
            int currentIndex = target.IndexOf(ordered[i]);
            if (currentIndex < 0)
            {
                target.Insert(i, ordered[i]);
            }
            else if (currentIndex != i)
            {
                target.Move(currentIndex, i);
            }
        }
    }

    private void SetField<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>网格页里的一个图块。编辑属性/改名后由 <see cref="Refresh"/> 更新显示。</summary>
public sealed class IconTileViewModel : INotifyPropertyChanged
{
    private string _displayName;
    private ImageSource? _iconSource;

    public IconTileViewModel(IconModel model, ImageSource? iconSource)
    {
        Model = model;
        _displayName = ResolveName(model);
        _iconSource = iconSource;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IconModel Model { get; }

    public string DisplayName
    {
        get => _displayName;
        private set => SetField(ref _displayName, value, nameof(DisplayName));
    }

    /// <summary>缓存图标的图片源；拿不到时为 null，模板会退回显示字形图标。</summary>
    public ImageSource? IconSource
    {
        get => _iconSource;
        private set
        {
            if (ReferenceEquals(_iconSource, value))
            {
                return;
            }

            _iconSource = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconSource)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIcon)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoIcon)));
        }
    }

    public bool HasIcon => IconSource is not null;

    public bool HasNoIcon => IconSource is null;

    /// <summary>图标类型对应的字形（与 Core 的兜底图标语义一致，仅作显示兜底）。</summary>
    public string Glyph => Model.Type switch
    {
        IconType.Folder => "\uE8B7",
        IconType.Shortcut => "\uE71B",
        IconType.Url => "\uE774",
        IconType.Command => "\uE756",
        _ => "\uE8A5",
    };

    public string TypeLabel => Model.Type switch
    {
        IconType.Folder => "文件夹",
        IconType.Shortcut => "快捷方式",
        IconType.Url => "网址",
        IconType.Command => "命令",
        _ => "文件",
    };

    /// <summary>
    /// 编辑属性 / 改图标之后刷新图块的显示（名称可能随路径变化，图标可能换了新缓存）。
    /// </summary>
    public void Refresh(ImageSource? iconSource)
    {
        IconSource = iconSource;
        DisplayName = ResolveName(Model);
    }

    /// <summary>只改了名字（重命名）时刷新 —— 不动图标图片源，省一次无谓的图片重设。</summary>
    public void RefreshName() => DisplayName = ResolveName(Model);

    /// <summary>没写名字时用路径/命令的第一段兜底（原版也是这么显示的）。</summary>
    private static string ResolveName(IconModel model)
    {
        if (!string.IsNullOrWhiteSpace(model.DisplayName))
        {
            return model.DisplayName;
        }

        var source = !string.IsNullOrWhiteSpace(model.SourcePath) ? model.SourcePath : model.TargetPath;
        if (string.IsNullOrWhiteSpace(source))
        {
            return "(未命名)";
        }

        try
        {
            var name = Path.GetFileName(source.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(name) ? source : name;
        }
        catch (ArgumentException)
        {
            return source;
        }
    }

    private void SetField<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>列表页里的一行（第 1 列说明，第 2 列路径）。</summary>
public sealed class ListRowViewModel
{
    public ListRowViewModel(ListItemModel model)
    {
        Model = model;
        Description = model.Description;
        Path = model.Path;
    }

    public ListItemModel Model { get; }

    public string Description { get; }

    public string Path { get; }
}
