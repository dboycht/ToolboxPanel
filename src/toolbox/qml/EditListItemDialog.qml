import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

/* 编辑列表项：说明 + 路径（对应原版 list.edit_item 对话框） */
Dialog {
    id: dlg
    property string itemId: ""

    function openFor(id, desc) {
        dlg.itemId = id
        descField.text = desc || ""
        pathField.text = ""
        dlg.open()
        descField.forceActiveFocus()
    }

    anchors.centerIn: Overlay.overlay
    modal: true
    width: 460
    padding: 20

    background: Rectangle {
        radius: 14
        color: theme.c.base
        border.width: 1
        border.color: theme.c.border
    }

    header: Text {
        text: tr.t("list.edit_item")
        color: theme.c.text
        font.pixelSize: 15
        font.weight: Font.DemiBold
        padding: 18
    }

    contentItem: ColumnLayout {
        spacing: 10
        Text { text: tr.t("edit.field.desc"); color: theme.c.text_dim; font.pixelSize: 12 }
        TextField {
            id: descField
            Layout.fillWidth: true
            color: theme.c.text
            placeholderTextColor: theme.c.text_hint
            selectByMouse: true
            font.pixelSize: 13
            background: Rectangle {
                radius: 8
                color: Qt.rgba(theme.c.alt_base.r, theme.c.alt_base.g,
                               theme.c.alt_base.b, 0.9)
                border.width: 1
                border.color: descField.activeFocus ? theme.c.accent : theme.c.border
            }
        }
        Text { text: tr.t("edit.field.path"); color: theme.c.text_dim; font.pixelSize: 12 }
        TextField {
            id: pathField
            Layout.fillWidth: true
            color: theme.c.text
            placeholderTextColor: theme.c.text_hint
            selectByMouse: true
            font.pixelSize: 13
            background: Rectangle {
                radius: 8
                color: Qt.rgba(theme.c.alt_base.r, theme.c.alt_base.g,
                               theme.c.alt_base.b, 0.9)
                border.width: 1
                border.color: pathField.activeFocus ? theme.c.accent : theme.c.border
            }
        }
    }

    footer: RowLayout {
        spacing: 10
        Item { Layout.fillWidth: true }
        DialogButton { text: tr.t("btn.cancel"); onClicked: dlg.reject() }
        DialogButton { text: tr.t("btn.ok"); primary: true; onClicked: dlg.accept() }
    }

    onAccepted: app.renameListItem(dlg.itemId, descField.text.trim())
}
