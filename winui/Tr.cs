// Tr.cs —— XAML 静态文案的本地化（附加属性 + 统一重刷）
//
// 为什么不用"给每个控件加 x:Name、再在代码里逐个赋值"：
//   设置面板 + 各对话框 + 两条提示加起来有上百处静态文字，那样会产生同样数量的名字与赋值语句，
//   而且是"加一处忘一处"的高发区。这里换一种更省事也更不容易漏的做法：
//
//     XAML 里只多一个属性：   <TextBlock ui:Tr.Key="settings.section.theme" />
//     运行期由本类写进 Text / Content / Header / PlaceholderText；
//     语言一换，调用一次 `Tr.RefreshAll()` 全部重刷。
//
// 元素类型 → 写到哪个属性，映射在 `ApplyKey` 里（表驱动，加控件类型只改一处）。
// 文案本身一律来自 Core 的 `I18n`（key 命名与原版 i18n.py 一致，有保真测试）。
//
// ⚠️ 用**弱引用**登记元素：对话框会被反复创建/销毁，强引用会把它们留在内存里。

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolboxPanel.Core;

namespace ToolboxPanel;

/// <summary>XAML 静态文案的本地化附加属性：<c>ui:Tr.Key="key"</c> / <c>ui:Tr.Tip="key"</c>。</summary>
public static class Tr
{
    private static readonly List<WeakReference<DependencyObject>> Tracked = new();

    // ────────────────────────────── 附加属性 ──────────────────────────────

    /// <summary>文案 key（写到 Text / Content / PlaceholderText 等"元素主文案"，按元素类型决定）。</summary>
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(Tr), new PropertyMetadata(null, OnKeyChanged));

    /// <summary>表单"表头"文案 key（TextBox / ComboBox / RadioButtons / ToggleSwitch / Slider / Expander 的 Header）。</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.RegisterAttached(
        "Header", typeof(string), typeof(Tr), new PropertyMetadata(null, OnHeaderChanged));

    /// <summary>提示气泡（ToolTip）的文案 key。</summary>
    public static readonly DependencyProperty TipProperty = DependencyProperty.RegisterAttached(
        "Tip", typeof(string), typeof(Tr), new PropertyMetadata(null, OnTipChanged));

    public static string? GetKey(DependencyObject element) => (string?)element.GetValue(KeyProperty);

    public static void SetKey(DependencyObject element, string? value) => element.SetValue(KeyProperty, value);

    public static string? GetHeader(DependencyObject element) => (string?)element.GetValue(HeaderProperty);

    public static void SetHeader(DependencyObject element, string? value) => element.SetValue(HeaderProperty, value);

    public static string? GetTip(DependencyObject element) => (string?)element.GetValue(TipProperty);

    public static void SetTip(DependencyObject element, string? value) => element.SetValue(TipProperty, value);

    private static void OnKeyChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is not string { Length: > 0 } key)
        {
            return;
        }

        Track(element);
        ApplyKey(element, key);
    }

    private static void OnHeaderChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is not string { Length: > 0 } key)
        {
            return;
        }

        Track(element);
        ApplyHeader(element, key);
    }

    private static void OnTipChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is not string { Length: > 0 } key)
        {
            return;
        }

        Track(element);
        ApplyTip(element, key);
    }

    // ────────────────────────────── 统一重刷 ──────────────────────────────

    /// <summary>语言切换后调用：把登记过的元素按当前语言重写一遍（顺便清理已销毁元素的弱引用）。</summary>
    public static void RefreshAll()
    {
        for (int i = Tracked.Count - 1; i >= 0; i--)
        {
            if (!Tracked[i].TryGetTarget(out var element))
            {
                Tracked.RemoveAt(i);
                continue;
            }

            if (GetKey(element) is { Length: > 0 } key)
            {
                ApplyKey(element, key);
            }

            if (GetHeader(element) is { Length: > 0 } header)
            {
                ApplyHeader(element, header);
            }

            if (GetTip(element) is { Length: > 0 } tip)
            {
                ApplyTip(element, tip);
            }
        }
    }

    // ────────────────────────────── 具体怎么写 ──────────────────────────────

    private static void ApplyKey(DependencyObject element, string key)
    {
        var text = I18n.T(key);

        switch (element)
        {
            case TextBlock textBlock:
                textBlock.Text = text;
                break;
            case Button button:
                button.Content = text;
                break;
            case CheckBox checkBox:
                checkBox.Content = text;
                break;
            case RadioButton radioButton:
                radioButton.Content = text;
                break;
            case ComboBoxItem comboBoxItem:
                comboBoxItem.Content = text;
                break;
            case MenuFlyoutItem menuFlyoutItem:
                menuFlyoutItem.Text = text;
                break;
            case TextBox textBox:
                textBox.PlaceholderText = text;
                break;
            case ToolTip toolTip:
                toolTip.Content = text;
                break;
        }
    }

    /// <summary>表单表头（Header）：TextBox / 选择器 / 滑块 / 折叠面板。</summary>
    private static void ApplyHeader(DependencyObject element, string key)
    {
        var text = I18n.T(key);

        switch (element)
        {
            case TextBox textBox:
                textBox.Header = text;
                break;
            case Expander expander:
                expander.Header = text;
                break;
            case ToggleSwitch toggleSwitch:
                toggleSwitch.Header = text;
                break;
            case RadioButtons radioButtons:
                radioButtons.Header = text;
                break;
            case ComboBox comboBox:
                comboBox.Header = text;
                break;
            case Slider slider:
                slider.Header = text;
                break;
        }
    }

    private static void ApplyTip(DependencyObject element, string key)
    {
        if (element is FrameworkElement frameworkElement)
        {
            ToolTipService.SetToolTip(frameworkElement, I18n.T(key));
        }
    }

    private static void Track(DependencyObject element)
    {
        foreach (var reference in Tracked)
        {
            if (reference.TryGetTarget(out var existing) && ReferenceEquals(existing, element))
            {
                return;     // 已经登记过（同一个元素同时设了 Key 与 Tip 时会走两次）
            }
        }

        Tracked.Add(new WeakReference<DependencyObject>(element));
    }
}
