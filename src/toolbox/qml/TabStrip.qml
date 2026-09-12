import QtQuick
import QtQuick.Controls

/* 标签栏：横向可滚动 + 选中弹簧指示器 + 双击重命名 + 右键菜单

   ⚠️ 定位纪律（ERROR.md E6）：delegate 内部不要再写 ListView 管理的定位属性；
   选中指示器放在 ListView 之外、用 currentIndex 计算 x，避免与布局抢属性。 */
Item {
    id: strip

    property int delegateW: 108
    property int spacing: 4

    ListView {
        id: list
        anchors.fill: parent
        orientation: ListView.Horizontal
        model: app.tabs
        clip: true
        spacing: strip.spacing
        boundsBehavior: Flickable.StopAtBounds
        currentIndex: app.currentIndex

        delegate: Item {
            width: strip.delegateW
            height: list.height

            Rectangle {
                id: hoverLayer
                anchors.fill: parent
                anchors.margins: 2
                radius: 9
                color: hover.hovered && index !== list.currentIndex
                       ? Qt.rgba(theme.c.text.r, theme.c.text.g, theme.c.text.b, 0.08)
                       : "transparent"
                Behavior on color {
                    ColorAnimation { duration: Math.round(theme.n.hover_ms) }
                }
            }

            Column {
                anchors.centerIn: parent
                spacing: 0
                width: parent.width - 16

                Text {
                    width: parent.width
                    horizontalAlignment: Text.AlignHCenter
                    elide: Text.ElideRight
                    text: tabName
                    color: index === list.currentIndex ? theme.c.text : theme.c.text_dim
                    font.pixelSize: 12
                    font.weight: index === list.currentIndex ? Font.DemiBold : Font.Normal
                    Behavior on color {
                        ColorAnimation { duration: Math.round(theme.n.hover_ms) }
                    }
                }
                Text {
                    width: parent.width
                    horizontalAlignment: Text.AlignHCenter
                    text: itemCount
                    color: theme.c.text_hint
                    font.pixelSize: 9
                }
            }

            HoverHandler { id: hover; cursorShape: Qt.PointingHandCursor }

            TapHandler {
                acceptedButtons: Qt.LeftButton
                onTapped: app.selectTab(index)
            }
            TapHandler {
                acceptedButtons: Qt.LeftButton
                onDoubleTapped: renameDlg.openFor(index, tabName)
            }
            TapHandler {
                acceptedButtons: Qt.RightButton
                onTapped: {
                    list.currentIndex = index
                    tabMenu.tabIndex = index
                    tabMenu.tabTitle = tabName
                    tabMenu.popup()
                }
            }
        }
    }

    // ── 选中指示器（弹簧滑动）──────────────────────────────────────────
    Rectangle {
        id: indicator
        z: -1
        height: list.height - 6
        y: 3
        width: strip.delegateW - 4
        x: app.currentIndex * (strip.delegateW + strip.spacing) - list.contentX + 2
        radius: 10
        color: Qt.rgba(theme.c.accent.r, theme.c.accent.g, theme.c.accent.b, 0.20)
        border.width: 1
        border.color: Qt.rgba(theme.c.accent.r, theme.c.accent.g, theme.c.accent.b, 0.38)

        Behavior on x {
            NumberAnimation {
                duration: Math.round(theme.n.anim_ms) + 180
                easing.type: Easing.OutBack
                easing.overshoot: 1.25
            }
        }
        Behavior on width {
            NumberAnimation { duration: Math.round(theme.n.anim_ms) }
        }
    }

    // ── 标签页右键菜单（与原版 tab.menu.* 一致）──────────────────────
    Menu {
        id: tabMenu
        property int tabIndex: 0
        property string tabTitle: ""

        MenuItem {
            text: tr.t("tab.menu.new")
            onTriggered: app.addTab()
        }
        MenuItem {
            text: tr.t("list.new_tab")
            onTriggered: app.addListTab()
        }
        MenuSeparator { }
        MenuItem {
            text: tr.t("tab.menu.rename")
            onTriggered: renameDlg.openFor(tabMenu.tabIndex, tabMenu.tabTitle)
        }
        MenuItem {
            text: tr.t("tab.menu.delete")
            enabled: app.canRemoveTab
            onTriggered: deleteDlg.openFor(tabMenu.tabIndex, tabMenu.tabTitle)
        }
    }

    RenameTabDialog { id: renameDlg }
    ConfirmDialog {
        id: deleteDlg
        onConfirmed: function (payload) { app.removeTab(payload) }
    }
}
