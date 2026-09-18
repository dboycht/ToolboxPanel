// I18n.cs —— 双语（中文 / English）文案的**运行时入口**（W5 · v2.0.3）
//
// 原版基线：`src/toolbox/i18n.py`（`tr(key, **kwargs)` / `set_language(lang)` / `on_language_changed`）。
// 移植时的三条保真约定（都有测试钉住）：
//   ① **key 命名原样不动**（`app.menu.batch` / `bulk_delete.confirm` …），文案表见 `I18n.Table.cs`；
//   ② **未知 key 回落 `??key??`**（原版行为，方便一眼看出漏翻/漏配）；
//   ③ 占位符是**命名的**（`{name}` / `{count}` / `{version}`），缺参数时**原样保留占位符、不抛异常**
//      （原版 `text.format(**kwargs)` 遇 KeyError 就返回原文）。
//
// 与设置的关系：语言持久化在 `config.json` 的 `language`（`AppSettings.Language`，白名单 zh/en），
// **白名单只有这一份**（这里直接引用 `AppSettings.Languages`，避免两处各写一套）。
//
// 与界面的关系：Core 只负责"取当前语言的文案"；界面切换语言时**订阅 `LanguageChanged`**
// 或由宿主窗口统一调用各页面的 `ApplyLanguage()` 重刷（本项目走后者，和主题/图标档同一套做法）。
//
// ⚠️ 放在 **Core 根命名空间**（不是 `Core.Services`）：`Models` / `Storage` / `Services` 三层都会用到它
//    （模型的默认名、数据层的拖放失败原因、系统能力的错误文案、界面的每一处文字），
//    放在根命名空间就不会出现"子命名空间互相引用"的绕圈。

namespace ToolboxPanel.Core;

public static partial class I18n
{
    /// <summary>语言白名单（与 `config.json` 的 `language` 同一份来源）。</summary>
    public static readonly string[] Languages = ToolboxPanel.Core.Storage.AppSettings.Languages;

    /// <summary>默认语言（中文）。</summary>
    public const string DefaultLanguage = ToolboxPanel.Core.Storage.AppSettings.DefaultLanguage;

    private static string _language = DefaultLanguage;

    /// <summary>语言真的变了才会触发（同一个语言重复设置不触发）。</summary>
    public static event Action? LanguageChanged;

    /// <summary>当前语言（`zh` / `en`）。</summary>
    public static string Current => _language;

    public static bool IsChinese => !string.Equals(_language, "en", StringComparison.OrdinalIgnoreCase);

    /// <summary>把外部传进来的语言值收敛成白名单里的值（未知 / 空 ⇒ 默认语言）。</summary>
    public static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return DefaultLanguage;
        }

        foreach (var candidate in Languages)
        {
            if (string.Equals(candidate, language.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return DefaultLanguage;
    }

    /// <summary>切换语言（幂等；未知语言会被收敛成默认语言）。真的变了才发通知。</summary>
    public static void SetLanguage(string? language)
    {
        var next = Normalize(language);
        if (string.Equals(next, _language, StringComparison.Ordinal))
        {
            return;
        }

        _language = next;
        LanguageChanged?.Invoke();
    }

    /// <summary>取当前语言的文案；未知 key 返回 `??key??`（照原版）。</summary>
    public static string T(string key)
        => T(key, Array.Empty<(string Name, object? Value)>());

    /// <summary>
    /// 取文案并替换命名占位符，例如 `T("status.added", ("name", "记事本"))`。
    /// 参数缺失时**保留占位符**（不抛异常）—— 原版 `str.format` 遇 KeyError 就是返回原文。
    /// </summary>
    public static string T(string key, params (string Name, object? Value)[] args)
    {
        if (!Table.TryGetValue(key, out var entry))
        {
            return $"??{key}??";
        }

        // 原版：entry.get(当前语言) or entry.get("en", key) —— 空串要回落到英文
        var text = IsChinese ? entry.Zh : entry.En;
        if (string.IsNullOrEmpty(text))
        {
            text = entry.En;
        }

        return args.Length == 0 ? text : Substitute(text, args);
    }

    /// <summary>这张表里有没有这个 key（界面可以据此做"有则覆盖、无则保留默认"）。</summary>
    public static bool Has(string key) => Table.ContainsKey(key);

    /// <summary>全部 key（保真测试与排查用）。</summary>
    public static IReadOnlyCollection<string> Keys => Table.Keys;

    /// <summary>取某个 key 在指定语言下的原文（保真测试用；语言值非法时回落默认语言）。</summary>
    public static string Raw(string key, string language)
    {
        if (!Table.TryGetValue(key, out var entry))
        {
            return $"??{key}??";
        }

        return string.Equals(Normalize(language), "en", StringComparison.Ordinal) ? entry.En : entry.Zh;
    }

    /// <summary>把 `{name}` 这类命名占位符替换成实参（只替换**配得上名字**的那些）。</summary>
    private static string Substitute(string text, (string Name, object? Value)[] args)
    {
        if (!text.Contains('{'))
        {
            return text;
        }

        var builder = new System.Text.StringBuilder(text.Length + 16);
        int index = 0;

        while (index < text.Length)
        {
            var open = text.IndexOf('{', index);
            if (open < 0)
            {
                builder.Append(text, index, text.Length - index);
                break;
            }

            var close = text.IndexOf('}', open + 1);
            if (close < 0)
            {
                builder.Append(text, index, text.Length - index);
                break;
            }

            builder.Append(text, index, open - index);
            var name = text.Substring(open + 1, close - open - 1);

            var matched = false;
            foreach (var (argName, value) in args)
            {
                if (string.Equals(argName, name, StringComparison.Ordinal))
                {
                    builder.Append(value?.ToString() ?? string.Empty);
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                // 参数没给 ⇒ 占位符原样留着（不抛异常，照原版）
                builder.Append('{').Append(name).Append('}');
            }

            index = close + 1;
        }

        return builder.ToString();
    }
}
