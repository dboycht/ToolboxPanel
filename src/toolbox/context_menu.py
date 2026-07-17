"""Menu factories for icon and tab context menus."""
from PyQt6.QtWidgets import QMenu
from PyQt6.QtGui import QAction


def create_icon_menu(parent, icon_model, callbacks: dict) -> QMenu:
    """Create a right-click context menu for an icon.

    callbacks should contain:
        - on_open: callable()
        - on_open_location: callable()
        - on_rename: callable()
        - on_remove: callable()
        - on_properties: callable()
    """
    menu = QMenu(parent)

    open_action = QAction("Open", menu)
    open_action.triggered.connect(callbacks.get("on_open"))
    menu.addAction(open_action)

    open_loc_action = QAction("Open File Location", menu)
    open_loc_action.triggered.connect(callbacks.get("on_open_location"))
    menu.addAction(open_loc_action)

    menu.addSeparator()

    rename_action = QAction("Rename", menu)
    rename_action.triggered.connect(callbacks.get("on_rename"))
    menu.addAction(rename_action)

    remove_action = QAction("Remove", menu)
    remove_action.triggered.connect(callbacks.get("on_remove"))
    menu.addAction(remove_action)

    menu.addSeparator()

    props_action = QAction("Properties", menu)
    props_action.triggered.connect(callbacks.get("on_properties"))
    menu.addAction(props_action)

    return menu


def create_tab_menu(parent, callbacks: dict) -> QMenu:
    """Create a right-click context menu for a tab.

    callbacks should contain:
        - on_new_tab: callable()
        - on_rename: callable()
        - on_delete: callable()
    """
    menu = QMenu(parent)

    new_action = QAction("New Tab", menu)
    new_action.triggered.connect(callbacks.get("on_new_tab"))
    menu.addAction(new_action)

    rename_action = QAction("Rename", menu)
    rename_action.triggered.connect(callbacks.get("on_rename"))
    menu.addAction(rename_action)

    menu.addSeparator()

    delete_action = QAction("Delete", menu)
    delete_action.triggered.connect(callbacks.get("on_delete"))
    menu.addAction(delete_action)

    return menu


def create_grid_menu(parent, callbacks: dict) -> QMenu:
    """Create a right-click context menu for the empty grid area.

    callbacks should contain:
        - on_new_file: callable()
        - on_new_folder: callable()
        - on_new_url: callable()
        - on_new_command: callable()
    """
    menu = QMenu(parent)

    menu.addAction(QAction("New File Icon", menu)).triggered.connect(
        callbacks.get("on_new_file")
    )
    menu.addAction(QAction("New Folder Icon", menu)).triggered.connect(
        callbacks.get("on_new_folder")
    )
    menu.addSeparator()
    menu.addAction(QAction("New URL Icon", menu)).triggered.connect(
        callbacks.get("on_new_url")
    )
    menu.addAction(QAction("New Command Icon", menu)).triggered.connect(
        callbacks.get("on_new_command")
    )

    return menu
