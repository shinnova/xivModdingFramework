﻿// xivModdingFramework
// Copyright © 2018 Rafael Gonzalez - All Rights Reserved
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using Newtonsoft.Json;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.Mods.FileTypes
{
    public class TTMP
    {
        private readonly string _currentWizardTTMPVersion = "1.0w";
        private readonly string _currentSimpleTTMPVersion = "1.0s";
        private const string _minimumAssembly = "1.0.0.0";

        private string _tempMPD, _tempMPL, _source;
        private readonly DirectoryInfo _modPackDirectory;

        public TTMP(DirectoryInfo modPackDirectory, string source)
        {
            _modPackDirectory = modPackDirectory;
            _source = source;
        }

        /// <summary>
        /// Creates a mod pack that uses a wizard for installation
        /// </summary>
        /// <param name="modPackData">The data that will go into the mod pack</param>
        /// <param name="progress">The progress of the mod pack creation</param>
        /// <returns>The number of pages created for the mod pack</returns>
        public async Task<int> CreateWizardModPack(ModPackData modPackData, IProgress<double> progress, bool overwriteModpack)
        {
            var processCount = await Task.Run<int>(() =>
            {
                _tempMPD = Path.GetTempFileName();
                _tempMPL = Path.GetTempFileName();
                var imageList = new Dictionary<string, string>();
                var pageCount = 1;

                var modPackJson = new ModPackJson
                {
                    TTMPVersion = _currentWizardTTMPVersion,
                    MinimumFrameworkVersion = _minimumAssembly,
                    Name = modPackData.Name,
                    Author = modPackData.Author,
                    Version = modPackData.Version.ToString(),
                    Description = modPackData.Description,
                    Url = modPackData.Url,
                    ModPackPages = new List<ModPackPageJson>()
                };

                using (var binaryWriter = new BinaryWriter(File.Open(_tempMPD, FileMode.Open)))
                {
                    foreach (var modPackPage in modPackData.ModPackPages)
                    {
                        var modPackPageJson = new ModPackPageJson
                        {
                            PageIndex = modPackPage.PageIndex,
                            ModGroups = new List<ModGroupJson>()
                        };

                        modPackJson.ModPackPages.Add(modPackPageJson);

                        foreach (var modGroup in modPackPage.ModGroups)
                        {
                            var modGroupJson = new ModGroupJson
                            {
                                GroupName = modGroup.GroupName,
                                SelectionType = modGroup.SelectionType,
                                OptionList = new List<ModOptionJson>()
                            };

                            modPackPageJson.ModGroups.Add(modGroupJson);

                            foreach (var modOption in modGroup.OptionList)
                            {
                                var randomFileName = "";

                                if (modOption.Image != null)
                                {
                                    randomFileName = $"{Path.GetRandomFileName()}.png";                                    
                                    imageList.Add(randomFileName, modOption.ImageFileName);
                                }

                                var modOptionJson = new ModOptionJson
                                {
                                    Name = modOption.Name,
                                    Description = modOption.Description,
                                    ImagePath = randomFileName,
                                    GroupName = modOption.GroupName,
                                    SelectionType = modOption.SelectionType,
                                    IsChecked=modOption.IsChecked,
                                    ModsJsons = new List<ModsJson>()
                                };

                                modGroupJson.OptionList.Add(modOptionJson);

                                foreach (var modOptionMod in modOption.Mods)
                                {
                                    var dataFile = GetDataFileFromPath(modOptionMod.Key);

                                    var modsJson = new ModsJson
                                    {
                                        Name = modOptionMod.Value.Name,
                                        Category = modOptionMod.Value.Category.GetEnDisplayName(),
                                        FullPath = modOptionMod.Key,
                                        IsDefault = modOptionMod.Value.IsDefault,
                                        ModSize = modOptionMod.Value.ModDataBytes.Length,
                                        ModOffset = binaryWriter.BaseStream.Position,
                                        DatFile = dataFile.GetDataFileName(),
                                    };

                                    binaryWriter.Write(modOptionMod.Value.ModDataBytes);

                                    modOptionJson.ModsJsons.Add(modsJson);
                                }
                            }
                        }

                        progress?.Report((double)pageCount / modPackData.ModPackPages.Count);

                        pageCount++;
                    }
                }

                File.WriteAllText(_tempMPL, JsonConvert.SerializeObject(modPackJson));

                var modPackPath = Path.Combine(_modPackDirectory.FullName, $"{modPackData.Name}.ttmp2");

                if (File.Exists(modPackPath) && !overwriteModpack)
                {
                    var fileNum = 1;
                    modPackPath = Path.Combine(_modPackDirectory.FullName, $"{modPackData.Name}({fileNum}).ttmp2");
                    while (File.Exists(modPackPath))
                    {
                        fileNum++;
                        modPackPath = Path.Combine(_modPackDirectory.FullName, $"{modPackData.Name}({fileNum}).ttmp2");
                    }
                }
                else if (File.Exists(modPackPath) && overwriteModpack)
                {
                    File.Delete(modPackPath);
                }

                using (var zip = ZipFile.Open(modPackPath, ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(_tempMPL, "TTMPL.mpl");
                    zip.CreateEntryFromFile(_tempMPD, "TTMPD.mpd");
                    foreach (var image in imageList)
                    {
                        zip.CreateEntryFromFile(image.Value, image.Key);
                    }
                }

                File.Delete(_tempMPD);
                File.Delete(_tempMPL);

                return pageCount;
            });

            return processCount;
        }

        /// <summary>
        /// Creates a mod pack that uses simple installation
        /// </summary>
        /// <param name="modPackData">The data that will go into the mod pack</param>
        /// <param name="gameDirectory">The game directory</param>
        /// <param name="progress">The progress of the mod pack creation</param>
        /// <returns>The number of mods processed for the mod pack</returns>
        public async Task<int> CreateSimpleModPack(SimpleModPackData modPackData, DirectoryInfo gameDirectory, IProgress<(int current, int total, string message)> progress, bool overwriteModpack)
        {
            var processCount = await Task.Run<int>(() =>
            {
                var dat = new Dat(gameDirectory);
                _tempMPD = Path.GetTempFileName();
                _tempMPL = Path.GetTempFileName();
                var modCount = 0;

                var modPackJson = new ModPackJson
                {
                    TTMPVersion = _currentSimpleTTMPVersion,
                    Name = modPackData.Name,
                    Author = modPackData.Author,
                    Version = modPackData.Version.ToString(),
                    MinimumFrameworkVersion = _minimumAssembly,
                    Url = modPackData.Url,
                    Description = modPackData.Description,
                    SimpleModsList = new List<ModsJson>()
                };

                try
                {
                    using (var binaryWriter = new BinaryWriter(File.Open(_tempMPD, FileMode.Open)))
                    {
                        foreach (var simpleModData in modPackData.SimpleModDataList)
                        {
                            var modsJson = new ModsJson
                            {
                                Name = simpleModData.Name,
                                Category = simpleModData.Category.GetEnDisplayName(),
                                FullPath = simpleModData.FullPath,
                                ModSize = simpleModData.ModSize,
                                DatFile = simpleModData.DatFile,
                                IsDefault = simpleModData.IsDefault,
                                ModOffset = binaryWriter.BaseStream.Position,
                                ModPackEntry = new ModPack
                                {
                                    name =  modPackData.Name,
                                    author = modPackData.Author,
                                    version = modPackData.Version.ToString(),
                                    url = modPackData.Url
                                }
                            };

                            var rawData = dat.GetRawData((int) simpleModData.ModOffset,
                                XivDataFiles.GetXivDataFile(simpleModData.DatFile),
                                simpleModData.ModSize);

                            if (rawData == null)
                            {
                                throw new Exception("Unable to obtain data for the following mod\n\n" +
                                                    $"Name: {simpleModData.Name}\nFull Path: {simpleModData.FullPath}\n" +
                                                    $"Mod Offset: {simpleModData.ModOffset}\nData File: {simpleModData.DatFile}\n\n" +
                                                    $"Unselect the above mod and try again.");
                            }

                            binaryWriter.Write(rawData);

                            modPackJson.SimpleModsList.Add(modsJson);

                            progress?.Report((++modCount, modPackData.SimpleModDataList.Count, string.Empty));
                        }
                    }

                    progress?.Report((0, modPackData.SimpleModDataList.Count, GeneralStrings.TTMP_Creating));

                    File.WriteAllText(_tempMPL, JsonConvert.SerializeObject(modPackJson));

                    var modPackPath = Path.Combine(_modPackDirectory.FullName, $"{modPackData.Name}.ttmp2");

                    if (File.Exists(modPackPath) && !overwriteModpack)
                    {
                        var fileNum = 1;
                        modPackPath = Path.Combine(_modPackDirectory.FullName, $"{modPackData.Name}({fileNum}).ttmp2");
                        while (File.Exists(modPackPath))
                        {
                            fileNum++;
                            modPackPath = Path.Combine(_modPackDirectory.FullName, $"{modPackData.Name}({fileNum}).ttmp2");
                        }
                    }
                    else if (File.Exists(modPackPath) && overwriteModpack)
                    {
                        File.Delete(modPackPath);
                    }

                    using (var zip = ZipFile.Open(modPackPath, ZipArchiveMode.Create))
                    {
                        zip.CreateEntryFromFile(_tempMPL, "TTMPL.mpl");
                        zip.CreateEntryFromFile(_tempMPD, "TTMPD.mpd");
                    }
                }
                finally
                {
                    File.Delete(_tempMPD);
                    File.Delete(_tempMPL);
                }

                return modCount;
            });

            return processCount;
        }

        /// <summary>
        /// Gets the data from a mod pack including images if present
        /// </summary>
        /// <param name="modPackDirectory">The directory of the mod pack</param>
        /// <returns>A tuple containing the mod pack json data and a dictionary of images if any</returns>
        public Task<(ModPackJson ModPackJson, Dictionary<string, Image> ImageDictionary)> GetModPackJsonData(DirectoryInfo modPackDirectory)
        {
            return Task.Run(() =>
            {
                ModPackJson modPackJson = null;
                var imageDictionary = new Dictionary<string, Image>();

                using (var archive = ZipFile.OpenRead(modPackDirectory.FullName))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith(".mpl"))
                        {
                            using (var streamReader = new StreamReader(entry.Open()))
                            {
                                var jsonString = streamReader.ReadToEnd();

                                modPackJson = JsonConvert.DeserializeObject<ModPackJson>(jsonString);
                            }
                        }

                        if (entry.FullName.EndsWith(".png"))
                        {
                            imageDictionary.Add(entry.FullName, Image.Load(entry.Open()));
                        }
                    }
                }

                return (modPackJson, imageDictionary);
            });
        }

        /// <summary>
        /// Gets the data from first generation mod packs
        /// </summary>
        /// <param name="modPackDirectory">The directory of the mod pack</param>
        /// <returns>A list containing original mod pack json data</returns>
        public Task<List<OriginalModPackJson>> GetOriginalModPackJsonData(DirectoryInfo modPackDirectory)
        {
            return Task.Run(() =>
            {
                var modPackJsonList = new List<OriginalModPackJson>();

                using (var archive = ZipFile.OpenRead(modPackDirectory.FullName))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith(".mpl"))
                        {
                            using (var streamReader = new StreamReader(entry.Open()))
                            {
                                var line = streamReader.ReadLine();
                                if (line.ToLower().Contains("version"))
                                {
                                    // Skip this line and read the next
                                    line = streamReader.ReadLine();
                                    if (line == null) return null;
                                    modPackJsonList.Add(JsonConvert.DeserializeObject<OriginalModPackJson>(line));
                                }
                                else
                                {
                                    modPackJsonList.Add(JsonConvert.DeserializeObject<OriginalModPackJson>(line));
                                }

                                while (streamReader.Peek() >= 0)
                                {
                                    line = streamReader.ReadLine();
                                    modPackJsonList.Add(JsonConvert.DeserializeObject<OriginalModPackJson>(line));
                                }
                            }
                        }
                    }
                }

                return modPackJsonList;
            });
        }

        /// <summary>
        /// Gets the version from a mod pack
        /// </summary>
        /// <param name="modPackDirectory">The mod pack directory</param>
        /// <returns>The version of the mod pack as a string</returns>
        public string GetVersion(DirectoryInfo modPackDirectory)
        {
            ModPackJson modPackJson = null;

            using (var archive = ZipFile.OpenRead(modPackDirectory.FullName))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith(".mpl"))
                    {
                        using (var streamReader = new StreamReader(entry.Open()))
                        {
                            var jsonString = streamReader.ReadToEnd();

                            modPackJson = JsonConvert.DeserializeObject<ModPackJson>(jsonString);
                        }
                    }
                }
            }

            return modPackJson.TTMPVersion;
        }

        /// <summary>
        /// Imports a mod pack asynchronously 
        /// </summary>
        /// <param name="modPackDirectory">The directory of the mod pack</param>
        /// <param name="modsJson">The list of mods to be imported</param>
        /// <param name="gameDirectory">The game directory</param>
        /// <param name="modListDirectory">The mod list directory</param>
        /// <param name="progress">The progress of the import</param>
        /// <returns>The number of total mods imported</returns>
        public async Task<(int ImportCount, string Errors)> ImportModPackAsync(DirectoryInfo modPackDirectory, List<ModsJson> modsJson,
            DirectoryInfo gameDirectory, DirectoryInfo modListDirectory, IProgress<(int current, int total, string message)> progress)
        {
            var dat = new Dat(gameDirectory);
            var modding = new Modding(gameDirectory);
            var modListFullPaths = new List<string>();
            var modList = modding.GetModList();
            var importErrors = "";

            // Disable the cache woker while we're installing multiple items at once, so that we don't process queue items mid-import.
            // (Could result in improper parent file calculations, as the parent files may not be actually imported yet)
            XivCache.CacheWorkerEnabled = false;


            // Loop through all the incoming mod entries, and only take
            // the *LAST* mod json entry for each file path.
            // This keeps us from having to constantly re-query the mod list file, and filters out redundant imports.
            var filePaths = new HashSet<string>();
            var newList = new List<ModsJson>(modsJson.Count);
            for(int i = modsJson.Count -1; i >= 0; i--)
            {
                var mj = modsJson[i];
                if(filePaths.Contains(mj.FullPath))
                {
                    // Already have a mod using this path, discard this mod entry.
                    continue;
                }

                filePaths.Add(mj.FullPath);
                newList.Add(mj);
            }
            modsJson = newList;

            var importCount = 0;

            try
            {
                foreach (var modListMod in modList.Mods)
                {
                    if (!string.IsNullOrEmpty(modListMod.fullPath))
                    {
                        modListFullPaths.Add(modListMod.fullPath);
                    }
                }

                await Task.Run(async () =>
                {
                    using (var archive = ZipFile.OpenRead(modPackDirectory.FullName))
                    {
                        foreach (var zipEntry in archive.Entries)
                        {
                            if (zipEntry.FullName.EndsWith(".mpd"))
                            {
                                _tempMPD = Path.GetTempFileName();

                                using (var zipStream = zipEntry.Open())
                                {
                                    using (var fileStream = new FileStream(_tempMPD, FileMode.OpenOrCreate))
                                    {
                                        progress?.Report((0, modsJson.Count, GeneralStrings.TTMP_ReadingContent));
                                        await zipStream.CopyToAsync(fileStream);
                                        progress?.Report((0, modsJson.Count, GeneralStrings.TTMP_StartImport));

                                        using (var binaryReader = new BinaryReader(fileStream))
                                        {
                                            foreach (var modJson in modsJson)
                                            {
                                                try
                                                {
                                                    if (modListFullPaths.Contains(modJson.FullPath))
                                                    {
                                                        var existingEntry = (from entry in modList.Mods
                                                                             where entry.fullPath.Equals(modJson.FullPath)
                                                                             select entry).FirstOrDefault();

                                                        binaryReader.BaseStream.Seek(modJson.ModOffset, SeekOrigin.Begin);

                                                        var data = binaryReader.ReadBytes(modJson.ModSize);

                                                        await (dat.WriteToDat(new List<byte>(data), existingEntry,
                                                            modJson.FullPath,
                                                            modJson.Category.GetDisplayName(), modJson.Name,
                                                            XivDataFiles.GetXivDataFile(modJson.DatFile), _source,
                                                            GetDataType(modJson.FullPath), modJson.ModPackEntry));
                                                    }
                                                    else
                                                    {
                                                        binaryReader.BaseStream.Seek(modJson.ModOffset, SeekOrigin.Begin);

                                                        var data = binaryReader.ReadBytes(modJson.ModSize);

                                                        await (dat.WriteToDat(new List<byte>(data), null, modJson.FullPath,
                                                            modJson.Category.GetDisplayName(), modJson.Name,
                                                            XivDataFiles.GetXivDataFile(modJson.DatFile), _source,
                                                            GetDataType(modJson.FullPath), modJson.ModPackEntry));
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    if (ex.GetType() == typeof(NotSupportedException))
                                                    {
                                                        importErrors = ex.Message;
                                                        break;
                                                    }

                                                    importErrors +=
                                                        $"Name: {modJson.Name}\nPath: {modJson.FullPath}\nOffset: {modJson.ModOffset}\nError: {ex.Message}\n\n";
                                                }
                                                importCount++;

                                                progress?.Report((importCount, modsJson.Count, string.Empty));

                                            }
                                        }
                                    }
                                }

                                File.Delete(_tempMPD);

                                break;
                            }
                        }
                    }
                });

                if (modsJson[0].ModPackEntry != null)
                {
                    modList = modding.GetModList();

                    // TODO - Probably need to look at keying this off more than just the name.
                    var modPackExists = modList.ModPacks.Any(modpack => modpack.name == modsJson[0].ModPackEntry.name);

                    if (!modPackExists)
                    {
                        modList.ModPacks.Add(modsJson[0].ModPackEntry);
                    }

                    modding.SaveModList(modList);
                }
            } finally
            {
                XivCache.CacheWorkerEnabled = true;
            }

            return (importCount, importErrors);
        }

        /// <summary>
        /// Gets the data type from an item path
        /// </summary>
        /// <param name="path">The path of the item</param>
        /// <returns>The data type</returns>
        private int GetDataType(string path)
        {
            if (path.Contains(".tex"))
            {
                return 4;
            }

            if (path.Contains(".mdl"))
            {
                return 3;
            }

            return 2;
        }


        /// <summary>
        /// Gets a XivDataFile category for the specified path.
        /// </summary>
        /// <param name="internalPath">The internal file path</param>
        /// <returns>A XivDataFile entry for the needed dat category</returns>
        private XivDataFile GetDataFileFromPath(string internalPath)
        {
            var folderKey = internalPath.Substring(0, internalPath.IndexOf("/", StringComparison.Ordinal));

            var cats = Enum.GetValues(typeof(XivDataFile)).Cast<XivDataFile>();

            foreach (var cat in cats)
            {
                if (cat.GetFolderKey() == folderKey)
                    return cat;
            }

            throw new ArgumentException("[Dat] Could not find category for path: " + internalPath);
        }
    }
}