import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

/* 通用确认对话框（删除标签页 / 删除图标等复用）

   payload 用 `var`：标签页用 int 下标，图标/列表项用 string id。 */
Dialog {
    id: dlg
    property string titleText: ""
    property string message: ""
    property var payload: null
    signal confirmed(var payload)

    function openFor(index, name) {
        dlg.titleText = tr.t("tab.delete.title")
        dlg.message = tr.tf("tab.delete.confirm", { "name": name || "" })
        dlg.payload = index
        dlg.open()
    }

    function openMessage(t, msg, token) {
        dlg.titleText = t
        dlg.message = msg
        dlg.payload = token
        dlg.open()
    }

    anchors.centerIn: Overlay.overlay
    modal: true
    width: 400
    padding: 20

    background: Rectangle {
        radius: 14
        color: theme.c.base
        border.width: 1
        border.color: theme.c.border
    }

    header: Text {
        text: dlg.titleText
        color: theme.c.text
        font.pixelSize: 15
        font.weight: Font.DemiBold
        padding: 18
        width: dlg.width
        wrapMode: Text.WordWrap
    }

    contentItem: Text {
        text: dlg.message
        color: theme.c.text_dim
        font.pixelSize: 12
        wrapMode: Text.WordWrap
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

    onAccepted: dlg.confirmed(dlg.payload)
}
