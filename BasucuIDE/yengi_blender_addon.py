bl_info = {
    "name": "Yengi Copilot for Blender",
    "author": "Yengi IDE",
    "version": (1, 5, 0),
    "blender": (2, 80, 0),
    "location": "3D Viewport > Sidebar (N Panel) > Yengi Copilot",
    "description": "Yengi IDE ile Blender arasında canlı çift yönlü kod çalıştırma, 3D sahne RAG ve ekran görüntüsü altyapısı sağlar.",
    "warning": "",
    "wiki_url": "",
    "category": "Development",
}

import bpy
import socket
import threading
import queue
import json
import traceback
import re
import os
import base64
import tempfile

PORT = 8181
SECRET_TOKEN = "" # Kurulumda veya ayarlarda belirlenir
server_socket = None
is_running = False
script_queue = queue.Queue()
last_status_message = "🟢 Sunucu Hazır (Port 8181)"
last_executed_script = ""

def get_scene_context_summary():
    """Mevcut Blender sahnesinin detaylı JSON özetini döner."""
    try:
        context = bpy.context
        scene = context.scene
        
        active_obj = context.active_object
        active_info = None
        if active_obj:
            active_info = {
                "name": active_obj.name,
                "type": active_obj.type,
                "location": [round(v, 3) for v in active_obj.location],
                "rotation_euler": [round(v, 3) for v in active_obj.rotation_euler],
                "scale": [round(v, 3) for v in active_obj.scale],
                "mode": getattr(active_obj, 'mode', 'OBJECT')
            }
            
        selected_objs = [{"name": obj.name, "type": obj.type} for obj in context.selected_objects]
        
        all_objs_summary = []
        for obj in scene.objects:
            all_objs_summary.append({
                "name": obj.name,
                "type": obj.type,
                "visible": not obj.hide_get()
            })
            
        materials = [mat.name for mat in bpy.data.materials]
        collections = [col.name for col in bpy.data.collections]
        
        active_cam = scene.camera.name if scene.camera else None
        
        return {
            "mode": getattr(context, 'mode', 'OBJECT'),
            "active_object": active_info,
            "selected_objects": selected_objs,
            "object_count": len(scene.objects),
            "objects": all_objs_summary[:35],
            "materials": materials[:25],
            "collections": collections[:15],
            "active_camera": active_cam,
            "render_engine": getattr(scene.render, 'engine', 'EEVEE')
        }
    except Exception as e:
        return {"error": f"Sahne özeti alınamadı: {str(e)}"}

def capture_viewport_screenshot():
    """3D Viewport ekran görüntüsünü alıp Base64 ve dosya yolu olarak döner."""
    try:
        temp_dir = tempfile.gettempdir()
        filepath = os.path.join(temp_dir, "yengi_viewport_preview.png")
        
        viewport_area = None
        for area in bpy.context.screen.areas:
            if area.type == 'VIEW_3D':
                viewport_area = area
                break
                
        if not viewport_area:
            return None, "", "3D Viewport ekranı bulunamadı."
            
        old_path = bpy.context.scene.render.filepath
        bpy.context.scene.render.filepath = filepath
        
        if hasattr(bpy.context, "temp_override"):
            with bpy.context.temp_override(area=viewport_area):
                bpy.ops.render.opengl(write_still=True)
        else:
            override = {'area': viewport_area, 'screen': bpy.context.screen, 'window': bpy.context.window}
            bpy.ops.render.opengl(override, write_still=True)
            
        bpy.context.scene.render.filepath = old_path
        
        if os.path.exists(filepath):
            with open(filepath, "rb") as image_file:
                b64_data = base64.b64encode(image_file.read()).decode('utf-8')
            return filepath, b64_data, "SUCCESS"
        else:
            return None, "", "Ekran görüntüsü dosyası oluşturulamadı."
    except Exception as e:
        return None, "", str(e)

def execute_queued_scripts():
    """Blender main thread üzerinde kuyruktaki komutları çalıştırır."""
    global last_status_message, last_executed_script
    
    while not script_queue.empty():
        item = script_queue.get()
        payload, conn = item
        response_dict = {"status": "ERROR", "message": "Bilinmeyen istek"}
        
        try:
            # İstek JSON mu yoksa düz metin/token mı kontrol et
            is_json = False
            req_data = {}
            try:
                req_data = json.loads(payload)
                is_json = True
            except:
                pass
                
            token = ""
            action = "execute"
            code = ""
            
            if is_json:
                token = req_data.get("token", "")
                action = req_data.get("action", "execute")
                code = req_data.get("code", "")
            else:
                code = payload
                if payload.startswith("YENGI_TOKEN:"):
                    lines = payload.split("\n", 1)
                    token = lines[0].replace("YENGI_TOKEN:", "").strip()
                    code = lines[1] if len(lines) > 1 else ""
                    
            # Token Kontrolü
            if SECRET_TOKEN and token != SECRET_TOKEN:
                response_dict = {
                    "status": "ERROR",
                    "message": "Yetkisiz Erişim: Geçersiz Güvenlik Tokenı!"
                }
                last_status_message = "❌ Güvenlik Hatası: Geçersiz token reddedildi."
                print(f"[Yengi Blender Copilot] {last_status_message}")
                conn.sendall(json.dumps(response_dict).encode('utf-8'))
                conn.close()
                continue
                
            # EYLEM 1: Sahne Bilgilerini Getir
            if action == "get_scene_info":
                scene_info = get_scene_context_summary()
                response_dict = {
                    "status": "SUCCESS",
                    "message": "Sahne bilgileri başarıyla alındı.",
                    "data": { "scene_info": scene_info }
                }
                last_status_message = "📊 Sahne durumu Yengi'ye aktarıldı."
                
            # EYLEM 2: Ekran Görüntüsü Al
            elif action == "capture_viewport":
                img_path, img_b64, msg = capture_viewport_screenshot()
                if img_b64:
                    response_dict = {
                        "status": "SUCCESS",
                        "message": "Viewport görüntüsü alındı.",
                        "data": {
                            "viewport_image_path": img_path,
                            "viewport_image_b64": img_b64
                        }
                    }
                    last_status_message = "📸 Viewport ekran görüntüsü alındı."
                else:
                    response_dict = {
                        "status": "ERROR",
                        "message": f"Ekran görüntüsü hatası: {msg}"
                    }
                    
            # EYLEM 3: Undo (Geri Al)
            elif action == "undo":
                try:
                    bpy.ops.ed.undo()
                    response_dict = {"status": "SUCCESS", "message": "Son işlem geri alındı."}
                    last_status_message = "↩️ İşlem geri alındı."
                except Exception as ex:
                    response_dict = {"status": "ERROR", "message": f"Undo hatası: {str(ex)}"}
                    
            # EYLEM 4: Python Scripti Çalıştır
            elif action == "execute":
                # Kodu temizle
                bpy_idx = code.find("import bpy")
                if bpy_idx < 0:
                    bpy_idx = code.find("from bpy")
                if bpy_idx >= 0:
                    code = code[bpy_idx:]
                    
                split_lines = code.split("\n")
                cleaned_lines = []
                for line in split_lines:
                    cleaned_line = re.sub(r'^\s*\d+[\.\:]?\s+', '', line)
                    cleaned_lines.append(cleaned_line)
                code = "\n".join(cleaned_lines)
                code = re.sub(r'^```[a-zA-Z]*\n', '', code)
                code = re.sub(r'\n```$', '', code)
                code = code.strip("\ufeff\r\n ")
                
                last_executed_script = code
                
                # Undo Checkpoint Ekle
                try:
                    bpy.ops.ed.undo_push(message="Yengi Script Execution")
                except:
                    pass
                    
                exec_scope = {"bpy": bpy}
                exec(code, exec_scope)
                
                # Güncel sahne bilgisi ve ekran görüntüsü al
                scene_info = get_scene_context_summary()
                img_path, img_b64, _ = capture_viewport_screenshot()
                
                response_dict = {
                    "status": "SUCCESS",
                    "message": "Script başarıyla çalıştırıldı.",
                    "data": {
                        "scene_info": scene_info,
                        "viewport_image_path": img_path,
                        "viewport_image_b64": img_b64
                    }
                }
                last_status_message = "✅ Script başarıyla çalıştırıldı."
                print("[Yengi Blender Copilot] Script çalıştırıldı.")

        except Exception as e:
            tb_str = traceback.format_exc()
            response_dict = {
                "status": "ERROR",
                "message": str(e),
                "traceback": tb_str
            }
            last_status_message = f"❌ Hata: {str(e)}"
            print(f"[Yengi Blender Copilot Hata] {e}\n{tb_str}")
            
        try:
            # İstemciye yanıtı ilet (JSON veya düz metin geriye dönük uyumluluk)
            if is_json:
                res_bytes = json.dumps(response_dict).encode('utf-8')
            else:
                if response_dict["status"] == "SUCCESS":
                    res_bytes = f"SUCCESS: {response_dict.get('message', '')}".encode('utf-8')
                else:
                    res_bytes = f"ERROR: {response_dict.get('message', '')}".encode('utf-8')
                    
            conn.sendall(res_bytes)
            conn.close()
        except:
            pass
            
    return 0.2  # Timer her 0.2 saniyede bir tetiklenir

def socket_listener_thread():
    global server_socket, is_running, last_status_message
    import time
    server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    
    try:
        server_socket.bind(('127.0.0.1', PORT))
        server_socket.listen(5)
        is_running = True
        last_status_message = f"🟢 Sunucu Dinliyor (127.0.0.1:{PORT})"
        print(f"[Yengi Blender Copilot] TCP Socket sunucusu 127.0.0.1:{PORT} üzerinde dinliyor...")
    except Exception as e:
        last_status_message = f"❌ Port Bağlama Hatası ({PORT}): {e}"
        print(f"[Yengi Blender Copilot] Port bağlama hatası ({PORT}): {e}")
        return

    while is_running:
        try:
            conn, addr = server_socket.accept()
            buffer = bytearray()
            while True:
                chunk = conn.recv(8192)
                if not chunk:
                    break
                buffer.extend(chunk)
                if len(chunk) < 8192:
                    # Küçük paketlerde veya veri bittiğinde döngüyü tamamla
                    break
            if buffer:
                payload = buffer.decode('utf-8')
                script_queue.put((payload, conn))
        except Exception:
            if not is_running:
                break
            time.sleep(0.05)
            continue

# --- BLENDER UI PANEL (N-PANEL SIDEBAR) ---

class YengiCopilotProperties(bpy.types.PropertyGroup):
    secret_token: bpy.props.StringProperty(
        name="Güvenlik Tokenı",
        description="Yengi IDE ile eşleşen güvenlik tokenı",
        default=""
    ) # type: ignore

class YENGI_PT_copilot_panel(bpy.types.Panel):
    """Blender 3D Viewport N-Panel Yengi Copilot Sekmesi"""
    bl_label = "🚀 Yengi AI Copilot"
    bl_idname = "YENGI_PT_copilot_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'Yengi Copilot'

    def draw(self, context):
        layout = self.layout
        global last_status_message, is_running
        
        # Sunucu Durumu
        box = layout.box()
        if is_running:
            box.label(text=last_status_message, icon='RADIOBUT_ON')
        else:
            box.label(text="🔴 Sunucu Durduruldu", icon='RADIOBUT_OFF')
            
        # Hızlı İşlem Butonları
        row = layout.row(align=True)
        row.operator("yengi.capture_viewport", text="📸 Viewport Resmi Al", icon='CAMERA_DATA')
        row.operator("yengi.show_scene_info", text="📊 Sahne Özetini Yazdır", icon='INFO')
        
        layout.separator()
        layout.label(text="Son Komut Durumu:", icon='CONSOLE')
        layout.label(text=last_status_message)

class YENGI_OT_capture_viewport(bpy.types.Operator):
    """3D Viewport ekran görüntüsünü alıp geçici klasöre kaydeder"""
    bl_idname = "yengi.capture_viewport"
    bl_label = "Viewport Ekran Görüntüsü Al"

    def execute(self, context):
        filepath, b64, msg = capture_viewport_screenshot()
        if b64:
            self.report({'INFO'}, f"Viewport görüntüsü kaydedildi: {filepath}")
        else:
            self.report({'ERROR'}, f"Hata: {msg}")
        return {'FINISHED'}

class YENGI_OT_show_scene_info(bpy.types.Operator):
    """Sahne özetini Blender konsoluna yazdırır"""
    bl_idname = "yengi.show_scene_info"
    bl_label = "Sahne Bilgilerini Göster"

    def execute(self, context):
        info = get_scene_context_summary()
        print(f"\n--- [YENGI SAHNE ÖZETİ] ---\n{json.dumps(info, indent=2)}\n-------------------------\n")
        self.report({'INFO'}, f"Sahne Özeti: {info.get('object_count', 0)} nesne, {len(info.get('materials', []))} materyal")
        return {'FINISHED'}

classes = (
    YengiCopilotProperties,
    YENGI_PT_copilot_panel,
    YENGI_OT_capture_viewport,
    YENGI_OT_show_scene_info,
)

def register():
    print("[Yengi Blender Copilot] Eklenti kaydediliyor...")
    for cls in classes:
        bpy.utils.register_class(cls)
        
    bpy.types.Scene.yengi_props = bpy.props.PointerProperty(type=YengiCopilotProperties)
    
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
        
    for cls in reversed(classes):
        bpy.utils.unregister_class(cls)
        
    del bpy.types.Scene.yengi_props
    print("[Yengi Blender Copilot] Eklenti durduruldu.")

if __name__ == "__main__":
    register()
