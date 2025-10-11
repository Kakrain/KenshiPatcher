# Kenshi Patcher

A tool for safely patching and merging records in **Kenshi** mods.

Kenshi Patcher allows you to define patch rules to automatically filter and merge mod records, such as races, animations, and other game data, producing `.mod` files that can be safely loaded in-game.

---

## 📌 Features

- Filter records based on flexible conditions (e.g., field existence, emptiness).
- Automatically merge conflicting records across multiple mods, preferring records from specified sources.
- Supports `.mod` files for **v16** and **v17** of the game.
- Generates patch files with correct dependencies and references.
- Works with both humanoid and animal records, ensuring animations are applied only where appropriate.

---

## ⚙️ Requirements

- **Windows 64-bit**
- **Microsoft Visual C++ 2015-2022 Redistributable (x64)**

No additional frameworks required — just run the executable.

---

## 🚀 Installation & Usage

1. Clone or download the repository.  
2. Open the solution in Visual Studio and build 'KenshiPatcher.exe', or download from the Releases page if available.  
3. Prepare a patch definition file ('.patch') using the patcher syntax, e.g.:

allraces:=(all)(A:RACE|!field_is_empty_or_not_exist("attachment points"))

4. Run the patcher:
5. Find your patch and click on Patch it!
6. Enjoy 

---

## 🛠 License

MIT License — see the [LICENSE](LICENSE) file for details.

---

## 📂 Notes

This tool is designed for modders who want to safely merge and patch records across multiple Kenshi mods. Future updates may include expanded filtering capabilities and more advanced record management features.