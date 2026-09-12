import QtQuick
import QtQuick.Controls

/* 标题栏：应用标记 + 标签栏 + 窗口按钮
   ⚠️ 两条纪律（都在 ERROR.md 里有记录）：
   1. **拖动区只覆盖左侧标题那一块**，绝不整条都做拖动区 ——
      否则按下按钮也会立刻进入系统拖动模态循环，QML 收不到 pointerUp，
      按钮 onTapped 永远不触发（E3）。
   2. 放进 `Row`/`Column` 的子项**不能**再用 left/right/fill/verticalCenter
      锚点（QML 会报错且布局失效，E6 同类）。需要用锚点时，外层用 `Item` 定位。 */
Item {
    id: bar
    height: 54

    // ── 左侧：可拖动区（应用标记 + 标题）──────────────────────────────
    Item {
        id: dragArea
        anchors.left: parent.left
        anchors.leftMargin: 20
        anchors.verticalCenter: parent.verticalCenter
        width: dragRow.width
        height: 32

        Row {
            id: dragRow
            anchors.verticalCenter: parent.verticalCenter
            spacing: 11

            Rectangle {
                id: mark
                width: 30; height: 30
                radius: 9
                gradient: Gradient {
                    GradientStop { position: 0.0; color: theme.c.accent }
                    GradientStop { position: 1.0; color: theme.c.glow_2 }
                }
                Text {
                    anchors.centerIn: parent
                    text: "T"
                    color: theme.c.on_accent
                    font.pixelSize: 16
                    font.weight: Font.Bold
                }
            }

            Column {
                anchors.verticalCenter: parent.verticalCenter
                spacing: 0
                Text {
                    text: app.appTitle
                    color: theme.c.text
                    font.pixelSize: 14
                    font.weight: Font.DemiBold
                }
                Text {
                    text: "v" + appVersion
                    color: theme.c.text_hint
                    font.pixelSize: 10
                }
            }
        }

        // 拖动触发：盖在整块拖动区之上
        MouseArea {
            anchors.fill: parent
            acceptedButtons: Qt.LeftButton
            cursorShape: Qt.SizeAllCursor
            onPressed: win.startDrag()
        }
    }

    // ── 右侧：搜索 / 设置 / 窗口按钮（**不**与拖动区重叠）──────────────
    Row {
        id: tools
        anchors.right: parent.right
        anchors.rightMargin: 14
        anchors.verticalCenter: parent.verticalCenter
        spacing: 6

        IconButton {
            glyph: "⌕"
            tip: "Ctrl+F"
            onInvoked: app.toggleSearch()
        }
        IconButton {
            glyph: "⚙"
            tip: "Ctrl+,"
            onInvoked: settingsSheet.open()
        }

        Item { width: 4; height: 1 }

        IconButton {
            glyph: "—"
            onInvoked: win.minimize()
        }
        IconButton {
            glyph: win.maximized ? "❐" : "▢"
            onInvoked: win.toggleMaximize()
        }
        IconButton {
            glyph: "✕"
            danger: true
            onInvoked: win.close()
        }
    }

    // ── 中间：标签栏（放在最后定义，避免与 tools 的宽度互相依赖成环）──
    TabStrip {
        id: tabStrip
        anchors.left: dragArea.right
        anchors.leftMargin: 18
        anchors.right: tools.left
        anchors.rightMargin: 12
        anchors.verticalCenter: parent.verticalCenter
        height: 34
    }
}
