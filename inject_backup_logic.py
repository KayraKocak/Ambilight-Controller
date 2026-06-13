import re
import os

with open('updater.py', 'r', encoding='utf-8') as f:
    code = f.read()

backup_func = """
    def create_backup(self):
        import zipfile
        import glob
        
        self.update_status("Creating local backup...", 10)
        
        if not os.path.exists("backups"):
            os.makedirs("backups")
            
        # Get local version
        version = "unknown"
        if os.path.exists("version.txt"):
            with open("version.txt", "r") as f:
                content = f.read()
                import re
                m = re.search(r"version:\s*([\d\.]+)", content)
                if m:
                    version = m.group(1)
                    
        import time
        timestamp = int(time.time())
        zip_filename = f"backups/backup_v{version}_{timestamp}.zip"
        
        # Excludes
        exclude_dirs = {".git", "bin", "obj", "backups", "update_temp_extracted"}
        exclude_files = {"update_temp.zip", "update_error.txt"}
        
        with zipfile.ZipFile(zip_filename, 'w', zipfile.ZIP_DEFLATED) as zipf:
            for root, dirs, files in os.walk("."):
                # Prune excluded dirs
                dirs[:] = [d for d in dirs if d not in exclude_dirs]
                
                for file in files:
                    if file in exclude_files:
                        continue
                    if file.endswith(".zip"):
                        continue
                    
                    file_path = os.path.join(root, file)
                    arcname = os.path.relpath(file_path, ".")
                    zipf.write(file_path, arcname)
                    
        self.update_status("Backup created.", 20)
        
        # Retention policy: keep 3 latest
        zips = sorted(glob.glob("backups/*.zip"), key=os.path.getmtime)
        while len(zips) > 3:
            oldest = zips.pop(0)
            try:
                os.remove(oldest)
            except:
                pass
"""

if "def create_backup(self):" not in code:
    code = code.replace("    def perform_update(self):", backup_func + "\n    def perform_update(self):")
    code = code.replace("time.sleep(1.5)", "time.sleep(1.5)\n            self.create_backup()")

    with open('updater.py', 'w', encoding='utf-8') as f:
        f.write(code)

    print("Injected backup logic!")
else:
    print("Logic already exists")
