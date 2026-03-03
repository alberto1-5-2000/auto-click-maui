import ctypes
import threading
import time
import tkinter as tk
from tkinter import ttk, messagebox

import cv2
import mss
import numpy as np
from PIL import Image, ImageTk


class TemplateSelectionWindow(tk.Toplevel):
    def __init__(self, parent, screenshot_bgr):
        super().__init__(parent)
        self.title("Recorta la imagen objetivo")
        self.resizable(False, False)
        self.result = None

        self.original_bgr = screenshot_bgr
        self.original_h, self.original_w = screenshot_bgr.shape[:2]

        max_w, max_h = 1200, 700
        scale_w = max_w / self.original_w
        scale_h = max_h / self.original_h
        self.scale = min(1.0, scale_w, scale_h)

        display_w = int(self.original_w * self.scale)
        display_h = int(self.original_h * self.scale)

        rgb = cv2.cvtColor(screenshot_bgr, cv2.COLOR_BGR2RGB)
        pil_image = Image.fromarray(rgb).resize((display_w, display_h), Image.Resampling.LANCZOS)
        self.photo = ImageTk.PhotoImage(pil_image)

        self.canvas = tk.Canvas(self, width=display_w, height=display_h, cursor="cross")
        self.canvas.pack(padx=10, pady=10)
        self.canvas.create_image(0, 0, anchor=tk.NW, image=self.photo)

        self.instructions = ttk.Label(
            self,
            text="Arrastra para seleccionar la plantilla. Enter = confirmar, Esc = cancelar",
        )
        self.instructions.pack(pady=(0, 10))

        self.start_x = None
        self.start_y = None
        self.rect_id = None
        self.end_x = None
        self.end_y = None

        self.canvas.bind("<ButtonPress-1>", self.on_press)
        self.canvas.bind("<B1-Motion>", self.on_drag)
        self.canvas.bind("<ButtonRelease-1>", self.on_release)

        self.bind("<Return>", self.confirm)
        self.bind("<Escape>", lambda _: self.cancel())
        self.grab_set()
        self.focus_set()

    def on_press(self, event):
        self.start_x, self.start_y = event.x, event.y
        self.end_x, self.end_y = event.x, event.y
        if self.rect_id:
            self.canvas.delete(self.rect_id)
        self.rect_id = self.canvas.create_rectangle(
            self.start_x,
            self.start_y,
            self.end_x,
            self.end_y,
            outline="red",
            width=2,
        )

    def on_drag(self, event):
        self.end_x, self.end_y = event.x, event.y
        if self.rect_id:
            self.canvas.coords(self.rect_id, self.start_x, self.start_y, self.end_x, self.end_y)

    def on_release(self, event):
        self.end_x, self.end_y = event.x, event.y

    def confirm(self, _=None):
        if None in (self.start_x, self.start_y, self.end_x, self.end_y):
            messagebox.showwarning("Falta selección", "Debes arrastrar un rectángulo primero.")
            return

        x1, x2 = sorted([self.start_x, self.end_x])
        y1, y2 = sorted([self.start_y, self.end_y])
        if x2 - x1 < 5 or y2 - y1 < 5:
            messagebox.showwarning("Selección muy pequeña", "Selecciona un área más grande.")
            return

        ox1 = int(x1 / self.scale)
        oy1 = int(y1 / self.scale)
        ox2 = int(x2 / self.scale)
        oy2 = int(y2 / self.scale)

        self.result = (ox1, oy1, ox2, oy2)
        self.destroy()

    def cancel(self):
        self.result = None
        self.destroy()


class PointSelectionWindow(tk.Toplevel):
    def __init__(self, parent, screenshot_bgr):
        super().__init__(parent)
        self.title("Selecciona el punto de clic")
        self.resizable(False, False)
        self.result = None

        self.original_bgr = screenshot_bgr
        self.original_h, self.original_w = screenshot_bgr.shape[:2]

        max_w, max_h = 1200, 700
        scale_w = max_w / self.original_w
        scale_h = max_h / self.original_h
        self.scale = min(1.0, scale_w, scale_h)

        display_w = int(self.original_w * self.scale)
        display_h = int(self.original_h * self.scale)

        rgb = cv2.cvtColor(screenshot_bgr, cv2.COLOR_BGR2RGB)
        pil_image = Image.fromarray(rgb).resize((display_w, display_h), Image.Resampling.LANCZOS)
        self.photo = ImageTk.PhotoImage(pil_image)

        self.canvas = tk.Canvas(self, width=display_w, height=display_h, cursor="tcross")
        self.canvas.pack(padx=10, pady=10)
        self.canvas.create_image(0, 0, anchor=tk.NW, image=self.photo)

        self.instructions = ttk.Label(
            self,
            text="Haz clic para elegir posición. Enter = confirmar, Esc = cancelar",
        )
        self.instructions.pack(pady=(0, 10))

        self.point = None
        self.marker = None
        self.canvas.bind("<Button-1>", self.on_click)

        self.bind("<Return>", self.confirm)
        self.bind("<Escape>", lambda _: self.cancel())
        self.grab_set()
        self.focus_set()

    def on_click(self, event):
        x, y = event.x, event.y
        self.point = (x, y)

        if self.marker:
            self.canvas.delete(self.marker)
        r = 6
        self.marker = self.canvas.create_oval(x - r, y - r, x + r, y + r, outline="lime", width=2)

    def confirm(self, _=None):
        if not self.point:
            messagebox.showwarning("Falta punto", "Haz clic para seleccionar la posición.")
            return
        x, y = self.point
        ox = int(x / self.scale)
        oy = int(y / self.scale)
        self.result = (ox, oy)
        self.destroy()

    def cancel(self):
        self.result = None
        self.destroy()


class AutoClickApp(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("Auto Click por Imagen")
        self.geometry("620x430")
        self.resizable(False, False)

        self.monitors = []
        self.selected_monitor_index = tk.IntVar(value=1)

        self.template_gray = None
        self.template_rect = None
        self.click_point = None

        self.threshold = tk.DoubleVar(value=0.88)
        self.scan_interval = tk.DoubleVar(value=0.2)
        self.cooldown = tk.DoubleVar(value=0.8)

        self.running = False
        self.worker_thread = None
        self.last_click_time = 0.0

        self.status_var = tk.StringVar(value="Listo")

        self.build_ui()
        self.refresh_monitors()

    def build_ui(self):
        main = ttk.Frame(self, padding=14)
        main.pack(fill=tk.BOTH, expand=True)

        monitor_frame = ttk.LabelFrame(main, text="Pantalla")
        monitor_frame.pack(fill=tk.X, pady=(0, 10))

        self.monitor_combo = ttk.Combobox(monitor_frame, state="readonly")
        self.monitor_combo.pack(side=tk.LEFT, fill=tk.X, expand=True, padx=8, pady=8)
        self.monitor_combo.bind("<<ComboboxSelected>>", self.on_monitor_changed)

        ttk.Button(monitor_frame, text="Actualizar", command=self.refresh_monitors).pack(
            side=tk.RIGHT, padx=8, pady=8
        )

        selection_frame = ttk.LabelFrame(main, text="Configuración")
        selection_frame.pack(fill=tk.X, pady=(0, 10))

        row1 = ttk.Frame(selection_frame)
        row1.pack(fill=tk.X, padx=8, pady=6)
        ttk.Button(row1, text="1) Recortar imagen objetivo", command=self.select_template).pack(side=tk.LEFT)
        self.template_label = ttk.Label(row1, text="Sin plantilla")
        self.template_label.pack(side=tk.LEFT, padx=10)

        row2 = ttk.Frame(selection_frame)
        row2.pack(fill=tk.X, padx=8, pady=6)
        ttk.Button(row2, text="2) Seleccionar punto de clic", command=self.select_click_point).pack(side=tk.LEFT)
        self.point_label = ttk.Label(row2, text="Sin punto")
        self.point_label.pack(side=tk.LEFT, padx=10)

        params = ttk.LabelFrame(main, text="Parámetros")
        params.pack(fill=tk.X, pady=(0, 10))

        ttk.Label(params, text="Umbral (0-1):").grid(row=0, column=0, padx=8, pady=6, sticky="w")
        ttk.Entry(params, textvariable=self.threshold, width=8).grid(row=0, column=1, padx=8, pady=6, sticky="w")

        ttk.Label(params, text="Intervalo escaneo (s):").grid(row=0, column=2, padx=8, pady=6, sticky="w")
        ttk.Entry(params, textvariable=self.scan_interval, width=8).grid(row=0, column=3, padx=8, pady=6, sticky="w")

        ttk.Label(params, text="Cooldown clic (s):").grid(row=1, column=0, padx=8, pady=6, sticky="w")
        ttk.Entry(params, textvariable=self.cooldown, width=8).grid(row=1, column=1, padx=8, pady=6, sticky="w")

        button_frame = ttk.Frame(main)
        button_frame.pack(fill=tk.X, pady=(4, 10))

        self.start_btn = ttk.Button(button_frame, text="Iniciar", command=self.start_detection)
        self.start_btn.pack(side=tk.LEFT, padx=(0, 8))

        self.stop_btn = ttk.Button(button_frame, text="Detener", command=self.stop_detection, state=tk.DISABLED)
        self.stop_btn.pack(side=tk.LEFT)

        ttk.Separator(main).pack(fill=tk.X, pady=(6, 8))

        status_row = ttk.Frame(main)
        status_row.pack(fill=tk.X)
        ttk.Label(status_row, text="Estado:").pack(side=tk.LEFT)
        ttk.Label(status_row, textvariable=self.status_var).pack(side=tk.LEFT, padx=6)

        self.protocol("WM_DELETE_WINDOW", self.on_close)

    def refresh_monitors(self):
        with mss.mss() as sct:
            all_monitors = sct.monitors[1:]

        self.monitors = all_monitors

        if not self.monitors:
            self.monitor_combo["values"] = []
            self.monitor_combo.set("")
            self.status_var.set("No se detectaron pantallas")
            return

        values = []
        for idx, mon in enumerate(self.monitors, start=1):
            values.append(f"Pantalla {idx}: {mon['width']}x{mon['height']} @ ({mon['left']},{mon['top']})")

        self.monitor_combo["values"] = values
        if not self.monitor_combo.get():
            self.monitor_combo.current(0)
        self.selected_monitor_index.set(self.monitor_combo.current() + 1)
        self.status_var.set("Pantallas actualizadas")

    def on_monitor_changed(self, _):
        self.selected_monitor_index.set(self.monitor_combo.current() + 1)
        self.status_var.set(f"Pantalla seleccionada: {self.selected_monitor_index.get()}")

    def get_selected_monitor(self):
        idx = self.monitor_combo.current()
        if idx < 0 or idx >= len(self.monitors):
            return None
        return self.monitors[idx]

    def capture_selected_monitor(self):
        mon = self.get_selected_monitor()
        if not mon:
            return None

        with mss.mss() as sct:
            shot = np.array(sct.grab(mon))

        bgr = cv2.cvtColor(shot, cv2.COLOR_BGRA2BGR)
        return bgr

    def select_template(self):
        screenshot = self.capture_selected_monitor()
        if screenshot is None:
            messagebox.showerror("Error", "No hay pantalla seleccionada.")
            return

        win = TemplateSelectionWindow(self, screenshot)
        self.wait_window(win)

        if not win.result:
            return

        x1, y1, x2, y2 = win.result
        tpl = screenshot[y1:y2, x1:x2]
        if tpl.size == 0:
            messagebox.showwarning("Error", "La plantilla seleccionada no es válida.")
            return

        self.template_gray = cv2.cvtColor(tpl, cv2.COLOR_BGR2GRAY)
        self.template_rect = (x1, y1, x2, y2)
        self.template_label.config(text=f"{x2-x1}x{y2-y1} px")
        self.status_var.set("Plantilla guardada")

    def select_click_point(self):
        screenshot = self.capture_selected_monitor()
        if screenshot is None:
            messagebox.showerror("Error", "No hay pantalla seleccionada.")
            return

        win = PointSelectionWindow(self, screenshot)
        self.wait_window(win)

        if not win.result:
            return

        self.click_point = win.result
        self.point_label.config(text=f"x={self.click_point[0]}, y={self.click_point[1]}")
        self.status_var.set("Punto de clic guardado")

    def _perform_click(self, x, y):
        ctypes.windll.user32.SetCursorPos(int(x), int(y))
        ctypes.windll.user32.mouse_event(0x0002, 0, 0, 0, 0)
        ctypes.windll.user32.mouse_event(0x0004, 0, 0, 0, 0)

    def start_detection(self):
        if self.running:
            return

        if self.template_gray is None:
            messagebox.showwarning("Falta plantilla", "Primero recorta la imagen objetivo.")
            return

        if self.click_point is None:
            messagebox.showwarning("Falta punto", "Primero selecciona la posición de clic.")
            return

        mon = self.get_selected_monitor()
        if not mon:
            messagebox.showerror("Error", "No hay pantalla seleccionada.")
            return

        try:
            threshold = float(self.threshold.get())
            interval = float(self.scan_interval.get())
            cooldown = float(self.cooldown.get())
        except ValueError:
            messagebox.showerror("Parámetros inválidos", "Revisa umbral, intervalo y cooldown.")
            return

        if not (0.0 <= threshold <= 1.0):
            messagebox.showerror("Umbral inválido", "El umbral debe estar entre 0 y 1.")
            return

        if interval <= 0 or cooldown < 0:
            messagebox.showerror("Parámetros inválidos", "Intervalo > 0 y cooldown >= 0.")
            return

        self.running = True
        self.last_click_time = 0.0
        self.start_btn.config(state=tk.DISABLED)
        self.stop_btn.config(state=tk.NORMAL)
        self.status_var.set("Detección activa")

        self.worker_thread = threading.Thread(target=self._detection_loop, daemon=True)
        self.worker_thread.start()

    def _detection_loop(self):
        while self.running:
            mon = self.get_selected_monitor()
            if mon is None:
                self._set_status_threadsafe("Pantalla inválida")
                time.sleep(0.5)
                continue

            with mss.mss() as sct:
                shot = np.array(sct.grab(mon))

            frame_gray = cv2.cvtColor(shot, cv2.COLOR_BGRA2GRAY)
            result = cv2.matchTemplate(frame_gray, self.template_gray, cv2.TM_CCOEFF_NORMED)
            _, max_val, _, max_loc = cv2.minMaxLoc(result)

            now = time.time()
            threshold = float(self.threshold.get())
            interval = float(self.scan_interval.get())
            cooldown = float(self.cooldown.get())

            if max_val >= threshold and (now - self.last_click_time) >= cooldown:
                click_x = mon["left"] + self.click_point[0]
                click_y = mon["top"] + self.click_point[1]
                self._perform_click(click_x, click_y)
                self.last_click_time = now
                self._set_status_threadsafe(
                    f"Coincidencia {max_val:.3f} en ({max_loc[0]},{max_loc[1]}) -> clic"
                )
            else:
                self._set_status_threadsafe(f"Esperando coincidencia... score={max_val:.3f}")

            time.sleep(interval)

    def _set_status_threadsafe(self, text):
        self.after(0, lambda: self.status_var.set(text))

    def stop_detection(self):
        self.running = False
        self.start_btn.config(state=tk.NORMAL)
        self.stop_btn.config(state=tk.DISABLED)
        self.status_var.set("Detección detenida")

    def on_close(self):
        self.stop_detection()
        self.destroy()


if __name__ == "__main__":
    app = AutoClickApp()
    app.mainloop()
