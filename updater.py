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

    def perform_update(self):
        try:
            # Step 1: Wait a tiny bit for the main C# application to fully close
            time.sleep(1.5)
            
            # Delete version.txt before pulling the update to guarantee a fresh copy
            if os.path.exists("version.txt"):
                try:
                    os.remove("version.txt")
                except Exception as e:
                    print(f"Failed to delete version.txt: {e}")
            
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
