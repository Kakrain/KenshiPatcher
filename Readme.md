# KenshiPatcher

**A powerful, rule-based patching and merging tool for Kenshi mods.**

KenshiPatcher allows mod authors to create flexible patch definitions that automatically inspect, filter, modify, and merge records from installed Kenshi mods.

Instead of manually creating compatibility patches for every possible combination of mods, you can define reusable rules in `.patch` files and let KenshiPatcher generate the resulting `.mod` files.

Whether you're handling races, animations, game data, or complex record conflicts, KenshiPatcher gives you the tools to automate the process.

---

## ✨ Features

### Record Processing
- Inspect and filter records using flexible conditions.
- Check whether fields exist, are empty, or contain specific values.
- Traverse and manipulate nested record data.
- Process records from multiple installed mods.

### Patch Language
KenshiPatcher includes its own flexible `.patch` scripting language.

- Variables and definitions.
- Functions and custom procedures.
- Lambda expressions.
- Conditional execution.
- Loops and iteration.
- Array manipulation.
- String and regular expression operations.
- Bitwise operations.
- Custom functions for working with Kenshi records.

### Record Manipulation
- Modify existing records and their fields.
- Merge conflicting records from different mods.
- Inspect and manipulate ExtraData.
- Work with arrays and nested structures.
- Apply conditional modifications based on record contents.

### Compatibility & Safety
- Supports Kenshi `.mod` file formats **v15, v16 and v17**.
- Generates patches with the required dependencies and references.
- Works with any kind of record.
- Leaves original mods and game files untouched.

---

## ⚙️ Requirements

- Windows 64-bit.
- Microsoft Visual C++ 2015–2022 Redistributable (x64).

No additional frameworks are required. Simply run the executable.

---

## 🚀 Installation

1. Download the latest release from [GitHub Releases](../../releases).
2. Extract the downloaded archive.
3. Install the Microsoft Visual C++ Redistributable if you don't already have it.
4. Run `KenshiPatcher.exe`.

Alternatively, clone the repository and build the solution using Visual Studio.

---

## 📝 Usage

KenshiPatcher uses `.patch` files to define how records should be processed.

### 1. Create a patch definition

Create a `.patch` file containing your patch rules.

For example:

```text
allraces:=(all)(A:RACE|true)
allraces->SetField("swim speed",100)