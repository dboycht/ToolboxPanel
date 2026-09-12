import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

/* 重命名图标对话框（对应原版 icon.menu.rename / 内联改名） */
Dialog {
    id: dlg
    property string iconId: ""

    function openFor(id, currentName) {
        dlg.iconId = id
        nameField.text = currentName || ""
        dlg.open()
        nameField.forceActiveFocus()
        nameField.selectAll()
    }

    anchors.centerIn: Overlay.overlay
    modal: true
    width: 380
    padding: 20

    background: Rectangle {
        radius: 14
        color: theme.c.base
        border.width: 1
        border.color: theme.c.border
    }

    header: Text {
        text: tr.t("icon.menu.rename")
        color: theme.c.text
        font.pixelSize: 15
        font.weight: Font.DemiBold
        padding: 18
    }

    contentItem: ColumnLayout {
        spacing: 10
        Text {
            text: tr.t("edit.field.name")
            color: theme.c.text_dim
            font.pixelSize: 12
        }
        TextField {
            id: nameField
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
                border.color: nameField.activeFocus ? theme.c.accent : theme.c.border
                Behavior on border.color {
                    ColorAnimation { duration: Math.round(theme.n.hover_ms) }
                }
            }
            onAccepted: dlg.accept()
        }
    }

    footer: RowLayout {
        spacing: 10
        Item { Layout.fillWidth: true }
        DialogButton {
            text: tr.t("btn.cancel")
            onClicked: dlg.reject()
        }
        DialogButton {
            text: tr.t("btn.ok")
            primary: true
            onClicked: dlg.accept()
        }
    }

    onAccepted: {
        var v = nameField.text.trim()
        if (v.length > 0) app.renameIcon(dlg.iconId, v)
        else app.showStatus(tr.t("validate.name_required"))
    }
}
