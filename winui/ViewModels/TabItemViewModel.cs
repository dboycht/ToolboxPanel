// TabItemViewModel.cs —— W3 主界面的视图模型
//
// 只负责「把 Core 的模型翻译成界面要显示的东西」：
//   · TabModel  → TabItemViewModel（标题栏标签 + 该页的图标/列表项集合）
//   · IconModel → IconTileViewModel（网格图块：图标图 + 名称）
//   · ListItemModel → ListRowViewModel（列表行：说明 + 路径）
//
// ⚠️ 视图模型不反向依赖 UI 之外的逻辑：数据来自 ToolboxPanel.Core，UI 只读它。

using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ToolboxPanel.Core.Models;

namespace ToolboxPanel.ViewModels;

/// <summary>一个标签页（标签栏一项 + 它自己的内容集合）。</summary>
public sealed class TabItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public TabItemViewModel(TabModel model)
    {
        Model = model;
        Id = model.Id;
        Name = string.IsNullOrEmpty(model.Name) ? "(未命名标签页)" : model.Name;
        IsList = model.IsListTab;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>是否选中（标签栏用它显示底部强调条；由窗口在 SelectionChanged 里维护）。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public TabModel Model { get; }

    public string Id { get; }

    public string Name { get; }

    public bool IsList { get; }

    /// <summary>标签栏图标字形：网格页 / 列表页（Segoe Fluent Icons）。</summary>
    public string Glyph => IsList ? "\uE8FD" : "\uE71D";

    public string Kind => Model.TabType;

    public ObservableCollection<IconTileViewModel> Icons { get; } = new();

    public ObservableCollection<ListRowViewModel> ListItems { get; } = new();

    public string Summary => IsList
        ? $"{ListItems.Count} 项"
        : $"{Icons.Count} 个图标";
}

/// <summary>网格页里的一个图块。</summary>
public sealed class IconTileViewModel
{
    public IconTileViewModel(IconModel model, ImageSource? iconSource)
    {
        Model = model;
        DisplayName = string.IsNullOrWhiteSpace(model.DisplayName)
            ? FallbackName(model)
            : model.DisplayName;
        IconSource = iconSource;
    }

    public IconModel Model { get; }

    public string DisplayName { get; }

    /// <summary>缓存图标的图片源；拿不到时为 null，模板会退回显示字形图标。</summary>
    public ImageSource? IconSource { get; }

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

    /// <summary>没写名字时用路径/命令的第一段兜底（原版也是这么显示的）。</summary>
    private static string FallbackName(IconModel model)
    {
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
