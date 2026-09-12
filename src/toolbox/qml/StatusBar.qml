import QtQuick
import QtQuick.Controls

/* 底部状态栏：呼吸状态点 + 状态文本 + 设置入口
   状态文本直接绑 app.status（Python 侧 setStatus 推动，与原版 statusBar 语义一致）。 */
Item {
    id: bar

    Row {
        anchors.left: parent.left
        anchors.leftMargin: 20
        anchors.verticalCenter: parent.verticalCenter
        spacing: 8

        Rectangle {
            anchors.verticalCenter: parent.verticalCenter
            width: 7; height: 7; radius: 4
            color: theme.c.success
            SequentialAnimation on opacity {
                loops: Animation.Infinite
                NumberAnimation { from: 1.0; to: 0.30
                                  duration: 1200; easing.type: Easing.InOutSine }
                NumberAnimation { from: 0.30; to: 1.0
                                  duration: 1200; easing.type: Easing.InOutSine }
            }
        }

        Text {
            anchors.verticalCenter: parent.verticalCenter
            text: app.status
            color: theme.c.text_dim
            font.pixelSize: 11
            elide: Text.ElideRight
            width: Math.min(implicitWidth, bar.width - 260)
        }
    }

    Row {
        anchors.right: parent.right
        anchors.rightMargin: 18
        anchors.verticalCenter: parent.verticalCenter
        spacing: 8

        Text {
            anchors.verticalCenter: parent.verticalCenter
            text: win.backdropActive ? "acrylic" : "no-blur"
            color: theme.c.text_hint
            font.pixelSize: 10
        }

        Rectangle {
            width: 1; height: 14
            anchors.verticalCenter: parent.verticalCenter
            color: theme.c.border
        }

        Text {
            anchors.verticalCenter: parent.verticalCenter
            text: app.language === "zh" ? "中文" : "EN"
            color: theme.c.text_dim
            font.pixelSize: 11
        }
    }
}
