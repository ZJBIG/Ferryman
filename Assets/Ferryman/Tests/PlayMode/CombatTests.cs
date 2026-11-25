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
            var flashes = Object.FindObjectsOfType<SpriteEffect>().Where(effect => effect.name == "MuzzleFlash(Clone)").ToArray();
            Assert.That(flashes, Has.Length.EqualTo(1), "A shot must show one supplied muzzle effect.");
            Assert.That(player.TryFire(Vector2.up), Is.False);
            Assert.That(Object.FindObjectsOfType<SpriteEffect>().Count(effect => effect.name == "MuzzleFlash(Clone)"), Is.EqualTo(1));
            yield return new WaitForSeconds(0.15f);
            Assert.That(Object.FindObjectsOfType<SpriteEffect>().Any(effect => effect.name == "MuzzleFlash(Clone)"), Is.False);
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
            crew.InitializeCrew(player.BoatHealth, damageSystem, profile);
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
        public IEnumerator BombHasNoColliderAndDoesNotBlockPlayerMovement()
        {
            var bomb = Object.FindObjectsOfType<TimedBomb>(true).First();
            bomb.transform.position = new Vector2(0.65f, 0f);
            bomb.gameObject.SetActive(true);
            Assert.That(bomb.GetComponentsInChildren<Collider2D>(), Is.Empty);
            Physics2D.SyncTransforms();
            player.Move(Vector2.right);
            yield return new WaitForSeconds(0.3f);
            player.Move(Vector2.zero);
            Assert.That(player.GetComponent<Rigidbody2D>().position.x, Is.GreaterThan(1f), "The bomb blocked player movement.");
        }

        [UnityTest]
        public IEnumerator BomberLandingsReserveFlightAndLandedBombAreasUntilCleared()
        {
            var encounter = Resources.Load<EncounterDefinition>("Voyage/Events/Bombardment");
            var firstBomber = Object.Instantiate(encounter.waves[0].enemies[0].prefab, new Vector3(0f, 8f), Quaternion.identity);
            var secondBomber = Object.Instantiate(encounter.waves[0].enemies[0].prefab, new Vector3(4f, 8f), Quaternion.identity);
            firstBomber.InitializeEnemy(player.BoatHealth, player, damageSystem);
            secondBomber.InitializeEnemy(player.BoatHealth, player, damageSystem);
            firstBomber.enabled = false;
            secondBomber.enabled = false;
            var chooseLanding = typeof(EnemyCharacter).GetMethod("TryChooseBombLanding", BindingFlags.Instance | BindingFlags.NonPublic);
            var getLanding = typeof(DestructibleObstacle).GetProperty("WorldLandingPosition", BindingFlags.Instance | BindingFlags.NonPublic);
            var getFootprint = typeof(DestructibleObstacle).GetProperty("Footprint", BindingFlags.Instance | BindingFlags.NonPublic);
            TimedBomb firstBomb = null;
            TimedBomb secondBomb = null;
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            Rect LandingArea(TimedBomb bomb)
            {
                var obstacle = bomb.GetComponent<DestructibleObstacle>();
                Vector2 size = (Vector2)getFootprint.GetValue(obstacle);
                return new Rect((Vector2)getLanding.GetValue(obstacle) - size * 0.5f, size);
            }
            try
            {
                var firstChoice = new object[] { Vector2.zero };
                var secondChoice = new object[] { Vector2.zero };
                UnityEngine.Random.InitState(42);
                Assert.That(chooseLanding.Invoke(firstBomber, firstChoice), Is.True);
                UnityEngine.Random.InitState(42);
                Assert.That(chooseLanding.Invoke(secondBomber, secondChoice), Is.True);
                Assert.That(secondChoice[0], Is.EqualTo(firstChoice[0]), "The fixture must select the same preferred cell.");
                Vector2 preferred = (Vector2)firstChoice[0];

                firstBomb = Object.Instantiate(firstBomber.BombPrefab, firstBomber.transform.position, Quaternion.identity);
                firstBomb.Throw(player.BoatHealth, preferred);
                secondBomb = Object.Instantiate(secondBomber.BombPrefab, secondBomber.transform.position, Quaternion.identity);
                secondBomb.Throw(player.BoatHealth, preferred);
                Assert.That(firstBomb.IsInFlight && secondBomb.IsInFlight, Is.True);
                Rect firstReserved = LandingArea(firstBomb);
                Rect secondReserved = LandingArea(secondBomb);
                Assert.That(firstReserved.Overlaps(secondReserved), Is.False, "Two flying bombs reserved overlapping landing areas.");
                BoatDeck deck = player.MovementDeck;
                Assert.That(deck.CanPlace(firstReserved.center, firstReserved.size), Is.False,
                    "A flying bomb's destination remained available to other items.");

                yield return WaitFor(() => firstBomb != null && secondBomb != null && !firstBomb.IsInFlight && !secondBomb.IsInFlight,
                    2f, "The two bombs did not land.");
                Rect firstLanded = LandingArea(firstBomb);
                Assert.That(firstLanded.Overlaps(LandingArea(secondBomb)), Is.False, "The bombs overlapped after landing.");
                Assert.That(firstBomb.GetComponentsInChildren<Collider2D>(), Is.Empty);
                Assert.That(deck.CanPlace(firstLanded.center, firstLanded.size), Is.False,
                    "A landed bomb without a collider did not block item placement.");
                Assert.That(deck.TryFindPlacement(firstLanded.center, firstLanded.size, null, out Vector2 alternative), Is.True);
                Assert.That(alternative, Is.Not.EqualTo(firstLanded.center), "A new item reused an occupied bomb position.");

                firstBomb.GetComponent<DestructibleObstacle>().Clear();
                Assert.That(deck.CanPlace(firstLanded.center, firstLanded.size), Is.True,
                    "Clearing a bomb did not release its landing area.");
                Assert.That(deck.TryFindPlacement(firstLanded.center, firstLanded.size, null, out Vector2 reused), Is.True);
                Assert.That(reused, Is.EqualTo(firstLanded.center));
            }
            finally
            {
                UnityEngine.Random.state = randomState;
                Object.Destroy(firstBomber.gameObject);
                Object.Destroy(secondBomber.gameObject);
                if (firstBomb != null)
                    Object.Destroy(firstBomb.gameObject);
                if (secondBomb != null)
                    Object.Destroy(secondBomb.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator CrewStunLiesDownWithoutTintingOrRotatingHitboxes()
        {
            var applySprite = typeof(Character).GetMethod("ApplyCrewSprite", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (string id in new[] { "gunner-monkey", "carpenter-beaver" })
            {
                var profile = Resources.Load<CrewDefinition>("Voyage/Crew/" + id);
                Character crew = Object.Instantiate(profile.prefab, Vector2.right * 3f, Quaternion.identity);
                applySprite.Invoke(crew, new object[] { profile.deckSprite });
                SpriteRenderer visual = crew.GetComponentsInChildren<SpriteRenderer>().Single(renderer => renderer.enabled);
                Quaternion standing = visual.transform.localRotation;
                Quaternion rootRotation = crew.transform.rotation;
                Vector3 rootScale = crew.transform.localScale;
                Vector2 footprint = ((BoxCollider2D)crew.Hitbox).size;
                Color color = visual.color;
                Assert.That(visual.transform.localScale, Is.EqualTo(new Vector3(2.028f, 2.028f, 1f)));

                Assert.That(crew.TryReceiveDamage(1f), Is.True);
                Assert.That(Quaternion.Angle(visual.transform.localRotation, standing), Is.EqualTo(90f).Within(0.01f));
                Assert.That(visual.color, Is.EqualTo(color));
                Assert.That(crew.transform.rotation, Is.EqualTo(rootRotation));
                Assert.That(crew.transform.localScale, Is.EqualTo(rootScale));
                Assert.That(((BoxCollider2D)crew.Hitbox).size, Is.EqualTo(footprint));
                yield return new WaitForSeconds(0.75f);
                Assert.That(crew.TryReceivePlayerKick(Vector2.up), Is.True);
                Assert.That(Quaternion.Angle(visual.transform.localRotation, standing), Is.EqualTo(90f).Within(0.01f),
                    "A repeated kick accumulated the lying rotation.");
                Assert.That(visual.color, Is.EqualTo(color));
                yield return new WaitForSeconds(0.36f);
                Assert.That(crew.IsStunned, Is.False);
                Assert.That(visual.transform.localRotation, Is.EqualTo(standing));
                Assert.That(visual.color, Is.EqualTo(color));

                Assert.That(crew.TryReceiveDamage(1f), Is.True);
                crew.gameObject.SetActive(false);
                Assert.That(visual.transform.localRotation, Is.EqualTo(standing), "Disabling crew left its display lying down.");
                Object.Destroy(crew.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator FlyingKickPushesCrewWithShortStunAndStopsAtDeckEdge()
        {
            var profile = Resources.Load<CrewDefinition>("Voyage/Crew/gunner");
            player.GetComponent<Rigidbody2D>().position = new Vector2(0f, 1f);
            player.transform.position = player.GetComponent<Rigidbody2D>().position;
            var crew = (CombatCrewMember)Object.Instantiate(profile.prefab, new Vector2(0f, 2f), Quaternion.identity);
            crew.InitializeCrew(player.BoatHealth, damageSystem, profile);
            crew.gameObject.AddComponent<CircleCollider2D>().isTrigger = true;
            Physics2D.SyncTransforms();
            Assert.That(player.TryMelee(Vector2.up), Is.True);
            Assert.That(crew.IsStunned, Is.True);
            Assert.That(crew.StunRemainingRatio, Is.EqualTo(1f / 3f).Within(0.03f));
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
            Assert.That(crew.TryReceiveDamage(1f), Is.True);
            yield return new WaitForSeconds(0.2f);
            float remaining = crew.StunRemainingRatio;
            Assert.That(crew.TryReceivePlayerKick(Vector2.up), Is.True);
            Assert.That(crew.StunRemainingRatio, Is.EqualTo(remaining).Within(0.02f), "A short kick shortened or added to a longer stun.");
            Assert.That(crew.TryReceiveDamage(1f), Is.False, "Monster damage repeated the stun.");
            yield return new WaitForSeconds(0.6f);
            Assert.That(crew.StunRemainingRatio, Is.LessThan(1f / 3f));
            Assert.That(crew.TryReceivePlayerKick(Vector2.up), Is.True);
            Assert.That(crew.StunRemainingRatio, Is.EqualTo(1f / 3f).Within(0.03f));
            yield return new WaitForSeconds(0.35f);
            Assert.That(crew.IsStunned, Is.False);
        }

        [UnityTest]
        public IEnumerator OwlMovesAwayFromRhinoBeforeResumingFire()
        {
            var rhinoProfile = Resources.Load<CrewDefinition>("Voyage/Crew/mechanic-rhino");
            var owlProfile = Resources.Load<CrewDefinition>("Voyage/Crew/sharpshooter-owl");
            Assert.That(rhinoProfile, Is.Not.Null);
            Assert.That(owlProfile, Is.Not.Null);
            Assert.That(owlProfile.portrait, Is.Not.Null);
            Assert.That(owlProfile.deckSprite, Is.Not.Null);

            var rhino = Object.Instantiate(rhinoProfile.prefab, new Vector2(3f, 0.4f), Quaternion.identity);
            var owl = (CombatCrewMember)Object.Instantiate(owlProfile.prefab, new Vector2(4.2f, 0.4f), Quaternion.identity);
            try
            {
                enemy.transform.position = new Vector2(4f, 7f);
                owl.InitializeCrew(player.BoatHealth, damageSystem, owlProfile);
                var setThreat = typeof(CombatCrewMember).GetMethod("SetAvoidedCrew", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(setThreat, Is.Not.Null);
                setThreat.Invoke(owl, new object[] { rhino });
                Physics2D.SyncTransforms();
                Assert.That(owl.IsAvoidingCrew, Is.True);

                bool HasFired() => Object.FindObjectsOfType<Projectile>().Any(shot => shot.Instigator == owl);
                float deadline = Time.realtimeSinceStartup + 3f;
                while (owl.IsAvoidingCrew && Time.realtimeSinceStartup < deadline)
                {
                    Assert.That(HasFired(), Is.False, "Owl fired while within the avoidance distance.");
                    yield return null;
                }

                Assert.That(owl.IsAvoidingCrew, Is.False, "Owl did not reach a safer part of the deck.");
                Assert.That(player.MovementDeck.ContainsFloor(owl.transform.position), Is.True);
                yield return WaitFor(HasFired, 1f, "Owl did not resume firing after moving away.");
            }
            finally
            {
                Object.Destroy(owl.gameObject);
                Object.Destroy(rhino.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator PassengersPlayFourWalkingAndSkillFramesAndKeepWanderingWithoutWork()
        {
            foreach (var actor in Object.FindObjectsOfType<EnemyCharacter>())
                actor.gameObject.SetActive(false);
            var appearance = typeof(Character).GetMethod("ApplyCrewAppearance", BindingFlags.Instance | BindingFlags.NonPublic);
            var gunnerProfile = Resources.Load<CrewDefinition>("Voyage/Crew/gunner-monkey");
            var gunner = (CombatCrewMember)Object.Instantiate(gunnerProfile.prefab, new Vector2(4f, 0.6f), Quaternion.identity);
            appearance.Invoke(gunner, new object[] { gunnerProfile });
            gunner.InitializeCrew(player.BoatHealth, damageSystem, gunnerProfile);
            var body = gunner.GetComponent<Rigidbody2D>();
            var visual = gunner.GetComponentsInChildren<SpriteRenderer>().Single(renderer => renderer.enabled);
            var seen = new HashSet<Sprite>();
            float stopped = 0f;
            for (float until = Time.time + 5f; Time.time < until;)
            {
                yield return new WaitForFixedUpdate();
                if (gunnerProfile.walkFrames.Contains(visual.sprite))
                    seen.Add(visual.sprite);
                stopped = body.velocity.sqrMagnitude > 0.01f ? 0f : stopped + Time.fixedDeltaTime;
                Assert.That(stopped, Is.LessThan(0.28f), "The gunner paused after reaching a waypoint on the open deck.");
            }
            Assert.That(seen, Is.EquivalentTo(gunnerProfile.walkFrames));

            enemy.transform.position = new Vector2(4f, 8f);
            enemy.gameObject.SetActive(true);
            seen.Clear();
            yield return WaitFor(() =>
            {
                if (gunnerProfile.skillFrames.Contains(visual.sprite))
                    seen.Add(visual.sprite);
                return seen.Count == 4;
            }, 2f, "The gunner did not display all four shot frames.");
            Object.Destroy(gunner.gameObject);

            var mechanicProfile = Resources.Load<CrewDefinition>("Voyage/Crew/mechanic-rhino");
            var boat = player.BoatHealth;
            Vector2 position = boat.Deck.CellToWorld(new Vector2Int(9, 2));
            var mechanic = (MaintenanceCrewMember)Object.Instantiate(mechanicProfile.prefab, position, Quaternion.identity);
            appearance.Invoke(mechanic, new object[] { mechanicProfile });
            mechanic.InitializeCrew(boat, mechanicProfile);
            visual = mechanic.GetComponentsInChildren<SpriteRenderer>().Single(renderer => renderer.enabled);
            Assert.That(boat.ApplyDamage(100f, position), Is.True);
            seen.Clear();
            yield return WaitFor(() =>
            {
                if (mechanic.IsRepairing && mechanicProfile.skillFrames.Contains(visual.sprite))
                    seen.Add(visual.sprite);
                return seen.Count == 4;
            }, 5f, "The mechanic did not display all four repair frames.");
            Object.Destroy(mechanic.gameObject);
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
            // An already stunned NPC lets enemy fire reach the player behind it.
            Assert.That(crew.TryReceiveDamage(1f), Is.True);
            var shooter = Object.Instantiate(Resources.Load<EncounterDefinition>("Voyage/Events/CoastalRaid")
                .waves[0].enemies[0].prefab, new Vector3(20f, 20f), Quaternion.identity);
            shooter.enabled = false;
            var incoming = FireFrom(shooter, new Vector3(0f, 1.5f), Vector2.down, 1f);
            yield return WaitFor(() => player.IsStunned, 0.8f, "A stunned NPC blocked the enemy projectile.");
            Assert.That(incoming == null || !incoming.Hitbox.enabled, Is.True);
            Assert.That(crew.StunRemainingRatio, Is.LessThan(1f));
            Object.Destroy(shooter.gameObject);
            Object.Destroy(crewObject);
        }

        [UnityTest]
        public IEnumerator ProjectileHitsTheMantisClawButPassesOutsideItsContour()
        {
            Vector2 position = new Vector2(0f, 7f);
            enemy.transform.position = position;
            Physics2D.SyncTransforms();

            Assert.That(enemy.Hitbox, Is.TypeOf<PolygonCollider2D>());
            Vector2 emptyCorner = position + new Vector2(0.55f, 0.9f) * 1.3f;
            Assert.That(enemy.Hitbox.bounds.Contains(emptyCorner), Is.True);
            var miss = FireFrom(player, emptyCorner, Vector2.right, player.AttackPower);
            yield return WaitFor(() => miss != null && miss.transform.position.x > enemy.Hitbox.bounds.max.x + 0.15f,
                0.5f, "The projectile should fly through the empty corner of the monster's bounds.");
            Assert.That(enemy.Health, Is.EqualTo(enemy.MaxHealth));
            Object.Destroy(miss.gameObject);

            Vector2 clawLane = position + new Vector2(0.68f, -1.6f) * 1.3f;
            Assert.That(enemy.Hitbox.OverlapPoint(position + new Vector2(0.68f, -0.65f) * 1.3f), Is.True);
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
        public IEnumerator SnailShotsDamageTheHullAndCreateOneRepairPointPerHundred()
        {
            var sentries = Resources.Load<EncounterDefinition>("Voyage/Events/ReefSentries");
            var boat = player.BoatHealth;
            var snail = Object.Instantiate(sentries.waves[0].enemies[0].prefab,
                boat.Deck.transform.position + new Vector3(1f, 6f), Quaternion.identity);
            snail.InitializeEnemy(boat, player, damageSystem);
            yield return WaitFor(() => boat.CurrentHealth == boat.MaxHealth - 75f, 6f, "Hull shots did not arrive.");
            Assert.That(Object.FindObjectsOfType<SpriteEffect>().Count(effect => effect.name.StartsWith("ProjectileImpact")),
                Is.EqualTo(1), "A hull shot must create exactly one impact at the ship.");
            Assert.That(boat.RepairPoints, Is.Empty);
            yield return WaitFor(() => boat.CurrentHealth == boat.MaxHealth - 100f, 3f, "The fourth hull shot did not arrive.");
            Assert.That(boat.RepairPoints, Has.Count.EqualTo(1));
            Assert.That(boat.Deck.ContainsCell(boat.RepairPoints.Single().Cell), Is.True);
            Assert.That(player.IsStunned, Is.False);
            Object.Destroy(snail.gameObject);
        }

        [UnityTest]
        public IEnumerator JellyShotsSlowTheCaptainAndCrewWithoutStunning()
        {
            var sentries = Resources.Load<EncounterDefinition>("Voyage/Events/ReefSentries");
            var boat = player.BoatHealth;
            var jelly = Object.Instantiate(sentries.waves[0].enemies[1].prefab,
                player.transform.position + Vector3.up * 5f, Quaternion.identity);
            jelly.InitializeEnemy(boat, player, damageSystem);
            yield return WaitFor(() => player.IsSlowed, 3f, "The captain was not slowed by a jelly shot.");
            jelly.enabled = false;
            Assert.That(player.IsStunned, Is.False);
            Assert.That(player.ActionSpeed, Is.EqualTo(0.65f));
            player.Move(Vector2.right);
            var body = player.GetComponent<Rigidbody2D>();
            Assert.That(body.velocity.x, Is.EqualTo(player.MoveSpeed * 0.65f).Within(0.001f));
            player.Move(Vector2.zero);
            Assert.That(boat.ApplyDamage(100f, player.transform.position), Is.True);
            var point = boat.RepairPoints.Single();
            body.position = point.transform.position;
            Assert.That(player.TryRepair(0.2f), Is.True);
            Assert.That(point.Progress, Is.EqualTo(player.RepairSpeed * 0.2f * 0.65f).Within(0.001f));
            Assert.That(player.TryFire(Vector2.right), Is.True);
            yield return new WaitForSeconds(player.FireInterval + 0.02f);
            Assert.That(player.TryFire(Vector2.right), Is.False, "The chill should extend the firing cooldown.");

            var profile = Resources.Load<CrewDefinition>("Voyage/Crew/mechanic-rhino");
            var crew = Object.Instantiate(profile.prefab, new Vector2(4f, 1f), Quaternion.identity);
            FireFrom(jelly, (Vector2)crew.transform.position + Vector2.up, Vector2.down, jelly.AttackPower);
            yield return WaitFor(() => crew.IsSlowed, 2f, "The crew member was not slowed.");
            Assert.That(crew.IsStunned, Is.False);
            yield return WaitFor(() => !player.IsSlowed, 4f, "The captain's chill did not expire.");
            Assert.That(player.ActionSpeed, Is.EqualTo(1f));
            Object.Destroy(crew.gameObject);
            Object.Destroy(jelly.gameObject);
        }

        [UnityTest]
        public IEnumerator BoardingCrabClimbsAboardAndStunsCrewBeforeCaptain()
        {
            var raid = Resources.Load<EncounterDefinition>("Voyage/Events/BoardingRaid");
            BoatHealth boat = player.BoatHealth;
            BoatDeck deck = boat.Deck;
            Vector2 crewPosition = deck.CellToWorld(new Vector2Int(9, 3));
            var crewProfile = Resources.Load<CrewDefinition>("Voyage/Crew/mechanic-rhino");
            Character crew = Object.Instantiate(crewProfile.prefab, crewPosition, Quaternion.identity);
            crew.enabled = false;
            var captainBody = player.GetComponent<Rigidbody2D>();
            captainBody.position = deck.CellToWorld(new Vector2Int(10, 3));
            Vector2 sea = new Vector2(crewPosition.x, deck.GetComponent<EdgeCollider2D>().bounds.max.y + 1.4f);
            var crab = Object.Instantiate(raid.waves[0].enemies[0].prefab, sea, Quaternion.identity);
            crab.InitializeEnemy(boat, player, damageSystem);
            yield return WaitFor(() => crab.Hitbox.isTrigger, 1f, "The crab did not leap from the water.");
            yield return WaitFor(() => !crab.Hitbox.isTrigger && deck.ContainsFloor(crab.transform.position), 2f,
                "The crab did not land inside the rails.");
            yield return WaitFor(() => crew.IsStunned, 4f, "The crab did not stun the crew member.");
            Assert.That(Object.FindObjectsOfType<SpriteEffect>().Count(effect => effect.name.StartsWith("MeleeSwing")), Is.EqualTo(1));
            Assert.That(Object.FindObjectsOfType<SpriteEffect>().Count(effect => effect.name.StartsWith("ProjectileImpact")), Is.EqualTo(1));
            Assert.That(player.IsStunned, Is.False);
            Assert.That(crew.Health, Is.EqualTo(crew.MaxHealth));
            Object.Destroy(crew.gameObject);

            var sprite = crab.GetComponentInChildren<SpriteRenderer>();
            yield return WaitFor(() => sprite.sprite.name.EndsWith("_02"), 5f, "The crab did not raise its claw at the captain.");
            Vector2 dodgePosition = deck.CellToWorld(new Vector2Int(12, 2));
            captainBody.position = dodgePosition;
            yield return new WaitForSeconds(0.6f);
            Assert.That(player.IsStunned, Is.False, "Moving out of reach did not dodge the claw.");
            var walking = new HashSet<Sprite>();
            yield return WaitFor(() =>
            {
                if (sprite.sprite.name.Contains("-walk_"))
                    walking.Add(sprite.sprite);
                return player.IsStunned;
            }, 6f, "The crab did not pursue the captain after missing.");
            Assert.That(walking, Has.Count.EqualTo(4), "Pursuit did not play the four walk frames.");
            Assert.That(player.Health, Is.EqualTo(player.MaxHealth));
            Assert.That(boat.CurrentHealth, Is.EqualTo(boat.MaxHealth));
            crab.enabled = false;
            yield return WaitFor(() => !player.IsStunned, 2f, "The captain did not recover before kicking the crab.");
            captainBody.position = deck.CellToWorld(new Vector2Int(9, 2));
            crab.GetComponent<Rigidbody2D>().position = deck.CellToWorld(new Vector2Int(10, 2));
            Physics2D.SyncTransforms();
            float health = crab.Health;
            Assert.That(player.TryMelee(Vector2.right), Is.True);
            Assert.That(crab.Health, Is.EqualTo(health - player.AttackPower), "The captain's kick did not hurt the boarding crab.");
            Bounds deckBounds = deck.GetComponent<EdgeCollider2D>().bounds;
            var occupied = new GameObject("Occupied Deck For Death");
            occupied.transform.position = deckBounds.center;
            occupied.AddComponent<BoxCollider2D>().size = deckBounds.size;
            crab.GetComponent<Rigidbody2D>().position = new Vector2(crab.GetComponent<Rigidbody2D>().position.x,
                deckBounds.max.y - crab.Hitbox.bounds.extents.y - 0.05f);
            Physics2D.SyncTransforms();
            Assert.That(damageSystem.ApplyDamage(player, crab, crab.Health), Is.True);
            var remains = Object.FindObjectsOfType<DestructibleObstacle>().Single(item => item.name.StartsWith("BoardingCrabRemains"));
            Assert.That(remains.GetComponent<BoxCollider2D>().enabled, Is.True);
            Assert.That(deck.HasClearFloor(remains.transform.position, remains.GetComponent<BoxCollider2D>().bounds.size),
                Is.True, "A crowded deck pushed the corpse's hitbox through the rail or fixed cargo.");
            Object.Destroy(occupied);
            yield return new WaitForSeconds(0.55f);
            captainBody.position = (Vector2)remains.transform.position + Vector2.left * 1.4f;
            Physics2D.SyncTransforms();
            Assert.That(player.TryMelee(Vector2.right), Is.True);
            Assert.That(remains.IsCleared, Is.True, "A kick did not clear the fallen crab.");
            Assert.That(remains.GetComponent<BoxCollider2D>().enabled, Is.False);
            Assert.That(Object.FindObjectsOfType<SpriteEffect>().Count(effect => effect.name.StartsWith("BombCleared")), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator TurtleShotsStunForLongerWithoutExtendingRepeatedHits()
        {
            var coastalRaid = Resources.Load<EncounterDefinition>("Voyage/Events/CoastalRaid");
            var prefab = coastalRaid.waves.SelectMany(wave => wave.enemies).Select(group => group.prefab)
                .First(actor => actor.name == "Large Gunner");
            var turtle = Object.Instantiate(prefab, new Vector2(0f, 7f), Quaternion.identity);
            turtle.InitializeEnemy(player.BoatHealth, player, damageSystem);
            turtle.enabled = false;
            Assert.That(turtle.FireInterval, Is.EqualTo(3f));
            int received = 0;
            damageSystem.DamageReceived.AddListener((source, target, damage) => received++);
            Assert.That(player.BoatHealth.ApplyDamage(100f, player.transform.position), Is.True);
            var point = player.BoatHealth.RepairPoints.Single();
            player.GetComponent<Rigidbody2D>().position = point.transform.position;
            Assert.That(player.TryRepair(0.1f), Is.True);
            var shot = FireFrom(turtle, player.transform.position, Vector2.down, turtle.AttackPower);
            Object.Destroy(turtle.gameObject);
            yield return WaitFor(() => player.IsStunned, 0.5f, "The turtle projectile lost its stun after its shooter died.");
            Assert.That(shot == null || !shot.Hitbox.enabled, Is.True);
            Assert.That(player.IsRepairing, Is.False);
            Assert.That(player.StunRemainingRatio, Is.InRange(0.95f, 1f));
            Assert.That(player.Health, Is.EqualTo(player.MaxHealth));
            Assert.That(player.TryFire(Vector2.right), Is.False);
            yield return new WaitForSeconds(1.1f);
            Assert.That(player.IsStunned, Is.True, "The heavy shot used the ordinary one-second stun.");
            Assert.That(player.StunRemainingRatio, Is.InRange(0.45f, 0.6f));
            FireFrom(enemy, player.transform.position, Vector2.down, enemy.AttackPower);
            yield return new WaitForSeconds(1.5f);
            Assert.That(player.IsStunned, Is.False, "A repeated hit extended the heavy stun.");
            Assert.That(received, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator RightSideBomberMirrorsItsBodyAndReleasesFromTheNearClaw()
        {
            var encounter = Resources.Load<EncounterDefinition>("Voyage/Events/Bombardment");
            Bounds deck = player.BoatHealth.Deck.GetComponent<EdgeCollider2D>().bounds;
            var bomber = Object.Instantiate(encounter.waves[0].enemies[0].prefab,
                new Vector2(deck.max.x + 4f, deck.center.y + 1f), Quaternion.identity);
            var visual = bomber.GetComponentInChildren<SpriteRenderer>();
            var outline = (PolygonCollider2D)bomber.Hitbox;
            Vector2[] original = outline.GetPath(0);
            Sprite resting = visual.sprite;
            bomber.InitializeEnemy(player.BoatHealth, player, damageSystem);
            Assert.That(visual.flipX, Is.True);
            Assert.That(outline.GetPath(0), Is.EqualTo(original.Select(point => new Vector2(-point.x, point.y)).ToArray()));
            yield return WaitFor(() => visual.sprite != resting, 1f, "The mirrored bomber did not raise its bomb.");
            Vector2[] held = outline.GetPath(0);
            Assert.That(held.Any(point => point.x < -1.3f), Is.True, "The raised claw collision stayed on the far side.");
            FieldInfo pending = typeof(EnemyCharacter).GetField("pendingBomb", BindingFlags.Instance | BindingFlags.NonPublic);
            object pose = pending.GetValue(bomber);
            Vector2 offset = (Vector2)pose.GetType().GetField("launchOffset").GetValue(pose);
            Vector2 nearClaw = visual.transform.TransformPoint(new Vector2(-offset.x, offset.y));
            yield return WaitFor(() => Object.FindObjectsOfType<TimedBomb>().Any(bomb => bomb.IsInFlight), 1f,
                "The mirrored bomber did not release a bomb.");
            var bomb = Object.FindObjectsOfType<TimedBomb>().First(item => item.IsInFlight);
            FieldInfo flightOrigin = typeof(DestructibleObstacle).GetField("flightOrigin", BindingFlags.Instance | BindingFlags.NonPublic);
            Vector2 origin = player.BoatHealth.Deck.transform.TransformPoint(
                (Vector2)flightOrigin.GetValue(bomb.GetComponent<DestructibleObstacle>()));
            Assert.That(Vector2.Distance(origin, nearClaw), Is.LessThan(0.01f), "The bomb came from the unmirrored claw.");
            Assert.That(outline.GetPath(0), Is.EqualTo(original.Select(point => new Vector2(-point.x, point.y)).ToArray()));
            Object.Destroy(bomb.gameObject);
            Object.Destroy(bomber.gameObject);
        }

        [UnityTest]
        public IEnumerator SeaMonsterShowsFourAttackFramesAndReturnsToItsRestingSprite()
        {
            var renderer = enemy.GetComponentInChildren<SpriteRenderer>();
            Sprite resting = renderer.sprite;
            yield return new WaitForSeconds(0.2f);
            Assert.That(renderer.sprite, Is.SameAs(resting));

            enemy.enabled = true;
            Assert.That(enemy.TryFire(Vector2.right), Is.True);
            var seen = new HashSet<Sprite>();
            yield return WaitFor(() =>
            {
                if (renderer.sprite != resting)
                    seen.Add(renderer.sprite);
                return renderer.sprite == resting;
            }, 1f, "The attack animation did not return to the resting sprite.");
            Assert.That(seen, Has.Count.EqualTo(4));
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
            Vector2 claw = bomber.transform.TransformPoint(new Vector2(1.28f, -0.3f) * 1.3f);
            Vector2 bomb = bomber.transform.TransformPoint(new Vector2(0.945f, -0.045f) * 1.3f);
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

        private Projectile FireFrom(CombatCharacter source, Vector3 origin, Vector2 direction, float damage)
        {
            var shot = Object.Instantiate(source.ProjectilePrefab, origin, Quaternion.identity);
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
