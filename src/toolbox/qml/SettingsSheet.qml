import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import QtQuick.Effects

/* 设置面板（右侧抽屉）：主题自定义入口
   - 预置主题：theme.presetNames / presetLabels（Python 侧 Theme 暴露）
   - 参数调节：theme.paramSpecs 自动生成滑杆（无需在 QML 里硬编码参数列表）
   - 语言 / 图标大小：与原版 config.json 字段一致
   颜色全部取令牌，抽屉本身用 MultiEffect 做应用内毛玻璃。 */
Item {
    id: sheet
    anchors.fill: parent
    z: 100

    property bool opened: false

    function open() { opened = true }
    function close() { opened = false }

    // 遮罩：点击空白关闭
    Rectangle {
        anchors.fill: parent
        color: Qt.rgba(0, 0, 0, 0.35)
        opacity: sheet.opened ? 1.0 : 0.0
        visible: opacity > 0.01
        Behavior on opacity {
            NumberAnimation { duration: Math.round(theme.n.anim_ms) }
        }
        MouseArea {
            anchors.fill: parent
            onClicked: sheet.close()
        }
    }

    // 抽屉本体
    Rectangle {
        id: panel
        width: 380
        height: parent.height
        anchors.right: parent.right
        x: sheet.opened ? 0 : width
        radius: 0

        color: Qt.rgba(theme.c.base.r, theme.c.base.g, theme.c.base.b, 0.97)
        border.width: 1
        border.color: theme.c.border

        Behavior on x {
            NumberAnimation {
                duration: Math.round(theme.n.anim_ms) + 220
                easing.type: Easing.OutBack
                easing.overshoot: 1.12
            }
        }

        ColumnLayout {
            anchors.fill: parent
            anchors.margins: 0
            spacing: 0

            // ── 标题 ─────────────────────────────────────────────────
            Item {
                Layout.fillWidth: true
                Layout.preferredHeight: 54

                Text {
                    anchors.left: parent.left
                    anchors.leftMargin: 22
                    anchors.verticalCenter: parent.verticalCenter
                    text: tr.t("settings.title")
                    color: theme.c.text
                    font.pixelSize: 16
                    font.weight: Font.DemiBold
                }
                IconButton {
                    anchors.right: parent.right
                    anchors.rightMargin: 14
                    anchors.verticalCenter: parent.verticalCenter
                    glyph: "✕"
                    onInvoked: sheet.close()
                }
            }

            Rectangle { Layout.fillWidth: true; height: 1; color: theme.c.border_soft }

            // ── 可滚动内容 ───────────────────────────────────────────
            Flickable {
                Layout.fillWidth: true
                Layout.fillHeight: true
                contentHeight: content.implicitHeight + 40
                clip: true
                boundsBehavior: Flickable.StopAtBounds

                ColumnLayout {
                    id: content
                    width: parent.width - 40
                    x: 20
                    y: 18
                    spacing: 16

                    // ── 预置主题 ────────────────────────────────────
                    SectionLabel { text: tr.t("settings.preset") }

                    GridLayout {
                        Layout.fillWidth: true
                        columns: 2
                        columnSpacing: 8
                        rowSpacing: 8

                        Repeater {
                            model: theme.presetNames
                            delegate: Rectangle {
                                Layout.fillWidth: true
                                Layout.preferredHeight: 40
                                radius: 10
                                property bool active: theme.preset === modelData
                                color: active
                                    ? Qt.rgba(theme.c.accent.r, theme.c.accent.g,
                                              theme.c.accent.b, 0.26)
                                    : Qt.rgba(theme.c.text.r, theme.c.text.g,
                                              theme.c.text.b, 0.06)
                                border.width: 1
                                border.color: active ? theme.c.accent : theme.c.border
                                Behavior on color {
                                    ColorAnimation { duration: Math.round(theme.n.hover_ms) }
                                }
                                Text {
                                    anchors.centerIn: parent
                                    // 与 theme.presetNames 同序取标签
                                    text: theme.presetLabels[index]
                                    color: theme.c.text
                                    font.pixelSize: 12
                                    font.weight: parent.active ? Font.DemiBold : Font.Normal
                                }
                                MouseArea {
                                    anchors.fill: parent
                                    hoverEnabled: true
                                    cursorShape: Qt.PointingHandCursor
                                    onClicked: theme.setPreset(modelData)
                                }
                            }
                        }
                    }

                    // ── 参数调节（自动按 paramSpecs 生成）────────────
                    SectionLabel { text: tr.t("settings.tuning") }

                    Repeater {
                        model: theme.paramSpecs
                        delegate: ColumnLayout {
                            Layout.fillWidth: true
                            spacing: 4

                            RowLayout {
                                Layout.fillWidth: true
                                Text {
                                    Layout.fillWidth: true
                                    text: modelData.label
                                    color: theme.c.text_dim
                                    font.pixelSize: 12
                                }
                                Text {
                                    text: {
                                        var v = modelData.value
                                        var s = modelData.step
                                        return (s >= 1) ? Math.round(v).toString()
                                                        : v.toFixed(2)
                                    }
                                    color: theme.c.text_hint
                                    font.pixelSize: 11
                                }
                            }

                            Slider {
                                id: slider
                                Layout.fillWidth: true
                                from: modelData.min
                                to: modelData.max
                                stepSize: modelData.step
                                value: modelData.value
                                onMoved: theme.setParam(modelData.key, value)

                                background: Rectangle {
                                    x: slider.leftPadding
                                    y: slider.topPadding + slider.availableHeight / 2 - 2
                                    width: slider.availableWidth
                                    height: 4
                                    radius: 2
                                    color: Qt.rgba(theme.c.text.r, theme.c.text.g,
                                                   theme.c.text.b, 0.14)
                                    Rectangle {
                                        width: slider.visualPosition * parent.width
                                        height: parent.height
                                        radius: 2
                                        color: theme.c.accent
                                    }
                                }
                                handle: Rectangle {
                                    x: slider.leftPadding + slider.visualPosition
                                       * (slider.availableWidth - width)
                                    y: slider.topPadding + slider.availableHeight / 2 - height / 2
                                    width: 16; height: 16; radius: 8
                                    color: theme.c.accent
                                    border.width: 2
                                    border.color: theme.c.base
                                    scale: slider.pressed ? 1.2 : 1.0
                                    Behavior on scale {
                                        NumberAnimation {
                                            duration: Math.round(theme.n.anim_ms)
                                            easing.type: Easing.OutBack
                                            easing.overshoot: 2.4
                                        }
                                    }
                                }
                            }
                        }
                    }

                    DialogButton {
                        Layout.fillWidth: true
                        text: tr.t("settings.reset")
                        onClicked: theme.resetOverrides()
                    }

                    // ── 语言 ────────────────────────────────────────
                    SectionLabel { text: tr.t("settings.language") }
                    RowLayout {
                        Layout.fillWidth: true
                        spacing: 8
                        ChoiceChip {
                            Layout.fillWidth: true
                            label: tr.t("app.menu.chinese")
                            active: app.language === "zh"
                            onInvoked: app.setLanguage("zh")
                        }
                        ChoiceChip {
                            Layout.fillWidth: true
                            label: tr.t("app.menu.english")
                            active: app.language === "en"
                            onInvoked: app.setLanguage("en")
                        }
                    }

                    // ── 图标大小 ────────────────────────────────────
                    SectionLabel { text: tr.t("app.menu.icon_size") }
                    RowLayout {
                        Layout.fillWidth: true
                        spacing: 8
                        Repeater {
                            model: [
                                { k: "small",  l: tr.t("app.menu.size_small") },
                                { k: "medium", l: tr.t("app.menu.size_medium") },
                                { k: "large",  l: tr.t("app.menu.size_large") }
                            ]
                            delegate: ChoiceChip {
                                Layout.fillWidth: true
                                label: modelData.l
                                active: app.iconSize === modelData.k
                                onInvoked: app.setIconSize(modelData.k)
                            }
                        }
                    }

                    Item { Layout.preferredHeight: 8 }
                }
            }
        }
    }

    // 入场：抽屉自身轻微淡入
    opacity: opened ? 1.0 : 1.0
}
