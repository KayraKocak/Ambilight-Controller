import os
import subprocess
import sys
import threading
import time
import tkinter as tk
from tkinter import ttk

class UpdateApp:
    def __init__(self, root):
        self.root = root
        self.root.title("System Update")
        self.root.geometry("450x180")
        self.root.resizable(False, False)
        
        # Premium dark theme styling
        self.bg_color = "#1e1e24"
        self.fg_color = "#e2e8f0"
        self.accent_color = "#06b6d4"  # Cyan
        self.panel_color = "#27272a"
        
        self.root.configure(bg=self.bg_color)
        
        # Center the window on the screen
        screen_width = self.root.winfo_screenwidth()
        screen_height = self.root.winfo_screenheight()
        x = (screen_width - 450) // 2
        y = (screen_height - 180) // 2
        self.root.geometry(f"450x180+{x}+{y}")
        
        # Custom styles
        style = ttk.Style()
        style.theme_use('default')
        style.configure("TProgressbar", 
                        thickness=8, 
                        troughcolor=self.panel_color, 
                        background=self.accent_color,
                        bordercolor=self.bg_color,
                        lightcolor=self.accent_color,
                        darkcolor=self.accent_color)
        
        # Labels and Layout
        self.title_label = tk.Label(
            self.root, 
            text="Ambilight Controller Update", 
            font=("Segoe UI", 14, "bold"), 
            bg=self.bg_color, 
            fg=self.accent_color
        )
        self.title_label.pack(pady=(20, 5))
        
        self.status_label = tk.Label(
            self.root, 
            text="Initializing update...", 
            font=("Segoe UI", 10), 
            bg=self.bg_color, 
            fg=self.fg_color
        )
        self.status_label.pack(pady=(5, 15))
        
        self.progress = ttk.Progressbar(
            self.root, 
            orient="horizontal", 
            length=350, 
            mode="determinate",
            style="TProgressbar"
        )
        self.progress.pack(pady=5)
        
        # Start update task in background
        threading.Thread(target=self.perform_update, daemon=True).start()

    def update_status(self, text, value):
        self.status_label.config(text=text)
        self.progress['value'] = value
        self.root.update_idletasks()

    def download_zip_fallback(self):
        import urllib.request
        import zipfile
        import shutil

        self.update_status("Git missing. Downloading update ZIP...", 40)
        zip_url = "https://github.com/KayraKocak/Ambilight-Controller/archive/refs/heads/main.zip"
        zip_path = "update_temp.zip"

        # Download ZIP
        urllib.request.urlretrieve(zip_url, zip_path)

        self.update_status("Extracting files...", 70)
        # Extract ZIP
        with zipfile.ZipFile(zip_path, 'r') as zip_ref:
            zip_ref.extractall("update_temp_extracted")

        self.update_status("Applying files...", 85)
        # Move files to root (overwriting local ones except ignored ones)
        extracted_dir = "update_temp_extracted/Ambilight-Controller-main"
        if not os.path.exists(extracted_dir):
            if os.path.exists("update_temp_extracted"):
                extracted_dirs = os.listdir("update_temp_extracted")
                if extracted_dirs:
                    extracted_dir = os.path.join("update_temp_extracted", extracted_dirs[0])

        for root_dir, dirs, files in os.walk(extracted_dir):
            rel_path = os.path.relpath(root_dir, extracted_dir)
            target_dir = "." if rel_path == "." else os.path.join(".", rel_path)
            if not os.path.exists(target_dir):
                os.makedirs(target_dir)
            for file in files:
                src_file = os.path.join(root_dir, file)
                dst_file = os.path.join(target_dir, file)

                # Exclude local-only scripts & logs
                if file in ["push_worker.py", "push.bat", "update_error.txt"]:
                    continue

                # Copy and overwrite
                shutil.copy2(src_file, dst_file)

        # Cleanup
        try:
            shutil.rmtree("update_temp_extracted")
            os.remove(zip_path)
        except Exception as cleanup_err:
            print(f"Cleanup warning: {cleanup_err}")


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

    def perform_update(self):
        try:
            # Step 1: Wait a tiny bit for the main C# application to fully close
            time.sleep(1.5)
            self.create_backup()

            # Delete version.txt before pulling the update to guarantee a fresh copy
            if os.path.exists("version.txt"):
                try:
                    os.remove("version.txt")
                except Exception as e:
                    print(f"Failed to delete version.txt: {e}")

            # Check if Git is installed on the system
            import shutil as python_shutil
            git_installed = python_shutil.which("git") is not None

            if not git_installed:
                # Fallback to direct zip download
                self.download_zip_fallback()
            else:
                # If git repository is missing, initialize and configure it automatically
                if not os.path.exists(".git"):
                    self.update_status("Initializing Git repository...", 45)
                    subprocess.run(
                        ["git", "init"],
                        creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
                    )
                    subprocess.run(
                        ["git", "remote", "add", "origin", "https://github.com/KayraKocak/Ambilight-Controller.git"],
                        creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
                    )
                    subprocess.run(
                        ["git", "branch", "-M", "main"],
                        creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
                    )

                # Step 2: Fetch and reset local files to remote state (ensures clean overwrite without merge conflicts)
                env = os.environ.copy()
                env["GIT_TERMINAL_PROMPT"] = "0"

                self.update_status("Fetching updates from GitHub...", 50)
                fetch_result = subprocess.run(
                    ["git", "fetch", "origin"],
                    capture_output=True,
                    text=True,
                    env=env,
                    creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
                )

                if fetch_result.returncode != 0:
                    print(fetch_result.stderr)
                    self.update_status("Error: Fetch failed. Checking connection...", 60)
                    time.sleep(2)
                    # Fallback fetch
                    subprocess.run(["git", "fetch"], env=env, creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)

                self.update_status("Applying update...", 75)
                # Force local tracked files to match the remote main branch exactly (restores version.txt)
                reset_result = subprocess.run(
                    ["git", "reset", "--hard", "origin/main"],
                    capture_output=True,
                    text=True,
                    env=env,
                    creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
                )

                if reset_result.returncode != 0:
                    # Try master branch as fallback
                    reset_result = subprocess.run(
                        ["git", "reset", "--hard", "origin/master"],
                        capture_output=True,
                        text=True,
                        env=env,
                        creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
                    )
                    if reset_result.returncode != 0:
                        raise Exception(reset_result.stderr)

            self.update_status("Update applied successfully! Relaunching...", 90)
            time.sleep(1.0)

            # Step 3: Relaunch the application
            if os.path.exists("run.bat"):
                subprocess.Popen(
                    ["cmd", "/c", "start", "run.bat"],
                    shell=True,
                    creationflags=subprocess.CREATE_NEW_CONSOLE
                )
            else:
                self.update_status("Warning: run.bat not found. Restart manually.", 100)
                time.sleep(3)

            self.update_status("Done", 100)

        except Exception as e:
            import traceback
            error_msg = f"Update failed: {str(e)}\n\nTraceback:\n{traceback.format_exc()}"
            try:
                with open("update_error.txt", "w") as log_file:
                    log_file.write(error_msg)
            except Exception as write_err:
                print(f"Failed to write log file: {write_err}")

            self.update_status(f"Update failed: {str(e)[:45]}...", 100)
            self.title_label.config(fg="#ef4444", text="Update Failed")
            time.sleep(5)

        finally:
            self.root.destroy()

def main():
    root = tk.Tk()
    app = UpdateApp(root)
    root.mainloop()

if __name__ == "__main__":
    main()
