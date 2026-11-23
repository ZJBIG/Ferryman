using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Ferryman.Characters;
using Ferryman.Combat;
using Ferryman.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ferryman.Tests
{
    public sealed class BoatRepairTests
    {
        private BoatHealth boat;
        private PlayerCharacter player;
        private EnemyCharacter enemy;
        private Rigidbody2D body;
        private SimulationMode2D originalSimulationMode;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            originalSimulationMode = Physics2D.simulationMode;
            Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
            yield return SceneManager.LoadSceneAsync("Prototype");
            boat = Object.FindObjectOfType<BoatHealth>();
            player = Object.FindObjectOfType<PlayerCharacter>();
            enemy = Object.FindObjectOfType<EnemyCharacter>();
            Assert.That(boat, Is.Not.Null);
            Assert.That(player, Is.Not.Null);
            Assert.That(enemy, Is.Not.Null);
            foreach (var actor in Object.FindObjectsOfType<EnemyCharacter>())
                actor.enabled = false;
            foreach (var obstacle in Object.FindObjectsOfType<DestructibleObstacle>())
                obstacle.gameObject.SetActive(false);
            foreach (var projectile in Object.FindObjectsOfType<Projectile>())
                Object.Destroy(projectile.gameObject);
            player.enabled = false;
            body = player.GetComponent<Rigidbody2D>();
            body.interpolation = RigidbodyInterpolation2D.None;
            body.position = Vector2.zero;
            body.velocity = Vector2.zero;
            Physics2D.SyncTransforms();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Physics2D.simulationMode = originalSimulationMode;
        }

        [UnityTest]
        public IEnumerator DamageAccumulatesSearchesNewRingsAndCompletesEachRepairOnce()
        {
            Vector2 hit = new Vector2(-0.25f, 0.1f);
            Vector2Int origin = boat.Deck.WorldToCell(hit);
            Assert.That(boat.ApplyDamage(60f, hit), Is.True);
            Assert.That(boat.CurrentHealth, Is.EqualTo(940f));
            Assert.That(boat.RepairPoints.Count, Is.Zero);
            Assert.That(boat.PendingDamage, Is.EqualTo(60f));
            boat.ApplyDamage(190f, hit);
            Assert.That(boat.CurrentHealth, Is.EqualTo(750f));
            Assert.That(boat.PendingDamage, Is.EqualTo(50f));
            Assert.That(boat.RepairPoints.Count, Is.EqualTo(2));
            var first = boat.RepairPoints.Single(point => point.Cell == origin);
            var neighbor = boat.RepairPoints.Single(point => point != first);
            Assert.That((Vector2)first.transform.position, Is.EqualTo(hit));
            Assert.That(BoatDeck.CellDistance(origin, neighbor.Cell), Is.EqualTo(1));
            Assert.That(first.BeginRepair(player), Is.True);
            Assert.That(first.BeginRepair(enemy), Is.True);
            first.AddProgress(player, 95f);
            first.AddProgress(enemy, 4f);
            Assert.That(first.Progress, Is.EqualTo(99f));
            Assert.That(boat.CurrentHealth, Is.EqualTo(750f), "Partial repair must not restore HP.");
            int healthChanges = 0;
            boat.HealthChanged.AddListener((current, maximum) => healthChanges++);
            Assert.That(first.AddProgress(player, 2f), Is.True);
            Assert.That(first.AddProgress(enemy, 50f), Is.False, "A second worker completed the same point twice.");
            Assert.That(first.IsComplete, Is.True);
            Assert.That(boat.CurrentHealth, Is.EqualTo(850f));
            Assert.That(boat.RepairPoints.Count, Is.EqualTo(1));
            Assert.That(healthChanges, Is.EqualTo(1));

            boat.ApplyDamage(100f, hit);
            var replacement = boat.RepairPoints.Single(point => point.Cell == origin);
            Assert.That(replacement, Is.Not.SameAs(first), "Completion did not release the occupied cell immediately.");
            Assert.That(player.TryRepair(0f), Is.True);
            Assert.That(player.IsRepairing, Is.True);
            int destroyed = 0;
            boat.Destroyed.AddListener(() => destroyed++);
            boat.ApplyDamage(5000f, hit);
            Assert.That(boat.CurrentHealth, Is.Zero);
            Assert.That(boat.IsDestroyed, Is.True);
            Assert.That(boat.PendingDamage, Is.Zero, "Only the remaining 750 HP should count as lethal damage.");
            Assert.That(boat.RepairPoints.Count, Is.EqualTo(10));
            Assert.That(boat.RepairPoints.Select(point => point.Cell).Distinct().Count(), Is.EqualTo(10));
            Assert.That(boat.RepairPoints.All(point => boat.Deck.ContainsCell(point.Cell)), Is.True);
            Assert.That(boat.RepairPoints.Count(point => BoatDeck.CellDistance(origin, point.Cell) == 1), Is.EqualTo(8));
            Assert.That(boat.RepairPoints.Max(point => BoatDeck.CellDistance(origin, point.Cell)), Is.EqualTo(2));
            Assert.That(boat.RepairPoints.All(point => boat.Deck.HasClearFloor(point.transform.position, new Vector2(1.96f, 1.54f))), Is.True);
            Assert.That(player.IsRepairing, Is.False);
            Assert.That(boat.RepairPoints.All(point => !point.IsBeingRepaired), Is.True);
            Assert.That(replacement.BeginRepair(player), Is.False);
            Assert.That(replacement.AddProgress(player, 100f), Is.False);
            Assert.That(player.TryRepair(1f), Is.False);
            Assert.That(player.TryFire(Vector2.right), Is.False);
            Assert.That(player.TryMelee(Vector2.right), Is.False);
            player.Move(Vector2.right);
            Assert.That(body.velocity, Is.EqualTo(Vector2.zero));
            Assert.That(boat.ApplyDamage(100f, hit), Is.False);
            Assert.That(destroyed, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerRepairKeepsTasksAndStopsForMovementAttackStunAndRelease()
        {
            Vector2Int origin = boat.Deck.WorldToCell(body.position);
            RepairPoint low = DamageCell(origin + Vector2Int.right);
            RepairPoint high = DamageCell(origin + Vector2Int.one);
            RepairPoint outside = DamageCell(origin + Vector2Int.right * 2);
            high.BeginRepair(enemy);
            high.AddProgress(enemy, 30f);
            high.EndRepair(enemy);
            outside.BeginRepair(enemy);
            outside.AddProgress(enemy, 80f);
            outside.EndRepair(enemy);
            Assert.That(player.TryRepair(0f), Is.True);
            Assert.That(high.IsBeingRepaired, Is.True, "Equal grid distances should prefer higher progress, including diagonals.");
            Assert.That(low.IsBeingRepaired, Is.False);
            Assert.That(outside.IsBeingRepaired, Is.False);
            high.gameObject.SetActive(false);
            Assert.That(high.IsBeingRepaired, Is.False);
            high.gameObject.SetActive(true);
            Assert.That(player.TryRepair(0f), Is.True);
            Assert.That(high.IsBeingRepaired, Is.True, "Re-enabled tasks must restore the player's contribution registration.");
            Assert.That(ProgressBar(high).activeSelf, Is.True);
            RepairPoint closer = DamageCell(origin);
            yield return RepairFor(0.12f);
            Assert.That(high.IsBeingRepaired, Is.True, "A new nearer point replaced the existing task.");
            Assert.That(high.Progress, Is.GreaterThan(30f));
            Assert.That(closer.Progress, Is.Zero);
            Assert.That(boat.CurrentHealth, Is.EqualTo(600f));
            player.StopRepairing();
            Assert.That(ProgressBar(high).activeSelf, Is.False);
            Assert.That(player.TryRepair(0f), Is.True);
            Assert.That(closer.IsBeingRepaired, Is.True, "Nearest distance should take priority over progress.");
            Assert.That(high.IsBeingRepaired, Is.False);

            SetField(player, "simultaneousRepairs", 2);
            float previousHigh = high.Progress;
            yield return RepairFor(0.12f);
            Assert.That(high.Progress - previousHigh, Is.EqualTo(closer.Progress).Within(0.001f),
                "Each parallel task should receive the full repair speed.");
            Assert.That(closer.Progress, Is.GreaterThan(5f));
            Assert.That(ProgressBar(closer).activeSelf, Is.True);
            Assert.That(ProgressBar(high).activeSelf, Is.True);
            player.Move(Vector2.right);
            Assert.That(player.IsRepairing, Is.False);
            Assert.That(body.velocity.x, Is.EqualTo(player.MoveSpeed));
            player.Move(Vector2.zero);
            float saved = closer.Progress;
            yield return new WaitForSeconds(0.1f);
            Assert.That(closer.Progress, Is.EqualTo(saved));

            player.TryRepair(0f);
            Assert.That(player.TryFire(Vector2.right), Is.True);
            Assert.That(player.IsRepairing, Is.False);
            player.TryRepair(0f);
            Assert.That(player.TryFire(Vector2.right), Is.False);
            Assert.That(player.IsRepairing, Is.False, "A fire request in cooldown did not stop repair.");
            player.TryRepair(0f);
            Assert.That(player.TryMelee(Vector2.right), Is.True);
            Assert.That(player.IsRepairing, Is.False);
            player.TryRepair(0f);
            Assert.That(player.TryMelee(Vector2.right), Is.False);
            Assert.That(player.IsRepairing, Is.False, "A melee request in cooldown did not stop repair.");

            player.TryRepair(0f);
            Assert.That(player.DamageSystem.ApplyDamage(enemy, player, 10f), Is.True);
            Assert.That(player.IsStunned, Is.True);
            Assert.That(player.IsRepairing, Is.False);
            Assert.That(ProgressBar(closer).activeSelf, Is.False);
            Assert.That(ProgressBar(high).activeSelf, Is.False);
            Assert.That(player.TryRepair(0.1f), Is.False);
            Assert.That(closer.Progress, Is.EqualTo(saved));
            yield return new WaitForSeconds(1.05f);
            Assert.That(player.IsStunned, Is.False);
            Assert.That(player.TryRepair(0f), Is.True);
            Assert.That(closer.Progress, Is.EqualTo(saved), "Repair progress was lost during stun.");
            player.StopRepairing();
            yield return new WaitForSeconds(0.1f);
            Assert.That(closer.Progress, Is.EqualTo(saved));
            Assert.That(ProgressBar(closer).activeSelf, Is.False);

            player.enabled = true;
            player.TryRepair(0f);
            player.enabled = false;
            Assert.That(player.IsRepairing, Is.False);
            Assert.That(ProgressBar(closer).activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator MeleeClearsBombBeforeFuseAndExplosionDamagesOnlyBoatOnce()
        {
            var bombs = Object.FindObjectsOfType<TimedBomb>(true);
            Assert.That(bombs.Length, Is.EqualTo(2));
            TimedBomb cleared = bombs[0];
            TimedBomb exploding = bombs[1];
            TimedBomb thrown = Object.Instantiate(exploding, Vector2.up * 6f, Quaternion.identity);
            cleared.transform.position = Vector2.right;
            exploding.transform.position = Vector2.left;
            var clearEffect = new GameObject("Test Bomb Clear Effect");
            var explosionEffect = new GameObject("Test Bomb Explosion Effect");
            SetField(cleared.GetComponent<DestructibleObstacle>(), "clearedEffectPrefab", clearEffect);
            SetField(cleared, "explosionEffectPrefab", explosionEffect);
            SetField(exploding, "explosionEffectPrefab", explosionEffect);
            cleared.gameObject.SetActive(true);
            exploding.gameObject.SetActive(true);
            Transform bombVisual = cleared.transform.Find("Visual");
            Transform fuseBar = cleared.transform.Find("Fuse Bar");
            Physics2D.SyncTransforms();
            Assert.That(player.TryMelee(Vector2.right), Is.True);
            Assert.That(cleared.GetComponent<DestructibleObstacle>().IsCleared, Is.True);
            Assert.That(cleared.GetComponent<Collider2D>().enabled, Is.False);
            Assert.That(bombVisual.parent, Is.Null);
            cleared.GetComponent<DestructibleObstacle>().Clear();
            Assert.That(cleared.Explode(), Is.False, "A cleared bomb still exploded before delayed destruction.");
            Assert.That(EffectCount(clearEffect), Is.EqualTo(1));
            Assert.That(EffectCount(explosionEffect), Is.Zero);
            yield return null;
            Assert.That(cleared == null && fuseBar == null, Is.True);
            yield return WaitFor(() => bombVisual != null && bombVisual.position.x > 1f, 0.55f,
                "The bomb sprite did not fly away.");
            yield return new WaitForSeconds(2f);
            Assert.That(bombVisual == null, Is.True, "The kicked bomb visual did not clean itself up.");
            Assert.That(boat.CurrentHealth, Is.EqualTo(1000f), "Bomb damage occurred before the fuse elapsed.");
            yield return WaitFor(() => exploding == null, 2f, "The intact bomb did not explode after its fuse.");
            Assert.That(boat.CurrentHealth, Is.EqualTo(900f));
            Assert.That(boat.RepairPoints.Count, Is.EqualTo(1));
            Assert.That((Vector2)boat.RepairPoints.Single().transform.position, Is.EqualTo(Vector2.left));
            Assert.That(player.IsStunned, Is.False, "Boat damage added an unrequested character AOE hit.");
            Assert.That(player.Health, Is.EqualTo(100f));
            Assert.That(EffectCount(explosionEffect), Is.EqualTo(1));
            yield return new WaitForSeconds(0.1f);
            Assert.That(boat.CurrentHealth, Is.EqualTo(900f));

            Vector2 blockedCabin = new Vector2(-5f, 0f);
            thrown.gameObject.SetActive(true);
            BoxCollider2D thrownHitbox = thrown.GetComponent<BoxCollider2D>();
            Assert.That(boat.Deck.CanPlace(blockedCabin, thrownHitbox.size), Is.False);
            Assert.That(boat.Deck.TryFindPlacement(blockedCabin, thrownHitbox.size, thrownHitbox, out Vector2 planned), Is.True);
            thrown.Throw(boat, blockedCabin);
            Assert.That(thrownHitbox.enabled, Is.False);
            Transform thrownFuse = thrown.transform.Find("Fuse Bar");
            Assert.That(thrownFuse.gameObject.activeSelf, Is.False);
            Assert.That(thrown.Explode(), Is.False);
            body.position = planned;
            Physics2D.SyncTransforms();
            yield return WaitFor(() => thrown != null && !thrown.IsInFlight, 1f, "The thrown bomb did not find a safe landing.");
            Assert.That(thrownHitbox.enabled, Is.True);
            Assert.That(thrownFuse.gameObject.activeSelf, Is.True);
            Assert.That(thrownFuse.Find("Fill").localScale.x, Is.GreaterThan(0.9f));
            Assert.That((Vector2)thrown.transform.position, Is.Not.EqualTo(planned));
            Assert.That(boat.Deck.CanPlace(thrown.transform.position, thrownHitbox.size, thrownHitbox), Is.True);
            Object.Destroy(thrown.gameObject);

            foreach (var effect in Object.FindObjectsOfType<Transform>()
                .Where(item => item.name.StartsWith("Test Bomb", StringComparison.Ordinal)).ToArray())
                Object.Destroy(effect.gameObject);
        }

        [UnityTest]
        public IEnumerator BowLandingsAndRepairCheckpointsUseTheExpandedDeck()
        {
            BoatDeck deck = boat.Deck;
            var oldEdge = new Vector2Int(11, 2);
            var bowCell = new Vector2Int(12, 2);
            Vector2 legacyPosition = deck.transform.TransformPoint(new Vector2(6f, 0f));
            Assert.That(deck.WorldToCell(legacyPosition), Is.EqualTo(oldEdge));
            boat.ApplyDamage(100f, legacyPosition);
            Assert.That(boat.RepairPoints.Single().Cell, Is.EqualTo(oldEdge));

            Vector2 landing = deck.CellToWorld(bowCell);
            var template = Object.FindObjectsOfType<TimedBomb>(true).First();
            var bomb = Object.Instantiate(template, landing + Vector2.up * 5f, Quaternion.identity);
            bomb.gameObject.SetActive(true);
            bomb.Throw(boat, landing);
            yield return WaitFor(() => bomb != null && !bomb.IsInFlight, 1.5f, "The bomb did not land on the bow.");
            Assert.That((Vector2)bomb.transform.position, Is.EqualTo(landing));
            Assert.That(bomb.Explode(), Is.True);
            RepairPoint point = boat.RepairPoints.Single(item => item.Cell == bowCell);
            Assert.That((Vector2)point.transform.position, Is.EqualTo(landing));

            Vector2 stoneLanding = deck.CellToWorld(new Vector2Int(13, 2));
            var stoneTemplate = Object.FindObjectsOfType<DestructibleObstacle>(true)
                .First(item => item.GetComponent<TimedBomb>() == null);
            var stone = Object.Instantiate(stoneTemplate, stoneLanding + Vector2.up * 5f, Quaternion.identity);
            stone.gameObject.SetActive(true);
            var flyTo = typeof(DestructibleObstacle).GetMethod("FlyTo", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(flyTo, Is.Not.Null);
            flyTo.Invoke(stone, new object[] { deck, stoneLanding, 0.4f, 0f, null });
            yield return WaitFor(() => stone != null && !stone.IsInFlight, 1.5f, "The stone did not land on the bow.");
            Assert.That((Vector2)stone.transform.position, Is.EqualTo(stoneLanding));
            var stoneHitbox = stone.GetComponent<BoxCollider2D>();
            Assert.That(stoneHitbox.enabled && deck.CanPlace(stoneLanding, stoneHitbox.size, stoneHitbox), Is.True);

            body.position = landing;
            Assert.That(player.TryRepair(0.5f), Is.True);
            Assert.That(point.Progress, Is.EqualTo(25f));
            player.StopRepairing();

            BoatHealth.State checkpoint = boat.CaptureState();
            boat.RestoreState(checkpoint);
            Assert.That(boat.CurrentHealth, Is.EqualTo(800f));
            Assert.That((Vector2)boat.RepairPoints.Single(item => item.Cell == oldEdge).transform.position, Is.EqualTo(legacyPosition));
            var restored = boat.RepairPoints.Single(item => item.Cell == bowCell);
            Assert.That((Vector2)restored.transform.position, Is.EqualTo(landing));
            Assert.That(restored.Progress, Is.EqualTo(25f));

            BoatHealth.PointState legacy = checkpoint.points.Single(item => item.cell == oldEdge);
            legacy.cell = new Vector2Int(11, 0);
            legacy.localPosition = new Vector2(6f, -2.5f);
            legacy.progress = 15f;
            Assert.That(deck.ContainsFloor(deck.transform.TransformPoint(legacy.localPosition)), Is.False);
            boat.RestoreState(checkpoint);
            var relocated = boat.RepairPoints.Single(item => item.Progress == 15f);
            Assert.That(deck.HasClearFloor(relocated.transform.position, new Vector2(1.96f, 1.54f)), Is.True);
        }

        private RepairPoint DamageCell(Vector2Int cell)
        {
            boat.ApplyDamage(100f, boat.Deck.CellToWorld(cell));
            return boat.RepairPoints.Single(point => point.Cell == cell);
        }

        private IEnumerator RepairFor(float duration)
        {
            float until = Time.time + duration;
            while (Time.time < until)
            {
                yield return null;
                Assert.That(player.TryRepair(Time.deltaTime), Is.True);
            }
        }

        private static GameObject ProgressBar(RepairPoint point)
        {
            return point.transform.Find("Progress Bar").gameObject;
        }

        private static int EffectCount(GameObject prefab)
        {
            return Object.FindObjectsOfType<Transform>().Count(item => item.name == prefab.name + "(Clone)");
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing fixture field: " + name);
            field.SetValue(target, value);
        }

        private static IEnumerator WaitFor(Func<bool> condition, float timeout, string message)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(condition(), Is.True, message);
        }
    }
}
