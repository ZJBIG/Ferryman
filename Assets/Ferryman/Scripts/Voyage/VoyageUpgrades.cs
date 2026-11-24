using System;
using UnityEngine;

namespace Ferryman.Voyage
{
    public enum VoyageUpgradeKind
    {
        Attack, AttackSpeed, Durability, RepairSpeed, RepairCount, RepairRange
    }

    public static class VoyageUpgrades
    {
        public const int Count = 6;
        public const int MaxRank = 5;
        public static string Name(VoyageUpgradeKind kind) => kind switch
        {
            VoyageUpgradeKind.Attack => "火力",
            VoyageUpgradeKind.AttackSpeed => "连射",
            VoyageUpgradeKind.Durability => "船体强化",
            VoyageUpgradeKind.RepairSpeed => "快速维修",
            VoyageUpgradeKind.RepairCount => "并行维修",
            _ => "维修范围"
        };
        public static string Description(VoyageUpgradeKind kind) => kind switch
        {
            VoyageUpgradeKind.Attack => "每级增加 20% 基础攻击伤害",
            VoyageUpgradeKind.AttackSpeed => "每级增加 15% 射击频率",
            VoyageUpgradeKind.Durability => "每级增加并恢复 150 船耐久",
            VoyageUpgradeKind.RepairSpeed => "每级增加 25% 基础维修速度",
            VoyageUpgradeKind.RepairCount => "每级同时多维修一个点",
            _ => "每级增加一格维修范围"
        };
        public static bool UsesSpecialItems(VoyageUpgradeKind kind) => kind == VoyageUpgradeKind.RepairCount || kind == VoyageUpgradeKind.RepairRange;
        public static int Price(VoyageUpgradeKind kind, int rank) => (UsesSpecialItems(kind) ? 1 : 50) * (rank + 1);
        public static bool IsValid(VoyageUpgradeKind kind) => (int)kind >= 0 && (int)kind < Count;
        public static int[] ReadRanks(int[] saved)
        {
            // Unity serializes null arrays as []; both represent a pre-upgrade save.
            if (saved == null || saved.Length == 0)
                return new int[Count];
            if (saved.Length != Count || Array.Exists(saved, rank => rank < 0 || rank > MaxRank))
                throw new System.IO.InvalidDataException("升级等级数据无效。");
            return (int[])saved.Clone();
        }
    }
}
