import QtQuick
import QtQuick.Controls

/* 通用图标按钮：悬停底色 + 按压回弹 + 可选危险色（关闭按钮用）
   颜色全部走主题令牌，不硬编码 —— 换主题即时生效。 */
Rectangle {
    id: btn
    property string glyph: ""
    property string tip: ""
    property bool danger: false
    signal invoked()

    width: 32
    height: 28
    radius: 8
    color: {
        if (mouse.pressed) return danger ? Qt.rgba(theme.c.danger.r, theme.c.danger.g,
                                                   theme.c.danger.b, 0.55)
                                         : Qt.rgba(theme.c.text.r, theme.c.text.g,
                                                   theme.c.text.b, 0.20)
        if (mouse.containsMouse) return danger ? Qt.rgba(theme.c.danger.r, theme.c.danger.g,
                                                         theme.c.danger.b, 0.38)
                                               : Qt.rgba(theme.c.text.r, theme.c.text.g,
                                                         theme.c.text.b, 0.12)
        return "transparent"
    }
    Behavior on color {
        ColorAnimation { duration: Math.round(theme.n.hover_ms) }
    }

    // 按压回弹（只动 scale，不影响布局定位 —— 见 ERROR.md E6）
    scale: mouse.pressed ? 0.92 : 1.0
    Behavior on scale {
        NumberAnimation {
            duration: Math.round(theme.n.anim_ms)
            easing.type: Easing.OutBack
            easing.overshoot: 2.0
        }
    }

    Text {
        anchors.centerIn: parent
        text: btn.glyph
        color: btn.danger && mouse.containsMouse ? theme.c.text : theme.c.text
        font.pixelSize: btn.glyph === "▢" || btn.glyph === "❐" ? 12 : 13
    }

    MouseArea {
        id: mouse
        anchors.fill: parent
        hoverEnabled: true
        cursorShape: Qt.PointingHandCursor
        onClicked: btn.invoked()
    }

    ToolTip.visible: tip !== "" && mouse.containsMouse
    ToolTip.text: tip
    ToolTip.delay: 700
}
