// PythonFactAttribute.cs —— "需要本机真实 Python"的 xUnit 特性（[PythonFact] / [PythonTheory]）
//
// 为什么要有它：这类"用真实原版 Python 做对照"的测试以前在探测不到 python 时是 `return;`
// ——报告里显示 **passed**，是**假绿**。现在统一走这里，探测不到就设 `Skip`，
// 报告里会明确显示"已跳过"（xUnit 2.9 没有 `Assert.Skip`，**在特性构造函数里设 Skip** 是可行做法）。
//
// ⚠️ 关键语义（都实现在 PythonRunner 里）：
//   · 非开发副本（canonical / CI / 仓外跑测试）⇒ `Available == false` ⇒ **显式跳过**；
//   · 开发副本（有 src\toolbox\i18n.py）里探测不到 python ⇒ `Available` **抛异常** ⇒ 报告里是**失败**，
//     不是跳过（开发副本里这是环境坏了，必须显式失败）。

namespace ToolboxPanel.Core.Tests;

/// <summary>需要本机 python 的 <c>[Fact]</c>：探测不到 ⇒ 显式跳过（开发副本里则显式失败）。</summary>
/// <param name="requiresModule">额外要求的 python 模块（如 <c>"PyQt6"</c> / <c>"win32com.client"</c>）；null = 只要求 python 本身。</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PythonFactAttribute : FactAttribute
{
    public PythonFactAttribute()
        : this(requiresModule: null)
    {
    }

    public PythonFactAttribute(string? requiresModule)
    {
        if (!PythonRunner.AvailableWith(requiresModule))
        {
            Skip = "需要本机 python（见 DEVELOPMENT.md §环境探测结果）";
        }
    }
}

/// <summary>需要本机 python 的 <c>[Theory]</c>：语义同 <see cref="PythonFactAttribute"/>。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PythonTheoryAttribute : TheoryAttribute
{
    public PythonTheoryAttribute()
        : this(requiresModule: null)
    {
    }

    public PythonTheoryAttribute(string? requiresModule)
    {
        if (!PythonRunner.AvailableWith(requiresModule))
        {
            Skip = "需要本机 python（见 DEVELOPMENT.md §环境探测结果）";
        }
    }
}
