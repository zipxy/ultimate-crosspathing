﻿using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Models.Towers;
using Assets.Scripts.Unity;
using Assets.Scripts.Utils;
using BTD_Mod_Helper.Extensions;
using HarmonyLib;
using Il2CppSystem;
using MelonLoader;
using Console = System.Console;
using IntPtr = System.IntPtr;
using Task = Il2CppSystem.Threading.Tasks.Task;


namespace UltimateCrosspathing.Merging
{
    public class AsyncMerging
    {
        private static int totalTowerModelsAdded;
        private static List<string> FinishedTowers = new List<string>();
        private static List<string> CurrentTowers = new List<string>();
        public static List<TowerModel> FinishedTowerModels = new List<TowerModel>();
        private static ConcurrentBag<TowerModel> CurrentTowerModels = new ConcurrentBag<TowerModel>();
        private static List<string> MergeProgress = new List<string>();
        
        public static int pass;
        public static int middlePass;
        public static int finishedPass;


        public static void GetNextTowerBatch()
        {
            FinishedTowers.AddRange(CurrentTowers);
            CurrentTowers = Main.TowersEnabled
                .Where(pair => pair.Value && !FinishedTowers.Contains(pair.Key))
                .Select(pair => pair.Key)
                .Take(Main.TowerBatchSize)
                .ToList();
            if (CurrentTowers.Any())
            {
                MelonLogger.Msg("");
                MelonLogger.Msg("Next Batch of Towers is: " + string.Join(", ", CurrentTowers));
            }
        }

        public static bool AsyncCreateCrosspaths()
        {
            FinishedTowerModels.AddRange(CurrentTowerModels);
            CurrentTowerModels = new ConcurrentBag<TowerModel>();

            var mergeInfos = CurrentTowers
                .ToDictionary(tower => tower, tower => Towers.GetMergeInfo(tower).ToArray())
                .ToArray();

            var size = mergeInfos.Sum(kvp => kvp.Value.Length);

            if (size == 0)
            {
                GetNextTowerBatch();
                return CurrentTowers.Any() && AsyncCreateCrosspaths();
            }

            pass++;

            MelonLogger.Msg($"Step {GetStep(1, pass)} Completed, generated MergeInfo for {size} Crosspaths");

            var length = mergeInfos.Length - 1;
            var tasks = new Task[length];

            PrintProgress(CurrentTowerModels, size);
            
            for (var i = 0; i < length; i++)
            {
                var (tower, info) = mergeInfos[i];
                tasks[i] = Task.Run(new System.Action(() =>
                {
                    MergeTowerModel(tower, info);
                }));
            }
            // do one on the main thread still
            MergeTowerModel(mergeInfos[length].Key, mergeInfos[length].Value);
            
            // is this actually needed?
            /*foreach (var task in tasks)
            {
                task.Wait();
            }*/

            return true;
        }

        private static void MergeTowerModel(string tower, MergeInfo[] info)
        {
            foreach (var mergeInfo in info)
            {
                var newTowerModel = Towers.AsyncMerge(mergeInfo);
                CurrentTowerModels.Add(newTowerModel);
            }

            lock (MergeProgress)
            {
                MergeProgress.Add(tower);
            }
        }
        
        private static int GetStep(int step, int pas)
        {
            for (var i = 1; i < pas; i++)
            {
                step += 5;
            }

            return step;
        }

        public static void FinishCreatingCrosspaths()
        {
            var msg =
                $"Step {GetStep(2, pass)} Completed, generated TowerModels for {CurrentTowerModels.Count} Crosspaths";
            AddFinishedToMsg(ref msg);
            MelonLogger.Msg(msg);

            /*try
            {
                CurrentTowerModels.Do(Towers.PostMerge);
            }
            catch (System.Exception e)
            {
                MelonLogger.Error("Failed at postmerging " + e);
            }

            MelonLogger.Msg(
                $"Step {GetStep(3, pass)} Completed, applied PostMerge fixes to {CurrentTowerModels.Count} TowerModels");*/

            CurrentTowerModels.Do(model => Game.instance.model.AddTowerToGame(model));

            MelonLogger.Msg($"Step {GetStep(4, pass)} Completed, added {CurrentTowerModels.Count} TowerModels to the game");


            Task.Run(new System.Action(() =>
            {
                CurrentTowerModels.Do(result =>
                {
                    var fileName = $"MergedTowers/{result.baseId}/{result.name}.json";
                    FileIOUtil.SaveObject(fileName, result);
                });
                MelonLogger.Msg(
                    $"Step {GetStep(5, pass)} Completed, saved {CurrentTowerModels.Count} TowerModels as JSONs in {FileIOUtil.sandboxRoot}MergedTowers");
                OnFinishCrosspathing();
            }));
        }

        private static void OnFinishCrosspathing()
        {
            totalTowerModelsAdded += CurrentTowerModels.Count;
            if (!AsyncCreateCrosspaths())
            {
                foreach (var towerModel in FinishedTowerModels)
                {
                    Towers.AddUpgradeToPrevs(towerModel);
                }

                MelonLogger.Msg($"All Steps Completed! Added {totalTowerModelsAdded} total TowerModels.");
                Main.ShowFinishedPopup();
            }
        }

        private static void PrintProgress(ConcurrentBag<TowerModel> bag, int size)
        {
            Task.Run(new System.Action(() =>
            {
                Task.Delay(TimeSpan.FromSeconds(1)).Wait();
                var bagCount = bag.Count;
                if (bagCount < size)
                {
                    var msg = $"Step {GetStep(2, pass)} Progress: {bagCount} / {size} TowerModels created";
                    AddFinishedToMsg(ref msg);
                    MelonLogger.Msg(msg);
                    PrintProgress(bag, size);
                }
                else
                {
                    middlePass++;
                }
            }));
        }

        private static void AddFinishedToMsg(ref string msg)
        {
            lock (MergeProgress)
            {
                if (MergeProgress.Any())
                {
                    msg += " (finished " + string.Join(", ", MergeProgress) + ")";
                    MergeProgress.Clear();
                }
            }
        }
    }
}