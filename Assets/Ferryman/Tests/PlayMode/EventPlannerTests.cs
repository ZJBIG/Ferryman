using System;
using System.Collections.Generic;
using System.Linq;
using Ferryman.Voyage;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ferryman.Tests
{
    public sealed class EventPlannerTests
    {
        private readonly List<ScriptableObject> ownedDefinitions = new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var definition in ownedDefinitions)
                Object.DestroyImmediate(definition);
            ownedDefinitions.Clear();
        }

        [Test]
        public void FixedEventsRemainEvenOverQuotaAndRejectConflictingConfiguration()
        {
            VoyageDefinition voyage = CreateVoyage(4);
            EncounterDefinition encounter = CreateDefinition<EncounterDefinition>();
            SeaEventDefinition special = CreateDefinition<SeaEventDefinition>();
            voyage.enemyEncounterCount = 0;
            voyage.specialEventCount = 0;
            voyage.randomEncounters = new[] { encounter };
            voyage.specialEvents = new[] { new SpecialEventEntry { definition = special, maxAppearances = 0 } };
            voyage.stops[1].fixedEncounter = encounter;
            voyage.stops[3].fixedSpecialEvent = special;
            PlannedVoyageEvent[] plan = VoyageEventPlanner.Build(voyage, 7);
            Assert.That(plan[1].isFixed, Is.True);
            Assert.That(plan[1].encounter, Is.SameAs(encounter));
            Assert.That(plan[3].isFixed, Is.True);
            Assert.That(plan[3].specialEvent, Is.SameAs(special));
            Assert.That(plan.Count(entry => entry.kind == VoyageEventKind.Combat), Is.EqualTo(1));
            Assert.That(plan.Count(entry => entry.kind == VoyageEventKind.Special), Is.EqualTo(1));
            Assert.That(plan.Where((entry, index) => index % 2 == 0).All(entry => entry.kind == VoyageEventKind.None), Is.True);
            Assert.That(plan[5].kind, Is.EqualTo(VoyageEventKind.None));
            Assert.That(plan[7].kind, Is.EqualTo(VoyageEventKind.None));

            voyage.stops[1].fixedSpecialEvent = special;
            Assert.Throws<ArgumentException>(() => VoyageEventPlanner.Build(voyage, 7));
            voyage.stops[1].fixedSpecialEvent = null;
            voyage.stops[0].fixedEncounter = encounter;
            Assert.Throws<ArgumentException>(() => VoyageEventPlanner.Build(voyage, 7));
            voyage.stops[0].fixedEncounter = null;
            voyage.specialEvents = new[]
            {
                new SpecialEventEntry { definition = special, maxAppearances = 1 },
                new SpecialEventEntry { definition = special, maxAppearances = 2 }
            };
            Assert.Throws<ArgumentException>(() => VoyageEventPlanner.Build(voyage, 7));
        }

        [Test]
        public void CapacityPreservesCombatAndLimitsEachSpecialTypeIncludingFixedUses()
        {
            VoyageDefinition voyage = CreateVoyage(5);
            EncounterDefinition encounter = CreateDefinition<EncounterDefinition>();
            SeaEventDefinition first = CreateDefinition<SeaEventDefinition>();
            SeaEventDefinition second = CreateDefinition<SeaEventDefinition>();
            voyage.enemyEncounterCount = 3;
            voyage.specialEventCount = 5;
            voyage.randomEncounters = new[] { encounter };
            voyage.specialEvents = new[]
            {
                new SpecialEventEntry { definition = first, maxAppearances = 1 },
                new SpecialEventEntry { definition = second, maxAppearances = 2 }
            };
            PlannedVoyageEvent[] plan = VoyageEventPlanner.Build(voyage, 10);
            Assert.That(plan.Count(entry => entry.kind == VoyageEventKind.Combat), Is.EqualTo(3));
            Assert.That(plan.Count(entry => entry.kind == VoyageEventKind.Special), Is.EqualTo(2),
                "Capacity shortages must remove special events before combat encounters.");
            Assert.That(plan.Count(entry => entry.specialEvent == first), Is.LessThanOrEqualTo(1));
            Assert.That(plan.Count(entry => entry.specialEvent == second), Is.LessThanOrEqualTo(2));

            voyage.enemyEncounterCount = 0;
            plan = VoyageEventPlanner.Build(voyage, 10);
            Assert.That(plan.Count(entry => entry.kind == VoyageEventKind.Special), Is.EqualTo(3));
            Assert.That(plan.Count(entry => entry.specialEvent == first), Is.EqualTo(1));
            Assert.That(plan.Count(entry => entry.specialEvent == second), Is.EqualTo(2));
            voyage.stops[1].fixedSpecialEvent = first;
            plan = VoyageEventPlanner.Build(voyage, 10);
            Assert.That(plan[1].isFixed, Is.True);
            Assert.That(plan.Count(entry => entry.specialEvent == first), Is.EqualTo(1),
                "A fixed appearance must consume that type's random capacity.");
            Assert.That(plan.Count(entry => entry.specialEvent == second), Is.EqualTo(2));

            voyage.enemyEncounterCount = 3;
            voyage.randomEncounters = new EncounterDefinition[] { null };
            plan = VoyageEventPlanner.Build(voyage, 10);
            Assert.That(plan.Count(entry => entry.kind == VoyageEventKind.Combat), Is.Zero,
                "An empty combat library must not create a contentless combat event.");
            Assert.That(plan.Count(entry => entry.kind == VoyageEventKind.Special), Is.EqualTo(3));
        }

        [Test]
        public void SeededPlanningIsStableAndCanUseEveryEligibleSea()
        {
            VoyageDefinition voyage = CreateVoyage(4);
            EncounterDefinition first = CreateDefinition<EncounterDefinition>();
            EncounterDefinition second = CreateDefinition<EncounterDefinition>();
            first.id = "first";
            second.id = "second";
            voyage.enemyEncounterCount = 3;
            voyage.specialEventCount = 0;
            voyage.randomEncounters = new[] { first, second };
            Assert.That(Signature(VoyageEventPlanner.Build(voyage, 42)),
                Is.EqualTo(Signature(VoyageEventPlanner.Build(voyage, 42))));
            int[] seaIndices = { 1, 3, 5, 7 };
            var unselectedSeas = new HashSet<int>();
            for (int seed = 0; seed < 64; seed++)
            {
                PlannedVoyageEvent[] plan = VoyageEventPlanner.Build(voyage, seed);
                Assert.That(plan.Count(entry => entry.kind == VoyageEventKind.Combat), Is.EqualTo(3));
                unselectedSeas.Add(seaIndices.Single(index => plan[index].kind == VoyageEventKind.None));
            }
            Assert.That(unselectedSeas.Count, Is.EqualTo(4), "A sea position was systematically excluded from random placement.");
        }

        private VoyageDefinition CreateVoyage(int seaCount)
        {
            VoyageDefinition voyage = CreateDefinition<VoyageDefinition>();
            voyage.stops = new VoyageStop[seaCount * 2 + 1];
            for (int i = 0; i < voyage.stops.Length; i++)
                voyage.stops[i] = new VoyageStop { kind = i % 2 == 0 ? VoyageStopKind.Island : VoyageStopKind.Sea };
            return voyage;
        }

        private T CreateDefinition<T>() where T : ScriptableObject
        {
            T definition = ScriptableObject.CreateInstance<T>();
            ownedDefinitions.Add(definition);
            return definition;
        }

        private static string Signature(PlannedVoyageEvent[] plan)
        {
            return string.Join("|", plan.Select(entry => entry.kind + ":" + entry.isFixed + ":"
                + (entry.encounter != null ? entry.encounter.id : "") + ":"
                + (entry.specialEvent != null ? entry.specialEvent.id : "")));
        }
    }
}
