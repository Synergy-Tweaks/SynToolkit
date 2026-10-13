using System;
using System.Collections.Generic;
using System.IO;
using SynToolkit.Models;
using SynToolkit.Models.ProfileModels;
using SynToolkit.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace SynToolkit.Utils
{
    public static class ProfileSerializing
    {
        /// <summary>
        /// Creates a .json profile with the enabled configuration services
        /// </summary>
        /// <param name="profileName"></param>
        /// <returns></returns>
        public static Profiles CreateProfile(string profileName)
        {
            profileName = profileName.Trim();
            if (string.IsNullOrWhiteSpace(profileName) || profileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException("Profile names cannot be empty or contain invalid filename characters.", nameof(profileName));
            }

            string profileDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Synergy",
                "Profiles");
            Directory.CreateDirectory(profileDirectory);

            List<string> configModelList = new ();
            List<KeyValuePair<string, string>> multiConfigModelList = new ();
            ProfileModel profileModel = new ();

            // Checks for enabled config services and adds them to the configModelList
            foreach (ConfigurationItemViewModel configItemViewModel in App._host.Services.GetRequiredService<IEnumerable<ConfigurationItemViewModel>>())
            {
                if (configItemViewModel.CurrentSetting == true) configModelList.Add(configItemViewModel.Key.ToString());
            }
            foreach (MultiOptionConfigurationItemViewModel configItemViewModel in App._host.Services.GetRequiredService<IEnumerable<MultiOptionConfigurationItemViewModel>>())
            {
                multiConfigModelList.Add(new (configItemViewModel.Key, configItemViewModel.CurrentSetting.ToString()));
            }
            profileModel.Name = profileName;
            profileModel.Config = configModelList;
            profileModel.MultiConfig = multiConfigModelList;

            string jsonString = System.Text.Json.JsonSerializer.Serialize(profileModel);

            string profilePath = Path.Combine(profileDirectory, $"{profileName}.json");
            // Atomic write: a kill (e.g. installer force-close) between open
            // and flush must never leave a half-written profile behind.
            string tempPath = profilePath + ".tmp";
            File.WriteAllText(tempPath, jsonString);
            File.Move(tempPath, profilePath, overwrite: true);
            return DeserializeProfile(profilePath);
        }

        /// <summary>
        /// Scans the Profiles folder on disk. Called fresh on every HomePageViewModel
        /// construction (rather than once at DI-container-build time) so profiles added or
        /// removed during a session are still visible after navigating away and back.
        /// </summary>
        public static List<Profiles> LoadProfilesFromDisk()
        {
            List<Profiles> profiles = new();
            string profilesPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Synergy",
                "Profiles");

            try
            {
                Directory.CreateDirectory(profilesPath);
                foreach (string profilePath in Directory.EnumerateFiles(
                    profilesPath,
                    "*.json",
                    SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        profiles.Add(DeserializeProfile(profilePath));
                    }
                    catch (Exception exception)
                    {
                        App.logger.Warn(exception, $"Ignoring invalid SynToolkit profile '{Path.GetFileName(profilePath)}'.");
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "SynToolkit profiles could not be enumerated.");
            }

            return profiles;
        }

        public static Profiles DeserializeProfile(string file)
        {
            if (string.IsNullOrWhiteSpace(file))
            {
                throw new ArgumentException("A profile path is required.", nameof(file));
            }

            ProfileModel profileModel = JsonConvert.DeserializeObject<ProfileModel>(File.ReadAllText(file))
                ?? throw new InvalidDataException("The profile file does not contain a valid profile object.");

            if (string.IsNullOrWhiteSpace(profileModel.Name))
            {
                throw new InvalidDataException("The profile does not contain a valid name.");
            }

            profileModel.Config ??= new List<string>();
            profileModel.MultiConfig ??= new List<KeyValuePair<string, string>>();
            App.logger.Info($"[PROFILES] Loaded profile: \"{profileModel.Name}\"");

            return new Profiles(profileModel.Name, profileModel.Name, profileModel.Config, profileModel.MultiConfig);
        }
    }
}
