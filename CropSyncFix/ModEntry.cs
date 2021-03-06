﻿using Harmony;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley.TerrainFeatures;
using System.Linq;
using StardewValley.Network;
using Netcode;
using System;
using StardewValley.Objects;
using StardewValley.Locations;
using xTile.ObjectModel;
using xTile.Tiles;
using StardewValley.Monsters;
using System.Reflection;

namespace CropSyncFix
{
    /// <summary>The mod entry point.</summary>
    public class ModEntry : Mod
    {
        public static ModEntry me;
        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            me = this;
            //Harmony patcher
            var harmony = HarmonyInstance.Create("com.github.kirbylink.cropsyncfix");

            var locOriginal = typeof(GameLocation).GetMethod("DayUpdate");
            var locPrefix = helper.Reflection.GetMethod(typeof(LocationDayUpdateMethod), "Prefix").MethodInfo;

            var hdNDOriginal = typeof(HoeDirt).GetMethod("dayUpdate");
            var hdUTOriginal = typeof(HoeDirt).GetMethod("tickUpdate");
            var hdNDPrefix = helper.Reflection.GetMethod(typeof(HoeDirtDayUpdateMethod), "NewDayPrefix").MethodInfo;
            var hdUTPrefix = helper.Reflection.GetMethod(typeof(HoeDirtDayUpdateMethod), "UpdateTickPrefix").MethodInfo;

            harmony.Patch(locOriginal, new HarmonyMethod(locPrefix), null);
            harmony.Patch(hdNDOriginal, new HarmonyMethod(hdNDPrefix), null);
            harmony.Patch(hdUTOriginal, new HarmonyMethod(hdUTPrefix), null);

            helper.Events.World.TerrainFeatureListChanged += this.WorldEvents_TerrainFeatureListChanged;
        }

        private void WorldEvents_TerrainFeatureListChanged(object sender, WorldTerrainFeatureListChangedEventArgs e)
        {
            if (e.Location is Farm)
            {
                foreach (KeyValuePair<Vector2, TerrainFeature> pair in e.Added)
                {
                    if (pair.Value is HoeDirt)
                    {
                        this.Monitor.Log($"Added: [{pair.Key.X}, {pair.Key.Y}]");
                    }
                }
                foreach (KeyValuePair<Vector2, TerrainFeature> pair in e.Removed)
                {
                    if (pair.Value is HoeDirt)
                    {
                        this.Monitor.Log($"Removed: [{pair.Key.X}, {pair.Key.Y}]");
                    }
                }
            }
        }
    }

    public static class LocationDayUpdateMethod
    {

        static bool Prefix(GameLocation __instance, int dayOfMonth)
        {
            __instance.updateMap();
            __instance.temporarySprites.Clear();

            //Update Terrain Features
            var tFeatures = __instance.terrainFeatures;
            
            for (int i = tFeatures.Count() - 1; i >= 0; i--)
            {
                KeyValuePair<Vector2, TerrainFeature> pair = tFeatures.Pairs.ElementAt(i);
                if (!__instance.isTileOnMap(pair.Key) || (!__instance.IsFarm && (pair.Value is HoeDirt && ((pair.Value as HoeDirt).crop == null || (pair.Value as HoeDirt).crop.forageCrop.Value))))
                {
                    tFeatures.Remove(pair.Key);
                }
                else 
                {
                    pair.Value.dayUpdate(__instance, pair.Key);
                }
            }
            
            //Update Large Terrain Features
            if (__instance.largeTerrainFeatures.Count > 0)
            {
                foreach (LargeTerrainFeature largeTerrainFeature in __instance.largeTerrainFeatures)
                    largeTerrainFeature.dayUpdate(__instance);
            }

            //Update Objects
            var objects = __instance.objects;
            for (int i = objects.Count() - 1; i >= 0; i--)
            {
                KeyValuePair<Vector2, StardewValley.Object> pair = objects.Pairs.ElementAt(i);
                pair.Value.DayUpdate(__instance);

                if (__instance.IsOutdoors)
                {
                    if (Game1.dayOfMonth % 7 == 0 && !(__instance is Farm))
                    {
                        if (pair.Value.IsSpawnedObject)
                        {
                            objects.Remove(pair.Key);
                        }

                        __instance.numberOfSpawnedObjectsOnMap = 0;
                        __instance.spawnObjects();
                        __instance.spawnObjects();
                    }
                }
            }

            if (!(__instance is FarmHouse))
            {
                __instance.debris.Filter(d => d.item != null);
            }

            if (__instance.IsOutdoors)
            {
                __instance.spawnObjects();

                if (Game1.dayOfMonth == 1)
                {
                    __instance.spawnObjects();
                }

                if (Game1.stats.DaysPlayed < 4U)
                {
                    __instance.spawnObjects();
                }

                bool flag = false;
                foreach (Component layer in __instance.map.Layers)
                {
                    if (layer.Id.Equals("Paths"))
                    {
                        flag = true;
                        break;
                    }
                }

                if (flag && !(__instance is Farm))
                {
                    for (int index1 = 0; index1 < __instance.map.Layers[0].LayerWidth; ++index1)
                    {
                        for (int index2 = 0; index2 < __instance.map.Layers[0].LayerHeight; ++index2)
                        {
                            if (__instance.map.GetLayer("Paths").Tiles[index1, index2] != null && Game1.random.NextDouble() < 0.5)
                            {
                                Vector2 key = new Vector2(index1, index2);
                                int which = -1;
                                switch (__instance.map.GetLayer("Paths").Tiles[index1, index2].TileIndex)
                                {
                                    case 9:
                                        which = 1;
                                        if (Game1.currentSeason.Equals("winter"))
                                        {
                                            which += 3;
                                            break;
                                        }
                                        break;
                                    case 10:
                                        which = 2;
                                        if (Game1.currentSeason.Equals("winter"))
                                        {
                                            which += 3;
                                            break;
                                        }
                                        break;
                                    case 11:
                                        which = 3;
                                        break;
                                    case 12:
                                        which = 6;
                                        break;
                                }

                                if (which != -1 && !tFeatures.ContainsKey(key) && !objects.ContainsKey(key))
                                {
                                    tFeatures.Add(key, new Tree(which, 2));
                                }
                            }
                        }
                    }
                }
            }

            __instance.LightLevel = 0.0f;

            if (__instance.Name.Equals("BugLand"))
            {
                for (int index1 = 0; index1 < __instance.map.Layers[0].LayerWidth; ++index1)
                {
                    for (int index2 = 0; index2 < __instance.map.Layers[0].LayerHeight; ++index2)
                    {
                        if (Game1.random.NextDouble() < 0.33)
                        {
                            Tile tile = __instance.map.GetLayer("Paths").Tiles[index1, index2];
                            if (tile != null)
                            {
                                Vector2 vector2 = new Vector2((float)index1, (float)index2);
                                switch (tile.TileIndex)
                                {
                                    case 13:
                                    case 14:
                                    case 15:
                                        if (!__instance.objects.ContainsKey(vector2))
                                        {
                                            __instance.objects.Add(vector2, new StardewValley.Object(vector2, GameLocation.getWeedForSeason(Game1.random, "spring"), 1));
                                            continue;
                                        }
                                        continue;
                                    case 16:
                                        if (!__instance.objects.ContainsKey(vector2))
                                        {
                                            __instance.objects.Add(vector2, new StardewValley.Object(vector2, Game1.random.NextDouble() < 0.5 ? 343 : 450, 1));
                                            continue;
                                        }
                                        continue;
                                    case 17:
                                        if (!__instance.objects.ContainsKey(vector2))
                                        {
                                            __instance.objects.Add(vector2, new StardewValley.Object(vector2, Game1.random.NextDouble() < 0.5 ? 343 : 450, 1));
                                            continue;
                                        }
                                        continue;
                                    case 18:
                                        if (!__instance.objects.ContainsKey(vector2))
                                        {
                                            __instance.objects.Add(vector2, new StardewValley.Object(vector2, Game1.random.NextDouble() < 0.5 ? 294 : 295, 1));
                                            continue;
                                        }
                                        continue;
                                    case 28:
                                        if (__instance.isTileLocationTotallyClearAndPlaceable(vector2) && __instance.characters.Count < 50)
                                        {
                                            __instance.characters.Add((NPC)new Grub(new Vector2(vector2.X * 64f, vector2.Y * 64f), true));
                                            continue;
                                        }
                                        continue;
                                    default:
                                        continue;
                                }
                            }
                        }
                    }
                }
            }

            __instance.addLightGlows();
            return false;
        }
    }

    public static class HoeDirtDayUpdateMethod
    {

        static bool NewDayPrefix(HoeDirt __instance, GameLocation environment, Vector2 tileLocation)
        {

            Crop crop = __instance.crop;
            var state = __instance.state.Value;
            var fertilizer = __instance.fertilizer.Value;

            if (crop != null)
            {
                crop.newDay(state, fertilizer, (int)tileLocation.X, (int)tileLocation.Y, environment);

                if (environment.IsOutdoors && Game1.currentSeason.Equals("winter") && !crop.isWildSeedCrop())
                {
                    __instance.destroyCrop(tileLocation, false, environment);
                }
            }

            if ((fertilizer == 370 && Game1.random.NextDouble() < 0.33) || (fertilizer == 371 && Game1.random.NextDouble() < 0.66))
                return false;

            __instance.state.Value = 0;
            return false;
        }

        public static void UpdateTickPrefix(HoeDirt __instance)
        {
            if (__instance != null)
            {
                var field = AccessTools.Field(typeof(HoeDirt), "queuedActions");
                var actions = field.GetValue(__instance);
                foreach (Action<GameLocation, Vector2> queuedAction in (List<Action<GameLocation, Vector2>>)actions)
                {
                    ModEntry.me.Monitor.Log($"Actions: {queuedAction}");
                }
            }
        }
    }
}
