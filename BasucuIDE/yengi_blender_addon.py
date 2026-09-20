bl_info = {
    "name": "Yengi Copilot for Blender",
    "author": "Yengi IDE",
    "version": (1, 0, 0),
    "blender": (2, 80, 0),
    "location": "Background Socket Server (Port 8181)",
    "description": "Yengi IDE ile Blender arasında canlı kod çalıştırma bağlantısı sağlar.",
    "warning": "",
    "wiki_url": "",
    "category": "Development",
}

import bpy
import socket
import threading
import queue

PORT = 8181
SECRET_TOKEN = "" # Populated on installation or validated via YENGI_TOKEN header
server_socket = None
is_running = False
script_queue = queue.Queue()

def execute_queued_scripts():
    """Blender main thread üzerinde kuyruktaki scriptleri çalıştırır."""
    while not script_queue.empty():
        item = script_queue.get()
        payload, conn = item
        response = "OK"
        try:
            code = payload
            if payload.startswith("YENGI_TOKEN:"):
                lines = payload.split("\n", 1)
                sent_token = lines[0].replace("YENGI_TOKEN:", "").strip()
                if SECRET_TOKEN and sent_token != SECRET_TOKEN:
                    response = "ERROR: Unauthorized access - Invalid Token"
                    print("[Yengi Blender Copilot] Güvenlik Hatası: Geçersiz token reddedildi.")
                    conn.sendall(response.encode('utf-8'))
                    conn.close()
                    continue
                code = lines[1] if len(lines) > 1 else ""

            # import bpy veya from bpy öncesindeki numaraları veya kalan metinleri temizle
            import re
            bpy_idx = code.find("import bpy")
            if bpy_idx < 0:
                bpy_idx = code.find("from bpy")
            if bpy_idx >= 0:
                code = code[bpy_idx:]

            # Satır başlarındaki numaralandırmaları (örn: "1. ", "2. ") ve BOM/Boşluk/Markdown temizle
            split_lines = code.split("\n")
            cleaned_lines = []
            for line in split_lines:
                cleaned_line = re.sub(r'^\s*\d+[\.\:]?\s+', '', line)
                cleaned_lines.append(cleaned_line)
            code = "\n".join(cleaned_lines)
            code = re.sub(r'^```[a-zA-Z]*\n', '', code)
            code = re.sub(r'\n```$', '', code)
            code = code.strip("\ufeff\r\n ")

            # bpy nesnelerini kapsama ekle
            exec_scope = {"bpy": bpy}
            exec(code, exec_scope)
            response = "SUCCESS: Script başarıyla çalıştırıldı."
            print("[Yengi Blender Copilot] Script çalıştırıldı.")
        except Exception as e:
            response = f"ERROR: {str(e)}"
            print(f"[Yengi Blender Copilot Hata] {e}")
        
        try:
            conn.sendall(response.encode('utf-8'))
            conn.close()
        except:
            pass
            
    return 0.2  # Timer 0.2 sn sonra tekrar çalışır

def socket_listener_thread():
    global server_socket, is_running
    import time
    server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    
    try:
        server_socket.bind(('127.0.0.1', PORT))
        server_socket.listen(5)
        is_running = True
        print(f"[Yengi Blender Copilot] TCP Socket sunucusu 127.0.0.1:{PORT} üzerinde dinliyor...")
    except Exception as e:
        print(f"[Yengi Blender Copilot] Port bağlama hatası ({PORT}): {e}")
        return

    while is_running:
        try:
            conn, addr = server_socket.accept()
            buffer = bytearray()
            while True:
                chunk = conn.recv(4096)
                if not chunk:
                    break
                buffer.extend(chunk)
            if buffer:
                code = buffer.decode('utf-8')
                script_queue.put((code, conn))
        except Exception:
            if not is_running:
                break
            time.sleep(0.05)
            continue

def register():
    print("[Yengi Blender Copilot] Eklenti kaydediliyor...")
    # Blender main loop timer'a ekle
    if not bpy.app.timers.is_registered(execute_queued_scripts):
        bpy.app.timers.register(execute_queued_scripts)
    
    t = threading.Thread(target=socket_listener_thread, daemon=True)
    t.start()

def unregister():
    global is_running, server_socket
    is_running = False
    if server_socket:
        try:
            server_socket.close()
        except:
            pass
    if bpy.app.timers.is_registered(execute_queued_scripts):
        bpy.app.timers.unregister(execute_queued_scripts)
    print("[Yengi Blender Copilot] Eklenti durduruldu.")

if __name__ == "__main__":
    register()
