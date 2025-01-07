using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Logger = Silk.Logger;
using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;

namespace Silk
{
    public static class Loader
    {
        private static readonly string ModsFolder = Utils.GetModsFolder();

        public static List<SilkModData> LoadedMods { get; } = new List<SilkModData>();
        public static List<SilkModData> FailedMods { get; } = new List<SilkModData>();

        public static void Initialize()
        {
            Logger.LogInfo("Silk Loader Initialized");

            if (!Directory.Exists(ModsFolder))
            {
                Logger.LogInfo("Mods folder does not exist, creating it");
                Directory.CreateDirectory(ModsFolder);
            }

            LoadMods();
        }

        private static void LoadMods()
        {
            var modFiles = Directory.GetFiles(ModsFolder, "*.dll");

            var modsToLoad = modFiles.Length;
            var modsLoaded = 0;
            var modsFailed = 0;

            Logger.LogInfo($"Found {modsToLoad} mods to load");

            foreach (var modFile in modFiles)
            {
                try
                {
                    Logger.LogInfo($"Loading mod: {Path.GetFileName(modFile)}");
                    using (var assemblyDefinition = AssemblyDefinition.ReadAssembly(modFile))
                    {
                        var modTypes = assemblyDefinition.MainModule.Types
                            .Where(type => type.CustomAttributes.Any(attr => attr.AttributeType.FullName == typeof(SilkModAttribute).FullName));

                        foreach (var type in modTypes)
                        {
                            var silkModAttribute = type.CustomAttributes.First(attr => attr.AttributeType.FullName == typeof(SilkModAttribute).FullName);

                            var modName = silkModAttribute.ConstructorArguments[0].Value.ToString();
                            var modAuthors = silkModAttribute.ConstructorArguments[1].Value as CustomAttributeArgument[];
                            var authors = modAuthors?.Select(a => a.Value.ToString()).ToArray();
                            var modVersion = silkModAttribute.ConstructorArguments[2].Value.ToString();
                            var modGameVersion = silkModAttribute.ConstructorArguments[3].Value.ToString();
                            var modId = silkModAttribute.ConstructorArguments[4].Value.ToString();
                            var modEntryPoint = silkModAttribute.ConstructorArguments.Count > 5
                                ? silkModAttribute.ConstructorArguments[5].Value.ToString()
                                : "Initialize";
                            
                            // Print mod info
                            Logger.LogInfo($"Found Mod: {modName} by {string.Join(", ", authors ?? new string[0])}");
                            Logger.LogInfo($"Mod Version: {modVersion}");
                            Logger.LogInfo($"Mod Game Version: {modGameVersion}");
                            Logger.LogInfo($"Mod Id: {modId}");
                            Logger.LogInfo($"Mod EntryPoint: {modEntryPoint}");

                            // Load the actual mod
                            var assembly = Assembly.LoadFrom(modFile);
                            var modClass = assembly.GetType(type.FullName);

                            // Check if the mod is a mod
                            if (modClass == null)
                            {
                                Logger.LogError($"Failed to load type: {type.FullName}");
                                FailedMods.Add(new SilkModData(modName, authors, modVersion, modGameVersion, modId));
                                modsFailed++;
                                continue;
                            }

                            // Check if the mod is a mod
                            if (typeof(SilkMod).IsAssignableFrom(modClass))
                            {
                                var modInstance = Activator.CreateInstance(modClass) as SilkMod;
                                modInstance?.Initialize();
                                Mods.Manager.AddMod(modInstance);
                                Logger.LogInfo($"Initialized mod: {modName}");
                            }
                            else
                            {
                                var entryPointMethod = modClass.GetMethod(modEntryPoint, BindingFlags.Public | BindingFlags.Static);
                                if (entryPointMethod != null)
                                {
                                    entryPointMethod.Invoke(null, null);
                                    Logger.LogInfo($"Executed entry point: {modEntryPoint} for mod: {modName}");
                                }
                                else
                                {
                                    Logger.LogError($"Entry point method {modEntryPoint} not found in {modClass.FullName}");
                                    FailedMods.Add(new SilkModData(modName, authors, modVersion, modGameVersion, modId));
                                    modsFailed++;
                                    continue;
                                }
                            }

                            LoadedMods.Add(new SilkModData(modName, authors, modVersion, modGameVersion, modId));
                            modsLoaded++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to load mod {Path.GetFileName(modFile)}: {ex.Message}");
                    Logger.LogError(ex.StackTrace);
                    var modName = Path.GetFileNameWithoutExtension(modFile);
                    FailedMods.Add(new SilkModData(modName, new string[0], "Unknown", "Unknown", modName));
                    modsFailed++;
                }
            }

            Logger.LogInfo($"Finished loading mods");

            // Mods loaded and mods fialed  
            Logger.LogInfo($"Mods loaded: {modsLoaded}");
            Logger.LogInfo($"Mods failed to load: {modsFailed}");

            // Setup mods
            Logger.LogInfo("Setting up mods...");
            Mods.Manager.SetupMods();

            // Announce mods that failed
            if (modsFailed > 0)
            {
                Logger.LogInfo("Mods failed to load:");
                foreach (var failedMod in FailedMods)
                {
                    Logger.LogInfo($"  {failedMod.ModName} by {string.Join(", ", failedMod.ModAuthors ?? new string[0])}");
                    Utils.Announce($"Failed to load mod: {failedMod.ModName}", 255, 0, 0, typeof(Loader));
                }
            }
        }

        public static void LoadBepInEx()
        {
            string path = Path.Combine("BepInEx", "core", "BepInEx.Preloader.dll");

            if (!File.Exists(path))
            {
                Logger.LogError($"BepInEx.Preloader.dll not found at {path}");
                return;
            }

            string actualInvokePath = Environment.GetEnvironmentVariable("DOORSTOP_INVOKE_DLL_PATH");
            Environment.SetEnvironmentVariable("DOORSTOP_INVOKE_DLL_PATH", Path.Combine(Directory.GetCurrentDirectory(), path));

            try
            {
                Assembly bepInExPreloader = Assembly.LoadFrom(path);
                var entryPointMethod = bepInExPreloader.GetType("BepInEx.Preloader.Entrypoint", true)
                    ?.GetMethod("Main");

                entryPointMethod?.Invoke(null, null);
                Logger.LogInfo("Successfully loaded BepInEx.Preloader.dll");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Exception while loading BepInEx.Preloader.dll: {ex.Message}");
                Logger.LogError(ex.StackTrace);
            }
            finally
            {
                Environment.SetEnvironmentVariable("DOORSTOP_INVOKE_DLL_PATH", actualInvokePath);
            }
        }
    }

    public class SilkModData
    {
        public string ModName { get; }
        public string[] ModAuthors { get; }
        public string ModVersion { get; }
        public string ModGameVersion { get; }
        public string ModId { get; }

        public SilkModData(string modName, string[] modAuthors, string modVersion, string modGameVersion, string modId)
        {
            ModName = modName;
            ModAuthors = modAuthors;
            ModVersion = modVersion;
            ModGameVersion = modGameVersion;
            ModId = modId;
        }
    }
}

