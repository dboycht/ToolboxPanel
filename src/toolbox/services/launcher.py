"""Launch handler — opens files, URLs, and runs commands."""
import os
import subprocess
import webbrowser
import sys

from ..models.icon_model import IconModel, IconType


class Launcher:
    """Opens files, URLs, runs commands, and opens file locations."""

    def open(self, icon: IconModel):
        """Execute/open the icon based on its type."""
        if icon.type == IconType.URL:
            self._open_url(icon.source_path)
        elif icon.type == IconType.FILE:
            self._open_file(icon.target_path or icon.source_path)
        elif icon.type == IconType.FOLDER:
            self._open_file(icon.target_path or icon.source_path)
        elif icon.type == IconType.SHORTCUT:
            self._launch_process(
                icon.target_path, icon.arguments, icon.working_dir
            )
        elif icon.type == IconType.COMMAND:
            self._launch_process(
                icon.target_path, icon.arguments, icon.working_dir
            )

    @staticmethod
    def _open_url(url: str):
        """Open a URL in the default browser."""
        if url and not url.startswith(("http://", "https://", "ftp://")):
            url = "https://" + url
        webbrowser.open(url)

    @staticmethod
    def _open_file(path: str):
        """Open a file or folder with the default handler."""
        if os.path.exists(path):
            os.startfile(path)
        else:
            raise FileNotFoundError(f"File not found: {path}")

    @staticmethod
    def _launch_process(target: str, args: str = "", cwd: str = ""):
        """Launch a process with optional arguments and working directory."""
        if not target:
            raise ValueError("No executable specified.")

        cmd = [target]
        if args:
            # Split args respecting quoted strings
            cmd.extend(_split_args(args))

        working_dir = cwd if cwd and os.path.isdir(cwd) else None

        if sys.platform == "win32":
            subprocess.Popen(
                cmd,
                cwd=working_dir,
                creationflags=subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP,
            )
        else:
            subprocess.Popen(cmd, cwd=working_dir)


def _split_args(args_str: str) -> list[str]:
    """Split a command-line argument string into parts, respecting quotes."""
    import shlex
    try:
        return shlex.split(args_str)
    except ValueError:
        return args_str.split()
