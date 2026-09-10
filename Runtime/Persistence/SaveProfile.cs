using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ProxyCore {
    /// <summary>
    /// Partitions ProxyCore-owned state (unlock keys, lock overrides, flag collections) under a
    /// caller-supplied save-profile id.
    ///
    /// <b>Ownership boundary.</b> The host game owns the save-game universe: what a save is, what
    /// identifies one, when to save and load, what metadata a save-select screen shows, and which
    /// save is active at boot. ProxyCore owns only its own state and keeps it correctly
    /// partitioned under whichever profile the game declares active. A profile id is an opaque
    /// key — ProxyCore assigns it no meaning, stores no metadata about it, and never remembers
    /// which one was active across launches. The game re-selects on every boot.
    ///
    /// <b>Layout.</b> With a profile active, ProxyCore's files live in one directory per profile:
    /// <code>
    /// {persistentDataPath}/proxycore/{profileId}/
    ///     unlocks.json        ← saved unlock keys + lock overrides
    ///     flags_{name}.json   ← one per GameFlagCollection
    /// </code>
    /// With no profile active (the default), the legacy flat paths
    /// <c>{persistentDataPath}/unlocks.json</c> and <c>flags_{name}.json</c> are used unchanged,
    /// so a project that never touches this API behaves exactly as before.
    ///
    /// <b>Typical use.</b>
    /// <code>
    /// string id = SaveProfile.Id(playerSlot, difficulty);   // injective, filename-safe
    /// SaveProfile.SetActive(id);                            // flush → switch → reload → evaluate
    /// </code>
    /// </summary>
    public static class SaveProfile {
        /// <summary>Folder under persistentDataPath that holds every profile directory.</summary>
        public const string ROOT_FOLDER = "proxycore";

        /// <summary>File name used for unlock state inside a profile root.</summary>
        public const string UNLOCKS_FILE = "unlocks.json";

        /// <summary>Search pattern matching flag-collection files inside a profile root.</summary>
        public const string FLAGS_PATTERN = "flags_*.json";

        /// <summary>
        /// Longest permitted profile id. Keeps the composed path clear of platform path limits.
        /// </summary>
        public const int MAX_ID_LENGTH = 128;

        private const char SEGMENT_SEPARATOR = '_';

        private static string _active = "";
        private static readonly List<IProfileScopedStore> _stores = new List<IProfileScopedStore>();

        // ────────────────────────────────────────────────────────────────────────
        // State
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Active profile id, or the empty string when no profile has been selected (in which
        /// case ProxyCore reads and writes the legacy flat paths).
        /// Resets on domain reload — the game sets it as part of loading a save.
        /// </summary>
        public static string Active => _active;

        /// <summary>Directory holding the active profile's ProxyCore files.</summary>
        public static string ActiveRoot => RootFor(_active);

        /// <summary>
        /// When true (the default), every unlock/flag mutation writes to disk immediately.
        /// Set false to batch writes and call <see cref="Save"/> at controlled moments; explicit
        /// saves and profile switches still flush regardless of this setting.
        /// </summary>
        public static bool AutoSave { get; set; } = true;

        /// <summary>
        /// Raised after the active profile changed and every registered ProxyCore store has
        /// reloaded. The argument is the new profile id ("" for the default profile).
        /// Subscribe to swap game-owned state in tandem instead of ordering calls by hand.
        /// </summary>
        public static event Action<string> ProfileChanged;

        // ────────────────────────────────────────────────────────────────────────
        // Id composition
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Composes a profile id from any number of opaque segments.
        ///
        /// The composition is <b>injective</b>: two different segment lists never produce the
        /// same id, so <c>Id("a_b", "c")</c> and <c>Id("a", "b_c")</c> stay distinct. Every
        /// character outside <c>[a-z0-9-]</c> is percent-encoded from its UTF-8 bytes, which
        /// makes the result filename-safe on every platform, immune to case-insensitive
        /// filesystems collapsing two ids, and free of path separators or <c>..</c> traversal.
        ///
        /// ProxyCore attaches no meaning to the segments — how many there are and what they
        /// represent is entirely the caller's business.
        /// </summary>
        /// <param name="segments">One or more opaque, non-null strings. Empty strings are legal.</param>
        /// <exception cref="ArgumentException">
        /// No segments given, a segment is null, or the composed id exceeds
        /// <see cref="MAX_ID_LENGTH"/>.
        /// </exception>
        public static string Id(params string[] segments) {
            if (segments == null || segments.Length == 0)
                throw new ArgumentException("At least one segment is required.", nameof(segments));

            var sb = new StringBuilder();
            for (int i = 0; i < segments.Length; i++) {
                if (segments[i] == null)
                    throw new ArgumentException($"Segment {i} is null.", nameof(segments));
                if (i > 0) sb.Append(SEGMENT_SEPARATOR);
                Encode(segments[i], sb);
            }

            string id = sb.ToString();
            if (id.Length > MAX_ID_LENGTH) {
                throw new ArgumentException(
                    $"Composed profile id is {id.Length} characters, over the {MAX_ID_LENGTH} limit. " +
                    "Use shorter segments, or map long names to short keys in your own save index.",
                    nameof(segments));
            }
            return id;
        }

        // The separator is itself encoded inside a segment (as %5F), so joining is unambiguous
        // and the decomposition of an id back into segments is unique.
        private static void Encode(string segment, StringBuilder sb) {
            foreach (byte b in Encoding.UTF8.GetBytes(segment)) {
                char c = (char)b;
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')
                    sb.Append(c);
                else
                    sb.Append('%').Append(b.ToString("X2"));
            }
        }

        /// <summary>
        /// True when the string is already in the encoded form produced by <see cref="Id"/>,
        /// and can therefore be used as a directory name verbatim.
        /// </summary>
        private static bool IsComposedId(string id) {
            foreach (char c in id) {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                          || c == '-' || c == SEGMENT_SEPARATOR || c == '%';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>
        /// Accepts either an id from <see cref="Id"/> (used verbatim) or an arbitrary string
        /// (encoded as a single segment). Null/whitespace means the default profile.
        /// </summary>
        private static string Normalize(string profileId) {
            if (string.IsNullOrWhiteSpace(profileId)) return "";
            return IsComposedId(profileId) ? profileId : Id(profileId);
        }

        // ────────────────────────────────────────────────────────────────────────
        // Paths
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Directory holding a profile's ProxyCore files. The empty id maps to
        /// persistentDataPath itself, which is where the pre-profile layout lives.
        /// </summary>
        public static string RootFor(string profileId) {
            string id = Normalize(profileId);
            return string.IsNullOrEmpty(id)
                ? Application.persistentDataPath
                : Path.Combine(Application.persistentDataPath, ROOT_FOLDER, id);
        }

        /// <summary>
        /// Resolves a ProxyCore file name against the active profile root. Stores use this so
        /// they never hard-code a layout.
        /// </summary>
        public static string PathFor(string fileName) => Path.Combine(ActiveRoot, fileName);

        // ────────────────────────────────────────────────────────────────────────
        // Store registration
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>Registers a ProxyCore-owned store to follow profile switches. Idempotent.</summary>
        public static void Register(IProfileScopedStore store) {
            if (store == null || _stores.Contains(store)) return;
            _stores.Add(store);
        }

        /// <summary>Stops a store from following profile switches.</summary>
        public static void Unregister(IProfileScopedStore store) {
            if (store == null) return;
            _stores.Remove(store);
        }

        // ────────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Switches the active profile: flushes every registered store to the outgoing profile,
        /// switches, reloads every store from the new profile, re-evaluates auto-unlock triggers,
        /// then raises <see cref="ProfileChanged"/>.
        ///
        /// Passing null or empty selects the default profile (the legacy flat paths).
        /// Switching to the profile that is already active does nothing.
        /// </summary>
        public static void SetActive(string profileId) {
            string next = Normalize(profileId);
            if (next == _active) return;

            Save();                 // flush to the outgoing profile while its root is still current
            _active = next;

            if (!string.IsNullOrEmpty(_active))
                Directory.CreateDirectory(ActiveRoot);

            Reload();               // stores read from the new root
            UnlockManager.EvaluateAutoTriggers();
            ProfileChanged?.Invoke(_active);
        }

        /// <summary>
        /// Writes every registered ProxyCore store to the active profile now, regardless of
        /// <see cref="AutoSave"/>. Call this at whatever moment the game considers a save point.
        /// </summary>
        public static void Save() {
            for (int i = 0; i < _stores.Count; i++)
                _stores[i].OnProfileChanging();
        }

        /// <summary>
        /// Discards in-memory ProxyCore state and reloads it from the active profile on disk.
        /// Unsaved changes are lost — call <see cref="Save"/> first to keep them.
        /// </summary>
        public static void Reload() {
            string root = ActiveRoot;
            for (int i = 0; i < _stores.Count; i++)
                _stores[i].OnProfileChanged(root);
        }

        // ────────────────────────────────────────────────────────────────────────
        // Data lifecycle — ProxyCore's own files only
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Profile ids ProxyCore currently holds data for, sorted. The default profile is never
        /// listed — it has no directory of its own. No metadata is returned or stored: mapping
        /// ids back to save games is the host game's job.
        /// </summary>
        public static IReadOnlyList<string> ListProfiles() {
            string root = Path.Combine(Application.persistentDataPath, ROOT_FOLDER);
            if (!Directory.Exists(root)) return Array.Empty<string>();

            var ids = new List<string>();
            foreach (string dir in Directory.GetDirectories(root))
                ids.Add(Path.GetFileName(dir));
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        /// <summary>True when ProxyCore holds any data for the given profile.</summary>
        public static bool ProfileExists(string profileId) {
            string id = Normalize(profileId);
            return !string.IsNullOrEmpty(id) && Directory.Exists(RootFor(id));
        }

        /// <summary>
        /// Deletes every ProxyCore file for a profile and nothing else.
        ///
        /// Deleting the profile that is currently active also deselects it: the active profile
        /// falls back to the default and <see cref="ProfileChanged"/> fires with "". Without
        /// that, the next switch would flush the still-registered stores to the directory that
        /// was just deleted and resurrect it — so a deleted save would reappear in
        /// <see cref="ListProfiles"/>. The game should select the next save explicitly.
        /// </summary>
        /// <exception cref="ArgumentException">The default profile cannot be deleted this way.</exception>
        public static void DeleteProfile(string profileId) {
            string id = Normalize(profileId);
            if (string.IsNullOrEmpty(id)) {
                throw new ArgumentException(
                    "The default profile has no directory of its own. Use UnlockManager.ResetSavedUnlocks() " +
                    "and GameFlagCollection.ResetSavedFlags() to clear it.",
                    nameof(profileId));
            }

            string dir = RootFor(id);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);

            if (id != _active) return;

            // Deselect without going through SetActive, whose flush would recreate the directory.
            _active = "";
            Reload();
            UnlockManager.EvaluateAutoTriggers();
            ProfileChanged?.Invoke(_active);
        }

        /// <summary>
        /// Copies one profile's ProxyCore data onto another id, replacing anything already there.
        /// The copy is independent — later writes to either profile do not affect the other.
        /// Copying from the active profile flushes it first so the copy includes pending changes.
        /// </summary>
        /// <exception cref="ArgumentException">The destination is the default profile.</exception>
        public static void CopyProfile(string fromProfileId, string toProfileId) {
            string from = Normalize(fromProfileId);
            string to = Normalize(toProfileId);

            if (string.IsNullOrEmpty(to)) {
                throw new ArgumentException(
                    "Cannot copy onto the default profile.", nameof(toProfileId));
            }
            if (from == to) return;

            if (from == _active) Save();

            string src = RootFor(from);
            string dst = RootFor(to);

            if (Directory.Exists(dst))
                Directory.Delete(dst, recursive: true);
            Directory.CreateDirectory(dst);

            // Enumerate ProxyCore's own files rather than the whole directory: for the default
            // profile the source root is persistentDataPath, which holds unrelated game files.
            foreach (string file in ProxyCoreFilesIn(src))
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);

            if (to == _active) Reload();
        }

        private static IEnumerable<string> ProxyCoreFilesIn(string root) {
            if (!Directory.Exists(root)) yield break;

            string unlocks = Path.Combine(root, UNLOCKS_FILE);
            if (File.Exists(unlocks)) yield return unlocks;

            foreach (string flags in Directory.GetFiles(root, FLAGS_PATTERN))
                yield return flags;
        }

        // ────────────────────────────────────────────────────────────────────────
        // File IO used by ProxyCore stores
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes text through a temp file and an atomic replace, creating the directory if
        /// needed. An interrupted write leaves the previous contents readable rather than a
        /// truncated file.
        /// </summary>
        public static void WriteAtomic(string path, string contents) {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string temp = path + ".tmp";
            File.WriteAllText(temp, contents);

            if (File.Exists(path)) {
                // Replace keeps the destination intact if the swap itself fails.
                File.Replace(temp, path, destinationBackupFileName: null);
            }
            else {
                File.Move(temp, path);
            }
        }

        /// <summary>
        /// Reads and deserializes a ProxyCore save file.
        ///
        /// Returns null when the file does not exist. When the file exists but cannot be parsed
        /// — truncated by a crash, hand-edited, written by a newer version — it is <b>not</b>
        /// silently discarded: the file is moved aside to <c>{path}.corrupt</c>, an error is
        /// logged naming both paths, and null is returned so the caller starts from empty state.
        /// The next successful save writes a fresh file, and the quarantined one stays on disk
        /// for recovery.
        /// </summary>
        public static T ReadJson<T>(string path) where T : class {
            if (!File.Exists(path)) return null;

            try {
                return JsonUtility.FromJson<T>(File.ReadAllText(path));
            }
            catch (Exception ex) {
                string quarantine = path + ".corrupt";
                try {
                    if (File.Exists(quarantine)) File.Delete(quarantine);
                    File.Move(path, quarantine);
                }
                catch (Exception moveEx) {
                    Debug.LogError($"[ProxyCore] Could not quarantine unreadable save '{path}': {moveEx.Message}");
                }

                Debug.LogError(
                    $"[ProxyCore] Save file '{path}' could not be read and was moved to '{quarantine}'. " +
                    $"Starting from empty state. {ex.Message}");
                return null;
            }
        }

    }
}
