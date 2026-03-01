## Plan: Fix SingletonSO Quitting flag not resetting on scene restart

**TL;DR:** When the scene restarts via `SceneManager.LoadScene()`, Unity calls `OnDisable()` on ScriptableObjects during teardown. `SingletonSO.OnDisable()` sets the **static** `Quitting = true` — but nothing ever resets it to `false`. After that, every `SingletonSO<T>.Instance` getter early-returns `null`. The generated accessor `ListenEvent.Chat.HideChat` then calls `.GetDefinition()` on that `null`, producing the NRE in `Portal.Start()`.

**Steps**

1. **Add `[RuntimeInitializeOnLoadMethod]` reset to `SingletonSO`** (the non-generic base class) in `SingletonSO.cs`. Add a static method with `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` that sets `Quitting = false`. This is the earliest Unity callback and fires on every play-mode entry and domain reload — it's specifically designed for resetting static state.

2. **Replace `OnDisable` quit detection with `Application.quitting`** in the same base class. `OnDisable()` is too broad — it fires on recompile, inspector changes, and scene teardowns, not just app quit. Instead, subscribe to `Application.quitting += () => Quitting = true` in an `OnEnable()` override (and unsubscribe in `OnDisable()` to avoid duplicates). Remove the current `Application.isPlaying` check from `OnDisable`.

3. **Apply the same `[RuntimeInitializeOnLoadMethod]` fix to `Singleton` (MonoBehaviour)** in `Singleton.cs`. Add `ResetStatics()` with `SubsystemRegistration` to reset `Quitting = false`. The existing `OnApplicationQuit` approach is fine here since MonoBehaviours don't get `OnDisable` during scene transitions the same way, but the reset is needed for disabled-domain-reload scenarios.

4. **Fix `BaseRegistry.OnEnable()` to call `base.OnEnable()`** in `BaseRegistry.cs`. Currently it overrides `OnEnable()` without calling `base.OnEnable()`, which skips the `OnAwake()` chain defined in `SingletonSO<T>`. `EventCoordinator` initializes its dictionaries in `OnAwake()`, and this missing call could cause secondary issues. Change to:
   ```csharp
   protected override void OnEnable() {
       base.OnEnable();
       InitializeLookup();
   }
   ```

5. **(Optional but recommended) Add null-safety to generated event accessors.** The code generator that produces files like `Chat.ListenEvent.Generated.cs` emits `EventCoordinator.Instance.GetDefinition(...)` with no null guard. If you control the code generator, consider emitting `EventCoordinator.Instance?.GetDefinition(...)` so a null instance degrades gracefully (returns null/default) rather than throwing an NRE.

**Verification**

- Enter Play Mode, trigger a scene restart via `GameRestartManager` (the `GameRestartReady` event path), and confirm no NREs appear in the console
- Add a `Debug.Log($"Quitting={SingletonSO.Quitting}")` temporarily in `Portal.Start()` to verify the flag is `false` after restart
- Test exiting Play Mode and re-entering — confirm no stale `Quitting = true` persists
- If you have a build pipeline, verify `EventCoordinator` loads correctly in builds (note: the `EventCoordinator` asset is not in a Resources folder, so the `Instance` getter will fail in builds — this is a separate issue worth addressing later)

**Decisions**
- Fix goes in ProxyCore package source (steps 1-4) rather than working around it in game code — the bug is architectural
- Use `SubsystemRegistration` load type, not `BeforeSceneLoad`, because it's the earliest callback and specifically targets static state reset
- Use `Application.quitting` event instead of `OnDisable` for quit detection — more precise and doesn't false-positive on scene transitions
