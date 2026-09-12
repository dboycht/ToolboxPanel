import QtQuick

/* 单选 chip（语言 / 图标大小等横向选择） */
Rectangle {
    id: chip
    property string label: ""
    property bool active: false
    signal invoked()

    implicitHeight: 34
    radius: 10
    color: active
        ? Qt.rgba(theme.c.accent.r, theme.c.accent.g, theme.c.accent.b, 0.26)
        : (mouse.containsMouse
            ? Qt.rgba(theme.c.text.r, theme.c.text.g, theme.c.text.b, 0.10)
            : Qt.rgba(theme.c.text.r, theme.c.text.g, theme.c.text.b, 0.05))
    border.width: 1
    border.color: active ? theme.c.accent : theme.c.border
    Behavior on color {
        ColorAnimation { duration: Math.round(theme.n.hover_ms) }
    }

    scale: mouse.pressed ? 0.95 : 1.0
    Behavior on scale {
        NumberAnimation {
            duration: Math.round(theme.n.anim_ms)
            easing.type: Easing.OutBack
            easing.overshoot: 2.2
        }
    }

    Text {
        anchors.centerIn: parent
        text: chip.label
        color: theme.c.text
        font.pixelSize: 12
        font.weight: chip.active ? Font.DemiBold : Font.Normal
    }

    MouseArea {
        id: mouse
        anchors.fill: parent
        hoverEnabled: true
        cursorShape: Qt.PointingHandCursor
        onClicked: chip.invoked()
    }
}
