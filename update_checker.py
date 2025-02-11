import requests
import os
import subprocess
import winreg  # For checking WinRAR in registry (Windows-only)

# GitHub repository details
repo_owner = "DataCluster0"
repo_name = "HeatedMetal"
release_file = "HeatedMetal.7z"
versions_file = "versions.txt"
api_url = f"https://api.github.com/repos/{repo_owner}/{repo_name}/releases/latest"

# =============================================================================
# Extraction Tool Detection
# =============================================================================
def find_winrar():
    """Check if WinRAR is installed via registry or default paths."""
    try:
        # Check registry (common installation path)
        key = winreg.OpenKey(
            winreg.HKEY_LOCAL_MACHINE,
            r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WinRAR archiver"
        )
        install_path, _ = winreg.QueryValueEx(key, "InstallLocation")
        winreg.CloseKey(key)
        winrar_exe = os.path.join(install_path, "WinRAR.exe")
        if os.path.isfile(winrar_exe):
            return winrar_exe
    except FileNotFoundError:
        pass

    # Check common paths if registry fails
    common_paths = [
        os.path.expandvars("%ProgramFiles%\\WinRAR\\WinRAR.exe"),
        os.path.expandvars("%ProgramFiles(x86)%\\WinRAR\\WinRAR.exe")
    ]
    for path in common_paths:
        if os.path.isfile(path):
            return path

    # Check PATH environment variable
    for path_dir in os.environ["PATH"].split(os.pathsep):
        possible_path = os.path.join(path_dir, "WinRAR.exe")
        if os.path.isfile(possible_path):
            return possible_path

    return None

def find_7z():
    """Check if 7-Zip is installed via default paths or PATH."""
    # Check default installation paths
    common_paths = [
        os.path.expandvars("%ProgramFiles%\\7-Zip\\7z.exe"),
        os.path.expandvars("%ProgramFiles(x86)%\\7-Zip\\7z.exe")
    ]
    for path in common_paths:
        if os.path.isfile(path):
            return path

    # Check PATH environment variable
    for path_dir in os.environ["PATH"].split(os.pathsep):
        possible_path = os.path.join(path_dir, "7z.exe")
        if os.path.isfile(possible_path):
            return possible_path

    return None

# =============================================================================
# Core Script Logic
# =============================================================================
def get_latest_release_tag():
    print("Fetching latest release information from GitHub...")
    response = requests.get(api_url)
    response.raise_for_status()
    tag_name = response.json()['tag_name']
    print(f"Latest release tag: {tag_name}")
    return tag_name

def read_local_version():
    print("Checking local version...")
    if os.path.exists(versions_file):
        with open(versions_file, 'r') as file:
            local_version = file.read().strip()
            print(f"Local version: {local_version}")
            return local_version
    print("No local version found.")
    return None

def download_release(asset_url, tag):
    print(f"Downloading release {tag}...")
    with requests.get(asset_url, stream=True) as r:
        r.raise_for_status()
        with open(release_file, 'wb') as f:
            for chunk in r.iter_content(chunk_size=8192):
                f.write(chunk)
    print(f"Download complete: {release_file}")
    with open(versions_file, 'w') as file:
        file.write(tag)
    print(f"Updated {versions_file} with new version: {tag}")

def extract_release():
    print("Extracting release...")
    if not os.path.exists(release_file):
        print(f"Error: {release_file} not found!")
        return

    # Prioritize WinRAR
    winrar_path = find_winrar()
    if winrar_path:
        print(f"Using WinRAR at: {winrar_path}")
        try:
            subprocess.run([winrar_path, 'x', release_file], check=True)
            print("Extraction with WinRAR succeeded!")
            return
        except subprocess.CalledProcessError as e:
            print(f"WinRAR extraction failed: {e}")

    # Fallback to 7-Zip
    seven_zip_path = find_7z()
    if seven_zip_path:
        print(f"Using 7-Zip at: {seven_zip_path}")
        try:
            subprocess.run([seven_zip_path, 'x', release_file], check=True)
            print("Extraction with 7-Zip succeeded!")
            return
        except subprocess.CalledProcessError as e:
            print(f"7-Zip extraction failed: {e}")
    else:
        print("Error: Neither WinRAR nor 7-Zip found!")
        print("Install WinRAR or 7-Zip and ensure they are in PATH.")

    raise RuntimeError("Extraction failed. No valid tool detected.")

def main():
    print("Starting update check...")
    try:
        latest_tag = get_latest_release_tag()
        local_version = read_local_version()

        if local_version != latest_tag:
            print(f"New version available: {latest_tag}")
            asset_url = f"https://github.com/{repo_owner}/{repo_name}/releases/download/{latest_tag}/{release_file}"
            download_release(asset_url, latest_tag)
            extract_release()
        else:
            print("Already on the latest version.")
    except Exception as e:
        print(f"Error: {e}")
    input("Press Enter to exit...")

if __name__ == "__main__":
    main()