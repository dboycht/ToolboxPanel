// IAnimationHost.cs —— 让"动效总开关"能统一切到各个页面
//
// 页面各自持有 GridView/ListView，过渡（入场交错、重排）挂在它们的 ItemContainerTransitions 上。
// 设置里关掉动效时，主窗口通过这个接口让所有已创建的页面把过渡清掉 / 装回去。

namespace ToolboxPanel.Views;

public interface IAnimationHost
{
    void SetAnimationsEnabled(bool enabled);
}
