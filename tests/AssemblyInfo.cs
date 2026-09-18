// AssemblyInfo.cs —— 测试装配级设置
//
// ⚠️ **关闭 xUnit 的并行执行**（默认是按测试类并行）。
// 理由：`I18n.Current` 是与原版 `i18n.py` 的模块级 `_current_lang` 同义的**进程内全局状态**，
// 而大量既有测试断言的是中文文案（"已添加: xxx" / "批量管理" …）。若语言切换测试与它们并行跑，
// 就会出现"偶发红"——那种红最难查，也最容易让人误以为是自己改坏了。
// 代价可以忽略：全部 380+ 项测试本来也只要几秒。

using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
