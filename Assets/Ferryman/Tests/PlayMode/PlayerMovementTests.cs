using System.Collections;
using Ferryman.Characters;
using Ferryman.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ferryman.Tests
{
    public sealed class PlayerMovementTests
    {
        private PlayerCharacter player;
        private Rigidbody2D body;
        private BoxCollider2D playerCollider;
        private BoatDeck deck;
        private SimulationMode2D originalSimulationMode;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            originalSimulationMode = Physics2D.simulationMode;
            yield return SceneManager.LoadSceneAsync("Prototype");
            player = Object.FindObjectOfType<PlayerCharacter>();
            deck = Object.FindObjectOfType<BoatDeck>();
            Assert.That(player, Is.Not.Null);
            Assert.That(deck, Is.Not.Null);
            foreach (var enemy in Object.FindObjectsOfType<EnemyCharacter>())
                enemy.gameObject.SetActive(false);
            foreach (var obstacle in Object.FindObjectsOfType<DestructibleObstacle>())
                obstacle.gameObject.SetActive(false);
            foreach (var projectile in Object.FindObjectsOfType<Ferryman.Combat.Projectile>())
            {
                projectile.gameObject.SetActive(false);
                Object.Destroy(projectile.gameObject);
            }
            body = player.GetComponent<Rigidbody2D>();
            playerCollider = player.GetComponent<BoxCollider2D>();
            player.enabled = false;
            body.interpolation = RigidbodyInterpolation2D.None;
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            Physics2D.simulationMode = originalSimulationMode;
        }

        [Test]
        public void MovementHasExpectedSpeedStopsAndLimitsDiagonalInput()
        {
            Vector2 start = body.position;
            Step(Vector2.right, 10);
            Assert.That(body.position.x - start.x, Is.EqualTo(0.8f).Within(0.001f));
            Vector2 stopped = body.position;
            Step(Vector2.zero, 10);
            Assert.That(Vector2.Distance(body.position, stopped), Is.LessThan(0.001f));

            body.position = start;
            Step(Vector2.one, 10);
            Assert.That(Vector2.Distance(body.position, start), Is.EqualTo(0.8f).Within(0.001f));
        }

        [Test]
        public void PlayerColliderStaysInsideAllEdgesAndCorners()
        {
            Vector2 start = Vector2.zero;
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0)
                    continue;
                body.position = start;
                for (int i = 0; i < 250; i++)
                {
                    Step(new Vector2(x, y), 1);
                    AssertInsideDeck();
                }

                Assert.That(Vector2.Distance(body.position, start), Is.GreaterThan(1.5f));
            }

            body.position = Vector2.zero;
            Step(Vector2.left, 100);
            float cabinSide = body.position.x;
            Assert.That(cabinSide, Is.InRange(-3.25f, -3.1f), "The middle cabin wall did not block the player.");
            body.position = Vector2.zero;
            Step(Vector2.up, 100);
            Step(Vector2.left, 100);
            Assert.That(body.position.x, Is.LessThan(cabinSide - 0.5f), "Upper-left access was blocked.");
            AssertInsideDeck();
            body.position = Vector2.zero;
            Step(Vector2.down, 100);
            Step(Vector2.left, 100);
            Assert.That(body.position.y, Is.LessThan(-2.1f), "Lower-left access was blocked.");
            Assert.That(body.position.x, Is.LessThan(cabinSide - 0.1f), "Lower cabin corner did not open up.");
            AssertInsideDeck();
        }

        [Test]
        public void PlayerCanSlideAlongWallAndMoveAway()
        {
            Step(Vector2.up, 100);
            Vector2 atWall = body.position;
            Step(Vector2.one, 20);
            Assert.That(body.position.x - atWall.x, Is.GreaterThan(1f));
            AssertInsideDeck();
            float wallY = body.position.y;
            Step(Vector2.down, 10);
            Assert.That(body.position.y, Is.LessThan(wallY - 0.5f));
        }

        private void Step(Vector2 direction, int count)
        {
            for (int i = 0; i < count; i++)
            {
                player.Move(direction);
                Assert.That(Physics2D.Simulate(0.02f), Is.True);
            }
        }

        private void AssertInsideDeck()
        {
            Bounds footprint = playerCollider.bounds;
            Assert.That(deck.HasClearFloor(footprint.center, (Vector2)footprint.size - Vector2.one * 0.04f), Is.True,
                "Player entered the ship fixtures or left the hull outline.");
        }
    }
}
