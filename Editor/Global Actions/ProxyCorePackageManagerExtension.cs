using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine.UIElements;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace ProxyCore.Editor
{
    /// <summary>
    /// Adds a "Check for Updates" button to the ProxyCore entry in Unity's Package
    /// Manager window. The button is only visible when ProxyCore is the selected
    /// package, and delegates to <see cref="UpdateProxyCorePackage.CheckAndUpdate"/>
    /// — the same action behind the ProxyCore ▸ Update ProxyCore Package menu item.
    /// </summary>
    sealed class ProxyCorePackageManagerExtension : IPackageManagerExtension
    {
        Button _button;

        public VisualElement CreateExtensionUI()
        {
            _button = new Button(UpdateProxyCorePackage.CheckAndUpdate)
            {
                text = "Check for Updates",
                tooltip = "Query the latest ProxyCore release and update if a newer version exists."
            };
            _button.style.display = DisplayStyle.None;

            var container = new VisualElement();
            container.Add(_button);
            return container;
        }

        public void OnPackageSelectionChange(PackageInfo packageInfo)
        {
            if (_button == null)
                return;

            bool isProxyCore = packageInfo != null && packageInfo.name == UpdateProxyCorePackage.PackageName;
            _button.style.display = isProxyCore ? DisplayStyle.Flex : DisplayStyle.None;
            _button.SetEnabled(isProxyCore && UpdateProxyCorePackage.CanUpdate());
        }

        public void OnPackageAddedOrUpdated(PackageInfo packageInfo) { }

        public void OnPackageRemoved(PackageInfo packageInfo) { }
    }

    [InitializeOnLoad]
    static class ProxyCorePackageManagerExtensionRegistration
    {
        static ProxyCorePackageManagerExtensionRegistration() =>
            PackageManagerExtensions.RegisterExtension(new ProxyCorePackageManagerExtension());
    }
}
