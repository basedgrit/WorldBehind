## Purpose
Short, actionable guidance for AI assistants editing this Unity project. Focus on concrete, discoverable code patterns, the important runtime invariants, and safe changes that won't break Inspector wiring or scenes.

## Big picture
- Unity C# scripts live under `Assets/Scripts`. The visible script is `Assets/Scripts/PhotoCapture.cs` which demonstrates the core runtime flow:
	- A camera or camera output writes into a `RenderTexture`.
	- `PhotoCapture.CapturePhoto()` runs as a coroutine, waits for end of frame, reads pixels from the `RenderTexture` into a new `Texture2D`, and assigns that texture to UI (`RawImage`) and a `Material`.

## Concrete patterns to follow (copy-paste safe)
- Inspector-exposed public fields are assumed to be wired in scenes: `photoRenderTexture`, `photoPreview`, `photoMaterial`. If you rename or remove them, update scene/prefab references.
- Capture coroutine pattern (must keep): `yield return new WaitForEndOfFrame();` -> set `RenderTexture.active` to source -> create `Texture2D` -> `ReadPixels` -> `Apply()` -> restore previous `RenderTexture.active`.
- Assign captured texture to UI and material: set `photoPreview.texture = capturedPhoto;` and `photoMaterial.mainTexture = capturedPhoto;` — but only if `photoMaterial` is not null.

## Concrete gotchas and how to fix them
- Unprotected material assignment: current `CapturePhoto()` logs an error when `photoMaterial` is null but still runs `photoMaterial.mainTexture = capturedPhoto;` afterward. When changing this method, ensure the assignment is performed only if `photoMaterial != null` (move the assignment into the if-branch or add a guard).
- Nested MonoBehaviour: `PhotoJournal` is declared as a nested class inside `PhotoCapture.cs` (`public class PhotoJournal : MonoBehaviour`). Unity prefers top-level MonoBehaviour classes. When refactoring, move `PhotoJournal` to its own `PhotoJournal.cs` file (class name must match file name) and update any scene components that reference it.

## Suggested safe edits (examples you can apply directly)
- Fix NullReference (prose): In `CapturePhoto()` remove the unconditional `photoMaterial.mainTexture = capturedPhoto;` after logging; instead, set `photoMaterial.mainTexture = capturedPhoto;` inside the `if (photoMaterial != null) { ... }` branch.
- Extract `PhotoJournal` (steps): create `Assets/Scripts/PhotoJournal.cs` with the same class body, keep the `journalSlots` List<RawImage> public, and search scenes for the nested type references (Unity will show missing script icons if not updated). Update references in scenes if needed.

## Developer workflow notes
- Validate changes in Unity Editor: open project in the Editor, press Play, exercise the capture (left mouse click triggers `StartCoroutine(CapturePhoto())`), and watch Console for `Debug.Log` / `Debug.LogError` output.
- No automated CI or tests detected — manual Editor validation is required. Small edits should be opened in Editor and tested in Play mode.

## PR & change rules for AI edits
- Make small, focused commits that either fix a single bug (e.g., null-guard) or perform a single refactor (e.g., move `PhotoJournal` to its own file). In PR description mention which scenes/prefabs must be updated if inspector names changed.
- When renaming or removing Inspector fields, include an explicit list of affected GameObjects (or supply a scene patch) to avoid runtime missing references.

## If you need more context
- To fully validate refactors, open the Unity scenes that use these components. If you can't run Unity locally, ask the human to list the scene/prefab names that reference `PhotoCapture`/`PhotoJournal` and I will update instructions/patches accordingly.

---
Tell me which of these you'd like me to apply now: (A) safe null-guard patch to `CapturePhoto()`, (B) extract `PhotoJournal` to `PhotoJournal.cs`, or (C) both. I can apply code changes and run quick verification steps.
