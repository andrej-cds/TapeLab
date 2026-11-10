# Contributing to CDS TapeLab

Thank you for your interest. This project is made on a repair bench, so practical feedback matters most.

## Report a problem

Open an [issue](https://github.com/andrej-cds/TapeLab/issues/new/choose) and tell me:

- the edition (Windows 10/11, Windows XP, macOS) and the program version (About window),
- the sound card and driver (the start screen shows them at the bottom),
- what you did, what you expected and what happened; a screenshot helps,
- on Windows XP: the file `%AppData%\CDSTapeLab\log.txt`.

## Suggest a measurement or improvement

Use the *Idea* issue template. Describe how you would use it on the bench.

## Send code

1. Fork the repository and create a branch.
2. Windows 10/11 lives in `windows/`, Windows XP in `windows-xp/`. Both share the structure `Core` (signal processing), `Controls` (charts and meters), `Views` (screens).
3. Keep changes small and focused. Do not mix a refactor with a new feature.
4. Test with a real sound card or a recorded signal when you touch audio or measurements.
5. Open a pull request. GitHub Actions builds every edition on each pull request.

By contributing you agree that your work is licensed under the [AGPL-3.0](LICENSE).

## Language

The interface is Slovenian and English (`Core/Loc.cs`). Please add both when you add a text.
