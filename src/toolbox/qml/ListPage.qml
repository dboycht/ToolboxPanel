import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

/* 列表页：两列（说明 / 路径），对应原版 ListTabPage
   - 行交替底色 + 悬停高亮 + 选中态（颜色全走令牌）
   - 单击打开（原版行为）、双击编辑说明
   - 拖拽排序留到 P3（原版是自定义 QDrag，QML 侧改用 DragHandler + DropArea） */
Item {
    id: page

    function openRow(id) {
        app.openIcon(id)
    }

    ColumnLayout {
        anchors.fill: parent
        spacing: 6

        // 表头
        Rectangle {
            Layout.fillWidth: true
            Layout.preferredHeight: 32
            radius: 9
            color: Qt.rgba(theme.c.alt_base.r, theme.c.alt_base.g,
                           theme.c.alt_base.b, 0.72)
            border.width: 1
            border.color: theme.c.border_soft

            RowLayout {
                anchors.fill: parent
                anchors.leftMargin: 14
                anchors.rightMargin: 14
                spacing: 10
                Text {
                    Layout.preferredWidth: 240
                    text: tr.t("list.col.desc")
                    color: theme.c.text_dim
                    font.pixelSize: 11
                    font.weight: Font.DemiBold
                }
                Text {
                    Layout.fillWidth: true
                    text: tr.t("list.col.path")
                    color: theme.c.text_dim
                    font.pixelSize: 11
                    font.weight: Font.DemiBold
                }
            }
        }

        Text {
            Layout.fillWidth: true
            Layout.topMargin: 24
            visible: app.icons.count === 0
            horizontalAlignment: Text.AlignHCenter
            text: app.searchQuery !== "" ? tr.t("search.no_result")
                                         : tr.t("list.empty_hint")
            color: theme.c.text_hint
            font.pixelSize: 13
            wrapMode: Text.WordWrap
        }

        ListView {
            id: list
            Layout.fillWidth: true
            Layout.fillHeight: true
            model: app.icons
            clip: true
            spacing: 4
            boundsBehavior: Flickable.StopAtBounds

            delegate: Rectangle {
                id: row
                width: list.width
                height: 42
                radius: 9

                color: mouse.pressed
                    ? Qt.rgba(theme.c.accent.r, theme.c.accent.g, theme.c.accent.b, 0.22)
                    : (mouse.containsMouse
                        ? Qt.rgba(theme.c.accent.r, theme.c.accent.g, theme.c.accent.b, 0.12)
                        : (index % 2 === 0
                            ? Qt.rgba(theme.c.card.r, theme.c.card.g, theme.c.card.b,
                                      theme.n.card_opacity * 0.7)
                            : "transparent"))
                Behavior on color {
                    ColorAnimation { duration: Math.round(theme.n.hover_ms) }
                }
                border.width: 1
                border.color: mouse.containsMouse ? Qt.rgba(theme.c.accent.r, theme.c.accent.g,
                                                            theme.c.accent.b, 0.30)
                                                  : "transparent"

                RowLayout {
                    anchors.fill: parent
                    anchors.leftMargin: 14
                    anchors.rightMargin: 14
                    spacing: 10

                    Text {
                        Layout.preferredWidth: 240
                        text: iconDesc
                        color: theme.c.text
                        font.pixelSize: 12
                        elide: Text.ElideRight
                    }
                    Text {
                        Layout.fillWidth: true
                        text: iconTarget
                        color: iconMissing ? theme.c.danger : theme.c.text_dim
                        font.pixelSize: 12
                        elide: Text.ElideMiddle
                    }
                }

                MouseArea {
                    id: mouse
                    anchors.fill: parent
                    hoverEnabled: true
                    acceptedButtons: Qt.LeftButton | Qt.RightButton
                    cursorShape: Qt.PointingHandCursor
                    onClicked: function (m) {
                        if (m.button === Qt.RightButton) {
                            rowMenu.itemId = iconId
                            rowMenu.itemName = iconDesc
                            rowMenu.popup()
                        } else {
                            page.openRow(iconId)
                        }
                    }
                    onDoubleClicked: editDlg.openFor(iconId, iconDesc)
                }
            }
        }
    }

    Menu {
        id: rowMenu
        property string itemId: ""
        property string itemName: ""

        MenuItem {
            text: tr.t("list.edit_item")
            onTriggered: editDlg.openFor(rowMenu.itemId, rowMenu.itemName)
        }
        MenuItem {
            text: tr.t("icon.menu.open")
            onTriggered: app.openIcon(rowMenu.itemId)
        }
        MenuSeparator { }
        MenuItem {
            text: tr.t("icon.menu.remove")
            onTriggered: removeDlg.openMessage(
                tr.t("icon.remove.title"),
                tr.tf("icon.remove.confirm", { "name": rowMenu.itemName }),
                rowMenu.itemId)
        }
    }

    EditListItemDialog { id: editDlg }
    ConfirmDialog {
        id: removeDlg
        onConfirmed: function (payload) { app.removeIcon(String(payload)) }
    }
}
