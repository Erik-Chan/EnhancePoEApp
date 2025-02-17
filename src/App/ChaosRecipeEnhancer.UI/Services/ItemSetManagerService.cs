﻿using System.Collections.Generic;
using System.Linq;
using ChaosRecipeEnhancer.UI.Constants;
using ChaosRecipeEnhancer.UI.Models;
using ChaosRecipeEnhancer.UI.Models.ApiResponses;
using ChaosRecipeEnhancer.UI.Models.ApiResponses.BaseModels;
using ChaosRecipeEnhancer.UI.Models.Enums;
using ChaosRecipeEnhancer.UI.Properties;

namespace ChaosRecipeEnhancer.UI.Services;

public interface IItemSetManagerService
{
    public void UpdateStashMetadata(List<BaseStashTabMetadata> metadata);
    public bool UpdateStashContents(int setThreshold, List<int> selectedTabIndices, List<EnhancedItem> filteredStashContents);
    public void GenerateItemSets();
    public void CalculateItemAmounts();
    public void ResetCompletedSets();
    public void ResetItemAmounts();
    public List<Dictionary<ItemClass, int>> RetrieveCurrentItemCountsForFilterManipulation();
    public List<BaseStashTabMetadata> RetrieveStashTabMetadataList();
    public List<BaseStashTabMetadata> FlattenStashTabs(ListStashesResponse response);
    public bool RetrieveNeedsFetching();
    public bool RetrieveNeedsLowerLevel();
    public int RetrieveCompletedSetCount();
    public List<EnhancedItemSet> RetrieveSetsInProgress();
    public int RetrieveRingsAmount();
    public int RetrieveAmuletsAmount();
    public int RetrieveBeltsAmount();
    public int RetrieveChestsAmount();
    public int RetrieveWeaponsSmallAmount();
    public int RetrieveWeaponsBigAmount();
    public int RetrieveGlovesAmount();
    public int RetrieveHelmetsAmount();
    public int RetrieveBootsAmount();
}

public class ItemSetManagerService : IItemSetManagerService
{
    private int _setThreshold;
    private List<EnhancedItemSet> _setsInProgress = new();
    private List<EnhancedItem> _currentItemsFilteredForRecipe = new(); // filtered for chaos recipe
    private List<BaseStashTabMetadata> _stashTabMetadataListStashesResponse;

    #region Item Amount Properties

    // item amounts by kind that will be exposed
    // while ItemSetManagerService doesn't know,
    // others need to render this out to users
    public int RingsAmount { get; set; }
    public int AmuletsAmount { get; set; }
    public int BeltsAmount { get; set; }
    public int ChestsAmount { get; set; }
    public int WeaponsSmallAmount { get; set; }
    public int WeaponsBigAmount { get; set; }
    public int GlovesAmount { get; set; }
    public int HelmetsAmount { get; set; }
    public int BootsAmount { get; set; }

    #endregion

    // full set amounts will also need to be rendered
    public int CompletedSetCount { get; set; }
    public bool NeedsFetching { get; set; } = true;
    public bool NeedsLowerLevel { get; set; } = false;

    // temporary housing for this field that is needed by some components
    // i'd likely want to move this to its own service tbh

    public void UpdateStashMetadata(List<BaseStashTabMetadata> metadata)
    {
        _stashTabMetadataListStashesResponse = metadata;
    }

    // this is the primary method being called by external entities
    // return true if successful update, false if some error (likely caller error missing important data)
    public bool UpdateStashContents(int setThreshold, List<int> selectedTabIndices, List<EnhancedItem> filteredStashContents)
    {
        // if no tabs are selected (this isn't a realistic case)
        if (selectedTabIndices.Count == 0) return false;

        // setting some new properties
        _setThreshold = setThreshold;
        _currentItemsFilteredForRecipe = filteredStashContents;
        NeedsFetching = false;

        return true;
    }

    public void CalculateItemAmounts()
    {
        foreach (var item in _currentItemsFilteredForRecipe)
        {
            switch (item.DerivedItemClass)
            {
                case GameTerminology.Rings:
                    RingsAmount++; // 0: rings
                    break;
                case GameTerminology.Amulets:
                    AmuletsAmount++; // 1: amulets
                    break;
                case GameTerminology.Belts:
                    BeltsAmount++; // 2: belts
                    break;
                case GameTerminology.BodyArmors:
                    ChestsAmount++; // 3: chests
                    break;
                case GameTerminology.OneHandWeapons:
                    WeaponsSmallAmount++; // 4: weaponsSmall
                    break;
                case GameTerminology.TwoHandWeapons:
                    WeaponsBigAmount++; // 5: weaponsBig
                    break;
                case GameTerminology.Gloves:
                    GlovesAmount++; // 6: gloves
                    break;
                case GameTerminology.Helmets:
                    HelmetsAmount++; // 7: helmets
                    break;
                case GameTerminology.Boots:
                    BootsAmount++; // 8: boots
                    break;
            }
        }

        NeedsFetching = false;
    }

    public void ResetCompletedSets()
    {
        CompletedSetCount = 0;
    }

    public void ResetItemAmounts()
    {
        // reset all item amounts
        RingsAmount = 0;
        AmuletsAmount = 0;
        BeltsAmount = 0;
        ChestsAmount = 0;
        WeaponsSmallAmount = 0;
        WeaponsBigAmount = 0;
        GlovesAmount = 0;
        HelmetsAmount = 0;
        BootsAmount = 0;
    }

    public void GenerateItemSets()
    {
        // filter for chaos recipe eligible items
        var eligibleChaosItems = _currentItemsFilteredForRecipe
            .Where(x => x.IsChaosRecipeEligible)
            .ToList();

        // sorting both of our item lists by item class
        // it's important to prioritize two-handed weapons at the beginning so our set composition is more efficient
        eligibleChaosItems = eligibleChaosItems.OrderByDescending(item => item.DerivedItemClass == "TwoHandWeapons")
            .ThenBy(item => item.DerivedItemClass)
            .ToList();

        _currentItemsFilteredForRecipe = _currentItemsFilteredForRecipe.OrderByDescending(item => item.DerivedItemClass == "TwoHandWeapons")
            .ThenBy(item => item.DerivedItemClass)
            .ToList();

        int trueSetThreshold;

        // if Early Set Turn In is enabled, we need to modify the upper bound to match how many sets we can possibly make
        // if we have less chaos items than the set threshold, we can only make as many sets as we have chaos items
        // otherwise we can make as many sets as the set threshold
        if (Settings.Default.VendorSetsEarly)
        {
            if (eligibleChaosItems.Count > _setThreshold)
            {
                trueSetThreshold = _setThreshold;
            }
            else
            {
                trueSetThreshold = eligibleChaosItems.Count;
            }
        }
        else
        {
            trueSetThreshold = _setThreshold;
        }

        if (eligibleChaosItems.Count < trueSetThreshold || eligibleChaosItems.Count == 0)
        {
            NeedsLowerLevel = true;
        }
        else
        {
            NeedsLowerLevel = false;
        }

        if (Settings.Default.DoNotPreserveLowItemLevelGear)
        {
            GenerateItemSets_Greedy(eligibleChaosItems, trueSetThreshold);
        }
        else
        {
            GenerateItemSets_ConserveChaosItems(eligibleChaosItems, trueSetThreshold);
        }
    }

    private void GenerateItemSets_ConserveChaosItems(List<EnhancedItem> eligibleChaosItems, int trueSetThreshold)
    {
        _setsInProgress.Clear();
        var listOfSets = new List<EnhancedItemSet>();

        // for every set we will start by trying to add a chaos item (and reporting if we need more low level chaos items)
        // this loop is NOT responsible for filling sets their entirety
        // however, if Early Set Turn In is enabled, we need to modify the upper bound to match how many sets we can possibly make
        // rather than trying to 'fill' the sets, we're just trying to make as many sets as possible
        for (var i = 0; i < trueSetThreshold; i++)
        {
            // create new 'empty' enhanced item set
            var enhancedItemSet = new EnhancedItemSet();

            // if there are still chaos items left in our stash
            if (eligibleChaosItems.Count != 0)
            {
                // try to add a single eligible chaos item in the set (where we're iterate in our loop on line 166)
                foreach (var item in eligibleChaosItems)
                {
                    var addSuccessful = enhancedItemSet.TryAddItem(item);

                    // if we successfully add to set (i.e. it wasn't an item slot that was already taken)
                    if (addSuccessful)
                    {
                        // remove from our stash
                        _currentItemsFilteredForRecipe.Remove(item);
                        // remove from the list of eligible chaos items
                        eligibleChaosItems.Remove(item);

                        // break out of loop
                        break;
                    }
                }
            }

            listOfSets.Add(enhancedItemSet);
        }

        // for every set we still need to loop to create the rest of the sets (even if they don't have chaos items in them)
        // when composing sets we will always try to add the closest item to the set to enhance user experience during item picking
        // we once again see the modified upper bound based on Early Set Turn In
        for (var i = 0; i < trueSetThreshold; i++)
        {
            // iterate until the end of time (jk)
            while (true)
            {
                // the closest item is assumed to be in another galaxy
                EnhancedItem closestMissingItem = null;
                var minDistance = double.PositiveInfinity;

                // find the for real closes item
                // this is a nested for over each other item in our current item filtered for recipe
                // probably could be optimized? maybe?
                foreach (var item in _currentItemsFilteredForRecipe
                             .Where(item => listOfSets[i].IsItemClassNeeded(item) && // item is of a class we need
                                            listOfSets[i].GetItemDistance(item) < minDistance)) // item is closer than the current closest
                {
                    minDistance = listOfSets[i].GetItemDistance(item);
                    closestMissingItem = item;
                }

                if (closestMissingItem is not null)
                {
                    // if we found a new closer we're good to add it to our enhanced set
                    _ = listOfSets[i].TryAddItem(closestMissingItem);
                    // promptly remove it from our pool of 'available' items
                    // do i actually want to do this? lol
                    _ = _currentItemsFilteredForRecipe.Remove(closestMissingItem);
                }
                // you didn't find a closer item, gg break out of infinite loop
                else
                {
                    break;
                }

                // my reason for separating out this logic is that it's a bit more readable and debuggable

                // if we have a recipe qualifier we can stop looking for items
                var canProduce = listOfSets[i].Items.FirstOrDefault(x => x.IsChaosRecipeEligible, null);

                // if a set is complete (i.e. it has a recipe qualifier and no empty item slots)
                // we can increment our completed set count
                // for a set to be completed it needs to meet both of these conditions
                if (canProduce is not null)
                {
                    listOfSets[i].HasRecipeQualifier = true;

                    if (listOfSets[i].EmptyItemSlots.Count == 0)
                    {
                        CompletedSetCount++;
                    }
                }
            }

            // add new enhanced item set to our list of sets in progress
            _setsInProgress = listOfSets;
        }
    }

    private void GenerateItemSets_Greedy(List<EnhancedItem> eligibleChaosItems, int trueSetThreshold)
    {
        // Clear any existing progress in item set generation
        _setsInProgress.Clear();
        var listOfSets = new List<EnhancedItemSet>();

        // Iteratively create item sets based on the number of available chaos items
        for (var i = 0; i < trueSetThreshold; i++)
        {
            // Initialize a new item set
            var enhancedItemSet = new EnhancedItemSet();

            // Add a chaos item to the set if any are available
            if (eligibleChaosItems.Any())
            {
                var chaosItem = eligibleChaosItems.First();

                if (enhancedItemSet.TryAddItem(chaosItem))
                {
                    // Remove the added chaos item from the available pools
                    eligibleChaosItems.Remove(chaosItem);
                    _currentItemsFilteredForRecipe.Remove(chaosItem);

                    // let's attempt to fill in gaps with one-handed weapons if possible
                    if (chaosItem.DerivedItemClass == GameTerminology.OneHandWeapons)
                    {
                        var oneHandedWeapon = _currentItemsFilteredForRecipe
                            .FirstOrDefault(x => x.DerivedItemClass == GameTerminology.OneHandWeapons);

                        if (oneHandedWeapon is not null)
                        {
                            enhancedItemSet.TryAddItem(oneHandedWeapon);
                            eligibleChaosItems.Remove(oneHandedWeapon);
                            _currentItemsFilteredForRecipe.Remove(oneHandedWeapon);
                        }
                    }
                }
            }

            // Continuously try to fill the rest of the set with available items
            while (true)
            {
                // Initialize variables to find the closest missing item for the set
                EnhancedItem closestMissingItem = null;
                var minDistance = double.PositiveInfinity;

                // Iterate over all available items to find the closest needed item
                foreach (var item in _currentItemsFilteredForRecipe
                             .Where(item => enhancedItemSet.IsItemClassNeeded(item) && // Check if the item class is needed for the set
                                            enhancedItemSet.GetItemDistance(item) < minDistance)) // Check if the item is closer than the current closest
                {
                    // Update closest item and distance if a closer item is found
                    minDistance = enhancedItemSet.GetItemDistance(item);
                    closestMissingItem = item;
                }

                // If a closest missing item is found, add it to the set
                if (closestMissingItem != null)
                {

                    if (enhancedItemSet.TryAddItem(closestMissingItem))
                    {
                        // Remove the item from the pool of available items
                        eligibleChaosItems.Remove(closestMissingItem);
                        _currentItemsFilteredForRecipe.Remove(closestMissingItem);

                        // let's attempt to fill in gaps with one-handed weapons if possible
                        if (closestMissingItem.DerivedItemClass == GameTerminology.OneHandWeapons)
                        {
                            var oneHandedWeapon = _currentItemsFilteredForRecipe
                                .FirstOrDefault(x => x.DerivedItemClass == GameTerminology.OneHandWeapons);

                            if (oneHandedWeapon is not null)
                            {
                                enhancedItemSet.TryAddItem(oneHandedWeapon);
                                _currentItemsFilteredForRecipe.Remove(oneHandedWeapon);
                            }
                        }
                    }
                }
                else
                {
                    // Break the loop if no suitable item is found
                    break;
                }

                // Check if the current set is complete (i.e., no empty item slots)
                if (enhancedItemSet.EmptyItemSlots.Count == 0)
                {
                    break;
                }
            }

            // Add the newly created set to the list of sets
            listOfSets.Add(enhancedItemSet);
        }

        for (var i = 0; i < trueSetThreshold; i++)
        {
            // my reason for separating out this logic is that it's a bit more readable and debuggable

            // if we have a recipe qualifier we can stop looking for items
            var canProduce = listOfSets[i].Items.FirstOrDefault(x => x.IsChaosRecipeEligible, null);

            // if a set is complete (i.e. it has a recipe qualifier and no empty item slots)
            // we can increment our completed set count
            // for a set to be completed it needs to meet both of these conditions
            if (canProduce is not null)
            {
                listOfSets[i].HasRecipeQualifier = true;
            }
        }

        // Update the sets in progress with the newly created list of sets
        _setsInProgress = listOfSets;
        // Update the count of completed sets based on the number of sets with no empty item slots
        CompletedSetCount = listOfSets.Count(set => set.EmptyItemSlots.Count == 0);
    }

    public List<Dictionary<ItemClass, int>> RetrieveCurrentItemCountsForFilterManipulation()
    {
        var result = new List<Dictionary<ItemClass, int>>
        {
            new()
            {
                {ItemClass.Rings, RingsAmount},
                {ItemClass.Amulets, AmuletsAmount},
                {ItemClass.Belts, BeltsAmount},
                {ItemClass.BodyArmours, ChestsAmount},
                {ItemClass.OneHandWeapons, WeaponsSmallAmount},
                {ItemClass.TwoHandWeapons, WeaponsBigAmount},
                {ItemClass.Gloves, GlovesAmount},
                {ItemClass.Helmets, HelmetsAmount},
                {ItemClass.Boots, BootsAmount}
            }
        };

        return result;
    }

    public List<BaseStashTabMetadata> FlattenStashTabs(ListStashesResponse response)
    {
        var allTabs = new List<BaseStashTabMetadata>();

        foreach (var tab in response.StashTabs)
        {
            if (tab.Type != "Folder")
            {
                allTabs.Add(tab); // If not folder, add tab
            }
            else if (tab.Children != null)
            {
                allTabs.AddRange(tab.Children); // Add the children if any
            }
        }

        return allTabs;
    }

    #region Properties as Functions

    // workaround to expose properties as functions on our interface
    public List<BaseStashTabMetadata> RetrieveStashTabMetadataList() => _stashTabMetadataListStashesResponse;
    public bool RetrieveNeedsFetching() => NeedsFetching;
    public bool RetrieveNeedsLowerLevel() => NeedsLowerLevel;
    public int RetrieveCompletedSetCount() => CompletedSetCount;
    public List<EnhancedItemSet> RetrieveSetsInProgress() => _setsInProgress;

    // item amount public properties via functions
    public int RetrieveRingsAmount() => RingsAmount;
    public int RetrieveAmuletsAmount() => AmuletsAmount;
    public int RetrieveBeltsAmount() => BeltsAmount;
    public int RetrieveChestsAmount() => ChestsAmount;
    public int RetrieveWeaponsSmallAmount() => WeaponsSmallAmount;
    public int RetrieveWeaponsBigAmount() => WeaponsBigAmount;
    public int RetrieveGlovesAmount() => GlovesAmount;
    public int RetrieveHelmetsAmount() => HelmetsAmount;
    public int RetrieveBootsAmount() => BootsAmount;

    #endregion
}