﻿using ChaosRecipeEnhancer.UI.Models;
using ChaosRecipeEnhancer.UI.Models.Config;
using ChaosRecipeEnhancer.UI.Models.Enums;
using ChaosRecipeEnhancer.UI.Models.UserSettings;
using ChaosRecipeEnhancer.UI.Properties;
using ChaosRecipeEnhancer.UI.Services.FilterManipulation.FilterGeneration;
using ChaosRecipeEnhancer.UI.Services.FilterManipulation.FilterGeneration.Factory;
using ChaosRecipeEnhancer.UI.Services.FilterManipulation.FilterGeneration.Factory.Managers;
using ChaosRecipeEnhancer.UI.Services.FilterManipulation.FilterStorage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ChaosRecipeEnhancer.UI.Services.FilterManipulation;

public interface IFilterManipulationService
{
    public Task GenerateSectionsAndUpdateFilterAsync(HashSet<string> missingItemClasses);
    public void RemoveChaosRecipeSectionAsync();
}

public class FilterManipulationService : IFilterManipulationService
{
    private readonly IUserSettings _userSettings;
    private ABaseItemClassManager _itemClassManager;
    private readonly List<string> _customStyle = new();

    public FilterManipulationService(IUserSettings userSettings)
    {
        _userSettings = userSettings;

        LoadCustomStyle();
    }

    // TODO: [Refactor] mechanism for receiving missing items from some other service and populating based on that limited information
    public async Task GenerateSectionsAndUpdateFilterAsync(HashSet<string> missingItemClasses)
    {
        var activeItemTypes = new ActiveItemTypes();
        var visitor = new CItemClassManagerFactory();
        var sectionList = new HashSet<string>();

        foreach (ItemClass item in Enum.GetValues(typeof(ItemClass)))
        {
            _itemClassManager = visitor.GetItemClassManager(item);

            var stillMissing = _itemClassManager.CheckIfMissing(missingItemClasses);

            // weapons might be buggy, will try to do some tests
            if (_itemClassManager.AlwaysActive || stillMissing)
            {
                // if we need chaos only gear to complete a set (60-74), add that to our filter section
                sectionList.Add(GenerateSection());

                // find better way to handle active items and sound notification on changes
                activeItemTypes = _itemClassManager.SetActiveTypes(activeItemTypes, true);
            }
            else
            {
                activeItemTypes = _itemClassManager.SetActiveTypes(activeItemTypes, false);
            }
        }

        if (Settings.Default.LootFilterManipulationEnabled) await UpdateFilterAsync(sectionList);
    }

    private string GenerateSection()
    {
        var result = "Show";

        result += StringConstruction.NewLineCharacter + StringConstruction.TabCharacter + "HasInfluence None";

        result = result + StringConstruction.NewLineCharacter + StringConstruction.TabCharacter + "Rarity Rare" + StringConstruction.NewLineCharacter + StringConstruction.TabCharacter;

        if (!Settings.Default.IncludeIdentifiedItemsEnabled) result += "Identified False" + StringConstruction.NewLineCharacter + StringConstruction.TabCharacter;

        // Setting item level section based on whether Chaoss Recipe Tracking is enabled (or disabled, in which the Regal Recipe is used)
        result += _userSettings.ChaosRecipeTrackingEnabled switch
        {
            // Chaos Recipe Tracking disabled, item class is NOT always active
            false when !_itemClassManager.AlwaysActive =>
                "ItemLevel >= 60" + StringConstruction.NewLineCharacter +
                StringConstruction.TabCharacter + "ItemLevel <= 74" + StringConstruction.NewLineCharacter +
                StringConstruction.TabCharacter,

            // Chaos Recipe Tracking diasbled, Regal Recipe is used
            false =>
                "ItemLevel >= 75" + StringConstruction.NewLineCharacter +
                StringConstruction.TabCharacter,

            // Chaos Recipe Tracking enabled or item class is always active
            _ =>
                "ItemLevel >= 60" + StringConstruction.NewLineCharacter +
                StringConstruction.TabCharacter
        };

        string baseType;

        // weapons get special treatment due to space saving options
        if (_itemClassManager.ClassName.Equals("OneHandWeapons"))
        {
            baseType = _itemClassManager.SetBaseType(
                _userSettings.LootFilterSpaceSavingHideLargeWeapons,
                _userSettings.LootFilterSpaceSavingHideOffHand
            );
        }
        else if (_itemClassManager.ClassName.Equals("TwoHandWeapons"))
        {
            baseType = _itemClassManager.SetBaseType(_userSettings.LootFilterSpaceSavingHideLargeWeapons);
        }
        else
        {
            baseType = _itemClassManager.SetBaseType();
        }

        result = result + baseType + StringConstruction.NewLineCharacter + StringConstruction.TabCharacter;

        var colors = GetColorRGBAValues();
        var bgColor = colors.Aggregate("SetBackgroundColor", (current, t) => current + " " + t);

        result = result + bgColor + StringConstruction.NewLineCharacter + StringConstruction.TabCharacter;

        result = _customStyle.Aggregate(result,
            (current, cs) =>
                current + cs + StringConstruction.NewLineCharacter + StringConstruction.TabCharacter);

        // Map Icon setting enabled
        if (Settings.Default.LootFilterIconsEnabled)
            // TODO: [Filter Manipulation] [Enhancement] Add ability to modify map icon for items added to loot filter
            result = result + "MinimapIcon 2 White Star" + StringConstruction.NewLineCharacter +
                     StringConstruction.TabCharacter;

        return result;
    }

    private static string GenerateLootFilter(string oldFilter, IEnumerable<string> sections)
    {
        // can't use our Env const due to compile time requirement
        const string newLine = "\n";

        var beforeSection = "";
        var sectionStart = "# Chaos Recipe START - Filter Manipulation by Chaos Recipe Enhancer";
        var sectionBody = "";
        var sectionEnd = "# Chaos Recipe END - Filter Manipulation by Chaos Recipe Enhancer";
        var afterSection = "";

        // generate chaos recipe section
        sectionBody += sectionStart + newLine + newLine;
        sectionBody = sections.Aggregate(sectionBody, (current, s) => current + s + newLine);
        sectionBody += sectionEnd + newLine;

        string[] sep = { sectionEnd + newLine };
        var split = oldFilter.Split(sep, StringSplitOptions.None);

        if (split.Length > 1)
        {
            afterSection = split[1];

            string[] sep2 = { sectionStart };
            var split2 = split[0].Split(sep2, StringSplitOptions.None);

            if (split2.Length > 1)
                beforeSection = split2[0];
            else
                afterSection = oldFilter;
        }
        else
        {
            afterSection = oldFilter;
        }

        return beforeSection + sectionBody + afterSection;
    }

    public async void RemoveChaosRecipeSectionAsync()
    {
        var filterStorage = FilterStorageFactory.Create(Settings.Default);
        var oldFilterContent = await filterStorage.ReadLootFilterAsync();

        // return if no old filter detected (usually caused by user error no path selected)
        // in our case the manager doesn't care about setting error this should likely be an Exception
        if (oldFilterContent == null) return;

        // Define the pattern for Chaos Recipe sections
        const string pattern = @"# Chaos Recipe START - Filter Manipulation by Chaos Recipe Enhancer[\s\S]*?# Chaos Recipe END - Filter Manipulation by Chaos Recipe Enhancer";
        var regex = new Regex(pattern, RegexOptions.Multiline);

        // Remove the Chaos Recipe sections from the content
        var cleanedContent = regex.Replace(oldFilterContent, "");
        await filterStorage.WriteLootFilterAsync(cleanedContent);
    }

    private static async Task UpdateFilterAsync(IEnumerable<string> sectionList)
    {
        var filterStorage = FilterStorageFactory.Create(Settings.Default);
        var oldFilter = await filterStorage.ReadLootFilterAsync();

        // return if no old filter detected (usually caused by user error no path selected)
        // in our case the manager doesn't care about setting error this should likely be an Exception
        if (oldFilter == null) return;

        var newFilter = GenerateLootFilter(oldFilter, sectionList);
        await filterStorage.WriteLootFilterAsync(newFilter);
    }

    private IEnumerable<int> GetColorRGBAValues()
    {
        int r;
        int g;
        int b;
        int a;
        var color = _itemClassManager.ClassColor;
        var colorList = new List<int>();

        if (color != "")
        {
            a = Convert.ToByte(color.Substring(1, 2), 16);
            r = Convert.ToByte(color.Substring(3, 2), 16);
            g = Convert.ToByte(color.Substring(5, 2), 16);
            b = Convert.ToByte(color.Substring(7, 2), 16);
        }
        else
        {
            a = 255;
            r = 255;
            g = 0;
            b = 0;
        }

        colorList.Add(r);
        colorList.Add(g);
        colorList.Add(b);
        colorList.Add(a);

        return colorList;
    }

    private void LoadCustomStyle()
    {
        _customStyle.Clear();

        var pathNormalItemsStyle = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, FilterConfig.DefaultNormalItemFilterStyleFilePath);

        var style = File.ReadAllLines(pathNormalItemsStyle);

        foreach (var line in style)
        {
            if (line == "") continue;
            if (line.Contains("#")) continue;
            _customStyle.Add(line.Trim());
        }
    }
}