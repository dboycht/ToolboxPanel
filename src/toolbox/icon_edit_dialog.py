"""图标编辑/创建对话框 — 按图标类型显示不同字段。

- 编辑：IconEditDialog(icon, parent)
- 创建：IconEditDialog.create_for_type(type, parent=..., **prefill)
- 快捷方式类型额外支持：描述、自定义图标（文件+索引）
"""
from PyQt6.QtWidgets import (QDialog, QVBoxLayout, QFormLayout, QLabel,
                              QLineEdit, QHBoxLayout, QPushButton,
                              QDialogButtonBox, QFileDialog, QSpinBox,
                              QWidget)
from .models.icon_model import IconModel, IconType
from .i18n import tr


class IconEditDialog(QDialog):
    """编辑或创建图标属性。按类型显示：名称 + 对应字段。"""

    def __init__(self, icon: IconModel, parent=None):
        super().__init__(parent)
        self._icon = icon
        self._creation_mode = False
        self._custom_icon: tuple[str, int] | None = None  # (path, index) or None
        self.setWindowTitle(tr("edit.title"))
        self.setMinimumWidth(500)

        layout = QVBoxLayout(self)

        # 类型提示：明确说明这是「属性编辑」（名称/路径等），不是替换图标图片
        type_hint = QLabel(tr(f"edit.type.{icon.type}"))
        type_hint.setStyleSheet("color: #666666; font-size: 9pt;")
        layout.addWidget(type_hint)

        form = QFormLayout()

        # 名称（所有类型通用）
        self._name_edit = QLineEdit(icon.display_name)
        form.addRow(tr("edit.field.name"), self._name_edit)

        # 按类型添加字段
        self._path_edit = None
        self._url_edit = None
        self._cmd_edit = None
        self._args_edit = None
        self._wd_edit = None
        self._desc_edit = None
        self._icon_path_edit = None
        self._icon_index_spin = None

        if icon.type in (IconType.FILE, IconType.FOLDER, IconType.SHORTCUT):
            self._path_edit = self._make_browse_row(
                form, tr("edit.field.path"),
                icon.target_path or icon.source_path,
                is_dir=(icon.type == IconType.FOLDER),
            )
            if icon.type == IconType.SHORTCUT:
                self._args_edit = QLineEdit(icon.arguments)
                self._args_edit.setPlaceholderText(tr("cmd.placeholder.args"))
                form.addRow(tr("cmd.label.args"), self._args_edit)
                self._wd_edit = self._make_browse_row(
                    form, tr("cmd.label.wd"), icon.working_dir, is_dir=True
                )
                # 描述（快捷方式属性）
                self._desc_edit = QLineEdit(icon.description)
                self._desc_edit.setPlaceholderText(tr("edit.field.desc_ph"))
                form.addRow(tr("edit.field.desc"), self._desc_edit)
                # 自定义图标（文件 + 索引 + 重置按钮）
                self._icon_path_edit, self._icon_index_spin, _reset_btn = \
                    self._make_icon_row(form)
        elif icon.type == IconType.URL:
            self._url_edit = QLineEdit(icon.source_path)
            self._url_edit.setPlaceholderText(tr("url.placeholder.url"))
            form.addRow(tr("url.label.url"), self._url_edit)
        elif icon.type == IconType.COMMAND:
            self._cmd_edit = self._make_browse_row(form, tr("cmd.label.command"), icon.target_path)
            self._args_edit = QLineEdit(icon.arguments)
            self._args_edit.setPlaceholderText(tr("cmd.placeholder.args"))
            form.addRow(tr("cmd.label.args"), self._args_edit)
            self._wd_edit = self._make_browse_row(
                form, tr("cmd.label.wd"), icon.working_dir, is_dir=True
            )

        layout.addLayout(form)

        # 按钮
        btns = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel
        )
        btns.button(QDialogButtonBox.StandardButton.Ok).setText(tr("btn.ok"))
        btns.button(QDialogButtonBox.StandardButton.Cancel).setText(tr("btn.cancel"))
        btns.accepted.connect(self.accept)
        btns.rejected.connect(self.reject)
        layout.addWidget(btns)

    # ── 工厂方法：创建模式 ──

    @classmethod
    def create_for_type(cls, icon_type: IconType, parent=None, **prefill) -> "IconEditDialog":
        """Create a dialog for making a NEW icon of the given type.

        Accepts optional prefill kwargs: display_name, source_path,
        target_path, arguments, working_dir.
        """
        from uuid import uuid4
        temp_icon = IconModel(
            id=str(uuid4()),
            type=icon_type,
            display_name=prefill.get("display_name", ""),
            source_path=prefill.get("source_path", ""),
            target_path=prefill.get("target_path", ""),
            arguments=prefill.get("arguments", ""),
            working_dir=prefill.get("working_dir", ""),
        )
        dlg = cls(temp_icon, parent=parent)
        dlg._creation_mode = True
        # Override title based on type
        title_keys = {
            IconType.FILE: "create.title.file",
            IconType.FOLDER: "create.title.folder",
            IconType.SHORTCUT: "create.title.shortcut",
            IconType.URL: "url.dialog.title",
            IconType.COMMAND: "cmd.dialog.title",
        }
        title_key = title_keys.get(icon_type, "edit.title")
        dlg.setWindowTitle(tr(title_key))
        return dlg

    def get_created_icon(self) -> IconModel:
        """After accept + apply, return the populated IconModel (creation mode)."""
        return self._icon

    def get_custom_icon_spec(self) -> tuple[str, int] | None:
        """Return the custom icon (path, index) chosen by the user, or None."""
        if self._icon_path_edit is None or self._icon_index_spin is None:
            return None
        p = self._icon_path_edit.text().strip()
        if not p:
            return None
        return p, self._icon_index_spin.value()

    # ── 输入行工厂 ──

    def _make_browse_row(self, form, label, text, is_dir=False):
        """创建带浏览按钮的输入行。浏览按钮使用中性灰色样式。"""
        row = QWidget()
        h = QHBoxLayout(row)
        h.setContentsMargins(0, 0, 0, 0)
        edit = QLineEdit(text or "")
        btn = QPushButton("…")
        btn.setFixedWidth(36)
        btn.setStyleSheet(_browse_btn_style())
        if is_dir:
            btn.clicked.connect(
                lambda: edit.setText(
                    QFileDialog.getExistingDirectory(self, tr("cmd.dialog.select_wd")) or edit.text()
                )
            )
        else:
            btn.clicked.connect(
                lambda: edit.setText(
                    QFileDialog.getOpenFileName(self, tr("grid.dialog.select_file"))[0] or edit.text()
                )
            )
        h.addWidget(edit)
        h.addWidget(btn)
        form.addRow(label, row)
        return edit

    def _make_icon_row(self, form):
        """创建图标选择行：路径 + 索引 + 重置按钮。

        返回 (path_edit, index_spin, container_widget)。
        """
        row = QWidget()
        h = QHBoxLayout(row)
        h.setContentsMargins(0, 0, 0, 0)
        h.setSpacing(4)

        edit = QLineEdit()
        edit.setPlaceholderText(tr("edit.field.icon_ph"))
        browse_btn = QPushButton("…")
        browse_btn.setFixedWidth(36)
        browse_btn.setStyleSheet(_browse_btn_style())
        browse_btn.clicked.connect(
            lambda: edit.setText(
                QFileDialog.getOpenFileName(
                    self, tr("edit.field.icon_browse"),
                    "", "Icons (*.exe *.dll *.ico *.lnk);;All Files (*)"
                )[0] or edit.text()
            )
        )

        idx_label = QLabel(tr("edit.field.icon_index"))
        idx_label.setStyleSheet("color: #666666;")
        spin = QSpinBox()
        spin.setRange(0, 999)
        spin.setValue(0)
        spin.setFixedWidth(70)

        reset_btn = QPushButton(tr("edit.btn.reset_icon"))
        reset_btn.setStyleSheet(_browse_btn_style())
        reset_btn.setToolTip(tr("edit.btn.reset_icon_tip"))
        reset_btn.clicked.connect(lambda: (edit.clear(), spin.setValue(0)))

        h.addWidget(edit)
        h.addWidget(browse_btn)
        h.addWidget(idx_label)
        h.addWidget(spin)
        h.addWidget(reset_btn)

        form.addRow(tr("edit.field.icon"), row)
        return edit, spin, reset_btn

    # ── 数据写入 ──

    def apply(self) -> tuple[bool, str | None]:
        """将对话框内容写回图标模型。

        返回 (是否有效, 错误消息键或None)。
        错误消息键：'validate.name_required' / 'validate.path_required' / 等。
        编辑模式仅名称必填；创建模式额外要求路径/URL/命令。
        """
        icon = self._icon
        new_name = self._name_edit.text().strip()
        if not new_name:
            return False, "validate.name_required"
        icon.display_name = new_name

        is_creating = self._creation_mode

        if icon.type in (IconType.FILE, IconType.FOLDER):
            p = self._path_edit.text().strip()
            if p:
                icon.source_path = p
                icon.target_path = p
            elif is_creating:
                return False, "validate.path_required"
        elif icon.type == IconType.SHORTCUT:
            p = self._path_edit.text().strip()
            if p:
                icon.target_path = p
            elif is_creating:
                return False, "validate.path_required"
            icon.arguments = self._args_edit.text().strip()
            icon.working_dir = self._wd_edit.text().strip()
            icon.description = self._desc_edit.text().strip()
            # 自定义图标规格（供调用方提取）
            spec = self.get_custom_icon_spec()
            self._custom_icon = spec if spec else None
        elif icon.type == IconType.URL:
            u = self._url_edit.text().strip()
            if u:
                if not u.startswith(("http://", "https://", "ftp://")):
                    u = "https://" + u
                icon.source_path = u
                icon.target_path = u
            elif is_creating:
                return False, "validate.url_required"
        elif icon.type == IconType.COMMAND:
            c = self._cmd_edit.text().strip()
            if c:
                icon.target_path = c
                icon.source_path = f"{c} {self._args_edit.text().strip()}".strip()
            elif is_creating:
                return False, "validate.command_required"
            icon.arguments = self._args_edit.text().strip()
            icon.working_dir = self._wd_edit.text().strip()
        return True, None


def _browse_btn_style() -> str:
    return """
        QPushButton {
            background-color: #e0e0e0;
            color: #1e1e1e;
            border: 1px solid #c0c0c0;
            border-radius: 4px;
            padding: 2px 6px;
        }
        QPushButton:hover {
            background-color: #d0d0d0;
        }
    """
