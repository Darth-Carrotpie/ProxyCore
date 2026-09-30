using System;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace ProxyCore.Editor
{
    /// <summary>
    /// One-click "update the ProxyCore package to the latest release" action.
    ///
    /// ProxyCore is consumed as a UPM package installed via a tag-pinned git URL
    /// (https://github.com/Darth-Carrotpie/ProxyCore.git#&lt;tag&gt;). Unity treats a
    /// tag-pinned git package as immutable, so the Package Manager shows no "update"
    /// affordance. This action queries the latest GitHub release (the same source of
    /// truth the deploy-ump CI uses) and, when newer, re-resolves the package via
    /// <see cref="Client.Add(string)"/> to that tag.
    ///
    /// Surfaced from two places, both delegating to <see cref="CheckAndUpdate"/>:
    ///   • Menu: ProxyCore ▸ Update ProxyCore Package
    ///   • The Package Manager window (see ProxyCorePackageManagerExtension).
    /// </summary>
    public static class UpdateProxyCorePackage
    {
        // Mirror the values hardcoded in .github/workflows/deploy-ump.yml.
        public const string PackageName = "com.shakotis.proxycore";
        private const string RepoSlug = "Darth-Carrotpie/ProxyCore";
        private const string GitUrl = "https://github.com/Darth-Carrotpie/ProxyCore.git";
        // ProxyCore publishes plain git tags, not GitHub "Releases", so releases/latest 404s.
        // Read the tags list and pick the highest semver ourselves.
        private const string TagsApi = "https://api.github.com/repos/" + RepoSlug + "/tags?per_page=100";
        private const string DialogTitle = "ProxyCore — Update Package";

        [Serializable]
        private struct TagInfo
        {
            public string name;
        }

        [Serializable]
        private struct TagList
        {
            public TagInfo[] items;
        }

        [MenuItem("ProxyCore/Update ProxyCore Package")]
        public static void UpdateMenu() => CheckAndUpdate();

        /// <summary>
        /// True when ProxyCore is installed as a git/registry UPM package (i.e. an
        /// update can actually be applied), false when it is embedded/local in this
        /// dev project. Used to enable/disable the Package Manager button.
        /// </summary>
        public static bool CanUpdate()
        {
            var pkg = FindPackage();
            return pkg != null &&
                   (pkg.source == PackageSource.Git || pkg.source == PackageSource.Registry);
        }

        /// <summary>
        /// Shared entry point. Resolves the installed package, fetches the latest
        /// release tag, compares versions, and (on confirmation) re-resolves to the
        /// latest tag. All failure modes surface as dialogs; never throws to the caller.
        /// </summary>
        public static void CheckAndUpdate()
        {
            var pkg = FindPackage();

            if (pkg == null)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    "Could not locate the installed ProxyCore package. Make sure ProxyCore is " +
                    "installed via the Package Manager before checking for updates.", "OK");
                return;
            }

            if (pkg.source != PackageSource.Git && pkg.source != PackageSource.Registry)
            {
                // Embedded/Local — e.g. this dev repo, where ProxyCore lives under Assets/.
                EditorUtility.DisplayDialog(DialogTitle,
                    $"ProxyCore is {pkg.source.ToString().ToLowerInvariant()} in this project " +
                    $"(version {pkg.version}), not installed as an updatable package.\n\n" +
                    "Package update only applies when ProxyCore is installed via a git URL " +
                    "(Package Manager ▸ Install package from git URL).", "OK");
                return;
            }

            string installedVersion = pkg.version;

            EditorUtility.DisplayProgressBar(DialogTitle, "Checking for the latest release…", 0.5f);
            FetchLatestTag(
                onSuccess: latestTag =>
                {
                    EditorUtility.ClearProgressBar();
                    OnLatestTagResolved(installedVersion, latestTag);
                },
                onError: message =>
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog(DialogTitle,
                        "Could not check for the latest ProxyCore version.\n\n" + message +
                        "\n\nGitHub's unauthenticated API is rate-limited (60 requests/hour); " +
                        "if you've checked several times recently, wait a bit and retry.", "OK");
                });
        }

        private static void OnLatestTagResolved(string installedVersion, string latestTag)
        {
            Version installed = ParseVersion(installedVersion);
            Version latest = ParseVersion(latestTag);

            if (installed != null && latest != null && latest <= installed)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    $"You're on the latest ProxyCore ({installedVersion}).", "OK");
                return;
            }

            string latestLabel = latest?.ToString() ?? latestTag;
            bool proceed = EditorUtility.DisplayDialog(DialogTitle,
                $"An update is available.\n\nInstalled: {installedVersion}\nLatest: {latestLabel}\n\n" +
                "ProxyCore will be re-resolved to the latest release. Unsaved scene/asset changes " +
                "are unaffected, but a domain reload will occur.",
                "Update", "Cancel");

            if (proceed)
                ApplyUpdate(latestTag);
        }

        private static void ApplyUpdate(string tag)
        {
            // Always add the tag URL so branch-pinned (#upm) installs also converge to a
            // clean tag-pinned ref.
            string url = $"{GitUrl}#{tag}";
            AddRequest request = Client.Add(url);

            EditorUtility.DisplayProgressBar(DialogTitle, $"Updating ProxyCore to {tag}…", 0.75f);

            void Poll()
            {
                if (!request.IsCompleted)
                    return;

                EditorApplication.update -= Poll;
                EditorUtility.ClearProgressBar();

                if (request.Status == StatusCode.Success)
                {
                    Debug.Log($"[ProxyCore] Updated to {request.Result.packageId}");
                    EditorUtility.DisplayDialog(DialogTitle,
                        $"ProxyCore updated to {request.Result.version}.", "OK");
                }
                else
                {
                    string error = request.Error?.message ?? "Unknown error.";
                    Debug.LogError($"[ProxyCore] Update failed: {error}");
                    EditorUtility.DisplayDialog(DialogTitle,
                        "Update failed:\n\n" + error, "OK");
                }
            }

            EditorApplication.update += Poll;
        }

        private static PackageInfo FindPackage() =>
            PackageInfo.FindForAssembly(typeof(UpdateProxyCorePackage).Assembly);

        /// <summary>
        /// Async GET of the repo's tags; selects and returns the highest-semver tag.
        /// Callbacks run on the main thread.
        /// </summary>
        private static void FetchLatestTag(Action<string> onSuccess, Action<string> onError)
        {
            var www = UnityWebRequest.Get(TagsApi);
            www.SetRequestHeader("User-Agent", "ProxyCore-UnityEditor");
            www.SetRequestHeader("Accept", "application/vnd.github+json");

            var op = www.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
                    if (www.result != UnityWebRequest.Result.Success)
                    {
                        onError(www.error ?? $"HTTP {www.responseCode}");
                        return;
                    }

                    string latest = SelectHighestTag(www.downloadHandler.text, out string parseError);
                    if (latest == null)
                        onError(parseError);
                    else
                        onSuccess(latest);
                }
                finally
                {
                    www.Dispose();
                }
            };
        }

        /// <summary>
        /// Parses the GitHub tags array and returns the raw name of the highest-semver tag,
        /// or null (with <paramref name="error"/> set) if the response is unusable.
        /// </summary>
        private static string SelectHighestTag(string json, out string error)
        {
            error = null;

            TagInfo[] tags;
            try
            {
                // JsonUtility can't deserialize a top-level array, so wrap it.
                tags = JsonUtility.FromJson<TagList>("{\"items\":" + json + "}").items;
            }
            catch (Exception ex)
            {
                error = "Could not parse the GitHub API response: " + ex.Message;
                return null;
            }

            if (tags == null || tags.Length == 0)
            {
                error = "No tags were found in the ProxyCore repository.";
                return null;
            }

            string bestName = null;
            Version bestVersion = null;

            foreach (var tag in tags)
            {
                if (string.IsNullOrEmpty(tag.name)) continue;

                Version v = ParseVersion(tag.name);
                if (v == null) continue; // ignore non-semver tags

                if (bestVersion == null || v > bestVersion)
                {
                    bestVersion = v;
                    bestName = tag.name;
                }
            }

            // No tag parsed as a version — fall back to the first (GitHub lists newest first).
            if (bestName == null)
                bestName = tags[0].name;

            if (string.IsNullOrEmpty(bestName))
            {
                error = "No usable tag name was found in the ProxyCore repository.";
                return null;
            }

            return bestName;
        }

        /// <summary>Strips a leading 'v' and parses to <see cref="System.Version"/>; null on failure.</summary>
        private static Version ParseVersion(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return null;

            string trimmed = raw.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(1);

            return Version.TryParse(trimmed, out var version) ? version : null;
        }
    }
}
