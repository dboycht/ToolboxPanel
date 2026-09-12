import QtQuick
import QtQuick.Controls

/* 主题化按钮（对话框底部用）。primary 为强调色实心。 */
Rectangle {
    id: btn
    property string text: ""
    property bool primary: false
    signal clicked()

    implicitWidth: Math.max(78, label.implicitWidth + 30)
    implicitHeight: 32
    radius: 9

    color: {
        if (primary) {
            if (mouse.pressed) return theme.c.accent_pressed
            if (mouse.containsMouse) return theme.c.accent_hover
            return theme.c.accent
        }
        var a = mouse.pressed ? 0.22 : (mouse.containsMouse ? 0.14 : 0.08)
        return Qt.rgba(theme.c.text.r, theme.c.text.g, theme.c.text.b, a)
    }
    border.width: 1
    border.color: primary
        ? Qt.rgba(theme.c.accent.r, theme.c.accent.g, theme.c.accent.b, 0.7)
        : theme.c.border

    Behavior on color {
        ColorAnimation { duration: Math.round(theme.n.hover_ms) }
    }

    scale: mouse.pressed ? 0.96 : 1.0
    Behavior on scale {
        NumberAnimation {
            duration: Math.round(theme.n.anim_ms)
            easing.type: Easing.OutBack
            easing.overshoot: 2.0
        }
    }

    Text {
        id: label
        anchors.centerIn: parent
        text: btn.text
        color: btn.primary ? theme.c.on_accent : theme.c.text
        font.pixelSize: 12
    }

    MouseArea {
        id: mouse
        anchors.fill: parent
        hoverEnabled: true
        cursorShape: Qt.PointingHandCursor
        onClicked: btn.clicked()
    }
}
