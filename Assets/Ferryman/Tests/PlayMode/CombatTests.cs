using System;
using System.Collections;
using System.Linq;
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
    public sealed class CombatTests
    {
        private PlayerCharacter player;
        private EnemyCharacter enemy;
        private DamageSystem damageSystem;
        private SimulationMode2D originalSimulationMode;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            originalSimulationMode = Physics2D.simulationMode;
            Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
            yield return SceneManager.LoadSceneAsync("Prototype");
            player = Object.FindObjectOfType<PlayerCharacter>();
            enemy = Object.FindObjectOfType<EnemyCharacter>();
            damageSystem = Object.FindObjectOfType<DamageSystem>();
            Assert.That(player, Is.Not.Null);
            Assert.That(enemy, Is.Not.Null);
            Assert.That(damageSystem, Is.Not.Null);
            foreach (var actor in Object.FindObjectsOfType<EnemyCharacter>())
                actor.enabled = false;
            foreach (var obstacle in Object.FindObjectsOfType<DestructibleObstacle>())
                obstacle.gameObject.SetActive(false);
            foreach (var projectile in Object.FindObjectsOfType<Projectile>())
                Object.Destroy(projectile.gameObject);
            player.enabled = false;
            player.GetComponent<Rigidbody2D>().interpolation = RigidbodyInterpolation2D.None;
            player.GetComponent<Rigidbody2D>().position = Vector2.zero;
            Physics2D.SyncTransforms();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Physics2D.simulationMode = originalSimulationMode;
        }

        [UnityTest]
        public IEnumerator ProjectileCrossesDeckFiltersAlliesAndAppliesEachHitOnce()
        {
            Bounds deckBounds = player.MovementDeck.GetComponent<EdgeCollider2D>().bounds;
            enemy.transform.position = new Vector3(0f, deckBounds.max.y + enemy.Hitbox.bounds.extents.y + 0.4f);
            enemy.gameObject.AddComponent<BoxCollider2D>().size = new Vector2(0.6f, 1.2f);
            var crewObject = new GameObject("Test Crew");
            crewObject.transform.position = new Vector3(0f, 0.9f);
            var crew = crewObject.AddComponent<CrewMember>();
            crewObject.GetComponent<BoxCollider2D>().size = new Vector2(0.4f, 0.4f);
            Physics2D.SyncTransforms();

            Assert.That(enemy.Hitbox.bounds.min.y, Is.GreaterThan(deckBounds.max.y));

            int received = 0;
            int sent = 0;
            damageSystem.DamageReceived.AddListener((source, target, amount) => received++);
            damageSystem.DamageSent.AddListener((source, target, amount) => sent++);
            // Start inside the shooter to exercise self filtering as well as the flight path.
            var shot = FireFrom(player, player.transform.position, Vector2.up, player.AttackPower);
            yield return WaitFor(() => shot == null, 2f, "Projectile did not hit the enemy beyond the deck.");

            Assert.That(player.IsStunned, Is.False);
            Assert.That(crew.IsStunned, Is.False);
            Assert.That(player.Health, Is.EqualTo(100f));
            Assert.That(crew.Health, Is.EqualTo(100f));
            Assert.That(enemy.Health, Is.EqualTo(90f));
            Assert.That(received, Is.EqualTo(1));
            Assert.That(sent, Is.EqualTo(1));

            FireFrom(player, player.transform.position, Vector2.up, enemy.Health);
            yield return WaitFor(() => enemy == null, 2f, "Lethal projectile did not remove the enemy.");
            Assert.That(received, Is.EqualTo(2));
            Assert.That(sent, Is.EqualTo(2));
            Object.Destroy(crewObject);
        }

        [UnityTest]
        public IEnumerator StunStopsActionsRejectsFurtherDamageAndThenRestoresCombat()
        {
            int received = 0;
            damageSystem.DamageReceived.AddListener((source, target, amount) => received++);
            var body = player.GetComponent<Rigidbody2D>();
            player.Move(Vector2.right);
            Assert.That(body.velocity.x, Is.EqualTo(player.MoveSpeed));
            FireFrom(enemy, player.transform.position, Vector2.down, enemy.AttackPower);
            yield return WaitFor(() => player.IsStunned, 2f, "Enemy projectile did not stun the player.");

            Assert.That(player.Health, Is.EqualTo(100f));
            Assert.That(player.StunRemainingRatio, Is.InRange(0.9f, 1f));
            Assert.That(body.velocity, Is.EqualTo(Vector2.zero));
            player.Move(Vector2.right);
            Assert.That(body.velocity, Is.EqualTo(Vector2.zero));
            Assert.That(player.TryFire(Vector2.right), Is.False);
            Assert.That(player.TryMelee(Vector2.right), Is.False);
            Assert.That(received, Is.EqualTo(1));

            yield return new WaitForSeconds(0.35f);
            var blockedShot = FireFrom(enemy, player.transform.position, Vector2.down, enemy.AttackPower);
            yield return WaitFor(() => blockedShot == null, 0.2f, "An invulnerable target should still consume the projectile.");
            Assert.That(player.IsStunned, Is.True);
            Assert.That(received, Is.EqualTo(1));
            Assert.That(player.StunRemainingRatio, Is.GreaterThan(0f).And.LessThan(0.7f));
            yield return new WaitForSeconds(0.7f);
            Assert.That(player.IsStunned, Is.False, "A second hit extended the stun.");
            Assert.That(player.StunRemainingRatio, Is.Zero);

            player.Move(Vector2.right);
            Assert.That(body.velocity.x, Is.EqualTo(player.MoveSpeed));
            player.Move(Vector2.zero);
            Assert.That(player.TryFire(Vector2.right), Is.True);
            var obstacle = FindStones()[0];
            obstacle.gameObject.SetActive(true);
            obstacle.transform.position = player.transform.position + Vector3.right;
            Physics2D.SyncTransforms();
            Assert.That(player.TryMelee(Vector2.right), Is.True);
            yield return null;
            Assert.That(obstacle == null, Is.True, "Melee did not clear the obstacle after recovery.");
        }

        [UnityTest]
        public IEnumerator MeleeIncludesIntersectingCollidersAndRejectsTargetsOutsideSector()
        {
            Vector2 origin = new Vector2(1.3f, -0.8f);
            player.transform.position = origin;
            var stones = FindStones();
            DestructibleObstacle edgeStone = stones[0];
            DestructibleObstacle behindStone = stones[1];
            edgeStone.gameObject.SetActive(true);
            edgeStone.transform.position = new Vector3(2f, 0f);
            behindStone.gameObject.SetActive(true);
            behindStone.transform.position = origin + Vector2.left;
            var distantStone = Object.Instantiate(edgeStone, origin + Vector2.right * 2.5f, Quaternion.identity);
            enemy.transform.position = origin + new Vector2(0.9f, 1.3f);
            enemy.gameObject.AddComponent<BoxCollider2D>().size = new Vector2(0.6f, 1.2f);
            Physics2D.SyncTransforms();
            Assert.That((Vector2)player.transform.position, Is.EqualTo(origin));
            Vector2 nearest = edgeStone.GetComponent<Collider2D>().ClosestPoint(origin) - origin;
            Assert.That(Vector2.Angle(Vector2.right, nearest), Is.GreaterThan(45f),
                "The fixture must exercise a hitbox whose nearest point lies outside the sector.");
            int received = 0;
            damageSystem.DamageReceived.AddListener((source, target, amount) => received++);

            Transform stoneVisual = edgeStone.transform.Find("Visual");
            Vector3 launchPosition = stoneVisual.position;
            Quaternion launchRotation = stoneVisual.rotation;
            Assert.That(player.TryMelee(Vector2.right), Is.True);
            Assert.That(edgeStone.IsCleared, Is.True);
            Assert.That(edgeStone.GetComponent<Collider2D>().enabled, Is.False);
            Assert.That(stoneVisual.parent, Is.Null);
            yield return null;
            Assert.That(edgeStone == null, Is.True,
                "The stone overlaps the sector even though its nearest point is outside the angle.");
            Assert.That(enemy.Health, Is.EqualTo(90f));
            Assert.That(received, Is.EqualTo(1), "Multiple colliders should cause only one melee hit.");
            Assert.That(behindStone != null, Is.True, "Melee hit a stone behind the player.");
            Assert.That(distantStone != null, Is.True, "Melee hit a stone beyond its radius.");
            Object.Destroy(distantStone.gameObject);
            yield return WaitFor(() => stoneVisual != null && stoneVisual.position.x > launchPosition.x
                && stoneVisual.position.y > launchPosition.y && Quaternion.Angle(stoneVisual.rotation, launchRotation) > 10f,
                0.55f, "The kicked stone did not move away and spin.");
            yield return WaitFor(() => stoneVisual == null, 1f, "The kicked stone visual did not clean itself up.");
        }

        private Projectile FireFrom(Character source, Vector3 origin, Vector2 direction, float damage)
        {
            var shot = Object.Instantiate(player.ProjectilePrefab, origin, Quaternion.identity);
            shot.Launch(direction, damage, source, damageSystem);
            return shot;
        }

        private static DestructibleObstacle[] FindStones()
        {
            return Object.FindObjectsOfType<DestructibleObstacle>(true)
                .Where(obstacle => obstacle.GetComponent<TimedBomb>() == null).ToArray();
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
