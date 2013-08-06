using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NLog;

namespace FIVES
{
    public class PluginManager
    {
        public readonly static PluginManager Instance = new PluginManager();

        public delegate void PluginLoaded(string pluginName);
        public event PluginLoaded OnPluginLoaded;

        public PluginManager()
        {
            OnPluginLoaded += updateDeferredPlugins;
        }

        private struct LoadedPluginInfo {
            public string path;
            public IPluginInitializer initializer;
            public List<string> remainingDeps;
        }

        private List<string> attemptedFilenames = new List<string>();
        private Dictionary<string, LoadedPluginInfo> loadedPlugins = new Dictionary<string, LoadedPluginInfo>();
        private Dictionary<string, LoadedPluginInfo> deferredPlugins = new Dictionary<string, LoadedPluginInfo>();

        private static Logger logger = LogManager.GetCurrentClassLogger();

        // Canoninizes the filename (converts .. and . into actual path). This allows to identify plugin from the same
        // file but different paths as the same. E.g. /foo/bar/baz/../plugin.dll is the same as /foo/bar/plugin.dll.
        private string getCanonicalPath(string filename)
        {
            return Path.GetFullPath(filename);
        }

        // Attempts to load a plugin from file located at |path|.
        public void loadPlugin(string path)
        {
            string canonicalPath = getCanonicalPath(path);
            if (!attemptedFilenames.Contains(canonicalPath)) {
                try {
                    // Add this plugin to the list of loaded paths.
                    attemptedFilenames.Add(canonicalPath);

                    // Load an assembly.
                    Assembly assembly = Assembly.LoadFrom(canonicalPath);

                    // Find initializer class.
                    List<Type> types = new List<Type>(assembly.GetTypes());
                    Type pluginInitializerInterface = typeof(IPluginInitializer);
                    Type initializerType = types.Find(t => pluginInitializerInterface.IsAssignableFrom(t));
                    if (initializerType == null) {
                        logger.Warn("Assembly in file " + path +
                                    " doesn't contain any class implementing IPluginInitializer.");
                        return;
                    }

                    // Construct basic plugin info.
                    LoadedPluginInfo info = new LoadedPluginInfo();
                    info.path = canonicalPath;
                    info.initializer = (IPluginInitializer)Activator.CreateInstance(initializerType);

                    // Check if plugin with the same name was already loaded.
                    string name = info.initializer.getName();
                    if (loadedPlugins.ContainsKey(name)) {
                        logger.Warn("Cannot load plugin from " + path + ". Plugin with the same name '" + name +
                                    "' was already loaded from " + loadedPlugins[name].path + ".");
                        return;
                    }

                    // Check if plugin has all required dependencies.
                    var dependencies = info.initializer.getDependencies();
                    info.remainingDeps = dependencies.FindAll(depencency => !loadedPlugins.ContainsKey(depencency));
                    if (info.remainingDeps.Count > 0) {
                        deferredPlugins.Add(name, info);
                        return;
                    }

                    // Initialize plugin.
                    info.initializer.initialize();
                    loadedPlugins.Add(name, info);
                    OnPluginLoaded(name);
                } catch (Exception e) {
                    logger.WarnException("Failed to load file " + path + " as a plugin.", e);
                    return;
                }
            }
        }

        // Updates deferred plugins by removing |loadedPlugin| from the list of their remaining dependecies. Plugins
        // that have no other remaining dependencies are initialized.
        private void updateDeferredPlugins(string loadedPlugin)
        {
            // Iterate over deferred plugins and remove |loadedPlugin| from the list of dependencies.
            foreach (var info in deferredPlugins.Values)
                info.remainingDeps.Remove(loadedPlugin);

            // Find plugins that have no other dependencies.
            List<string> pluginsWithNoDeps = new List<string>();
            foreach (var plugin in deferredPlugins) {
                if (plugin.Value.remainingDeps.Count == 0)
                    pluginsWithNoDeps.Add(plugin.Key);
            }

            // Initialize these plugins and move them to loadedPlugins dictionary.
            foreach (var name in pluginsWithNoDeps) {
                deferredPlugins[name].initializer.initialize();
                loadedPlugins[name] = deferredPlugins[name];
                deferredPlugins.Remove(name);
                OnPluginLoaded(name);
            }
        }

        // Loads all valid plugins from the |pluginDirectory|
        public void loadPluginsFrom(string pluginDirectory)
        {
            string[] files = Directory.GetFiles(pluginDirectory);
            foreach (string filename in files)
                loadPlugin(filename);
        }

        // Returns whether plugin in |path| was loaded and initialized.
        public bool isPathLoaded(string path)
        {
            // Check if we've attempted loading this filename before.
            string canonicalPath = getCanonicalPath(path);
            if (!attemptedFilenames.Contains(canonicalPath))
                return false;

            // Check if the plugin was loaded.
            foreach (var plugin in loadedPlugins) {
                if (plugin.Value.path == canonicalPath)
                    return true;
            }

            return false;
        }

        // Returns whether plugin with |name| is loaded.
        public bool isPluginLoaded(string name)
        {
            return loadedPlugins.ContainsKey(name);
        }
    }
}

