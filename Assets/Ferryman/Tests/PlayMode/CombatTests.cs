using System;
using System.Collections;
using System.Collections.Generic;
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
        public IEnumerator KicksShowImpactOnSolidEntitiesAndEnemiesKeepOnlyRedFeedback()
        {
            var stone = FindStones()[0];
            var bomb = Object.FindObjectsOfType<TimedBomb>(true).First();
            var crewObject = new GameObject("Kick Feedback Crew");
            var crew = crewObject.AddComponent<CrewMember>();
            crew.Hitbox.isTrigger = false;
            crew.gameObject.AddComponent<BoxCollider2D>().size = new Vector2(0.4f, 0.4f);
            var solid = new GameObject("Kick Feedback Solid");
            solid.AddComponent<BoxCollider2D>().size = new Vector2(0.5f, 0.5f);
            var trigger = new GameObject("Kick Feedback Trigger");
            trigger.AddComponent<BoxCollider2D>().isTrigger = true;
            crewObject.SetActive(false);
            solid.SetActive(false);
            trigger.SetActive(false);
            var impactField = typeof(PlayerCharacter).GetField("meleeImpactEffectPrefab", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(impactField, Is.Not.Null);
            Assert.That(impactField.GetValue(player), Is.Not.Null, "The player has no kick impact prefab.");

            foreach (var entity in new[] { stone.gameObject, bomb.gameObject, crewObject, solid, trigger })
            {
                entity.transform.position = Vector2.right;
                entity.SetActive(true);
                Physics2D.SyncTransforms();
                Assert.That(player.TryMelee(Vector2.right), Is.True);
                int expected = entity == trigger ? 0 : 1;
                Assert.That(ImpactCount(), Is.EqualTo(expected), "Wrong impact count for " + entity.name);
                var swings = Object.FindObjectsOfType<SpriteEffect>().Where(effect => effect.name == "MeleeSwing(Clone)").ToArray();
                Assert.That(swings, Has.Length.EqualTo(1), "The existing kick effect must play once per attack.");
                Assert.That(swings[0].transform.position.x, Is.GreaterThan(player.transform.position.x + 0.5f),
                    "The kick effect belongs in front of the player.");
                Assert.That(player.transform.Find("Melee Outline").GetComponent<LineRenderer>().enabled, Is.False,
                    "A kick restored the old outline effect on the player.");
                entity.SetActive(false);
                yield return new WaitForSeconds(0.55f);
                Assert.That(ImpactCount(), Is.Zero, "Kick impact did not clean up.");
            }

            enemy.transform.position = Vector2.right;
            Physics2D.SyncTransforms();
            float health = enemy.Health;
            Assert.That(player.TryMelee(Vector2.right), Is.True);
            Assert.That(enemy.Health, Is.LessThan(health));
            Assert.That(ImpactCount(), Is.Zero, "Melee added yellow feedback to an enemy.");
            var feedback = typeof(EnemyCharacter).GetMethod("TryGetHitFeedbackPosition", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(feedback.Invoke(enemy, new object[] { Vector3.zero }), Is.True);
            var shot = FireFrom(player, enemy.transform.position, Vector2.up, 1f);
            yield return WaitFor(() => shot == null, 0.3f, "The projectile did not hit the enemy.");
            Assert.That(ImpactCount(), Is.Zero, "Projectile added yellow feedback to an enemy.");
            Assert.That(player.TryFire(Vector2.up), Is.True);
            Assert.That(Object.FindObjectsOfType<SpriteEffect>().Any(effect => effect.name == "MuzzleFlash(Clone)"), Is.False,
                "Player shooting added an extra effect at the muzzle.");
            Object.Destroy(crewObject);
            Object.Destroy(solid);
            Object.Destroy(trigger);
        }

        private static int ImpactCount()
        {
            return Object.FindObjectsOfType<SpriteEffect>().Count(effect => effect.name == "ProjectileImpact(Clone)");
        }

        [UnityTest]
        public IEnumerator BombPushesPlayerAndCrewWithoutStunOrCharacterDamage()
        {
            var profile = Resources.Load<CrewDefinition>("Voyage/Crew/gunner");
            var crew = (CombatCrewMember)Object.Instantiate(profile.prefab, new Vector2(1.5f, 0f), Quaternion.identity);
            crew.InitializeCrew(player.BoatHealth, damageSystem, player.ProjectilePrefab, profile);
            var bomb = Object.FindObjectsOfType<TimedBomb>(true).First();
            bomb.transform.position = new Vector2(0.75f, 0f);
            bomb.BindBoat(player.BoatHealth);
            bomb.gameObject.SetActive(true);
            player.enabled = true;
            Physics2D.SyncTransforms();
            int received = 0;
            damageSystem.DamageReceived.AddListener((source, target, amount) => received++);

            player.BoatHealth.ApplyDamage(100f, new Vector2(-0.5f, 0f));
            Assert.That(player.TryRepair(0f), Is.True);

            Assert.That(bomb.Explode(), Is.True);
            Assert.That(bomb.Explode(), Is.False);
            Assert.That(player.IsRepairing, Is.False, "Bomb knockback did not interrupt repair.");
            yield return new WaitForSeconds(0.12f);
            Assert.That(player.transform.position.x, Is.LessThan(-0.2f));
            Assert.That(crew.transform.position.x, Is.GreaterThan(1.7f));
            Assert.That(player.IsStunned || crew.IsStunned, Is.False);
            Assert.That(player.Health, Is.EqualTo(100f));
            Assert.That(crew.Health, Is.EqualTo(100f));
            Assert.That(received, Is.Zero);
            Assert.That(player.BoatHealth.CurrentHealth, Is.EqualTo(800f));
            Assert.That(player.TryFire(Vector2.up), Is.True, "Bomb knockback blocked attacks.");
        }

        [UnityTest]
        public IEnumerator FlyingKickPushesCrewOnceWithoutStunAndStopsAtDeckEdge()
        {
            var profile = Resources.Load<CrewDefinition>("Voyage/Crew/gunner");
            player.GetComponent<Rigidbody2D>().position = new Vector2(0f, 1f);
            player.transform.position = player.GetComponent<Rigidbody2D>().position;
            var crew = (CombatCrewMember)Object.Instantiate(profile.prefab, new Vector2(0f, 2f), Quaternion.identity);
            crew.InitializeCrew(player.BoatHealth, damageSystem, player.ProjectilePrefab, profile);
            crew.gameObject.AddComponent<CircleCollider2D>().isTrigger = true;
            Physics2D.SyncTransforms();
            Assert.That(player.TryMelee(Vector2.up), Is.True);
            yield return new WaitForSeconds(0.35f);
            Assert.That(crew.transform.position.y, Is.GreaterThan(2.1f));
            Assert.That(crew.IsStunned, Is.False);
            Assert.That(crew.Health, Is.EqualTo(100f));
            Assert.That(player.MovementDeck.HasClearFloor(crew.Hitbox.bounds.center,
                (Vector2)crew.Hitbox.bounds.size - Vector2.one * 0.04f), Is.True,
                "Knockback pushed crew through the deck boundary.");
            crew.enabled = false;
            yield return new WaitForSeconds(profile.fireInterval);
            Assert.That(crew.TryFire(Vector2.up), Is.True);
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
        public IEnumerator ProjectileHitsTheMantisClawButPassesOutsideItsContour()
        {
            Vector2 position = new Vector2(0f, 7f);
            enemy.transform.position = position;
            Physics2D.SyncTransforms();

            Assert.That(enemy.Hitbox, Is.TypeOf<PolygonCollider2D>());
            Vector2 emptyCorner = position + new Vector2(0.55f, 0.9f);
            Assert.That(enemy.Hitbox.bounds.Contains(emptyCorner), Is.True);
            var miss = FireFrom(player, emptyCorner, Vector2.right, player.AttackPower);
            yield return WaitFor(() => miss != null && miss.transform.position.x > enemy.Hitbox.bounds.max.x + 0.15f,
                0.5f, "The projectile should fly through the empty corner of the monster's bounds.");
            Assert.That(enemy.Health, Is.EqualTo(enemy.MaxHealth));
            Object.Destroy(miss.gameObject);

            Vector2 clawLane = position + new Vector2(0.68f, -1.6f);
            Assert.That(enemy.Hitbox.OverlapPoint(position + new Vector2(0.68f, -0.65f)), Is.True);
            var graze = FireFrom(player, clawLane, Vector2.up, player.AttackPower);
            yield return WaitFor(() => graze == null, 0.5f, "The projectile should hit the claw outside the old box.");
            Assert.That(enemy.Health, Is.EqualTo(enemy.MaxHealth - player.AttackPower));
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

        [UnityTest]
        public IEnumerator HitFeedbackStaysOnVisiblePixelsThroughBomberPoseChanges()
        {
            var encounter = Resources.Load<EncounterDefinition>("Voyage/Events/Bombardment");
            var bomber = Object.Instantiate(encounter.waves[0].enemies[0].prefab, new Vector3(0f, 8f), Quaternion.identity);
            var renderer = bomber.GetComponentInChildren<SpriteRenderer>();
            var collider = (PolygonCollider2D)bomber.Hitbox;
            var getAnchor = typeof(EnemyCharacter).GetProperty("HealthBarAnchor", BindingFlags.Instance | BindingFlags.NonPublic);
            var idleAnchor = (Vector3)getAnchor.GetValue(bomber);
            Vector2[] idleOutline = collider.GetPath(0);
            Vector2 claw = bomber.transform.TransformPoint(new Vector2(1.28f, -0.3f));
            Vector2 bomb = bomber.transform.TransformPoint(new Vector2(0.945f, -0.045f));
            Physics2D.SyncTransforms();
            Assert.That(collider.OverlapPoint(claw), Is.False);
            Sprite idlePose = renderer.sprite;
            bomber.InitializeEnemy(player.BoatHealth, player, damageSystem);
            yield return WaitFor(() => renderer.sprite != idlePose, 1f, "Bomber did not display its held pose.");
            Sprite heldPose = renderer.sprite;
            bomber.enabled = false;
            Physics2D.SyncTransforms();
            Assert.That(collider.OverlapPoint(claw), Is.True, "The raised claw must be hittable.");
            Assert.That(collider.OverlapPoint(bomb), Is.False, "The bomb must stay outside the body outline.");
            Assert.That(Vector3.Distance((Vector3)getAnchor.GetValue(bomber), idleAnchor), Is.LessThan(0.001f));

            var getPosition = typeof(EnemyCharacter).GetMethod("TryGetHitFeedbackPosition", BindingFlags.Instance | BindingFlags.NonPublic);
            var args = new object[] { Vector3.zero };
            var distinctPoints = new HashSet<Vector3>();
            for (int i = 0; i < 8; i++)
            {
                Assert.That(bomber.TryReceiveDamage(1f), Is.True);
                Assert.That(getPosition.Invoke(bomber, args), Is.True);
                var heldPoint = (Vector3)args[0];
                Assert.That(getPosition.Invoke(bomber, args), Is.True);
                Assert.That(args[0], Is.EqualTo(heldPoint), "The point moved without a new hit or pose.");
                AssertOpaqueAt(renderer, heldPoint);
                distinctPoints.Add(heldPoint);
                renderer.sprite = idlePose;
                Assert.That(getPosition.Invoke(bomber, args), Is.True);
                AssertOpaqueAt(renderer, (Vector3)args[0]);
                renderer.sprite = heldPose;
            }
            Assert.That(distinctPoints.Count, Is.GreaterThan(1));
            bomber.enabled = true;
            yield return WaitFor(() => renderer.sprite == idlePose, 1f, "Bomber did not restore the idle pose after throwing.");
            Assert.That(collider.GetPath(0), Is.EqualTo(idleOutline));
        }

        private static void AssertOpaqueAt(SpriteRenderer renderer, Vector3 position)
        {
            Sprite sprite = renderer.sprite;
            Vector3 local = renderer.transform.InverseTransformPoint(position);
            if (renderer.flipX)
                local.x = -local.x;
            if (renderer.flipY)
                local.y = -local.y;
            Vector2 pixel = (Vector2)local * sprite.pixelsPerUnit + sprite.pivot + sprite.rect.position;
            Assert.That(sprite.rect.Contains(pixel), Is.True);
            Assert.That(sprite.texture.GetPixel(Mathf.FloorToInt(pixel.x), Mathf.FloorToInt(pixel.y)).a, Is.GreaterThanOrEqualTo(0.5f));
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
