import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

/* 网格页：图标网格 + 搜索栏 + 右键菜单（与原版 IconGrid 行为对齐）
   - 图标位置只由 GridView 布局决定，delegate 内部不写定位属性（ERROR.md E6）
   - 搜索：按 display_name 不区分大小写子串匹配（与原版 _apply_filter 一致）
   - 双击 = 打开；右键 = 打开/编辑/重命名/删除 */
Item {
    id: page

    property int cellW: iconSizeCell()
    property int cellH: Math.round(cellW * 1.06)

    function iconSizeCell() {
        // 与原版三档图标大小对应（small/medium/large）
        var s = app.iconSize
        if (s === "small") return 104
        if (s === "large") return 152
        return 124
    }

    ColumnLayout {
        anchors.fill: parent
        spacing: 6

        // ── 搜索栏（Ctrl+F 或标题栏按钮切换）────────────────────────
        Rectangle {
            id: searchBox
            Layout.fillWidth: true
            Layout.preferredHeight: app.searchOpen ? 42 : 0
            visible: Layout.preferredHeight > 0
            clip: true
            radius: 11
            color: Qt.rgba(theme.c.alt_base.r, theme.c.alt_base.g,
                           theme.c.alt_base.b, 0.75)
            border.width: 1
            border.color: field.activeFocus ? theme.c.accent : theme.c.border

            Behavior on Layout.preferredHeight {
                NumberAnimation {
                    duration: Math.round(theme.n.anim_ms)
                    easing.type: Easing.OutCubic
                }
            }
            Behavior on border.color {
                ColorAnimation { duration: Math.round(theme.n.hover_ms) }
            }

            RowLayout {
                anchors.fill: parent
                anchors.leftMargin: 12
                anchors.rightMargin: 12
                spacing: 8

                Text {
                    text: "⌕"
                    color: theme.c.text_dim
                    font.pixelSize: 15
                }

                TextField {
                    id: field
                    Layout.fillWidth: true
                    placeholderText: tr.t("search.placeholder")
                    color: theme.c.text
                    placeholderTextColor: theme.c.text_hint
                    font.pixelSize: 13
                    selectByMouse: true
                    background: Item { }
                    onTextChanged: app.setSearch(text)
                    onAccepted: app.setSearch(text)

                    Connections {
                        target: app
                        function onSearchChanged() {
                            if (field.text !== app.searchQuery)
                                field.text = app.searchQuery
                        }
                    }

                    Keys.onEscapePressed: app.toggleSearch()
                }

                IconButton {
                    glyph: "✕"
                    onInvoked: app.toggleSearch()
                }
            }

            onVisibleChanged: if (visible) field.forceActiveFocus()
        }

        // ── 空状态提示 ────────────────────────────────────────────────
        Text {
            Layout.fillWidth: true
            Layout.topMargin: 30
            visible: app.icons.count === 0
            horizontalAlignment: Text.AlignHCenter
            text: app.searchQuery !== "" ? tr.t("search.no_result")
                                         : tr.t("grid.empty_hint")
            color: theme.c.text_hint
            font.pixelSize: 13
            wrapMode: Text.WordWrap
        }

        // ── 图标网格 ──────────────────────────────────────────────────
        GridView {
            id: grid
            Layout.fillWidth: true
            Layout.fillHeight: true
            visible: app.icons.count > 0
            model: app.icons
            cellWidth: page.cellW
            cellHeight: page.cellH
            boundsBehavior: Flickable.StopAtBounds
            clip: true
            // 注意：这里不要在 delegate 上做 y 动画（ERROR.md E6）
            property int appearTick: 0

            delegate: Item {
                id: cell
                width: grid.cellWidth
                height: grid.cellHeight

                // 入场：透明度 + 内层位移。
                // ⚠️ delegate 自身的 x/y 归 GridView 管，绝不在它上面做位移动画（E6）
                opacity: 0.0
                Component.onCompleted: fadeIn.start()

                NumberAnimation {
                    id: fadeIn
                    target: cellInner
                    property: "y"
                    from: 22; to: 0
                    duration: 460
                    easing.type: Easing.OutBack
                    easing.overshoot: 1.4
                }
                SequentialAnimation {
                    running: true
                    PauseAnimation { duration: Math.min(index, 18) * 30 }
                    NumberAnimation {
                        target: cell
                        property: "opacity"
                        from: 0.0; to: 1.0
                        duration: 300
                        easing.type: Easing.OutCubic
                    }
                }

                Item {
                    id: cellInner
                    anchors.fill: parent

                    Rectangle {
                        id: tile
                        anchors.centerIn: parent
                        width: Math.round(page.cellW * 0.88)
                        height: Math.round(page.cellH * 0.86)
                        radius: Math.round(theme.n.radius * 1.05)

                        color: mouse.pressed
                            ? Qt.rgba(theme.c.card.r, theme.c.card.g, theme.c.card.b,
                                      Math.min(0.9, theme.n.card_hover_opacity + 0.08))
                            : (mouse.containsMouse
                                ? Qt.rgba(theme.c.card.r, theme.c.card.g, theme.c.card.b,
                                          theme.n.card_hover_opacity)
                                : Qt.rgba(theme.c.card.r, theme.c.card.g, theme.c.card.b,
                                          theme.n.card_opacity))
                        Behavior on color {
                            ColorAnimation { duration: Math.round(theme.n.hover_ms) }
                        }
                        border.width: 1
                        border.color: mouse.containsMouse
                            ? Qt.rgba(theme.c.accent.r, theme.c.accent.g, theme.c.accent.b, 0.45)
                            : theme.c.border_soft

                        scale: mouse.pressed ? 0.94 : (mouse.containsMouse ? 1.06 : 1.0)
                        Behavior on scale {
                            NumberAnimation {
                                duration: Math.round(theme.n.anim_ms) + 160
                                easing.type: Easing.OutBack
                                easing.overshoot: 2.2
                            }
                        }

                        Column {
                            anchors.centerIn: parent
                            spacing: 8
                            width: parent.width - 12

                            Image {
                                anchors.horizontalCenter: parent.horizontalCenter
                                source: iconPath
                                sourceSize.width: Math.round(tile.width * 0.52)
                                sourceSize.height: Math.round(tile.width * 0.52)
                                width: Math.round(tile.width * 0.5)
                                height: Math.round(tile.width * 0.5)
                                fillMode: Image.PreserveAspectFit
                                smooth: true
                                asynchronous: true
                                opacity: status === Image.Ready ? 1.0 : 0.25
                                scale: mouse.containsMouse ? 1.08 : 1.0
                                Behavior on scale {
                                    NumberAnimation {
                                        duration: Math.round(theme.n.anim_ms) + 200
                                        easing.type: Easing.OutBack
                                        easing.overshoot: 2.0
                                    }
                                }
                                // 图标缺失时用类型首字兜底
                                Text {
                                    anchors.centerIn: parent
                                    visible: parent.status !== Image.Ready
                                    text: "?"
                                    color: theme.c.text_hint
                                    font.pixelSize: 22
                                }
                            }

                            Text {
                                width: parent.width
                                horizontalAlignment: Text.AlignHCenter
                                text: iconName
                                color: mouse.containsMouse ? theme.c.text : theme.c.text_dim
                                font.pixelSize: 12
                                elide: Text.ElideRight
                                maximumLineCount: 2
                                wrapMode: Text.Wrap
                                Behavior on color {
                                    ColorAnimation { duration: Math.round(theme.n.hover_ms) }
                                }
                            }
                        }

                        // 批量管理复选框（与原版批量模式一致）
                        Rectangle {
                            visible: app.isBatchMode
                            anchors.top: parent.top
                            anchors.left: parent.left
                            anchors.margins: 7
                            width: 18; height: 18; radius: 5
                            color: checked ? theme.c.accent
                                           : Qt.rgba(theme.c.base.r, theme.c.base.g,
                                                     theme.c.base.b, 0.85)
                            border.width: 1
                            border.color: checked ? theme.c.accent : theme.c.border
                            property bool checked: false

                            Text {
                                anchors.centerIn: parent
                                text: parent.checked ? "✓" : ""
                                color: theme.c.on_accent
                                font.pixelSize: 11
                            }
                            MouseArea {
                                anchors.fill: parent
                                onClicked: parent.checked = !parent.checked
                            }
                        }
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
                            iconMenu.iconId = iconId
                            iconMenu.iconName = iconName
                            iconMenu.iconType = iconType
                            iconMenu.iconPath = iconTarget
                            iconMenu.popup()
                        }
                    }
                    onDoubleClicked: app.openIcon(iconId)
                }
            }
        }
    }

    // ── 图标右键菜单（对应原版 icon.menu.* ）──────────────────────────
    Menu {
        id: iconMenu
        property string iconId: ""
        property string iconName: ""
        property string iconType: ""
        property string iconPath: ""

        MenuItem {
            text: tr.t("icon.menu.open")
            onTriggered: app.openIcon(iconMenu.iconId)
        }
        MenuSeparator { }
        MenuItem {
            text: tr.t("icon.menu.rename")
            onTriggered: renameIconDlg.openFor(iconMenu.iconId, iconMenu.iconName)
        }
        MenuItem {
            text: tr.t("icon.menu.remove")
            onTriggered: removeIconDlg.openMessage(
                tr.t("icon.remove.title"),
                tr.tf("icon.remove.confirm", { "name": iconMenu.iconName }),
                iconMenu.iconId)
        }
    }

    RenameIconDialog { id: renameIconDlg }
    ConfirmDialog {
        id: removeIconDlg
        onConfirmed: function (payload) { app.removeIcon(String(payload)) }
    }
}
