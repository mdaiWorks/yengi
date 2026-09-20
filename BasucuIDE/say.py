import os

def list_files_in_script_dir():
    # Scriptin bulunduğu klasör
    script_dir = os.path.dirname(os.path.abspath(__file__))
    
    # Klasördeki tüm öğeleri al
    items = os.listdir(script_dir)
    
    # Sadece dosyaları filtrele (klasörleri hariç tut)
    files = [f for f in items if os.path.isfile(os.path.join(script_dir, f))]
    
    return files

# Örnek kullanım
dosyalar = list_files_in_script_dir()

print("Script klasöründeki dosyalar:")
for d in dosyalar:
    print(d)
