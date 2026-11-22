using System;
using System.Collections.Generic;

namespace Ferryman.Voyage
{
    public enum VoyageEventKind
    {
        None,
        Combat,
        Special
    }

    [Serializable]
    public sealed class PlannedVoyageEvent
    {
        public VoyageEventKind kind;
        public EncounterDefinition encounter;
        public SeaEventDefinition specialEvent;
        public bool isFixed;
    }

    public static class VoyageEventPlanner
    {
        public static PlannedVoyageEvent[] Build(VoyageDefinition definition, int seed)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (definition.stops == null)
                throw new ArgumentException("Voyage stops are missing.", nameof(definition));

            var plan = new PlannedVoyageEvent[definition.stops.Length];
            var freeSeas = new List<int>();
            var fixedSpecialUses = new Dictionary<SeaEventDefinition, int>();
            int fixedCombatCount = 0;
            int fixedSpecialCount = 0;
            for (int i = 0; i < definition.stops.Length; i++)
            {
                VoyageStop stop = definition.stops[i];
                if (stop == null)
                    throw new ArgumentException("Voyage stop " + i + " is missing.", nameof(definition));
                plan[i] = new PlannedVoyageEvent();
                if (stop.fixedEncounter != null && stop.fixedSpecialEvent != null)
                    throw new ArgumentException("Voyage stop " + i + " has two fixed events.", nameof(definition));
                if (stop.kind != VoyageStopKind.Sea)
                {
                    if (stop.fixedEncounter != null || stop.fixedSpecialEvent != null)
                        throw new ArgumentException("Island stop " + i + " cannot contain a sea event.", nameof(definition));
                    continue;
                }

                if (stop.fixedEncounter != null)
                {
                    plan[i].kind = VoyageEventKind.Combat;
                    plan[i].encounter = stop.fixedEncounter;
                    plan[i].isFixed = true;
                    fixedCombatCount++;
                }
                else if (stop.fixedSpecialEvent != null)
                {
                    plan[i].kind = VoyageEventKind.Special;
                    plan[i].specialEvent = stop.fixedSpecialEvent;
                    plan[i].isFixed = true;
                    fixedSpecialCount++;
                    fixedSpecialUses.TryGetValue(stop.fixedSpecialEvent, out int used);
                    fixedSpecialUses[stop.fixedSpecialEvent] = used + 1;
                }
                else
                    freeSeas.Add(i);
            }

            var encounters = new List<EncounterDefinition>();
            if (definition.randomEncounters != null)
            {
                foreach (var encounter in definition.randomEncounters)
                {
                    if (encounter != null)
                        encounters.Add(encounter);
                }
            }

            var specialBudgets = new List<SpecialBudget>();
            var configuredSpecials = new HashSet<SeaEventDefinition>();
            int specialCapacity = 0;
            if (definition.specialEvents != null)
            {
                foreach (var entry in definition.specialEvents)
                {
                    if (entry == null || entry.definition == null)
                        continue;
                    if (!configuredSpecials.Add(entry.definition))
                        throw new ArgumentException("A special event type has more than one capacity entry.", nameof(definition));
                    fixedSpecialUses.TryGetValue(entry.definition, out int used);
                    int remaining = Math.Max(0, Math.Max(0, entry.maxAppearances) - used);
                    if (remaining == 0)
                        continue;
                    specialBudgets.Add(new SpecialBudget { definition = entry.definition, remaining = remaining });
                    specialCapacity += Math.Min(remaining, freeSeas.Count - specialCapacity);
                }
            }

            int wantedCombat = Math.Max(0, Math.Max(0, definition.enemyEncounterCount) - fixedCombatCount);
            int combatCount = encounters.Count > 0 ? Math.Min(wantedCombat, freeSeas.Count) : 0;
            int wantedSpecial = Math.Max(0, Math.Max(0, definition.specialEventCount) - fixedSpecialCount);
            int specialCount = Math.Min(wantedSpecial, Math.Min(specialCapacity, freeSeas.Count - combatCount));
            var random = new Random(seed);
            for (int i = freeSeas.Count - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                int value = freeSeas[i];
                freeSeas[i] = freeSeas[other];
                freeSeas[other] = value;
            }
            for (int i = 0; i < combatCount; i++)
            {
                PlannedVoyageEvent entry = plan[freeSeas[i]];
                entry.kind = VoyageEventKind.Combat;
                entry.encounter = encounters[random.Next(encounters.Count)];
            }
            for (int i = 0; i < specialCount; i++)
            {
                int selected = random.Next(specialBudgets.Count);
                SpecialBudget budget = specialBudgets[selected];
                PlannedVoyageEvent entry = plan[freeSeas[combatCount + i]];
                entry.kind = VoyageEventKind.Special;
                entry.specialEvent = budget.definition;
                if (--budget.remaining == 0)
                    specialBudgets.RemoveAt(selected);
            }
            return plan;
        }

        private sealed class SpecialBudget
        {
            public SeaEventDefinition definition;
            public int remaining;
        }
    }
}
