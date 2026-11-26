using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Ferryman.Characters;
using Ferryman.Combat;
using Ferryman.Voyage;
using Ferryman.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ferryman.Tests
{
    public sealed class SpecialCompanionCombatTests
    {
        private PlayerCharacter captain;
        private BoatHealth boat;
        private DamageSystem damageSystem;
        private CrewDefinition profile;
        private SimulationMode2D originalSimulationMode;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            originalSimulationMode = Physics2D.simulationMode;
            Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
            yield return SceneManager.LoadSceneAsync("Prototype");
            captain = Object.FindObjectOfType<PlayerCharacter>();
            boat = captain.BoatHealth;
            damageSystem = captain.DamageSystem;
            foreach (EnemyCharacter enemy in Object.FindObjectsOfType<EnemyCharacter>())
                enemy.gameObject.SetActive(false);
            foreach (DestructibleObstacle obstacle in Object.FindObjectsOfType<DestructibleObstacle>())
                obstacle.gameObject.SetActive(false);
            foreach (Projectile projectile in Object.FindObjectsOfType<Projectile>())
                Object.Destroy(projectile.gameObject);
            captain.enabled = false;
            captain.GetComponent<Rigidbody2D>().simulated = false;
            profile = ScriptableObject.CreateInstance<CrewDefinition>();
            profile.attackPower = 6f;
            profile.fireInterval = 1f;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Physics2D.simulationMode = originalSimulationMode;
            Object.DestroyImmediate(profile);
        }

        [UnityTest]
        public IEnumerator MarkIsConsumedOnceByAcceptedCaptainProjectile()
        {
            SpecialCompanion spotter = CreateCompanion(SpecialCompanionSkill.Mark, new Vector2(-50f, 0f));
            EnemyCharacter target = CreateTarget(new Vector2(10f, 5f));
            var friendly = new GameObject("Ordinary Ally").AddComponent<CrewMember>();
            friendly.transform.position = new Vector2(-20f, 5f);
            Physics2D.SyncTransforms();

            yield return Hit(target, spotter, 2f);
            Assert.That(target.Health, Is.EqualTo(98f));
            yield return Hit(target, friendly, 2f);
            Assert.That(target.Health, Is.EqualTo(96f));
            yield return Hit(target, captain, 0f);
            Assert.That(target.Health, Is.EqualTo(96f));
            yield return Hit(target, captain, 2f);
            Assert.That(target.Health, Is.EqualTo(70f));
            yield return Hit(target, captain, 2f);
            Assert.That(target.Health, Is.EqualTo(68f));
        }

        [UnityTest]
        public IEnumerator KickLeavesFlyingAndObstructedBombsAloneThenKicksClearThreat()
        {
            SpecialCompanion kicker = CreateCompanion(SpecialCompanionSkill.Kick, Vector2.down * 1.2f);
            TimedBomb bomb = Object.FindObjectsOfType<TimedBomb>(true).First();
            bomb.transform.position = Vector2.up * 4f;
            bomb.gameObject.SetActive(true);
            bomb.Throw(boat, Vector2.right * 2f);
            Assert.That(bomb.IsInFlight, Is.True);
            yield return new WaitForSeconds(0.28f);
            Assert.That(bomb != null && bomb.IsInFlight, Is.True);
            yield return WaitFor(() => bomb == null || !bomb.IsInFlight, 1.2f, "Bomb did not land.");
            Assert.That(bomb, Is.Not.Null);

            Bounds bounds = bomb.transform.Find("Visual").GetComponent<SpriteRenderer>().bounds;
            var edge = new Vector2(bounds.min.x, bounds.center.y);
            Vector2 kickOrigin = edge - Vector2.right;
            kicker.GetComponent<Rigidbody2D>().position = kickOrigin;
            var obstacle = new GameObject("Kick Occluder");
            obstacle.transform.position = kickOrigin + Vector2.right * 0.5f;
            obstacle.AddComponent<BoxCollider2D>().size = new Vector2(0.1f, 1f);
            Physics2D.SyncTransforms();
            Assert.That(Physics2D.LinecastAll(kickOrigin, edge).Any(hit => hit.collider.gameObject == obstacle), Is.True);

            yield return new WaitForSeconds(1.57f);
            Assert.That(bomb != null && !bomb.GetComponent<DestructibleObstacle>().IsCleared, Is.True);
            float health = boat.CurrentHealth;
            obstacle.SetActive(false);
            Physics2D.SyncTransforms();
            string nearbyColliders = string.Join(", ", Physics2D.LinecastAll(kickOrigin, edge)
                .Select(hit => hit.collider.name));
            yield return WaitFor(() => bomb == null, 0.85f,
                $"The landed bomb was not kicked away in time. Kick origin {kickOrigin}, bomb edge {edge}, colliders {nearbyColliders}.");
            Assert.That(boat.CurrentHealth, Is.EqualTo(health));
        }

        [UnityTest]
        public IEnumerator CoverBurstEndsWhileCaptainStaysStunnedAndHasAnInternalCooldown()
        {
            SpecialCompanion cover = CreateCompanion(SpecialCompanionSkill.Repair, new Vector2(-50f, 0f));
            CreateTarget(new Vector2(10f, 5f));
            yield return null;
            Assert.That(cover.FireInterval, Is.EqualTo(profile.fireInterval));
            Assert.That(captain.TryReceiveDamage(1f, 3.2f), Is.True);
            yield return WaitFor(() => cover.FireInterval < profile.fireInterval, 0.5f, "Cover burst did not begin.");
            yield return WaitFor(() => cover.FireInterval == profile.fireInterval, 2.7f, "Cover burst never ended.");
            Assert.That(captain.IsStunned, Is.True);
            yield return WaitFor(() => !captain.IsStunned, 1f, "Captain did not recover.");
            yield return null;
            Assert.That(captain.TryReceiveDamage(1f, 1f), Is.True);
            yield return new WaitForSeconds(0.2f);
            Assert.That(cover.FireInterval, Is.EqualTo(profile.fireInterval));
        }

        [UnityTest]
        public IEnumerator HeavyShotPrioritizesActualThrowWindupOverNearerEnemy()
        {
            EnemyCharacter nearer = CreateTarget(Vector2.right * 1.5f);
            EncounterDefinition encounter = Resources.Load<EncounterDefinition>("Voyage/Events/GarbageTideColossus");
            Assert.That(encounter, Is.Not.Null);
            EnemyCharacter boss = Object.Instantiate(encounter.waves[0].enemies[0].prefab,
                Vector2.up * 9f, Quaternion.identity);
            boss.InitializeEnemy(boat, captain, damageSystem);
            var windup = typeof(EnemyCharacter).GetProperty("IsPreparingThrow", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(windup, Is.Not.Null);
            yield return WaitFor(() => (bool)windup.GetValue(boss), 2f, "The Boss did not begin its actual throw animation.");

            SpecialCompanion heavy = CreateCompanion(SpecialCompanionSkill.Throw, Vector2.zero);
            Character hit = null;
            damageSystem.DamageSent.AddListener((source, target, _) =>
            {
                if (source == heavy)
                    hit ??= target;
            });
            yield return WaitFor(() => hit != null, 2f, "The heavy companion did not land a shot.");
            Assert.That(hit, Is.SameAs(boss));
            Assert.That(nearer.Health, Is.EqualTo(nearer.MaxHealth));
        }

        private SpecialCompanion CreateCompanion(SpecialCompanionSkill skill, Vector2 position)
        {
            var actor = new GameObject(skill.ToString());
            actor.transform.position = position;
            var companion = actor.AddComponent<SpecialCompanion>();
            actor.GetComponent<BoxCollider2D>().size = new Vector2(0.585f, 0.91f);
            actor.GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeAll;
            companion.ConfigureCombat(damageSystem, captain.ProjectilePrefab, profile.attackPower, profile.fireInterval);
            companion.InitializeCompanion(boat, damageSystem, captain, profile, skill);
            return companion;
        }

        private static EnemyCharacter CreateTarget(Vector2 position)
        {
            var target = new GameObject("Target");
            target.transform.position = position;
            target.AddComponent<BoxCollider2D>().size = Vector2.one * 0.5f;
            return target.AddComponent<EnemyCharacter>();
        }

        private IEnumerator Hit(EnemyCharacter target, Character shooter, float setDamage)
        {
            var projectile = Object.Instantiate(captain.ProjectilePrefab,
                target.transform.position - Vector3.right * 0.55f, Quaternion.identity);
            projectile.Launch(Vector2.right, setDamage, shooter, damageSystem);
            yield return WaitFor(() => projectile == null, 0.45f, "The projectile did not reach the target.");
        }

        private static IEnumerator WaitFor(Func<bool> ready, float seconds, string failure)
        {
            float deadline = Time.time + seconds;
            while (!ready() && Time.time < deadline)
                yield return null;
            Assert.That(ready(), Is.True, failure);
        }
    }
}
