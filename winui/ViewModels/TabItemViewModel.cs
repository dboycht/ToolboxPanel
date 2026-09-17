// TabItemViewModel.cs —— 标签栏一项的展示模型
//
// 只负责「把 Core 的模型翻译成界面要显示的东西」：
//   · TabModel  → TabItemViewModel（标签栏标签 + 该页的图标/列表项集合）
//   · IconModel → IconTileViewModel（网格图块：图标图 + 名称）
//   · ListItemModel → ListRowViewModel（列表行：说明 + 路径）

using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;
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

        // 集合一变（新建 / 删除 / 拖拽重排 / 导入后重建）可见视图跟着重算 ——
        // 少了这一条就会出现"过滤着新建了一个图标，界面却什么都不动"。
        Icons.CollectionChanged += (_, _) => RebuildVisible();
        ListItems.CollectionChanged += (_, _) => RebuildVisible();
        RebuildVisible();
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

    // ────────────────────────────── 搜索过滤（W5）──────────────────────────────
    //
    // **界面绑的是下面这两个可见集合**（过滤后仍是 Core 顺序的一个子集），不是 Icons / ListItems 本身。
    //
    // 为什么不用"在模板里把不匹配的项 Visibility 隐藏"：GridView/ListView 照样给隐藏项留一格
    // （ItemsWrapGrid 按集合项数排），观感是"图标之间一片空档"；而"把不匹配的从集合里删掉"
    // 又会让界面顺序与 Core 顺序脱钩（拖拽落库、重启后的顺序全靠这两者一致）。
    // 所以：**Icons / ListItems 永远是完整顺序（与 Core 对齐），另存一份过滤后的可见视图**。
    //
    // 判定与"可见位 → Core 下标"的换算都在 Core 的 `SearchFilter`（有单测）；这里只负责搬运。

    /// <summary>过滤后仍然显示的图块（网格页的 ItemsSource）。</summary>
    public ObservableCollection<IconTileViewModel> VisibleIcons { get; } = new();

    /// <summary>过滤后仍然显示的行（列表页的 ItemsSource）。</summary>
    public ObservableCollection<ListRowViewModel> VisibleListItems { get; } = new();

    /// <summary>当前查询（空 / 全空白 = 不过滤）。</summary>
    public string Filter { get; private set; } = string.Empty;

    public bool IsFilterActive => SearchFilter.IsActive(Filter);

    /// <summary>可见项数量（搜索栏上的「匹配 N / M」）。</summary>
    public int VisibleCount => IsList ? VisibleListItems.Count : VisibleIcons.Count;

    /// <summary>这一页真实持有的项数（与可见数无关）。</summary>
    public int TotalCount => IsList ? ListItems.Count : Icons.Count;

    /// <summary>套用查询（幂等：同一查询重复下发什么都不做）。</summary>
    public void SetFilter(string? query)
    {
        var next = query?.Trim() ?? string.Empty;
        if (string.Equals(next, Filter, StringComparison.Ordinal))
        {
            return;
        }

        Filter = next;

        // 过滤会让一部分项消失；勾选留着的话，清空过滤后会冒出"莫名其妙被勾上"的项
        foreach (var tile in Icons)
        {
            tile.IsChecked = false;
        }

        RebuildVisible();
    }

    /// <summary>
    /// 模型字段被改过（重命名 / 改路径 / 编辑属性）之后重新判定一次 —— 命中与否可能变了
    /// （集合本身没动，所以订阅 CollectionChanged 收不到这种变化）。
    /// </summary>
    public void ReapplyFilter() => RebuildVisible();

    /// <summary>按当前查询重算两个可见集合（不做过滤时 = 全量，与完整集合逐项一致）。</summary>
    private void RebuildVisible()
    {
        SyncVisible(Icons, VisibleIcons, tile => SearchFilter.Matches(tile.Model, Filter));
        SyncVisible(ListItems, VisibleListItems, row => SearchFilter.Matches(row.Model, Filter));
    }

    /// <summary>
    /// 把 <paramref name="target"/> 调成"按当前过滤条件筛出来的 source 顺序"。
    /// 先剔除不该出现的（保持剩余项的相对顺序），再补齐/重排 —— 尽量走 Move/Insert，
    /// 避免 Clear+Add 让 GridView 丢掉容器（那会把入场动画、滚动位置一起重置）。
    /// </summary>
    private void SyncVisible<T>(IReadOnlyList<T> source, ObservableCollection<T> target, Func<T, bool> matches)
        where T : class
    {
        var desired = IsFilterActive
            ? source.Where(matches).ToList()
            : source.ToList();

        var wanted = new HashSet<T>(desired);
        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        ReorderObservable(target, desired);
    }

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
    private IconSizeMetrics _size = IconSizeMetrics.Medium;

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

    // ────────────────────────────── 图标大小三档（设置：小/中/大）──────────────────────────────
    //
    // 尺寸表在 Core（`IconSizeMetrics`，可单测；medium 与"已定版密度"逐值一致）。
    // 模板里 x:Bind Mode=OneWay 绑下面这几个 ⇒ 换档时**就地刷新**，不用重建图块、也不动图标位图缓存。

    /// <summary>当前档位（默认 medium；由页面在创建时与设置变化时统一下发）。</summary>
    public IconSizeMetrics Size
    {
        get => _size;
        private set
        {
            if (_size == value)
            {
                return;
            }

            _size = value;

            foreach (var name in new[]
                     {
                         nameof(TileWidth), nameof(TileHeight), nameof(IconPixels),
                         nameof(GlyphPixels), nameof(FontSize), nameof(LineHeight),
                     })
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
    }

    /// <summary>套用一档尺寸（页面调用；重复套用同一档不会触发通知）。</summary>
    public void ApplySize(IconSizeMetrics metrics) => Size = metrics;

    public double TileWidth => _size.TileWidth;

    public double TileHeight => _size.TileHeight;

    public double IconPixels => _size.IconPixels;

    public double GlyphPixels => _size.GlyphPixels;

    public double FontSize => _size.FontSize;

    public double LineHeight => _size.LineHeight;

    // ────────────────────────────── 批量管理（勾选）──────────────────────────────
    //
    // 批量模式下每个图块显示一个勾选框（模板里 IsHitTestVisible=False，只作指示）；
    // "当前在不在批量模式"与"勾没勾上"都由页面统一下发/收集（勾选清单 = 各图块 IsChecked）。

    private bool _isBulkMode;
    private bool _isChecked;

    /// <summary>是否处于批量管理模式（控制勾选框显隐）。</summary>
    public bool IsBulkMode
    {
        get => _isBulkMode;
        private set
        {
            if (_isBulkMode == value)
            {
                return;
            }

            _isBulkMode = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBulkMode)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BulkCheckVisibility)));
        }
    }

    /// <summary>勾选框的可见性（直接给 x:Bind 用；省掉一个 BoolToVisibility 转换器）。</summary>
    public Visibility BulkCheckVisibility => _isBulkMode ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>批量模式下是否被勾选。</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
            {
                return;
            }

            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    /// <summary>切换批量模式（退出时**清掉勾选**，与原版 set_batch_mode(False) 一致）。</summary>
    public void SetBulkMode(bool on)
    {
        IsBulkMode = on;

        if (!on)
        {
            IsChecked = false;
        }
    }

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
public sealed class ListRowViewModel : INotifyPropertyChanged
{
    public ListRowViewModel(ListItemModel model)
    {
        Model = model;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ListItemModel Model { get; }

    // ⚠️ 直接**透读模型**（而不是构造时拷一份）：编辑属性/重命名之后就地把模型改了，
    //    这里再 Refresh() 一通知，模板（x:Bind Mode=OneWay）立刻刷新 —— 不用重建整行、不丢滚动位置。
    public string Description => Model.Description;

    public string Path => Model.Path;

    /// <summary>模型字段被改过之后刷新显示。</summary>
    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Path)));
    }
}
