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
    color: "transparent"
    flags: Qt.Window | Qt.FramelessWindowHint
    title: app.appTitle + " · v" + appVersion

    // 最大化时去掉圆角
    readonly property bool maxed: win.maximized
    readonly property real shellRadius: maxed ? 0 : theme.n.radius
    readonly property int animMs: Math.round(theme.n.anim_ms)

    // 毛玻璃未生效时（老系统 / 系统关闭了透明效果）提高底色不透明度兜底
    readonly property real shellOpacity:
        win.backdropActive ? theme.n.window_opacity
                           : Math.min(1.0, theme.n.window_opacity + 0.12)

    // ── 外壳：圆角 + 半透明玻璃 ─────────────────────────────────────────
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
