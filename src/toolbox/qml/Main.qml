import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import QtQuick.Window

/* ============================================================================
   ToolboxPanel v2 · 主窗口
   无边框 + 逐像素半透明 + 圆角 + 系统毛玻璃（acrylic 由 Python 侧落到 HWND）

   主题一律取 `theme.c.*`（颜色）/ `theme.n.*`（数字参数），不硬编码颜色 ——
   这是「主题高度自定义」能生效的前提。
   ============================================================================ */

Window {
    id: root
    width: 1000
    height: 680
    minimumWidth: 640
    minimumHeight: 460
    visible: true
    // ⚠️ 半透明三件套，缺一不可（踩过，见 ERROR.md E8）：
    //   1) color: 透明
    //   2) Qt.FramelessWindowHint（Qt 因此**不会**把窗口标记为不透明）
    //   3) Python 侧去掉 WS_CAPTION|WS_THICKFRAME（见 window.py _strip_frame）
    // 只要窗口被当成不透明，DWM acrylic 与圆角镂空**都不会生效**，
    // 表现就是"毛玻璃没透明感、圆角外也不透桌面"。
    color: "transparent"
    flags: Qt.Window | Qt.FramelessWindowHint
    title: app.appTitle + " · v" + appVersion

    // 最大化时去掉圆角
    readonly property bool maxed: win.maximized
    readonly property real shellRadius: maxed ? 0 : theme.n.radius
    readonly property int animMs: Math.round(theme.n.anim_ms)

    // 毛玻璃未生效时（老系统 / 系统关闭了透明效果 / 虚拟显示适配器禁用 acrylic）
    // 才提高不透明度兜底 —— 但上限只到 0.88，留一点通透感；
    // 注意：**生效时不要额外加不透明度**，否则会把模糊盖住（E9）。
    readonly property real shellOpacity:
        win.backdropActive ? theme.n.window_opacity
                           : Math.min(0.88, theme.n.window_opacity + 0.30)

    // ── 外壳：圆角 + **淡色调**（关键：不能盖深色渐变）──────────────────
    // ⚠️ 实测教训（ERROR.md E9）：这里原先铺的是深色渐变
    //   #6E1A1D2B → #C4101219（60%~88% 不透明），结果把 DWM acrylic
    //   **整个糊住**，看起来就是一块实心深色板 —— 用户反馈"根本没有透明效果"。
    //   实测对照：同一窗口左半用深色渐变 ΔRGB=0（完全不透），
    //             右半用 alpha 0.34 的淡色调 ΔRGB=581（明显透出桌面/模糊）。
    //   结论：**玻璃感靠"极淡色调 + DWM 模糊"，不靠深色底**。
    //   要加深就把设置里的「窗口不透明度」调高，但超过约 0.6 就会盖住模糊。
    Rectangle {
        id: shell
        anchors.fill: parent
        radius: root.shellRadius
        color: Qt.rgba(theme.c.window.r, theme.c.window.g,
                       theme.c.window.b, root.shellOpacity)
        border.width: 1
        border.color: theme.c.border

        Behavior on radius {
            NumberAnimation { duration: 160; easing.type: Easing.OutCubic }
        }

        // 背景光斑：让「玻璃」有内容可透（也便于肉眼确认 blur 是否生效）
        Item {
            anchors.fill: parent
            clip: true
            opacity: win.backdropActive ? 0.9 : 0.6
            z: 0

            Rectangle {
                id: glowA
                width: 460; height: 460; radius: width / 2
                color: theme.c.glow_1
                x: -110; y: -140
                SequentialAnimation on x {
                    loops: Animation.Infinite
                    NumberAnimation { from: -110; to: 180
                                      duration: 11000; easing.type: Easing.InOutSine }
                    NumberAnimation { from: 180; to: -110
                                      duration: 11000; easing.type: Easing.InOutSine }
                }
            }
            Rectangle {
                id: glowB
                width: 380; height: 380; radius: width / 2
                color: theme.c.glow_2
                // 注意：不能写 parent.height —— SequentialAnimation 会改变绑定
                // 上下文，parent 会指向动画自身（见 ERROR.md E6 同类坑）
                property real baseY: shell.height - 300
                x: shell.width - 300
                y: baseY
                SequentialAnimation on y {
                    loops: Animation.Infinite
                    NumberAnimation { from: glowB.baseY; to: glowB.baseY - 90
                                      duration: 13000; easing.type: Easing.InOutSine }
                    NumberAnimation { from: glowB.baseY - 90; to: glowB.baseY
                                      duration: 13000; easing.type: Easing.InOutSine }
                }
            }
        }

        ColumnLayout {
            anchors.fill: parent
            spacing: 0
            z: 1

            TitleBar {
                Layout.fillWidth: true
                Layout.preferredHeight: 54
            }

            StackLayout {
                id: contentStack
                Layout.fillWidth: true
                Layout.fillHeight: true
                Layout.leftMargin: 14
                Layout.rightMargin: 14
                Layout.bottomMargin: 4
                currentIndex: app.currentTabType === "list" ? 1 : 0

                IconGridPage { }
                ListPage { }
            }

            StatusBar {
                Layout.fillWidth: true
                Layout.preferredHeight: 38
            }
        }
    }

    // ── 设置面板（主题自定义入口）────────────────────────────────────────
    SettingsSheet {
        id: settingsSheet
        parent: root.contentItem
    }

    // ── 全局快捷键（与原版一致）──────────────────────────────────────────
    Shortcut { sequences: ["Ctrl+T"];       onActivated: app.addTab() }
    Shortcut { sequences: ["Ctrl+Shift+T"]; onActivated: app.addListTab() }
    Shortcut { sequences: ["Ctrl+Comma"];   onActivated: settingsSheet.open() }
    Shortcut {
        sequences: ["Ctrl+W"]
        onActivated: app.removeTab(app.currentIndex)
    }
    Shortcut {
        sequences: ["Escape"]
        onActivated: if (settingsSheet.opened) settingsSheet.close()
    }
}
