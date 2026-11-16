"""애플리케이션 진입점과 Qt 초기화."""

from __future__ import annotations

import sys

# PyVista가 Qt와 함께 쓰일 때의 전역 설정
import pyvista as pv

from PySide6 import QtGui, QtWidgets

from . import APP_NAME

DARK_STYLESHEET = """
QMainWindow, QWidget { background-color: #1b1e26; color: #dfe3ee; }
QDockWidget { titlebar-close-icon: none; }
QDockWidget::title { background: #232733; padding: 5px; font-weight: bold; }
QToolBar { background: #232733; border: none; spacing: 4px; }
QMenuBar { background: #232733; }
QMenuBar::item:selected { background: #37405a; }
QMenu { background: #232733; color: #dfe3ee; }
QMenu::item:selected { background: #37405a; }
QTabWidget::pane { border: 1px solid #333a4a; }
QTabBar::tab { background: #232733; padding: 6px 12px; color: #aab; }
QTabBar::tab:selected { background: #37405a; color: #fff; }
QPushButton { background: #2d3345; border: 1px solid #3a4258; padding: 5px 10px;
              border-radius: 4px; }
QPushButton:hover { background: #37405a; }
QComboBox, QListWidget, QTableWidget, QPlainTextEdit, QLineEdit {
    background: #222634; border: 1px solid #343b4e; border-radius: 3px;
    selection-background-color: #37405a; }
QSlider::groove:horizontal { height: 5px; background: #343b4e; border-radius: 2px; }
QSlider::handle:horizontal { background: #6b8cff; width: 13px; margin: -5px 0;
                             border-radius: 6px; }
QToolButton { background: #2d3345; border: 1px solid #3a4258; border-radius: 4px;
              padding: 3px; }
QToolButton:hover { background: #37405a; }
QCheckBox { padding: 2px; }
"""


def build_application(argv: list[str] | None = None) -> QtWidgets.QApplication:
    """QApplication을 만들고 테마를 적용한다(이미 있으면 재사용)."""
    app = QtWidgets.QApplication.instance()
    if app is None:
        app = QtWidgets.QApplication(argv or sys.argv)
    app.setApplicationName(APP_NAME)
    app.setStyle("Fusion")
    app.setStyleSheet(DARK_STYLESHEET)
    try:
        pv.global_theme.multi_samples = 8
        pv.global_theme.smooth_shading = True
    except Exception:  # pragma: no cover
        pass
    return app


def main(argv: list[str] | None = None) -> int:
    """프로그램 실행."""
    app = build_application(argv)
    from .ui.main_window import MainWindow

    window = MainWindow()
    window.show()
    return app.exec()


if __name__ == "__main__":  # pragma: no cover
    raise SystemExit(main())
